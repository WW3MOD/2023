# Recon — ground movement freedom & locomotion (2026-07-28, main @ 33747425)

Read-only recon (worker 0a57d28a, engine-modernization study). All claims code-verified with file:line by the recon worker; line refs as of main @ 33747425.

## Executive summary

**Curved turning between cells already exists** (elliptical arc in `Move.MovePart`, `Move.cs:445-469,544-556`). Remaining grid-feel comes from three separable sources, cheapest first:

1. **Standstill stop-turn + slow `TurnSpeed`** — from a stop, a vehicle turns in place before moving (`Move.cs:210-216`); `TurnSpeed` is low (20/1024 wheeled, 10 heavy — `vehicles.yaml:40`, `vehicles-russia.yaml:334`). Pure YAML tuning.
2. **8-direction paths** — pathfinder only emits steps along the 8 `CVec.Directions` (`CVec.cs:66-76`, `DensePathGraph.cs:75-86`); any "diagonal-ish" route is a 45° zig-zag. The **dominant** visual grid artifact; structural.
3. **Vehicles can't cut corners or use subcells** — one vehicle per full cell (only `foot*` locomotors have `SharesCell` in world.yaml).

**Cheapest change that actually reduces grid-feel: interpolation-level corner-cutting (string-pulling) of the WPos path while leaving pathfinding and cell occupancy untouched.** Units already move continuously in WPos between cell centers (`Move.cs:543-563`); aiming the segment at a farther waypoint when the straight line is passable removes the zig-zag without touching pathfinder, collision, or determinism. One-line-YAML first step: raise `TurnSpeed`, drop `TurnSpeedLoss` so existing arcs read as smooth sweeps.

## Q1 — Pipeline anatomy

Order: `Mobile.ResolveOrder` (`Mobile.cs:986-1013`) → `WrapMove(new Move(...))`. Pathfinder: `HierarchicalPathFinder` over grid A\* on `MapPathGraph : DensePathGraph` — **pure cell grid**, 8-neighbor expansion (`DensePathGraph.cs:107-135`), diagonal cost ×√2 (`:194-198`), no subcell nodes. Move activity: `Move.Tick` (`Move.cs:156`) pops one cell (`PopPath` `:245-353`), computes facing (`:186`); if facing differs → queue `Turn`, return (`:210-216`); else `MoveFirstHalf` (`:240`). WPos interpolation: `MovePart` (`Move.cs:409-593`), `progress += CurrentSpeed` (`:522`), `WPos.Lerp` straight (`:556`) or elliptical arc when turning (`:544-549`), `SetCenterPosition` (`:563`), facing `WAngle.Lerp` (`:579`). `Turn` activity: `Turn.cs:35-50`.

## Q2 — Turning: stop-turn only from standstill; mid-path turns are curved

- Standstill/re-align: yes, stop-turn (`Move.cs:210-216`).
- Mid-path: curved arcs via `EnableArc` (`Move.cs:446-468`); only sharp turns (~135°+, `delta < 384 || delta > 640`, `:613`) revert to turn-in-place. `AlwaysTurnInPlace` (`Mobile.cs:53`) forces it for infantry.
- Knobs: `TurnSpeed`, `TurnSpeedLoss` (`Mobile.cs:46-47`, applied `Move.cs:551-553`), `Acceleration`/`Deceleration` momentum (`Mobile.cs:44,50`; `Move.cs:514-522`), infantry `CanRedirectMidCell`+`RedirectSpeedPenalty` (`Mobile.cs:96-100`, `Move.cs:125-138`), reverse drive (`CanMoveBackward`, `Move.cs:190-208`).
- **TRAP: `MobileInfo.TurnsWhileMoving` (`Mobile.cs:55-56`) is declared but NEVER read — inert flag.**
- Continuous-turn precedent: aircraft `Fly` computes real turn radius (`Fly.cs:360-366`) and steers curved intercepts (`:323-344`).

