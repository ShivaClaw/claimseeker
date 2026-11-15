namespace ClaimSeeker.Store;

/// <summary>The store with nothing behind it but memory. Harnesses use it; real runs use SQLite.</summary>
public sealed class InMemoryClaimStore : IClaimStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FindingRow> _findings = new(StringComparer.Ordinal);
    private readonly List<string> _findingOrder = [];
    private readonly List<SearchRow> _searches = [];
    private readonly List<DraftRow> _drafts = [];
    private readonly List<EventRow> _events = [];
    private readonly Dictionary<string, string> _config = new(StringComparer.Ordinal);
    private string? _household;

    public void Initialize() { }

    public void Dispose() { }

    public void SaveHousehold(string json, int subjectCount, string actor, string now)
    {
        lock (_gate)
        {
            _household = json;
            Append(new EventInput(actor, StoreVocabulary.HouseholdSaved, StoreVocabulary.HouseholdEntity, "1", StoreRules.HouseholdPayload(subjectCount)), now);
        }
    }

    public string? GetHouseholdJson()
    {
        lock (_gate) return _household;
    }

    public FindingWriteResult UpsertFinding(FindingUpsert finding, string now)
    {
        StoreRules.RequireFinding(finding);
        lock (_gate)
        {
            if (_findings.TryGetValue(finding.Id, out var existing))
            {
                _findings[finding.Id] = existing with
                {
                    SubjectId = finding.SubjectId,
                    OwnerName = finding.OwnerName,
                    HolderName = finding.HolderName,
                    City = finding.City,
                    UsState = finding.UsState,
                    ValueText = finding.ValueText,
                    ValueLow = StoreRules.Cents(finding.ValueLow),
                    ValueHigh = StoreRules.Cents(finding.ValueHigh),
                    PropertyTypeText = finding.PropertyTypeText,
                    Family = finding.Family,
                    Confidence = finding.Confidence,
                    NameMatch = finding.NameMatch,
                    Rationale = finding.Rationale,
                    NameChangeDocsLikely = finding.NameChangeDocsLikely,
                    EstateRecord = finding.EstateRecord,
                    QueryKey = finding.QueryKey,
                    LastSeen = now,
                    MissingSince = null,
                    UpdatedAt = now,
                };
                return new FindingWriteResult(finding.Id, false, existing.DuplicateOf);
            }

            // The same property seen through another source points at the finding that was there first.
            string? duplicateOf = null;
            foreach (var id in _findingOrder)
            {
                var other = _findings[id];
                if (other.DedupeKey != finding.DedupeKey || other.SourceId == finding.SourceId) continue;
                duplicateOf = other.DuplicateOf ?? other.Id;
                break;
            }

            _findings[finding.Id] = new FindingRow
            {
                Id = finding.Id,
                SubjectId = finding.SubjectId,
                SourceId = finding.SourceId,
                SourceRecordId = finding.SourceRecordId,
                DedupeKey = finding.DedupeKey,
                DuplicateOf = duplicateOf,
                OwnerName = finding.OwnerName,
                HolderName = finding.HolderName,
                City = finding.City,
                UsState = finding.UsState,
                ValueText = finding.ValueText,
                ValueLow = StoreRules.Cents(finding.ValueLow),
                ValueHigh = StoreRules.Cents(finding.ValueHigh),
                PropertyTypeText = finding.PropertyTypeText,
                Family = finding.Family,
                Confidence = finding.Confidence,
                NameMatch = finding.NameMatch,
                Rationale = finding.Rationale,
                NameChangeDocsLikely = finding.NameChangeDocsLikely,
                EstateRecord = finding.EstateRecord,
                QueryKey = finding.QueryKey,
                CaseState = StoreRules.InitialState,
                FirstSeen = now,
                LastSeen = now,
                UpdatedAt = now,
            };
            _findingOrder.Add(finding.Id);

            Append(new EventInput("engine", StoreVocabulary.FindingDiscovered, StoreVocabulary.FindingEntity, finding.Id, StoreRules.DiscoveredPayload(finding)), now);
            return new FindingWriteResult(finding.Id, true, duplicateOf);
        }
    }

    public FindingRow? GetFinding(string id)
    {
        lock (_gate) return _findings.GetValueOrDefault(id);
    }

    public IReadOnlyList<FindingRow> ListFindings()
    {
        lock (_gate) return _findingOrder.Select(id => _findings[id]).ToList();
    }

    public bool TryTransition(string findingId, string expectedState, string newState, string actor, string payloadJson, string now)
    {
        StoreRules.RequireCaseState(newState);
        var payload = StoreRules.TransitionPayload(expectedState, newState, payloadJson);
        lock (_gate)
        {
            if (!_findings.TryGetValue(findingId, out var row) || row.CaseState != expectedState) return false;
            _findings[findingId] = row with { CaseState = newState, UpdatedAt = now };
            Append(new EventInput(actor, StoreVocabulary.StateChange, StoreVocabulary.FindingEntity, findingId, payload), now);
            return true;
        }
    }

    public void SaveRank(string findingId, decimal expectedValue, decimal effortHours, decimal score, bool urgent, string rationaleJson, string now)
    {
        lock (_gate)
        {
            if (!_findings.TryGetValue(findingId, out var row)) return;
            _findings[findingId] = row with
            {
                ExpectedValue = expectedValue,
                EffortHours = effortHours,
                Score = score,
                Urgent = urgent,
                RankRationaleJson = rationaleJson,
                UpdatedAt = now,
            };
        }
    }

    public void SetPacketPath(string findingId, string path, string now)
    {
        lock (_gate)
        {
            if (!_findings.TryGetValue(findingId, out var row)) return;
            _findings[findingId] = row with { PacketPath = path, UpdatedAt = now };
        }
    }

    public IReadOnlyList<string> MarkMissing(string sourceId, string queryKey, IReadOnlyCollection<string> seenIds, string now)
    {
        var seen = new HashSet<string>(seenIds, StringComparer.Ordinal);
        var marked = new List<string>();
        lock (_gate)
        {
            foreach (var id in _findingOrder)
            {
                var row = _findings[id];
                if (row.SourceId != sourceId || row.QueryKey != queryKey || row.MissingSince is not null || seen.Contains(id)) continue;
                _findings[id] = row with { MissingSince = now, UpdatedAt = now };
                marked.Add(id);
            }
        }

        return marked;
    }

    public long RecordSearch(SearchInput search)
    {
        lock (_gate)
        {
            var id = _searches.Count + 1L;
            _searches.Add(new SearchRow(id, search));
            return id;
        }
    }

    public SearchRow? GetLastSearch(string sourceId, string queryKey)
    {
        lock (_gate) return _searches.LastOrDefault(s => s.Search.SourceId == sourceId && s.Search.QueryKey == queryKey);
    }

    public IReadOnlyList<SearchRow> ListSearches()
    {
        lock (_gate) return _searches.ToList();
    }

    public long RecordDraft(string findingId, string templateId, string path, string sha256, string now)
    {
        lock (_gate)
        {
            var id = _drafts.Count + 1L;
            _drafts.Add(new DraftRow(id, findingId, templateId, path, sha256, now));
            return id;
        }
    }

    public IReadOnlyList<DraftRow> ListDrafts(string findingId)
    {
        lock (_gate) return _drafts.Where(d => d.FindingId == findingId).ToList();
    }

    public long AppendEvent(EventInput e, string now)
    {
        lock (_gate) return Append(e, now);
    }

    public IReadOnlyList<EventRow> GetEvents()
    {
        lock (_gate) return _events.ToList();
    }

    public AuditVerification VerifyAudit()
    {
        lock (_gate) return AuditChain.Verify(_events);
    }

    public string? GetConfig(string key)
    {
        lock (_gate) return _config.GetValueOrDefault(key);
    }

    public void SetConfig(string key, string value)
    {
        lock (_gate) _config[key] = value;
    }

    private long Append(EventInput e, string now)
    {
        var seq = _events.Count + 1L;
        var prev = _events.Count == 0 ? AuditChain.Genesis : _events[^1].Hash;
        _events.Add(AuditChain.Link(seq, now, prev, e));
        return seq;
    }

    /// <summary>Harness seam: replaces a stored audit line, as someone editing the log by hand would.</summary>
    public void TamperWithEventForTest(int index, EventRow replacement)
    {
        lock (_gate) _events[index] = replacement;
    }
}
