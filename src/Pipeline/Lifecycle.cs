using ClaimSeeker.Core;

namespace ClaimSeeker.Pipeline;

/// <summary>
/// The life of a finding, as a graph. The graph is the rule: a change that is not an edge here does
/// not happen, whoever asks for it.
///
/// Two properties are what the product rests on, and both can be read straight off the edges.
///
/// A letter becomes ready only by passing the Attestation Gate. DRAFT_READY has one way in, from
/// ATTESTED, and ATTESTED has one way in, from a draft that was put to the gate.
///
/// The engine cannot file a claim, so the engine cannot say one was filed. FILED_BY_USER and every
/// state after it can be entered only by the person. There is no engine edge into them, because
/// there is no engine action they could describe.
/// </summary>
public static class Lifecycle
{
    private static readonly IReadOnlyDictionary<CaseState, CaseState[]> Forward = new Dictionary<CaseState, CaseState[]>
    {
        [CaseState.DISCOVERED] = [CaseState.RANKED, CaseState.DISMISSED],
        [CaseState.RANKED] = [CaseState.PACKET_READY, CaseState.DISMISSED],
        [CaseState.PACKET_READY] = [CaseState.DRAFT_PENDING, CaseState.FILED_BY_USER, CaseState.DISMISSED, CaseState.EXPIRED],

        // The gate. A requested letter is either attested or blocked; nothing else leaves DRAFT_PENDING.
        // A letter that was attested can still be blocked before it is written: if the engine stops
        // between the gate and the outbox, the gate is asked again on resume, and the facts may have changed.
        [CaseState.DRAFT_PENDING] = [CaseState.ATTESTED, CaseState.BLOCKED_ATTESTATION],
        [CaseState.BLOCKED_ATTESTATION] = [CaseState.DRAFT_PENDING, CaseState.FILED_BY_USER, CaseState.DISMISSED, CaseState.EXPIRED],
        [CaseState.ATTESTED] = [CaseState.DRAFT_READY, CaseState.BLOCKED_ATTESTATION],
        [CaseState.DRAFT_READY] = [CaseState.DRAFTED, CaseState.BLOCKED_ATTESTATION],
        [CaseState.DRAFTED] = [CaseState.DRAFT_PENDING, CaseState.FILED_BY_USER, CaseState.DISMISSED, CaseState.EXPIRED],

        // From here on, only the person knows what happened.
        [CaseState.FILED_BY_USER] = [CaseState.IN_REVIEW, CaseState.NEEDS_DOCUMENTS, CaseState.PAID, CaseState.DENIED],
        [CaseState.IN_REVIEW] = [CaseState.NEEDS_DOCUMENTS, CaseState.PAID, CaseState.DENIED],
        [CaseState.NEEDS_DOCUMENTS] = [CaseState.FILED_BY_USER, CaseState.PAID, CaseState.DENIED],

        [CaseState.PAID] = [],
        [CaseState.DENIED] = [],
        [CaseState.DISMISSED] = [],
        [CaseState.EXPIRED] = [],
    };

    /// <summary>States only the person can put a case in: each reports something only they did or learned.</summary>
    public static readonly IReadOnlySet<CaseState> UserOnly = new HashSet<CaseState>
    {
        CaseState.FILED_BY_USER, CaseState.IN_REVIEW, CaseState.NEEDS_DOCUMENTS, CaseState.PAID, CaseState.DENIED, CaseState.DISMISSED,
    };

    /// <summary>States only the engine can put a case in: each records work the engine did.</summary>
    public static readonly IReadOnlySet<CaseState> EngineOnly = new HashSet<CaseState>
    {
        CaseState.RANKED, CaseState.PACKET_READY, CaseState.ATTESTED, CaseState.BLOCKED_ATTESTATION,
        CaseState.DRAFT_READY, CaseState.DRAFTED, CaseState.EXPIRED,
    };

    public static readonly IReadOnlySet<CaseState> Terminal = new HashSet<CaseState>
    {
        CaseState.PAID, CaseState.DENIED, CaseState.DISMISSED, CaseState.EXPIRED,
    };

    /// <summary>The states in which the person has told the engine a claim was filed.</summary>
    public static readonly IReadOnlySet<CaseState> Filed = new HashSet<CaseState>
    {
        CaseState.FILED_BY_USER, CaseState.IN_REVIEW, CaseState.NEEDS_DOCUMENTS,
    };

    public static IReadOnlyList<CaseState> Next(CaseState from) => Forward[from];

    public static IReadOnlyList<CaseState> Predecessors(CaseState to) =>
        Forward.Where(pair => pair.Value.Contains(to)).Select(pair => pair.Key).ToList();

    public static bool CanTransition(CaseState from, CaseState to, Actor actor)
    {
        if (!Forward[from].Contains(to)) return false;
        if (UserOnly.Contains(to)) return actor == Actor.User;
        if (EngineOnly.Contains(to)) return actor == Actor.Engine;
        return true;
    }
}
