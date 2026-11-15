using System.Text.Json.Nodes;
using ClaimSeeker.Core;
using ClaimSeeker.Dispatcher;
using ClaimSeeker.Preparer;
using ClaimSeeker.Scout;
using ClaimSeeker.Store;
using ClaimSeeker.Verifier;

namespace ClaimSeeker.Pipeline;

public sealed class CaseException(string message) : Exception(message);

public sealed record DraftOutcome(
    bool Passed,
    string? Path,
    string? SenderName,
    bool SenderIsAccountHolder,
    GatePassedDraft? Draft,
    IReadOnlyList<Violation> Violations);

/// <summary>
/// Moves one finding along: writes its claim packet, drafts a letter through the Attestation Gate,
/// and records what the person says happened next. Every move is checked against the lifecycle
/// graph first, with the actor that is really making it.
/// </summary>
public sealed class CaseService(IClaimStore store, TimeProvider time)
{
    // ---------- packets ----------

    /// <summary>Writes the claim packet for a finding. Returns the file's path.</summary>
    public string PreparePacket(string findingId, Household household, string packetsDirectory)
    {
        var finding = Require(findingId);
        var state = Enum.Parse<CaseState>(finding.CaseState);
        if (state == CaseState.DISCOVERED) throw new CaseException("This finding has not been ranked yet. Run a sweep first.");
        if (Lifecycle.Terminal.Contains(state)) throw new CaseException($"This case is closed ({FindingsReport.StateWords(finding.CaseState)}).");

        var source = SourceRegistry.Find(finding.SourceId) ?? throw new CaseException("The source of this finding is not in the registry.");
        var packet = ClaimPacketBuilder.Build(finding, household, source, Clock.Today(time));

        Directory.CreateDirectory(packetsDirectory);
        var path = Path.Combine(packetsDirectory, packet.FileName);
        File.WriteAllText(path, packet.Markdown);

        var now = Clock.Stamp(time);
        store.SetPacketPath(findingId, path, now);
        if (state == CaseState.RANKED)
            Move(finding, CaseState.PACKET_READY, Actor.Engine, "{}");

        store.AppendEvent(new EventInput("engine", "packet_written", StoreVocabulary.FindingEntity, findingId), now);
        return path;
    }

    /// <summary>Writes a packet for every open finding that does not have one yet. Returns how many were written.</summary>
    public int PreparePackets(Household household, string packetsDirectory)
    {
        var written = 0;
        foreach (var finding in store.ListFindings())
        {
            if (finding.CaseState != nameof(CaseState.RANKED) || finding.DuplicateOf is not null) continue;
            PreparePacket(finding.Id, household, packetsDirectory);
            written++;
        }

        return written;
    }

    // ---------- letters ----------

    /// <summary>
    /// Drafts a letter for a finding. The letter is composed, put to the Attestation Gate, and only if
    /// it passes handed to the outbox. A blocked letter leaves no file behind.
    /// </summary>
    public DraftOutcome Draft(string findingId, string? templateId, Household household, IDraftOutbox outbox)
    {
        var finding = Require(findingId);
        var state = Enum.Parse<CaseState>(finding.CaseState);

        templateId ??= Lifecycle.Filed.Contains(state) ? LetterTemplates.ClaimFollowUp : DraftComposer.DefaultTemplateFor(finding, household);
        var template = LetterTemplates.Find(templateId) ?? throw new CaseException($"There is no letter called \"{templateId}\".");

        // A letter written before filing moves the case through the gate's states. A follow-up on a
        // filed claim does not: the case stays filed, and the gate's decision is logged beside it.
        var filed = Lifecycle.Filed.Contains(state);
        var startsFresh = state is CaseState.PACKET_READY or CaseState.DRAFTED or CaseState.BLOCKED_ATTESTATION;
        // A case found part-way through the gate was interrupted there. It picks up where it stopped,
        // and the gate is asked again: nothing is written on the strength of an earlier answer.
        var resumes = state is CaseState.DRAFT_PENDING or CaseState.ATTESTED or CaseState.DRAFT_READY;
        if (!filed && !startsFresh && !resumes)
            throw new CaseException(state is CaseState.DISCOVERED or CaseState.RANKED
                ? "Prepare the claim packet for this finding before drafting a letter."
                : $"A letter cannot be drafted while the case is {FindingsReport.StateWords(finding.CaseState)}.");

        // Asked here as well as at the gate, so a letter the case is not ready for is refused before the
        // case is moved at all. The gate's own check stays: it does not rely on this one having run.
        if (template.AllowedCaseStates is { } allowedStates && !allowedStates.Contains(state))
            throw new CaseException(template.Id == LetterTemplates.ClaimFollowUp
                ? $"A follow-up letter is for a claim you have filed. Once you have filed it, run `claimseeker mark {findingId} filed`, then ask again."
                : $"\"{template.Id}\" cannot be written while the case is {FindingsReport.StateWords(finding.CaseState)}.");

        DraftRequest request;
        try
        {
            request = DraftComposer.Compose(template, finding, household);
        }
        catch (DraftCompositionException ex)
        {
            throw new CaseException(ex.Message);
        }

        if (startsFresh) finding = Move(finding, CaseState.DRAFT_PENDING, Actor.User, Detail(("template", template.Id)));
        request = request with { CaseState = Current(finding) };

        var result = AttestationGate.Evaluate(request, DraftComposer.ContextFor(finding, household));
        var now = Clock.Stamp(time);
        store.AppendEvent(new EventInput("engine", "gate_decision", StoreVocabulary.FindingEntity, findingId, new JsonObject
        {
            ["template"] = template.Id,
            ["passed"] = result.Passed,
            ["violations"] = new JsonArray(result.Violations.Select(v => (JsonNode)v.Code).ToArray()),
        }.ToJsonString()), now);

        var sender = household.Find(request.SenderSubjectId);
        if (!result.Passed)
        {
            if (!filed) Move(finding, CaseState.BLOCKED_ATTESTATION, Actor.Engine, Detail(("template", template.Id)));
            return new DraftOutcome(false, null, sender?.FullName, sender?.Relationship == Relationship.Self, null, result.Violations);
        }

        if (!filed)
        {
            if (Current(finding) == CaseState.DRAFT_PENDING) finding = Move(finding, CaseState.ATTESTED, Actor.Engine, Detail(("template", template.Id)));
            if (Current(finding) == CaseState.ATTESTED) finding = Move(finding, CaseState.DRAFT_READY, Actor.Engine, "{}");
        }

        // The outbox is reached from exactly two places: a case the gate has just made ready, or a claim
        // the person has reported filing. Checked here, immediately before the call, whatever ran above.
        if (!filed && Current(finding) != CaseState.DRAFT_READY)
            throw new CaseException($"A letter cannot be written while the case is {FindingsReport.StateWords(finding.CaseState)}.");

        // The same letter written twice is one file, so repeating this after an interruption is safe.
        var receipt = outbox.CreateDraft(result.Draft!);
        store.RecordDraft(findingId, template.Id, receipt.Path, receipt.Sha256, now);
        store.AppendEvent(new EventInput("engine", "draft_created", StoreVocabulary.FindingEntity, findingId, new JsonObject
        {
            ["template"] = template.Id,
            ["sha256"] = receipt.Sha256,
            ["alreadyExisted"] = receipt.AlreadyExisted,
        }.ToJsonString()), now);

        if (!filed) Move(finding, CaseState.DRAFTED, Actor.Engine, "{}");
        return new DraftOutcome(true, receipt.Path, sender?.FullName, sender?.Relationship == Relationship.Self, result.Draft, []);
    }

