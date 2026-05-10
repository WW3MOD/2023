# WW3MOD v1 Release Tracker

> Single source of truth for v1 scope. Update continuously as items are tested, fixed, deferred, or cut.
>
> **Status legend:** `[ ]` open · `[~]` in-progress · `[T]` testing · `[T:trusted]` code-verified spot-check (fix is in the tree, no contradicting later commit; not yet AUTOTEST-confirmed) · `! [T]` urgent + testing · `[v1.1]` deferred · `[cut]` won't-fix v1
>
> **Scope is locked.** New features need explicit "yes, add to v1" from the user. Otherwise → `BACKLOG.md` or `Pending decisions` below.
>
> **Items pass AUTOTEST or playtest → removed entirely.** Commit history is the archive. No `[x]` graveyard, no "Recently completed" section.

## Phase

**Currently in: Phase A — Stabilize**

- **Phase 0 — Tooling** — autotester / harness friction that speeds up other tasks
- **Phase A — Stabilize** — get every "needs playtesting" system verified or fixed. No new features.
- **Phase B — Tier-1 fixes** — bugs and gameplay gaps that block release.
- **Phase C — Polish** — sounds, icons, descriptions, open polish threads.

---

## Phase 0 — Tooling

- [ ] **Autotester launches focused, interrupting work in another window** — flash on launch steals focus when I'm typing in another window. Want: launch minimized so it doesn't pull focus
- [ ] **Autotester launch position should follow current terminal, not be fixed-left** — earlier attempt landed on "opposite side of focused window" but I'm often jumping between windows when it spawns. Best behaviour TBD: per-session override + default to opposite-of-active-terminal? Discuss before implementing — come with your thoughts

---

## Phase A — Stabilize

### Big systems
- [T] **Garrison overhaul** (Phases 1–6) — indestructible buildings, dynamic ownership, directional targeting, suppression integration, visuals
- [ ] **Cargo system** (Phases 2A–E) — TRUK auto-rearm, mark+unload, rally points, supply drop, merge
- [ ] **Helicopter crash + crew overhaul** — critical=total loss, safe land=neutral+repairable, capture-by-pilot-entry
- [ ] **Stance rework** (4 phases) — modifiers (Click/Ctrl/Ctrl+Alt/Alt), resupply behavior, cohesion, patrol
- [ ] **AI overhaul** (Tiers 0–3.1) — bot modules, multi-axis attacks
- [ ] **Supply Route contestation** — graduated control bar, production slowdown, notifications
- [ ] **Three-mode move system** — Move/Attack-Move/Force-Move, SmartMove wrapping
- [ ] **Vehicle crew system** — slot ejection, re-entry, commander substitution
- [ ] **Infantry mid-cell redirect** — tune `RedirectSpeedPenalty` (currently 50%)

### Supply & ammo economy
- [T] **Supply & ammo economy overhaul** (260506, plan: `WORKSPACE/plans/260506_supply_ammo_economy.md`) — P1–P3 shipped (15 commits):
  - **P1:** empty-truck refund deduction; LC `Range 3c0→2c0` + `unit.docked` gate; Ctrl+click = deliver, default = repair+refill via new `RefillFromHost`/`Restock`. Tests in `CargoSupplyEconomyTest.cs`
  - **P2:** new `IProvideTooltipDescription` interface; `AmmoPoolInfo` adds weapon block + grand-total to production tooltip
  - **P3:** ~63 AmmoPools across 9 YAMLs given explicit `SupplyValue`/`CreditValue` per tier table (T0=1 → T9=1500)
  - **Verify:** empty TRUK refund = 250; cannot refill within 3c0 unless docked at 2c0; right-click LC behaviour; multi-pool tooltip; tier-cost feel
- [T:trusted] **Supply truck resupply bar + LC refill** (260504, commit 179aba43) — TRUK gets 3-stance resupply bar (default Evacuate). Auto seeks LC, refills via `SupplyProvider`. `AutoRefillIfEmpty` Hold/Auto/Evacuate dispatch verified at `CargoSupply.cs:749-766`
- [ ] **Verify unit sell value at different ammo levels** — broader than the TRUK refund check above. Spent ammo should be deducted from cashback at evac for ALL units (tanks, infantry with reload). Sweep

