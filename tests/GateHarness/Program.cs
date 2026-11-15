// GateHarness: the Attestation Gate. Every way a letter can be wrong is tried, one at a time, and
// each must be blocked for its own reason. Then the letters the product actually writes are put
// through it for the people and findings they are meant for.
using System.Reflection;
using ClaimSeeker.Core;
using ClaimSeeker.Preparer;
using ClaimSeeker.Store;
using ClaimSeeker.Verifier;
using static Harness;

Title("ClaimSeeker Attestation Gate");
var household = SampleHousehold.Load();

FindingRow Finding(string subject = "self", string owner = "SAMPLE JANE", string? holder = "PRAIRIE STATE ELECTRIC COOP",
    string? city = "SPRINGFIELD", string? state = "IL", string confidence = "Strong", string caseState = "PACKET_READY", bool nameChange = false) => new()
{
    Id = "f_test",
    SubjectId = subject,
    SourceId = "missingmoney",
    DedupeKey = "d_test",
    OwnerName = owner,
    HolderName = holder,
    City = city,
    UsState = state,
    Family = nameof(PropertyFamily.Utility),
    Confidence = confidence,
    NameMatch = "ExactCurrent",
    Rationale = "",
    NameChangeDocsLikely = nameChange,
    QueryKey = "missingmoney|SAMPLE||",
    CaseState = caseState,
    FirstSeen = "2026-11-15T10:00:00Z",
    LastSeen = "2026-11-15T10:00:00Z",
    UpdatedAt = "2026-11-15T10:00:00Z",
};

GateResult Gate(FindingRow finding, string templateId, Household? people = null, Func<DraftRequest, DraftRequest>? change = null, GateContext? context = null)
{
    people ??= household;
    var request = DraftComposer.Compose(LetterTemplates.Find(templateId)!, finding, people);
    return AttestationGate.Evaluate(change is null ? request : change(request), context ?? DraftComposer.ContextFor(finding, people));
}

bool Blocked(GateResult result, string code) => !result.Passed && result.Draft is null && result.Violations.Any(v => v.Code == code);
string Codes(GateResult result) => string.Join(",", result.Violations.Select(v => v.Code));

// ---------- the built-in letters ----------
Section("The letters themselves are sound");
foreach (var template in LetterTemplates.All)
{
    Check($"{template.Id}: nothing is wrong with the template", AttestationGate.TemplateProblems(template).Count == 0, string.Join(" ", AttestationGate.TemplateProblems(template)));
    Check($"{template.Id}: it asks; it does not state ownership", template.Class == TemplateClass.Inquiry);
    Check($"{template.Id}: it says identity documents go through the holder's secure process, not by email",
        template.Paragraphs.Any(p => p.Text.Contains("secure process, not by email", StringComparison.Ordinal)));
    Check($"{template.Id}: every personal slot is tied to a kind of fact", template.Slots.Where(s => s.Source == SlotSource.Fact).All(s => s.FactKind is not null));
}
Check("there is a letter for one's own record, for an heir, and for a filed claim",
    string.Join(",", LetterTemplates.All.Select(t => t.Id)) == "holder-inquiry,heir-inquiry,claim-follow-up");
Check("an unknown letter is not found", LetterTemplates.Find("demand-letter") is null);

