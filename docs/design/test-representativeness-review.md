# Test Representativeness Review

Analysis of why ColdCeph's test suite passes while the Ceph integration is unreliable in practice.

Date: 2026-09-28. Scope: all six test projects, CI, and the production code they claim to guard.

> **Status: acted on.** Sections 2–5 describe the state of the repository when this review was
> written. The defects in §5 are fixed and the gates in §6 are in place; §8 records what was done,
> what is verified and what is still open. The analysis is kept as written because the reasoning is
> the reusable part — the rules it produced now live in AGENTS.md under *Testing → Representativeness*.

---

## 1. Verdict

The suite is not weak because it is small. It is ~320 tests against ~160 production files, with
executable architecture rules and a real Testcontainers cluster — more discipline than most
projects this size.

It is weak because **almost every test is positioned on the wrong side of the seam where the bugs
actually live.**

ColdCeph's risk is concentrated in four places:

| Risk | Where the logic lives | Where the tests are |
|------|----------------------|---------------------|
| Interpreting `ceph` CLI output | `CephCliQueryProvider` | a fake that replaces the interpretation |
| Interpreting node command responses | `HttpNodeOsdsClient` / `HttpNodeDevicesClient` | a fake that always succeeds |
| Physically powering disks | `HdparmDiskPower` | nothing |
| Forwarding signed S3 requests | `StreamingRgwProxy` | nothing |

Every one of those four is an **adapter to an external reality**. The suite tests the code on
either side of each adapter and skips the adapter itself. The unit tests stub past it; the E2E
suite is configured so the adapter can never encounter interesting input. The result is a suite
whose green state is genuinely uninformative about whether ColdCeph can drive a Ceph cluster.

This is a single structural mistake with many symptoms, not a backlog of missing tests. Sections
2–4 establish the mechanism; section 5 lists nine live defects the current gates cannot see;
section 6 proposes the fix.

---

## 2. Root cause: the fake replaces the derivation, not the data source

`ICephQueryProvider` exposes eight independent booleans plus health, capacity and OSD membership:

```csharp
bool GetQuorumAvailable();   bool GetPgsActive();   bool GetPgsClean();
bool GetHasUnfound();        bool GetHasInconsistent();
bool GetHasRecoveryOrBackfill();  bool GetHasStaleOrIncomplete();  bool GetHasFullOsds();
```

In production, **all of these are derived from two or three `ceph` invocations by substring
matching** (`CephCliQueryProvider.cs:60-85, 122`):

```csharp
public bool GetQuorumAvailable()
    => Run("quorum_status").Contains("quorum", StringComparison.OrdinalIgnoreCase);

public bool GetPgsActive() => !GetHasStaleOrIncomplete();
public bool GetHasUnfound() => ContainsHealth("unfound");
public bool GetHasFullOsds() => ContainsHealth("full") || ContainsHealth("nearfull");

private bool ContainsHealth(string token)
    => GetHealthChecks().Any(check => check.Contains(token, StringComparison.OrdinalIgnoreCase));
```

`FakeCephQueryProvider` replaces this with eight settable `bool` properties. That single decision
produces three separate failures of representativeness:

### 2.1 The derivation is the untested gap between the two test levels

The unit tests set `HasUnfound = true` directly. The E2E tests run the real provider but only
against a cluster forced to `HEALTH_OK` (see §3). So **no test in the repository ever feeds real
`ceph health detail` / `pg stat` / `quorum_status` JSON through the substring matchers.**

The only provider-level fixture makes this visible. `RecordingProcessRunner` returns one fixed
string for every command, so this test feeds health-detail JSON to the quorum and PG parsers
(`CephCliQueryProviderTests.cs:74-86`):

```csharp
var runner = new RecordingProcessRunner { Output = """{"status":"HEALTH_OK","checks":{}}""" };
...
_ = provider.GetQuorumAvailable();   // parsing health JSON as quorum_status
_ = provider.GetPgsClean();          // parsing health JSON as pg stat
Assert.That(runner.Commands.Any(c => c.Contains("ok-to-stop")), Is.False);
```

The assertion is about argv, not about the answer. `GetQuorumAvailable` and `GetPgsClean` have no
test that checks what they return, for any input. `GetPgsActive` — which gates `ReadReady` and
therefore S3 admission — is never invoked by any test at all.

### 2.2 The fake's state space is wider than reality, and differently shaped

Real Ceph output is heavily correlated: unfound objects imply an `OBJECT_UNFOUND` check *and*
`HEALTH_ERR`; active recovery implies `PG_DEGRADED` *and* `HEALTH_WARN`. The fake lets each
signal be set alone, and the tests routinely do (`IntegrityServiceTests.cs:129-137`):

