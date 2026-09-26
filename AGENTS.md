# ColdCeph

ColdCeph is a cold-storage orchestration layer around an existing Ceph cluster. Ceph remains
responsible for object durability. ColdCeph owns S3 admission, HDD/OSD power-state orchestration,
integrity classification, and the operator UI.

The product specification is `docs/spec/cold-storage.md`. First shippable product is spec §52 MVP.

# Definition Synchronization

`AGENTS.md` and `docs/` are definition sources for this product.

Any change that alters behaviour, architecture, ownership, project layout, workflows, configuration
shape, runtime contracts, testing rules, or implementation guidance must update the matching
definition source in the same change.

Do not leave implementation, `AGENTS.md`, and docs disagreeing.

# Solution Shape

Projects live at repo root. Target framework is `net10.0` with nullable reference types and
`TreatWarningsAsErrors`.

| Project | Role |
|---------|------|
| `ColdCeph.Core` | Shared slice DTOs and IDs. No HTTP, no `ceph` CLI, no systemd. |
| `ColdCeph.Agent` | Privileged per-node host implementing Hosts, Osds, and Devices. |
| `ColdCeph.Control` | One Kestrel process: operator MVC + `/v1` + S3 listener. |
| Matching `*.Tests` | NUnit, builders, `Features/<Slice>/<Kind>/`. |
| `ColdCeph.Architecture.Tests` | Filesystem and ArchUnitNET rules, including slice boundaries. |
| `ColdCeph.E2E.Tests` | Thin Xcepto journeys only. |

Debs: `coldceph-control`, `coldceph-agent`.

Control must not project-reference Agent. They speak HTTP using Core DTOs.

# Slice Rule

This is the organizing rule for every production project.

- A slice is a **user-facing subsystem** (client, operator, or host maintainer capability). Not an
  adapter, protocol, or mechanism.
- Each slice is self-contained: `Controllers/`, `Services/`, `Providers/`, `Models/`, `DTOs/`, plus
  `Views/`/`ViewModels/` where that slice has SSR.
- **A slice owns its writes.** Only that slice’s services (and its own protocol entrypoints: MVC,
  `/v1`, S3 listener, agent HTTP) mutate its state.
- **Other slices may only read.** They inject the target `*Controller` and call query methods
  (`Get*`, `List*`, `Has*`, `Is*`). They must not call commands (`Wake`, `StopOsd`, `Standby`,
  `AppendAudit`, …).
- Cross-slice traffic goes through `Controllers/` and `DTOs/` only. No imports of sibling
  `Services/`, `Models/`, `Providers/`, `Repositories/`, `Interfaces/`, `Views/`.
- Protocol surfaces (Razor, REST, S3) are not slices. They live in the owning slice’s
  `Controllers/` (and `Views/`). There is **no** `Features/Dashboard`, `Features/Ceph`,
  `Features/ClusterState`, or `Features/Agents` dump.
- Reconcile loops sit **inside** the slice that owns the write: they **read** sibling controllers,
  then write locally. Example: Devices watches StoragePlane=`WAKING` and issues disk wake itself;
  StoragePlane does not call `Devices.Wake()`.
- Architecture tests enforce sibling import paths and that production call sites outside a slice
  do not invoke that slice’s command methods.

`ceph --format json`, systemd, hdparm, YARP, SQLite, mTLS/token clients are **providers inside the
owning slice**, never slice names.

Production files live under `Features/`, `Shared/`, `Composition/`, or an approved entrypoint
(`Program.cs`). Feature files use only the type folders listed above plus `Repositories/` and
`Interfaces/` for same-slice persistence.

# Slice Catalog

## Control (`ColdCeph.Control`)

| Slice | User question | Owns writes | Siblings may read |
|-------|---------------|-------------|-------------------|
| **S3** | Can clients GET/PUT objects through the cold endpoint? | In-flight/queued requests, proxy sessions, wait vs 503 | Pending work, active count, last activity |
| **StoragePlane** | Is the data plane COLD, WAKING, READY, …? Should it sleep? | Operational state, transition lease, idle policy, controller-owned `noout` records | State, readiness, lease holder |
| **Integrity** | Is my data known safe? What does Ceph say vs expected-cold? | Last verified-clean snapshot, classified health checks, PG/pool durability view | Integrity DTO, raw Ceph health fields |
| **Osds** | Which OSDs exist, up/in, started/stopped? | Desired/observed OSD process commands to agents | OSD inventory DTOs |
| **Devices** | Which HDDs, identity, power state? | Wake/standby commands to agents | Device inventory DTOs |
| **Hosts** | Which machines, agent liveness? | Agent registration, session/liveness | Host DTOs, agent endpoints |
| **Operations** | What ran, who initiated, what flags changed? | Audit/operation records | Operation/event lists |
| **Auth** | Who may operate this appliance? | Operator sessions/credentials | Current principal (queries only) |

## Agent (`ColdCeph.Agent`)

