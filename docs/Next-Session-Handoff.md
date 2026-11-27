# Next session: start here

For the coding session that picks this work up. Written 2026-11-27.

This page is a to-do list with reasons. When its tasks are done, delete it and let
[ClaimSeeker-Project-Summary.md](ClaimSeeker-Project-Summary.md) carry the record.

## Where things stand

- The engine core is on GitHub as draft pull request 1, `claude/engine-core` into `main`. Brandon
  merges it.
- 981 offline checks pass on Linux, Windows and macOS. CI runs the verify script on both target
  systems.
- **No source has ever been searched.**
- The one source with a playbook, MissingMoney, publishes a policy for AI agents. It was read on
  2026-11-26. It does not allow the unattended search the playbook describes.
  [playbooks/missingmoney.md](../playbooks/missingmoney.md) has the reading.
- The engine does not know that. Its source map and the playbook header still say `auto`.

## Hard stops

1. **Do not run `claimseeker sweep --live`**, and do not drive missingmoney.com's search form by any
   other means. Brandon's yes to a first live sweep came before the rules were read.
2. Do not change how the browser identifies itself. That is invariant 4, and it is Brandon's
   decision (O14 below).
3. Do not store, or add a way to store, anything a real MissingMoney search returns until O15 is
   answered.
4. Everything in [CLAUDE.md](../CLAUDE.md) and [AGENTS.md](../AGENTS.md).

## Task 1: make an unattended search of an assisted source impossible

This is change 2a in [SPECIFICATION.md](SPECIFICATION.md) section 5.20, which has the design and
the checks. Brandon was asked on 2026-11-26 whether to make this change at once, and chose to
record the finding in the documents and leave the code to a coding session. CLAUDE.md asks for his
confirmation before the files below are touched, so confirm with him at the start that this is the
change he wants.

Where the change lands:

| File | What changes |
|---|---|
| `src/Scout/SourceRegistry.cs` | `missingmoney` becomes assisted and its operator is corrected. `Searchable` stays "automatic, with a playbook". A new `Replayable` is "has a playbook". `EffectiveMode` treats an assisted source as guided until the assisted search is built |
| `src/Scout/PlaybookRunner.cs` | The unattended path refuses a playbook whose mode is not `auto`, before it touches the browser |
| `src/Engine/EngineCli.cs` | `sweep --fixtures` replays `Replayable`. `sweep --live` runs `Searchable`, opens no browser when there is none, and lists the names for the person to search. `plan`, `sources` and `doctor` follow |
| `src/Preparer/FindingsReport.cs` | The list of places to look includes a source the engine cannot search itself yet |
| `playbooks/missingmoney.md` | The header says `assisted`. The notice at the top is reworded |
| `docs/Playbook-Format.md` | Normative: an assisted playbook is never run unattended. Change it with the runner |
| `docs/Autonomy-Contract.md` | One line moves: the engine searches unasked only where the source's own rules allow it. Say so in the pull request |
| `CLAUDE.md`, `AGENTS.md` | The standing hold becomes an invariant with a check behind it |
| `scripts/Verify.ps1`, `README.md`, the project summary | The pinned total, both harness tables, and the three pinned hold sentences |

What to expect in the harnesses:

- `ScoutHarness` asserts that exactly one source is searched by the engine today. That becomes
  none, plus new checks that the runner makes no browser call for an assisted playbook.
- `EngineHarness` asserts that one source is marked searched, and has a section that drives a live
  sweep through an injected scripted browser. With no automatic source that section has nothing to
  run. Replace it with checks that the browser factory is never called and that the names are
  listed. The wiring from the command line to the browser then has no check until a real automatic
  source exists. Say so in the summary.
- `PortalHarness` builds its local playbook from the shipped header. It needs a copy marked `auto`,
  with a comment saying a page served from this machine is the harness's own to automate.
- The fixture walk-through's headline, `At least $1,889.90 across 10 matches`, is a sum of floors
  and should not move. Effort figures and the order of findings may, because mode feeds the ranker.

Every new check has to be shown to fail: remove the guard on a scratch copy and watch it go red.

## Task 2: decide whether to share a platform with CareerSeeker

Brandon delegated this decision (specification O4) to a coding session. It matters when phase 5
(licence) or phase 6 (relay and phone apps) starts, and not before.

Read CareerSeeker's sync protocol document and test vectors, and its licence design. Share if all
five of these hold:

1. A ClaimSeeker payload cannot be mistaken for a CareerSeeker one on the wire, or the protocol
   can be extended to make that so without breaking its existing vectors.
2. A licence for one product cannot unlock the other.
3. The relay keeps the two products' pairings apart.
4. Nothing shared would put a network reference into an assembly that must not have one
   (`src/Dispatcher` above all).
5. The shared pieces can be used from this repository without copying private code into it. This
   repository is public for now and CareerSeeker's is not.

If one fails, build ClaimSeeker's own to the same wire format. Either way, write the decision and
the test that decided it into section 12 of the specification and into the project summary.

## Waiting on Brandon

Do not build past these.

| # | Question | Why it blocks |
|---|---|---|
| O14 | May the engine name itself to a site that asks agents to? | MissingMoney's policy requires it. It rewords invariant 4 and needs an operator name and a public page describing the agent |
| O15 | What may be kept from a source that says results are not to be kept beyond the session? | It decides whether MissingMoney findings can be stored at all. It wants a lawyer's reading and, better, the operator's answer in writing |
| O16 | On MissingMoney, family name alone or given and family name? | The policy forbids working through names |
| O1, O2 | What is free, what is paid, the price | Deferred on 2026-11-26. Blocks phase 5 only |

## Smaller things found along the way

- `SourceRegistry` gives MissingMoney's operator as NAUPA. The site says it is operated by Kelmar
  Associates for the National Association of State Treasurers, in collaboration with NAUPA.
- The MissingMoney recipe is out of date where it can be checked without searching: the state field
  is required now, the search button is `button#btn-turnstile`, and results are paged.
- A consent basis of "next of kin" is looser than the policy's "legal authority to claim". The
  first-run flow should ask which a person has.
- The site does its own loose name matching, so searching guessed misspellings there may add
  nothing but requests.
- An idea for phase 4: record, for each source, when its rules were read and what they allow, and
  have the engine refuse to search any source by itself without that record.

## Re-reading the rules

Rules change. Before any work that depends on them, open these in a browser and compare:

1. `https://missingmoney.com/robots.txt`, and its `SiteAI:` line.
2. `https://missingmoney.com/siteai.json`. Its `metadata.lastUpdated` was `2026-10-22`.
3. `https://missingmoney.com/app/privacy-policy`. It was last updated 2022-10-06.

If any has changed, read all three again and update the playbook's notes with the new date before
doing anything else. Reading these pages is research. Submitting the search form is a live run.
