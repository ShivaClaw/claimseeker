# ClaimSeeker Blueprint

What the product is, who it is for, how it makes money, and the lines it does not cross.

| | |
|---|---|
| Written | 2026-11-15 |
| Replaces | The ClaimScout blueprint, roadmap and schematic of 2026, which described a personal command-line tool |
| Companions | [SPECIFICATION.md](SPECIFICATION.md) for how it works; [ROADMAP.md](ROADMAP.md) for the order of work |
| Status of the code | Engine core built and checked offline. The engine has never been run against a real source. See [ClaimSeeker-Project-Summary.md](ClaimSeeker-Project-Summary.md) |

Decisions marked **(recommended)** are proposals waiting for Brandon's word. They are collected in
section 12 of the specification.

---

## 1. The product in one page

**The problem.** Governments and companies in the United States hold tens of billions of dollars
for people they could not find. A bank account that went quiet, a final paycheck sent to an old
address, a utility deposit, a life insurance payout nobody knew to claim, stock from an employer
that was bought, the surplus when a foreclosed home sold for more than the debt. The money is
real and the claim is free. But it sits behind hundreds of separate searches, each with its own
form and its own deadline, and it is filed under whatever name and address the holder had at the
time. Nobody tells you to look, and looking properly is a weekend of work that has to be repeated
every year.

**The product.** ClaimSeeker is a program that does the looking. It runs on your own Windows or
Mac computer. You tell it who is in your household, every name each person has used and every
place they have lived. It searches the official sources on a schedule, decides which records are
plausibly yours, tells you what each is worth and how much work the claim is, and prepares the
paperwork. When something new turns up, the phone app tells you.

**The promise.** It prepares; you review and submit. It never files a claim, sends a message or
handles a Social Security number. It never takes a share. Filing a claim is free, and ClaimSeeker
says so every time it shows you one.

**The shape.**

- A **paid engine** for Windows and macOS. It holds all the data and does all the work.
- **Free companion apps** for Android and iPhone. They pair with the engine by QR code and show
  what it found. On their own they are a demonstration and a checklist of official places to look.
- Two small services: a **licence check** that knows a licence id and nothing else, and a **blind
  relay** that passes encrypted messages between the engine and the phone and cannot read them.

This is the same shape as CareerSeeker, on purpose. The two products can share an engine shell, a
relay, a licence service and an app shell.

## 2. Who it is for

- **Anyone with a history.** People who have moved between states, changed jobs, changed their
  name, closed accounts in a hurry, or been through a foreclosure or a bankruptcy.
- **People settling a parent's affairs.** Property in the name of someone who has died is common
  and hard to find, because nobody is left who remembers the accounts.
- **Households.** One person runs it for a spouse, parents and adult children who have agreed.

It is **not** for professional finders. ClaimSeeker searches only for people the account holder is
entitled to search for, with a recorded basis for each, and it is not built to work a list of
strangers' names.

## 3. The lines it does not cross

Each of these is enforced in code and checked by a harness. They are the product, not a wrapper
around it: in a market full of services that charge people for their own money, a tool that is
provably on the person's side is the thing worth paying for.

| # | Line | Why it matters commercially, not only ethically |
|---|---|---|
| 1 | **The engine prepares; the person submits.** No claim is filed, no message sent, nothing signed or paid by the program. | A claim is a statement under penalty of perjury. Software that files for people is a regulated business; software that prepares paperwork is tax-prep software. |
| 2 | **Automation is declared, and a refusal is final.** The browser never disguises itself, never answers a challenge, never retries a source that said no. | The sources are public services. Staying a welcome guest is what keeps the product working. Circumvention is where the legal trouble starts. |
| 3 | **No identifiers.** No Social Security number, taxpayer id, or account or card number is ever stored or put in a letter. | The worst breach this product could have is the one it is built to be unable to have. |
| 4 | **Strangers' records are discarded.** A search by family name returns other people. What matches nobody in the household is never written down. | The engine never becomes a database of other people's property. |
| 5 | **No consent, no search.** Every person searched has a recorded basis: self, consent given, guardian, or next of kin of someone who has died. | Keeps the product out of the hands of people fishing for someone else's money. |
| 6 | **Possible is not confirmed.** Matches are strong, likely or possible. Only the holder confirms ownership. | A product that overstates gets one angry review per false hope. |
| 7 | **Honest totals.** The headline adds up only amounts a source actually stated, for matches the engine trusts. | "At least $1,889.90" that turns out to be more builds trust. "$12,000" that turns out to be $40 destroys it. |
| 8 | **A letter says only what the person attested.** Every personal statement in a draft comes word for word from the household file, or the letter is blocked. | The Attestation Gate is the reason a person can sign what the engine wrote. |
| 9 | **Source text is data.** Text that came from a source is shown with links disarmed and is never followed, trusted or acted on. | A holder name is a free-text field anyone can file. It must not be able to phish the user through us. |
| 10 | **Local first.** Household, findings, packets, drafts and the audit log stay on the person's computer. | There is no server holding a list of people and their unclaimed money. |
| 11 | **Free-claims messaging.** Every finding says filing is free and nobody needs a share. No copy implies government affiliation. | State regulators warn consumers about exactly the opposite pattern. |

