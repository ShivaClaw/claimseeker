// StoreParityHarness: the in-memory store and the SQLite store are put through the same script and
// must say the same thing at every step. Then the properties that matter are checked on each: a
// refresh never moves a case, a state change is all-or-nothing, the audit log notices tampering,
// and the SQLite file is exactly what a reopened engine picks up.
using System.Text.Json;
using ClaimSeeker.Core;
using ClaimSeeker.Store;
using Microsoft.Data.Sqlite;
using static Harness;

Title("ClaimSeeker store parity and audit");
using var temp = NewTempFolder("store");

FindingUpsert Finding(string id, string source = "missingmoney", string dedupe = "d_1", string confidence = "Strong",
    decimal? low = 100m, decimal? high = null, string owner = "SAMPLE JANE", string query = "missingmoney|SAMPLE||") => new()
{
    Id = id,
    SubjectId = "self",
    SourceId = source,
    DedupeKey = dedupe,
    OwnerName = owner,
    HolderName = "A HOLDER",
    City = "SPRINGFIELD",
    UsState = "IL",
    ValueText = "OVER $100",
    ValueLow = low,
    ValueHigh = high,
    PropertyTypeText = "SAVINGS ACCOUNT",
    Family = nameof(PropertyFamily.AccountBalance),
    Confidence = confidence,
    NameMatch = "ExactCurrent",
    Rationale = "The given and family names match.",
    QueryKey = query,
};

SearchInput Search(string outcome, string ranAt, string key = "missingmoney|SAMPLE||") => new()
{
    SourceId = "missingmoney",
    QueryKey = key,
    FamilyName = "SAMPLE",
    Kind = "FamilyOnly",
    Mode = "Auto",
    Outcome = outcome,
    ItemsSeen = 13,
    Matched = 10,
    RanAt = ranAt,
};

// One script, run against each store. Every observable result is written down.
List<string> Script(IClaimStore store)
{
    var log = new List<string>();
    void Note(string what, object? value) => log.Add($"{what}: {JsonSerializer.Serialize(value)}");

    store.Initialize();
    Note("household before", store.GetHouseholdJson());
    store.SaveHousehold("{\"version\":1}", 3, "user", "2026-11-15T10:00:00Z");
    Note("household after", store.GetHouseholdJson());

    Note("insert a", store.UpsertFinding(Finding("f_a"), "2026-11-15T10:01:00Z"));
    Note("insert b", store.UpsertFinding(Finding("f_b", dedupe: "d_2", low: 12.345m, high: 12.345m), "2026-11-15T10:01:01Z"));
    Note("same property through another source", store.UpsertFinding(Finding("f_c", source: "il-treasurer"), "2026-11-15T10:01:02Z"));
    Note("a third sighting points at the first", store.UpsertFinding(Finding("f_d", source: "third-source"), "2026-11-15T10:01:03Z"));
    Note("refresh a", store.UpsertFinding(Finding("f_a", confidence: "Likely", owner: "SAMPLE JANE Q"), "2026-11-16T09:00:00Z"));

    Note("wrong expected state", store.TryTransition("f_a", "RANKED", "PACKET_READY", "engine", "{}", "2026-11-16T09:01:00Z"));
    Note("unknown finding", store.TryTransition("f_zzz", "DISCOVERED", "RANKED", "engine", "{}", "2026-11-16T09:01:01Z"));
    Note("discovered to ranked", store.TryTransition("f_a", "DISCOVERED", "RANKED", "engine", "{}", "2026-11-16T09:01:02Z"));
    Note("ranked to packet ready", store.TryTransition("f_a", "RANKED", "PACKET_READY", "engine", "{\"why\":\"test\"}", "2026-11-16T09:01:03Z"));
    Note("same transition again", store.TryTransition("f_a", "RANKED", "PACKET_READY", "engine", "{}", "2026-11-16T09:01:04Z"));
    Note("refresh a after its case has moved", store.UpsertFinding(Finding("f_a", confidence: "Likely", owner: "SAMPLE JANE Q"), "2026-11-16T09:01:05Z"));

    store.SaveRank("f_a", 200m, 0.5m, 400m, true, "[\"Open-ended amount.\"]", "2026-11-16T09:02:00Z");
    store.SetPacketPath("f_a", "packets/f_a.md", "2026-11-16T09:02:01Z");
    store.SaveRank("f_missing", 1m, 1m, 1m, false, "[]", "2026-11-16T09:02:02Z");

    Note("mark missing", store.MarkMissing("missingmoney", "missingmoney|SAMPLE||", ["f_a"], "2026-12-16T09:00:00Z"));
    Note("mark missing again", store.MarkMissing("missingmoney", "missingmoney|SAMPLE||", ["f_a"], "2026-12-17T09:00:00Z"));
    Note("seen again clears it", store.UpsertFinding(Finding("f_b", dedupe: "d_2", low: 12.345m, high: 12.345m), "2026-12-18T09:00:00Z"));

    Note("search 1", store.RecordSearch(Search("fallback", "2026-11-15T10:00:30Z")));
    Note("search 2", store.RecordSearch(Search("captured", "2026-11-15T10:01:00Z")));
    Note("search 3", store.RecordSearch(Search("captured", "2026-11-15T10:01:30Z", key: "missingmoney|EXAMPLE||")));
    Note("last search", store.GetLastSearch("missingmoney", "missingmoney|SAMPLE||"));
    Note("last search, never run", store.GetLastSearch("missingmoney", "missingmoney|NOBODY||"));
    Note("searches", store.ListSearches());

    Note("draft 1", store.RecordDraft("f_a", "holder-inquiry", "drafts/a.eml", "abc", "2026-11-16T09:03:00Z"));
    Note("draft 2", store.RecordDraft("f_a", "claim-follow-up", "drafts/b.eml", "def", "2026-11-16T09:04:00Z"));
    Note("drafts for a", store.ListDrafts("f_a"));
    Note("drafts for b", store.ListDrafts("f_b"));

    Note("config before", store.GetConfig("source.missingmoney.blocked_streak"));
    store.SetConfig("source.missingmoney.blocked_streak", "1");
    store.SetConfig("source.missingmoney.blocked_streak", "2");
    Note("config after", store.GetConfig("source.missingmoney.blocked_streak"));

    Note("event", store.AppendEvent(new EventInput("engine", "sweep_completed", "search", "missingmoney", "{\"new\":2}"), "2026-11-16T09:05:00Z"));
    Note("finding a", store.GetFinding("f_a"));
    Note("finding b", store.GetFinding("f_b"));
    Note("findings", store.ListFindings());
    Note("events", store.GetEvents());
    Note("audit", store.VerifyAudit());
    return log;
}

