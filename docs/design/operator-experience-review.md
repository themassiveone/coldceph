# Operator experience design review

Status: design assessment and delivery recommendation, with the first value-bearing implementation
completed 2026-09-27.

Implemented in the first delivery: Integrity-owned confirmed capacity, a cached read-only Overview,
first-run readiness guidance, plain-language status and navigation, progressive disclosure of Ceph
and transition details, responsive navigation, and provider/unit/controller/HTTP/E2E coverage.
Settings consolidation, machine-grouped inventory, and richer action feedback remain follow-up work.

This review evaluates the current operator UI against the product goal: ColdCeph should be usable
by an appliance owner who does not know Ceph. Findings come first; the recommended design and
delivery plan follow them.

## Findings

### 1. The UI exposes the implementation model instead of the operator's mental model

The primary navigation is a list of internal subsystems: Storage, Integrity, Hosts, OSDs, Devices,
S3, and Operations. The home page is effectively a StoragePlane state-machine inspector. This is
internally coherent, but it makes a new operator assemble the answer to “is my storage okay?” from
seven pages.

The first screen emphasizes `Journal`, `Lease holder`, `Trusted`, and an exact state enum. Those are
valuable diagnostic facts, not primary household/appliance information. Conversely, there is no
single answer for used space, free space, total capacity, protection, machine/disk readiness, or
whether backup clients can use the service.

**Severity: critical.** The product currently feels like a control-plane console for its authors,
not a cold-storage appliance for its intended operator.

### 2. The dashboard requirement is not implemented

There is no dashboard summary and no capacity model in the application DTOs. The Ceph provider
reads health, quorum, PG state, and OSD membership, but not the `ceph status` or `ceph osd df`
capacity fields already identified by the product specification. “Storage” currently means only
the power/state-machine state.

This omission is deeper than presentation. A view cannot show trustworthy used/free/total values
until an owning slice defines the data contract, parsing, freshness, error behavior, and tests.
Capacity belongs to Integrity's observed Ceph snapshot; the StoragePlane-owned overview may read
that snapshot but must not create a new cross-slice write path or poll Ceph.

**Severity: critical.** Usage and capacity are table stakes for a storage product.

### 3. Most page introductions are developer explanations

Examples include explanations of push inventory, overlay behavior, the S3 pending-work signal, and
the fact that a disk is not stood by while its OSD runs. These explain how ColdCeph is constructed.
They do not tell a non-Ceph operator what is true, what it means, or what to do next.

Empty states also expose configuration names such as `COLDCEPH_CONTROL_ENDPOINT`. That may be useful
in maintainer documentation, but it makes the appliance look unfinished and requires the operator
to understand deployment internals.

**Severity: high.** Generic and architectural prose creates cognitive load while still failing to
answer the user's immediate question.

### 4. Ceph vocabulary is used before it is translated

The UI leads with OSD, RGW, PG, `HEALTH_ERR`, up/in, `noout`, classified checks, and raw endpoint or
device identifiers. The Integrity page does separate ColdCeph's conclusion from raw Ceph health,
which is an important safety property, but its conclusion is still expressed as `Write ready yes`
and `Sleep safe no` rather than a plain-language status, impact, and action.

Raw Ceph detail must remain accessible, but it should be progressive disclosure under an
“Advanced details” area. A person should not need to know that an OSD is a Ceph storage daemon to
understand that one disk service failed to start.

**Severity: high.** The current language excludes the exact audience the product targets.

### 5. Status does not establish a clear hierarchy or next action

The header simultaneously shows an operational state and raw Ceph health. A normal cold appliance
can therefore show a calm `COLD` state beside alarming `HEALTH_ERR`. The Integrity page explains
this distinction, but the persistent chrome does not. This produces a false alarm on the most
common successful state.

There is also no page-level priority order such as:

1. needs action now;
2. transitioning, wait;
3. available and protected;
4. asleep and protected as of a stated time;
5. not yet set up.

