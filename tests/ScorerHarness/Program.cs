// ScorerHarness: whose record is whose, how sure that is, and what order findings are shown in.
// These are pure functions, so every check is a table row: a record in, a verdict out.
using ClaimSeeker.Core;
using ClaimSeeker.Scorer;
using static Harness;

Title("ClaimSeeker matching and ranking");
var household = SampleHousehold.Load();

RawRecord Record(string owner, string? city = null, string? state = null, string? value = null, string? type = null) =>
    new("missingmoney", null, owner, "A HOLDER", city, state, value, type);

void Expect(string name, RawRecord record, string? subjectId, Confidence? confidence = null, NameMatch? nameMatch = null, Household? among = null)
{
    var match = Matcher.Match(record, among ?? household);
    if (subjectId is null)
    {
        Check(name, match is null, match is null ? null : $"matched {match.SubjectId} as {match.Confidence}/{match.NameMatch}");
        return;
    }

    var ok = match is not null && match.SubjectId == subjectId
             && (confidence is null || match.Confidence == confidence)
             && (nameMatch is null || match.NameMatch == nameMatch);
    Check(name, ok, match is null ? "no match" : $"got {match.SubjectId} {match.Confidence}/{match.NameMatch}");
}

// ---------- the place decides how far a name is trusted ----------
Section("An exact name is trusted as far as the place supports it");
Expect("name and city match: strong", Record("SAMPLE JANE", "SPRINGFIELD", "IL"), "self", Confidence.Strong, NameMatch.ExactCurrent);
Expect("name matches, state only: likely", Record("SAMPLE JANE", "CLEVELAND", "OH"), "self", Confidence.Likely, NameMatch.ExactCurrent);
Expect("name matches, a state she never lived in: possible", Record("SAMPLE JANE", "PHOENIX", "AZ"), "self", Confidence.Possible);
Expect("name matches, no location at all: possible", Record("SAMPLE JANE"), "self", Confidence.Possible);
Expect("the right city in the wrong state is not a city match", Record("SAMPLE JANE", "SPRINGFIELD", "MO"), "self", Confidence.Possible);
Expect("order of the name does not matter", Record("JANE SAMPLE", "SPRINGFIELD", "IL"), "self", Confidence.Strong);
Expect("a comma does not matter", Record("Sample, Jane", "Springfield", "il"), "self", Confidence.Strong);
Expect("her own middle initial fits", Record("SAMPLE JANE Q", "SPRINGFIELD", "IL"), "self", Confidence.Strong);
Expect("her own middle name fits", Record("JANE QUINN SAMPLE", "SPRINGFIELD", "IL"), "self", Confidence.Strong);
Expect("someone else's middle initial steps the match down", Record("SAMPLE JANE X", "SPRINGFIELD", "IL"), "self", Confidence.Likely);
Expect("and cannot push a possible match lower", Record("SAMPLE JANE X", "PHOENIX", "AZ"), "self", Confidence.Possible);
Expect("a suffix is not part of the name", Record("SAMPLE JANE JR", "SPRINGFIELD", "IL"), "self", Confidence.Strong);
Expect("a different given name is not a match", Record("SAMPLE JANET", "SPRINGFIELD", "IL"), null);
Expect("a different family name is not a match", Record("SAMPLES JANE", "SPRINGFIELD", "IL"), null);

Section("A former name counts, and says papers will be needed");
Expect("former name and city: strong", Record("EXAMPLE JANE", "COLUMBUS", "OH"), "self", Confidence.Strong, NameMatch.ExactPrior);
Check("a former-name match flags the name-change papers", Matcher.Match(Record("EXAMPLE JANE", "COLUMBUS", "OH"), household)!.NameChangeDocsLikely);
Check("a current-name match does not", !Matcher.Match(Record("SAMPLE JANE", "COLUMBUS", "OH"), household)!.NameChangeDocsLikely);
Expect("a stranger under the former name is not a match", Record("EXAMPLE JOHN", "COLUMBUS", "OH"), null);

