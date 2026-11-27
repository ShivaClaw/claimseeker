namespace ClaimSeeker.Core;

public enum FactKind { Name, PriorName, Alias, Address, Employer, Relationship, Deceased }

/// <summary>
/// One thing the account holder has attested about a person in the household. These are the only
/// statements about a person that a draft may make: the Attestation Gate checks every personal slot
/// in a draft against this list.
/// </summary>
public sealed record AttestedFact(string Id, string SubjectId, FactKind Kind, string Value);

public static class HouseholdFacts
{
    /// <summary>
    /// The attested facts in a household, with ids that stay stable while the household's lists keep
    /// their order. A household without an attestation yields nothing: unattested text is not a fact.
    /// </summary>
    public static IReadOnlyList<AttestedFact> Derive(Household household)
    {
        if (household.Attestation is null) return [];

        var facts = new List<AttestedFact>();
        foreach (var s in household.Subjects)
        {
            facts.Add(new AttestedFact(NameId(s.Id), s.Id, FactKind.Name, s.FullName));
            facts.Add(new AttestedFact(RelationshipId(s.Id), s.Id, FactKind.Relationship, RelationshipWords(s.Relationship)));

            for (var i = 0; i < s.PriorFamilyNames.Count; i++)
                facts.Add(new AttestedFact(PriorNameId(s.Id, i), s.Id, FactKind.PriorName, $"{s.GivenName} {s.PriorFamilyNames[i]}"));
            for (var i = 0; i < s.Aliases.Count; i++)
                facts.Add(new AttestedFact($"{s.Id}/alias/{i}", s.Id, FactKind.Alias, s.Aliases[i]));
            for (var i = 0; i < s.Addresses.Count; i++)
                facts.Add(new AttestedFact(AddressId(s.Id, i), s.Id, FactKind.Address, s.Addresses[i].CityState));
            for (var i = 0; i < s.Employers.Count; i++)
                facts.Add(new AttestedFact($"{s.Id}/employer/{i}", s.Id, FactKind.Employer, s.Employers[i]));

            if (s.Deceased) facts.Add(new AttestedFact(DeceasedId(s.Id), s.Id, FactKind.Deceased, "deceased"));
        }

        return facts;
    }

    public static string NameId(string subjectId) => $"{subjectId}/name";
    public static string RelationshipId(string subjectId) => $"{subjectId}/relationship";
    public static string DeceasedId(string subjectId) => $"{subjectId}/deceased";
    public static string PriorNameId(string subjectId, int index) => $"{subjectId}/prior-name/{index}";
    public static string AddressId(string subjectId, int index) => $"{subjectId}/address/{index}";

    /// <summary>How the account holder refers to this person in a letter.</summary>
    public static string RelationshipWords(Relationship relationship) => relationship switch
    {
        Relationship.Self => "myself",
        Relationship.Spouse => "my spouse",
        Relationship.Parent => "my parent",
        Relationship.Sibling => "my sibling",
        Relationship.Grandparent => "my grandparent",
        Relationship.Child => "my child",
        _ => "my relative",
    };
}
