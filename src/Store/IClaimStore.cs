namespace ClaimSeeker.Store;

/// <summary>A record matched to someone in the household, as the sweep hands it to the store.</summary>
public sealed record FindingUpsert
{
    public required string Id { get; init; }
    public required string SubjectId { get; init; }
    public required string SourceId { get; init; }
    public string? SourceRecordId { get; init; }
    public required string DedupeKey { get; init; }
    public required string OwnerName { get; init; }
    public string? HolderName { get; init; }
    public string? City { get; init; }
    public string? UsState { get; init; }
    public string? ValueText { get; init; }
    public decimal? ValueLow { get; init; }
    public decimal? ValueHigh { get; init; }
    public string? PropertyTypeText { get; init; }
    public required string Family { get; init; }
    public required string Confidence { get; init; }
    public required string NameMatch { get; init; }
    public required string Rationale { get; init; }
    public bool NameChangeDocsLikely { get; init; }
    public bool EstateRecord { get; init; }
    public required string QueryKey { get; init; }
}

/// <summary>A stored finding: what the source said, whose it looks like, and where the case stands.</summary>
public sealed record FindingRow
{
    public required string Id { get; init; }
    public required string SubjectId { get; init; }
    public required string SourceId { get; init; }
    public string? SourceRecordId { get; init; }
    public required string DedupeKey { get; init; }
    public string? DuplicateOf { get; init; }
    public required string OwnerName { get; init; }
    public string? HolderName { get; init; }
    public string? City { get; init; }
    public string? UsState { get; init; }
    public string? ValueText { get; init; }
    public decimal? ValueLow { get; init; }
    public decimal? ValueHigh { get; init; }
    public string? PropertyTypeText { get; init; }
    public required string Family { get; init; }
    public required string Confidence { get; init; }
    public required string NameMatch { get; init; }
    public required string Rationale { get; init; }
    public bool NameChangeDocsLikely { get; init; }
    public bool EstateRecord { get; init; }
    public required string QueryKey { get; init; }
    public required string CaseState { get; init; }
    public decimal? ExpectedValue { get; init; }
    public decimal? EffortHours { get; init; }
    public decimal? Score { get; init; }
    public bool Urgent { get; init; }
    public string? RankRationaleJson { get; init; }
    public required string FirstSeen { get; init; }
    public required string LastSeen { get; init; }
    public string? MissingSince { get; init; }
    public string? PacketPath { get; init; }
    public required string UpdatedAt { get; init; }
}

public sealed record FindingWriteResult(string Id, bool IsNew, string? DuplicateOf);

public sealed record SearchInput
{
    public required string SourceId { get; init; }
    public required string QueryKey { get; init; }
    public required string FamilyName { get; init; }
    public string? GivenName { get; init; }
    public string? UsState { get; init; }
    public required string Kind { get; init; }
    public required string Mode { get; init; }
    /// <summary>captured, fallback, deferred or shape_changed.</summary>
    public required string Outcome { get; init; }
    public string? Reason { get; init; }
    public int ItemsSeen { get; init; }
    public int Matched { get; init; }
    public bool Truncated { get; init; }
    public required string RanAt { get; init; }
}

public sealed record SearchRow(long Id, SearchInput Search);

public sealed record DraftRow(long Id, string FindingId, string TemplateId, string Path, string Sha256, string CreatedAt);

/// <summary>
/// The engine's memory. Two implementations sit behind it, an in-memory one for harnesses and a
/// SQLite one for real use, and a parity harness holds them to the same behaviour call for call.
///
/// Times are passed in, already formatted, by the caller. The store never reads a clock, which is
/// what lets the two implementations be compared exactly.
/// </summary>
public interface IClaimStore : IDisposable
{
    void Initialize();

    // ---- household ----
    void SaveHousehold(string json, int subjectCount, string actor, string now);
    string? GetHouseholdJson();

    // ---- findings ----
    /// <summary>
    /// Adds a finding or refreshes one already known. A refresh updates what the source said and how
    /// well it matches, and never moves the case's state. A new finding is logged as discovered.
    /// </summary>
    FindingWriteResult UpsertFinding(FindingUpsert finding, string now);
    FindingRow? GetFinding(string id);
    IReadOnlyList<FindingRow> ListFindings();

    /// <summary>
    /// Moves a case from one state to another only if it is still in the first. The change and its
    /// audit line are written together or not at all.
    /// </summary>
    bool TryTransition(string findingId, string expectedState, string newState, string actor, string payloadJson, string now);

    void SaveRank(string findingId, decimal expectedValue, decimal effortHours, decimal score, bool urgent, string rationaleJson, string now);
    void SetPacketPath(string findingId, string path, string now);

    /// <summary>
    /// Marks the findings a search used to return and no longer does. A record that leaves a source's
    /// list has usually been paid out or withdrawn. Returns the ids newly marked.
    /// </summary>
    IReadOnlyList<string> MarkMissing(string sourceId, string queryKey, IReadOnlyCollection<string> seenIds, string now);

    // ---- searches ----
    long RecordSearch(SearchInput search);
    SearchRow? GetLastSearch(string sourceId, string queryKey);
    IReadOnlyList<SearchRow> ListSearches();

    // ---- drafts ----
    long RecordDraft(string findingId, string templateId, string path, string sha256, string now);
    IReadOnlyList<DraftRow> ListDrafts(string findingId);

    // ---- audit ----
    long AppendEvent(EventInput e, string now);
    IReadOnlyList<EventRow> GetEvents();
    AuditVerification VerifyAudit();

    // ---- settings ----
    string? GetConfig(string key);
    void SetConfig(string key, string value);
}

public static class StoreVocabulary
{
    public const string FindingEntity = "finding";
    public const string HouseholdEntity = "household";
    public const string SearchEntity = "search";

    public const string FindingDiscovered = "finding_discovered";
    public const string StateChange = "state_change";
    public const string HouseholdSaved = "household_saved";
}
