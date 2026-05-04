# Supply Truck Resupply Behavior + Rubble Building Evacuation

**Date:** 2026-05-04
**Topic:** Two related QoL fixes — supply trucks need the resupply stance bar (with LC-based refill), and garrison buildings at "rubble" 1HP state need to allow soldier evacuation with reduced protection.

---

## Issue 1 — Supply Trucks: Resupply Stance Bar + LC Refill

### Goal
Treat supply trucks' supply pool the same way other units' ammo pool is treated: empty trucks should have the same automation options as empty ammo units.

### Behavior

The standard 3-stance resupply bar applies, with truck-specific semantics:

| Stance | Truck behavior when empty |
|---|---|
| **Hold** | Sit still. No auto-action. |
| **Auto** | Find nearest friendly Logistics Center with `Supply > 0`, drive there, refill from LC's pool (pip-by-pip). If none exist or all empty, evacuate via Supply Route. |
| **Evacuate** *(default)* | Always evacuate via Supply Route (`RotateToEdge`) when empty. |

**Default = Evacuate** for trucks (overrides normal `Auto` default). Reasoning: zero-micro for new players, opt-in to LC logistics for advanced play.

**Alt-click Evacuate**: works automatically — the existing `DoNow` → `RotateToEdge` path is unit-agnostic, no change needed.

### LC ↔ Truck mechanics

