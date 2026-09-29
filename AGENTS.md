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
| **S3** | Can clients GET/PUT objects through the cold endpoint? What objects are in a bucket? | In-flight/queued requests, proxy sessions, wait vs 503, operator browse upload | Pending work, active count, last activity |
| **StoragePlane** | Is the data plane COLD, WAKING, READY, …? Should it sleep? | Operational state, transition lease, idle policy, controller-owned `noout` records | State, readiness, lease holder |
| **Integrity** | Is my data known safe? What does Ceph say vs expected-cold? | Last verified-clean snapshot, classified health checks, confirmed cluster capacity, PG/pool durability view | Integrity DTO, raw Ceph health fields, last capacity reading |
| **Osds** | Which OSDs exist, up/in, started/stopped? | Observed inventory from Nodes, start/stop commands to nodes | OSD inventory DTOs |
| **Devices** | Which HDDs, identity, power state? | Observed inventory from Nodes, wake/standby commands to nodes | Device inventory DTOs |
| **Hosts** | Which machines, node liveness? | Join requests, operator allow/deny, enrolled host liveness | Host DTOs, pending/blocked joins, node endpoints |
| **Operations** | What ran, who initiated, what flags changed? | Audit/operation records | Operation/event lists |
| **Auth** | Who may operate this appliance? | Operator sessions/credentials | Current principal (queries only) |

## Node (`ColdCeph.Node`)

| Slice | User/maintainer meaning | Owns writes |
|-------|-------------------------|-------------|
| **Osds** | Local OSD processes | start/stop/isRunning; push observations to Control after enroll, after re-enroll, and when the local snapshot changes |
| **Devices** | Local physical drives | identity, wake/standby; refuse standby while the mapped OSD still runs; push observations with Osds after enroll, re-enroll, and local change |
| **Hosts** | This node’s identity and liveness | `/v1/status`; join request to Control when `COLDCEPH_CONTROL_ENDPOINT` is set; 200 means enrolled for inventory push; unreachable Control clears enrollment so the next 200 re-pushes inventory |

## Core (`ColdCeph.Core`)

Same slice names, **DTOs and IDs only**. No services that mutate. StoragePlane state-machine
**behavior** lives in Control’s StoragePlane services so writes stay in one host.

# MVP State Machine and Safety

States: `COLD → WAKING → READY → QUIESCING → SLEEPING → COLD`, plus `FAULTED`. `WAKING` ends
when Nodes report every OSD process running. Unexpected integrity (unfound objects, inconsistent
or incomplete PGs) → `FAULTED` after an operator/`/v1`/S3 confirmation, not a timer. Ordinary
Ceph `HEALTH_WARN` checks such as too-few PGs or `OSD_DOWN` while Node processes already run are
not a reason to `FAULT`. `PEERING`, `MAINTENANCE`, and `DEGRADED` are later-release states.

Invariants:

- Devices never standby while the mapped OSD process is running.
- Sleep uses scoped `noout` owned by StoragePlane; never `out` / `safe-to-destroy` / destroy/purge/rm.
  The scope is `COLDCEPH_NOOUT_SCOPE`, defaulting to the `default` CRUSH root because every cluster
  has one. It must name a bucket that exists and covers the cold OSDs: `osd set-group noout` fails on
  an unknown name, and a failed set leaves sleep unable to record what it owns. Narrow it on an
  appliance whose root also holds OSDs outside the cold plane.
- `ok-to-stop` is not the all-OSD sleep predicate.
- Clear only flags StoragePlane recorded as controller-owned.
- Startup: Nodes push OSD/device snapshots after Allow; Control overlays Ceph `osd dump` up/in
  at Osds list time (once per operator/`/v1` GET). Persisted `COLD` is not trusted. Control
  reconcilers do not GET Node inventory and do not invoke the Ceph CLI. Enrolled hosts, pending
  and blocked joins, and the last Node-pushed OSD/device observations persist in SQLite so a
  Control restart does not require Allow again and does not drop inventory.
