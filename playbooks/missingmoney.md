# MissingMoney.com

The multi-state unclaimed property search. It is operated by Kelmar Associates for the National
Association of State Treasurers, in collaboration with NAUPA, and one search covers the programs of
every participating state.

> **Do not run this playbook against the live site.** The header below describes an unattended
> search. The site's published agent policy, read on 2026-11-26, requires a person to confirm each
> search and to complete the bot check themselves. See "What the site allows". The header is kept
> because the offline harnesses replay recorded answers through it, and because its capture and
> result mapping are what an assisted search will need. Changing the engine so that it cannot run
> this source unattended is a named task in
> [docs/Next-Session-Handoff.md](../docs/Next-Session-Handoff.md).

```json
{
  "format": 1,
  "id": "missingmoney",
  "version": 1,
  "source": "missingmoney",
  "mode": "auto",
  "provenance": "Taken from the project's original agent notes, written in July 2026, which record the recipe as working against the live site. Not yet re-run by this codebase, and not to be run as written: the site's agent policy, read on 2026-11-26, does not allow an unattended search. The selectors and timings are known to be out of date in places; see the notes below the header.",
  "allowedHosts": ["missingmoney.com", "www.missingmoney.com"],
  "entry": "https://missingmoney.com/app/claim-search",
  "steps": [
    { "op": "capture", "name": "results", "urlContains": "/SWS/properties", "status": 200, "why": "The results arrive as JSON from this endpoint. The listener has to exist before the page opens." },
    { "op": "goto", "url": "{entry}", "waitUntil": "networkIdle", "timeoutMs": 60000 },
    { "op": "wait", "ms": 5000, "why": "The page is an Angular app. It needs this long to render the form and start its Turnstile check." },
    { "op": "fill", "selector": "input[name=\"lastName\"]", "value": "{familyName}" },
    { "op": "fill", "selector": "input[name=\"firstName\"]", "value": "{givenName}", "when": "givenName" },
    { "op": "select", "selector": "select[id=\"state\"]", "value": "{state}", "when": "state" },
    { "op": "wait", "ms": 5000, "why": "Turnstile settles on its own in this time. A form submitted sooner is answered with 'Too many attempts'." },
    { "op": "click", "selector": "button[type=\"submit\"]" },
    { "op": "await", "capture": "results", "timeoutMs": 20000 }
  ],
  "result": {
    "capture": "results",
    "items": "properties",
    "fields": {
      "ownerName": "ownerName",
      "holderName": "holderName",
      "valueText": "propertyValueDescription",
      "city": "city",
      "state": "state",
      "propertyTypeText": "propertyTypeDescription"
    }
  },
  "pacing": { "minMsBetweenQueries": 30000, "maxQueriesPerSession": 12 },
  "fallback": {
    "deepLink": "https://missingmoney.com/app/claim-search",
    "instructions": "Open the link, type the family name as shown, leave the state on all states and press Search. Each result links to the official claim site of the state that holds it."
  }
}
```

## What the site allows

Read on 2026-11-26, in a browser, by opening pages only. No search was submitted.

**There is no terms-of-use page.** The site's sitemap lists twelve pages and none is a terms page.
Three documents govern access:

| Document | What it is |
|---|---|
| `https://missingmoney.com/robots.txt` | Allows general access and search indexing, refuses a list of named crawlers, forbids use for model training, and points agents acting for a person to the agent policy |
| `https://missingmoney.com/siteai.json` | The site's policy for AI agents, in the A2WF format, last updated 2026-10-22 |
| `https://missingmoney.com/app/privacy-policy` | The privacy policy, last updated 2022-10-06. The agent policy names it as the site's terms |

**The agent policy, in plain words:**

- An agent **may search**, at no more than five searches a minute, and only for an identified
  person looking for their own property or property they have legal authority to claim. An estate
  executor is the example it gives. Searching for third parties, working through lists of names,
  and lead generation are prohibited.
- **A person has to confirm each search.** Search is on the policy's list of actions that need
  human verification, and the method it names is `redirect-to-browser`. Bot-protection challenges
  are the person's to complete. An agent must not solve one, get around one, or replay its token.
- **The agent has to say who it is**: a name, an operator and a purpose. Anonymous agents are not
  allowed.
- **Results are not to be kept.** Owner and claimant records, search results included, may not be
  stored, aggregated, resold or shared beyond the person who asked. The policy's retention line
  says an agent must not retain search results beyond the user session that requested them.
- **No bulk collection**, caching, republication, model training, or lists for finders and
  locators.
- An agent may guide a person to a claim. The claimant completes identity verification and the
  attestation personally. An agent must not submit a claim, an attestation or an identity
  document, and must not create an account.