```csharp
ceph.PgsClean = false;
ceph.HasRecoveryOrBackfill = true;
// Health remains the default HEALTH_OK with zero checks
```

That is a cluster state Ceph cannot produce. Conversely the coupling that *does* exist in
production — every predicate deriving from the same health text — is never exercised. The tests
explore a state space that overlaps reality only partially, and they explore the wrong part of it.

The fake also requires two fields to be kept consistent by hand, `Health.Checks` and
`HealthChecks`, which the real provider derives from one JSON document. Tests set them
separately, and some set only one.

### 2.3 Test fixtures assert on health strings nobody has ever compared to a cluster

Because no test sees real output, every health string in the suite was written from imagination.
Two concrete consequences:

**A dead code path with a passing test named after it.** `IntegrityService.ClassifyOne`
(`IntegrityService.cs:145-150`) classifies on `Contains(check, "HEALTH_ERR")` and
`Contains(check, "HEALTH_WARN")`. But `Classify` only ever passes it `raw.Checks` entries, which
the provider formats as `"{checkName}: {message}"`, or `raw.Summary`, which is those same entries
joined. **Neither ever contains the literal `HEALTH_WARN`.** `raw.Status` — the only field that
carries it — is never classified.

So AGENTS.md's rule "cold `HEALTH_ERR` is classified expected, not hidden" is implemented against
a field that never holds that value. The test named
`Cold_health_err_from_osds_down_is_expected_cold` passes for an unrelated reason: the check text
happens to contain `"osds down"`. Delete both `HEALTH_*` branches and every test still passes.

**A fixture string the product probably cannot emit.** `Controller_owned_noout_flags_are_expected_when_ready`
asserts on `"OSDMAP_FLAGS: noout flag(s) set"`. But StoragePlane issues
`ceph osd set-group noout <scope>` (`CephNooutProvider.cs:18`), and Ceph reports *scoped* flags
under a different check than cluster-wide ones — `OSD_FLAGS: N OSDs or CRUSH {nodes,
device-classes} have {NOOUT} flags set`, not `OSDMAP_FLAGS`. **This needs verifying against a
cluster**, but if it holds, the classification still works by luck (the match is
case-insensitive, so `NOOUT` hits), while the test is asserting a contract the product does not
have. That is what an unverified fixture buys you: a green test that does not describe the
system.

Net effect for the `noout` invariant — arguably the single most safety-critical piece of
classification, since misclassifying it FAULTs a sleeping appliance: it is asserted only against
an invented string, and §3 shows the E2E suite cannot produce the real one either.

---

## 3. The E2E suite deletes the conditions it exists to test

`ColdCeph.E2E.Tests` is the one place real `ceph` output reaches production code. Three choices
in the harness ensure it never carries information.

### 3.1 The cluster is forced to HEALTH_OK before any test runs

`CephCluster.StartAsync` (`CephCluster.cs:52-54`) mutes warnings, then refuses to start unless
the cluster is clean:

```csharp
await SilenceDemoHealthWarningsAsync();   // mutes POOL_NO_REDUNDANCY, TOO_FEW_OSDS, AUTH_*
await EnsureNooutScopeAsync();
await WaitForHealthOkAsync();             // throws otherwise
```

and the failure message states the motive outright:

```csharp
throw new InvalidOperationException(
    $"Demo Ceph did not reach HEALTH_OK (Integrity would FAULT at READY). Last health: {last}");
```

The suite encountered its own classifier producing a false FAULT on ordinary demo-cluster
warnings, and resolved it by removing the warnings. **The classifier's entire purpose is
distinguishing expected-cold from unexpected health, and the harness is configured so it only
ever sees the empty case.** Every warning that would exercise classification is muted at startup;
`SilenceDemoHealthWarningsAsync` swallows all exceptions, so a mute that fails is silent too.

This is also why the `noout` string in §2.3 cannot be checked here: real scoped `noout` would
raise a health check, and a health check fails the startup gate.

### 3.2 Wake and sleep are asserted over a simulation decoupled from the cluster

`SharedEnvironment` substitutes `InMemoryOsdRuntime` and `InMemoryDiskPower`
(`SharedEnvironment.cs:80-83`) and seeds exactly one OSD and one device. So in
`Wake_reaches_ready_against_ceph`:

- `WAKING → READY` fires because a `HashSet<int>.Add` made `ProcessRunning` true, instantly.
- The demo container's real `ceph-osd` was already running and is unaffected.
- `SLEEPING → COLD` fires because the same set was emptied, while the real OSD keeps serving.

