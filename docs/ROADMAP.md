# ClaimSeeker Roadmap

The order of work, what finishes each phase, and what has to be decided before it starts.

| | |
|---|---|
| Written | 2026-11-15. Updated 2026-11-27 after the first CI run and the reading of MissingMoney's rules |
| Replaces | The ClaimScout roadmap of 2026 |
| Companions | [BLUEPRINT.md](BLUEPRINT.md), [SPECIFICATION.md](SPECIFICATION.md), [ClaimSeeker-Project-Summary.md](ClaimSeeker-Project-Summary.md) for today's status |

## Where things stand

Phases 0 and 1 are done: the repository conventions, the three handoff documents, and an engine
core that runs the whole path from a household file to an unsent letter on invented records. It is
checked by 981 offline checks, which pass on Linux, Windows and macOS. The engine has never been
run against a real source.

Phase 2 was to be one supervised live sweep of MissingMoney. It did not happen, and as planned it
cannot: the site's published agent policy, read on 2026-11-26, does not allow an unattended search.
Phase 2 below has been rewritten around that. [Next-Session-Handoff.md](Next-Session-Handoff.md)
is the short version of what to do first.

## Gates

A gate is something only Brandon can open. Work behind a closed gate does not start.

| Gate | What it is | Blocks | Standing on 2026-11-27 |
|---|---|---|---|
| **G1** | The Claude GitHub app can push to the repository | Any push, any pull request, CI | **Open.** Access was granted on 2026-11-26, and the branch was pushed that day. Brandon chose to leave the repository public for now |
| **G2** | MissingMoney's rules for automated access have been read, and what they allow is recorded in `playbooks/missingmoney.md` | Phase 2 | **Done, 2026-11-26.** An unattended search is not allowed. A search a person confirms is |
| **G3** | Brandon says a live search may run, on his machine, for his own household | Phase 2 | **Closed.** Brandon said yes on 2026-11-26, before the rules were read. Nothing was run. Ask again once the engine can run a search with a person at the controls |
| **G4** | What is free, what is paid, and the price (specification O1, O2) | Phase 5 | **Deferred** by Brandon, 2026-11-26 |
| **G5** | Share the relay protocol, licence service and app shell with CareerSeeker, or not (O4) | Phases 5 and 6 | **Delegated** by Brandon, 2026-11-26, to the coding session that starts phase 5 or 6 |
| **G6** | Legal review: finder statutes by state, source terms, privacy policy, store listings | Phase 9 | Not started |
| **G7** | May the engine name itself to a site that asks agents to (specification O14)? This changes invariant 4 | Any assisted search of MissingMoney | New. Brandon's decision |
| **G8** | How to read "results must not be kept beyond the session" (specification O15): a legal reading, and ideally the operator's own answer | Storing anything found on MissingMoney | New. Brandon's decision |

### What G2 found

MissingMoney publishes a policy for AI agents. In short: an agent may search for an identified
person, at most five times a minute, but a person has to confirm each search and complete the bot
check; the agent has to name itself; and results are not to be kept beyond the session or shared
beyond the person who asked. `playbooks/missingmoney.md` has the full reading.

So MissingMoney is an **assisted** source, not an automatic one. The engine does not know that
yet: its source map and the playbook header still say `auto`. Until the engine is changed, the only
thing preventing an unattended run is the rule in `AGENTS.md`. The product still works without
automation here: a person can always run the search by hand.

## How a phase ends

Every phase ends the same way, and none ends on "should work".

1. `pwsh scripts/Verify.ps1` passes, with the pinned total updated in the same change as the
   checks that moved it.
2. Every new check was shown to fail when the code it guards was broken.
3. The project summary says what is now true, and labels what is still unproven.
4. Nothing live ran without Brandon's word.

---

## Phase 0: Foundations. Done 2026-11-15

- Solution layout, pinned SDK, locked restore, warnings as errors.
- `scripts/Verify.ps1` with the pinned total and the doc drift checks.
- `CLAUDE.md`, `AGENTS.md`, the invariants.
- Blueprint, specification, roadmap, project summary, autonomy contract, playbook format.
- CI workflow for Windows and macOS. First run on 2026-11-26, at commit `a43c3f7`: all 981 checks
  passed on both, including the real-browser harness and the vulnerability gate.

## Phase 1: Engine core, offline. Done 2026-11-15

