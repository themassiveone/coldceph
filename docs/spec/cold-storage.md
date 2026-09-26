# Cold-Storage Orchestration Layer for Ceph

**Status:** Design specification  
**Target:** Any Ceph installation  
**Primary workload:** S3-compatible cold/backup object storage  
**Core idea:** Keep the Ceph control plane available while allowing all HDD-backed OSD storage devices to stop and enter hardware standby. An external S3 gateway wakes the storage plane before forwarding requests to Ceph RGW.

---

# 1. Executive Summary

This system provides a **cold-storage orchestration layer around an existing Ceph cluster**.

Ceph remains entirely responsible for:

- distributed object storage;
- replication or erasure coding;
- CRUSH placement;
- node and OSD topology;
- object checksums;
- scrubbing and deep scrubbing;
- recovery;
- backfill;
- OSD replacement;
- placement-group state;
- S3 object persistence through RADOS Gateway.

The application is responsible for:

- providing the externally visible S3 endpoint;
- preventing S3 access while the Ceph storage plane is unavailable;
- detecting incoming work;
- waking physical HDDs;
- starting stopped Ceph OSDs;
- waiting for Ceph to become ready;
- forwarding S3 traffic to RGW;
- detecting periods of inactivity;
- quiescing S3 traffic;
- validating Ceph integrity and readiness;
- stopping OSDs safely;
- spinning HDDs down;
- exposing an operator-oriented health and integrity dashboard;
- guiding disk replacement and maintenance workflows.

The system **does not implement distributed storage itself**.

Conceptually:

```text
                       Clients
                          │
                          │ S3
                          ▼
              ┌──────────────────────┐
              │ Cold Storage Gateway │
              │ + Controller         │
              └──────────┬───────────┘
                         │
              ┌──────────┴───────────┐
              │                      │
        Ceph Control API        Host Nodes
              │                      │
              │                 OS + disks
              ▼                      ▼
       MON / MGR / CRUSH       Physical HDDs
              │
              ▼
             OSDs
              │
              ▼
             RADOS
              │
              ▼
             RGW
```

During normal cold operation:

```text
MON/MGR                  running
Controller               running
S3 gateway               running
Host nodes              running
RGW                      running or idle
HDD-backed OSDs          stopped
HDDs                     standby
Ceph data PGs            unavailable
External S3 API          reachable but gated
```

When an S3 request arrives:

```text
request
   │
   ▼
wake coordinator
   │
   ├─ wake disks
   ├─ start OSDs
   ├─ wait for peering
   ├─ verify required PG state
   └─ enable backend traffic
          │
          ▼
         RGW
          │
          ▼
        RADOS
```

The resulting system is intentionally optimized for **capacity efficiency, durability, low idle power consumption, and operational simplicity**, not latency.

---

# 2. Design Goals

## 2.1 Primary goals

The system shall:

1. Work with a generic Ceph installation.
2. Not require Proxmox, cephadm, Rook, Kubernetes, or any specific Linux distribution.
3. Use Ceph as the authoritative distributed storage engine.
4. Permit all managed HDD-backed OSD devices to enter physical standby.
5. Keep enough non-HDD infrastructure online to detect requests and wake storage.
6. Expose an S3-compatible endpoint independent from the RGW endpoint.
7. Reject or delay requests while storage cannot safely serve them.
8. Never silently acknowledge a write that the gateway knows cannot satisfy configured durability policy.
9. Expose Ceph integrity and redundancy information in a simpler operator-facing form.
10. Make HDD replacement and maintenance explicit workflows.
11. Handle heterogeneous HDD counts and capacities.
12. Respect Ceph's existing CRUSH failure domains and placement policies.
13. Be resilient against controller crashes during wake/sleep transitions.
14. Avoid modifying Ceph's internal storage format.
15. Avoid depending on private Ceph implementation details wherever possible.

---

# 3. Non-Goals

The system shall **not**:

- implement its own replication;
- implement its own erasure coding;
- implement object placement;
- replace CRUSH;
- replace Ceph MON consensus;
- replace Ceph PG recovery;
- replace BlueStore;
- directly modify RADOS objects;
- pretend sleeping OSDs are healthy Ceph OSDs;
- suppress genuine Ceph corruption warnings;
- automatically perform destructive OSD operations;
- require all Ceph clusters to use the same daemon deployment mechanism.

The application is an **orchestrator**, not another distributed storage engine.

---

# 4. Fundamental Architecture Principle

The application must maintain a strict separation between:

```text
STORAGE TRUTH             Ceph
POWER / LIFECYCLE TRUTH   Application + host nodes
CLIENT ENTRYPOINT         Application S3 gateway
```

Ceph decides:

```text
Where does object X live?
How many copies/shards exist?
Which OSD owns a PG?
Is data degraded?
Does a PG need recovery?
Is an object inconsistent?
How should a failed OSD be rebuilt?
```

The orchestration layer decides:

```text
Should the storage plane currently be awake?
May new S3 requests enter the cluster?
Should the HDD OSDs be stopped?
Is this disk intentionally sleeping?
Should this disk be woken?
What operator action should be recommended?
```

These responsibilities must not overlap.

---

# 5. Required Deployment Topology

At least the following components must remain on **always-on storage**:

```text
Ceph MON
Ceph MGR
Cold-storage controller
S3 gateway
Host nodes
Controller state
```

Their state must not depend exclusively on HDDs that the controller itself is capable of sleeping.

A typical node could therefore look like:

```text
Host
├── SSD / NVMe / system disk
│   ├── Linux
│   ├── Ceph MON/MGR where applicable
│   ├── controller or node
│   └── application state
│
├── HDD A
│   └── OSD 1
├── HDD B
│   └── OSD 2
└── HDD C
    └── OSD 3
```

Ceph BlueStore may optionally place DB/WAL on separate SSD/NVMe devices. The storage controller must model the physical devices associated with an OSD rather than assuming an OSD maps exactly one-to-one to `/dev/sdX`.

---

# 6. Logical Components

## 6.1 S3 Gateway

The gateway is the only S3 endpoint exposed to clients.

Responsibilities:

- terminate or proxy TLS;
- accept S3 requests;
- determine whether the storage plane is ready;
- initiate a wake operation if required;
- coordinate concurrent cold requests;
- wait, reject, or retry according to configured policy;
- proxy accepted requests to RGW;
- preserve S3 protocol semantics;
- track active requests;
- prevent sleep while requests remain in flight.

It should ideally be a **transparent S3 reverse proxy**, not a complete S3 implementation.

---

## 6.2 Cluster Controller

The controller owns the global state machine.

Responsibilities:

- coordinate wake operations;
- coordinate sleep operations;
- query Ceph state;
- query nodes;
- evaluate safety predicates;
- maintain operation locks;
- maintain application state;
- track which Ceph flags it owns;
- expose the management API;
- aggregate events;
- derive human-readable health status.

Only one logical controller may execute a storage-state transition at a time.

---

