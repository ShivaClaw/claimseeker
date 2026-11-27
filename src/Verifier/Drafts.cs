using ClaimSeeker.Core;

namespace ClaimSeeker.Verifier;

/// <summary>What a letter does. An inquiry asks; an assertion states that the property is the sender's.</summary>
public enum TemplateClass { Inquiry, Assertion }

/// <summary>Where a slot's text is allowed to come from.</summary>
public enum SlotSource
{
    /// <summary>A fact the account holder attested about a person in the household.</summary>
    Fact,
    /// <summary>A field of the finding, exactly as the source reported it. Untrusted; must be inert text.</summary>
    FindingField,
    /// <summary>A value from the source registry, such as an official address.</summary>
    Registry,
}

/// <summary>Whose fact a slot may be filled from.</summary>
public enum SlotPerson { Sender, Subject }

/// <summary>
/// One hole in a template and the only place its text may come from. A slot filled from a fact names
/// the kind of fact and whose it must be; a slot filled from the finding names the field.
/// </summary>
public sealed record SlotSpec
{
    public required string Name { get; init; }
    public required SlotSource Source { get; init; }
    public FactKind? FactKind { get; init; }
    public SlotPerson Person { get; init; } = SlotPerson.Sender;
    /// <summary>For a finding or registry slot: the one field or key it may carry.</summary>
    public string? Field { get; init; }
}

/// <summary>
/// A paragraph that appears only when its conditions hold. <see cref="When"/> lists slots that must
/// all be filled; <see cref="Unless"/> lists slots that must all be empty. This is how a letter adapts
/// to what is known without any sentence being assembled from parts.
/// </summary>
public sealed record TemplateParagraph(string Text, IReadOnlyList<string>? When = null, IReadOnlyList<string>? Unless = null);

/// <summary>
/// A letter with holes in it. All of its wording is fixed and reviewed; the only variable text is what
/// goes in the slots, and each slot says where that text must come from.
/// </summary>
public sealed record DraftTemplate
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required TemplateClass Class { get; init; }
    public required Confidence MinConfidence { get; init; }
    public required string SubjectLine { get; init; }
    public required IReadOnlyList<TemplateParagraph> Paragraphs { get; init; }
    public required IReadOnlyList<SlotSpec> Slots { get; init; }

    /// <summary>When set, the letter may be written only while the case is in one of these states.</summary>
    public IReadOnlyList<CaseState>? AllowedCaseStates { get; init; }

    public SlotSpec? Slot(string name) => Slots.FirstOrDefault(s => s.Name == name);
}

/// <summary>Text offered for a slot, and what it claims to come from.</summary>
public sealed record SlotBinding(string Slot, string Reference, string Value);

public sealed record DraftRequest
{
    public required DraftTemplate Template { get; init; }
    public required string FindingId { get; init; }
    /// <summary>The person the finding was matched to.</summary>
    public required string SubjectId { get; init; }
    /// <summary>The person who will sign and send the letter.</summary>
    public required string SenderSubjectId { get; init; }
    public required Confidence Confidence { get; init; }
    public required CaseState CaseState { get; init; }
    public required IReadOnlyList<SlotBinding> Bindings { get; init; }
    /// <summary>The address the letter is for. Null leaves it for the person to fill in.</summary>
    public string? Recipient { get; init; }
}

/// <summary>What the gate checks a draft against: the attested facts, the stored finding, the registry.</summary>
public sealed record GateContext(
    IReadOnlyList<AttestedFact> Facts,
    IReadOnlyDictionary<string, string?> FindingFields,
    IReadOnlyDictionary<string, string> RegistryValues);

public sealed record Violation(string Code, string Message);

/// <summary>
/// A letter the gate has passed. It can only be made by the gate: the constructor is not visible
/// outside this assembly, so nothing that skipped the gate can be handed to the dispatcher.
/// </summary>
public sealed class GatePassedDraft
{
    internal GatePassedDraft(string templateId, string findingId, string senderSubjectId, string subjectLine, string body, string? recipient)
    {
        TemplateId = templateId;
        FindingId = findingId;
        SenderSubjectId = senderSubjectId;
        SubjectLine = subjectLine;
        Body = body;
        Recipient = recipient;
    }

    public string TemplateId { get; }
    public string FindingId { get; }
    public string SenderSubjectId { get; }
    public string SubjectLine { get; }
    public string Body { get; }
    public string? Recipient { get; }
}

public sealed record GateResult(GatePassedDraft? Draft, IReadOnlyList<Violation> Violations)
{
    public bool Passed => Draft is not null;
}