- One StoragePlane transition lease; many S3 requests create one pending-work signal, one wake.
- Writes fail closed unless Integrity reports `write_ready`; cold `HEALTH_ERR` is classified
expected, not hidden. Controller-owned scoped `noout` (OSDMAP_FLAGS) is expected, not a
reason to `FAULT`. Ordinary `HEALTH_WARN` checks are not a reason to `FAULT`.
- `FAULTED` does not leave automatically when a later confirmation is clean. The operator may
Wake (`FAULTED` → `WAKING`) when the last confirmation is not a durability failure.

Persistence: SQLite under `/var/lib/coldceph` behind the owning slice’s repositories. Never on
sleeping HDDs.

S3-triggered wake: S3 **does not** call `StoragePlane.Wake()`. S3 records pending work. StoragePlane’s
reconciler **reads** `S3.HasPendingWork` and **owns** the transition to `WAKING`.

Sleep: StoragePlane moves to `QUIESCING`/`SLEEPING` after reading S3 idle plus the last Integrity
sleep-safe snapshot from an operator/`/v1`/S3 confirmation. It does not fetch Ceph on a timer.
Osds/Devices reconcilers **read** that state and stop/standby themselves. StoragePlane must not
standby disks.

# SSR

Classic MVC, no Blazor/SPA/HTMX. Cookie operator auth in **Auth** (appliance password, not
multi-tenant Identity). Antiforgery on POSTs. `/v1` returns 401/403 without a login redirect.
Browser GETs to operator HTML pages send the operator to `/auth/login`. Request-time HTML.
Pages must not auto-refresh; the operator reloads for a new reading. Wake and Sleep are
StoragePlane POSTs and only appear when that transition is legal (Wake from `COLD`, Wake from
`FAULTED` when the last confirmation is not a durability failure, Sleep from `READY`).

A Razor `ViewLocationExpander` keeps views under `Features/<Slice>/Views/`. Each slice Views
folder includes `_ViewStart.cshtml` so Razor applies `_Layout` (it discovers ViewStart from the
view path, not from `Shared/Views`). Layout chrome reads StoragePlane + Integrity controllers
**only when the operator is authenticated**. It shows the classified protection conclusion rather
than an unexplained raw cold `HEALTH_ERR`. Login and layout chrome never invoke the Ceph CLI.

`/` is a StoragePlane-owned, read-only appliance Overview. It composes query methods from sibling
controllers to show setup readiness, backup availability, last verified protection, the last
confirmed capacity reading, and hardware totals. It uses Node-pushed inventory and Integrity’s
cached snapshot; it must not cause an OSD overlay or Ceph command. Wake is not offered until at
least one live enrolled host plus disk and OSD observations exist. Primary copy uses plain storage
language. Unknown facts are explicitly dated and never presented as healthy. There is no Integrity
HTML page and no Advanced disclosures; protection is the Overview card, refreshed by
`POST /integrity/check`. Operator navigation is Overview, Storage, Buckets, Activity, plus
storage-service and disk pages.

Wake from the UI is `POST` on **StoragePlane**’s controller (protocol entry).

# S3

Second Kestrel URL (do not mix S3 and HTML): operator `:8080` (or `WEB_PORT` under Agent-Up), S3
`:7480`. Both bind `http://*:port` so `localhost` (IPv6) and `127.0.0.1` reach the operator UI.
HttpClient access-log lines for the node/RGW clients stay at Warning so reconciler polls do not
drown `Now listening on`. Transparent proxy to RGW. Preserve signed method/path/query/`Host`/`x-amz-*`. Wait-mode
holds the connection until StoragePlane is `READY` (Node-pushed processes), then confirms Ceph
**once** and forwards or returns 503; retry-mode returns 503 + `Retry-After` without a Ceph
call while not `READY`. No unlimited local buffering.
`Expect: 100-continue` where possible.

