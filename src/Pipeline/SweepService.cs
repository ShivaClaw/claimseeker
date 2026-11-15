using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaimSeeker.Core;
using ClaimSeeker.Scorer;
using ClaimSeeker.Scout;
using ClaimSeeker.Store;

namespace ClaimSeeker.Pipeline;

public sealed record SweepOptions
{
    /// <summary>Run only the searches whose cadence has come round. Off for a first sweep or a forced one.</summary>
    public bool OnlyDue { get; init; } = true;
    public VariantOptions? Variants { get; init; }
    /// <summary>Try a source again after it has turned automation away twice running.</summary>
    public bool RetryBlocked { get; init; }
}

public sealed record QueryReport(SourceQuery Query, string Outcome, string? Reason, int ItemsSeen, int Matched, bool Truncated);

public sealed record SweepReport(
    string SourceId,
    IReadOnlyList<QueryReport> Queries,
    IReadOnlyList<SkippedSubject> Skipped,
    int NewFindings,
    int RefreshedFindings,
    int DiscardedRecords);

public static class SearchOutcomes
{
    public const string Captured = "captured";
    public const string Fallback = "fallback";
    public const string Deferred = "deferred";
    public const string ShapeChanged = "shape_changed";
    public const string NotDue = "not_due";
}

/// <summary>
/// One pass over one source: plan the searches, run the ones that are due, keep the records that
/// belong to the household and let the rest go, then rank what is held.
///
/// Three things it will not do. It does not search for a person with no consent basis. It does not
/// store a record that matched no one, so the engine never accumulates strangers' data. And it does
/// not keep knocking: a source that turns automation away on two sweeps running is left alone until
/// the person asks for another try, and its searches are handed to them instead.
/// </summary>
public sealed class SweepService(IClaimStore store, TimeProvider time)
{
    /// <summary>After this many sweeps in a row end in a refusal, the source is treated as asking for a person.</summary>
    public const int BlockedStreakLimit = 2;

    public const string BlockedReason =
        "This source has turned automated searches away on the last two sweeps, so ClaimSeeker has stopped sending them. Run the search yourself from the link.";

    public static string BlockedStreakKey(string sourceId) => $"source.{sourceId}.blocked_streak";

    public async Task<SweepReport> SweepAsync(
        Household household,
        SourceDescriptor source,
        Playbook playbook,
        IQueryExecutor executor,
        SweepOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new SweepOptions();
        if (playbook.Source != source.Id) throw new ArgumentException("The playbook is for a different source.", nameof(playbook));

        var plan = QueryPlanner.PlanSurnameSweep(household, source, options.Variants);
        var reports = new List<QueryReport>();
        var counts = new Counts();

        if (options.RetryBlocked) store.SetConfig(BlockedStreakKey(source.Id), "0");
        var standingDown = BlockedStreak(source.Id) >= BlockedStreakLimit;
        var refusedThisSweep = false;
        var answeredThisSweep = false;

        foreach (var query in plan.Queries)
        {
            ct.ThrowIfCancellationRequested();

            if (standingDown)
            {
                reports.Add(Record(query, playbook, SearchOutcomes.Fallback, BlockedReason, 0, 0, false));
                continue;
            }

            if (options.OnlyDue && !IsDue(source, query))
            {
                reports.Add(new QueryReport(query, SearchOutcomes.NotDue, null, 0, 0, false));
                continue;
            }

            var report = await RunQueryAsync(household, source, playbook, executor, query, counts, ct);
            reports.Add(report);
            refusedThisSweep |= report.Outcome is SearchOutcomes.Fallback or SearchOutcomes.ShapeChanged;
            answeredThisSweep |= report.Outcome == SearchOutcomes.Captured;

            // A list that came back cut off is narrowed by given name, once, for the people it was for.
            if (report is { Outcome: SearchOutcomes.Captured, Truncated: true })
                foreach (var refined in QueryPlanner.Refine(query, household))
                    reports.Add(await RunQueryAsync(household, source, playbook, executor, refined, counts, ct));
        }

        if (!standingDown)
        {
            if (answeredThisSweep) store.SetConfig(BlockedStreakKey(source.Id), "0");
            else if (refusedThisSweep) store.SetConfig(BlockedStreakKey(source.Id), (BlockedStreak(source.Id) + 1).ToString(CultureInfo.InvariantCulture));
        }

        RankAll(household);

        store.AppendEvent(new EventInput("engine", "sweep_completed", StoreVocabulary.SearchEntity, source.Id, new JsonObject
        {
            ["queries"] = reports.Count,
            ["captured"] = reports.Count(r => r.Outcome == SearchOutcomes.Captured),
            ["needsPerson"] = reports.Count(r => r.Outcome is SearchOutcomes.Fallback or SearchOutcomes.ShapeChanged),
            ["new"] = counts.New,
            ["refreshed"] = counts.Refreshed,
            ["discarded"] = counts.Discarded,
            ["skippedPeople"] = plan.Skipped.Count,
        }.ToJsonString()), Now());

        return new SweepReport(source.Id, reports, plan.Skipped, counts.New, counts.Refreshed, counts.Discarded);
    }

