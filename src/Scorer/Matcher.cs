using ClaimSeeker.Core;
using ClaimSeeker.Scout;

namespace ClaimSeeker.Scorer;

/// <summary>How a record's name lines up with a person's.</summary>
public enum NameMatch
{
    /// <summary>Given and current family name both match.</summary>
    ExactCurrent,
    /// <summary>Given name matches, under a former family name.</summary>
    ExactPrior,
    /// <summary>The record uses another given name the person has gone by.</summary>
    Alias,
    /// <summary>The record uses the person's middle name as a first name.</summary>
    MiddleAsGiven,
    /// <summary>The record has only an initial where the given name would be.</summary>
    InitialOnly,
    /// <summary>The record carries the family name and nothing else.</summary>
    FamilyOnly,
    /// <summary>The record is under one part of a compound family name.</summary>
    FamilyPart,
    /// <summary>The record is under a likely misspelling of the family name.</summary>
    Misspelling,
}

public sealed record MatchResult(
    string SubjectId,
    Confidence Confidence,
    NameMatch NameMatch,
    bool CityCorroborated,
    bool StateCorroborated,
    bool NameChangeDocsLikely,
    bool EstateRecord,
    string Rationale);

/// <summary>
/// Decides whether a record a source returned belongs to someone in the household, and how sure that
/// is. A family-name sweep brings back every stranger who shares the name; this is the filter that
/// keeps the household's records and lets the rest go. A record that matches no one is not stored.
///
/// The rule in one line: the name gets a record considered, and the place decides how far it is
/// trusted. A weak name with no place behind it is dropped. Nothing found by a guessed spelling or
/// by half a family name ever rises above "possible".
/// </summary>
public static class Matcher
{
    private static readonly HashSet<string> EstateWords =
        new(StringComparer.Ordinal) { "ESTATE", "EST", "DECEASED", "DECD", "DEC", "HEIRS", "HEIR" };

    private static readonly HashSet<string> FillerWords = new(StringComparer.Ordinal) { "OF", "THE", "MR", "MRS", "MS", "DR" };

    // A record under one of these is an organisation, not a person in the household.
    private static readonly HashSet<string> EntityWords = new(StringComparer.Ordinal)
    {
        "LLC", "INC", "CORP", "CORPORATION", "COMPANY", "LTD", "LP", "LLP", "PLLC", "TRUST", "TRUSTEE", "FOUNDATION",
        "ASSOCIATION", "ASSOC", "CHURCH", "BANK", "PARTNERS", "PARTNERSHIP", "ENTERPRISES", "HOLDINGS", "GROUP", "FUND",
    };

    /// <summary>The best match among the people the engine may search for, or null.</summary>
    public static MatchResult? Match(RawRecord record, Household household)
    {
        MatchResult? best = null;
        foreach (var subject in household.Subjects)
        {
            if (!subject.MaySearch) continue;
            var candidate = Match(record, subject);
            if (candidate is null) continue;
            if (best is null || IsBetter(candidate, best)) best = candidate;
        }

        return best;
    }

    public static MatchResult? Match(RawRecord record, Subject subject)
    {
        MatchResult? best = null;
        string? inheritedFamily = null;

        foreach (var part in OwnerParts(record.OwnerName))
        {
            var tokens = part.ToList();
            if (tokens.Any(EntityWords.Contains)) continue;

            var estate = tokens.RemoveAll(EstateWords.Contains) > 0;
            tokens.RemoveAll(FillerWords.Contains);
            tokens.RemoveAll(NameText.Suffixes.Contains);
            if (tokens.Count == 0) continue;

            var family = MatchFamily(tokens, subject);
            if (family is null && inheritedFamily is not null && tokens.Count <= 2)
            {
                // "SAMPLE JOHN & JANE": the second owner is written without the family name the first one carried.
                tokens.InsertRange(0, inheritedFamily.Split(' '));
                family = MatchFamily(tokens, subject);
            }

            if (family is null) continue;
            inheritedFamily ??= family.Value.MatchedText;

            var result = Decide(record, subject, family.Value, estate);
            if (result is not null && (best is null || IsBetter(result, best))) best = result;
        }

        return best;
    }