## Q3 — Subcells

6 offsets in `MapGrid.SubCellOffsets` (`MapGrid.cs:117-125`). Only `SharesCell:true` locomotors use them = the four `foot*` infantry locomotors (world.yaml:32,47,64,80). Vehicles → `SubCell.FullCell` (`Mobile.cs:305`). Subcells are positional offsets only — no subcell nodes in the search; occupancy is per-cell via ActorMap influence (`Mobile.cs:582-587,622-632`); a moving unit reserves BOTH from- and to-cell (`Mobile.cs:285-295`). Turning on SharesCell for vehicles would allow co-occupancy (unwanted) — subcells alone don't reduce grid-feel.

## Q4 — Off-axis: the logical-cell / visual-WPos split is the seam

Hard-8-direction only at the pathfinder. Downstream `MovePart` interpolation is arbitrary continuous WPos. Targeting/weapons consume WPos; pathing/blocking consume CPos. The `From`/`To` WPos handed to `MoveFirstHalf` (`Move.cs:231-240,656-657`) are NOT required to be cell centers (`Move.cs:227-229` already substitutes CenterPosition for `CanRedirectMidCell`) — the concrete injection point for string-pulling with unchanged cell reservations.

## Q5 — Determinism

WPos/WVec/WAngle integer; interpolation integer lerps; `CenterPosition`/`Facing`/cells `[Sync]` (`Mobile.cs:253-279`). **Two float uses in the sim path (pre-existing, tolerated): `Move.cs:517-519` (acceleration step index) and `Move.cs:134-135` (redirect turnFraction).** New smoothing math must stay integer/WDist/WAngle — do not widen the float surface.

## Q6 — Prior art (movement already heavily modified in WW3MOD)

Momentum model (commit 284ad8a5), TurnSpeedLoss + arcs, reverse drive (9f9b42f4, d6f5de63, 241ba2de, f885ad03, 3231aeee), infantry mid-cell redirect (91dbd48a; RedirectSpeedPenalty tuning open in RELEASE_V1.md:41), SmartMove fire-while-moving, formations/cohesion. **Nothing touches the pathfinder graph or any-angle/subcell-vehicle movement** — all smoothing so far is activity/interpolation-layer. Grid is Rectangular (mod.yaml:319).

## Q7 — Incremental smoothing ladder (cheapest → most invasive)

- **(a) YAML knob tuning — ~zero risk.** Raise `TurnSpeed`, lower `TurnSpeedLoss` in `vehicles*.yaml` so arcs read as speed-keeping sweeps. Optionally trial `CanRedirectMidCell` on vehicles to kill standstill stop-turn — but that path was written for SharesCell infantry; verify for FullCell vehicles first. Breaks: combat feel/balance only (faster-turning tanks aim sooner).
- **(b) Interpolation string-pulling — contained engine change.** Keep A\* path + cell reservations; when building the next segment, if the straight WPos line to a LATER waypoint crosses only passable cells (`CanEnterCell` LOS walk), aim `To` there and skip the corner. Integer WPos throughout. Watch: visual cell vs reserved cell divergence mid-corner (targeting reads CenterPosition); `PopPath`'s `Util.AreAdjacentCells` invariant (`Move.cs:253`); crush/`PassAction` fires on cell entry (`Mobile.cs:596-620`) and must still walk every reserved cell.
- **(c) Any-angle multi-cell straights with explicit Bresenham reservation** (post-process path in `Move.OnFirstRun`, `Move.cs:147-154`). Breaks `PopPath` adjacency outright; blocking/nudge/wait logic (`Move.cs:262-347`) assumes one-cell-ahead.
- **(d) True subcell/off-grid vehicle occupancy or theta\* search** (`DensePathGraph.cs:75-135` + locomotor SharesCell model). Highest payoff, highest risk — touches determinism surface, crush, `[Sync]` cell model. Not incremental.
