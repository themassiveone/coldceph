using ColdCeph.Control.Composition;
using ColdCeph.Control.Tests.Support;

namespace ColdCeph.Control.Tests.Features.Integrity.Unit;

[TestFixture]
public sealed class ControlConfigCephBinaryTests
{
    [Test]
    public void FromEnvironment_keeps_the_ceph_program_name()
    {
        using var binary = EnvScope.Set("COLDCEPH_CEPH_BINARY", "ceph");
        using var container = EnvScope.Set("COLDCEPH_CEPH_CONTAINER", null);

        var config = ControlConfig.FromEnvironment();
        var invoke = config.InvokeCeph(["health", "detail"]);

        Assert.That(config.CephBinary, Is.EqualTo("ceph"));
        Assert.That(invoke.FileName, Is.EqualTo("ceph"));
        Assert.That(invoke.Arguments, Is.EqualTo(new[] { "health", "detail" }));
    }

    [Test]
    public void FromEnvironment_rejects_a_relative_script_path()
    {
        using var binary = EnvScope.Set("COLDCEPH_CEPH_BINARY", "docker/ceph/ceph");
        using var container = EnvScope.Set("COLDCEPH_CEPH_CONTAINER", null);

        Assert.That(() => ControlConfig.FromEnvironment(), Throws.InvalidOperationException);
    }

    [Test]
    public void FromEnvironment_rejects_a_non_ceph_filename()
    {
        using var binary = EnvScope.Set("COLDCEPH_CEPH_BINARY", "bash");
        using var container = EnvScope.Set("COLDCEPH_CEPH_CONTAINER", null);

        Assert.That(() => ControlConfig.FromEnvironment(), Throws.InvalidOperationException);
    }

    [Test]
    public void InvokeCeph_docker_execs_ceph_in_the_configured_container()
    {
        using var binary = EnvScope.Set("COLDCEPH_CEPH_BINARY", "ceph");
        using var container = EnvScope.Set("COLDCEPH_CEPH_CONTAINER", "coldceph-mon");

        var config = ControlConfig.FromEnvironment();
        var invoke = config.InvokeCeph(["--format", "json", "health", "detail"]);

        Assert.That(config.CephContainer, Is.EqualTo("coldceph-mon"));
        Assert.That(invoke.FileName, Is.EqualTo("docker"));
        Assert.That(
            invoke.Arguments,
            Is.EqualTo(new[] { "exec", "coldceph-mon", "ceph", "--format", "json", "health", "detail" }));
    }

    [Test]
    public void InvokeCeph_does_not_pass_host_conf_into_docker_exec()
    {
        var config = new ControlConfig
        {
            CephContainer = "coldceph-mon",
            CephConf = "/tmp/ceph.conf",
            CephKeyring = "/tmp/ceph.keyring"
        };

        var invoke = config.InvokeCeph(["health", "detail"]);

        Assert.That(invoke.FileName, Is.EqualTo("docker"));
        Assert.That(invoke.Arguments.Any(argument => argument is "--conf" or "--keyring"), Is.False);
    }

    [Test]
    public void InvokeCeph_rejects_a_relative_script_when_no_container_is_set()
    {
        var config = new ControlConfig { CephBinary = "docker/ceph/ceph" };

        Assert.That(() => config.InvokeCeph(["health", "detail"]), Throws.InvalidOperationException);
    }

    [Test]
    public void InvokeCeph_prepends_conf_and_keyring_for_a_host_ceph_program()
    {
        var config = new ControlConfig
        {
            CephBinary = "ceph",
            CephConf = "/etc/ceph/ceph.conf",
            CephKeyring = "/etc/ceph/keyring"
        };

        var invoke = config.InvokeCeph(["pg", "stat"]);

        Assert.That(invoke.FileName, Is.EqualTo("ceph"));
        Assert.That(
            invoke.Arguments,
            Is.EqualTo(new[] { "--conf", "/etc/ceph/ceph.conf", "--keyring", "/etc/ceph/keyring", "pg", "stat" }));
    }
}
