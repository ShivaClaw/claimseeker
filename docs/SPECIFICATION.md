# ClaimSeeker Specification

How ClaimSeeker works, in the detail needed to build the rest of it.

| | |
|---|---|
| Written | 2026-11-15 |
| Companions | [BLUEPRINT.md](BLUEPRINT.md) for what and why; [ROADMAP.md](ROADMAP.md) for the order of work; [Playbook-Format.md](Playbook-Format.md) and [Autonomy-Contract.md](Autonomy-Contract.md), which are normative for their subjects |
| Code | .NET 10, C#. Ten projects under `src/`, nine console harnesses under `tests/` |

## 0. How to read this document

Every section carries one of three marks.

- **[BUILT]** describes code that exists and is checked offline by the harnesses. If the code and
  this text disagree, one of them is wrong; fix both in one change.
- **[DESIGNED]** is a decision about something not yet built. Build to it, or change this document
  first.
- **[OPEN]** needs Brandon's decision. Each has a recommendation. They are collected in section 12.

Two facts frame everything below. **The engine has never been run against a real source**: every
check so far uses invented records, a scripted browser, or a page served from the local machine.
And **the code has only been built and run on Linux**. Windows and macOS are the targets and are
unproven until the CI workflow runs.

The current count is 981 offline checks. [README.md](../README.md) has the table.

---

## 1. Teardown: what the earlier design and the brief got right and wrong

The project began in 2026 as "ClaimScout", an agent persona and a plan for a personal command-line
tool. The October 2026 brief turned it into a commercial product shaped like CareerSeeker. Both
were read closely before anything was built. The earlier documents are kept, marked as history, in
[docs/legacy](legacy).

**Kept, because it was right**

- *Read the answer from the wire, not the page.* The MissingMoney recipe captures the JSON response
  and ignores the rendered HTML. It is the basis of the playbook format.
- *Search the family name once, nationwide, and filter locally.* Fewest requests to the source, and
  one search covers everyone who shares the name.
- *Name variants are where hidden money is.* Former names, a middle name used as a first name,
  initials, a clerk's misspelling.
- *Possible matches are not confirmed matches.* Now a three-level scale with rules behind it.
- *Filing is always free, and the product says so.*
- *Rank by value against effort.* Kept, with a confidence gate, urgency and source mode added.
- *One session, paced.* Now enforced by the engine instead of trusted to a script.

**Changed, because it was wrong or unsafe**

| Earlier idea | Problem | Now |
|---|---|---|
| "Mediate the negotiations between the parties" | For state unclaimed property there is nobody to negotiate with. You file; the state pays. | Prepare, draft, track. Letters are inquiries to holders, counties and administrators. |
| "Claim filing assistant" that walks through forms | Drifts toward the program filing, and toward holding identifiers. | The engine stops at a packet and a draft. The person files on the official site. |
| "Distinguish confirmed matches from possible" | The engine cannot confirm ownership. Only the holder can. | Strong, likely, possible. No fourth value. |
| "Present the total as the headline" | A total built from guesses is a false promise. | The headline is a floor: stated amounts of trusted matches only. |
| "Search exhaustively, every state" | One nationwide search covers discovery; state sites matter for the claim. | One sweep per family name; state programs as guided links until each has a playbook. |
| Timing and manners left to the script | A recipe can be edited into something aggressive. | Pacing, host limits and the one-attempt rule belong to the runner. |
| "Scour the web" | Unbounded crawling finds junk and scams, and no link from it can be trusted. | A fixed map of official sources. Links come only from the map. |
| "24/7" | Sources refresh slowly; hammering them is rude and useless. | Always on, searching on each source's cadence. |
| Lottery winnings | Prizes are claimed by ticket, not by name. | Out of scope, and the product says so. |
| An email for every finding | Most findings have no email counterparty. | A claim packet for every finding; a letter only where a person will read it. |
| Node and TypeScript command line | Two products, two stacks. | .NET 10, the same as CareerSeeker (decided 2026-11-15). |

## 2. Decisions that amend the brief

| # | Decision | Status |
|---|---|---|
| D1 | The product is **ClaimSeeker** everywhere: binary `claimseeker`, namespace `ClaimSeeker`. | Decided |
| D2 | **.NET 10**, pinned SDK, console harnesses, one verify script, mirroring CareerSeeker's conventions without copying its code. | Decided |
| D3 | **Paid engine, free apps**, sold on the web through a merchant of record; no purchase flow in the apps. | Briefed; details open (O1, O2) |
| D4 | **No model in version 1.** Matching, ranking and letters are deterministic. No provider key, no per-user cost, no generated prose to check. | Built that way; see O7 |
| D5 | **Draft-only, forever.** Version 1 writes `.eml` drafts. Mailbox drafts come later with draft-only permissions. Sending is never built. | Decided |
| D6 | **No identifiers and no documents in version 1.** A document vault is a later, separate decision (O8). | Built |
| D7 | **Consent basis per person**, attested by the account holder. | Built |
| D8 | **Per-source interaction mode**, with guided as the floor that always works. | Built |
| D9 | **Terms are read before a playbook is written**, and the first live run of any source needs Brandon's word. | Rule; not yet exercised |

---

## 3. System overview

### 3.1 Components

