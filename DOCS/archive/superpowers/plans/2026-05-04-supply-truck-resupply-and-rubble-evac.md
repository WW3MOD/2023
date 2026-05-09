# Supply Truck Resupply Bar + Rubble Building Evacuation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give supply trucks the same resupply automation as ammo units (Hold / Auto / Evacuate), with Auto driving them to a Logistics Center for pip-by-pip refill. Plus: ensure 1HP rubble garrison buildings allow soldier evacuation, reduce protection at rubble state, and fix port+basement double-display bug.

**Architecture:**
- Reuse the existing `SupplyProvider` (already on LC) by extending it to recognise `CargoSupply` targets in addition to `Rearmable` ammo pools. One supply unit transferred per `RearmDelay` tick. No new trait classes.
- Add an `AutoRefillIfEmpty` dispatcher on `CargoSupply` that mirrors `AmmoPool.AutoRearmIfAllEmpty` — same three behaviors (Hold / Auto / Evacuate), Auto seeks nearest LC and falls through to Evacuate if none found.
- Resupply stance bar visibility extended to also show for units with `CargoSupply`.
- Default stance for TRUK overridden to `Evacuate` (ammo units stay at `Auto`).
- For rubble: add `RubbleProtection` field to `GarrisonProtection`, ensure `Unload` order is never gated by HP/damage state, and fix the soldier-state asymmetry that produces double-display.

**Tech Stack:** C# .NET (engine), NUnit 3 (tests), YAML (mod rules), MiniYaml.

---

## File Structure

**Modify (engine):**
- `engine/OpenRA.Mods.Common/Traits/CargoSupply.cs` — add `AutoRefillIfEmpty` dispatch, hook into Tick when supply hits zero, INotifyBecomingIdle.
- `engine/OpenRA.Mods.Common/Traits/SupplyProvider.cs` — extend `IsValidTarget`, `FindGreatestNeedTarget`, `ResupplyTarget` to handle `CargoSupply` targets in addition to `Rearmable`.
- `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/ResupplyBehaviorSelectorLogic.cs` — extend selection gate to include `CargoSupply`.
- `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonProtection.cs` — add `RubbleProtection` field, gate by HP at minimum.
- `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs` — ensure Unload accepts at any HP; investigate and fix soldier-state asymmetry.
- `engine/OpenRA.Mods.Common/Traits/Garrison/WithGarrisonDecoration.cs` — ensure each soldier renders in exactly one location.

**Modify (mod YAML):**
- `mods/ww3mod/rules/ingame/vehicles-america.yaml`, `vehicles-russia.yaml`, `vehicles.yaml` — TRUK default stance.
- `mods/ww3mod/rules/ingame/civilian.yaml` — `RubbleProtection` field on `GarrisonProtection`.

**Create (tests):**
- `engine/OpenRA.Test/CargoSupplyAutoRefillTest.cs` — unit tests for the Hold/Auto/Evacuate dispatch logic.

---

## Phase 1: Supply Truck Resupply Bar (UI gating + dispatch)

### Task 1.1: Resupply bar shows for CargoSupply units

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/ResupplyBehaviorSelectorLogic.cs:66-81`

- [ ] **Step 1: Edit the selection gate**

In `UpdateStateIfNecessary()`, change:

```cs
actorStances = world.Selection.Actors
    .Where(a => a.Owner == world.LocalPlayer && a.IsInWorld
        && a.TraitsImplementing<AmmoPool>().Any())
    .SelectMany(...)
```

to:

```cs
actorStances = world.Selection.Actors
    .Where(a => a.Owner == world.LocalPlayer && a.IsInWorld
        && (a.TraitsImplementing<AmmoPool>().Any() || a.TraitsImplementing<CargoSupply>().Any()))
    .SelectMany(...)
```

- [ ] **Step 2: Build**

Run: `./make.ps1 all`
Expected: clean build. If the game is running, the build will fail with locked DLLs — that's normal, just continue.

- [ ] **Step 3: Commit**

```bash
git add engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/ResupplyBehaviorSelectorLogic.cs
git commit -m "ResupplyBehaviorSelector: also show for CargoSupply units (supply trucks)"
```

---

### Task 1.2: CargoSupply.AutoRefillIfEmpty dispatcher (Hold / Auto / Evacuate)

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/CargoSupply.cs`

