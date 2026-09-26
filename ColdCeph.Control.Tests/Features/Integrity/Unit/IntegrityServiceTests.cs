using ColdCeph.Control.Features.Integrity.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Control.Composition;
using ColdCeph.Core.Features.Integrity.DTOs;
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

    private static (IntegrityService Integrity, FakeCephQueryProvider Ceph, StoragePlaneService Plane) Create()
    {
        var clock = new FakeClock();
        var plane = new StoragePlaneService(new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), clock, new ControlConfig());
        var ceph = new FakeCephQueryProvider();
        var integrity = new IntegrityService(ceph, new MemoryIntegrityRepository(), new StoragePlaneController(plane), clock);
        return (integrity, ceph, plane);
    }
}
