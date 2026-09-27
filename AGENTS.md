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
| `ColdCeph.Node` | Privileged per-node host implementing Hosts, Osds, and Devices. |
| `ColdCeph.Control` | One Kestrel process: operator MVC + `/v1` + S3 listener. |
| Matching `*.Tests` | NUnit, builders, `Features/<Slice>/<Kind>/`. |
| `ColdCeph.Architecture.Tests` | Filesystem and ArchUnitNET rules, including slice boundaries. |
| `ColdCeph.E2E.Tests` | Thin Xcepto journeys only. |
| `ColdCeph.Debug` | Maintainer visual-debug CLI (`./cc-debug`). Not a product runtime. |

Debs: `coldceph-control`, `coldceph-node`.

Control must not project-reference Node. They speak HTTP using Core DTOs.

# Slice Rule

This is the organizing rule for every production project.

- A slice is a **user-facing subsystem** (client, operator, or host maintainer capability). Not an
  adapter, protocol, or mechanism.
- Each slice is self-contained: `Controllers/`, `Services/`, `Providers/`, `Models/`, `DTOs/`, plus
  `Views/`/`ViewModels/` where that slice has SSR.
- **A slice owns its writes.** Only that slice’s services (and its own protocol entrypoints: MVC,
  `/v1`, S3 listener, node HTTP) mutate its state.
- **Other slices may only read.** They inject the target `*Controller` and call query methods
  (`Get*`, `List*`, `Has*`, `Is*`). They must not call commands (`Wake`, `StopOsd`, `Standby`,
  `AppendAudit`, …).
- Cross-slice traffic goes through `Controllers/` and `DTOs/` only. No imports of sibling
  `Services/`, `Models/`, `Providers/`, `Repositories/`, `Interfaces/`, `Views/`.
- Protocol surfaces (Razor, REST, S3) are not slices. They live in the owning slice’s
  `Controllers/` (and `Views/`). There is **no** `Features/Dashboard`, `Features/Ceph`,
  `Features/ClusterState`, or `Features/Nodes` dump.
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
| **Osds** | Which OSDs exist, up/in, started/stopped? | Desired/observed OSD process commands to nodes | OSD inventory DTOs |
| **Devices** | Which HDDs, identity, power state? | Wake/standby commands to nodes | Device inventory DTOs |
| **Hosts** | Which machines, node liveness? | Join requests, operator allow/deny, enrolled host liveness | Host DTOs, pending/blocked joins, node endpoints |
| **Operations** | What ran, who initiated, what flags changed? | Audit/operation records | Operation/event lists |
| **Auth** | Who may operate this appliance? | Operator sessions/credentials | Current principal (queries only) |

## Node (`ColdCeph.Node`)

| Slice | User/maintainer meaning | Owns writes |
|-------|-------------------------|-------------|
| **Osds** | Local OSD processes | start/stop/isRunning (systemd provider first) |
| **Devices** | Local physical drives | identity, wake/standby; refuse standby while the mapped OSD still runs |
| **Hosts** | This node’s identity and liveness | `/v1/status`; join request to Control when `COLDCEPH_CONTROL_ENDPOINT` is set |

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
- Startup: each slice reconciles from Ceph/nodes/disks; persisted `COLD` is not trusted.
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
multi-tenant Identity). Antiforgery on POSTs. `/v1` returns 401/403 without a login redirect.
Browser GETs to operator HTML pages send the operator to `/auth/login`. Request-time HTML.
Pages must not auto-refresh; the operator reloads for a new reading. Wake and Sleep are
StoragePlane POSTs and only appear when that transition is legal (Wake from `COLD`, Sleep
from `READY`).

A Razor `ViewLocationExpander` keeps views under `Features/<Slice>/Views/`. Each slice Views
folder includes `_ViewStart.cshtml` so Razor applies `_Layout` (it discovers ViewStart from the
view path, not from `Shared/Views`). Layout chrome reads StoragePlane + Integrity controllers
**only when the operator is authenticated**, so operational state and raw Ceph health stay
distinct without making login invoke the Ceph CLI.