- [ ] **Step 1: Add the dispatch method**

At the end of the `CargoSupply` class (before closing brace), add:

```cs
public void AutoRefillIfEmpty(Actor self)
{
    if (supplyCount > 0 || effectiveSupply > 0)
        return;

    var autoTarget = self.TraitOrDefault<AutoTarget>();
    var behavior = autoTarget?.ResupplyBehaviorValue ?? ResupplyBehavior.Auto;

    switch (behavior)
    {
        case ResupplyBehavior.Hold:
            // Sit. No-op.
            return;

        case ResupplyBehavior.Auto:
            if (TryQueueMoveToLogisticsCenter(self))
                return;
            // No LC available — fall through to Evacuate.
            goto case ResupplyBehavior.Evacuate;

        case ResupplyBehavior.Evacuate:
            var amount = self.GetSellValue();
            self.QueueActivity(false, new RotateToEdge(self, true, amount));
            self.ShowTargetLines();
            return;
    }
}

bool TryQueueMoveToLogisticsCenter(Actor self)
{
    var move = self.TraitOrDefault<IMove>();
    if (move == null)
        return false;

    var targetLC = self.World.ActorsHavingTrait<SupplyProvider>()
        .Where(a => !a.IsDead && a.IsInWorld
            && Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner))
            && a.Trait<SupplyProvider>().CurrentSupply > 0)
        .ClosestToIgnoringPath(self);

    if (targetLC == null)
        return false;

    var targetCell = self.World.Map.CellContaining(targetLC.CenterPosition);
    self.QueueActivity(false, move.MoveTo(targetCell, 3));
    self.ShowTargetLines();
    return true;
}
```

- [ ] **Step 2: Trigger AutoRefill on supply depletion**

In `ResupplyTarget()`, immediately after the line `if (newCount != supplyCount)` block (around line 386), add a check after the supply has been decremented. Find the section:

```cs
if (newCount != supplyCount)
{
    supplyCount = newCount;
    UpdateSupplyCondition();
}
```

Right after this block, before the `if (!string.IsNullOrEmpty(bestPool.Info.RearmSound))` block, insert:

```cs
if (supplyCount <= 0)
    AutoRefillIfEmpty(self);
```

- [ ] **Step 3: Add INotifyBecomingIdle hook**

Change the class declaration:

```cs
public class CargoSupply : ITick, INotifyCreated, ISelectionBar, ITransformActorInitModifier, IResolveOrder
```

to:

```cs
public class CargoSupply : ITick, INotifyCreated, INotifyBecomingIdle, ISelectionBar, ITransformActorInitModifier, IResolveOrder
```

Add the method anywhere convenient in the class:

```cs
void INotifyBecomingIdle.OnBecomingIdle(Actor self)
{
    AutoRefillIfEmpty(self);
}
```

- [ ] **Step 4: Build**

Run: `./make.ps1 all`
Expected: clean build.

- [ ] **Step 5: Commit**

```bash
git add engine/OpenRA.Mods.Common/Traits/CargoSupply.cs
git commit -m "CargoSupply: AutoRefillIfEmpty dispatch (Hold/Auto/Evacuate)

Auto seeks nearest friendly Logistics Center with supply > 0 and queues
move toward it. If no LC available, falls through to Evacuate (rotate
to map edge). Hold is no-op. Triggered on supply depletion in
ResupplyTarget and on becoming idle."
```

---

