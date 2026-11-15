// EngineHarness: the command line, run in-process against a temporary workspace with a fixed clock.
// It checks the things only the outermost layer can get wrong: that nothing contacts a source
// without --live, that a bad household is refused whole, that the workspace is locked and can be
// deleted only on purpose, and that the commands print what actually happened.
using ClaimSeeker.Browser;
using ClaimSeeker.Core;
using ClaimSeeker.Engine;
using ClaimSeeker.Scout;
using ClaimSeeker.Store;
using static Harness;

Title("ClaimSeeker engine command line");
using var temp = NewTempFolder("engine");
var playbooks = Path.Combine(RepoRoot(), "playbooks");
var fixtures = Fixtures("sources");

Cli NewCli(string name, string start = "2026-11-15T12:00:00Z") => new(temp.File(name), playbooks, FixedTime.At(start));

// ---------- before there is a workspace ----------
Section("Commands that need no workspace");
{
    var cli = NewCli("none");
    Check("help lists the commands", cli.Run("help") is { Exit: 0 } help && help.Out.Contains("sweep --live", StringComparison.Ordinal) && help.Out.Contains("Filing a claim is free.", StringComparison.Ordinal));
    Check("no command at all prints help", cli.Run() is { Exit: 0 } bare && bare.Out.Contains("Getting started", StringComparison.Ordinal));
    Check("version prints a version", cli.Run("version") is { Exit: 0 } version && version.Out.StartsWith("ClaimSeeker ", StringComparison.Ordinal));
    var sources = cli.Run("sources");
    Check("sources lists the map", sources.Exit == 0 && sources.Out.Contains("missingmoney", StringComparison.Ordinal) && sources.Out.Contains("county-surplus", StringComparison.Ordinal));
    Check("only one source is marked searched", sources.Out.Split('\n').Count(l => l.StartsWith("  ", StringComparison.Ordinal) && l.Contains(" searched ", StringComparison.Ordinal)) == 1);
    Check("an unknown command is a usage error", cli.Run("negotiate") is { Exit: 2 } unknown && unknown.Err.Contains("not a ClaimSeeker command", StringComparison.Ordinal));
    Check("an unknown option is a usage error", cli.Run("plan", "--stealth") is { Exit: 2 });
    Check("an option without its value is a usage error", cli.Run("report", "--out") is { Exit: 2 });
    foreach (var command in new[] { "plan", "findings", "packets", "report", "household show", "audit verify" })
        Check($"`{command}` before init asks for init", cli.Run(command.Split(' ')) is { Exit: 2 } early && early.Err.Contains("claimseeker init", StringComparison.Ordinal));
    Check("none of that created a workspace", !Directory.Exists(temp.File("none")));
}

// ---------- init and the household ----------
Section("Init makes a workspace and an example to edit");
var main = NewCli("main");
{
    var init = main.Run("init");
    Check("init succeeds", init.Exit == 0, init.Err);
    Check("the workspace is marked as one", File.Exists(Path.Combine(main.Workspace, EnginePaths.Marker)));
    Check("the database is created", File.Exists(Path.Combine(main.Workspace, EnginePaths.Database)));
    Check("the folders for packets, drafts and reports exist", new[] { EnginePaths.Packets, EnginePaths.Drafts, EnginePaths.Reports }.All(f => Directory.Exists(Path.Combine(main.Workspace, f))));
    Check("it warns against putting an identifier in the file", init.Out.Contains("Social Security number", StringComparison.Ordinal));
    var example = File.ReadAllText(Path.Combine(main.Workspace, EnginePaths.ExampleHousehold));
    Check("the example household parses and validates", HouseholdValidator.Validate(HouseholdJson.Parse(example), new DateOnly(2026, 11, 15)).Ok);
    Check("the example is dated today", example.Contains("\"attestedOn\": \"2026-11-15\"", StringComparison.Ordinal));
    File.WriteAllText(Path.Combine(main.Workspace, EnginePaths.ExampleHousehold), example + " ");
    Check("running init again changes nothing", main.Run("init").Exit == 0 && File.ReadAllText(Path.Combine(main.Workspace, EnginePaths.ExampleHousehold)).EndsWith(' '));
    Check("nothing can be planned before a household is imported", main.Run("plan") is { Exit: 2 } noHousehold && noHousehold.Err.Contains("household import", StringComparison.Ordinal));
}

