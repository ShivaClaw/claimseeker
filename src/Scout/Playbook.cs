using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimSeeker.Core;

namespace ClaimSeeker.Scout;

/// <summary>
/// Everything a playbook step can do. The list is closed on purpose. There is no step that runs
/// script in the page, sets a header, or changes how the browser describes itself, so a playbook
/// cannot be used to disguise the engine or to make it do anything but fill in a search form and
/// read the answer. docs/Playbook-Format.md is the normative description.
/// </summary>
public enum StepOp { Capture, Goto, Wait, Fill, Select, Click, Await }

public enum WaitUntil { Load, DomContentLoaded, NetworkIdle }

public sealed record PlaybookStep
{
    public required StepOp Op { get; init; }

    /// <summary>capture: the name later steps refer to.</summary>
    public string? Name { get; init; }
    /// <summary>capture: the path fragment a response URL must contain.</summary>
    public string? UrlContains { get; init; }
    /// <summary>capture: the HTTP status to accept. Defaults to 200.</summary>
    public int? Status { get; init; }

    /// <summary>goto: "{entry}", or a literal address on an allowed host.</summary>
    public string? Url { get; init; }
    public WaitUntil? WaitUntil { get; init; }

    /// <summary>goto and await: how long to wait before giving up, in milliseconds.</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>wait: how long to pause, in milliseconds. The runner never shortens it.</summary>
    public int? Ms { get; init; }

    /// <summary>fill, select, click: a CSS selector.</summary>
    public string? Selector { get; init; }
    /// <summary>fill, select: the text to enter; may refer to inputs as {familyName}, {givenName}, {state}.</summary>
    public string? Value { get; init; }
    /// <summary>fill, select: run this step only when the named input has a value.</summary>
    public string? When { get; init; }

    /// <summary>await: the capture to wait for.</summary>
    public string? Capture { get; init; }

    /// <summary>Why the step is there. Read by people; ignored by the runner.</summary>
    public string? Why { get; init; }
}

public sealed record PlaybookResult
{
    public required string Capture { get; init; }
    /// <summary>Dotted path from the top of the captured JSON to the list of records.</summary>
    public required string Items { get; init; }
    /// <summary>Record field name to the source's property name.</summary>
    public required IReadOnlyDictionary<string, string> Fields { get; init; }
    /// <summary>When the source caps a result list at a known size, that size. A list this long is treated as cut off.</summary>
    public int? MaxItemsHint { get; init; }
}

public sealed record PlaybookPacing
{
    /// <summary>The least time between two searches in one session.</summary>
    public required int MinMsBetweenQueries { get; init; }
    public required int MaxQueriesPerSession { get; init; }
}

public sealed record PlaybookFallback
{
    /// <summary>Where the person runs the search themselves when the source will not take automation.</summary>
    public required string DeepLink { get; init; }
    public required string Instructions { get; init; }
}

/// <summary>
/// How to search one source: the page, the fields, the response to read, the pacing, and what to hand
/// the person when the source asks for one. A playbook is data, read from a markdown file whose first
/// fenced json block is this record and whose prose records the failure modes that were found the
/// hard way.
/// </summary>
public sealed record Playbook
{
    public required int Format { get; init; }
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required string Source { get; init; }
    public required InteractionMode Mode { get; init; }
    public required IReadOnlyList<string> AllowedHosts { get; init; }
    public required string Entry { get; init; }
    public required IReadOnlyList<PlaybookStep> Steps { get; init; }
    public required PlaybookResult Result { get; init; }
    public required PlaybookPacing Pacing { get; init; }
    public required PlaybookFallback Fallback { get; init; }

    /// <summary>Where the recipe came from and when it last worked.</summary>
    public string? Provenance { get; init; }

    public bool IsAllowedHost(string host) =>
        AllowedHosts.Any(allowed => string.Equals(allowed, host, StringComparison.OrdinalIgnoreCase));
}

public sealed class PlaybookFormatException(string message) : Exception(message);

public static class PlaybookParser
{
    public const int MaxBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>
    /// Reads a playbook from its markdown. The header is the first fenced block marked json. A header
    /// with a property this build does not know is refused, so a newer playbook cannot be half-read
    /// by an older engine. The result is validated before it is returned.
    /// </summary>
    public static Playbook Parse(string markdown)
    {
        if (markdown.Length > MaxBytes) throw new PlaybookFormatException("The playbook file is too large.");

        var header = ExtractHeader(markdown) ?? throw new PlaybookFormatException("The playbook has no fenced json header.");
        Playbook playbook;
        try
        {
            playbook = JsonSerializer.Deserialize<Playbook>(header, Options)
                       ?? throw new PlaybookFormatException("The playbook header is empty.");
        }
        catch (JsonException ex)
        {
            throw new PlaybookFormatException($"The playbook header could not be read: {ex.Message}");
        }

        var errors = PlaybookValidator.Validate(playbook);
        if (errors.Count > 0)
            throw new PlaybookFormatException($"The playbook \"{playbook.Id}\" is not valid: {string.Join(" ", errors)}");
        return playbook;
    }