The tests named `*_against_ceph` would pass unchanged if `StartAll`, `StopAll`, `WakeAll` and
`StandbyAll` were all no-ops. The real timing — OSDs taking 10–60s to come up, PGs peering,
health passing through WARN before OK — is the substance of "wake works", and it is exactly what
the in-memory runtime removes. The OSD id collision (node fakes osd.0; the demo cluster also has
osd.0) means the `osd dump` overlay appears to work by coincidence rather than by design.

### 3.3 The one "S3 works" test passes on any non-503

`S3Adapter.SeeForwarded` (`S3Adapter.cs:40-50`):

```csharp
var response = await _client.SendAsync(request);   // GET /cold/missing
return response.StatusCode is not HttpStatusCode.ServiceUnavailable;
```

A 404 passes. So does a 403 from a broken SigV4 signature, a 400, a 500. And 503 is generated by
ColdCeph's own admission control before RGW is contacted, so `SeeUnavailable` proves nothing
about the proxy either.

**There is no test in the repository — at any level — that reads or writes an S3 object through
ColdCeph.** No PUT is tested anywhere. `S3Adapter.GetObject` exists and is never called. The demo
container creates a `cold` bucket; the test asks for `/cold/missing`.

---

## 4. What the quality gates actually measure

AGENTS.md states the rule that matters:

> Positive + negative tests for every behavioral expectation. A test is only meaningful if it can
> distinguish correct behaviour from incorrect behaviour.

Nothing enforces it. The 17 architecture tests enforce **file layout, folder names, slice import
direction, builder usage, and absence of `[SetUp]`** — structure only. `TestFileLayout`,
`TestFixtureSetup` and `TestDataBuilders` would all pass on a suite of empty test methods in
correctly named folders.

There is no coverage collection, no mutation testing, no flake detection, and no test-result or
log artifact upload anywhere in `.github/workflows/ci.yml`. CI is two `dotnet test` invocations.

So the gates measure how tests are *filed*, and say nothing about whether they can *fail*. Three
instances where that is load-bearing:

**Missing positive cases at the READY gate.** `SystemdOsdRuntime.IsRunning` and
`DockerExecOsdRuntime.IsRunning` each have exactly one test — `IsRunning_is_false_when_*_fails`.
Neither has a test that `IsRunning` returns **true**. `IsRunning` is the predicate the entire
`WAKING → READY` transition rests on. `Stop` has no test in either runtime.

**Assertions on fake defaults.** `Sleep_safe_does_not_consult_ok_to_stop`
(`IntegrityServiceTests.cs:141-148`) asserts `snapshot.Raw.Summary` does not contain
`"ok-to-stop"` — where `Summary` is `FakeCephQueryProvider`'s hardcoded `"HEALTH_OK"`. The test
cannot fail regardless of production code. `GetIntegrity_when_ceph_answers_is_not_unavailable` is
the same shape.

**An invariant measured with a blind instrument.** Every reconciler test asserts
`Ceph.HealthDetailCalls == 0` to enforce "Control never polls Ceph". But the fake increments that
counter only in `GetHealthDetail` — not in `GetHealthChecks`, which in production is a *separate
`ceph` process*. One `GetIntegrity()` fans out through `BuildPredicates`
(`IntegrityService.cs:166-180`) to eight predicate calls, six of which route to
`ContainsHealth → GetHealthChecks → Run("health","detail")`. The counter under-reports real
subprocess count by up to ~9×, so it can confirm "zero" but can never detect the fan-out that
actually costs wall time.

---

## 5. Live defects the current gates cannot see

Found while tracing the gaps. Each is unguarded by any existing test, and each produces the
symptoms described — "Ceph integration doesn't work" or "super flaky".

### 5.1 Node command failures are recorded as success (fail-open at the READY gate)

`HttpNodeOsdsClient.cs:28` and `HttpNodeDevicesClient.cs:28`:

```csharp
=> Client(endpoint).PostAsJsonAsync(path, request).GetAwaiter().GetResult()
    .Content.ReadFromJsonAsync<OsdMutationResult>().GetAwaiter().GetResult()
   ?? new OsdMutationResult(request.OsdId, request.DesiredRunning, false);
```

`OsdMutationResult`'s second positional parameter is `ProcessRunning`. There is no
`EnsureSuccessStatusCode`. So when a node answers 401/500/empty and the body does not deserialize,
**Control fabricates `ProcessRunning := DesiredRunning`** and `OsdsService.MutateHost` writes it
into inventory. Consequences:

- Wake: node unreachable → OSDs recorded running → `IsEveryProcessRunning()` true → `READY` →
  S3 admits traffic against OSDs that never started.