// ---------- letters that should pass ----------
Section("A letter backed by attested facts and the source's own words passes");
{
    var result = Gate(Finding(), LetterTemplates.HolderInquiry);
    Check("it passes", result.Passed, Codes(result));
    var draft = result.Draft!;
    Equal("the subject names the record", draft.SubjectLine, "Inquiry about unclaimed property reported for SAMPLE JANE");
    Check("it quotes the record's name and place", draft.Body.Contains("reported under the name \"SAMPLE JANE\", with a last known address in SPRINGFIELD, IL.", StringComparison.Ordinal));
    Check("it names the holder as the source did", draft.Body.Contains("The record names PRAIRIE STATE ELECTRIC COOP as the holder.", StringComparison.Ordinal));
    Check("it gives the sender's attested name", draft.Body.Contains("My name is Jane Quinn Sample.", StringComparison.Ordinal));
    Check("it gives the attested address that matches the record", draft.Body.Contains("I have lived in Springfield, IL.", StringComparison.Ordinal));
    Check("it asks, and does not claim", draft.Body.Contains("may refer to me", StringComparison.Ordinal) && !draft.Body.Contains("belongs to me", StringComparison.OrdinalIgnoreCase));
    Check("it is signed by the sender", draft.Body.EndsWith("Thank you,\nJane Quinn Sample", StringComparison.Ordinal));
    Check("no slot is left open", !draft.Body.Contains('{') && !draft.SubjectLine.Contains('{'));
    Check("the recipient is left for the person to fill in", draft.Recipient is null);
    Check("a former name is not mentioned when the record is under the current one", !draft.Body.Contains("formerly known", StringComparison.Ordinal));
}
{
    var result = Gate(Finding(owner: "EXAMPLE JANE", city: "COLUMBUS", state: "OH", nameChange: true), LetterTemplates.HolderInquiry);
    Check("a record under a former name passes", result.Passed, Codes(result));
    Check("and the letter gives the attested former name", result.Draft!.Body.Contains("I was formerly known as Jane Example.", StringComparison.Ordinal));
    Check("and the matching address", result.Draft.Body.Contains("I have lived in Columbus, OH.", StringComparison.Ordinal));
}
{
    var result = Gate(Finding(holder: null, city: null, state: null, confidence: "Possible"), LetterTemplates.HolderInquiry);
    Check("a possible match with no holder or place can still ask", result.Passed, Codes(result));
    Check("the letter drops the sentences it has nothing for", !result.Draft!.Body.Contains("as the holder", StringComparison.Ordinal)
        && !result.Draft.Body.Contains("last known address", StringComparison.Ordinal) && !result.Draft.Body.Contains("I have lived in", StringComparison.Ordinal));
    Check("and still reads as a whole letter", result.Draft.Body.StartsWith("Hello,\n\nI am writing about unclaimed property reported under the name \"SAMPLE JANE\".", StringComparison.Ordinal));
}
{
    var result = Gate(Finding(subject: "dad", owner: "SAMPLE ROBERT", city: "DAYTON", state: "OH"), LetterTemplates.HeirInquiry);
    Check("an heir's letter for a parent who has died passes", result.Passed, Codes(result));
    Check("it is signed by the account holder", result.Draft!.SenderSubjectId == "self");
    Check("it states the attested relationship and that he has died",
        result.Draft.Body.Contains("may refer to Robert Sample, my parent, who is deceased. I am writing as next of kin. My name is Jane Quinn Sample.", StringComparison.Ordinal));
    Check("it gives his attested address, not hers", result.Draft.Body.Contains("Robert Sample lived in Dayton, OH.", StringComparison.Ordinal));
}
{
    var result = Gate(Finding(caseState: "FILED_BY_USER"), LetterTemplates.ClaimFollowUp);
    Check("a follow-up on a claim the person reported filing passes", result.Passed, Codes(result));
    Check("it says a claim was filed", result.Draft!.Body.Contains("I filed a claim for unclaimed property", StringComparison.Ordinal));
}
Equal("the letter chosen for a parent who has died is the heir's", DraftComposer.DefaultTemplateFor(Finding(subject: "dad"), household), LetterTemplates.HeirInquiry);
Equal("the letter chosen for one's own record is the holder inquiry", DraftComposer.DefaultTemplateFor(Finding(), household), LetterTemplates.HolderInquiry);
Check("the gate gives the same answer every time it is asked",
    Gate(Finding(), LetterTemplates.HolderInquiry).Draft!.Body == Gate(Finding(), LetterTemplates.HolderInquiry).Draft!.Body);

// ---------- facts ----------
Section("Every sentence about a person needs an attested fact, of the right kind, about the right person");
Check("no attestation, no letter",
    Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, household with { Attestation = null }), "fact-not-attested"));
Check("a name that was changed after it was attested",
    Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Bindings = r.Bindings.Select(b => b.Slot == "sender_name" ? b with { Value = "Jane Q. Sample, Esq." } : b).ToList() }), "fact-value-mismatch"));
Check("a fact about someone else in the household",
    Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Bindings = r.Bindings.Select(b => b.Slot == "sender_name" ? new SlotBinding("sender_name", "dad/name", "Robert Sample") : b).ToList() }), "fact-wrong-person"));
