using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Providers;
using ColdCeph.Control.Tests.Fake;

namespace ColdCeph.Control.Tests.Features.Integrity.Provider;

[TestFixture]
public sealed class CephCliQueryProviderTests
{
    [Test]
    public void Health_detail_uses_json_format()
    {
        var runner = new RecordingProcessRunner { Output = """{"status":"HEALTH_OK","checks":{}}""" };
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        var health = provider.GetHealthDetail();

        Assert.That(runner.Commands.Any(command => command == "ceph --format json health detail"), Is.True);
        Assert.That(health.Status, Is.EqualTo("HEALTH_OK"));
    }

    [Test]
    public void Health_detail_includes_configured_conf_and_keyring()
    {
        var runner = new RecordingProcessRunner { Output = """{"status":"HEALTH_OK","checks":{}}""" };
        var provider = new CephCliQueryProvider(
            new ControlConfig
            {
                CephBinary = "ceph",
                CephConf = "/tmp/ceph.conf",
                CephKeyring = "/tmp/ceph.keyring"
            },
            runner);

        _ = provider.GetHealthDetail();

        Assert.That(
            runner.Commands.Any(command => command.Contains("--conf /tmp/ceph.conf") && command.Contains("--keyring /tmp/ceph.keyring")),
            Is.True);
    }

    [Test]
    public void Health_detail_docker_execs_ceph_in_the_configured_container()
    {
        var runner = new RecordingProcessRunner { Output = """{"status":"HEALTH_OK","checks":{}}""" };
        var provider = new CephCliQueryProvider(new ControlConfig { CephContainer = "abc123" }, runner);

        _ = provider.GetHealthDetail();

        Assert.That(runner.Commands, Is.EqualTo(new[] { "docker exec abc123 ceph --format json health detail" }));
    }

    [Test]
    public void Health_detail_does_not_start_a_relative_script()
    {
        var runner = new RecordingProcessRunner { Output = """{"status":"HEALTH_OK","checks":{}}""" };
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "docker/ceph/ceph" }, runner);

        Assert.That(() => provider.GetHealthDetail(), Throws.InvalidOperationException);
        Assert.That(runner.Commands, Is.Empty);
    }

    [Test]
    public void Health_detail_does_not_pass_conf_flags_when_unset()
    {
        var runner = new RecordingProcessRunner { Output = """{"status":"HEALTH_OK","checks":{}}""" };
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        _ = provider.GetHealthDetail();

        Assert.That(runner.Commands.Any(command => command.Contains("--conf") || command.Contains("--keyring")), Is.False);
    }

    [Test]
    public void Provider_does_not_issue_ok_to_stop()
    {
        var runner = new RecordingProcessRunner { Output = """{"status":"HEALTH_OK","checks":{}}""" };
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        _ = provider.GetHealthDetail();
        _ = provider.GetQuorumAvailable();
        _ = provider.GetPgsClean();

        Assert.That(runner.Commands.Any(command => command.Contains("ok-to-stop")), Is.False);
    }

    [Test]
    public void Health_predicates_reuse_one_cached_health_cli()
    {
        var (provider, runner, _) = Create();
        _ = provider.GetHealthDetail();
        _ = provider.GetHasUnfound();
        _ = provider.GetHasInconsistent();
        _ = provider.GetHasRecoveryOrBackfill();
        _ = provider.GetHasStaleOrIncomplete();
        _ = provider.GetHasFullOsds();

        Assert.That(HealthDetailCount(runner), Is.EqualTo(1));
    }

    [Test]
    public void Health_predicates_reissue_cli_when_cache_ttl_is_zero()
    {
        var (provider, runner, _) = Create(ttl: TimeSpan.Zero);
        _ = provider.GetHealthDetail();
        _ = provider.GetHasUnfound();

        Assert.That(HealthDetailCount(runner), Is.GreaterThan(1));
    }

    [Test]
    public void Cache_expires_and_reissues_health_cli()
    {
        var (provider, runner, clock) = Create();
        _ = provider.GetHealthDetail();
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        _ = provider.GetHealthDetail();

        Assert.That(HealthDetailCount(runner), Is.EqualTo(2));
    }

    [Test]
    public void Concurrent_health_queries_issue_one_cli_invocation()
    {
        var (provider, runner, _) = Create();
        Parallel.For(0, 8, _ => provider.GetHealthDetail());

        Assert.That(HealthDetailCount(runner), Is.EqualTo(1));
    }

    [Test]
    public void Osd_dump_parses_numeric_up_and_in()
    {
        var runner = new RecordingProcessRunner
        {
            Output = """{"osds":[{"osd":0,"up":1,"in":1},{"osd":1,"up":0,"in":1}]}"""
        };
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        var membership = provider.ListOsdMembership();

        Assert.That(runner.Commands.Single(), Does.Contain("osd dump"));
        Assert.That(membership[0].Up, Is.True);
        Assert.That(membership[0].In, Is.True);
        Assert.That(membership[1].Up, Is.False);
    }

    [Test]
    public void Osd_dump_without_osds_is_empty()
    {
        var runner = new RecordingProcessRunner { Output = """{"fsid":"abc"}""" };
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        Assert.That(provider.ListOsdMembership(), Is.Empty);
    }

    [Test]
    public void Status_parses_cluster_capacity()
    {
        var runner = new RecordingProcessRunner
        {
            Output = """{"pgmap":{"bytes_total":3000,"bytes_used":1000,"bytes_avail":2000}}"""
        };
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        var capacity = provider.GetCapacity();

        Assert.That(runner.Commands, Is.EqualTo(new[] { "ceph --format json status" }));
        Assert.That(capacity.TotalBytes, Is.EqualTo(3000));
        Assert.That(capacity.UsedBytes, Is.EqualTo(1000));
        Assert.That(capacity.AvailableBytes, Is.EqualTo(2000));
    }

    [TestCase("{}")]
    [TestCase("{\"pgmap\":{\"bytes_total\":-1,\"bytes_used\":0,\"bytes_avail\":0}}")]
    public void Status_rejects_missing_or_invalid_capacity(string output)
    {
        var runner = new RecordingProcessRunner { Output = output };
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        Assert.That(() => provider.GetCapacity(), Throws.InvalidOperationException);
    }

    private static (CephCliQueryProvider Provider, RecordingProcessRunner Runner, FakeClock Clock) Create(TimeSpan? ttl = null)
    {
        var runner = new RecordingProcessRunner { Output = """{"status":"HEALTH_OK","checks":{}}""" };
        var clock = new FakeClock();
        var provider = new CephCliQueryProvider(
            new ControlConfig { CephBinary = "ceph", CephQueryCacheTtl = ttl ?? TimeSpan.FromSeconds(2) },
            runner,
            clock);
        return (provider, runner, clock);
    }

    private static int HealthDetailCount(RecordingProcessRunner runner)
        => runner.Commands.Count(command => command.Contains("health detail", StringComparison.Ordinal));
}
