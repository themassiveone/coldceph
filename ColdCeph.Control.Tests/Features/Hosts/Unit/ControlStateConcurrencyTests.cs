using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Services;
using ColdCeph.Control.Features.Hosts.Services;
using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Hosts.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Tests.Features.Hosts.Unit;

/// <summary>
/// Control's inventory singletons are written by node pushes arriving on HTTP threads and read by
/// three reconcile loops every second and by every operator page render. Unsynchronised
/// <see cref="Dictionary{TKey,TValue}"/> access under that pattern throws or tears a read.
/// <para>
/// The Node's matching services already took a lock, because AGENTS.md requires it there — the
/// identical race on Control went unnoticed, and no test in the suite was multi-threaded, so
/// nothing would have caught either.
/// </para>
/// </summary>
[TestFixture]
public sealed class ControlStateConcurrencyTests
{
    // Sized so an unsynchronised implementation actually fails: the window in ApplyObserved
    // between removing a host's entries and re-adding them is short, so it takes a wide
    // inventory and many attempts from several readers to land inside it.
    private const int Iterations = 4_000;
    private const int Readers = 4;
    private const int InventorySize = 120;

    [Test]
    public void Pushing_osd_observations_while_enumerating_does_not_throw()
    {
        var service = new OsdsService(new FakeNodeOsdsClient(), new ControlConfig());
        var wide = Enumerable.Range(0, InventorySize).Select(id => Osd(id, running: true)).ToArray();

        Assert.DoesNotThrow(() => Race(
            write: index => service.ApplyObserved(new HostOsdsObservationDto
            {
                HostId = "h1",
                Osds = index % 2 == 0 ? wide : wide.Take(InventorySize - 1).ToArray()
            }),
            read: () =>
            {
                _ = service.ListOsds().Count;
                _ = service.IsEveryProcessRunning();
                _ = service.IsEveryProcessStopped();
                _ = service.ListObservationErrors().Count;
                _ = service.GetOsd(3);
            }));
    }

    [Test]
    public void Pushing_device_observations_while_enumerating_does_not_throw()
    {
        var service = new DevicesService(new FakeNodeDevicesClient(), new ControlConfig());
        var wide = Enumerable.Range(0, InventorySize).Select(Device).ToArray();

        Assert.DoesNotThrow(() => Race(
            write: index => service.ApplyObserved(new HostDevicesObservationDto
            {
                HostId = "h1",
                Devices = index % 2 == 0 ? wide : wide.Take(InventorySize - 1).ToArray()
            }),
            read: () =>
            {
                _ = service.ListDevices().Count;
                _ = service.IsEveryDeviceStandby();
                _ = service.ListObservationErrors().Count;
                _ = service.GetDevice("d3");
            }));
    }

    [Test]
    public void Joining_hosts_while_the_reconcilers_enumerate_does_not_throw()
    {
        var clock = new FakeClock();
        var service = new HostsService(new ControlConfig(), clock);

        Assert.DoesNotThrow(() => Race(
            write: index =>
            {
                var status = new NodeStatusDto
                {
                    HostId = $"host-{index % 6}",
                    Hostname = $"host-{index % 6}",
                    ObservedAt = clock.UtcNow
                };
                if (index % 3 == 0)
                    _ = service.RequestJoin(status, $"http://127.0.0.1:{7080 + index % 6}");
                else if (index % 3 == 1)
                    _ = service.Approve($"host-{index % 6}");
                else
                    _ = service.RegisterHeartbeat(status, new Uri($"http://127.0.0.1:{7080 + index % 6}"));
            },
            read: () =>
            {
                _ = service.ListHosts().Count;
                _ = service.ListPendingJoins().Count;
                _ = service.ListBlockedJoins().Count;
                _ = service.GetHost("host-2");
            }));
    }

    /// <summary>
    /// A read taken while a host's inventory is being replaced must see either the old set or the
    /// new one, never a half-applied one. <c>ApplyObserved</c> removes then re-adds, so an
    /// unsynchronised reader can observe the gap and conclude the host has no OSDs at all —
    /// which reads as "every process stopped".
    /// </summary>
    [Test]
    public void A_reader_never_sees_a_host_mid_replacement()
    {
        var service = new OsdsService(new FakeNodeOsdsClient(), new ControlConfig());
        var observation = new HostOsdsObservationDto
        {
            HostId = "h1",
            Osds = Enumerable.Range(0, InventorySize).Select(id => Osd(id, running: true)).ToArray()
        };
        service.ApplyObserved(observation);
        var counts = new System.Collections.Concurrent.ConcurrentBag<int>();

        Race(
            write: _ => service.ApplyObserved(observation),
            read: () => counts.Add(service.ListOsds().Count));

        Assert.That(counts, Is.Not.Empty);
        Assert.That(counts.Distinct(), Is.EquivalentTo(new[] { InventorySize }),
            "a reader saw a partially replaced host, so 'every process stopped' could read true mid-push");
    }

    private static DeviceDto Device(int id)
        => new()
        {
            DeviceId = $"d{id}",
            HostId = "h1",
            MappedOsdId = id,
            Wwn = $"wwn-{id}",
            Serial = $"s{id}",
            Path = $"/dev/sd{id}",
            PowerState = DevicePowerState.Standby
        };

    private static OsdDto Osd(int osdId, bool running)
        => new()
        {
            OsdId = osdId,
            HostId = "h1",
            DeviceId = $"d{osdId}",
            Up = running,
            In = true,
            ProcessRunning = running
        };

    private static void Race(Action<int> write, Action read)
    {
        Exception? failure = null;
        var stop = false;
        var started = new CountdownEvent(Readers + 1);

        void Guard(Action body)
        {
            started.Signal();
            started.Wait(TimeSpan.FromSeconds(5));
            try
            {
                body();
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref failure, exception, null);
            }
        }

        var writer = new Thread(() => Guard(() =>
        {
            for (var index = 0; index < Iterations && failure is null; index++)
                write(index);
            stop = true;
        }));

        var readers = Enumerable.Range(0, Readers)
            .Select(_ => new Thread(() => Guard(() =>
            {
                while (!Volatile.Read(ref stop) && failure is null)
                    read();
            })))
            .ToArray();

        writer.Start();
        foreach (var reader in readers)
            reader.Start();

        writer.Join(TimeSpan.FromSeconds(60));
        stop = true;
        foreach (var reader in readers)
            reader.Join(TimeSpan.FromSeconds(60));

        if (failure is not null)
            throw new InvalidOperationException($"Concurrent access failed: {failure.Message}", failure);
    }
}
