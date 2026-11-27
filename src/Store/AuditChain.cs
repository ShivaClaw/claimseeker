using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ClaimSeeker.Store;

/// <summary>What happened, before the store gives it a place in the log.</summary>
public sealed record EventInput(string Actor, string Kind, string Entity, string EntityId, string PayloadJson = "{}");

/// <summary>One line of the audit log, with the links that tie it to the line before.</summary>
public sealed record EventRow(
    long Seq,
    string At,
    string Actor,
    string Kind,
    string Entity,
    string EntityId,
    string PayloadJson,
    string PrevHash,
    string Hash);

public sealed record AuditVerification(bool Ok, long? FirstBrokenSeq, string? Reason)
{
    public static readonly AuditVerification Valid = new(true, null, null);
}

/// <summary>
/// The record of everything the engine did for the household. Each line's hash covers the line and
/// the hash of the line before it, so the log cannot be edited, shortened in the middle or reordered
/// without <see cref="Verify"/> naming the first line that no longer fits.
///
/// Payloads carry ids, counts and state names. They do not carry names or addresses: the log is the
/// thing a person exports when they ask "what have you done in my name", and it should be safe to
/// hand to someone helping them.
/// </summary>
public static class AuditChain
{
    public const string Genesis = "GENESIS";

    public static string ComputeHash(long seq, string at, string actor, string kind, string entity, string entityId, string payloadJson, string prevHash)
    {
        // Each field is written with its length in front, so text can never slide from one field into the next.
        var sb = new StringBuilder();
        sb.Append(seq.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var field in new[] { at, actor, kind, entity, entityId, payloadJson, prevHash })
            sb.Append(Encoding.UTF8.GetByteCount(field).ToString(CultureInfo.InvariantCulture)).Append(':').Append(field).Append('\n');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public static EventRow Link(long seq, string at, string prevHash, EventInput e) =>
        new(seq, at, e.Actor, e.Kind, e.Entity, e.EntityId, e.PayloadJson, prevHash,
            ComputeHash(seq, at, e.Actor, e.Kind, e.Entity, e.EntityId, e.PayloadJson, prevHash));

    /// <summary>Checks a log read in sequence order.</summary>
    public static AuditVerification Verify(IReadOnlyList<EventRow> rows)
    {
        var expectedPrev = Genesis;
        long expectedSeq = 1;

        foreach (var row in rows)
        {
            if (row.Seq != expectedSeq)
                return new AuditVerification(false, row.Seq, $"Line {expectedSeq} is missing or out of order.");
            if (row.PrevHash != expectedPrev)
                return new AuditVerification(false, row.Seq, "The line does not link to the one before it.");
            if (ComputeHash(row.Seq, row.At, row.Actor, row.Kind, row.Entity, row.EntityId, row.PayloadJson, row.PrevHash) != row.Hash)
                return new AuditVerification(false, row.Seq, "The line's content does not match its hash.");

            expectedPrev = row.Hash;
            expectedSeq++;
        }

        return AuditVerification.Valid;
    }
}