Operator Buckets (`/s3`, S3 slice SSR) is a small path-style viewer against `COLDCEPH_RGW` using
`COLDCEPH_S3_ACCESS_KEY` / `COLDCEPH_S3_SECRET_KEY` (defaults `coldceph` / `coldcephsecret`).
List buckets, click into a bucket, folders change the page URL, objects show Download, and an
Upload form is available inside a bucket. S3 does not call `StoragePlane.Wake()`; the greyed
page posts the StoragePlane `/wake` form when that transition is legal. Browse talks to RGW only
when StoragePlane is `READY`; it does not use the cold `:7480` wait-mode listener and does not
query Ceph.

# Testing

## Representativeness

These rules exist because the suite once had ~320 passing tests while the Ceph integration did not
work. Every signal was derived by grepping Ceph's prose, no test had ever seen real `ceph` output,
and the E2E cluster was forced to `HEALTH_OK` before any test ran. See
`docs/design/test-representativeness-review.md`.

- **A fake stands in for an external system, never for our own reading of that system's output.**
  The seam goes at the process or socket boundary — `IProcessRunner`, `HttpMessageHandler` — not at
  an interface that returns the conclusions we wanted to test.
- **Captured output, not invented output.** A health string, PG state, wire response or CLI output
  shape that no one has observed from the real system is not valid test data. Ceph fixtures live in
  `ColdCeph.Control.Tests/Support/CephFixtures`, one folder per cluster condition; add a scenario
  there rather than a string in a test. `ColdCeph.E2E.Tests` asserts the live output still parses to
  the shape those fixtures model.
- **Decisions key on stable identifiers.** Ceph health check *names* and PG *state tokens*, never
  substring searches over messages. Messages are prose and change between releases; a false positive
  here drives an irreversible `FAULTED`.
- **Adapters fail closed.** A provider that cannot confirm what happened throws. It never reports
  the state it requested as the state it observed — that is what let an unreachable node read as
  "every OSD is running".
- **Every `Providers/` type has a `Provider/` fixture, and every process runner has tests.**
  Enforced by `ProviderCoverage`. A fake of a production interface does not substitute for testing
  the real implementation of that interface.
- **A test that cannot fail is worse than no test.** Do not assert on a fake's default values. When
  a test guards a specific regression, check it fails against the old behaviour before trusting it.
- Positive + negative tests for every behavioral expectation. A test is only meaningful if it can
  distinguish correct behaviour from incorrect behaviour.
- CI collects coverage and prints every production file no test executes. That list is a queue, not
  a score.

## Mechanics

- NEVER run `dotnet` commands directly on `.csproj` files. ALWAYS use solution-wide `dotnet` commands.
- No `[SetUp]` / `[OneTimeSetUp]` on new **test fixtures**. Arrange inside the test or a local helper
  with explicit arguments. `ColdCeph.E2E.Tests` may use a project `[SetUpFixture]` to host a
  Testcontainers Ceph demo plus Control + Node once for the whole Xcepto project.
