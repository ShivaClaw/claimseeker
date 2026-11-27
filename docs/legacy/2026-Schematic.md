> **Superseded, kept for history. Not guidance.**
> This is part of the 2026 plan for a personal command-line tool, written before ClaimSeeker became a
> commercial product. Where it disagrees with [BLUEPRINT.md](../BLUEPRINT.md),
> [SPECIFICATION.md](../SPECIFICATION.md), [Autonomy-Contract.md](../Autonomy-Contract.md) or
> [CLAUDE.md](../../CLAUDE.md), those documents win. Do not build from this file.

# ClaimSeeker — SCHEMATIC

Step-by-step build instructions for Phase 1–3. Written for an agent (or human) with no prior context. Read BLUEPRINT.md first for the *why*; this file is the *how*.

---

## 0. Prerequisites
- Node.js ≥ 20, npm
- Playwright with Chromium (`npx playwright install chromium`)
- The two Phase-0 assets: the resource list and `ClaimSeeker_Soul.md` (contains the reference MissingMoney playbook — copy it into `playbooks/` verbatim before changing anything)

## 1. Scaffold

```bash
mkdir claimseeker && cd claimseeker
npm init -y
npm i playwright zod commander
npm i -D typescript tsx @types/node
npx tsc --init --strict
mkdir -p src playbooks data/runs data/reports
```

Layout:
```
claimseeker/
├── playbooks/missingmoney.md      # migrated from ClaimSeeker_Soul.md
├── subjects.json                  # user input
├── src/
│   ├── cli.ts                     # commander entry: search | report | rank
│   ├── schema.ts                  # zod types below
│   ├── variants.ts                # name-variant generator
│   ├── runner.ts                  # Playwright session + playbook executor
│   ├── sources/missingmoney.ts    # source adapter implementing the playbook
│   ├── store.ts                   # JSON now, SQLite in Phase 3
│   ├── rank.ts                    # scoring
│   └── report.ts                  # markdown report generator
```

## 2. Data Model (`src/schema.ts`)

```ts
import { z } from "zod";

export const Subject = z.object({
  id: z.string(),
  firstName: z.string(),
  lastName: z.string(),
  middleNames: z.array(z.string()).default([]),
  priorLastNames: z.array(z.string()).default([]),   // maiden/former names
  statesLived: z.array(z.string().length(2)).default([]),
  relationship: z.enum(["self","spouse","parent","sibling","grandparent","other"]),
  deceased: z.boolean().default(false),
});

export const PropertyMatch = z.object({
  id: z.string(),                       // hash(source+ownerName+holder+city+state)
  subjectId: z.string(),
  source: z.string(),                   // "missingmoney" | "ca-sco" | ...
  ownerName: z.string(),
  holderName: z.string().optional(),
  city: z.string().optional(),
  state: z.string().optional(),
  propertyType: z.string().optional(),  // NAUPA description
  valueDescription: z.string().optional(), // often a range: "Over $100"
  valueLow: z.number().default(0),      // parsed floor of the range
  valueHigh: z.number().nullable(),     // null = unbounded ("Over $X")
  confidence: z.enum(["confirmed","likely","possible"]),
  matchedVariant: z.string(),           // which name variant hit
  claimUrl: z.string().optional(),
  discoveredAt: z.string(),
});
```

`subjects.json` is an array of `Subject`. Never commit it (contains PII); add to `.gitignore` along with `data/`.

## 3. Name-Variant Generator (`src/variants.ts`)

For each subject, emit search tuples `{firstName?, lastName}`:
1. `{last}` alone (nationwide sweep — highest recall)
2. `{first, last}` and `{firstInitial, last}`
3. Every `priorLastName` repeated through steps 1–2
4. Middle name as first name (`{middle, last}`)
5. Common-misspelling pass on the surname (double letters, dropped letters, ie/ei swaps) — cap at 3 to control session time

Dedupe tuples across subjects sharing a surname: **one last-name-only search covers the whole family**, then filter client-side. This is the credit-economy optimization from the playbook.

## 4. Runner + MissingMoney Adapter

