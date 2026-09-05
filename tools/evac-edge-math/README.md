# evac-edge-math

Static arithmetic over the ten shipped maps: **when a ground unit evacuates, which map
edge does it leave through, and how far does it drive to get there?**

Written to settle the unverified premise in `WORKSPACE/pipeline/items/78-evacuation-edge-choice.md`.
No game, no simulation, no build — it reads `map.yaml` + `map.bin` through nav-guard's
decoder and replicates the engine's edge choice in cell coordinates.

```bash
python tools/evac-edge-math/evac_edge_math.py                 # all ten maps, stride 2
python tools/evac-edge-math/evac_edge_math.py --pop raid       # just the raider population
python tools/evac-edge-math/evac_edge_math.py --map twin --examples 5
```

Runs in ~7 s. Standard library plus nav-guard's `modload`/`nav_guard`, which it imports
rather than reimplementing — bounds, terrain decode, locomotor passability and connected
components all come from there.

## What it models

`RotateToEdge.ChooseEdgeCell`'s ground branch (`RotateToEdge.cs:161-168`):

```csharp
var searchOrigin = FindClosestSpawnAreaForOwner(self) ?? self.Location;
return Map.ChooseClosestMatchingEdgeCell(searchOrigin,
    c => mobileInfo.CanEnterCell(world, null, c) && CanReach(self, mobileInfo, c));
```

Three details that are easy to get wrong and are all load-bearing here:

1. **Two different choosers exist and the ground branch uses the less obvious one.**
   `ChooseClosestMatchingEdgeCell` (`Map.cs:1874-1877`) is an exact argmin over the
   perimeter cell list by `LengthSquared`, filtered. `ChooseClosestEdgeCell`
   (`Map.cs:1821-1863`) — used by the **aircraft** branch and five non-evac callers — is a
   half-plane heuristic that projects onto whichever wall is nearer. Both are reproduced;
   the tool reports how often they disagree.
2. **The sort origin and the reachability origin are different actors' business.** The
   sort key is `searchOrigin`; the `CanReach` predicate paths from `self.Location`
   (`RotateToEdge.cs:177-180`). They coincide only when `searchOrigin` fell back to the
   unit — i.e. on the nine maps with no `spawnarea`.
3. **`FindClosestSpawnAreaForOwner` is owner-side.** It anchors on the `spawnarea` nearest
   the player's own `ProductionFromMapEdge` (`SUPPLYROUTE`, placed at their `mpspawn` by
   `world.yaml:463+`) — not on the unit. So `river-zeta-ww3`, the one shipped map with
   `spawnarea` actors, already resolves the exit from an owner-side origin. Also stated at
   `DOCS/reference/economy.md:118-126`.

Grid is `Rectangular` with `MaximumTerrainHeight 0` (`mod.yaml:328-330`, `MapGrid.cs:110`),
so projection is the identity (`Map.cs:781-790`) and `CPos == MPos == PPos`. That is what
lets the whole thing be plain integer geometry.

## Columns

| column | meaning |
|---|---|
| `own% / enemy% / flank%` | which of the four Bounds walls the exit cell is on, against the wall each spawn sits nearest. On 8 of 10 maps every spawn is on Left or Right, so a Top/Bottom exit is a **neutral flank**, nobody's back edge. |
| `voron%` | exit cell attributed to its nearest spawn. **Near-tautological** — the exit is the perimeter cell nearest the unit, so a unit in the enemy half almost has to pick a cell the enemy spawn is nearest to. Printed so nobody re-derives it and thinks it means something. |
| `drive` | median cells driven to the exit, straight-line. |
| `fix` | median cells driven under item 78's proposed `?? self.Owner.HomeLocation`. |

Populations: `all` (every passable cell × every owner), `enemy-half` (some opponent's
spawn is nearer to the unit than its own), `raid` (within `--raid-radius`, default 20, of
an opponent's spawn).

## Fidelity limits

- Passability is nav-guard's static model: terrain plus non-mobile authored actors.
  `CanEnterCell(world, null, c)` runs with `BlockedByActor.All` against the **live** world,
  so anything built or parked during a match is invisible here.
- `CanReach` is approximated by 8-connected component identity under the shipped
  `tagged` squeeze rule, not by running the real pathfinder.
- Distances are straight-line cell distance, not path length. Real drives are longer;
  the ratio between the two columns is the robust part, not the absolute cells.
- Only the `tracked` locomotor by default. `--locomotor` takes any of the 19.

## Keep or throw away?

**Worth keeping.** It is 400 lines, has no dependencies beyond nav-guard, and answers a
question that recurs: any future change to `RotateToEdge`, to spawn placement, or to a
map's border terrain moves these numbers, and re-running is cheaper than re-deriving the
argument. It is deliberately *not* wired into any gate — it reports, it does not judge.