    private static CaseState Current(FindingRow finding) => Enum.Parse<CaseState>(finding.CaseState);

    // ---------- what the person reports ----------

    /// <summary>Records something only the person can know: that they filed, were paid, were refused, or that the record is not theirs.</summary>
    public FindingRow Mark(string findingId, CaseState target)
    {
        var finding = Require(findingId);
        var from = Enum.Parse<CaseState>(finding.CaseState);
        if (!Lifecycle.UserOnly.Contains(target))
            throw new CaseException($"\"{target}\" is not something a person reports.");
        if (!Lifecycle.CanTransition(from, target, Actor.User))
            throw new CaseException($"A case that is {FindingsReport.StateWords(finding.CaseState)} cannot be marked {FindingsReport.StateWords(target.ToString())}.");

        return Move(finding, target, Actor.User, "{}");
    }

    // ---------- plumbing ----------

    private FindingRow Require(string findingId) =>
        store.GetFinding(findingId) ?? throw new CaseException($"There is no finding with the id \"{findingId}\".");

    /// <summary>
    /// The one place a case changes state. The graph is asked first, with the real actor, and the
    /// store changes the state only if it is still what was read.
    /// </summary>
    private FindingRow Move(FindingRow finding, CaseState to, Actor actor, string detailJson)
    {
        var from = Enum.Parse<CaseState>(finding.CaseState);
        if (!Lifecycle.CanTransition(from, to, actor))
            throw new CaseException($"The lifecycle does not allow {from} to {to} by the {actor.Stored()}.");
        if (!store.TryTransition(finding.Id, finding.CaseState, to.ToString(), actor.Stored(), detailJson, Clock.Stamp(time)))
            throw new CaseException("The case changed while this was running. Nothing was done; try again.");

        return store.GetFinding(finding.Id)!;
    }

    private static string Detail(params (string Key, string Value)[] pairs)
    {
        var node = new JsonObject();
        foreach (var (key, value) in pairs) node[key] = value;
        return node.ToJsonString();
    }
}

/// <summary>Gathers what a report needs from the store.</summary>
public sealed class ReportService(IClaimStore store, TimeProvider time)
{
    public ReportInput BuildInput(Household household, IReadOnlyList<FindingRow> rankedFindings, IReadOnlyDictionary<string, Playbook> playbooksBySource)
    {
        // The latest attempt at each search decides whether it still needs the person.
        var latest = new Dictionary<string, SearchRow>(StringComparer.Ordinal);
        foreach (var search in store.ListSearches())
            latest[search.Search.QueryKey] = search;

        var fallbacks = new List<FallbackItem>();
        foreach (var search in latest.Values.OrderBy(s => s.Id))
        {
            if (search.Search.Outcome is not (SearchOutcomes.Fallback or SearchOutcomes.ShapeChanged)) continue;
            if (!playbooksBySource.TryGetValue(search.Search.SourceId, out var playbook)) continue;

            var source = SourceRegistry.Find(search.Search.SourceId);
            var searched = search.Search.GivenName is null
                ? $"the family name {search.Search.FamilyName}"
                : $"{search.Search.GivenName} {search.Search.FamilyName}";

            fallbacks.Add(new FallbackItem(
                source?.Name ?? search.Search.SourceId,
                searched,
                search.Search.Reason ?? "The source did not take the search.",
                playbook.Fallback.DeepLink,
                playbook.Fallback.Instructions,
                search.Search.RanAt));
        }

        return new ReportInput(household, rankedFindings, fallbacks, Clock.Today(time));
    }
}
