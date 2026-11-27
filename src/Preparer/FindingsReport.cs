using System.Globalization;
using System.Text;
using ClaimSeeker.Core;
using ClaimSeeker.Scout;
using ClaimSeeker.Store;

namespace ClaimSeeker.Preparer;

/// <summary>A search the source would not take from automation, handed to the person.</summary>
public sealed record FallbackItem(string SourceName, string Searched, string Reason, string Link, string Instructions, string RanAt);

public sealed record ReportInput(
    Household Household,
    IReadOnlyList<FindingRow> RankedFindings,
    IReadOnlyList<FallbackItem> Fallbacks,
    DateOnly Today);

/// <summary>
/// What the totals at the top of a report are made of. Only matches the engine trusts are counted,
/// only while their case is open, and only for the amount a source actually stated.
/// </summary>
public sealed record ReportTotals(int Trusted, decimal StatedFloor, int Undisclosed, int Possible, int Paid, decimal PaidFloor);

/// <summary>
/// The report a person reads after a sweep. The first line is a total, and the total is honest: it is
/// the sum of what sources stated, for matches the engine trusts. It is never an estimate, and a
/// possible match never adds to it. Apps in this market are faulted for showing a large number that
/// turns into a small one; this report can only err the other way.
/// </summary>
public static class FindingsReport
{
    private static readonly HashSet<string> Closed = new(StringComparer.Ordinal)
    {
        nameof(CaseState.PAID), nameof(CaseState.DENIED), nameof(CaseState.DISMISSED), nameof(CaseState.EXPIRED),
    };

    public static bool IsOpen(FindingRow finding) => !Closed.Contains(finding.CaseState);

    public static bool IsTrusted(FindingRow finding) =>
        Enum.TryParse<Confidence>(finding.Confidence, out var confidence) && confidence >= Confidence.Likely;

    public static ReportTotals Totals(IReadOnlyList<FindingRow> findings)
    {
        // A property seen through two sources is one property. Only the first sighting is counted.
        var unique = findings.Where(f => f.DuplicateOf is null).ToList();
        var trusted = unique.Where(f => IsOpen(f) && IsTrusted(f)).ToList();
        var paid = unique.Where(f => f.CaseState == nameof(CaseState.PAID)).ToList();

        return new ReportTotals(
            Trusted: trusted.Count,
            StatedFloor: trusted.Sum(f => Value(f).Floor),
            Undisclosed: trusted.Count(f => !Value(f).Disclosed),
            Possible: unique.Count(f => IsOpen(f) && !IsTrusted(f)),
            Paid: paid.Count,
            PaidFloor: paid.Sum(f => Value(f).Floor));
    }

    public static string Headline(ReportTotals totals)
    {
        var sb = new StringBuilder();
        if (totals.Trusted == 0)
            sb.Append("**No matches the engine trusts yet.**");
        else if (totals.StatedFloor > 0m)
            sb.Append("**At least ").Append(MoneyRange.Dollars(totals.StatedFloor)).Append(" across ").Append(Count(totals.Trusted, "match", "matches")).Append(" the engine trusts.**");
        else
            sb.Append("**").Append(Count(totals.Trusted, "match", "matches")).Append(" the engine trusts, none with a stated amount.**");

        if (totals.Trusted > 0 && totals.Undisclosed > 0 && totals.StatedFloor > 0m)
            sb.Append(' ').Append(totals.Undisclosed).Append(totals.Undisclosed == 1 ? " of them does" : " of them do")
              .Append(" not state an amount, so ").Append(totals.Undisclosed == 1 ? "it adds" : "they add").Append(" nothing to that figure.");

        if (totals.Possible > 0)
            sb.Append(' ').Append(Count(totals.Possible, "possible match is", "possible matches are")).Append(" listed further down and not counted.");

        if (totals.Paid > 0)
            sb.Append(' ').Append(Count(totals.Paid, "claim", "claims")).Append(" marked paid so far")
              .Append(totals.PaidFloor > 0m ? $", worth at least {MoneyRange.Dollars(totals.PaidFloor)}." : ".");

        return sb.ToString();
    }

