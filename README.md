# ClaimSeeker

ClaimSeeker finds money that is owed to you and your family and was never collected, and prepares
each claim for you to file. It runs on your own computer. It prepares; you review and submit.
Filing a claim is free.

> **Status: engine core, offline only.** This repository holds the search, matching, ranking, claim
> packet and letter-drafting core, proven by **981 offline checks** on invented records and a page
> served from the local machine, on Linux, Windows and macOS.
> The engine has never been run against a real source. There is no installer, no scheduler, no
> phone app and no licence service yet.
> [docs/ROADMAP.md](docs/ROADMAP.md) says what comes next and what has to be decided first, and
> [docs/Next-Session-Handoff.md](docs/Next-Session-Handoff.md) is where the next session starts.

## What it is for

States, courts, counties, federal agencies and companies hold money for people they could not find:
old bank accounts, uncashed paychecks, utility deposits, insurance proceeds, stock and dividends,
pension benefits, and the surplus left when a foreclosed home sells for more than was owed. Each
holder has its own search, its own form and its own deadline, and nobody tells you to look.

ClaimSeeker looks for you, on a schedule, under every name the money might be filed under, for
everyone in your household who has agreed to it. When it finds something it tells you how sure it
is, what it is worth, how much work the claim will be, where the official claim form is, and what
to have on hand. Where a letter helps, it writes one for you to read, address and send.

The finished product is a paid engine for Windows and macOS with free companion apps for Android
and iPhone. [docs/BLUEPRINT.md](docs/BLUEPRINT.md) has the whole picture.

## What it will not do

These are built into the code and each has a check that fails if it is weakened.

1. **It does not file, send, sign or pay.** There is no code path that submits a claim form or
   sends a message. A letter leaves the engine only as an unsent draft.
2. **It does not hide that it is automated, and it does not answer challenges.** The browser
   declares itself. If a source turns automation away, the engine stops and hands that search to
   you. It does not retry.
3. **It does not hold identifiers.** A Social Security number, taxpayer id, or bank or card number
   in a household file or a letter is refused. You enter those yourself, on the official site.
4. **It does not keep other people's records.** A search by family name returns strangers. Records
   that match nobody in your household are discarded in memory and never written down.
5. **It searches only for people with a recorded consent basis.**
6. **It never calls a match confirmed.** Matches are strong, likely or possible. Only the holder can
   confirm who owns property, after checking identity.
7. **It never adds up money a source did not state.** The headline is a floor.
8. **A letter says only what you attested.** Every personal statement in a draft is filled word for
   word from a fact you put in your household file and attested to, or the letter is blocked.
9. **Text from a source is data.** It is shown with links disarmed, never followed, never used as
   an address, never acted on.

## Try it

Needs the .NET SDK named in [global.json](global.json). Nothing below uses the network: the sweep
replays invented answers from [tests/fixtures](tests/fixtures).

```bash
dotnet build ClaimSeeker.sln -c Release
alias claimseeker='dotnet run --project src/Engine/ClaimSeeker.Engine.csproj -c Release --no-build --'

# PowerShell: function claimseeker { dotnet run --project src/Engine/ClaimSeeker.Engine.csproj -c Release --no-build -- @args }

claimseeker init --workspace ./workspace
claimseeker household import ./workspace/household.example.json --workspace ./workspace
claimseeker sweep --fixtures tests/fixtures/sources --workspace ./workspace
claimseeker findings --workspace ./workspace
claimseeker draft <finding id> --workspace ./workspace
claimseeker report --print --workspace ./workspace
claimseeker audit verify --workspace ./workspace
```

The example household is an invented family. The sweep reports
`At least $1,889.90 across 10 matches the engine trusts`, writes eleven claim packets, and discards
four records that belong to nobody in the household.

