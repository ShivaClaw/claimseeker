using System.Globalization;
using ClaimSeeker.Core;
using Microsoft.Data.Sqlite;

namespace ClaimSeeker.Store;

/// <summary>
/// The store on disk: one SQLite file in the person's own workspace. The file holds names and
/// addresses, so it is created per user and never anywhere shared. It holds no government or
/// financial identifier, because the engine has nowhere to put one.
/// </summary>
public sealed class SqliteClaimStore : IClaimStore
{
    /// <summary>The schema this build writes. A database from a newer build is refused, not guessed at.</summary>
    public const int SchemaVersion = 1;

    private readonly SqliteConnection _connection;
    private readonly Lock _gate = new();

    public SqliteClaimStore(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Without pooling the file is released the moment the store is disposed, so it can be
            // moved or deleted straight away. Delete-all-data depends on that.
            Pooling = false,
        };
        _connection = new SqliteConnection(builder.ToString());
    }

    public void Initialize()
    {
        lock (_gate)
        {
            _connection.Open();
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA foreign_keys=ON;");
            Execute("PRAGMA synchronous=NORMAL;");

            var version = Convert.ToInt32(Scalar("PRAGMA user_version;"), CultureInfo.InvariantCulture);
            if (version > SchemaVersion)
                throw new InvalidOperationException($"This database was written by a newer build (schema {version}); this build reads schema {SchemaVersion}.");
            if (version == SchemaVersion) return;

            using var tx = _connection.BeginTransaction();
            foreach (var statement in SchemaV1())
                Execute(statement, tx);
            Execute($"PRAGMA user_version={SchemaVersion};", tx);
            tx.Commit();
        }
    }

    public void Dispose()
    {
        lock (_gate) _connection.Dispose();
    }

    private static IEnumerable<string> SchemaV1()
    {
        // The state and confidence vocabularies come from the enums, so there is no second list to keep in step.
        var states = string.Join(",", Enum.GetNames<CaseState>().Select(s => $"'{s}'"));
        var confidences = string.Join(",", Enum.GetNames<Confidence>().Select(s => $"'{s}'"));

        yield return "CREATE TABLE household(id INTEGER PRIMARY KEY CHECK(id = 1), json TEXT NOT NULL, updated_at TEXT NOT NULL);";
        yield return $"""
            CREATE TABLE findings(
                id TEXT PRIMARY KEY,
                subject_id TEXT NOT NULL,
                source_id TEXT NOT NULL,
                source_record_id TEXT,
                dedupe_key TEXT NOT NULL,
                duplicate_of TEXT,
                owner_name TEXT NOT NULL,
                holder_name TEXT,
                city TEXT,
                us_state TEXT,
                value_text TEXT,
                value_low_cents INTEGER,
                value_high_cents INTEGER,
                property_type_text TEXT,
                family TEXT NOT NULL,
                confidence TEXT NOT NULL CHECK(confidence IN ({confidences})),
                name_match TEXT NOT NULL,
                rationale TEXT NOT NULL,
                name_change_docs INTEGER NOT NULL,
                estate_record INTEGER NOT NULL,
                query_key TEXT NOT NULL,
                case_state TEXT NOT NULL CHECK(case_state IN ({states})),
                expected_value TEXT,
                effort_hours TEXT,
                score TEXT,
                urgent INTEGER NOT NULL DEFAULT 0,
                rank_rationale TEXT,
                first_seen TEXT NOT NULL,
                last_seen TEXT NOT NULL,
                missing_since TEXT,
                packet_path TEXT,
                updated_at TEXT NOT NULL);
            """;
        yield return "CREATE INDEX ix_findings_dedupe ON findings(dedupe_key);";
        yield return """
            CREATE TABLE searches(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_id TEXT NOT NULL,
                query_key TEXT NOT NULL,
                family_name TEXT NOT NULL,
                given_name TEXT,
                us_state TEXT,
                kind TEXT NOT NULL,
                mode TEXT NOT NULL,
                outcome TEXT NOT NULL,
                reason TEXT,
                items_seen INTEGER NOT NULL,
                matched INTEGER NOT NULL,
                truncated INTEGER NOT NULL,
                ran_at TEXT NOT NULL);
            """;
        yield return "CREATE INDEX ix_searches_key ON searches(source_id, query_key, id);";
        yield return """
            CREATE TABLE drafts(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                finding_id TEXT NOT NULL REFERENCES findings(id),
                template_id TEXT NOT NULL,
                path TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                created_at TEXT NOT NULL);
            """;
        yield return """
            CREATE TABLE events(
                seq INTEGER PRIMARY KEY,
                at TEXT NOT NULL,
                actor TEXT NOT NULL,
                kind TEXT NOT NULL,
                entity TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                prev_hash TEXT NOT NULL,
                hash TEXT NOT NULL);
            """;
        yield return "CREATE TABLE config(key TEXT PRIMARY KEY, value TEXT NOT NULL);";
    }

    // ---------- household ----------

    public void SaveHousehold(string json, int subjectCount, string actor, string now)
    {
        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();
            Execute("INSERT INTO household(id, json, updated_at) VALUES(1, $json, $now) ON CONFLICT(id) DO UPDATE SET json = $json, updated_at = $now;",
                tx, ("$json", json), ("$now", now));
            Append(new EventInput(actor, StoreVocabulary.HouseholdSaved, StoreVocabulary.HouseholdEntity, "1", StoreRules.HouseholdPayload(subjectCount)), now, tx);
            tx.Commit();
        }
    }

    public string? GetHouseholdJson()
    {
        lock (_gate) return Scalar("SELECT json FROM household WHERE id = 1;") as string;
    }

    // ---------- findings ----------

    public FindingWriteResult UpsertFinding(FindingUpsert finding, string now)
    {
        StoreRules.RequireFinding(finding);
        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();

            var existingDuplicate = QueryOne(
                "SELECT duplicate_of FROM findings WHERE id = $id;", tx,
                r => (Found: true, DuplicateOf: r.IsDBNull(0) ? null : r.GetString(0)),
                ("$id", finding.Id));

            if (existingDuplicate.Found)
            {
                Execute("""
                    UPDATE findings SET
                        subject_id = $subject, owner_name = $owner, holder_name = $holder, city = $city, us_state = $state,
                        value_text = $valueText, value_low_cents = $low, value_high_cents = $high, property_type_text = $type,
                        family = $family, confidence = $confidence, name_match = $nameMatch, rationale = $rationale,
                        name_change_docs = $nameChange, estate_record = $estate, query_key = $queryKey,
                        last_seen = $now, missing_since = NULL, updated_at = $now
                    WHERE id = $id;
                    """, tx, FindingParameters(finding, now));
                tx.Commit();
                return new FindingWriteResult(finding.Id, false, existingDuplicate.DuplicateOf);
            }

            var duplicateOf = QueryOne(
                "SELECT COALESCE(duplicate_of, id) FROM findings WHERE dedupe_key = $key AND source_id <> $source ORDER BY rowid LIMIT 1;", tx,
                r => (Found: true, Id: (string?)r.GetString(0)),
                ("$key", finding.DedupeKey), ("$source", finding.SourceId)).Id;

            Execute("""
                INSERT INTO findings(
                    id, subject_id, source_id, source_record_id, dedupe_key, duplicate_of, owner_name, holder_name, city, us_state,
                    value_text, value_low_cents, value_high_cents, property_type_text, family, confidence, name_match, rationale,
                    name_change_docs, estate_record, query_key, case_state, first_seen, last_seen, updated_at)
                VALUES(
                    $id, $subject, $sourceId, $recordId, $dedupe, $duplicateOf, $owner, $holder, $city, $state,
                    $valueText, $low, $high, $type, $family, $confidence, $nameMatch, $rationale,
                    $nameChange, $estate, $queryKey, $caseState, $now, $now, $now);
                """, tx,
                [.. FindingParameters(finding, now),
                    ("$sourceId", finding.SourceId), ("$recordId", finding.SourceRecordId), ("$dedupe", finding.DedupeKey),
                    ("$duplicateOf", duplicateOf), ("$caseState", StoreRules.InitialState)]);

            Append(new EventInput("engine", StoreVocabulary.FindingDiscovered, StoreVocabulary.FindingEntity, finding.Id, StoreRules.DiscoveredPayload(finding)), now, tx);
            tx.Commit();
            return new FindingWriteResult(finding.Id, true, duplicateOf);
        }
    }

    private static (string, object?)[] FindingParameters(FindingUpsert f, string now) =>
    [
        ("$id", f.Id), ("$subject", f.SubjectId), ("$owner", f.OwnerName), ("$holder", f.HolderName), ("$city", f.City),
        ("$state", f.UsState), ("$valueText", f.ValueText), ("$low", StoreRules.ToCents(f.ValueLow)), ("$high", StoreRules.ToCents(f.ValueHigh)),
        ("$type", f.PropertyTypeText), ("$family", f.Family), ("$confidence", f.Confidence), ("$nameMatch", f.NameMatch),
        ("$rationale", f.Rationale), ("$nameChange", f.NameChangeDocsLikely ? 1 : 0), ("$estate", f.EstateRecord ? 1 : 0),
        ("$queryKey", f.QueryKey), ("$now", now),
    ];

    private const string FindingColumns = """
        id, subject_id, source_id, source_record_id, dedupe_key, duplicate_of, owner_name, holder_name, city, us_state,
        value_text, value_low_cents, value_high_cents, property_type_text, family, confidence, name_match, rationale,
        name_change_docs, estate_record, query_key, case_state, expected_value, effort_hours, score, urgent, rank_rationale,
        first_seen, last_seen, missing_since, packet_path, updated_at
        """;

    private static FindingRow ReadFinding(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        SubjectId = r.GetString(1),
        SourceId = r.GetString(2),
        SourceRecordId = Text(r, 3),
        DedupeKey = r.GetString(4),
        DuplicateOf = Text(r, 5),
        OwnerName = r.GetString(6),
        HolderName = Text(r, 7),
        City = Text(r, 8),
        UsState = Text(r, 9),
        ValueText = Text(r, 10),
        ValueLow = StoreRules.FromCents(r.IsDBNull(11) ? null : r.GetInt64(11)),
        ValueHigh = StoreRules.FromCents(r.IsDBNull(12) ? null : r.GetInt64(12)),
        PropertyTypeText = Text(r, 13),
        Family = r.GetString(14),
        Confidence = r.GetString(15),
        NameMatch = r.GetString(16),
        Rationale = r.GetString(17),
        NameChangeDocsLikely = r.GetInt64(18) != 0,
        EstateRecord = r.GetInt64(19) != 0,
        QueryKey = r.GetString(20),
        CaseState = r.GetString(21),
        ExpectedValue = StoreRules.Number(Text(r, 22)),
        EffortHours = StoreRules.Number(Text(r, 23)),
        Score = StoreRules.Number(Text(r, 24)),
        Urgent = r.GetInt64(25) != 0,
        RankRationaleJson = Text(r, 26),
        FirstSeen = r.GetString(27),
        LastSeen = r.GetString(28),
        MissingSince = Text(r, 29),
        PacketPath = Text(r, 30),
        UpdatedAt = r.GetString(31),
    };

    public FindingRow? GetFinding(string id)
    {
        lock (_gate)
            return QueryList($"SELECT {FindingColumns} FROM findings WHERE id = $id;", null, ReadFinding, ("$id", id)).FirstOrDefault();
    }

    public IReadOnlyList<FindingRow> ListFindings()
    {
        lock (_gate) return QueryList($"SELECT {FindingColumns} FROM findings ORDER BY rowid;", null, ReadFinding);
    }

    public bool TryTransition(string findingId, string expectedState, string newState, string actor, string payloadJson, string now)
    {
        StoreRules.RequireCaseState(newState);
        var payload = StoreRules.TransitionPayload(expectedState, newState, payloadJson);
        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();
            var changed = Execute(
                "UPDATE findings SET case_state = $new, updated_at = $now WHERE id = $id AND case_state = $expected;", tx,
                ("$new", newState), ("$now", now), ("$id", findingId), ("$expected", expectedState));
            if (changed != 1)
            {
                tx.Rollback();
                return false;
            }

            Append(new EventInput(actor, StoreVocabulary.StateChange, StoreVocabulary.FindingEntity, findingId, payload), now, tx);
            tx.Commit();
            return true;
        }
    }

    public void SaveRank(string findingId, decimal expectedValue, decimal effortHours, decimal score, bool urgent, string rationaleJson, string now)
    {
        lock (_gate)
            Execute(
                "UPDATE findings SET expected_value = $ev, effort_hours = $effort, score = $score, urgent = $urgent, rank_rationale = $why, updated_at = $now WHERE id = $id;",
                null, ("$ev", StoreRules.Text(expectedValue)), ("$effort", StoreRules.Text(effortHours)), ("$score", StoreRules.Text(score)),
                ("$urgent", urgent ? 1 : 0), ("$why", rationaleJson), ("$now", now), ("$id", findingId));
    }

    public void SetPacketPath(string findingId, string path, string now)
    {
        lock (_gate)
            Execute("UPDATE findings SET packet_path = $path, updated_at = $now WHERE id = $id;", null,
                ("$path", path), ("$now", now), ("$id", findingId));
    }

    public IReadOnlyList<string> MarkMissing(string sourceId, string queryKey, IReadOnlyCollection<string> seenIds, string now)
    {
        var seen = new HashSet<string>(seenIds, StringComparer.Ordinal);
        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();
            var candidates = QueryList(
                "SELECT id FROM findings WHERE source_id = $source AND query_key = $key AND missing_since IS NULL ORDER BY rowid;", tx,
                r => r.GetString(0), ("$source", sourceId), ("$key", queryKey));

            var marked = candidates.Where(id => !seen.Contains(id)).ToList();
            foreach (var id in marked)
                Execute("UPDATE findings SET missing_since = $now, updated_at = $now WHERE id = $id;", tx, ("$now", now), ("$id", id));

            tx.Commit();
            return marked;
        }
    }

    // ---------- searches ----------

    public long RecordSearch(SearchInput s)
    {
        lock (_gate)
        {
            Execute("""
                INSERT INTO searches(source_id, query_key, family_name, given_name, us_state, kind, mode, outcome, reason, items_seen, matched, truncated, ran_at)
                VALUES($source, $key, $family, $given, $state, $kind, $mode, $outcome, $reason, $items, $matched, $truncated, $ranAt);
                """, null,
                ("$source", s.SourceId), ("$key", s.QueryKey), ("$family", s.FamilyName), ("$given", s.GivenName), ("$state", s.UsState),
                ("$kind", s.Kind), ("$mode", s.Mode), ("$outcome", s.Outcome), ("$reason", s.Reason), ("$items", s.ItemsSeen),
                ("$matched", s.Matched), ("$truncated", s.Truncated ? 1 : 0), ("$ranAt", s.RanAt));
            return Convert.ToInt64(Scalar("SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);
        }
    }

    private const string SearchColumns =
        "id, source_id, query_key, family_name, given_name, us_state, kind, mode, outcome, reason, items_seen, matched, truncated, ran_at";

    private static SearchRow ReadSearch(SqliteDataReader r) => new(r.GetInt64(0), new SearchInput
    {
        SourceId = r.GetString(1),
        QueryKey = r.GetString(2),
        FamilyName = r.GetString(3),
        GivenName = Text(r, 4),
        UsState = Text(r, 5),
        Kind = r.GetString(6),
        Mode = r.GetString(7),
        Outcome = r.GetString(8),
        Reason = Text(r, 9),
        ItemsSeen = r.GetInt32(10),
        Matched = r.GetInt32(11),
        Truncated = r.GetInt64(12) != 0,
        RanAt = r.GetString(13),
    });

    public SearchRow? GetLastSearch(string sourceId, string queryKey)
    {
        lock (_gate)
            return QueryList(
                $"SELECT {SearchColumns} FROM searches WHERE source_id = $source AND query_key = $key ORDER BY id DESC LIMIT 1;", null,
                ReadSearch, ("$source", sourceId), ("$key", queryKey)).FirstOrDefault();
    }

    public IReadOnlyList<SearchRow> ListSearches()
    {
        lock (_gate) return QueryList($"SELECT {SearchColumns} FROM searches ORDER BY id;", null, ReadSearch);
    }

    // ---------- drafts ----------

    public long RecordDraft(string findingId, string templateId, string path, string sha256, string now)
    {
        lock (_gate)
        {
            Execute("INSERT INTO drafts(finding_id, template_id, path, sha256, created_at) VALUES($finding, $template, $path, $sha, $now);", null,
                ("$finding", findingId), ("$template", templateId), ("$path", path), ("$sha", sha256), ("$now", now));
            return Convert.ToInt64(Scalar("SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);
        }
    }

    public IReadOnlyList<DraftRow> ListDrafts(string findingId)
    {
        lock (_gate)
            return QueryList("SELECT id, finding_id, template_id, path, sha256, created_at FROM drafts WHERE finding_id = $finding ORDER BY id;", null,
                r => new DraftRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)),
                ("$finding", findingId));
    }

    // ---------- audit ----------

    public long AppendEvent(EventInput e, string now)
    {
        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();
            var seq = Append(e, now, tx);
            tx.Commit();
            return seq;
        }
    }

    private long Append(EventInput e, string now, SqliteTransaction tx)
    {
        var last = QueryOne("SELECT seq, hash FROM events ORDER BY seq DESC LIMIT 1;", tx, r => (Found: true, Seq: r.GetInt64(0), Hash: r.GetString(1)));
        var seq = last.Found ? last.Seq + 1 : 1;
        var row = AuditChain.Link(seq, now, last.Found ? last.Hash : AuditChain.Genesis, e);

        Execute("""
            INSERT INTO events(seq, at, actor, kind, entity, entity_id, payload_json, prev_hash, hash)
            VALUES($seq, $at, $actor, $kind, $entity, $entityId, $payload, $prev, $hash);
            """, tx,
            ("$seq", row.Seq), ("$at", row.At), ("$actor", row.Actor), ("$kind", row.Kind), ("$entity", row.Entity),
            ("$entityId", row.EntityId), ("$payload", row.PayloadJson), ("$prev", row.PrevHash), ("$hash", row.Hash));
        return seq;
    }

    public IReadOnlyList<EventRow> GetEvents()
    {
        lock (_gate)
            return QueryList("SELECT seq, at, actor, kind, entity, entity_id, payload_json, prev_hash, hash FROM events ORDER BY seq;", null,
                r => new EventRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                    r.GetString(6), r.GetString(7), r.GetString(8)));
    }

    public AuditVerification VerifyAudit() => AuditChain.Verify(GetEvents());

    // ---------- settings ----------

    public string? GetConfig(string key)
    {
        lock (_gate) return Scalar("SELECT value FROM config WHERE key = $key;", null, ("$key", key)) as string;
    }

    public void SetConfig(string key, string value)
    {
        lock (_gate)
            Execute("INSERT INTO config(key, value) VALUES($key, $value) ON CONFLICT(key) DO UPDATE SET value = $value;", null,
                ("$key", key), ("$value", value));
    }

    // ---------- plumbing ----------

    private SqliteCommand Command(string sql, SqliteTransaction? tx, (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = tx;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private int Execute(string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, tx, parameters);
        return command.ExecuteNonQuery();
    }

    private object? Scalar(string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, tx, parameters);
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    private List<T> QueryList<T>(string sql, SqliteTransaction? tx, Func<SqliteDataReader, T> read, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, tx, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(read(reader));
        return rows;
    }

    /// <summary>The first row, or the default value of <typeparamref name="T"/> when there is none.</summary>
    private T QueryOne<T>(string sql, SqliteTransaction? tx, Func<SqliteDataReader, T> read, params (string Name, object? Value)[] parameters)
        where T : struct
    {
        using var command = Command(sql, tx, parameters);
        using var reader = command.ExecuteReader();
        return reader.Read() ? read(reader) : default;
    }

    private static string? Text(SqliteDataReader r, int ordinal) => r.IsDBNull(ordinal) ? null : r.GetString(ordinal);

    /// <summary>Harness seam: rewrites one audit line in place, as someone editing the file by hand would.</summary>
    public void TamperWithEventForTest(long seq, string payloadJson)
    {
        lock (_gate)
            Execute("UPDATE events SET payload_json = $payload WHERE seq = $seq;", null, ("$payload", payloadJson), ("$seq", seq));
    }
}
