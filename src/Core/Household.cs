using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaimSeeker.Core;

/// <summary>How a person in the household is related to the account holder.</summary>
public enum Relationship { Self, Spouse, Parent, Sibling, Grandparent, Child, Other }

/// <summary>
/// Why the engine may search for a person. There is no default that permits a search: a person with
/// <see cref="None"/> is listed but never looked up.
/// </summary>
public enum ConsentBasis
{
    /// <summary>No basis recorded. The engine will not search for this person.</summary>
    None,
    /// <summary>The account holder searching for themselves.</summary>
    Self,
    /// <summary>A living adult who told the account holder they may search on their behalf.</summary>
    ConsentGiven,
    /// <summary>The account holder is this person's parent or legal guardian.</summary>
    Guardian,
    /// <summary>The person has died and the account holder is their heir, next of kin or executor.</summary>
    DeceasedNextOfKin,
}

/// <summary>Somewhere a person lived. City and state are what unclaimed-property records carry.</summary>
public sealed record PostalAddress
{
    public required string City { get; init; }
    public required string State { get; init; }
    public string? Zip { get; init; }
    public string? Street { get; init; }
    public int? FromYear { get; init; }
    public int? ToYear { get; init; }

    public string CityState => $"{City}, {State}";
}

/// <summary>One person the household searches for.</summary>
public sealed record Subject
{
    /// <summary>Short stable handle chosen by the user, such as "self" or "dad".</summary>
    public required string Id { get; init; }
    public required Relationship Relationship { get; init; }
    public required string GivenName { get; init; }
    public required string FamilyName { get; init; }
    public IReadOnlyList<string> MiddleNames { get; init; } = [];

    /// <summary>Maiden and other former family names.</summary>
    public IReadOnlyList<string> PriorFamilyNames { get; init; } = [];

    /// <summary>Other given names this person has gone by. Only what the user typed; never inferred.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    public string? Suffix { get; init; }
    public bool Deceased { get; init; }
    public ConsentBasis Consent { get; init; } = ConsentBasis.None;
    public IReadOnlyList<PostalAddress> Addresses { get; init; } = [];
    public IReadOnlyList<string> Employers { get; init; } = [];

    [JsonIgnore]
    public string FullName
    {
        get
        {
            var parts = new List<string> { GivenName };
            parts.AddRange(MiddleNames);
            parts.Add(FamilyName);
            if (!string.IsNullOrWhiteSpace(Suffix)) parts.Add(Suffix);
            return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    /// <summary>The two-letter states this person has lived in, upper case, without repeats.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> StatesLived =>
        Addresses.Select(a => a.State.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>True when the engine has a recorded basis to search for this person.</summary>
    [JsonIgnore]
    public bool MaySearch => Consent != ConsentBasis.None;
}

/// <summary>
/// The account holder's statement that the household's facts are true and that they may search for
/// each living person in it. Without one, nothing in the household counts as attested and the
/// Attestation Gate blocks every draft.
/// </summary>
public sealed record Attestation
{
    public required string Statement { get; init; }
    public required string AttestedBy { get; init; }
    /// <summary>ISO date, yyyy-MM-dd.</summary>
    public required string AttestedOn { get; init; }
}

/// <summary>The people this install searches for. The engine's only source of truth about them.</summary>
public sealed record Household
{
    public int Version { get; init; } = 1;
    public Attestation? Attestation { get; init; }
    public IReadOnlyList<Subject> Subjects { get; init; } = [];

    public Subject? Find(string subjectId) =>
        Subjects.FirstOrDefault(s => string.Equals(s.Id, subjectId, StringComparison.Ordinal));

    [JsonIgnore]
    public Subject? Self => Subjects.FirstOrDefault(s => s.Relationship == Relationship.Self);
}

/// <summary>
/// Reads and writes the household file. Reading is strict: an unknown property is an error, so a
/// misspelled field name cannot silently drop a prior name or a consent basis.
/// </summary>
public static class HouseholdJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>The largest household file the engine will read.</summary>
    public const int MaxBytes = 512 * 1024;

    public static Household Parse(string json)
    {
        if (json.Length > MaxBytes) throw new HouseholdFormatException($"The household file is larger than {MaxBytes / 1024} KB.");
        try
        {
            return JsonSerializer.Deserialize<Household>(json, Options)
                   ?? throw new HouseholdFormatException("The household file is empty.");
        }
        catch (JsonException ex)
        {
            throw new HouseholdFormatException($"The household file could not be read: {ex.Message}");
        }
    }

    public static string Serialize(Household household) => JsonSerializer.Serialize(household, Options);
}

public sealed class HouseholdFormatException(string message) : Exception(message);
