# Crew Evacuation Rework — Brainstorm Handoff

**Status:** Brainstorm in progress, paused. Restart in a fresh session.
**Started:** 2026-05-06 (Opus 4.7)

## To the next agent

You are picking up a brainstorming session about reworking how crew (drivers, gunners, commanders, pilots, copilots) evacuate damaged ground vehicles and aircraft in WW3MOD. The previous session reached a coherent design but the user wanted a checkpoint document before continuing — this captures the agreed model and the remaining work.

**How to use this:**

1. Read it fully before asking the user anything.
2. Re-enter the `superpowers:brainstorming` skill.
3. Treat the **Settled decisions** section as locked unless the user reopens specific items.
4. The model is finalized at the conceptual level (Section "The Model" below). What's left is mostly tuning numbers, sequencing of implementation work, and converting it to a formal spec.
5. The user is technically engaged, prefers concrete worked examples, and explicitly asked for the system to be **simple, not over-engineered**. Resist any urge to add new traits, fields, or paths beyond what's listed here.

---

## 1. The user's vision

### What's wrong today

- Way too many crew/pilots emerge from destroyed vehicles. The battlefield ends up with "whole armies of crew/pilots just wandering around."
- Outcomes don't reflect kill realism — a tank one-shotted by a sabot ejects crew the same as a tank ground down by small arms.
- No fire mechanic — the "burning tank, crew barely escaping before it explodes" moment doesn't exist.
- Crew that emerges gets accidentally selected with the rest of the player's army (mixed selections), creating annoying micro.
- Crew sometimes ejects while the vehicle is still moving — should wait until the wreck has stopped.

### Gameplay role of crew

- **Side-mission rescue objects.** A surviving crew member is a small reward — bring them home, get cashback. They are not a meaningful combat unit.
- Limited combat: SMG with ~1/3 the ammo of a regular soldier. Otherwise as effective as any infantryman with an SMG (which is already weaker than rifle infantry).
- Cashback values:
  - Driver / Gunner = 100
  - Commander = 200
  - Pilot = 300
  - Copilot = 200
  - Vehicle-agnostic (a tank Commander = an IFV Commander = 200)
- Default behavior: auto-evacuate via the existing `Evacuate` resupply stance, walk to the map edge biased toward a friendly Supply Route. Player can override with explicit orders.

---

## 2. Settled decisions

These are locked. Do not relitigate without the user reopening.

| # | Decision | Rationale |
|---|----------|-----------|
| Q1 | Extend the existing `VehicleCrew` system; redesign only if structurally needed | User open to redesign but baseline is "extend." Architecture decision below makes this concrete. |
| Q2 | Live/die for crew is driven by **finishing-shot damage / vehicleMaxHp** | Simple, captures one-shot-kill = crew dies and grind = crew survives |
| Q3 | **Drop incendiary weapon flag.** Fire is *not* tied to weapon type. | User explicitly walked back the incendiary flag |
| Q4 | Fire and live/die are **independent per crew member**. Crew can be dead inside, alive on fire, alive clean. | Replaces earlier "fire is a separate track that skips live/die" |
| Q5 | Survival distribution target: **A-baseline harsh** (~60 dead inside / 15 fire-eject / 25 clean per 100 crew) | User wants "much fewer crew" |
| Q6 | Helicopters: keep `HeliEmergencyLanding` heavy=safe-land (guaranteed eject) and critical=crash; *but* in the critical-crash path apply the new resolver so a rare survivor can crawl from the wreckage on fire | Hybrid (Q6-C). Most crashes still kill all hands. |
| Q7 | Fire mechanic uses the **existing `onfire` stackable condition (cap 10)** with `ChangesHealth@BurnDamage_N` traits and `FireDeath` damage type | Already implemented, no new logic needed |
| Q8a | Cashback per role: D/G/Copilot=100, Commander=200, Pilot=300 | Vehicle-agnostic |
| Q8b | All crew weapon: SMG with ~1/3 of a regular infantryman's ammo | No AT, no secondaries |
| Q8c | Combat behavior: as good as any other SMG soldier (no extra nerf). They're naturally weaker due to limited ammo. | Self-defense soldier |
| Q9 | Default behavior: auto-evac via existing `Evacuate` resupply stance + Defensive engagement + FireAtWill | Reuse existing systems |
| Q9a | If SR captured/destroyed mid-evac: **don't reroute**. Map-edge target stays valid because Evacuate goes to map edge, not the SR. | Existing behavior is fine |
| Q9b | Pathing through enemies: accept it. Rescue is risky. Out of scope for danger-aware pathfinding. | |
| Q9c | Selection deprioritization: **soft via priority + class** (like technicians today). Box-select still includes them but per-class filtering separates them. No engine change. | |
| Q9d | No special "evacing" icon decoration. The move-order line is enough. | |
| Q10 | AI uses the same code path; no special handling. | |
| Arch | Pull the eject-decision logic into a new pure-function helper: **`CrewEjectResolver.Resolve(...)`**. `VehicleCrew` keeps its state machine and applies the outcome. | Approach B — testable in isolation; keeps `VehicleCrew` from ballooning |
| Realism | **Final realism model: drop incendiary flag entirely.** Just use finishing-shot damage relative to max HP, plus the existing critical-state passive DOT to simulate the burning vehicle. | User's final correction; replaces earlier two-path cookoff/slow-burn model |
| Stop | Crew waits for vehicle to come to a complete stop, then a short post-stop delay, then ejects one by one. **Harden stop detection** — current logic ejects too early because movement-state flickers. Require N consecutive stopped ticks. | User-reported bug fix |
| HP | Per-member damage uses **HIGH variance** — one crewman can die while others walk away clean | "Realistic spread" |
| Fire | Per-member fire stack uses **LOW variance (±2..3)** — same vehicle is burning, but small per-member differences | |
| DOT | Late-ejecting crew get **higher fire stacks** because the existing critical-DOT (`ChangesHealth@CriticalDamage`) drains vehicle HP between ejections — `(1 - remainingHpFraction)` boosts the fire base level | The "vehicle is burning while you're inside" simulation falls out of existing systems for free |