`runner.ts`: opens ONE browser session per CLI invocation, iterates queued searches, closes. All timing/backoff lives here, not in adapters.

`sources/missingmoney.ts`: implement the reference playbook **exactly** — it encodes solved failure modes:

```ts
// Non-obvious, previously-debugged specifics — do not "improve" these:
// URL: https://missingmoney.com/app/claim-search   (other paths 404)
// Selectors: input[name="lastName"], input[name="firstName"],
//            select[id="state"], button[type="submit"]
// Data: intercept JSON from /SWS/properties via page.on('response')
//       registered BEFORE page.goto — never scrape the HTML
// Timing: 5s after load, 5s after fill (invisible Turnstile settles),
//         20s after submit. Faster = "Too many attempts" rate limiting.
```

Map each element of `properties[]` into `PropertyMatch`: parse `propertyValueDescription` ranges ("Over $100" → low 100, high null; "$50–$100" → 50/100), set `matchedVariant`, compute the dedupe hash.

**Compliance rule (hard):** if the challenge does not resolve on its own or the portal blocks the session, do NOT retry aggressively and do NOT attempt to solve/bypass anything. Emit a `ManualFallback` record: deep link + the exact search terms for the human to run by hand.

## 5. Store (`src/store.ts`)

Phase 1: append each run to `data/runs/<ISO-timestamp>.json`; maintain `data/properties.json` as the merged, deduped set (merge key = the id hash; keep highest confidence on collision).
Phase 3: swap to SQLite with tables `subjects`, `searches`, `properties`, `claims` — same shapes, same ids, so migration is a load loop.

## 6. Ranking (`src/rank.ts`)

```
score = expectedValue / effortHours

expectedValue = valueHigh ? (valueLow+valueHigh)/2
              : valueLow * typeMultiplier      // unbounded ranges
  typeMultiplier priors: securities/insurance 3.0, bank deposits 2.0,
                         paychecks 1.5, utility refunds 1.1

effortHours (base 0.5) +=
  +0.5 different current name (name-change docs)
  +1.5 deceased owner (heirship/probate docs)
  +1.0 state requires notarization at this value tier
  +0.5 manual-fallback source (human runs the search too)

confidence gate: "possible" matches rank below all "likely"/"confirmed"
regardless of score — never let a big speculative number top the list.
```

## 7. Report (`src/report.ts`)

Markdown, in this order (mirrors the Soul's communication style):
1. **Headline total** — sum of expectedValue, stated as a range, with confidence caveat
2. **Top finds** — best 3–5 by score, each with claim link + doc checklist stub
3. **By state** — `CA — $2,400 est. (paycheck + deposit)` style lines
4. **By family member** rollup
5. **Manual fallbacks** — searches the human must run by hand
6. **Standing footer:** filing is always free; never pay a finder's fee; the tool prepares, you submit.

## 8. CLI (`src/cli.ts`)

```
claimseeker search --subjects subjects.json [--source missingmoney] [--state ""]
claimseeker rank
claimseeker report --out data/reports/
```

`search` = variants → dedupe tuples → one runner session → store. `rank` and `report` are offline (no browser, no credits).

## 9. Build Order & Verification

1. Scaffold + schema → `tsx src/cli.ts --help` runs
2. Variant generator → unit test: subject with maiden name yields expected tuple set
3. Store + report with **fixture JSON** (fake `/SWS/properties` payload) → verify report end-to-end before spending any browser session
4. Runner + MissingMoney adapter → first live run: one last-name-only nationwide search
5. Ranking → check top-5 against a hand-ranked control set
6. Then proceed to ROADMAP Phase 2 (playbook file format + CA/NY adapters)

## 10. Guardrails (restated because agents skim)
- One browser session per run. Ever.
- Human submits all claims and provides all identity documents. No auto-fill of SSNs/IDs into portals.
- No bot-detection bypass, no CAPTCHA solving, no aggressive retries. Blocked ⇒ manual fallback.
- PII stays local; `subjects.json` and `data/` are gitignored.
- Possible ≠ confirmed. Label everything.