Section("A household file is taken whole or not at all");
{
    var good = Path.Combine(main.Workspace, EnginePaths.ExampleHousehold);
    var json = File.ReadAllText(good);

    var withNumber = temp.File("with-number.json");
    File.WriteAllText(withNumber, json.Replace("\"Acme Widgets\"", "\"Acme Widgets 123-45-6789\""));
    var refused = main.Run("household", "import", withNumber);
    Check("a file with a Social Security number in it is refused", refused.Exit == 2 && refused.Err.Contains("Social Security number", StringComparison.Ordinal));
    Check("and nothing from it is stored", main.Run("household", "show") is { Exit: 2 });

    var typo = temp.File("typo.json");
    File.WriteAllText(typo, json.Replace("\"priorFamilyNames\"", "\"priorFamilyName\""));
    Check("a misspelled field name is refused, not silently dropped", main.Run("household", "import", typo) is { Exit: 2 });
    Check("a file that does not exist is a usage error", main.Run("household", "import", temp.File("nope.json")) is { Exit: 2 });
    Check("import with no file named is a usage error", main.Run("household", "import") is { Exit: 2 });

    var imported = main.Run("household", "import", good);
    Check("a valid file is stored", imported.Exit == 0 && imported.Out.Contains("Household stored: 3 people.", StringComparison.Ordinal), imported.Err);
    Check("it says who is searched", imported.Out.Contains("Jane Quinn Sample (self): searched", StringComparison.Ordinal) && imported.Out.Contains("Robert Sample (parent, deceased): searched", StringComparison.Ordinal));
    Check("and who is not, and why", imported.Out.Contains("Alex Sample (spouse): not searched, because no consent basis is recorded", StringComparison.Ordinal));
    Check("show repeats it from the store", main.Run("household", "show").Out.Contains("Alex Sample (spouse): not searched", StringComparison.Ordinal));
}

// ---------- plan ----------
Section("Plan shows what would be searched and touches nothing");
{
    var before = Events(main.Workspace);
    var plan = main.Run("plan");
    Check("plan succeeds", plan.Exit == 0, plan.Err);
    Check("it names both family-name searches", plan.Out.Contains("SAMPLE", StringComparison.Ordinal) && plan.Out.Contains("EXAMPLE", StringComparison.Ordinal) && plan.Out.Contains("former family name", StringComparison.Ordinal));
    Check("it says who each search is for", plan.Out.Contains("for self, dad", StringComparison.Ordinal));
    Check("it says who is left out", plan.Out.Contains("not searched: alex", StringComparison.Ordinal));
    Check("it says the searches have never run", plan.Out.Contains("never run", StringComparison.Ordinal));
    Check("planning writes nothing to the record", Events(main.Workspace) == before);
    Check("planning opens no browser", main.BrowsersOpened == 0);
}

// ---------- the --live rule ----------
Section("Nothing contacts a source unless the person types --live");
{
    var noMode = main.Run("sweep");
    Check("a sweep with no mode refuses to run", noMode.Exit == 2 && noMode.Err.Contains(EngineCli.SweepNeedsAMode, StringComparison.Ordinal));
    Check("both modes at once is refused", main.Run("sweep", "--live", "--fixtures", fixtures) is { Exit: 2 });
    Check("a fixtures folder that does not exist is refused", main.Run("sweep", "--fixtures", temp.File("missing")) is { Exit: 2 });
    Check("none of those opened a browser", main.BrowsersOpened == 0);
    Check("none of those recorded a search", Searches(main.Workspace) == 0);
}