`claimseeker sweep --live` is the only command that contacts a real source. It is never implied: a
sweep with neither `--live` nor `--fixtures` is refused. It has not been run, and **it must not be
run yet.** The one source with a playbook is MissingMoney, and that site's published agent policy
does not allow an unattended search: a person has to confirm each one. The engine does not enforce
that yet. [playbooks/missingmoney.md](playbooks/missingmoney.md) has what the policy says, and
[docs/ROADMAP.md](docs/ROADMAP.md) has the gates.

## Verify

```powershell
pwsh scripts/Verify.ps1
```

Builds with warnings as errors, runs the nine offline harnesses, checks their sum against the
pinned total, checks that this file and the project summary report the same counts, and runs the
built engine end to end on recorded answers. `-IncludeAudit` adds a known-vulnerability check, which
needs the network.

| Harness | Checks | What it proves |
|---|---:|---|
| CoreHarness | 151 | Names, amounts and property kinds are read as stated; the household file is strict; identifiers are refused; source text cannot act |
| ScoutHarness | 158 | Name variants, the search plan, the playbook format and its validator, and a runner that follows the playbook exactly and stops when a source says no |
| ScorerHarness | 90 | Who a record belongs to and how far to trust it; what a claim is worth for the time it takes |
| StoreParityHarness | 121 | The in-memory and SQLite stores agree step by step; the audit log notices tampering; a restarted engine picks up the same file |
| GateHarness | 93 | A letter passes only when every personal statement is an attested fact about the right person; no identifier and no unofficial link reaches a letter |
| DispatcherNoSendHarness | 51 | The outbox has one verb, makes an unsent file, and its assembly references no network code |
| LifecycleHarness | 144 | The whole pipeline on both stores: only the gate leads to a letter, only the person can say a claim was filed, a refused source is left alone |
| EngineHarness | 128 | Every command; nothing contacts a source without `--live`; one engine at a time; deletion only on purpose |
| PortalHarness | 45 | The real browser driver against a local page: it declares itself, fills the form, reads the answer from the wire, and stops on silence or a redirect |
| **Total** | **981** | |

The browser harness needs Microsoft Edge or Google Chrome on the machine.

## Layout

| Path | What lives there |
|---|---|
| `src/Core` | Names, money, property kinds, the household file and its validator, facts, identifiers, untrusted text |
| `src/Scout` | Name variants, query planning, the playbook format, the runner, the source map |
| `src/Browser` | The Playwright driver. The only project that references a browser library |
| `src/Scorer` | Matching and ranking |
| `src/Store` | The store interface, in-memory and SQLite implementations, the hash-chained audit log |
| `src/Verifier` | The Attestation Gate |
| `src/Preparer` | Letter templates, claim packets, the findings report |
| `src/Dispatcher` | The draft outbox. No network references |
| `src/Pipeline` | The case lifecycle, sweeps, packets, drafts and marks |
| `src/Engine` | The command line and the workspace |
| `playbooks/` | One recipe per source the engine searches itself |
| `tests/` | Console harnesses and invented fixtures |

## Documents

- [docs/BLUEPRINT.md](docs/BLUEPRINT.md): what the product is, who it is for, and the lines it does not cross.
- [docs/SPECIFICATION.md](docs/SPECIFICATION.md): how it works, in the detail needed to build the rest.
- [docs/ROADMAP.md](docs/ROADMAP.md): the phases, their exit tests, and the decisions that gate them.
- [docs/ClaimSeeker-Project-Summary.md](docs/ClaimSeeker-Project-Summary.md): the living status page.
- [docs/Autonomy-Contract.md](docs/Autonomy-Contract.md): what the engine may do on its own.
- [docs/Playbook-Format.md](docs/Playbook-Format.md): the playbook format, normative.
- [docs/Next-Session-Handoff.md](docs/Next-Session-Handoff.md): what to do next, and what is on hold.
- [CLAUDE.md](CLAUDE.md) and [AGENTS.md](AGENTS.md): rules for coding agents working here.

## Licence

No licence is granted. This code is published for review only; all rights are reserved by its
authors. Do not use, copy, modify or distribute it without written permission.

ClaimSeeker is not affiliated with any government agency, with NAUPA, or with any site it searches.
