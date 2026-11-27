using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ClaimSeeker.Browser;
using ClaimSeeker.Core;
using ClaimSeeker.Dispatcher;
using ClaimSeeker.Pipeline;
using ClaimSeeker.Preparer;
using ClaimSeeker.Scout;
using ClaimSeeker.Store;

namespace ClaimSeeker.Engine;

/// <summary>What the command line is given to work with. A harness swaps the clock, the output and the browser.</summary>
public sealed class EngineEnvironment
{
    public TimeProvider Time { get; init; } = TimeProvider.System;
    public TextWriter Out { get; init; } = Console.Out;
    public TextWriter Error { get; init; } = Console.Error;
    public string PlaybookDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "playbooks");
    public IPacer Pacer { get; init; } = new RealPacer();

    /// <summary>Opens the browser a live sweep searches through. Nothing calls this without the --live flag.</summary>
    public Func<BrowserOptions, Task<IBrowserSession>> OpenBrowser { get; init; } =
        async options => await PlaywrightBrowserSession.LaunchAsync(options);

    /// <summary>The workspace to use when neither the flag nor the environment variable names one.</summary>
    public string? DefaultWorkspace { get; init; }
}

/// <summary>
/// The command line. Every command is a thin layer over the services below it: it reads flags,
/// opens the workspace, calls a service and prints what happened.
///
/// One rule lives here and nowhere else: the engine does not contact a source unless the person
/// typed --live. Without it a sweep refuses to run, or replays recorded answers from a folder.
/// </summary>
public sealed class EngineCli(EngineEnvironment env)
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Usage = 2;

    public const string SweepNeedsAMode =
        "A sweep has to be told where to search. Use --live to search the real sources from this computer, " +
        "or --fixtures <folder> to replay recorded answers without touching the network.";

    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            return await DispatchAsync(new Arguments(args));
        }
        catch (Exception ex) when (ex is EngineUsageException or CaseException or HouseholdFormatException or PlaybookFormatException)
        {
            env.Error.WriteLine(ex.Message);
            return Usage;
        }
        catch (BrowserUnavailableException ex)
        {
            env.Error.WriteLine(ex.Message);
            return Failed;
        }
    }

    private async Task<int> DispatchAsync(Arguments a)
    {
        if (a.Has("help") || a.Command is null or "help") return Help();

        switch (a.Command)
        {
            case "version": env.Out.WriteLine($"ClaimSeeker {Version()}"); return Ok;
            case "sources": return Sources();
            case "init": return Init(a);
            case "household": return Household(a);
            case "plan": return Plan(a);
            case "sweep": return await SweepAsync(a);
            case "findings": return Findings(a);
            case "packets": return Packets(a);
            case "packet": return Packet(a);
            case "draft": return Draft(a);
            case "mark": return Mark(a);
            case "report": return Report(a);
            case "audit": return Audit(a);
            case "doctor": return Doctor(a);
            case "delete-all-data": return DeleteAllData(a);
            default: throw new EngineUsageException($"\"{a.Command}\" is not a ClaimSeeker command. Run `claimseeker help` for the list.");
        }
    }

    // ---------- help, version, sources ----------

    private int Help()
    {
        env.Out.WriteLine("""
            ClaimSeeker finds money owed to you and your family, and prepares the claims for you to file.
            It prepares; you review and submit. Filing a claim is free.

            Getting started
              init                              Create your workspace and an example household file to edit.
              household import <file>           Check a household file and store it.
              household show                    Who the engine searches for, and who it does not.

            Searching
              sources                           The places ClaimSeeker knows to look.
              plan                              The searches a sweep would run. Touches nothing.
              sweep --live                      Search the real sources from this computer.
              sweep --fixtures <folder>         Replay recorded answers. Never touches the network.
                    --all                       Run every search, not only the ones that are due.
                    --retry-blocked             Try again a source that has been turning automation away.
                    --headless                  Do not show the browser window.

            Acting on what was found
              findings                          What was found, in the order to look at it.
              report [--out <file>] [--print]   The full report.
              packets                           Write a claim packet for each new finding.
              packet <finding>                  Write or refresh one claim packet.
              draft <finding> [--template <id>] Draft a letter for you to check and send.
              mark <finding> <what happened>    filed, in-review, needs-documents, paid, denied or dismissed.

            Your data
              audit verify                      Check the record of everything the engine has done.
              audit export [--out <file>]       Write that record to a file.
              doctor                            Check the workspace, the playbooks and the record.
              delete-all-data                   Remove everything ClaimSeeker has stored.

            Every command accepts --workspace <folder>.
            """);
        return Ok;
    }

    private static string Version() =>
        typeof(EngineCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private int Sources()
    {
        env.Out.WriteLine("Sources ClaimSeeker knows about. Only those marked \"searched\" are searched by the engine.");
        env.Out.WriteLine();
        foreach (var source in SourceRegistry.All)
        {
            var how = source.PlaybookId is not null && source.Mode == InteractionMode.Auto
                ? "searched"
                : SourceRegistry.EffectiveMode(source).ToString().ToLowerInvariant();
            env.Out.WriteLine($"  {source.Id,-22} {how,-9} {source.Name}");
            env.Out.WriteLine($"  {"",-22} {"",-9} {source.Operator}");
        }

        return Ok;
    }

    // ---------- init and household ----------

    private int Init(Arguments a)
    {
        var workspace = EnginePaths.Resolve(a.Option("workspace"), env.DefaultWorkspace);
        Directory.CreateDirectory(workspace);
        foreach (var folder in new[] { EnginePaths.Packets, EnginePaths.Drafts, EnginePaths.Reports })
            Directory.CreateDirectory(Path.Combine(workspace, folder));

        var marker = Path.Combine(workspace, EnginePaths.Marker);
        if (!File.Exists(marker)) File.WriteAllText(marker, "ClaimSeeker workspace. Created by `claimseeker init`.\n");

        using (var session = Open(a))
        {
            // Opening the session creates the database.
        }

        var example = Path.Combine(workspace, EnginePaths.ExampleHousehold);
        if (!File.Exists(example)) File.WriteAllText(example, ExampleHousehold.For(Clock.Today(env.Time)));

        env.Out.WriteLine($"Workspace ready at {workspace}");
        env.Out.WriteLine($"Next: copy {EnginePaths.ExampleHousehold} there, replace the invented people with your own household, and run");
        env.Out.WriteLine("  claimseeker household import <your file>");
        env.Out.WriteLine("Do not put a Social Security number or an account number in it. ClaimSeeker has no use for either and will refuse the file.");
        return Ok;
    }

    private int Household(Arguments a)
    {
        var action = a.Positional(0) ?? throw new EngineUsageException("Use `household import <file>` or `household show`.");
        using var session = Open(a);

        switch (action)
        {
            case "import":
                var file = a.Positional(1) ?? throw new EngineUsageException("Name the household file to import.");
                if (!File.Exists(file)) throw new EngineUsageException($"There is no file at {file}.");
                if (new FileInfo(file).Length > HouseholdJson.MaxBytes) throw new EngineUsageException("That file is too large to be a household file.");

                var household = HouseholdJson.Parse(File.ReadAllText(file));
                var validation = HouseholdValidator.Validate(household, Clock.Today(env.Time));
                if (!validation.Ok)
                {
                    env.Error.WriteLine("The household file was not imported:");
                    foreach (var error in validation.Errors) env.Error.WriteLine($"  - {error}");
                    return Usage;
                }

                session.Store.SaveHousehold(HouseholdJson.Serialize(household), household.Subjects.Count, Actor.User.Stored(), Clock.Stamp(env.Time));
                env.Out.WriteLine($"Household stored: {household.Subjects.Count} people.");
                PrintPeople(household);
                foreach (var warning in validation.Warnings) env.Out.WriteLine($"  note: {warning}");
                return Ok;

            case "show":
                PrintPeople(LoadHousehold(session.Store));
                return Ok;

            default:
                throw new EngineUsageException("Use `household import <file>` or `household show`.");
        }
    }

    private void PrintPeople(Household household)
    {
        foreach (var subject in household.Subjects)
        {
            var role = subject.Relationship.ToString().ToLowerInvariant() + (subject.Deceased ? ", deceased" : "");
            env.Out.WriteLine(subject.MaySearch
                ? $"  {subject.FullName} ({role}): searched"
                : $"  {subject.FullName} ({role}): not searched, because no consent basis is recorded");
        }
    }

    // ---------- plan and sweep ----------

    private int Plan(Arguments a)
    {
        using var session = Open(a);
        var household = LoadHousehold(session.Store);
        var playbooks = LoadPlaybooks();

        foreach (var source in SourceRegistry.Searchable)
        {
            if (!playbooks.ContainsKey(source.Id)) continue;
            var plan = QueryPlanner.PlanSurnameSweep(household, source);
            env.Out.WriteLine($"{source.Name}: {plan.Queries.Count} searches, one at a time.");
            foreach (var query in plan.Queries)
            {
                var last = session.Store.GetLastSearch(source.Id, query.Key);
                var when = last is null ? "never run" : $"last run {ClaimPacketBuilder.DateOnlyText(last.Search.RanAt)} ({last.Search.Outcome.Replace('_', ' ')})";
                env.Out.WriteLine($"  {query.FamilyName,-18} {Describe(query.Kind),-28} for {string.Join(", ", query.SubjectIds)}; {when}");
            }

            foreach (var skipped in plan.Skipped) env.Out.WriteLine($"  not searched: {skipped.SubjectId}. {skipped.Reason}");
        }

        var guided = SourceRegistry.All.Count(s => SourceRegistry.EffectiveMode(s) is InteractionMode.Guided or InteractionMode.Draft);
        env.Out.WriteLine($"{guided} other sources are listed in the report for you to check yourself.");
        return Ok;
    }

    private static string Describe(VariantKind kind) => kind switch
    {
        VariantKind.FamilyOnly => "family name, nationwide",
        VariantKind.PriorFamilyOnly => "former family name",
        VariantKind.FamilyPart => "part of the family name",
        VariantKind.Misspelling => "likely misspelling",
        VariantKind.GivenFamily or VariantKind.GivenPriorFamily => "given and family name",
        _ => kind.ToString(),
    };

    private async Task<int> SweepAsync(Arguments a)
    {
        var fixtures = a.Option("fixtures");
        var live = a.Has("live");
        if (live && fixtures is not null) throw new EngineUsageException("Use --live or --fixtures, not both.");
        if (!live && fixtures is null) throw new EngineUsageException(SweepNeedsAMode);
        if (fixtures is not null && !Directory.Exists(fixtures)) throw new EngineUsageException($"There is no fixtures folder at {fixtures}.");

        using var session = Open(a);
        var household = LoadHousehold(session.Store);
        var playbooks = LoadPlaybooks();
        var sources = SourceRegistry.Searchable.Where(s => playbooks.ContainsKey(s.Id)).ToList();
        if (sources.Count == 0) throw new EngineUsageException("No source has a playbook this build can run.");

        IBrowserSession? browser = null;
        IQueryExecutor executor;
        if (live)
        {
            env.Out.WriteLine("Searching the real sources from this computer, one search at a time.");
            browser = await env.OpenBrowser(new BrowserOptions { Headless = a.Has("headless") });
            executor = new BrowserQueryExecutor(browser, env.Pacer);
        }
        else
        {
            env.Out.WriteLine($"Replaying recorded answers from {fixtures}. The network is not used.");
            executor = new FixtureQueryExecutor(fixtures!);
        }

        try
        {
            var sweeps = new SweepService(session.Store, env.Time);
            var options = new SweepOptions { OnlyDue = !a.Has("all"), RetryBlocked = a.Has("retry-blocked") };

            foreach (var source in sources)
            {
                var report = await sweeps.SweepAsync(household, source, playbooks[source.Id], executor, options);
                env.Out.WriteLine();
                env.Out.WriteLine(source.Name);
                foreach (var query in report.Queries) env.Out.WriteLine("  " + DescribeOutcome(query));
                foreach (var skipped in report.Skipped) env.Out.WriteLine($"  not searched: {skipped.SubjectId}. {skipped.Reason}");
                env.Out.WriteLine($"  {report.NewFindings} new, {report.RefreshedFindings} seen again, {report.DiscardedRecords} records belonging to other people discarded.");
            }

            var written = new CaseService(session.Store, env.Time).PreparePackets(household, Path.Combine(session.Workspace, EnginePaths.Packets));
            if (written > 0) env.Out.WriteLine($"\n{written} claim {(written == 1 ? "packet" : "packets")} written to {Path.Combine(session.Workspace, EnginePaths.Packets)}");

            env.Out.WriteLine();
            env.Out.WriteLine(Plain(FindingsReport.Headline(FindingsReport.Totals(session.Store.ListFindings()))));
            env.Out.WriteLine("Run `claimseeker findings` to see them, or `claimseeker report` for the full report.");
            return Ok;
        }
        finally
        {
            if (browser is not null) await browser.DisposeAsync();
        }
    }

    private static string DescribeOutcome(QueryReport report)
    {
        var name = report.Query.GivenName is null ? report.Query.FamilyName : $"{report.Query.GivenName} {report.Query.FamilyName}";
        return report.Outcome switch
        {
            SearchOutcomes.Captured => $"{name}: {report.ItemsSeen} records, {report.Matched} matched to your household{(report.Truncated ? " (list was cut off; narrowed by given name)" : "")}.",
            SearchOutcomes.NotDue => $"{name}: not due yet.",
            SearchOutcomes.Deferred => $"{name}: not sent this time. {report.Reason}",
            SearchOutcomes.ShapeChanged => $"{name}: the source's answer was not in the expected form, so nothing was read. {report.Reason}",
            _ => $"{name}: needs you. {report.Reason}",
        };
    }

    // ---------- findings, packets, drafts, marks ----------

    private int Findings(Arguments a)
    {
        using var session = Open(a);
        var household = LoadHousehold(session.Store);
        var ranked = new SweepService(session.Store, env.Time).RankedFindings().Where(f => f.DuplicateOf is null).ToList();
        if (ranked.Count == 0)
        {
            env.Out.WriteLine("Nothing found yet. Run a sweep.");
            return Ok;
        }

        env.Out.WriteLine(Plain(FindingsReport.Headline(FindingsReport.Totals(ranked))));
        env.Out.WriteLine();
        var n = 1;
        foreach (var finding in ranked)
        {
            var value = MoneyRange.FromBounds(finding.ValueLow, finding.ValueHigh).Display();
            var person = household.Find(finding.SubjectId)?.FullName ?? finding.SubjectId;
            var place = UntrustedText.Defang(ClaimPacketBuilder.Place(finding)) is { Length: > 0 } shown ? shown : "no location";
            var holder = string.IsNullOrEmpty(finding.HolderName) ? "holder not named" : UntrustedText.Defang(finding.HolderName);
            env.Out.WriteLine($"{n++,3}. {finding.Id}  {value}, {holder}");
            env.Out.WriteLine($"     {person}; {finding.Confidence.ToLowerInvariant()} match; {place}; {FindingsReport.StateWords(finding.CaseState)}");
        }

        return Ok;
    }

    private int Packets(Arguments a)
    {
        using var session = Open(a);
        var folder = Path.Combine(session.Workspace, EnginePaths.Packets);
        var written = new CaseService(session.Store, env.Time).PreparePackets(LoadHousehold(session.Store), folder);
        env.Out.WriteLine(written == 0 ? "No new findings need a packet." : $"{written} claim {(written == 1 ? "packet" : "packets")} written to {folder}");
        return Ok;
    }

    private int Packet(Arguments a)
    {
        var id = a.Positional(0) ?? throw new EngineUsageException("Name the finding, for example `claimseeker packet f_1a2b3c`.");
        using var session = Open(a);
        var path = new CaseService(session.Store, env.Time).PreparePacket(id, LoadHousehold(session.Store), Path.Combine(session.Workspace, EnginePaths.Packets));
        env.Out.WriteLine($"Claim packet written to {path}");
        return Ok;
    }

    private int Draft(Arguments a)
    {
        var id = a.Positional(0) ?? throw new EngineUsageException("Name the finding, for example `claimseeker draft f_1a2b3c`.");
        using var session = Open(a);
        var household = LoadHousehold(session.Store);
        var outbox = new FileDraftOutbox(Path.Combine(session.Workspace, EnginePaths.Drafts));
        var outcome = new CaseService(session.Store, env.Time).Draft(id, a.Option("template"), household, outbox);

        if (!outcome.Passed)
        {
            env.Out.WriteLine("No letter was written. The Attestation Gate blocked it:");
            foreach (var violation in outcome.Violations) env.Out.WriteLine($"  - {violation.Message}");
            return Failed;
        }

        env.Out.WriteLine($"Draft written to {outcome.Path}");
        env.Out.WriteLine(outcome.SenderIsAccountHolder
            ? "Open it in your mail program, check every line, add the address it is for, and send it yourself. Nothing has been sent."
            : $"This letter is written for {outcome.SenderName} to send in their own name. Nothing has been sent.");
        env.Out.WriteLine();
        env.Out.WriteLine($"Subject: {outcome.Draft!.SubjectLine}");
        env.Out.WriteLine();
        env.Out.WriteLine(outcome.Draft.Body);
        return Ok;
    }

    private int Mark(Arguments a)
    {
        var id = a.Positional(0) ?? throw new EngineUsageException("Name the finding and what happened, for example `claimseeker mark f_1a2b3c filed`.");
        var what = a.Positional(1) ?? throw new EngineUsageException("Say what happened: filed, in-review, needs-documents, paid, denied or dismissed.");
        var target = what.ToLowerInvariant() switch
        {
            "filed" => CaseState.FILED_BY_USER,
            "in-review" => CaseState.IN_REVIEW,
            "needs-documents" => CaseState.NEEDS_DOCUMENTS,
            "paid" => CaseState.PAID,
            "denied" => CaseState.DENIED,
            "dismissed" => CaseState.DISMISSED,
            _ => throw new EngineUsageException($"\"{what}\" is not one of: filed, in-review, needs-documents, paid, denied, dismissed."),
        };

        using var session = Open(a);
        var finding = new CaseService(session.Store, env.Time).Mark(id, target);
        env.Out.WriteLine($"{finding.Id} is now marked {FindingsReport.StateWords(finding.CaseState)}.");
        return Ok;
    }

    // ---------- report ----------

    private int Report(Arguments a)
    {
        using var session = Open(a);
        var household = LoadHousehold(session.Store);
        var ranked = new SweepService(session.Store, env.Time).RankedFindings();
        var input = new ReportService(session.Store, env.Time).BuildInput(household, ranked, LoadPlaybooks());
        var markdown = FindingsReport.Markdown(input);

        if (a.Has("print"))
        {
            env.Out.WriteLine(markdown);
            return Ok;
        }

        var path = a.Option("out")
                   ?? Path.Combine(session.Workspace, EnginePaths.Reports, $"claimseeker-report-{Clock.Today(env.Time):yyyy-MM-dd}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, markdown);
        env.Out.WriteLine(Plain(FindingsReport.Headline(FindingsReport.Totals(ranked))));
        env.Out.WriteLine($"Report written to {path}");
        return Ok;
    }

    // ---------- audit, doctor, deletion ----------

    private int Audit(Arguments a)
    {
        var action = a.Positional(0) ?? throw new EngineUsageException("Use `audit verify` or `audit export`.");
        using var session = Open(a);

        switch (action)
        {
            case "verify":
                var verification = session.Store.VerifyAudit();
                var count = session.Store.GetEvents().Count;
                if (verification.Ok)
                {
                    env.Out.WriteLine($"The record is intact: {count} entries, each linked to the one before.");
                    return Ok;
                }

                env.Error.WriteLine($"The record has been altered at entry {verification.FirstBrokenSeq}. {verification.Reason}");
                return Failed;

            case "export":
                var path = a.Option("out") ?? Path.Combine(session.Workspace, EnginePaths.Reports, "claimseeker-audit.json");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var check = session.Store.VerifyAudit();
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    exportedAt = Clock.Stamp(env.Time),
                    intact = check.Ok,
                    firstBrokenEntry = check.FirstBrokenSeq,
                    entries = session.Store.GetEvents(),
                }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                env.Out.WriteLine($"Record written to {path}");
                return check.Ok ? Ok : Failed;

            default:
                throw new EngineUsageException("Use `audit verify` or `audit export`.");
        }
    }

    private int Doctor(Arguments a)
    {
        var problems = 0;
        void Check(bool ok, string good, string bad)
        {
            env.Out.WriteLine(ok ? $"  ok    {good}" : $"  FAIL  {bad}");
            if (!ok) problems++;
        }

        var workspace = EnginePaths.Resolve(a.Option("workspace"), env.DefaultWorkspace);
        env.Out.WriteLine($"Workspace: {workspace}");
        var hasWorkspace = File.Exists(Path.Combine(workspace, EnginePaths.Marker));
        Check(hasWorkspace, "The workspace exists.", "There is no workspace here. Run `claimseeker init`.");

        Dictionary<string, Playbook>? playbooks = null;
        try { playbooks = LoadPlaybooks(); }
        catch (Exception ex) when (ex is PlaybookFormatException or EngineUsageException)
        {
            Check(false, "", $"A playbook could not be read. {ex.Message}");
        }

        if (playbooks is not null)
        {
            Check(true, $"{playbooks.Count} {(playbooks.Count == 1 ? "playbook reads" : "playbooks read")} and {(playbooks.Count == 1 ? "validates" : "validate")}.", "");
            foreach (var source in SourceRegistry.Searchable)
                Check(playbooks.ContainsKey(source.Id), $"{source.Name} has its playbook.", $"{source.Name} is marked searchable but its playbook is missing.");
        }

        if (hasWorkspace)
        {
            using var session = Open(a);
            var audit = session.Store.VerifyAudit();
            Check(audit.Ok, $"The record is intact ({session.Store.GetEvents().Count} entries).", $"The record has been altered at entry {audit.FirstBrokenSeq}.");

            var json = session.Store.GetHouseholdJson();
            if (json is null) Check(false, "", "No household has been imported yet.");
            else
            {
                var validation = HouseholdValidator.Validate(HouseholdJson.Parse(json), Clock.Today(env.Time));
                Check(validation.Ok, "The household is valid.", "The stored household no longer validates: " + string.Join(" ", validation.Errors));
            }
        }

        env.Out.WriteLine(problems == 0 ? "Everything checks out." : $"{problems} {(problems == 1 ? "problem" : "problems")} found.");
        return problems == 0 ? Ok : Failed;
    }

    private int DeleteAllData(Arguments a)
    {
        var workspace = EnginePaths.Resolve(a.Option("workspace"), env.DefaultWorkspace);
        if (DataDeletion.Refusal(workspace) is { } refusal) throw new EngineUsageException(refusal);

        var phrase = DataDeletion.ConfirmationPhrase(workspace);
        if (a.Option("confirm") is not { } typed)
        {
            env.Out.WriteLine("This removes everything ClaimSeeker has stored: your household, every finding, packet, draft and report, and the record of what was done.");
            env.Out.WriteLine($"Folder: {workspace}");
            env.Out.WriteLine("Nothing has been deleted. To go ahead, run:");
            env.Out.WriteLine($"  claimseeker delete-all-data --confirm \"{phrase}\"");
            return Ok;
        }

        if (!string.Equals(typed, phrase, StringComparison.Ordinal))
            throw new EngineUsageException("The confirmation does not match. Nothing was deleted. Run the command without --confirm to see the exact phrase.");

        // Taken and released first, so a running engine is never deleted out from under itself.
        using (WorkspaceLock.Acquire(workspace)) { }
        var gone = DataDeletion.Delete(workspace);
        env.Out.WriteLine(gone ? $"Deleted. Nothing is left at {workspace}." : $"The folder could not be fully removed. Check {workspace}.");
        return gone ? Ok : Failed;
    }

    // ---------- plumbing ----------

    private sealed class Session(string workspace, WorkspaceLock held, SqliteClaimStore store) : IDisposable
    {
        public string Workspace { get; } = workspace;
        public SqliteClaimStore Store { get; } = store;

        public void Dispose()
        {
            Store.Dispose();
            held.Dispose();
        }
    }

    private Session Open(Arguments a)
    {
        var workspace = EnginePaths.Resolve(a.Option("workspace"), env.DefaultWorkspace);
        if (!File.Exists(Path.Combine(workspace, EnginePaths.Marker)))
            throw new EngineUsageException($"There is no ClaimSeeker workspace at {workspace}. Run `claimseeker init` first.");

        var held = WorkspaceLock.Acquire(workspace);
        try
        {
            var store = new SqliteClaimStore(Path.Combine(workspace, EnginePaths.Database));
            store.Initialize();
            return new Session(workspace, held, store);
        }
        catch
        {
            held.Dispose();
            throw;
        }
    }

    private static Household LoadHousehold(IClaimStore store)
    {
        var json = store.GetHouseholdJson()
                   ?? throw new EngineUsageException("No household has been imported yet. Run `claimseeker household import <file>`.");
        return HouseholdJson.Parse(json);
    }

    /// <summary>Reads every playbook beside the executable. One that does not validate stops the command.</summary>
    private Dictionary<string, Playbook> LoadPlaybooks()
    {
        if (!Directory.Exists(env.PlaybookDirectory))
            throw new EngineUsageException($"The playbooks folder is missing at {env.PlaybookDirectory}.");

        var playbooks = new Dictionary<string, Playbook>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(env.PlaybookDirectory, "*.md").OrderBy(f => f, StringComparer.Ordinal))
        {
            if (new FileInfo(file).Length > PlaybookParser.MaxBytes) throw new PlaybookFormatException($"{Path.GetFileName(file)} is too large to be a playbook.");
            var playbook = PlaybookParser.Parse(File.ReadAllText(file));
            if (!playbooks.TryAdd(playbook.Source, playbook))
                throw new PlaybookFormatException($"Two playbooks claim the source \"{playbook.Source}\".");
        }

        return playbooks;
    }

    /// <summary>Report text without its markdown emphasis, for the terminal.</summary>
    private static string Plain(string markdown) => markdown.Replace("**", "", StringComparison.Ordinal);

    /// <summary>The command line, split into a command, its positional words, its options and its flags.</summary>
    private sealed class Arguments
    {
        private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal) { "workspace", "fixtures", "template", "out", "confirm" };
        private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "live", "all", "retry-blocked", "headless", "print", "help" };

        private readonly List<string> _positionals = [];
        private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
        private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

        public Arguments(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    if (Command is null) Command = arg;
                    else _positionals.Add(arg);
                    continue;
                }

                var name = arg[2..];
                if (Flags.Contains(name)) _flags.Add(name);
                else if (ValueOptions.Contains(name))
                {
                    if (i + 1 >= args.Length) throw new EngineUsageException($"--{name} needs a value.");
                    _options[name] = args[++i];
                }
                else throw new EngineUsageException($"--{name} is not an option ClaimSeeker knows.");
            }
        }

        public string? Command { get; }
        public bool Has(string flag) => _flags.Contains(flag);
        public string? Option(string name) => _options.GetValueOrDefault(name);
        public string? Positional(int index) => index < _positionals.Count ? _positionals[index] : null;
    }
}