## 6.3 Ceph Control Adapter

All Ceph-specific interaction is isolated behind a logical interface such as:

```text
CephControlAdapter
    getClusterIdentity()
    getClusterStatus()
    getHealthDetail()
    getMonitorQuorum()
    getOSDs()
    getOSDTree()
    getOSDMetadata()
    getPools()
    getPlacementGroups()
    getDevices()
    getFlags()

    setNoOut(scope)
    clearNoOut(scope)

    requestScrub(osd)
    requestDeepScrub(osd)

    safeToStop(osds)
    safeToDestroy(osds)
```

The rest of the application must not care whether this is implemented using:

- `librados`;
- Ceph MON commands;
- the `ceph` CLI with JSON output;
- Dashboard REST;
- some future Ceph management interface.

Ceph provides programmatic monitor/manager command mechanisms through librados, including monitor and manager commands; the CLI is another frontend to the same management model.

---

## 6.4 Host Node

One privileged node runs on every physical machine containing managed OSD devices.

Responsibilities:

- map Ceph OSD IDs to local processes/containers;
- map OSDs to local block devices;
- obtain stable physical drive identity;
- stop an OSD;
- verify the OSD process has stopped;
- verify the backing device is no longer actively used by that OSD;
- wake an HDD;
- request HDD standby;
- inspect physical disk state;
- report SMART/device information where available;
- start an OSD;
- verify the process has started.

The controller **must not directly execute arbitrary remote shell commands**.

The node exposes a constrained, authenticated management protocol.

---

## 6.5 Operator Dashboard

The dashboard presents two distinct categories of health:

### Raw Ceph health

What Ceph itself reports:

```text
HEALTH_OK
HEALTH_WARN
HEALTH_ERR
PG states
OSD up/down
OSD in/out
scrub state
recovery state
```

### Cold-storage operational health

What the orchestration system concludes:

```text
COLD
WAKING
READY
QUIESCING
SLEEPING
MAINTENANCE
DEGRADED
FAULTED
```

The dashboard must **never replace or hide raw Ceph health information**.

Example:

```text
Cold-storage state
COLD — expected

Ceph health
HEALTH_ERR

Reason
12 OSDs intentionally stopped.
512 PGs inactive because the data plane is intentionally cold.

Last verified warm state
2026-09-26 03:47 UTC
512 / 512 PGs active+clean
0 inconsistent PGs
0 degraded objects
```

This distinction is essential because once all HDD OSDs are intentionally stopped, Ceph correctly considers the data plane unavailable.

---

# 7. Application State Machine

The global storage state must be explicit.

Recommended states:

```text
COLD
WAKING
PEERING
READY
QUIESCING
SLEEPING
MAINTENANCE
DEGRADED
FAULTED
```

---

## 7.1 COLD

Expected conditions:

- S3 ingress is gated;
- managed HDD OSDs are stopped;
- managed HDDs are in standby where supported;
- MON quorum remains available;
- MGR is ideally available;
- controller remains available;
- host nodes remain available.

Ceph may show:

```text
OSDs down
PGs inactive
PGs stale
HEALTH_WARN / HEALTH_ERR
```

That is expected.

`noout` prevents down OSDs from automatically being marked `out`; it does **not** make those OSDs available or keep PGs serviceable.

---

## 7.2 WAKING

Entered because of:

- incoming S3 request;
- scheduled maintenance;
- requested disk replacement;
- operator action;
- scheduled scrub;
- integrity verification.

Actions:

```text
wake physical drives
wait for drive readiness
start OSDs
```

No normal S3 traffic is forwarded yet.

---

## 7.3 PEERING

OSDs have started, but Ceph has not yet reached the required service condition.

The controller watches:

```text
OSD up/in state
PG peering
PG active state
PG clean state
recovery
backfill
health checks
```

---

## 7.4 READY

The Ceph state satisfies the configured admission policy.

Example:

```text
all managed OSDs expected online
all required RGW pools available
no stale PGs
no incomplete PGs
no unfound objects
no relevant failed health check
```

A stricter write policy can additionally require:

```text
all relevant PGs active+clean
no degraded objects
no recovery
no backfill
```

Ceph documentation distinguishes `active` from `clean`: `active` indicates that the PG can generally service I/O, while `clean` indicates expected replicas/shards have successfully peered and placement is complete.

---

## 7.5 QUIESCING

New S3 operations are blocked.

The controller waits for:

- active requests to complete;
- multipart operations according to configured semantics;
- Ceph recovery to finish;
- backfill to finish;
- required integrity conditions;
- pending controller actions.

---

## 7.6 SLEEPING

The controller is actively transitioning OSDs and drives to the cold state.

No S3 requests may reach RGW.

---

## 7.7 MAINTENANCE

Used for:

- disk replacement;
- OSD replacement;
- cluster upgrades;
- manual operator work;
- forced scrub;
- CRUSH changes.

Automatic sleep timers must be disabled while maintenance mode is active.

---

## 7.8 DEGRADED

The storage plane remains operational but does not satisfy desired redundancy/integrity policy.

Normally:

```text
GET may be configurable
PUT/POST/DELETE blocked
automatic sleep blocked
```

---

## 7.9 FAULTED

The controller cannot establish a trustworthy cluster state.

Examples:

- MON quorum unavailable;
- host nodes disagree with Ceph topology;
- some OSDs cannot start;
- a drive disappeared;
- controller lost ownership information for Ceph flags;
- PGs remain incomplete;
- unfound objects exist.

No automatic sleep transition should proceed from `FAULTED`.

---

# 8. Core Safety Invariants

These invariants are non-negotiable.

## 8.1 Never power down a block device beneath a running OSD

The required sequence is:

```text
stop OSD
↓
verify OSD stopped
↓
verify device can be released
↓
request physical standby
```

Never:

```text
hdparm / ATA standby
↓
while ceph-osd remains active
```

---

## 8.2 Never mark an OSD `out` merely because it is sleeping

`out` tells Ceph that data should be remapped elsewhere.

That is exactly what the cold-storage workflow does not want.

The intended sleep operation is:

```text
OSD remains logically in
+
noout prevents automatic transition out
+
OSD becomes down intentionally
```

Ceph documents `noout` specifically as preventing down OSDs from automatically being marked out; scoped OSD/CRUSH-group flags are supported.

---

## 8.3 `ok-to-stop` is not permission to sleep the entire data plane

Ceph's:

```bash
ceph osd ok-to-stop ...
```

answers:

> Can these OSDs stop while data remains readable and writable?

It may permit reduced redundancy, but its purpose is continued service availability.

Stopping **every data OSD** intentionally makes the cold store unavailable.

Therefore:

```text
ok-to-stop
```

is useful for normal maintenance and partial failure domains, but it is **not the sleep-all-OSDs safety predicate**.

The cold transition instead requires:

```text
cluster known-good before shutdown
+
S3 traffic quiesced
+
required integrity state achieved
+
no pending recovery/backfill
+
application intentionally accepts temporary total data unavailability
```

