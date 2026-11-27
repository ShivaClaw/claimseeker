# Ledger: 2026-11-26, first push and the source's rules

A record of what was run and what it showed. Dates are Pacific time; the CI run named below began
at 2026-11-27T07:57Z.

## What Brandon decided

Answers to the four decisions put to him on 2026-11-15, and to three questions asked during this
session:

| Question | Answer |
|---|---|
| Make the repository private and give the Claude GitHub app access | Yes. Access was given |
| The repository was still public: make it private before pushing, push as it is, or wait | Push while it is public |
| Read MissingMoney's terms, then run one supervised live sweep for his own household | Yes |
| Free and paid, and the price | Deferred |
| Share a platform with CareerSeeker | Left to a coding session |
| After the rules were read: change the engine now so it cannot search MissingMoney unattended | Documents only for now; the code change is left to a coding session |

## What this session did

**Recovered the code.** The cloud workspace of 2026-11-15 no longer existed, and GitHub was still
empty. The only copies of the code were the bundle and the zip handed over that day. With Brandon's
approval the bundle was written to a folder on his computer and read back from there. Both branch
heads matched the commit ids recorded on 2026-11-15:

```
75325e300444c3321a07fc4fc33d6fcd52dfc23e refs/heads/main
a43c3f7801166556d30b552feb2cbc9ea75049f0 refs/heads/claude/engine-core
```

**Verified it again** on Linux, in the new workspace, before pushing. `scripts/Verify.ps1
-IncludeAudit` ended with 981 offline checks passed, 0 failed, and no vulnerable packages.

**Pushed.** `main`, which holds the one commit that starts the repository, and
`claude/engine-core`. Draft pull request 1 was opened from the branch.

**Ran CI for the first time.** Run 45962091177, on the pull request, at commit `a43c3f7`:

| Job | Runner image | Result |
|---|---|---|
| verify (windows-latest) | `windows-2025-vs2026` | 981 passed, 0 failed; 19 projects, no known vulnerabilities |
| verify (macos-latest) | `macos-26-arm64` | 981 passed, 0 failed; 19 projects, no known vulnerabilities |

Both jobs ran the real-browser harness (45 checks) and the engine smoke. This is the first proof
that the code builds and passes on the two systems it is meant for, and that the workflow itself
is right.

**Read MissingMoney's rules.** In a browser, by opening pages: the home page, `/robots.txt`,
`/siteai.json`, `/sitemap.xml`, `/app/privacy-policy`, `/app/faq-general` and `/app/claim-search`.
A read-only script listed the search form's fields. Nothing was typed into the form, no search was
submitted, and the cookie banner was left unanswered. The A2WF specification that the agent policy
follows was read at a2wf.org. [playbooks/missingmoney.md](../../playbooks/missingmoney.md) holds
what was found.

**Did not run the live sweep.** The rules require a person to confirm each search and to complete
the bot check. The playbook describes a search with nobody at the controls. Running it would have
been the engine doing something its source does not allow.

**Brought the documents in line.** The playbook, README, CLAUDE.md, AGENTS.md, blueprint,
specification (new section 5.20, and section 12), roadmap (gates, and phase 2 rewritten), and the
project summary. A new page, `docs/Next-Session-Handoff.md`, is where a coding session starts.

**Pinned three sentences** in `scripts/Verify.ps1`, because until the engine refuses an assisted
source by itself, sentences are all that stand between `sweep --live` and that site:

| File | Sentence |
|---|---|
| `playbooks/missingmoney.md` | "Do not run this playbook against the live site" |
| `AGENTS.md` | "No live search of MissingMoney" |
| `CLAUDE.md` | "Standing hold: MissingMoney" |

Each was removed in turn on the working tree and the verifier run. It failed all three times,
naming the file and the sentence. The tree was restored after each.

## What this session did not do

- **No source was searched.** By the engine, a harness, a script or a browser tool.
- **No engine code changed.** Nothing under `src/` or `tests/` differs from commit `a43c3f7`. The
  source map and the playbook header still say `auto`.
- The repository's visibility was not changed.
- The Autonomy Contract and the playbook format, both normative, were not changed. Each needs a
  line changed when the engine does, and the handoff page says so.

## Verification after the document changes

`pwsh -NoProfile -File scripts/Verify.ps1 -IncludeAudit`, exit code 0, on Linux:

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
  5 statements of the total across 10 docs: all say 981
  10 pinned statements present

=== No personal data is tracked ===
  116 tracked files, none of them a household file, a database, a letter or a workspace

=== Engine smoke: the built program, end to end, on recorded answers ===
  init, import, sweep on fixtures, findings, draft, audit, doctor: ok
  one letter written, marked unsent; nothing was sent and nothing was filed

=== Known vulnerabilities (network) ===
  19 projects checked, no known vulnerabilities

=== Verify: ok. 981 offline checks passed, 0 failed (pinned: 981) ===
```

"116 tracked files" is the count before this session's two new files were committed.

## A slip, for the record

While this page was being written, a shell command meant only to write the file passed its text
through the shell unquoted, and the shell tried to run each code span in it as a command. Every
one failed: none names a program, and the files named are not executable. Nothing was changed by
it. The page was then written again with a file tool.

## Corrections to the record

- The ledger of 2026-11-15 says the source's terms had not been read. They now have.
- The source map names NAUPA as MissingMoney's operator. The site says it is operated by Kelmar
  Associates for the National Association of State Treasurers, in collaboration with NAUPA. The
  documents now say that; the source map is code and waits for the coding session.
- The roadmap of 2026-11-15 made the repository going private a gate. Brandon has chosen to leave
  it public for now, and the gate is reworded.
