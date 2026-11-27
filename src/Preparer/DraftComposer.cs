using ClaimSeeker.Core;
using ClaimSeeker.Store;
using ClaimSeeker.Verifier;

namespace ClaimSeeker.Preparer;

public sealed class DraftCompositionException(string message) : Exception(message);

/// <summary>
/// Fills a letter's slots for one finding. It only gathers: each slot gets a fact id or a finding
/// field, and the value that fact or field holds. Whether the result is allowed to become a draft is
/// the Attestation Gate's decision, made afterwards against the same facts and the same finding.
/// </summary>
public static class DraftComposer
{
    public static DraftRequest Compose(DraftTemplate template, FindingRow finding, Household household)
    {
        var subject = household.Find(finding.SubjectId)
                      ?? throw new DraftCompositionException("The person this finding was matched to is no longer in the household.");
        var sender = SenderFor(template, subject, household);

        var bindings = new List<SlotBinding>();
        void Bind(string slot, string reference, string? value)
        {
            if (template.Slot(slot) is not null && !string.IsNullOrEmpty(value)) bindings.Add(new SlotBinding(slot, reference, value));
        }

        Bind("owner_name", "ownerName", finding.OwnerName);
        Bind("holder_name", "holderName", finding.HolderName);
        // A place is a city and a state together. One without the other says nothing useful.
        if (!string.IsNullOrEmpty(finding.City) && !string.IsNullOrEmpty(finding.UsState))
        {
            Bind("record_city", "city", finding.City);
            Bind("record_state", "state", finding.UsState);
        }

        Bind("sender_name", HouseholdFacts.NameId(sender.Id), sender.FullName);
        Bind("subject_name", HouseholdFacts.NameId(subject.Id), subject.FullName);
        Bind("relationship", HouseholdFacts.RelationshipId(subject.Id), HouseholdFacts.RelationshipWords(subject.Relationship));
        if (subject.Deceased) Bind("deceased_status", HouseholdFacts.DeceasedId(subject.Id), "deceased");

        if (finding.NameChangeDocsLikely && PriorNameIndex(subject, finding.OwnerName) is { } priorIndex)
            Bind("prior_name", HouseholdFacts.PriorNameId(subject.Id, priorIndex), $"{subject.GivenName} {subject.PriorFamilyNames[priorIndex]}");

        if (AddressIndex(subject, finding.City, finding.UsState) is { } addressIndex)
        {
            var address = subject.Addresses[addressIndex].CityState;
            Bind("sender_address", HouseholdFacts.AddressId(subject.Id, addressIndex), address);
            Bind("subject_address", HouseholdFacts.AddressId(subject.Id, addressIndex), address);
        }

        return new DraftRequest
        {
            Template = template,
            FindingId = finding.Id,
            SubjectId = subject.Id,
            SenderSubjectId = sender.Id,
            Confidence = Enum.Parse<Confidence>(finding.Confidence),
            CaseState = Enum.Parse<CaseState>(finding.CaseState),
            Bindings = bindings,
        };
    }

    /// <summary>What the gate needs to check a draft for this finding.</summary>
    public static GateContext ContextFor(FindingRow finding, Household household) => new(
        HouseholdFacts.Derive(household),
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ownerName"] = finding.OwnerName,
            ["holderName"] = finding.HolderName,
            ["city"] = finding.City,
            ["state"] = finding.UsState,
        },
        new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// The letter template that fits a finding: an heir's letter when the person has died, otherwise
    /// the letter a person writes about a record in their own name.
    /// </summary>
    public static string DefaultTemplateFor(FindingRow finding, Household household) =>
        household.Find(finding.SubjectId) is { Deceased: true } ? LetterTemplates.HeirInquiry : LetterTemplates.HolderInquiry;

    /// <summary>
    /// Who signs. A person signs letters about their own records. The account holder signs for a
    /// relative who has died. Nobody signs for a living adult: their letter is written in their name,
    /// for them to send.
    /// </summary>
    private static Subject SenderFor(DraftTemplate template, Subject subject, Household household)
    {
        if (template.Id == LetterTemplates.HeirInquiry)
        {
            if (!subject.Deceased) throw new DraftCompositionException("An heir's letter is only for a person marked deceased.");
            return household.Self ?? throw new DraftCompositionException("The household has no account holder to sign the letter.");
        }

        if (template.Id == LetterTemplates.HolderInquiry && subject.Deceased)
            throw new DraftCompositionException("This person is marked deceased; use the heir's letter.");

        return subject.Deceased
            ? household.Self ?? throw new DraftCompositionException("The household has no account holder to sign the letter.")
            : subject;
    }

    private static int? PriorNameIndex(Subject subject, string ownerName)
    {
        var owner = " " + NameText.Normalize(ownerName) + " ";
        var compact = NameText.Compact(ownerName);
        for (var i = 0; i < subject.PriorFamilyNames.Count; i++)
        {
            var prior = NameText.Normalize(subject.PriorFamilyNames[i]);
            if (prior.Length == 0) continue;
            if (owner.Contains(" " + prior + " ", StringComparison.Ordinal) || compact.Contains(NameText.Compact(prior), StringComparison.Ordinal))
                return i;
        }

        return null;
    }

    private static int? AddressIndex(Subject subject, string? city, string? state)
    {
        var recordCity = NameText.Normalize(city);
        var recordState = NameText.Normalize(state);
        if (recordCity.Length == 0 || recordState.Length == 0) return null;

        for (var i = 0; i < subject.Addresses.Count; i++)
            if (NameText.Normalize(subject.Addresses[i].City) == recordCity && NameText.Normalize(subject.Addresses[i].State) == recordState)
                return i;
        return null;
    }
}