| Component | Runs on | Holds | Status |
|---|---|---|---|
| Engine core | The person's computer | Household, findings, searches, drafts, audit log | **[BUILT]** |
| Desktop shell | The person's computer | Scheduler, local dashboard, installer, updater | **[DESIGNED]** section 6 |
| Playbook feed | A static host | Signed playbook files | **[DESIGNED]** 6.4 |
| Licence service | A small web service | Licence ids and their state | **[DESIGNED]** section 8 |
| Relay | A small web service | Encrypted envelopes it cannot read, briefly | **[DESIGNED]** section 7 |
| Phone apps | Android, iPhone | A copy of what the engine chose to send | **[DESIGNED]** section 7 |

### 3.2 The life of a finding

```
household.json --attested--> facts
      |
      v
 plan: one search per family name, per source that has a playbook
      |
      v
 sweep: runner + browser (declared, paced, one attempt) --refused--> handed to the person
      |
      v
 raw records --match--> belongs to someone in the household?  --no--> discarded, never stored
      |                                                         yes
      v
 finding (DISCOVERED) --rank--> RANKED --packet--> PACKET_READY
      |
      v   (only when a letter is asked for)
 DRAFT_PENDING --Attestation Gate--> ATTESTED --> DRAFT_READY --outbox--> DRAFTED
                       |
                       +--> BLOCKED_ATTESTATION (says why, writes nothing)

 The person reports the rest: FILED_BY_USER, IN_REVIEW, NEEDS_DOCUMENTS, PAID, DENIED, DISMISSED
```

### 3.3 Trust boundaries

| Input | Trusted as | Handling |
|---|---|---|
| The household file | The person's own attested statement | Validated strictly; the only source of personal facts |
| A playbook | Reviewed data from the project | Parsed strictly, validated, cannot carry code |
| A source's response | **Untrusted** | Size-limited, cleaned, matched; shown disarmed; never a link, an address or an instruction |
| A page in the browser | **Untrusted** | Host checked before every action; its words are never repeated to the person |
| The phone | A paired device that may be lost | May propose marks; cannot cause a send, a filing or a search for a new person |
| The relay | **Untrusted** | Sees ciphertext and metadata only |

---

## 4. Onboarding and first run

**[BUILT]** as commands: `init` creates a workspace and writes `household.example.json`, an
invented family to edit. `household import <file>` validates the file and stores it whole or not at
all. `household show` lists who will be searched and who will not, with the reason. `plan` shows
every search a sweep would run and touches nothing.

**[DESIGNED]** as a first-run flow in the desktop shell:

1. *Who are you?* Name, former names, other names used.
2. *Where have you lived?* City and state, with rough years. No street address is needed to match.
3. *Who else?* For each person: relationship, names, places, and the basis for searching. The
   choices are consent given, guardian, or next of kin of someone who has died. A person with no
   basis can be listed and is not searched.
4. *Attest.* The account holder confirms the facts are true and that each living adult agreed.
   Nothing is searched before this.
5. *First sweep,* visible, with the browser window shown.
6. *The number.* The floor total, and the list.

**Life events** are the way the household grows after first run, in place of a long form: "a
relative died", "I changed jobs", "I lost a house", "my bank failed". Each adds the facts that a
particular source needs and turns on that source's checklist.

The flow never asks for a Social Security number, a date of birth or an account number.

---

## 5. The engine **[BUILT]**

### 5.1 Projects

Dependencies run downward only. `Core` references nothing.

| Project | References | Role |
|---|---|---|
| `Core` | none | Names, money, property kinds, household, facts, identifiers, untrusted text, findings |
| `Scout` | Core | Variants, planner, schedule, playbook model, parser, validator, runner, result mapper, source map |
| `Browser` | Scout, Microsoft.Playwright | The real browser session. The only project that references a browser library |
| `Scorer` | Core, Scout | Matcher and ranker |
| `Store` | Core, Microsoft.Data.Sqlite | Store interface, two implementations, audit chain |
| `Verifier` | Core | Attestation Gate |
| `Preparer` | Core, Scout, Store, Verifier | Templates, composer, packets, report |
| `Dispatcher` | Core, Verifier | Draft outbox. References no network assembly |
| `Pipeline` | all of the above except Browser | Lifecycle, sweep, case and report services |
| `Engine` | Pipeline, Browser | Command line, workspace, deletion |

### 5.2 Household and attestation

The household is one JSON file (schema in 9.1). It is read strictly: unknown properties are
refused, enums are strings, and the file may be at most 512 KiB.

The validator (`HouseholdValidator`) returns errors, which stop the import, and warnings, which do
not.

- Exactly one person has the relationship `self`. At most 24 people.
- Ids are 1 to 32 lower-case letters, digits or hyphens, and unique.
- Names are at most 80 characters and contain letters. A person's name contains no digits.
- Suffix is one of Jr, Sr, II, III, IV, V.
- Addresses: a city, a two-letter US state or territory code, an optional ZIP (5 digits or ZIP+4),
  optional years between 1900 and next year, in order. At most 12 addresses, former names, aliases
  or employers per person.
- **Consent.** The account holder's basis is `self` and nobody else's is. A person marked deceased
  has `deceasedNextOfKin`, and only such a person does. The account holder cannot be marked
  deceased. A person with `none` is a warning: listed, not searched.
- **Attestation is required**: a statement, who made it, and a date that is not in the future.
- **Identifiers anywhere in the file are an error** (5.2.1).
- A person with no address draws a warning: no match for them can rise above possible.

**Facts.** `HouseholdFacts.Derive` turns an attested household into a list of facts, each with a
stable id: `self/name`, `self/prior-name/0`, `self/alias/0`, `self/address/1`, `self/employer/0`,
`dad/relationship`, `dad/deceased`. A household with no attestation yields no facts. Facts are the
only thing a letter may say about a person (5.15).

