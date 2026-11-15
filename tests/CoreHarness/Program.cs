// CoreHarness: the domain vocabulary. Names, amounts, property families, the household file and
// its validation, attested facts, and the two text rules everything else leans on: no government or
// financial identifier is ever accepted, and text from a source is never allowed to act.
using ClaimSeeker.Core;
using static Harness;

Title("ClaimSeeker core vocabulary");
var today = new DateOnly(2026, 11, 15);

// ---------- names ----------
Section("Names are compared in one normal form");
Equal("apostrophes join a name", NameText.Normalize("O'Brien"), "OBRIEN");
Equal("typographic apostrophes join it too", NameText.Normalize("O’Brien"), "OBRIEN");
Equal("accents fold to plain letters", NameText.Normalize("Peña"), "PENA");
Equal("sharp s unfolds", NameText.Normalize("Strauß"), "STRAUSS");
Equal("a hyphen separates words", NameText.Normalize("Smith-Jones"), "SMITH JONES");
Equal("periods vanish and spaces collapse", NameText.Normalize("  St.  John ,Jr. "), "ST JOHN JR");
Equal("compact form runs a split name together", NameText.Compact("Mc Donald"), "MCDONALD");
Equal("empty in, empty out", NameText.Normalize(null), "");
Equal("only punctuation is empty", NameText.Normalize("--- ,,"), "");
Check("an over-long name is cut, not trusted", NameText.Normalize(new string('a', 500)).Length == NameText.MaxLength);
Equal("tokens split on the normal form", string.Join("|", NameText.Tokens("de la Cruz-Ortiz")), "DE|LA|CRUZ|ORTIZ");
Equal("initial comes from the normal form", NameText.Initial("érica"), 'E');

// ---------- amounts ----------
Section("A source's value text is read as stated, never guessed");
Equal("exact amount", ValueText.Parse("$1,234.56"), MoneyRange.Exact(1234.56m));
Equal("exact amount without cents", ValueText.Parse("$40"), MoneyRange.Exact(40m));
Equal("a range with TO", ValueText.Parse("$50 TO $100"), MoneyRange.Between(50m, 100m));
Equal("a range with a dash", ValueText.Parse("$50 - $100"), MoneyRange.Between(50m, 100m));
Equal("a range given backwards is put in order", ValueText.Parse("$100 to $50"), MoneyRange.Between(50m, 100m));
Equal("OVER is a floor", ValueText.Parse("OVER $100"), MoneyRange.AtLeast(100m));
Equal("OR MORE is a floor", ValueText.Parse("$100 or more"), MoneyRange.AtLeast(100m));
Equal("a plus sign is a floor", ValueText.Parse("$250+"), MoneyRange.AtLeast(250m));
Equal("UNDER is a ceiling", ValueText.Parse("UNDER $50"), MoneyRange.UpTo(50m));
Equal("LESS THAN is a ceiling", ValueText.Parse("Less than $25"), MoneyRange.UpTo(25m));
Equal("a bare number with only range words is money", ValueText.Parse("OVER 100"), MoneyRange.AtLeast(100m));
Equal("a bare number alone is money", ValueText.Parse("125.50"), MoneyRange.Exact(125.50m));
Equal("a count is not money", ValueText.Parse("100 SHARES"), MoneyRange.Undisclosed);
Equal("a count beside an amount is ignored", ValueText.Parse("1 PROPERTY OVER $100"), MoneyRange.AtLeast(100m));
Equal("UNDISCLOSED is undisclosed", ValueText.Parse("UNDISCLOSED"), MoneyRange.Undisclosed);
Equal("empty is undisclosed", ValueText.Parse("  "), MoneyRange.Undisclosed);
Equal("null is undisclosed", ValueText.Parse(null), MoneyRange.Undisclosed);
Equal("words alone are undisclosed", ValueText.Parse("see holder"), MoneyRange.Undisclosed);
Equal("an implausible amount is noise", ValueText.Parse("$999,999,999,999"), MoneyRange.Undisclosed);
Equal("text past the length cap is not read", ValueText.Parse(new string('x', 300) + " $500"), MoneyRange.Undisclosed);

