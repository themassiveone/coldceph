using ColdCeph.E2E.Tests.Support;

namespace ColdCeph.E2E.Tests;

[SetUpFixture]
public sealed class E2EEnvironment
{
    internal static readonly CephCluster Ceph = new();

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        try
        {
            await Ceph.StartAsync(startupTimeout.Token);
            await SharedEnvironment.StartAsync(Ceph);
        }
        catch
        {
            await SharedEnvironment.StopAsync();
            await Ceph.DisposeAsync();
            throw;
        }
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        await SharedEnvironment.StopAsync();
        await Ceph.DisposeAsync();
    }
}