## 4. What money exists, and how ClaimSeeker reaches it

### Four ways of working with a source

Every source has a mode. It is a field in the source map, and the engine obeys it.

| Mode | What the engine does | What the person does |
|---|---|---|
| **Auto** | Runs the search itself, from a playbook, at the playbook's pace | Nothing, unless the source turns automation away |
| **Assisted** | Opens the official page and fills in the name | Clears any challenge and presses search |
| **Guided** | Gives the official address and a checklist, and tracks that it was done | Runs the search. Used wherever a sign-in or a Social Security number is needed |
| **Draft** | Writes a letter | Reads it, addresses it and sends it |

A source the map marks auto or assisted is treated as guided until it has a playbook. Today one
source has a playbook.

### The source map

This is the map in [src/Scout/SourceRegistry.cs](../src/Scout/SourceRegistry.cs). "Planned" means
the engine knows the source and shows it to the person as somewhere to look, and nothing more.

| Source | What is there | Target mode | Today |
|---|---|---|---|
| MissingMoney.com (NAUPA) | One search across the participating state programs | Auto | Playbook written, never run live by this code |
| State unclaimed property programs | The same property with exact amounts, and where the claim is filed | Guided, then Auto or Assisted by state | Planned |
| County foreclosure and tax-sale surplus | The difference when a home sells for more than was owed | Guided, then Auto by county | Planned |
| DOL Workers Owed Wages | Back wages collected for workers who could not be found; searched by employer | Auto | Planned |
| U.S. Courts Unclaimed Funds Locator | Undelivered money from bankruptcy cases | Assisted (the search has a CAPTCHA) | Planned |
| PBGC unclaimed retirement benefits | Pensions from plans that ended | Auto | Planned |
| FDIC and NCUA unclaimed funds | Deposits from banks and credit unions that failed | Auto / Guided | Planned |
| DOL Retirement Savings Lost and Found | Old employer retirement plans | Guided (Login.gov) | Planned |
| Treasury Hunt | Matured savings bonds | Guided (needs a Social Security number) | Planned |
| NAIC Life Insurance Policy Locator | Policies on a relative who has died | Guided (insurers answer the person) | Planned |
| FHA mortgage insurance refunds, VA insurance funds | Federal refunds and insurance money | Guided | Planned |
| IRS refunds | Undelivered and never-claimed refunds | Guided (needs a Social Security number) | Planned |
| Individual Indian Money accounts | Trust accounts whose owners cannot be found | Guided | Planned |
| Class action settlements | Open settlements a person may qualify for | Guided | Planned |
| The holder itself | Money a company has not yet turned over to a state | Draft | Letter templates built |

### What the brief asked for, source by source

| The brief said | Where it lives | Honest note |
|---|---|---|
| Inheritance | Searches in the name of a relative who has died, the policy locator, and an heir's letter | Probate court dockets are a later phase |
| Refunds, utility company refunds | State programs, where deposits and refunds end up, and letters to the holder | |
| Taxes | IRS refunds, guided | The IRS search needs a Social Security number, so the person runs it |
| Dividends | State programs (securities) and letters to transfer agents | |
| Class-action settlements | A guided list with deadlines | A claim form is sworn. The person decides whether they qualify |
| Foreclosure surplus | County surplus, the most differentiated source and the most work | Every county publishes differently |
| Lottery winnings | **Not covered** | Prizes are claimed with a ticket, not a name. There is nothing to search, and the product should say so |

### Three facts that shape the design

- **Always on does not mean always searching.** State programs take in property on a yearly
  reporting cycle. The engine runs each source on its own cadence, from two weeks for class actions
  to a year for savings bonds. What justifies an always-on engine is the sources with deadlines:
  county surplus, back wages, settlements.
- **Deadlines are real.** Back wages go to the U.S. Treasury after three years. County surplus
  moves to the state after a period that differs by county. A finding with a close deadline goes to
  the top of the list.
- **Coverage claims go stale.** As of 2024, 49 states were reported to take part in MissingMoney
  and Hawaii did not. The older project notes said California and New York did not take part. That
  has to be checked again at build time and never hard-coded into copy.

