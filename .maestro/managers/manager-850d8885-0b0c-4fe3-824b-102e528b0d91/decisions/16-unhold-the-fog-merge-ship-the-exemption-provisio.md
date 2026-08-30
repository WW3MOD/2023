# Unhold the fog merge: ship the exemption provisionally rather than wait on the Supply Route ruling

_Recorded 2026-08-27T00:51:57.099Z by 17dc66e4_

Decision 15 posted the SR visibility question to the user and I queued the branch's confirmation runs behind the answer, so one launch pass would cover both. Worker `542d1157`'s standby report broke that reasoning in two places and I reversed within the turn.

## What broke it

**The runs were never gated on the ruling.** I had assumed the re-runs and the SR rung wanted the same window. But the worker proved the deleted statement is unobservable *by construction* — post-deletion `grep` leaves three executable references to `partitionedFrozenActorIds` / `dirtyFrozenActorIds`, all declarations and one constructor, so the set is now write-never and read-never. It then drew the inference I should have drawn: **if a run showed a difference, the correct conclusion would be that the enumeration is wrong, not that the deletion mattered.** No run can be evidence about the deletion. What the runs actually cover is the lambda body it rewrote *around* the surviving flag loop — a live risk that exists today and has nothing to do with the SR.

**And the ruling is free to apply later.** Both existing scenarios are ruling-agnostic — neither asserts anything about an SR — and `test-unscouted-building-hidden` already places an enemy `SUPPLYROUTE` at `64,30` on never-scouted ground, unasserted. The actors are on the map; only the assertion is missing.

So the batching bought nothing, and its cost was holding a six-month regression fix hostage to a balance question that may not be answered for hours.

## What ships instead

Apply the exemption **provisionally** — a line whose effect is a no-op against current behaviour — so the branch merges as a pure bug fix with zero gameplay change riding along. That is exactly the property I chose the question's `default_on_skip` to preserve, and I had already told the user in the question that the fix merges either way. Applying the neutral option makes that true rather than aspirational.

Required of the line: explicitly provisional in both the YAML comment and the commit message, naming that the alternative is a one-line flip and that whoever flips it must also invert the scenario rung. **A provisional line that loses the word "provisional" is precisely how the `return true` being removed here survived six months** — the same failure mode, in the same file, would be an embarrassing thing to reintroduce in the commit that fixes it.

## Why this is not deciding the user's question for them

The question stays open and unchanged. Nothing about the exempt-now path prejudges it: going dark remains one line plus one inverted rung, and the user's answer costs the same whenever it arrives. What changes is only that the *bug fix* stops waiting. If the answer had any cost asymmetry — if exempting were hard to undo, or if it changed behaviour anyone would notice — this would be the wrong call.

## Generalisation

I batched two things because they touched the same file and the same launch window. Neither is a real dependency. **The test for batching is whether one piece genuinely cannot be evaluated without the other**, not whether they are adjacent — and here the worker could show they were independent because it had established what each run could and could not be evidence about. That distinction did the work; proximity had fooled me into thinking there was a dependency.