Section("A weaker name needs a place behind it, and is capped");
Expect("another given name she uses, with the city: likely", Record("SAMPLE JANIE", "SPRINGFIELD", "IL"), "self", Confidence.Likely, NameMatch.Alias);
Expect("the same without the city: possible", Record("SAMPLE JANIE", "TAMPA", "FL"), "self", Confidence.Possible, NameMatch.Alias);
Expect("middle name as first name, with the city: likely", Record("SAMPLE QUINN", "SPRINGFIELD", "IL"), "self", Confidence.Likely, NameMatch.MiddleAsGiven);
Expect("middle name as first name, state only: possible", Record("SAMPLE QUINN", "PEORIA", "IL"), "self", Confidence.Possible, NameMatch.MiddleAsGiven);
Expect("middle name as first name, nowhere she lived: dropped", Record("SAMPLE QUINN", "TAMPA", "FL"), null);
Expect("an initial, with the city: likely", Record("SAMPLE J", "SPRINGFIELD", "IL"), "self", Confidence.Likely, NameMatch.InitialOnly);
Expect("an initial, state only: possible", Record("SAMPLE J", "PEORIA", "IL"), "self", Confidence.Possible, NameMatch.InitialOnly);
Expect("an initial, nowhere she lived: dropped", Record("SAMPLE J", "TAMPA", "FL"), null);
Expect("the wrong initial is not a match", Record("SAMPLE K", "SPRINGFIELD", "IL"), null);
Expect("an initial beside a full first name is not an initial match", Record("SAMPLE J MARIA", "SPRINGFIELD", "IL"), null);
Expect("the family name alone, in her city: possible", Record("SAMPLE", "COLUMBUS", "OH"), "self", Confidence.Possible, NameMatch.FamilyOnly);
Expect("the family name alone, elsewhere: dropped", Record("SAMPLE", "TAMPA", "FL"), null);

Section("Each person in the household is matched on their own facts");
Expect("the father's record goes to the father", Record("SAMPLE ROBERT", "DAYTON", "OH"), "dad", Confidence.Strong);
Expect("an estate record is his, and marked as an estate", Record("ESTATE OF ROBERT SAMPLE", "DAYTON", "OH"), "dad", Confidence.Strong);
Check("the estate flag is carried", Matcher.Match(Record("ESTATE OF ROBERT SAMPLE", "DAYTON", "OH"), household)!.EstateRecord);
Check("an ordinary record is not an estate", !Matcher.Match(Record("SAMPLE ROBERT", "DAYTON", "OH"), household)!.EstateRecord);
Expect("a person with no consent basis is never matched", Record("SAMPLE ALEX", "SPRINGFIELD", "IL"), null);
Expect("a stranger with the family name is not a match", Record("SAMPLE MARIA", "TAMPA", "FL"), null);
Expect("an organisation is not a person", Record("SAMPLE FAMILY TRUST", "SPRINGFIELD", "IL"), null);
Expect("a company is not a person", Record("JANE SAMPLE LLC", "SPRINGFIELD", "IL"), null);
Expect("an empty owner matches no one", Record("   ", "SPRINGFIELD", "IL"), null);

Section("Two owners on one line are two owners");
Expect("the second owner inherits the family name", Record("SAMPLE ROBERT & JANE", "SPRINGFIELD", "IL"), "self", Confidence.Strong);
Expect("AND separates owners too", Record("SAMPLE ROBERT AND JANE", "DAYTON", "OH"), "dad", Confidence.Strong);
Expect("one owner's given name is never paired with the other's family name", Record("DOE JANE & SAMPLE MARIA", "SPRINGFIELD", "IL"), null);
Expect("an unrelated co-owner does not block a real one", Record("DOE JOHN & SAMPLE JANE", "SPRINGFIELD", "IL"), "self", Confidence.Strong);

