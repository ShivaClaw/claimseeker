using System.Globalization;
using System.Text.Json.Nodes;
using ClaimSeeker.Core;

namespace ClaimSeeker.Store;

/// <summary>The rules both stores apply the same way, kept in one place so they cannot drift apart.</summary>
internal static class StoreRules
{
    public static readonly IReadOnlySet<string> CaseStates =
        new HashSet<string>(Enum.GetNames<CaseState>(), StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> Confidences =
        new HashSet<string>(Enum.GetNames<Confidence>(), StringComparer.Ordinal);

    public const string InitialState = nameof(CaseState.DISCOVERED);

    public static void RequireCaseState(string state)
    {
        if (!CaseStates.Contains(state)) throw new ArgumentException($"\"{state}\" is not a case state.", nameof(state));
    }

    public static void RequireFinding(FindingUpsert finding)
    {
        if (string.IsNullOrWhiteSpace(finding.Id)) throw new ArgumentException("A finding needs an id.", nameof(finding));
        if (!Confidences.Contains(finding.Confidence))
            throw new ArgumentException($"\"{finding.Confidence}\" is not a confidence level.", nameof(finding));
    }

    /// <summary>Money is kept to the cent.</summary>
    public static decimal? Cents(decimal? value) =>
        value is { } v ? decimal.Round(v, 2, MidpointRounding.AwayFromZero) : null;

    public static long? ToCents(decimal? value) =>
        value is { } v ? (long)decimal.Round(v * 100m, 0, MidpointRounding.AwayFromZero) : null;

    public static decimal? FromCents(long? cents) => cents is { } c ? c / 100m : null;

    public static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static decimal? Number(string? text) =>
        text is null ? null : decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    public static string DiscoveredPayload(FindingUpsert finding) => new JsonObject
    {
        ["source"] = finding.SourceId,
        ["confidence"] = finding.Confidence,
    }.ToJsonString();

    public static string TransitionPayload(string from, string to, string detailJson)
    {
        JsonNode? detail;
        try { detail = JsonNode.Parse(string.IsNullOrWhiteSpace(detailJson) ? "{}" : detailJson); }
        catch (System.Text.Json.JsonException) { throw new ArgumentException("The transition detail is not JSON.", nameof(detailJson)); }

        return new JsonObject { ["from"] = from, ["to"] = to, ["detail"] = detail }.ToJsonString();
    }

    public static string HouseholdPayload(int subjectCount) =>
        new JsonObject { ["subjects"] = subjectCount }.ToJsonString();
}
