using ClaimSeeker.Core;

namespace ClaimSeeker.Scout;

/// <summary>One search to run against one source.</summary>
public sealed record SourceQuery
{
    public required string SourceId { get; init; }
    public required string FamilyName { get; init; }
    public string? GivenName { get; init; }
    public string? State { get; init; }
    public required VariantKind Kind { get; init; }

    /// <summary>Everyone in the household this one search can find money for.</summary>
    public required IReadOnlyList<string> SubjectIds { get; init; }

    /// <summary>Identifies the search across sweeps, so the engine knows when it last ran.</summary>
    public string Key => $"{SourceId}|{FamilyName}|{GivenName ?? ""}|{State ?? ""}";

    /// <summary>The values a playbook's steps may refer to.</summary>
    public IReadOnlyDictionary<string, string?> Inputs() => new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["familyName"] = FamilyName,
        ["givenName"] = GivenName,
        ["state"] = State,
    };
}

public sealed record SkippedSubject(string SubjectId, string Reason);

public sealed record QueryPlan(
    SourceDescriptor Source,
    IReadOnlyList<SourceQuery> Queries,
    IReadOnlyList<SkippedSubject> Skipped);

/// <summary>
/// Decides which searches to run. The rule is the one the MissingMoney prototype proved: search each
/// family name once, nationwide, and sort out whose record is whose on this machine. One search then
/// covers everyone in the household who shares the name, and the source sees the fewest requests that
/// can do the job.
/// </summary>
public static class QueryPlanner
{
    public static QueryPlan PlanSurnameSweep(Household household, SourceDescriptor source, VariantOptions? options = null)
    {
        if (source.QueryBy != QueryBy.Name)
            throw new ArgumentException($"{source.Id} is not searched by name.", nameof(source));

        var skipped = new List<SkippedSubject>();
        var byFamily = new Dictionary<string, (VariantKind Kind, List<string> Subjects)>(StringComparer.Ordinal);

        foreach (var subject in household.Subjects)
        {
            if (!subject.MaySearch)
            {
                skipped.Add(new SkippedSubject(subject.Id, "No consent basis is recorded for this person, so the engine does not search for them."));
                continue;
            }

            foreach (var variant in NameVariants.For(subject, options))
            {
                if (variant.Given is not null) continue; // given names narrow a sweep later; they do not start one

                if (!byFamily.TryGetValue(variant.Family, out var entry))
                    byFamily[variant.Family] = entry = (variant.Kind, new List<string>());
                else if (variant.Kind < entry.Kind)
                    byFamily[variant.Family] = entry = (variant.Kind, entry.Subjects);

                if (!entry.Subjects.Contains(subject.Id)) entry.Subjects.Add(subject.Id);
            }
        }

        var queries = byFamily
            .OrderBy(pair => pair.Value.Kind)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SourceQuery
            {
                SourceId = source.Id,
                FamilyName = pair.Key,
                Kind = pair.Value.Kind,
                SubjectIds = pair.Value.Subjects.ToList(),
            })
            .ToList();

        return new QueryPlan(source, queries, skipped);
    }

    /// <summary>
    /// The narrower searches to run when a family-name sweep came back cut off: one per person the
    /// sweep was for, by given name. Only people whose own name the sweep used are refined, so a
    /// misspelling sweep never fans out into more guesses.
    /// </summary>
    public static IReadOnlyList<SourceQuery> Refine(SourceQuery sweep, Household household)
    {
        if (sweep.GivenName is not null) return [];
        if (sweep.Kind is VariantKind.Misspelling or VariantKind.FamilyPart) return [];

        var refined = new List<SourceQuery>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var subjectId in sweep.SubjectIds)
        {
            var subject = household.Find(subjectId);
            if (subject is null || !subject.MaySearch) continue;

            var given = NameText.Normalize(subject.GivenName);
            if (given.Length == 0 || !seen.Add(given)) continue;

            refined.Add(new SourceQuery
            {
                SourceId = sweep.SourceId,
                FamilyName = sweep.FamilyName,
                GivenName = given,
                Kind = sweep.Kind == VariantKind.PriorFamilyOnly ? VariantKind.GivenPriorFamily : VariantKind.GivenFamily,
                SubjectIds = household.Subjects
                    .Where(s => s.MaySearch && NameText.Normalize(s.GivenName) == given && sweep.SubjectIds.Contains(s.Id))
                    .Select(s => s.Id)
                    .ToList(),
            });
        }

        return refined;
    }
}

/// <summary>
/// When a search is worth running again. State databases refresh on a yearly reporting cycle, so the
/// engine being always on does not mean it is always searching: each source has a cadence, and a
/// search that ran inside it is not due.
/// </summary>
public static class SweepSchedule
{
    public static bool IsDue(DateTimeOffset? lastRun, int cadenceDays, DateTimeOffset now) =>
        lastRun is null || now >= NextDue(lastRun, cadenceDays, now);

    public static DateTimeOffset NextDue(DateTimeOffset? lastRun, int cadenceDays, DateTimeOffset now) =>
        lastRun is { } last ? last + TimeSpan.FromDays(Math.Max(1, cadenceDays)) : now;
}
