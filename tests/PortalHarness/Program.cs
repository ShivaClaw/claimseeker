// PortalHarness: the real browser driver, against a page served from this machine. The page has the
// same shape as the one the MissingMoney playbook was written for: a form whose submit calls a JSON
// endpoint. Nothing here leaves the loopback interface.
//
// What it proves that no scripted session can: that the driver really fills a form, really reads the
// answer from the wire, really declares itself automated, and really stops when the page stays
// silent or sends it somewhere else.
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ClaimSeeker.Browser;
using ClaimSeeker.Scout;
using static Harness;

Title("ClaimSeeker browser driver against a local portal");

// ---------- the driver's source is clean ----------
Section("The driver has no way to disguise the browser");
var driverSource = string.Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Browser"), "*.cs", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
    .Select(File.ReadAllText));
foreach (var call in new[] { "AddInitScript", "SetExtraHTTPHeaders", "ExtraHTTPHeaders", "UserAgent =", "IgnoreDefaultArgs", "disable-blink-features", "stealth", "RouteAsync", "Args =" })
    Check($"it never uses {call.TrimEnd(' ', '=')}", !driverSource.Contains(call, StringComparison.OrdinalIgnoreCase));
Equal("it runs exactly one script in a page", CountOf(driverSource, "EvaluateAsync"), 1);
Check("and that script only reads the automation signal", driverSource.Contains("EvaluateAsync<bool>(\"() => navigator.webdriver === true\")", StringComparison.Ordinal));
Check("only the Browser project references the browser library",
    Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
        .Where(f => File.ReadAllText(f).Contains("Microsoft.Playwright", StringComparison.Ordinal))
        .Select(Path.GetFileName).SequenceEqual(["ClaimSeeker.Browser.csproj"]));

// ---------- start the portal and the browser ----------
using var portal = new Portal();
var playbook = PortalPlaybook(portal.Port, "/app/claim-search");

PlaywrightBrowserSession session;
try
{
    session = await PlaywrightBrowserSession.LaunchAsync(new BrowserOptions { Headless = true });
}
catch (BrowserUnavailableException ex)
{
    // Fails closed. A machine with no browser has not run these checks, and the summary must say so.
    Check("a browser could be started", false, ex.Message);
    return Finish();
}

