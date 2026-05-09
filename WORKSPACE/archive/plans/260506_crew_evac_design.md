# Crew & Passenger Evacuation Rework — Design Spec

**Status:** Brainstorm complete. Awaiting user review before plan writing.
**Date:** 2026-05-06
**Authors:** Opus 4.7 + @FreadyFish brainstorming session
**Supersedes:** `260506_crew_evac_brainstorm_handoff.md` (kept for history only)

---

## 1. Goal

Rework crew (drivers, gunners, commanders, pilots, copilots) and passenger evacuation so that:

- A finishing shot that's a high fraction of vehicle MaxHP → most occupants die inside, only rare survivors crawl out.
- A grinding death (small repeated hits) → most occupants escape, possibly with light wounds and smoke.
- Vehicles + helicopters visibly burn before death (existing 10-stack `onfire` system, applied to vehicles).
- Occupants inherit the vehicle's burning state on eject — they emerge "as cooked as the vehicle was."
- Helicopter emergency landings are dramatic but consistent with ground vehicles: every emergency landing ends in destruction; pilots can be rescued before the airframe explodes; the cockpit shields them while inside but not when caught in the explosion.
- Crew/passenger re-entry into vehicles is removed — burning vehicles are unrecoverable, occupants are pure rescue objects.
- Math is simple, testable, and YAML-tunable.

---

## 2. Settled decisions

| # | Decision |
|---|----------|
| D1 | Vehicle `onfire` stack scales linearly with HP%, in 5% bands below 50%. `stacks = clamp(0, 10, ceil((50 - hpPct) / 5))`. So 50%=0, 49%=1, 44%=2, …, 4%=10. |
| D2 | New trait `OnFireFromHealth` watches HP each tick and grants/revokes self `onfire` external condition stacks based on HP threshold bands. |
| D3 | Existing critical DOT (`ChangesHealth@CriticalDamage`) remains the actual burn damage on vehicles. The new `onfire` stack on vehicles is a state counter + visual driver, not a damage source. No new `BurnDamage_N` traits on vehicles. |
| D4 | Crew + passenger ejection unified through one resolver: `EvacResolver` static helper with two pure functions (damage roll + onfire inherit). Both `VehicleCrew` and `Cargo` call it. |
| D5 | Critical state triggers staged ejection for crew AND passengers. Wait-for-stop → post-stop delay → eject one-by-one with pacing variance. Today's death-only passenger path becomes the fallback. |
| D6 | Damage formula: `hpLoss = clamp(0, 2*occMaxHP, finishingFraction * occMaxHP * jitter)` where `finishingFraction = finishingDamage / vehicleMaxHP` and `jitter = uniform(0, 2)`. ±100% means lucky=0, unlucky=2× = dies inside. **Independent rolls per occupant** so the same vehicle yields a spread of outcomes. |
| D7 | Onfire inherit, two paths:<br>• **Staged path** (occupant exits cleanly while vehicle still alive): `inheritedStacks = clamp(0, 10, vehicleStacks * transferPct / 100)`.<br>• **Death path** (vehicle exploded with occupants still inside): `inheritedStacks = 10` always, regardless of `transferPct`. They're caught in the explosion. |
| D8 | Per-vehicle YAML knob: `CrewFireTransferPct` on `VehicleCrew`. Default 100 (tanks fully inherit). 0 for helicopters (cockpit protects on staged eject; explosion still engulfs). |
| D9 | Pacing — crew slow, passengers fast, both staggered from a hardened stop:<br>• `StoppedTicksRequired: 8` — 8 consecutive ticks of zero movement before counting as stopped.<br>• `PostStopDelay: 38 ± 13` — ~1.0–2.0s before first eject.<br>• Crew `EjectionDelay: 38 ± 13` — ~1.0–2.0s between ejects.<br>• Passenger `EjectionDelay: 12 ± 4` — ~0.32–0.64s between ejects (a Chinook of 8 disgorges in ~4–5s). |
| D10 | Visual: 5 burn tiers grouped from the 10 stacks, mirroring infantry's existing `WithIdleOverlay@Burn_1..5` pattern (`onfire == 1∥2`, `3∥4`, `5∥6`, `7∥8`, `9∥10`). First implementation reuses `infantry-burn-N` sprites; if scale/positioning looks off, dedicated `vehicle-burn-N` / `aircraft-burn-N` sprites are filed for sprite work. |
| D11 | Helicopter state machine: **Heavy = nothing** (just keeps flying with damage-state penalties). **Critical = controlled descent** (existing `HeliAutorotate` activity, kept as-is). After landing: sits and burns until destroyed via existing critical DOT; `VehicleCrew` and `Cargo` run their staged sequences naturally once the heli is stationary at <50% HP. |
| D12 | Mid-glide death rules:<br>• `SpinsOnCrash: false` (Chinook/HALO) → always falls, no spin, `rotor-destroyed` granted.<br>• `SpinsOnCrash: true` AND killing shot ≥ `RotorDestroyDamageThresholdPct` × MaxHP (default 50%) → falls, no spin, `rotor-destroyed` granted (sprite hidden).<br>• Otherwise → existing `HeliCrashLand` spinning crash. |
| D13 | Re-entry feature **removed entirely**. Delete `CrewMember.cs`, `EnterAsCrew.cs`, `AllowForeignCrew` plumbing on `VehicleCrew`, capture-by-pilot path on `HeliEmergencyLanding`, any cursor/UI plumbing. Crew/passengers are pure infantry post-eject. |
| D14 | Critical state is a one-way trip to destruction. **Engineers cannot save a burning vehicle.** Existing `Repairable*` traits get gated on `RequiresCondition: !critical-damage` (the existing condition from `GrantConditionOnDamageState`). The "repaired out of critical → cancel ejecting" branch in current `VehicleCrew.DamageStateChanged` is removed. |
| D15 | `EjectionSurvivalRate` removed. Replaced by ±100% damage variance — natural lethality without a coin flip. |
| D16 | Crew survivor selectability: lower `Selectable: Priority` than infantry + distinct `Selectable: Class: CrewSurvivor`. Box-select still grabs them; per-class filters separate them. **Passengers keep their original selection priority** — a regular rifleman who happened to be in a transport shouldn't suddenly be deprioritized like a technician. |
| D17 | Crew survivor combat: SMG + ~1/3 of regular SMG infantry's ammo (per handoff Q8b). |
| D18 | Crew survivor cashback: Driver / Gunner = 100, Commander = 200, Pilot = 300, Copilot = 200. |
| D19 | Crew survivor default stance: Resupply = Evacuate, Engagement = Defensive, Fire = FireAtWill. Walks toward map edge biased toward friendly Supply Route, refunds cashback on arrival via the existing rotate-to-edge path. |

