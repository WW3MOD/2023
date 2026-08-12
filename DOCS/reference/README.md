# DOCS/reference — the knowledge bank

Curated engineering reference. The value of this folder is that its claims can be **trusted without re-verification** — every rule below exists to protect that property. A wrong claim here is worse than a missing one: agents act on it.

## Current docs

| Doc | Covers |
|---|---|
| [`game-model.md`](game-model.md) | How WW3MOD's gameplay differs from Red Alert — reinforcements, no factories, no tech tree |
| [`supply-route.md`](supply-route.md) | Canonical Supply Route mental model (the recurring AI-design trap) |
| [`economy.md`](economy.md) | Supply/ammo economy |
| [`architecture.md`](architecture.md) | Engine layout, scenario system, custom traits, aircraft movement, suppression/stances, AI config, shadows.bin, asset & audio/music pipeline, networking/NAT, saved games, widget gotchas |
| [`influence-stack.md`](influence-stack.md) | The @experimental influence stack (Stages 0 + A–F): belief store, danger fields, control field, heli/ground danger nav, strategic repoint |
| [`conventions.md`](conventions.md) | WDist, WAngle, YAML idioms, PITFALL comments, engine code rules |
| [`pitfalls.md`](pitfalls.md) | Full PITFALL comment-system spec |
| [`project-assessment.md`](project-assessment.md) | Big-picture assessment, engine-upgrade considerations |
| [`shadow-los-plan.md`](shadow-los-plan.md) | Shadow / firing-LOS roadmap |

## How knowledge gets in (curation flow)

**Workers do not add new knowledge here directly.** The flow:

1. **Capture** — during any task, non-obvious insights go to `WORKSPACE/DISCOVERIES.md`: dated, one entry per fact, with code refs (`file:line` or trait/YAML key) so the claim is checkable.
2. **Promote** — a periodic curation pass (manager-dispatched or user-triggered) verifies each unpromoted entry **against the code, not from memory**, then merges it into the right doc above. Mark the DISCOVERIES entry `[promoted]` (or `[rejected: reason]`). Entries that fail verification never land here.
3. **Seed** — a new subject doc starts with a focused research session dedicated to that one subject. Same standard: every claim cited to code the researcher actually read. A seeded doc gets a header line naming its verification date.

Two exceptions to the no-direct-writes rule:

- **Corrections**: any agent that finds a verifiably wrong statement fixes it on sight (staleness is the enemy; this is how the "13 maps" class of rot dies).
- **Mechanical updates**: renames/moves that break links or paths.

## Standards for content

- **Provenance** — claims that could drift cite where they're checkable: code paths, YAML keys, commit hashes. "The SR uses `ProductionFromMapEdge`" is verifiable; "production feels slow" is not reference material.
- **Date volatile claims** — counts, lists of maps/tests, status statements get a `(as of YYYY-MM)` tag or belong in `WORKSPACE/` instead. Timeless mechanics don't need dates.
- **Reference ≠ tracker** — how systems *work* lives here; what's *in flight* lives in `WORKSPACE/`. If a statement will be false in a month, it's in the wrong folder.
- **Prune on contradiction** — code contradicts doc → fix the doc in the same session or delete the claim. Never leave both standing.
- **One home per fact** — deep-link between docs rather than restating; duplicated facts drift apart.
