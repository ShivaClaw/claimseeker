namespace ClaimSeeker.Scout;

public enum RunStatus
{
    /// <summary>The source answered; <see cref="RunOutcome.Json"/> holds what it said.</summary>
    Captured,
    /// <summary>The source would not take the search from automation. A person runs it from the fallback link.</summary>
    Fallback,
    /// <summary>The search was not attempted this time and will be tried at the next sweep.</summary>
    Deferred,
}

public sealed record RunOutcome(RunStatus Status, string? Json, string? Reason, IReadOnlyList<string> StepLog)
{
    public static RunOutcome Captured(string json, IReadOnlyList<string> log) => new(RunStatus.Captured, json, null, log);
    public static RunOutcome Fallback(string reason, IReadOnlyList<string> log) => new(RunStatus.Fallback, null, reason, log);
    public static RunOutcome Deferred(string reason) => new(RunStatus.Deferred, null, reason, []);
}

/// <summary>
/// Runs one search through a playbook. The rules it holds to are the product's, not the playbook's:
///
/// - It opens only addresses on the playbook's allowed hosts.
/// - It never shortens a wait. The pauses are what make the engine a polite visitor.
/// - It checks, once the page is open, that the browser is declaring itself automated, and stops if not.
/// - It makes one attempt. A source that does not answer is not retried; the search is handed to the
///   person with the playbook's fallback link. Nothing here solves, waits out or works around a challenge.
/// </summary>
public sealed class PlaybookRunner(IPacer pacer)
{
    public const string NotDeclaredReason =
        "The browser did not declare itself as automated, so the search was not run. ClaimSeeker does not search with a disguised browser.";

    public const string NoAnswerReason =
        "The source did not answer the search in time. It may be asking for a person.";

    public const string WrongPlaceReason =
        "The source sent the browser to an address outside the playbook's allowed hosts, so nothing was typed there.";

    /// <summary>Nothing is typed or clicked unless the page in front of the engine is the source's own.</summary>
    private static bool IsOnAllowedHost(Playbook playbook, IBrowserSession session) =>
        Uri.TryCreate(session.CurrentUrl, UriKind.Absolute, out var uri) && playbook.IsAllowedHost(uri.Host);

    public async Task<RunOutcome> RunAsync(
        Playbook playbook,
        IBrowserSession session,
        IReadOnlyDictionary<string, string?> inputs,
        CancellationToken ct = default)
    {
        var log = new List<string>();
        var declaredChecked = false;
        session.ResetCaptures();

        for (var i = 0; i < playbook.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = playbook.Steps[i];
            var label = $"{i + 1}:{step.Op.ToString().ToLowerInvariant()}";

            try
            {
                switch (step.Op)
                {
                    case StepOp.Capture:
                        session.StartCapture(step.Name!, step.UrlContains!, step.Status ?? 200, playbook.AllowedHosts);
                        log.Add(label);
                        break;

                    case StepOp.Goto:
                        var url = step.Url == "{entry}" ? playbook.Entry : step.Url!;
                        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !playbook.IsAllowedHost(uri.Host))
                            return RunOutcome.Fallback("The playbook tried to open an address outside its allowed hosts.", log);

                        await session.GotoAsync(url, step.WaitUntil ?? WaitUntil.Load, step.TimeoutMs ?? 60_000, ct);
                        log.Add(label);

                        if (!declaredChecked)
                        {
                            declaredChecked = true;
                            if (!await session.IsAutomationDeclaredAsync(ct))
                                return RunOutcome.Fallback(NotDeclaredReason, log);
                        }

                        break;

                    case StepOp.Wait:
                        await pacer.DelayAsync(TimeSpan.FromMilliseconds(step.Ms!.Value), ct);
                        log.Add($"{label}:{step.Ms}");
                        break;

                    case StepOp.Fill:
                    case StepOp.Select:
                        if (step.When is { } condition && string.IsNullOrEmpty(inputs.GetValueOrDefault(condition)))
                        {
                            log.Add($"{label}:skipped");
                            break;
                        }

                        if (!IsOnAllowedHost(playbook, session)) return RunOutcome.Fallback(WrongPlaceReason, log);
                        var value = PlaybookTemplate.Resolve(step.Value!, inputs);
                        if (step.Op == StepOp.Fill) await session.FillAsync(step.Selector!, value, ct);
                        else await session.SelectAsync(step.Selector!, value, ct);
                        log.Add(label);
                        break;

                    case StepOp.Click:
                        if (!IsOnAllowedHost(playbook, session)) return RunOutcome.Fallback(WrongPlaceReason, log);
                        await session.ClickAsync(step.Selector!, ct);
                        log.Add(label);
                        break;

                    case StepOp.Await:
                        var body = await session.AwaitCaptureAsync(step.Capture!, step.TimeoutMs!.Value, ct);
                        if (body is null) return RunOutcome.Fallback(NoAnswerReason, log);
                        log.Add(label);
                        if (step.Capture == playbook.Result.Capture) return RunOutcome.Captured(body, log);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One attempt only. The step that failed is named; the page's own words are not repeated.
                return RunOutcome.Fallback($"Step {i + 1} ({step.Op.ToString().ToLowerInvariant()}) could not be completed ({ex.GetType().Name}).", log);
            }
        }

        return RunOutcome.Fallback("The playbook finished without a result.", log);
    }
}