#### 5.2.1 Identifiers

`SensitiveIdentifiers.Find` recognises a Social Security number, a taxpayer identification number,
a bank account or card number (12 to 19 digits), and a bare nine-digit number that could be a
Social Security or routing number. Text it cannot check safely is refused as well. It runs on every
string in a household file and on every finished letter. There is no field, prompt or store for an
identifier anywhere in the engine.

### 5.3 Names and variants

`NameText.Normalize` reduces any name to upper-case ASCII letters and digits separated by single
spaces, folding accents, so that every comparison is made in one form. Anything past 200 characters
is cut.

`NameVariants.For(subject)` lists the names a person's money may be filed under. Every variant
comes from what the person typed, except the misspelling pass.

| Kind | Example for Jane Quinn Sample, formerly Example, also Janie |
|---|---|
| FamilyOnly | SAMPLE |
| GivenFamily | JANE SAMPLE |
| InitialFamily | J SAMPLE |
| PriorFamilyOnly | EXAMPLE |
| GivenPriorFamily | JANE EXAMPLE |
| InitialPriorFamily | J EXAMPLE |
| AliasFamily | JANIE SAMPLE |
| MiddleAsGiven | QUINN SAMPLE |
| FamilyPart | each half of a compound or hyphenated family name |
| Misspelling | at most three, from a fixed set of common clerical errors |

### 5.4 Source map and modes

`SourceRegistry.All` has 18 entries. Each records an id, name, operator, mode, status, how it is
queried (by name, by employer, or not at all), the official hosts, an entry address, a cadence in
days, an optional playbook id, a deadline note and a plain description.

- An **entry address is recorded only when it was seen** on the operator's own site or in the
  project's vetted list. Otherwise it is null and the person is given the operator's name.
- `EffectiveMode` treats an auto or assisted source with no playbook as guided.
- `Searchable` is the sources that are auto and have a playbook. Today: MissingMoney.
- Status is `Proven` (this codebase has run it live), `ProvenLegacy` (a recipe exists from the
  earlier notes and has not been re-run here) or `Planned`. No source is `Proven`.

### 5.5 Planning and cadence

`QueryPlanner.PlanSurnameSweep` builds one search per distinct family name across everyone who may
be searched: current names first, then former names, then name parts and misspellings. People with
no consent basis are listed as skipped, with the reason.

A sweep searches by **family name alone**, with no given name and no state. The source returns
every record under the name; the engine decides locally whose they are.

`QueryPlanner.Refine` narrows a sweep whose answer was cut off: one search per person the sweep was
for, by given name, once. A misspelling or name-part sweep is never narrowed, so guesses do not fan
out into more guesses.

`SweepSchedule.IsDue` compares the last time each search ran with the source's cadence. A sweep
runs only what is due unless told `--all`.

### 5.6 Playbooks and the runner

[Playbook-Format.md](Playbook-Format.md) is normative. In short: a playbook is a markdown file
whose first fenced `json` block is a strict header with seven possible step kinds and no way to
run code, set a header or change how the browser identifies itself.

`PlaybookRunner` holds the product's rules, which a playbook cannot turn off: allowed hosts only,
checked before every fill, select and click; waits never shortened; automation must be declared or
the search is not run; one attempt; a failure reported in the engine's words.

`BrowserQueryExecutor` adds the session rules: the playbook's pause between searches, its cap on
searches per session, and deferral of everything left in a session once one search has been turned
away. `FixtureQueryExecutor` replays recorded answers from a folder and never touches the network.

Each search ends as one of: `captured`, `fallback` (handed to the person), `deferred` (left for the
next sweep), `shape_changed` (the answer did not have the expected list: a broken playbook, never
an empty result) or `not_due`.

**Standing down.** A sweep in which a source refused and never answered raises a counter. At two,
the engine stops trying that source and tells the person, until `--retry-blocked`.

### 5.7 The browser driver

`PlaywrightBrowserSession` starts a browser already on the machine: Edge on Windows, then Chrome,
then a Playwright-managed Chromium. The window is shown by default.

It sets no user agent, adds no header, injects no script, registers no request interception and
passes no launch argument. It runs exactly one script in a page, a fixed read of
`navigator.webdriver`. Responses are captured only from the playbook's allowed hosts and only up to
8 MB. `PortalHarness` checks all of this against the source text of the project and against a real
browser talking to a local page.

### 5.8 Reading an answer

`ResultMapper.Map` reads the captured JSON with limits on: about 8 million characters, 5,000
records, 200 characters per field, control and formatting characters removed. A missing list is a
`ResultFormatException`, which the sweep records as `shape_changed`.

Text that came from a source is **data**. `UntrustedText.Defang` rewrites anything link-like for
display (`http://a.example` becomes `hxxp[:]//a[.]example`), and packets escape it for markdown.
Source text never becomes a link, an address, a recipient or a command.

### 5.9 Matching

`Matcher.Match(record, household)` returns the best match among the people who may be searched, or
nothing. **The name gets a record considered; the place decides how far it is trusted.**

The owner text is split into separate owners on `&`, `/`, `;`, `AND` and `OR`, so one owner's given
name is never paired with another's family name. Business words mark a record as not a person's.
Estate words ("ESTATE OF") are noted and removed. A second owner written without a family name
inherits the first owner's.