    private async Task<QueryReport> RunQueryAsync(
        Household household, SourceDescriptor source, Playbook playbook, IQueryExecutor executor, SourceQuery query, Counts counts, CancellationToken ct)
    {
        var outcome = await executor.ExecuteAsync(playbook, query, ct);
        if (outcome.Status == RunStatus.Fallback) return Record(query, playbook, SearchOutcomes.Fallback, outcome.Reason, 0, 0, false);
        if (outcome.Status == RunStatus.Deferred) return Record(query, playbook, SearchOutcomes.Deferred, outcome.Reason, 0, 0, false);

        MappedResult mapped;
        try
        {
            mapped = ResultMapper.Map(playbook, outcome.Json!);
        }
        catch (ResultFormatException ex)
        {
            // The source answered, but not in the shape the playbook knows. That is a broken recipe, and it
            // is recorded as one. It must never be mistaken for "nothing found".
            return Record(query, playbook, SearchOutcomes.ShapeChanged, ex.Message, 0, 0, false);
        }

        var now = Now();
        var seenIds = new List<string>();
        var identicalRows = new Dictionary<string, int>(StringComparer.Ordinal);
        var matched = 0;

        foreach (var record in mapped.Records)
        {
            var contentKey = FindingKeys.ContentKey(record);
            var ordinal = identicalRows.GetValueOrDefault(contentKey);
            identicalRows[contentKey] = ordinal + 1;

            var match = Matcher.Match(record, household);
            if (match is null)
            {
                counts.Discarded++;
                continue;
            }

            var value = ValueText.Parse(record.ValueText);
            var result = store.UpsertFinding(new FindingUpsert
            {
                Id = FindingKeys.FindingId(record, ordinal),
                SubjectId = match.SubjectId,
                SourceId = source.Id,
                SourceRecordId = record.SourceRecordId,
                DedupeKey = FindingKeys.DedupeKey(record),
                OwnerName = record.OwnerName,
                HolderName = record.HolderName,
                City = record.City,
                UsState = record.State,
                ValueText = record.ValueText,
                ValueLow = value.Disclosed ? value.Low : null,
                ValueHigh = value.Disclosed ? value.High : null,
                PropertyTypeText = record.PropertyTypeText,
                Family = Naupa.Classify(record.PropertyTypeText).ToString(),
                Confidence = match.Confidence.ToString(),
                NameMatch = match.NameMatch.ToString(),
                Rationale = match.Rationale,
                NameChangeDocsLikely = match.NameChangeDocsLikely,
                EstateRecord = match.EstateRecord,
                QueryKey = query.Key,
            }, now);

            seenIds.Add(result.Id);
            matched++;
            if (result.IsNew) counts.New++; else counts.Refreshed++;
        }

        store.MarkMissing(source.Id, query.Key, seenIds, now);
        return Record(query, playbook, SearchOutcomes.Captured, null, mapped.ItemsSeen, matched, mapped.Truncated);
    }

