// The invented household every harness works with: the account holder, a parent who has died, and a
// spouse with no consent basis. It is the same household `claimseeker init` writes as its example.
using ClaimSeeker.Core;

internal static class SampleHousehold
{
    public const string Json = """
        {
          "version": 1,
          "attestation": {
            "statement": "These facts are true to the best of my knowledge.",
            "attestedBy": "Jane Quinn Sample",
            "attestedOn": "2026-11-01"
          },
          "subjects": [
            {
              "id": "self", "relationship": "self", "givenName": "Jane", "middleNames": ["Quinn"], "familyName": "Sample",
              "priorFamilyNames": ["Example"], "aliases": ["Janie"], "consent": "self",
              "addresses": [
                { "city": "Springfield", "state": "IL", "zip": "62701", "fromYear": 2019 },
                { "city": "Columbus", "state": "OH", "fromYear": 2011, "toYear": 2019 }
              ],
              "employers": ["Acme Widgets"]
            },
            {
              "id": "dad", "relationship": "parent", "givenName": "Robert", "familyName": "Sample", "deceased": true,
              "consent": "deceasedNextOfKin", "addresses": [ { "city": "Dayton", "state": "OH" } ]
            },
            {
              "id": "alex", "relationship": "spouse", "givenName": "Alex", "familyName": "Sample", "consent": "none",
              "addresses": [ { "city": "Springfield", "state": "IL" } ]
            }
          ]
        }
        """;

    public static Household Load() => HouseholdJson.Parse(Json);

    /// <summary>A one-person household, for checks about a particular kind of name.</summary>
    public static Household Of(Subject subject) => new()
    {
        Attestation = new Attestation { Statement = "True.", AttestedBy = subject.FullName, AttestedOn = "2026-11-01" },
        Subjects = [subject],
    };

    public static Subject Person(string given, string family, params (string City, string State)[] addresses) => new()
    {
        Id = "self",
        Relationship = Relationship.Self,
        GivenName = given,
        FamilyName = family,
        Consent = ConsentBasis.Self,
        Addresses = addresses.Select(a => new PostalAddress { City = a.City, State = a.State }).ToList(),
    };
}