---

## 8.4 `safe-to-destroy` is never part of normal sleep

These concepts must remain separate:

```text
ok-to-stop       temporary daemon outage
safe-to-destroy  permanent OSD removal
sleep            deliberate temporary total/partial data unavailability
```

Ceph explicitly provides `safe-to-destroy` before destructive OSD removal.

The sleep workflow must never call:

```text
osd destroy
osd purge
osd rm
```

---

## 8.5 Only clear flags owned by this application

Suppose the operator manually sets:

```text
noout
```

before the controller starts.

The controller must not later execute:

```text
unset noout
```

and accidentally remove the operator's maintenance flag.

State must record:

```text
flag
scope
previous state
operation ID
time set
controller instance
```

The application may clear only flags that it can prove it introduced.

---

## 8.6 Reconcile reality after every restart

Stored controller state is not authoritative.

After startup:

```text
stored state: COLD
```

cannot be trusted until the application queries:

```text
Ceph
+
all host nodes
+
managed drives
+
OSD runtime state
```

Actual infrastructure always wins.

---

# 9. Wake Workflow

A recommended wake transition is:

```text
COLD
  │
  ▼
WAKING
  │
  ▼
PEERING
  │
  ▼
READY
```

Detailed algorithm:

```text
1. Acquire global transition lease.

2. Gate external S3 forwarding.

3. Query MON quorum.

4. Query expected OSD inventory.

5. Query all host nodes.

6. Verify every required managed OSD has a known host/device mapping.

7. Wake all required HDDs.

8. Wait until each physical device responds normally.

9. Start all required OSDs.

10. Wait until Ceph sees expected OSDs as up.

11. Verify expected OSDs remain in.

12. Wait for PG peering.

13. Monitor recovery/backfill.

14. Evaluate read readiness.

15. Evaluate write readiness.

16. Clear only controller-owned temporary flags when appropriate.

17. Enter READY.

18. Release queued S3 requests.
```

---

# 10. Sleep Workflow

Recommended sequence:

```text
READY
  │
  ▼
QUIESCING
  │
  ▼
SLEEPING
  │
  ▼
COLD
```

Detailed algorithm:

```text
1. Acquire global transition lease.

2. Disable acceptance of new backend S3 operations.

3. Wait for existing proxied requests to finish.

4. Confirm no controller maintenance task is active.

5. Query Ceph health.

6. Confirm relevant PGs satisfy configured pre-sleep condition.

7. Confirm no unrecovered / unfound / inconsistent data exists.

8. Wait for recovery and backfill to finish.

9. Optionally require scheduled scrub policy to be satisfied.

10. Capture a "last known good" integrity snapshot.

11. Apply scoped noout to OSDs that will sleep.

12. Verify the intended noout state.

13. Stop OSDs through host nodes.

14. Confirm OSD processes stopped.

15. Confirm Ceph sees them down.

16. Request standby on physical HDDs.

17. Verify standby when hardware permits.

18. Persist final transition record.

19. Enter COLD.

20. Release transition lease.
```

The system should not use `norecover`, `nobackfill`, or `norebalance` merely as a routine substitute for waiting until the cluster is settled. Those flags explicitly suspend recovery/rebalancing behavior and can hide work the cluster should complete before sleeping.

---

# 11. Request Admission Policy

Reads and writes should be treated differently.

## 11.1 Read readiness

A reasonable read predicate is:

```text
MON quorum available
AND RGW reachable
AND every RGW-required pool can service IO
AND no relevant PG is stale
AND no relevant PG is incomplete
AND no relevant PG has unfound data
```

The safest initial implementation can simply require all RGW-related PGs to be active before releasing reads.

---

## 11.2 Write readiness

Writes should be stricter:

```text
read_ready
AND relevant PGs active+clean
AND no degraded objects in those pools
AND no ongoing recovery/backfill
AND required OSDs are up/in
AND no full condition
AND no integrity condition blocks writes
```

The application should prefer rejecting a write over attempting a degraded write.

---

# 12. Strict Durability Semantics

There is an important distinction between:

```text
"The gateway starts writes only when the cluster is fully redundant"
```

and:

```text
"Ceph can never acknowledge an in-flight write if redundancy becomes
degraded during that request"
```

They are not equivalent.

Ceph pools have a `size` and `min_size`. `min_size` controls the minimum number of available replicas/shards required for I/O. For an erasure-coded pool, `size` represents the total `k+m` shard count.

Therefore two operating modes should be documented.

## 12.1 Normal strict-admission mode

Before forwarding a write:

```text
require fully healthy / clean state
```

If an OSD fails during the write, Ceph's configured `min_size` semantics still apply.

This gives high availability and normal Ceph behavior.

---

## 12.2 Full-redundancy-acknowledgement mode

An operator who requires:

> Do not permit writes unless the complete configured replica/shard count participates.

may choose stricter Ceph pool configuration.

That potentially means increasing `min_size` toward the complete configured pool size.

This severely sacrifices write availability.

The orchestration application should:

- detect the configuration;
- report whether it satisfies the selected durability profile;
- refuse writes if the profile is violated;

but should **not silently rewrite `min_size` automatically**.

Changing pool durability parameters must be an explicit administrative action.

---

# 13. S3 Gateway Requirements

The gateway should avoid becoming another object-storage implementation.

Its preferred role is:

```text
wake-aware reverse proxy
```

rather than:

```text
parse object
store object itself
later replay object into RGW
```

---

## 13.1 Signature transparency

AWS Signature Version 4 signs parts of the HTTP request, including:

- request method;
- path;
- query parameters;
- signed headers;
- normally the `Host` header;
- payload information.

Therefore the proxy must preserve signed request semantics.

It must not casually rewrite:

```text
bucket path
query string
Host
x-amz-* headers
signed Content-* headers
body encoding
```

---

## 13.2 Required S3 behaviors

At minimum test:

```text
GET Object
HEAD Object
PUT Object
DELETE Object
ListObjectsV2
multipart create
multipart part upload
multipart completion
multipart abort
Range GET
conditional requests
presigned URLs
SigV4 streaming/chunked uploads
virtual-hosted bucket addressing
path-style bucket addressing where enabled
```

---

## 13.3 Cold request behavior

Two gateway modes should exist.

### Wait mode

```text
client request
↓
connection remains open
↓
cluster wakes
↓
request forwarded
```

Best when clients tolerate spin-up latency.

### Retry mode

Return something such as:

```http
HTTP/1.1 503 Service Unavailable
Retry-After: 30
```

Best for clients with robust retry policies.

---

## 13.4 Large uploads

The gateway should avoid buffering multi-gigabyte backup objects merely because Ceph is cold.

Where supported, use:

```http
Expect: 100-continue
```

semantics so the gateway can wake the backend before allowing the client to send the full body.

If the client begins sending immediately, the gateway needs an explicit policy:

```text
stream while waking
buffer up to N bytes
backpressure TCP
or reject/retry
```

