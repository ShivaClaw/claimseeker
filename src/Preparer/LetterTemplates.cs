using ClaimSeeker.Core;
using ClaimSeeker.Verifier;

namespace ClaimSeeker.Preparer;

/// <summary>
/// The letters ClaimSeeker can draft. Every word outside a slot is fixed here and reviewed here.
/// All three ask; none states that the property belongs to the sender. A letter that did would be
/// an assertion-class template and would need a match the engine trusts.
///
/// Each letter tells the holder that identity documents will go through the holder's secure process
/// and not by email. That sentence is part of the product, not politeness: it is why no draft ever
/// needs an identifier in it.
/// </summary>
public static class LetterTemplates
{
    public const string HolderInquiry = "holder-inquiry";
    public const string HeirInquiry = "heir-inquiry";
    public const string ClaimFollowUp = "claim-follow-up";

    private static readonly SlotSpec OwnerName = new() { Name = "owner_name", Source = SlotSource.FindingField, Field = "ownerName" };
    private static readonly SlotSpec RecordCity = new() { Name = "record_city", Source = SlotSource.FindingField, Field = "city" };
    private static readonly SlotSpec RecordState = new() { Name = "record_state", Source = SlotSource.FindingField, Field = "state" };
    private static readonly SlotSpec HolderName = new() { Name = "holder_name", Source = SlotSource.FindingField, Field = "holderName" };
    private static readonly SlotSpec SenderName = new() { Name = "sender_name", Source = SlotSource.Fact, FactKind = FactKind.Name, Person = SlotPerson.Sender };

    private static readonly IReadOnlyList<string> Place = ["record_city", "record_state"];

    public static IReadOnlyList<DraftTemplate> All { get; } =
    [
        new DraftTemplate
        {
            Id = HolderInquiry,
            Title = "Ask a holder about a record in your own name",
            Class = TemplateClass.Inquiry,
            MinConfidence = Confidence.Possible,
            SubjectLine = "Inquiry about unclaimed property reported for {owner_name}",
            Slots =
            [
                OwnerName, RecordCity, RecordState, HolderName, SenderName,
                new SlotSpec { Name = "prior_name", Source = SlotSource.Fact, FactKind = FactKind.PriorName, Person = SlotPerson.Sender },
                new SlotSpec { Name = "sender_address", Source = SlotSource.Fact, FactKind = FactKind.Address, Person = SlotPerson.Sender },
            ],
            Paragraphs =
            [
                new("Hello,"),
                new("I am writing about unclaimed property reported under the name \"{owner_name}\", with a last known address in {record_city}, {record_state}.", When: Place),
                new("I am writing about unclaimed property reported under the name \"{owner_name}\".", Unless: Place),
                new("The record names {holder_name} as the holder.", When: ["holder_name"]),
                new("I believe this record may refer to me. My name is {sender_name}."),
                new("I was formerly known as {prior_name}.", When: ["prior_name"]),
                new("I have lived in {sender_address}.", When: ["sender_address"]),
                new("Could you tell me whether this property is still held, and what you need from me to verify ownership and claim it? I will send identity documents through your secure process, not by email."),
                new("Thank you,\n{sender_name}"),
            ],
        },
        new DraftTemplate
        {
            Id = HeirInquiry,
            Title = "Ask a holder about a record in the name of a relative who has died",
            Class = TemplateClass.Inquiry,
            MinConfidence = Confidence.Possible,
            SubjectLine = "Inquiry about unclaimed property reported for {owner_name}",
            Slots =
            [
                OwnerName, RecordCity, RecordState, HolderName, SenderName,
                new SlotSpec { Name = "subject_name", Source = SlotSource.Fact, FactKind = FactKind.Name, Person = SlotPerson.Subject },
                new SlotSpec { Name = "relationship", Source = SlotSource.Fact, FactKind = FactKind.Relationship, Person = SlotPerson.Subject },
                new SlotSpec { Name = "deceased_status", Source = SlotSource.Fact, FactKind = FactKind.Deceased, Person = SlotPerson.Subject },
                new SlotSpec { Name = "prior_name", Source = SlotSource.Fact, FactKind = FactKind.PriorName, Person = SlotPerson.Subject },
                new SlotSpec { Name = "subject_address", Source = SlotSource.Fact, FactKind = FactKind.Address, Person = SlotPerson.Subject },
            ],
            Paragraphs =
            [
                new("Hello,"),
                new("I am writing about unclaimed property reported under the name \"{owner_name}\", with a last known address in {record_city}, {record_state}.", When: Place),
                new("I am writing about unclaimed property reported under the name \"{owner_name}\".", Unless: Place),
                new("The record names {holder_name} as the holder.", When: ["holder_name"]),
                new("I believe this record may refer to {subject_name}, {relationship}, who is {deceased_status}. I am writing as next of kin. My name is {sender_name}."),
                new("{subject_name} was formerly known as {prior_name}.", When: ["prior_name"]),
                new("{subject_name} lived in {subject_address}.", When: ["subject_address"]),
                new("Could you tell me whether this property is still held, and what a relative needs to provide to claim it for the estate? I will send any documents through your secure process, not by email."),
                new("Thank you,\n{sender_name}"),
            ],
        },
        new DraftTemplate
        {
            Id = ClaimFollowUp,
            Title = "Ask where a claim you filed stands",
            Class = TemplateClass.Inquiry,
            MinConfidence = Confidence.Possible,
            SubjectLine = "Status of an unclaimed property claim for {owner_name}",
            AllowedCaseStates = [CaseState.FILED_BY_USER, CaseState.IN_REVIEW, CaseState.NEEDS_DOCUMENTS],
            Slots = [OwnerName, RecordCity, RecordState, SenderName],
            Paragraphs =
            [
                new("Hello,"),
                new("I filed a claim for unclaimed property reported under the name \"{owner_name}\", with a last known address in {record_city}, {record_state}.", When: Place),
                new("I filed a claim for unclaimed property reported under the name \"{owner_name}\".", Unless: Place),
                new("Could you tell me where the claim stands, and whether you need anything further from me? I will send any documents through your secure process, not by email."),
                new("Thank you,\n{sender_name}"),
            ],
        },
    ];

    public static DraftTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