- Sleep: identical path records `PowerState := Standby` → `SLEEPING → COLD` while disks spin.

This is precisely the class of bug behind "the plane says READY but Ceph isn't". It is
unreachable in tests because `FakeNodeOsdsClient.Start` always returns
`new OsdMutationResult(request.OsdId, true, false)` and its only failure mode is throwing. No
fake can return "I tried and it is still not running" — the single most likely real response
during a wake.

### 5.2 `Content-*` headers are dropped from every proxied S3 body request

`StreamingRgwProxy.ProxyAsync` calls `CopyHeaders` at line 22 and assigns `request.Content` at
line 25 — in that order. `CopyHeaders` routes content headers via:

```csharp
if (!target.Headers.TryAddWithoutValidation(...) && target.Content is not null)
    target.Content.Headers.TryAddWithoutValidation(...);
```

For a PUT, `target.Headers` rejects `Content-Type` / `Content-MD5` / `Content-Length` /
`Content-Encoding`, and `target.Content` is still null, so the fallback is skipped. **All
`Content-*` headers are silently discarded.** `Content-Type` is re-added afterwards; `Content-MD5`
and `Content-Encoding: aws-chunked` are not. Both are signed headers under SigV4 → 403 from RGW
on upload.

Also at line 52: `Expect` is copied as a plain header rather than set via
`request.Headers.ExpectContinue`, so the `Expect: 100-continue` behaviour AGENTS.md asks for does
not take effect.

`StreamingRgwProxy` has **zero tests**, and §3.3 shows the E2E S3 test passes on a 403.

### 5.3 Three Control singletons mutate unsynchronised dictionaries under concurrent access

`OsdsService`, `DevicesService` and `HostsService` hold plain `Dictionary<,>` fields written by
HTTP handlers (`/v1/osds/observed`, `/v1/devices/observed`, `/v1/hosts/join`) and read
concurrently by three 1-second reconcilers and every operator page render. No `lock`, no
concurrent collection.

Concurrent `Dictionary` read/write yields `InvalidOperationException`, torn reads, or a spin.
Under node push traffic plus reconciler ticks this is a genuine intermittent-failure source.

The asymmetry is telling: AGENTS.md explicitly mandates a lock for the **Node's** snapshots
("mutated under a lock so report loops and node HTTP cannot tear the dictionary"), and
`ColdCeph.Node`'s services do take one. The identical race on Control went unnoticed — because
no test would catch either. The Node lock exists because someone hit the bug in production, not
because a test found it. Every test in the suite is single-threaded except three deliberate
`Parallel.For` cases.

### 5.4 Wake has no disk-before-OSD ordering

`OsdsReconciler` (`StartAll`) and `DevicesReconciler` (`WakeAll`) are two independent
`BackgroundService`s on independent 1-second timers. In `WAKING` nothing sequences them, so an
OSD process can be started before its mapped disk is awake.

Note the asymmetry: the *sleep* path does guard ordering
(`Sleeping && _osds.IsEveryProcessStopped()` before `StandbyAll`). The wake path has no
equivalent. Because each reconciler is unit-tested alone via `ReconcileOnce()`, no test observes
the interleaving of the two.

### 5.5 Wake and sleep issue a command storm

Both reconcilers call `StartAll` / `WakeAll` for **every host on every tick** for the entire
duration of `WAKING`, with no idempotence check and no backoff — one node HTTP call per OSD per
host per second, each followed by a SQLite `Persist()`. With a realistic 30–60s spin-up that is
dozens of redundant `systemctl start` and `hdparm` invocations against drives that are mid-spin-up.

Every reconciler test calls `ReconcileOnce()` exactly once, so no test counts commands issued
over time.

### 5.6 The "single-flight" Ceph cache is a 2-second TTL cache, and its test freezes time

AGENTS.md: "Ceph CLI is **single-flight** … `ContainsHealth` reuses that JSON; it must not spawn
another process", and "only once per request".

The implementation (`CephCliQueryProvider.cs:151-166`) is a wall-clock TTL cache, default 2s
(`ControlConfig.CephQueryCacheTtl`). So:

- **Within one confirmation:** `BuildPredicates` fans out to ~9 serialized `ceph` invocations. On
  a real cluster `health detail` alone takes 1–3s, so the TTL expires *mid-confirmation* and the
  remaining predicates each spawn a fresh process — which lengthens the confirmation, which
  expires the cache again. Self-reinforcing, and worst exactly when the cluster is slow (cold,
  waking, degraded).
- **Across requests:** two operator requests 1.5s apart silently share one snapshot, so
  "a live classified snapshot once per request" is false in the other direction.