- Household file, validation, attestation, facts.
- Name variants, search plan, cadence.
- Playbook format, validator, runner, the MissingMoney playbook, fixture replay.
- Real browser driver, proven against a page served from the local machine.
- Matching, amounts, property kinds, ranking.
- In-memory and SQLite stores in step; hash-chained audit log.
- Case lifecycle, claim packets, the Attestation Gate, three letter templates, the draft outbox.
- Findings report with floor totals.
- Command line: init, household, plan, sweep, findings, report, packets, draft, mark, audit,
  doctor, delete-all-data.

**Exit test, met:** the fixture walk-through in the README, run by the verifier on every change.

---

## Phase 2: A search the source allows. Next

Rewritten on 2026-11-27. The first two rows of the old plan are done. The live sweep it led up to
is replaced by the work below, because the source does not allow it.

| Work | Status | Notes |
|---|---|---|
| Run CI on Windows and macOS | Done | Green on the first run, both systems |
| Read MissingMoney's rules | Done | G2. The playbook holds the reading |
| **2a. Stop the engine searching MissingMoney unattended** | Next, first | Code. The source map says assisted; the runner refuses an assisted playbook unattended; a live sweep opens no browser for it and lists the names for the person to search; recorded answers still replay. Specification 5.20 has the design and the checks. Brandon has left this to a coding session |
| **2b. Decide G7 and G8** | Brandon | Whether the engine may name itself, and what may be kept. An assisted search of MissingMoney waits for both |
| **2c. Build the assisted search** | After 2a and 2b | The engine opens the official page and fills in the name. The person presses Search and clears any check. The engine reads the answer. One confirmation for each search, no more than five a minute. A playbook format change, made in `Playbook-Format.md` first |
| **2d. Bring the recipe up to date** | With 2c | The state field is now required, the search button has its own id, results are paged. Only what can be seen without searching is known so far |
| **2e. The first live search** | After 2c. Needs G3 again | Brandon's machine, his own household, his own hand on the Search button. One name first |
| Record what a real answer looks like | With 2e | The shape only. New fixtures are invented records in the real shape; no real record enters the repository |
| Update the status | With 2e | Source status, playbook provenance and version, the pinned sentences in the verifier and the docs, in one change |

A second road to a first live proof does not depend on MissingMoney: pick one source from phase 4
whose own rules allow an unattended search, read those rules first, and prove the runner there.
Federal sources are the likeliest candidates. Either road ends phase 2.

**Exit test:** the engine cannot be made to search an assisted source unattended, and a check
fails if that is undone; and one real search, on a source and in a manner its rules allow, either
captured an answer or was cleanly handed to the person, with the record showing which.

**Size:** 2a is small. 2c is medium. The decisions in 2b are the long pole.

## Phase 3: Desktop alpha, Windows first

Turn the command line into something that runs by itself.

| Work | Notes |
|---|---|
| Engine host and scheduler | One browser session at a time, cadence-driven, jittered, power-aware (specification 6.2) |
| Local dashboard | Household, findings, packets, drafts, guided checklist, record |
| First-run flow | Specification section 4. Attest before anything is searched |
| Guided checklist for every planned source | The official address, what to have on hand, a "done" mark that is the person's |
| Workspace encryption | Specification O5. Before anyone but Brandon runs it |
| Installer, signing, update check | Reuse what CareerSeeker has for Windows |
| Dependency and package size | Specification O6 |

**Exit test:** Brandon's household runs unattended for 30 days on Windows. No source went on hold
because of something the engine did. Everything the command line can do, the dashboard can do.

**Size:** large.

## Phase 4: More sources

Each source follows the checklist in [AGENTS.md](../AGENTS.md): terms read, playbook, registry
entry, invented fixtures, harness checks, Brandon's word before its first live run.

| Work | Notes |
|---|---|
| State programs | Start with the states in the alpha households' address history. Exact amounts and the claim link per state |
| Per-state claim requirements as data | Notary thresholds, forms, what an heir must send (O9). Feeds the packet and the effort estimate |
| Federal sources searchable by name | PBGC, FDIC, then others, one at a time |
| Search by employer | Workers Owed Wages needs a planner that searches employer names from the household file |
| Assisted mode for a second source | Built in phase 2 for MissingMoney. Next: the bankruptcy courts' locator, whose search has a CAPTCHA |
| Terms as a recorded fact | A source the engine searches by itself carries a record of when its rules were read and what they allow, and the engine refuses to search one that has none |
| Deadlines on findings | Feed the ranker's urgency and the `EXPIRED` state, which exist and are not yet wired |
| Playbook feed, signed | Specification 6.4 |