Unlimited local buffering must not be the default.

---

# 14. Concurrent Wake Requests

If 100 clients arrive while the cluster is cold:

```text
100 requests
     │
     ▼
one wake operation
```

not:

```text
100 independent wake operations
```

Use a single-flight mechanism:

```text
cold request #1 → creates wake operation W123
cold request #2 → joins W123
cold request #3 → joins W123
...
```

All waiting requests receive the same wake result.

---

# 15. Idle Detection

Sleep must not depend only on network inactivity.

The system should require all of:

```text
no active S3 requests
AND no queued requests
AND no active multipart operation requiring immediate continuation
AND no maintenance activity
AND no Ceph recovery/backfill
AND no scrub operation that policy requires to complete
AND idle timer expired
```

The idle timeout should be configurable.

Example:

```yaml
sleep:
  idle_after: 45m
```

---

# 16. Ceph Integration Strategy

The application should implement **three integration tiers**.

---

# 17. Tier 1 — Native Ceph Command API

This should be the primary portable interface.

The `ceph` utility exposes the cluster's monitor/manager command interface. The same command model can be accessed programmatically through librados monitor/manager command calls.

An implementation may therefore use:

```text
librados
```

or:

```bash
ceph ... --format json
```

The core domain model should not know which transport was selected.

---

## 17.1 Essential read commands

| Command | Purpose |
|---|---|
| `ceph status --format json` | Primary cluster summary |
| `ceph health detail --format json` | Detailed health conditions |
| `ceph quorum_status --format json` | MON quorum verification |
| `ceph fsid` | Persistent cluster identity |
| `ceph version` | Client version |
| `ceph versions --format json` | Running daemon versions where available |
| `ceph mgr dump --format json` | Manager topology |
| `ceph osd stat --format json` | OSD summary |
| `ceph osd tree --format json` | CRUSH hierarchy and OSD host topology |
| `ceph osd dump --format json` | OSD map and flags |
| `ceph osd df --format json` | Per-OSD usage/capacity |
| `ceph osd metadata [id] --format json` | OSD metadata |
| `ceph osd find <id> --format json` | CRUSH location |
| `ceph pg stat --format json` | PG-state summary |
| `ceph pg dump --format json` | Complete PG state |
| `ceph osd pool ls detail --format json` | Pool inventory/configuration |
| `ceph osd pool get <pool> all --format json` | Pool durability configuration |
| `ceph osd erasure-code-profile get <profile> --format json` | EC policy |
| `ceph osd pool application get` | Identify application ownership such as `rgw` |

The current Ceph administration interface documents `osd tree`, `metadata`, `ok-to-stop`, pool parameters, erasure-code profiles, PG commands and related management operations as standard `ceph` commands.

---

## 17.2 `ceph status`

```bash
ceph status --format json
```

Use for:

- global health;
- number of OSDs;
- PG state distribution;
- recovery activity;
- data usage;
- service summary.

Do not base all readiness decisions on the top-level `HEALTH_OK/WARN/ERR` field alone.

A cold cluster will intentionally violate normal Ceph availability expectations.

---

## 17.3 `ceph health detail`

```bash
ceph health detail --format json
```

Use to classify health checks into:

```text
expected because cold
transient because waking
actual integrity problem
configuration problem
capacity problem
unknown
```

The dashboard should retain the original Ceph health-check ID and text.

---

## 17.4 `ceph quorum_status`

```bash
ceph quorum_status --format json
```

Use before every major state transition.

If monitor quorum cannot be established:

```text
wake may proceed only according to explicit recovery policy
sleep MUST NOT begin automatically
writes MUST NOT be admitted
```

---

## 17.5 `ceph osd tree`

```bash
ceph osd tree --format json
```

This is the principal generic topology source.

It exposes the CRUSH hierarchy:

```text
root
└── datacenter
    └── rack
        └── host
            └── osd
```

The application should not invent a second topology model.

It may cache a normalized form for UI purposes, but CRUSH remains authoritative.

---

## 17.6 `ceph osd metadata`

```bash
ceph osd metadata
ceph osd metadata 12
```

Use as supplementary metadata for:

- daemon identity;
- host relationship;
- implementation/version details;
- correlating an OSD with local host discovery.

The host node must independently verify the actual local block-device relationship before power operations.

---

## 17.7 `ceph osd ok-to-stop`

```bash
ceph osd ok-to-stop 1 2 3
```

Ceph defines this as checking whether the supplied OSDs can be stopped **without immediately making data unavailable**; redundancy may become degraded while data remains readable and writable.

Use for:

- individual OSD maintenance;
- host maintenance;
- replacement workflows;
- validating partial shutdowns.

Do **not** require it to approve the global cold transition.

---

## 17.8 OSD `noout`

For individual OSDs, installations may support:

```bash
ceph osd add-noout osd.12
ceph osd rm-noout osd.12
```

For CRUSH scopes:

```bash
ceph osd set-group noout host-a
ceph osd unset-group noout host-a
```

For a complete cluster:

```bash
ceph osd set noout
ceph osd unset noout
```

Scoped flags are preferable because they reduce accidental interference with unrelated operator actions. Ceph documentation specifically supports scoped flags on OSDs and CRUSH buckets/classes.

The application should select the narrowest scope that covers all sleeping OSDs.

---

## 17.9 `ceph pg stat`

```bash
ceph pg stat --format json
```

Fast readiness probe.

Use for state-machine polling when full PG details are unnecessary.

---

## 17.10 `ceph pg dump`

```bash
ceph pg dump --format json
```

Use when:

- diagnosing readiness failure;
- determining affected pools;
- building detailed integrity views;
- investigating stuck/degraded/inconsistent PGs.

Do not poll full dumps excessively on very large clusters.

---

## 17.11 Pool configuration

For each relevant RGW pool:

```bash
ceph osd pool get <pool> all --format json
```

Important fields include:

```text
size
min_size
crush_rule
erasure_code_profile
pg_num
application metadata
```

The controller should construct an explicit durability model from these values rather than assuming `3x replicated`.

---

# 18. Identifying RGW-Relevant Pools

The gateway should know which pools must be available before RGW can service requests.

Possible discovery:

```bash
ceph osd pool application get
```

RGW-managed pools are normally associated with the `rgw` application.

However, production installations may use custom layouts.

Therefore configuration should support:

```yaml
ceph:
  rgw_pools:
    mode: discover
```

and:

```yaml
ceph:
  rgw_pools:
    mode: explicit
    pools:
      - default.rgw.meta
      - default.rgw.buckets.index
      - default.rgw.buckets.data
```

Discovery may provide defaults, but explicit operator overrides must always exist.

---

# 19. Tier 2 — Ceph Dashboard REST API

The Ceph Dashboard exposes an HTTP API under:

```text
https://<dashboard>/api
```

It uses JSON, JWT authentication, and versioned media types. Current documentation requires callers to specify an API media version such as:

```http
Accept: application/vnd.ceph.api.v1.0+json
```

Ceph also warns that some Dashboard API endpoints remain under active development and can receive backward-incompatible changes between releases.

Therefore:

> Dashboard REST is an optional adapter, not the portable core contract.

---

## 19.1 Authentication

### `POST /api/auth`

Obtains a Dashboard JWT.

Example logical request:

```http
POST /api/auth
Content-Type: application/json
Accept: application/vnd.ceph.api.v1.0+json

{
  "username": "...",
  "password": "..."
}
```

Subsequent calls use:

```http
Authorization: Bearer <token>
```

The API also exposes token checking/logout operations.

---

# 20. Dashboard Health Endpoints

Useful optional endpoints include:

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `/api/health/snapshot` | Quick cluster overview analogous to `ceph status` |
| `GET` | `/api/health/minimal` | Reduced health report |
| `GET` | `/api/health/full` | Detailed health report |
| `GET` | `/api/health/get_cluster_capacity` | Capacity information |
| `GET` | `/api/health/get_cluster_fsid` | Cluster identifier |

These endpoints are documented in the current Ceph Dashboard API.

For core readiness logic, prefer the portable command abstraction.

---

# 21. Dashboard OSD Endpoints

Useful optional API operations include:

```text
GET /api/osd
GET /api/osd/{svc_id}
GET /api/osd/{svc_id}/devices
GET /api/osd/{svc_id}/smart
GET /api/osd/{svc_id}/histogram
GET /api/osd/settings
GET /api/osd/flags/individual
PUT /api/osd/flags/individual
POST /api/osd/{svc_id}/scrub
GET /api/osd/safe_to_destroy
```

The API also includes destructive or lifecycle-changing operations.

Those must never be mapped casually into the sleep state machine.

For example:

```text
safe_to_destroy
```

belongs to **disk removal**, not sleep. The current Dashboard API explicitly exposes an OSD safe-to-destroy check.

---

# 22. Dashboard Pool Endpoints

Useful endpoints include:

```text
GET /api/pool
GET /api/pool/{pool_name}
GET /api/pool/{pool_name}/configuration
```

Use these for:

- capacity;
- replica size;
- minimum size;
- CRUSH rule;
- EC profile;
- application association;
- dashboard presentation.

The documented pool configuration endpoint is:

```text
GET /api/pool/{pool_name}/configuration
```

Pool-changing endpoints should not be used automatically by normal cold-storage operation.

---

# 23. Dashboard Host Endpoints

Current Dashboard APIs include operations such as:

```text
GET /api/host
GET /api/host/{hostname}
GET /api/host/{hostname}/daemons
GET /api/host/{hostname}/devices
GET /api/host/{hostname}/inventory
GET /api/host/{hostname}/smart
```

These can enrich the management UI.

However, some host mutation features explicitly depend on the Ceph Orchestrator being enabled. For example, the Dashboard documentation notes that updating host maintenance/drain state through `PUT /api/host/{hostname}` is supported only when the Ceph Orchestrator is enabled.

Therefore the portable application must **not** require:

```text
cephadm
orchestrator
Dashboard host maintenance
```

to function.

---

# 24. Dashboard Task API

Some Dashboard operations return:

```http
202 Accepted
```

and continue asynchronously.

When Dashboard mutation APIs are used, the application should also understand the task API, including:

```text
GET /api/task
```

This is optional because the native command adapter may use a different completion mechanism.

---

# 25. Tier 3 — Deployment-Specific Adapters

The system may provide optional adapters for:

```text
cephadm
systemd/package installations
Rook
Kubernetes
distribution-specific service managers
```

These adapters handle **daemon process lifecycle**, not Ceph data semantics.

Example interface:

```text
OsdRuntimeDriver

discover(osdId)
start(osdId)
stop(osdId)
isRunning(osdId)
waitStarted(osdId)
waitStopped(osdId)
```

A package-based installation might use systemd.

A cephadm cluster might manage containerized OSD daemons.

A Rook cluster might require Kubernetes operations.

The core state machine must not care.

---

# 26. Why an Out-of-Tree `ceph-mgr` Module Should Not Be Required

A custom MGR module could provide deep integration, but it creates additional Ceph-version coupling.

Ceph supports manager modules and allows them to issue monitor commands, but an external controller using public management interfaces creates a cleaner compatibility boundary.

A MGR plugin may therefore exist as an **optional enhancement** for:

- custom Ceph health checks;
- richer metrics;
- event streaming;
- dashboard integration;

but should not be mandatory for basic wake/sleep operation.

---

# 27. Ceph Readiness Predicates

Readiness must be represented as explicit predicates rather than a single boolean.

---

## 27.1 `control_plane_available`

Example:

```text
MON quorum known
AND Ceph status query succeeds
AND cluster FSID matches configured cluster
```

MGR availability may be required for optional features but should not necessarily prevent basic monitor commands.

---

## 27.2 `osd_plane_expected`

```text
every managed OSD has expected identity
AND every expected OSD is in
AND no unknown replacement OSD has appeared
```

---

## 27.3 `read_ready`

```text
control_plane_available
AND relevant OSDs up
AND relevant PGs active
AND no relevant stale PG
AND no relevant incomplete PG
AND no relevant unfound data
AND RGW reachable
```

---

## 27.4 `write_ready`

Recommended default:

```text
read_ready
AND relevant PGs active+clean
AND zero relevant degraded objects
AND zero relevant recovery/backfill
AND no relevant near-full/full write blocker
AND configured durability policy verified
```

---

## 27.5 `sleep_safe`

```text
controller owns transition lease
AND S3 ingress closed
AND zero active proxied requests
AND control_plane_available
AND relevant PGs satisfy configured clean condition
AND zero unfound objects
AND zero inconsistent PGs
AND no recovery/backfill
AND no maintenance operation
AND every managed OSD maps to a reachable host node
AND every managed drive has a known stable identity
```

---

# 28. Cold-State Health Model

The system must not say:

```text
Ceph is healthy
```

while all Ceph OSDs are stopped.

Instead expose:

```text
Operational state: COLD / expected
Ceph availability: UNAVAILABLE / expected
Last verified integrity: HEALTHY at T
```

Three independent concepts should exist:

```text
Availability
Durability
Last verified integrity
```

Example:

```text
Availability
COLD — S3 data plane offline by policy

Durability at last verification
12/12 OSDs present
100% PG active+clean
0 degraded objects

Integrity
Last deep-scrub window completed 17h ago
0 inconsistent PGs

Physical storage
12 HDDs
12 standby
0 missing
```

This avoids lying to the operator.

---

# 29. Integrity Dashboard Requirements

The dashboard should display at minimum:

## Cluster

```text
FSID
Ceph version distribution
MON quorum
MGR state
overall Ceph health
cold-storage state
time since last READY
time since last verified clean state
```

## OSDs

```text
OSD ID
host
CRUSH location
device class
up/down
in/out
noout ownership
capacity
used
available
physical drive identity
power state
last wake
last sleep
```

