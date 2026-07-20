# Playtest bugs 1–3 — root-cause triage (implement-ready)

**Date:** 2026-07-21
**Researched against:** `main @ 2ed2c0ac` (working tree: only `.maestro/managers/` untracked; no engine/ai.yaml drift)
**Source:** user spectated Experimental-vs-Experimental on River Zeta; reported 3 bugs + 3 early-game behaviour problems (early-game in `260721_earlygame_tuning.md`).
**Mode:** read-only recon — no build/launch/test run. All claims cited file:line.

Standing constraints honoured in every fix below:
- New Info fields **must default to frozen behaviour**: `@stable`, Normal/Rush/Turtle stay byte-identical. Opt-in `true`/non-default only on the `@experimental` trait instance (mirror the `CohesionSwitchEnabled` pattern — `PoiOffensiveBotModule.cs:87`).
- Never mutate shared singletons: world `PoiMap`, `PoiGoalGuard@poi`, `MountedTransportBotModule@poi`. Per-bot Info fields only.

---

## Bug 1 — evacuating (out-of-ammo) units get re-ordered onto attacks

### Root cause
The evac is an **engine unit-level behaviour**, invisible to the offense bot module, and the offense module's unit selection has **no ammo/evac filter**.

- Out-of-ammo evac originates in `AmmoPool.AutoRearmIfAllEmpty`, `case ResupplyBehavior.Evacuate` → `self.QueueActivity(false, new RotateToEdge(self, true, amount))` — `engine/OpenRA.Mods.Common/Traits/AmmoPool.cs:197-205`. Fired from `INotifyAttack.Attacking` when `!HasAmmo` (`AmmoPool.cs:247-248`) and `INotifyBecomingIdle.OnBecomingIdle` (`AmmoPool.cs:252-254`). WW3MOD vehicles opt in via `InitialResupplyBehaviorAI: Evacuate` (`mods/ww3mod/rules/ingame/vehicles.yaml:514-515`). The `evacuating` condition granted by `RotateToEdge` (`RotateToEdge.cs:143-145`) is **cosmetic** (selection pip/deprioritisation) and is not read by any bot module.
- The evac path sets `NeedsResupply = false` and **never Commits the unit to `PoiGoalGuard.Ledger`**. So the evacuating unit is *uncommitted*.
- `PoiOffensiveBotModule.BuildFreePool` (`PoiOffensiveBotModule.cs:391-401`) admits any unit that is (owner/alive/in-world) + `IsEligibleCombatUnit` + not axis-claimed + not ledger-committed. `IsEligibleCombatUnit` (`:403-412`) filters only owner/alive/positionable/`AttackBase`/not-aircraft/not-`ExcludeUnitTypes`. **There is no ammo check anywhere in offense unit selection.** An uncommitted zero-ammo evac unit therefore re-enters the free pool, is recruited to an axis, and its `AttackMove` (queued `false`, `:507`) replaces the `RotateToEdge` activity — sending an empty unit at the enemy.

**Asymmetry that proves the fix:** the sibling `LayeredDefenceBotModule` **already guards this** — `if (Info.SkipOutOfAmmoUnits && IsOutOfAmmo(actor)) continue;` (`LayeredDefenceBotModule.cs:273`), with `SkipOutOfAmmoUnits = true` (`:102`) and the helper `IsOutOfAmmo` = "all AmmoPools at 0" (`:465-471`). PoiOffensive simply never got the same guard.

### Minimal fix
1. Add `static bool IsOutOfAmmo(Actor)` to `PoiOffensiveBotModule` (copy verbatim from `LayeredDefenceBotModule.cs:465-471`) and a `readonly bool SkipOutOfAmmoUnits = false` Info field.
   - **Default `false`** (NOT `true` like LayeredDefence) because `PoiOffensiveBotModule@stable` is a frozen twin — default-false keeps it byte-identical; set `SkipOutOfAmmoUnits: true` only on `@experimental` (ai.yaml:175 block).
2. In `IsEligibleCombatUnit` (`:403-412`) return false when `Info.SkipOutOfAmmoUnits && IsOutOfAmmo(a)` — excludes evac units from recruitment.
3. In `PruneAxes` (`:415-437`) also drop a unit that has *become* out-of-ammo while on an axis (add `Info.SkipOutOfAmmoUnits && IsOutOfAmmo(u)` to the `RemoveAll` predicate) and `goalGuard.Ledger.Release` it — otherwise a unit that empties mid-axis stays committed and never evacuates. (Release is already done for other prune reasons via `ReleaseAxis`; here do the per-unit release inline as PruneAxes currently only trims the list.)