| Name on the record | City and state match an address | Only the state matches | Neither |
|---|---|---|---|
| Given name and current family name | Strong | Likely | Possible |
| Given name and a former family name | Strong | Likely | Possible |
| Another given name the person has used | Likely | Possible | Possible |
| Middle name used as a first name | Likely | Possible | not a match |
| An initial only | Likely | Possible | not a match |
| Family name alone | Possible | not a match | not a match |
| One part of a compound family name, with the given name | Possible | Possible | not a match |
| A likely misspelling, with the given name | Possible | Possible | not a match |

A middle initial on the record that is not the person's steps a match down one level. A match under
a former name is flagged as needing proof of the name change. Every verdict carries a sentence
saying why, which the person sees.

A record that matches nobody is counted and discarded. It is never stored, logged or shown.

### 5.10 Amounts and kinds

`ValueText.Parse` reads what a source said about value, and never guesses.

- A dollar-signed amount wins. A bare number counts only when the words around it are range words,
  so "100 SHARES" is not a hundred dollars.
- It understands an exact amount, a range, "over" and "under" bands. Anything else is not
  disclosed.
- An amount above $100 million is treated as not disclosed.

`MoneyRange` keeps the low and high bounds. `Floor` is the least the source's words support, and
is the only number that may be added to a total.

`Naupa.Classify` sorts the source's description into a property family: account balance, uncashed
check, court deposit, insurance, mineral proceeds, wages, securities, safe deposit, trust, utility,
retirement, health savings, education savings, miscellaneous, unknown.

### 5.11 Ranking

`Ranker.Rank` gives each finding an expected value, an effort in hours, and a score: expected
value divided by effort. Every number is a prior for sorting. None is shown to the person as money.

**Expected value**

- Not disclosed: 0, so the finding is ordered by effort alone.
- Exact: the amount. A closed range: the midpoint.
- Open-ended ("over $100"): the floor times a multiplier by kind. Securities, insurance and
  retirement 3.0; account balance 2.0; wages 1.5; utility 1.1; everything else 1.0.

**Effort**, in hours

| Component | Hours |
|---|---|
| Base | 0.5 |
| Filed under a former name | + 0.5 |
| The owner has died | + 1.5 |
| Expected value of $1,000 or more (a notary is likely) | + 1.0 |
| The search had to be run by hand | + 0.5 |
| Source mode: assisted | + 0.25 |
| Source mode: guided or draft | + 0.5 |

The $1,000 figure is a working assumption until each state's requirement is recorded (O9).

**Order.** Trusted matches (strong or likely) come before possible ones, always. Within a group,
urgent first (a deadline within 60 days), then score, then the larger expected value.

**Not yet fed.** The ranker accepts a deadline and a "run by hand" flag, and the pipeline does not
supply either yet: findings carry no deadline, so nothing is urgent and nothing expires. That
arrives with the first source that has deadlines (roadmap phase 4).

### 5.12 Store and audit log

`IClaimStore` has two implementations that must behave identically: `InMemoryClaimStore` and
`SqliteClaimStore`. `StoreParityHarness` runs the same script against both and compares every
result. The schema is in 9.3.

- One writer. The engine takes `engine.lock` in the workspace; a second engine is refused.
- Money is stored as integer cents. Times are UTC, ISO 8601.
- A finding's id is `f_` plus 24 hex characters from a hash of the record's content. Two identical
  records in one answer get different ids, because they are two pieces of property.
- A finding seen again is refreshed, not duplicated. The same property seen at a second source is
  linked with `duplicate_of` and counted once.
- A finding that drops out of a source's answer has `missing_since` set. It is kept.
- A state change is compare-and-set: it is written, together with its audit line, only if the case
  is still in the state the caller saw. Whether the move is allowed at all is the lifecycle's rule,
  applied by the pipeline before it asks the store (5.13).

**Audit log.** Every household save, discovery, state change, gate decision, packet, draft and
sweep appends an event. Each event's hash is SHA-256 over its length-prefixed fields and the
previous event's hash. `audit verify` recomputes the chain and requires the sequence to run 1 to n
with no gap. `audit export` writes it out. The log records what happened to the household's own
findings; it never holds a discarded record.

The log shows tampering. It does not prevent it: someone with the file can rewrite the whole chain.
Anchoring the head hash elsewhere is a later option.

### 5.13 Case lifecycle

| From | May move to |
|---|---|
| DISCOVERED | RANKED, DISMISSED |
| RANKED | PACKET_READY, DISMISSED |
| PACKET_READY | DRAFT_PENDING, FILED_BY_USER, DISMISSED, EXPIRED |
| DRAFT_PENDING | ATTESTED, BLOCKED_ATTESTATION |
| BLOCKED_ATTESTATION | DRAFT_PENDING, FILED_BY_USER, DISMISSED, EXPIRED |
| ATTESTED | DRAFT_READY, BLOCKED_ATTESTATION |
| DRAFT_READY | DRAFTED, BLOCKED_ATTESTATION |
| DRAFTED | DRAFT_PENDING, FILED_BY_USER, DISMISSED, EXPIRED |
| FILED_BY_USER | IN_REVIEW, NEEDS_DOCUMENTS, PAID, DENIED |
| IN_REVIEW | NEEDS_DOCUMENTS, PAID, DENIED |
| NEEDS_DOCUMENTS | FILED_BY_USER, PAID, DENIED |
| PAID, DENIED, DISMISSED, EXPIRED | nothing |

Three rules sit on the graph (`Lifecycle.CanTransition`):

