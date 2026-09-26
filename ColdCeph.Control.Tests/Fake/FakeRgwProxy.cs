using ColdCeph.Control.Features.S3.Interfaces;
using Microsoft.AspNetCore.Http;

namespace ColdCeph.Control.Tests.Fake;

public sealed class FakeRgwProxy : IRgwProxy
{
    public int Calls { get; private set; }
    public int StatusCode { get; set; } = 200;

    public Task ProxyAsync(HttpContext context)
    {
        Calls++;
        context.Response.StatusCode = StatusCode;
        return Task.CompletedTask;
    }
}