## 5. How it works, from the person's side

1. **Install the engine** and describe the household: each person, the names they have used, where
   they have lived, and the basis on which you may search for them. You attest that it is true.
2. **The first sweep** runs while you watch. The engine searches each family name once, nationwide,
   and sorts out on your machine which records are plausibly yours.
3. **The findings** arrive in the order worth acting on: what the money is likely worth for the
   time the claim will take, with anything on a deadline first and anything merely possible last.
4. **Each finding has a claim packet**: what was found, how sure the match is and why, where the
   official claim form is, what to have on hand, and how long it should take.
5. **Where a letter helps**, to a company, a county office or a settlement administrator, the
   engine drafts one. It appears as an unsent draft. You read every line, add the address and send
   it yourself.
6. **You tell it what happened**: filed, in review, more documents needed, paid, denied, not mine.
   The engine cannot say any of those for you.
7. **It keeps watching.** New records, new sources and approaching deadlines reach you on the
   phone.

## 6. System shape

```
  YOUR COMPUTER (Windows or macOS)                              YOUR PHONE
 +----------------------------------------------+            +------------------+
 |  ClaimSeeker engine            (paid)        |            |  ClaimSeeker app |
 |                                              |  encrypted |  (free)          |
 |  household -> plan -> sweep -> match -> rank |  end to    |  findings, alerts|
 |      -> packet -> gate -> draft -> track     |  end       |  marks, checklist|
 |                                              |            +---------+--------+
 |  SQLite store, audit log, packets, drafts    |                      |
 +----+------------------+----------------------+                      |
      |                  |                                              |
      | searches         | licence id only                   +---------+--------+
      v                  v                                   |  blind relay     |
 official sources    licence service  <- web checkout        |  cannot read     |
 (your own browser,  (signed tokens)     (merchant of        |  or forge        |
  declared, paced)                        record)            +------------------+
```

| Piece | Job | Status |
|---|---|---|
| Engine core | Everything in the box above, driven from a command line | Built, offline |
| Desktop shell | Tray app, local dashboard, scheduler, installer, updates | Designed |
| Playbook feed | Signed playbook updates, so a changed site is one file to fix | Designed |
| Licence service | Sells and renews licences; the engine verifies them offline | Designed |
| Relay | Passes encrypted envelopes between engine and phone | Designed; CareerSeeker's protocol is the starting point |
| Phone apps | Viewer, alerts, marks, guided checklist, demonstration mode | Designed |

## 7. Business model

**Paid engine, free apps, sold on the web.** The engine is licensed. The apps are free downloads
with no purchase flow inside them. Licences are sold on the website through a merchant of record,
which also handles sales tax. This mirrors CareerSeeker, and the two products can share the
licence service.

**What is free and what is paid (recommended).** Sell the engine's work. Never sell the sight of
the money.

| Free, with no account | Paid |
|---|---|
| Install the engine and describe a household | Automatic sweeps on each source's cadence |
| Run a sweep by hand | Claim packets |
| See every finding, how sure it is, what it is worth, and the official place to claim it | Letter drafts |
| The guided checklist of official sources | Case tracking and deadline reminders |
| Export and delete all data, and verify the audit log | Pairing with the phone, and alerts |

The reasoning: charging to reveal a finding is the pattern regulators warn about and the one a
finder's fee most resembles. Showing the number is also the best advertisement the product has. A
trial of the paid features, without a card, starts on first run.

**One-time or subscription (recommended).** An annual household licence, with a one-time perpetual
licence at about three times the annual price for people who refuse subscriptions. The case for
annual is real: sources refresh yearly, new property is reported every year, and playbooks need
upkeep whenever a site changes. Starting hypotheses to test, not believe: $59 a year, $9.99 a
month, $149 once.

**Never a percentage.** Many states cap what a finder may charge, commonly at 10 percent, and
regulate the agreements. ClaimSeeker charges a flat price for software, takes no share, holds no
money and files nothing. That is a different business and it must stay one.

**Costs.** The engine runs on the customer's machine and uses no paid model or API in version 1,
so there is no per-user compute. The real costs are playbook upkeep, support and the two small
services.

## 8. Why a desktop engine

Every competitor surveyed in October 2026 was a phone app backed by a cloud service. A desktop
engine is more to install, so it has to earn that:

- **Your data never leaves.** Names, addresses and family history stay on your computer. There is
  no account and no server-side profile.
- **Your own connection.** Searches come from your own browser on your own network at a human
  pace, as you would run them, not from a data centre working through a list.
- **Family scale.** Dozens of name variants across many sources is cheap on your own machine and
  expensive for a cloud service per customer.