Section("Only a stated amount can be added to a total");
Equal("an exact amount's floor is itself", MoneyRange.Exact(40m).Floor, 40m);
Equal("an open-ended amount's floor is its stated floor", MoneyRange.AtLeast(100m).Floor, 100m);
Equal("a ceiling adds nothing", MoneyRange.UpTo(50m).Floor, 0m);
Equal("an undisclosed amount adds nothing", MoneyRange.Undisclosed.Floor, 0m);
Equal("exact reads as dollars", MoneyRange.Exact(1250m).Display(), "$1,250");
Equal("cents are kept when there are any", MoneyRange.Exact(312.4m).Display(), "$312.40");
Equal("a floor reads as over", MoneyRange.AtLeast(100m).Display(), "over $100");
Equal("a ceiling reads as under", MoneyRange.UpTo(50m).Display(), "under $50");
Equal("a range reads as a range", MoneyRange.Between(50m, 100m).Display(), "$50–$100");
Equal("undisclosed says so", MoneyRange.Undisclosed.Display(), "amount not disclosed");
Equal("stored bounds rebuild a floor", MoneyRange.FromBounds(100m, null), MoneyRange.AtLeast(100m));
Equal("stored nulls rebuild undisclosed", MoneyRange.FromBounds(null, null), MoneyRange.Undisclosed);

// ---------- property families ----------
Section("Property kinds follow the NAUPA code families");
Equal("AC is an account balance", Naupa.FamilyFromCode("AC01"), PropertyFamily.AccountBalance);
Equal("CK is an uncashed check", Naupa.FamilyFromCode("ck13"), PropertyFamily.UncashedCheck);
Equal("MS01 is wages", Naupa.FamilyFromCode("MS01"), PropertyFamily.Wages);
Equal("other MS codes are miscellaneous", Naupa.FamilyFromCode("MS05"), PropertyFamily.Miscellaneous);
Equal("SC is securities", Naupa.FamilyFromCode("SC08"), PropertyFamily.Securities);
Equal("an unknown prefix is unknown", Naupa.FamilyFromCode("ZZ99"), PropertyFamily.Unknown);
Equal("two letters alone are not a code", Naupa.FamilyFromCode("AC"), PropertyFamily.Unknown);
Equal("a leading code wins over the words", Naupa.Classify("IN05 PREMIUM REFUNDS"), PropertyFamily.Insurance);
Equal("a utility deposit is a utility refund", Naupa.Classify("UTILITY DEPOSIT REFUND"), PropertyFamily.Utility);
Equal("a security deposit is not a security", Naupa.Classify("Security Deposit"), PropertyFamily.AccountBalance);
Equal("a payroll check is wages", Naupa.Classify("PAYROLL CHECK"), PropertyFamily.Wages);
Equal("a pension check is a check", Naupa.Classify("PENSION CHECKS"), PropertyFamily.UncashedCheck);
Equal("an IRA is retirement", Naupa.Classify("Traditional IRA"), PropertyFamily.Retirement);
Equal("a 401(k) is retirement", Naupa.Classify("401(k) balance"), PropertyFamily.Retirement);
Equal("IRA inside another word is not retirement", Naupa.Classify("MIRACLE PRODUCTS CREDIT BALANCE"), PropertyFamily.Miscellaneous);
Equal("dividends are securities", Naupa.Classify("DIVIDENDS"), PropertyFamily.Securities);
Equal("a death benefit is insurance", Naupa.Classify("DEATH BENEFIT"), PropertyFamily.Insurance);
Equal("a safe deposit box is tangible property", Naupa.Classify("SAFE DEPOSIT BOX CONTENTS"), PropertyFamily.SafeDeposit);
Equal("a savings account is an account balance", Naupa.Classify("SAVINGS ACCOUNT"), PropertyFamily.AccountBalance);
Equal("a vendor check is a check", Naupa.Classify("VENDOR CHECK"), PropertyFamily.UncashedCheck);
Equal("an escrow refund is trust property", Naupa.Classify("ESCROW REFUND"), PropertyFamily.Trust);
Equal("a customer refund is miscellaneous", Naupa.Classify("CUSTOMER REFUND"), PropertyFamily.Miscellaneous);
Equal("a gas bill credit is not mineral proceeds", Naupa.Classify("NATURAL GAS BILL CREDIT BALANCE"), PropertyFamily.Miscellaneous);
Equal("royalties are mineral proceeds", Naupa.Classify("OIL AND GAS ROYALTIES"), PropertyFamily.MineralProceeds);
Equal("unrecognised text is unknown", Naupa.Classify("SUNDRY"), PropertyFamily.Unknown);
Equal("empty text is unknown", Naupa.Classify(""), PropertyFamily.Unknown);
Check("every family has plain words", Enum.GetValues<PropertyFamily>().All(f => Naupa.Describe(f).Length > 0));

