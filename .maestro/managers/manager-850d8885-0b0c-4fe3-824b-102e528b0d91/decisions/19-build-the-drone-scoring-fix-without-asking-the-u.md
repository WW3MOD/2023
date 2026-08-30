# Build the drone scoring fix without asking the user

_Recorded 2026-08-27T02:25:06.650Z by 17dc66e4_

The worker flagged the scoring change as blocked on the user's design call, and decision 18 said the same thing. **Overruled, and the distinction matters.**

## What the measurement established

582 evaluations, ticks 1800–72000, two operators, **674,584 of 674,584 candidate cells refused as too fresh — exactly 100%.** Zero refused by `MaxPoiDistanceCells`, zero by `MaxAirDanger`, zero by the SR exclusion, zero already covered. The last evaluation looks identical to the first: the absorbing state does not break up in late game.

It overturned both live hypotheses. **Mine**: I suspected `ControlField` of counting a weakest-band graze as verification. It does not — `GridCellVisible` → `MapLayers.IsVisible(cell, 1)` resolves to a **strict** `ResolvedVisibility[puv] > visibility` (`MapLayers.cs:579`), because tick stamps every *explored* cell at 1 whether or not anything is looking. So the outermost band (Strength 1, 28c0–32c0) does not verify and the verifying radius is **28, not 32**. The field is innocent. **The worker's**: it had recorded a missing reposition leg; the disc is centred on the operator, so walking moves both together and a reposition leg is *insufficient*, not missing. Its own previous entry would have aimed design work at nothing.

The actual defect is better than either: **the module scored the hover cell by its own staleness, and the hover cell can never be stale by construction.** The drone is not redundant — `quadcopterdrone` inherits `^StandardVision` itself, so parked at the leash edge it verifies its own 28-cell bubble centred 22 cells out, reaching ground the operator cannot see. One scoring function.

## Why this is not the user's call, when the earlier version was

Decision 18 was right at the time: *what a drone is for in this game* is taste, upstream of any number, and the user is the designer. That question dissolved when the cause resolved.

The module's own `[Desc]` already says it sends drones to observe stale ground. Scoring the hover cell instead of the revealed area does not implement a *different* intent — it **fails to implement the stated one**. The user's request was explicit that the experimental bot should use drones. Fixing this executes the request; it does not choose between requests. Asking here would be asking permission for the routine next step of the agent's own workflow, which is the noise the question-routing rule exists to prevent.

The test I applied: *would either answer surprise the user?* Under decision 18's framing, yes — "drones fly to reveal unseen ground" and "drones are redundant, cut them" were both live and the user would have had a view. Now only one answer exists that is consistent with what was already asked for.

## The risk I flagged that the worker had not

**Cost.** 674,584 candidates were scored at one cell each in a single match. Scoring "area this drone would reveal" naively turns each into a set computation, and the 2025-per-operator-per-200-ticks bound was justified to a reviewer on the old cost model. Told it to settle resolution before formula — `ControlField` already works in grid squares and `MinStalenessTicks` is already a per-square notion — and to state the new per-evaluation cost in the same terms the reviewer used, because it will be the first thing asked. A correct and unshippable fix is the failure mode here.

Two measured facts help: `offmap=181734` means a large share of each disc falls off-map and is cheap to skip, and the absorbing state means today's scan already does 674k units of work per match to produce nothing, so almost any bound beats the status quo.

## Held

Merge still held per decision 18 — the bot still buys operators that never launch, and that is exactly what this fix must clear. Also forbade running a match on the first draft: a launch only means something after a reviewer has seen the scoring change, and this branch has now spent two matches learning things a review could not have told us. That budget is better spent on a reviewed fix.

## Recorded separately

Neither match reached a verdict — both TIMEOUT-FAIL, the second at 919 s elapsed with a 900 s cap and `--speed 8` against a 720 s tournament clock, which suggests `--speed` may not do what its help text implies. Filed as its own item, since anything resting on tournament outcomes — benchmarks, the `@stable` baseline — depends on a completion path nobody has watched succeed. The drone finding is unaffected: it rests on per-evaluation behaviour across 70k ticks, not on a completed match.