Risk: **low**. Behaviour-gated behind a default-false field; `@stable`/Normal untouched. Only shrinks the offense pool by units that literally cannot fight.

### Structural option
Give evac a **first-class reservation** instead of relying on every module to re-implement an ammo check. Cleanest: have the evac trigger (`AmmoPool` Evacuate branch, or a thin new `EvacReservationBotModule`) `Commit` the unit to the shared `PoiGoalGuard.Ledger` under an `"evac:<id>"` objective for the RotateToEdge duration; every module already skips ledger-committed units (`BuildFreePool` `:399`, `FindIdleSupportersNear` `:722`, LayeredDefence). This reserves evacuees **globally** in one place and removes the per-module ammo-filter duplication. Downside: couples the engine `AmmoPool` trait to a bot trait (or needs a small new module) — heavier than the one-field guard, so ship the minimal fix first and consider this if a *third* module needs the same guard.

### Verify strategy
- **Autotest (behaviour-level, hidden Mode-B):** new test — spawn an Experimental bot with a unit forced to zero ammo near the SR while an offensive axis is active; assert the empty unit's activity is/stays `RotateToEdge` (or it moves toward its own map edge, not toward the enemy target) across ≥1 `ReevaluateInterval` (100t). Reuse the harness pattern from the capture-reliability tests (`WORKSPACE/plans/260720_capture_reliability_cycle1.md`).
- **Ladder non-regression:** S1 + S2 on River Zeta with `SkipOutOfAmmoUnits: true` on `@experimental` only; expect neutral-to-positive swing (fewer wasted empty-unit deaths). Because the field is default-false, the `@stable` control is provably unchanged (byte-identical to `2ed2c0ac`).

---

## Bug 2 — helicopters fly to a corner and idle forever

Two independent halves; **Part B is the real blocker**.

### Part A — what sends them to the corner (arrival logic, not RA idle-return)
Called-in aircraft spawn at a map-edge cell near the SR SpawnArea and are handed a waypoint plan of `hasRallyPoint ? rp.Path : { self.Location }`, where `hasRallyPoint = rp != null && rp.Path.Count > 0` — `ProductionFromMapEdge.cs:89, 173-175` (spawn `:97-111`). The SR `RallyPoint` (`mods/ww3mod/rules/ingame/structures.yaml:272-274`) sets **no default Path**, and the AI never issues a rally order, so `rp.Path.Count == 0` → the heli is told to `MoveTo(self.Location)` = the SR building's own cell, which sits at a map edge → it arrives and stops. RA idle-return is **ruled out**: `Aircraft.IdleBehavior` defaults `None` (`engine/OpenRA.Mods.Common/Traits/Air/Aircraft.cs:27`; `OnBecomingIdle` only acts for `LeaveMap*`/`ReturnToBase`/`Land`, `:903-933`) and no `IdleBehavior` is set on `^Airborne`/`^Helicopter`/any heli. The "corner" is the edge-spawn/SR arrival point, and the heli hovers there.

### Part B — why nothing re-tasks them: the rearm-full mission gate
`HelicopterSquadBotModule` is active for all factions (`ai.yaml:605-613`, `enable-ai-any`) and **does** recruit helis correctly — by trait `AIHelicopterRole` (`HelicopterSquadBotModule.cs:146`), which HELI/littlebird/TRAN/HIND/MI28/HALO all carry. The block is mission readiness:
- `TryLaunchAttackMission` filters idle helis through `IsReadyForMission` (`HelicopterSquadBotModule.cs:229`).
- `IsReadyForMission` (`:399-408`): a heli with `AmmoPool` **and** `Rearmable` must have **every pool `HasFullAmmo`**, else returns false.
- Attack helis have `AmmoPool` + `Rearmable{ RearmActors: hpad }` (e.g. `mods/ww3mod/rules/ingame/aircraft-russia.yaml:166-225`, `aircraft-america.yaml:327-377`) and their `ReloadAmmoPool RequiresCondition: unit.docked && !airborne` (e.g. `aircraft-russia.yaml:178`) — **ammo only refills while docked at an `hpad`, and the mod builds no HPAD**. So after the first shot (or if it never tops off post-call-in) a pool is below full forever → `IsReadyForMission` permanently false → `attackHelicopters.Count < neededSize` (2–3) → **no squad ever forms**, the `HelicopterStates` FSM never runs, and the heli is never issued a move/attack. This is the squad-path twin of the documented production-side `SkipRearmBuildingCheck` trap (`DOCS/reference/architecture.md:~289`), which only bypasses the *builder* gate.