---

## 3. The Model

### Inputs

Locked in at the moment of critical transition (except where noted):

| Input | Source | Notes |
|---|---|---|
| `finishingDamage` | `AttackInfo.Damage.Value` from the shot that pushed vehicle into critical | |
| `vehicleMaxHp` | `IHealth.MaxHP` | |
| `vehicleHpAtThisEject` | re-read at the moment **each individual crew member** ejects | Changes between members because of passive DOT |
| `globalLethalityScale` | new world-trait field | Default 1.0; single global tuning knob |

No incendiary flag. No per-weapon lethality. No per-vehicle cookoff field.

### Constants (computed once at critical)

```
finishingFraction = clamp(0, 2, finishingDamage / vehicleMaxHp)
```

### Sequence at critical state

1. Vehicle hits critical → mark `ejecting`, lock in `finishingDamage`.
2. **Wait for stop, hardened.** Require N consecutive ticks (e.g. 8) of `mobile.CurrentMovementTypes == None` before counting as stopped. Today it triggers on a single tick which can be a transient zero during pathfinding.
3. After stop: wait `PostStopDelay` (~1.6s).
4. Eject crew one at a time (existing schedule). Between ejects, the existing critical-DOT (`ChangesHealth@CriticalDamage: PercentageStep: -1, Delay: 5, StartIfBelow: 50`) ticks the vehicle's HP down.
5. **At each member's eject moment**, `CrewEjectResolver.ResolveOne(...)` runs with the *current* `vehicleHpAtThisEject`.
6. If the vehicle's HP hits 0 mid-sequence, the `INotifyKilled` path takes over and runs the resolver for remaining members with `remainingFraction = 0`. **The two paths converge on the same resolver** — no separate logic for "death-event eject" vs "critical-state eject."

### Per-member resolution

For each crew member, independently:

```
remainingFraction = clamp(0, 1, vehicleHpAtThisEject / vehicleMaxHp)

# 1) Damage — HIGH variance (one might die while others walk away)
hpLossPercent = clamp(0, 100, finishingFraction * hpDamageK * 100 + jitter HIGH (±50%))

if hpLossPercent >= 100:
    deadInside = true   # no actor spawned
else:
    startHp = crewMaxHp * (1 - hpLossPercent/100)

# 2) Fire stack — LOW variance per member, DOT-progress shared
fireBase  = finishingFraction * fireFromHitK + (1 - remainingFraction) * fireFromDotK
fireStack = clamp(0, 10, fireBase + jitter LOW (±2..3))

if fireStack <= 0:
    no fire (clean eject with rolled HP)
else:
    apply onfire condition × fireStack times
```