Section("Names as databases really hold them");
var obrien = SampleHousehold.Of(SampleHousehold.Person("Seán", "O'Brien", ("Boston", "MA")));
Expect("an apostrophe and an accent", Record("OBRIEN SEAN", "BOSTON", "MA"), "self", Confidence.Strong, among: obrien);
var mcdonald = SampleHousehold.Of(SampleHousehold.Person("Ann", "McDonald", ("Reno", "NV")));
Expect("a name split in two by a clerk", Record("MC DONALD ANN", "RENO", "NV"), "self", Confidence.Strong, among: mcdonald);
var delacruz = SampleHousehold.Of(SampleHousehold.Person("Maria", "de la Cruz", ("El Paso", "TX")));
Expect("a compound name as written", Record("DE LA CRUZ MARIA", "EL PASO", "TX"), "self", Confidence.Strong, NameMatch.ExactCurrent, delacruz);
Expect("a compound name run together", Record("DELACRUZ MARIA", "EL PASO", "TX"), "self", Confidence.Strong, NameMatch.ExactCurrent, delacruz);
Expect("one part of a compound name, in her city: possible at most", Record("CRUZ MARIA", "EL PASO", "TX"), "self", Confidence.Possible, NameMatch.FamilyPart, delacruz);
Expect("one part of a compound name, elsewhere: dropped", Record("CRUZ MARIA", "MIAMI", "FL"), null, among: delacruz);
var miller = SampleHousehold.Of(SampleHousehold.Person("John", "Miller", ("Dayton", "OH")));
Expect("a likely misspelling, in his city: possible at most", Record("MILER JOHN", "DAYTON", "OH"), "self", Confidence.Possible, NameMatch.Misspelling, miller);
Expect("a likely misspelling, elsewhere: dropped", Record("MILER JOHN", "TAMPA", "FL"), null, among: miller);
Expect("a misspelling with only an initial: dropped", Record("MILER J", "DAYTON", "OH"), null, among: miller);
var maryAnn = SampleHousehold.Of(SampleHousehold.Person("Mary Ann", "Lopez", ("Tucson", "AZ")));
Expect("a two-word given name", Record("LOPEZ MARY ANN", "TUCSON", "AZ"), "self", Confidence.Strong, among: maryAnn);

Section("The verdict explains itself");
var explained = Matcher.Match(Record("SAMPLE JANE", "CLEVELAND", "OH"), household)!;
Check("the reason names the name", explained.Rationale.Contains("given and family names match", StringComparison.Ordinal));
Check("and the place", explained.Rationale.Contains("state this person lived in", StringComparison.Ordinal));
Check("matching the same record twice gives the same verdict", Matcher.Match(Record("SAMPLE JANE", "CLEVELAND", "OH"), household) == explained);

// ---------- ranking ----------
Section("Expected value follows what the source stated");
var day = new DateOnly(2026, 11, 15);
RankInput Input(MoneyRange value, PropertyFamily family = PropertyFamily.Miscellaneous, Confidence confidence = Confidence.Strong,
    bool nameChange = false, bool deceased = false, InteractionMode mode = InteractionMode.Auto, bool fallback = false, DateOnly? deadline = null) =>
    new(value, family, confidence, nameChange, deceased, mode, fallback, deadline);

Equal("an exact amount is itself", Ranker.Rank(Input(MoneyRange.Exact(312.40m)), day).ExpectedValue, 312.40m);
Equal("a range is its midpoint", Ranker.Rank(Input(MoneyRange.Between(50m, 100m)), day).ExpectedValue, 75m);
Equal("a ceiling is half of it", Ranker.Rank(Input(MoneyRange.UpTo(50m)), day).ExpectedValue, 25m);
Equal("an undisclosed amount is nothing", Ranker.Rank(Input(MoneyRange.Undisclosed), day).ExpectedValue, 0m);
Equal("open-ended securities: three times the floor", Ranker.Rank(Input(MoneyRange.AtLeast(100m), PropertyFamily.Securities), day).ExpectedValue, 300m);
Equal("open-ended insurance: three times the floor", Ranker.Rank(Input(MoneyRange.AtLeast(100m), PropertyFamily.Insurance), day).ExpectedValue, 300m);
Equal("open-ended bank deposit: twice the floor", Ranker.Rank(Input(MoneyRange.AtLeast(100m), PropertyFamily.AccountBalance), day).ExpectedValue, 200m);
Equal("open-ended wages: one and a half times", Ranker.Rank(Input(MoneyRange.AtLeast(100m), PropertyFamily.Wages), day).ExpectedValue, 150m);
Equal("open-ended utility refund: barely more", Ranker.Rank(Input(MoneyRange.AtLeast(100m), PropertyFamily.Utility), day).ExpectedValue, 110m);
Equal("open-ended anything else: the floor", Ranker.Rank(Input(MoneyRange.AtLeast(100m), PropertyFamily.Unknown), day).ExpectedValue, 100m);
Check("the multiplier applies only to open-ended amounts",
    Ranker.Rank(Input(MoneyRange.Exact(100m), PropertyFamily.Securities), day).ExpectedValue == 100m);