### Active items in flight
- ! [T:trusted] **Supply truck deploy → drop cache** — fixed 260504 (commit b3699b63; `CargoSupply` `IIssueDeployOrder` + `DropCargoSupply` `DeployOrderTargeter` verified at `CargoSupply.cs:82,585,598`). Scope locked to TRUK only.
  - **Open design discussion (urgent flag is here):** dropped supply cache should act as a real supply actor with its own supply bar under it. Right now it doesn't — and I think it's indestructible? It should be very destructible, causing a large explosion when destroyed (size could vary based on remaining supplies). I also think we should be able to target it with supply trucks to replenish the same pile over and over, or something like that — even open to automatic replenishment by selecting the cache and using stances on it. Needs discussion first.
- [ ] **Supply truck → building = transfer supplies** *(new feature, not started)* — building gains supply bar; soldiers inside/nearby drain it
- [T:trusted] **Helicopters evacuated near map edge bypass missile fire** — fixed 260504 (commit 98742d4e). `EvacuatingOffMap` field + `IsClearOfMapEdge` despawn gate verified present in `Aircraft.cs:286,663`. Vehicle extension is a separate design item (below)
- [ ] **Vehicle off-map evac flight (extension of heli fix)** — same off-map-fly-before-sold treatment for vehicles, shorter distance. Past the boundary: targetable but unselectable. Goal: prevent border-camp evac that dodges incoming fire, plus better visuals than vanishing at edge tile
- [ ] **Littlebird rotor still spins after safe landing** — needs investigation (sweep all helis)
- [T:trusted] **TECN capture order lost when shot at + panicking** — fixed 260504 (commit be46cde9). Code verified intact; ScaredyCat.cs untouched since
- [T:trusted] **Shift+G on attack-ground orders converts them to move orders** — fixed 260504 (commit dd6cc18f). `IAttackActivity` interface still implemented in all 4 attack-activity classes; GroupScatterHotkeyLogic still consumes it
- [T] **Aircraft can't spawn if waypoint blocked** — partial fix; aircraft branch may have separate cause from ground-unit fix. Re-test (no commit verified yet)
- [T:trusted] **Garrison: only first soldier of a batch enters** — mitigation 260503 (commit bf63eef4, `ChangeOwnerInPlace(updateGeneration:false)` at `GarrisonManager.cs:261,325,330`). Keeps in-flight Enter activities valid through ownership flip
- [T:trusted] **Stop order doesn't cancel garrisoned firing** — fixed (commit 97e192cc). `AttackGarrisoned.OnStopOrder` → `GarrisonManager.OnStopOrder` clears forceTarget, port targets, ambushTriggered. Verified at `AttackGarrisoned.cs:391-396`, `GarrisonManager.cs:1131-1143`
- [T:trusted] **Soldiers under fire abandon Enter-building order** — fixed 260504 (commit fdfaffb1). New `MoveToTargetRaw`/`MoveIntoTargetRaw` on `IMove` bypass WrapMove; Enter uses raw variants at `Enter.cs:117,131`. Capture/Demolish/Ride/Infiltrate also benefit
- [T:trusted] **Iskander/HIMARS shockwave radius too large** — tuned 260509 (commit 9578557c). `MaxRadius` values verified in `weapons-explosions.yaml`: Iskander 4c0 (line 495), HIMARS 2c512 (line 532). Feel needs human eye in next playtest

### Known design issues
- [ ] **Tank frontal armor stalemate** — sim shows pen 50 vs 700 thickness = 7% dmg per hit. Either rework armor model or rebalance pen values
- [~] **Buildings invisible / fog visibility model** — quick-fix 260503: `FrozenUnderFog.IsVisible` short-circuits to `return true`. Proper fix: investigate `FrozenActor.Visible` initial state and whether buildings should fog at all in WW3MOD
- [ ] **Visibility / fog design decisions for v1** — open questions raised during garrison playtest:
  - Should buildings block line of sight at all? Old solution: only trees & static cover. Hiding behind a building is micro-intense and unintuitive — bad gameplay
  - Should "fog" be a visibility *modifier* (weather-style, partial) on top of shroud/sight, or binary?
  - What lobby options ship with v1: just toggles, or richer fine-tuning (sight range modifiers, weather modes)?
  - **Decision needed before:** Phase A garrison playtest can fully complete; SR contestation depends on visibility too

