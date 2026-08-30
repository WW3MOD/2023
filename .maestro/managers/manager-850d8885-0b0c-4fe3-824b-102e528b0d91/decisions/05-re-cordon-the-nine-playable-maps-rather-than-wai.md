# Re-cordon the nine playable maps rather than waive the lint

_Recorded 2026-08-16T21:06:20.789Z by 17dc66e4_

The user delegated this call outright ("the cordon call (re-cordon vs waive)"). Taking it: **re-cordon.**

## What the check actually requires

`CheckMapCordon.cs:20-24` errors when `Bounds.Left == 0 || Bounds.Top == 0 || Bounds.Right == MapSize.X || Bounds.Bottom == MapSize.Y` — a one-cell border on all four sides between playable bounds and map edge.

## The state, measured rather than assumed

Every shipped playable map declares `Bounds: 0,0,W,H` identical to its `MapSize` — `arena-tank-duel`, `nuclear-winter`, `polar-disorder`, `river-zeta`, `seventh-woods`, `siberian-pass`, `twin-rivers`, `woodland-warfare`, `x-lake`.

**`shellmap-open-field` is the exception, and it is the one that passes:** `MapSize: 92,62` with `Bounds: 1,1,90,60`. So the correct shape already exists inside this mod. The nine are the outliers, not the rule — which is the single fact that decided this.

## What lands inside the new cordon strip

Scanned every actor on the outermost ring of the nine maps: **406 actors total.**

- ~398 are **decoration** — trees (`t01`–`t17`, `tc01`–`tc05`), `v17` civilian buildings (102 of them), `rice`, `wood`. Props in a border strip you can see but not play in is precisely what a cordon *is*.
- **8 are gameplay actors and must move inward one cell: 6 `spawnarea` and 2 `oilb`.**

Per map: river-zeta 196, woodland-warfare 115, siberian-pass 36, twin-rivers 20, polar-disorder 17, nuclear-winter 12, x-lake 7, seventh-woods 3, arena-tank-duel 0.

So the job is: nine `Bounds:` lines, eight actor relocations, nav-guard to prove no reachable ground was lost.

## Why not waive

The waive case is real — the rule is a validator requirement, not an observed in-game fault, and `4f67b375` recorded it rather than fixing it. But:

1. **"Nothing has visibly broken" is weak evidence, and I have been taught that twice today.** The stability sweep found an *armed but unloaded* crash mechanism precisely where nothing had broken yet; the packaging gate survived for months because every machine already had the content it never installed. A cordon guards code that expands a cell region by one (`CellRegion.Expand`) — absence of reports is not absence of hazard.
2. **The cost of the red gate is the actual release risk.** The audit's own framing: *"a permanently-red gate trains everyone to ignore the one check that would catch a real content regression."* CLAUDE.md points every worker at `make test`, and it has been red on main since the bounds expansion. That is worse than the missing cordon.
3. **Waiving is mod-wide and one-way in practice.** Once maps ship with bounds == MapSize and players learn the edges, shrinking later changes gameplay under them. Re-cordoning now is the cheap moment.
4. The cost is one cell per side on maps of 66–130 cells — under 5% area, mostly water/tree border.

## Risk I am accepting

I have not verified that the 8 gameplay actors have somewhere sensible to move to, nor that no map has a spawn whose fairness changes when the playable rectangle shrinks. Both go into the worker's brief as explicit checks with authority to come back rather than force it. If any map cannot be re-cordoned without moving a spawn in a way that changes balance, that map is the exception to escalate — not a reason to waive the rule for all nine.