---

## 3. Architecture

### Component diagram

```
┌──────────────────────────┐
│   EvacResolver (static)  │  ← pure-function math, no state
│   • RollDamage()         │
│   • InheritOnFireStacks()│
└─────┬────────────────┬───┘
      │                │
  ┌───▼──────┐   ┌─────▼─────┐
  │VehicleCrew│  │   Cargo   │  ← state machines, both call resolver
  │ (modified)│  │ (modified)│
  └─────┬─────┘  └────┬──────┘
        │             │
  ┌─────▼─────────────▼─────┐
  │  HeliEmergencyLanding   │  ← orchestrates heli paths
  │       (modified)        │
  └─────────────────────────┘

┌──────────────────────────┐
│    OnFireFromHealth      │  ← new trait: grants self onfire stacks
│         (new)            │     based on HP%
└──────────────────────────┘
```

### File changes

**New (engine):**
- `engine/OpenRA.Mods.Common/Traits/EvacResolver.cs` — static helper with two pure functions.
- `engine/OpenRA.Mods.Common/Traits/OnFireFromHealth.cs` — grants self `onfire` external condition stacks based on HP threshold bands.
- `engine/OpenRA.Test/Traits/EvacResolverTest.cs` — unit tests for the resolver.

**Modified (engine):**
- `engine/OpenRA.Mods.Common/Traits/VehicleCrew.cs`
  - Hardened stop detection (8-tick hysteresis).
  - Calls `EvacResolver` per crew member at eject time.
  - Reads vehicle's current onfire stacks from `OnFireFromHealth.CurrentStacks` (or the condition manager).
  - Drops `EjectionSurvivalRate`, `CrewDamageThresholdPercent`, `CrewDamageVarianceDivisor`.
  - Adds `CrewFireTransferPct` field, `StoppedTicksRequired` field.
  - Removes the "repaired out of critical → cancel ejecting" branch.
  - Death-path: instant-eject all remaining, calls resolver for damage roll, applies `onfire = 10` directly (resolver inherit bypassed).
  - Removes `AllowForeignCrew`, `EjectAllCrew()`, `FillSlot/ReserveSlot` (re-entry obsolete).
