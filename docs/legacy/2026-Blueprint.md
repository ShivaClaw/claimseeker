> **Superseded, kept for history. Not guidance.**
> This is part of the 2026 plan for a personal command-line tool, written before ClaimSeeker became a
> commercial product. Where it disagrees with [BLUEPRINT.md](../BLUEPRINT.md),
> [SPECIFICATION.md](../SPECIFICATION.md), [Autonomy-Contract.md](../Autonomy-Contract.md) or
> [CLAUDE.md](../../CLAUDE.md), those documents win. Do not build from this file.

# ClaimSeeker — BLUEPRINT

**Status:** Phase 0 (research + playbook prototyping) complete. This document is the canonical design reference for agents joining the project without prior context.

---

## 1. The Problem

U.S. states collectively hold tens of billions of dollars in unclaimed property (dormant bank accounts, uncashed paychecks, insurance proceeds, utility deposits, safe-deposit contents). Recovering it is free but painful:

- **Fragmentation.** ~50 state databases plus federal sources (TreasuryDirect, DOI, PBGC) plus MissingMoney.com (the NAUPA-backed aggregator, which not all states participate in). No single search is complete.
- **Name-variant blindness.** Databases match on exact-ish strings. Maiden names, middle names, misspellings, and old addresses hide real money.
- **Family scope.** Most people never search for parents, grandparents, or deceased relatives — where the largest balances usually sit.
- **Follow-through friction.** Finding a match is 10% of the work; assembling ID/address/name-change documentation and tracking claim status is the other 90%.
- **Predatory intermediaries.** "Finders" charge 10–35% for work that is free to do yourself. ClaimSeeker exists to make the free path as easy as the paid one.

## 2. The Solution

ClaimSeeker is an agentic pipeline with five stages:

1. **Discover** — automated searches across MissingMoney and per-state portals, expanded across name variants and family members.
2. **Verify** — dedupe and confidence-score matches (confirmed vs. possible; identity verification needed vs. not).
3. **Rank** — order matches by expected value ÷ expected effort (see §5).
4. **File** — generate a per-claim packet: portal link, prefilled data, document checklist, estimated timeline. **A human always performs the final submission.**
5. **Track & Correspond** — monitor claim status; draft follow-up correspondence where a human counterparty exists.

### Scope correction (important — supersedes earlier framing)
The original persona described "mediating negotiations." For **state unclaimed property there is no negotiation**: states pay verified claims through standardized portals. Email drafting and correspondence apply only to the **secondary track**:
- Class-action settlements (classaction.org leads)
- Holder-direct recovery (property not yet escheated to a state)
- Deceased-owner / estate claims requiring documentation exchanges with a state examiner

Do not build a "negotiation engine" for the primary track. Build a **claim-packet generator + status tracker** instead.

## 3. Architecture (target)

```
┌─────────────┐   ┌──────────────┐   ┌───────────┐   ┌──────────────┐
│  Playbooks   │→ │ Search Runner │→ │  Property  │→ │ Rank & Report │
│ (md, per-src)│   │ (Playwright)  │   │   Store    │   │   Engine     │
└─────────────┘   └──────────────┘   └───────────┘   └──────┬───────┘
                                                            ↓
                                          ┌────────────────────────┐
                                          │ Claim Packet Generator  │
                                          │ + Status Tracker        │
                                          │ (human-in-the-loop)     │
                                          └────────────────────────┘
```

**Key pattern — playbooks as data.** Each source gets a markdown playbook (like the existing MissingMoney one) containing: target URL, selectors, API-interception details, timing constants, failure modes. Playbooks are versioned, human-readable, and consumed by a generic runner. This is the project's core IP; the MissingMoney playbook in `ClaimSeeker_Soul.md` is the reference implementation.

**Key pattern — API interception over HTML scraping.** SPA portals render client-side; the reliable data is in the JSON responses. The runner registers response listeners *before* navigation and treats HTML only as an interaction surface.

## 4. Commitments (lock-ins to make now)

| Decision | Commitment | Rationale |
|---|---|---|
| Language/runtime | **TypeScript on Node.js (≥20)** | The proven playbook is already Playwright-JS; typed schemas matter for the data model |
| Browser automation | **Playwright** | Playbook selectors, timing, and `page.on('response')` pattern are Playwright-native |
| Data store | **SQLite (via better-sqlite3 or Drizzle)** — start with JSON files in MVP | Local-first, zero-ops, trivially portable; PII stays on the user's machine |
| Schema alignment | **NAUPA property-type codes** as the canonical taxonomy | Every state reports in NAUPA terms; free interop |
| Playbook format | **Markdown with a fenced YAML/JSON header** | Human-auditable, diffable, agent-writable |
| Execution model | **CLI-first, single browser session per run** | Credit economy (10 credits/session) demands batching; UI comes later |
| Human-in-the-loop | **Hard requirement** for identity documents, form submission, and anything touching CAPTCHAs/consent | Legal, ethical, and practical necessity |

## 5. Tradeoff Space

- **Breadth vs. depth.** MissingMoney gives ~40 states in one search; non-participating states (notably CA, NY, and a few others — verify current list at build time) need dedicated playbooks. Strategy: MissingMoney first, then per-state playbooks ordered by population × non-participation.
- **Automation vs. compliance.** Portals use Cloudflare Turnstile and rate limiting. We wait for invisible challenges to resolve naturally and respect timing constants; we **never bypass or solve bot-detection**, and when a portal hard-blocks automation the playbook must define a manual-fallback path (deep link + instructions for the human). This is a permanent constraint, not a bug to engineer around.
- **Ranking precision vs. data availability.** States often report value ranges ("Over $100") not exact amounts. The ranking engine must work on ranges and property-type priors, not point values.
- **Local-first vs. hosted.** PII (SSNs, IDs, addresses) makes hosted storage a liability. Commitment: local-first; any future hosted version stores only non-PII search results.
- **Session economics.** Each browser session costs credits. Design bias: batch all queries for a run into one session; last-name-only nationwide search + client-side filtering beats N narrow searches.

## 6. Non-negotiable Principles

1. Filing claims is **always free** — the product must never charge a percentage of recoveries and must warn users away from sites that do.
2. Search only for the user and family members the user names; surface consent considerations for living third parties.
3. Possible matches are never presented as confirmed. Confidence labels are mandatory.
4. PII never leaves the user's machine without explicit action by the user.
5. The human submits; the agent prepares.

## 7. Current Assets

- `Unclaimed_Property_search_and_discovery_resources` — vetted source list (usa.gov, TreasuryDirect, DOI, unclaimed.org, MissingMoney, classaction.org).
- `ClaimSeeker_Soul.md` — agent persona + the working MissingMoney playbook (selectors, `/SWS/properties` JSON interception, 5s/5s/20s timing constants, anti-patterns list). Treat as the reference playbook.
