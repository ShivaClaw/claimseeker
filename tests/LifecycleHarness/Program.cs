// LifecycleHarness: the lifecycle graph and the services that walk it. The first half checks the
// graph as a graph: which states lead where, and who may take each step. The second half drives real
// findings through sweeps, packets, letters and the person's own reports, and checks the audit log
// tells the same story.
using ClaimSeeker.Core;
using ClaimSeeker.Dispatcher;
using ClaimSeeker.Pipeline;
using ClaimSeeker.Preparer;
using ClaimSeeker.Scout;
using ClaimSeeker.Store;
using ClaimSeeker.Verifier;
using static Harness;

Title("ClaimSeeker lifecycle");
var states = Enum.GetValues<CaseState>();

// ---------- the graph ----------
Section("The gate is the only way to a ready letter");
Equal("a letter becomes ready only from attested", string.Join(",", Lifecycle.Predecessors(CaseState.DRAFT_READY)), "ATTESTED");
Equal("attested is reached only from a letter put to the gate", string.Join(",", Lifecycle.Predecessors(CaseState.ATTESTED)), "DRAFT_PENDING");
Equal("a letter is drafted only from ready", string.Join(",", Lifecycle.Predecessors(CaseState.DRAFTED)), "DRAFT_READY");
Equal("a requested letter is either attested or blocked, nothing else", string.Join(",", Lifecycle.Next(CaseState.DRAFT_PENDING)), "ATTESTED,BLOCKED_ATTESTATION");
Check("with the gate's pass removed from the graph, no letter can ever be drafted", !Reachable(CaseState.DISCOVERED, CaseState.DRAFTED, without: CaseState.ATTESTED));
Check("with it in place, one can", Reachable(CaseState.DISCOVERED, CaseState.DRAFTED));
Check("a blocked letter cannot become ready without going back through the gate", !Lifecycle.Next(CaseState.BLOCKED_ATTESTATION).Contains(CaseState.DRAFT_READY)
    && !Lifecycle.Next(CaseState.BLOCKED_ATTESTATION).Contains(CaseState.DRAFTED) && !Lifecycle.Next(CaseState.BLOCKED_ATTESTATION).Contains(CaseState.ATTESTED));

Section("The engine cannot say a claim was filed");
foreach (var target in Lifecycle.UserOnly)
    Check($"no engine step leads into {target}", states.All(from => !Lifecycle.CanTransition(from, target, Actor.Engine)));
foreach (var target in Lifecycle.EngineOnly)
    Check($"no person's step leads into {target}", states.All(from => !Lifecycle.CanTransition(from, target, Actor.User)));
Check("the two sets do not overlap", !Lifecycle.UserOnly.Overlaps(Lifecycle.EngineOnly));
Check("with the person's filing removed from the graph, nothing is ever paid", !Reachable(CaseState.DISCOVERED, CaseState.PAID, without: CaseState.FILED_BY_USER));
Check("paid is reached only from states the person reported", Lifecycle.Predecessors(CaseState.PAID).All(Lifecycle.Filed.Contains));
Check("the person can file straight from a packet, with no letter at all", Lifecycle.CanTransition(CaseState.PACKET_READY, CaseState.FILED_BY_USER, Actor.User));

Section("The graph is whole");
Check("every state has an entry", states.All(s => Lifecycle.Next(s) is not null));
Check("closed cases go nowhere", Lifecycle.Terminal.All(s => Lifecycle.Next(s).Count == 0));
Check("every other state leads somewhere", states.Where(s => !Lifecycle.Terminal.Contains(s)).All(s => Lifecycle.Next(s).Count > 0));
Check("every state can be reached from discovered", states.All(s => s == CaseState.DISCOVERED || Reachable(CaseState.DISCOVERED, s)));
Check("every open state can still end", states.Where(s => !Lifecycle.Terminal.Contains(s)).All(s => Lifecycle.Terminal.Any(end => Reachable(s, end))));
Check("no state leads to itself", states.All(s => !Lifecycle.Next(s).Contains(s)));
Check("a step that is not an edge is refused for everyone", !Lifecycle.CanTransition(CaseState.DISCOVERED, CaseState.DRAFTED, Actor.Engine) && !Lifecycle.CanTransition(CaseState.RANKED, CaseState.PAID, Actor.User));
Check("the person can dismiss a finding at any point before filing",
    new[] { CaseState.DISCOVERED, CaseState.RANKED, CaseState.PACKET_READY, CaseState.BLOCKED_ATTESTATION, CaseState.DRAFTED }.All(s => Lifecycle.CanTransition(s, CaseState.DISMISSED, Actor.User)));
