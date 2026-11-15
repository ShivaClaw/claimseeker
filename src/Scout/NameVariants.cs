using ClaimSeeker.Core;

namespace ClaimSeeker.Scout;

/// <summary>Why a name is being searched. Ordered from the strongest reason to the weakest.</summary>
public enum VariantKind
{
    FamilyOnly,
    GivenFamily,
    InitialFamily,
    PriorFamilyOnly,
    GivenPriorFamily,
    InitialPriorFamily,
    AliasFamily,
    MiddleAsGiven,
    FamilyPart,
    Misspelling,
}

/// <summary>One name to search, already normalised. <see cref="Given"/> is null for a family-name sweep.</summary>
public sealed record SearchName(string? Given, string Family, VariantKind Kind);

public sealed record VariantOptions
{
    public bool IncludeMisspellings { get; init; } = true;
    public int MaxMisspellings { get; init; } = 3;
}

/// <summary>
/// The names a person's money may be filed under. Databases match close to exactly, so a maiden name,
/// a middle name used as a first name, or a clerk's misspelling hides real money. Every variant comes
/// from what the user typed about that person; nothing is invented except the capped misspelling
/// pass, and a match found that way can never rank above "possible".
/// </summary>
public static class NameVariants
{
    public static IReadOnlyList<SearchName> For(Subject subject, VariantOptions? options = null)
    {
        options ??= new VariantOptions();
        var family = NameText.Normalize(subject.FamilyName);
        var given = NameText.Normalize(subject.GivenName);
        if (family.Length == 0) return [];

        var initial = given.Length > 0 ? given[..1] : null;
        var variants = new List<SearchName>();
        var seen = new HashSet<(string?, string)>();

        void Add(string? g, string f, VariantKind kind)
        {
            if (f.Length == 0) return;
            if (g is { Length: 0 }) g = null;
            if (seen.Add((g, f))) variants.Add(new SearchName(g, f, kind));
        }

        Add(null, family, VariantKind.FamilyOnly);
        Add(given, family, VariantKind.GivenFamily);
        if (initial is not null && initial != given) Add(initial, family, VariantKind.InitialFamily);

        foreach (var prior in subject.PriorFamilyNames.Select(NameText.Normalize))
        {
            Add(null, prior, VariantKind.PriorFamilyOnly);
            Add(given, prior, VariantKind.GivenPriorFamily);
            if (initial is not null && initial != given) Add(initial, prior, VariantKind.InitialPriorFamily);
        }

        foreach (var alias in subject.Aliases.Select(NameText.Normalize))
            Add(alias, family, VariantKind.AliasFamily);

        foreach (var middle in subject.MiddleNames.Select(NameText.Normalize))
            if (middle.Length > 1) Add(middle, family, VariantKind.MiddleAsGiven);

        foreach (var part in FamilyParts(family))
            Add(null, part, VariantKind.FamilyPart);

        if (options.IncludeMisspellings)
            foreach (var misspelling in Misspellings(family).Take(Math.Max(0, options.MaxMisspellings)))
                Add(null, misspelling, VariantKind.Misspelling);

        return variants;
    }

    /// <summary>
    /// A compound family name ("SMITH JONES", "DE LA CRUZ") is often filed under one part of it, or
    /// run together. Parts shorter than three letters are particles, not names.
    /// </summary>
    public static IReadOnlyList<string> FamilyParts(string normalizedFamily)
    {
        var tokens = normalizedFamily.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return [];

        var parts = new List<string>();
        foreach (var token in tokens)
            if (token.Length >= 3 && !parts.Contains(token)) parts.Add(token);

        var joined = string.Concat(tokens);
        if (!parts.Contains(joined)) parts.Add(joined);
        return parts;
    }

    /// <summary>
    /// The misspellings a clerk most often makes of a single-word family name, in a fixed order:
    /// a doubled letter dropped, "ie" and "ei" swapped, a single l, n, r, s or t between vowels
    /// doubled, and -son written -sen (or the reverse). Deterministic, so a sweep is repeatable.
    /// </summary>
    public static IReadOnlyList<string> Misspellings(string normalizedFamily)
    {
        var name = normalizedFamily;
        if (name.Length < 4 || name.Contains(' ', StringComparison.Ordinal)) return [];

        var results = new List<string>();
        void Offer(string? candidate)
        {
            if (candidate is { Length: >= 3 } && candidate != name && !results.Contains(candidate)) results.Add(candidate);
        }

        Offer(Undouble(name));
        Offer(SwapIeEi(name));
        Offer(DoubleBetweenVowels(name));
        Offer(SwapSonSen(name));
        return results;
    }

    private static string? Undouble(string name)
    {
        for (var i = 0; i < name.Length - 1; i++)
            if (name[i] == name[i + 1]) return name.Remove(i, 1);
        return null;
    }

    private static string? SwapIeEi(string name)
    {
        var ie = name.IndexOf("IE", StringComparison.Ordinal);
        var ei = name.IndexOf("EI", StringComparison.Ordinal);
        if (ie >= 0 && (ei < 0 || ie < ei)) return string.Concat(name.AsSpan(0, ie), "EI", name.AsSpan(ie + 2));
        if (ei >= 0) return string.Concat(name.AsSpan(0, ei), "IE", name.AsSpan(ei + 2));
        return null;
    }

    private static string? DoubleBetweenVowels(string name)
    {
        for (var i = 1; i < name.Length - 1; i++)
        {
            var c = name[i];
            if (c is not ('L' or 'N' or 'R' or 'S' or 'T')) continue;
            if (!IsVowel(name[i - 1]) || !IsVowel(name[i + 1])) continue;
            return name.Insert(i, c.ToString());
        }

        return null;
    }

    private static string? SwapSonSen(string name)
    {
        if (name.EndsWith("SON", StringComparison.Ordinal)) return string.Concat(name.AsSpan(0, name.Length - 3), "SEN");
        if (name.EndsWith("SEN", StringComparison.Ordinal)) return string.Concat(name.AsSpan(0, name.Length - 3), "SON");
        return null;
    }

    private static bool IsVowel(char c) => c is 'A' or 'E' or 'I' or 'O' or 'U' or 'Y';
}
