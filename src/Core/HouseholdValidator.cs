using System.Globalization;

namespace ClaimSeeker.Core;

/// <summary>What is wrong with a household, and what the engine will do about the rest of it.</summary>
public sealed record HouseholdValidation(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Checks a household before the engine acts on it. Errors stop the import. Warnings do not, but each
/// names something the engine will decline to do, such as searching for a person with no consent basis.
/// </summary>
public static class HouseholdValidator
{
    public const int MaxSubjects = 24;
    public const int MaxNameLength = 80;
    private const int MaxListItems = 12;

    /// <summary>USPS codes for the states, the District of Columbia and the territories.</summary>
    public static readonly IReadOnlySet<string> UsStateCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL", "GA", "HI", "ID", "IL", "IN", "IA", "KS",
        "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC",
        "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
        "PR", "GU", "VI", "AS", "MP",
    };

    public static HouseholdValidation Validate(Household household, DateOnly today)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (household.Version != 1) errors.Add($"Unsupported household version {household.Version}; this build reads version 1.");

        ValidateAttestation(household.Attestation, today, errors);

        if (household.Subjects.Count == 0) errors.Add("The household lists no one to search for.");
        if (household.Subjects.Count > MaxSubjects) errors.Add($"The household lists more than {MaxSubjects} people.");

        var selfCount = household.Subjects.Count(s => s.Relationship == Relationship.Self);
        if (selfCount != 1) errors.Add($"Exactly one person must have the relationship \"self\"; found {selfCount}.");

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var subject in household.Subjects)
        {
            var label = string.IsNullOrWhiteSpace(subject.Id) ? "(no id)" : subject.Id;
            if (!IsSlug(subject.Id)) errors.Add($"{label}: the id must be 1-32 lower-case letters, digits or hyphens.");
            else if (!seenIds.Add(subject.Id)) errors.Add($"{label}: this id is used more than once.");

            ValidateName(label, "givenName", subject.GivenName, errors);
            ValidateName(label, "familyName", subject.FamilyName, errors);
            ValidateNameList(label, "middleNames", subject.MiddleNames, errors);
            ValidateNameList(label, "priorFamilyNames", subject.PriorFamilyNames, errors);
            ValidateNameList(label, "aliases", subject.Aliases, errors);
            ValidateNameList(label, "employers", subject.Employers, errors, allowDigits: true);
            if (subject.Suffix is { } suffix && !NameText.Suffixes.Contains(NameText.Normalize(suffix)))
                errors.Add($"{label}: the suffix \"{suffix}\" is not one of Jr, Sr, II, III, IV, V.");

            if (subject.Addresses.Count > MaxListItems) errors.Add($"{label}: more than {MaxListItems} addresses.");
            for (var i = 0; i < subject.Addresses.Count; i++)
                ValidateAddress($"{label}: address {i + 1}", subject.Addresses[i], today, errors);

            ValidateConsent(label, subject, errors, warnings);

            if (subject.Addresses.Count == 0)
                warnings.Add($"{label}: no addresses listed, so no match for this person can rise above \"possible\".");
        }

        foreach (var text in AllText(household))
            if (SensitiveIdentifiers.Find(text) is { } kind)
                errors.Add($"The household contains what looks like a {kind}. ClaimSeeker never stores these; remove it.");

        return new HouseholdValidation(errors, warnings);
    }

    private static void ValidateAttestation(Attestation? attestation, DateOnly today, List<string> errors)
    {
        if (attestation is null)
        {
            errors.Add("The household has no attestation. The engine acts only on facts the account holder has attested.");
            return;
        }

        if (string.IsNullOrWhiteSpace(attestation.Statement)) errors.Add("The attestation statement is empty.");
        if (string.IsNullOrWhiteSpace(attestation.AttestedBy)) errors.Add("The attestation does not say who made it.");
        if (!DateOnly.TryParseExact(attestation.AttestedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on))
            errors.Add("The attestation date must be written yyyy-MM-dd.");
        else if (on > today.AddDays(1))
            errors.Add("The attestation date is in the future.");
    }

    private static void ValidateConsent(string label, Subject subject, List<string> errors, List<string> warnings)
    {
        switch (subject.Relationship, subject.Deceased, subject.Consent)
        {
            case (_, _, ConsentBasis.None):
                warnings.Add($"{label}: no consent basis recorded, so the engine will not search for this person.");
                break;
            case (Relationship.Self, _, not ConsentBasis.Self):
                errors.Add($"{label}: the account holder's own consent basis must be \"self\".");
                break;
            case (not Relationship.Self, _, ConsentBasis.Self):
                errors.Add($"{label}: only the account holder can have the consent basis \"self\".");
                break;
            case (_, true, not ConsentBasis.DeceasedNextOfKin):
                errors.Add($"{label}: a person marked deceased needs the consent basis \"deceasedNextOfKin\".");
                break;
            case (_, false, ConsentBasis.DeceasedNextOfKin):
                errors.Add($"{label}: \"deceasedNextOfKin\" applies only to a person marked deceased.");
                break;
        }

        if (subject is { Relationship: Relationship.Self, Deceased: true })
            errors.Add($"{label}: the account holder cannot be marked deceased.");
    }

    private static void ValidateName(string label, string field, string? value, List<string> errors, bool allowDigits = false)
    {
        if (string.IsNullOrWhiteSpace(value)) { errors.Add($"{label}: {field} is empty."); return; }
        if (value.Length > MaxNameLength) errors.Add($"{label}: {field} is longer than {MaxNameLength} characters.");
        var normalized = NameText.Normalize(value);
        if (normalized.Length == 0) errors.Add($"{label}: {field} has no letters in it.");
        else if (!allowDigits && normalized.Any(char.IsAsciiDigit)) errors.Add($"{label}: {field} contains digits.");
    }

    private static void ValidateNameList(string label, string field, IReadOnlyList<string> values, List<string> errors, bool allowDigits = false)
    {
        if (values.Count > MaxListItems) errors.Add($"{label}: more than {MaxListItems} entries in {field}.");
        foreach (var value in values) ValidateName(label, field, value, errors, allowDigits);
    }

    private static void ValidateAddress(string label, PostalAddress address, DateOnly today, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(address.City) || address.City.Length > MaxNameLength) errors.Add($"{label}: the city is empty or too long.");
        var state = (address.State ?? "").Trim().ToUpperInvariant();
        if (!UsStateCodes.Contains(state)) errors.Add($"{label}: \"{address.State}\" is not a two-letter US state or territory code.");
        if (address.Zip is { } zip && !IsZip(zip)) errors.Add($"{label}: the ZIP code must be 5 digits or ZIP+4.");
        if (address.Street is { Length: > 120 }) errors.Add($"{label}: the street line is too long.");

        foreach (var year in new[] { address.FromYear, address.ToYear })
            if (year is { } y && (y < 1900 || y > today.Year + 1)) errors.Add($"{label}: the year {y} is out of range.");
        if (address is { FromYear: { } from, ToYear: { } to } && from > to) errors.Add($"{label}: the years are in the wrong order.");
    }

    private static bool IsSlug(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 32 && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static bool IsZip(string zip)
    {
        if (zip.Length == 5) return zip.All(char.IsAsciiDigit);
        return zip.Length == 10 && zip[5] == '-' && zip[..5].All(char.IsAsciiDigit) && zip[6..].All(char.IsAsciiDigit);
    }

    private static IEnumerable<string> AllText(Household household)
    {
        if (household.Attestation is { } a) { yield return a.Statement; yield return a.AttestedBy; }
        foreach (var s in household.Subjects)
        {
            yield return s.Id;
            yield return s.GivenName;
            yield return s.FamilyName;
            foreach (var v in s.MiddleNames) yield return v;
            foreach (var v in s.PriorFamilyNames) yield return v;
            foreach (var v in s.Aliases) yield return v;
            foreach (var v in s.Employers) yield return v;
            foreach (var address in s.Addresses)
            {
                yield return address.City;
                if (address.Street is { } street) yield return street;
            }
        }
    }
}
