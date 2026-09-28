# Ceph CLI fixture corpus

One folder per cluster condition. Each holds the output of the commands a ColdCeph
confirmation issues, named after the command:

| File | Command |
|------|---------|
| `health-detail.json` | `ceph --format json health detail` |
| `status.json` | `ceph --format json status` |
| `quorum_status.json` | `ceph --format json quorum_status` |
| `pg-stat.json` | `ceph --format json pg stat` |
| `osd-dump.json` | `ceph --format json osd dump` |

## Why this exists

Health strings and PG state names are the inputs ColdCeph's safety decisions are made
from. Before this corpus, every health string in the test suite was written from
imagination, and the E2E cluster was forced to `HEALTH_OK` before any test ran — so no
test had ever compared ColdCeph's parsing to output Ceph actually produces. See
`docs/design/test-representativeness-review.md`.

A health string, PG state or wire response that no one has observed from the real system
is not valid test data. Add a scenario here rather than inventing a string in a test.

## Provenance — read this before trusting a fixture

These files were **written against Ceph Quincy's documented JSON schema, not captured from
a running cluster.** They are trimmed to the fields ColdCeph reads plus enough surrounding
context to stay recognisable. Treat them as a faithful model that still needs confirming.

`CephClusterTests.Control_ceph_cli_runs_against_the_cluster` in `ColdCeph.E2E.Tests` is what
closes that gap. Through `CephAdapter.SeeConfirmationShapeMatchesFixtures` it runs the real
provider against the Testcontainers cluster and asserts the live output parses to the same
*shape* these fixtures do — check names matching Ceph's identifier convention, a non-empty
`pgs_by_state`, a named quorum, positive capacity. If Ceph changes its schema, or a fixture
here was written wrong, that journey fails.

When you do have a cluster in front of you, prefer re-capturing:

```sh
./cc-debug up
for c in "health detail" "status" "quorum_status" "pg stat" "osd dump"; do
  docker exec coldceph-mon ceph --format json $c \
    > ColdCeph.Control.Tests/Support/CephFixtures/<scenario>/$(echo "$c" | tr ' ' '-').json
done
```

## Scenarios

| Scenario | Condition | Matters because |
|----------|-----------|-----------------|
| `healthy` | `HEALTH_OK`, all PGs `active+clean` | the only state the E2E suite used to allow |
| `demo-warnings` | the three warnings the E2E harness mutes at startup | muting them is why classification was never tested |
| `cold-osds-down` | every OSD stopped, PGs stale | normal for a cold appliance; must not FAULT |
| `waking-peering` | PGs peering and degraded mid-wake | must not FAULT, must not admit writes |
| `scoped-noout` | `OSD_FLAGS` from `osd set-group noout` | controller-owned; expected, not a fault |
| `cluster-noout` | `OSDMAP_FLAGS` from a cluster-wide `osd set noout` | also reported as noout-only |
| `other-flag` | `noup,noout` set together | not controller-owned alone; must stay unexpected |
| `unfound` | `OBJECT_UNFOUND` + `recovery_unfound` PGs | durability failure → FAULT after confirmation |
| `inconsistent` | `PG_DAMAGED` + `OSD_SCRUB_ERRORS` | durability failure; note neither name contains "inconsistent" |
| `incomplete` | `PG_AVAILABILITY` with `incomplete` PGs | durability failure sharing a check name with a benign wake |
| `nearfull` | `OSD_NEARFULL` | holds writes and sleep, loses nothing |
| `recovering` | `active+recovering`, `backfill_wait` | holds writes and refuses sleep |
| `no-quorum` | monitor answered, named no quorum | the JSON still contains the word "quorum" |