- Builders in `Support/` for hot DTOs (wide constructors used many times).
- Slice **Unit/**: own writes and refuse illegal writes.
- Slice **Controller/**: sibling-visible queries; commands only from that slice’s protocol tests.
- **HTTP/**: MVC and `/v1` on the owning slice.
- **Provider/**: ceph CLI, systemd, hdparm, disk identity, RGW proxy and client, node HTTP clients,
  process runners. These assert the values the provider concludes and how it behaves on a failure,
  not only the argv it produced.
- **Concurrency**: Control's Osds/Devices/Hosts singletons are written by node pushes on HTTP threads
  and read by three reconcile loops and every page render, so they are tested under real contention.
- Layout chrome and Overview query StoragePlane + Integrity **only after authentication**. Login and
  anonymous 401 pages must not invoke the Ceph CLI. Authenticated chrome and Overview read
  Integrity’s last snapshot so HTML does not wait on the Ceph CLI. `POST /integrity/check` (and
  Integrity `/v1`) load a live classified snapshot and capacity **once** per request.
- Control **never polls** Ceph. The monitor is queried only when an external request needs
  confirmation, and only once per request: operator `POST /integrity/check`, Integrity `/v1`,
  S3 admission after `READY`, and Osds `osd dump` overlay on Osds list GET. Historical OSD/device
  inventory comes from Node push on enroll and local change. Reconcilers read Node-pushed state
  and the last Integrity snapshot; they do not invoke the Ceph CLI.
- One confirmation is **one pass**: `ICephQueryProvider.GetObservation()` issues `health detail`,
  `status` and `quorum_status` once each and returns a `CephObservation`; `CephSignals` derives every
  readiness and durability signal from it. PG states and capacity both come from `status`'s `pgmap`,
  so neither needs a call of its own — and reading PG states from `pg stat` instead gave an empty
  state list against a real cluster, which correctly but uselessly held every predicate closed on a
  HEALTH_OK cluster. That property is structural, not a cache —
  a TTL cache expires mid-confirmation on exactly the slow cluster where it matters. `osd dump` is a
  separate confirmation, serving the Osds list GET. Control’s process runner admits **one child
  process at a time**, and every child has a timeout and is killed on expiry.
- StoragePlane, Osds, and Devices reconcilers catch provider exceptions per tick so a
  failed node call cannot stop the loop. The next tick retries. They **log** every such failure and
  keep the last one on `ReconcilerLoop.LastError`: a silently failing loop is indistinguishable from
  a healthy one, which is how a plane stuck in `WAKING` used to have no explanation anywhere.
- Reconcilers only issue a command when the observed state differs from the desired one. Reissuing
  start/wake every tick for the whole of `WAKING` is one call per drive per second against drives
  that are spinning up.
- Osds waits for Devices to report the mapped disk `Active` before starting an OSD, by reading
  Devices’ controller. StoragePlane does not sequence them.
- Nodes push `POST /v1/osds/observed` and `POST /v1/devices/observed` (node token, enrolled
  host only) on enroll, on re-enroll after Control was unreachable, and when the local snapshot
  changes. An unchanged snapshot is not resent while enrollment stays continuous. Node OSD/device
  snapshots are mutated under a lock so report loops and node HTTP cannot tear the dictionary.
  Integrity overlays Ceph `osd dump` up/in once per Osds list GET. Osds/Devices reconcilers
  issue WAKING start/wake and SLEEPING stop/standby only.
- `ColdCeph.E2E.Tests`: thin Xcepto only. A project `[SetUpFixture]` starts an assembly-wide
  Testcontainers Ceph demo (`CephCluster`) plus in-process Control and Node **once** and
  reuses that environment. Tests do not start their own stack and must not shell
  `docker compose`. Custom adapters (`Operator`, `Node`, `S3`, `Ceph`) are created through
  fluent builders on the Xcepto transition builder. Each test is 3–5 steps: actions plus
  `EvaluateConditionsForTransition` expectations. Shared Control state is driven with
  `EnsureCold` / `EnsureReady` by polling `/health`, not by retrying full SSR. HTML is a
  one-shot assertion. Xcepto timeouts stay fail-fast (seconds, not minutes).   Node OSD/disk
  uses in-memory runtimes because systemd cannot manage the Testcontainer OSDs; the E2E Node sets
  `ControlEndpoint` so join returns 200 and report loops POST inventory. Control talks
  to that container’s Ceph/RGW by `Process.Start` of `docker exec {containerId} ceph …`
  (the same argv Control uses when `COLDCEPH_CEPH_CONTAINER` is set). Xcepto is
  used only inside `ColdCeph.E2E.Tests`. The project-level `[SetUpFixture]` is the allowed
  exception to “no OneTimeSetUp on fixtures”.
- **The E2E cluster is never forced healthy.** `CephCluster` waits for a monitor quorum, an OSD up
  and in, and RGW answering — preconditions, not verdicts about health — and it does not mute
  warnings or gate on `HEALTH_OK`. Muting them is what kept classification, the most
  safety-critical logic in the product, from ever meeting a real health check. Journeys assert what
  ColdCeph *concludes* from whatever the cluster reports, and a check the classifier does not
  recognise fails the run by name. Setup commands fail loudly rather than being swallowed.
- The E2E noout scope is **discovered, not created**: `CephCluster` reads the CRUSH host bucket
  osd.0 sits under and passes it as `ControlConfig.NooutScope`, so `set-group noout` covers a real
  OSD and Ceph raises the flags check ColdCeph has to classify. Creating a fresh empty bucket meant
  the flag covered nothing; creating one as a new CRUSH *root* and moving the host into it took the
  OSD out of `root=default`, which broke pool mapping and stopped RGW from ever bootstrapping.
  Nothing in setup changes the cluster's topology.
- Setup waits in order: quorum, an OSD up and in, then RGW — and nothing touches the cluster until
  RGW has answered, because it is still creating its own pools. When a wait gives up it attaches the
  container log and `ceph -s` / `osd tree` / `health detail` to the exception; a CI step that runs
  afterwards finds nothing, because Testcontainers has already disposed the container.
- S3 journeys assert explicit statuses and include a real object **round trip** — PUT then GET the
  same bytes — signed with a payload hash and signed `Content-*` headers, so a dropped signed header
  surfaces as RGW’s 403. “Not a 503” passes on a 403, a 404 and a 500 alike.
- **Known limitation:** the E2E node's OSD start/stop is simulated, because the `demo` container has
  no supervisor that restarts `ceph-osd` on request. The `osd dump` overlay and everything Control
  reads from Ceph are genuine; the OSD process lifecycle is not, so real wake timing — OSDs taking
  tens of seconds, PGs peering — is still unexercised. Closing it needs a supervisor in the
  container or a move to the compose stack; do not claim wake is covered end to end until then.

# Packaging and CI

No LocalInstaller. Self-contained `linux-x64` publish, `packaging/linux/` systemd units
(`Restart=on-failure`), `/opt/coldceph/{control,node}`, `/etc/coldceph/*.yaml`, `dpkg-deb` →
GitHub Release assets.

CI: all branches run unit tests (`dotnet test` excluding `ColdCeph.E2E.Tests`) and a separate
E2E job that only runs `ColdCeph.E2E.Tests`. The `[SetUpFixture]` starts Ceph via Testcontainers.
The unit job collects coverage and runs `.github/scripts/coverage-summary.js`, which lists every
production file no test executes; the E2E job runs under `--blame-hang`. Both upload test results,
and an E2E failure uploads every container's logs — a flaky run has to leave something behind.
CI plans one version before those jobs: non-`main` runs use `0.0.0-ci.<run>`, while serialized
`main` runs use semantic-release with Conventional Commits to determine the next SemVer. After
both test jobs pass, semantic-release publishes the two versioned debs plus checksums, creates the
`v<version>` tag and GitHub Release, and skips publishing when no release-worthy commit exists.

Dependabot (`.github/dependabot.yml`) opens weekly version-update PRs for NuGet, npm release
tooling, and GitHub Actions. Commits use `chore(deps)` so they do not cut a release. The Ceph
daemon image is not Dependabot-managed: compose and E2E pin a tag that still ships ceph-container
entrypoints, and `latest-reef` does not.

`agent-up.json` launches local Control with injected `WEB_PORT`. Applications consume that
variable; do not hard-code the development operator port when integrating with Agent-Up.

`./cc-debug` is the maintainer workflow CLI (like `au-debug`). `up` runs `docker compose up -d --wait`
and starts Control only when `/health` is not already ready. `status` prints compose `ps` plus
Control readiness. `allow` POSTs Hosts Allow for every pending join (or one host id). `down` stops
compose and stops Control only if `up` started it. `./cc-debug` (or `screenshot`) signs in and
captures operator HTML. Optional path limits the capture. `pages` lists routes. Control URL is
`COLDCEPH_OPERATOR_URL` or `http://127.0.0.1:$WEB_PORT`. Screenshots write under
`.git/coldceph/debug/`. Do not `docker compose up` or `dotnet run` Control with a one-off env
list from the shell for this workflow, and do not walk login or click pages by hand.

# Local development

Root `compose.yaml` is a **multi-host** Ceph for **local IDE getting-started only**: one container
per node (`mon`, `mgr`, `node-a`/`node-b`/`node-c`, `rgw`). Each service is written out in full
(no YAML anchors). ColdCeph.Node is not a compose service. Each storage node has one BlueStore OSD
on a tmpfs ramdisk (loop device, default 1GiB). Replica size 3 places one copy on each storage
host. E2E does not use this file; the Xcepto `[SetUpFixture]` still starts a single Testcontainers
`demo` container. Pin `CEPH_IMAGE` to a daemon tag that still ships the ceph-container entrypoints
(`v7.0.3-stable-7.0-quincy-centos-stream8`); `latest-reef` does not.

The cluster is fully ephemeral: no named volumes. MON, MGR, OSD, RGW, and `/etc/ceph` live on
tmpfs. `./cc-debug down` discards the cluster; `./cc-debug up` after a down bootstraps a new
one. Storage nodes fetch conf/keyrings from the monitor over the compose network (HTTP on
`CEPH_CONFIG_PORT`, not published to the host). If a storage container is recreated while the
monitor is still up, its entrypoint **purges that hostname’s old OSDs** then prepares a fresh
ramdisk OSD so CRUSH matches the live disks. That purge is compose bootstrap only — StoragePlane
must still never `out` / `purge` / destroy. The compose network is a pinned subnet with a static
`MON_IP` (`172.28.90.10`). Ready means `ceph osd tree` shows three hosts, `ceph -s` is healthy, and
RGW answers on host 7481. The RGW entrypoint creates the `coldceph` S3 user once
radosgw is listening.

ColdCeph.Node is **not** in compose. Run the `Node-a` / `Node-b` / `Node-c` launch profiles so
each process matches a CRUSH host (`node-a` / `node-b` / `node-c`), listens on `7081` / `7082` /
`7083`, and docker-execs into `coldceph-node-a` / `coldceph-node-b` / `coldceph-node-c` to
start/stop that host’s `ceph-osd`. Host processes reach Control at `127.0.0.1` without docker
hairpin. After Allow, each Node pushes OSD and device inventory to Control; Control’s Osds and
Devices reconcilers visit **every enrolled host** for start/stop/wake/standby, not the first one.
They do not poll Node GET lists. Control runs from the IDE (`Control` launch profile or
`./cc-debug up`). Nodes join with `POST /v1/hosts/join` (no join token) and
`X-ColdCeph-Node-Endpoint`. Hosts keeps each request **pending** until the operator Allows it on
`/hosts`. That Allow is a one-time trust decision and survives Control restart. Deny blocks that
node until Allow. Credential and port defaults live in `.env.example`
and in the Control/Node `Properties/launchSettings.json` profiles. Every default is overridable
with the same env var name. Optional `COLDCEPH_NODE_ENDPOINT` is configured discovery: Control
seeds that host as already enrolled. `COLDCEPH_NODE_TOKEN` is only Control→Node command auth, not
a join secret.

Control talks to compose Ceph by running the `ceph` CLI from the Control process
(`Process.Start`). Packaged installs use `ceph` on PATH (optional `COLDCEPH_CEPH_CONF` /
`COLDCEPH_CEPH_KEYRING`). Local compose uses `COLDCEPH_CEPH_CONTAINER` (default
`coldceph-mon`) so Control runs `docker exec {container} ceph …` — never a repo bash
wrapper. `docker/ceph/ceph` stays a maintainer convenience for a shell, not a Control
argv. Control talks to RGW through `COLDCEPH_RGW`.

# Gold Standards

- Agent-Up: capability slices, Controller → Service → Provider, 1:1 test projects, NUnit
  positive+negative, executable architecture tests, systemd + `.deb`, fail-closed robustness.
- HiveShardEE: ASP.NET Core MVC + Razor Views SSR, `AppComposition`, form POST + PRG,
  `WebApplicationFactory` HTML asserts.