`Health_predicates_reuse_one_cached_health_cli` passes because it uses a `FakeClock` that never
advances. **The test asserts the invariant under the one condition — zero elapsed time — that
never holds in production.** This is the clearest single example of the pattern in the repo.

### 5.7 One hung `ceph` call blocks all Ceph access process-wide, forever

`SystemProcessRunner.Run` has **no timeout** and no `Process.Kill`. It is wrapped in
`SerialProcessRunner`, a single `SemaphoreSlim(1,1)` with an untimed `Wait()`. The `ceph` CLI
blocks for a long time against an unreachable monitor. So one hung invocation stalls every
subsequent Ceph query, health page, and S3 admission check in the process indefinitely.

Separately, `Run` reads stdout to completion *then* stderr:

```csharp
var stdout = process.StandardOutput.ReadToEnd();
var stderr = process.StandardError.ReadToEnd();
process.WaitForExit();
```

If the child fills the stderr pipe buffer while we block on stdout, both sides deadlock — the
textbook `ProcessStartInfo` deadlock. `ceph` writes to stderr on warnings and auth problems.
`SystemProcessRunner` is referenced by no test; `SerialProcessRunnerTests` uses an in-memory
inner runner.

### 5.8 Reconciler failures are erased

All three reconcilers:

```csharp
catch (Exception)
{
    // A failed node call must not stop the hosted loop.
}
```

No logging, no counter, no last-error surface. AGENTS.md asks for the loop to survive — it does —
but a *permanently* failing reconciler is now indistinguishable from a healthy one. The operator
sees a plane stuck in `WAKING` with no reason available anywhere, and neither does anyone
debugging it afterwards.

This is a significant part of why these problems are hard to pin down: the system is built to
discard the evidence. `IntegrityService.ListOsdMembership` does the same — it catches everything
and returns an empty dictionary, making "Ceph unreachable" and "no OSDs exist" identical to every
caller.

### 5.9 A test-only back door ships in production

`AppComposition.cs:156-158`:

```csharp
private static bool IsS3(HttpContext context, ControlConfig config)
    => context.Connection.LocalPort == config.S3Port
       || context.Request.Headers.ContainsKey("X-ColdCeph-S3");
```

The header clause exists solely because `WebApplicationFactory` cannot bind two ports, and its
only other use in the repo is `S3HttpTests.cs:14`. In production, **any request to the operator
port carrying `X-ColdCeph-S3` bypasses MVC, cookie auth and antiforgery entirely** and is proxied
to RGW.

Two problems at once: a security hole, and the fact that the real dispatch mechanism — port-based
separation, which AGENTS.md is emphatic about ("do not mix S3 and HTML") — has no test. The tests
exercise a code path that exists only for them.

### 5.10 Other gaps worth recording

- **`HdparmDiskPower` has zero tests**, while `MemoryDiskPower` — its in-memory stand-in — has a
  dedicated fixture. It is also bypassed in local dev (`NodeAppComposition` selects
  `MemoryDiskPower` whenever `OsdContainer` is set) and in E2E. The provider that physically
  powers drives, the product's core value, runs for the first time on a real appliance.
  Two things a test would likely have caught: `Wake` issues `hdparm -S 0`, which sets the
  spindown *timer* and does not spin a standby drive up (that needs an I/O); and
  `GetPowerState` maps anything not containing `"standby"` to `Active`, so `hdparm -C`'s
  `sleeping` state reports as Active and `IsEveryDeviceStandby()` never becomes true.
- **The reconcilers never run in any HTTP-level test.** `ControlAppFactory` sets
  `COLDCEPH_BIND=0`, and `AppComposition` registers the three hosted services only when
  `BindHttpListeners` is true. The reconcile loop — the heart of the state machine — is exercised
  only by hand-driven `ReconcileOnce()` calls.
- **`ReconcileBody` reasons about a stale snapshot.** `StoragePlaneReconciler` reads `snapshot`
  once and then evaluates several transition guards against it after mutating state, so at most
  one transition happens per tick and the sequence is timing-dependent across ticks. No test
  drives more than a couple of ticks.
- **`ApplyObserved` lets an error report wipe inventory.** It removes all of a host's OSDs before
  applying the new list, so an observation carrying an `Error` and an empty list deletes that
  host's OSDs from Control. `IsEveryProcessStopped()` is then vacuously true across the cluster.
- **`ControlAppFactory` mutates process-wide environment variables in its constructor**, which is
  why `ColdCeph.Control.Tests` must run with `ParallelScope.None`. Order-dependent env leakage
  between fixtures, and the whole 200-test project serialised.