- `engine/OpenRA.Mods.Common/Traits/Cargo.cs`
  - Implements `INotifyDamageStateChanged` for new critical-state staged eject path.
  - Stop detection mirrors `VehicleCrew` (8-tick hysteresis, `PostStopDelay`, `EjectionDelay`).
  - Each passenger: call `EvacResolver.RollDamage()` and `EvacResolver.InheritOnFireStacks()`.
  - Existing `INotifyKilled.Killed` becomes the death-path: instant-eject remaining, damage roll via resolver, `onfire = 10`.
  - Drops the existing simpler damage formula (`damageToDeal = passengerMaxHP * vehicleDamage / vehicleMaxHP + small_random`) in favor of the resolver.
  - New YAML fields: `EjectOnCritical: True`, `EjectionDelay`, `EjectionDelayVariance`, `PostStopDelay`, `StopTimeout`, `StoppedTicksRequired`.
- `engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs`
  - Removes "heavy = autorotation safe-land" path (start at critical only).
  - Removes `OnSafeLanding`'s `EjectAllCrew()` call, `TransferToNeutralOnSafeLanding`, `AllowForeignCrew = true` plumbing. After landing, just zero velocity + grant `crash-disabled` condition. `VehicleCrew` and `Cargo` run their critical-state staged sequences naturally.
  - Removes `RepairableBuilding` activation on `crash-disabled` (no engineer-saving).
  - Adds mid-glide death detection in `INotifyKilled.Killed`:
    - If `SpinsOnCrash == false` OR killing shot ≥ `RotorDestroyDamageThresholdPct × MaxHP` → grant `rotor-destroyed`, queue `Falls` activity.
    - Else → queue `HeliCrashLand` (existing spinning crash).
  - New YAML fields: `RotorDestroyDamageThresholdPct: 50`, `RotorDestroyedCondition: rotor-destroyed`.

**Deleted (engine):**
- `engine/OpenRA.Mods.Common/Traits/CrewMember.cs` (re-entry IIssueOrder).
- `engine/OpenRA.Mods.Common/Activities/Air/EnterAsCrew.cs` (re-entry activity).
- Any cursor / order-string plumbing that referenced re-entry orders.

**Modified (YAML):**
- `mods/ww3mod/rules/ingame/defaults.yaml` — add `OnFireFromHealth`, `ExternalCondition@onfire`, `WithIdleOverlay@Burn_1..5` to `^Vehicle` and `^Aircraft` templates. Gate `Repairable*` on `!critical-damage` (or existing critical condition).
- `mods/ww3mod/rules/ingame/vehicles-america.yaml`, `vehicles-russia.yaml` — tune per-vehicle `VehicleCrew` knobs (drop obsolete fields, add `CrewFireTransferPct: 100`, `StoppedTicksRequired: 8`).
- `mods/ww3mod/rules/ingame/aircraft.yaml` (`^Helicopter` template) — `CrewFireTransferPct: 0`, removed re-entry plumbing, new `RotorDestroyDamageThresholdPct`, `RotorDestroyedCondition`.
- `mods/ww3mod/rules/ingame/aircraft-america.yaml`, `aircraft-russia.yaml` — same per-heli tuning. Confirm `SpinsOnCrash: false` on Chinook/HALO.
- `mods/ww3mod/rules/ingame/crew.yaml` — SMG weapon, ~1/3 ammo, role-based `Valued: Cost`, lower `Selectable: Priority`, `Selectable: Class: CrewSurvivor`. (Note: `^CrewMember` here is the *YAML actor template*, distinct from the deleted `CrewMember.cs` re-entry trait — same name, different concept.)
- `mods/ww3mod/rules/world.yaml` — optional `EvacRules` world trait if `CrewLethalityScale` is exposed (otherwise const in code).

---

## 4. The model

### Inputs

Locked at the moment of critical transition (or at killing-shot moment for death path):

| Input | Source | Notes |
|---|---|---|
| `finishingDamage` | `AttackInfo.Damage.Value` | The shot that pushed vehicle to critical (or killed it). |
| `vehicleMaxHP` | `IHealth.MaxHP` | |

