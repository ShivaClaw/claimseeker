# Autonomy Contract

**Status: normative.** What the ClaimSeeker engine may do without asking, what it may do only when
asked, and what it may never do. Code that crosses a line in this document is a defect, whatever
the reason.

The whole contract in one sentence: **the engine looks and prepares; the person decides, submits
and reports.**

## 1. Who can do what

| Act | Engine, unasked | Engine, when the person asks | Person only | Nobody |
|---|:-:|:-:|:-:|:-:|
| Plan the searches for the household | yes | | | |
| Search a source that has a playbook, on its cadence | yes | | | |
| Match, rank and store the household's own findings | yes | | | |
| Write a claim packet | yes | | | |
| Tell the person something new was found | yes | | | |
| Draft a letter, through the Attestation Gate | | yes | | |
| Retry a source that has been turning automation away | | yes | | |
| Run a search that needs a sign-in, a challenge or an identifier | | | yes | |
| Add a person to the household, or record a basis for searching them | | | yes | |
| Attest that the household's facts are true | | | yes | |
| Address and send a letter | | | yes | |
| Fill in and submit a claim form | | | yes | |
| Say a claim was filed, is in review, needs documents, was paid or was denied | | | yes | |
| Dismiss a finding as not theirs | | | yes | |
| Delete all data | | | yes | |
| Send a message on someone's behalf | | | | never |
| Submit a claim, sign, attest or pay on someone's behalf | | | | never |
| Answer a CAPTCHA or any other challenge | | | | never |
| Make the browser look less like a program | | | | never |
| Store or transmit a Social Security number, taxpayer id, or account or card number | | | | never |
| Keep a record that belongs to nobody in the household | | | | never |
| Search for a person with no recorded basis | | | | never |
| State that a record belongs to the person | | | | never |
| Follow a link, or use an address, that came from a source's text | | | | never |

## 2. Levels

The engine has two levels. There is no third.

**Level 0, look.** Plan, search, match, rank, store. The only thing that leaves the computer is
the search itself: a family name, and a given name when a cut-off answer is narrowed, typed into
the official source's own search form.

**Level 1, prepare.** Claim packets, the report, and letters as unsent drafts. A letter exists
only when the person asked for it and the gate passed it.

Everything past level 1 belongs to the person. This is the product's design and not a version-one
limit: no later release adds a level that files or sends.

## 3. How each line is held

| Line | Mechanism | Checked by |
|---|---|---|
| The engine cannot report a filing or a payment | `Lifecycle.UserOnly`: those states are refused for the engine actor | LifecycleHarness |
| A letter exists only by passing the gate | `DRAFT_READY` has one way in; `GatePassedDraft` cannot be constructed outside the gate's assembly; the state is tested again before the outbox call | GateHarness, LifecycleHarness |
| Nothing is sent | `IDraftOutbox` has one method; its assembly references no network code | DispatcherNoSendHarness |
| Automation is declared | The driver has no means to alter how the browser presents itself; the runner refuses a browser whose automation signal is off | PortalHarness, ScoutHarness |
| A refusal is final | One attempt per search; a fallback defers the rest of the session; two refused sweeps put the source on hold | ScoutHarness, LifecycleHarness |
| No identifiers | `SensitiveIdentifiers` runs on the household file and on every finished letter | CoreHarness, GateHarness |
| Strangers' records are not kept | Unmatched records are counted and dropped in memory | LifecycleHarness |
| No basis, no search | `Subject.MaySearch` gates the plan and the matcher | ScoutHarness, ScorerHarness |
| No claim of ownership | Three confidence values; templates that ask and do not assert | ScorerHarness, GateHarness |
| Source text cannot act | Disarmed for display; refused in a letter unless inert; links only from the source map | CoreHarness, GateHarness |
| Nothing live by accident | `sweep` requires `--live` or `--fixtures` | EngineHarness |

## 4. The phone

A paired phone is a second screen and a place to tap "filed". It may propose a state for a
finding, and the engine applies it only if the lifecycle allows that move for the person. It may
not add a person, start a search for a new name, change the household's facts, request a letter to
be sent, or cause anything in the "nobody" column. A stolen phone can mark cases; it cannot spend,
send or file.

## 5. Unattended running

When the scheduler exists, it runs level 0 and packet writing on its own, one source at a time, on
each source's cadence. It never drafts a letter unasked, never retries a source on hold, and never
starts a search with a browser the person cannot see unless they chose that.

## 6. Changing this contract

Only Brandon changes it, in a pull request that says which line moves and why. A change that would
move any act out of the "nobody" column is a change of product, not of code, and needs legal review
first.
