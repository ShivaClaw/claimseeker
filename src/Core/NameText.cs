using System.Text;

namespace ClaimSeeker.Core;

/// <summary>
/// Puts a person's name into the shape unclaimed-property databases store it in: upper case, plain
/// ASCII letters, single spaces. Every comparison in the engine goes through this, so "O'Brien",
/// "OBRIEN" and "O’BRIEN" are one name, and "Peña" finds "PENA".
///
/// Deliberately culture-free. The fold table below is the whole rule, so the same input gives the
/// same output on Windows, macOS and Linux whatever globalization data the machine has.
/// </summary>
public static class NameText
{
    /// <summary>Generational suffixes. They are kept apart from the name tokens they follow.</summary>
    public static readonly IReadOnlySet<string> Suffixes =
        new HashSet<string>(StringComparer.Ordinal) { "JR", "SR", "II", "III", "IV", "V" };

    /// <summary>Longest name the engine will compare. Anything past it is cut, not trusted.</summary>
    public const int MaxLength = 200;

    /// <summary>Upper-case ASCII letters and digits separated by single spaces. Never null.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Length > MaxLength ? value[..MaxLength] : value;

        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var raw in text)
        {
            // Marks inside a word join it; everything else that is not a letter or digit separates words.
            if (raw is '\'' or '’' or '‘' or '`' or '.') continue;

            var folded = Fold(raw);
            if (folded.Length == 0)
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(folded);
        }

        return sb.ToString();
    }

    /// <summary>The normalised name split into words.</summary>
    public static IReadOnlyList<string> Tokens(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? [] : normalized.Split(' ');
    }

    /// <summary>The normalised name with its spaces removed, so "MC DONALD" and "MCDONALD" compare equal.</summary>
    public static string Compact(string? value) => Normalize(value).Replace(" ", "", StringComparison.Ordinal);

    /// <summary>The first letter of the normalised name, or null when there is none.</summary>
    public static char? Initial(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized[0];
    }

    /// <summary>One character to its upper-case ASCII form; empty when it is not a letter or digit.</summary>
    private static string Fold(char c)
    {
        if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c.ToString();
        if (c is >= 'a' and <= 'z') return ((char)(c - 32)).ToString();

        return c switch
        {
            'À' or 'Á' or 'Â' or 'Ã' or 'Ä' or 'Å' or 'Ā' or 'Ă' or 'Ą'
                or 'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' or 'ā' or 'ă' or 'ą' => "A",
            'Æ' or 'æ' => "AE",
            'Ç' or 'Ć' or 'Ĉ' or 'Ċ' or 'Č' or 'ç' or 'ć' or 'ĉ' or 'ċ' or 'č' => "C",
            'Ď' or 'Đ' or 'ď' or 'đ' or 'Ð' or 'ð' => "D",
            'È' or 'É' or 'Ê' or 'Ë' or 'Ē' or 'Ĕ' or 'Ė' or 'Ę' or 'Ě'
                or 'è' or 'é' or 'ê' or 'ë' or 'ē' or 'ĕ' or 'ė' or 'ę' or 'ě' => "E",
            'Ĝ' or 'Ğ' or 'Ġ' or 'Ģ' or 'ĝ' or 'ğ' or 'ġ' or 'ģ' => "G",
            'Ĥ' or 'Ħ' or 'ĥ' or 'ħ' => "H",
            'Ì' or 'Í' or 'Î' or 'Ï' or 'Ĩ' or 'Ī' or 'Ĭ' or 'Į' or 'İ'
                or 'ì' or 'í' or 'î' or 'ï' or 'ĩ' or 'ī' or 'ĭ' or 'į' or 'ı' => "I",
            'Ĵ' or 'ĵ' => "J",
            'Ķ' or 'ķ' => "K",
            'Ĺ' or 'Ļ' or 'Ľ' or 'Ŀ' or 'Ł' or 'ĺ' or 'ļ' or 'ľ' or 'ŀ' or 'ł' => "L",
            'Ñ' or 'Ń' or 'Ņ' or 'Ň' or 'ñ' or 'ń' or 'ņ' or 'ň' => "N",
            'Ò' or 'Ó' or 'Ô' or 'Õ' or 'Ö' or 'Ø' or 'Ō' or 'Ŏ' or 'Ő'
                or 'ò' or 'ó' or 'ô' or 'õ' or 'ö' or 'ø' or 'ō' or 'ŏ' or 'ő' => "O",
            'Œ' or 'œ' => "OE",
            'Ŕ' or 'Ŗ' or 'Ř' or 'ŕ' or 'ŗ' or 'ř' => "R",
            'Ś' or 'Ŝ' or 'Ş' or 'Š' or 'ś' or 'ŝ' or 'ş' or 'š' => "S",
            'ß' => "SS",
            'Ţ' or 'Ť' or 'Ŧ' or 'ţ' or 'ť' or 'ŧ' => "T",
            'Þ' or 'þ' => "TH",
            'Ù' or 'Ú' or 'Û' or 'Ü' or 'Ũ' or 'Ū' or 'Ŭ' or 'Ů' or 'Ű' or 'Ų'
                or 'ù' or 'ú' or 'û' or 'ü' or 'ũ' or 'ū' or 'ŭ' or 'ů' or 'ű' or 'ų' => "U",
            'Ŵ' or 'ŵ' => "W",
            'Ý' or 'Ÿ' or 'Ŷ' or 'ý' or 'ÿ' or 'ŷ' => "Y",
            'Ź' or 'Ż' or 'Ž' or 'ź' or 'ż' or 'ž' => "Z",
            _ => "",
        };
    }
}
