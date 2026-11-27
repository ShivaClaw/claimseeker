# Fixtures

Everything here is invented. No name, holder or amount describes a real person or a real record.

`sources/<source>/<FAMILY>.json` is a recorded-style answer for a family-name search, in the shape the
source's playbook expects. `claimseeker sweep --fixtures tests/fixtures/sources` replays them without
touching the network, and the harnesses read them the same way. A file named `<FAMILY>.blocked`
stands for a source that turned the search away.

The household these answers belong to is the example one that `claimseeker init` writes.
