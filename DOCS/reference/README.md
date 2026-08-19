# DOCS/reference — the knowledge bank

Curated engineering reference. The value of this folder is that its claims can be **trusted without re-verification** — every rule below exists to protect that property. A wrong claim here is worse than a missing one: agents act on it.

## Current docs

| Doc | Covers |
|---|---|
| [`game-model.md`](game-model.md) | How WW3MOD's gameplay differs from Red Alert — reinforcements, no factories, no tech tree |
| [`supply-route.md`](supply-route.md) | Canonical Supply Route mental model (the recurring AI-design trap) |
| [`economy.md`](economy.md) | Supply/ammo economy |
| [`architecture.md`](architecture.md) | Engine layout, scenario system, custom traits, aircraft movement, suppression/stances, AI config, determinism & sync, shadows.bin, asset & audio/music pipeline, networking/NAT, saved games, replays & build fingerprint, widget gotchas |
| [`missiles.md`](missiles.md) | Missile guidance, launch angles, termination paths |
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

## Four shapes of "confident correctness claim with nothing behind it"

From a 2026-08-10 audit that re-derived ~15 high-consequence absolute claims from current code instead of trusting their citations. Most held; the failures clustered into four shapes. **Accurate line citations are not evidence** — every failure below carried real ones. Each shape comes with a detector to run *before* writing the sentence.

1. **Proof quantified over one code path when several write the same state.** *Detector: for any "X can never happen", list every writer of the state X depends on before writing the sentence. If you enumerated by walking outward from the function you were editing, you did not enumerate.* (This is the shape that let a "provably correct, one winner always emerges" claim survive four months and wrongly close a live game-ending bug — and the same shape that hid `World.EndGame()`'s other callers; see architecture.md §Saved games.)
2. **A claim whose truth is contingent on config the project's own lifecycle is designed to change.** Every "`@stable` is byte-identical because the new field defaults off" statement is true when written and **expires at the next parity promotion** — `b8d2e601` flipped nine such flags at once. Nothing automated detects this: no lint rule, no `make` target, and unit tests cannot see profile YAML. *Detector: if a claim's truth depends on a YAML value, it is a dated observation, not an invariant. Write "as of `<sha>`, `@stable` sets X" — never "`@stable` is frozen". Prefer flag-relative phrasing ("when this flag is off, the path is the pre-feature one"), which stays true across promotions.*
3. **The cited mechanism does not do the work, and the conclusion is true for an unrelated reason.** The most insidious, because everything looks correct until someone edits the *real* guarantee. *Detector: name the line of code you would delete to break the property. If deleting the line you cited would change nothing, you cited the wrong line.* (Worked example: the Supply Route's `Armor: Type: Indestructable` is entirely inert — what actually makes it unkillable is `Targetable: TargetTypes: NoAutoTarget`. Corrected in [`supply-route.md`](supply-route.md) and [`game-model.md`](game-model.md).)
4. **"NUnit-pinned" where the test pins something weaker than, or different from, the claim** — including a green test guarding a branch production no longer reaches. *Detector: name the branch the test executes, and check something still reaches it.*

**The common root: the claim was written at the moment of maximum context and never re-read.** Each was true-ish about the thing its author was looking at and silently universal about everything else. The cheap mitigation is grammatical — prefer *"as of `<sha>`, on the path I traced, X"* over *"X is impossible"*, and reserve absolute words for properties enforced by a guard you can point at, not by the current arrangement of the code.

**Corollary for promotion specifically: a conclusion can outlive its own justification.** A correct verdict resting on an expired premise reads as verified and is not. When you promote an entry, re-check its *reasons*, not just its verdicts.