await using (session)
{
    Section("A real browser, declaring itself");
    Check("a browser was started", session.Description.Length > 0, session.Description);
    Console.WriteLine($"        browser: {session.Description}");

    var runner = new PlaybookRunner(new RealPacer());
    var inputs = new Dictionary<string, string?> { ["familyName"] = "SAMPLE", ["givenName"] = null, ["state"] = null };

    Section("The search form is filled, submitted and its answer read from the wire");
    var outcome = await runner.RunAsync(playbook, session, inputs);
    Equal("the search is captured", outcome.Status, RunStatus.Captured);
    if (outcome.Status != RunStatus.Captured)
    {
        // Everything after this reads the answer. Without one, say why and stop.
        Console.WriteLine($"        reason: {outcome.Reason}");
        return Finish();
    }

    Check("the page saw automation declared", await session.IsAutomationDeclaredAsync(CancellationToken.None));
    Equal("the portal was told the same thing by the page itself", portal.LastWebdriverFlag, "true");
    Equal("the portal received the family name that was typed", portal.LastQuery("ln"), "SAMPLE");
    Equal("the given name was left empty", portal.LastQuery("fn"), "");
    Equal("the state was left on all states", portal.LastQuery("st"), "");
    var mapped = ResultMapper.Map(playbook, outcome.Json!);
    Equal("the answer maps to the portal's two records", mapped.Records.Count, 2);
    Check("with the fields the playbook names", mapped.Records[0] is { OwnerName: "SAMPLE JANE", HolderName: "LOCAL PORTAL HOLDER", City: "SPRINGFIELD", State: "IL", ValueText: "OVER $100" });
    Equal("the page was opened once and searched once", (portal.PageLoads, portal.ApiCalls), (1, 1));
    Check("the browser sent its own, unaltered user agent", portal.LastUserAgent.Contains("Chrome", StringComparison.Ordinal) && !portal.LastUserAgent.Contains("ClaimSeeker", StringComparison.Ordinal));

    Section("A narrowed search fills the other fields");
    var narrowed = await runner.RunAsync(playbook, session, new Dictionary<string, string?> { ["familyName"] = "SAMPLE", ["givenName"] = "JANE", ["state"] = "OH" });
    Equal("the search is captured", narrowed.Status, RunStatus.Captured);
    Equal("the given name reached the portal", portal.LastQuery("fn"), "JANE");
    Equal("the state reached the portal", portal.LastQuery("st"), "OH");
    Check("the second answer is the second search's, not the first's again", narrowed.Json!.Contains("\"echo\":\"SAMPLE|JANE|OH\"", StringComparison.Ordinal));

    Section("An answer is read only from the source's own host");
    portal.Reset();
    var decoy = await runner.RunAsync(PortalPlaybook(portal.Port, "/app/decoy-search"), session, inputs);
    Equal("the search is captured", decoy.Status, RunStatus.Captured);
    Check("the other host really was called first", portal.DecoyCalls == 1);
    Check("its answer was ignored", !decoy.Json!.Contains("DECOY", StringComparison.Ordinal) && decoy.Json.Contains("LOCAL PORTAL HOLDER", StringComparison.Ordinal));

    Section("A page that stays silent is left alone");
    portal.Reset();
    var silent = await runner.RunAsync(PortalPlaybook(portal.Port, "/app/silent-search"), session, inputs);
    Equal("the search is handed to the person", silent.Status, RunStatus.Fallback);
    Equal("for the stated reason", silent.Reason, PlaybookRunner.NoAnswerReason);
    Equal("the page was opened once and not reloaded", portal.PageLoads, 1);
    Equal("no search reached the source", portal.ApiCalls, 0);

    Section("A redirect off the allowed hosts stops the search before anything is typed");
    portal.Reset();
    var redirected = await runner.RunAsync(PortalPlaybook(portal.Port, "/app/redirect-search"), session, inputs);
    Equal("the search is handed to the person", redirected.Status, RunStatus.Fallback);
    Equal("for the stated reason", redirected.Reason, PlaybookRunner.WrongPlaceReason);
    Check("the browser did land on the other host", new Uri(session.CurrentUrl).Host == "localhost");
    Equal("and nothing was submitted there", portal.ApiCalls, 0);

    Section("A field the page does not have ends the search; it is not retried");
    portal.Reset();
    var header = JsonNode.Parse(PlaybookParser.ExtractHeader(PortalPlaybookText(portal.Port, "/app/claim-search"))!)!.AsObject();
    header["steps"]![3]!["selector"] = "input[name=\"surname\"]";
    var drifted = PlaybookParser.Parse("# Drifted\n\n```json\n" + header.ToJsonString() + "\n```\n");
    var missingField = await new PlaybookRunner(new RealPacer()).RunAsync(drifted, new ShortTimeoutSession(session), inputs);
    Equal("the search is handed to the person", missingField.Status, RunStatus.Fallback);
    Check("the failing step is named", missingField.Reason!.Contains("Step 4 (fill)", StringComparison.Ordinal));
    Equal("the page was opened once", portal.PageLoads, 1);

    Section("Through the paced executor");
    portal.Reset();
    var pacer = new TimingPacer();
    var executor = new BrowserQueryExecutor(session, pacer);
    var first = await executor.ExecuteAsync(playbook, Query("SAMPLE"));
    var second = await executor.ExecuteAsync(playbook, Query("EXAMPLE"));
    Check("two searches in one session both return", first.Status == RunStatus.Captured && second.Status == RunStatus.Captured);
    Equal("each reached the portal once", portal.ApiCalls, 2);
    Check("the pause between searches was taken", pacer.Delays.Contains(TimeSpan.FromMilliseconds(playbook.Pacing.MinMsBetweenQueries)));
    Check("the pauses before each submit were taken", pacer.Delays.Count(d => d == TimeSpan.FromMilliseconds(60)) == 4);
}

return Finish();

// ---------- helpers ----------

static int CountOf(string text, string value)
{
    var count = 0;
    var index = 0;
    while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) { count++; index += value.Length; }
    return count;
}

