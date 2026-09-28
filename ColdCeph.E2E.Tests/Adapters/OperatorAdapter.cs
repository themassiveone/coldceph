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
                   && html.Contains("Overview", StringComparison.Ordinal)
                   && html.Contains("Capacity", StringComparison.Ordinal)
                   && html.Contains("Data protection", StringComparison.Ordinal);
        }));
    }

    public void SeeOverviewCapacity()
    {
        AddStep(new ExpectationStepState("overview shows confirmed storage capacity", async () =>
        {
            var html = await GetHomeHtmlAsync();
            return html.Contains("available", StringComparison.Ordinal)
                   && html.Contains("used of", StringComparison.Ordinal)
                   && !html.Contains("No capacity reading yet", StringComparison.Ordinal);
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
            var html = await GetHomeHtmlAsync();
            await PostFormAsync("/integrity/check", html);
            html = await GetHomeHtmlAsync();
            return !html.Contains("UNAVAILABLE", StringComparison.Ordinal)
                   && !html.Contains("No capacity reading yet", StringComparison.Ordinal)
                   && (html.Contains("HEALTH_OK", StringComparison.Ordinal)
                       || html.Contains("Protected when last checked", StringComparison.Ordinal)
                       || html.Contains("used of", StringComparison.Ordinal));
        }));
    }

    /// <summary>
    /// Reads the live confirmation and asserts ColdCeph classified everything the cluster is
    /// actually reporting as expected.
    /// <para>
    /// This is the step the harness used to make impossible. It muted the demo cluster's
    /// warnings and refused to start unless health was clean — because, as its own throw said,
    /// "Integrity would FAULT at READY". Classification is the most safety-critical logic in
    /// ColdCeph and it had never met a single real health check.
    /// </para>
    /// </summary>
    public void SeeLiveHealthIsClassifiedExpected()
    {
        AddStep(new ExpectationStepState("live Ceph health classifies as expected", async () =>
        {
            var snapshot = await ConfirmIntegrityAsync();
            if (snapshot is null)
                return false;
            var unexpected = UnexpectedChecks(snapshot);
            if (unexpected.Count > 0)
                throw new InvalidOperationException(
                    "Ceph is reporting checks ColdCeph classifies as unexpected, which holds writes back: "
                    + string.Join(", ", unexpected)
                    + ". Either the condition is real, or the check belongs in IntegrityService's "
                    + "cold-phase list and in Support/CephFixtures.");
            return true;
        }));
    }

    /// <summary>
    /// A live cluster is not a durability failure. If this trips, an ordinary warning is being
    /// read as data loss and the appliance would FAULT — which does not clear on its own.
    /// </summary>
    public void SeeLiveHealthIsNotADurabilityFailure()
    {
        AddStep(new ExpectationStepState("live Ceph health is not a durability failure", async () =>
        {
            var snapshot = await ConfirmIntegrityAsync();
            if (snapshot is null)
                return false;
            return snapshot.Contains("\"durabilityFailure\":false", StringComparison.OrdinalIgnoreCase)
                   || snapshot.Contains("\"durabilityFailure\": false", StringComparison.OrdinalIgnoreCase);
        }));
    }

    /// <summary>
    /// After a sleep, StoragePlane holds a scoped noout and Ceph raises a flags check for it. This
    /// is the invariant "controller-owned noout is expected, not a reason to FAULT" meeting the
    /// string Ceph actually emits — which no test had ever done, because the unit fixtures were
    /// written from imagination and the E2E cluster was gated on HEALTH_OK.
    /// </summary>
    public void SeeOwnedNooutIsClassifiedExpected()
    {
        AddStep(new ExpectationStepState("controller-owned noout classifies as expected", async () =>
        {
            var snapshot = await ConfirmIntegrityAsync();
            if (snapshot is null)
                return false;

            // Ceph raises the flags check on the next mgr tick, so its absence is a retry rather
            // than a verdict. If it never appears the step times out naming itself, which says the
            // scoped noout covered nothing.
            var flags = snapshot.Contains("OSD_FLAGS", StringComparison.Ordinal)
                        || snapshot.Contains("OSDMAP_FLAGS", StringComparison.Ordinal);
            if (!flags)
                return false;

            var unexpected = UnexpectedChecks(snapshot);
            if (unexpected.Count > 0)
                throw new InvalidOperationException(
                    "A noout this controller owns was classified unexpected: " + string.Join(", ", unexpected));
            return true;
        }));
    }

    /// <summary>
    /// The Osds list overlays Ceph's <c>osd dump</c> onto node-pushed inventory. This asserts the
    /// overlay actually carried Ceph's up/in through, rather than the page rendering whatever the
    /// node last said.
    /// <para>
    /// Note what this does and does not prove. The E2E node uses an in-memory OSD runtime, because
    /// the demo container has no supervisor to restart <c>ceph-osd</c> on request, so starting and
    /// stopping an OSD here is simulated. The overlay, and therefore ColdCeph's reading of real
    /// Ceph membership, is genuine; the process lifecycle is not. See
    /// docs/design/test-representativeness-review.md.
    /// </para>
    /// </summary>
    public void SeeOsdOverlayReportsCephMembership()
    {
        AddStep(new ExpectationStepState("Osds list carries Ceph's up/in through the overlay", async () =>
        {
            var response = await _client.GetAsync(new Uri(_baseUrl, "/v1/osds"));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"/v1/osds failed: {(int)response.StatusCode}");
            if (body.Contains("[]", StringComparison.Ordinal))
                throw new InvalidOperationException("No OSD inventory reached Control from the node.");

            // The demo cluster's osd.0 is up and in, and the overlay is the only thing that could
            // put that on a row the node reported as down.
            var up = body.Contains("\"up\":true", StringComparison.OrdinalIgnoreCase)
                     || body.Contains("\"up\": true", StringComparison.OrdinalIgnoreCase);
            var inCluster = body.Contains("\"in\":true", StringComparison.OrdinalIgnoreCase)
                            || body.Contains("\"in\": true", StringComparison.OrdinalIgnoreCase);
            return up && inCluster;
        }));
    }

    /// <summary>
    /// Asserts the live confirmation actually admits reads and writes, and says which predicate
    /// refused if not.
    /// <para>
    /// S3 answers 503 for every reason there is — not READY, not read-ready, not write-ready — so a
    /// refused GET or PUT on its own tells you nothing. This turns that into the predicate and the
    /// checks behind it.
    /// </para>
    /// </summary>
    public void SeeConfirmationAdmitsReadsAndWrites()
    {
        AddStep(new ExpectationStepState("the live confirmation admits reads and writes", async () =>
        {
            var snapshot = await ConfirmIntegrityAsync();
            if (snapshot is null)
                return false;

            var readReady = Flag(snapshot, "readReady");
            var writeReady = Flag(snapshot, "writeReady");
            if (readReady == true && writeReady == true)
                return true;

            throw new InvalidOperationException(
                $"The cluster is READY but the confirmation does not admit "
                + $"{(readReady == true ? string.Empty : "reads")}"
                + $"{(readReady != true && writeReady != true ? " or " : string.Empty)}"
                + $"{(writeReady == true ? string.Empty : "writes")}, so S3 answers 503. "
                + $"Unexpected checks: {Describe(UnexpectedChecks(snapshot))}. "
                + $"Predicates and checks: {snapshot}");
        }));
    }

    private static string Describe(IReadOnlyList<string> names)
        => names.Count == 0 ? "(none)" : string.Join(", ", names);

    /// <summary>Reads a boolean out of the integrity document, or null when it is not there.</summary>
    private static bool? Flag(string json, string name)
    {
        var match = Regex.Match(json, $"\"{name}\"\\s*:\\s*(?<value>true|false)");
        return match.Success ? match.Groups["value"].Value == "true" : null;
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

    /// <summary>
    /// Refreshes protection through the operator POST, then reads the classified document from
    /// <c>/v1</c>. That is the same confirmation the operator triggers, so what is asserted is what
    /// the appliance concluded, not a separate reading taken beside it.
    /// </summary>
    /// <summary>
    /// Returns null when the confirmation has not landed yet — an unreachable monitor on the first
    /// tick is a retry, not a verdict. Only a classification ColdCeph got wrong throws, so a
    /// transient does not abort the journey before Xcepto can retry it.
    /// </summary>
    private async Task<string?> ConfirmIntegrityAsync()
    {
        await PostFormAsync("/integrity/check", await GetAntiforgeryHtmlAsync());
        var response = await _client.GetAsync(new Uri(_baseUrl, "/v1/cluster/integrity"));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"/v1/cluster/integrity failed: {(int)response.StatusCode}");
        return body.Contains("\"status\":\"UNAVAILABLE\"", StringComparison.Ordinal) ? null : body;
    }

    /// <summary>
    /// Names the checks classified unexpected, so a failure says which condition ColdCeph did not
    /// recognise rather than just that something was wrong.
    /// </summary>
    private static List<string> UnexpectedChecks(string integrityJson)
        => Regex
            .Matches(
                integrityJson,
                "\\{\\s*\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"(?<body>.*?)\\}",
                RegexOptions.Singleline)
            .Where(match => match.Groups["body"].Value.Contains("\"unexpected\"", StringComparison.OrdinalIgnoreCase)
                            || match.Groups["body"].Value.Contains("\"classification\":1", StringComparison.Ordinal)
                            || match.Groups["body"].Value.Contains("\"classification\": 1", StringComparison.Ordinal))
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

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