var memory = new InMemoryClaimStore();
var databasePath = temp.File("parity.db");
var sqlite = new SqliteClaimStore(databasePath);

Section("The two stores agree, step by step");
var memoryLog = Script(memory);
var sqliteLog = Script(sqlite);
Equal("the same number of steps were observed", sqliteLog.Count, memoryLog.Count);
for (var i = 0; i < Math.Min(memoryLog.Count, sqliteLog.Count); i++)
{
    var step = memoryLog[i][..memoryLog[i].IndexOf(':')];
    Check($"same result: {step}", memoryLog[i] == sqliteLog[i], $"memory {memoryLog[i]} / sqlite {sqliteLog[i]}");
}

// The properties, checked on each store in turn.
foreach (var (name, store) in new (string, IClaimStore)[] { ("in memory", memory), ("sqlite", sqlite) })
{
    Section($"Findings ({name})");
    var a = store.GetFinding("f_a")!;
    var b = store.GetFinding("f_b")!;
    Equal("a refresh after the case has moved leaves it where it was", a.CaseState, "PACKET_READY");
    Equal("a refresh updates what the source said", a.OwnerName, "SAMPLE JANE Q");
    Equal("and how well it matches", a.Confidence, "Likely");
    Equal("but not when it was first seen", a.FirstSeen, "2026-11-15T10:01:00Z");
    Equal("the last sighting is recorded", a.LastSeen, "2026-11-16T09:01:05Z");
    Equal("money is kept to the cent", b.ValueLow, 12.35m);
    Equal("an open-ended amount has no ceiling", a.ValueHigh, null);
    Equal("the same property through another source points at the first sighting", store.GetFinding("f_c")!.DuplicateOf, "f_a");
    Equal("a third sighting points at the first too, not at the second", store.GetFinding("f_d")!.DuplicateOf, "f_a");
    Equal("the first sighting is nobody's duplicate", a.DuplicateOf, null);
    Equal("the rank is stored exactly", (a.ExpectedValue, a.EffortHours, a.Score, a.Urgent), (200m, 0.5m, 400m, true));
    Equal("the packet path is stored", a.PacketPath, "packets/f_a.md");
    Equal("findings are listed in the order they were found", string.Join(",", store.ListFindings().Select(f => f.Id)), "f_a,f_b,f_c,f_d");
    Check("a record that is seen again is no longer marked missing", b.MissingSince is null && b.LastSeen == "2026-12-18T09:00:00Z");
    Check("an unknown finding is null", store.GetFinding("f_none") is null);
    Check("a state that is not a case state is refused", Throws<ArgumentException>(() => store.TryTransition("f_a", "PACKET_READY", "SENT", "engine", "{}", "t")));
    Check("a confidence that is not a level is refused", Throws<ArgumentException>(() => store.UpsertFinding(Finding("f_x", confidence: "Confirmed"), "t")));
    Check("transition detail that is not JSON is refused", Throws<ArgumentException>(() => store.TryTransition("f_a", "PACKET_READY", "DRAFT_PENDING", "user", "not json", "t")));

    Section($"Audit ({name})");
    var events = store.GetEvents();
    Check("the log verifies", store.VerifyAudit().Ok);
    Equal("the first entry links to the start", events[0].PrevHash, AuditChain.Genesis);
    Check("every entry links to the one before", events.Skip(1).Select((e, i) => e.PrevHash == events[i].Hash).All(ok => ok));
    Check("entries are numbered from one without gaps", events.Select(e => e.Seq).SequenceEqual(Enumerable.Range(1, events.Count).Select(i => (long)i)));
    Equal("each new finding is logged once", events.Count(e => e.Kind == StoreVocabulary.FindingDiscovered), 4);
    Equal("a refresh is not logged as a discovery", events.Count(e => e.Kind == StoreVocabulary.FindingDiscovered && e.EntityId == "f_a"), 1);
    Equal("each real state change is logged once", events.Count(e => e.Kind == StoreVocabulary.StateChange), 2);
    Check("a failed state change writes nothing", !events.Any(e => e.PayloadJson.Contains("f_zzz", StringComparison.Ordinal)));
    Check("a state change records where from and where to", events.Any(e => e.PayloadJson == """{"from":"RANKED","to":"PACKET_READY","detail":{"why":"test"}}"""));
    Check("saving the household is logged, by the person", events[0] is { Kind: StoreVocabulary.HouseholdSaved, Actor: "user" });
    Check("no entry carries a name", !events.Any(e => e.PayloadJson.Contains("SAMPLE", StringComparison.OrdinalIgnoreCase) || e.PayloadJson.Contains("JANE", StringComparison.OrdinalIgnoreCase)));
}

