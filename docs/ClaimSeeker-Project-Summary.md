# ClaimSeeker Project Summary

The living status page. Read this to learn what is true today. Update it, and the verifier's
expectations, in the same change that makes something new true.

| | |
|---|---|
| Last updated | 2026-11-27. Dates on this page are Pacific time |
| Phase | Phases 0 and 1 are done, of phases 0 to 9: foundations, and the engine core offline. Next: phase 2, rewritten on 2026-11-27 ([ROADMAP.md](ROADMAP.md)). Start at [Next-Session-Handoff.md](Next-Session-Handoff.md) |
| Repository | Pushed on 2026-11-26. Draft pull request 1, `claude/engine-core` into `main`. CI green on Windows and macOS |
| Pinned offline verifier | **981 passed, 0 failed** (`scripts/Verify.ps1`, `$ExpectedOfflineTotal`) |

## The short version

ClaimSeeker can take a household file, plan the searches, run them through a playbook, keep only
the records that plausibly belong to the household, rank them, write a claim packet for each, pass
a letter through the Attestation Gate and leave it as an unsent draft, track what the person says
happened next, and prove from its own record what it did. All of that is checked by 981 offline
checks.

**The engine has never been run against a real source.** Every search so far was a recorded answer
made of invented records, a scripted browser session, or a real browser talking to a page served
from the same machine.

**And it must not be, as it stands.** The one source with a playbook is MissingMoney. Its published
agent policy, read on 2026-11-26, requires a person to confirm each search. The engine would run
that search unattended if told to. The first task of the next session is to make that impossible.

## What exists

| Area | State |
|---|---|
| Engine core (ten projects under `src/`) | Built, checked offline |
| Command line `claimseeker` | Built, checked offline |
| MissingMoney playbook | Written from the project's earlier notes. Not yet re-run by this codebase, and not to be run as written: the site's rules were read on 2026-11-26 and do not allow an unattended search. Parts of the recipe are known to be out of date |
| Other 17 sources | In the source map as places to look. No playbooks |
| Scheduler, tray app, dashboard, installer | Not built |
| Licence, relay, phone apps | Not built |
| Mailbox drafts | Not built. Drafts are `.eml` files |
| CI workflow | Runs the verify script on Windows and macOS. Green on its first run, 2026-11-26 |

## Harnesses

| Harness | Checks | Covers |
|---|---:|---|
| CoreHarness | 151 | Name normal form; reading amounts as stated; property kinds; strict household file; validation; attested facts; identifiers refused; source text cannot act; stable finding ids |
| ScoutHarness | 158 | Name variants and capped misspellings; one search per family name; narrowing; cadence; the shipped playbook; the validator's refusals; the runner's rules; fixture replay; result limits; the source map |
| ScorerHarness | 90 | Name and place rules for confidence; former names; weak names capped; two owners on one line; ranking arithmetic; urgency; the order shown |
| StoreParityHarness | 121 | In-memory and SQLite agree step by step; tampering shows in the audit log; a restarted engine reads the same file |
| GateHarness | 93 | Templates are sound; attested facts of the right kind about the right person; source words verbatim and inert; no identifier; official links only; only the gate makes a passed draft |
| DispatcherNoSendHarness | 51 | One-method outbox; no network assembly referenced; unsent file; same letter is one file; no header forgery |
| LifecycleHarness | 144 | On both stores: the gate is the only way to a letter; the engine cannot say a claim was filed; sweep, packet, letter, block, resume; a refused source is left alone; a changed shape is a broken playbook; floor totals |
| EngineHarness | 128 | Every command; no source contacted without `--live`; one engine at a time; deletion only on purpose; workspace location |
| PortalHarness | 45 | The real driver against a local page: no disguise in its source, declares itself, fills and submits, reads the wire, ignores other hosts, stops on silence and on a redirect |
| **Total** | **981** | |

### Can the checks fail?

Yes, and this was tested. During the build, 52 deliberate faults were put into the code on scratch
copies, in four rounds, and the harnesses were run against each. Five faults were not caught at
first. Each exposed a missing check, and each was closed:

| Fault put in | What now catches it |
|---|---|
| Refreshing a finding moved its case back to the start | A refresh after a state change, in the store parity script |
| A hole in the audit log's numbering was accepted | Checks that the sequence must run 1 to n |
| A packet was written for a finding that had not been ranked | A check that an unranked finding gets no packet |
| A closed case reached the outbox before the lifecycle refused it | The outbox is counted in the check, and `CaseService.Draft` now tests the state immediately before the call |
| The first browser search was not captured, and the harness crashed instead of failing | The harness now reports the failure and stops |

## Evidence

Each dated block is backed by a ledger entry under [docs/ledger](ledger).

**2026-11-26.**

- The workspace of 2026-11-15 no longer existed. The branch was restored from the bundle handed
  over that day, and its commit ids matched the ones recorded then.
- `pwsh scripts/Verify.ps1 -IncludeAudit` on Linux at commit `a43c3f7`: 981 passed, no vulnerable
  packages.
- The first CI run, on the draft pull request: 981 passed on Windows and 981 on macOS, with the
  real-browser harness, the engine smoke and the vulnerability gate on both.
- No source was searched. MissingMoney's rules were read by opening its pages in a browser.

**2026-11-15.** Run in a Linux build environment, with the SDK pinned in `global.json`.

- `dotnet build ClaimSeeker.sln -c Release`: 0 warnings, 0 errors, with warnings as errors.
- `dotnet restore ClaimSeeker.sln --locked-mode`: succeeds; 19 lock files, one per project.
- `dotnet list ClaimSeeker.sln package --vulnerable --include-transitive`: no vulnerable packages
  in any of the 19 projects.