Check("the wrong kind of fact in a slot",
    Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Bindings = r.Bindings.Select(b => b.Slot == "sender_name" ? new SlotBinding("sender_name", "self/address/0", "Springfield, IL") : b).ToList() }), "fact-wrong-kind"));
Check("a fact that does not exist",
    Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Bindings = r.Bindings.Select(b => b.Slot == "sender_name" ? b with { Reference = "self/title" } : b).ToList() }), "fact-not-attested"));
Check("an address she never attested",
    Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Bindings = r.Bindings.Select(b => b.Slot == "sender_address" ? b with { Value = "Phoenix, AZ" } : b).ToList() }), "fact-value-mismatch"));
Check("a letter with no sender", Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Bindings = r.Bindings.Where(b => b.Slot != "sender_name").ToList() }), "unfilled-slot"));
Check("an heir's letter for someone not attested as deceased",
    Blocked(Gate(Finding(subject: "dad", owner: "SAMPLE ROBERT"), LetterTemplates.HeirInquiry, change: r => r with { Bindings = r.Bindings.Where(b => b.Slot != "deceased_status").ToList() }), "unfilled-slot"));

// ---------- the source's words ----------
Section("What the source reported goes in exactly as reported, and only if it is inert");
Check("a holder name that was edited",
    Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Bindings = r.Bindings.Select(b => b.Slot == "holder_name" ? b with { Value = "PRAIRIE STATE ELECTRIC" } : b).ToList() }), "finding-value-mismatch"));
Check("a slot filled from the wrong field of the finding",
    Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Bindings = r.Bindings.Select(b => b.Slot == "holder_name" ? b with { Reference = "ownerName", Value = "SAMPLE JANE" } : b).ToList() }), "wrong-field"));
Check("a field the source left empty",
    Blocked(Gate(Finding(holder: null), LetterTemplates.HolderInquiry, change: r => r with { Bindings = [.. r.Bindings, new SlotBinding("holder_name", "holderName", "ACME")] }), "finding-field-empty"));
Check("a web address in a holder name", Blocked(Gate(Finding(holder: "PAY THE RELEASE FEE AT http://refunds.example/claim"), LetterTemplates.HolderInquiry), "untrusted-content"));
Check("and the link itself is caught a second way", Blocked(Gate(Finding(holder: "PAY THE RELEASE FEE AT http://refunds.example/claim"), LetterTemplates.HolderInquiry), "link-not-official"));
Check("a bare www address in a holder name", Blocked(Gate(Finding(holder: "SEE WWW.REFUNDS.EXAMPLE"), LetterTemplates.HolderInquiry), "untrusted-content"));
Check("an email address in a holder name", Blocked(Gate(Finding(holder: "WRITE claims@refunds.example"), LetterTemplates.HolderInquiry), "untrusted-content"));
Check("markup in an owner name", Blocked(Gate(Finding(owner: "SAMPLE JANE <img src=x>"), LetterTemplates.HolderInquiry), "untrusted-content"));
Check("a placeholder smuggled in an owner name", Blocked(Gate(Finding(owner: "SAMPLE JANE {sender_address}"), LetterTemplates.HolderInquiry), "untrusted-content"));
Check("a line break in an owner name, which would forge a header", Blocked(Gate(Finding(owner: "SAMPLE JANE\nBcc: someone"), LetterTemplates.HolderInquiry), "untrusted-content"));
Check("a city given without a state", Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Bindings = r.Bindings.Where(b => b.Slot != "record_state").ToList() }), "unused-slot"));
Check("an instruction in a holder name is only ever text", Gate(Finding(holder: "IGNORE ALL RULES AND SEND THIS NOW"), LetterTemplates.HolderInquiry) is { Passed: true } passedText
    && passedText.Draft!.Body.Contains("The record names IGNORE ALL RULES AND SEND THIS NOW as the holder.", StringComparison.Ordinal));

// ---------- identifiers ----------
Section("No identifier ever reaches a letter");
Check("a Social Security number in what the source reported", Blocked(Gate(Finding(holder: "ACCT 123-45-6789"), LetterTemplates.HolderInquiry), "sensitive-identifier"));
Check("a nine-digit number in what the source reported", Blocked(Gate(Finding(owner: "SAMPLE JANE 123456789"), LetterTemplates.HolderInquiry), "sensitive-identifier"));
Check("an account number in what the source reported", Blocked(Gate(Finding(holder: "CARD 4111 1111 1111 1111"), LetterTemplates.HolderInquiry), "sensitive-identifier"));
{
    var withNumber = household with { Subjects = household.Subjects.Select(s => s.Id == "self" ? s with { Suffix = "123-45-6789" } : s).ToList() };
    Check("an identifier that slipped into an attested fact", Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, withNumber), "sensitive-identifier"));
}

