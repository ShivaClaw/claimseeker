using System.Security.Cryptography;
using System.Text;
using ClaimSeeker.Verifier;

namespace ClaimSeeker.Dispatcher;

public sealed record DraftReceipt(string Path, string Sha256, bool AlreadyExisted);

/// <summary>
/// The only way a letter leaves the engine. It has one method, and that method makes a draft. There
/// is no second verb to add to it: this assembly holds no code that reaches a network, and a harness
/// checks that by reflection on every build.
///
/// The method takes a <see cref="GatePassedDraft"/>, which only the Attestation Gate can make, so a
/// letter that skipped the gate cannot be passed in.
/// </summary>
public interface IDraftOutbox
{
    DraftReceipt CreateDraft(GatePassedDraft draft);
}

/// <summary>
/// Writes the letter as an .eml file in the person's workspace, marked unsent, for them to open in
/// their own mail program, read, address and send. The same letter written twice is one file.
/// </summary>
public sealed class FileDraftOutbox(string directory) : IDraftOutbox
{
    public DraftReceipt CreateDraft(GatePassedDraft draft)
    {
        var bytes = EmlWriter.Write(draft.SubjectLine, draft.Body, draft.Recipient);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (!IsSafeName(draft.FindingId) || !IsSafeName(draft.TemplateId))
            throw new ArgumentException("The draft's ids cannot be used in a file name.", nameof(draft));

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{draft.FindingId}-{draft.TemplateId}-{sha[..12]}.eml");
        if (File.Exists(path)) return new DraftReceipt(path, sha, true);

        // Written beside the target and moved into place, so a crash never leaves half a letter under the real name.
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: false);
        return new DraftReceipt(path, sha, false);
    }

    private static bool IsSafeName(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
}

/// <summary>Builds the bytes of an unsent message.</summary>
public static class EmlWriter
{
    private const int MaxLineBytes = 998;

    public static byte[] Write(string subjectLine, string body, string? recipient)
    {
        if (subjectLine.Any(char.IsControl)) throw new ArgumentException("A subject line is a single line.", nameof(subjectLine));
        if (recipient is not null && !IsPlainAddress(recipient)) throw new ArgumentException("The recipient is not a plain email address.", nameof(recipient));

        var normalizedBody = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalizedBody.Split('\n');
        var plain = lines.All(line => Encoding.UTF8.GetByteCount(line) <= MaxLineBytes);

        var sb = new StringBuilder();
        // Outlook and Windows Mail open a message carrying this header as an editable draft.
        sb.Append("X-Unsent: 1\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        if (recipient is not null) sb.Append("To: ").Append(recipient).Append("\r\n");
        sb.Append("Subject: ").Append(EncodeHeader(subjectLine)).Append("\r\n");
        sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
        sb.Append("Content-Transfer-Encoding: ").Append(plain ? "8bit" : "base64").Append("\r\n");
        sb.Append("\r\n");

        if (plain)
        {
            foreach (var line in lines) sb.Append(line).Append("\r\n");
        }
        else
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(normalizedBody.Replace("\n", "\r\n", StringComparison.Ordinal)));
            for (var i = 0; i < encoded.Length; i += 76)
                sb.Append(encoded, i, Math.Min(76, encoded.Length - i)).Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>ASCII as it is; anything else as RFC 2047 encoded words, folded.</summary>
    public static string EncodeHeader(string value)
    {
        if (value.All(c => c is >= ' ' and <= '~')) return value;

        var words = new List<string>();
        var chunk = new StringBuilder();
        var chunkBytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (chunkBytes + size > 45)
            {
                words.Add(EncodedWord(chunk.ToString()));
                chunk.Clear();
                chunkBytes = 0;
            }

            chunk.Append(rune.ToString());
            chunkBytes += size;
        }

        if (chunk.Length > 0) words.Add(EncodedWord(chunk.ToString()));
        return string.Join("\r\n ", words);
    }

    private static string EncodedWord(string text) => "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "?=";

    private static bool IsPlainAddress(string address)
    {
        if (address.Length > 254) return false;
        var at = address.IndexOf('@');
        if (at <= 0 || at != address.LastIndexOf('@') || at == address.Length - 1) return false;
        return address.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_' or '+' or '@');
    }
}
