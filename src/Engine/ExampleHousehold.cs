namespace ClaimSeeker.Engine;

/// <summary>
/// The household file `init` writes for a person to edit. Everyone in it is invented. It shows the
/// three cases that matter: the account holder, a relative who has died, and a living adult who has
/// not yet agreed to be searched for and so is not.
/// </summary>
internal static class ExampleHousehold
{
    public const string DatePlaceholder = "{today}";

    public const string Json = """
        {
          "version": 1,
          "attestation": {
            "statement": "These facts are true to the best of my knowledge, and each living adult listed here with a consent basis has agreed that I may search on their behalf.",
            "attestedBy": "Jane Quinn Sample",
            "attestedOn": "{today}"
          },
          "subjects": [
            {
              "id": "self",
              "relationship": "self",
              "givenName": "Jane",
              "middleNames": ["Quinn"],
              "familyName": "Sample",
              "priorFamilyNames": ["Example"],
              "aliases": ["Janie"],
              "consent": "self",
              "addresses": [
                { "city": "Springfield", "state": "IL", "zip": "62701", "fromYear": 2019 },
                { "city": "Columbus", "state": "OH", "fromYear": 2011, "toYear": 2019 }
              ],
              "employers": ["Acme Widgets"]
            },
            {
              "id": "dad",
              "relationship": "parent",
              "givenName": "Robert",
              "familyName": "Sample",
              "deceased": true,
              "consent": "deceasedNextOfKin",
              "addresses": [
                { "city": "Dayton", "state": "OH" }
              ]
            },
            {
              "id": "alex",
              "relationship": "spouse",
              "givenName": "Alex",
              "familyName": "Sample",
              "consent": "none",
              "addresses": [
                { "city": "Springfield", "state": "IL" }
              ]
            }
          ]
        }
        """;

    public static string For(DateOnly today) =>
        Json.Replace(DatePlaceholder, today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