    private static MatchResult? Decide(RawRecord record, Subject subject, FamilyHit family, bool estate)
    {
        var given = MatchGiven(family.Remaining, subject);
        if (given is null) return null;

        NameMatch nameMatch;
        switch (family.Kind, given.Value.Kind)
        {
            case (FamilyKind.Current, GivenKind.Exact): nameMatch = NameMatch.ExactCurrent; break;
            case (FamilyKind.Prior, GivenKind.Exact): nameMatch = NameMatch.ExactPrior; break;
            case (FamilyKind.Current or FamilyKind.Prior, GivenKind.Alias): nameMatch = NameMatch.Alias; break;
            case (FamilyKind.Current or FamilyKind.Prior, GivenKind.Middle): nameMatch = NameMatch.MiddleAsGiven; break;
            case (FamilyKind.Current or FamilyKind.Prior, GivenKind.Initial): nameMatch = NameMatch.InitialOnly; break;
            case (FamilyKind.Current or FamilyKind.Prior, GivenKind.None): nameMatch = NameMatch.FamilyOnly; break;
            case (FamilyKind.Part, GivenKind.Exact): nameMatch = NameMatch.FamilyPart; break;
            case (FamilyKind.Misspelling, GivenKind.Exact): nameMatch = NameMatch.Misspelling; break;
            default: return null; // a guessed family name needs an exact given name beside it
        }

        var recordCity = NameText.Normalize(record.City);
        var recordState = NameText.Normalize(record.State);
        var stateOk = recordState.Length > 0 && subject.StatesLived.Contains(recordState);
        var cityOk = recordCity.Length > 0 && subject.Addresses.Any(a =>
            NameText.Normalize(a.City) == recordCity && NameText.Normalize(a.State) == recordState);

        Confidence? confidence = nameMatch switch
        {
            NameMatch.ExactCurrent or NameMatch.ExactPrior => cityOk ? Confidence.Strong : stateOk ? Confidence.Likely : Confidence.Possible,
            NameMatch.Alias => cityOk ? Confidence.Likely : Confidence.Possible,
            NameMatch.MiddleAsGiven or NameMatch.InitialOnly => cityOk ? Confidence.Likely : stateOk ? Confidence.Possible : null,
            NameMatch.FamilyOnly => cityOk ? Confidence.Possible : null,
            NameMatch.FamilyPart or NameMatch.Misspelling => cityOk || stateOk ? Confidence.Possible : null,
            _ => null,
        };
        if (confidence is null) return null;

        // A middle initial on the record that is not the person's is evidence against, so the match steps down.
        if (given.Value.MiddleConflict && confidence > Confidence.Possible) confidence--;

        var priorName = family.Kind == FamilyKind.Prior;
        return new MatchResult(
            subject.Id,
            confidence.Value,
            nameMatch,
            cityOk,
            stateOk,
            NameChangeDocsLikely: priorName,
            EstateRecord: estate,
            Rationale: Explain(nameMatch, cityOk, stateOk, given.Value.MiddleConflict, record));
    }

    private static string Explain(NameMatch nameMatch, bool cityOk, bool stateOk, bool middleConflict, RawRecord record)
    {
        var name = nameMatch switch
        {
            NameMatch.ExactCurrent => "The given and family names match.",
            NameMatch.ExactPrior => "The given name matches, under a former family name.",
            NameMatch.Alias => "The record uses another given name this person has gone by.",
            NameMatch.MiddleAsGiven => "The record uses this person's middle name as a first name.",
            NameMatch.InitialOnly => "The record has only an initial for the given name.",
            NameMatch.FamilyOnly => "The record carries the family name alone.",
            NameMatch.FamilyPart => "The record is under one part of this person's family name.",
            _ => "The record is under a likely misspelling of this person's family name.",
        };

        var place = cityOk ? " The record's city and state match an address on file."
            : stateOk ? " The record is in a state this person lived in, but the city is not on file."
            : string.IsNullOrWhiteSpace(record.State) ? " The record gives no location."
            : " The record is in a state this person has no address in.";

        var middle = middleConflict ? " The record shows a middle initial that is not this person's." : "";
        return name + place + middle;
    }

    private static bool IsBetter(MatchResult candidate, MatchResult current)
    {
        if (candidate.Confidence != current.Confidence) return candidate.Confidence > current.Confidence;
        return candidate.NameMatch < current.NameMatch;
    }

    // ---------- owner text ----------

    /// <summary>
    /// The owners named in a record. Two people who share a line ("A &amp; B", "A OR B") are separate
    /// owners and are compared separately, so one owner's given name can never be paired with the
    /// other's family name.
    /// </summary>
    private static IEnumerable<IReadOnlyList<string>> OwnerParts(string ownerName)
    {
        var text = (ownerName ?? "").Replace('&', '|').Replace('/', '|').Replace(';', '|');
        foreach (var piece in text.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var current = new List<string>();
            foreach (var token in NameText.Tokens(piece))
            {
                if (token is "AND" or "OR")
                {
                    if (current.Count > 0) yield return current;
                    current = [];
                    continue;
                }

                current.Add(token);
            }

            if (current.Count > 0) yield return current;
        }
    }