1. `DRAFT_READY` has one way in, from `ATTESTED`, and `ATTESTED` one way in, from `DRAFT_PENDING`
   through the gate.
2. `FILED_BY_USER`, `IN_REVIEW`, `NEEDS_DOCUMENTS`, `PAID`, `DENIED` and `DISMISSED` can be entered
   only by the person. The engine cannot say a claim was filed or paid.
3. `RANKED`, `PACKET_READY`, `ATTESTED`, `BLOCKED_ATTESTATION`, `DRAFT_READY`, `DRAFTED` and
   `EXPIRED` can be entered only by the engine: each records work the engine did. `DRAFT_PENDING`
   is open to both, because asking for a letter is the person's act.

### 5.14 Claim packets

`ClaimPacketBuilder.Build` writes one markdown file per finding in `packets/`:

- what was found, in the source's words and as the engine read them;
- how sure the match is, and the sentence explaining why;
- where to claim, with links taken from the source map and never from the record;
- what to have on hand: photo ID; a Social Security number *to enter on the official site only*;
  proof of the old address; proof of a name change or of heirship when the match calls for it;
- the estimated time, with the reasons;
- how to dismiss it if it is not yours;
- the closing line that filing is free and nobody needs a share.

If the record's text contains what looks like a web or email address, the packet adds a caution
and shows the text disarmed.

### 5.15 The Attestation Gate and letters

**Templates.** `LetterTemplates.All` has three, each with fixed wording and named slots:

| Id | For | Signed by |
|---|---|---|
| `holder-inquiry` | A record in the person's own name | That person |
| `heir-inquiry` | A record in the name of a relative who has died | The account holder, as next of kin |
| `claim-follow-up` | A claim already filed; allowed only in FILED_BY_USER, IN_REVIEW or NEEDS_DOCUMENTS | The person who filed |

All three *ask*. None states that the property belongs to the sender. Each says documents will go
"through your secure process, not by email". Nobody signs for a living adult: their letter is
written in their own name for them to send.

**The gate.** `AttestationGate.Evaluate(request, context)` is a pure function. It returns a
`GatePassedDraft` or a list of violations. It passes a letter only if all of these hold:

- the template is itself sound, and the match and the case state meet its minimums;
- every slot is one the template declares, bound once, and used;
- every **fact slot** is filled from an attested fact that exists, is about the right person, is of
  the right kind, and whose value equals the text placed in the letter, word for word;
- every **finding slot** is filled from the named field of that finding, verbatim, and the text is
  inert: nothing link-like, no control characters;
- every **registry slot** equals the source map's value;
- no brace is left unfilled, and the subject is a single line;
- **no identifier** appears anywhere in the finished text;
- every link is on an official host from the source map, and so is any recipient;
- the letter is within its length limit.

Violation codes: `template-invalid`, `confidence-too-low`, `case-state`, `unknown-slot`,
`duplicate-slot`, `fact-not-attested`, `fact-wrong-person`, `fact-wrong-kind`,
`fact-value-mismatch`, `wrong-field`, `finding-field-empty`, `finding-value-mismatch`,
`untrusted-content`, `registry-value-mismatch`, `unfilled-slot`, `unused-slot`, `stray-brace`,
`bad-subject`, `too-long`, `sensitive-identifier`, `link-not-official`, `recipient-not-official`.

`GatePassedDraft` has an internal constructor. No code outside the `Verifier` assembly can make
one, so the outbox cannot be handed a letter that skipped the gate.

A blocked letter moves the case to `BLOCKED_ATTESTATION`, records why, and writes nothing. When
the cause is a missing or mismatched fact, the person fixes the household file, not the letter.
When the cause is the record's own text, such as a holder name that contains a link, no letter is
written for that record; its claim packet still says where to claim.

### 5.16 The draft outbox

`IDraftOutbox` has one method, `CreateDraft(GatePassedDraft)`. `FileDraftOutbox` writes the letter
as `drafts/<finding>-<template>-<hash>.eml`: a plain-text message beginning `X-Unsent: 1`, which
Outlook and Windows Mail open as an editable draft. There is no `To:` line unless the source map
supplied an official address; today it never does, so the person adds the address.

The same letter written twice is one file. The file is written beside its name and moved into
place, so a crash leaves no half letter. Header text cannot be forged through a subject line.

`CaseService.Draft` reaches the outbox from two states only: a case the gate has just made
`DRAFT_READY`, or a filed claim asking for a follow-up. The state is checked again immediately
before the call. A letter interrupted partway is resumed by asking the gate again, never by
trusting the earlier answer.

The `Dispatcher` assembly references no `System.Net` assembly. `DispatcherNoSendHarness` checks
that by reflection, and that the interface has exactly one method.

### 5.17 Report and totals

`FindingsReport.Totals` counts a finding toward the headline only if it is trusted (strong or
likely), open (not dismissed, denied, paid or expired), not a duplicate, and states an amount. It
adds the **floor**. Findings with no stated amount, and possible matches, are counted and named
separately:

> At least $1,889.90 across 10 matches the engine trusts. 1 of them does not state an amount, so it
> adds nothing to that figure. 1 possible match is listed further down and not counted.

The report lists findings in rank order, then searches that were handed to the person, with the
official link for each.

### 5.18 Commands