/// <summary>Runs searches. The engine depends on this, not on a browser.</summary>
public interface IQueryExecutor
{
    Task<RunOutcome> ExecuteAsync(Playbook playbook, SourceQuery query, CancellationToken ct = default);
}

/// <summary>
/// Runs a session's searches through one browser, one at a time. It holds the pacing rules that
/// belong to the engine rather than to any playbook: the pause between searches is never cut short,
/// a session never runs more searches than its playbook allows, and once a source has turned one
/// search away, the rest of the session's searches are not sent.
/// </summary>
public sealed class BrowserQueryExecutor(IBrowserSession session, IPacer pacer) : IQueryExecutor
{
    private readonly PlaybookRunner _runner = new(pacer);
    private readonly Dictionary<string, int> _runsBySource = new(StringComparer.Ordinal);
    private readonly HashSet<string> _blockedSources = new(StringComparer.Ordinal);

    public async Task<RunOutcome> ExecuteAsync(Playbook playbook, SourceQuery query, CancellationToken ct = default)
    {
        if (_blockedSources.Contains(playbook.Source))
            return RunOutcome.Deferred("An earlier search in this session was turned away by the source, so this one was not sent.");

        var runs = _runsBySource.GetValueOrDefault(playbook.Source);
        if (runs >= playbook.Pacing.MaxQueriesPerSession)
            return RunOutcome.Deferred("This session has reached the source's search limit. The search will run at the next sweep.");

        if (runs > 0)
            await pacer.DelayAsync(TimeSpan.FromMilliseconds(playbook.Pacing.MinMsBetweenQueries), ct);

        _runsBySource[playbook.Source] = runs + 1;
        var outcome = await _runner.RunAsync(playbook, session, query.Inputs(), ct);
        if (outcome.Status == RunStatus.Fallback) _blockedSources.Add(playbook.Source);
        return outcome;
    }
}

/// <summary>
/// Answers searches from files instead of the network: <c>&lt;directory&gt;/&lt;source&gt;/&lt;FAMILY&gt;.json</c>,
/// or <c>&lt;FAMILY&gt;__&lt;GIVEN&gt;.json</c> for a narrowed search. A file named <c>&lt;FAMILY&gt;.blocked</c> stands for a
/// source that turned the search away. Used by the harnesses and by an offline dry run; it never
/// touches a browser or a socket.
/// </summary>
public sealed class FixtureQueryExecutor(string directory) : IQueryExecutor
{
    public const int MaxBytes = 8 * 1024 * 1024;

    public Task<RunOutcome> ExecuteAsync(Playbook playbook, SourceQuery query, CancellationToken ct = default)
    {
        var stem = query.GivenName is null ? query.FamilyName : $"{query.FamilyName}__{query.GivenName}";
        stem = stem.Replace(' ', '_');
        if (!IsSafeStem(stem) || !IsSafeStem(playbook.Source))
            return Task.FromResult(RunOutcome.Fallback("The search name cannot be used as a fixture file name.", []));

        var folder = Path.Combine(directory, playbook.Source);
        if (File.Exists(Path.Combine(folder, stem + ".blocked")))
            return Task.FromResult(RunOutcome.Fallback(PlaybookRunner.NoAnswerReason, ["fixture:blocked"]));

        var path = Path.Combine(folder, stem + ".json");
        if (!File.Exists(path))
            return Task.FromResult(RunOutcome.Fallback("No fixture is recorded for this search.", ["fixture:missing"]));
        if (new FileInfo(path).Length > MaxBytes)
            return Task.FromResult(RunOutcome.Fallback("The fixture is too large to read.", ["fixture:too-large"]));

        return Task.FromResult(RunOutcome.Captured(File.ReadAllText(path), ["fixture:" + stem]));
    }

    private static bool IsSafeStem(string stem) =>
        stem.Length is > 0 and <= 120 && stem.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
}
