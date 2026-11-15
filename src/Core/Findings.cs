using System.Security.Cryptography;
using System.Text;

namespace ClaimSeeker.Core;

/// <summary>
/// One row a source returned, before anyone has decided whose it is. Everything in it came from a
/// web response and is untrusted: it is data to compare and display, never an instruction, a link
/// to follow, or an address to write to.
/// </summary>
public sealed record RawRecord(
    string SourceId,
    string? SourceRecordId,
    string OwnerName,
    string? HolderName,
    string? City,
    string? State,
    string? ValueText,
    string? PropertyTypeText);

/// <summary>
/// How sure the engine is that a record belongs to a person in the household. There is no level
/// called "confirmed": only the holder of the money can confirm who owns it, after it has checked
/// that person's identity.
/// </summary>
public enum Confidence
{
    /// <summary>The name could be theirs, but nothing ties the record to somewhere they lived.</summary>
    Possible = 1,
    /// <summary>The name matches and the record is in a state where they lived.</summary>
    Likely = 2,
    /// <summary>The name matches and the record names a city and state where they lived.</summary>
    Strong = 3,
}

/// <summary>
/// Where a finding stands. The names are the stored values, so the store, the lifecycle graph and
/// every report share one vocabulary.
/// </summary>
public enum CaseState
{
    DISCOVERED,
    RANKED,
    PACKET_READY,
    DRAFT_PENDING,
    ATTESTED,
    BLOCKED_ATTESTATION,
    DRAFT_READY,
    DRAFTED,
    FILED_BY_USER,
    IN_REVIEW,
    NEEDS_DOCUMENTS,
    PAID,
    DENIED,
    DISMISSED,
    EXPIRED,
}

/// <summary>Who caused a change. The stored values are the lower-case names.</summary>
public enum Actor { Engine, User }

public static class ActorText
{
    public static string Stored(this Actor actor) => actor == Actor.User ? "user" : "engine";
}

public static class FindingKeys
{
    /// <summary>
    /// A stable id for one record from one source. The source's own record id is used when it gives
    /// one. Otherwise the id is built from the record's content plus its position among identical
    /// rows, so two identical uncashed checks stay two findings.
    /// </summary>
    public static string FindingId(RawRecord record, int ordinalAmongIdentical)
    {
        var material = string.IsNullOrWhiteSpace(record.SourceRecordId)
            ? Join("content", record.SourceId, NameText.Normalize(record.OwnerName), NameText.Normalize(record.HolderName),
                NameText.Normalize(record.City), NameText.Normalize(record.State), Flat(record.ValueText),
                Flat(record.PropertyTypeText), ordinalAmongIdentical.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : Join("record", record.SourceId, record.SourceRecordId.Trim());
        return "f_" + Hex(material, 12);
    }

    /// <summary>
    /// The same property seen through two sources has the same key: owner, holder, place and value.
    /// The source is left out on purpose.
    /// </summary>
    public static string DedupeKey(RawRecord record) =>
        "d_" + Hex(Join("dedupe", NameText.Normalize(record.OwnerName), NameText.Normalize(record.HolderName),
            NameText.Normalize(record.City), NameText.Normalize(record.State), Flat(record.ValueText)), 12);

    /// <summary>The content identity used to count identical rows within one response.</summary>
    public static string ContentKey(RawRecord record) =>
        Join("row", record.SourceId, NameText.Normalize(record.OwnerName), NameText.Normalize(record.HolderName),
            NameText.Normalize(record.City), NameText.Normalize(record.State), Flat(record.ValueText), Flat(record.PropertyTypeText));

    private static string Flat(string? value) => (value ?? "").Trim().ToUpperInvariant();

    // Length-prefixed, so no field's text can run into the next and make two different records hash alike.
    private static string Join(params string[] fields)
    {
        var sb = new StringBuilder();
        foreach (var field in fields)
            sb.Append(field.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(field).Append('|');
        return sb.ToString();
    }

    private static string Hex(string material, int bytes)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(digest, 0, bytes).ToLowerInvariant();
    }
}