Section("The audit log notices tampering");
{
    var rows = memory.GetEvents();
    Check("an edited payload is found at its entry", AuditChain.Verify(rows.Select(r => r.Seq == 3 ? r with { PayloadJson = "{\"source\":\"x\"}" } : r).ToList()) is { Ok: false, FirstBrokenSeq: 3 });
    Check("an edited actor is found", AuditChain.Verify(rows.Select(r => r.Seq == 1 ? r with { Actor = "engine" } : r).ToList()) is { Ok: false, FirstBrokenSeq: 1 });
    Check("a removed entry is found where the gap is", AuditChain.Verify(rows.Where(r => r.Seq != 4).ToList()) is { Ok: false, FirstBrokenSeq: 5 });
    Check("swapped entries are found", AuditChain.Verify([rows[1], rows[0], .. rows.Skip(2)]) is { Ok: false });
    Check("a log cut short still verifies up to the cut, so the count matters", AuditChain.Verify(rows.Take(3).ToList()).Ok);
    Check("an entry rewritten with a fresh hash breaks the next link",
        AuditChain.Verify(rows.Select(r => r.Seq == 2 ? AuditChain.Link(2, r.At, r.PrevHash, new EventInput("engine", "forged", "finding", "f_a")) : r).ToList()) is { Ok: false, FirstBrokenSeq: 3 });
    Check("an empty log verifies", AuditChain.Verify([]).Ok);
    {
        // A log written with a hole in its numbering, every hash and link otherwise correct.
        var one = AuditChain.Link(1, "t", AuditChain.Genesis, new EventInput("engine", "a", "finding", "f_a"));
        var three = AuditChain.Link(3, "t", one.Hash, new EventInput("engine", "b", "finding", "f_a"));
        Check("a hole in the numbering is found even when every hash links", AuditChain.Verify([one, three]) is { Ok: false, FirstBrokenSeq: 3 });
        var zero = AuditChain.Link(0, "t", AuditChain.Genesis, new EventInput("engine", "a", "finding", "f_a"));
        Check("a log that does not start at one is refused", AuditChain.Verify([zero]) is { Ok: false });
    }
    Check("text cannot slide between fields",
        AuditChain.ComputeHash(1, "t", "engine", "kind", "finding", "f_a", "{}", AuditChain.Genesis)
        != AuditChain.ComputeHash(1, "t", "engine", "kindfinding", "", "f_a", "{}", AuditChain.Genesis));

    memory.TamperWithEventForTest(2, rows[2] with { PayloadJson = "{\"source\":\"edited\"}" });
    Check("the in-memory store reports an edit", memory.VerifyAudit() is { Ok: false, FirstBrokenSeq: 3 });
    sqlite.TamperWithEventForTest(3, "{\"source\":\"edited\"}");
    Check("the SQLite store reports an edit made in the file", sqlite.VerifyAudit() is { Ok: false, FirstBrokenSeq: 3 });
}