Check("a letter can be asked for by the person", Lifecycle.CanTransition(CaseState.PACKET_READY, CaseState.DRAFT_PENDING, Actor.User));

// ---------- real cases ----------
var household = SampleHousehold.Load();
var playbook = PlaybookParser.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "playbooks", "missingmoney.md")));
var source = SourceRegistry.Find(SourceRegistry.MissingMoney)!;
using var temp = NewTempFolder("lifecycle");

async Task<(InMemoryClaimStore Store, CaseService Cases, FixedTime Time)> SweptAsync()
{
    var time = FixedTime.At("2026-11-15T12:00:00Z");
    var store = new InMemoryClaimStore();
    store.SaveHousehold(HouseholdJson.Serialize(household), household.Subjects.Count, "user", Clock.Stamp(time));
    await new SweepService(store, time).SweepAsync(household, source, playbook, new FixtureQueryExecutor(Fixtures("sources")));
    return (store, new CaseService(store, time), time);
}

string IdOf(IClaimStore store, string holder) => store.ListFindings().First(f => f.HolderName == holder).Id;
string State(IClaimStore store, string id) => store.GetFinding(id)!.CaseState;
List<string> Changes(IClaimStore store, string id) => store.GetEvents()
    .Where(e => e.Kind == StoreVocabulary.StateChange && e.EntityId == id)
    .Select(e => $"{e.Actor}:{System.Text.Json.JsonDocument.Parse(e.PayloadJson).RootElement.GetProperty("to").GetString()}")
    .ToList();

Section("A sweep finds, keeps what is the household's, and ranks it");
{
    var (store, _, _) = await SweptAsync();
    var findings = store.ListFindings();
    Equal("eleven records belong to the household", findings.Count, 11);
    Check("every one has been ranked", findings.All(f => f.CaseState == "RANKED" && f.Score is not null));
    Check("ranking was the engine's step", findings.All(f => Changes(store, f.Id).SequenceEqual(["engine:RANKED"])));
    Check("the record of the person with no consent basis was not kept", !findings.Any(f => f.OwnerName.Contains("ALEX", StringComparison.Ordinal)));
    Check("strangers' records were not kept", !findings.Any(f => f.OwnerName.Contains("MARIA", StringComparison.Ordinal) || f.OwnerName.Contains("TRUST", StringComparison.Ordinal) || f.OwnerName.Contains("JOHN", StringComparison.Ordinal)));
    Check("two identical checks are two findings", findings.Count(f => f.HolderName == "HEARTLAND GROCERS") == 2);
    Check("each finding belongs to someone the engine may search for", findings.All(f => household.Find(f.SubjectId)!.MaySearch));
    Check("the log verifies after a sweep", store.VerifyAudit().Ok);
}

