using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaimSeeker.Core;

/// <summary>
/// What a source said a property is worth. States usually publish a band ("OVER $100"), sometimes an
/// exact figure, often nothing. The engine keeps exactly what was said and never fills a gap with a
/// guess: an undisclosed amount stays undisclosed in every total the user sees.
/// </summary>
public readonly record struct MoneyRange
{
    private MoneyRange(decimal? low, decimal? high, bool disclosed)
    {
        Low = low;
        High = high;
        Disclosed = disclosed;
    }

    /// <summary>The least the property is worth, when the source said so.</summary>
    public decimal? Low { get; }

    /// <summary>The most the property is worth, when the source gave a ceiling.</summary>
    public decimal? High { get; }

    /// <summary>False when the source gave no amount at all.</summary>
    public bool Disclosed { get; }

    public static MoneyRange Undisclosed => new(null, null, false);
    public static MoneyRange Exact(decimal amount) => new(amount, amount, true);
    public static MoneyRange AtLeast(decimal amount) => new(amount, null, true);
    public static MoneyRange UpTo(decimal amount) => new(0m, amount, true);
    public static MoneyRange Between(decimal low, decimal high) => low <= high ? new(low, high, true) : new(high, low, true);

    /// <summary>Rebuilds a range from stored bounds.</summary>
    public static MoneyRange FromBounds(decimal? low, decimal? high) =>
        low is null && high is null ? Undisclosed : new(low ?? 0m, high, true);

    /// <summary>The amount that can honestly be added to an "at least" total: the stated floor, or nothing.</summary>
    public decimal Floor => Disclosed ? Low ?? 0m : 0m;

    public bool IsExact => Disclosed && Low is { } l && High is { } h && l == h;
    public bool IsOpenEnded => Disclosed && High is null;

    /// <summary>How the range reads in a report: "$1,234.56", "over $100", "under $50", "$50–$100".</summary>
    public string Display()
    {
        if (!Disclosed) return "amount not disclosed";
        if (IsExact) return Dollars(Low!.Value);
        if (IsOpenEnded) return $"over {Dollars(Low ?? 0m)}";
        if ((Low ?? 0m) == 0m) return $"under {Dollars(High!.Value)}";
        return $"{Dollars(Low!.Value)}–{Dollars(High!.Value)}";
    }

    public override string ToString() => Display();

    /// <summary>"$1,234.56", or "$1,234" when there are no cents.</summary>
    public static string Dollars(decimal amount)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var format = rounded == decimal.Truncate(rounded) ? "N0" : "N2";
        return "$" + rounded.ToString(format, CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Reads a source's free-text value description. The text comes from a web response, so it is
/// untrusted: it is length-capped, matched with a time limit, and anything that does not read as a
/// plausible dollar amount is treated as "not disclosed" instead of being interpreted.
/// </summary>
public static partial class ValueText
{
    private const int MaxLength = 200;

    /// <summary>No single unclaimed property is plausibly larger. A bigger number is noise, not money.</summary>
    private const decimal MaxPlausibleAmount = 100_000_000m;

    [GeneratedRegex(@"(\$)?\s*(\d{1,3}(?:,\d{3})+|\d+)(\.\d{1,2})?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex AmountPattern();

    private static readonly string[] FloorWords =
        ["OVER", "MORE THAN", "ABOVE", "GREATER THAN", "AT LEAST", "OR MORE", "AND UP", "EXCEEDS", ">", "+"];

    private static readonly string[] CeilingWords =
        ["UNDER", "LESS THAN", "BELOW", "UP TO", "NOT MORE THAN", "OR LESS", "<"];

    /// <summary>Words that may sit beside a bare number without making it something other than dollars.</summary>
    private static readonly string[] NeutralWords = ["BETWEEN", "DOLLARS", "USD", "AND", "TO"];

    public static MoneyRange Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return MoneyRange.Undisclosed;
        var bounded = (text.Length > MaxLength ? text[..MaxLength] : text).ToUpperInvariant();

        var withSign = new List<decimal>();
        var bare = new List<decimal>();
        string residue;
        try
        {
            foreach (Match match in AmountPattern().Matches(bounded))
            {
                var digits = match.Groups[2].Value.Replace(",", "", StringComparison.Ordinal) + match.Groups[3].Value;
                if (!decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)) continue;
                if (amount > MaxPlausibleAmount) return MoneyRange.Undisclosed;
                (match.Groups[1].Success ? withSign : bare).Add(amount);
            }

            residue = AmountPattern().Replace(bounded, " ");
        }
        catch (RegexMatchTimeoutException)
        {
            return MoneyRange.Undisclosed;
        }

        // A number with a dollar sign is money. A bare number is money only when nothing else in the
        // text could make it a count: "100 SHARES" is not a hundred dollars.
        List<decimal> amounts;
        if (withSign.Count > 0) amounts = withSign;
        else if (bare.Count > 0 && IsOnlyBoundWords(residue)) amounts = bare;
        else return MoneyRange.Undisclosed;
        if (amounts.Count >= 2) return MoneyRange.Between(amounts.Min(), amounts.Max());

        var only = amounts[0];
        if (ContainsAny(bounded, FloorWords)) return MoneyRange.AtLeast(only);
        if (ContainsAny(bounded, CeilingWords)) return MoneyRange.UpTo(only);
        return MoneyRange.Exact(only);
    }

    private static bool ContainsAny(string text, string[] words)
    {
        foreach (var word in words)
            if (text.Contains(word, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>True when, with the numbers taken out, only range words and punctuation are left.</summary>
    private static bool IsOnlyBoundWords(string residue)
    {
        var rest = residue;
        // Longest first, so "NOT MORE THAN" is removed whole before "MORE THAN" could split it.
        foreach (var word in FloorWords.Concat(CeilingWords).Concat(NeutralWords).OrderByDescending(w => w.Length))
            rest = rest.Replace(word, " ", StringComparison.Ordinal);

        foreach (var c in rest)
            if (char.IsLetterOrDigit(c)) return false;
        return true;
    }
}
