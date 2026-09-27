namespace ColdCeph.Debug.Shared;

public static class OperatorEndpoint
{
    public static Uri Url()
    {
        var configured = Environment.GetEnvironmentVariable("COLDCEPH_OPERATOR_URL");
        if (!string.IsNullOrWhiteSpace(configured))
            return new Uri(configured.TrimEnd('/') + "/", UriKind.Absolute);
        var port = Environment.GetEnvironmentVariable("WEB_PORT") ?? "8080";
        return new Uri($"http://127.0.0.1:{port}/");
    }
}
