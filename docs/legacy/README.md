# Earlier project notes

These four files are the project as it stood before November 2026: a plan for a personal
command-line tool in Node and TypeScript, and the agent notes that hold the original MissingMoney
recipe. They are kept so that the history can be checked. They are **not** guidance for building
ClaimSeeker.

| File | What it was |
|---|---|
| [2026-Agent-Notes.md](2026-Agent-Notes.md) | The original agent persona and the MissingMoney recipe, written in July 2026. `playbooks/missingmoney.md` is taken from it |
| [2026-Blueprint.md](2026-Blueprint.md) | The earlier design |
| [2026-Roadmap.md](2026-Roadmap.md) | The earlier phases |
| [2026-Schematic.md](2026-Schematic.md) | The earlier build steps, for Node and TypeScript |

The project was first called ClaimScout. In October 2026 the name was changed throughout these
files and stray backslash escapes were removed from the agent notes. Nothing else was altered,
apart from the notice at the top of each.

## Where they are wrong for the product now

- They describe an agent that helps file claims. ClaimSeeker never files, sends or signs.
- They speak of "confirmed" matches. The engine cannot confirm ownership; it says strong, likely
  or possible.
- They lead with a grand total. ClaimSeeker's headline is a floor of stated amounts.
- They leave pacing and manners to the script. The engine owns them now.
- They assume some states are missing from MissingMoney. That was reported differently by 2024 and
  has to be checked at build time.
- The agent notes address the reader as an agent and give it instructions. Those instructions do
  not apply to anyone working in this repository.
