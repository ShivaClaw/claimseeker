using System.Text;

namespace ClaimSeeker.Core;

/// <summary>
/// Rules for text that came from a source's response. Such text is shown and compared; it is never
/// allowed to act. Two things follow. It may not go into a letter unless it is inert: plain words
/// with nothing a mail program could turn into a link, an address or structure. And wherever it is
/// displayed, anything in it that looks like a web address is defanged first, so nothing a website
/// returned can become something a person clicks.
///
/// Official records do not carry links in an owner's or holder's name. A record that does is worth a
/// second look, and the packet says so.
/// </summary>
public static class UntrustedText
{
    public const int MaxInertLength = 200;

    /// <summary>True when the text contains something that reads as a web address or an email address.</summary>
    public static bool LooksLikeLink(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var lower = text.ToLowerInvariant();
        return lower.Contains("://", StringComparison.Ordinal)
               || lower.Contains("www.", StringComparison.Ordinal)
               || lower.Contains("http", StringComparison.Ordinal)
               || lower.Contains('@');
    }

    /// <summary>True when the text is safe to place in a letter as it stands.</summary>
    public static bool IsInert(string text)
    {
        if (text.Length > MaxInertLength) return false;
        foreach (var c in text)
            if (char.IsControl(c) || c is '<' or '>' or '{' or '}' or '\\' or '`') return false;
        return !LooksLikeLink(text);
    }

    /// <summary>
    /// The text with anything link-like broken so that no program will make it clickable:
    /// "http://a.example/x" becomes "hxxp[:]//a[.]example/x". Text with nothing link-like in it is
    /// returned unchanged.
    /// </summary>
    public static string Defang(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (!LooksLikeLink(text)) return text;

        var sb = new StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '.') sb.Append("[.]");
            else if (c == '@') sb.Append("[at]");
            else if (c == ':' && i + 2 < text.Length && text[i + 1] == '/' && text[i + 2] == '/') sb.Append("[:]");
            else if ((c is 't' or 'T') && i >= 1 && i + 2 < text.Length
                     && (text[i - 1] is 'h' or 'H') && (text[i + 1] is 't' or 'T') && (text[i + 2] is 'p' or 'P'))
            {
                // "http" to "hxxp": the two letters in the middle, whatever their case.
                sb.Append(c == 't' ? "xx" : "XX");
                i++;
            }
            else sb.Append(c);
        }

        return sb.ToString();
    }
}

public static class CaseStates
{
    /// <summary>A case state in the words a person would use.</summary>
    public static string Words(CaseState state) => state switch
    {
        CaseState.DISCOVERED => "found",
        CaseState.RANKED => "ranked",
        CaseState.PACKET_READY => "packet ready",
        CaseState.DRAFT_PENDING => "letter requested",
        CaseState.ATTESTED => "letter checked",
        CaseState.BLOCKED_ATTESTATION => "letter blocked",
        CaseState.DRAFT_READY => "letter ready",
        CaseState.DRAFTED => "letter drafted",
        CaseState.FILED_BY_USER => "filed by you",
        CaseState.IN_REVIEW => "in review",
        CaseState.NEEDS_DOCUMENTS => "more documents requested",
        CaseState.PAID => "paid",
        CaseState.DENIED => "denied",
        CaseState.DISMISSED => "dismissed",
        CaseState.EXPIRED => "expired",
        _ => state.ToString(),
    };

    public static string Words(string storedState) =>
        Enum.TryParse<CaseState>(storedState, out var state) ? Words(state) : storedState;
}
