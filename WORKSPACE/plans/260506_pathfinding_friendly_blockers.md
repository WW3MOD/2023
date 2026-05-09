# Pathfinding & friendly-blocking — scope for follow-up chat

> Author: Claude, 2026-05-06. The user reports units in groups dropping moves and going idle on long routes when narrow gaps are clogged with friendly units. They want a discussion in a new chat — this doc is the briefing so the next session has full context without re-investigating from scratch.

## The user's report (paraphrased)

> A group of vehicles ordered to move — one by one they drop the order and become idle. Worse on long routes. Worse with narrow gaps. Units in the back are affected more. The pathfinder seems to fail because the gap is "blocked", but it isn't really blocked — friendly units are there.

> The pathfinder should not treat friendly units as walls. In reality, a unit told to cross a bridge that's full of allies would either nudge through, wait its turn, or accept that it's queued — not reroute to the bridge on the other side of the map.

## Current behavior (what the code actually does)

### `BlockedByActor` levels
`engine/OpenRA.Mods.Common/TraitsInterfaces.cs:930` — four-level enum:

| Level | Meaning |
|---|---|
| `None` | All actors ignored (terrain only) |
| `Immovable` | Allies-capable-of-moving are ignored. Only impassable terrain + immovable actors block. |
| `Stationary` | Moving actors ignored, stationary blocks |
| `All` | Everything blocks |

### Initial-path search order
`engine/OpenRA.Mods.Common/Activities/Move/Move.cs:32-35`:

```csharp
public readonly BlockedByActor[] PathSearchOrder =
{
    BlockedByActor.All,
    BlockedByActor.Stationary,
    BlockedByActor.Immovable,
    BlockedByActor.None
};
```

The Move activity tries `All` first, falls through progressively to `None`. So the *first* path that exists (treating all actors as blockers) is preferred. Only when no such path exists does it relax. **A long crowded route where every step has friendlies will path-through-friendlies-as-walls and find a long route around.** That's the user's "go to the bridge on the other side of the map" behavior.

There's a comment on line 146:
```
// TODO: Change this to BlockedByActor.Stationary after improving the local avoidance behaviour
```

So the OpenRA upstream maintainers also know `All` is too restrictive but defer the change pending better local avoidance. WW3MOD has not changed this.

### What happens when units "drop" and go idle
`Move.cs:245-340` (PopPath):
- If next cell in the path is blocked by another actor:
  1. Check if we're "close enough" (within `nearEnough` cells of destination). If yes and there's no useful nudge cell available → `path.Clear()` → unit gives up.
  2. If blocker is an immovable actor → repath with `BlockedByActor.Immovable`. If THAT path is empty → unit gives up.
  3. Otherwise, wait `WaitAverage` ticks (locomotor-defined). Then repath with `BlockedByActor.All`. If empty → unit gives up.
- "Gives up" = path becomes empty → Move's Tick line 174-178 sets `destination = mobile.ToCell` and returns false → eventually the activity completes without reaching the goal → unit becomes idle.

So the "idle drop" the user sees has three different triggers, all in PopPath:
- (a) destination cell occupied by friendly + no nearby free cell → give up
- (b) repath under immovable-only fails (rare — usually terrain is open)
- (c) wait for friendly to clear the cell, then repath, repath fails → give up

For a group of vehicles, (a) and (c) compound: each vehicle waits for the one ahead, repaths through the same congested funnel, the funnel is even more congested by then, and they give up one by one starting from the back.

### Group movement = N independent path searches
Group orders just dispatch one Move order per selected unit. There is no formation pathfinding — each unit independently solves "how do I get to point X" treating its allies as obstacles. This is why the back of a formation suffers most: by the time those units search, the front has already filled the chokepoint.

`CohesionMoveModifier` (in `engine/OpenRA.Mods.Common/Traits/CohesionMoveModifier.cs`) does add per-unit waypoint offsets to spread the formation, but it doesn't share a path or coordinate ordering.

## Related issues uncovered while investigating