Read fresh at each occupant's eject moment (staged path):

| Input | Source | Notes |
|---|---|---|
| `vehicleStacksAtEject` | `OnFireFromHealth.CurrentStacks` | Grows as HP drops via critical DOT. |

Constant per-vehicle:

| Input | Source | Notes |
|---|---|---|
| `transferPct` | `VehicleCrew.CrewFireTransferPct` | 100 = full inherit (tanks). 0 = cockpit protected (helis). |

### Per-occupant damage roll

```
finishingFraction = clamp(0, 2, finishingDamage / vehicleMaxHP)
jitter            = uniform(0, 2)              // independent per occupant
hpLoss            = finishingFraction * occupantMaxHP * jitter
hpLoss            = clamp(0, 2 * occupantMaxHP, hpLoss)

if hpLoss >= occupantMaxHP:
    deadInside = true   // resolver returns "no actor"
else:
    spawn occupant with HP = occupantMaxHP - hpLoss
```

**Why ±100% jitter?** A finishing shot that's 50% MaxHP gives expected `hpLoss = 50%` of crew MaxHP, but individual rolls range 0–100%. Most crew take half-damage; rare ones die inside; rare ones walk out clean. Independent rolls per occupant give the "one dead, one wounded, one fine" spread the design wants.

### Onfire inherit

```
// staged path (vehicle still alive)
inheritedStacks = clamp(0, 10, vehicleStacksAtEject * transferPct / 100)

// death path (vehicle exploded, occupant caught inside)
inheritedStacks = 10                         // override, transferPct ignored
```

For a tank with `transferPct: 100`: staged inherit ranges 0–10 depending on HP at eject; death-path is 10. Effectively the same model.
For a helicopter with `transferPct: 0`: staged inherit is always 0 (cockpit protected); death-path is 10 (explosion). This delivers the "engulfed pilot crawls out and dies outside" visual — the cockpit shielded them up to the moment of explosion.

### Worked examples

| # | Scenario | finishingFrac | vehStacks at eject | transferPct | Per-occupant outcome | Overall |
|---|---|---|---|---|---|---|
| 1 | Sabot one-shots Abrams (3 crew) | ~1.5 | death-path | 100 | hpLoss = 1.5 × 100 × U(0,2), median 300, ±300; almost all dead inside | 0–1 survivor, engulfed (onfire=10) |
| 2 | .50 cal grinds Humvee (3 crew) | ~0.05 | 0 → 7 → 10 (HP drops over ejects) | 100 | hpLoss median ~5, most ~0; stack inherit 0 → 7 → 10 | All 3 out, last one staggers out engulfed |
| 3 | ATGM critical-hits MBT (3 crew, 50% damage) | ~0.5 | 5 (at 25% HP after hit) | 100 | hpLoss median ~50 (50% crewMaxHP), spread 0–100; ~25% die inside, ~75% wounded | 2–3 survivors, light-to-medium burn |
| 4 | Helicopter critical hit by missile | ~0.7 | 7 (at ~15% HP) | 0 | Damage as above; **inherit = 0** (cockpit) | Pilots emerge wounded but clean (no fire) — cockpit shielded |
| 5 | Helicopter explodes mid-eject (HP=0 with 1 pilot still inside) | (whatever killed it) | 10 | 0 → 10 (death override) | Pilot ejects engulfed + heavily damaged | Pilot crawls out burning, dies seconds later |
| 6 | FAE on MBT | ~1.2 | death-path | 100 | hpLoss median 240, all dead inside | 0 survivors |
| 7 | Chinook killed mid-glide by rocket (50% MaxHP shot) | n/a | n/a | n/a | falls, no spin, rotor-destroyed | Everyone dies, dramatic plummet |
| 8 | Hind killed mid-glide by .50 cal trickle (10% MaxHP shot) | n/a | n/a | n/a | spins, dies on impact | Everyone dies, spinning crash |

---

## 5. Sequence specifications

### 5.1 — Critical-state staged ejection (vehicles AND transports)

