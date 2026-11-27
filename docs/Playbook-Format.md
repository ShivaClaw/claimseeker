# Playbook format 1

**Status: normative.** This document defines what a ClaimSeeker playbook is. The parser and
validator in [src/Scout/Playbook.cs](../src/Scout/Playbook.cs) implement it, and `ScoutHarness`
checks them against it. Where the code and this document disagree, that is a defect: fix both in
one change.

## What a playbook is

A playbook is the recipe for searching one source: the page to open, the fields to fill, the
response that carries the answer, how slowly to go, and what to hand the person when the source
wants a human. It is a markdown file in `playbooks/`, one per source.

- The **first fenced `json` block** is the header. The engine reads the header and nothing else.
- The **prose** around it is for people. It records where the recipe came from, why each step is
  there, what was tried and failed, and what is still unknown. A playbook without that prose is
  incomplete, because the next person to fix it starts from zero.

A playbook is data. It cannot carry code, and the format is built so that it cannot be used to
make the engine look like something it is not.

## The header

```json
{
  "format": 1,
  "id": "missingmoney",
  "version": 1,
  "source": "missingmoney",
  "mode": "auto",
  "provenance": "Where the recipe came from and when it last worked.",
  "allowedHosts": ["missingmoney.com", "www.missingmoney.com"],
  "entry": "https://missingmoney.com/app/claim-search",
  "steps": [ ... ],
  "result": { ... },
  "pacing": { "minMsBetweenQueries": 30000, "maxQueriesPerSession": 12 },
  "fallback": { "deepLink": "https://...", "instructions": "What the person does by hand." }
}
```

| Property | Required | Rule |
|---|---|---|
| `format` | yes | `1`. Any other value is refused. |
| `id` | yes | Lower-case letters, digits and hyphens, at most 48 characters. The registry refers to it. |
| `version` | yes | 1 or more. Raise it whenever a step, selector, timing or mapping changes. |
| `source` | yes | The id of the source in `SourceRegistry`. Same character rule as `id`. |
| `mode` | yes | `auto` or `assisted`. Guided and draft sources have nothing to run and no playbook. |
| `provenance` | no | Free text. Say plainly whether this codebase has run the recipe against the real source. |
| `allowedHosts` | yes | At least one plain host name: lower-case letters, digits, hyphens and dots. No wildcards, no schemes, no ports. |
| `entry` | yes | An absolute address on an allowed host. |
| `steps` | yes | 1 to 40 steps, described below. |
| `result` | yes | How to read the captured answer. |
| `pacing` | yes | How slowly to go. |
| `fallback` | yes | What to give the person when the source says no. |

**Unknown properties are refused**, at every level. A newer playbook cannot be half-read by an
older engine, and a property the validator has never heard of cannot be smuggled in.

The file may be at most 256 KiB.

### Addresses

Every address in a playbook (`entry`, `fallback.deepLink`, a literal `goto` url) must:

- be absolute;
- be on one of `allowedHosts`;
- use `https`, unless the host is a loopback one (`127.0.0.1`, `localhost`, `::1`), which exists for
  harnesses and may use `http`;
- carry no credentials.

## Steps

There are seven kinds of step. The list is closed. There is no step that runs script in a page,
sets a request header, changes the user agent, sets a cookie, or takes a screenshot.

| `op` | Properties | What the runner does |
|---|---|---|
| `capture` | `name`, `urlContains`, `status` (default 200) | Starts listening for a response whose path contains `urlContains` and whose status matches. Only responses from an allowed host count. |
| `goto` | `url` (`"{entry}"` or a literal address), `waitUntil` (`load`, `domContentLoaded`, `networkIdle`), `timeoutMs` (1000 to 120000) | Opens the address. |
| `wait` | `ms` (1 to 120000) | Pauses. The runner never shortens a wait. |
| `fill` | `selector`, `value`, `when` | Types `value` into the field. |
| `select` | `selector`, `value`, `when` | Chooses `value` in a list. |
| `click` | `selector` | Clicks. |
| `await` | `capture`, `timeoutMs` (1000 to 120000) | Waits for the named capture to arrive. |

Any step may carry `why`, a sentence for the next reader. The runner ignores it.

### Inputs

