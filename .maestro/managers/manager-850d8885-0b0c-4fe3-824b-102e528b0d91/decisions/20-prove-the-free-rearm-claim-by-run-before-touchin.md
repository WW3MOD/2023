# Prove the free-rearm claim by run before touching anything it implicates

_Recorded 2026-08-27T15:24:13.303Z by 17dc66e4_

Worker `376eaf7c` stopped mid-phase-2, as instructed, with a finding that reaches **backwards into a merge I pushed this morning**.

## The claim

Rearming at the Logistics Centre is **free**. `Resupply.cs:131` sets its rearm branch from `Rearmable.RearmActors` membership alone; the rearm itself is `Rearmable.RearmTick` (`Resupply.cs:301` → `Rearmable.cs:57-78`), which calls `GiveAmmo` and never reads a `SupplyProvider`. Nothing downstream charges: `SupplyProvider` implements **neither** `INotifyResupply` **nor** `INotifyDockHost`, the only two hooks `Resupply` fires at the host, whose sole engine-wide implementers are `WithResupplyAnimation` and `WithRepairOverlay`. Supply is spent only by the *push* path — the 4c0 aura for infantry, and among vehicles only `himars` and `iskander`, the two declaring the `replenish-vehicles` condition `MatchClientele` requires. **The other thirteen rearm entirely free.**

## Why it matters more than its own scope

Phase 2 was scoped around bringing `ChooseAffordableResupplier` into the seek path so vehicles would not inherit a shuttle. If rearm is free there is **no shuttle** — a depot below one batch still serves on arrival — and that change would *withhold a journey that would have succeeded*. The worker made the change, saw it, and reverted before committing.

**The same question hangs over `e36ab29a`, which I merged and pushed this morning after three review passes.** Its C1 fix put exactly that chooser into `AutoRearmIfDry`'s Auto arm, justified by a two-depot scenario: a unit stalling beside a depot at 750 while one at 2250 sat eight cells away. If the 750 depot would have served it for nothing, that fix is a regression rather than an improvement, and three reviewers and I all missed it because none of us asked whether the price was ever charged.

I think the merge splits cleanly and told the worker to confirm rather than assume: the **drained-versus-absent** half (a unit waits instead of driving off the map) looks unaffected, because the dispatcher's `CurrentSupply > 0` filter still refuses a zeroed depot as a *destination* — the worker's own correction, that this is a dispatcher filter and not a depot incapacity, is what makes waiting still correct. Only the **affordability pick** is in question.

## Decision: one run before anything

Granted a launch to settle it, and put it ahead of both the extension and the re-examination. The claim is derived from three code sites — strong, but derived — and today has punished that shape at least six times, twice inside this worker's own task. **Require the test, not the argument** is the standing lesson and this is exactly its case.

Design constraints I specified, each closing a way the run could lie: dispatch the vehicle **explicitly** so the dispatcher filter is not what gets measured (that filter is precisely what the worker separated from depot incapacity, and confusing them would reproduce the original error); assert **both** halves, ammunition arriving *and* supply not moving; use one of the **thirteen** rather than `himars`/`iskander`, which take the push path and would confound it; and RED before green. I also asked what it means if no RED can be constructed — a claim that cannot be falsified by a run is worth knowing about as such.

## What I am not deciding

**Whether rearm *should* be free is the user's call, not mine.** Thirteen of fifteen vehicles refilling at no cost is a design fact about the mod's economy, not a bug on its face — it may be an intended affordance. I will put it to them once there is evidence rather than a derivation. Asking now would be asking them to rule on a code reading.

## The pattern worth keeping

The worker's first reading was the *opposite* — that thirteen vehicle types name a depot which cannot serve them — and a curated note at `DISCOVERIES.md:5193` falsified it. It went looking for the thing that would contradict it and found it. That is the behaviour the knowledge bank exists to enable, and it is the third time today a confident reading was overturned *before* shipping rather than after.