```
1. INotifyDamageStateChanged: damageState >= Critical, previous < Critical
2. Capture finishingDamage = e.Damage.Value
3. ejecting = true; waitingForStop = true; stoppedTickCounter = 0
4. Each tick while waitingForStop:
   - if mobile is null OR mobile.CurrentMovementTypes == None:
       stoppedTickCounter++
   - else:
       stoppedTickCounter = 0
   - stopWaitCounter++
   - if stoppedTickCounter >= StoppedTicksRequired (8) OR stopWaitCounter >= StopTimeout (150):
       waitingForStop = false
       ejectionCountdown = PostStopDelay (38) + uniform(-13, +13)
5. Each tick while ejectionCountdown > 0: tick down
6. Eject one occupant:
   - read vehicleStacksAtEject from OnFireFromHealth
   - hpLoss = EvacResolver.RollDamage(finishingDamage, vehicleMaxHP, occupantMaxHP, random)
   - inheritedStacks = EvacResolver.InheritOnFireStacks(vehicleStacksAtEject, transferPct)  // staged path
   - if hpLoss >= occupantMaxHP: skip spawn (dead inside)
   - else:
       spawn occupant at vehicle's cell (or annulus 1..2 if blocked)
       apply hpLoss damage
       grant `onfire` external condition × inheritedStacks
       set Resupply=Evacuate, Engagement=Defensive, Fire=FireAtWill
7. ejectionCountdown = EjectionDelay + uniform(-Variance, +Variance)
8. Loop until all occupants ejected
```

**No "repaired out of critical → cancel" branch.** Once started, ejection completes (D14). Engineers can't save a burning vehicle.

### 5.2 — Death-path eject (vehicle dies before staged finishes)

```
1. INotifyKilled fires
2. Capture killingDamage = e.Damage.Value
3. For each remaining occupant (in original ejection order):
   - hpLoss = EvacResolver.RollDamage(killingDamage, vehicleMaxHP, occupantMaxHP, random)
   - inheritedStacks = 10 (death-path override; transferPct ignored)
   - if hpLoss >= occupantMaxHP: skip spawn
   - else: spawn + damage + grant onfire × 10 + default stances
4. All ejects in a single frame; no further staggering
```

### 5.3 — Helicopter controlled descent (replaces today's "heavy = safe land")

```
1. INotifyDamageStateChanged: damageState >= Critical, previous < Critical, !IsAtGroundLevel
2. State = Crashing
3. Grant crash-landing condition
4. Set VehicleCrew.SuppressEjection = true; set Cargo.SuppressEjection = true (new flag)
   (so the staged eject doesn't fire mid-descent)
5. CancelActivity(); QueueActivity(HeliAutorotate)   // existing controlled-descent activity
6. During descent: player can right-click to steer (existing IResolveOrder Move-to-facing)
7. On touchdown:
   - if terrain suitable:
       zero velocity
       grant crash-disabled condition
       revoke SuppressEjection on VehicleCrew + Cargo
       (heli now stationary at <50% HP → VehicleCrew + Cargo critical-state machines fire)
   - if terrain unsuitable:
       apply unsafe-landing damage (TBD — see Open Items §8)
       then proceed as suitable
8. Heli sits on ground. Existing critical DOT ticks HP toward 0.
9. Crew/passengers eject staggered per §5.1.
10. If HP hits 0 before all ejected → death-path §5.2 runs.
```

### 5.4 — Helicopter mid-glide death

```
INotifyKilled while State == Crashing (still airborne):
- killingDamage = e.Damage.Value
- if SpinsOnCrash == false
     OR killingDamage >= MaxHP * RotorDestroyDamageThresholdPct / 100:
     grant rotor-destroyed condition (hides rotor sprite via WithIdleOverlay@Rotor: !rotor-destroyed)
     queue Falls activity (existing)
- else:
     queue HeliCrashLand activity (existing spinning crash)
- All occupants die: SuppressEjection is still active from descent, so no eject path runs.
  (This matches today's behavior — mid-air death = nobody gets out.)
```

---

## 6. YAML

### `^Vehicle` / `^Aircraft` template (defaults.yaml)

```yaml
^Vehicle:
    OnFireFromHealth:
        Condition: onfire
        StartHealthPct: 50
        BandSize: 5
        MaxStacks: 10
    ExternalCondition@onfire:
        Condition: onfire
        TotalCap: 10
    WithIdleOverlay@Burn_1:
        RequiresCondition: onfire == 1 || onfire == 2
        Image: vehicle-burn-1     # try infantry-burn-1 reuse first; if scale is wrong, swap to dedicated
        Sequence: loop
        Palette: effect
    WithIdleOverlay@Burn_2:
        RequiresCondition: onfire == 3 || onfire == 4
        Image: vehicle-burn-2
        ...
    WithIdleOverlay@Burn_5:
        RequiresCondition: onfire == 9 || onfire == 10
        Image: vehicle-burn-5
        ...

    # Gate engineer repair below 50% HP — burning vehicles are unrecoverable.
    # Reuses the existing `critical-damage` condition from GrantConditionOnDamageState.
    Repairable:
        RequiresCondition: !critical-damage
    RepairableNear:
        RequiresCondition: !critical-damage
```