Section("A sweep from recorded answers");
{
    var sweep = main.Run("sweep", "--fixtures", fixtures);
    Check("the sweep succeeds", sweep.Exit == 0, sweep.Err);
    Check("it says the network is not used", sweep.Out.Contains("The network is not used.", StringComparison.Ordinal));
    Check("it reports each search", sweep.Out.Contains("SAMPLE: 13 records, 10 matched to your household.", StringComparison.Ordinal) && sweep.Out.Contains("EXAMPLE: 2 records, 1 matched to your household.", StringComparison.Ordinal));
    Check("it says what was kept and what was let go", sweep.Out.Contains("11 new, 0 seen again, 4 records belonging to other people discarded.", StringComparison.Ordinal));
    Check("it writes the claim packets", sweep.Out.Contains("11 claim packets written", StringComparison.Ordinal) && Directory.GetFiles(Path.Combine(main.Workspace, EnginePaths.Packets), "*.md").Length == 11);
    Check("it ends with the honest total", sweep.Out.Contains("At least $1,889.90 across 10 matches the engine trusts.", StringComparison.Ordinal));
    Check("a fixture sweep opens no browser", main.BrowsersOpened == 0);

    using var store = Open(main.Workspace);
    var findings = store.ListFindings();
    Check("no record of the person with no consent basis is in the database", !findings.Any(f => f.OwnerName.Contains("ALEX", StringComparison.Ordinal)));
    Check("no stranger's record is in the database", findings.All(f => f.SubjectId is "self" or "dad"));
    Check("no stranger's name is anywhere in the workspace files",
        !Directory.EnumerateFiles(main.Workspace, "*.md", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains("MARIA", StringComparison.Ordinal)));

    var again = main.Run("sweep", "--fixtures", fixtures);
    Check("a second sweep straight away finds nothing due", again.Out.Contains("SAMPLE: not due yet.", StringComparison.Ordinal) && again.Out.Contains("0 new, 0 seen again", StringComparison.Ordinal));
    var forced = main.Run("sweep", "--fixtures", fixtures, "--all");
    Check("--all runs them anyway and finds the same records", forced.Out.Contains("0 new, 11 seen again", StringComparison.Ordinal));
}

Section("A live sweep goes through the browser, at the playbook's pace");
{
    var live = NewCli("live");
    live.Run("init");
    live.Run("household", "import", Path.Combine(live.Workspace, EnginePaths.ExampleHousehold));
    var sweep = live.Run("sweep", "--live", "--headless");
    Check("the sweep succeeds", sweep.Exit == 0, sweep.Err);
    Check("it says it is searching the real sources", sweep.Out.Contains("Searching the real sources from this computer, one search at a time.", StringComparison.Ordinal));
    Equal("one browser was opened for the whole sweep", live.BrowsersOpened, 1);
    Check("it was asked to stay hidden only because --headless was typed", live.LastBrowserOptions is { Headless: true });
    Check("the browser was closed afterwards", live.Session!.Disposed);
    Check("the two searches were typed into the family-name field", live.Session.Calls.Count(c => c.StartsWith("fill:input[name=\"lastName\"]", StringComparison.Ordinal)) == 2);
    Check("with thirty seconds between them", live.Pacer.Delays.Any(d => d.TotalMilliseconds == 30000));
    Check("and both five-second pauses before each submit", live.Pacer.Delays.Count(d => d.TotalMilliseconds == 5000) == 4);
    Check("the same findings come back as from the recordings", sweep.Out.Contains("11 new, 0 seen again, 4 records belonging to other people discarded.", StringComparison.Ordinal));

    var visible = NewCli("visible");
    visible.Run("init");
    visible.Run("household", "import", Path.Combine(visible.Workspace, EnginePaths.ExampleHousehold));
    visible.Run("sweep", "--live");
    Check("without --headless the window is shown", visible.LastBrowserOptions is { Headless: false });
}