Section("The SQLite file is what a restarted engine picks up");
sqlite.Dispose();
{
    using var reopened = new SqliteClaimStore(databasePath);
    reopened.Initialize();
    Equal("the household is there", reopened.GetHouseholdJson(), "{\"version\":1}");
    Equal("the findings are there", reopened.ListFindings().Count, 4);
    Equal("the case state is there", reopened.GetFinding("f_a")!.CaseState, "PACKET_READY");
    Equal("the settings are there", reopened.GetConfig("source.missingmoney.blocked_streak"), "2");
    Equal("the searches are there", reopened.ListSearches().Count, 3);
    Check("opening it again does not rebuild or reset anything", reopened.GetEvents().Count == memory.GetEvents().Count);
}
{
    using var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
    raw.Open();
    string Scalar(string sql) { using var c = raw.CreateCommand(); c.CommandText = sql; return Convert.ToString(c.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!; }
    Equal("the schema version is recorded in the file", Scalar("PRAGMA user_version;"), SqliteClaimStore.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
    Equal("the file is in write-ahead mode", Scalar("PRAGMA journal_mode;"), "wal");
    var tables = new List<string>();
    using (var c = raw.CreateCommand())
    {
        c.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var reader = c.ExecuteReader();
        while (reader.Read()) tables.Add(reader.GetString(0));
    }
    Equal("the tables are the ones the specification names", string.Join(",", tables), "config,drafts,events,findings,household,searches");
    var columns = Scalar("SELECT group_concat(name) FROM pragma_table_info('findings');") + "," + Scalar("SELECT group_concat(name) FROM pragma_table_info('household');");
    Check("no column exists that could hold an identifier", !new[] { "ssn", "social", "tax", "account", "card", "license", "dob", "birth" }.Any(word => columns.Contains(word, StringComparison.OrdinalIgnoreCase)));

    bool Rejected(string sql)
    {
        try { using var c = raw.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); return false; }
        catch (SqliteException) { return true; }
    }
    Check("the file itself refuses a state that is not in the lifecycle", Rejected("UPDATE findings SET case_state = 'SENT' WHERE id = 'f_a';"));
    Check("the file itself refuses a confidence called confirmed", Rejected("UPDATE findings SET confidence = 'Confirmed' WHERE id = 'f_a';"));
    Check("there can only be one household", Rejected("INSERT INTO household(id, json, updated_at) VALUES(2, '{}', 't');"));
    Check("a draft must belong to a finding", Rejected("PRAGMA foreign_keys=ON; INSERT INTO drafts(finding_id, template_id, path, sha256, created_at) VALUES('f_nope', 't', 'p', 's', 't');"));

    using (var c = raw.CreateCommand()) { c.CommandText = "PRAGMA user_version = 99;"; c.ExecuteNonQuery(); }
}
{
    using var newer = new SqliteClaimStore(databasePath);
    Check("a database from a newer build is refused, not guessed at", Throws<InvalidOperationException>(() => newer.Initialize()));
}
{
    var releasePath = temp.File("release.db");
    var store = new SqliteClaimStore(releasePath);
    store.Initialize();
    store.SetConfig("k", "v");
    store.Dispose();
    var deleted = true;
    try { File.Delete(releasePath); } catch (IOException) { deleted = false; }
    Check("a closed store lets go of its file, so it can be deleted at once", deleted && !File.Exists(releasePath));
}

return Finish();