- `pwsh scripts/Verify.ps1 -IncludeAudit`: see the session ledger for the output.
- The fixture walk-through from the README, by hand and inside the verifier:

  ```
  MissingMoney.com
    SAMPLE: 13 records, 10 matched to your household.
    EXAMPLE: 2 records, 1 matched to your household.
    not searched: alex. No consent basis is recorded for this person, so the engine does not search for them.
    11 new, 0 seen again, 4 records belonging to other people discarded.

  At least $1,889.90 across 10 matches the engine trusts. 1 of them does not state an amount, so it
  adds nothing to that figure. 1 possible match is listed further down and not counted.
  ```

Direct dependencies: `Microsoft.Data.Sqlite` 10.0.15 and `Microsoft.Playwright` 1.63.0. Nothing
else.

## What is proven, and what is not

| Claim | Standing |
|---|---|
| The pipeline works end to end on invented records | Proven offline |
| The runner stops on a refusal, a redirect, silence or a missing field, and does not retry | Proven offline, against a scripted session and a real browser on a local page |
| The browser driver does not disguise itself | Proven by a scan of its source and by the user agent a local page received |
| No letter can be written without passing the gate | Proven offline, by construction and by mutation |
| No code path sends anything | Proven by reflection on the outbox assembly |
| The two stores behave the same | Proven offline |
| The MissingMoney recipe works on the real site today | **UNPROVEN, and partly known to be wrong.** Taken from notes written in July 2026. The page now requires a state, the search button has changed, and results are paged |
| What a real MissingMoney answer looks like in every field | **UNPROVEN.** It cannot be learned without searching |
| Automated searching is allowed by MissingMoney's rules | **Read 2026-11-26. Not unattended.** A search that a person confirms is allowed, for an agent that names itself, with nothing kept beyond the session. `playbooks/missingmoney.md` |
| The engine cannot search a source in a way its rules forbid | **FALSE today.** `sweep --live` would search MissingMoney unattended. Only the rule in `AGENTS.md` prevents it |
| The code builds and passes on Windows and macOS | **Proven 2026-11-26.** CI run 45962091177 at commit `a43c3f7`: 981 passed on `windows-2025-vs2026` and on `macos-26-arm64` |
| The CI workflow is correct | **Proven 2026-11-26** by the same run: locked restore, build, nine harnesses, doc checks, engine smoke and the vulnerability gate on both systems |
| The matching rules are well calibrated | **UNPROVEN.** They are reasoned, and tested on invented records only |

## Known gaps

- **Deadlines are not wired.** The ranker understands a deadline and the lifecycle has an `EXPIRED`
  state, and nothing supplies a deadline yet. No finding is urgent and none expires.
- **"Run by hand" effort is not wired.** The ranker has the half hour; the pipeline never sets it.
- **No recipient on a letter.** The source map has no contact addresses, so the person adds one.
- **The workspace is not encrypted** by the engine. It depends on the operating system.
- **The notary threshold is a guess** of $1,000 until each state's rule is recorded.
- **Narrowing triggers only past the engine's own 5,000-record limit on MissingMoney**, because the
  size at which the site cuts a list off is not known.
- **Only searches by name are planned.** Workers Owed Wages is searched by employer and has no
  planner.
- **The engine treats MissingMoney as automatic.** The source map and the playbook header say
  `auto`. Its rules say a person must confirm each search. Specification 5.20 has the fix.
- **The source map names the wrong operator for MissingMoney.** It says NAUPA. The site says it is
  operated by Kelmar Associates for the National Association of State Treasurers, in collaboration
  with NAUPA.
- **Assisted mode is a field, not a feature.** Nothing opens a pre-filled page for a person yet.
- **The driver does not name the agent.** MissingMoney's policy asks agents to. Invariant 4 says
  the driver sets no user agent. That is a decision, not a bug (O14).
- **The audit log shows tampering and does not prevent it.**
- **The example household's attestation is pre-filled** so the walk-through runs. A real first-run
  flow must make the person write their own.

## Decisions waiting for Brandon

The full list is section 12 of [SPECIFICATION.md](SPECIFICATION.md).

Answered on 2026-11-26:

| Question | Answer |
|---|---|
| Give the Claude GitHub app access; make the repository private | Access given. Asked before the first push, Brandon chose to push while the repository is public |
| Read MissingMoney's rules, then run one supervised live sweep | Yes. The rules were read. They do not allow an unattended search, so no sweep was run |
| Change the engine now so it cannot search MissingMoney unattended | Documents only for now. The code change is left to a coding session |
| What is free, what is paid, and the price | Deferred |
| Share a platform with CareerSeeker | Left to the coding session that reaches that work |

Still waiting, and blocking any real search of MissingMoney:

1. May the engine name itself to a site that asks agents to (O14)? It changes invariant 4.
2. What may the engine keep from a source that says results are not to be kept beyond the session
   (O15)? This wants a lawyer's reading and, better, the operator's own answer in writing.
3. On MissingMoney, search by family name alone, or by given and family name (O16)?

## Keeping this page honest

- A row moves from unproven to proven only when the command that proves it ran, and the output is
  in a ledger entry under [docs/ledger](ledger).
- The harness table and the pinned line are checked by `scripts/Verify.ps1` against what the
  harnesses just ran. Change them in the same commit as the check that moved them.
- The sentence about never having run against a real source is pinned by the verifier. The change
  that makes it false also changes the verifier.