- **Your own mailbox.** Drafts land where you already write.
- **It can be checked.** The audit log is on your disk and verifies itself.

## 9. Compliance posture

Not legal advice. A lawyer should review this before launch, state by state.

- **Not a finder.** Flat fee, no share, no custody of funds, no submission, no power of attorney.
- **Terms of use.** Each source's terms and robots policy are read before a playbook is written,
  and recorded in it. Where a source forbids automated searching, it is a guided source.
  **MissingMoney's terms have not been read yet. That is a gate before the first live run.**
- **No implied affiliation.** The name, the copy and the store listings stay clearly third-party.
- **Consent.** A recorded basis for every living person searched. For someone who has died, next
  of kin; the proof of heirship belongs in the claim, not in ClaimSeeker.
- **Class actions.** The person reads the eligibility terms and decides. No "claim everything"
  button, ever.
- **Privacy.** The services hold a licence id, push tokens and encrypted blobs. Nothing searchable.
- **Marketing.** No promise of money. Results are a floor, labelled as such.

## 10. Risks

| Risk | Answer in the design |
|---|---|
| A source blocks or forbids automation | Per-source mode; a refusal ends the search and hands it to the person; two refusals put the source on hold; guided mode always works |
| A site changes and a playbook breaks | A changed answer is reported as a broken playbook, never as "nothing found"; playbooks are data and ship as signed updates |
| False hope from weak matches | Three levels, place decides trust, possible matches never top the list or count in the total |
| A hostile record (a holder name that is a phishing link) | Source text is disarmed and never becomes a link or an address |
| A letter that says something untrue | The Attestation Gate; fixed templates; no generated prose |
| Identity data breach | Nothing worth stealing is stored: no identifiers, no documents in version 1 |
| App store rejects a free app tied to a paid desktop product | The app is useful alone (demonstration and guided checklist) and contains no purchase flow; fallback is to offer the purchase in-app as well |
| Regulatory reading of the product as a finder | Flat fee, no submission, legal review before launch |
| The desktop install is too much friction | Free first sweep shows a dollar figure within the first session |

## 11. What ClaimSeeker is not

- Not a claims filer, an agent or an attorney.
- Not a government service and not affiliated with one.
- Not a crawler. It searches a fixed map of official sources and links only to those.
- Not a people-search tool. It will not search for someone without a recorded basis.
- Not a mediator. For state unclaimed property there is nobody to negotiate with: you file and the
  state pays. The older project notes said "mediate the negotiations"; the product is "prepare,
  draft, track".

## 12. Sources for the dated facts

Researched 2026-10-23. Re-verify before any of it reaches marketing copy.

- Scale of what states hold, reported as about $70 billion:
  [NBC Los Angeles, "States Have $70 Billion in Unclaimed Assets"](https://nbclosangeles.com/news/business/money-report/states-have-70-billion-in-unclaimed-assets-how-to-check-if-any-is-yours/3086368)
- MissingMoney participation: [MissingMoney.com, Wikipedia](https://en.wikipedia.org/wiki/MissingMoney.com)
- Retirement Savings Lost and Found, launched December 2024, Login.gov sign-in:
  [PLANSPONSOR](https://www.plansponsor.com/?p=183663),
  [Pension Rights Center](https://pensionrights.org/dol-introduces-lost-and-found-database/)
- County surplus practice and deadlines:
  [Indiana Lawyer on Allen Superior Court](https://theindianalawyer.com/?p=170261),
  [ABC15 Arizona](https://www.abc15.com/news/let-joe-know/foreclosure-you-may-be-entitled-extra-money),
  [Cuyahoga County Clerk of Courts, Excess Funds](https://www.cuyahogacounty.gov/coc/excess-funds)
- Back wages held three years:
  [WTTW](https://news.wttw.com/node/46483),
  [News is my Business](https://newsismybusiness.com/u-s-department-of-labor-seeks-to-return-3m-owed-to-puerto-rico-workers)
- Bankruptcy unclaimed funds search: [U.S. Courts Unclaimed Funds Locator](https://ucfl.uscourts.gov/about?court=ksb)
- Finder fee limits:
  [California State Controller, About Investigators](https://sco.ca.gov/upd_investigator_about.html),
  [Rhode Island General Treasurer testimony on H7569](https://rilegislature.gov/Special/comdoc/House%20Judiciary%202024/03-05-2024--H7569--The%20Honorable%20James%20A.%20Diossa,%20RI%20General%20Treasurer.pdf)
- Companion apps and store rules:
  [Apple Developer Forums on guideline 3.1.3(f)](https://developer.apple.com/forums/thread/781935)
- The project's own vetted list of official sources is in the Claude Project, "Unclaimed Property
  search and discovery resources".
