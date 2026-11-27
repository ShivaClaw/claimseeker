# AGENTS.md

Working rules for every coding agent in this repository, whichever tool it runs in.
[CLAUDE.md](CLAUDE.md) has the invariants and the verification ritual; this file is the short
version of how work is done here.

## Who decides

- **Brandon merges every change.** Agents work on a branch, open a draft pull request, and never
  merge their own work or push to `main`.
- **Nothing live without Brandon's word.** That covers `claimseeker sweep --live` against any real
  source, any request to a real unclaimed-property, court, county or agency site from a harness or
  a script, any deploy, any publish or signing step, any change to DNS or hosting, and anything
  that spends money. Reading a public page to research a source is fine; searching it for a
  person's name is a live run.
- **No live search of MissingMoney.** Its published agent policy, read on 2026-11-26, requires a
  person to confirm each search and complete the bot check. The playbook header still says `auto`
  and the engine will run it if told to, so this rule is the only thing in the way: do not run
  `claimseeker sweep --live`, and do not drive that site's search form from any script, harness or
  browser tool. Brandon's yes to a first live sweep came before the policy was read. Do not rely
  on it: ask again once the engine can run a search with a person at the controls.
  [playbooks/missingmoney.md](playbooks/missingmoney.md) has the detail.
- **Do not widen scope.** A product promise in [CLAUDE.md](CLAUDE.md) changes only by Brandon's
  explicit decision, recorded in the pull request.

## Before you say it is done

1. `pwsh scripts/Verify.ps1` ran in this session and ended with `=== Verify: ok`.
2. The pull request quotes that output and gives file and line for what changed.
3. Anything not executed is labelled `UNPROVEN`, with what it would take to prove it.
4. New checks were shown to fail when the code they guard is broken.
5. Counts in the docs match the verifier (see the drift trap in [CLAUDE.md](CLAUDE.md)).

## Data

- Invented people only: the Sample family in `tests/Shared/SampleHousehold.cs` and the records in
  `tests/fixtures`. Never a real name, address or search result in code, tests, docs, commit
  messages or pull requests.
- Never print or commit a real household file, workspace, database, packet or draft.
- Never read or print secret contents. Existence checks only.

## Adding a source

A source the engine searches itself needs all of the following, in one pull request:

1. the source's own terms of use, robots policy and any agent policy it publishes (look for a
   `SiteAI:` line in robots.txt and a `siteai.json` file) read, and what they allow recorded in the
   playbook's notes with the date;
2. a playbook in `playbooks/` that passes `PlaybookValidator`, with a fallback the person can
   follow by hand;
3. a registry entry in `src/Scout/SourceRegistry.cs` whose hosts and entry address were seen on
   the operator's own site;
4. invented fixtures for it under `tests/fixtures/sources/<source id>/`;
5. harness checks for its result mapping and its matching quirks;
6. Brandon's word before its first live run, and the playbook's provenance updated after it.

A source that needs a person (a CAPTCHA, a sign-in, a Social Security number) is a guided source:
a registry entry with the official address and a checklist, and no playbook.
