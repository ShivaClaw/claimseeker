namespace ClaimSeeker.Core;

/// <summary>
/// How the engine may work with a source. This is a property of the source, not a setting: a source
/// that asks for a person at the keyboard gets one.
/// </summary>
public enum InteractionMode
{
    /// <summary>The engine runs the search unattended, as declared automation, at a human pace.</summary>
    Auto,
    /// <summary>The engine opens the search with the fields filled in; the person submits it and answers any challenge.</summary>
    Assisted,
    /// <summary>The person does the search themselves from a link and a checklist; the engine tracks it.</summary>
    Guided,
    /// <summary>There is nothing to search. The engine writes a letter for the person to send.</summary>
    Draft,
}

public enum SourceStatus
{
    /// <summary>A playbook exists and has been run against the live source by this codebase.</summary>
    Proven,
    /// <summary>A playbook exists and worked in an earlier prototype; not yet re-run by this codebase.</summary>
    ProvenLegacy,
    /// <summary>On the map, no playbook yet.</summary>
    Planned,
}

/// <summary>What a source is searched by.</summary>
public enum QueryBy { Name, Employer, Case, None }

/// <summary>One place money owed to a person can be found.</summary>
public sealed record SourceDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Operator { get; init; }
    public required InteractionMode Mode { get; init; }
    public required SourceStatus Status { get; init; }
    public required QueryBy QueryBy { get; init; }

    /// <summary>
    /// The only hosts the engine will open, or link a person to, for this source. A link anywhere
    /// else is never shown, wherever the engine read it.
    /// </summary>
    public required IReadOnlyList<string> OfficialHosts { get; init; }

    /// <summary>Where a person starts, when that address has been checked against the source itself.</summary>
    public string? EntryUrl { get; init; }

    /// <summary>How often the source is worth looking at again, in days.</summary>
    public required int CadenceDays { get; init; }

    public string? PlaybookId { get; init; }

    /// <summary>What happens to the money if nobody claims it in time, in plain words.</summary>
    public string? DeadlineNote { get; init; }

    public required string What { get; init; }

    public bool IsOfficialHost(string host)
    {
        foreach (var official in OfficialHosts)
        {
            if (string.Equals(host, official, StringComparison.OrdinalIgnoreCase)) return true;
            if (host.EndsWith("." + official, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