// ---------- reading what was found ----------
Section("Findings and the report");
{
    var findings = main.Run("findings");
    Check("findings lists all eleven", findings.Exit == 0 && findings.Out.Split('\n').Count(l => l.Contains("f_", StringComparison.Ordinal)) == 11);
    Check("the possible match is last", findings.Out.TrimEnd().Split('\n')[^1].Contains("possible match", StringComparison.Ordinal));
    Check("a web address in a source's text is broken before it is printed", !findings.Out.Contains("http://refunds.example", StringComparison.Ordinal) && findings.Out.Contains("hxxp[:]//refunds[.]example", StringComparison.Ordinal));

    var report = main.Run("report");
    var path = Path.Combine(main.Workspace, EnginePaths.Reports, "claimseeker-report-2026-11-15.md");
    Check("report writes a dated file", report.Exit == 0 && File.Exists(path), report.Err);
    Check("and prints the headline", report.Out.Contains("At least $1,889.90", StringComparison.Ordinal));
    var printed = main.Run("report", "--print");
    Check("--print writes the report to the screen instead", printed.Out.Contains("# ClaimSeeker report, 2026-11-15", StringComparison.Ordinal) && printed.Out.Contains("## Other places to look", StringComparison.Ordinal));
    var custom = temp.File("out", "my-report.md");
    Check("--out writes where it is told", main.Run("report", "--out", custom).Exit == 0 && File.Exists(custom));
}

// ---------- acting on a finding ----------
Section("Packets, letters and marks from the command line");
{
    string Id(string holder) { using var s = Open(main.Workspace); return s.ListFindings().First(f => f.HolderName == holder).Id; }
    string State(string id) { using var s = Open(main.Workspace); return s.GetFinding(id)!.CaseState; }
    var own = Id("PRAIRIE STATE ELECTRIC COOP");
    var dad = Id("DAYTON TOOL AND DIE CREDIT UNION");
    var wages = Id("GREAT LAKES PAYROLL SERVICES");
    string hostile;
    using (var s = Open(main.Workspace)) hostile = s.ListFindings().First(f => f.HolderName!.Contains("refunds", StringComparison.Ordinal)).Id;

    Check("packet refreshes one packet", main.Run("packet", own) is { Exit: 0 } packet && packet.Out.Contains(own + ".md", StringComparison.Ordinal));
    Check("packet with no finding named is a usage error", main.Run("packet") is { Exit: 2 });
    Check("packet for an unknown finding is a usage error", main.Run("packet", "f_nope") is { Exit: 2 });
    Check("packets has nothing left to write", main.Run("packets").Out.Contains("No new findings need a packet.", StringComparison.Ordinal));

    var draft = main.Run("draft", own);
    Check("a letter is drafted", draft.Exit == 0 && draft.Out.Contains("Draft written to", StringComparison.Ordinal), draft.Err + draft.Out);
    Check("the letter is shown for checking", draft.Out.Contains("Subject: Inquiry about unclaimed property reported for SAMPLE JANE Q", StringComparison.Ordinal) && draft.Out.Contains("My name is Jane Quinn Sample.", StringComparison.Ordinal));
    Check("it says nothing has been sent", draft.Out.Contains("Nothing has been sent.", StringComparison.Ordinal));
    Check("the draft file is in the workspace", Directory.GetFiles(Path.Combine(main.Workspace, EnginePaths.Drafts), own + "-holder-inquiry-*.eml").Length == 1);
    Equal("the case is drafted", State(own), "DRAFTED");

    var heir = main.Run("draft", dad);
    Check("a parent who has died gets the heir's letter by default", heir.Exit == 0 && heir.Out.Contains("I am writing as next of kin.", StringComparison.Ordinal), heir.Err + heir.Out);

    var blocked = main.Run("draft", hostile);
    Check("a blocked letter exits non-zero and says why", blocked.Exit == 1 && blocked.Out.Contains("The Attestation Gate blocked it:", StringComparison.Ordinal) && blocked.Out.Contains("contains a link", StringComparison.Ordinal));
    Check("and leaves no file", Directory.GetFiles(Path.Combine(main.Workspace, EnginePaths.Drafts), hostile + "*").Length == 0);

    var early = main.Run("draft", wages, "--template", "claim-follow-up");
    Check("a follow-up before filing is refused with the next step", early.Exit == 2 && early.Err.Contains($"claimseeker mark {wages} filed", StringComparison.Ordinal));
    Equal("and the case has not moved", State(wages), "PACKET_READY");

    Check("mark filed", main.Run("mark", wages, "filed") is { Exit: 0 } marked && marked.Out.Contains("is now marked filed by you.", StringComparison.Ordinal));
    Check("a follow-up can now be drafted", main.Run("draft", wages) is { Exit: 0 } followUp && followUp.Out.Contains("I filed a claim", StringComparison.Ordinal));
    Check("mark paid", main.Run("mark", wages, "PAID") is { Exit: 0 });
    Check("a word that is not something a person reports is refused", main.Run("mark", own, "ranked") is { Exit: 2 });
    Check("a step the lifecycle does not allow is refused in plain words", main.Run("mark", own, "paid") is { Exit: 2 } notYet && notYet.Err.Contains("cannot be marked paid", StringComparison.Ordinal));
    Check("mark with nothing after it is a usage error", main.Run("mark", own) is { Exit: 2 });
    Check("the headline now counts the paid claim apart", main.Run("findings").Out.Contains("1 claim marked paid so far, worth at least $312.40.", StringComparison.Ordinal));
}

