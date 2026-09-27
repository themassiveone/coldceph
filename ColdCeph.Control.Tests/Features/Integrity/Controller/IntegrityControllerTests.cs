using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Integrity.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;

namespace ColdCeph.Control.Tests.Features.Integrity.Controller;

[TestFixture]
public sealed class IntegrityControllerTests
{
    [Test]
    public void GetPredicates_does_not_query_ceph()
    {
        var (controller, ceph) = Create();

        _ = controller.GetPredicates();

        Assert.That(ceph.HealthDetailCalls, Is.EqualTo(0));
    }

    [Test]
    public void GetIntegrity_queries_ceph_once()
    {
        var (controller, ceph) = Create();

        _ = controller.GetIntegrity();

        Assert.That(ceph.HealthDetailCalls, Is.EqualTo(1));
        Assert.That(ceph.HealthDetailCalls, Is.Not.EqualTo(0));
    }

    private static (IntegrityController Controller, FakeCephQueryProvider Ceph) Create()
    {
        var clock = new FakeClock();
        var ceph = new FakeCephQueryProvider();
        var plane = new StoragePlaneService(new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), clock, new ControlConfig());
        var controller = new IntegrityController(new IntegrityService(ceph, new MemoryIntegrityRepository(), new StoragePlaneController(plane), clock));
        return (controller, ceph);
    }
}