(`^Aircraft` follows the same pattern.)

### `^Tank` (tank actors)

```yaml
^Tank:
    VehicleCrew:
        CrewSlots: Driver, Gunner, Commander
        CrewActors:
            Driver: crew.driver.<faction>
            Gunner: crew.gunner.<faction>
            Commander: crew.commander.<faction>
        EjectionDelay: 38
        EjectionDelayVariance: 13
        PostStopDelay: 38
        StopTimeout: 150
        StoppedTicksRequired: 8       # NEW
        EjectionDamageState: Critical
        TransferVeterancy: True
        CrewFireTransferPct: 100      # NEW
        # REMOVED: EjectionSurvivalRate, CrewDamageThresholdPercent, CrewDamageVarianceDivisor
```

### `^Helicopter`

```yaml
^Helicopter:
    VehicleCrew:
        CrewSlots: Pilot, Copilot                    # or Pilot, Gunner per heli
        CrewActors:
            Pilot: crew.pilot.<faction>
            Copilot: crew.copilot.<faction>
        EjectionDelay: 38
        EjectionDelayVariance: 13
        PostStopDelay: 38
        StoppedTicksRequired: 8
        CrewFireTransferPct: 0                       # NEW — cockpit protects on staged
        # REMOVED: EjectionSurvivalRate, AllowForeignCrew plumbing

    HeliEmergencyLanding:
        AutorotationDescentRate: 20
        AutorotationSpeedPercent: 60
        AutorotationAcceleration: 3
        FlareAltitude: 512
        FlareDescentPercent: 30
        FlareSpeedPercent: 60
        SpinsOnCrash: True                           # false for Chinook/HALO
        MaxSpinRate: 80
        SpinAcceleration: 4
        AutorotationCondition: autorotation
        CrashLandingCondition: crash-landing
        DisabledCondition: crash-disabled
        SuppressEjectCondition: suppress-eject
        RotorStoppedCondition: rotor-stopped
        RotorDestroyDamageThresholdPct: 50           # NEW
        RotorDestroyedCondition: rotor-destroyed     # NEW
        RotorWindDownTicks: 60
        SuitableLandingTerrains: ...
        EjectPassengersOnSafeLanding: False          # CHANGED — Cargo handles its own eject now
        EjectPassengersOnCrash: False                # CHANGED — same
        CrashExplosion: UnitExplode
        CrashDamageState: Critical
        # REMOVED: AutorotationDamageState (heavy path is gone)
        # REMOVED: TransferToNeutralOnSafeLanding
```

### `^Transport` (transports with passenger capacity)

```yaml
^Transport:
    Cargo:
        EjectOnDeath: True            # already present, kept (death-path)
        EjectOnCritical: True         # NEW — opt-in critical-state staged eject
        EjectionDelay: 12             # NEW — ~0.4s base for passengers
        EjectionDelayVariance: 4      # NEW — ~0.16s
        PostStopDelay: 38             # NEW — same as crew
        StopTimeout: 150              # NEW
        StoppedTicksRequired: 8       # NEW
```

### `^CrewMember` (YAML actor template — distinct from the deleted CrewMember.cs trait)

```yaml
^CrewMember:
    Tooltip:
        Name: Crew Survivor
    Selectable:
        Priority: 5                   # lower than infantry (default 10)
        Class: CrewSurvivor           # distinct class for per-class filter
    Valued:
        Cost: 100                     # baseline (Driver/Gunner/Copilot)
    Armament@1:
        Weapon: SMG                   # was Pistol
    AmmoPool@1:
        Ammo: 10                      # ~1/3 of regular SMG infantry's ammo
    # default stances applied via UnitDefaultsManager / spawn-time defaults:
    #   Resupply=Evacuate, Engagement=Defensive, Fire=FireAtWill
```

### Per-role overrides

