using System.Text;
using System.Text.Json;
using ClaimSeeker.Core;
using ClaimSeeker.Scout;
using ClaimSeeker.Store;

namespace ClaimSeeker.Preparer;

public sealed record ClaimPacket(string FindingId, string FileName, string Markdown);

/// <summary>
/// Writes the page a person works from when they file a claim themselves: what was found, how sure
/// the match is, where the official claim starts, and what to have on hand. It is a local document
/// for the person; nothing in it is sent anywhere.
///
/// The checklist is the usual set, stated as usual and not as a requirement. Each program's own
/// claim form is the authority, and the packet says so.
/// </summary>
public static class ClaimPacketBuilder
{
    public const string FreeToFile =
        "Filing a claim is free. Nobody needs to be paid a share of your money to file it. ClaimSeeker prepares; you review and submit.";

    public static ClaimPacket Build(FindingRow finding, Household household, SourceDescriptor source, DateOnly today)
    {
        var subject = household.Find(finding.SubjectId);
        var value = MoneyRange.FromBounds(finding.ValueLow, finding.ValueHigh);
        var family = Enum.TryParse<PropertyFamily>(finding.Family, out var parsed) ? parsed : PropertyFamily.Unknown;
        var confidence = Enum.Parse<Confidence>(finding.Confidence);
        var place = Place(finding);

        var md = new StringBuilder();
        md.Append("# Claim packet: ").Append(U(finding.OwnerName));
        if (!string.IsNullOrEmpty(finding.HolderName)) md.Append(" / ").Append(U(finding.HolderName));
        md.Append("\n\n");
        md.Append("Prepared ").Append(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        if (subject is not null) md.Append(" for ").Append(Md(subject.FullName));
        md.Append(". **You file this claim yourself, and it is free.**\n\n");

        md.Append("## What was found\n\n");
        md.Append("| | |\n|---|---|\n");
        // Only what the source said is escaped and defanged. The engine's own words are written as they are.
        Row(md, "Reported owner", U(finding.OwnerName));
        Row(md, "Holder", string.IsNullOrEmpty(finding.HolderName) ? "not named by the source" : U(finding.HolderName));
        Row(md, "Last known address", place is null ? "not given by the source" : U(place));
        Row(md, "Kind", string.IsNullOrEmpty(finding.PropertyTypeText)
            ? Naupa.Describe(family)
            : $"{Naupa.Describe(family)} (reported as \"{U(finding.PropertyTypeText)}\")");
        Row(md, "Amount", string.IsNullOrEmpty(finding.ValueText) ? value.Display() : $"{value.Display()} (reported as \"{U(finding.ValueText)}\")");
        Row(md, "Source", $"{source.Name}, run by {source.Operator}");
        if (!string.IsNullOrEmpty(finding.SourceRecordId)) Row(md, "Source record", U(finding.SourceRecordId));
        Row(md, "First seen", DateOnlyText(finding.FirstSeen));
        if (finding.MissingSince is { } missing)
            Row(md, "Note", $"Not in the source's list since {DateOnlyText(missing)}. It may have been paid or withdrawn.");
        md.Append('\n');

        if (new[] { finding.OwnerName, finding.HolderName, finding.City, finding.ValueText, finding.PropertyTypeText }.Any(UntrustedText.LooksLikeLink))
            md.Append("**Caution:** the source's text for this record contains what looks like a web or email address. Official records do not carry links. ")
              .Append("ClaimSeeker has broken the address so it cannot be opened. Use only the official addresses under \"Where to claim\".\n\n");

        md.Append("## How sure this is yours\n\n");
        md.Append("**").Append(ConfidenceWords(confidence)).Append("** ").Append(finding.Rationale).Append("\n\n");
        md.Append(confidence == Confidence.Possible
            ? "Treat this as a lead to check, not as money owed. Only the holder can confirm who owns it, after checking identity.\n\n"
            : "Only the holder can confirm who owns it, after checking identity.\n\n");

        md.Append("## Where to claim\n\n");
        var step = 1;
        if (source.EntryUrl is { } entry)
            md.Append(step++).Append(". Open ").Append(entry).Append(" and search for the name exactly as it is reported above.\n");
        else
            md.Append(step++).Append(". Go to the official site of ").Append(source.Operator).Append(" (")
              .Append(string.Join(", ", source.OfficialHosts)).Append(") and search for the name exactly as it is reported above.\n");

        md.Append(step++).Append(". The claim itself is filed with the official program that holds the property");
        if (!string.IsNullOrEmpty(finding.UsState)) md.Append(" (the record is listed under ").Append(U(finding.UsState)).Append(')');
        md.Append(". Follow the result to that program's own site.\n");

        var directory = SourceRegistry.Find("state-programs")?.EntryUrl;
        if (directory is not null && source.Id != "state-programs")
            md.Append(step).Append(". NAUPA's directory of official state programs is at ").Append(directory).Append(".\n");

        md.Append("\nThe addresses above come from ClaimSeeker's source registry. A site that asks for a fee to file a claim, or to release your money, is not the official one.\n\n");

        md.Append("## What to have on hand\n\n");
        md.Append("- [ ] A government-issued photo ID.\n");
        md.Append("- [ ] Your Social Security number, if the claim form asks for it. Enter it only on the official site. ClaimSeeker never asks for it and has nowhere to keep it.\n");
        if (place is not null)
            md.Append("- [ ] Something that ties ").Append(subject is { Deceased: true } ? "the owner" : "you").Append(" to ").Append(U(place))
              .Append(": an old bill, bank statement, lease or tax form showing that address.\n");
        if (finding.NameChangeDocsLikely)
            md.Append("- [ ] Proof of the name change: a marriage certificate, divorce decree or court order.\n");
        if (subject is { Deceased: true } || finding.EstateRecord)
            md.Append("- [ ] A certified death certificate, and proof that you may claim for the estate: letters testamentary or of administration, a small-estate affidavit, or the program's own heirship form.\n");
        if (finding.ExpectedValue is { } expected && expected >= 1000m)
            md.Append("- [ ] A notary. Larger claims often need a notarized form; the form will say.\n");
        md.Append("\nThis is the usual set. The program's own claim form is the authority on what it needs.\n\n");

        md.Append("## Time\n\n");
        if (finding.EffortHours is { } hours)
            md.Append("Estimated effort: about ").Append(Hours(hours)).Append(".\n");
        foreach (var line in Rationale(finding.RankRationaleJson))
            md.Append("- ").Append(line).Append('\n');
        if (source.DeadlineNote is { } deadline) md.Append("\n**Deadline:** ").Append(deadline).Append('\n');
        md.Append('\n');

        md.Append("## If this is not yours\n\n");
        md.Append("Mark it dismissed and ClaimSeeker will stop showing it: `claimseeker mark ").Append(finding.Id).Append(" dismissed`.\n\n");

        md.Append("---\n\n").Append(FreeToFile).Append('\n');

        return new ClaimPacket(finding.Id, finding.Id + ".md", md.ToString());
    }

    public static string ConfidenceWords(Confidence confidence) => confidence switch
    {
        Confidence.Strong => "Strong match.",
        Confidence.Likely => "Likely match.",
        _ => "Possible match.",
    };

    public static string? Place(FindingRow finding)
    {
        if (!string.IsNullOrEmpty(finding.City) && !string.IsNullOrEmpty(finding.UsState)) return $"{finding.City}, {finding.UsState}";
        return string.IsNullOrEmpty(finding.UsState) ? null : finding.UsState;
    }

    public static string Hours(decimal hours) =>
        hours == 1m ? "1 hour" : $"{hours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} hours";

    public static IReadOnlyList<string> Rationale(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>The date part of a stored timestamp.</summary>
    public static string DateOnlyText(string timestamp) => timestamp.Length >= 10 ? timestamp[..10] : timestamp;

    /// <summary>A table row. The value is written as given, so the caller escapes whatever in it came from a source.</summary>
    private static void Row(StringBuilder md, string label, string value) =>
        md.Append("| ").Append(label).Append(" | ").Append(value).Append(" |\n");

    /// <summary>
    /// Text from a source, made safe to show: anything link-like is broken first, then the characters
    /// markdown gives meaning to are escaped. What a website returned is shown as words and nothing else.
    /// </summary>
    public static string U(string? text) => Md(UntrustedText.Defang(text));

    /// <summary>
    /// Escapes the characters that could start a link, an image, emphasis, a code span, markup or a
    /// table cell. Used for names a person typed as well as for text from a source.
    /// </summary>
    public static string Md(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c is '\r' or '\n') { sb.Append(' '); continue; }
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '|') sb.Append('\\');
            sb.Append(c);
        }

        return sb.ToString();
    }
}
