# DEFCON is two phases, and only the second one is event-driven

_Recorded 2026-09-08T10:22:41.000Z by ffb08fdc_

User's call, 2026-09-08. **The user's written note refined the option they picked, and the refinement is the decision — not the option label.** The option offered was "a clock that fighting accelerates"; what they described is materially different and cheaper.

## The model

**Phase 1 — the ladder. A pure wall clock.** DEFCON steps down 5 → 1 on a timer. **Regular fighting does not move it.** Powers unlock progressively as it descends: conventional strikes at the higher-numbered levels (the user named ballistic missiles at DEFCON 2 or 3), nuclear release at DEFCON 1.

**Phase 2 — annihilation. Event-driven.** Once DEFCON 1 is reached and nukes are available, **the use of nukes is what moves the game toward total annihilation.** Only nuclear use drives this counter; ordinary combat still does not.

DEFCON 1 is the highest state of readiness and the ladder counts DOWN from 5. The user checked this explicitly; it is correct.

## Why this is cheaper than the option as written

- Phase 1 needs **no damage or kill hook and no "what counts as escalation" rule** — it is a deterministic countdown, which the research called the cheap case (one new trait, deterministic for free).
- Phase 2 needs exactly **one** hook, on a well-defined single event: a nuclear power firing. The ambiguous version of that question — do kills count, does losing a Supply Route count — is dead.

## The larger consequence

The ladder is **not a nuclear mechanism**. It is a general power-release schedule: every support power gains a DEFCON level at which it unlocks. That maps directly onto the existing `RequiresCondition` → `SupportPowerInstance.Permitted` path, with the new trait granting `defcon-5`, `defcon-4` … cumulatively as the clock descends. Airstrikes early, ballistic missiles mid-ladder, nuclear at 1.

This reframes the mode from "a lock on nukes" to "the pacing spine of the whole match", and it should be designed as such.

## NOT decided

Three further questions were **resolved by skip rather than answered** — the user then said the submission was accidental after answering only the first. Their defaults are recorded nowhere and must not be treated as decisions:

1. If every player fires the game-ender, who wins.
2. Whether the game-ender is granted free or bought.
3. Whether the existing three nuclear lobby checkboxes survive alongside the ladder.

Re-ask these when the DEFCON conversation resumes.