### Minimal fix
- **Part B (unblocks everything):** add `readonly bool SkipRearmReadyCheck = false` to `HelicopterSquadBotModuleInfo`; in `IsReadyForMission` (`:401`) skip the `HasFullAmmo` loop when set (or, better, when no rearm actor of `rearmable.Info.RearmActors` exists for the owner — auto-detect "nowhere to rearm"). Set `SkipRearmReadyCheck: true` in `ai.yaml:605`. This is `enable-ai-any` (shared by all bots) — acceptable because it fixes an equally-broken behaviour for every profile and defaults false, but if strict per-profile isolation is wanted for the ladder, gate the flag on a new `@experimental` HelicopterSquad twin instead.
- **Part A (staging):** once Part B forms squads the FSM issues moves so the corner-idle clears for engaged helis; for the pre-squad wait, optionally issue a forward staging `Move` on recruit in `FindNewHelicopters` (toward `threatMap.FindWeakestEnemyCell`) so they don't loiter at the edge.

### Structural option
Give called-in aircraft an **off-map / always-available rearm path** (an invisible auto-resupply, or make `ReloadAmmoPool` not require `unit.docked` for AI-owned helis). This resolves `IsReadyForMission`, the FSM's `SquadHasAmmo`/`SendLowAmmoUnitsHome`, and the production-side check *uniformly* — otherwise every attack heli is permanently benched after its first sortie even with the minimal fix. Aligns with the game-model note that HPAD/AFLD are optional rearm *accelerators*, not prerequisites (`DOCS/reference/game-model.md:12`).

### Verify strategy
- **Autotest (behaviour-level, hidden Mode-B):** Experimental bot produces helis; after N ticks assert ≥1 heli has left its spawn cell and an attack squad formed (grep the `HelicopterSquadBotModule` debug line for a launched mission). Guard against the permanent-bench regression: fire once, then assert the heli still becomes mission-ready.
- Log the discovered permanent-bench issue in `WORKSPACE/bugs/discovered.md` (done).

---

## Bug 3 — TECN walks to distant captures; mounting never observed (user priority)

The `MountedTransportBotModule` **is instantiated** for Experimental (`ai.yaml:358-372`, `enable-ai-experimental || enable-ai-stable`) and even emits a chat line on enable (`MountedTransportBotModule.cs:121-124`) — so it's present but ineffective, matching "never saw a soldier mount." Carriers **are** produced (`bradley: 25`, `m113: 15` — `mods/ww3mod/rules/ai/ai-america.yaml:27-28`; `bmp2`/… Russia equivalent) and are excluded from LayeredDefence (`ai.yaml:341`) and PoiOffensive (`ai.yaml:187`), so they're free for transport — carrier availability is **not** the blocker. Three real causes, in priority order:

### Cause 3.1 — the module is dormant until frontline contact (dominant, early-game)
`TryAssignNewTasks` computes a shared drop-off via `PickDropOffCell` (`MountedTransportBotModule.cs:312-314, 373-415`), which returns **null** when `influenceMap == null` OR `influenceMap.GetFrontline(player) == null`. `GetFrontline` marks only cells where **both** friendly and enemy influence are present — `InfluenceMap.cs:170-174` → `InfluenceMapMath.DeriveFrontline` requires `friendly[x,y] > 0 && enemy[x,y] > 0` (`:248-256`). **Pre-contact there is no frontline → dropOff null → the module returns without assigning any carrier** (`:313-314`). So in the entire early game (the window the user watches) mounting is impossible by construction, and the idle carriers pile up at the SR (this is a direct contributor to early-game item (c) massing — see tuning doc).

### Cause 3.2 — TECN is not an eligible passenger, and capture is fully decoupled from transport
`PassengerTypes` (`ai.yaml:366`) lists line/support infantry only — **`tecn`/`tecn.*` are absent**, so a technician can never be reserved as a passenger (`:296-302` filters on `PassengerTypes.Contains`). Separately, there is **no code path from `CaptureCoordinatorBotModule` to `MountedTransportBotModule`**: the coordinator issues `CaptureActor` to the TECN (`CaptureCoordinatorBotModule.cs:514`) and recruits escorts **on foot** via `FindIdleSupportersNear` + grouped `AttackMove` (`:627-643`); it never requests a ride. MountedTransport's destination is the thinnest *frontline* cell (`PickDropOffCell`), not a capture target. So the two systems only share the goal-guard ledger for deconfliction — they never cooperate. **"Technicians riding first" is therefore not implemented at all**, independent of 3.1.