The operator receives state labels, not a recommendation. Faulted guidance merely sends the user
to another page. Observation errors render as unstructured list items, and POST actions have no
success/failure confirmation.

**Severity: high.** Safety facts exist, but the UI does not turn them into confident decisions.

### 6. “Plug and play” has no visible setup journey

Host enrollment exists, but setup is implicit. A first run presents an empty storage screen with a
Wake action even if no machines, disks, or OSDs have been discovered. The operator must find the
Hosts page, infer that nodes need starting, understand environment variables, approve each node,
then visit separate inventory pages to determine whether setup worked.

There is no readiness checklist for:

- Control connected to Ceph;
- RGW reachable;
- expected machines enrolled and alive;
- disks discovered and mapped;
- redundancy verified;
- S3 endpoint and credentials ready;
- first clean verification completed.

**Severity: critical.** Installation mechanics may work, but the product does not guide the user
from “installed” to “safe to use.”

### 7. Inventory pages optimize for debugging rather than decisions

Hosts, OSDs, and Devices are three separate flat tables. They require the operator to correlate
host IDs, OSD IDs, and device IDs manually. The views do not group a physical disk with its
machine, process, capacity, health, and power state. Nor do they summarize “3 of 3 machines
online” or “12 of 12 disks sleeping.”

The detailed tables are useful for support and should remain available, but the default inventory
experience should be grouped by machine and highlight only exceptions.

**Severity: medium-high.** The data is present but not shaped around diagnosis or replacement.

### 8. Freshness and uncertainty are inconsistent

Some pages trigger a live Ceph confirmation, some show node-pushed observations, and the header
shows the last raw-health snapshot. Only the Storage page exposes an observation timestamp, and it
does so as a technical field. Operators cannot reliably tell whether a green or red statement is
live, cached, historical, or unavailable.

Cold storage particularly needs honest historical wording: “Protected when last checked 17 hours
ago” is materially different from “Healthy.” Unknown must never be colored or worded as success.

**Severity: high.** A simple dashboard that hides freshness would be simpler but unsafe.

### 9. The visual system is serviceable, but not yet an appliance experience

The UI has consistent cards, chips, tables, responsive grids, and restrained styling. However, the
small dense type, seven-item rail, monospace identifiers, and table-first pages make it read like
an infrastructure admin console. On narrow screens the fixed two-column shell has no navigation
adaptation. Status relies heavily on color and several controls lack supplementary descriptions
for consequences.

**Severity: medium.** Visual polish is not the root problem; information architecture and missing
product data are. Restyling alone will not solve them.

### 10. The tests preserve protocol and safety behavior, but not usability outcomes

Current HTML tests correctly protect authentication, antiforgery, legal Wake/Sleep visibility,
one-confirmation semantics, raw Ceph visibility, and no auto-refresh. Many presentation assertions,
however, lock in phrases such as “No OSDs reported” rather than user outcomes.

There are no tests that prove the home page answers capacity, availability, last verification,
setup status, or the recommended next action. There are also no semantic/accessibility checks for
heading structure, current navigation, non-color status, mobile layout, or action feedback.

**Severity: medium-high.** The suite makes the existing console stable, not necessarily usable.

## Root cause

The core issue is **a missing product-facing read model and information architecture**.

ColdCeph has implemented write ownership and operational safety as vertical slices. The UI then
mapped those slices one-for-one into pages. Slice boundaries are an implementation constraint, not
a navigation design. A useful overview is necessarily a composition of read-only facts from
StoragePlane, Integrity, Hosts, Osds, Devices, and S3.

Without that composed read model, every Razor view can only make its local subsystem prettier. It
cannot answer the appliance-level questions. Without capacity and setup contracts, it also cannot
show the two facts users expect first: “how much space do I have?” and “is this ready?”

This is why removing a few explanations or changing labels has not solved the problem.

## Recommended experience

### Primary navigation

Use four operator destinations:

| Destination | Question answered | Current material folded into it |
|---|---|---|
| **Overview** | Is storage usable, protected, and large enough? | StoragePlane summary, capacity, integrity summary, S3 activity, next action |
| **Storage** | Are my machines and disks present? | Hosts, Devices, and OSD summary grouped by machine |
| **Activity** | What happened and is anything running? | Operations plus meaningful current/queued S3 work |
| **Settings** | How is this appliance connected and configured? | enrollment, endpoint/config status, advanced links |

Keep **Ceph details** as an advanced route reachable from health and storage detail. Keep stable
existing URLs during migration so bookmarks and tests do not break. The internal slices remain
unchanged; only presentation and read composition change.

### Overview hierarchy

Above the fold, show no more than these items:

1. **One overall sentence:** “Storage is asleep and your data was protected when last checked.”
2. **One relevant action:** Wake, Sleep, Review problem, or Finish setup. Never show an action that
   cannot succeed.
3. **Capacity:** used, available, total, percentage, and the time it was measured.
4. **Availability:** “Backups can connect now,” “Waking—usually takes …,” or “Asleep—new requests
   will wake storage,” based on the configured admission mode.
5. **Protection:** plain conclusion plus last verified-clean time.
6. **Hardware:** “3 machines online · 12 disks present · 12 sleeping,” with exceptions linked.

Show transition journal, lease, operation IDs, raw health checks, endpoints, WWNs, and up/in only
after the user chooses details.

### Status language

Translate internal states consistently:

| Internal fact | Primary operator wording | Secondary detail |
|---|---|---|
| `COLD`, last clean known | **Asleep** | Protected when last checked; requests follow configured wake behavior |
| `WAKING` | **Getting storage ready** | Disks and storage services are starting |
| `READY`, write-ready | **Ready for backups** | Reads and writes available |
| `QUIESCING`/`SLEEPING` | **Going to sleep** | Finishing activity and parking disks |
| `FAULTED` | **Needs attention** | State the impact and the single recommended next action |
| no confirmed snapshot | **Not checked yet** | Never imply protected or healthy |

Do not put raw `HEALTH_ERR` in global chrome during an expected cold state. Instead show the
interpreted ColdCeph conclusion globally and expose Ceph's exact status in advanced details. This
does not hide raw health; it gives it correct context.

### Contextual copy rule

Every visible explanation should do at least one of four jobs:

- state what is happening;
- state impact on backups or data;
- state when the fact was observed;
- tell the operator what to do next.

Move architecture, protocols, environment variables, state-machine mechanics, and safety rationale
to maintainer documentation or an Advanced details disclosure. Empty states should be concrete:
“No storage machines have joined yet. Check that ColdCeph Node is running on each storage machine.”
If the product can detect the remedy, offer it; do not merely name a configuration variable.

### First-run journey

Before storage is operational, Overview becomes a setup checklist rather than a misleading empty
dashboard:

1. connect to the Ceph cluster;
2. approve discovered storage machines;
3. verify every expected disk has a stable identity and OSD mapping;
4. confirm the S3 target;
5. run the first integrity check;
6. present the endpoint and a copyable client example.

Automatic discovery should complete steps without clicks where it is safe. Approval and first
integrity confirmation remain explicit safety gates. Each incomplete step must distinguish
“waiting,” “cannot connect,” and “needs your approval.”

## Required read model

Introduce an overview view model in the StoragePlane slice because `/` is that slice's protocol
entrypoint. Its controller may query sibling controllers only. It must not call sibling commands,
repositories, providers, or the Ceph CLI directly.

The model needs at least:

- overall presentation state, tone, summary, impact, and recommended action;
- StoragePlane state and transition progress;
- last confirmed capacity (`used`, `available`, `total`, percentage, timestamp, unavailable reason);
- last integrity conclusion and last verified-clean timestamp;
- host totals, alive totals, and pending approvals;
- disk totals, expected/unknown/missing counts, and power summary;
- current S3 activity and admission behavior;
- data freshness for every composed section;
- setup step state.

Integrity should own observed cluster capacity and refresh it only as part of an allowed external
confirmation. The overview reads the last snapshot and must remain fast when Ceph is asleep. This
preserves the “Control never polls Ceph” rule.