Section("A claim packet moves a ranked case on, and only the engine writes it");
{
    var (store, cases, _) = await SweptAsync();
    var id = IdOf(store, "CAPITAL CITY TITLE CO");
    var path = cases.PreparePacket(id, household, temp.File("packets-a"));
    Check("the packet is written", File.Exists(path));
    Equal("the case is packet ready", State(store, id), "PACKET_READY");
    Equal("the path is recorded", store.GetFinding(id)!.PacketPath, path);
    var packet = File.ReadAllText(path);
    Check("the packet says the claim is free and the person files it", packet.Contains("You file this claim yourself, and it is free.", StringComparison.Ordinal) && packet.Contains(ClaimPacketBuilder.FreeToFile, StringComparison.Ordinal));
    Check("it links only to official addresses", Links(packet).All(l => SourceRegistry.All.Any(s => s.IsOfficialHost(new Uri(l).Host))), string.Join(" ", Links(packet)));
    Check("it asks for name-change papers when the record is under a former name", packet.Contains("Proof of the name change", StringComparison.Ordinal));
    Check("it never asks the person to give ClaimSeeker an identifier", packet.Contains("ClaimSeeker never asks for it", StringComparison.Ordinal));
    Check("writing it again leaves the case where it is", cases.PreparePacket(id, household, temp.File("packets-a")) == path && State(store, id) == "PACKET_READY");
    Equal("every ranked finding gets a packet in one pass", cases.PreparePackets(household, temp.File("packets-a")), 10);
    Equal("and a second pass has nothing left to write", cases.PreparePackets(household, temp.File("packets-a")), 0);
    Check("a packet for a finding that does not exist is refused", Throws<CaseException>(() => cases.PreparePacket("f_none", household, temp.File("packets-a"))));
    {
        var unranked = store.ListFindings()[0];
        store.UpsertFinding(new FindingUpsert
        {
            Id = "f_unranked", SubjectId = unranked.SubjectId, SourceId = unranked.SourceId, DedupeKey = "d_unranked", OwnerName = unranked.OwnerName,
            Family = unranked.Family, Confidence = unranked.Confidence, NameMatch = unranked.NameMatch, Rationale = unranked.Rationale, QueryKey = unranked.QueryKey,
        }, "2026-11-15T12:00:01Z");
        Check("a finding that has not been ranked gets no packet", Throws<CaseException>(() => cases.PreparePacket("f_unranked", household, temp.File("packets-a")))
            && !File.Exists(temp.File("packets-a", "f_unranked.md")) && State(store, "f_unranked") == "DISCOVERED");
    }

    var dad = IdOf(store, "DAYTON TOOL AND DIE CREDIT UNION");
    Check("a deceased owner's packet asks for a death certificate and proof of heirship", File.ReadAllText(store.GetFinding(dad)!.PacketPath!).Contains("certified death certificate", StringComparison.Ordinal));
    var possible = IdOf(store, "SUNBELT SAVINGS BANK");
    Check("a possible match's packet calls itself a lead, not money owed", File.ReadAllText(store.GetFinding(possible)!.PacketPath!).Contains("Treat this as a lead to check, not as money owed.", StringComparison.Ordinal));
    var hostile = store.ListFindings().First(f => f.HolderName!.Contains("refunds.example", StringComparison.Ordinal));
    var hostilePacket = File.ReadAllText(hostile.PacketPath!);
    Check("a web address in a source's text cannot be opened from the packet",
        Links(hostilePacket).All(l => !l.Contains("refunds", StringComparison.Ordinal))
        && !hostilePacket.Contains("refunds.example", StringComparison.Ordinal)
        && hostilePacket.Contains("hxxp", StringComparison.Ordinal));
    Check("and the packet warns about it", hostilePacket.Contains("**Caution:**", StringComparison.Ordinal));
}

Section("A letter goes through the gate, and the log shows each step and who took it");
{
    var (store, cases, _) = await SweptAsync();
    cases.PreparePackets(household, temp.File("packets-b"));
    var outbox = new CountingOutbox(new FileDraftOutbox(temp.File("drafts-b")));
    var id = IdOf(store, "PRAIRIE STATE ELECTRIC COOP");

    var outcome = cases.Draft(id, null, household, outbox);
    Check("the letter passes", outcome.Passed);
    Check("the draft file exists", File.Exists(outcome.Path));
    Equal("the case is drafted", State(store, id), "DRAFTED");
    Equal("the steps, in order, with who took them",
        string.Join(" ", Changes(store, id)), "engine:RANKED engine:PACKET_READY user:DRAFT_PENDING engine:ATTESTED engine:DRAFT_READY engine:DRAFTED");
    Equal("the outbox was used once", outbox.Calls, 1);
    Equal("the draft is recorded against the finding", store.ListDrafts(id).Count, 1);
    Check("the gate's decision is in the log", store.GetEvents().Any(e => e is { Kind: "gate_decision", EntityId: var entity } && entity == id && e.PayloadJson.Contains("\"passed\":true", StringComparison.Ordinal)));
    Check("the sender is the account holder", outcome is { SenderIsAccountHolder: true, SenderName: "Jane Quinn Sample" });

    var second = cases.Draft(id, null, household, outbox);
    Check("asking again goes through the gate again", second.Passed && State(store, id) == "DRAFTED");
    Check("and the same letter is still one file", second.Path == outcome.Path && Directory.GetFiles(temp.File("drafts-b")).Length == 1);
}