## Placement groups

```text
total
active+clean
active+degraded
peering
recovering
backfilling
stale
incomplete
inconsistent
unfound objects
```

## Pools

```text
pool
application
type
size
min_size
EC profile
CRUSH rule
capacity
PG count
durability policy assessment
```

## Physical devices

```text
stable ID
serial
model
capacity
rotational/nonrotational
host
OSD association
SMART summary
power state
replacement status
```

---

# 30. Disk Identity

Never treat:

```text
/dev/sdb
```

as durable identity.

Linux block-device enumeration may change.

The host node should build identity from stable attributes such as:

```text
/dev/disk/by-id/*
WWN
serial
persistent enclosure slot where available
```

The model should distinguish:

```text
PhysicalDeviceId
OperatingSystemPath
OSDId
```

These are different identifiers.

---

# 31. Physical Device State

Recommended state machine:

```text
UNKNOWN
ACTIVE
IDLE
SPINNING_DOWN
STANDBY
SPINNING_UP
FAILED
REMOVED
```

A device being `STANDBY` is **not** equivalent to the Ceph OSD being `down`.

They must be tracked separately.

Example:

```text
osd.7:
  ceph_state: down/in
  process_state: stopped
  disk_state: standby
  coldstore_state: expected-cold
```

---

# 32. Host Node API

An implementation-specific protocol can be REST, RPC, gRPC, message bus, etc.

The logical operations should be stable.

Example:

```text
GET  /v1/status
GET  /v1/osds
GET  /v1/osds/{id}
POST /v1/osds/{id}/start
POST /v1/osds/{id}/stop

GET  /v1/devices
GET  /v1/devices/{id}
POST /v1/devices/{id}/wake
POST /v1/devices/{id}/standby
```

Operations must be idempotent.

For example:

```text
POST stop(osd.5)
```

when `osd.5` is already stopped should report success with the observed state, not blindly fail.

Every mutation should include:

```text
operation ID
controller identity
deadline
requested state
```

---

# 33. Management API

The controller can expose a separate operator API.

Recommended conceptual routes:

```text
GET  /v1/cluster
GET  /v1/cluster/health
GET  /v1/cluster/integrity
GET  /v1/cluster/state

POST /v1/cluster/wake
POST /v1/cluster/sleep

GET  /v1/hosts
GET  /v1/hosts/{host}

GET  /v1/osds
GET  /v1/osds/{id}

GET  /v1/devices
GET  /v1/devices/{id}

POST /v1/devices/{id}/maintenance
POST /v1/devices/{id}/replace

GET  /v1/operations
GET  /v1/operations/{id}

GET  /v1/events
```

This API is entirely owned by the application and can remain much more stable than Ceph's Dashboard API.

---

# 34. Controller Persistence

Controller metadata is tiny.

It may contain:

```text
cluster identity
configuration
managed OSD set
managed physical device set
last state
last verified-clean snapshot
flag ownership
active transition
transition history
maintenance intents
disk replacement intents
node identities
audit log
```

This database **must not exist only inside the Ceph HDD pool being put to sleep**.

A local SSD, replicated lightweight database, or controller consensus store is appropriate.

---

# 35. Controller High Availability

A first implementation may have one controller.

A production HA design may run several.

The requirement is:

```text
at most one active storage transition coordinator
```

Use:

```text
leader election
lease
consensus
or equivalent fencing
```

Two controllers must never concurrently execute:

```text
controller A → sleep
controller B → wake
```

---

# 36. Crash Recovery

Every state transition must be resumable.

---

## 36.1 Controller crashes during wake

Example:

```text
5/12 disks awake
4 OSDs started
controller dies
```

On restart:

```text
query Ceph
query nodes
discover actual state
continue or fail safely
```

Do not return to `COLD` merely because the persisted state says COLD.

---

## 36.2 Controller crashes during sleep

Example:

```text
noout applied
7 OSDs stopped
3 disks standby
controller dies
```

Recovery must discover:

```text
which flags exist
which OSDs are running
which disks are awake
which transition owned the flags
```

Then choose:

```text
complete sleep
or recover to READY
```

based on configured recovery policy.

---

# 37. Disk Replacement Workflow

Replacement is separate from cold sleep.

Example UI:

```text
Disk XYZ
FAILED / replacement recommended

[Begin replacement]
```

Workflow:

```text
1. Wake storage cluster.

2. Enter MAINTENANCE.

3. Identify affected OSD.

4. Keep all other required storage online.

5. Use standard Ceph OSD drain/removal/replacement process.

6. Wait for Ceph to restore required placement/redundancy.

7. Use safe-to-destroy only when performing permanent OSD removal.

8. Stop affected daemon.

9. Physically replace disk.

10. Provision replacement OSD using environment-specific tooling.

11. Wait for recovery/backfill.

12. Verify active+clean.

13. Exit maintenance.

14. Permit cold transition again.
```

The application should guide this process but should leave data movement to Ceph.

---

# 38. Scrubbing Strategy

Sleeping HDDs conflict naturally with continuous maintenance.

Therefore the system should introduce **maintenance wake windows**.

Example:

```yaml
maintenance:
  wake:
    - saturday 02:00
  require_deep_scrub_age: 7d
```

A maintenance window might:

```text
wake all drives
start all OSDs
wait active+clean
allow/request scrub
allow/request deep scrub
wait for completion
repair if necessary
record integrity snapshot
sleep again
```

The system must never disable scrubbing permanently simply to maximize sleep time.

The purpose of the project is cold storage **with integrity**, not merely low power usage.

---

# 39. Device Health

Ceph exposes device associations and health information through its device management facilities and Dashboard APIs; the application can supplement this with host-level SMART information. The dashboard API also exposes host/device and OSD/device views where available.

However:

```text
SMART healthy
```

must never mean:

```text
Ceph data healthy
```

Those are separate layers.

---

# 40. Failure Handling Matrix

| Failure | Required behavior |
|---|---|
| One disk fails while cluster is cold | Record physical failure if detectable; wake cluster before determining Ceph-level impact |
| Disk refuses to wake | Enter FAULTED/DEGRADED; do not enable writes |
| OSD refuses to start | Remain PEERING/DEGRADED; block writes |
| MON quorum unavailable | Block automatic sleep and writes |
| MGR unavailable | Degrade optional monitoring; determine whether required core queries remain possible |
| Host node unreachable | Do not power-manage unknown devices; block sleep |
| PG remains peering | Do not enter READY |
| PG becomes incomplete | Block S3 writes and normally reads |
| Unfound object exists | Block sleep and writes; surface critical operator action |
| Recovery is running | Keep disks awake |
| Backfill is running | Keep disks awake |
| Disk fails during write | Let Ceph determine request result; subsequent gateway writes are blocked until policy restored |
| Controller dies during wake | Reconcile and resume |
| Controller dies during sleep | Reconcile physical + Ceph state before acting |
| Operator manually sets `noout` | Preserve it |
| RGW fails | Keep Ceph awake for diagnostic grace period; return S3 failure |
| One complete host fails | Allow Ceph to express resulting durability; gateway applies configured write policy |