### Task 1.3: Expose `SupplyProvider.CurrentSupply`

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/SupplyProvider.cs:71-80`

- [ ] **Step 1: Add public accessor**

Find the field declaration `int currentSupply;` near line 75. Add a public read-only accessor right after the field declarations:

```cs
public int CurrentSupply => currentSupply;
```

- [ ] **Step 2: Build**

Run: `./make.ps1 all`
Expected: clean build. (Required by Task 1.2's call site.)

- [ ] **Step 3: Commit**

```bash
git add engine/OpenRA.Mods.Common/Traits/SupplyProvider.cs
git commit -m "SupplyProvider: expose CurrentSupply for CargoSupply auto-refill scan"
```

---

## Phase 2: Logistics Center Refills Supply Trucks

### Task 2.1: SupplyProvider recognises CargoSupply targets

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/SupplyProvider.cs:199-232` (FindGreatestNeedTarget), `:257-287` (IsValidTarget), `:336+` (ResupplyTarget)

- [ ] **Step 1: Extend `IsValidTarget` to accept CargoSupply units**

Replace the body of `IsValidTarget` (lines ~257–287) with:

```cs
bool IsValidTarget(Actor a)
{
    if (a == null || a.IsDead || !a.IsInWorld || a == self)
        return false;

    if (!Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner)))
        return false;

    var dist = (a.CenterPosition - self.CenterPosition).HorizontalLength;
    if (dist > Info.Range.Length)
        return false;

    // Ammo target: Rearmable with at least one non-full pool.
    var rearmable = a.TraitOrDefault<Rearmable>();
    if (rearmable != null && rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo))
    {
        if (!string.IsNullOrEmpty(Info.RearmCondition))
        {
            var ec = a.TraitsImplementing<ExternalCondition>()
                .FirstOrDefault(e => e.Info.Condition == Info.RearmCondition);
            if (ec == null)
                return false;
        }

        return true;
    }

    // Supply truck target: CargoSupply with free capacity.
    var cargoSupply = a.TraitOrDefault<CargoSupply>();
    if (cargoSupply != null && cargoSupply.SupplyCount < cargoSupply.Info.MaxSupply)
        return true;

    return false;
}
```

- [ ] **Step 2: Extend `FindGreatestNeedTarget` to consider CargoSupply units**

Replace `FindGreatestNeedTarget` (lines ~199–232) with:

```cs
Actor FindGreatestNeedTarget(out bool hasUnaffordableTargets)
{
    Actor best = null;
    var bestNeed = 0f;
    hasUnaffordableTargets = false;

    foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, Info.Range))
    {
        if (!IsValidTarget(a))
            continue;

        var rearmable = a.TraitOrDefault<Rearmable>();
        if (rearmable != null && rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo))
        {
            if (!rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo && currentSupply >= p.Info.SupplyValue))
            {
                hasUnaffordableTargets = true;
                continue;
            }

            var need = CalculateNeed(a);
            if (need < Info.MinNeedThreshold)
                continue;

            if (need > bestNeed)
            {
                bestNeed = need;
                best = a;
            }

            continue;
        }

        var cargoSupply = a.TraitOrDefault<CargoSupply>();
        if (cargoSupply != null && cargoSupply.SupplyCount < cargoSupply.Info.MaxSupply)
        {
            // Need = fraction of capacity missing.
            var capacity = cargoSupply.Info.MaxSupply;
            var missing = capacity - cargoSupply.SupplyCount;
            var need = (float)missing / capacity;

            if (need < Info.MinNeedThreshold)
                continue;

            if (need > bestNeed)
            {
                bestNeed = need;
                best = a;
            }
        }
    }

    return best;
}
```

- [ ] **Step 3: Extend `ResupplyTarget` to deliver to CargoSupply units**

Find `ResupplyTarget()` (line ~336). The current method gives ammo to a `Rearmable` target. Replace with logic that branches by target type. Replace the existing method body with:

