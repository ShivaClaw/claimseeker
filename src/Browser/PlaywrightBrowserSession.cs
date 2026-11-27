using System.Collections.Concurrent;
using ClaimSeeker.Scout;
using Microsoft.Playwright;

namespace ClaimSeeker.Browser;

public sealed record BrowserOptions
{
    /// <summary>
    /// False shows the window, which is the default: the person can see what the engine is doing in
    /// their name. True is for harnesses on machines with no display.
    /// </summary>
    public bool Headless { get; init; }

    /// <summary>A browser already on the machine, by Playwright channel name: "msedge" or "chrome".</summary>
    public string? Channel { get; init; }

    /// <summary>A browser already on the machine, by path.</summary>
    public string? ExecutablePath { get; init; }
}

public sealed class BrowserUnavailableException(string message) : Exception(message);

/// <summary>
/// A real browser behind <see cref="IBrowserSession"/>.
///
/// What this class does not do is the point of it. It sets no user agent, adds no header, injects no
/// script and passes no flag that changes what a page can learn about the browser. The browser's own
/// automation signal is left on, and <see cref="IsAutomationDeclaredAsync"/> reads it back so the
/// runner can refuse to search if it is ever off. A source that does not want automation sees
/// automation, and can say no; the engine then hands the search to the person.
///
/// PortalHarness reads this project's source for the calls that would break that, and
/// scripts/Verify.ps1 runs it on every change, so a change that adds one fails verification instead
/// of reading as an innocent line in a diff.
/// </summary>
public sealed class PlaywrightBrowserSession : IBrowserSession
{
    private const int ActionTimeoutMs = 30_000;
    private const long MaxBodyBytes = 8L * 1024 * 1024;

    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly IPage _page;
    private readonly ConcurrentDictionary<string, CaptureRule> _captures = new(StringComparer.Ordinal);

    private PlaywrightBrowserSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page, string description)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        _page = page;
        Description = description;
        _page.SetDefaultTimeout(ActionTimeoutMs);
        _page.Response += OnResponse;
    }

    /// <summary>Which browser was started, for the log.</summary>
    public string Description { get; }

    public string CurrentUrl => _page.Url;

    /// <summary>
    /// Starts a browser. An explicit path or channel is used if given. Otherwise the browsers already
    /// on the machine are tried first, Edge on Windows and then Chrome, and a Playwright-managed
    /// Chromium last, so a normal install downloads nothing.
    /// </summary>
    public static async Task<PlaywrightBrowserSession> LaunchAsync(BrowserOptions? options = null)
    {
        options ??= new BrowserOptions();
        var playwright = await Playwright.CreateAsync();

        var attempts = new List<(string Label, BrowserTypeLaunchOptions Launch)>();
        if (options.ExecutablePath is { Length: > 0 } path)
            attempts.Add(($"the browser at {path}", new BrowserTypeLaunchOptions { Headless = options.Headless, ExecutablePath = path }));
        else if (options.Channel is { Length: > 0 } channel)
            attempts.Add((channel, new BrowserTypeLaunchOptions { Headless = options.Headless, Channel = channel }));
        else
        {
            if (OperatingSystem.IsWindows())
                attempts.Add(("msedge", new BrowserTypeLaunchOptions { Headless = options.Headless, Channel = "msedge" }));
            attempts.Add(("chrome", new BrowserTypeLaunchOptions { Headless = options.Headless, Channel = "chrome" }));
            attempts.Add(("Playwright's Chromium", new BrowserTypeLaunchOptions { Headless = options.Headless }));
        }

        var failures = new List<string>();
        foreach (var (label, launch) in attempts)
        {
            try
            {
                var browser = await playwright.Chromium.LaunchAsync(launch);
                var context = await browser.NewContextAsync();
                var page = await context.NewPageAsync();
                return new PlaywrightBrowserSession(playwright, browser, context, page, $"{label} {browser.Version}");
            }
            catch (PlaywrightException ex)
            {
                failures.Add($"{label}: {FirstLine(ex.Message)}");
            }
        }

        playwright.Dispose();
        throw new BrowserUnavailableException(
            "No browser could be started. ClaimSeeker uses Microsoft Edge or Google Chrome when one is installed. Tried: " + string.Join("; ", failures));
    }

    public void StartCapture(string name, string urlContains, int status, IReadOnlyList<string> allowedHosts) =>
        _captures[name] = new CaptureRule(urlContains, status, allowedHosts);

    public void ResetCaptures() => _captures.Clear();

    public async Task GotoAsync(string url, WaitUntil waitUntil, int timeoutMs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _page.GotoAsync(url, new PageGotoOptions
        {
            Timeout = timeoutMs,
            WaitUntil = waitUntil switch
            {
                WaitUntil.NetworkIdle => WaitUntilState.NetworkIdle,
                WaitUntil.DomContentLoaded => WaitUntilState.DOMContentLoaded,
                _ => WaitUntilState.Load,
            },
        });
    }

    public async Task FillAsync(string selector, string value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _page.FillAsync(selector, value);
    }

    public async Task SelectAsync(string selector, string value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _page.SelectOptionAsync(selector, value);
    }

    public async Task ClickAsync(string selector, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _page.ClickAsync(selector);
    }

    public async Task<string?> AwaitCaptureAsync(string name, int timeoutMs, CancellationToken ct)
    {
        if (!_captures.TryGetValue(name, out var rule)) return null;
        var finished = await Task.WhenAny(rule.Body.Task, Task.Delay(timeoutMs, ct));
        ct.ThrowIfCancellationRequested();
        return finished == rule.Body.Task ? await rule.Body.Task : null;
    }

    /// <summary>
    /// Reads the browser's own automation signal. This is the only script the session ever runs in
    /// a page, it is fixed, and it only reads.
    /// </summary>
    public async Task<bool> IsAutomationDeclaredAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _page.EvaluateAsync<bool>("() => navigator.webdriver === true");
    }

    private async void OnResponse(object? sender, IResponse response)
    {
        try
        {
            foreach (var rule in _captures.Values)
            {
                if (rule.Body.Task.IsCompleted) continue;
                if (response.Status != rule.Status) continue;
                if (!Uri.TryCreate(response.Url, UriKind.Absolute, out var uri)) continue;
                if (!uri.PathAndQuery.Contains(rule.UrlContains, StringComparison.Ordinal)) continue;
                // An answer is read only from the source itself, never from another host the page happened to call.
                if (!rule.AllowedHosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase))) continue;

                var headers = await response.AllHeadersAsync();
                if (headers.TryGetValue("content-length", out var length)
                    && long.TryParse(length, out var bytes) && bytes > MaxBodyBytes) continue;

                rule.Body.TrySetResult(await response.TextAsync());
            }
        }
        catch (Exception)
        {
            // A response that cannot be read is the same as one that never came: the await times out
            // and the search goes to the person. An event handler has no caller to throw to.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _page.Response -= OnResponse;
        try { await _context.CloseAsync(); } catch (PlaywrightException) { }
        try { await _browser.CloseAsync(); } catch (PlaywrightException) { }
        _playwright.Dispose();
    }

    private static string FirstLine(string message)
    {
        var line = message.Split('\n', 2)[0].Trim();
        return line.Length > 160 ? line[..160] : line;
    }

    private sealed record CaptureRule(string UrlContains, int Status, IReadOnlyList<string> AllowedHosts)
    {
        public TaskCompletionSource<string> Body { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