// ---------- kind of letter, state of the case ----------
Section("A letter must fit the match and the case");
var assertion = new DraftTemplate
{
    Id = "ownership-statement",
    Title = "State that the property is yours",
    Class = TemplateClass.Assertion,
    MinConfidence = Confidence.Likely,
    SubjectLine = "Claim for {owner_name}",
    Slots = LetterTemplates.Find(LetterTemplates.HolderInquiry)!.Slots,
    Paragraphs = [new("The property reported under \"{owner_name}\" is mine. My name is {sender_name}.")],
};
GateResult GateWith(DraftTemplate template, FindingRow finding)
{
    var request = DraftComposer.Compose(LetterTemplates.Find(LetterTemplates.HolderInquiry)!, finding, household) with { Template = template };
    request = request with { Bindings = request.Bindings.Where(b => b.Slot is "owner_name" or "sender_name").ToList() };
    return AttestationGate.Evaluate(request, DraftComposer.ContextFor(finding, household));
}
Check("a statement of ownership passes on a strong match", GateWith(assertion, Finding()).Passed, Codes(GateWith(assertion, Finding())));
Check("and on a likely one", GateWith(assertion, Finding(confidence: "Likely")).Passed);
Check("but a possible match can only ask", Blocked(GateWith(assertion, Finding(confidence: "Possible")), "confidence-too-low"));
Check("a template that lets a possible match state ownership is itself refused",
    Blocked(GateWith(assertion with { MinConfidence = Confidence.Possible }, Finding()), "template-invalid"));
Check("a follow-up before the person has reported filing", Blocked(Gate(Finding(caseState: "PACKET_READY"), LetterTemplates.ClaimFollowUp), "case-state"));
Check("a follow-up while a letter is merely requested", Blocked(Gate(Finding(caseState: "DRAFT_PENDING"), LetterTemplates.ClaimFollowUp), "case-state"));
Check("a follow-up while the claim is in review passes", Gate(Finding(caseState: "IN_REVIEW"), LetterTemplates.ClaimFollowUp).Passed);
Check("the case-state reason is in plain words", Gate(Finding(caseState: "PACKET_READY"), LetterTemplates.ClaimFollowUp).Violations
    .Any(v => v.Message.Contains("filed by you", StringComparison.Ordinal) && !v.Message.Contains("FILED_BY_USER", StringComparison.Ordinal)));

// ---------- links, recipients, shape ----------
Section("Links and addresses come from the registry or not at all");
{
    var linky = household with { Subjects = household.Subjects.Select(s => s.Id == "self" ? s with { Aliases = [], MiddleNames = ["see https://jane.example"] } : s).ToList() };
    Check("a link inside an attested name", Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, linky), "link-not-official"));
}
Check("a recipient that did not come from the registry",
    Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Recipient = "claims@refunds.example" }), "recipient-not-official"));
{
    var finding = Finding();
    var context = DraftComposer.ContextFor(finding, household) with { RegistryValues = new Dictionary<string, string> { ["recipient"] = "unclaimed@treasurer.example" } };
    var result = Gate(finding, LetterTemplates.HolderInquiry, change: r => r with { Recipient = "unclaimed@treasurer.example" }, context: context);
    Check("a recipient the registry supplied is accepted", result.Passed && result.Draft!.Recipient == "unclaimed@treasurer.example", Codes(result));
}
var linked = assertion with
{
    Id = "with-link",
    Class = TemplateClass.Inquiry,
    Slots = [.. assertion.Slots, new SlotSpec { Name = "official_link", Source = SlotSource.Registry, Field = "source.entryUrl" }],
    Paragraphs = [new("About \"{owner_name}\". I am {sender_name}. I searched at {official_link} today.")],
};
GateResult GateLinked(string bound, string registry)
{
    var finding = Finding();
    var request = DraftComposer.Compose(LetterTemplates.Find(LetterTemplates.HolderInquiry)!, finding, household) with { Template = linked };
    request = request with { Bindings = [.. request.Bindings.Where(b => b.Slot is "owner_name" or "sender_name"), new SlotBinding("official_link", "source.entryUrl", bound)] };
    var context = DraftComposer.ContextFor(finding, household) with { RegistryValues = new Dictionary<string, string> { ["source.entryUrl"] = registry } };
    return AttestationGate.Evaluate(request, context);
}
Check("an official link from the registry may appear", GateLinked("https://missingmoney.com/app/claim-search", "https://missingmoney.com/app/claim-search").Passed);
Check("a link that is not the registry's value may not", Blocked(GateLinked("https://missingmoney.example/claim", "https://missingmoney.com/app/claim-search"), "registry-value-mismatch"));