Section("Effort adds up what the person will have to do");
Equal("the base is half an hour", Ranker.Rank(Input(MoneyRange.Exact(40m)), day).EffortHours, 0.5m);
Equal("a former name adds half an hour", Ranker.Rank(Input(MoneyRange.Exact(40m), nameChange: true), day).EffortHours, 1.0m);
Equal("a deceased owner adds an hour and a half", Ranker.Rank(Input(MoneyRange.Exact(40m), deceased: true), day).EffortHours, 2.0m);
Equal("a large claim adds an hour for a notary", Ranker.Rank(Input(MoneyRange.Exact(1000m)), day).EffortHours, 1.5m);
Equal("just under the notary line adds nothing", Ranker.Rank(Input(MoneyRange.Exact(999.99m)), day).EffortHours, 0.5m);
Equal("a search run by hand adds half an hour", Ranker.Rank(Input(MoneyRange.Exact(40m), fallback: true), day).EffortHours, 1.0m);
Equal("an assisted source adds a quarter", Ranker.Rank(Input(MoneyRange.Exact(40m), mode: InteractionMode.Assisted), day).EffortHours, 0.75m);
Equal("a guided source adds a half", Ranker.Rank(Input(MoneyRange.Exact(40m), mode: InteractionMode.Guided), day).EffortHours, 1.0m);
Equal("everything at once", Ranker.Rank(Input(MoneyRange.Exact(1250m), nameChange: true, deceased: true, mode: InteractionMode.Guided, fallback: true), day).EffortHours, 4.5m);
Equal("score is value over effort", Ranker.Rank(Input(MoneyRange.Exact(1250m), nameChange: true), day).Score, 625m);
Check("each addition is explained", Ranker.Rank(Input(MoneyRange.Exact(1250m), nameChange: true), day).Rationale.Count == 3);

Section("A close deadline is urgent");
Check("sixty days out is urgent", Ranker.Rank(Input(MoneyRange.Exact(40m), deadline: day.AddDays(60)), day).Urgent);
Check("sixty-one days out is not", !Ranker.Rank(Input(MoneyRange.Exact(40m), deadline: day.AddDays(61)), day).Urgent);
Check("today is urgent", Ranker.Rank(Input(MoneyRange.Exact(40m), deadline: day), day).Urgent);
Check("a deadline already passed is not urgent", !Ranker.Rank(Input(MoneyRange.Exact(40m), deadline: day.AddDays(-1)), day).Urgent);
Check("no deadline is not urgent", !Ranker.Rank(Input(MoneyRange.Exact(40m)), day).Urgent);

Section("The order a person sees");
Check("a possible match never outranks a likely one, whatever it might be worth",
    Ranker.Compare((Confidence.Likely, false, 1m, 1m), (Confidence.Possible, true, 1_000_000m, 1_000_000m)) < 0);
Check("likely and strong are ranked together, by score",
    Ranker.Compare((Confidence.Likely, false, 600m, 300m), (Confidence.Strong, false, 150m, 75m)) < 0);
Check("urgent goes first within its group",
    Ranker.Compare((Confidence.Strong, true, 10m, 5m), (Confidence.Strong, false, 600m, 300m)) < 0);
Check("then the higher score", Ranker.Compare((Confidence.Strong, false, 600m, 300m), (Confidence.Strong, false, 150m, 75m)) < 0);
Check("then the larger amount", Ranker.Compare((Confidence.Strong, false, 100m, 300m), (Confidence.Strong, false, 100m, 75m)) < 0);
Check("equal findings compare equal", Ranker.Compare((Confidence.Strong, false, 100m, 75m), (Confidence.Strong, false, 100m, 75m)) == 0);
Check("the order is antisymmetric",
    Math.Sign(Ranker.Compare((Confidence.Possible, false, 5m, 5m), (Confidence.Likely, false, 1m, 1m)))
    == -Math.Sign(Ranker.Compare((Confidence.Likely, false, 1m, 1m), (Confidence.Possible, false, 5m, 5m))));

return Finish();