    /// <summary>The text of the first fenced json block, or null when the file has none.</summary>
    public static string? ExtractHeader(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (start < 0)
            {
                if (line.Equals("```json", StringComparison.OrdinalIgnoreCase)) start = i + 1;
            }
            else if (line == "```")
            {
                return string.Join('\n', lines[start..i]);
            }
        }

        return null;
    }
}

public static class PlaybookValidator
{
    /// <summary>The inputs a step may refer to.</summary>
    public static readonly IReadOnlySet<string> KnownInputs =
        new HashSet<string>(StringComparer.Ordinal) { "familyName", "givenName", "state" };

    /// <summary>The record fields a result mapping may fill.</summary>
    public static readonly IReadOnlySet<string> KnownFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "sourceRecordId", "ownerName", "holderName", "city", "state", "valueText", "propertyTypeText",
    };

    private const int MaxSteps = 40;
    private const int MaxWaitMs = 120_000;

    public static IReadOnlyList<string> Validate(Playbook p)
    {
        var errors = new List<string>();

        if (p.Format != 1) errors.Add($"Format {p.Format} is not supported; this build reads format 1.");
        if (!IsSlug(p.Id)) errors.Add("The id must be lower-case letters, digits and hyphens.");
        if (!IsSlug(p.Source)) errors.Add("The source must be lower-case letters, digits and hyphens.");
        if (p.Version < 1) errors.Add("The version must be 1 or more.");
        if (p.Mode is not (InteractionMode.Auto or InteractionMode.Assisted))
            errors.Add("Only auto and assisted sources have steps to run.");

        if (p.AllowedHosts.Count == 0) errors.Add("No allowed hosts are listed.");
        foreach (var host in p.AllowedHosts)
            if (!IsHostName(host)) errors.Add($"\"{host}\" is not a plain host name.");

        var loopbackOnly = p.AllowedHosts.Count > 0 && p.AllowedHosts.All(IsLoopback);
        CheckUrl(p, p.Entry, "The entry address", errors);
        CheckUrl(p, p.Fallback.DeepLink, "The fallback link", errors);
        if (string.IsNullOrWhiteSpace(p.Fallback.Instructions)) errors.Add("The fallback has no instructions for the person.");

        if (p.Pacing.MaxQueriesPerSession is < 1 or > 50) errors.Add("maxQueriesPerSession must be between 1 and 50.");
        if (p.Pacing.MinMsBetweenQueries < 0 || p.Pacing.MinMsBetweenQueries > 3_600_000) errors.Add("minMsBetweenQueries is out of range.");
        if (!loopbackOnly && p.Pacing.MinMsBetweenQueries < 1000)
            errors.Add("A real source needs at least one second between searches.");

        ValidateSteps(p, errors);
        ValidateResult(p, errors);
        return errors;
    }

    private static void ValidateSteps(Playbook p, List<string> errors)
    {
        if (p.Steps.Count == 0) { errors.Add("The playbook has no steps."); return; }
        if (p.Steps.Count > MaxSteps) errors.Add($"The playbook has more than {MaxSteps} steps.");

        var captures = new HashSet<string>(StringComparer.Ordinal);
        var sawGoto = false;
        var sawAwait = false;
        var editedSinceWait = false;

        for (var i = 0; i < p.Steps.Count; i++)
        {
            var step = p.Steps[i];
            var label = $"Step {i + 1} ({step.Op.ToString().ToLowerInvariant()})";

            switch (step.Op)
            {
                case StepOp.Capture:
                    if (!IsSlug(step.Name)) errors.Add($"{label}: a capture needs a name.");
                    else if (!captures.Add(step.Name!)) errors.Add($"{label}: the capture name \"{step.Name}\" is used twice.");
                    if (string.IsNullOrWhiteSpace(step.UrlContains) || !step.UrlContains.StartsWith('/'))
                        errors.Add($"{label}: urlContains must be a path fragment starting with \"/\".");
                    if (step.Status is { } status && status is < 100 or > 599) errors.Add($"{label}: the status is not an HTTP status.");
                    // The proven rule: the listener is registered before the page is opened, or the response is missed.
                    if (sawGoto) errors.Add($"{label}: captures must be declared before the first goto.");
                    break;

                case StepOp.Goto:
                    sawGoto = true;
                    if (step.Url is null) errors.Add($"{label}: a goto needs a url.");
                    else if (step.Url != "{entry}") CheckUrl(p, step.Url, label + ": the url", errors);
                    if (step.TimeoutMs is { } gotoTimeout && gotoTimeout is < 1000 or > MaxWaitMs)
                        errors.Add($"{label}: timeoutMs must be between 1000 and {MaxWaitMs}.");
                    break;

                case StepOp.Wait:
                    if (step.Ms is not { } ms || ms < 1 || ms > MaxWaitMs) errors.Add($"{label}: ms must be between 1 and {MaxWaitMs}.");
                    editedSinceWait = false;
                    break;

                case StepOp.Fill:
                case StepOp.Select:
                    CheckSelector(step, label, errors);
                    if (step.Value is null) errors.Add($"{label}: a value is needed.");
                    else foreach (var name in PlaybookTemplate.Placeholders(step.Value))
                        if (!KnownInputs.Contains(name)) errors.Add($"{label}: \"{{{name}}}\" is not an input this build provides.");
                    if (step.When is { } condition && !KnownInputs.Contains(condition))
                        errors.Add($"{label}: \"{condition}\" is not an input this build provides.");
                    editedSinceWait = true;
                    break;

                case StepOp.Click:
                    CheckSelector(step, label, errors);
                    // The other proven rule: a form submitted the instant it is filled is rate-limited.
                    if (editedSinceWait) errors.Add($"{label}: a wait step must sit between the last field edit and a click.");
                    break;

                case StepOp.Await:
                    sawAwait = true;
                    if (step.Capture is null || !captures.Contains(step.Capture)) errors.Add($"{label}: it waits for a capture that was not declared.");
                    if (step.TimeoutMs is not { } awaitTimeout || awaitTimeout < 1000 || awaitTimeout > MaxWaitMs)
                        errors.Add($"{label}: timeoutMs must be between 1000 and {MaxWaitMs}.");
                    break;
            }
        }

        if (!sawGoto) errors.Add("The playbook never opens a page.");
        if (!sawAwait) errors.Add("The playbook never waits for a result.");
        if (!captures.Contains(p.Result.Capture)) errors.Add("The result refers to a capture that was not declared.");
    }

    private static void ValidateResult(Playbook p, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(p.Result.Items)) errors.Add("The result has no items path.");
        if (!p.Result.Fields.ContainsKey("ownerName")) errors.Add("The result must map ownerName.");
        foreach (var (field, property) in p.Result.Fields)
        {
            if (!KnownFields.Contains(field)) errors.Add($"The result maps an unknown field \"{field}\".");
            if (string.IsNullOrWhiteSpace(property)) errors.Add($"The result field \"{field}\" has no source property.");
        }

        if (p.Result.MaxItemsHint is { } hint && hint < 1) errors.Add("maxItemsHint must be 1 or more.");
    }

    private static void CheckSelector(PlaybookStep step, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(step.Selector) || step.Selector.Length > 300) errors.Add($"{label}: a selector is needed.");
    }

    /// <summary>An address is acceptable when it is https on an allowed host, or http on a loopback one.</summary>
    private static void CheckUrl(Playbook p, string? url, string what, List<string> errors)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) { errors.Add($"{what} is not an absolute address."); return; }
        if (!p.IsAllowedHost(uri.Host)) { errors.Add($"{what} is not on an allowed host."); return; }
        var loopback = IsLoopback(uri.Host);
        if (uri.Scheme != Uri.UriSchemeHttps && !(loopback && uri.Scheme == Uri.UriSchemeHttp))
            errors.Add($"{what} must use https.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) errors.Add($"{what} must not carry credentials.");
    }

    public static bool IsLoopback(string host) =>
        host is "127.0.0.1" or "localhost" or "[::1]" or "::1";

    private static bool IsSlug(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 48 && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static bool IsHostName(string? host) =>
        !string.IsNullOrEmpty(host) && host.Length <= 253 && !host.StartsWith('.') && !host.EndsWith('.')
        && host.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.');
}

public static class PlaybookTemplate
{
    /// <summary>The input names a template refers to, as {name}.</summary>
    public static IReadOnlyList<string> Placeholders(string template)
    {
        var names = new List<string>();
        var i = 0;
        while (i < template.Length)
        {
            var open = template.IndexOf('{', i);
            if (open < 0) break;
            var close = template.IndexOf('}', open + 1);
            if (close < 0) break;
            names.Add(template[(open + 1)..close]);
            i = close + 1;
        }

        return names;
    }

    /// <summary>Fills a template from the inputs. A missing input becomes an empty string.</summary>
    public static string Resolve(string template, IReadOnlyDictionary<string, string?> inputs)
    {
        var result = template;
        foreach (var name in Placeholders(template))
            result = result.Replace("{" + name + "}", inputs.TryGetValue(name, out var value) ? value ?? "" : "", StringComparison.Ordinal);
        return result;
    }
}