---

## Phase B — Tier-1 Fixes

### Active bugs
- [ ] Artillery fires all ammo at once when critically damaged
- [~] **Artillery force-attack blocked during setup countdown** — Layer 1 fixed 260509 (commit 51db91f7), `NextActivity is AttackActivity` skip-clear verified at `AttackFollow.cs:400-401`. Layer 2 OPEN: turret stalls mid-rotation; `Turreted.Tick` realign path likely fighting `FaceTarget`. Repro: `test-arty-force-attack-during-setup` (currently RED). Touches: `Turreted.cs:191-216`, `AttackTurreted.cs:36-48`
- [ ] **Heavy artillery deliberately ignores infantry** *(noted 260508)* — by design via `^AutoTargetArtillery`. Decision: add low-priority Infantry, or keep heavy-only?
- [ ] **Some enemy soldiers untargetable (mutual)** *(reported 260508)* — needs repro: unit type, stance, near garrison port?
- [ ] **Bridge pathing — units walk off the bridge** — *Investigated 260509 (read-only):* `Bridge.cs:158,322` correctly overrides `Map.CustomTerrain[c]` for footprint cells, so the bridge cells DO get `Bridge` terrain type. But the `foot` locomotor (`world.yaml:28-42`) permits `Beach: 80`, `RiverShallow: 40`, `Shallow: 30` — so infantry can legally walk along the shore *next* to a bridge. Pathfinder cost is inverse of speed, and `Beach: 80` vs `Bridge: 100` is a ~25% penalty per cell — small enough that even a 1-2 cell shortcut along the beach can beat going across the bridge. Likely fixes: (a) reduce `Beach`/`Shallow` passability for `foot` (breaks beach landings), (b) widen bridge footprint to cover the shore approach cells, (c) add a per-bridge guide cell that pulls paths onto the deck. Vehicles may have the same issue; check `wheeled`/`tracked` locomotor speeds for shore terrains
- [ ] **Allied shared vision blinks rapidly (~3-4 Hz) for ~2s** *(reported 260505, USA Abrams dying, allied team)* — static analysis ruled out condition-gated Vision, VisionModifiers, EjectOnHusk, owner flicker. Cannot reproduce. Wait for recurrence — note attacker, healer presence, HP%, motion, replay if possible
- [ ] **Helicopter→helicopter missiles silently vanish on impact** — *Investigated 260509 (read-only):* `Missile.cs:829` does set `flyStraight=true` when the missile overshoots its closest approach. But the airburst trigger at `Missile.cs:980` (`height.Length < AirburstAltitude && relTarHorDist < CloseEnough`) does NOT gate on `flyStraight`. So once a heli→heli missile is committed to fly-straight, if its trajectory passes near the target's *current* position with the missile at low altitude, it detonates anyway — at empty space (target moved) or below the target heli (AirburstAltitude is ground-relative, not target-relative). Result: silent detonation, no visible explosion if the warhead is height-gated, no damage to the airborne target. Likely fix: gate line 980's airburst on `!flyStraight`, OR make `AirburstAltitude` target-relative for airborne targets. Possibly same root as WGM mid-flight loss below
- [ ] Helicopter husks on water don't sink
- [ ] ATGM units can't unload while shooting (attack lock)
- [ ] Walking sequence speed mismatches locomotor on different terrains
- [ ] **Mobile sensor (CounterBatteryRadar) doesn't work** — *Investigated 260509 (read-only):* the wiring chain looks complete: MSAR has `CounterBatteryRadar: Range: 42c0, RequiresCondition: deployed` (`vehicles.yaml:352`); Paladin has `Detectable.CounterBatteryRadar: 1, CounterBatteryRadarDetectableCondition: firing` + `GrantConditionOnPreparingAttack: Condition: firing, RevokeDelay: 100` (`vehicles-america.yaml:585-598`); `Detectable.cs:110,115` consults the layer; `MapLayersExts.AnyVisibleOnCounterBatteryRadar` exists. So mechanically it should fire when (a) MSAR is deployed, (b) Paladin is in MSAR's 42c0 range, (c) Paladin is firing or within 100 ticks of a shot. Likely "doesn't work" reasons: user testing without deploying the MSAR, the 4-second reveal window too short for any UI feedback, or no audio/icon cue so the player doesn't realize it briefly revealed. Needs reproduction with deployed MSAR + active enemy artillery to confirm
- [ ] **River Zeta: neutral SAM** — always invisible (probably deprecated Cloak trait). Should have low visibility (Cloak replacement) but not invisible, and capturable by technician. Plus broken capturable building elsewhere on the map

