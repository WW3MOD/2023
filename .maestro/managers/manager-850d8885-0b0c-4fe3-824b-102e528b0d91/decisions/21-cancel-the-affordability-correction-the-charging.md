# Cancel the affordability correction — the charging ruling makes the merged code right

_Recorded 2026-08-27T15:52:08.877Z by 17dc66e4_

The user ruled: **"All supply always costs, nothing is free ever."** Free rearm at the Logistics Centre is a hole. I withdrew a run grant I had issued minutes earlier and cancelled the fix it would have verified.

## Why the ruling cancels the fix rather than adjusting it

`1bfd5e2c` teaches `HostCanAffordSomethingWeNeed` that a docking host can **always** serve on arrival. Under a charged economy that is exactly backwards: a drained Centre genuinely cannot serve, so affordability is precisely the right gate for it.

Follow it through and the conclusion is stronger than "adjust the fix": **the regression I diagnosed largely evaporates.** The stranding exists *only because rearm is free*. Once the Centre charges, a unit declining to drive to a depot that cannot serve it is correct, and `e36ab29a`'s affordability pick becomes right rather than wrong. What I had called a fault in the merge was a fault in the economy the merge was reasoning about.

So `main` today is **closer to the intended behaviour than the pre-merge state was** — pre-merge, the unit drove over and got a refill that should never have been free. There is no urgency to patch around it, and shipping a change we would revert within a day would be worse than leaving it.

I was wrong in the previous decision, and specifically wrong in a way worth naming: I told the worker the correction did not depend on the design ruling, because gating on an uncharged price is wrong today whatever we decide tomorrow. That reasoning was sound *given* free rearm as a fixed fact. It stopped being sound the moment free rearm became the thing under repair. **The lesson is that "this is right regardless of the pending decision" is only safe when the pending decision cannot change the fact you are reasoning from** — and here it changed exactly that fact.

The commit stays on the branch, marked superseded at both the message and the code site, so the investigation is legible and the reason it did not land is on the record.

## New scope, and the sequencing constraint that matters

The ruling carries three pieces:

1. **Rearming always draws supply from the rearming actor.** Assigned to the current worker, plan-first: where the charge belongs (ideally one site shared with the already-metered push path), what happens when the depot runs out mid-rearm, and what breaks across the thirteen vehicle types plus infantry at a Centre — including confirming `himars`/`iskander` do not end up paying twice. **Told it not to build until I reply.** Three times on this branch, building before the premise settled would have wasted the work; it caught two of those itself.
2. **Evacuation cashback reduced by ammunition consumed.** Backlogged, sequenced *after* (1), because the two together define what a unit is holding when it evacuates.
3. **Trucks refill the Centre**, default order resupplies the LC, empty truck inverts, force-move inverts, Wrench and Enter cursors. Backlogged with the user's wording verbatim.

**(3) is not optional once (1) lands.** Charging makes a drained Centre genuinely unable to serve; without an inflow, a drained Centre is permanently dead weight and everything depending on it strands. Charging and refilling are two halves of one economy and shipping the first alone makes the game worse. That constraint is written into both backlog items.

## The finding that outlived the pivot

The worker's scenario had **both arms fail identically**, and the reason invalidated the pair: neither tank ever dispatched, because neither ever became *idle*. `Actor.Tick:318` recomputes `wasIdle` from `IsIdle` at the top of each tick, so a unit placed with no activity is already idle on tick one and never satisfies `!wasIdle && IsIdle`.

Its own sentence is the one to keep: *had GREEN happened to pass, I would have shipped a fix on the strength of a control that could not distinguish it.* **A control that cannot fail for the reason under test is worse than no control, because it manufactures confidence.** Second time today — the drone regression test was the first.

And it sharpens the premise this whole branch rests on, in the worse direction: "a dry unit asks exactly once" holds only for a unit that goes dry *while busy*. A unit **already idle** when it runs dry asks **zero** times — the ordinary state of a vehicle holding position that fires its last round. That correction has to lead in `DISCOVERIES.md` rather than trail, because the old wording is now quoted in a commit message on `main` and in decision 20. Under a charged economy, drained depots become common, so a unit that never asks again is a materially worse problem than it was an hour ago.
