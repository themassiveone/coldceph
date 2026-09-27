using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using ColdCeph.E2E.Tests.States;
using ColdCeph.E2E.Tests.Support;
using Xcepto.Adapters;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class OperatorAdapter : XceptoAdapter
{
    private readonly HttpClient _client;
    private readonly Uri _baseUrl;
    private bool _signedIn;
    private bool _sleepPosted;
    private bool _wakePosted;

    internal OperatorAdapter(HttpClient client, Uri baseUrl)
    {
        _client = client;
        _baseUrl = baseUrl;
    }

    public void EnsureCold(string password)
    {
        AddStep(new ExpectationStepState("ensure storage plane is Cold", async () =>
        {
            await EnsureSignedInAsync(password);
            var state = await GetPlaneStateAsync();
            if (state == "Cold")
                return true;
            if (state == "Faulted")
                throw new InvalidOperationException("Storage plane is Faulted.");
            if (state == "Ready" && !_sleepPosted)
            {
                await PostFormAsync("/sleep", await GetAntiforgeryHtmlAsync());
                _sleepPosted = true;
            }

            return false;
        }));
    }

    public void EnsureReady(string password)
    {
        AddStep(new ExpectationStepState("ensure storage plane is Ready", async () =>
        {
            await EnsureSignedInAsync(password);
            var state = await GetPlaneStateAsync();
            if (state == "Ready")
                return true;
            if (state == "Faulted")
                throw new InvalidOperationException("Storage plane is Faulted.");
            if (state == "Cold" && !_wakePosted)
            {
                await PostFormAsync("/wake", await GetAntiforgeryHtmlAsync());
                _wakePosted = true;
            }

            return false;
        }));
    }

    public void OpenLogin()
    {
        AddStep(new ExpectationStepState("open operator login", async () =>
        {
            var response = await _client.GetAsync(new Uri(_baseUrl, "/auth/login"));
            var html = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode && html.Contains("Operator login", StringComparison.Ordinal);
        }));
    }

    public void Login(string password)
    {
        AddStep(new ActionStepState("login as operator", async () => await EnsureSignedInAsync(password)));
    }

    public void LoginRejected(string password)
    {
        AddStep(new ExpectationStepState("reject operator password", async () =>
        {
            var loginPage = await _client.GetAsync(new Uri(_baseUrl, "/auth/login"));
            var html = await loginPage.Content.ReadAsStringAsync();
            var token = SharedEnvironment.ExtractAntiforgeryToken(html);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["password"] = password,
                ["__RequestVerificationToken"] = token
            });
            var response = await _client.PostAsync(new Uri(_baseUrl, "/auth/login"), content);
            return response.StatusCode == HttpStatusCode.Unauthorized;
        }));
    }

    public void SeeUnauthorizedHome()
    {
        AddStep(new ExpectationStepState("home sends anonymous operators to login", async () =>
        {
            var response = await _client.GetAsync(new Uri(_baseUrl, "/"));
            var location = response.Headers.Location?.OriginalString;
            var toLogin = location is "/auth/login"
                          || location?.EndsWith("/auth/login", StringComparison.Ordinal) == true;
            return response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found
                   && toLogin;
        }));
    }

    public void SeeOsds(string password)
    {
        AddStep(new ExpectationStepState("operator OSDs page lists an OSD", async () =>
        {
            await EnsureSignedInAsync(password);
            var response = await _client.GetAsync(new Uri(_baseUrl, "/osds"));
            var html = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode && html.Contains("osd.", StringComparison.Ordinal);
        }));
    }

    public void SeeDevices(string password)
    {
        AddStep(new ExpectationStepState("operator devices page lists a drive", async () =>
        {
            await EnsureSignedInAsync(password);
            var response = await _client.GetAsync(new Uri(_baseUrl, "/devices"));
            var html = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode && html.Contains("d0", StringComparison.Ordinal);
        }));
    }

    public void SeeDashboard()
    {
        AddStep(new ExpectationStepState("operator dashboard is visible", async () =>
        {
            var html = await GetHomeHtmlAsync();
            return html.Contains("ColdCeph", StringComparison.Ordinal)
                   && html.Contains("Operational</span>", StringComparison.Ordinal)
                   && html.Contains("Storage plane", StringComparison.Ordinal);
        }));
    }

    public void SeeStoragePlane(string state)
    {
        AddStep(new ExpectationStepState($"storage plane is {state}", async () =>
        {
            var observed = await GetPlaneStateAsync();
            return observed == state;
        }));
    }

    public void SeeCephHealthExists()
    {
        AddStep(new ExpectationStepState("raw Ceph health is reported", async () =>
        {
            var response = await _client.GetAsync(new Uri(_baseUrl, "/integrity"));
            var html = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode
                   && html.Contains("Raw Ceph health:", StringComparison.Ordinal)
                   && !html.Contains("UNAVAILABLE", StringComparison.Ordinal)
                   && (html.Contains("HEALTH_OK", StringComparison.Ordinal)
                       || html.Contains("HEALTH_WARN", StringComparison.Ordinal)
                       || html.Contains("HEALTH_ERR", StringComparison.Ordinal));
        }));
    }

    public void Wake()
    {
        AddStep(new ActionStepState("wake storage plane", async () =>
        {
            var html = await GetAntiforgeryHtmlAsync();
            await PostFormAsync("/wake", html);
            _wakePosted = true;
        }));
    }

    public void Sleep()
    {
        AddStep(new ActionStepState("sleep storage plane", async () =>
        {
            var html = await GetAntiforgeryHtmlAsync();
            await PostFormAsync("/sleep", html);
            _sleepPosted = true;
        }));
    }

    private async Task EnsureSignedInAsync(string password)
    {
        if (_signedIn)
            return;
        await SignInAsync(password);
        _signedIn = true;
    }

    private async Task SignInAsync(string password)
    {
        var loginPage = await _client.GetAsync(new Uri(_baseUrl, "/auth/login"));
        var html = await loginPage.Content.ReadAsStringAsync();
        var token = SharedEnvironment.ExtractAntiforgeryToken(html);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = password,
            ["__RequestVerificationToken"] = token
        });
        var response = await _client.PostAsync(new Uri(_baseUrl, "/auth/login"), content);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Redirect or HttpStatusCode.Found))
            throw new InvalidOperationException($"Login failed: {(int)response.StatusCode}");
    }

    private async Task<string?> GetPlaneStateAsync()
    {
        var response = await _client.GetAsync(new Uri(_baseUrl, "/health"));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return null;
        var match = Regex.Match(body, "\"state\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<string> GetAntiforgeryHtmlAsync()
    {
        var response = await _client.GetAsync(new Uri(_baseUrl, "/auth/login"));
        var html = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Login page failed: {(int)response.StatusCode}");
        return html;
    }

    private async Task<string> GetHomeHtmlAsync()
    {
        var response = await _client.GetAsync(new Uri(_baseUrl, "/"));
        var html = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Home failed: {(int)response.StatusCode}");
        return html;
    }

    private async Task PostFormAsync(string path, string html)
    {
        var token = SharedEnvironment.ExtractAntiforgeryToken(html);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        var response = await _client.PostAsync(new Uri(_baseUrl, path), content);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Redirect or HttpStatusCode.Found))
            throw new InvalidOperationException($"{path} failed: {(int)response.StatusCode}");
    }
}