// ---------- household file ----------
Section("The household file is read strictly");
var example = SampleHousehold.Load();
Equal("the sample household has three people", example.Subjects.Count, 3);
Equal("the account holder is found", example.Self?.Id, "self");
Equal("full name includes the middle name", example.Find("self")!.FullName, "Jane Quinn Sample");
Equal("states lived are distinct and upper case", string.Join(",", example.Find("self")!.StatesLived), "IL,OH");
Check("a person with no consent basis may not be searched", !example.Find("alex")!.MaySearch);
Check("round trip keeps the household", HouseholdJson.Serialize(HouseholdJson.Parse(HouseholdJson.Serialize(example))) == HouseholdJson.Serialize(example));
Check("an unknown property is an error, not ignored",
    Throws<HouseholdFormatException>(() => HouseholdJson.Parse(SampleHousehold.Json.Replace("\"aliases\"", "\"alaises\""))));
Check("a missing required field is an error",
    Throws<HouseholdFormatException>(() => HouseholdJson.Parse("""{ "subjects": [ { "id": "self", "relationship": "self", "givenName": "Jane" } ] }""")));
Check("a relationship given as a number is an error",
    Throws<HouseholdFormatException>(() => HouseholdJson.Parse(SampleHousehold.Json.Replace("\"relationship\": \"self\"", "\"relationship\": 0"))));
Check("a file that is not JSON is an error", Throws<HouseholdFormatException>(() => HouseholdJson.Parse("not json")));
Check("an over-size file is refused", Throws<HouseholdFormatException>(() => HouseholdJson.Parse(new string(' ', HouseholdJson.MaxBytes + 1))));

Section("A household is checked before the engine acts on it");
var valid = HouseholdValidator.Validate(example, today);
Check("the sample household is valid", valid.Ok, string.Join(" ", valid.Errors));
Check("a person with no consent basis is a warning, not an error", valid.Warnings.Any(w => w.StartsWith("alex:", StringComparison.Ordinal)));

IReadOnlyList<string> ErrorsFor(Func<Household, Household> change) => HouseholdValidator.Validate(change(example), today).Errors;
Household WithSelf(Func<Subject, Subject> change) =>
    example with { Subjects = example.Subjects.Select(s => s.Id == "self" ? change(s) : s).ToList() };
Household WithDad(Func<Subject, Subject> change) =>
    example with { Subjects = example.Subjects.Select(s => s.Id == "dad" ? change(s) : s).ToList() };

Check("no attestation is an error", ErrorsFor(h => h with { Attestation = null }).Count == 1);
Check("an attestation dated in the future is an error",
    ErrorsFor(h => h with { Attestation = h.Attestation! with { AttestedOn = "2030-01-01" } }).Count == 1);
Check("an attestation with a malformed date is an error",
    ErrorsFor(h => h with { Attestation = h.Attestation! with { AttestedOn = "11/15/2026" } }).Count == 1);
Check("an empty household is an error", ErrorsFor(h => h with { Subjects = [] }).Count >= 1);
Check("two account holders is an error", ErrorsFor(_ => WithDad(s => s with { Relationship = Relationship.Self, Deceased = false, Consent = ConsentBasis.Self })).Count >= 1);
Check("a duplicate id is an error", ErrorsFor(_ => WithDad(s => s with { Id = "self" })).Any(e => e.Contains("more than once", StringComparison.Ordinal)));
Check("an id with capitals or spaces is an error", ErrorsFor(_ => WithDad(s => s with { Id = "My Dad" })).Count >= 1);
Check("an unknown state code is an error",
    ErrorsFor(_ => WithSelf(s => s with { Addresses = [new PostalAddress { City = "Springfield", State = "ZZ" }] })).Count == 1);
Check("a malformed ZIP is an error",
    ErrorsFor(_ => WithSelf(s => s with { Addresses = [new PostalAddress { City = "Springfield", State = "IL", Zip = "6270" }] })).Count == 1);
