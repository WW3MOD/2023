# Repair the dead frozen-actor loop minimally; do not finish the half-imported upstream design

_Recorded 2026-08-26T23:34:44.282Z by 17dc66e4_

## Correction to decision 11

Decision 11 ruled the fog leak fixed at source and recorded the worker's suspicion that the May commit's symptom was "a conflation of correctly-hidden-under-shroud with a bug." **That is now disproved, and the real cause is bigger than the leak.**

I attached a condition to decision 11: the t=0 regression risk must become a permanent autotest rather than a screenshot. **That condition inverted the conclusion within one run.** It is the single highest-value thing this manager did today, and the reasoning behind it generalises: the worker's own model was plausible, self-consistent, and wrong, and only an executed test could tell the difference. A capture would have proved nothing after the session ended.

## What is actually broken

`FrozenActor.UpdateVisibility()` runs **once**, from the constructor (`FrozenActorLayer.cs:113`). Its only other call site (`:162`) is gated on `UpdateVisibilityNextTick` — declared `:73`, read `:161`, cleared `:167`, and **assigned nowhere in the engine**. So `FrozenState.IsVisible` is `false` forever for every non-ally viewer of every building, and `IsVisibleInner` can never return true.

**There is no first-frame race. There is a dead loop.**

Cause: `71687440` (2026-03-24), a 112-conflict upstream merge, resolved a conflict marker by keeping the new side and dropping the old, deleting the only line that set the flag. The surviving side is a stub — `dirtyFrozenActorIds` is written and never read, fed from `partitionedFrozenActorIds`, which nothing ever fills because `Add` populates the differently-named `partitionedFrozenActors`.

## What this exonerates

- `2d7603bf` (April) was **correct**. It only looked broken because the loop had been dead for three weeks.
- `12a9b91b` (May) was chasing a **real symptom with a wrong model**. Buildings genuinely were invisible; "first-frame race" was the wrong diagnosis. Decision 11's framing of this as the author being confused was unfair and is corrected here.
- The "same defect introduced twice" reading in decision 11 is wrong. It was introduced once, by a merge, and then papered over.

**The generalisable lesson: a large conflict-heavy merge can delete a single line and leave a system that fails silently and looks like a design decision for six months.** Both subsequent authors reasoned correctly from what they could see and both reached wrong conclusions, because the evidence they needed had been deleted rather than changed.

## The ruling

**Minimal repair: reinstate the deleted handler against `partitionedFrozenActors`.** Rejected: finishing the half-imported upstream design.

Why. The pre-merge behaviour is *known-good* — `2d7603bf` demonstrably worked in April — so reinstating returns the engine to a proven state. Finishing the upstream design means reimplementing, from two orphaned identifiers, a shape we do not have the source of; the names imply dirty-tracking for performance, but not what the finished design was. That is a speculative rewrite of the fog pipeline, and a six-month-old regression does not need one. **Restore correctness first; performance is a separate, later, measured decision.**

Orphaned identifiers stay, commented as artefacts of `71687440`. Deleting them would destroy the evidence that an upstream design was partially landed.

## Conditions

- The worker must audit what **else** `71687440` touched in the frozen-actor pipeline — a merge that ate one line across 112 conflicts may have eaten others. Reading the resolution, not just the result.
- The commit sequence must make it legible that removing the short-circuit *alone* makes buildings permanently invisible.
- Both scenarios re-run: `test-building-visible-at-spawn` green on both rungs, `test-unscouted-building-hidden` green on its control **and** its assertion.

## Scale, for the user

Previously recorded as "live updates degrade to remembered images." That understated it. **Fog on buildings has not worked at all since March** — every building has been visible to every player regardless of scouting. The repair restores real building fog for the first time in six months. That is a large, deliberate gameplay change, and the user is actively playtesting. Still judged not to need a gate — it is a bug fix and one revert away — but it must be reported prominently rather than slipped in.
