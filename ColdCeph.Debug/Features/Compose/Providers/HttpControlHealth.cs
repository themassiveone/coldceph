using ColdCeph.Debug.Features.Compose.Interfaces;

namespace ColdCeph.Debug.Features.Compose.Providers;

public sealed class HttpControlHealth : IControlHealth
{
    public bool IsReady()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var port = Environment.GetEnvironmentVariable("WEB_PORT") ?? "8080";
            using var response = client.GetAsync($"http://127.0.0.1:{port}/health").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