**LC → Truck refill (new):**
- LC's existing `SupplyProvider` (3000 pool, 3c0 range, 25-tick rearm delay) drains into truck's `CargoSupply`.
- One supply unit per `RearmDelay` ticks. Pip-by-pip, mirrors how LC currently rearms vehicle ammo.
- Truck queues `MoveTo(LC, range)` then waits — passive transfer like any other rearming unit.
- When truck full or LC depleted, truck idles at LC (next dispatch is the player's call).

**Truck → LC delivery (already works, document only):**
- Loaded truck near LC → use existing **UnloadCargoSupply** to drop a `SUPPLYCACHE`.
- LC's existing `AbsorbsSupplyCache` (range 2c512) pulls it in.
- No new mechanic required.

### Implementation hooks

1. **Resupply bar visibility gate** — `ResupplyBehaviorSelectorLogic.UpdateStateIfNecessary()`: change
   ```cs
   a.TraitsImplementing<AmmoPool>().Any()
   ```
   to also accept `CargoSupply`.

2. **Empty-trigger dispatch** — In `CargoSupply.Tick()`, when `supplyCount` first transitions to 0, read `AutoTarget.ResupplyBehaviorValue` and branch:
   - `Hold` → no-op
   - `Auto` → seek nearest friendly LC with supply, queue move toward it; on arrival, sit in range. If no LC found, fall through to `Evacuate`.
   - `Evacuate` → `RotateToEdge`

3. **LC accepts supply trucks as refill targets** — `SupplyProvider.IsValidTarget` (or equivalent scan) needs to recognise `CargoSupply` units that have free capacity (`SupplyCount < MaxSupply`), in addition to `Rearmable` ammo units. When transferring to a truck: drain LC's pool, add to truck's CargoSupply via `AddSupply(1)` per tick cycle.

4. **Default stance override** — Either set `InitialResupplyBehavior: Evacuate` on TRUK's AutoTarget, or allow per-trait override. Both AI and player default to Evacuate for trucks.

5. **`AlsoSeeksLC` flag** — Only supply trucks (units with `CargoSupply`) seek LCs for self-refill. Other vehicles continue to seek SupplyProvider buildings/trucks for ammo. No cross-contamination.

### Open implementation details (decide in plan)
- How to find "nearest LC with supply" — actor scan filtered by `SupplyProvider` trait + `Supply > 0` + ally relationship.
- Movement target — adjacent cell of LC (use `RallyPoint` cell, or any cell within `Range`).
- What happens if LC is occupied by other refilling units — queue/wait, not blocked (LC handles one at a time but refilling is a passive radius effect, not docking).
- Whether truck should attempt to find a *new* LC if its target gets destroyed mid-trip.

### Out of scope (v1.1)
- LC capture/contestation interactions
- Supply trucks delivering to LC via movement order alone (no manual unload click)
- Truck-to-truck supply transfer

---

## Issue 2 — Stuck Soldiers in Rubble Garrison Building

### Symptom
After a garrison building takes enough damage to become "rubble" (1HP indestructible state with damaged sprite):
- Player cannot issue an evacuation/unload order to free the soldiers.
- Soldiers visually appear in **both port AND basement** at the same time.

### Goal
- Soldiers in a rubble building can always be evacuated by player order.
- Rubble offers minimal-but-nonzero protection (gameplay: reward for taking the building down, but don't auto-execute the soldiers).
- Fix the port+basement double-display bug.

### Design

**Rubble protection:**
- New `RubbleProtection` field on `GarrisonProtection`. Default ~`30` (vs current `BaseProtection: 95`, `CriticalProtection: 70`).
- Active condition: when building HP at 1 (clamped by `Indestructible`).
- Soldiers can stay inside but take ~70% of incoming damage (vs 5% at full health).

**Evacuation from rubble:**
- The existing `Unload` order path on GarrisonManager + Cargo must remain functional regardless of HP.
- Investigate what currently blocks it — likely a `RequiresCondition` somewhere (Cargo's command button visibility, AttackGarrisoned gating, or a damage-state condition that disables Unload).
- The Unload command button must remain visible/clickable on the building when at 1HP.
- Selecting the soldier(s) and issuing an Evacuate order must also work — they're either in shelter (Cargo) or deployed (port). Both paths exist in `IResolveOrder`.

**Port + basement double-display fix:**
- Investigate `WithGarrisonDecoration` — does it iterate `PortStates[i].DeployedSoldier` AND `cargo.Passengers` separately, possibly listing the same actor in both?
- Check whether at 1HP a port-deployed soldier gets recalled to shelter without clearing `PortStates[i].DeployedSoldier`, leaving stale data.
- Fix: ensure soldier is in exactly one of `{port, shelter}` at any given time, decoration reads accordingly.

### Implementation hooks

1. **`GarrisonProtection.RubbleProtection`** — new field; activate when `Health.HP <= 1` (or via a `rubble` condition granted by GarrisonManager).
2. **Audit `Unload` order accept path** — confirm it's not gated by damage states. If gated, ungate.
3. **Audit `WithGarrisonDecoration`** — make sure single soldier renders once.
4. **Audit recall-to-shelter flow** — at any HP threshold or suppression event, when a soldier moves from port → shelter, ensure `PortStates[i].DeployedSoldier = null` and the soldier is added to `cargo.Passengers` (or vice-versa) atomically.

### Out of scope
- Changing the rubble visual (already uses damaged sprite via `^DamageStates`).
- Changing how the building enters rubble (HP clamp logic).
- Garrison sidebar panel rewrite (Phase 4 — separate task).

---

## Acceptance criteria

**Issue 1:**
- Selecting a TRUK shows the resupply stance bar with all 3 buttons.
- TRUK defaults to Evacuate stance on spawn.
- TRUK on Evacuate, when supply hits 0, drives to map edge (RotateToEdge).
- TRUK on Auto, when supply hits 0, drives to nearest friendly LC with supply and refills there. Pips fill over time. When full, sits at LC.
- TRUK on Auto with no LC available falls through to Evacuate.
- TRUK on Hold sits still when empty.
- Alt-click Evacuate on a supply-loaded TRUK rotates it out immediately.
- Loaded TRUK can drop SUPPLYCACHE near LC; LC absorbs it as before.

**Issue 2:**
- Garrison building reduced to 1HP rubble: pressing Unload (or selecting and ejecting soldiers) successfully ejects all port + shelter soldiers.
- Damage taken inside a rubble building is roughly 70% of incoming (configurable via `RubbleProtection`).
- Each soldier appears in exactly one location (port OR shelter) — no double-display.

## Risks
- LC refill could clash with the existing vehicle-rearm flow if both share the same `SupplyProvider` cycle (LC currently grants one ammo pip per `RearmDelay` per nearby unit). May need to pick "biggest need" between empty trucks and ammo-poor vehicles.
- Default stance change for TRUK affects existing scenarios/saves (if any). Mitigation: stance state is not saved across games; first spawn after deploy gets new default.
- Unload audit may surface a deeper Cargo/condition issue we don't yet understand.

## Files likely touched
- `engine/OpenRA.Mods.Common/Traits/CargoSupply.cs`
- `engine/OpenRA.Mods.Common/Traits/SupplyProvider.cs`
- `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/ResupplyBehaviorSelectorLogic.cs`
- `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs` (if per-trait default override needed)
- `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs`
- `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonProtection.cs`
- `engine/OpenRA.Mods.Common/Traits/Garrison/WithGarrisonDecoration.cs`
- `mods/ww3mod/rules/ingame/vehicles-america.yaml`, `vehicles-russia.yaml` (TRUK default stance)
- `mods/ww3mod/rules/ingame/civilian.yaml`, `structures-defenses.yaml` (RubbleProtection)