Section("A blocked letter writes nothing and says why");
{
    var (store, cases, _) = await SweptAsync();
    cases.PreparePackets(household, temp.File("packets-c"));
    var outbox = new CountingOutbox(new FileDraftOutbox(temp.File("drafts-c")));
    var hostile = store.ListFindings().First(f => f.HolderName!.Contains("refunds.example", StringComparison.Ordinal)).Id;

    var outcome = cases.Draft(hostile, null, household, outbox);
    Check("the letter is blocked", !outcome.Passed && outcome.Path is null && outcome.Draft is null);
    Equal("the case is marked blocked", State(store, hostile), "BLOCKED_ATTESTATION");
    Equal("the outbox was never called", outbox.Calls, 0);
    Check("no draft file exists", !Directory.Exists(temp.File("drafts-c")) || Directory.GetFiles(temp.File("drafts-c")).Length == 0);
    Check("no draft is recorded", store.ListDrafts(hostile).Count == 0);
    Check("the reasons are returned", outcome.Violations.Any(v => v.Code == "untrusted-content"));
    Check("the log records the block by code, not by content", store.GetEvents().Any(e => e.Kind == "gate_decision" && e.PayloadJson.Contains("untrusted-content", StringComparison.Ordinal))
        && !store.GetEvents().Any(e => e.PayloadJson.Contains("refunds.example", StringComparison.Ordinal)));
    Check("the case never passed through attested", !Changes(store, hostile).Contains("engine:ATTESTED"));
    Check("the person can still file it themselves", cases.Mark(hostile, CaseState.FILED_BY_USER).CaseState == "FILED_BY_USER");
}

Section("A letter the case is not ready for is refused before the case is touched");
{
    var (store, cases, _) = await SweptAsync();
    var outbox = new CountingOutbox(new FileDraftOutbox(temp.File("drafts-d")));
    var id = IdOf(store, "GREAT LAKES PAYROLL SERVICES");
    Check("no letter before there is a packet", Throws<CaseException>(() => cases.Draft(id, null, household, outbox)) && State(store, id) == "RANKED");
    cases.PreparePacket(id, household, temp.File("packets-d"));
    Check("no follow-up on a claim that has not been filed", Throws<CaseException>(() => cases.Draft(id, LetterTemplates.ClaimFollowUp, household, outbox)));
    Equal("and the case is exactly where it was", State(store, id), "PACKET_READY");
    Check("an unknown letter is refused", Throws<CaseException>(() => cases.Draft(id, "demand-letter", household, outbox)));
    Check("an heir's letter for a living person is refused", Throws<CaseException>(() => cases.Draft(id, LetterTemplates.HeirInquiry, household, outbox)));
    Equal("none of that reached the outbox", outbox.Calls, 0);
}

Section("What happens next is the person's to report");
{
    var (store, cases, _) = await SweptAsync();
    cases.PreparePackets(household, temp.File("packets-e"));
    var outbox = new CountingOutbox(new FileDraftOutbox(temp.File("drafts-e")));
    var id = IdOf(store, "GREAT LAKES PAYROLL SERVICES");

    Check("a case cannot be marked paid before it is filed", Throws<CaseException>(() => cases.Mark(id, CaseState.PAID)));
    Check("the person cannot mark a state that is the engine's", Throws<CaseException>(() => cases.Mark(id, CaseState.DRAFTED)));
    Equal("filed", cases.Mark(id, CaseState.FILED_BY_USER).CaseState, "FILED_BY_USER");

    var followUp = cases.Draft(id, null, household, outbox);
    Check("after filing, the letter offered is the follow-up", followUp.Passed && followUp.Draft!.TemplateId == LetterTemplates.ClaimFollowUp);
    Equal("a follow-up does not move the case", State(store, id), "FILED_BY_USER");
    Equal("in review", cases.Mark(id, CaseState.IN_REVIEW).CaseState, "IN_REVIEW");
    Equal("more documents asked for", cases.Mark(id, CaseState.NEEDS_DOCUMENTS).CaseState, "NEEDS_DOCUMENTS");
    Equal("filed again", cases.Mark(id, CaseState.FILED_BY_USER).CaseState, "FILED_BY_USER");
    Equal("paid", cases.Mark(id, CaseState.PAID).CaseState, "PAID");
    Check("every one of those steps is logged as the person's", Changes(store, id).Skip(2).All(c => c.StartsWith("user:", StringComparison.Ordinal)));
    Check("a paid case is closed to further marks", Throws<CaseException>(() => cases.Mark(id, CaseState.DENIED)));
    var lettersBefore = outbox.Calls;
    Check("and to letters", Throws<CaseException>(() => cases.Draft(id, null, household, outbox)));
    Equal("and the refusal comes before anything is written", outbox.Calls, lettersBefore);
    Check("and to packets", Throws<CaseException>(() => cases.PreparePacket(id, household, temp.File("packets-e"))));

    var other = IdOf(store, "MIDWEST CABLE");
    Equal("not mine", cases.Mark(other, CaseState.DISMISSED).CaseState, "DISMISSED");
    Check("the log verifies after all of it", store.VerifyAudit().Ok);
}