```yaml
crew.driver.america:        Inherits: ^CrewMember           # Cost: 100
crew.gunner.america:        Inherits: ^CrewMember           # Cost: 100
crew.commander.america:     Inherits: ^CrewMember
                            Valued: { Cost: 200 }
crew.pilot.america:         Inherits: ^CrewMember
                            Valued: { Cost: 300 }
crew.copilot.america:       Inherits: ^CrewMember
                            Valued: { Cost: 200 }
# (Russia faction variants follow the same pattern)
```

### `EvacRules` world trait — **skipped for v1**

`CrewLethalityScale` lives as a const in `EvacResolver` for v1 (default 100). Promote to a world trait only if playtest tuning needs frequent changes.

---

## 7. Tests

### Unit tests — `EvacResolverTest.cs`

| # | Test | Inputs | Expected |
|---|---|---|---|
| 1 | Sabot one-shot tank crew | finishingDmg=2000, maxHP=1000, occMaxHP=100, jitter=1.0 | hpLoss=200; clamped at occMaxHP → dead inside (returns null) |
| 2 | Small-arms grind, lucky roll | finishingDmg=50, maxHP=1000, occMaxHP=100, jitter=0.0 | hpLoss=0; survives clean |
| 3 | Critical-but-alive, median roll | finishingDmg=500, maxHP=1000, occMaxHP=100, jitter=1.0 | hpLoss=50 |
| 4 | Critical-but-alive, unlucky roll | finishingDmg=500, maxHP=1000, occMaxHP=100, jitter=2.0 | hpLoss=100; dead inside |
| 5 | Death-path inherit | transferPct=0, vehicleStacks=arbitrary, isDeathPath=true | inheritedStacks=10 |
| 6 | Death-path inherit (tank) | transferPct=100, isDeathPath=true | inheritedStacks=10 |
| 7 | Staged-path inherit (tank) | transferPct=100, vehicleStacks=7 | inheritedStacks=7 |
| 8 | Staged-path inherit (heli) | transferPct=0, vehicleStacks=7 | inheritedStacks=0 |
| 9 | Staged-path inherit (clamped) | transferPct=100, vehicleStacks=12 | inheritedStacks=10 |
| 10 | Onfire stack from HP | hpPct=49, threshold=50, band=5 | stacks=1 |
| 11 | Onfire stack from HP | hpPct=4, threshold=50, band=5 | stacks=10 |
| 12 | Onfire stack from HP | hpPct=51, threshold=50, band=5 | stacks=0 |
| 13 | finishingFraction clamp | finishingDmg=5000, maxHP=1000 | finishingFraction=2 (clamped) |
| 14 | Variance distribution | 1000 samples, finishingFrac=0.5, occMaxHP=100 | mean ~50, range 0–100, ~uniform |

### Integration playtest checks

- Tank one-shot by ATGM: 0–1 crew survive, 0–1 are engulfed (onfire=10, dies seconds later from infantry BurnDamage_N).
- Tank ground down by small-arms: 2–3 crew survive, mostly clean or low burn (1–4).
- IFV at 50% HP after a hit: starts disgorging passengers staggered ~0.4s apart while still alive; existing IFV either explodes shortly after (death-path catches stragglers) or sits damaged (passengers all out).
- Helicopter critical descent on safe terrain: lands, sits, pilots eject one-by-one over 5–10s as HP keeps draining; no onfire on emerged pilots (cockpit protected).
- Helicopter critical descent then explodes mid-eject: remaining pilots emerge engulfed (onfire=10) + heavily damaged, die seconds later from BurnDamage.
- Helicopter shot mid-glide by big missile (≥50% MaxHP): falls without spinning, rotor sprite hidden, all dead.
- Helicopter shot mid-glide by smaller round: spins down, dies on impact.
- Chinook killed mid-glide: always falls (SpinsOnCrash: false).
- Crew survivor box-selected with mixed army: still selected, but per-class filter keyboard-shortcut separates them.
- Crew survivor on default stances: walks toward map edge biased toward friendly SR; refunds cashback (100/200/300/200) on arrival.
- Critically-damaged tank cannot be repaired by engineer (Repairable gated on `!critical-damage`).
- No re-entry: clicking on a critical/disabled vehicle with crew survivor selected shows no enter cursor; ordering "enter" via legacy hotkey is rejected.
- Vehicle burn overlay visible at appropriate stack tiers (1–2: light smoke, 9–10: engulfed).

