using System.Text.RegularExpressions;

namespace ClaimSeeker.Core;

/// <summary>
/// Spots government and financial identifiers in text. ClaimSeeker has no field for a Social Security
/// number, a taxpayer id, or an account or card number, and never needs one: identity is proved to the
/// holder through the holder's own secure process, by the person. This check is what keeps such a
/// number from being stored in the household or written into a draft by accident.
///
/// It errs toward refusing. A nine-digit reference number is refused too, because nothing here can
/// tell it from a Social Security number, and a false refusal costs a re-typed line while a miss
/// puts an identifier in an email.
/// </summary>
public static partial class SensitiveIdentifiers
{
    private const int MaxScanLength = 50_000;

    [GeneratedRegex(@"(?<!\d)\d{3}[- ]\d{2}[- ]\d{4}(?!\d)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex SocialSecurityPattern();

    [GeneratedRegex(@"(?<!\d)\d{9}(?!\d)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex NineDigitPattern();

    [GeneratedRegex(@"(?<!\d)\d{2}-\d{7}(?!\d)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex TaxpayerIdPattern();

    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){11,18}\d(?!\d)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex LongNumberPattern();

    /// <summary>Plain words for the kind of identifier found, or null when the text is clean.</summary>
    public static string? Find(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var bounded = text.Length > MaxScanLength ? text[..MaxScanLength] : text;

        try
        {
            if (SocialSecurityPattern().IsMatch(bounded)) return "Social Security number";
            if (TaxpayerIdPattern().IsMatch(bounded)) return "taxpayer identification number";
            if (LongNumberPattern().IsMatch(bounded)) return "bank account or card number";
            if (NineDigitPattern().IsMatch(bounded)) return "Social Security or bank routing number";
        }
        catch (RegexMatchTimeoutException)
        {
            // Text that cannot be checked in time is treated as if it failed the check.
            return "number that could not be checked";
        }

        // Text too long to scan in full is refused for the same reason: the unscanned part is unknown.
        return text.Length > MaxScanLength ? "number that could not be checked" : null;
    }

    public static bool IsClean(string? text) => Find(text) is null;
}