static SourceQuery Query(string family) => new() { SourceId = "local-portal", FamilyName = family, Kind = VariantKind.FamilyOnly, SubjectIds = ["self"] };

static Playbook PortalPlaybook(int port, string path) => PlaybookParser.Parse(PortalPlaybookText(port, path));

// The MissingMoney playbook's steps, pointed at the loopback portal, with pauses short enough for a harness.
static string PortalPlaybookText(int port, string path) => $$"""
    # Local portal

    ```json
    {
      "format": 1,
      "id": "local-portal",
      "version": 1,
      "source": "local-portal",
      "mode": "auto",
      "allowedHosts": ["127.0.0.1"],
      "entry": "http://127.0.0.1:{{port}}{{path}}",
      "steps": [
        { "op": "capture", "name": "results", "urlContains": "/SWS/properties", "status": 200 },
        { "op": "goto", "url": "{entry}", "waitUntil": "networkIdle", "timeoutMs": 30000 },
        { "op": "wait", "ms": 60 },
        { "op": "fill", "selector": "input[name=\"lastName\"]", "value": "{familyName}" },
        { "op": "fill", "selector": "input[name=\"firstName\"]", "value": "{givenName}", "when": "givenName" },
        { "op": "select", "selector": "select[id=\"state\"]", "value": "{state}", "when": "state" },
        { "op": "wait", "ms": 60 },
        { "op": "click", "selector": "button[type=\"submit\"]" },
        { "op": "await", "capture": "results", "timeoutMs": 4000 }
      ],
      "result": {
        "capture": "results",
        "items": "properties",
        "fields": { "ownerName": "ownerName", "holderName": "holderName", "valueText": "propertyValueDescription", "city": "city", "state": "state", "propertyTypeText": "propertyTypeDescription" }
      },
      "pacing": { "minMsBetweenQueries": 120, "maxQueriesPerSession": 5 },
      "fallback": { "deepLink": "http://127.0.0.1:{{port}}{{path}}", "instructions": "Run the search by hand." }
    }
    ```
    """;

