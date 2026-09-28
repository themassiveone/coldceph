using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.S3.Interfaces;
using ColdCeph.Control.Features.S3.ViewModels;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.S3.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Features.S3.Services;

public sealed class S3BrowseService
{
    private readonly IRgwObjectStore _store;
    private readonly StoragePlaneController _plane;
    private readonly IntegrityController _integrity;
    private readonly HostsController _hosts;
    private readonly DevicesController _devices;
    private readonly OsdsController _osds;

    public S3BrowseService(
        IRgwObjectStore store,
        StoragePlaneController plane,
        IntegrityController integrity,
        HostsController hostsController,
        DevicesController devicesController,
        OsdsController osdsController)
    {
        _store = store;
        _plane = plane;
        _integrity = integrity;
        _hosts = hostsController;
        _devices = devicesController;
        _osds = osdsController;
    }

    public bool CanBrowse() => _plane.GetState().State == StoragePlaneState.Ready;

    public S3BrowseViewModel Page(string? bucket, string? prefix)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        var snapshot = _plane.GetState();
        var integrity = _integrity.GetLastIntegrity();
        var hostList = _hosts.ListHosts();
        var pending = _hosts.ListPendingJoins().Count;
        var devices = _devices.ListDevices();
        var osds = _osds.ListObservedOsds();
        var setup = hostList.Count > 0
                    && hostList.Count(host => host.Alive) == hostList.Count
                    && pending == 0
                    && devices.Count > 0
                    && osds.Count > 0;
        var canWake = setup && snapshot.State switch
        {
            StoragePlaneState.Cold => true,
            StoragePlaneState.Faulted => !IntegrityDurability.HasFailure(integrity),
            _ => false
        };
        var ready = snapshot.State == StoragePlaneState.Ready;
        var model = new S3BrowseViewModel
        {
            State = snapshot.State,
            BrowseReady = ready,
            CanWake = canWake,
            Banner = Banner(snapshot.State, setup, ready),
            Bucket = bucket,
            Prefix = normalizedPrefix,
            Crumbs = Crumbs(bucket, normalizedPrefix)
        };
        if (!ready || bucket is not null && !ValidBucket(bucket))
            return model;

        try
        {
            if (bucket is null)
                return model with { Buckets = _store.ListBuckets() };

            var listing = _store.ListObjects(bucket, normalizedPrefix);
            return model with { Prefixes = listing.Prefixes, Objects = listing.Objects };
        }
        catch (Exception exception)
        {
            return model with { Error = exception.Message };
        }
    }

    public bool TryOpen(string bucket, string key, out Stream stream, out string name)
    {
        stream = Stream.Null;
        name = "";
        if (!CanBrowse() || !ValidBucket(bucket) || !ValidKey(key))
            return false;
        var normalized = NormalizeKey(key);
        stream = _store.OpenObject(bucket, normalized);
        name = S3KeyRules.FileName(normalized);
        return true;
    }

    public string Upload(string bucket, string? prefix, string? fileName, Stream? body, string? contentType)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        if (!CanBrowse() || !ValidBucket(bucket) || body is null || body.CanSeek && body.Length == 0)
            return BrowsePath(bucket, normalizedPrefix);
        var name = Path.GetFileName(fileName ?? "");
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal))
            return BrowsePath(bucket, normalizedPrefix);
        _store.PutObject(bucket, normalizedPrefix + name, body, contentType ?? "application/octet-stream");
        return BrowsePath(bucket, normalizedPrefix);
    }

    public IReadOnlyList<S3BucketDto> ListBuckets()
    {
        EnsureReady();
        return _store.ListBuckets();
    }

    public S3ObjectListingDto ListObjects(string bucket, string prefix)
    {
        EnsureReady();
        return _store.ListObjects(bucket, prefix);
    }

    public Stream OpenObject(string bucket, string key)
    {
        EnsureReady();
        return _store.OpenObject(bucket, key);
    }

    public void PutObject(string bucket, string key, Stream body, string contentType)
    {
        EnsureReady();
        _store.PutObject(bucket, key, body, contentType);
    }

    private void EnsureReady()
    {
        if (!CanBrowse())
            throw new InvalidOperationException("Storage is not ready for object access.");
    }

    private static string Banner(StoragePlaneState state, bool setup, bool ready)
    {
        if (ready)
            return string.Empty;
        if (!setup)
            return "Finish setting up storage before browsing buckets.";
        return state switch
        {
            StoragePlaneState.Cold => "Storage is asleep. Wake it to browse buckets.",
            StoragePlaneState.Waking => "Storage is waking. Buckets will be available when it is ready.",
            StoragePlaneState.Quiescing or StoragePlaneState.Sleeping => "Storage is going to sleep. Buckets are unavailable.",
            StoragePlaneState.Faulted => "Storage needs attention. Buckets are unavailable.",
            _ => "Storage is not ready for buckets."
        };
    }

    private static IReadOnlyList<(string Label, string Href)> Crumbs(string? bucket, string prefix)
    {
        var crumbs = new List<(string, string)> { ("Buckets", "/s3") };
        if (string.IsNullOrWhiteSpace(bucket))
            return crumbs;
        crumbs.Add((bucket, $"/s3/{Uri.EscapeDataString(bucket)}"));
        var accumulated = "";
        foreach (var part in prefix.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated += part + "/";
            crumbs.Add((part, $"/s3/{Uri.EscapeDataString(bucket)}/{EscapePrefix(accumulated)}"));
        }

        return crumbs;
    }

    private static string BrowsePath(string bucket, string prefix)
        => string.IsNullOrEmpty(prefix)
            ? $"/s3/{Uri.EscapeDataString(bucket)}"
            : $"/s3/{Uri.EscapeDataString(bucket)}/{EscapePrefix(prefix)}";

    private static string EscapePrefix(string prefix)
        => string.Join('/', prefix.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private static string NormalizePrefix(string? prefix)
    {
        var value = (prefix ?? "").Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(value))
            return "";
        return value.EndsWith('/') ? value : value + "/";
    }

    private static string NormalizeKey(string key)
        => key.Replace('\\', '/').TrimStart('/');

    private static bool ValidBucket(string bucket)
        => !string.IsNullOrWhiteSpace(bucket)
           && !bucket.Contains('/', StringComparison.Ordinal)
           && !bucket.Contains("..", StringComparison.Ordinal);

    private static bool ValidKey(string key)
        => !string.IsNullOrWhiteSpace(key) && !key.Contains("..", StringComparison.Ordinal);
}