Section("An interrupted letter picks up where it stopped, and the gate is asked again");
{
    var (store, cases, _) = await SweptAsync();
    cases.PreparePackets(household, temp.File("packets-f"));
    var id = IdOf(store, "BUCKEYE MUTUAL LIFE");
    var failing = new CountingOutbox(new FileDraftOutbox(temp.File("drafts-f"))) { FailNext = true };

    Check("the outbox fails once", Throws<IOException>(() => cases.Draft(id, null, household, failing)));
    Equal("the case is left ready, not drafted", State(store, id), "DRAFT_READY");
    Check("no draft is recorded for a letter that was not written", store.ListDrafts(id).Count == 0);

    var resumed = cases.Draft(id, null, household, failing);
    Check("the retry completes", resumed.Passed && State(store, id) == "DRAFTED");
    Equal("the gate was asked both times", store.GetEvents().Count(e => e.Kind == "gate_decision" && e.EntityId == id), 2);
    Equal("one letter exists", Directory.GetFiles(temp.File("drafts-f")).Length, 1);

    // The same interruption, but the facts change before the retry: the gate must block what it passed before.
    var other = IdOf(store, "HEARTLAND GROCERS");
    failing.FailNext = true;
    Throws<IOException>(() => cases.Draft(other, null, household, failing));
    Equal("a second case is left ready", State(store, other), "DRAFT_READY");
    var unattested = household with { Attestation = null };
    var blocked = cases.Draft(other, null, unattested, failing);
    Check("with the attestation withdrawn, the resumed letter is blocked", !blocked.Passed && State(store, other) == "BLOCKED_ATTESTATION");
    Equal("and no second letter was written", Directory.GetFiles(temp.File("drafts-f")).Length, 1);
}

Section("A source that says no is left alone");
{
    var time = FixedTime.At("2026-11-15T12:00:00Z");
    var store = new InMemoryClaimStore();
    var sweeps = new SweepService(store, time);
    var blockedFolder = temp.File("blocked");
    Directory.CreateDirectory(Path.Combine(blockedFolder, "missingmoney"));
    File.WriteAllText(Path.Combine(blockedFolder, "missingmoney", "SAMPLE.blocked"), "");
    File.WriteAllText(Path.Combine(blockedFolder, "missingmoney", "EXAMPLE.blocked"), "");
    var counting = new CountingExecutor(new FixtureQueryExecutor(blockedFolder));

    var first = await sweeps.SweepAsync(household, source, playbook, counting);
    Check("the first refused sweep hands every search to the person", first.Queries.All(q => q.Outcome == SearchOutcomes.Fallback));
    Equal("the streak is one", store.GetConfig(SweepService.BlockedStreakKey(source.Id)), "1");
    await sweeps.SweepAsync(household, source, playbook, counting);
    Equal("after a second, the streak is two", store.GetConfig(SweepService.BlockedStreakKey(source.Id)), "2");
    var sent = counting.Calls;
    var third = await sweeps.SweepAsync(household, source, playbook, counting);
    Equal("the third sweep does not contact the source at all", counting.Calls, sent);
    Check("and says why", third.Queries.All(q => q is { Outcome: SearchOutcomes.Fallback, Reason: SweepService.BlockedReason }));
    Check("no finding was invented from a refusal", store.ListFindings().Count == 0);

    var retried = await sweeps.SweepAsync(household, source, playbook, new CountingExecutor(new FixtureQueryExecutor(Fixtures("sources"))), new SweepOptions { RetryBlocked = true });
    Check("when the person asks for another try, the searches are sent", retried.Queries.All(q => q.Outcome == SearchOutcomes.Captured) && retried.NewFindings == 11);
    Equal("and an answer clears the streak", store.GetConfig(SweepService.BlockedStreakKey(source.Id)), "0");
}

