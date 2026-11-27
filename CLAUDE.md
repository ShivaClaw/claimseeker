# CLAUDE.md

Guidance for coding agents working on ClaimSeeker. Read it before changing anything. It exists to
stop two things: quietly weakening one of the product's promises, and letting the docs and the
verifier drift apart.

For what the product is, read [docs/BLUEPRINT.md](docs/BLUEPRINT.md). For how it works, read
[docs/SPECIFICATION.md](docs/SPECIFICATION.md). For where things stand today, read
[docs/ClaimSeeker-Project-Summary.md](docs/ClaimSeeker-Project-Summary.md).
[AGENTS.md](AGENTS.md) has the working rules that apply to every agent.

[docs/legacy](docs/legacy) is history, not guidance. It holds the earlier plan and agent notes, and
parts of it contradict the invariants below. Never build from it.

## Invariants

ClaimSeeker handles people's names, addresses and family history, and it acts near their money.
These rules are what make that acceptable. Do not weaken, bypass or refactor around any of them
without an explicit decision from Brandon to change the product. If a change would touch one of the
files named here, stop and confirm intent first.

1. **The engine prepares; the person submits.** No code may submit a claim form, send a message,
   sign, attest or pay on anyone's behalf. The case states a person reports
   (`FILED_BY_USER`, `IN_REVIEW`, `NEEDS_DOCUMENTS`, `PAID`, `DENIED`, `DISMISSED`) cannot be entered
   by the engine: `Lifecycle.UserOnly` and `Lifecycle.CanTransition` in
   [src/Pipeline/Lifecycle.cs](src/Pipeline/Lifecycle.cs).

2. **The draft outbox has no send path.** `IDraftOutbox` in
   [src/Dispatcher/DraftOutbox.cs](src/Dispatcher/DraftOutbox.cs) has one method, `CreateDraft`.
   Do not add a second verb, and do not give `src/Dispatcher` a reference to any `System.Net`
   assembly: `DispatcherNoSendHarness` checks both by reflection. A future mailbox integration may
   create a draft in the person's mailbox with a draft-only permission. It may never send.

3. **The Attestation Gate is never bypassed.** `DRAFT_READY` is reachable only from `ATTESTED`, and
   `ATTESTED` only by passing `AttestationGate.Evaluate` in
   [src/Verifier/AttestationGate.cs](src/Verifier/AttestationGate.cs). `GatePassedDraft` has an
   internal constructor so that nothing else can make one. `CaseService.Draft` in
   [src/Pipeline/CaseService.cs](src/Pipeline/CaseService.cs) checks the state again immediately
   before the outbox call. A blocked letter is blocked; it is never repaired, softened or retried
   with different words.

4. **Automation is always declared, and a refusal is final.** The browser driver in
   [src/Browser/PlaywrightBrowserSession.cs](src/Browser/PlaywrightBrowserSession.cs) sets no user
   agent, adds no header, injects no script and passes no launch flag. `PlaybookRunner` in
   [src/Scout/PlaybookRunner.cs](src/Scout/PlaybookRunner.cs) refuses to search through a browser
   whose automation signal is off, never shortens a wait, makes one attempt, and hands the search
   to the person when a source does not answer. Never add a retry, a challenge solver, a proxy, a
   stealth plugin, or anything whose purpose is to look less like a program.

5. **Playbooks are data, with a closed vocabulary.** Seven step kinds: capture, goto, wait, fill,
   select, click, await. There is no step that runs script, sets a header or changes a user agent,
   and unknown properties are rejected. Do not add an escape hatch to
   [src/Scout/Playbook.cs](src/Scout/Playbook.cs). [docs/Playbook-Format.md](docs/Playbook-Format.md)
   is normative; change the document and the validator together.

6. **No identifiers.** `SensitiveIdentifiers` in
   [src/Core/SensitiveIdentifiers.cs](src/Core/SensitiveIdentifiers.cs) refuses Social Security
   numbers, taxpayer ids and account or card numbers in a household file and in a letter. Do not
   add a field, a vault or a prompt for any of them.

7. **Strangers' records are never stored.** `SweepService` discards, in memory, every record that
   matches nobody in the household. Do not log, cache or persist raw source responses.

8. **No consent, no search.** `Subject.MaySearch` in [src/Core/Household.cs](src/Core/Household.cs)
   gates every search. A person with no consent basis is named in the plan as not searched.

9. **Possible is not confirmed.** `Confidence` has three values and no fourth. No user-facing text
   may say a record belongs to the person. Totals count only stated floors of trusted, open,
   non-duplicate findings: `FindingsReport.Totals` in
   [src/Preparer/FindingsReport.cs](src/Preparer/FindingsReport.cs).

