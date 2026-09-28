using ColdCeph.Control.Features.S3.Services;
using ColdCeph.Core.Features.S3.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.S3.Controllers;

[Authorize]
public sealed class S3PagesController : Controller
{
    private readonly S3BrowseService _browse;

    public S3PagesController(S3BrowseService browse)
    {
        _browse = browse;
    }

    [HttpGet("/s3")]
    public IActionResult Index() => View("Index", _browse.Page(null, ""));

    [HttpGet("/s3/{bucket}/download/{*key}")]
    public IActionResult Download(string bucket, string key)
    {
        if (!_browse.TryOpen(bucket, key, out var stream, out var name))
            return Redirect("/s3");
        return File(stream, "application/octet-stream", name);
    }

    [HttpPost("/s3/{bucket}/upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(100_000_000)]
    public IActionResult Upload(string bucket, string? prefix, IFormFile? file)
    {
        using var stream = file?.OpenReadStream();
        return Redirect(_browse.Upload(bucket, prefix, file?.FileName, stream, file?.ContentType));
    }

    [HttpGet("/s3/{bucket}/{*prefix}")]
    public IActionResult Bucket(string bucket, string? prefix)
        => View("Index", _browse.Page(bucket, prefix));

    [HttpGet("/s3/{bucket}")]
    public IActionResult BucketRoot(string bucket)
        => View("Index", _browse.Page(bucket, ""));
}