Section("A source that changes shape is a broken playbook, not an empty result");
{
    var time = FixedTime.At("2026-11-15T12:00:00Z");
    var store = new InMemoryClaimStore();
    var changedFolder = temp.File("changed");
    Directory.CreateDirectory(Path.Combine(changedFolder, "missingmoney"));
    File.WriteAllText(Path.Combine(changedFolder, "missingmoney", "SAMPLE.json"), """{"results":{"items":[]}}""");
    File.WriteAllText(Path.Combine(changedFolder, "missingmoney", "EXAMPLE.json"), """{"properties":[]}""");
    var report = await new SweepService(store, time).SweepAsync(household, source, playbook, new FixtureQueryExecutor(changedFolder));
    Equal("the changed answer is recorded as a changed shape", report.Queries[0].Outcome, SearchOutcomes.ShapeChanged);
    Equal("a genuinely empty answer is an ordinary empty result", report.Queries[1] is { Outcome: SearchOutcomes.Captured, ItemsSeen: 0 }, true);
    var input = new ReportService(store, time).BuildInput(household, [], new Dictionary<string, Playbook> { [source.Id] = playbook });
    Check("the changed search is handed to the person in the report", input.Fallbacks is [{ Searched: "the family name SAMPLE" }]);
    Check("with the official link", input.Fallbacks[0].Link == playbook.Fallback.DeepLink);
}

Section("Searches run on a cadence, and a record that leaves a list is noticed");
{
    var time = FixedTime.At("2026-11-15T12:00:00Z");
    var store = new InMemoryClaimStore();
    var sweeps = new SweepService(store, time);
    var counting = new CountingExecutor(new FixtureQueryExecutor(Fixtures("sources")));
    await sweeps.SweepAsync(household, source, playbook, counting);
    Equal("the first sweep runs both searches", counting.Calls, 2);

    time.Advance(TimeSpan.FromDays(29));
    var early = await sweeps.SweepAsync(household, source, playbook, counting);
    Check("twenty-nine days later nothing is due", early.Queries.All(q => q.Outcome == SearchOutcomes.NotDue) && counting.Calls == 2);
    var forced = await sweeps.SweepAsync(household, source, playbook, counting, new SweepOptions { OnlyDue = false });
    Check("a forced sweep runs anyway, and finds nothing new", counting.Calls == 4 && forced is { NewFindings: 0, RefreshedFindings: 11 });
    Check("a refresh never moves a case", store.ListFindings().All(f => f.CaseState == "RANKED"));

    time.Advance(TimeSpan.FromDays(30));
    var shrunk = temp.File("shrunk");
    Directory.CreateDirectory(Path.Combine(shrunk, "missingmoney"));
    File.WriteAllText(Path.Combine(shrunk, "missingmoney", "SAMPLE.json"), File.ReadAllText(Fixtures("sources", "missingmoney", "SAMPLE.json")).Replace("GREAT LAKES PAYROLL SERVICES", "SOMEONE ELSES PAYROLL").Replace("\"SAMPLE JANE\", \"holderName\": \"SOMEONE ELSES PAYROLL\"", "\"DOE PAT\", \"holderName\": \"SOMEONE ELSES PAYROLL\""));
    File.Copy(Fixtures("sources", "missingmoney", "EXAMPLE.json"), Path.Combine(shrunk, "missingmoney", "EXAMPLE.json"));
    var later = await sweeps.SweepAsync(household, source, playbook, new FixtureQueryExecutor(shrunk));
    Check("thirty days after that, the searches are due again", later.Queries.All(q => q.Outcome == SearchOutcomes.Captured));
    var gone = store.ListFindings().Single(f => f.HolderName == "GREAT LAKES PAYROLL SERVICES");
    Equal("the record that left the list is marked, with the date", gone.MissingSince, "2027-01-13T12:00:00Z");
    Check("the others are not", store.ListFindings().Count(f => f.MissingSince is not null) == 1);
}

