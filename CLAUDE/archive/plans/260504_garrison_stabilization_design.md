# Garrison Stabilization (Round 1) — Design Spec

**Date:** 2026-05-04
**Phase:** v1 Phase A — Stabilize
**Scope:** 6 items from RELEASE_V1.md garrison-related list. **This document is the design only — implementation plan is a follow-up artifact.**

## Status of items entering this round

| # | Item | Status entering round |
|---|---|---|
| 1 | Rearm regression (CargoSupply.cs:330) | **Shipped** in commit `56a31d89`. Excluded from this design. |
| 2 | Batch-entry verification | **Playtest only.** `updateGeneration: false` mitigation already in code (GarrisonManager.cs:198–208, 261–273). 60s playtest check, no design needed. |
| 3 | Stop order doesn't cancel garrisoned firing | Designed below. |
| 4 | Soldiers under fire abandon Enter | Designed below. |
| 5 | Pre-entry stop + skip-ahead for queues | Designed below (5A and 5B). |
| 6 | Port↔shelter chaotic switching | Designed below. |

Out of scope: Phase 4 sidebar panel rewrite (deferred), all visual / feature work (truck→building cargo, protection viz redesign — already shipped per user) and all non-garrison items.

---

## Item 3 — Stop order silences garrison briefly

### Problem
`AttackBase.OnStopOrder` calls `self.CancelActivity()`. Garrison firing happens autonomously in `GarrisonManager.Tick()` and `AttackGarrisoned.DoGarrisonedAttack()` — not through activities. Stop is a literal no-op.

### Solution: Soft stop (matches Mobile-unit semantics)
Override `AttackGarrisoned.OnStopOrder(self)` to call base, then a new `GarrisonManager.OnStopOrder()` method.

`GarrisonManager.OnStopOrder()` clears:
- `forceTarget`, `hasForceTarget`
- Every `PortState.CurrentTarget = Target.Invalid`
- Every `PortState.PlayerOverride = false`
- `ambushTriggered = false`

Auto-target rescans on next scan tick per stance. FireAtWill picks new target, HoldFire stays quiet, Ambush re-arms.

### Files
- `engine/OpenRA.Mods.Common/Traits/Attack/AttackGarrisoned.cs` — override `OnStopOrder`
- `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs` — add public `OnStopOrder()` method

### Trade-off acknowledged
On FireAtWill with enemies still visible, Stop's effect is invisible — auto-target picks the same enemies on next scan. Matches Mobile-unit Stop semantics. Player wanting permanent silence uses HoldFire stance.

---

## Item 4 — Soldiers entering buildings don't pause to return fire

### Problem
`Enter.cs` calls `move.MoveToTarget(self, target)` and `move.MoveIntoTarget(self, target)`. Both go through `Mobile.WrapMove` → `SmartMoveActivity`. While approaching the building, SmartMove engages enemies in range, soldier pauses to fire.

### Solution: Raw IMove variants
Add to `IMove` interface:

```csharp
Activity MoveToTargetRaw(Actor self, in Target target,
    WPos? initialTargetPosition = null, Color? targetLineColor = null);
Activity MoveIntoTargetRaw(Actor self, in Target target);
```

`Mobile.cs` implementation: identical to `MoveToTarget` / `MoveIntoTarget` but **without** `WrapMove`.