- **No `Program.cs`/config test for the two-listener bind.** `ListenUrls()` is unit-tested as a
  string list; nothing asserts both listeners actually come up and route differently.

---

## 6. Recommendations

Ordered by ratio of risk removed to effort. The theme is constant: **move every seam one layer
outward, to the boundary with the external system.**

### 6.1 Move the Ceph test seam from `ICephQueryProvider` to `IProcessRunner`

The highest-value change in this document.

1. Capture a corpus of **real** `ceph --format json` output — `health detail`, `status`,
   `quorum_status`, `pg stat`, `osd dump` — in `Support/CephFixtures/`, for at least: healthy;
   cold with all OSDs down; mid-wake with PGs peering/incomplete; scoped `noout` set;
   `OBJECT_UNFOUND`; `PG_DAMAGED`; nearfull; no quorum; `ceph` exiting non-zero with stderr.
   Harvest them from the compose cluster with `cc-debug`, and commit them. Treat any health
   string not present in that corpus as inadmissible in a test.
2. Replace `RecordingProcessRunner`'s single `Output` with a **per-command** map, so
   `quorum_status` and `pg stat` stop being answered with health JSON. Have it model non-zero
   exit with stderr, and empty output.
3. Drive `CephCliQueryProviderTests` from the corpus, asserting return **values** — including the
   first tests for `GetQuorumAvailable`, `GetPgsActive`, `GetPgsClean`.
4. Keep `FakeCephQueryProvider` for the classifier's own tests, but **derive** it from a raw
   fixture rather than exposing eight free booleans, so an impossible state cannot be constructed.

Expect this to surface the substring predicates directly. `GetQuorumAvailable` returns true for
any output containing `"quorum"` — which `quorum_status` JSON always does, via `quorum_names`.
`GetPgsClean` returns true if *any* PG is `active+clean`. `GetPgsActive` never looks at PG state
at all. `GetHasFullOsds` matches `"full"` anywhere in any health message.

Then reconsider the design: these predicates should read structured JSON fields
(`pgs_by_state[].state_name`, `quorum_names`, `osdmap.num_up_osds`), not grep free text. A
mis-grep here drives an irreversible `FAULTED`.

### 6.2 Let the E2E cluster be unhealthy

Delete `SilenceDemoHealthWarningsAsync` and `WaitForHealthOkAsync` as *gates*. Replace them with
journeys that **assert the classification** of the states they currently suppress:

- demo cluster's native `POOL_NO_REDUNDANCY` / `TOO_FEW_OSDS` while `COLD` → classified
  expected-cold, not `FAULTED`.
- scoped `noout` actually applied to a CRUSH bucket **containing the OSD** → the real health
  check appears → classified expected-cold. (Today `EnsureNooutScopeAsync` creates an empty
  bucket and swallows failures, so `set-group noout` affects nothing.)
- stop the real `ceph-osd` in the container → `OSD_DOWN` → expected-cold while cold,
  and the classification while `READY`.
- `ceph osd set noout` cluster-wide *outside* ColdCeph → **not** controller-owned → must not be
  cleared by StoragePlane.

Stop swallowing exceptions in cluster setup; a mute or CRUSH command that fails should fail the
run loudly.

### 6.3 Make at least one E2E journey drive real OSDs and real objects

Keep the fast in-memory journeys, and add a small number of slow, honest ones:

- Point `DockerExecOsdRuntime` at the demo container so `WAKING` waits for a **real** `ceph-osd`
  to start and `SLEEPING` actually stops it. This is the only way the timing, the peering window,
  and the `osd dump` overlay get tested. Give it a generous per-journey timeout rather than the
  shared 60s.
- **PUT an object and GET it back** through `:7480`, asserting the body round-trips. This single
  test catches §5.2 and gives the S3 path its first real assertion. Then replace
  `SeeForwarded`'s `is not 503` with an explicit expected status.

### 6.4 Fix the four adapters, with a test each

- `HttpNodeOsdsClient` / `HttpNodeDevicesClient`: `EnsureSuccessStatusCode`, an explicit
  per-request timeout, and **fail closed** — a non-2xx or unparseable response must leave the
  observation unchanged (or mark it unknown), never assert the desired state. Give
  `FakeNodeOsdsClient` a "responded, still not running" mode and a "non-2xx" mode; use them.
- `SystemProcessRunner`: a timeout with `Kill(entireProcessTree: true)`, and async/concurrent
  stdout+stderr draining to remove the deadlock. Add a provider test using a real short-lived
  child process that writes heavily to stderr and one that hangs.
- `StreamingRgwProxy`: assign `request.Content` before copying headers; set `ExpectContinue`
  properly. Add its first tests — header preservation for GET and PUT, including `Content-MD5`
  and `x-amz-*`, against a stub HTTP handler.