| Command | Does |
|---|---|
| `init` | Creates the workspace and the example household |
| `household import <file>`, `household show` | Validates and stores; shows who is searched |
| `sources` | The source map |
| `plan` | The searches a sweep would run. Touches nothing |
| `sweep --fixtures <folder>` | Replays recorded answers. Never uses the network |
| `sweep --live` | Searches real sources through the browser. `--headless`, `--all`, `--retry-blocked` |
| `findings`, `report [--out] [--print]` | What was found |
| `packets`, `packet <finding>` | Writes claim packets |
| `draft <finding> [--template <id>]` | Drafts a letter through the gate |
| `mark <finding> <filed, in-review, needs-documents, paid, denied, dismissed>` | The person reports what happened |
| `audit verify`, `audit export [--out]` | The record |
| `doctor` | Checks the workspace, playbooks, record and household |
| `delete-all-data [--confirm "<phrase>"]` | Removes everything |

Exit codes: 0 done, 1 failed, 2 usage. **A sweep with neither `--live` nor `--fixtures` is a usage
error.** There is no default mode, so nothing contacts a source by accident.

### 5.19 Workspace

The workspace defaults to `ClaimSeeker` under the user's local application data folder, or
`--workspace`, or `CLAIMSEEKER_WORKSPACE`. It contains a marker file, `claimseeker.db`,
`engine.lock`, and the folders `packets/`, `drafts/` and `reports/`.

`delete-all-data` needs the exact phrase `delete <full path>`, refuses a folder that is not a
ClaimSeeker workspace, and does not follow links out of it.

---

## 6. The desktop product **[DESIGNED]**

### 6.1 Shell

A tray application that owns the engine process, starts with the computer, and opens a local
dashboard. The dashboard is a local web page served on a loopback port and protected by a
per-session token. It shows the household, the findings in rank order, each packet, drafts
waiting to be read, the checklist of guided sources, the record, and licence state.

The command line remains the engine's real interface. The shell calls the same services; it adds
no capability the command line lacks.

### 6.2 Scheduler

A loop that wakes on a timer, asks each searchable source which searches are due, and runs them
one source at a time. Rules:

- Never more than one browser session at a time.
- Run only when the computer is awake and on mains power or above a battery floor.
- Jitter start times, so a fleet of engines does not arrive at a source together.
- A source on hold stays on hold until the person asks.
- Every run is visible in the record.

### 6.3 Mailbox drafts

Version 1 writes `.eml` files. The next step puts the draft in the person's own drafts folder:

| Mailbox | Mechanism | Permission |
|---|---|---|
| Gmail | Create draft | `gmail.compose` only |
| Outlook and Microsoft 365 | Microsoft Graph, create message in Drafts | The narrowest scope that can create a draft; it is broader than Gmail's and needs its own review |
| Anything else | IMAP append to Drafts | The person's app password, stored in the OS keychain |

Each is a second implementation of `IDraftOutbox`, in its own assembly, with the same single
method. None gets a send call, and each gets its own no-send harness. OAuth tokens live in the OS
keychain (DPAPI on Windows, Keychain on macOS), never in the workspace.

### 6.4 Playbook feed

Playbooks ship with the engine and are also updated from a feed, so that a changed site is one
file to fix and not a new release.

- The feed is a static index plus playbook files, served over HTTPS.
- Each file is signed with a key whose public half is pinned in the engine. An unsigned or
  wrongly signed playbook is ignored and the bundled one stays.
- The engine takes a newer `version` for a `format` it understands, validates it, and records the
  change in the audit log.
- The feed request carries no information about the household.
- A playbook is still only data. The feed cannot deliver code.

### 6.5 Installer and updates

Signed installers for Windows and macOS (Developer ID and notarization on macOS). The Playwright
driver's size is a packaging concern (O6). Updates are signed and verified before they are applied.

### 6.6 Later: a document vault

Scans of ID, a marriage certificate or a death certificate, stored once so that the third claim
takes minutes. This is the first feature that would put sensitive documents on disk, and it is a
separate decision with its own design: encrypted at rest with a key in the OS keychain, never
synced, never sent (O8).

---

## 7. Phone apps and relay **[DESIGNED]**

### 7.1 What the app is

A viewer and an approver for the engine, free to download, with no purchase flow.

**Without an engine** the app is honest about what it is: a demonstration with the invented Sample
family, and the checklist of official sources with their links, which works on its own.

**Paired**, it shows the floor total, the findings in rank order with their packets, new-finding
and deadline alerts, and drafts waiting to be read on the computer. The person can mark a case
filed, paid, denied or dismissed from the phone.

The phone never holds the household file. It cannot add a person, start a search for a new name,
send a letter or file a claim. A mark from the phone is a proposal the engine applies through the
same lifecycle rules as a command typed at the computer.

### 7.2 Pairing

The engine shows a QR code carrying the relay address, a pairing id, a one-time secret and the
engine's public key. The phone scans it, generates its own key pair, and completes the exchange
through the relay. After that every message is encrypted end to end.

### 7.3 Relay

The relay stores and forwards encrypted envelopes. It sees a pairing id, a direction, a sequence
number, a size and a time. It never sees plaintext, never holds a key and cannot forge a message
either end will accept. It has no accounts and no idea who anyone is. Envelopes expire.

The requirements are: authenticated encryption with keys agreed at pairing; sequence numbers with
replay protection; the engine as the authority on every state; a written, versioned wire format
with test vectors that the engine and both apps check against. CareerSeeker already has a protocol
of this kind. ClaimSeeker should adopt it with its own payload kinds and not design a second one
(O4).

### 7.4 Payloads