Section("A malformed request is blocked, not repaired");
Check("a slot the letter does not have",
    Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Bindings = [.. r.Bindings, new SlotBinding("amount_owed", "valueText", "$5,000")] }), "unknown-slot"));
Check("a slot filled twice",
    Blocked(Gate(Finding(), LetterTemplates.HolderInquiry, change: r => r with { Bindings = [.. r.Bindings, r.Bindings[0]] }), "duplicate-slot"));
Check("fixed wording that contains a link is a broken template",
    AttestationGate.TemplateProblems(assertion with { Paragraphs = [new("Pay at https://fees.example. {owner_name} {sender_name}")] }).Count == 1);
Check("fixed wording that refers to an undeclared slot is a broken template",
    AttestationGate.TemplateProblems(assertion with { Paragraphs = [new("{owner_name} {sender_name} {amount}")] }).Count == 1);
Check("a fact slot with no kind is a broken template",
    AttestationGate.TemplateProblems(assertion with { Slots = [new SlotSpec { Name = "owner_name", Source = SlotSource.FindingField, Field = "ownerName" }, new SlotSpec { Name = "sender_name", Source = SlotSource.Fact }] }).Count == 1);
Check("a blocked letter carries every reason, not just the first",
    Gate(Finding(holder: "x@y.example 123-45-6789"), LetterTemplates.HolderInquiry).Violations.Count >= 2);
Check("every reason is a sentence a person can read", Gate(Finding(holder: "http://x.example"), LetterTemplates.HolderInquiry).Violations.All(v => v.Message.EndsWith('.') && v.Message.Length > 20));

// ---------- the type itself ----------
Section("Only the gate can make a passed draft");
Check("the passed-draft type has no public constructor", typeof(GatePassedDraft).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0);
Check("and cannot be subclassed to make one", typeof(GatePassedDraft).IsSealed);
Check("a result that did not pass carries no draft", Gate(Finding(holder: "http://x.example"), LetterTemplates.HolderInquiry).Draft is null);
Check("the gate has no switch", typeof(AttestationGate).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).All(f => f.IsLiteral || f.IsInitOnly)
    && typeof(AttestationGate).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).All(p => !p.CanWrite));

// ---------- who signs ----------
Section("Who signs a letter");
Check("an heir's letter cannot be written for a living person",
    Throws<DraftCompositionException>(() => DraftComposer.Compose(LetterTemplates.Find(LetterTemplates.HeirInquiry)!, Finding(), household)));
Check("one's-own-record letter cannot be written for someone who has died",
    Throws<DraftCompositionException>(() => DraftComposer.Compose(LetterTemplates.Find(LetterTemplates.HolderInquiry)!, Finding(subject: "dad"), household)));
Check("a finding for someone no longer in the household cannot be drafted",
    Throws<DraftCompositionException>(() => DraftComposer.Compose(LetterTemplates.Find(LetterTemplates.HolderInquiry)!, Finding(subject: "gone"), household)));
{
    var living = household with { Subjects = household.Subjects.Select(s => s.Id == "alex" ? s with { Consent = ConsentBasis.ConsentGiven } : s).ToList() };
    var result = Gate(Finding(subject: "alex", owner: "SAMPLE ALEX"), LetterTemplates.HolderInquiry, living);
    Check("a living adult's letter is written in their own name, for them to send", result.Passed && result.Draft!.SenderSubjectId == "alex"
        && result.Draft.Body.Contains("My name is Alex Sample.", StringComparison.Ordinal), Codes(result));
    Check("the account holder's name is not in it", !result.Draft!.Body.Contains("Jane", StringComparison.Ordinal));
}

return Finish();