Wake from the UI is `POST` on **StoragePlane**’s controller (protocol entry).

# S3

Second Kestrel URL (do not mix S3 and HTML): operator `:8080` (or `WEB_PORT` under Agent-Up), S3
`:7480`. Both bind `http://*:port` so `localhost` (IPv6) and `127.0.0.1` reach the operator UI.
HttpClient access-log lines for the node/RGW clients stay at Warning so reconciler polls do not
drown `Now listening on`. Transparent proxy to RGW. Preserve signed method/path/query/`Host`/`x-amz-*`. Wait-mode
holds the connection; retry-mode returns 503 + `Retry-After`. No unlimited local buffering.
`Expect: 100-continue` where possible.

# Testing

- NEVER run `dotnet` commands directly on `.csproj` files. ALWAYS use solution-wide `dotnet` commands.
- Positive + negative tests for every behavioral expectation. A test is only meaningful if it can
  distinguish correct behaviour from incorrect behaviour.
- No `[SetUp]` / `[OneTimeSetUp]` on new **test fixtures**. Arrange inside the test or a local helper
  with explicit arguments. `ColdCeph.E2E.Tests` may use a project `[SetUpFixture]` to host a
  Testcontainers Ceph demo plus Control + Node once for the whole Xcepto project.
- Builders in `Support/` for hot DTOs (wide constructors used many times).
- Slice **Unit/**: own writes and refuse illegal writes.
- Slice **Controller/**: sibling-visible queries; commands only from that slice’s protocol tests.
- **HTTP/**: MVC and `/v1` on the owning slice.
- **Provider/**: ceph CLI, systemd, disk identity, proxy command shapes.
- Layout chrome queries StoragePlane + Integrity **only after authentication**. Login and
  anonymous 401 pages must not invoke the Ceph CLI. Authenticated chrome reads Integrity’s
  last raw-health snapshot so HTML does not wait on the Ceph CLI. The Integrity page still
  loads the full classified snapshot.
- Ceph CLI is **single-flight and TTL-cached** (default 2s) inside Integrity’s query provider.
  `ContainsHealth` reuses the cached `health detail` JSON; it must not spawn another process.
  Control’s process runner admits **one child process at a time**.
- StoragePlane, Osds, and Devices reconcilers catch provider exceptions per tick so a
  failed `ceph` or node call cannot stop the loop. The next tick retries.
- `ColdCeph.E2E.Tests`: thin Xcepto only. A project `[SetUpFixture]` starts an assembly-wide
  Testcontainers Ceph demo (`CephCluster`) plus in-process Control and Node **once** and
  reuses that environment. Tests do not start their own stack and must not shell
  `docker compose`. Custom adapters (`Operator`, `Node`, `S3`, `Ceph`) are created through
  fluent builders on the Xcepto transition builder. Each test is 3–5 steps: actions plus
  `EvaluateConditionsForTransition` expectations. Shared Control state is driven with
  `EnsureCold` / `EnsureReady` by polling `/health`, not by retrying full SSR. HTML is a
  one-shot assertion. Xcepto timeouts stay fail-fast (seconds, not minutes). Node OSD/disk
  uses in-memory runtimes because systemd cannot manage the Testcontainer OSDs; Control talks
  to that container’s Ceph/RGW through a `docker exec` wrapper the fixture writes. Xcepto is
  used only inside `ColdCeph.E2E.Tests`. The project-level `[SetUpFixture]` is the allowed
  exception to “no OneTimeSetUp on fixtures”.

# Packaging and CI

No LocalInstaller. Self-contained `linux-x64` publish, `packaging/linux/` systemd units
(`Restart=on-failure`), `/opt/coldceph/{control,node}`, `/etc/coldceph/*.yaml`, `dpkg-deb` →
GitHub Release assets.

CI: all branches run unit tests (`dotnet test` excluding `ColdCeph.E2E.Tests`) and a separate
E2E job that only runs `ColdCeph.E2E.Tests`. The `[SetUpFixture]` starts Ceph via Testcontainers.
`main`/`v*` publish debs + checksums after both jobs pass.

`agent-up.json` launches local Control with injected `WEB_PORT`. Applications consume that
variable; do not hard-code the development operator port when integrating with Agent-Up.

`./cc-debug` (or `./cc-debug screenshot`) signs in and captures every operator HTML page from a
running Control. Optional path limits the capture. `pages` lists routes. Control URL is
`COLDCEPH_OPERATOR_URL` or `http://127.0.0.1:$WEB_PORT`. Screenshots write under
`.git/coldceph/debug/`. Do not walk login or click pages by hand for visual inspection.

# Local development

Root `compose.yaml` is a **multi-host** Ceph for **local IDE getting-started only**: one container
per node (`mon`, `mgr`, `node-a`/`node-b`/`node-c`, `rgw`). Each storage node has one BlueStore OSD
on a tmpfs ramdisk (loop device, default 1GiB). Replica size 3 places one copy on each storage
host. E2E does not use this file; the Xcepto `[SetUpFixture]` still starts a single Testcontainers
`demo` container. Pin `CEPH_IMAGE` to a daemon tag that still ships the ceph-container entrypoints
(`v7.0.3-stable-7.0-quincy-centos-stream8`); `latest-reef` does not.

The cluster is fully ephemeral: no named volumes. MON, MGR, OSD, RGW, and `/etc/ceph` live on
tmpfs. `docker compose down` discards the cluster; `docker compose up` always bootstraps a new
one. Storage nodes fetch conf/keyrings from the monitor over the compose network (HTTP on
`CEPH_CONFIG_PORT`, not published to the host). If a storage container is recreated while the
monitor is still up, its entrypoint **purges that hostname’s old OSDs** then prepares a fresh
ramdisk OSD so CRUSH matches the live disks. That purge is compose bootstrap only — StoragePlane
must still never `out` / `purge` / destroy. The compose network is a pinned subnet with a static
`MON_IP` (`172.28.90.10`). Ready means `ceph osd tree` shows three hosts, `ceph -s` is healthy, and
RGW answers on host 7481. The RGW entrypoint creates the `coldceph` S3 user once
radosgw is listening. Credential and port defaults live in `.env.example` and in the
Control/Node `Properties/launchSettings.json` profiles. Every default is overridable
with the same env var name. Control runs with zero nodes. A Node asks to join with `POST /v1/hosts/join` (no join
token) and `X-ColdCeph-Node-Endpoint`. Hosts keeps the request **pending** until the operator
Allows it on `/hosts`. Deny blocks that node until Allow. Optional `COLDCEPH_NODE_ENDPOINT` is
configured discovery: Control seeds that host as already enrolled. `COLDCEPH_NODE_TOKEN` is only
Control→Node command auth, not a join secret.

Control talks to compose Ceph through `COLDCEPH_CEPH_BINARY` (default wrapper `docker/ceph/ceph`)
and to RGW through `COLDCEPH_RGW`. Optional `COLDCEPH_CEPH_CONF` / `COLDCEPH_CEPH_KEYRING` are
prepended as `--conf` / `--keyring` when a host `ceph` binary is used instead of the wrapper.

# Gold Standards

- Agent-Up: capability slices, Controller → Service → Provider, 1:1 test projects, NUnit
  positive+negative, executable architecture tests, systemd + `.deb`, fail-closed robustness.
- HiveShardEE: ASP.NET Core MVC + Razor Views SSR, `AppComposition`, form POST + PRG,
  `WebApplicationFactory` HTML asserts.