Check("ZIP+4 is accepted",
    ErrorsFor(_ => WithSelf(s => s with { Addresses = [new PostalAddress { City = "Springfield", State = "IL", Zip = "62701-1234" }] })).Count == 0);
Check("years in the wrong order is an error",
    ErrorsFor(_ => WithSelf(s => s with { Addresses = [new PostalAddress { City = "Springfield", State = "IL", FromYear = 2020, ToYear = 2010 }] })).Count == 1);
Check("a name with digits is an error", ErrorsFor(_ => WithSelf(s => s with { GivenName = "Jane2" })).Count == 1);
Check("an empty family name is an error", ErrorsFor(_ => WithSelf(s => s with { FamilyName = " " })).Count == 1);
Check("an unknown suffix is an error", ErrorsFor(_ => WithSelf(s => s with { Suffix = "Esq" })).Count == 1);
Check("a known suffix is accepted", ErrorsFor(_ => WithSelf(s => s with { Suffix = "Jr." })).Count == 0);
Check("the account holder's basis must be self", ErrorsFor(_ => WithSelf(s => s with { Consent = ConsentBasis.ConsentGiven })).Count == 1);
Check("the account holder cannot be deceased", ErrorsFor(_ => WithSelf(s => s with { Deceased = true })).Count >= 1);
Check("a deceased person needs the next-of-kin basis", ErrorsFor(_ => WithDad(s => s with { Consent = ConsentBasis.ConsentGiven })).Count == 1);
Check("the next-of-kin basis is only for the deceased", ErrorsFor(_ => WithDad(s => s with { Deceased = false })).Count == 1);
Check("only the account holder may use the self basis", ErrorsFor(_ => WithDad(s => s with { Deceased = false, Consent = ConsentBasis.Self })).Count == 1);
Check("a Social Security number anywhere in the file is an error",
    ErrorsFor(_ => WithSelf(s => s with { Employers = ["Acme 123-45-6789"] })).Any(e => e.Contains("Social Security", StringComparison.Ordinal)));
Check("an account number in the attestation is an error",
    ErrorsFor(h => h with { Attestation = h.Attestation! with { Statement = "Account 4111 1111 1111 1111 is mine." } }).Count == 1);
Check("no addresses is a warning that caps the match", HouseholdValidator.Validate(WithDad(s => s with { Addresses = [] }), today).Warnings.Any(w => w.StartsWith("dad:", StringComparison.Ordinal)));

// ---------- attested facts ----------
Section("Facts exist only once the household is attested");
var facts = HouseholdFacts.Derive(example);
Check("an unattested household has no facts", HouseholdFacts.Derive(example with { Attestation = null }).Count == 0);
Equal("the account holder's name is a fact", facts.Single(f => f.Id == "self/name").Value, "Jane Quinn Sample");
Equal("a former name is a fact", facts.Single(f => f.Id == "self/prior-name/0").Value, "Jane Example");
Equal("an address is a fact, as city and state", facts.Single(f => f.Id == "self/address/1").Value, "Columbus, OH");
Equal("a relationship is a fact", facts.Single(f => f.Id == "dad/relationship").Value, "my parent");
Check("being deceased is a fact only for the deceased", facts.Any(f => f.Id == "dad/deceased") && !facts.Any(f => f.Id == "self/deceased"));
Check("fact ids are unique", facts.Select(f => f.Id).Distinct().Count() == facts.Count);
Check("every fact names a person in the household", facts.All(f => example.Find(f.SubjectId) is not null));

// ---------- identifiers ----------
Section("Government and financial identifiers are recognised and refused");
Equal("SSN with hyphens", SensitiveIdentifiers.Find("my number is 123-45-6789"), "Social Security number");
Equal("SSN with spaces", SensitiveIdentifiers.Find("123 45 6789"), "Social Security number");
Equal("nine digits in a row", SensitiveIdentifiers.Find("ref 123456789"), "Social Security or bank routing number");
Equal("an employer id", SensitiveIdentifiers.Find("EIN 12-3456789"), "taxpayer identification number");
Equal("a card number in groups", SensitiveIdentifiers.Find("4111 1111 1111 1111"), "bank account or card number");
Equal("a long account number", SensitiveIdentifiers.Find("acct 000123456789012"), "bank account or card number");
Check("a phone number is not an identifier", SensitiveIdentifiers.IsClean("call 303-555-0100"));
Check("a ZIP+4 is not an identifier", SensitiveIdentifiers.IsClean("Springfield, IL 62701-1234"));
Check("a year range is not an identifier", SensitiveIdentifiers.IsClean("lived there 2011-2019"));
Check("a dollar amount is not an identifier", SensitiveIdentifiers.IsClean("$1,250.00 reported"));
Check("empty text is clean", SensitiveIdentifiers.IsClean(""));
Check("text too long to scan in full is refused", !SensitiveIdentifiers.IsClean(new string('a', 60_000)));