- `HdparmDiskPower`: provider tests for command shape and for each `hdparm -C` output variant
  (`active/idle`, `standby`, `sleeping`, `unknown`, SG_IO error). Decide and document what `Wake`
  should actually issue.

### 6.5 Make the state machine observable, then test it as a sequence

- Log every swallowed reconciler exception, and expose a per-slice last-error the operator UI can
  show. A silent `catch (Exception)` is a testability problem as much as an operability one.
- Make `ListOsdMembership` distinguish "Ceph unreachable" from "no OSDs".
- Add a **multi-tick** reconciler harness: inject the tick instead of `Task.Delay(1s)`, drive N
  ticks, and assert on the *sequence* — command counts per tick (catches §5.5), wake ordering
  across the two reconcilers (§5.4), and `FAULTED` behaviour across an operator Wake.
- Add a concurrency test: `ApplyObserved` on one thread while `ListOsds` enumerates on another.
  It will fail today; fix by locking the three Control services as the Node's already are.

### 6.6 Point the architecture tests at meaning, and add a real signal to CI

- Add rules that enforce the intent, not just the filing: a `Providers/` type with no
  corresponding `Provider/` test fixture is a violation; a `Fake/` implementation of a production
  interface whose real implementation has no provider test is a violation. Those two rules alone
  would have flagged `HdparmDiskPower`, `StreamingRgwProxy` and `SystemProcessRunner`.
- Collect coverage in CI and fail on regression — not for the percentage, but because the
  zero-coverage files above would have been visible from day one.
- Trial **mutation testing** (Stryker.NET) scoped to `Features/*/Providers/` and
  `IntegrityService`. That is the only tool that mechanically finds the failure mode described
  throughout this document: tests that cannot distinguish correct from incorrect behaviour.
  §2.3's dead `HEALTH_*` branches, §4's fake-default assertions and §5.6's frozen clock are all
  surviving mutants.
- Upload test results and container logs as artifacts on E2E failure. Today a flaky E2E run
  leaves nothing behind to diagnose.
- Delete the `X-ColdCeph-S3` clause from production. Have the S3 HTTP tests bind two real ports
  (a `WebApplication` on ephemeral ports rather than `WebApplicationFactory`), or accept that
  gateway coverage lives in E2E only.

### 6.7 Record the rule in AGENTS.md

AGENTS.md's testing section defines structure thoroughly and representativeness not at all. Acting
on this report means adding the principle that is currently missing — something like:

> A fake stands in for an **external system**, never for our own interpretation of that system's
> output. Every provider that parses external output is tested against **captured real output**,
> committed under `Support/`. A health string, CLI output shape or wire response that no one has
> observed from the real system is not valid test data.

Per the Definition Synchronization rule, that change and the CI/architecture-rule changes above
belong in the same commits as the code.

---

## 7. Summary table

| # | Defect | Gate that should have caught it | Why it didn't |
|---|--------|--------------------------------|---------------|
| 5.1 | Node failure recorded as success → false `READY` | `OsdsReconcilerTests` | fake only ever succeeds or throws |
| 5.2 | `Content-*` headers dropped → PUT 403 | S3 `Provider/` tests | `StreamingRgwProxy` has no tests; E2E passes on 403 |
| 5.3 | Unsynchronised dictionaries in 3 Control singletons | any concurrency test | all tests single-threaded |
| 5.4 | No disk-before-OSD wake ordering | reconciler tests | reconcilers tested in isolation |
| 5.5 | Per-second command storm during wake | reconciler tests | `ReconcileOnce()` called once |
| 5.6 | TTL cache masquerading as single-flight | `CephCliQueryProviderTests` | `FakeClock` never advances |
| 5.7 | No process timeout; stdout/stderr deadlock | `SystemProcessRunner` tests | class has no tests |
| 5.8 | Reconciler errors silently discarded | any observability assertion | nothing is logged to assert on |
| 5.9 | `X-ColdCeph-S3` auth bypass in production | S3 HTTP tests | the tests are the reason it exists |
| 5.10 | `HdparmDiskPower` untested and likely non-functional | Devices `Provider/` tests | only the in-memory fake is tested |

Nine of the ten are adapters to something outside the process. That is the whole finding.

---

## 8. What was done

### Fixed and verified by test