### SmartMove's "interrupt to fire" disagreement
Today `SmartMoveActivity` interrupts a regular Move whenever it can fire on a fresh enemy in range. The user's mental model is that *only* Attack-Move should pause-and-fire; regular Move should never. Two stances on this:
- **Current code intent (preserved upstream behavior + WW3MOD's docstring):** "Wraps regular Move so units pause to fire while moving."
- **User intent:** Regular Move = no firing pauses. Attack-Move = full pause-and-fire.

This is independent of pathfinding but came up in the same conversation and is a candidate for the same chat. **Today's fix** (per-armament `NoSelfDefenseInterrupt`) only covers the drone-jammer case; the broader policy change is unresolved.

### `MoveWithinRange.ShouldStop` uses CenterPosition not cell
`MoveWithinRange.cs:42` — stops as soon as the actor's interpolated CenterPosition enters range, even mid-cell. With `HoldFireWhileMoving` shipped today, firing waits, but the stopping logic might still cause weird "stop in middle of cell" visuals. Worth verifying alongside pathfinding work.

### `MoveAdjacentTo` cancels inner Move but `MovePart` is non-interruptible for vehicles
Already documented in CLAUDE.md "Common pitfalls" — not a bug per se, but interacts with the pathfinder when a unit is told to redirect mid-route.

## Where to start in the next chat

**Files to read first:**
- `engine/OpenRA.Mods.Common/Activities/Move/Move.cs` — main Move activity (PopPath is the key function, lines 245-340)
- `engine/OpenRA.Mods.Common/TraitsInterfaces.cs:930` — BlockedByActor enum
- `engine/OpenRA.Mods.Common/Pathfinder/PathSearch.cs` — A* implementation
- `engine/OpenRA.Mods.Common/Pathfinder/HierarchicalPathFinder.cs` — top-level pathfinder
- `engine/OpenRA.Mods.Common/Activities/Move/MoveAdjacentTo.cs` — used for MoveWithinRange + MoveWithinRange.cs
- `engine/OpenRA.Mods.Common/Activities/Move/SmartMoveActivity.cs` — SmartMove wrapper

**Specific questions to brainstorm with the user:**

1. **Should friendly mobile units always be treated as `None` for path search?**
   - Pro: matches user's mental model ("path through, queue at chokepoints")
   - Con: pathfinding becomes very fragile to local congestion; the existing `Stationary` fallback already handles this case partially
   - Compromise: search at `BlockedByActor.Immovable` first (skip "All"), so allied movables are passable from the start. The `// TODO` on line 146 implies this is the upstream-recommended direction, blocked on local avoidance work.

2. **What "local avoidance" actually has to do?** When a unit reaches a step blocked by an allied unit:
   - (a) wait? — already does, but eventually gives up
   - (b) nudge the blocker? — `Nudge` activity exists, called via `NotifyBlocker`
   - (c) detour one cell sideways? — current behavior, but only checks 8 neighbors (line 282)
   - (d) push through (swap positions with the blocker)? — not implemented
   - (e) queue (form a virtual queue at the chokepoint)? — not implemented

3. **Group pathfinding** — should the engine compute one shared path for a group and have units follow staggered? OR keep N independent paths but coordinate ordering at chokepoints? The latter is simpler to bolt on.

4. **Give-up policy** — when a unit's path fails, should it retry after a delay before becoming idle? Today the unit just stops. A polling retry (e.g. re-evaluate every 50 ticks while there's a queued destination) would feel much better in groups.

5. **SmartMove policy split** — does the user want regular Move to stop pausing-and-firing entirely? If so, the change is in `SmartMoveActivity.Tick` line 105:
   ```csharp
   if (!holdingPosition && (underFire || !targetSaturated))
   ```
   becomes:
   ```csharp
   if (!holdingPosition && underFire)  // only return fire when actually attacked
   ```
   …or even removed entirely so SmartMove is just a Move passthrough. This decouples cleanly from the pathfinding work.

## What's NOT in scope here

- Aircraft pathfinding (Aircraft.cs has its own movement, not subject to BlockedByActor levels)
- Air units inside garrison ports / repair pads (separate system)
- Husk decay / wreckage as path blockers (probably deserves its own pass)

## Recent context (already shipped today, 2026-05-06)

Pre-existing the pathfinding chat, today's commits added:
- `AttackBaseInfo.HoldFireWhileMoving` — gates fire on `Mobile.IsMovingBetweenCells`
- `AttackBaseInfo.SetupTicks` + `SetupCondition` — artillery deploy phase
- `Mobile.cs` cursor truthfulness fix
- `ArmamentInfo.RequiresForceFire` — DroneTargeter, HIMARSTargeter, IskanderTargeter no longer auto-fire on enemy ground actors (this was the actual cause of the DR "prepare" animation issue)
- `ArmamentInfo.NoSelfDefenseInterrupt` — DroneJammer doesn't cancel a player Move via SmartMove
- Bumped `AimingDelay` to 30-40 across artillery/MLRS armaments
- Stripped dead `GrantConditionOnPreparingAttack` fields (`PreparingRevokeDelay`, `AttackingRevokeDelay`, `RevokeOnNewTarget`)

These do not address pathfinding. They're the "stabilize first" pass.