// ---------- untrusted text ----------
Section("Text from a source is shown, never allowed to act");
Check("a plain holder name is inert", UntrustedText.IsInert("PRAIRIE STATE ELECTRIC COOP"));
Check("an ampersand is inert", UntrustedText.IsInert("AT&T MOBILITY"));
Check("a web address is not inert", !UntrustedText.IsInert("PAY AT http://refunds.example/claim"));
Check("a bare www address is not inert", !UntrustedText.IsInert("SEE WWW.EXAMPLE.COM"));
Check("an email address is not inert", !UntrustedText.IsInert("write claims@example.com"));
Check("markup is not inert", !UntrustedText.IsInert("<b>ACME</b>"));
Check("a brace is not inert", !UntrustedText.IsInert("ACME {sender_name}"));
Check("a control character is not inert", !UntrustedText.IsInert("ACME\nBcc: someone"));
Check("over-long text is not inert", !UntrustedText.IsInert(new string('A', 201)));
Equal("a web address is defanged", UntrustedText.Defang("PAY AT http://refunds.example/claim"), "PAY AT hxxp[:]//refunds[.]example/claim");
Equal("upper-case HTTP is defanged too", UntrustedText.Defang("HTTPS://X.EXAMPLE"), "HXXPS[:]//X[.]EXAMPLE");
Equal("an email address is defanged", UntrustedText.Defang("claims@example.com"), "claims[at]example[.]com");
Equal("ordinary text is left exactly as it was", UntrustedText.Defang("ST. LOUIS GAS CO."), "ST. LOUIS GAS CO.");
Check("defanged text no longer reads as a web address", !UntrustedText.Defang("http://a.example").Contains("://", StringComparison.Ordinal));

// ---------- finding identity ----------
Section("A finding's identity is stable and does not merge distinct records");
var check = new RawRecord("missingmoney", null, "SAMPLE JANE", "HEARTLAND GROCERS", "SPRINGFIELD", "IL", "$18.75", "VENDOR CHECK");
Equal("the same record gives the same id", FindingKeys.FindingId(check, 0), FindingKeys.FindingId(check with { }, 0));
Check("an id is a short lower-case token", FindingKeys.FindingId(check, 0) is { Length: 26 } id && id.StartsWith("f_", StringComparison.Ordinal) && id == id.ToLowerInvariant());
Check("two identical rows are two findings", FindingKeys.FindingId(check, 0) != FindingKeys.FindingId(check, 1));
Check("case and spacing do not change the id", FindingKeys.FindingId(check, 0) == FindingKeys.FindingId(check with { OwnerName = "Sample   Jane" }, 0));
Check("a different amount is a different finding", FindingKeys.FindingId(check, 0) != FindingKeys.FindingId(check with { ValueText = "$19.75" }, 0));
var withId = check with { SourceRecordId = "P-1001" };
Check("a source's own record id decides the id", FindingKeys.FindingId(withId, 0) == FindingKeys.FindingId(withId with { ValueText = "changed" }, 5));
Check("the same property through two sources shares a dedupe key", FindingKeys.DedupeKey(check) == FindingKeys.DedupeKey(check with { SourceId = "il-treasurer" }));
Check("a field cannot bleed into its neighbour", FindingKeys.DedupeKey(check with { OwnerName = "SAMPLE", HolderName = "JANE HEARTLAND GROCERS" }) != FindingKeys.DedupeKey(check));

Section("Case states read as plain words");
Check("every case state has words", Enum.GetValues<CaseState>().All(s => CaseStates.Words(s) != s.ToString()));
Equal("stored names map to the same words", CaseStates.Words("FILED_BY_USER"), "filed by you");

return Finish();
