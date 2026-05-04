# Garrison Stabilization (Round 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stabilize the WW3MOD garrison system so the 4 design items (#3 Stop, #4 Enter+SmartMove, #5A pause, #5B skip-ahead, #6 port↔shelter chaos) are fixed and the system can sign off `[T] → [x]` after the next playtest.

**Architecture:** Five independent surgical changes across `Enter` activity, `IMove` interface, `RideTransport`, `AttackGarrisoned`, and `GarrisonManager`. No new traits, no new YAML structures (only new tunable fields on existing `GarrisonManagerInfo`). Item 6 is the only complex one — adds 5 PortState fields and 4 gate checks in Tick.

**Tech Stack:** C# .NET, OpenRA engine `release-20230225` base, NUnit3 tests in `engine/OpenRA.Test/`.

**Design spec:** [`CLAUDE/plans/260504_garrison_stabilization_design.md`](./260504_garrison_stabilization_design.md)

**Build command (Windows):** `powershell -ExecutionPolicy Bypass -File ./make.ps1 all`
**Build expectation:** "Build succeeded. 0 Warning(s) 0 Error(s)" (build will fail if user has the game running — DLLs locked. That's normal; wait or come back later.)

**Implementation order** (low → high risk): Item 3 → Item 5A → Item 4 → Item 5B → Item 6 → Wrap-up. Each item is its own commit.

---

## Task 1: Item 3 — Soft Stop for Garrison

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs`
- Modify: `engine/OpenRA.Mods.Common/Traits/Attack/AttackGarrisoned.cs`

### Step 1.1: Add `OnStopOrder()` public method to `GarrisonManager`

- [ ] Open `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs`. Add this public method to the `GarrisonManager` class (place it next to `ClearForceTarget` around line 963):

```csharp
// Called by AttackGarrisoned.OnStopOrder. Mirrors Mobile-unit Stop semantics:
// clears player-issued force-attack and current per-port targets, resets ambush.
// Auto-target rescans next scan tick per stance — FireAtWill picks new targets,
// HoldFire stays quiet, Ambush re-arms.
public void OnStopOrder(Actor self)
{
	forceTarget = Target.Invalid;
	hasForceTarget = false;

	for (var i = 0; i < PortStates.Length; i++)
	{
		PortStates[i].CurrentTarget = Target.Invalid;
		PortStates[i].PlayerOverride = false;
		PortStates[i].TargetLockTicks = 0;
	}

	ambushTriggered = false;
}
```

### Step 1.2: Override `OnStopOrder` in `AttackGarrisoned`

- [ ] Open `engine/OpenRA.Mods.Common/Traits/Attack/AttackGarrisoned.cs`. Add this override inside the `AttackGarrisoned` class (place it after `DoAttack` around line 382):

```csharp
public override void OnStopOrder(Actor self)
{
	base.OnStopOrder(self);

	if (useGarrisonManager && garrisonManager != null)
		garrisonManager.OnStopOrder(self);
}
```

### Step 1.3: Build

- [ ] Run: `powershell -ExecutionPolicy Bypass -File ./make.ps1 all`
- [ ] Expected: "Build succeeded. 0 Warning(s) 0 Error(s)"

### Step 1.4: Commit

- [ ] Run:

```bash
git add engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs engine/OpenRA.Mods.Common/Traits/Attack/AttackGarrisoned.cs
git commit -m "Garrison: Stop order now clears force/port targets and ambush state

Stop pressed on a garrison building was a no-op — firing happens in
GarrisonManager.Tick / AttackGarrisoned.DoGarrisonedAttack each tick,
not via activities, so AttackBase.OnStopOrder's CancelActivity had
nothing to cancel.

AttackGarrisoned now overrides OnStopOrder, calling base then a new
GarrisonManager.OnStopOrder that clears forceTarget, all PortState
CurrentTarget, all PlayerOverride flags, and resets ambushTriggered.
Auto-target rescans on the next scan tick per stance (FireAtWill
picks new target, HoldFire stays quiet, Ambush re-arms).

Matches Mobile-unit Stop semantics: cancel current intent, baseline
behavior resumes."
```

---

## Task 2: Item 5A — Pre-Entry Cooldown Bypass

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Activities/Enter.cs:41`

### Step 2.1: Collapse the cooldown range in `Enter` constructor

- [ ] Open `engine/OpenRA.Mods.Common/Activities/Enter.cs`. Locate line 41:

```csharp
moveCooldownHelper = new MoveCooldownHelper(self.World, move as Mobile) { RetryIfDestinationBlocked = true };
```

- [ ] Replace with:

```csharp
// Cooldown collapsed to (0, 1) — the visible 0.8-1.2s pause "outside the building"
// before entering came from MoveCooldownHelper's default (20, 31) cooldown firing
// when the destination cell registered as blocked (the building's own cell).
// RetryIfDestinationBlocked stays true so genuine blocks still abort cleanly via
// TryStartEnter's Cancel path; just don't make the player wait between retries.
moveCooldownHelper = new MoveCooldownHelper(self.World, move as Mobile)
{
	RetryIfDestinationBlocked = true,
	Cooldown = (0, 1)
};
```

### Step 2.2: Build

- [ ] Run: `powershell -ExecutionPolicy Bypass -File ./make.ps1 all`
- [ ] Expected: "Build succeeded. 0 Warning(s) 0 Error(s)"

### Step 2.3: Commit

- [ ] Run:

```bash
git add engine/OpenRA.Mods.Common/Activities/Enter.cs
git commit -m "Enter: collapse MoveCooldownHelper cooldown so soldiers enter immediately

The visible 0.8-1.2s pause when soldiers reached a building before
entering was MoveCooldownHelper applying its default (20, 31) tick
cooldown after MoveAdjacentTo returned CompleteDestinationBlocked.

The building's own cell counts as blocked from the locomotor's view, so
the cooldown fires every time. With RetryIfDestinationBlocked = true,
the activity waits the cooldown then retries — visible pre-entry pause.

Set Cooldown = (0, 1) in the Enter constructor so retries are
effectively immediate. RetryIfDestinationBlocked stays true so
TryStartEnter still gets a chance to abort cleanly via its existing
Cancel path; we just don't make the player wait between attempts."
```

---

## Task 3: Item 4 — Raw IMove Variants for Enter

**Files:**
- Modify: `engine/OpenRA.Mods.Common/TraitsInterfaces.cs` (add 2 methods to `IMove`)
- Modify: `engine/OpenRA.Mods.Common/Traits/Mobile.cs` (add raw implementations)
- Modify: `engine/OpenRA.Mods.Common/Traits/Air/Aircraft.cs` (add delegating implementations)
- Modify: `engine/OpenRA.Mods.Common/Activities/Enter.cs:108,122` (use raw variants)

### Step 3.1: Locate IMove interface in TraitsInterfaces.cs

- [ ] Open `engine/OpenRA.Mods.Common/TraitsInterfaces.cs`. Search for `interface IMove` (likely near `IWrapMove`).
- [ ] Identify where `MoveToTarget` and `MoveIntoTarget` are declared.

### Step 3.2: Add raw variants to IMove

- [ ] In the `IMove` interface, immediately after the existing `MoveIntoTarget` declaration, add:

```csharp
/// <summary>
/// Like <see cref="MoveToTarget"/> but bypasses any <see cref="IWrapMove"/> wrappers
/// (e.g. SmartMove). Use when the caller — Enter and its subclasses — needs the
/// unit to focus on reaching the target without being interrupted by SmartMove's
/// fire-while-moving behavior.
/// </summary>
Activity MoveToTargetRaw(Actor self, in Target target,
	WPos? initialTargetPosition = null, Color? targetLineColor = null);

/// <summary>
/// Like <see cref="MoveIntoTarget"/> but bypasses any <see cref="IWrapMove"/> wrappers.
/// </summary>
Activity MoveIntoTargetRaw(Actor self, in Target target);
```

### Step 3.3: Implement raw variants on Mobile

- [ ] Open `engine/OpenRA.Mods.Common/Traits/Mobile.cs`. Locate the existing `MoveToTarget` implementation (around line 726):

```csharp
public Activity MoveToTarget(Actor self, in Target target,
	WPos? initialTargetPosition = null, Color? targetLineColor = null)
{
	if (target.Type == TargetType.Invalid)
		return null;

	return WrapMove(new MoveAdjacentTo(self, target, initialTargetPosition, targetLineColor));
}
```

- [ ] Immediately after that method, add:

```csharp
public Activity MoveToTargetRaw(Actor self, in Target target,
	WPos? initialTargetPosition = null, Color? targetLineColor = null)
{
	if (target.Type == TargetType.Invalid)
		return null;

	return new MoveAdjacentTo(self, target, initialTargetPosition, targetLineColor);
}
```

- [ ] Locate the existing `MoveIntoTarget` implementation (around line 735):

```csharp
public Activity MoveIntoTarget(Actor self, in Target target)
{
	if (target.Type == TargetType.Invalid)
		return null;

	// Activity cancels if the target moves by more than half a cell
	// to avoid problems with the cell grid
	return WrapMove(new LocalMoveIntoTarget(self, target, new WDist(512)));
}
```

- [ ] Immediately after that method, add:

```csharp
public Activity MoveIntoTargetRaw(Actor self, in Target target)
{
	if (target.Type == TargetType.Invalid)
		return null;

	return new LocalMoveIntoTarget(self, target, new WDist(512));
}
```

### Step 3.4: Implement raw variants on Aircraft

- [ ] Open `engine/OpenRA.Mods.Common/Traits/Air/Aircraft.cs`. Locate the existing `MoveToTarget` and `MoveIntoTarget` implementations (search for `public Activity MoveToTarget` and `public Activity MoveIntoTarget`).
- [ ] Aircraft does NOT implement `IWrapMove`, so its `MoveToTarget` already returns the raw activity. The raw variants just delegate. Add immediately after each existing method:

For `MoveToTarget`:

```csharp
public Activity MoveToTargetRaw(Actor self, in Target target,
	WPos? initialTargetPosition = null, Color? targetLineColor = null)
{
	// Aircraft does not implement IWrapMove; raw == regular.
	return MoveToTarget(self, target, initialTargetPosition, targetLineColor);
}
```

For `MoveIntoTarget`:

```csharp
public Activity MoveIntoTargetRaw(Actor self, in Target target)
{
	// Aircraft does not implement IWrapMove; raw == regular.
	return MoveIntoTarget(self, target);
}
```

### Step 3.5: Sanity build to catch interface holes

- [ ] Run: `powershell -ExecutionPolicy Bypass -File ./make.ps1 all`
- [ ] Expected: "Build succeeded. 0 Warning(s) 0 Error(s)"
- [ ] If build fails complaining about missing interface members on a third `IMove` implementation, search for `: IMove` to find any other implementer and add the same delegating raw variants.

### Step 3.6: Update Enter.cs to use raw variants

- [ ] Open `engine/OpenRA.Mods.Common/Activities/Enter.cs`. Locate line 108 in the `Approaching` state (inside the `if (target.Type != TargetType.Invalid && !move.CanEnterTargetNow(self, target))` block):

```csharp
QueueChild(move.MoveToTarget(self, target, initialTargetPosition));
```

- [ ] Replace with:

```csharp
QueueChild(move.MoveToTargetRaw(self, target, initialTargetPosition));
```

- [ ] Locate line 122 (inside the `if (TryStartEnter(self, target.Actor))` block):

```csharp
QueueChild(move.MoveIntoTarget(self, target));
```

- [ ] Replace with:

```csharp
QueueChild(move.MoveIntoTargetRaw(self, target));
```

### Step 3.7: Build

- [ ] Run: `powershell -ExecutionPolicy Bypass -File ./make.ps1 all`
- [ ] Expected: "Build succeeded. 0 Warning(s) 0 Error(s)"

### Step 3.8: Commit

- [ ] Run:

```bash
git add engine/OpenRA.Mods.Common/TraitsInterfaces.cs engine/OpenRA.Mods.Common/Traits/Mobile.cs engine/OpenRA.Mods.Common/Traits/Air/Aircraft.cs engine/OpenRA.Mods.Common/Activities/Enter.cs
git commit -m "IMove: add MoveToTargetRaw / MoveIntoTargetRaw — Enter bypasses SmartMove

Soldiers ordered to enter a building paused mid-approach to return fire
because Enter.MoveToTarget / MoveIntoTarget went through Mobile.WrapMove
and got wrapped in SmartMoveActivity. Player's intent — 'go inside that
building' — was being overridden by SmartMove's fire-while-moving logic.

Add raw variants on IMove that skip WrapMove. Mobile returns the inner
MoveAdjacentTo / LocalMoveIntoTarget directly. Aircraft delegates (no
IWrapMove on aircraft). Enter.cs uses the raw variants on lines 108
and 122.

Side effect (intentional): all Enter subclasses inherit the change —
Capture, Demolish, RideTransport, EnterCarrierMaster, Infiltrate,
DonateCash, DonateExperience, RepairBuilding, RepairBridge,
InstantRepair, EnterAsCrew. Any 'go to that thing and act on it' order
now stays focused on the action."
```

---

## Task 4: Item 5B — Skip-Ahead on Full Buildings

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Activities/RideTransport.cs` (add `TickInner` override)

### Step 4.1: Add TickInner override to RideTransport

- [ ] Open `engine/OpenRA.Mods.Common/Activities/RideTransport.cs`. Locate the existing `TryStartEnter` method (around line 32). Immediately before it, add this override:

```csharp
protected override void TickInner(Actor self, in Target target, bool targetIsDeadOrHiddenActor)
{
	// Skip-ahead: if the target building is already full while we're approaching,
	// cancel this Enter so the next queued Enter (next building in chain) can take
	// over. Soldiers shift-queued across multiple buildings then redistribute
	// themselves across capacity automatically.
	//
	// Cargo.HasSpace already accounts for reservedWeight so racing soldiers don't
	// double-count an empty slot.
	if (targetIsDeadOrHiddenActor || target.Type != TargetType.Actor)
		return;

	var cargo = target.Actor.TraitOrDefault<Cargo>();
	if (cargo != null && !cargo.HasSpace(passenger.Info.Weight))
		Cancel(self, true);
}
```

### Step 4.2: Build

- [ ] Run: `powershell -ExecutionPolicy Bypass -File ./make.ps1 all`
- [ ] Expected: "Build succeeded. 0 Warning(s) 0 Error(s)"

### Step 4.3: Commit

- [ ] Run:

```bash
git add engine/OpenRA.Mods.Common/Activities/RideTransport.cs
git commit -m "RideTransport: skip-ahead to next queued building if current one is full

When a soldier shift-queued onto multiple buildings (Enter→B1, Enter→B2,
Enter→B3) reached B1 and found it full, only TryStartEnter — called
post-arrival — would Cancel and let the next queued Enter run. So all
soldiers walked to B1 first, half then walked to B2, etc. — wasted
travel across the whole batch.

Override Enter.TickInner to query Cargo.HasSpace(passenger weight) on
each tick during Approaching. If the target is full, Cancel(keepQueue:
true) so the next queued Enter takes over immediately. Cargo.HasSpace
already accounts for reservedWeight, so racing soldiers don't
double-claim an empty slot.

Edge case (acceptable): a soldier far from B1 may switch to B2 if B1
fills, then B1 empties before they could have arrived. Low frequency,
not worth gating with a distance threshold."
```

---

## Task 5: Item 6 — Port↔Shelter Stabilization (Hysteresis + Sticky Targets)

This is the largest task. New tunable fields, new PortState fields, new gate logic in Tick. Done in three sub-passes: (5a) data structures and YAML fields, (5b) deploy/recall gates, (5c) sticky target.

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs`

### Step 5.1: Add new tunable fields to `GarrisonManagerInfo`

- [ ] Open `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs`. Locate the `IdleRecallTicks` field (around line 65). Change its default from `125` to `250`:

```csharp
[Desc("Ticks a deployed soldier must be idle (no valid target) before being recalled to shelter. 0 disables.")]
public readonly int IdleRecallTicks = 250;
```

- [ ] Immediately after `IdleRecallTicks`, add the four new fields:

```csharp
[Desc("Minimum ticks a soldier must remain at a port after deploying, regardless of target loss. " +
	"Prevents instant-recall flapping when targets churn briefly.")]
public readonly int MinDeployTicks = 75;

[Desc("After a recall, ticks before the same port can redeploy. Per-port cooldown.")]
public readonly int RedeployBlackoutTicks = 30;

[Desc("A new target must be visible from an empty port for this many ticks before deployment fires. " +
	"Prevents flicker-targets from triggering deploys.")]
public readonly int TargetConfirmTicks = 10;

[Desc("After a deployed port loses its current target's arc/LOS, ticks the port stays committed " +
	"to that target before clearing and rescanning. Smooths over brief LOS gaps.")]
public readonly int StickyTargetTicks = 50;
```

### Step 5.2: Add new fields to `PortState`

- [ ] In the same file, locate the `PortState` class (around line 107). Add these fields after `IsDucking`:

```csharp
// Tick when this port's current soldier deployed; used to gate recall via MinDeployTicks.
public int DeployedAtTick;

// Ticks remaining before this port can redeploy after a recall.
public int RedeployBlackoutRemaining;

// Empty port has seen this target — needs to be confirmed across TargetConfirmTicks before deploying.
public Target PendingDeployTarget;
public int PendingDeployTicks;

// Deployed port has lost its current target's arc/LOS but is staying committed for this long.
public int StickyTargetRemaining;
```

- [ ] In the `PortState` constructor, initialize `PendingDeployTarget`:

```csharp
public PortState(GarrisonPortInfo port)
{
	Port = port;
	CurrentTarget = Target.Invalid;
	PendingDeployTarget = Target.Invalid;
}
```

### Step 5.3: Set `DeployedAtTick` on deploy

- [ ] In the same file, locate `DeployToPort` (around line 277). At the end of the method, after the `PortStates[portIndex].IdleTicks = 0;` line, add:

```csharp
PortStates[portIndex].DeployedAtTick = (int)self.World.WorldTick;
PortStates[portIndex].StickyTargetRemaining = 0;
PortStates[portIndex].PendingDeployTarget = Target.Invalid;
PortStates[portIndex].PendingDeployTicks = 0;
```

(Cast to `int` because `WorldTick` is `long`; PortState's existing fields are `int` — keeping consistency. Even at 25 tps the int range covers ~27 years of play, well beyond any realistic match.)

### Step 5.4: Set `RedeployBlackoutRemaining` on recall

- [ ] In the same file, locate `RecallToShelter` (around line 327). At the start of the method, after the dead-check, add:

```csharp
PortStates[portIndex].RedeployBlackoutRemaining = Info.RedeployBlackoutTicks;
PortStates[portIndex].StickyTargetRemaining = 0;
PortStates[portIndex].PendingDeployTarget = Target.Invalid;
PortStates[portIndex].PendingDeployTicks = 0;
```

So the modified head of `RecallToShelter` looks like:

```csharp
void RecallToShelter(int portIndex)
{
	var soldier = PortStates[portIndex].DeployedSoldier;
	if (soldier == null || soldier.IsDead)
		return;

	PortStates[portIndex].RedeployBlackoutRemaining = Info.RedeployBlackoutTicks;
	PortStates[portIndex].StickyTargetRemaining = 0;
	PortStates[portIndex].PendingDeployTarget = Target.Invalid;
	PortStates[portIndex].PendingDeployTicks = 0;

	// Clear port assignment
	// ... (existing code unchanged)
```

### Step 5.5: Decrement RedeployBlackoutRemaining each tick

- [ ] In the same file, locate the `Tick` method's per-port decrement loop (around line 485):

```csharp
for (var i = 0; i < PortStates.Length; i++)
{
	if (PortStates[i].SwapCooldownRemaining > 0)
		PortStates[i].SwapCooldownRemaining--;
	if (PortStates[i].SuppressionLockoutRemaining > 0)
		PortStates[i].SuppressionLockoutRemaining--;
}
```

- [ ] Replace with:

```csharp
for (var i = 0; i < PortStates.Length; i++)
{
	if (PortStates[i].SwapCooldownRemaining > 0)
		PortStates[i].SwapCooldownRemaining--;
	if (PortStates[i].SuppressionLockoutRemaining > 0)
		PortStates[i].SuppressionLockoutRemaining--;
	if (PortStates[i].RedeployBlackoutRemaining > 0)
		PortStates[i].RedeployBlackoutRemaining--;
	if (PortStates[i].StickyTargetRemaining > 0)
		PortStates[i].StickyTargetRemaining--;
}
```

### Step 5.6: Gate idle-recall on `MinDeployTicks`

- [ ] In the same file, locate the idle-accumulation block in `Tick` (around line 567):

```csharp
else
{
	// No valid target — increment idle timer and recall if threshold reached
	ps.IdleTicks += Info.TargetScanInterval;
	if (Info.IdleRecallTicks > 0 && ps.IdleTicks >= Info.IdleRecallTicks)
	{
		RecallToShelter(i);
		continue;
	}

	// Reload swap: ...
```

- [ ] Replace the `if (Info.IdleRecallTicks > 0 && ps.IdleTicks >= Info.IdleRecallTicks)` block so it also requires `MinDeployTicks` to have elapsed:

```csharp
else
{
	// No valid target — increment idle timer and recall if threshold reached
	ps.IdleTicks += Info.TargetScanInterval;
	var deployedFor = (int)self.World.WorldTick - ps.DeployedAtTick;
	if (Info.IdleRecallTicks > 0 && ps.IdleTicks >= Info.IdleRecallTicks
		&& deployedFor >= Info.MinDeployTicks)
	{
		RecallToShelter(i);
		continue;
	}

	// Reload swap: ...
```

### Step 5.7: Gate empty-port deploy on `RedeployBlackoutTicks` and `TargetConfirmTicks`

- [ ] In the same file, locate the empty-port deploy block in `Tick` (around line 600):

```csharp
else
{
	// Port is empty — skip if locked out by suppression
	if (ps.SuppressionLockoutRemaining > 0)
		continue;

	// Respect fire discipline stance before auto-deploying
	if (buildingStance == UnitStance.HoldFire)
		continue;

	if (buildingStance == UnitStance.Ambush && !ambushTriggered)
		continue;

	// Check if there's a target that warrants deployment
	var target = ScanForTarget(i);
	if (target.IsValidFor(self))
	{
		var soldier = FindBestShelterSoldier(i, target);
		if (soldier != null)
		{
			DeployToPort(i, soldier);
			ps.CurrentTarget = target;
			ps.TargetLockTicks = Info.TargetScanInterval * 2;
		}
	}
}
```

- [ ] Replace with:

```csharp
else
{
	// Port is empty — skip if locked out by suppression OR redeploy-blackout
	if (ps.SuppressionLockoutRemaining > 0 || ps.RedeployBlackoutRemaining > 0)
		continue;

	// Respect fire discipline stance before auto-deploying
	if (buildingStance == UnitStance.HoldFire)
		continue;

	if (buildingStance == UnitStance.Ambush && !ambushTriggered)
		continue;

	// Check if there's a target that warrants deployment
	var target = ScanForTarget(i);
	if (target.IsValidFor(self))
	{
		// Confirm the target across TargetConfirmTicks before deploying.
		// Stops a target that's only briefly visible (LOS flicker) from triggering deploys.
		if (TargetsMatch(ps.PendingDeployTarget, target))
		{
			ps.PendingDeployTicks += Info.TargetScanInterval;
		}
		else
		{
			ps.PendingDeployTarget = target;
			ps.PendingDeployTicks = Info.TargetScanInterval;
		}

		if (ps.PendingDeployTicks >= Info.TargetConfirmTicks)
		{
			var soldier = FindBestShelterSoldier(i, target);
			if (soldier != null)
			{
				DeployToPort(i, soldier);
				ps.CurrentTarget = target;
				ps.TargetLockTicks = Info.TargetScanInterval * 2;
			}

			ps.PendingDeployTarget = Target.Invalid;
			ps.PendingDeployTicks = 0;
		}
	}
	else
	{
		// No target — clear pending deploy state
		ps.PendingDeployTarget = Target.Invalid;
		ps.PendingDeployTicks = 0;
	}
}
```

### Step 5.8: Add `TargetsMatch` helper

- [ ] In the same file, immediately before the `Tick` method, add this private helper:

```csharp
// Check if two Targets refer to the same actor (ignoring transient Target.Position state).
// Used by the deploy-confirm window to detect "same target seen N ticks in a row".
static bool TargetsMatch(in Target a, in Target b)
{
	if (a.Type != b.Type)
		return false;

	if (a.Type == TargetType.Actor)
		return a.Actor == b.Actor;

	// For non-actor targets (terrain, frozen actors), be conservative: don't match.
	// Empty-port deploy only cares about actor targets in practice.
	return false;
}
```

### Step 5.9: Apply sticky-target logic in `UpdatePortTarget`

- [ ] In the same file, locate `UpdatePortTarget` (around line 672). The existing logic ends with a rescan when `TargetLockTicks` expires:

```csharp
void UpdatePortTarget(int portIndex)
{
	var ps = PortStates[portIndex];

	// Player override persists until target dies or goes out of range
	if (ps.PlayerOverride)
	{
		if (ps.CurrentTarget.IsValidFor(self))
			return;

		ps.PlayerOverride = false;
	}

	// Force attack from AttackGarrisoned
	if (hasForceTarget && forceTarget.IsValidFor(self))
	{
		if (IsTargetInPortArc(portIndex, forceTarget))
		{
			ps.CurrentTarget = forceTarget;
			ps.TargetLockTicks = Info.TargetScanInterval;
			ps.PlayerOverride = true;
			return;
		}
	}

	// If target lock is active and target still valid, keep it
	if (ps.TargetLockTicks > 0 && ps.CurrentTarget.IsValidFor(self))
		return;

	// Scan for best target
	ps.CurrentTarget = ScanForTarget(portIndex);
	ps.TargetLockTicks = Info.TargetScanInterval;
}
```

- [ ] Replace the body so sticky-target keeps `CurrentTarget` even when the target leaves the arc:

```csharp
void UpdatePortTarget(int portIndex)
{
	var ps = PortStates[portIndex];

	// Player override persists until target dies or goes out of range
	if (ps.PlayerOverride)
	{
		if (ps.CurrentTarget.IsValidFor(self))
			return;

		ps.PlayerOverride = false;
	}

	// Force attack from AttackGarrisoned
	if (hasForceTarget && forceTarget.IsValidFor(self))
	{
		if (IsTargetInPortArc(portIndex, forceTarget))
		{
			ps.CurrentTarget = forceTarget;
			ps.TargetLockTicks = Info.TargetScanInterval;
			ps.PlayerOverride = true;
			ps.StickyTargetRemaining = 0;
			return;
		}
	}

	// If target lock is active and target still valid, keep it
	if (ps.TargetLockTicks > 0 && ps.CurrentTarget.IsValidFor(self))
		return;

	// Sticky-target: when the existing CurrentTarget is alive but currently outside
	// the port's arc, stay committed for StickyTargetTicks before rescanning. This
	// stops port↔shelter flapping when a target briefly steps out of view.
	if (ps.CurrentTarget.IsValidFor(self) && !IsTargetInPortArc(portIndex, ps.CurrentTarget))
	{
		if (ps.StickyTargetRemaining <= 0)
			ps.StickyTargetRemaining = Info.StickyTargetTicks;

		// Sticky timer drains in the per-port decrement loop in Tick.
		// While > 0, keep CurrentTarget — don't rescan.
		if (ps.StickyTargetRemaining > 0)
		{
			ps.TargetLockTicks = Info.TargetScanInterval;
			return;
		}
	}

	// Target re-entered arc: reset sticky timer
	if (ps.CurrentTarget.IsValidFor(self) && IsTargetInPortArc(portIndex, ps.CurrentTarget))
		ps.StickyTargetRemaining = Info.StickyTargetTicks;

	// Scan for best target
	ps.CurrentTarget = ScanForTarget(portIndex);
	ps.TargetLockTicks = Info.TargetScanInterval;
	ps.StickyTargetRemaining = ps.CurrentTarget.IsValidFor(self) ? Info.StickyTargetTicks : 0;
}
```

### Step 5.10: Build

- [ ] Run: `powershell -ExecutionPolicy Bypass -File ./make.ps1 all`
- [ ] Expected: "Build succeeded. 0 Warning(s) 0 Error(s)"
- [ ] If build fails on the `TargetsMatch` access (e.g. ambiguity with another method), rename to `IsSameDeployTarget`.

### Step 5.11: Commit

- [ ] Run:

```bash
git add engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs
git commit -m "GarrisonManager: hysteresis + sticky targets to stop port↔shelter flapping

Soldiers thrashed between ports and shelter when targets churned (LOS
flicker, multiple enemies passing through arc one at a time, sustained
suppression). Root cause: IdleRecallTicks was the only recall gate, no
hysteresis on redeploy.

Five new tunables on GarrisonManagerInfo:
- IdleRecallTicks: 125 -> 250 (~10s, was ~5s)
- MinDeployTicks: 75 (~3s) — soldier stays at port at least this long
  after deploy regardless of target loss
- RedeployBlackoutTicks: 30 (~1.2s) — port can't redeploy this long
  after recall
- TargetConfirmTicks: 10 (~0.4s) — target must be visible this long
  before triggering empty-port deploy
- StickyTargetTicks: 50 (~2s) — port stays committed to its target
  through brief arc/LOS gaps

Five new PortState fields track the gates: DeployedAtTick,
RedeployBlackoutRemaining, PendingDeployTarget, PendingDeployTicks,
StickyTargetRemaining. Decrements happen in the existing per-port
loop. UpdatePortTarget keeps CurrentTarget when alive but out-of-arc
until StickyTargetRemaining drains.

All tunables are YAML-overridable per-building. Starter values; expect
1-2 playtest passes to settle."
```

---

## Task 6: RELEASE_V1.md and Hotboard updates, completion bell

### Step 6.1: Update RELEASE_V1.md status flags

- [ ] Open `CLAUDE/RELEASE_V1.md`. Find the `Garrison playtest 260504 — observations checklist` section.

- [ ] Mark the **Garrisoned soldiers can't be rearmed** item as fixed and update the description:

Old:

```markdown
  - [ ] **Garrisoned soldiers can't be rearmed** — recent CargoSupply commit (58e434bb) tried to rearm shelter soldiers; user reports rearm now broken for *both* port AND shelter soldiers (previously port worked). Suspect: `ResupplyTarget` early-exits on `!currentTarget.IsInWorld` (CargoSupply.cs:330) which kicks out shelter soldiers immediately after they're picked. Need: rearm regardless of port/shelter location.
```

New:

```markdown
  - [T] **Garrisoned soldiers can't be rearmed** — *Fixed 260504 (commit 56a31d89)*: removed the `!currentTarget.IsInWorld` early-exit in `ResupplyTarget` (CargoSupply.cs:330). Shelter passengers are intentionally out-of-world; SetTarget already skipped the move-toward and condition-grant correctly. Verify both port AND shelter rearm in playtest.
```

- [ ] Update the Phase B garrison-related items to flag they're being addressed by Round 1:

Find:

```markdown
- [ ] **Garrison: only first soldier of a batch enters** — when 2+ soldiers ordered to enter a building together, only the first completes; later soldiers approach, then go idle near the building. Strong hypothesis: building's `ChangeOwnerInPlace` on first entry (`GarrisonManager.cs:203`) triggers `World.Remove/Add + shroud recalc` (`Cargo.cs:466` comment confirms this is expensive) which invalidates the second soldier's Enter activity targeting the actor. Workaround: order each soldier individually after the first is in. *Reported 260503*
```

Replace with:

```markdown
- [T] **Garrison: only first soldier of a batch enters** — Mitigation already in code (GarrisonManager.cs:198-208, 261-273): both `OnPassengerEntered` and `CheckOwnershipAfterExit` use `ChangeOwnerInPlace(updateGeneration: false)` specifically to keep in-flight Enter activities from other allied soldiers valid. Verify in next playtest — if bug is gone, flip to `[x]`. *Reported 260503*
```

Find:

```markdown
- [ ] **Stop order doesn't cancel garrisoned firing** — soldiers inside a building keep firing after `S` (stop) is pressed; the stop order isn't reaching the garrisoned soldier or the building's AttackGarrisoned activity. *Reported 260503*
```

Replace with:

```markdown
- [T] **Stop order doesn't cancel garrisoned firing** — *Fixed 260504*: AttackGarrisoned now overrides OnStopOrder, calling new GarrisonManager.OnStopOrder which clears forceTarget, all PortState targets, PlayerOverride flags, and resets ambushTriggered. Matches Mobile-unit Stop semantics (cancel current intent, baseline behavior resumes per stance). Verify in playtest — note: with FireAtWill, soldiers will re-pick the same enemies on next scan tick (this is correct behavior; for permanent silence use HoldFire stance). *Reported 260503*
```

Find:

```markdown
- [ ] **Soldiers under fire abandon Enter-building order** — when ordered to enter a building while enemies are firing, most soldiers pause to return fire instead of completing the entry. Almost certainly SmartMove (`IWrapMove`) wrapping the move-to-building portion of the Enter activity — the move-portion fires SmartMove behavior, soldiers stop to engage. Needs SmartMove to skip wrapping when the inner move is part of an Enter/Garrison activity, OR Enter should bypass SmartMove. *Reported 260503*
```

Replace with:

```markdown
- [T] **Soldiers under fire abandon Enter-building order** — *Fixed 260504*: added `MoveToTargetRaw`/`MoveIntoTargetRaw` to IMove that bypass WrapMove. Enter uses raw variants on lines 108, 122. Side effect (intentional): Capture, Demolish, RideTransport, Infiltrate etc. all benefit — any "go to that thing and act" order stays focused. *Reported 260503*
```

- [ ] Update the playtest 260504 sub-items for **Pre-entry stop** and **Garrison port↔shelter chaotic switching**:

Find:

```markdown
  - [ ] **Pre-entry stop** — units pause ~0.5–1s outside the building before entering. Should start entering immediately on arrival (or, ideally, check fullness 4 tiles out so a queue of soldiers ordered to fill multiple buildings can skip-ahead to the next building when current one is full instead of all walking up to verify). When no further orders queued: walk all the way & stop normally. Touches Enter activity / GarrisonManager fullness check.
```

Replace with:

```markdown
  - [T] **Pre-entry stop + queue skip-ahead** — *Fixed 260504*: (a) collapsed `MoveCooldownHelper` cooldown to (0,1) for Enter (was the source of the visible 0.8-1.2s pause when destination cell registered as blocked); (b) RideTransport.TickInner now checks `Cargo.HasSpace` per-tick during Approaching and `Cancel(keepQueue:true)` on full so shift-queued soldiers skip-ahead to the next building. Verify in playtest.
```

Find:

```markdown
  - [ ] **Garrison port↔shelter chaotic switching** — units flip between ports and shelter too often, looks frantic. Add cooldowns: building max 1 switch per ~2s; per-unit min 5s between switches. Tunable. Touches GarrisonManager Tick / RecallToShelter / DeployToPort.
```

Replace with:

```markdown
  - [T] **Garrison port↔shelter chaotic switching** — *Fixed 260504*: added 4 hysteresis fields to GarrisonManagerInfo (`MinDeployTicks=75`, `RedeployBlackoutTicks=30`, `TargetConfirmTicks=10`, `StickyTargetTicks=50`) plus bumped `IdleRecallTicks` 125→250. Sticky targets keep ports committed through brief arc/LOS gaps. All YAML-tunable. Playtest will tune values.
```

### Step 6.2: Update HOTBOARD.md

- [ ] Open `CLAUDE/HOTBOARD.md`. Update "Working on" and "Recent wins":

Old "Working on":

```markdown
## Working on
- **Garrison overhaul playtest** (260503_1241) — checklist in `CLAUDE/playtests/260503_1241_garrison.md`, awaiting findings
```

New "Working on":

```markdown
## Working on
- **Garrison stabilization Round 1 (260504)** — items 3, 4, 5A, 5B, 6 shipped; awaiting playtest verification. Spec: `CLAUDE/plans/260504_garrison_stabilization_design.md`. Plan: `CLAUDE/plans/260504_garrison_stabilization_plan.md`
```

Move the previous "Garrison overhaul Phases 1–6" line to Recent Wins (drop the oldest entry to keep last 5):

```markdown
## Recent Wins (last 5)
- **Garrison stabilization Round 1** — Stop order, raw IMove, pre-entry pause, skip-ahead, hysteresis+sticky-targets (5 commits)
- **Garrison overhaul Phases 1–6** — indestructible buildings, dynamic ownership, directional targeting, suppression integration, visuals
- **Cargo system Phases 2A–E** — CargoSupply, TRUK→pure transport, cargo panel, mark+unload
- **Helicopter crash + crew overhaul** — VehicleCrew on all helis, two-tier emergency landing, capture-by-pilot
- **Stance rework Phases 1–4** — modifiers, tooltips, resupply, cohesion, patrol
```

### Step 6.3: Ring the bell

- [ ] Run: `printf "\a"`

### Step 6.4: Final commit

- [ ] Run:

```bash
git add CLAUDE/RELEASE_V1.md CLAUDE/HOTBOARD.md
git commit -m "Garrison stabilization Round 1: update RELEASE_V1.md + Hotboard

Round 1 stabilization shipped — items 3 (Stop), 4 (raw IMove), 5A
(cooldown bypass), 5B (skip-ahead), 6 (hysteresis+sticky-targets).
Item 1 (rearm regression) shipped earlier in commit 56a31d89.
Item 2 (batch-entry verify) is playtest-only; updateGeneration:false
mitigation already in code, just needs visual confirmation.

All flipped from [ ] -> [T] (testing). Next playtest verifies the
stack and any item that comes back clean flips to [x]."
```

### Step 6.5: Verify

- [ ] Run: `git log --oneline -10`
- [ ] Expected: 6 new commits ahead of `f75d0a9e` covering items 1, 3, 5A, 4, 5B, 6, plus this RELEASE update.

---

## Self-Review

Spec coverage check (each spec section maps to a task):

| Spec section | Task |
|---|---|
| Item 3 — Soft Stop | Task 1 |
| Item 4 — Raw IMove variants | Task 3 |
| Item 5A — Cooldown bypass | Task 2 |
| Item 5B — Skip-ahead capacity check | Task 4 |
| Item 6 — Hysteresis + sticky targets | Task 5 |
| Open Q1 (shift-queue per-soldier chains) | Verified in spec dialog; trust in playtest. No task — was confirmation, not work. |
| Open Q2 (RequestedTarget clear in OnStopOrder) | Mitigated by base.OnStopOrder call in Task 1 Step 1.2 (AttackBase clears its own state via CancelActivity); GarrisonManager.OnStopOrder handles its own additional state. |
| RELEASE_V1.md / Hotboard updates | Task 6 |

Type consistency: `OnStopOrder(Actor self)` signature on GarrisonManager and the override on AttackGarrisoned both match `public override void OnStopOrder(Actor self)` from AttackBase. `MoveToTargetRaw` / `MoveIntoTargetRaw` signatures are consistent across IMove, Mobile, and Aircraft. `TargetsMatch` is the only new helper; named consistently in both definition and use.

Placeholder scan: no TBDs, no "implement later", every code step has actual code, every command has an expected output.

## Verification gates

After all 6 tasks land, before declaring done:

- `git log --oneline f75d0a9e..HEAD` → should show 6 commits (rearm + 5 round-1 items + status update)
- Build clean from a clean run: `powershell -ExecutionPolicy Bypass -File ./make.ps1 all`
- User playtest pass through the 260504 garrison checklist; any item that doesn't surface a regression flips `[T] → [x]`.