| Slice | User/maintainer meaning | Owns writes |
|-------|-------------------------|-------------|
| **Osds** | Local OSD processes | start/stop/isRunning (systemd provider first) |
| **Devices** | Local physical drives | identity, wake/standby; refuse standby while the mapped OSD still runs |
| **Hosts** | This node’s identity and liveness | `/v1/status` heartbeat payload |

## Core (`ColdCeph.Core`)

Same slice names, **DTOs and IDs only**. No services that mutate. StoragePlane state-machine
**behavior** lives in Control’s StoragePlane services so writes stay in one host.

# MVP State Machine and Safety

States: `COLD → WAKING → READY → QUIESCING → SLEEPING → COLD`, plus `FAULTED`. Peering wait stays
inside `WAKING`; unexpected integrity → `FAULTED`. `PEERING`, `MAINTENANCE`, and `DEGRADED` are
later-release states.

Invariants:

- Devices never standby while the mapped OSD process is running.
- Sleep uses scoped `noout` owned by StoragePlane; never `out` / `safe-to-destroy` / destroy/purge/rm.
- `ok-to-stop` is not the all-OSD sleep predicate.
- Clear only flags StoragePlane recorded as controller-owned.
- Startup: each slice reconciles from Ceph/agents/disks; persisted `COLD` is not trusted.
- One StoragePlane transition lease; many S3 requests create one pending-work signal, one wake.
- Writes fail closed unless Integrity reports `write_ready`; cold `HEALTH_ERR` is classified
  expected, not hidden.

Persistence: SQLite under `/var/lib/coldceph` behind the owning slice’s repositories. Never on
sleeping HDDs.

S3-triggered wake: S3 **does not** call `StoragePlane.Wake()`. S3 records pending work. StoragePlane’s
reconciler **reads** `S3.HasPendingWork` and **owns** the transition to `WAKING`.

Sleep: StoragePlane moves to `QUIESCING`/`SLEEPING` after reading S3 idle + Integrity sleep-safe.
Osds/Devices reconcilers **read** that state and stop/standby themselves. StoragePlane must not
standby disks.

# SSR

Classic MVC, no Blazor/SPA/HTMX. Cookie operator auth in **Auth** (appliance password, not
multi-tenant Identity). Antiforgery on POSTs. 401/403 without login redirect. Request-time HTML;
optional meta refresh while StoragePlane is `WAKING`/`SLEEPING`.

A Razor `ViewLocationExpander` keeps views under `Features/<Slice>/Views/`. Layout chrome reads
StoragePlane + Integrity controllers so operational state and raw Ceph health stay distinct.

Wake from the UI is `POST` on **StoragePlane**’s controller (protocol entry).

# S3

Second Kestrel URL (do not mix S3 and HTML): operator `:8080` (or `WEB_PORT` under Agent-Up), S3
`:7480`. Transparent proxy to RGW. Preserve signed method/path/query/`Host`/`x-amz-*`. Wait-mode
holds the connection; retry-mode returns 503 + `Retry-After`. No unlimited local buffering.
`Expect: 100-continue` where possible.

# Testing

- NEVER run `dotnet` commands directly on `.csproj` files. ALWAYS use solution-wide `dotnet` commands.
- Positive + negative tests for every behavioral expectation. A test is only meaningful if it can
  distinguish correct behaviour from incorrect behaviour.
- No `[SetUp]` / `[OneTimeSetUp]` on new fixtures. Arrange inside the test or a local helper with
  explicit arguments.
- Builders in `Support/` for hot DTOs (wide constructors used many times).
- Slice **Unit/**: own writes and refuse illegal writes.
- Slice **Controller/**: sibling-visible queries; commands only from that slice’s protocol tests.
- **HTTP/**: MVC and `/v1` on the owning slice.
- **Provider/**: ceph CLI, systemd, disk identity, proxy command shapes.
- `ColdCeph.E2E.Tests`: thin Xcepto scenarios only (register adapters, GET/POST, assert HTML/S3
  status). Shared fakes live in product test support or tiny E2E `Support/`, not a second domain
  layer. Xcepto is used only inside `ColdCeph.E2E.Tests`.

# Packaging and CI

No LocalInstaller. Self-contained `linux-x64` publish, `packaging/linux/` systemd units
(`Restart=on-failure`), `/opt/coldceph/{control,agent}`, `/etc/coldceph/*.yaml`, `dpkg-deb` →
GitHub Release assets.

CI: all branches `dotnet test`; `main`/`v*` publish debs + checksums.

`agent-up.json` launches local Control with injected `WEB_PORT`. Applications consume that
variable; do not hard-code the development operator port when integrating with Agent-Up.

# Gold Standards

- Agent-Up: capability slices, Controller → Service → Provider, 1:1 test projects, NUnit
  positive+negative, executable architecture tests, systemd + `.deb`, fail-closed robustness.
- HiveShardEE: ASP.NET Core MVC + Razor Views SSR, `AppComposition`, form POST + PRG,
  `WebApplicationFactory` HTML asserts.