    // ---------- family name ----------

    private enum FamilyKind { Current, Prior, Part, Misspelling }

    private readonly record struct FamilyHit(FamilyKind Kind, string MatchedText, IReadOnlyList<string> Remaining);

    private static FamilyHit? MatchFamily(List<string> tokens, Subject subject)
    {
        var current = NameText.Normalize(subject.FamilyName);
        if (Consume(tokens, current) is { } rest) return new FamilyHit(FamilyKind.Current, current, rest);

        foreach (var prior in subject.PriorFamilyNames.Select(NameText.Normalize))
            if (Consume(tokens, prior) is { } priorRest) return new FamilyHit(FamilyKind.Prior, prior, priorRest);

        foreach (var part in NameVariants.FamilyParts(current))
            if (Consume(tokens, part) is { } partRest) return new FamilyHit(FamilyKind.Part, part, partRest);

        foreach (var misspelling in NameVariants.Misspellings(current))
            if (Consume(tokens, misspelling) is { } wrongRest) return new FamilyHit(FamilyKind.Misspelling, misspelling, wrongRest);

        return null;
    }

    /// <summary>
    /// The tokens left once the family name is taken out, or null when it is not there. The name may
    /// be written as its own words, run together, or split in two.
    /// </summary>
    private static List<string>? Consume(List<string> tokens, string family)
    {
        if (family.Length == 0) return null;
        var familyTokens = family.Split(' ');

        for (var i = 0; i + familyTokens.Length <= tokens.Count; i++)
        {
            var match = true;
            for (var j = 0; j < familyTokens.Length; j++)
                if (tokens[i + j] != familyTokens[j]) { match = false; break; }
            if (match) return Without(tokens, i, familyTokens.Length);
        }

        var compact = family.Replace(" ", "", StringComparison.Ordinal);
        if (familyTokens.Length > 1)
        {
            var index = tokens.IndexOf(compact);
            if (index >= 0) return Without(tokens, index, 1);
        }
        else
        {
            for (var i = 0; i + 1 < tokens.Count; i++)
                if (tokens[i] + tokens[i + 1] == compact) return Without(tokens, i, 2);
        }

        return null;
    }

    private static List<string> Without(List<string> tokens, int index, int count)
    {
        var rest = new List<string>(tokens);
        rest.RemoveRange(index, count);
        return rest;
    }

    // ---------- given name ----------

    private enum GivenKind { Exact, Alias, Middle, Initial, None }

    private readonly record struct GivenHit(GivenKind Kind, bool MiddleConflict);

    private static GivenHit? MatchGiven(IReadOnlyList<string> remaining, Subject subject)
    {
        if (remaining.Count == 0) return new GivenHit(GivenKind.None, false);

        var givenTokens = NameText.Tokens(subject.GivenName);
        if (givenTokens.Count == 0) return null;
        var given = givenTokens[0];
        var middles = subject.MiddleNames.SelectMany(NameText.Tokens).ToList();
        var aliases = subject.Aliases.SelectMany(NameText.Tokens).ToHashSet(StringComparer.Ordinal);

        bool IsOwnMiddle(string token) =>
            middles.Contains(token) || (token.Length == 1 && middles.Any(m => m[0] == token[0])) || givenTokens.Skip(1).Contains(token);

        // Whatever sits beside the matched given name must be this person's middle name or initial.
        // A middle that is not theirs counts against the match; it does not end it, because records
        // are keyed by hand. When no middle name is on file there is nothing to compare, so nothing counts.
        bool Conflict(string matched)
        {
            if (middles.Count == 0) return false;
            var others = remaining.Where(t => t != matched).ToList();
            return others.Count > 0 && others.Any(t => !IsOwnMiddle(t));
        }

        if (remaining.Contains(given)) return new GivenHit(GivenKind.Exact, Conflict(given));

        var alias = remaining.FirstOrDefault(aliases.Contains);
        if (alias is not null) return new GivenHit(GivenKind.Alias, Conflict(alias));

        var middle = remaining.FirstOrDefault(t => t.Length > 1 && middles.Contains(t));
        if (middle is not null) return new GivenHit(GivenKind.Middle, false);

        // An initial counts only when the record gives no full first name at all.
        if (remaining.All(t => t.Length == 1) && remaining[0][0] == given[0])
            return new GivenHit(GivenKind.Initial, Conflict(remaining[0]));

        return null;
    }
}
