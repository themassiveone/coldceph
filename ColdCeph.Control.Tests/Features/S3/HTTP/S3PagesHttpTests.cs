using System.Net;
using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Hosts.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace ColdCeph.Control.Tests.Features.S3.HTTP;

[TestFixture]
public sealed class S3PagesHttpTests
{
    [Test]
    public async Task Buckets_page_is_greyed_without_wake_before_setup()
    {
        using var factory = new Support.ControlAppFactory();
        factory.Rgw.SeedBucket("cold");
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/s3");

        Assert.That(html, Does.Contain("Buckets"));
        Assert.That(html, Does.Contain("Finish setting up storage before browsing buckets."));
        Assert.That(html, Does.Contain("is-disabled"));
        Assert.That(html, Does.Not.Contain("Wake storage"));
        Assert.That(html, Does.Not.Contain(">cold<"));
        Assert.That(factory.Ceph.ObservationCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Buckets_page_offers_wake_when_storage_is_asleep()
    {
        using var factory = new Support.ControlAppFactory();
        ConfigureStorage(factory);
        factory.Rgw.SeedBucket("cold");
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/s3");

        Assert.That(html, Does.Contain("Storage is asleep. Wake it to browse buckets."));
        Assert.That(html, Does.Contain("action=\"/wake\""));
        Assert.That(html, Does.Contain("Wake storage"));
        Assert.That(html, Does.Contain("is-disabled"));
        Assert.That(html, Does.Not.Contain("href=\"/s3/cold\""));
        Assert.That(factory.Ceph.ObservationCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Buckets_page_lists_buckets_when_ready()
    {
        using var factory = new Support.ControlAppFactory();
        ConfigureStorage(factory);
        EnterReady(factory);
        factory.Rgw.SeedBucket("cold");
        factory.Rgw.SeedObject("cold", "readme.txt", "hi"u8.ToArray());
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/s3");

        Assert.That(html, Does.Contain("href=\"/s3/cold\""));
        Assert.That(html, Does.Not.Contain("is-disabled"));
        Assert.That(html, Does.Not.Contain("action=\"/wake\""));
        Assert.That(factory.Ceph.ObservationCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Bucket_page_lists_folders_and_download_for_objects()
    {
        using var factory = new Support.ControlAppFactory();
        ConfigureStorage(factory);
        EnterReady(factory);
        factory.Rgw.SeedObject("cold", "readme.txt", "hello"u8.ToArray());
        factory.Rgw.SeedObject("cold", "photos/cat.jpg", "img"u8.ToArray());
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/s3/cold");

        Assert.That(html, Does.Contain("readme.txt"));
        Assert.That(html, Does.Contain("href=\"/s3/cold/download/readme.txt\""));
        Assert.That(html, Does.Contain(">photos<"));
        Assert.That(html, Does.Contain("href=\"/s3/cold/photos\""));
        Assert.That(html, Does.Contain("Upload"));
        Assert.That(html, Does.Not.Contain("href=\"/s3/cold/readme.txt\""));
    }

    [Test]
    public async Task Folder_page_lists_nested_objects()
    {
        using var factory = new Support.ControlAppFactory();
        ConfigureStorage(factory);
        EnterReady(factory);
        factory.Rgw.SeedObject("cold", "photos/cat.jpg", "img"u8.ToArray());
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/s3/cold/photos");

        Assert.That(html, Does.Contain("cat.jpg"));
        Assert.That(html, Does.Contain("href=\"/s3/cold/download/photos/cat.jpg\""));
        Assert.That(html, Does.Contain("href=\"/s3\""));
        Assert.That(html, Does.Contain("Upload"));
    }

    [Test]
    public async Task Download_returns_the_object_bytes()
    {
        using var factory = new Support.ControlAppFactory();
        ConfigureStorage(factory);
        EnterReady(factory);
        factory.Rgw.SeedObject("cold", "photos/cat.jpg", "img"u8.ToArray());
        using var client = await Support.OperatorClient.SignedIn(factory);

        var response = await client.GetAsync("/s3/cold/download/photos/cat.jpg");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.EqualTo("img"));
        Assert.That(response.Content.Headers.ContentDisposition?.FileName, Does.Contain("cat.jpg"));
    }

    [Test]
    public async Task Download_while_asleep_redirects_home_to_buckets()
    {
        using var factory = new Support.ControlAppFactory();
        ConfigureStorage(factory);
        factory.Rgw.SeedObject("cold", "readme.txt", "hi"u8.ToArray());
        using var client = await Support.OperatorClient.SignedIn(factory);

        var response = await client.GetAsync("/s3/cold/download/readme.txt");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/s3"));
    }

    [Test]
    public async Task Upload_stores_the_file_at_the_current_prefix()
    {
        using var factory = new Support.ControlAppFactory();
        ConfigureStorage(factory);
        EnterReady(factory);
        factory.Rgw.SeedBucket("cold");
        using var client = await Support.OperatorClient.SignedIn(factory);
        var page = await client.GetStringAsync("/s3/cold/photos");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(Support.OperatorClient.AntiforgeryToken(page)), "__RequestVerificationToken");
        content.Add(new StringContent("photos/"), "prefix");
        content.Add(new ByteArrayContent("note"u8.ToArray()), "file", "note.txt");

        var response = await client.PostAsync("/s3/cold/upload", content);
        var html = await client.GetStringAsync("/s3/cold/photos");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(html, Does.Contain("note.txt"));
        Assert.That(html, Does.Contain("href=\"/s3/cold/download/photos/note.txt\""));
    }

    [Test]
    public async Task Layout_uses_buckets_instead_of_data_protection()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/");

        Assert.That(html, Does.Contain("href=\"/s3\""));
        Assert.That(html, Does.Contain(">Buckets<"));
        Assert.That(html, Does.Not.Contain("href=\"/integrity\""));
        Assert.That(html, Does.Not.Contain("Data protection</a>"));
        Assert.That(html, Does.Not.Contain("Advanced"));
        Assert.That(html, Does.Not.Contain("Backup traffic"));
    }

    private static void ConfigureStorage(Support.ControlAppFactory factory)
    {
        var hosts = factory.Services.GetRequiredService<HostsController>();
        hosts.RequestJoin(
            new NodeStatusDto { HostId = "node-a", Hostname = "node-a", ObservedAt = DateTimeOffset.UtcNow },
            "http://127.0.0.1:7081");
        hosts.Approve("node-a");
        factory.Services.GetRequiredService<OsdsController>().ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds = [new OsdDto { OsdId = 0, HostId = "node-a", DeviceId = "disk-a", Up = false, In = true, ProcessRunning = false }]
        });
        factory.Services.GetRequiredService<DevicesController>().ApplyObserved(new HostDevicesObservationDto
        {
            HostId = "node-a",
            Devices =
            [
                new DeviceDto
                {
                    DeviceId = "disk-a",
                    HostId = "node-a",
                    MappedOsdId = 0,
                    Wwn = "wwn-a",
                    Serial = "serial-a",
                    Path = "/dev/sda",
                    PowerState = DevicePowerState.Standby
                }
            ]
        });
    }

    private static void EnterReady(Support.ControlAppFactory factory)
    {
        var plane = factory.Services.GetRequiredService<StoragePlaneService>();
        var operationId = OperationIdRules.Create().Value;
        plane.RequestWake(operationId, "operator");
        plane.EnterReady(operationId);
    }
}
