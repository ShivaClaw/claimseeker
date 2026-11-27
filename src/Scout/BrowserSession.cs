namespace ClaimSeeker.Scout;

/// <summary>
/// The whole of what the engine can do in a browser. A playbook's steps map onto these calls one for
/// one, and there is nothing else: no script injection, no header or user-agent override, no way to
/// alter what the page can learn about the browser. Implementations must leave the browser's own
/// automation signal switched on; <see cref="IsAutomationDeclaredAsync"/> reports whether it is, and
/// the runner refuses to search through a session that hides it.
/// </summary>
public interface IBrowserSession : IAsyncDisposable
{
    /// <summary>
    /// Starts keeping the body of any response whose address contains <paramref name="urlContains"/>,
    /// has the given status, and comes from one of <paramref name="allowedHosts"/>. Called before the
    /// page is opened, so the response cannot be missed.
    /// </summary>
    void StartCapture(string name, string urlContains, int status, IReadOnlyList<string> allowedHosts);

    /// <summary>Forgets every capture, ready for the next search in the same session.</summary>
    void ResetCaptures();

    /// <summary>
    /// The address of the page the session is on. The runner reads it before it touches the page, so
    /// a source that redirects somewhere else is noticed before anything is typed there.
    /// </summary>
    string CurrentUrl { get; }

    Task GotoAsync(string url, WaitUntil waitUntil, int timeoutMs, CancellationToken ct);
    Task FillAsync(string selector, string value, CancellationToken ct);
    Task SelectAsync(string selector, string value, CancellationToken ct);
    Task ClickAsync(string selector, CancellationToken ct);

    /// <summary>The captured body, or null when nothing matching arrived in time.</summary>
    Task<string?> AwaitCaptureAsync(string name, int timeoutMs, CancellationToken ct);

    /// <summary>True when the page can see that the browser is automated.</summary>
    Task<bool> IsAutomationDeclaredAsync(CancellationToken ct);
}

/// <summary>Waits. A seam so that harnesses can run a playbook's pauses without sitting through them.</summary>
public interface IPacer
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

public sealed class RealPacer : IPacer
{
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, ct);
}