## Delivery sequence

### Phase 0 — vocabulary and content (small, low risk)

- Replace developer-facing page introductions with status/impact/action copy.
- Rename primary labels (`OSDs` → `Storage services`, `Integrity` → `Data protection`) while keeping
  Ceph terms in secondary detail.
- Add a consistent “Last checked” presentation and explicit Unknown state.
- Make errors actionable and add POST result/error feedback.

### Phase 1 — truthful overview (highest product value)

- Add capacity DTOs and parse capacity during Integrity confirmation.
- Build the read-only composed overview model.
- Replace the state-machine inspector at `/` with the Overview hierarchy above.
- Move journal and lease fields behind Advanced details.
- Add first-run detection and checklist.

### Phase 2 — task-based navigation

- Consolidate the primary navigation to Overview, Storage, Activity, and Settings.
- Group hosts, disks, and their services into machine cards with exception-first summaries.
- Keep the current detailed tables as advanced drill-down routes.
- Contextualize raw Ceph health instead of displaying it as an unexplained global alarm.

### Phase 3 — confidence and refinement

- Add transition progress based only on facts the system actually observes; do not invent a
  percentage.
- Add clear confirmation for destructive or availability-changing actions and completion banners.
- Add responsive navigation, focus states, skip link, table captions, and non-color status cues.
- Test with operators who administer backups but have never administered Ceph.

## Acceptance criteria

The redesign is successful when a first-time operator can answer the following from `/` without
knowing Ceph terminology or opening another page:

- How much usable space is total, used, and available?
- Can backup clients read and write now?
- Is the appliance intentionally asleep, transitioning, or in trouble?
- Is the data known to have been protected, and when was that last verified?
- Are all expected machines and disks present?
- Does anything require action? If so, what is the next action?

Additional guardrails:

- The overview renders without invoking Ceph and clearly dates cached facts.
- A live confirmation occurs only on the existing allowed request paths and at most once per
  request.
- Unknown or stale data is never presented as healthy.
- Expected cold Ceph errors do not appear as an unexplained critical global alarm.
- Raw Ceph health text and identifiers remain reachable within one click from the interpreted
  result.
- Wake is offered only from `COLD`; Sleep only from `READY`; neither is offered before minimum
  setup prerequisites are satisfied.
- At 320 CSS pixels wide, navigation and primary actions remain usable without horizontal page
  scrolling.
- Every status has a text label and every operator action reports success or a useful failure.

## What not to do

- Do not create a generic `Dashboard` feature slice. Compose read-only slice controllers at the
  StoragePlane-owned `/` entrypoint.
- Do not make the home page run a battery of Ceph commands. Show the last confirmed snapshot and
  its age.
- Do not hide raw Ceph health or collapse “unknown” into “healthy.”
- Do not solve the problem only with new colors, icons, or friendlier headings.
- Do not make operators configure routine setup through environment-variable instructions in the
  browser.
- Do not estimate transition progress unless there is a real observable denominator.

## Evidence reviewed

This assessment traced the rendered experience and the contracts behind it rather than reviewing
screens in isolation:

- `ColdCeph.Control/Shared/Views/_Layout.cshtml` for navigation and persistent status;
- every `ColdCeph.Control/Features/*/Views/Index.cshtml` page and the login view;
- StoragePlane, Integrity, Hosts, Osds, Devices, S3, and Operations page controllers/view models;
- Core DTOs to identify which overview facts can and cannot currently be represented;
- `CephCliQueryProvider` to verify which Ceph observations exist and when they execute;
- Control HTML tests to distinguish deliberate safety/runtime constraints from accidental copy;
- specification sections 17, 29, 49, 52, and 54 for capacity, integrity, UX, MVP scope, and safety.

The review intentionally does not prescribe a visual mockup before the capacity and overview read
models exist. Mocking a polished dashboard around invented or un-dated data would conceal the root
problem instead of solving it.