// ---------- the record ----------
Section("The record of what was done");
{
    var verify = main.Run("audit", "verify");
    Check("the record verifies", verify.Exit == 0 && verify.Out.Contains("The record is intact", StringComparison.Ordinal));
    var exported = temp.File("out", "audit.json");
    Check("the record can be exported", main.Run("audit", "export", "--out", exported).Exit == 0 && File.ReadAllText(exported).Contains("\"intact\": true", StringComparison.Ordinal));
    var export = File.ReadAllText(exported);
    Check("the export carries no name or address", !export.Contains("Sample", StringComparison.OrdinalIgnoreCase) && !export.Contains("Springfield", StringComparison.OrdinalIgnoreCase));
    Check("the export says who took each step", export.Contains("\"actor\": \"user\"", StringComparison.Ordinal) && export.Contains("\"actor\": \"engine\"", StringComparison.Ordinal));

    var doctor = main.Run("doctor");
    Check("doctor finds nothing wrong", doctor.Exit == 0 && doctor.Out.Contains("Everything checks out.", StringComparison.Ordinal), doctor.Out);

    using (var store = Open(main.Workspace)) store.TamperWithEventForTest(2, "{\"edited\":true}");
    var tampered = main.Run("audit", "verify");
    Check("an edit made in the database file is reported, with the entry", tampered.Exit == 1 && tampered.Err.Contains("altered at entry 2", StringComparison.Ordinal));
    Check("doctor reports it too", main.Run("doctor") is { Exit: 1 } sick && sick.Out.Contains("FAIL", StringComparison.Ordinal));
    Check("an export of an altered record says so and exits non-zero", main.Run("audit", "export", "--out", exported).Exit == 1 && File.ReadAllText(exported).Contains("\"intact\": false", StringComparison.Ordinal));
}

Section("Doctor notices what is missing");
{
    var fresh = NewCli("fresh");
    Check("no workspace", fresh.Run("doctor") is { Exit: 1 } none && none.Out.Contains("Run `claimseeker init`", StringComparison.Ordinal));
    fresh.Run("init");
    Check("no household yet", fresh.Run("doctor") is { Exit: 1 } empty && empty.Out.Contains("No household has been imported yet.", StringComparison.Ordinal));

    var brokenPlaybooks = temp.File("broken-playbooks");
    Directory.CreateDirectory(brokenPlaybooks);
    File.WriteAllText(Path.Combine(brokenPlaybooks, "missingmoney.md"), File.ReadAllText(Path.Combine(playbooks, "missingmoney.md")).Replace("\"op\": \"click\"", "\"op\": \"evaluate\""));
    var broken = new Cli(fresh.Workspace, brokenPlaybooks, FixedTime.At("2026-11-15T12:00:00Z"));
    Check("a playbook with a step outside the vocabulary", broken.Run("doctor") is { Exit: 1 } bad && bad.Out.Contains("A playbook could not be read.", StringComparison.Ordinal));
    Check("and a sweep refuses to run with it", broken.Run("sweep", "--fixtures", fixtures) is { Exit: 2 });
    var noPlaybooks = new Cli(fresh.Workspace, temp.File("no-playbooks"), FixedTime.At("2026-11-15T12:00:00Z"));
    Check("a missing playbooks folder", noPlaybooks.Run("doctor") is { Exit: 1 });
}

