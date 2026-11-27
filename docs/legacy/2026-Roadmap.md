> **Superseded, kept for history. Not guidance.**
> This is part of the 2026 plan for a personal command-line tool, written before ClaimSeeker became a
> commercial product. Where it disagrees with [BLUEPRINT.md](../BLUEPRINT.md),
> [SPECIFICATION.md](../SPECIFICATION.md), [Autonomy-Contract.md](../Autonomy-Contract.md) or
> [CLAUDE.md](../../CLAUDE.md), those documents win. Do not build from this file.

# ClaimSeeker — ROADMAP

From current state (Phase 0 complete) to production-ready. Each phase has an exit criterion; do not advance until it's met.

---

## Phase 0 — Research & Playbook Prototyping ✅ COMPLETE
- Vetted free/official source list assembled.
- Agent persona defined (`ClaimSeeker_Soul.md`).
- MissingMoney.com playbook proven: correct URL, form selectors, `/SWS/properties` JSON interception, timing constants, documented anti-patterns.

**Carry-forward lesson:** results live in intercepted JSON, not HTML; register listeners before navigation; respect timing or hit rate limits.

---

## Phase 1 — MVP: Single-Source Search & Report (target: first working build)
**Goal:** One CLI command runs the proven MissingMoney playbook for one or more subjects and emits a findings report.

Must-haves:
- [ ] Repo scaffold (TypeScript, Playwright, CLI entry point)
- [ ] `subjects.json` input format (names, variants, states lived in, relationship)
- [ ] Generic search runner that executes the MissingMoney playbook verbatim
- [ ] Name-variant generator v1 (maiden names, middle-name permutations, initials, common misspellings)
- [ ] Raw results persisted as JSON per run (append-only, timestamped)
- [ ] Markdown findings report: totals headline, grouped by state, confidence labels
- [ ] Manual-fallback output when the portal blocks automation (deep link + human instructions)

**Exit criterion:** a real nationwide last-name search completes in one session and produces a correct, readable report.

## Phase 2 — Multi-Source Coverage
**Goal:** Cover states not in MissingMoney plus federal sources.

- [ ] Playbook file format finalized (markdown + structured header) and MissingMoney playbook migrated into it
- [ ] Verify current list of MissingMoney non-participating states; write playbooks for the top ones by population (California SCO and New York OSC first)
- [ ] Federal playbooks: TreasuryDirect (savings bonds), DOI/OST (Indian trust accounts) where applicable
- [ ] Class-action lead capture from classaction.org (secondary track, read-only)
- [ ] Per-source failure logging so broken selectors are detected, not silently skipped

**Exit criterion:** a subject who lived in CA + a MissingMoney state gets complete coverage in one run.

## Phase 3 — Property Store, Dedupe & Ranking
**Goal:** Turn raw results into a ranked action list.

- [ ] SQLite store with NAUPA-aligned schema (subjects, searches, properties, claims)
- [ ] Dedupe/merge across sources and name variants (same holder + city + amount ⇒ one property)
- [ ] Confidence scoring: exact name + known address > exact name > variant match
- [ ] Ranking engine: expected value ÷ expected effort (see SCHEMATIC §6 for the formula)
- [ ] Report v2: ranked list, "file these first" section, family-member rollups

**Exit criterion:** given messy multi-source results, the top-5 list matches what a careful human would prioritize.

## Phase 4 — Claim Packet Generator (human-in-the-loop filing)
**Goal:** Make filing each claim a 10-minute human task.

- [ ] Per-state claim-requirements knowledge base (docs needed, notarization thresholds, typical timelines)
- [ ] Packet generator: claim portal deep link, prefill data block, document checklist, timeline estimate
- [ ] Special-case packets: deceased owner (probate/heirship docs), name change (marriage cert/court order)
- [ ] Explicit "you submit, we prepare" UX language; fee-warning boilerplate

**Exit criterion:** a non-technical family member can file a claim using only the packet.

## Phase 5 — Tracking & Correspondence (secondary track)
**Goal:** Close the loop from filed to paid.

- [ ] Claim status model (found → packet ready → filed → in review → paid/denied) with manual status updates in MVP
- [ ] Follow-up reminders keyed to each state's stated timeline
- [ ] Correspondence drafting for the cases where humans exist on the other end: state examiners requesting more docs, holders (pre-escheat), class-action administrators. Drafts only — user sends.

**Exit criterion:** at least one real claim tracked from discovery to payment through the tool.

## Phase 6 — Hardening & Ship
- [ ] Playbook health checks (weekly dry-run detecting selector drift)
- [ ] PII audit: confirm nothing sensitive is written outside the local store; encrypt at rest
- [ ] Rate-limit/backoff policy per source, enforced by the runner not the playbook
- [ ] Consent + disclaimer flow (not legal advice; searching living third parties)
- [ ] Packaging: `npx claimseeker` or single binary; README; example subjects file
- [ ] Optional later: local web UI on top of the CLI core

---

## Risks & Watch Items
- **Selector drift** — portals redesign without notice. Mitigation: playbook health checks (Phase 6) and per-source failure logging (Phase 2).
- **Bot-detection escalation** — if a source hard-blocks automation, the playbook's manual-fallback path becomes the primary path for that source. Never engineer around bot detection.
- **State participation changes** — MissingMoney's member list shifts; re-verify each release.
- **Legal posture** — the tool prepares, the human files. Any drift toward auto-submission is a red flag.