| # | Defect | Fix | How the fix is held |
|---|--------|-----|---------------------|
| 2.1–2.3, 6.1 | Signals grepped from Ceph's prose | `GetObservation()` returns one `CephObservation`; `CephSignals` derives everything from check **names** and PG **state tokens** | 13-scenario fixture corpus under `Support/CephFixtures`; `CephSignalsTests` covers each predicate both ways |
| 5.1 | Node clients fabricated success | Both require a parsed 2xx, carry a timeout, and throw otherwise | `HttpNode*ClientTests` assert the desired state is never reported back; fakes gained a "responded, unchanged" mode |
| 5.2 | `Content-*` dropped from every PUT | Body attached before headers; `ExpectContinue` set properly | 17 `StreamingRgwProxyTests`, including `Content-MD5` survival |
| 5.3 | Unsynchronised Control singletons | All three locked | `ControlStateConcurrencyTests` — **verified to fail without the locks** |
| 5.4 | No disk-before-OSD ordering | Osds reads Devices' inventory and waits | `PlaneHarness` asserts ordering across loops |
| 5.5 | Per-second command storm | Commands only on drift | Multi-tick tests assert exact command counts |
| 5.6 | TTL cache posing as single-flight | Cache removed; one call per confirmation, structurally | Provider tests assert one `health detail` per confirmation and two per two requests |
| 5.7 | Unbounded wait, stdout/stderr deadlock | Bounded wait with tree kill; concurrent drain | 10 `SystemProcessRunnerTests` with real children — **verified to hang the run against the old implementation** |
| 5.8 | Reconciler failures discarded | Logged, and kept on `ReconcilerLoop.LastError` | Tests assert the failure is recorded and cleared on recovery |
| 5.9 | `X-ColdCeph-S3` auth bypass | Removed | `TwoPortControlHost` binds both ports; a test asserts no header substitutes |
| 5.10 | `HdparmDiskPower` untested, `Wake` ineffective | Every `-C` state mapped; `Wake` forces a read first | 19 `HdparmDiskPowerTests` |
| 5.10 | `IsRunning` had no positive case | — | Both runtimes now assert true, false, and every intermediate systemd state |
| 5.10 | Error observation erased inventory | An error report with no entries keeps what Control knows | Tests both ways |
| 6.6 | Gates measured file layout only | `ProviderCoverage` rules; coverage summary and failure artifacts in CI | The rules found `RgwS3Client` untested on their first run |
| new | `RgwS3Client` (hand-rolled SigV4) untested; signed path re-canonicalised before sending | Clock injected; path preserved exactly | 25 tests asserting the signature depends on each element it covers |
| new | `/v1` surface almost entirely unexecuted | — | StoragePlane and Integrity `/v1` now tested |

Unit tests went from 284 to 590. Two of the fixes were mutation-checked against the old code rather
than assumed.

### Changed on reflection

Two of §5's claims were wrong about Ceph, and the fixture corpus is what showed it:

- **`active+clean+inconsistent` is clean.** Damage does not clear the `clean` token; it is reported
  through `PG_DAMAGED`. Writes are held by the damage signal, not by cleanliness.
- **`stale+active+clean` keeps the `active` token.** Ceph retains the last state it saw and adds
  `stale`. Availability therefore comes from the `stale` token, not from the absence of `active`.

Both are now tests in their own right. Separately, `PG_DEGRADED` turned out to be wrong to treat as
unexpected: degraded means reduced redundancy, not unreadable data, and classifying it that way
closed reads for the whole of every recovery window. Unrecognised checks are now judged on severity
rather than against a list of names, so a warning from a later Ceph release does not hold writes back
for being unfamiliar while an unrecognised error still fails closed.

### Still open

- **E2E OSD lifecycle is still simulated.** The `demo` container has no supervisor that restarts
  `ceph-osd` on request, so `InMemoryOsdRuntime` stays. The `osd dump` overlay and everything
  Control reads from Ceph are now genuine, and the cluster is no longer forced healthy — but real
  wake timing, with OSDs taking tens of seconds and PGs peering, is still unexercised. Closing it
  needs a supervisor in the container or a move to the compose stack.
- **The fixture corpus is modelled, not captured.** It follows Ceph Quincy's documented schema; it
  was not taken from a running cluster, because the container this work was done in had no Docker
  daemon. `CephAdapter.SeeConfirmationShapeMatchesFixtures` asserts the live output parses to the
  same shape, which catches schema drift but is not the same as re-capturing. The corpus README says
  how.
- **None of the E2E changes have been run.** No Docker daemon was available. They compile; the
  journeys themselves are unverified.
- **Operations' HTTP surface and the Debug CLI have no coverage.** Surfaced by the new coverage
  summary, out of scope here.
- **No mutation testing.** §6.6 recommended trialling Stryker.NET on `Providers/` and
  `IntegrityService`. The two manual mutation checks in this round both found something, which is an
  argument for automating it.
