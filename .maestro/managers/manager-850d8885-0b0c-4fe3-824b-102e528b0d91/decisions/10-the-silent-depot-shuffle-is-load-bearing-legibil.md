# The silent depot shuffle is load-bearing — legibility, not a footprint change

_Recorded 2026-08-22T01:52:23.660Z by 17dc66e4_

## The question

The user reported units backing up after arriving "as if given a hidden extra order". Root cause: `Mobile.OnBecomingIdle` shoves a unit off a cell it may not stop on, and a vehicle serviced at a `LOGISTICSCENTER` is parked on exactly such a cell — the depot's centre is `+` (transit-only) in `=+= +++ =+=`.

## Options considered

**(a) Make the dock cell stayable** — `+` → `=` on the centre only, giving `=+= +=+ =+=`. The vehicle simply stays put and the phantom move disappears. This was the worker's first recommendation, argued on the grounds that `OnBecomingIdle` performs **no contention check whatsoever** (it fires on the `!wasIdle && IsIdle` edge at `Actor.cs:319-324`, so by construction nothing is queued behind the unit) and that `LOGISTICSCENTER` carries **no `Reservable`**, unlike `HPAD:513` and `AFLD:588`. Conclusion drawn: "clears the pad for the next customer" is not a behaviour this building implements, so the movement has no purpose.

**(b) Keep the movement, make it legible** — give the correction a `targetLineColor` at `Mobile.cs:946` so it reads as the game vacating the pad rather than as a click the player does not remember making.

**(c) Vacate on reservation rather than on idleness** — keep the dock both stayable and free. `Reservable` and `DockHost` already exist and are used by `HPAD`/`AFLD`.

## What decided it

I pushed back on (a) with one objection: **purpose does not have to be explicit to be real.** Bouncing unconditionally means the dock cell is *always free by construction*, which is precisely the property a docking system with no queue and no reservation needs in order to work at all. The missing `Reservable` is the reason to worry, not the reason not to.

The trace came back decisively against (a):

- `MoveOnto.CalculatePathToTarget` (`MoveOnto.cs:41-58`) is explicit — *"If we are close to the target but can't enter, we wait"* → `return (false, PathFinder.NoPath)`. It does not stack, sidestep or re-path.
- `CanEnterCell` defaults to `BlockedByActor.All` and vehicles do not share cells, so a squatter blocks absolutely.
- There is no near-enough escape: `Resupply`'s `isCloseEnough` for the LC is `WDist.Zero`, because the LC has no `RearmsUnits` trait for `AmmoPool.cs:374` to read a `CloseEnough` from. Exact coincidence with the building centre is required.

So `+` → `=` would park vehicle 1 on the pad forever and stall vehicle 2 at the door forever — trading a cosmetic defect for a hard functional one.

## Decision

**(b).** The colour was authored on `wt/heal-legibility` (a shared `AutomaticOrder.LineColor` meaning "the game issued this", applied at nine dispatch sites), landed at `44af5911`. `wt/phantom-move` verifies it rather than duplicating it.

Explicitly **not** made a first-class `Order`: `OnBecomingIdle` is raised deterministically on every client from identical state, so ordering it would add network traffic and drop a simulation invariant into the player's order history.

(c) rejected as more machinery than this bug is worth — but it is real, not hypothetical, if the dock ever needs to be both stayable and free.

## Why this is worth recording

Three things here will otherwise be re-litigated:

1. **`LOGISTICSCENTER`'s footprint must not be "fixed".** It looks wrong and is not. `TransitOnlyServiceHostTest` was INVERTED to pin the `+` dock cell rather than forbid it, with the argument in its failure text — a test encoding a rejected goal reads to the next person as settled policy.
2. **There is no two-vehicles-one-depot case anywhere in the test suite.** That is exactly why the bad recommendation would have passed review and shipped a stall. `test-depot-vacate-phantom` is now the only one.
3. **`MoveOnto` overrides away the `CanStayInCell && CanEnterCell` filter its own base class `MoveAdjacentTo` applies** (`:129`), substituting one unfiltered cell. That is precisely what `Mobile.cs:944`'s HACK comment has been accusing.

## Process note

I briefed the phantom-move worker to author the colour at `Mobile.cs:946` **while a sibling worker was already writing that exact line**. Caught before duplication. The reusable lesson: two workers on adjacent legibility problems converge on the same call site, and a sibling's coverage table is the cheapest place to check before briefing.
