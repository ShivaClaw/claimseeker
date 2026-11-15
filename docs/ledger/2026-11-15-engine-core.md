# Ledger: 2026-11-15, engine core

One entry per working session. A ledger entry is a record of what was run and what it printed. It
is not edited afterwards; a later session that changes the picture writes its own entry.

## What this session did

- Chose the stack: .NET 10, the same as CareerSeeker (Brandon, 2026-11-15).
- Chose the scope: documents, scaffold and a tested offline engine core. No live searches.
- Built ten projects under `src/` and nine console harnesses under `tests/`.
- Wrote the MissingMoney playbook from the project's earlier agent notes.
- Wrote `scripts/Verify.ps1`, the CI workflow, `README.md`, `CLAUDE.md`, `AGENTS.md`.
- Wrote the blueprint, specification, roadmap, project summary, autonomy contract and playbook
  format.

## What this session did not do

- **Nothing contacted a real source.** `claimseeker sweep --live` was never run. No request was
  made to any unclaimed-property, court, county or agency site by the engine, a harness or a
  script. The only browser traffic was to a page served from the build machine's loopback
  interface.
- MissingMoney's terms of use were not read. A fetch of its home page for that purpose was not
  permitted in this session, and no other attempt was made.
- Nothing was pushed. The session's GitHub access to the repository is read-only.
- Nothing was built or run on Windows or macOS.
- The CI workflow was not run.
- No code or document text was copied from CareerSeeker. Its conventions were read and mirrored.

## Environment

| | |
|---|---|
| System | Linux build container |
| .NET SDK | 10.0.404, pinned in `global.json` |
| PowerShell | 7.6.10 |
| Browser used by PortalHarness | The Chromium already on the machine, through Playwright's `chrome` channel, headless |
| Direct packages | Microsoft.Data.Sqlite 10.0.15, Microsoft.Playwright 1.63.0 |

## Final verification

`pwsh -NoProfile -File scripts/Verify.ps1 -IncludeAudit`, exit code 0:

```
=== Toolchain ===
dotnet SDK 10.0.404 (pinned by global.json)

=== Restore, locked ===

=== Build, warnings as errors ===
0 Warning(s), 0 Error(s)

=== Offline harnesses ===
  CoreHarness                 151 passed
  ScoutHarness                158 passed
  ScorerHarness                90 passed
  StoreParityHarness          121 passed
  GateHarness                  93 passed
  DispatcherNoSendHarness      51 passed
  LifecycleHarness            144 passed
  EngineHarness               128 passed
  PortalHarness                45 passed

=== Offline total: 981 passed, 0 failed ===

=== Docs agree with what was measured ===
  README.md: harness table matches
  docs/ClaimSeeker-Project-Summary.md: harness table matches
  4 statements of the total across 9 docs: all say 981
  7 pinned statements present

=== No personal data is tracked ===
  0 tracked files, none of them a household file, a database, a letter or a workspace

=== Engine smoke: the built program, end to end, on recorded answers ===
  init, import, sweep on fixtures, findings, draft, audit, doctor: ok
  one letter written, marked unsent; nothing was sent and nothing was filed

=== Known vulnerabilities (network) ===
  19 projects checked, no known vulnerabilities

=== Verify: ok. 981 offline checks passed, 0 failed (pinned: 981) ===
```

"0 tracked files" is because nothing had been committed when this ran.

## Can the verifier fail?

Nine faults were put into the working tree one at a time. After each, the verifier was run and the
tree restored.

| Fault | Result |
|---|---|
| README harness row changed from 151 to 150 | Failed: "README.md reports 150 checks for CoreHarness" |
| Project summary total row changed to 980 | Failed: "reports a total of 980" |
| Roadmap sentence changed to "975 offline checks" | Failed: "docs/ROADMAP.md says '975 offline checks'" |
| `$ExpectedOfflineTotal` changed to 982 | Failed: "The offline total drifted: measured 981, pinned 982" |
| "has never been run against a real source" altered in the README | Failed: "README.md no longer says ..." |
| `Ranker.BaseEffortHours` changed from 0.5 to 0.6 | Failed: "ScorerHarness failed" |
| An unused local added to `Ranker.cs` | Failed at the build: warnings are errors |
| `Program.cs` changed to drop its arguments | Failed in the engine smoke. First caught one step late, at the household import, because `init` with no arguments printed help and exited 0. The smoke now requires "Workspace ready" and the example file, and the same fault fails at `init` |
| A `household.json` added to the git index | Failed: "Files that hold a person's data are tracked by git" |

## Can the harnesses fail?

During the build, 52 faults were put into the engine on scratch copies, in rounds of 22, 16, 12
and 2. Five were not caught at first. Each exposed a missing check, which was added, and the fault
was run again to confirm. They are listed in the project summary.

## Things found while writing the documents

Writing down what the code does turned up statements that were not true. Each was corrected in the
document or the code before this entry was written.

- The browser driver's comment said the verify script scans its source. The scan is in
  PortalHarness; the comment now says so.
- The playbook's provenance said the recipe "worked against the live site in the July 2026
  prototype". What is known is that the project's notes, written in July 2026, record it as
  working. The playbook now says that.
- The verifier's count of statements of the total missed one that wrapped across two lines. The
  pattern now allows any whitespace.
- A first draft of the specification listed `DRAFT_PENDING` as a state only the engine can enter.
  It is open to both.
- A first draft said the store enforces the lifecycle. The store does compare-and-set; the
  pipeline applies the lifecycle.
- The ranker's deadline and "run by hand" inputs exist and nothing feeds them. That is now listed
  as a known gap, and wired in roadmap phase 4.

## For the next session

1. Gate G1: the repository private, and push access for the Claude GitHub app.
2. Push the branch, open a draft pull request, and watch the first CI run on Windows and macOS.
3. Gate G2: read MissingMoney's terms and robots policy, and record them in the playbook.
4. Gate G3: Brandon's word for one supervised live sweep.