---

# 41. Security Model

## 41.1 Dedicated Ceph identity

Never run the application permanently as:

```text
client.admin
```

Create a dedicated CephX identity.

Separate privileges into:

```text
read-only monitoring identity
control identity
```

where practical.

The control identity should have only the commands necessary for:

```text
reading topology
reading health
reading PG state
reading pool policy
managing noout scope
initiating approved scrub operations
```

Destructive commands should not be granted to normal cold-operation credentials.

---

## 41.2 Host node privilege separation

Physical disk power management requires elevated privileges.

Therefore:

```text
S3 gateway       unprivileged
UI               unprivileged
controller       minimally privileged
host node       privileged but tightly constrained
```

The host node should expose specific verbs, never arbitrary shell execution.

---

## 41.3 Controller-node authentication

Use strong mutual authentication:

```text
mTLS
or equivalent node identity
```

An attacker able to call:

```text
sleepDisk()
stopOSD()
```

effectively has storage-denial capabilities.

---

# 42. Ceph Dashboard Authentication

If the optional Dashboard adapter is used, authentication occurs through the Dashboard API and bearer JWT. API requests must also negotiate an explicit API media version.

Dashboard credentials must not replace CephX credentials used by the native adapter.

They are separate trust domains.

---

# 43. Configuration Model

Example language-agnostic configuration:

```yaml
cluster:
  expected_fsid: "..."

ceph:
  adapter: native-command

  rgw_pools:
    mode: discover

s3:
  listen: ":443"
  cold_request_policy: wait
  wake_timeout: 120s

backend:
  rgw:
    endpoints:
      - "https://rgw-a.internal"
      - "https://rgw-b.internal"

sleep:
  enabled: true
  idle_after: 45m
  require_clean_before_sleep: true
  require_no_recovery: true
  require_no_backfill: true

wake:
  reads_when: active
  writes_when: active-clean

integrity:
  block_writes_on_degraded: true
  block_sleep_on_inconsistent: true
  block_sleep_on_unfound: true

maintenance:
  deep_scrub_max_age: 7d

nodes:
  discovery: configured

controller:
  state_path: "/var/lib/coldstore"
```

Configuration represents **policy**, not current cluster state.

---

# 44. Capability Discovery

The application should feature-detect Ceph capabilities at startup.

Store something like:

```text
CephVersion
ClusterFSID
AvailableCommands
DashboardAvailable
DashboardApiVersion
OrchestratorAvailable
HostInventoryAvailable
DeviceHealthAvailable
```

Do not assume:

```text
Ceph version X => endpoint Y definitely behaves exactly as expected
```

Probe optional functionality.

Ceph's Dashboard HTTP API explicitly uses endpoint API versioning and warns that some endpoints can change between Ceph releases.

---

# 45. API Compatibility Strategy

The application's internal domain model should remain stable:

```text
ClusterSnapshot
Host
OSD
PhysicalDevice
Pool
PlacementGroupSummary
HealthCheck
DurabilityPolicy
```

Adapters normalize Ceph responses into those objects.

For example:

```text
Ceph Tentacle response
        │
        ▼
TentacleAdapter
        │
        ▼
ClusterSnapshot
```

A later Ceph release can introduce:

```text
NewReleaseAdapter
```

without rewriting the controller.

---

# 46. Recommended Ceph Query Frequency

Not every command needs the same polling interval.

Example while `READY`:

```text
ceph status          every 5–10 s
pg stat              every 5–10 s
health detail        on status change / 30–60 s
osd tree             30–60 s
pool configuration   minutes / topology change
osd metadata         minutes / topology change
device inventory     minutes
full pg dump         only on transition/problem
```

While `COLD`:

```text
Ceph status          slower
MON quorum           periodic
node heartbeat      periodic
disk power status    conservative
full PG inspection   unnecessary until wake
```

Avoid queries that themselves require sleeping data devices.

---

# 47. Observability

Expose structured metrics such as:

```text
coldstore_state
coldstore_wake_total
coldstore_wake_duration_seconds
coldstore_sleep_total
coldstore_sleep_duration_seconds
coldstore_s3_wait_seconds
coldstore_active_requests
coldstore_osds_expected
coldstore_osds_up
coldstore_disks_standby
coldstore_pg_active_clean
coldstore_pg_degraded
coldstore_last_verified_clean_timestamp
coldstore_last_deep_scrub_timestamp
```

Events should include:

```text
transition started
transition completed
transition failed
drive wake timeout
OSD start timeout
PG readiness timeout
Ceph health changed
operator maintenance entered
disk replacement started
```

---

# 48. Auditability

Every storage lifecycle change should create an audit event.

Example:

```json
{
  "operation": "sleep-cluster",
  "operationId": "op-...",
  "initiator": "idle-policy",
  "startedAt": "...",
  "preconditionSnapshot": "...",
  "osds": [1, 2, 3, 4],
  "flagsAdded": ["noout"],
  "result": "cold"
}
```

This matters because the controller deliberately performs operations that make Ceph unavailable.

---

# 49. Operator UX Principles

The UI should answer:

```text
What is happening?
Why is it happening?
Is my data known to be safe?
What does Ceph currently say?
When was data last verified?
Can I write right now?
Can I safely replace this disk?
What action should I take?
```

Avoid presenting only:

```text
PG 1.a3 active+undersized+degraded
```

without interpretation.

Instead:

```text
Data redundancy reduced.

Cause:
OSD 7 failed to start after wake.

Impact:
Reads remain available.
New writes are disabled by policy.

Ceph detail:
23 PGs active+degraded.

Recommended action:
Inspect host storage-2 / disk WWN ...
```

Raw Ceph detail must remain accessible.

---

# 50. Maintenance Versus Cold State

These states must remain semantically distinct.

## Cold

```text
planned
repeatable
all expected devices present
data unavailable intentionally
no operator repair expected
```

## Maintenance

```text
operator activity expected
topology may change
replacement may occur
automatic sleep disabled
```

## Faulted

```text
unexpected state
automatic transitions stopped
operator action possibly required
```

---

# 51. Important Anti-Patterns

Do not implement any of the following.

### Powering down active OSD drives

```text
OSD running
→ disk standby
```

Unsafe design.

### Marking sleeping OSDs out

Causes Ceph to redistribute data unnecessarily.

### Keeping `norecover` forever

Converts temporary faults into accumulating risk.

### Disabling scrub permanently

Defeats cold-storage integrity goals.

### Depending exclusively on Dashboard REST

Creates unnecessary version/deployment coupling.

### Requiring cephadm

Prevents support for package, Rook, and other installations.

### Running permanently as `client.admin`

Unnecessarily exposes destructive cluster operations.

### Storing controller state only in sleeping Ceph