```cs
void ResupplyTarget()
{
    if (currentTarget == null || currentTarget.IsDead || !currentTarget.IsInWorld)
    {
        RevokeTargetCondition();
        currentTarget = null;
        return;
    }

    // CargoSupply target: transfer one supply unit per cycle.
    var cargoSupply = currentTarget.TraitOrDefault<CargoSupply>();
    if (cargoSupply != null && cargoSupply.SupplyCount < cargoSupply.Info.MaxSupply)
    {
        // Cost of one supply unit at the LC = SupplyPerUnit (one ammo "pip" worth).
        var costPerUnit = cargoSupply.Info.SupplyPerUnit;
        if (currentSupply >= costPerUnit)
        {
            var added = cargoSupply.AddSupply(1);
            if (added > 0)
            {
                currentSupply -= costPerUnit;
                UpdateSupplyConditions();
            }
        }

        // Drop target to re-evaluate on next scan.
        RevokeTargetCondition();
        currentTarget = null;
        rearmTicks = Info.RearmDelay;
        return;
    }

    // Existing ammo-rearm path follows. (Preserve current ammo logic verbatim.)
    var rearmable = currentTarget.TraitOrDefault<Rearmable>();
    if (rearmable == null)
    {
        RevokeTargetCondition();
        currentTarget = null;
        return;
    }

    // ... [rest of existing ResupplyTarget body — find pool, give ammo, etc.] ...
}
```

**Important:** preserve the existing ammo-rearm logic verbatim after the CargoSupply branch — copy from current lines ~336 to the end of the method. Do not delete the existing flow.

- [ ] **Step 4: Verify `UpdateSupplyConditions` exists**

Search SupplyProvider.cs for `UpdateSupplyConditions` — if it's named differently (e.g. `UpdateSupplyCondition`), use whatever the file actually calls. The intent is: refresh the supply-level conditions after `currentSupply` changes.

- [ ] **Step 5: Build**

Run: `./make.ps1 all`
Expected: clean build.

- [ ] **Step 6: Commit**

```bash
git add engine/OpenRA.Mods.Common/Traits/SupplyProvider.cs
git commit -m "SupplyProvider: refill CargoSupply targets (supply trucks at LC)

LC's existing supply pool now also services empty supply trucks. Targets
are picked by greatest need (capacity fraction missing); transfer rate
is one CargoSupply unit per RearmDelay, costing SupplyPerUnit from LC's
pool per truck-pip. Existing ammo-pool refill behavior preserved."
```

---

### Task 2.2: Default TRUK stance = Evacuate

**Files:**
- Modify: `mods/ww3mod/rules/ingame/vehicles.yaml` (TRUK template) or `vehicles-america.yaml` / `vehicles-russia.yaml`

- [ ] **Step 1: Find TRUK definition**

Run: search the ingame YAML for `^Truck` or `TRUK:` to locate the template/actor:

```bash
```

(Use Grep tool — `Truck|TRUK:` in `mods/ww3mod/rules/ingame`.)

- [ ] **Step 2: Add `InitialResupplyBehavior: Evacuate` under AutoTarget**

In whichever file holds the TRUK base trait set (likely `vehicles.yaml` under `^Truck` or under `TRUK:` directly), find the `AutoTarget:` block and add:

```yaml
AutoTarget:
    InitialResupplyBehavior: Evacuate
    InitialResupplyBehaviorAI: Evacuate
```