| Kind | Direction | Content |
|---|---|---|
| `snapshot` | engine to phone | Totals, the findings list, case states, guided checklist state |
| `delta` | engine to phone | A new finding, a state change, a deadline |
| `mark` | phone to engine | A proposed state for one finding |
| `entitlement` | engine to phone | Whether the engine is licensed, so the app can say why it is quiet |

Findings sent to the phone carry what the packet shows: the reported owner, holder, place, amount
band, confidence and state. Source text is sent disarmed.

### 7.5 Alerts

Push notifications say that there is something new and nothing else: "ClaimSeeker has something
new for you." The amount and the names stay inside the encrypted payload, off the lock screen and
out of the push provider's hands.

---

## 8. Licence **[DESIGNED]**

### 8.1 States

| State | Paid features | How it arises |
|---|---|---|
| None | off | A fresh install. The free path |
| Trial | on until it ends | Starts on first run, no card |
| Paid | on until expiry plus a grace period | A token, renewed by the engine |
| Perpetual | on | A token with no expiry, from the one-time purchase |
| Lapsed | off | A trial or paid token past its end. Derived, never written |

Nothing already found is ever hidden or deleted when a licence lapses. The findings, the packets
already written, export, the record and delete-all-data always work.

### 8.2 Token

A small signed statement, verified offline against a public key pinned in the engine. It carries
a format version, an opaque licence id, a kind (trial, paid or perpetual), when it was issued, when
it ends (absent for perpetual), the id of the signing key so keys can rotate, and an ECDSA P-256
signature. The exact format should be CareerSeeker's, so one service can serve both products.

It contains no personal data. The licence id is the only identifier the engine ever sends
anywhere. A token copied to a second computer works there; that is accepted in exchange for never
fingerprinting a machine.

### 8.3 What is gated

One place: the services behind the scheduler, packets, drafts, tracking reminders and pairing ask
the licence state before they act. The gate, the lifecycle, the store and the record do not change
with licence state, and no check is weaker on the free path.

### 8.4 Renewal

The engine asks the licence service for a fresh token on a schedule, sending the licence id and
nothing else. If the service cannot be reached the token stays good for a grace period. An engine
with no token, or a perpetual one, never calls.

### 8.5 Sales

On the website, through a merchant of record. The apps contain no purchase flow and no link to
one. Sharing the licence service with CareerSeeker is the cheapest path (O4).

---

## 9. Schemas

### 9.1 Household file **[BUILT]**

```json
{
  "version": 1,
  "attestation": {
    "statement": "These facts are true to the best of my knowledge, and each living adult listed here with a consent basis has agreed that I may search on their behalf.",
    "attestedBy": "Jane Quinn Sample",
    "attestedOn": "2026-11-15"
  },
  "subjects": [
    {
      "id": "self",
      "relationship": "self",
      "givenName": "Jane",
      "middleNames": ["Quinn"],
      "familyName": "Sample",
      "priorFamilyNames": ["Example"],
      "aliases": ["Janie"],
      "consent": "self",
      "addresses": [
        { "city": "Springfield", "state": "IL", "zip": "62701", "fromYear": 2019 },
        { "city": "Columbus", "state": "OH", "fromYear": 2011, "toYear": 2019 }
      ],
      "employers": ["Acme Widgets"]
    },
    {
      "id": "dad",
      "relationship": "parent",
      "givenName": "Robert",
      "familyName": "Sample",
      "deceased": true,
      "consent": "deceasedNextOfKin",
      "addresses": [ { "city": "Dayton", "state": "OH" } ]
    }
  ]
}
```

`consent` is one of `none`, `self`, `consentGiven`, `guardian`, `deceasedNextOfKin`. Other subject
properties: `suffix`, and `street` on an address. Unknown properties are refused.

### 9.2 Playbook **[BUILT]**

See [Playbook-Format.md](Playbook-Format.md).

### 9.3 Store **[BUILT]**

SQLite, schema version 1 in `PRAGMA user_version`, write-ahead logging, connection pooling off.

| Table | Holds |
|---|---|
| `household` | One row: the household JSON and when it was saved |
| `findings` | One row per finding: subject, source, the record's fields, value in cents, kind, confidence, the name match and its explanation, flags for name-change and estate, the search that found it, case state, rank, first and last seen, missing-since, packet path |
| `searches` | One row per search run: source, name searched, kind, mode, outcome, reason, items seen, matched, whether cut off, when |
| `drafts` | One row per letter written: finding, template, path, hash, when |
| `events` | The audit log: sequence, time, actor, kind, entity, payload, previous hash, hash |
| `config` | Small engine settings, such as a source's refusal counter |

`confidence` and `case_state` carry CHECK constraints built from the enums. A schema change is a
migration to version 2, never an edit to version 1.

The `searches` table holds the names the engine searched for the household. It holds no record
that did not match.

### 9.4 Audit events **[BUILT]**

| Kind | Actor | When |
|---|---|---|
| `household_saved` | user | A household file is imported |
| `finding_discovered` | engine | A record matched and was stored |
| `state_change` | engine or user | Any lifecycle move, with from and to |
| `packet_written` | engine | A claim packet was written |
| `gate_decision` | engine | The gate passed or blocked a letter, with the violation codes |
| `draft_created` | engine | A letter was written to the outbox, with its hash |
| `sweep_completed` | engine | Counts for one source's sweep, including how many records were discarded |

### 9.5 Draft file **[BUILT]**