Creates a circular dependency:

```text
need controller state
→ need Ceph
→ need controller to wake Ceph
```

### Treating `HEALTH_ERR` during COLD as corruption

Availability health and integrity health must be distinguished.

### Treating `COLD` as proof of integrity

Cold means intentionally unavailable.

Only the last warm verification proves anything about known integrity.

---

# 52. Suggested MVP

A sensible first release should deliberately be narrow.

## Support

```text
one Ceph cluster
one RGW deployment
HDD-backed OSDs only
all managed HDD OSDs wake/sleep together
external S3 proxy
single active controller
one host node per storage host
native Ceph command adapter
systemd runtime adapter first
manual device-to-OSD verification
```

## States

```text
COLD
WAKING
READY
QUIESCING
SLEEPING
FAULTED
```

## Ceph commands

Minimum useful set:

```text
status
health detail
quorum_status
osd stat
osd tree
osd dump
osd metadata
pg stat
pg dump
osd pool ls detail
osd pool get ... all
osd set-group/unset-group noout
```

## Later releases

Add:

```text
cephadm runtime adapter
Rook runtime adapter
Dashboard REST adapter
HA controller
scheduled integrity windows
guided OSD replacement
multiple RGW realms/zones
selective storage groups
Prometheus integration
notifications
```

---

# 53. Testing Requirements

A storage orchestrator must be tested primarily against failures.

## State-machine tests

Inject a crash after every transition step.

Example:

```text
sleep step 1 → crash
sleep step 2 → crash
sleep step 3 → crash
...
```

Restart must always converge to a safe state.

---

## Storage failures

Test:

```text
one HDD missing
one HDD refusing wake
one OSD refusing start
one OSD crashing during PUT
one host unavailable
multiple degraded PGs
incomplete PG
inconsistent PG
unfound object
recovery running indefinitely
backfill running
near-full condition
full condition
```

---

## Control-plane failures

Test:

```text
MON minority available
MON quorum loss
MGR restart
controller restart
node restart
controller-node network partition
Ceph API timeout
stale cached status
```

---

## S3 tests

Test real clients:

```text
AWS CLI
rclone
restic if using S3 backend
backup software
multipart-heavy clients
presigned URLs
large object streaming
range downloads
```

Execute each against:

```text
READY
COLD
WAKING
DEGRADED
wake failure
mid-request OSD failure
```

---

# 54. Acceptance Criteria

The system is production-ready only when all of the following are demonstrably true.

## Cold transition

```text
No client request is forwarded after quiescing begins.
No OSD device enters standby while its OSD is running.
All controller-owned noout state is accounted for.
Every cold transition leaves a reconstructable audit record.
```

## Wake transition

```text
Requests cannot reach RGW before configured readiness.
Concurrent requests cause one wake operation.
Partial drive wake cannot result in accidental READY.
```

## Integrity

```text
A degraded cluster cannot accidentally be shown as fully protected.
Cold state cannot erase visibility of the last known Ceph error.
Unfound/inconsistent data blocks automatic sleep.
```

## Crash recovery

```text
Controller restart during every transition converges safely.
Unknown state fails closed.
```

## Compatibility

```text
No Proxmox dependency.
No mandatory Dashboard dependency.
No mandatory cephadm dependency.
No assumption that OSDs are systemd processes.
```

---

# 55. Reference Architecture

```text
                          External network
                                │
                                │ S3 / HTTPS
                                ▼
                    ┌─────────────────────┐
                    │ S3 Wake-Aware Proxy │
                    └──────────┬──────────┘
                               │
                        readiness gate
                               │
                    ┌──────────▼──────────┐
                    │ Cluster Controller  │
                    └──────┬────────┬─────┘
                           │        │
                  Ceph API │        │ Node protocol
                           │        │
                 ┌─────────▼──┐   ┌─▼───────────────┐
                 │ MON / MGR  │   │ Host A node    │
                 └──────┬─────┘   │ OSD + HDD ctrl  │
                        │         └─────────────────┘
                        │
                        │         ┌─────────────────┐
                        ├────────►│ Host B node    │
                        │         │ OSD + HDD ctrl  │
                        │         └─────────────────┘
                        │
                        │         ┌─────────────────┐
                        └────────►│ Host C node    │
                                  │ OSD + HDD ctrl  │
                                  └─────────────────┘

                          when READY
                              │
                              ▼
                         Ceph RGW
                              │
                              ▼
                            RADOS
                              │
                   ┌──────────┼──────────┐
                   ▼          ▼          ▼
                 OSDs       OSDs       OSDs
                  HDD        HDD        HDD
```

---

# 56. Ceph Interface Summary

The implementation should conceptually rank interfaces as follows:

| Priority | Interface | Purpose |
|---|---|---|
| 1 | Native MON/MGR command API | Portable control and authoritative cluster information |
| 1 | `ceph --format json` | Simple implementation of the same logical control adapter |
| 2 | Dashboard REST API | UI-oriented supplementary information |
| 2 | Host-local OS interfaces | Physical device/runtime management |
| 3 | cephadm/Rook/orchestrator APIs | Deployment-specific lifecycle enhancement |
| Optional | Custom ceph-mgr module | Deep integration where version coupling is acceptable |

The Dashboard API is useful, but its own documentation warns that some endpoints are actively developed and can change incompatibly. For that reason, the application's core safety decisions should depend on the stable Ceph management model, not on a particular Dashboard HTTP schema.

---

# 57. Final Design Rule

The project should obey one architectural rule above all others:

```text
Ceph owns data correctness.
The application owns availability scheduling.
```

The application should never attempt to become smarter than Ceph about where data is stored or how redundancy is reconstructed.

Its value is instead to turn:

```text
normal always-online Ceph
```

into:

```text
a controlled, S3-accessible, integrity-aware cold storage appliance
```

without modifying Ceph's data model.

The clean lifecycle is therefore:

```text
                    ┌───────────────┐
                    │     COLD      │
                    │ HDDs sleeping │
                    └───────┬───────┘
                            │ S3 request
                            ▼
                    ┌───────────────┐
                    │    WAKING     │
                    └───────┬───────┘
                            │ OSDs started
                            ▼
                    ┌───────────────┐
                    │    PEERING    │
                    └───────┬───────┘
                            │ policy satisfied
                            ▼
                    ┌───────────────┐
                    │     READY     │
                    │ S3 forwarded  │
                    └───────┬───────┘
                            │ idle timeout
                            ▼
                    ┌───────────────┐
                    │   QUIESCING   │
                    └───────┬───────┘
                            │ verified clean
                            ▼
                    ┌───────────────┐
                    │   SLEEPING    │
                    └───────┬───────┘
                            │ OSDs stopped
                            │ HDD standby
                            └──────────────► COLD
```

That leaves the difficult distributed-storage problems with Ceph and concentrates this application's complexity where it provides unique value: **power-state orchestration, S3 admission control, lifecycle safety, and understandable storage health.**