/// <summary>
/// A search page served on the loopback interface. `/app/claim-search` behaves like the real page's
/// shape. The other paths misbehave in one way each: one calls another host first, one never
/// answers, one redirects to another host.
/// </summary>
internal sealed class Portal : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, string> _lastQuery = new();
    private int _pageLoads, _apiCalls, _decoyCalls;

    public Portal()
    {
        Port = FreePort();
        // Two names for the same listener. The playbook allows only the first.
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public int Port { get; }
    public int PageLoads => _pageLoads;
    public int ApiCalls => _apiCalls;
    public int DecoyCalls => _decoyCalls;
    public string LastUserAgent { get; private set; } = "";
    public string LastWebdriverFlag { get; private set; } = "";

    public string LastQuery(string key) => _lastQuery.GetValueOrDefault(key, "");

    public void Reset()
    {
        _pageLoads = 0;
        _apiCalls = 0;
        _decoyCalls = 0;
        _lastQuery.Clear();
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }

            try { await HandleAsync(context); }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException) { }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var path = request.Url!.AbsolutePath;
        var viaLocalhost = request.Url.Host == "localhost";
        var response = context.Response;
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Cache-Control"] = "no-store";

        if (path == "/SWS/properties")
        {
            var query = request.QueryString;
            if (viaLocalhost)
            {
                Interlocked.Increment(ref _decoyCalls);
                await WriteAsync(response, "application/json", """{"properties":[{"ownerName":"DECOY","holderName":"DECOY HOST"}]}""");
                return;
            }

            Interlocked.Increment(ref _apiCalls);
            foreach (var key in new[] { "ln", "fn", "st", "wd" }) _lastQuery[key] = query[key] ?? "";
            LastWebdriverFlag = query["wd"] ?? "";
            LastUserAgent = request.UserAgent ?? "";
            var echo = $"{query["ln"]}|{query["fn"]}|{query["st"]}";
            await WriteAsync(response, "application/json", $$"""
                {"echo":"{{echo}}","properties":[
                  {"ownerName":"SAMPLE JANE","holderName":"LOCAL PORTAL HOLDER","propertyValueDescription":"OVER $100","city":"SPRINGFIELD","state":"IL","propertyTypeDescription":"SAVINGS ACCOUNT"},
                  {"ownerName":"SAMPLE ROBERT","holderName":"LOCAL PORTAL HOLDER","propertyValueDescription":"$25.00","city":"DAYTON","state":"OH","propertyTypeDescription":"VENDOR CHECK"}
                ]}
                """);
            return;
        }

        if (path == "/app/redirect-search")
        {
            Interlocked.Increment(ref _pageLoads);
            response.StatusCode = 302;
            response.RedirectLocation = $"http://localhost:{Port}/app/claim-search";
            response.Close();
            return;
        }

        if (path is "/app/claim-search" or "/app/decoy-search" or "/app/silent-search")
        {
            if (!viaLocalhost) Interlocked.Increment(ref _pageLoads);
            await WriteAsync(response, "text/html", Page(path));
            return;
        }

        response.StatusCode = 404;
        response.Close();
    }

    // The form's shape matches the playbook's selectors. The script is the page's own; the driver adds none.
    private string Page(string path)
    {
        var onSubmit = path switch
        {
            "/app/silent-search" => "/* this page asks for a person and never calls the endpoint */",
            "/app/decoy-search" => $"await fetch('http://localhost:{Port}/SWS/properties?' + q).then(r => r.text()); await search(q);",
            _ => "await search(q);",
        };

        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>Local portal</title></head><body>
            <form id="search">
              <input name="lastName"><input name="firstName">
              <select id="state"><option value="">All states</option><option value="IL">Illinois</option><option value="OH">Ohio</option></select>
              <button type="submit">Search</button>
            </form>
            <pre id="out"></pre>
            <script>
              async function search(q) {
                const r = await fetch('/SWS/properties?' + q);
                document.getElementById('out').textContent = await r.text();
              }
              document.getElementById('search').addEventListener('submit', async e => {
                e.preventDefault();
                const f = e.target;
                const q = new URLSearchParams({ ln: f.lastName.value, fn: f.firstName.value, st: f.querySelector('#state').value, wd: String(navigator.webdriver) }).toString();
                {{onSubmit}}
              });
            </script></body></html>
            """;
    }

    private static async Task WriteAsync(HttpListenerResponse response, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentType = contentType + "; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose()
    {
        try { _listener.Stop(); } catch (ObjectDisposedException) { }
        _listener.Close();
    }
}

/// <summary>Takes every pause in full, and writes down that it did.</summary>
internal sealed class TimingPacer : IPacer
{
    public List<TimeSpan> Delays { get; } = [];

    public async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        Delays.Add(delay);
        await Task.Delay(delay, ct);
    }
}

/// <summary>
/// The real session, but a field that is not on the page fails after two seconds instead of thirty.
/// The harness does not need to sit through the driver's full patience to see it give up.
/// </summary>
internal sealed class ShortTimeoutSession(IBrowserSession inner) : IBrowserSession
{
    public string CurrentUrl => inner.CurrentUrl;
    public void StartCapture(string name, string urlContains, int status, IReadOnlyList<string> allowedHosts) => inner.StartCapture(name, urlContains, status, allowedHosts);
    public void ResetCaptures() => inner.ResetCaptures();
    public Task GotoAsync(string url, WaitUntil waitUntil, int timeoutMs, CancellationToken ct) => inner.GotoAsync(url, waitUntil, timeoutMs, ct);
    public Task SelectAsync(string selector, string value, CancellationToken ct) => inner.SelectAsync(selector, value, ct);
    public Task ClickAsync(string selector, CancellationToken ct) => inner.ClickAsync(selector, ct);
    public Task<string?> AwaitCaptureAsync(string name, int timeoutMs, CancellationToken ct) => inner.AwaitCaptureAsync(name, timeoutMs, ct);
    public Task<bool> IsAutomationDeclaredAsync(CancellationToken ct) => inner.IsAutomationDeclaredAsync(ct);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task FillAsync(string selector, string value, CancellationToken ct)
    {
        var fill = inner.FillAsync(selector, value, ct);
        if (await Task.WhenAny(fill, Task.Delay(2000, ct)) != fill) throw new TimeoutException("The field did not appear.");
        await fill;
    }
}