```
X-Unsent: 1
MIME-Version: 1.0
Subject: Inquiry about unclaimed property reported for EXAMPLE JANE
Content-Type: text/plain; charset=utf-8
Content-Transfer-Encoding: 8bit

Hello,
...
```

Lines end CRLF. A non-ASCII subject is encoded as RFC 2047 words. A body with a line over 998
bytes is sent base64.

### 9.6 Licence token and relay payloads **[DESIGNED]**

Sections 8.2 and 7.4. Both should reuse the formats CareerSeeker already has (O4).

---

## 10. Security, privacy and compliance

### 10.1 What is stored, and where

| Data | Where | Leaves the machine? |
|---|---|---|
| Household: names, former names, cities and states, relationships, employers | Workspace database | A family name goes to the official source being searched, and a given name when a cut-off answer is narrowed. Nothing else |
| Findings, packets, drafts, record | Workspace | To the paired phone, encrypted, once pairing exists |
| Identifiers | **Nowhere** | Never |
| Records about other people | **Nowhere** | Never |
| Licence token | Workspace | The licence id goes to the licence service |

The workspace is not encrypted by the engine today. It relies on the operating system's account
protection and disk encryption (O5).

### 10.2 Threats and answers

| Threat | Answer |
|---|---|
| A hostile record: a holder name that is a link or an instruction | Disarmed for display; never a link, recipient or command; a caution in the packet |
| A hostile page: a redirect to a look-alike host | Host checked before every action; nothing is typed off the allowed hosts |
| A hostile or mistaken playbook | Strict parsing; closed step list; allowed hosts; engine-owned pacing; signed feed |
| A letter that says something untrue or leaks an identifier | The gate; fixed templates; identifier scan on the finished text |
| An agent or a developer adding a send path | One-method interface; no network reference in the assembly; a harness that fails if either changes |
| An agent weakening a rule to make a test pass | Mutation-checked harnesses; the pinned total; the invariants in CLAUDE.md |
| The engine being used to fish for someone else's money | Consent basis per person; attestation; no bulk import of names |
| A lost phone | It holds no household file; unpair from the engine; nothing can be sent or filed from it |
| A compromised relay | It has ciphertext only |
| A tampered record | The hash chain shows it |
| A dependency with a known hole | Locked restore, pinned SDK, a vulnerability gate in CI, actions pinned by commit |

### 10.3 Compliance rules that are also code

- No fee language except "filing is free".
- No statement that a record belongs to the person.
- No submission, signing or payment.
- Terms read and recorded before a playbook exists.
- Consent basis per person.

Legal review before launch covers: finder and heir-finder statutes by state, each automated
source's terms, the privacy policy, and the store listings.

---

## 11. What is new here

- **The Attestation Gate.** A letter is assembled only from facts the person attested and words
  the source reported, checked by a pure function, with the result unforgeable by construction.
- **Declared automation as a rule of the runner.** The engine checks its own browser for honesty
  and refuses to search through one that hides.
- **Playbooks that cannot misbehave.** A closed vocabulary with the manners owned by the engine.
- **Discard by default.** A family-name sweep that never stores a stranger.
- **Floor totals.** A headline that can only be exceeded.
- **User-only states.** The engine is structurally unable to claim that a claim was filed or paid.

---

## 12. Open decisions

Each needs Brandon's word. The recommendation is what the documents assume until then.

| # | Question | Recommendation |
|---|---|---|
| O1 | What is free and what is paid? | Free: install, household, sweep by hand, every finding with its official claim link, guided checklist, export, delete. Paid: scheduled sweeps, packets, drafts, tracking, phone pairing and alerts. Never charge to reveal a finding |
| O2 | One-time fee or subscription, and the prices? | Annual household licence plus a one-time perpetual option at about three times the annual price. Test $59 a year, $9.99 a month, $149 once |
| O3 | May the first live sweep of MissingMoney run? | Not until its terms of use and robots policy have been read and recorded in the playbook. Then one visible, supervised run with the example family replaced by Brandon's own household |
| O4 | Share a platform with CareerSeeker (relay protocol, licence service, app shell), or build ClaimSeeker's own? | Share. Adopt the sync protocol and the licence token as they are, and extract the common shell when the second product needs it |
| O5 | Encrypt the workspace database at rest? | Yes before any public build: SQLCipher or an encrypted container, key in the OS keychain. Not needed for a private alpha on an encrypted disk |
| O6 | Keep Playwright, whose driver adds roughly 135 MB per platform, or move to a smaller browser driver? | Keep it through the first live proof. Decide at packaging time, behind the existing `IBrowserSession` seam |
| O7 | Is there a role for a model later? | Possibly for reading county surplus lists published as PDF or spreadsheet. Only as extraction of untrusted data that is then matched by the same deterministic rules. Never for letters |
| O8 | A document vault? | Later, as its own design. Not in version 1 |
| O9 | Per-state claim requirements (notary thresholds, forms)? | Record them as data when state programs get playbooks; until then the packet says the program's own form is the authority |
| O10 | County surplus: launch feature or fast follow? | Fast follow. It is the strongest differentiator and the most work; do not hold the first release for it |
| O11 | Class actions in version 1? | Guided list only. No matching against a person's purchases, no prefilled claim forms |
| O12 | Is the repository public or private? | Private. It was made public only to unblock access during setup |
| O13 | The Claude Project's instruction 4 still says "mediate the negotiations" | Change it to "prepare the claim and any letters, and track each case until it is paid" |

---

## 13. Build order

[ROADMAP.md](ROADMAP.md).