### Drone fixes
- [ ] DR animations — prepare runs idle, drone launches before prep finishes
- [ ] Drone autotarget of other drones broken
- [ ] Anti-drone weapon too effective — freeze mid-air, fall when battery dies?
- [ ] Drone death needs crash animation

### Aircraft polish
- [ ] Edge spawn/leave for planes
- [ ] Helicopter landing refinement (slow before landing, faster turn to avoid overshoot)
- [ ] Apache shouldn't shoot guns at structures
- [ ] Ballistic missile tilt fix — Iskander/HIMARS missiles don't pitch properly on arc

### Combat / suppression / bypass
- [ ] Suppression tuning — playtest vehicle values, per-weapon fine-tuning
- [T] Bypass system refinement (ATGM tree handling) — *260510* density-based fire-time gate on WGM/Hellfire (`FreeLineDensity`, `MissChancePerDensity`); per-weapon `ClearSightThreshold` now applies in `Armament.CheckFire` instead of being washed out by coarmaments. Tests: `test-wgm-fires-clean`, `test-wgm-fires-thru-1-tree`, `test-wgm-deny-thru-5-trees`. *Range-based hit chance still pending.*
- [ ] Flametrooper effective vs unarmored
- [ ] Units out of ammo reject attack orders (don't freeze aiming)
- [ ] **No-ammo units must reject attack-move + go idle if ammo runs out mid-attack-move** *(reported 260508)* — needs design pass: interaction with Resupply stances, whether to complete move or stop in place, mixed-group handling
- [ ] Shoot at last known location for stationary targets
- [T] WGM should not fire if it won't hit — *260510* same change as the Bypass system row; `ClearSightThreshold: 4` + `FreeLineDensity: 1` + `MissChancePerDensity: 15` on WGM/WGM.bradley/Hellfire, plus `Blockable: false` on the projectile so the fire-time roll is the sole arbiter.
- [T] **WGM (Bradley/BMP) loses track during normal flight** *(reported 260508, fixed 260510)* — root cause: when `flyStraight` latched (missile overshot target or target invalidated), `HomingInnerTick` was skipped — and that's the only place `ChangeSpeed` is called. A missile that decelerated approaching the target locked in the slow speed and crawled the rest of the way to fuel-out. Fix in `Missile.HomingTick`: always `ChangeSpeed()` while flying-straight (motor stays burning), drop the redundant target-invalid hFacing freeze (let velVec drive homing toward the preserved last-known `targetPosition`), and reset `flyStraight`/`minDistanceToTarget` when operator retargets so the new target is actually homed on. Plus `Armament.FireBarrel` re-validates the captured target across the FireDelay gap — no more launching at a target that died between aim and pull. Test `test-wgm-target-dies-midflight` covers the post-target-death path; `test-wgm-accuracy-moving` jumped 63%→92% as side effect
- [ ] Ballistics deprioritize targets if hit chance too low

### Supply Route
- [ ] Captured SR handling — what spawns link, neutral SRs between players
- [ ] Primary SR selection UI

### AI
- [ ] AI builds Logistics Centers, rearms
- [ ] AI conscripts don't abandon capture for squad orders
- [ ] AI stops firing at buildings marked for capture
- [ ] AI garrisons defense buildings
- [ ] AI uses attack-move for aircraft

### Misc gameplay
- [ ] Helicopter force-land tuning + crew bloat fix + crew vehicle re-entry testing

---

## Phase C — Polish

### Sounds (the big gap)
- [ ] Unit firing sounds
- [ ] Explosion sounds
- [ ] Unit voice responses

### Visuals
- [ ] Unit icons
- [ ] Per-unit rot/bleedout sprites (currently uses generic e1)
- [ ] Unit description box sizing

### Open development threads
- [ ] **Garrison Phase 4** — sidebar icon panel rewrite
- [ ] **Cargo Phase 3** — template sidebar for pre-loaded transport purchasing

### Performance pass
- [ ] Pre-release perf pass (see "Pending decisions" → Performance pass for approach)
- [ ] **6-player skirmish slow on MacBook** *(reported 260508)* — first step: read git history for prior perf work (shadow-cache freeze, density layer, AI tick budgets) before re-investigating. Then profile

---

## Pending decisions

> Items raised during work that need a "yes / no / defer" call before they're scoped into v1 or sent to backlog.

- [decision] **Fog richness in v1** — ship just shroud/fog toggles, or invest in weather fog / sight-range modifiers / per-faction sensors? Lean simple for v1, richer goes v1.1
- [decision] **Infantry self-defense baseline + AT soldier rebalance** *(260503)* — give most infantry a basic firearm; AT soldiers rifle + 2 missiles (down from 3). Open: which specialists become hybrids? Sidearm damage gap? Engineers/medics? AI comp impact?
- [decision] **Playtest session logging (developer mode)** — proposal: lobby checkbox opens `gameplay.log` channel for orders, production state changes, unit lifecycle, per-tick frame budget. Decide: ship in v1 or dev-only
- [decision] **Performance pass before v1** — A) VS profiler (thorough), B) tick-budget log channel, C) `dotnet-trace` + PerfView. Recommend B+C; VS only if those miss it
- [decision] **Garrison entry flow + visuals** — wants: (a) "inside" on footprint not center; (b) transfer flash; (c) replace green chevron with vehicle-health-style pips tied to damage state. Touches `EnterGarrison`, `GarrisonManager`, `WithGarrisonDecoration`. Needs design pass
- [decision] **Targeting code review session** — custom scoring (type/distance/specialist) with AI-era edits since. Not broken but worth a walkthrough. Schedule in v1 or defer to v1.1?
- [decision] **Helicopter formation flying ("flock-style")** *(260504)* — same-destination helis jostle under `Repulsable`. Sketch: group-formation modifier akin to `CohesionMoveModifier` distributing perpendicular offsets. Probably v1.1 unless blocker
- [decision] **Shadow / visibility recalc cost vs. dynamic obstacles** — branches: A) drop buildings/trees from visibility entirely, B) keep static-only (current), C) optimize recalc (incremental/deferred). C is the expensive path. Intersects fog/visibility decisions above

---

## Deferred to v1.1 / Won't fix v1

- [v1.1] Per-Supply-Route production queues (needs engine changes)
- [v1.1] Ukraine as third faction
- [v1.1] Ammo costs money (full economy rework)
- [v1.1] Tier 2 hotkey overhaul (Alt/Ctrl modifier polish)
- [v1.1] Lobby option dropdowns (army upkeep, kill bounties, short-game threshold)
- [v1.1] Map editor improvements (more civilian structures, road tiles)
- [v1.1] Engine upgrade to release-20250330 (12–22 sessions)
- [v1.1] River Zeta shellmap overhaul
- [v1.1] Unit description overhaul & auto-generated stats
- [v1.1] Rename tech levels to "DEFCON"
- [v1.1] Move widgets to edges, free up UI space
- [v1.1] Airstrike support powers (A-10, Su-25) — hidden for v1. `AirstrikePower` + lobby option commented out in `player.yaml`/`world.yaml`; A10/FROG actor defs left orphan. Re-enable by uncommenting; needs balance pass