### Why the variance shapes work

- **HIGH damage variance** → damage isn't shared evenly, gives the "crew of three: one dead, one wounded, one fine" texture
- **LOW fire variance** → all surviving crew share roughly the same fire intensity (it's the *same* burning vehicle), with small per-member spread
- **DOT progress** → later-ejecting crew systematically more burned, because they were in the burning hulk longer

### Outcome shape

```csharp
struct CrewEjectOutcome {
  PerMemberOutcome[] members;   // one per crew slot
}

struct PerMemberOutcome {
  bool deadInside;              // no actor spawned
  int  startHp;                 // if alive: starting HP after wounds
  int  onFireStacks;            // 0..10; only nonzero if fire rolled in
}
```

---

## 4. Examples / Scenarios table

Numbers below assume starting tuning constants `hpDamageK ≈ 0.6`, `fireFromHitK ≈ 6`, `fireFromDotK ≈ 3`, `globalLethalityScale = 1.0`. Adjust at will — the table shows the *shape* of outcomes, not exact rolls.

| # | Scenario | finishingFraction | remainingFraction | hpLoss (median, ±jitter) | fire base | Per-member outcome | Overall |
|---|---|---|---|---|---|---|---|
| 1 | T-90 sabot front-hits Abrams (vehicle dies instantly) | ~1.5 | 0 (death-path) | 90% ±50 | 9 + 3 = 12 → clamp 10 | Most members 100%+ → dead inside; rare survivor at level 9–10 fire | Usually all 3 dead inside; occasional engulfed survivor |
| 2 | .50 cal grinds a Humvee to death | ~0.05 | 0.5 → 0.3 → 0.1 over ejects | 3% ±50 (clamped 0..max) | 0.3 + (1.5..2.7) = 1.8..3.0 | Mostly clean with light smoke (fire 1–4) | All 3 out, last one limping with smoke |
| 3 | Flamethrower bakes a Humvee until critical | ~0.1 | 0.5 → 0.3 → 0.1 | 6% ±50 | 0.6 + (1.5..2.7) = 2.1..3.3 | Clean to mild fire, last one most burned | All 3 out, progressive fire on later ejects |
| 4 | ATGM one-shots a Humvee | ~1.5 | 0 (death-path) | 90% ±50 | 9 + 3 = 12 → clamp 10 | Most dead inside; rare survivor engulfed | Usually all 3 dead, rare engulfed survivor |
| 5 | Small-arms grind on an MBT (silly but valid) | ~0.05 | 0.5 → 0.3 → 0.1 | 3% ±50 | 0.3 + (1.5..2.7) = 1.8..3.0 | Most survive with light smoke | Tank crew bails clean, last one smokes |
| 6 | FAE / thermobaric on MBT | ~1.2 | 0 (death-path) | 72% ±50 | 7.2 + 3 = 10.2 → clamp 10 | Most dead, rare engulfed survivor | Catastrophic |
| 7 | Helicopter takes heavy damage and lands safely (HeliEmergencyLanding) | n/a | n/a | (bypass resolver) | (bypass) | All crew + passengers eject guaranteed-survival, neutral ownership, capturable | Existing behavior — unchanged |
| 8 | Helicopter critical crash (uncontrolled) | finishing shot | 0 | resolver runs, like Example 1 | resolver runs | Most crew dead in crash; rare engulfed survivor crawls from wreckage | Replaces current 100% kill rate with the resolver curve |
| 9 | Tank wounded to ~10%, then sabot finisher | ~1.5 (sabot still huge vs maxHp) | very low | same as Example 1 | same as Example 1 | Catastrophic ending | Wounded tank + finishing sabot still cooks off |

---

## 5. Knobs / Tunable Settings

Three new fields, all in a single new `CrewEvacRules` world trait. Per-vehicle YAML stays clean — no per-vehicle bias needed (the natural HP differences between MBT / IFV / Humvee handle class-dependence implicitly).

### World trait (new)

```yaml
World:
    CrewEvacRules:
        # Single global lethality scalar. 1.0 = A-baseline harsh.
        # Lower (0.5) = more crew survive. Higher (1.5) = even harsher.
        CrewLethalityScale: 100        # percent, default 100 = 1.0×

        # Tuning constants — exposed for fine adjustment but rarely changed
        HpDamageK: 60                  # finishingFraction * 0.60 = expected hpLoss%
        FireFromHitK: 6                # finishingFraction contribution to fire stack base
        FireFromDotK: 3                # (1 - remainingFraction) contribution to fire stack base
        HpDamageJitterPct: 50          # ± high variance on per-member damage
        FireStackJitter: 2             # ± low variance on per-member fire stack

        # Stop-detection hardening
        StoppedTicksRequired: 8        # consecutive ticks of zero movement before counting as stopped

        # Crash / death-path inputs
        CrashFinishingFractionFloor: 1.0   # min finishingFraction used for vehicles that die before crew eject (treat as catastrophic)
```

### Per-vehicle (existing fields, retuned)

```yaml
^Tank:
    VehicleCrew:
        # Existing fields stay — these still drive the slot model
        CrewSlots: Driver, Gunner, Commander
        CrewActors:
            Driver: crew.driver.<faction>
            Gunner: crew.gunner.<faction>
            Commander: crew.commander.<faction>
        EjectionDelay: 25
        EjectionDelayVariance: 10
        PostStopDelay: 40
        StopTimeout: 150
        EjectionDamageState: Critical
        TransferVeterancy: True

        # OBSOLETE under new model — to be removed
        # EjectionSurvivalRate: 90               (replaced by resolver)
        # CrewDamageThresholdPercent: 25         (replaced by resolver)
        # CrewDamageVarianceDivisor: 5           (replaced by resolver)
```

### Per-crew-actor (existing, just retuned)

```yaml
^CrewMember:
    Valued:
        Cost: 100                         # D/G/Copilot
    Armament@1:
        Weapon: SMG                       # was Pistol
    AmmoPool@1:
        Ammo: 10                          # ~1/3 of a regular SMG infantry's ammo

# Per-role overrides:
# crew.commander.* → Cost: 200
# crew.pilot.*     → Cost: 300
# crew.copilot.*   → Cost: 200            (per Q9 follow-up)
```

> User said in Q8 that Commander = 200 and Pilot = 300. Pilot/Copilot = 300/200 was confirmed in a follow-up. **Note for next agent:** the user said "Copilot 200" but D/G are 100. Confirm with user whether Copilot should match Pilot's seniority (200) or match the rank-and-file (100). The handoff currently records it as 200 per the latest user statement.

---

## 6. Behavior — auto-evac default

On spawn, every crew member:

1. Has `Evacuate` resupply stance set by default
2. Has `Defensive` engagement stance set by default
3. Has `FireAtWill` fire stance set by default
4. Walks toward map edge (existing rotate-to-edge behavior, biased toward the friendly SR's spawn area)
5. Cashback paid when they reach the edge (existing behavior)

Deprioritized in selection via:
- Lower `Selectable: Priority` than regular infantry
- Distinct `Selectable: Class` (e.g. `CrewSurvivor`) so per-class selection separates them
- *No engine changes* — same model used by Technicians today

If player issues an explicit order, the auto-evac is overridden. Player can re-trigger by setting Resupply stance back to Evacuate.

---

## 7. Helicopter integration

Mostly unchanged. `HeliEmergencyLanding` keeps its two paths:

- **Heavy damage (controlled autorotation, safe land):** existing behavior. `EjectAllCrew()` with guaranteed survival. Helicopter goes Neutral, becomes RepairableBuilding, capture-by-pilot via `AllowForeignCrew`. **No resolver involvement.**
- **Critical damage (uncontrolled crash):** today this sets `SuppressEjection = true` and everyone dies. **New behavior: run the resolver with the crash's finishing shot as input and `remainingFraction = 0`.** Most crew still die (same outcome as today's binary), but rare survivors crawl from the wreckage on fire. Drops the `SuppressEjection` shortcut.

The "rare burning survivor crawls from wreck" path is purely a Q6-C mood enhancer — most crashes still kill all hands.

---

## 8. Known issues / things to double-check during implementation

- **Stop detection is currently buggy.** User reports crew ejecting while vehicle still moving. Current logic in `VehicleCrew.Tick` checks `mobile.CurrentMovementTypes` for a single tick. Movement state can flicker during pathfinding; need consecutive-ticks hysteresis (`StoppedTicksRequired` knob).
- **`EjectAllCrew()` (used by HeliEmergencyLanding safe-land) bypasses the resolver entirely.** Confirm this is fine — safe landings should be guaranteed-survival regardless of damage history.
- **`INotifyKilled` path needs to converge with critical-state path** through the resolver. Current code has two separate code paths (`Tick` for staged, `Killed` for instant-eject-all). Refactor so both call into the resolver.
- **EjectOnHusk is fully commented out** in `engine/OpenRA.Mods.Common/Traits/EjectOnHusk.cs` — leave it commented; not used.
- **EjectOnDeath was removed from helicopter husks** in commit 2026-04-04. Don't reintroduce it.

---

## 9. Files involved

### Engine (C#)
- `engine/OpenRA.Mods.Common/Traits/VehicleCrew.cs` — primary file. Resolver call sites + state machine + stop-detection hardening.
- `engine/OpenRA.Mods.Common/Traits/CrewMember.cs` — re-entry trait, unchanged but verify auto-evac doesn't break re-entry orders.
- `engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs` — drop `SuppressEjection` shortcut on critical crash; route to resolver instead.
- **NEW:** `engine/OpenRA.Mods.Common/Traits/CrewEjectResolver.cs` (or as a `World` trait paired with a static helper) — pure-function resolver.
- **NEW:** `engine/OpenRA.Mods.Common/Traits/CrewEvacRules.cs` — world trait holding the tuning knobs.

### YAML
- `mods/ww3mod/rules/ingame/crew.yaml` — change weapon to SMG, ammo to ~1/3, cost per role (Commander=200, Pilot=300, Copilot=200, D/G=100), add Selectable Priority + Class for deprioritization.
- `mods/ww3mod/rules/ingame/vehicles.yaml`, `aircraft.yaml`, faction variants — remove obsolete `EjectionSurvivalRate`, `CrewDamageThresholdPercent`, `CrewDamageVarianceDivisor` from `VehicleCrew` blocks.
- `mods/ww3mod/rules/world.yaml` — add `CrewEvacRules` world trait.
- Possibly `mods/ww3mod/rules/ingame/defaults.yaml` for the `^CrewMember` template tweaks.

### Tests
- **NEW:** `engine/OpenRA.Test/Traits/CrewEjectResolverTest.cs` — unit-test the resolver as a pure function. Cover the 9 scenarios in the table above plus edge cases (zero finishingDamage, finishingDamage > 2× maxHP, remainingFraction = 0 / 1).

---

## 10. Where to pick up

1. Confirm Copilot cashback = 200 vs 100 with the user (one-line clarification).
2. Convert this handoff into a formal spec at `docs/superpowers/specs/YYYY-MM-DD-crew-evacuation-design.md` (or `CLAUDE/plans/<date>_crew_evac_design.md` to match repo convention).
3. Get user sign-off on the spec.
4. Invoke `superpowers:writing-plans` to produce an implementation plan. The plan should sequence:
   - Phase 1: refactor existing `VehicleCrew` paths to converge on a resolver call (no behavior change yet — verify with tests)
   - Phase 2: implement `CrewEjectResolver` with the new model + tuning knobs + tests
   - Phase 3: harden stop detection
   - Phase 4: update YAML (crew weapon, ammo, costs, selection class)
   - Phase 5: update HeliEmergencyLanding critical path to call the resolver
   - Phase 6: playtest + tune `CrewLethalityScale`

Each phase is independently testable. Phase 1 is the riskiest because it touches a load-bearing existing system without changing behavior; do it carefully.

---

## What was deliberately ruled out

For the next agent's awareness — these were considered and rejected:

- ❌ Per-weapon `IsIncendiary` flag — user explicitly walked back from this
- ❌ Per-weapon `Lethality` multiplier — same
- ❌ Per-vehicle `CookoffProneness` — same
- ❌ Cumulative incendiary damage tracking on the vehicle
- ❌ Medic extinguish behavior — explicitly out of scope ("consider later")
- ❌ Water-tile auto-extinguish — not requested
- ❌ Stop-and-roll behavior for on-fire crew — not requested
- ❌ EVA voice line "crew rescued" — left to spec phase, not user-prioritized
- ❌ Special "auto-evacing" icon decoration — user said move-order line is enough
- ❌ Engine-side hard exclusion of crew from box-select — Q9c-A rejected in favor of soft Q9c-B
- ❌ Special AI handling — AI uses same path
- ❌ Danger-aware pathfinding for evac route — accepted as feature/scope-cut

---

*End of handoff. Anything below this line is space for the user to add adjustments and notes.*

## User adjustments / notes

<!-- (space for the user) -->