**Exit test:** five sources at `Proven`. A playbook broken on purpose is detected as a changed
shape, fixed in the feed, and picked up by a running engine without a new release.

**Size:** large, and it never fully ends. Playbook upkeep is the product's running cost.

## Phase 5: Licence and sales. Needs G4, G5

| Work | Notes |
|---|---|
| Licence state in the engine | None, trial, paid, perpetual, lapsed. Offline verification of a signed token |
| The gating points | Scheduler, packets, drafts, reminders, pairing. Nothing already found is ever hidden |
| Renewal | Licence id only. Grace period when the service is unreachable |
| Checkout | Website and merchant of record |
| Harness | Bad signature, unknown key, expired, clock skew, lapse mid-run. Each with a mutation |

**Exit test:** buy, receive a token, paid features turn on; let it lapse, they turn off and every
finding is still there; pull the network cable, the engine keeps working through the grace period.

**Size:** medium, small if the service is shared with CareerSeeker.

## Phase 6: Relay and phone apps. Needs G5

| Work | Notes |
|---|---|
| Adopt the sync protocol | ClaimSeeker payload kinds: snapshot, delta, mark, entitlement |
| Pairing | QR code from the dashboard |
| Android app, then iPhone | Findings, packets, alerts, marks, guided checklist |
| Demonstration mode | The invented Sample family. The app is useful and honest with no engine |
| Push | Content-free notifications; the detail stays in the encrypted payload |
| Store listings | No purchase flow, no implied government affiliation |

**Exit test:** pair by QR; a new finding on the computer reaches the phone; "filed" tapped on the
phone is applied by the engine through the same lifecycle rules; a harness shows the relay holds
only ciphertext; a lost phone can be unpaired from the engine.

**Size:** large.

## Phase 7: Drafts in the person's mailbox

| Work | Notes |
|---|---|
| Gmail drafts | `gmail.compose` only. Google's verification of the OAuth app takes weeks: start it early |
| Outlook and Microsoft 365 drafts | The narrowest permission that can create a draft |
| IMAP append | For everyone else |
| A no-send harness per outbox | One method, no send call, checked by reflection |

**Exit test:** a letter appears in the Drafts folder of each mailbox type, unsent, and no code path
in any outbox can send.

**Size:** medium.

## Phase 8: The differentiators

| Work | Notes |
|---|---|
| County foreclosure and tax-sale surplus | Playbooks for the counties in a household's address history, written on demand. Lists arrive as PDF, spreadsheet or HTML (O7) |
| Letters to county offices and administrators | New templates, through the gate like the others |
| Class action settlements | A guided list with deadlines. The person reads the terms and decides (O11) |
| Holder letters with an address | Official contact addresses in the source map, so a draft can carry a recipient |
| Life events | "A relative died", "I lost a house", "my bank failed": each turns on the right sources |

**Exit test:** a household with a past foreclosure in a covered county gets a surplus finding with
its deadline at the top of the list, and a letter to the right office.

**Size:** large.

## Phase 9: macOS, hardening, launch. Needs G6

| Work | Notes |
|---|---|
| macOS installer | Developer ID, notarization, Keychain |
| Legal review | G6 |
| Privacy policy, support page, incident response | |
| Opt-in aggregate measurement | Source health and dollar bands only. Never names |
| External review | Hand a reviewer the summary, the invariants and the verifier |
| Public release | |

**Exit test:** a stranger installs it, describes a household, sees a floor total in the first
session, and nothing in the product or its copy overstates what was found.

---

## Not on the roadmap

These are not postponed. They are out.

- Sending a message or filing a claim for someone.
- Answering a CAPTCHA, or anything that makes the browser look less like a program.
- Storing a Social Security number or an account number.
- Charging a percentage of what is recovered, or holding anyone's money.
- Crawling the open web for money.
- Searching for someone without a recorded basis.
- Lottery prizes: there is nothing to search by name.

## Order, and what can move

Phase 2 comes first because everything else rests on a search the engine has never made for real,
and because 2a closes the gap between what the main source allows and what the engine would do if
told. Phases 3 and 4 can overlap once 2a is done. Phase 5 can start as soon as G4 is answered, and should not
wait for phase 4 to finish. Phase 7 has outside lead time and should be started during phase 3.
County surplus (phase 8) is the strongest reason to buy; if the alpha shows the basic sources are
not enough to carry the product, pull it forward.