    /// <summary>Scores every finding and moves the newly found ones on to RANKED.</summary>
    public void RankAll(Household household)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var now = Now();

        foreach (var finding in store.ListFindings())
        {
            var source = SourceRegistry.Find(finding.SourceId);
            var subject = household.Find(finding.SubjectId);
            var rank = Ranker.Rank(new RankInput(
                Value: MoneyRange.FromBounds(finding.ValueLow, finding.ValueHigh),
                Family: Enum.TryParse<PropertyFamily>(finding.Family, out var family) ? family : PropertyFamily.Unknown,
                Confidence: Enum.Parse<Confidence>(finding.Confidence),
                NameChangeDocsLikely: finding.NameChangeDocsLikely,
                DeceasedOwner: finding.EstateRecord || subject is { Deceased: true },
                Mode: source is null ? InteractionMode.Guided : SourceRegistry.EffectiveMode(source),
                ManualFallback: false,
                Deadline: null), today);

            store.SaveRank(finding.Id, rank.ExpectedValue, rank.EffortHours, rank.Score, rank.Urgent, JsonSerializer.Serialize(rank.Rationale), now);

            if (finding.CaseState == nameof(CaseState.DISCOVERED))
                store.TryTransition(finding.Id, nameof(CaseState.DISCOVERED), nameof(CaseState.RANKED), Actor.Engine.Stored(), "{}", now);
        }
    }

    /// <summary>The findings in the order a person should look at them.</summary>
    public IReadOnlyList<FindingRow> RankedFindings()
    {
        var findings = store.ListFindings().ToList();
        findings.Sort((a, b) =>
        {
            var order = Ranker.Compare(Key(a), Key(b));
            return order != 0 ? order : string.CompareOrdinal(a.Id, b.Id);
        });
        return findings;

        static (Confidence, bool, decimal, decimal) Key(FindingRow f) =>
            (Enum.Parse<Confidence>(f.Confidence), f.Urgent, f.Score ?? 0m, f.ExpectedValue ?? 0m);
    }

    private bool IsDue(SourceDescriptor source, SourceQuery query)
    {
        var last = store.GetLastSearch(source.Id, query.Key);
        if (last is null || last.Search.Outcome != SearchOutcomes.Captured) return true;
        var ranAt = DateTimeOffset.Parse(last.Search.RanAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return SweepSchedule.IsDue(ranAt, source.CadenceDays, time.GetUtcNow());
    }

    private QueryReport Record(SourceQuery query, Playbook playbook, string outcome, string? reason, int itemsSeen, int matched, bool truncated)
    {
        store.RecordSearch(new SearchInput
        {
            SourceId = query.SourceId,
            QueryKey = query.Key,
            FamilyName = query.FamilyName,
            GivenName = query.GivenName,
            UsState = query.State,
            Kind = query.Kind.ToString(),
            Mode = playbook.Mode.ToString(),
            Outcome = outcome,
            Reason = reason,
            ItemsSeen = itemsSeen,
            Matched = matched,
            Truncated = truncated,
            RanAt = Now(),
        });
        return new QueryReport(query, outcome, reason, itemsSeen, matched, truncated);
    }

    private int BlockedStreak(string sourceId) =>
        int.TryParse(store.GetConfig(BlockedStreakKey(sourceId)), NumberStyles.None, CultureInfo.InvariantCulture, out var streak) ? streak : 0;

    private string Now() => Clock.Stamp(time);

    private sealed class Counts
    {
        public int New;
        public int Refreshed;
        public int Discarded;
    }
}

public static class Clock
{
    /// <summary>The current time as the engine stores it: UTC, to the second, ISO 8601.</summary>
    public static string Stamp(TimeProvider time) =>
        time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
}