// ---------- one engine per workspace ----------
Section("One engine at a time");
{
    using (WorkspaceLock.Acquire(main.Workspace))
    {
        var second = main.Run("findings");
        Check("a second engine on the same workspace is refused", second.Exit == 2 && second.Err.Contains("Another ClaimSeeker process is using this workspace.", StringComparison.Ordinal));
        Check("deletion is refused too while an engine holds it", main.Run("delete-all-data", "--confirm", DataDeletion.ConfirmationPhrase(main.Workspace)) is { Exit: 2 } && Directory.Exists(main.Workspace));
    }

    Check("once the first lets go, the workspace opens again", main.Run("findings").Exit == 0);
}

// ---------- deletion ----------
Section("Everything can be deleted, and only on purpose");
{
    var phrase = DataDeletion.ConfirmationPhrase(main.Workspace);
    var first = main.Run("delete-all-data");
    Check("the first run only shows the folder and the phrase", first.Exit == 0 && first.Out.Contains(phrase, StringComparison.Ordinal) && first.Out.Contains("Nothing has been deleted.", StringComparison.Ordinal));
    Check("and deletes nothing", File.Exists(Path.Combine(main.Workspace, EnginePaths.Database)));
    Check("the wrong phrase deletes nothing", main.Run("delete-all-data", "--confirm", "delete everything") is { Exit: 2 } && Directory.Exists(main.Workspace));
    Check("the right phrase for a different folder deletes nothing", main.Run("delete-all-data", "--confirm", DataDeletion.ConfirmationPhrase(temp.File("live"))) is { Exit: 2 } && Directory.Exists(main.Workspace));

    var plain = temp.File("not-a-workspace");
    Directory.CreateDirectory(plain);
    File.WriteAllText(Path.Combine(plain, "keep.txt"), "mine");
    var notOurs = new Cli(plain, playbooks, FixedTime.At("2026-11-15T12:00:00Z"));
    Check("a folder that is not a workspace is refused", notOurs.Run("delete-all-data", "--confirm", DataDeletion.ConfirmationPhrase(plain)) is { Exit: 2 } && File.Exists(Path.Combine(plain, "keep.txt")));
    Check("a home folder is refused even if it were marked", DataDeletion.Refusal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) is not null);
    Check("a drive root is refused", DataDeletion.Refusal(Path.GetPathRoot(Path.GetFullPath(temp.Path))!) is not null);
    Check("a folder that does not exist is refused", DataDeletion.Refusal(temp.File("nowhere")) is not null);

    // A link inside the workspace points at someone else's data. Deleting the workspace must not follow it.
    var outside = temp.File("outside");
    Directory.CreateDirectory(outside);
    File.WriteAllText(Path.Combine(outside, "precious.txt"), "not ClaimSeeker's");
    var linkMade = true;
    try { Directory.CreateSymbolicLink(Path.Combine(main.Workspace, "linked"), outside); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { linkMade = false; }

    var deleted = main.Run("delete-all-data", "--confirm", phrase);
    Check("the right phrase deletes the workspace", deleted.Exit == 0 && deleted.Out.Contains("Deleted.", StringComparison.Ordinal), deleted.Err + deleted.Out);
    Check("nothing is left", !Directory.Exists(main.Workspace));
    Check("what a link inside the workspace pointed at is untouched", !linkMade || File.ReadAllText(Path.Combine(outside, "precious.txt")) == "not ClaimSeeker's");
    Check("deleting again says there is nothing there", main.Run("delete-all-data") is { Exit: 2 });
    Check("the engine works from a clean start afterwards", main.Run("init").Exit == 0 && main.Run("household", "show") is { Exit: 2 });
}