Section("The report's total is only what sources stated");
{
    var (store, cases, time) = await SweptAsync();
    var ranked = new SweepService(store, time).RankedFindings();
    var totals = FindingsReport.Totals(ranked);
    Equal("ten matches are trusted", totals.Trusted, 10);
    Equal("their stated amounts add up to the cent", totals.StatedFloor, 1889.90m);
    Equal("one states no amount", totals.Undisclosed, 1);
    Equal("one is only possible, and is not counted", totals.Possible, 1);
    Equal("the headline says exactly that", FindingsReport.Headline(totals),
        "**At least $1,889.90 across 10 matches the engine trusts.** 1 of them does not state an amount, so it adds nothing to that figure. 1 possible match is listed further down and not counted.");
    Check("the possible match is ranked last", ranked[^1].Confidence == "Possible");
    Check("the largest stated amount for the least work is first", ranked[0].HolderName == "CAPITAL CITY TITLE CO");
    Check("an 'over $100' possible match adds nothing even though its floor is $100", totals.StatedFloor == ranked.Where(FindingsReport.IsTrusted).Sum(f => MoneyRange.FromBounds(f.ValueLow, f.ValueHigh).Floor));

    var input = new ReportService(store, time).BuildInput(household, ranked, new Dictionary<string, Playbook> { [source.Id] = playbook });
    var report = FindingsReport.Markdown(input);
    Check("the report opens with the headline", report.Contains(FindingsReport.Headline(totals), StringComparison.Ordinal));
    Check("it lists the possible match apart", report.Contains("## Possible matches", StringComparison.Ordinal) && report.IndexOf("SUNBELT SAVINGS BANK", StringComparison.Ordinal) > report.IndexOf("## Possible matches", StringComparison.Ordinal));
    Check("it groups by state and by person", report.Contains("- IL: at least", StringComparison.Ordinal) && report.Contains("- Robert Sample (parent, deceased): at least $100 (2 matches)", StringComparison.Ordinal));
    Check("it lists the sources it does not search yet", report.Contains("## Other places to look", StringComparison.Ordinal) && report.Contains("Workers Owed Wages", StringComparison.Ordinal));
    Check("every link in it is an official address", Links(report).All(l => SourceRegistry.All.Any(s => s.IsOfficialHost(new Uri(l).Host))), string.Join(" ", Links(report)));
    Check("a web address from a source's text cannot be opened from it", !report.Contains("http://refunds.example", StringComparison.Ordinal));
    Check("it ends by saying filing is free", report.Contains(ClaimPacketBuilder.FreeToFile, StringComparison.Ordinal));
    Check("it never uses the word confirmed", !report.Contains("confirmed", StringComparison.OrdinalIgnoreCase));

    var paid = IdOf(store, "PRAIRIE STATE ELECTRIC COOP");
    cases.PreparePacket(paid, household, temp.File("packets-g"));
    cases.Mark(paid, CaseState.FILED_BY_USER);
    cases.Mark(paid, CaseState.PAID);
    var after = FindingsReport.Totals(new SweepService(store, time).RankedFindings());
    Check("a paid claim leaves the open total and is counted as paid", after is { Trusted: 9, StatedFloor: 1839.90m, Paid: 1, PaidFloor: 50m });
    Check("an empty store has an honest headline too", FindingsReport.Headline(FindingsReport.Totals([])) == "**No matches the engine trusts yet.**");
}

return Finish();

// ---------- helpers ----------

static bool Reachable(CaseState from, CaseState to, CaseState? without = null)
{
    var seen = new HashSet<CaseState> { from };
    var queue = new Queue<CaseState>([from]);
    while (queue.Count > 0)
    {
        foreach (var next in Lifecycle.Next(queue.Dequeue()))
        {
            if (next == without || !seen.Add(next)) continue;
            if (next == to) return true;
            queue.Enqueue(next);
        }
    }

    return false;
}

static List<string> Links(string text)
{
    var links = new List<string>();
    var index = 0;
    while ((index = text.IndexOf("http", index, StringComparison.Ordinal)) >= 0)
    {
        var end = index;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        links.Add(text[index..end].TrimEnd('.', ',', ')', ';'));
        index = end;
    }

    return links;
}

internal sealed class CountingOutbox(IDraftOutbox inner) : IDraftOutbox
{
    public int Calls { get; private set; }
    public bool FailNext { get; set; }

    public DraftReceipt CreateDraft(GatePassedDraft draft)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new IOException("the disk is full");
        }

        Calls++;
        return inner.CreateDraft(draft);
    }
}

internal sealed class CountingExecutor(IQueryExecutor inner) : IQueryExecutor
{
    public int Calls { get; private set; }

    public Task<RunOutcome> ExecuteAsync(Playbook playbook, SourceQuery query, CancellationToken ct = default)
    {
        Calls++;
        return inner.ExecuteAsync(playbook, query, ct);
    }
}