(Merge with existing AutoTarget fields if the trait is already present — don't duplicate.)

- [ ] **Step 3: Build & start the game (manual sanity check)**

Run: `./make.ps1 all`
Expected: clean build. The user will playtest later — don't launch the game from the agent.

- [ ] **Step 4: Commit**

```bash
git add mods/ww3mod/rules/ingame/vehicles*.yaml
git commit -m "TRUK: default ResupplyBehavior = Evacuate

Supply trucks now default to evacuating off-map when empty. Player can
opt into Auto behavior (refill at Logistics Center) via the resupply
stance bar. Hold = sit still."
```

---

### Task 2.3: Tests for CargoSupply auto-refill dispatch

**Files:**
- Create: `engine/OpenRA.Test/CargoSupplyAutoRefillTest.cs`

- [ ] **Step 1: Inspect existing test patterns**

Open `engine/OpenRA.Test/AmmoPoolTest.cs` to copy the file header / namespace / NUnit setup style. The tests should follow the same structure.

- [ ] **Step 2: Write tests focused on dispatch logic only**

Most of `CargoSupply` requires a full World instance to test (Tick, IMove, AutoTarget). Realistic NUnit coverage in this codebase is for math/state pieces. Test just the choice logic — given a behavior + LC-availability, what does dispatch do?

If `AutoRefillIfEmpty` is too entangled with World/Actor to mock, **document this in the file's top comment and write tests for just the public state predicates** (`SupplyCount`, `EffectiveSupply`, `AddSupply`/`RemoveSupply` clamping):

```cs
using NUnit.Framework;

namespace OpenRA.Test
{
    [TestFixture]
    public class CargoSupplyAutoRefillTest
    {
        // CargoSupply's AutoRefillIfEmpty dispatch hits World/Actor APIs that
        // are awkward to mock. These tests cover the public state predicates
        // and capacity math; full dispatch behavior is verified by playtest.

        [Test]
        public void AddSupply_ClampedToMaxSupply()
        {
            // Pseudocode — adapt to whatever construction path AmmoPoolTest uses.
            // Assert AddSupply over MaxSupply returns only the available amount.
        }

        [Test]
        public void RemoveSupply_ClampedToZero()
        {
            // Assert RemoveSupply on empty returns 0 and supplyCount stays 0.
        }
    }
}
```

If there's no clean way to instantiate `CargoSupply` outside a World, **skip this task** — playtest is the validation path. Write a comment in the spec/plan saying so, and move on.

- [ ] **Step 3: Run tests**

Run: `dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release`
Expected: PASS (or skip the task if instantiation isn't feasible).

- [ ] **Step 4: Commit (only if tests were actually written)**

```bash
git add engine/OpenRA.Test/CargoSupplyAutoRefillTest.cs
git commit -m "CargoSupply: add capacity math tests"
```

---

## Phase 3: Rubble Building — Soldier Evacuation + Reduced Protection

### Task 3.1: Investigate why Unload doesn't work at 1HP

This is investigation, not implementation. Goal: identify the gating cause before patching.

**Files:**
- Read: `engine/OpenRA.Mods.Common/Traits/Cargo.cs` (look for Unload order resolve, RequiresCondition)
- Read: `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs:1251+` (Unload resolution)
- Read: `mods/ww3mod/rules/ingame/civilian.yaml`, `^DamageStates` template (search `^DamageStates`)

- [ ] **Step 1: Trace the Unload command UI**

The Unload button on a Cargo-equipped building comes from `Cargo.cs`. Find: `IssueOrderUnload`, `LoadingCondition`, or any `RequiresCondition` on commands. Specifically check whether Cargo's UnloadCommand has a `RequiresCondition` that excludes `heavy-damage-attained` or `critical-damage`.

- [ ] **Step 2: Trace damage-state conditions**

Check `^DamageStates` template (likely in `defaults.yaml` or similar). It usually grants conditions like `heavy-damage-attained` or `critical-damage` based on health. Note which conditions are granted at the 1HP indestructible state — these are the ones we need to NOT use for gating Unload.

- [ ] **Step 3: Trace AttackGarrisoned gating**

In `engine/OpenRA.Mods.Common/Traits/AttackGarrisoned.cs`, check if it has any `PauseOnCondition` or `RequiresCondition` involving damage states. (Probably yes — to disable port firing at critical damage.) This is fine; the issue is whether **Unload** is similarly gated.

- [ ] **Step 4: Reproduce-style read of GarrisonManager.IResolveOrder**

The Unload case at line 1255 of GarrisonManager.cs only handles port soldiers. It expects Cargo's own ResolveOrder to handle shelter soldiers. Check whether `Cargo.cs`'s ResolveOrder for "Unload" has any HP/condition gate.

- [ ] **Step 5: Write a notes file with findings**

Create `docs/superpowers/specs/2026-05-04-rubble-evacuation-investigation.md` summarising:
- What gates the Unload button visibility in UI
- What gates the Unload order resolution in code
- What conditions are active at 1HP rubble state
- Root cause hypothesis

- [ ] **Step 6: Commit**

```bash
git add docs/superpowers/specs/2026-05-04-rubble-evacuation-investigation.md
git commit -m "Investigation notes: rubble building Unload gating"
```

---

### Task 3.2: Fix the Unload gating

**Files:** depends on Task 3.1 findings. Likely candidates:
- `engine/OpenRA.Mods.Common/Traits/Cargo.cs` — remove a damage-condition gate on UnloadCommand or ResolveOrder
- `mods/ww3mod/rules/ingame/civilian.yaml` — adjust a `RequiresCondition` on a trait

- [ ] **Step 1: Apply the smallest fix that makes Unload reachable at 1HP**

Based on Task 3.1's investigation, remove or relax the gate. Code change shape will be either:
- Engine: drop a `RequiresCondition` from a UnloadCommand-like trait
- YAML: remove `RequiresCondition: !critical-damage` from a Cargo or GarrisonManager block

- [ ] **Step 2: Build**

Run: `./make.ps1 all`
Expected: clean build.

- [ ] **Step 3: Commit**

```bash
git add <file>
git commit -m "Garrison: allow Unload at any HP including rubble state

<one sentence: what was gated, and why removing the gate is correct>"
```

---

### Task 3.3: RubbleProtection field on GarrisonProtection

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonProtection.cs`
- Modify: `mods/ww3mod/rules/ingame/civilian.yaml:109-112`

- [ ] **Step 1: Read GarrisonProtection's current shape**

Open the file. Find `BaseProtection`, `CriticalProtection`, `MinPassThrough`. The protection model: incoming damage is multiplied by `(100 - protection) / 100` percent for soldiers inside.

- [ ] **Step 2: Add `RubbleProtection` field**

In `GarrisonProtectionInfo`, add:

```cs
[Desc("Protection percent when building is at minimum HP (rubble state). " +
    "Lower than CriticalProtection — represents reduced cover from heavy damage. " +
    "Default 30 means soldiers take 70% of incoming damage.")]
public readonly int RubbleProtection = 30;
```

- [ ] **Step 3: Use RubbleProtection at 1HP**

Find the method that picks between `BaseProtection` and `CriticalProtection` (likely a helper called from `IDamageModifier.GetDamageModifier` or similar). Add a third branch: when `health.HP <= 1`, use `RubbleProtection`. Pseudocode:

```cs
int CurrentProtection()
{
    if (health.HP <= 1)
        return Info.RubbleProtection;

    if (<critical-damage condition>)
        return Info.CriticalProtection;

    return Info.BaseProtection;
}
```

(Use whatever existing pattern the file uses to detect critical damage state — `health.DamageState`, a condition token, or similar.)

- [ ] **Step 4: Update YAML**

In `mods/ww3mod/rules/ingame/civilian.yaml`, add `RubbleProtection: 30` to the GarrisonProtection block:

```yaml
GarrisonProtection:
    BaseProtection: 95
    CriticalProtection: 70
    RubbleProtection: 30
    MinPassThrough: 15
```

- [ ] **Step 5: Build**

Run: `./make.ps1 all`
Expected: clean build.

- [ ] **Step 6: Commit**

```bash
git add engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonProtection.cs mods/ww3mod/rules/ingame/civilian.yaml
git commit -m "GarrisonProtection: add RubbleProtection (30%) for 1HP buildings

When indestructible buildings are clamped to 1HP, occupants get reduced
cover. Soldiers can stay inside but take ~70% of incoming damage,
incentivising evacuation."
```

---

### Task 3.4: Investigate and fix port+basement double-display

**Files:**
- Read: `engine/OpenRA.Mods.Common/Traits/Garrison/WithGarrisonDecoration.cs`
- Read: `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs` (recall flow, suppression recall, deploy flow)

- [ ] **Step 1: Read WithGarrisonDecoration's pip-rendering code**

Open the file. Find where it iterates port soldiers and shelter soldiers. The bug is one of:
- (a) The same actor appearing in both `PortStates[i].DeployedSoldier` and `cargo.Passengers` simultaneously.
- (b) Decoration counting `cargo.Passengers.Count` for shelter pips, but `cargo.Passengers` includes a deployed soldier.
- (c) A stale `DeployedSoldier` reference after a soldier was forcibly recalled but the port wasn't cleared.

- [ ] **Step 2: Trace recall flow**

In GarrisonManager.cs, search for code that adds a soldier back to Cargo (recall to shelter). Verify each recall site:
- Sets `PortStates[i].DeployedSoldier = null`
- Calls `RevokePortCondition(i)`
- Calls `cargo.Load(self, soldier)` (or equivalent — the soldier must be added to Cargo's passenger list)

If any path adds to Cargo without clearing the port reference, that's the bug.

- [ ] **Step 3: Trace suppression recall specifically**

The user reports this happens at 1HP rubble — likely in combat where soldiers get suppressed. Suppression recall (`SuppressionRecallThreshold: 60`) is where the asymmetry probably lives. Find the recall-on-suppression code path and check it against the Step 2 invariants.

- [ ] **Step 4: Apply the fix**

Whatever the root cause, ensure: **at any time, a soldier is in exactly one of `{PortStates[*].DeployedSoldier, cargo.Passengers}`, never both.** Add the missing clear/add as needed.

- [ ] **Step 5: Build**

Run: `./make.ps1 all`
Expected: clean build.

- [ ] **Step 6: Commit**

```bash
git add engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs
git commit -m "GarrisonManager: fix port+shelter double-display on recall

<root cause sentence>. Soldier now occupies exactly one of port/shelter
at any time, eliminating duplicate pip rendering."
```

---

## Phase 4: Verification

### Task 4.1: Build clean and update tracking docs

- [ ] **Step 1: Full build**

Run: `./make.ps1 all`
Expected: zero errors, zero warnings on changed files.

- [ ] **Step 2: Run tests**

Run: `dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release`
Expected: all existing tests pass.

- [ ] **Step 3: Update CLAUDE/RELEASE_V1.md**

Add an entry under "Recently completed" (or appropriate phase) summarizing both fixes — supply truck resupply bar + LC refill, and rubble building evacuation.

- [ ] **Step 4: Update CLAUDE/HOTBOARD.md**

Refresh "Working on" / recent wins section.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE/RELEASE_V1.md CLAUDE/HOTBOARD.md
git commit -m "Tracking: supply truck resupply + rubble evacuation shipped"
```

- [ ] **Step 6: Hand back to user for playtest**

Final message should:
- List concrete things to try (✅ select TRUK, see resupply bar; empty TRUK on Auto drives to LC; empty TRUK on Evacuate rotates out; rubble building Unload works; rubble protection feels right).
- Use the end-of-message block format from CLAUDE.md, ending with `⏭️`.

---

## Risks / Notes

- **AmmoConservation conflict:** if a TRUK's CargoSupply target lookup happens at the same tick as an ammo unit also requesting from the same LC, the LC picks by `bestNeed` — make sure the comparison is fair (CargoSupply uses fractional `missing/capacity`, Rearmable uses `totalMissing/totalCapacity`). Both are 0–1 ratios. OK.
- **Default stance for AI:** `InitialResupplyBehaviorAI: Evacuate` may cause AI to lose trucks too aggressively. If that's a problem in playtest, switch AI default back to `Auto` so AI bots try LC refill first (only if AI has built one).
- **Unused investigation file:** if Task 3.1 reveals the issue is trivial (e.g. one YAML line), the investigation notes file is short. That's fine — keep it as a record.
- **YAML changes invalidate save games:** stance defaults are not saved, but be aware of any in-progress lobby tests.

## Acceptance (manual playtest)

After all tasks, the user should verify:
- [ ] Selecting a TRUK shows the 3-button resupply bar.
- [ ] TRUK defaults to Evacuate stance (button highlighted).
- [ ] TRUK on Evacuate, when supply hits 0, drives to map edge and returns credits.
- [ ] TRUK on Auto, when supply hits 0, drives to nearest friendly LC with supply and refills there. Pips fill over time.
- [ ] TRUK on Auto with no LC available falls through to Evacuate.
- [ ] TRUK on Hold sits still when empty.
- [ ] Alt-click Evacuate on a supply-loaded TRUK rotates it out immediately.
- [ ] Garrison building at 1HP rubble: Unload command works; soldiers eject.
- [ ] Soldiers in 1HP rubble take noticeably more damage than at full HP (~70% pass-through vs ~5%).
- [ ] No more double-display: each soldier appears in exactly one location (port OR shelter).
