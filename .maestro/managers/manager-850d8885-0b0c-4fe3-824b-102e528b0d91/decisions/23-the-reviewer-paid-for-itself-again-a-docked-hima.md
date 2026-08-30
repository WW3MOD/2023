# The reviewer paid for itself again — a docked himars was one merge away from being deleted from the game

_Recorded 2026-08-27T16:49:32.106Z by 17dc66e4_

Adversarial review of `f8b424f6` returned **DO NOT MERGE** with a defect that reading settles and no run could have refuted. Routed the fix back to the same implementer (`376eaf7c`) as a new commit rather than an amend, per the pipeline shape.

## The defect

`Rearmable.cs:88` returns `false` — not complete — for any client `CanSelect` owns, *before* the per-pool affordability check at `:99` that would have ended the errand. But `CanSelect` carries no supply term by design (`SupplyProvider.cs:748`), while the push arm's accept test is strictly narrower: `IsValidTarget` **plus** affordability (`:646`) **plus** `MinNeedThreshold` (`:655`), reached only after a Tick prologue that early-returns on five separate guards including `currentSupply <= 0` (`:444`).

So `CanSelect(c)` does not imply the push arm will ever serve `c`, and once docked, `RearmTick` returning true is the only exit — `Resupply.cs:301` is the sole one, and `SelfAssignedErrandIsOver` at `:240` is gated on `!actualResupplyStarted`. A himars docks, takes its one affordable 1500 round, and sits at 1/2 forever: combat-inert, `IsSeekingRearm` true so `StarvingRecruitGate` withholds it from every bot module. **The steady state the commit's own balance section describes.**

## The reusable lessons

**"Could own" is not "will serve", and a predicate reused across a boundary silently changes which question it answers.** `CanSelect` was correct as an ownership test and fatal as a service test. The implementer verified the thing that was true (it is genuinely the same `IsValidTarget` the sweep calls, not a drifted copy) and inherited the thing that was not (that the sweep's *accept* test is that same call).

**A named Watch is not a diagnosis.** The implementer flagged this exact code path as its weakest point and then guessed the wrong cause — it named range mismatch as prime suspect, and range is the one thing that is safe. Twice today its Watch was where the bug lived; twice the specific mechanism it proposed was not the one. The habit to keep is pointing at the door; the habit not to trust is the key it offers.

**The codebase had already solved this and the change stepped around the guard.** `ChooseAffordableResupplier` guards the identical failure at *dispatch* time, and the doc block describing it (`AmmoPool.cs:374-380`) was the very block the new method displaced. The deferral reintroduced it at *arrival* time, downstream. Worth generalizing: when a change orphans a doc block, read what the block said — this one was describing the bug being reintroduced three lines below it.

**A commit message can assert a falsehood that survives review by sounding like provenance.** The message claimed `SelfAssignedErrandIsOver` already ended a dry-dispatched errand on the first batch, making partial-refill-then-leave the pre-existing norm. True in general, false for exactly the docked case the change creates. It is now on the branch and must be corrected in the fix commit rather than left in history unqualified.

## Other findings routed with the fix

- `structures.yaml:492-493`: an **11× error**, pre-existing but newly load-bearing. Claims a full E3 refill is 5 supply (~450 per Centre); ignores the RPG pool (`Ammo: 1`, `SupplyValue: 50`) which is in his `Rearmable.AmmoPools`. Real figures: 55 and ~41. Harmless while infantry rearmed free; not harmless now.
- Residual free ammunition: `ReloadAmmoPool.remainingTicks` is not reset when the granted condition drops, so it accumulates across grant windows. **Record in `bugs/discovered.md`, do not fix** — a second unmeasured behavioural change beside this one is the thing to avoid.
- Legibility: `WithDecoration@AmmoReplenishing` keys on `replenish-soldiers`, so the replenishing pip goes from every soldier in 4c0 to exactly one at a time. Record, don't fix.

## Run sizing, which is why the ask was held

The reviewer was asked to say which defects a run would catch that reading cannot, and answered precisely: **the wedge is a closed chain of unconditional returns — no run establishes it and none can refute it**, and `adb221ca` already measured that a himars satisfies `CanSelect` at the dock. So no slot was spent proving the defect. Both scenarios need re-pointing from "did it refill free" to "did it undock and leave", which is test design, not a launch; then **one** slot on the re-pointed `test-who-pays-for-a-rearm` catches the wedge and its repair together.

Holding the run ask until the reviewer's verdict was the right call and is worth repeating: asking for slots before review would have spent them proving something reading settled for free.
