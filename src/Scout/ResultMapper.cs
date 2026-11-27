using System.Text;
using System.Text.Json;
using ClaimSeeker.Core;

namespace ClaimSeeker.Scout;

public sealed record MappedResult(IReadOnlyList<RawRecord> Records, int ItemsSeen, bool Truncated);

/// <summary>Thrown when a source's answer is not in the shape its playbook describes.</summary>
public sealed class ResultFormatException(string message) : Exception(message);

/// <summary>
/// Turns a source's JSON answer into records. The answer is untrusted, so every limit here is
/// enforced on the way in: the size of the text, the depth of the JSON, the number of rows, the
/// length of each field, and the characters a field may contain.
///
/// An answer without the list the playbook names is an error, not an empty result. A source that
/// changes its response shape must show up as a broken playbook; it must never read as "no money
/// found".
/// </summary>
public static class ResultMapper
{
    public const int MaxJsonChars = 8 * 1024 * 1024;
    public const int MaxItems = 5000;
    public const int MaxFieldLength = 200;

    public static MappedResult Map(Playbook playbook, string json)
    {
        if (json.Length > MaxJsonChars) throw new ResultFormatException("The source's answer is too large to read.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException)
        {
            throw new ResultFormatException("The source's answer is not JSON.");
        }

        using (document)
        {
            var items = document.RootElement;
            foreach (var segment in playbook.Result.Items.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (items.ValueKind != JsonValueKind.Object || !items.TryGetProperty(segment, out var next))
                    throw new ResultFormatException($"The source's answer has no \"{playbook.Result.Items}\" list. The source may have changed.");
                items = next;
            }

            if (items.ValueKind != JsonValueKind.Array)
                throw new ResultFormatException($"\"{playbook.Result.Items}\" in the source's answer is not a list. The source may have changed.");

            var records = new List<RawRecord>();
            var seen = 0;
            foreach (var item in items.EnumerateArray())
            {
                seen++;
                if (records.Count >= MaxItems) continue;
                if (item.ValueKind != JsonValueKind.Object) continue;

                var owner = Field(playbook, item, "ownerName");
                if (string.IsNullOrEmpty(owner)) continue;

                records.Add(new RawRecord(
                    SourceId: playbook.Source,
                    SourceRecordId: Field(playbook, item, "sourceRecordId"),
                    OwnerName: owner,
                    HolderName: Field(playbook, item, "holderName"),
                    City: Field(playbook, item, "city"),
                    State: Field(playbook, item, "state")?.ToUpperInvariant(),
                    ValueText: Field(playbook, item, "valueText"),
                    PropertyTypeText: Field(playbook, item, "propertyTypeText")));
            }

            var truncated = seen > MaxItems || (playbook.Result.MaxItemsHint is { } hint && seen >= hint);
            return new MappedResult(records, seen, truncated);
        }
    }

    private static string? Field(Playbook playbook, JsonElement item, string field)
    {
        if (!playbook.Result.Fields.TryGetValue(field, out var property)) return null;
        if (!item.TryGetProperty(property, out var value)) return null;

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };

        return Clean(text);
    }

    /// <summary>One line of printable text, trimmed and capped. Empty becomes null.</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var sb = new StringBuilder(Math.Min(text.Length, MaxFieldLength));
        var lastWasSpace = true;
        foreach (var c in text)
        {
            if (sb.Length >= MaxFieldLength) break;
            var isSpace = char.IsWhiteSpace(c) || char.IsControl(c);
            if (isSpace)
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            // Characters that only exist to reorder or hide text have no place in a name or an amount.
            if (char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format
                or System.Globalization.UnicodeCategory.Surrogate
                or System.Globalization.UnicodeCategory.PrivateUse
                or System.Globalization.UnicodeCategory.OtherNotAssigned) continue;

            sb.Append(c);
            lastWasSpace = false;
        }

        var cleaned = sb.ToString().Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