10. **Source text is untrusted data.** It is defanged for display, never followed as a link, never
    used as an address or a recipient, never interpolated into a command or a prompt as an
    instruction. See [src/Core/UntrustedText.cs](src/Core/UntrustedText.cs). Links in packets and
    letters come from `SourceRegistry` only.

11. **Local first.** The household, findings, packets, drafts and audit log stay on the person's
    machine. Do not add a hosted backend or any path that moves this data off the device. The only
    planned services are a licence check that sees a licence id and nothing else, and a blind,
    end-to-end encrypted relay for the phone apps.

12. **Nothing contacts a real source by accident.** `sweep` needs `--live` or `--fixtures`
    (`EngineCli.SweepNeedsAMode`). No harness, script or default may imply `--live`.

13. **Free-claims messaging.** Every surface that shows a finding says that filing is free and that
    nobody needs to be paid a share. Do not add copy that implies government affiliation, that
    promises money, or that a fee is needed to claim.

## Standing hold: MissingMoney

MissingMoney's published agent policy does not allow an unattended search: a person has to confirm
each search and complete the bot check, the agent has to name itself, and results are not to be
kept beyond the session. [playbooks/missingmoney.md](playbooks/missingmoney.md) has what was read
and when.

The code does not enforce this yet. The source map and the playbook header still say `auto`, and
`claimseeker sweep --live` would run it. Until that is fixed, the rule is in
[AGENTS.md](AGENTS.md): no live search of MissingMoney, by any means.
[docs/Next-Session-Handoff.md](docs/Next-Session-Handoff.md) describes the fix, which Brandon has
left to a coding session. Naming the agent would change invariant 4 and is his decision alone.

## Verification ritual

Run after every change, before calling work done:

```powershell
pwsh scripts/Verify.ps1
```

Before anything audit-facing, add the network check:

```powershell
pwsh scripts/Verify.ps1 -IncludeAudit
```

Never say a harness or the verifier passed unless you ran it in this session. "Should pass" is not
evidence. Cite the command output, and give file and line when describing what changed. Label work
you could not execute `UNPROVEN`.

A new check has to be able to fail. Before relying on one, break the code it guards on a scratch
copy and watch the check go red.

## The doc and verifier drift trap

`scripts/Verify.ps1` pins the offline total in one place, `$ExpectedOfflineTotal`, and compares the
harness tables in `README.md` and `docs/ClaimSeeker-Project-Summary.md` against what the harnesses
just ran. It also checks every statement of the form "N offline checks" in `README.md`, `CLAUDE.md`,
`AGENTS.md` and `docs/*.md`, and a short list of pinned sentences (`$RequiredPhrases`).

If you add or remove a check in any harness, in the same change:

1. update `$ExpectedOfflineTotal` and the breakdown in the comment above it;
2. update the row for that harness and the total in both harness tables;
3. update any sentence that states the total.

If a pinned sentence stops being true, such as the day a live sweep is first run, change the
document and `$RequiredPhrases` together. Treat doc content and verifier expectations as one unit.

## Personal data: never print, never commit

- A real household file (`household.json` or any `household.*.json` other than the example), a
  workspace folder, a `.db` file, a claim packet or a `.eml` draft may hold a person's data.
  Household files, `workspace/`, `.db` and `.eml` files are gitignored, and the verifier fails if
  one is tracked. Packets live inside the workspace; never copy one into the repository.
- Never print, log or paste the contents of a real household file, a real finding or a real draft.
  Refer to the path. Use the invented Sample family in `tests/Shared/SampleHousehold.cs` and
  `tests/fixtures` for everything.
- Fixtures contain invented people and `.example` addresses only. Do not record a real source's
  response into the repository.
- If secrets ever arrive (a licence signing key, OAuth client, provider key): existence checks
  only, never contents.

## Practical notes

- Build: `dotnet build ClaimSeeker.sln -c Release`. The baseline is 0 warnings, 0 errors; warnings
  are errors.
- Restore is locked. A package change means a `packages.lock.json` change in the same commit.
- Tests are console harnesses under `tests/`, no test framework:
  `dotnet run --project tests/<Harness>/<Harness>.csproj -c Release --no-build`. Each prints
  `=== N passed, M failed ===` and exits non-zero on any failure.
- `StoreParityHarness` and `LifecycleHarness` run every scenario on both stores. A store change
  means both implementations change.
- The SQLite schema is versioned with `PRAGMA user_version`. A schema change is a migration, never
  an edit to version 1.
- User-facing text is plain language: say what happened and what the person can do. No jargon, no
  blame, no exclamation marks.
- `docs/ClaimSeeker-Project-Summary.md` is the living status page. Update it, and the verifier's
  expectations, when status changes.