`value` may refer to the search's inputs as `{familyName}`, `{givenName}` and `{state}`. Those
three are the only inputs. A step with `"when": "givenName"` runs only when that input has a value,
which is how one playbook serves both the wide search by family name and the narrowed one.

Selectors are CSS, at most 300 characters.

### Rules the validator enforces on the sequence

1. **Every `capture` comes before the first `goto`.** A listener registered after the page opens
   can miss the response.
2. **A `wait` sits between the last `fill` or `select` and any `click`.** A form submitted the
   instant it is filled is the mark of a program in a hurry, and sources answer it by rate-limiting.
3. There is at least one `goto` and at least one `await`.
4. Every `await` names a capture that was declared, and so does `result.capture`.
5. Capture names are unique. `urlContains` is a path fragment that starts with `/`.

## Result

```json
"result": {
  "capture": "results",
  "items": "properties",
  "fields": { "ownerName": "ownerName", "holderName": "holderName", "valueText": "propertyValueDescription" },
  "maxItemsHint": 500
}
```

- `capture`: which captured response holds the answer.
- `items`: a dotted path from the top of the JSON to the list of records.
- `fields`: record field to the source's property name. `ownerName` is required. The fields a
  playbook may fill are `sourceRecordId`, `ownerName`, `holderName`, `city`, `state`, `valueText`
  and `propertyTypeText`, and no others.
- `maxItemsHint` (optional): the size at which the source cuts a list off, when that is known. A
  list this long is treated as cut off and narrowed by given name.

The answer is read with every limit on: about 8 million characters of JSON at most, at most 5,000 records, each field
cut to 200 characters with control characters removed. An answer with no list where `items` points
is a **changed shape**, reported as a broken playbook. It is never read as "nothing found".

## Pacing

| Property | Rule |
|---|---|
| `minMsBetweenQueries` | 0 to 3,600,000. On any real host, at least 1,000. |
| `maxQueriesPerSession` | 1 to 50. |

The engine enforces both. A playbook cannot ask the engine to go faster than one search a second
against a real source, and the engine waits the stated time between searches whether or not the
playbook's steps would.

## Fallback

`deepLink` is the official page where the person runs the search themselves. `instructions` says
what to type and press. Both are required, because every automated search can end with the source
asking for a person.

## What the runner guarantees, whatever the playbook says

These are the engine's rules, in [src/Scout/PlaybookRunner.cs](../src/Scout/PlaybookRunner.cs). A
playbook cannot turn them off.

- **Allowed hosts only.** Before every `fill`, `select` and `click` the runner checks that the page
  in front of it is on an allowed host. If the source has sent the browser somewhere else, nothing
  is typed and the search ends.
- **Declared automation.** Once the page is open the runner asks the browser whether it is
  declaring itself automated. If it is not, the search is not run.
- **One attempt.** A step that fails ends the search. A source that does not answer in time is not
  asked again.
- **No challenge is answered.** A CAPTCHA, a sign-in or an interstitial is a "no". The search goes
  to the person with the fallback.
- **After one fallback, the rest of the session is deferred.** The engine does not keep knocking.
- **Two refused sweeps in a row put the source on hold** until the person asks for another try with
  `--retry-blocked`.
- **The page's own words are not repeated.** A failure is reported in the engine's words, so a
  hostile page cannot put text in front of the person through an error message.

## Writing a playbook

1. Read the source's terms of use and its robots policy. Record what they allow, with the date, in
   the prose. If they forbid automated searching, the source is a guided one: add a registry entry
   and a checklist, and do not write a playbook.
2. Work the search by hand in a browser with the network panel open. Find the response that
   carries the results. Prefer a JSON response to anything drawn on the page.
3. Write the header. Start with long waits and shorten nothing until a live run shows it is safe.
4. Write the prose: where the recipe came from, why each step, what failed, what is unknown.
5. Add invented fixtures under `tests/fixtures/sources/<source id>/` and harness checks for the
   mapping.
6. The first live run needs Brandon's word (see [AGENTS.md](../AGENTS.md)). After it, update
   `provenance` and raise `version`.

## Versioning

`format` changes only when a header written for the old format would be misread by a new engine.
Adding a step kind, an input or a result field is a format change, and it is made here first.
Format 1 has no step kind beyond the seven above, and a proposal to add one that executes script,
alters a request or alters how the browser identifies itself is out of scope for this product.