Section("Where the workspace lives");
{
    var perUser = EnginePaths.DefaultWorkspace();
    Check("the default workspace is in the person's own data folder", perUser.EndsWith(EnginePaths.WorkspaceFolderName, StringComparison.Ordinal)
        && perUser.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.Ordinal));
    Check("it is not inside the source checkout", !perUser.StartsWith(RepoRoot(), StringComparison.Ordinal));
    var flagged = new Cli(temp.File("default"), playbooks, FixedTime.At("2026-11-15T12:00:00Z"));
    Check("--workspace overrides the default", flagged.Run("init", "--workspace", temp.File("flagged")).Exit == 0 && Directory.Exists(temp.File("flagged")) && !Directory.Exists(temp.File("default")));
    Check("the example household in the engine is the one the harnesses use",
        HouseholdJson.Serialize(HouseholdJson.Parse(ExampleHousehold.For(new DateOnly(2026, 11, 1))) with { Attestation = null })
        == HouseholdJson.Serialize(SampleHousehold.Load() with { Attestation = null }));
}

return Finish();

// ---------- helpers ----------

static SqliteClaimStore Open(string workspace)
{
    var store = new SqliteClaimStore(Path.Combine(workspace, EnginePaths.Database));
    store.Initialize();
    return store;
}

static int Events(string workspace)
{
    using var store = Open(workspace);
    return store.GetEvents().Count;
}

static int Searches(string workspace)
{
    using var store = Open(workspace);
    return store.ListSearches().Count;
}

internal sealed record CliResult(int Exit, string Out, string Err);

/// <summary>The command line with its clock, output and browser replaced, pointed at one workspace.</summary>
internal sealed class Cli(string workspace, string playbookDirectory, FixedTime time)
{
    public string Workspace { get; } = workspace;
    public RecordingPacer Pacer { get; } = new();
    public int BrowsersOpened { get; private set; }
    public BrowserOptions? LastBrowserOptions { get; private set; }
    public FixtureSession? Session { get; private set; }

    public CliResult Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new EngineCli(new EngineEnvironment
        {
            Time = time,
            Out = output,
            Error = error,
            PlaybookDirectory = playbookDirectory,
            DefaultWorkspace = Workspace,
            Pacer = Pacer,
            OpenBrowser = options =>
            {
                BrowsersOpened++;
                LastBrowserOptions = options;
                Session = new FixtureSession(Harness.Fixtures("sources", "missingmoney"));
                return Task.FromResult<IBrowserSession>(Session);
            },
        });

        var exit = cli.RunAsync(args).GetAwaiter().GetResult();
        return new CliResult(exit, output.ToString(), error.ToString());
    }
}

/// <summary>Stands in for a browser: answers each search from the recording for the family name that was typed.</summary>
internal sealed class FixtureSession(string folder) : IBrowserSession
{
    private string _family = "";

    public List<string> Calls { get; } = [];
    public bool Disposed { get; private set; }
    public string CurrentUrl { get; private set; } = "about:blank";

    public void StartCapture(string name, string urlContains, int status, IReadOnlyList<string> allowedHosts) => Calls.Add($"capture:{name}");
    public void ResetCaptures() { }

    public Task GotoAsync(string url, WaitUntil waitUntil, int timeoutMs, CancellationToken ct)
    {
        Calls.Add($"goto:{url}");
        CurrentUrl = url;
        return Task.CompletedTask;
    }

    public Task FillAsync(string selector, string value, CancellationToken ct)
    {
        Calls.Add($"fill:{selector}={value}");
        if (selector.Contains("lastName", StringComparison.Ordinal)) _family = value;
        return Task.CompletedTask;
    }

    public Task SelectAsync(string selector, string value, CancellationToken ct) { Calls.Add($"select:{selector}={value}"); return Task.CompletedTask; }
    public Task ClickAsync(string selector, CancellationToken ct) { Calls.Add($"click:{selector}"); return Task.CompletedTask; }

    public Task<string?> AwaitCaptureAsync(string name, int timeoutMs, CancellationToken ct)
    {
        var path = Path.Combine(folder, _family + ".json");
        return Task.FromResult(File.Exists(path) ? File.ReadAllText(path) : null);
    }

    public Task<bool> IsAutomationDeclaredAsync(CancellationToken ct) => Task.FromResult(true);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingPacer : IPacer
{
    public List<TimeSpan> Delays { get; } = [];

    public Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}