---

## 8. Open items (resolved at review; remaining items checked during implementation)

**Resolved at review (2026-05-07):**

- **Copilot cashback** = 200 (locked).
- **Helicopter unsafe-terrain landing damage** = 30% of remaining HP on touchdown (placeholder; revisit during playtest).
- **`EvacRules` world trait skipped for v1.** `CrewLethalityScale` lives as a const in `EvacResolver`. Promote to YAML world trait only if tuning needs it.
- **Critical-state condition** — reuse existing `critical-damage` (granted by `GrantConditionOnDamageState`). Don't add a new `is-burning`.

**Verify during implementation:**

1. **Vehicle-burn sprites.** First pass tries reusing `infantry-burn-1..5`. If scale/positioning is wrong on tanks/helis, file a sprite-work follow-up under `BACKLOG.md`.
2. **`Cargo.LoadingBlocked` plumbing.** Today `HeliEmergencyLanding.OnSafeLanding` sets `cargo.LoadingBlocked = true` to prevent infantry from entering the disabled heli. With re-entry removed and capture path gone, this may be obsolete — confirm and clean up.
3. **`AllowForeignCrew` on `VehicleCrew`.** All references being removed (capture-by-pilot is gone). Check no other code paths depend on it.
4. **Helicopter `EjectAllCrew()` + `EjectAllPassengers()` methods.** Becomes dead code with the new model (crew/passengers handled via critical-state staged path). Delete or repurpose.

---

## 9. Future / V1.1+

- **Rotor shrapnel animation.** When killing shot ≥ `RotorDestroyDamageThresholdPct`, render rotor blades flying off in random directions (not just hidden). Pure visual polish.
- **Custom HP→stack curve (option C from Q1).** Replace 5%-band linear with a front-loaded curve so most of the burn-up happens late (e.g., stack 8/9/10 only at <10% HP). Tunable per-vehicle if some types should feel slow-burn vs fast-cookoff.
- **Vehicle-specific burn sprites.** Author dedicated `vehicle-burn-N` and `aircraft-burn-N` sprite sets if reused infantry sprites look wrong.
- **Medic extinguish.** Medic targeting an onfire crew member could remove a stack per heal pulse. Out of scope v1.
- **Stop-and-roll.** Onfire infantry could play a "stop, drop, and roll" animation that drops a stack. Out of scope v1.
- **Water-tile auto-extinguish.** Onfire infantry stepping on water tile clears stacks. Out of scope v1.
- **EVA "crew rescued" voice line.** When a crew survivor reaches the map edge, play a notification.

---

## 10. Implementation sequencing (informational — actual plan written separately)

This spec decomposes cleanly into independently-testable phases. The implementation plan (next step, via `superpowers:writing-plans`) should sequence roughly:

- **Phase 1**: `EvacResolver` + tests (no callers yet — pure scaffolding).
- **Phase 2**: `OnFireFromHealth` trait. Wire into `^Vehicle` / `^Aircraft` defaults. Verify in-game that vehicle onfire stacks track HP correctly (no behavioral change yet because crew code doesn't read stacks yet).
- **Phase 3**: Refactor `VehicleCrew.cs` to call resolver. Drop obsolete fields. Hardened stop detection. Remove "repaired out of critical → cancel" branch.
- **Phase 4**: Add critical-state staged eject path to `Cargo.cs`. Same model as `VehicleCrew`.
- **Phase 5**: Update `HeliEmergencyLanding.cs`: remove heavy path, simplify safe-landing, add mid-glide death rules. Drop neutral-transfer + capture plumbing.
- **Phase 6**: YAML pass — `OnFireFromHealth` + burn overlays on all vehicles + helicopters; per-vehicle `CrewFireTransferPct`; crew weapon/cost/selection; gate `Repairable*`.
- **Phase 7**: Delete `CrewMember.cs` + `EnterAsCrew.cs` + UI/order plumbing for re-entry. Clean up `AllowForeignCrew`, `LoadingBlocked`, `EjectAllCrew`, etc.
- **Phase 8**: Playtest + tune `CrewLethalityScale` (if exposed), pacing values, transfer percentages, sprite tier thresholds.

Phase 3 is the riskiest (load-bearing existing system). Phase 1 + 2 must land first as scaffolding. Phase 7 cleanup happens last — easier to delete with confidence once the new path is proven.