### Cause 3.3 — even post-contact, the passenger reserve window is narrow
Passengers must be within `ReserveZoneRadiusCells: 14` of the SR (`ai.yaml:369`, `:296-302`). LayeredDefence/PoiOffensive grab fresh production and push it forward on their own cadence; MountedTransport does *not* require idle and uses `EnterTransport` (queued false) to cancel forward orders (`:344-345`), so it *can* claw a passenger back — but only inside the 14-cell reserve ring and only when a valid dropOff exists (3.1). In practice the frontline gate (3.1) means this rarely triggers early.

### Minimal fix (sequenced — one behaviour per verify cycle; see tuning doc for the split)
1. **Un-gate early delivery (3.1):** when `GetFrontline` yields no contested cell, fall back to a sensible forward staging dropOff (e.g. toward the top `PoiMap.GetOffensiveTargets(player)` cell, or a fixed fraction of SR→enemy-SR). Add `readonly bool DeliverBeforeContact = false` (default false = frozen) and enable on the shared `@poi` instance **only if** both Experimental and Stable should get it — since `@poi` is a shared singleton this is *not* per-profile isolable; if the ladder needs isolation, this fix must wait for a per-profile transport instance (flagged risk).
2. **TECN-first ferrying (3.2, the user priority):** the intended behaviour is a *capture-oriented* transport, which the current frontline-gap transport cannot express. Two options:
   - **(A, lighter)** Add `tecn.america`/`tecn.russia` to `PassengerTypes` **and** a capture-aware dropOff: when a TECN boards, set that carrier task's dropOff to the TECN's committed capture target (read `PoiGoalGuard.Ledger` objective `capture:<id>` → resolve actor cell) instead of the frontline cell. Requires MountedTransport to consult the ledger — new coupling but read-only.
   - **(B, structural)** A dedicated `CaptureRideCoordinator` (or a mode on CaptureCoordinator) that, before issuing `CaptureActor`, checks for a free carrier and issues `EnterTransport` → carrier `Move` to target → `UnloadCargo` → TECN `CaptureActor`. Keeps frontline transport and capture transport as distinct intents.

### Structural option
Reframe MountedTransport as **destination-agnostic ferrying**: a carrier task takes a `(passengers, destination-provider)` where the provider is pluggable (frontline gap **or** capture target **or** offensive axis staging). CaptureCoordinator and LayeredDefence both request rides through one queue. This is the clean way to get "TECN rides to the derrick, soldiers ride to the line" without special-casing, and it removes the frontline-only assumption baked into `PickDropOffCell`.

### Risk & shared-singleton caveat
`MountedTransportBotModule@poi` is a **shared singleton** across Experimental+Stable (`ai.yaml:358-359`, single-instance `TraitOrDefault` lookup). Any Info-field change there mutates Stable too → **breaks byte-identical ladder control**. Therefore: field changes with default-frozen values are safe *only if* enabling them via YAML on `@poi` is acceptable for both profiles; true per-profile isolation requires splitting the singleton (consumers use `TraitOrDefault<MountedTransportBotModule>()`, so a second instance throws — splitting needs a keyed lookup refactor first). **Recommend:** land the TECN-first behaviour as new code defaulting to the current behaviour, enable on `@poi`, and accept that Stable inherits it (or defer until a per-profile split). Call this out at cycle start.

### Verify strategy
- **Autotest (behaviour-level, hidden Mode-B):** Experimental bot with a reachable derrick; assert (a) at least one TECN enters a carrier (`EnterTransport` → `Cargo.PassengerCount > 0`) before reaching the target on foot, and (b) the carrier unloads within N cells of the capture target. For 3.1: assert a carrier receives a delivery order before any frontline contact exists.
- **Ladder:** capture-count + capture-income on S1 River Zeta; compare TECN time-to-capture with/without ferrying. Because the shared-singleton caveat means Stable may move too, run the A/B as experimental-code-on vs a build with the new path compiled-out, not `@experimental`-vs-`@stable`.

---

## Recommended fix order
1. **Bug 1** (offense ammo guard) — smallest, self-contained, default-false, proven pattern already in LayeredDefence. Ship first.
2. **Bug 2 Part B** (heli rearm-ready bypass) — unblocks the entire helicopter layer that is currently 100% inert; high impact, one field.
3. **Bug 3.1** (transport pre-contact delivery) — makes mounting possible at all; prerequisite for the user's priority.
4. **Bug 3.2 (A then B)** (TECN-first ferrying) — the user's stated priority, but depends on 3.1 and carries the shared-singleton caveat; do after 3.1 proves the delivery path.
5. **Bug 2 Part A** + **Bug 2 structural** (heli staging + off-map rearm) — polish/durability once squads form.