`Aircraft.cs` implementation: delegates to existing `MoveToTarget` / `MoveIntoTarget` (aircraft don't implement `IWrapMove`).

`Enter.cs` line 108 (`MoveToTarget` call) → `MoveToTargetRaw`.
`Enter.cs` line 122 (`MoveIntoTarget` call) → `MoveIntoTargetRaw`.

### Side effect (intentional)
All Enter subclasses inherit the change: Capture, Demolish, RideTransport, EnterCarrierMaster, Infiltrate, DonateCash, DonateExperience, RepairBuilding, RepairBridge, InstantRepair, EnterAsCrew. Player intent for any of these = "go to that thing and do the action" → focus, don't pause to fight.

### Files
- `engine/OpenRA.Mods.Common/TraitsInterfaces.cs` — add interface methods to `IMove`
- `engine/OpenRA.Mods.Common/Traits/Mobile.cs` — implement raw variants
- `engine/OpenRA.Mods.Common/Traits/Air/Aircraft.cs` — implement raw variants
- `engine/OpenRA.Mods.Common/Activities/Enter.cs` — call raw variants on lines 108, 122

---

## Item 5A — Pre-entry pause (~0.5–1s outside building)

### Problem
`MoveCooldownHelper.Cooldown` defaults to `(20, 31)` ticks (0.8–1.2s). When `MoveAdjacentTo` reaches the cell adjacent to the building, the move returns `MoveResult.CompleteDestinationBlocked` (the building's own cell is "blocked"). With `Enter.cs:41` setting `RetryIfDestinationBlocked = true`, the helper waits the cooldown before allowing the activity to retry. That wait is the visible pause.

### Solution: Collapse the cooldown for Enter
In `Enter.cs` constructor (line 41):

```csharp
moveCooldownHelper = new MoveCooldownHelper(self.World, move as Mobile)
{
    RetryIfDestinationBlocked = true,
    Cooldown = (0, 1)
};
```

The retry-if-blocked behavior is preserved (in case the building's cell really is blocked). The wait between retries collapses to effectively 0.

### Files
- `engine/OpenRA.Mods.Common/Activities/Enter.cs` — line 41

### Risk
Spam-pathfinding if a target is permanently unreachable. Mitigated because `TryStartEnter` (in subclasses) calls `Cancel(self, true)` on transport-full / can't-load conditions — so Enter aborts cleanly.

---

## Item 5B — Skip-ahead for queued building entries

### Problem
A soldier walking toward a queued building keeps walking even after the building has filled. Only at `TryStartEnter` (after arrival) does `Reserve` fail and `Cancel` fire. By then the soldier has wasted travel time.

### Solution: A1 — check capacity every tick during Approaching
In `RideTransport.TickInner(self, target, targetIsDeadOrHiddenActor)`:

```csharp
if (target.Type == TargetType.Actor && !targetIsDeadOrHiddenActor)
{
    var cargo = target.Actor.TraitOrDefault<Cargo>();
    if (cargo != null && !cargo.HasSpace(passenger.Info.Weight))
        Cancel(self, true);  // keepQueue: true → next queued Enter takes over
}
```

`Cancel(self, keepQueue=true)` lets the next queued Enter activity (next building in chain) execute. If queue is empty, soldier goes idle near building.

### Verification at implementation time
Confirm shift-clicking N buildings with M soldiers selected creates per-soldier `[Enter→B1, Enter→B2, …, Enter→BN]` chains. (Confirmed in brainstorming dialog — but worth a 5-minute code path trace.)

### Files
- `engine/OpenRA.Mods.Common/Activities/RideTransport.cs` — implement `TickInner` override

`Cargo.HasSpace(int weight)` exists at `Cargo.cs:375` and already accounts for `totalWeight + reservedWeight + supplyReservedWeight`, so reservation-based contention between racing soldiers is handled.

### Trade-offs
- A soldier far from a target who switches to the next building based on the current state may "miss" if the current building empties (someone leaves) before the soldier could have arrived. Edge case, low frequency, acceptable.
- No 4-cell distance gate. The original idea ("only check from 4 cells out") would defer the switch but complicates logic for marginal benefit.

---

## Item 6 — Port↔shelter chaotic switching

### Problem
Soldiers thrash between ports and shelter when targets churn (LOS flicker, multiple enemies entering arc one at a time, sustained suppression). Root cause: `IdleRecallTicks = 125` is the only gate on recall, and there's no cooldown / hysteresis on redeploy.

### Solution: Hysteresis + sticky targets
Combines time-based commitment thresholds (B) with target-side stickiness (C). No artificial cooldowns.

### Tuning fields (all on `GarrisonManagerInfo`)

| Field | Old default | New default | Description |
|---|---|---|---|
| `IdleRecallTicks` | 125 | **250** | Idle ticks before recall (~10s) |
| `MinDeployTicks` | — | **75** (new) | Min ticks at port after deploy, regardless of target loss (~3s) |
| `RedeployBlackoutTicks` | — | **30** (new) | After recall, port can't redeploy for this long (~1.2s) |
| `TargetConfirmTicks` | — | **10** (new) | New target must be visible this long before empty-port deploy fires (~0.4s) |
| `StickyTargetTicks` | — | **50** (new) | Port keeps committed to `CurrentTarget` after losing arc/LOS for this long (~2s) |

### New PortState fields

```csharp
public class PortState
{
    // ...existing...
    public int DeployedAtTick;                 // for MinDeployTicks gate
    public int RedeployBlackoutRemaining;      // for RedeployBlackoutTicks gate
    public Target PendingDeployTarget;         // for TargetConfirmTicks gate
    public int PendingDeployTicks;             // counter for TargetConfirmTicks
    public int StickyTargetRemaining;          // for StickyTargetTicks
}
```

### Logic changes in `GarrisonManager.Tick`

**MinDeployTicks:** before incrementing `IdleTicks` toward recall, check `(currentTick - ps.DeployedAtTick) >= MinDeployTicks`. If not yet, don't recall on idle.

**RedeployBlackoutTicks:** in `RecallToShelter`, set `ps.RedeployBlackoutRemaining = Info.RedeployBlackoutTicks`. Decrement each tick. Empty-port deploy logic skips when this is > 0.

**TargetConfirmTicks:** when an empty port detects a target via `ScanForTarget`:
- If new target == `PendingDeployTarget` → increment `PendingDeployTicks`. If ≥ `TargetConfirmTicks`, deploy.
- If new target ≠ `PendingDeployTarget` → reset `PendingDeployTarget` and `PendingDeployTicks = 1`
- If no target → clear pending state

**StickyTargetTicks:** in `UpdatePortTarget`, when `CurrentTarget.IsValidFor(self)` but `IsTargetInPortArc(portIndex, CurrentTarget)` returns false (target left arc/LOS):
- Keep `CurrentTarget`. Decrement `StickyTargetRemaining` each tick.
- If target re-enters arc → reset `StickyTargetRemaining = StickyTargetTicks`.
- When `StickyTargetRemaining <= 0` → clear and rescan.

### Files
- `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs` — all changes here
- `mods/ww3mod/rules/ingame/structures.yaml` (and any per-building overrides) — none required if defaults work; YAML can override per-building

### Risk
- Tuning is starter values; expect 1–2 playtest cycles to settle.
- `StickyTargetTicks` introduces a state where a port soldier visibly aims at empty space (target out of arc, sticky timer running). Likely fine — soldiers tracking lost targets is realistic. Watch for "weird-looking" frames in playtest.

---

## Implementation order (low → high risk)

1. **Item 3 (Stop)** — small isolated change, no cross-cutting impact
2. **Item 5A (cooldown bypass)** — one-line ctor change in `Enter.cs`
3. **Item 4 (raw IMove)** — cross-cutting (interface + 2 impls + Enter.cs), straightforward
4. **Item 5B (skip-ahead)** — independent, modifies `RideTransport.TickInner`
5. **Item 6 (port stabilization)** — most complex; new state, new YAML fields, non-trivial Tick logic

Items can land as separate commits.

## Testing

- **Unit-testable:** Item 5B (capacity-check logic), Item 6 (timing gates, sticky-target lifecycle). Add to `engine/OpenRA.Test/`.
- **Behavioral:** Items 3, 4, 5A — verify in playtest only.
- **Round playtest:** after all 5 land, full pass through `RELEASE_V1.md` 260504 checklist (8 items including the 4 covered here, the 2 already-shipped quick-fixes, and the 2 helicopter items unrelated to this round).

## Open questions to resolve at implementation

1. Confirm shift-queue produces per-soldier Enter chains (vs. a distributed allocation). Confirmed in conversation; verify in code at impl time.
2. Decide whether `AttackGarrisoned.OnStopOrder` should also clear `RequestedTarget` on `AttackBase` directly (depends on whether AttackBase already clears it on its own Stop path).
