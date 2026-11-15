# MissingMoney.com

The multi-state unclaimed property search endorsed by NAUPA. One search by family name covers the
programs of every participating state, which makes it the first source ClaimSeeker searches.

```json
{
  "format": 1,
  "id": "missingmoney",
  "version": 1,
  "source": "missingmoney",
  "mode": "auto",
  "provenance": "Taken from the project's original agent notes, written in July 2026, which record the recipe as working against the live site. Not yet re-run by this codebase: treat every selector and timing below as due for a live check.",
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

## Where this recipe came from

It comes from the project's original agent notes
([docs/legacy/2026-Agent-Notes.md](../docs/legacy/2026-Agent-Notes.md)), written in July 2026,
which record it as worked out by hand against the live site: every specific below is there because the notes list the
approach that failed without it. This codebase has run the recipe only against a local copy of the
page's shape (`tests/PortalHarness`), never against missingmoney.com, so the first live run is also
the first live check.

## Why each step is there

**The capture comes first.** The page renders its results from a JSON response at
`/SWS/properties`. A listener registered after the page has opened can miss it. The engine reads
that JSON and never reads the rendered HTML.

**The address is exact.** `/app/claim-search` is the only address with the search form. The site's
home page has a form too, but submitting it redirects and clears what was typed.

**Two pauses, five seconds each.** The first lets the app render and start its Turnstile check. The
second lets that check finish quietly before the form is submitted. Both are floors: the runner never
shortens a wait, and a faster submit is rate-limited.

**One search at a time, thirty seconds apart, at most twelve in a session.** The source is a public
service run for the states. ClaimSeeker is a guest on it.

## What a search looks like

A sweep searches the family name alone, with no given name and no state. That one request returns
every record under the name nationwide, and the engine decides on the person's own machine which
records belong to the household. Records that match no one are discarded, not stored.

The given-name and state fields are filled only when a sweep's result is cut off and has to be
narrowed. The size at which the site cuts a list off is not known yet, so `maxItemsHint` is unset
and narrowing does not trigger on its own.

## When the site says no

If the Turnstile check does not clear by itself, or the response does not arrive, the engine stops.
It does not retry, wait the challenge out, or answer it. The search is handed to the person with the
fallback link above. Two sweeps in a row that end this way put the source on hold until the person
asks for another attempt.

## What does not work

- `/en/`, `/en/Property/SearchIndex` and `/app/search`: not found, or the wrong page.
- Reading results from the page's HTML: they are drawn client-side from the JSON.
- Proximity selectors such as `:near()`: they time out.
- The home page form: it redirects and resets the fields.
- More than one browser session at once.

## Not yet known

- The exact text forms of `propertyValueDescription`. The value parser accepts amounts, ranges and
  "over"/"under" bands, and treats anything else as not disclosed.
- Whether a record carries an id of its own. If it does, map it to `sourceRecordId`; until then a
  record's identity is its content.
- How `ownerName` orders its parts. The matcher does not depend on the order.