**About the format.** A2WF is a public draft specification (version 1.0, dated 2026-03-18,
<https://a2wf.org/specification/>). Like robots.txt it works by voluntary compliance. It defines a
per-action rate limit as requests per minute and "human verification" as requiring human
confirmation. It does not define how an agent conveys its name, operator and purpose.

**The privacy policy** says the site is for visitors who enter their details by hand, that queries
and results are not saved by the site, and that using the site is acceptance of the policy.

## What that means for this playbook

ClaimSeeker's own rule is that a source's terms decide its mode. On that rule MissingMoney is not
an automatic source.

| The policy asks for | ClaimSeeker today |
|---|---|
| A search only for an identified person, or someone with authority over their property | Meets it in spirit: every person searched has a recorded basis. "Next of kin" is looser than "legal authority", and the first-run flow should ask which it is |
| A person confirming each search and completing the bot check | **Does not meet it.** This header fills the form, waits for the check to pass by itself and presses the button |
| The agent naming itself | **Does not meet it.** The driver deliberately sets no user agent, so the browser declares automation and nothing more. Naming the agent changes invariant 4 in CLAUDE.md, which is Brandon's decision |
| Results not kept beyond the session or the requesting user | **Unresolved.** The engine keeps the household's own matches on the person's own computer, and later shows them on that person's phone. Whether that is "beyond the session" needs a legal reading, and ideally an answer from the operator |
| No working through lists of names | A sweep by family name alone returns every record under the name. Records that match nobody are discarded unstored, but they are still received. A search by given and family name receives far fewer |
| No more than five searches a minute | Meets it: thirty seconds apart, at most twelve in a session |
| Never submitting a claim or an identity document | Meets it: the engine has no such code path |

The search this source allows is an **assisted** one: the engine opens the official page and fills
in the name, the person presses Search and clears any check, and the engine reads the answer. That
has not been designed in detail or built. Until it is, a person runs this search by hand with the
fallback link.

## What the page looks like now

Seen on 2026-11-26 on `/app/claim-search`, without searching. Several details in the header above
are out of date.

- The fields are `lastName`, `firstName`, `city` and `state`.
- **A state is now required.** The list starts empty, and "Search All States" is the value `ALL`.
  The header selects a state only when narrowing, so as written it leaves a required field empty.
- **The search button is `button#btn-turnstile`.** The page has three buttons of type submit, so
  the header's `button[type="submit"]` no longer names one button.
- The page carries a hidden Cloudflare Turnstile response field, and the search button's own id
  names Turnstile: a bot check accompanies a search.
- **Results are paged**, 20, 40 or 80 to a page. One captured response may be one page and not the
  whole answer.
- The site says exact name matches come first, followed by similar names. It does its own loose
  matching, so searching guessed misspellings may add little.

Whether results still arrive from `/SWS/properties`, and in what shape, cannot be learned without
searching, so it is unknown.

## Where this recipe came from

It comes from the project's original agent notes
([docs/legacy/2026-Agent-Notes.md](../docs/legacy/2026-Agent-Notes.md)), written in July 2026,
which record it as worked out by hand against the live site: every specific in the header is there
because the notes list the approach that failed without it. This codebase has run the recipe only
against a local copy of the page's shape (`tests/PortalHarness`), never against missingmoney.com.

## Why each step is there

**The capture comes first.** The page renders its results from a JSON response at
`/SWS/properties`. A listener registered after the page has opened can miss it. The engine reads
that JSON and never reads the rendered HTML.

**The address is exact.** `/app/claim-search` is the only address with the search form. The site's
home page has a form too, but the notes record that submitting it redirects and clears what was
typed.

**Two pauses, five seconds each.** The first lets the app render and start its Turnstile check. The
notes relied on the second to let that check finish by itself before the form was submitted. That
reliance is what the agent policy rules out: the check is the person's to complete.

**One search at a time, thirty seconds apart, at most twelve in a session.** The source is a public
service run for the states. ClaimSeeker is a guest on it.

## What a search looks like

As the header is written, a sweep searches the family name alone, with no given name and no state.
That one request returns every record under the name, and the engine decides on the person's own
machine which records belong to the household. Records that match no one are discarded, not
stored.

The given-name and state fields are filled only when a sweep's result is cut off and has to be
narrowed. The size at which the site cuts a list off is not known, so `maxItemsHint` is unset.

Both habits need rethinking for this source: the policy's rule against working through names
argues for searching by given and family name, and the form now requires a state.

## When the site says no

If a check does not clear, or the response does not arrive, the engine stops. It does not retry,
wait the challenge out, or answer it. The search is handed to the person with the fallback link
above. Two sweeps in a row that end this way put the source on hold until the person asks for
another attempt.

## What does not work

From the 2026 notes:

- `/en/`, `/en/Property/SearchIndex` and `/app/search`: not found, or the wrong page.
- Reading results from the page's HTML: they are drawn client-side from the JSON.
- Proximity selectors such as `:near()`: they time out.
- The home page form: it redirects and resets the fields.
- More than one browser session at once.

## Not yet known

- Whether results still arrive from `/SWS/properties`, and whether one response is one page.
- The exact text forms of `propertyValueDescription`. The value parser accepts amounts, ranges and
  "over"/"under" bands, and treats anything else as not disclosed.
- Whether a record carries an id of its own. If it does, map it to `sourceRecordId`; until then a
  record's identity is its content.
- How `ownerName` orders its parts. The matcher does not depend on the order.
- How an agent is expected to name itself to this site. The A2WF draft does not say.
- Whether the operator reads "not beyond the session" as covering a person's own copy of their own
  results on their own computer.