    public static string Markdown(ReportInput input)
    {
        var findings = input.RankedFindings.Where(f => f.DuplicateOf is null).ToList();
        var trusted = findings.Where(f => IsOpen(f) && IsTrusted(f)).ToList();
        var possible = findings.Where(f => IsOpen(f) && !IsTrusted(f)).ToList();
        var totals = Totals(input.RankedFindings);

        var md = new StringBuilder();
        md.Append("# ClaimSeeker report, ").Append(input.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\n\n");
        md.Append(Headline(totals)).Append("\n\n");

        if (trusted.Count > 0)
        {
            md.Append("## Start here\n\n");
            md.Append("In order of what each is likely worth for the time it takes. Each has a claim packet with the steps.\n\n");
            var n = 1;
            foreach (var finding in trusted) md.Append(n++).Append(". ").Append(Line(finding, input.Household)).Append('\n');
            md.Append('\n');

            md.Append("## By state\n\n");
            foreach (var group in trusted.GroupBy(f => string.IsNullOrEmpty(f.UsState) ? "State not given" : f.UsState!).OrderBy(g => g.Key, StringComparer.Ordinal))
                md.Append("- ").Append(ClaimPacketBuilder.U(group.Key)).Append(": ").Append(GroupSummary(group.ToList())).Append('\n');
            md.Append('\n');

            md.Append("## By person\n\n");
            foreach (var group in trusted.GroupBy(f => f.SubjectId))
                md.Append("- ").Append(ClaimPacketBuilder.Md(PersonLabel(group.Key, input.Household))).Append(": ").Append(GroupSummary(group.ToList())).Append('\n');
            md.Append('\n');
        }

        if (possible.Count > 0)
        {
            md.Append("## Possible matches\n\n");
            md.Append("The name could be right, but nothing ties these to somewhere the person lived. They are leads to check, and no amount from them is counted above.\n\n");
            foreach (var finding in possible) md.Append("- ").Append(Line(finding, input.Household)).Append('\n');
            md.Append('\n');
        }

        var gone = findings.Where(f => f.MissingSince is not null && IsOpen(f)).ToList();
        if (gone.Count > 0)
        {
            md.Append("## No longer listed\n\n");
            md.Append("A record that leaves a source's list has usually been paid or withdrawn.\n\n");
            foreach (var finding in gone)
                md.Append("- ").Append(ClaimPacketBuilder.U(finding.OwnerName)).Append(", `").Append(finding.Id).Append("`: not listed since ")
                  .Append(ClaimPacketBuilder.DateOnlyText(finding.MissingSince!)).Append(".\n");
            md.Append('\n');
        }

        if (input.Fallbacks.Count > 0)
        {
            md.Append("## Searches that need you\n\n");
            md.Append("These sources did not take the search from ClaimSeeker. It does not try again or work around that; the search is yours to run.\n\n");
            foreach (var item in input.Fallbacks)
            {
                md.Append("- **").Append(item.SourceName).Append("**, search for ").Append(ClaimPacketBuilder.Md(item.Searched)).Append(". ")
                  .Append(item.Reason).Append('\n');
                md.Append("  ").Append(item.Link).Append('\n');
                md.Append("  ").Append(item.Instructions).Append('\n');
            }

            md.Append('\n');
        }

        md.Append("## Other places to look\n\n");
        md.Append("ClaimSeeker does not search these yet. Each is an official source you can check yourself.\n\n");
        foreach (var source in SourceRegistry.All)
        {
            if (SourceRegistry.EffectiveMode(source) is not (InteractionMode.Guided or InteractionMode.Draft)) continue;
            md.Append("- **").Append(source.Name).Append("** (").Append(source.Operator).Append("). ").Append(source.What);
            if (source.DeadlineNote is { } deadline) md.Append(' ').Append(deadline);
            if (source.EntryUrl is { } entry) md.Append("\n  ").Append(entry);
            else if (source.OfficialHosts.Count > 0) md.Append("\n  On ").Append(string.Join(", ", source.OfficialHosts)).Append('.');
            md.Append('\n');
        }

        md.Append("\n---\n\n").Append(ClaimPacketBuilder.FreeToFile).Append('\n');
        md.Append("\nTotals count only amounts a source stated, for matches the engine trusts. \"At least\" means exactly that.\n");
        return md.ToString();
    }

    private static string Line(FindingRow finding, Household household)
    {
        var value = Value(finding);
        var confidence = Enum.TryParse<Confidence>(finding.Confidence, out var parsed) ? parsed : Confidence.Possible;
        var family = Enum.TryParse<PropertyFamily>(finding.Family, out var parsedFamily) ? parsedFamily : PropertyFamily.Unknown;

        var parts = new List<string>
        {
            $"**{char.ToUpperInvariant(value.Display()[0])}{value.Display()[1..]}** from {(string.IsNullOrEmpty(finding.HolderName) ? "a holder the source does not name" : ClaimPacketBuilder.U(finding.HolderName))}",
            ClaimPacketBuilder.Md(PersonLabel(finding.SubjectId, household)),
            ClaimPacketBuilder.ConfidenceWords(confidence).TrimEnd('.'),
        };
        if (ClaimPacketBuilder.Place(finding) is { } place) parts.Add(ClaimPacketBuilder.U(place));
        parts.Add(Naupa.Describe(family));
        if (finding.EffortHours is { } hours) parts.Add("about " + ClaimPacketBuilder.Hours(hours));
        if (finding.Urgent) parts.Add("deadline close");

        return string.Join(" · ", parts) + $"\n   `{finding.Id}` · {StateWords(finding.CaseState)}";
    }

    private static string GroupSummary(IReadOnlyList<FindingRow> group)
    {
        var floor = group.Sum(f => Value(f).Floor);
        var count = Count(group.Count, "match", "matches");
        return floor > 0m ? $"at least {MoneyRange.Dollars(floor)} ({count})" : $"{count}, no stated amount";
    }

    private static string PersonLabel(string subjectId, Household household)
    {
        var subject = household.Find(subjectId);
        if (subject is null) return subjectId;
        var role = subject.Relationship.ToString().ToLowerInvariant();
        return subject.Deceased ? $"{subject.FullName} ({role}, deceased)" : $"{subject.FullName} ({role})";
    }

    public static string StateWords(string caseState) => CaseStates.Words(caseState);

    private static MoneyRange Value(FindingRow finding) => MoneyRange.FromBounds(finding.ValueLow, finding.ValueHigh);

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}
