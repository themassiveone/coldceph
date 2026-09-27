using ColdCeph.Control.Features.Integrity.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Control.Composition;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Tests.Features.Integrity.Unit;

[TestFixture]
public sealed class IntegrityServiceTests
{
    [Test]
    public void Cold_health_err_from_osds_down_is_expected_cold()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Health = new CephHealthRaw
        {
            Status = "HEALTH_ERR",
            Summary = "1 osds down",
            Checks = ["OSD_DOWN: 1 osds down"]
        };
        ceph.HealthChecks = ["OSD_DOWN: 1 osds down"];
        plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.All(check => check.Classification == HealthClassification.ExpectedCold), Is.True);
        Assert.That(snapshot.Raw.Status, Is.EqualTo("HEALTH_ERR"));
    }

    [Test]
    public void Health_warn_while_waking_is_not_unexpected()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Health = new CephHealthRaw
        {
            Status = "HEALTH_WARN",
            Summary = "OSD_DOWN: 1 osds down",
            Checks = ["OSD_DOWN: 1 osds down"]
        };
        ceph.HealthChecks = ["OSD_DOWN: 1 osds down"];
        var operationId = OperationIdRules.Create().Value;
        plane.RequestWake(operationId, "operator");

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.All(check => check.Classification != HealthClassification.Unexpected), Is.True);
        Assert.That(plane.GetState().State, Is.EqualTo(StoragePlaneState.Waking));
    }

    [Test]
    public void Unfound_objects_while_waking_remain_unexpected()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Health = new CephHealthRaw
        {
            Status = "HEALTH_ERR",
            Summary = "unfound objects",
            Checks = ["OBJECT_UNFOUND: unfound objects"]
        };
        ceph.HasUnfound = true;
        ceph.HealthChecks = ["OBJECT_UNFOUND: unfound objects"];
        plane.RequestWake(OperationIdRules.Create().Value, "operator");

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.Any(check => check.Classification == HealthClassification.Unexpected), Is.True);
        Assert.That(plane.GetState().State, Is.EqualTo(StoragePlaneState.Waking));
    }

    [Test]
    public void Unfound_objects_are_unexpected_even_when_cold()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Health = new CephHealthRaw
        {
            Status = "HEALTH_ERR",
            Summary = "unfound objects",
            Checks = ["OBJECT_UNFOUND: unfound objects"]
        };
        ceph.HasUnfound = true;
        ceph.HealthChecks = ["OBJECT_UNFOUND: unfound objects"];
        plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.Any(check => check.Classification == HealthClassification.Unexpected), Is.True);
        Assert.That(snapshot.Predicates.WriteReady, Is.False);
        Assert.That(snapshot.Predicates.SleepSafe, Is.False);
    }

    [Test]
    public void Controller_owned_noout_flags_are_expected_when_ready()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Health = new CephHealthRaw
        {
            Status = "HEALTH_WARN",
            Summary = "noout flag(s) set",
            Checks = ["OSDMAP_FLAGS: noout flag(s) set"]
        };
        ceph.HealthChecks = ["OSDMAP_FLAGS: noout flag(s) set"];
        var operationId = OperationIdRules.Create().Value;
        plane.RequestWake(operationId, "operator");
        plane.EnterReady(operationId);

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.All(check => check.Classification == HealthClassification.ExpectedCold), Is.True);
        Assert.That(snapshot.Checks.Any(check => check.Classification == HealthClassification.Unexpected), Is.False);
    }

    [Test]
    public void Other_osdmap_flags_remain_unexpected_when_ready()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Health = new CephHealthRaw
        {
            Status = "HEALTH_WARN",
            Summary = "noup flag(s) set",
            Checks = ["OSDMAP_FLAGS: noup flag(s) set"]
        };
        ceph.HealthChecks = ["OSDMAP_FLAGS: noup flag(s) set"];
        var operationId = OperationIdRules.Create().Value;
        plane.RequestWake(operationId, "operator");
        plane.EnterReady(operationId);

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.Any(check => check.Classification == HealthClassification.Unexpected), Is.True);
    }

    [Test]
    public void Write_ready_requires_clean_pgs_and_fails_closed_on_recovery()
    {
        var (integrity, ceph, _) = Create();
        ceph.PgsClean = false;
        ceph.HasRecoveryOrBackfill = true;

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Predicates.WriteReady, Is.False);
    }

    [Test]
    public void Sleep_safe_does_not_consult_ok_to_stop()
    {
        var (integrity, _, _) = Create();
        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Predicates.SleepSafe, Is.True);
        Assert.That(snapshot.Raw.Summary, Does.Not.Contain("ok-to-stop"));
    }

    [Test]
    public void Raw_health_reports_the_ceph_status_chip()
    {
        var (integrity, ceph, _) = Create();
        ceph.Health = new CephHealthRaw
        {
            Status = "HEALTH_WARN",
            Summary = "too few PGs",
            Checks = ["TOO_FEW_PGS: too few PGs"]
        };

        _ = integrity.GetIntegrity();
        var raw = integrity.GetRawHealth();

        Assert.That(raw.Status, Is.EqualTo("HEALTH_WARN"));
    }

    [Test]
    public void Raw_health_does_not_rewrite_err_as_ok()
    {
        var (integrity, ceph, _) = Create();
        ceph.Health = new CephHealthRaw
        {
            Status = "HEALTH_ERR",
            Summary = "1 osds down",
            Checks = ["OSD_DOWN: 1 osds down"]
        };

        _ = integrity.GetIntegrity();
        var raw = integrity.GetRawHealth();

        Assert.That(raw.Status, Is.Not.EqualTo("HEALTH_OK"));
        Assert.That(raw.Status, Is.EqualTo("HEALTH_ERR"));
    }

    [Test]
    public void Raw_health_reuses_the_last_integrity_snapshot()
    {
        var (integrity, ceph, _) = Create();
        _ = integrity.GetIntegrity();
        var calls = ceph.HealthDetailCalls;

        _ = integrity.GetRawHealth();

        Assert.That(ceph.HealthDetailCalls, Is.EqualTo(calls));
    }

    [Test]
    public void Raw_health_does_not_fetch_when_no_snapshot_exists()
    {
        var (integrity, ceph, _) = Create();
        ceph.Health = new CephHealthRaw
        {
            Status = "HEALTH_ERR",
            Summary = "1 osds down",
            Checks = ["OSD_DOWN: 1 osds down"]
        };

        var raw = integrity.GetRawHealth();

        Assert.That(ceph.HealthDetailCalls, Is.EqualTo(0));
        Assert.That(raw.Status, Is.EqualTo("UNAVAILABLE"));
        Assert.That(raw.Status, Is.Not.EqualTo("HEALTH_ERR"));
    }

    [Test]
    public void GetIntegrity_when_ceph_throws_is_unavailable()
    {
        var (integrity, ceph, _) = Create();
        ceph.ThrowOnHealth = true;

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Raw.Status, Is.EqualTo("UNAVAILABLE"));
        Assert.That(snapshot.Predicates.WriteReady, Is.False);
    }

    [Test]
    public void GetIntegrity_when_ceph_answers_is_not_unavailable()
    {
        var (integrity, ceph, _) = Create();
        ceph.ThrowOnHealth = false;

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Raw.Status, Is.EqualTo("HEALTH_OK"));
        Assert.That(snapshot.Raw.Status, Is.Not.EqualTo("UNAVAILABLE"));
    }

    private static (IntegrityService Integrity, FakeCephQueryProvider Ceph, StoragePlaneService Plane) Create()
    {
        var clock = new FakeClock();
        var plane = new StoragePlaneService(new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), clock, new ControlConfig());
        var ceph = new FakeCephQueryProvider();
        var integrity = new IntegrityService(ceph, new MemoryIntegrityRepository(), new StoragePlaneController(plane), clock);
        return (integrity, ceph, plane);
    }
}
