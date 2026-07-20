# Discoveries

> Patterns, gotchas, and insights found during work. Dated entries.
> Stable, broadly applicable items should also go into CLAUDE.md.

## 2026-07-21 — Autotest sim speed: single tests are 1× by omission; the render-per-tick coupling caps the tournament's 8×

Found during a read-only throughput audit (full options report: `WORKSPACE/plans/260721_sim_throughput.md`).
- **`run-test.sh` runs at 1× because it never passes a speed arg.** `TestMode.SpeedMultiplier` defaults to `1` (`engine/OpenRA.Game/TestMode.cs:80`) and the single-test launcher forwards no `Test.SpeedMultiplier` (`tools/autotest/run-test.sh:285-295`). Mod default `Timestep` is 60 ms (`mods/ww3mod/mod.yaml:369-372`) → ~16.7 sim ticks/s. Only the tournament path passes `Test.SpeedMultiplier=8` from config (`run-tournament.sh:298`).
- **The multiplier apply-site is tournament-only.** `Test.SpeedMultiplier` is parsed + clamped 1–16 (`TestMode.cs:100-102`) but *applied* only inside `BotVsBotMatchWatcher.WorldLoaded` via `world.Timestep = max(1, base/N)` (`BotVsBotMatchWatcher.cs:152-158`). Lua single-tests get nothing even if you pass the arg — the fix must add a universal apply site (world trait or `Game.LoadMap` next to the `GameSpeedOverride` hook, `TestMode.cs:62-65`).
- **`Test.GameSpeed=fastest` is only ~1.5×** (`Timestep: 40`, `mod.yaml:381-384`) — that's why every config note says SpeedMultiplier dominates. The cheat button caps at 8× by the same `world.Timestep` division (`SpeedControlButtonLogic.cs:58-62`).
- **The real ceiling is CPU, and rendering is the tax.** Every `LogicTick` forces a `RenderTick` (`Game.cs:1026-1027`), so 8× also renders 8×; harness comments claim ~3-4× realized (`run-tournament.sh:286-289`). `MaxLogicTicksBehind=250` (`Game.cs:970,1010`) drops catch-up, so the sim never outruns tick-compute.
- **Minimized window skips rendering, but only helps with an *uncapped* framerate.** SDL minimize/hide sets `IsSuspended` (`Sdl2Input.cs:124-126`) → loop skips `RenderTick` (`Game.cs:1032`) and only pumps input (`1049-1059`). BUT the forced-render flag clears only at render cadence when suspended (`Game.cs:1058`), so minimize + 5 fps cap throttles logic to ~5 ticks/s. Fast combo = **minimize + `CapFramerate=false`** (default `renderInterval≈1 ms`, `Settings.cs:201`, `Game.cs:994-998`). The tournament's current 5 fps profile is *visible*, not suspended (`run-tournament.sh:301-302`).
- **Speed is behavior-neutral (verified).** `world.Timestep` is pure wall-clock pacing, never synced; all rendering is `Sync.RunUnsynced`; Lua timers are tick-based (`test-helpers.lua:82-83`); `OrderLatency:2` is 2 ticks. Bot decisions are a pure function of tick+seed (`BotVsBotMatchWatcher.cs:56-58`). Separate latent bug: `TicksPerSecond=25` (`test-helpers.lua:9`) vs actual 16.7 at 60 ms — constant across speeds, so not a validity issue.
- **Headless ≠ dedicated server.** `OpenRA.Server.dll` (`launch-dedicated.sh`) is a lockstep order relay; it does not run `world.Tick()`/bots. A true headless harness is a *rendering-disabled client* (null graphics platform + a `logicInterval=1` loop branch — the engine already does exactly this for save-loading, `Game.cs:1001-1005`), not the server.
- **Parallelism blocker is a shared support dir.** `launch-game.sh:60` sets no `Engine.SupportDir`, so instances collide on `settings.yaml`, `Logs/debug.log`, and the local server port. Per-instance `Engine.SupportDir` (the dedicated launcher already threads it, `launch-dedicated.sh:98`) + distinct ports unlocks concurrent matches.

## 2026-07-20 — `RenderPlayer = null` world view only clears shroud from a cold start; ShroudRenderer never clears it mid-game

Found while adding full-map vision to the visible TestMode window (worktree `test-observer-vision`).
- **`RenderPlayer` is purely render-side.** `World.FogObscures/ShroudObscures` all short-circuit to `false` when `RenderPlayer == null` (`engine/OpenRA.Game/World.cs:105-111`); no player's `MapLayers` (shroud/fog) is touched, and the sync hash reads `p.UnlockedRenderPlayer`, not `world.RenderPlayer` (`World.cs:541-544`). So switching a real player's client to world view leaves AI perception + the test verdict byte-identical. The dev "disable shroud" cheat is **not** an equivalent: `DeveloperMode` `DevVisibility/DevAll` do `MapLayers.ExploreAll()` + `MapLayers.FogDisabled = true` (`Traits/Player/DeveloperMode.cs:171-197`) on synced (`[Sync] disableFog`) per-player state — that changes the local combatant's unit targeting and the sync hash, so it's unusable under a byte-identical constraint.
- **The trap:** `ShroudRenderer.UpdateShroud` was wrapped in `if (world.RenderPlayer != null)` (`Traits/World/ShroudRenderer.cs:252`), so when `RenderPlayer` flips to null on a *live* client the already-drawn shroud sprites are never cleared → the map stays black even though `WorldOnRenderPlayerChanged(null)` set uniform visibility. True observers look correct only because they start null and never draw shroud at all. Fix: always clear each dirty cell's sprites, then repaint only when a render player is active (same commit). This also repairs the `DevCinematicView` cheat, which toggles `RenderPlayer` to null the same way.
## 2026-07-21 — Heli rearm-full bench has TWO gates: the module readiness check AND the FSM's SquadHasAmmo (minimal fix is not sufficient)

Found while implementing playtest Bug 2 (branch `fix-evac-heli`). The triage's minimal fix (bypass `HelicopterSquadBotModule.IsReadyForMission`'s full-ammo loop) is **necessary but not sufficient** — it lets a squad FORM but not LAUNCH.
- **Second, independent gate:** `HelicopterStates.HelicopterIdleState.Tick` returns early on `!SquadHasAmmo(owner)` (`engine/OpenRA.Mods.Common/Traits/BotModules/Squads/States/HelicopterStates.cs:183`). `SquadHasAmmo` (`:118-131`) *skips* every unit for which `ReloadsAutomatically` is true, then returns false if none remain. `ReloadsAutomatically` (`StateBase.cs:129-139`) is true when a `Rearmable` covers all the unit's pools — EXACTLY the case for attack helis (`Rearmable{ AmmoPools: primary-ammo, secondary-ammo }`). So an all-attack-heli squad reports "no ammo" **even at full ammo**, and the idle/withdraw/re-engage gates (`:183, :427, :458`) never pass. The squad forms and sits.
- **Proven via trace:** with only the module bypass, `squad-formed size=2` fires once, then `idle-blocked reason=SquadHasAmmo` repeats every 5 ticks forever; the helis never leave the ground. Gating those three `SquadHasAmmo` uses behind the same per-module `SkipRearmReadyCheck` flag (read from the player's `HelicopterSquadBotModule` in the FSM) makes the squad reach `HelicopterApproachState` and issue `Attack` orders — helis then take off and fly.
- **Autotest gotcha that cost several runs:** a heli issued `Attack` on an UN-attackable target (the enemy `supplyroute` is `NoAutoTarget` and matches no weapon's `ValidTargets`) never takes off — the attack activity no-ops and the heli stays grounded. The squad target-picker also fixates on the SR over a nearer tank. A deterministic heli-movement test needs a REAL attackable target (t90: Vehicle+Ground → Hellfire+30mm) and NO enemy SR to hijack targeting. Use `TestHarness.AssertWithin` (polls + exits on first movement) rather than a fixed `AfterDelay` — the latter left games running for minutes as orphaned processes when a run was interrupted.
## 2026-07-21 — Out-of-ammo evac is engine-level and invisible to bot modules; only LayeredDefence guards it

Found while triaging the "evac units re-ordered onto attacks" playtest bug (`2ed2c0ac`, plan `WORKSPACE/plans/260721_playtest_bugs_triage.md`).
- **Evac is a unit-level `AmmoPool` behaviour, not an AI decision.** `AmmoPool.AutoRearmIfAllEmpty` `case Evacuate` → `RotateToEdge` (`engine/OpenRA.Mods.Common/Traits/AmmoPool.cs:197-205`), fired from `INotifyAttack`/`INotifyBecomingIdle` (`:247-254`); WW3MOD vehicles opt in via `InitialResupplyBehaviorAI: Evacuate` (`mods/ww3mod/rules/ingame/vehicles.yaml:514-515`). The granted `evacuating` condition is **cosmetic only** (selection pip) — no bot module reads it, and the evac path never Commits the unit to `PoiGoalGuard.Ledger`.
- **Therefore an evacuating unit is "free" to any module that lacks an ammo filter.** `PoiOffensiveBotModule.IsEligibleCombatUnit` (`PoiOffensiveBotModule.cs:403-412`) has none → recruits empty units onto axes, overwriting `RotateToEdge`. `LayeredDefenceBotModule` is the **only** module that guards it: `SkipOutOfAmmoUnits` (default `true`, `:102`) + `IsOutOfAmmo` = all AmmoPools at 0 (`:465-471`), applied at `:273`. Reusable pattern: any module that pulls units by proximity/idle needs this guard or a shared evac reservation.

## 2026-07-21 — AI helicopters are permanently benched with no HPAD: the squad path has its own rearm-full gate

Found while triaging "helis fly to a corner and idle" (`2ed2c0ac`).
- **The documented `SkipRearmBuildingCheck` bypass only covers PRODUCTION.** The attack path has an independent gate: `HelicopterSquadBotModule.IsReadyForMission` (`engine/OpenRA.Mods.Common/Traits/BotModules/HelicopterSquadBotModule.cs:399-408`) requires **every AmmoPool `HasFullAmmo`** for any heli that has `AmmoPool`+`Rearmable`. Attack helis' `ReloadAmmoPool RequiresCondition: unit.docked && !airborne` (e.g. `mods/ww3mod/rules/ingame/aircraft-russia.yaml:178`) + `Rearmable{ RearmActors: hpad }` mean they can only refill at an HPAD — and the mod builds none. First shot ⇒ never full again ⇒ `IsReadyForMission` false forever ⇒ no squad ever forms ⇒ the `HelicopterStates` FSM never runs. Recruitment (by trait `AIHelicopterRole`, `:146`) works fine; the *readiness* gate is the block.
- **Corner-idle is arrival logic, not RA idle-return.** `ProductionFromMapEdge` gives aircraft `hasRallyPoint ? rp.Path : {self.Location}` (`ProductionFromMapEdge.cs:89,173-175`); the SR `RallyPoint` has no default Path (`structures.yaml:272-274`) so helis fly to the SR/edge cell and stop. `Aircraft.IdleBehavior` defaults `None` (`Air/Aircraft.cs:27`), so no return-to-base residue is involved.

## 2026-07-21 — MountedTransport is dormant until frontline contact, and never carries TECN (capture is fully decoupled)

Found while triaging "TECN walks to captures; mounting never observed" (`2ed2c0ac`).
- **`PickDropOffCell` returns null with no frontline**, so the whole module no-ops pre-contact: `MountedTransportBotModule.cs:313-314, 373-380` depend on `InfluenceMap.GetFrontline`, which marks only cells with **both** friendly AND enemy influence (`InfluenceMap.cs:170-174` → `DeriveFrontline` `:248-256`). Early game has no such cell → no mounting ever happens in the window players watch. Idle carriers (bradley/m113/bmp2, produced per `ai-america.yaml:27-28`, excluded from offense `ai.yaml:187` + defence `:341`) then pile up at the SR — a direct contributor to the "vehicles massing at the SR" complaint.
- **TECN is not a passenger and capture never requests a ride.** `PassengerTypes` (`ai.yaml:366`) omits `tecn*`; `CaptureCoordinatorBotModule` issues `CaptureActor` + on-foot escort `AttackMove` (`CaptureCoordinatorBotModule.cs:514, 627-643`) with **zero** call into MountedTransport, whose destination is a frontline gap, not a capture target. "Technicians riding first" is therefore unimplemented, not merely mis-tuned — it needs a capture-aware transport path.

## 2026-07-21 — The tournament ladder measures `startingunits: none`, not the Motorized regime players use

Found while folding the Motorized directive into early-game recon (`2ed2c0ac`).
- **`startingunits` is a lobby dropdown** (`SpawnStartingUnits.cs:23-53`, key `:51`, default `"none"` `:25`); values `none/squad/platoon/motorized/air` defined per-faction in `mods/ww3mod/rules/world.yaml:364-436`. **Motorized** (`:404-419`) ships abrams/bradley/humvee (America) or t90/bmp2/bmp2 (Russia) + infantry, but **no dedicated SAM** — its only AA is the humvee/bmp2 autocannon.
- **All tournament scenarios use the default `none`** (bots start with only two hand-placed `supplyroute`; `WORKSPACE/ai-bench/LADDER.md:448-451`, no `StartingUnitsClass` on bot PlayerReferences). Optimising for Motorized ⇒ scenario change ⇒ **re-BASELINE** (S1/S2 bars) before trusting Motorized tuning; the item-b AA-share floor in particular must be tuned against Motorized's built-in AA, not the `none` regime.

Recorded live from the project owner while spectating an Experimental-vs-Experimental match. This is
**north-star design intent**, not a code finding — promoted into the standing design doc
[`DOCS/design/ai-realism.md`](../DOCS/design/ai-realism.md) → "Long-term vision (user-authored, 2026-07-20)".
Logged here so a curation pass can link the two. Three themes:

1. **Territorial-control map layer (the centerpiece).** A fog-respecting map layer classifying territory
   **safe / grayzone / enemy**; own-half assumed safe at start (2-player prior) until proven otherwise;
   updates only from real intel (no seeing through fog). Safe = "capture + set up defensive positions".
   Runs the whole game: enemy retreats/dies → area safer → advance there → **always push where the enemy
   is comparatively weak**; a balance-of-power reading of the same layer drives repositioning + reinforcing
   weak spots. End state: forces **spread along the ENTIRE line of combat** (most important sectors first,
   eventually some soldiers along the whole front), front **steps forward wherever it is safe**. A held,
   advancing line — not a death-ball.
2. **Early-game economy sensibilities.** No supply trucks while all units have full ammo (a start-bought
   truck just sits as a target; simple rule now, foresight later). AA proportionate to the real air threat
   (a couple of AA infantry already deter helicopters; multiple SHORAD/Tunguska at start = overbuild).
   Early urgency to spread out + capture fast in **small groups/packets** rather than one armada at the SR.
3. **Mounted infantry doctrine.** Technicians ride vehicles to distant captures (first priority); later,
   soldiers ride with context-appropriate dismount (far from enemy when just reaching the front to
   hold/defend, closer for assault transport) — always weighing that **one missile can kill vehicle +
   squad together**.

Relevant engine systems for eventual translation (per the realism doc's mapping): `InfluenceMap` /
`PoiMap` (territory + weak-point reading), `PoiOffensiveBotModule` (advance where weak), the garrison
module (hold captured safe ground), `MountedTransportBotModule` (mounted doctrine), the SR call-in budget
(early-eco discipline). No code written this session — vision capture only.

## 2026-07-20 — An SR Pressure offensive axis does NOT starve the TECN capture layer (offense/capture pool independence, empirical)

Found during SR-contestation cycle 1 (`runs/260720_sr_contestation_cycle1_n10.md`). With the new
`PoiOffensiveBotModule.SrPressureScoreMultiplier: 260` on `@experimental`, the enemy Supply Route
**safe-threat** Pressure score reaches ~**57M** (observed axis line: `action=Pressure score=57408000
units=8`), which **outranks neutral oilbs** and pulls a full 8-unit offensive axis mid-game (first tick
~1600–2150, minutes ~5–7). Despite that, the **S1 economy result was byte-for-byte the reference tier**
(capture 8/10, conditional gross median $6,457, win 10–0, same two $0 seeds). Non-obvious takeaway: the
offensive-axis layer (`PoiOffensiveBotModule`, combat units, `AttackMove`) and the capture layer
(`CaptureCoordinatorBotModule`, TECNs) draw from the **shared `PoiGoalGuard` ledger free pool
independently** — pulling combat units onto an SR Pressure axis does **not** consume the TECN pool, so
income capture is unaffected even when the SR wins a high-scoring axis. Useful prior for any future cycle
that boosts an offensive axis score: expect **no** first-order S1 capture regression from offense re-ranking
alone; a capture regression would instead point at the TECN production pipeline. (Also: at `260` the SR can
top the ranking at safe threat — a heavier multiplier risks over-prioritising it; the `ThreatHostileMultiplier`
gate ×260 ⇒ ~4.25M still keeps the AI off a garrisoned SR. `PoiOffensiveBotModule.cs` RescaleSrPressure +
call site ~:196.)

## 2026-07-20 — The TECN-floor request dies at a *busy Infantry queue*, and the M-2 vs every-scan placement is identical at floor 1

Found during the capture-throughput recon (`WORKSPACE/plans/260720_capture_throughput_cycle.md`).
Two non-obvious code facts about the `IBotRequestUnitProduction` floor path:

1. **Request-death point (the m7-class "requested 82× never converted"):** a popped build request
   only starts production on a queue where `!q.AllQueued().Any()` — i.e. a **free** queue —
   `UnitBuilderBotModule.BuildUnit(name)` (`:155`). With one busy Infantry queue the popped request
   finds no free queue and is **silently dropped** (the pop at `:90-91` removes it regardless), so
   `RequestedProductionCount` reads 0 again and the floor re-requests next scan. N re-requests = N
   popped-and-dropped cycles against a saturated queue — a **production-starvation** tail (with
   `tecn-killed=0`, the unit is never produced), NOT a survival/dispatch problem. Re-requesting faster
   cannot fix it; the lever is queue reservation / a dedicated capturer production path / lower
   competing infantry share.

2. **Floor placement is a no-op at floor 1:** moving `MaintainTecnFloor` off the M-2
   (`idleCapturers==0`) gate (`CaptureCoordinatorBotModule.cs:271-272`) to run every scan is
   **byte-identical at `TecnFloor: 1`** — every-scan fires only when `alive+pending<1` ⇒ `alive=0` ⇒ no
   capturers ⇒ `idleCapturers=0` ⇒ M-2 already reached. The placement only differs at `TecnFloor ≥ 2`.
   Practical consequence: the code move is **safe for a frozen `@stable` that stays at floor 1** with no
   new gate field — a rare case where a shared-class behaviour change needs no default-off bool.

Corollary for diagnosis: a $0 capture run that already holds ≥1 alive TECN is a **conversion stall**,
not an availability gap — the floor is satisfied, so neither placement nor a floor bump is guaranteed to
flip it; only redundancy (floor 2 = a second independent attempt) or a screen (escort reservation) does.

Code refs: `UnitBuilderBotModule.cs:85-96,142-165`, `CaptureCoordinatorBotModule.cs:245-274,380-405`.

## 2026-07-20 — Benchmark run-to-run variance is one unseeded line; the fixed seed already flows everywhere *except* `LocalRandom`

> **[promoted → architecture.md "Bot decisions ARE seed-reproducible"]** (curation 2026-07-20). Verified `World.cs:213-224` (LocalRandom now seeded from RandomSeed via the LCG transform, guarded on `!= 0`). Pre-fix recon subsumed by the verify entry below.

Found during the seeded-determinism recon (`WORKSPACE/plans/260720_seeded_determinism.md`).
The S1 4/10-vs-9/10 wobble traces to a single line: `World.cs:214` builds
`LocalRandom = new MersenneTwister()` **unseeded**, which chains to
`this(Environment.TickCount)` (`MersenneTwister.cs:25-26`) — a wall-clock seed. ~40 bot-decision
sites read `world.LocalRandom` (scan/reeval countdowns, unit call-in picks, squad splits, rally
cells — see plan §1b), so bot behavior differs every launch even with an identical seed.

The non-obvious part: the deterministic seed **is already plumbed end-to-end**. The tournament
runner passes `Test.RandomSeed=$((i*1000+17))` (`run-tournament.sh:282,298`) →
`TestMode.RandomSeedOverride` (`TestMode.cs:96-98`) → `Server.cs:310,332` →
`GlobalSettings.RandomSeed`, which already seeds `SharedRandom` (`World.cs:213`) and `playerRandom`
(`World.cs:237`). Combat RNG (inaccuracy/miss/burst) also rides `SharedRandom`
(`Armament.cs:513,536,567,654`), so it is already deterministic. Only `LocalRandom` is the gap —
seeding it (decorrelated from the shared seed) is the whole fix; no shell/YAML/env-var work needed.
Corollary: the `BotVsBotMatchWatcher` header documents a `"seed"` verdict field (`:21`) that
`SerializeVerdict` (`:287-356`) never actually emits.

This makes the `architecture.md:291-293` note ("Bot decisions are not seed-reproducible") a
*current-state* fact that a ~2-line change would invert — update that note if/when the seeding lands.
**RESOLVED** (main @ `2d3c8fe0`): the seeding landed and verified FULL determinism — see next entry.

## 2026-07-20 — Seeding `LocalRandom` gives FULL replay determinism; async pathfinding did NOT leak nondeterminism

> **[promoted → architecture.md "Bot decisions ARE seed-reproducible"]** (curation 2026-07-20). Verified `World.cs:213-224`; added the async-pathfinding-is-deterministic clause to the doc. The "per-seed capture is near-binary Bernoulli" observation left here as benchmark-methodology (covered in reference by "one seed is one battlefield").

Verify of the seeding fix (`World.cs:214`, main @ `2d3c8fe0`;
`WORKSPACE/ai-bench/runs/260720_seeded_determinism_verify.md`). Two hidden Mode-B matches at the
same seed came back **byte-identical** — not just the final verdict, but the watcher's tick-by-tick
score log (60 logged intervals over 7500 ticks) matched line-for-line. The plan's prime suspect for
residual nondeterminism (async pathfinding after the seeding fix, §5.3) **did not materialize**:
seeding the single unseeded `LocalRandom` was sufficient for full reproduction. So OpenRA's
off-thread pathfinding applies its results deterministically on the sim thread even with WW3MOD's
modified movement — no extra work needed for benchmark determinism.

Second, non-obvious for benchmark design: **in-window derrick capture is a near-binary per-seed
outcome, not a gradual dial.** Seed 1017 → *both* bots `capture_income_gross=0` (no capture landed
in 7500t); seed 9017 → experimental `gross=10917`. That is the whole 4/10-vs-9/10 variance — each
seed either lands the early capture or it doesn't. Implication: a stable capture-rate mean needs
enough seeds to sample that Bernoulli-ish distribution; a single seed tells you nothing about the
rate, only that *this* battlefield did/didn't capture.

Transform (decorrelates `LocalRandom` from `SharedRandom` while staying a pure function of the seed):
`(int)(RandomSeed*6364136223846793005 + 1442695040888963407)`, guarded on `RandomSeed != 0` so
normal gameplay (seed = `DateTime.Now.ToBinary()`) still varies per launch. Verdict now records the
seed (`verdict_version` 5).

## 2026-07-20 — `UnitBuilderBotModule` UnitsToBuild weight is a share *ceiling*, NOT a priority

> **[promoted → architecture.md "AI production: `UnitsToBuild` weights are share ceilings"]** (curation 2026-07-20). Verified `UnitBuilderBotModule.cs:25,49,125-136,167-195` (shuffle + `count*100 < weight*total` at :190; idle-cap uniform-random path; single-name overload bypass).

Found during the TECN-availability cycle-2 recon (`WORKSPACE/plans/260720_tecn_availability_cycle2.md`).
A common misread: a big weight like `tecn.*: 500` (`ai-{america,russia}.yaml:8`) makes the AI
*prioritize* that unit. It does not. `ChooseUnitToBuild` (`UnitBuilderBotModule.cs:177-195`)
**shuffles** `UnitsToBuild` and returns the **first** entry passing `count*100 < weight*total`
(`:190`) — i.e. `count/total < weight/100`, so `weight/100` is a per-type share *ceiling as a
percent*. Any weight ≥100 (100%) can never bind, so the unit is merely "always eligible,"
selected **uniformly** among eligibles by the shuffle. Weight 500 = 120 = identical odds early
game. Below the roster average weight a unit gets *throttled*; above it, no boost. Separately,
while `idleBaseUnits < IdleBaseUnitsMaximum` (12, `:25`) the module ignores weights entirely and
picks a **uniform random** buildable (`ChooseRandomUnitToBuild :167-175`), discarding picks not
in `UnitsToBuild`. Net: there is **no YAML field for a production floor/priority** — `UnitsToBuild`
is a ceiling, `UnitLimits` is a ceiling, `UnitDelays` is a delay. A guaranteed keep-N-ready
requires code (the `IBotRequestUnitProduction` queue, which is processed first each cycle and
bypasses both the share test and `UnitLimits` — `:87-92,142-165`). This is why cycle-1's TECN
starve cannot be tuned away in `ai-*.yaml`.

Code refs: `UnitBuilderBotModule.cs:78-97,112,125-136,167-195`, `TraitsInterfaces.cs:727-732`.

## 2026-07-20 — `IBotRequestUnitProduction` demand queue is a working code-level production floor (verified live, S1 cycle 2)

> **[promoted → architecture.md "AI production" (the code-level floor half)]** (curation 2026-07-20). Verified queue mechanics `UnitBuilderBotModule.cs:87-92,99-107,142-165` (pop-one-before-lottery, drop-on-failure at :90-91, `RequestedProductionCount`), reference impls `CaptureCoordinatorBotModule.cs:389-402` (`MaintainTecnFloor`, `alive+pending<floor`) and `AdaptiveProductionBotModule.cs:64,159`. Run-specific results (commit hashes, 4/10→8/10, side-split) kept here, not copied to reference.

The cycle-2 recon's proposed fix — request production through the shared UnitBuilder's queue to
bypass the share-ceiling — was **implemented and verified**: a default-off `TecnFloor` on
`CaptureCoordinatorBotModule` (merged `c6a71c14`) lifted S1 in-window capture **4/10 → 8/10** and
cut matches-fielding-zero-TECNs **5/10 → 0/10**. Confirmed mechanics from the live run:

- `bot.QueueOrder` is **not** how you pull a *unit type* on demand — you call
  `up.RequestUnitProduction(bot, name)` on each `player.PlayerActor.TraitsImplementing<IBotRequestUnitProduction>()`.
  In WW3MOD only `UnitBuilderBotModule` implements the sink; its `BotTick` pops **one** queued
  request per `FeedbackTime=30`-tick cycle **before** the lottery (`:87-92`) and routes it through
  the single-name `BuildUnit` overload (`:142-165`) that skips both `UnitsToBuild` and `UnitLimits`.
- **Drop-on-failure is real** (`:91` removes the entry whether or not the queue was free), so a
  floor must **re-request each scan** and subtract already-queued via `RequestedProductionCount`
  to avoid piling duplicates. `alive(pool) + pending(requested) < floor` is the correct gate.
- **Faction-correct build type with no hardcoding:** intersect the module's `CapturingActorTypes`
  with the player's Infantry-queue `BuildableItems()` names — the generic `~disabled` `tecn` and
  any wrong-faction variant fall out because they aren't buildable. Resolve lazily and **don't
  cache a null** (queues/prereqs may be cold on the first scan).
- **Gotcha found by running it:** gating the request at the M-2 (`idleCapturers==0`) branch means
  the floor *stops re-firing* on a bot that keeps an idle lottery-built capturer around (M-2 never
  reached). Observed as a perfect side-split: america-side fired the floor once then went quiet
  (still captured, ~1 derrick), russia-side fired 60–82× (multiple derricks). Fine for `floor=1`,
  but a stricter floor should check `alive+pending < floor` every scan, not only at M-2.

Code refs: `CaptureCoordinatorBotModule.cs` (`MaintainTecnFloor`/`ResolveTecnBuildType`/`CaptureTargetExists`),
`UnitBuilderBotModule.cs:87-92,99-107,142-165`, `AdaptiveProductionBotModule.cs:62-65,153-162` (reference impl).

## 2026-07-20 — `CVec.Length` / `CPos` subtraction is EUCLIDEAN, not Chebyshev — compute cell "grid distance" by hand

> **[promoted → conventions.md "Engine behaviors that surprise"]** (curation 2026-07-20). Verified `CVec.cs:49-50` (`Length => Exts.ISqrt(X*X + Y*Y)`).

The dispersion design sketch (`260720_dispersion_cycle_design.md` §2b/§3b) labelled
`(centroid - axis.TargetCell).Length` as "Chebyshev". It is **not**:
`engine/OpenRA.Game/CVec.cs:49-50` defines `Length => Exts.ISqrt(LengthSquared)` with
`LengthSquared => X*X + Y*Y` — i.e. rounded **Euclidean** length. Using it for the
"cells from target" gate would make a diagonal approach read ~1.4× farther than the
grid distance a watcher sees on the minimap.

For true chessboard distance in cells, `max(|dx|, |dy|)`. The dispersion implementation
adds pure helpers on `PoiOffenseMath` (`Chebyshev`, `CellCentroid`, `MaxChebyshev`) —
engine-free `(int X, int Y)` tuples, unit-tested in `PoiOffenseTest` — used for both the
assault-radius gate and the `clumpRadius` telemetry. Refs:
`engine/OpenRA.Mods.Common/Traits/BotModules/PoiOffensiveBotModule.cs`
(`PoiOffenseMath.Chebyshev/CellCentroid/MaxChebyshev`, `CommitAndOrder`).

## 2026-07-20 — Dispersion doctrine needs a kill-switch or it silently mutates the frozen `@stable` control

> **[promoted → architecture.md "Adding a behavioural field to a trait shared by both bot profiles"]** (curation 2026-07-20). Verified `PoiOffensiveBotModule.cs:87` (`CohesionSwitchEnabled=false`), `:96` (`ApproachCohesion=Spread` non-baseline default), `:424` (dispersion gated on the switch); `ai.yaml:41-46` (`@experimental`/`@stable` share the trait). Generalized into the shared-trait-default rule.

`PoiOffensiveBotModule` is instantiated by BOTH `ModularBot@experimental` (gate
`enable-ai-experimental`) and `ModularBot@stable` (gate `enable-ai-stable`, the frozen
validated snapshot — `mods/ww3mod/rules/ai/ai.yaml:44-46, 643`). New Info fields with
non-baseline **code defaults** (e.g. `ApproachCohesion=Spread`) therefore leak into
`@stable` even when its YAML block is left untouched — changing a benchmark control.
The design (§2b) anticipated this with `CohesionSwitchEnabled`; shipped it **default
`false`**, flipped `true` only on `@experimental`. Rule of thumb: any behavioural Info
field added to a trait shared by an experimental AND a frozen bot profile must default
to the frozen behaviour and be opted-in per-profile via YAML.

## 2026-07-20 — Capture escorts are dispatched but NEVER committed to the goal-guard ledger

> **[rejected: incidental bug in the experimental AI (escort desync), tied to an in-flight WORKSPACE plan and slated to be fixed by the mission model — belongs in bugs/discovered.md, not reference. The experimental goal-guard/PoiOffense layer is not documented in DOCS/reference at all. Code-verified against current source: `DispatchEscort` at `CaptureCoordinatorBotModule.cs:627-643` issues the escort AttackMove and adds to the per-tick set but never calls `Ledger.Commit`; only the capturer is committed at `:516` in `IssueCaptureOrder` (line numbers shifted from the entry's :486-502/:395-396).]** (curation 2026-07-20).

Found during the mission-abstraction costing recon (`WORKSPACE/plans/260720_mission_abstraction_costing.md`).
`CaptureCoordinatorBotModule.DispatchEscort` (`CaptureCoordinatorBotModule.cs:486-502`) issues an
`AttackMove` to the escort units and adds them to a **per-tick** `escortsRecruitedThisTick` set
(`:497-498`), but it never calls `goalGuard.Ledger.Commit`. Only the TECN itself is committed
(`IssueCaptureOrder :395-396`). Consequence: ~100 ticks later `PoiOffensiveBotModule.BuildFreePool`
(`:320-330`) sees the escorts as uncommitted and can pull them onto an attack axis, abandoning the
escort mid-approach. This is an escort *desync* distinct from — and compounding — the known F-4
bug (escort `AttackMove`s the derrick cell, not the capturer; `260720_capture_reliability_cycle1.md:71-82`).
Implication: escorts are a one-shot nudge, not a durable sub-force; the mission model fixes this by
committing the escort sub-force under `escort:<captureId>`.

Code refs: `CaptureCoordinatorBotModule.cs:486-502`, `CaptureCoordinatorBotModule.cs:395-396`, `PoiOffensiveBotModule.cs:320-330`.

## 2026-07-20 — MEASURED: 88% of experimental capture scans see ZERO TECNs (availability, not survival, gates S1)

> **[rejected: run-specific N=10 measurement on one scenario — belongs in runs/, not reference (results decay, and the run doc `260720_capture_reliability_cycle1_n10.md` already holds it). The durable takeaway (availability, not survival, is the binding constraint; the TECN pool is a consumable) is already in reference via the promoted "TECN is consumed on successful capture" entry → game-model.md.]** (curation 2026-07-20).

Instrumented N=10 confirmation of the availability hypothesis below. With the M-2
`no-idle-capturers` marker (`CaptureCoordinatorBotModule.cs`, the `idleCapturers.Length==0`
branch) preserved per-match, the pooled `total-tecns` distribution over 994 capture scans on
`tournament-s1-eco-river-zeta` (hidden Mode-B, 5min) was: **total-tecns=0 → 875 scans (88%)**,
=1 → 94, =2 → 17, =3 → 8. **5 of 10 matches had zero TECNs for the entire match and issued 0
capture orders.** The `tecn-killed` (M-1) marker fired only twice, and both with
`committed=False objective=<none>` — i.e. the TECNs that died were *not* pursuing a derrick.
So the S1 ~40% capture rate is gated by **TECN production/delivery/availability**, NOT capturer
survival on the approach and NOT coordinator logic (which fires correctly whenever a free TECN
exists — all 6 captures issued at ticks 680–1477). Raising `DefaultCommitmentTicks` 300→600 and
adding an `INotifyKilled` scan-reset (cycle 1, branch `exp-capture-reliability`) left the rate
at 4/10 — confirming the binding constraint is upstream of the capture loop. Next lever:
TECN call-in/build cadence, `ConsumedByCapture` pool drain, and a "keep N TECNs ready" floor
(UnitLimit `tecn.*: 3` is a ceiling, not a floor).

Run: `WORKSPACE/ai-bench/runs/260720_capture_reliability_cycle1_n10.md`.
Code refs: `CaptureCoordinatorBotModule.cs` (M-2 branch), `tools/autotest/run-tournament.sh`
(per-match `debug.log` preservation), `ai-{america,russia}.yaml:8` (`tecn.*: 500` builder weight).

## 2026-07-20 — TECN is consumed on successful capture (`ConsumedByCapture: true`)

> **[promoted → game-model.md — "Capturing neutral buildings consumes the technician"]** (curation 2026-07-20). Verified `infantry.yaml:897,903`.

`^CapturesNeutralBuildings` (infantry.yaml:897–905) sets `ConsumedByCapture: true`
(infantry.yaml:903). Every successful neutral-building capture removes the TECN from
the game. This means the AI's TECN pool shrinks by one on every SUCCESS as well as
every combat death. With `UnitLimits: tecn.america/russia: 3` (ai-america.yaml:37,
ai-russia.yaml:37), capturing 2–3 derricks can exhaust the live pool entirely, after
which no further captures are possible until production replaces them. Key implication
for capture-reliability design: the TECN pool is a **consumable**, not a persistent
resource — availability is the binding constraint, not coordinator logic.

Code refs: `infantry.yaml:903`, `ai-america.yaml:37`, `CaptureCoordinatorBotModule.cs:432`.

## 2026-07-20 — `PoiGoalGuard` commitment TTL (300 ticks) is borderline short for Speed-25 infantry on 8-cell routes

> **[rejected: in-flight tuning proposal tied to a WORKSPACE plan; not a timeless mechanic — the TTL value is a design knob, not reference material]** (curation 2026-07-20).

`DefaultCommitmentTicks: 300` (ai.yaml:122; PoiGoalGuard.cs:129). At `Speed: 25`
(infantry.yaml:37, `^Infantry` template inherited by `^TECN` via the chain
`^ArmedCivilian → ^CivInfantry → ^Infantry`), one cell takes `⌈1024 / 25⌉ ≈ 41` ticks.
An 8-cell edge-to-SR-to-target route takes ~330 ticks, exceeding the TTL. When the TTL
expires, `Prune()` (PoiGoalGuard.cs:104–116) drops the commitment and marks the unit
as available again. If the unit has an `IsIdle` flicker mid-walk, the coordinator can
re-issue a new capture order, aborting the in-progress approach. Fix: raise to 600
(covers ~14-cell walk). River Zeta derricks are ~3–4 cells from SR (baseline §failures);
combined edge-to-SR walk ~3–5 cells; total ~6–8 cells ≈ 250–330 ticks — borderline
at 300, safe at 600.

Code refs: `ai.yaml:122`, `PoiGoalGuard.cs:104`, `infantry.yaml:37`.

## 2026-07-20 — `CohesionMoveModifier` is a cover-aware intent system, NOT a simple offset system; and it DOES fire for bot orders

> **[rejected: correction already applied — architecture.md:161 already carries the four-strategy cover-aware description and the bot-order-routing note]** (curation 2026-07-20).

`architecture.md` description is **wrong**: "offsets group move targets based on CohesionMode
(Tight/Loose/Spread). Preserves relative formation shape with capped offsets." The real
implementation (`engine/OpenRA.Mods.Common/Traits/CohesionMoveModifier.cs`) is an
**intent-aware cover-placement system** that classifies the target cell against
`Map.DensityLayer` and dispatches to one of four formation strategies: `Open` (box
layout — fires on open terrain, the typical AI case), `SpreadInside` (into cover),
`EdgeLine` (along a cover gradient), `Approach` (boundary-anchored line for far clicks).
CohesionMode (`Tight`/`Loose`/`Spread`) controls ONLY spacing (col/row WDist), NOT
which strategy fires. For AI AttackMoves to open-terrain objectives, `Intent.Open`
almost always fires.

**Bot-order routing confirmed**: `PoiOffensiveBotModule` issues grouped AttackMove with
`groupedActors:` set. `Order.cs:400-401` serializes GroupedActors (flag `Grouped`).
`UnitOrders.cs:397-413` runs the `IModifyGroupOrder` pipeline whenever
`order.GroupedActors != null`. So CohesionMoveModifier fires for bot-issued grouped
orders exactly as for player-issued ones. AI units default to `CohesionMode.Loose`
(`AutoTarget.cs:120: InitialCohesionAI = CohesionMode.Loose`), giving 2-cell column
and 1.5-cell row spacing in the Open box — tight enough to read as a death-ball.

**`SetCohesion` order is bot-callable** (`AutoTarget.cs:434-435`):
`new Order("SetCohesion", unit, false) { ExtraData = (uint)mode }`. The bot can switch
per-unit cohesion mode before issuing a grouped AttackMove. SetCohesion orders queue
before the AttackMove and drain first (FIFO), so the modifier reads the updated mode.
This is the key mechanism for the Dispersion Cycle (§2,
`WORKSPACE/plans/260720_dispersion_cycle_design.md`).

## 2026-07-20 — Tournament scenario bot assignment lives in `map.yaml` Players, NOT in `tournament.yaml` `Matchup`

> **[rejected: already documented in-tree — the `tournament.yaml` files comment `Matchup` as "informational", and WORKSPACE/ai/archive/tournament_swap_guide.md covers swaps; harness tooling, not engine/gameplay reference]** (curation 2026-07-20). Confirmed nothing but `TournamentConfig.cs:70-71` reads the field.
- Building the S1 mirror (`tournament-s1-eco-river-zeta-mirror`) required swapping
  which bot plays which spawn. The `tournament-eco-5min.yaml` (and every scenario's
  `tournament.yaml`) has a `Matchup: { P1Bot, P2Bot }` block that *looks* like the
  assignment — but it is **informational only**: `TournamentConfig.LoadFromFile`
  parses it into `config.Matchup` and **nothing in the engine ever reads that field**
  (grep: the only references are the load site + the class def). The real assignment
  is the `Bot:` key on each `PlayerReference@…` in `map.yaml` Players. So a mirror =
  copy the folder, swap the two `Bot:` lines in `map.yaml`; leave `tournament.yaml`
  byte-identical. (The existing combat-stub mirror swaps *factions* instead, because
  S2/S3 control for faction bias; S1 controls for derrick *distance*, so it swaps the
  bot on each fixed spawn.)

## 2026-07-20 — Scorer `capture_income` term repointed net→gross; `verdict_version` 3→4 flags an emitted field's changed *meaning* (not a schema add)

> **[rejected: in-flight scorer changelog — describes a specific code change + version bump, not a durable mechanic; belongs with the commit/WORKSPACE, not reference]** (curation 2026-07-20).
- `WeightedComponentMatchScorer.capture_income` (which feeds `TimeOrSrCaptureWinRule`,
  i.e. match *outcomes*) previously read net `PlayerResources.Earned`. In the
  SR-budget economy net Earned only rises on a net-positive periodic tick, so a held
  $50 derrick whose gross income doesn't overcome upkeep contributed **0** — outcomes
  were blind to captured income (the same defect the S1 metric fixed at v3). It now
  reads the gross integral via `state.GrossCaptureIncomeFor(player)` (the same value
  emitted as `capture_income_gross`), so the scorer reads `MatchTrackingState`, not
  just player traits. **Non-obvious versioning rule applied:** no JSON field was
  added or removed, but the *value/meaning* of an already-emitted field
  (`score_components.capture_income`) changed, so `verdict_version` was bumped 3→4.
  Bump on emitted-field-meaning change, not only on field add/remove — a downstream
  parser keyed on `verdict_version` must know the economy column now means gross.
- The weighting math was factored to a pure `WeightedComponentScoring.Compute` so it's
  unit-testable without a World (same pattern as `PoiScoring`/`GoalGuardLedger`):
  `WeightedComponentScoringTest` pins `capture_income == gross × weight`. This
  supersedes the 2026-07-19 note below that the scorer "reads … `PlayerResources.Earned`".

## 2026-07-19 — Tournament matches are NOT reproducible per seed: the AI ignores the seed via unseeded `world.LocalRandom`

> **[promoted → architecture.md — "Bot decisions are not seed-reproducible"]** (curation 2026-07-20). Verified `World.cs:213-214`.
- Verified empirically: two `BotVsBotMatchWatcher` runs of the SAME scenario
  (`tournament-arena-diagonal-2p`/`tournament-smoke.yaml`) with the SAME
  `Test.RandomSeed=1017` produced **different** winners and scores. Divergence
  starts within the first 125 ticks from an identical initial state (same SR
  positions, same players). `duration_ticks` (750, the fixed time limit) is the
  only thing that matches.
- **Root cause:** `World.cs:213` seeds `SharedRandom` from `RandomSeed`
  (deterministic, network-synced), but `World.cs:214` creates
  `LocalRandom = new MersenneTwister()` **unseeded**. The bot modules make
  *decisions* off `world.LocalRandom` — `UnitBuilderBotModule.cs:173/188` picks
  which unit to call in; LayeredDefence / HelicopterSquad / BaseBuilder /
  Minelayer / SupportPower all use it for scan timing and target/ location
  choice. Unseeded → different picks every run → army composition (and thus
  `army_value`/scores) diverges immediately.
- **Consequence:** the `Test.RandomSeed` "reproducible per seed" claim
  (PITFALLS §15) is only true for the *synced* sim, NOT for AI behavior. For a
  benchmark substrate this means a fixed seed gives you a *sample*, not a
  *reproduction*. Sample-over-N stays statistically valid; single-match
  reproduction/debugging does not work.
- **Fix (separable, not done here):** under `TestMode.RandomSeedOverride`, seed
  `LocalRandom` too (e.g. `new MersenneTwister(RandomSeed ^ constant)`), or route
  AI decision randomness through `SharedRandom`. Until then, do not expect
  bit-identical tournament verdicts across runs.
- **Corollary for `OPENRA_WINDOW_HIDDEN`:** the hidden-window flag does NOT
  change sim results — it only removes SDL rendering, which is decoupled from the
  lockstep sim and cannot touch `LocalRandom`. The hidden-vs-windowed divergence
  observed during flag verification is entirely this pre-existing AI
  nondeterminism, not the flag. (Flag verification: hidden run created no visible
  window, stole no focus, completed, and wrote the v2 verdict JSON.)

## 2026-07-19 — Bot-vs-bot benchmark substrate: harness already exists but is macOS-gated; no headless mode; a hidden-window flag is the crux

> **[rejected: in-flight research findings — full report already lives in WORKSPACE/plans/260719_ai_benchmark_substrate_findings.md; status/effort notes, not durable reference]** (curation 2026-07-20).
- Researching a foundation for an **autonomous AI benchmark** (many unsupervised bot-vs-bot games, metrics from logs) surfaced that most of it is **already built**: `tools/autotest/run-tournament.sh` + `loop-tournament.sh` + `aggregate-tournament.sh` run N seeded matches, aggregate to CSV/JSON, and drive a milestone loop (winrate/budget stop-conditions). Engine side: `BotVsBotMatchWatcher` (world trait) writes a per-match JSON verdict (winner, win_reason, duration, per-player score_total + components); `WeightedComponentMatchScorer` already reads live `PlayerStatistics.ArmyValue/KillsCost` + `PlayerResources.Earned` (the `tournament.yaml` "only army_value" note is stale). 7 tournament scenarios exist incl. v2-vs-normal.
- **Two blockers for the user's Windows goal:** (1) the whole harness is `.sh` + `uname` Darwin/Linux branches + `osascript` focus mitigation — **Windows is unhandled**; (2) **no headless mode** — only one `IPlatform` (`DefaultPlatform`), the SDL window is always shown (`Sdl2PlatformWindow.cs:227`, no `SDL_WINDOW_HIDDEN`), and on Windows it **steals focus** with no mitigation. The dedicated server can't substitute — it's order-relay only (`OpenRA.Server/Program.cs:100-109`, no `World`); bots tick client-side (`ModularBot.cs:86`).
- A true headless/null renderer was **explicitly rejected** (`WORKSPACE/ai/archive/PITFALLS.md §17`) as "days of work, risk of breaking determinism" — but that call was made for macOS where `osascript` already tamed focus. On Windows the calculus flips. **Cheapest fix: ~10-line `OPENRA_WINDOW_HIDDEN=1` env flag adding `SDL_WINDOW_HIDDEN` at window creation** — no-window + no-focus-theft in one stroke, keeps a real GL context (unlike a null platform).
- **Speed:** `GameSpeed` caps at 2×; real lever is `Test.SpeedMultiplier` (1–16, lowers `world.Timestep`), 4–6× practical with render on (renderer is the ceiling; ~30s fixed init dominates short matches). **Seeds:** `Test.RandomSeed` override makes matches reproducible per seed (`PITFALLS §15`); vary for a sample, fix to reproduce.
- **Riskiest unverified assumption:** that a hidden SDL window ticks the sim to completion on Windows with identical (deterministic) results. Retire with one bounded run after the flag lands.
- Full report + effort estimates: [`plans/260719_ai_benchmark_substrate_findings.md`](plans/260719_ai_benchmark_substrate_findings.md).

## 2026-07-19 — SUPPLYROUTE is NOT capturable today; the doc's "capture → Neutral" is a misread of OwnerLostAction

> **[promoted → supply-route.md (§Capture rewrite + engine-integration bullets) & game-model.md; drove on-sight fixes to both]** (curation 2026-07-20). Verified `structures.yaml:202-343` (no Capturable/CaptureManager), `OwnerLostAction.cs`, `ConquestVictoryConditions.cs:109` / `StrategicVictoryConditions.cs:152`.
- The game-model docs (`DOCS/reference/supply-route.md` §Capture, `game-model.md`) state an enemy SR can be captured by an engineer/technician and flips to Neutral. **This does not work in-game.** SUPPLYROUTE has **no `Capturable` and no `CaptureManager`** — not in its own block (`mods/ww3mod/rules/ingame/structures.yaml:202-343`), not in any template it inherits (`^ExistsInWorld`, `^SpriteActor`, `^SelectableBuilding` — all clean; `defaults.yaml:2-13, 772-775`), and not patched by any map/world/ai/campaign rules (checked). The Phase-2 AI worker's report was correct.
- **The doc conflates two unrelated mechanisms.** `OwnerLostAction: ChangeOwner → Neutral` (structures.yaml:227-229) does NOT fire on capture. `OwnerLostAction` implements `INotifyOwnerLost` (`engine/OpenRA.Mods.Common/Traits/OwnerLostAction.cs:20,42` — "when the actor's owner is **defeated**"), and `OnOwnerLost` is called **only** from `ConquestVictoryConditions.cs:109-110` and `StrategicVictoryConditions.cs:152-153`, both iterating the actors of a just-defeated player. So an SR goes Neutral **only when its owning player loses the game**, never via an engineer.
- **Capturer side is fully wired, target side is not.** TECN inherits `^CapturesNeutralBuildings` = `CaptureManager` + `Captures{CaptureTypes: building-neutral}` (`infantry.yaml:2164, 897-904`); soldiers get `^CapturesOccupiedBuildings` (`building-occupied`, 885-896). Capturable tech buildings (OILB/FCOM/BIO…) get the matching side via `^BasicBuilding → ^NeutralOrOccupiedCapturable` (`structures.yaml:2-10, 149-157`: `Capturable@neutral: building-neutral` + `Capturable@occupied: building-occupied`). **SUPPLYROUTE inherits none of that chain**, so there is no capture-type to intersect — TECN literally has nothing to enter/capture on an SR (neutral or enemy).
- **Verdicts:** (a) enemy SR → capturable → flips Neutral = **ABSENT**. (b) neutral SR → capturable by a player (gain a 2nd reinforcement lane) = **ABSENT** — the harness `NeutralSR` (`test-v2-poi-harness/map.yaml:173`) is a plain `supplyroute`/Neutral actor with no Capturable; its `rules.yaml` adds none.
- **Gap to match the stated design:** add `CaptureManager` + a `Capturable` to SUPPLYROUTE. Note two subtleties: (1) neutral-SR capture needs `Types: building-neutral`; enemy-SR needs `building-occupied`. (2) **Standard `Captures`/`Capturable` transfers to the CAPTURER, not to Neutral** — so the "capturer can never use it, it just goes Neutral" design cannot be done with vanilla capture traits alone; it needs a custom on-capture hook (or `OwnerLostAction`-style flip triggered by capture). The commented-out `CaptureNotification` at structures.yaml:216-217 is unrelated and wires nothing.
- No live test was needed — the YAML+C# reading is unambiguous (no Capturable anywhere on the actor). A run could only confirm the negative.

## 2026-07-20 — PoiMap enemy-SR score: three factors conspire to keep it last in offensive ranking

> **[rejected: in-flight design analysis tied to WORKSPACE/plans/260720_sr_contestation_cycle1.md — concrete map numbers + a proposed fix direction, not a stable mechanic]** (curation 2026-07-20).

Computed from `PoiMap.GetOffensiveTargets` (PoiMap.cs:279) + world.yaml PoiMap block (line 296):

**Enemy SR score formula:** `value × distFactor × threatFactor × ownershipMul × bias/100`
- `SupplyRouteDenyValue = 120` (world.yaml:305)
- `distFactor = 20×100/(20+dist)` → on River Zeta (spawn-to-spawn ~95 cells): **17**
- `threatFactor` → enemy SR always has enemy troops nearby → mild=40 or hostile=10
- `OffensiveEnemyAttackBias = 80` (shared with enemy income buildings, below 100)

River Zeta concrete numbers (P1 SR at (15,6), P2 SR at (80,76)):
- Enemy SR, mild threat: 120×17×40×100×80/100 = **6.5M**
- Enemy SR, hostile: 120×17×10×100×80/100 = **1.6M**
- Nearest neutral oilb (dist 3, safe): 50×87×100×100×150/100 = **65M**
- Mid-distance neutral oilb (dist 46, safe): 50×30×100×100×150/100 = **22.5M**

**The enemy SR never enters any axis with the current config.** With MaxAxes=4 and a
32-unit army, the top-4 offensive targets are always neutral oilbs. The SR would only
rank in the top-4 after all neutral oilbs are captured — at which point the game is
almost certainly decided.

**Root cause — three structural factors, not a single tuning miss:**
1. **Distance:** the SR is always at max distance (enemy spawn edge). At 95 cells,
   distFactor=17 gives 17% of a local-POI score. Half-life of 20 cells was designed for
   income (closer = less travel time) but the SR position is fixed.
2. **ThreatFactor semantics are inverted for Pressure:** `ThreatFactor` hostile=10 was
   designed to deter lone TECNs from risky captures — but it also deters the entire
   army from SR pressure. For Pressure, enemy presence near the SR is an *opportunity*
   (garrison is there to be contested), not a deterrent. The existing threat gate is
   intentionally kept for Cycle 1 (it prevents suicide pushes at defended SRs) but the
   semantics mismatch is a known design tension.
3. **OffensiveEnemyAttackBias=80 conflates Pressure (SR) with Attack (enemy income):**
   the below-100 bias was correct for "don't rush enemy income before securing own income"
   but wrong for the SR, which is the highest-value strategic objective in the game model.

**Fix direction (Cycle 1):** raise `SupplyRouteDenyValue: 120→250` + split off a dedicated
`OffensiveSrPressureBias: 100` field from the shared OffensiveEnemyAttackBias=80. This
raises mild-threat SR score to 17M (competitive in top-4 mid-game) while hostile threat
(4.25M) still prevents suicide pushes. Pure YAML + ~6 lines C#. Full design note:
`WORKSPACE/plans/260720_sr_contestation_cycle1.md`.

## 2026-07-19 — Bot skirmish maps produce no army without a scenario applied

> **[rejected: AUTOTEST test-setup methodology — belongs in DOCS/recipes, not engine/gameplay reference; scenario application itself is visible at World.cs:216-222]** (curation 2026-07-20).
- Ran a bounded v2-vs-normal capture skirmish (`test-v2-poi-observe`, a bounded
  copy of `demo-v2-capture-coordinator`) for 55s to capture live AI logs. The v2
  bot built **nothing**: `[v2-poi] disperse pool=0 contested=0` for the whole
  run and **zero** `[v2-capture]` lines (no TECNs ever produced). Engine logged
  `Scenario selection: 'none', available scenarios: []`.
- **Takeaway:** the SR reinforcement/production pipeline is (at least partly)
  scenario-gated. A skirmish map that just places SRs + bots does NOT make the
  bots call in units in a short window — so it's useless as a runtime AI-behaviour
  observation vehicle. Any future live AI trace (death-ball, spread offense,
  capture) needs a harness where the scenario/production system actually feeds
  the SR queue, plus a longer window than ~1 min. Confirm what applies a scenario
  before relying on bot production in autotests.
- Logs land in `AppData/Roaming/OpenRA/Logs/debug.log` on Windows, and it
  **rotates per run** (truncated on each launch) — snapshot/grep right after.
- Unaffected: the `[v2-poi]` diagnostic itself works and emits clean per-scan
  lines; the death-ball root cause is confirmed structurally in code regardless
  (see plan 260719 Phase 0 findings).

## 2026-05-18 — Handicap unreachable in the V5 player row (deferred until usage data exists)

> **[rejected: WORKSPACE/lobby v1-cut decision (deferred to v1.1) — tracker material, tied to WORKSPACE/lobby/decisions.md]** (curation 2026-07-20).
- The V5 player row (`engine/mods/common/chrome/lobby-players.yaml`) keeps `DropDownButton@HANDICAP_DROPDOWN` and `Label@HANDICAP` widgets in every template, but parks them at `X: -200 W: 1 H: 1` so the C# `Get<>()` calls in `SetupEditableHandicapWidget` still resolve while nothing paints. The column was dropped in phase 5 redesign — agreed in `WORKSPACE/lobby/decisions.md` as a deliberate v1 cut.
- **Net effect:** the handicap mechanic still works (server orders, etc.) but players cannot SEE or CHANGE their handicap value from the lobby. Default applies.
- **Access path options when re-introducing** (per `IMPLEMENTATION_PLAN.md` Phase 8): right-click context menu on the player row; expandable detail row; spawn-cell dropdown overload; drop entirely if usage data shows it's unused.
- **Decision deferred to v1.1** — needs usage telemetry first. Bot-vs-bot tournaments and human skirmishes don't touch handicap today, so impact is low.

## 2026-05-18 — Empty MiniYaml values must be a bare trailing colon, not `""`

> **[promoted → conventions.md — "Disabling a string field: bare colon, not \"\""]** (curation 2026-07-20). Verified `FieldLoader.cs:161` + `DropDownButtonWidget.cs:71-73`.
- `Separators: ""` parses as the literal 2-char string `""` (FieldLoader.ParseString returns the raw value). It then fails `IsNullOrEmpty` inside DropDownButtonWidget.Draw, and `WidgetUtils.GetCachedStatefulImage("\"\"", "separator")` throws `Sprite ""/separator was not found`.
- Correct form: `Separators:` (bare trailing colon) — the parser treats it as a null string, IsNullOrEmpty fires, the lookup is skipped.
- Applies to any chrome/widget string field where you want to disable a feature by clearing it (Background, Decorations, Separators, TooltipText).

## 2026-05-13 — CohesionMoveModifier feels broken because EdgeLine looks identical to the old box

> **[rejected: in-flight feel-bug diagnosis (specific test probes + slot-bidder bugs) — the mechanism spec is already in architecture.md:161; diagnosis belongs in WORKSPACE]** (curation 2026-07-20).
Autotest-driven diagnosis on real river-zeta (`test-cohesion-river-zeta-actual`, 12 probes spanning open ground / sparse fringe / dense cluster / cross-map clicks) produced the [Cohesion] log lines below. Three things, in priority order:

1. **EdgeLine is the dominant intent for near-cover clicks (totalDensity 70–530), and it produces a perfectly straight perpendicular line of slots.** That visual output is indistinguishable from "spread to a line oriented along the move direction" — exactly the legacy box behavior the user thinks is broken. SpreadInside (the cluster-around-best-cover layout) only fires for clicks DEEP in dense cover (centroid offset < ~1.4 cells). Most natural clicks are at the edge of a cluster or 1–3 cells outside it — those resolve to EdgeLine.

2. **EdgeLine slot cells are picked geometrically, not by CoverScore.** `ComputeEdgeLineSlots` walks the perpendicular axis at fixed spacing and `NudgeToPassable`s impassable cells back along the gradient. There is no "of the cells near my ideal slot, pick the one with the highest CoverScore" step. So slots routinely land between trunks rather than behind them.

3. **Approach has a logic bug when the group is already adjacent to a cover patch.** `ComputeApproachSlots` walks `step = 1..maxSteps` from group centroid toward click and stops at the first cell with `CoverScore > 0`. If there's any cover immediately east of the group, Approach finds it at step 1 and anchors the formation right there — even when the click is 50+ cells away. In the river-zeta probes, clicks to (68,20), (80,75), and (10,75) all produced slots in the (22–26, 31–39) box (right next to the A cluster) because the squad was sitting on A's west edge. Units never reach the click.

`Open` is rare and not the user's complaint — it only fires when totalDensity in the 9×9 window is 0, which on river-zeta is genuinely-open ground. The classifier itself is calibrated reasonably; the issue is the **slot bidders downstream of the classifier**.

Other notes: DensityLayer is populated correctly (trees contribute density=10 to one trunk cell via `Building.Density`; `BlocksSight` has `IDensityInfo` commented out — only Buildings contribute). The `IModifyGroupOrder` dispatch works for every Test.GroupMove probe (the older "1 of 8" datapoint must predate a fix). Diagnostic log line restored at the bottom of `CohesionMoveModifier.ModifyGroupOrder` (idx==0) — strip when the feel issue is resolved.

## 2026-05-09 — AttackTurreted overrides CanAttack and short-circuits before base

> **[promoted → conventions.md — "Engine behaviors that surprise"]** (curation 2026-07-20). Verified `AttackTurreted.cs:36-48`.
- `AttackTurreted.CanAttack(self, target)` returns `turretReady && base.CanAttack(self, target)`. When `turretReady = FaceTarget(target)` is false (turret mid-rotation), `base.CanAttack` is never reached. So traces / breakpoints in `AttackBase.CanAttack` won't fire if the turret hasn't finished aiming. If you're trying to debug "why isn't this unit firing", check `AttackTurreted.cs` first — the answer is often "turret hasn't pointed at the target yet".

## 2026-05-09 — Activity.IsCanceling is always false inside OnLastRun

> **[promoted → conventions.md — "Engine behaviors that surprise"]** (curation 2026-07-20). Verified `Activity.cs:84,132-135`.
- `Activity.TickOuter` sets `State = ActivityState.Done` *before* calling `OnLastRun(self)`. `IsCanceling` is `State == ActivityState.Canceling`, so by the time OnLastRun runs, the cancel flag has been cleared. Useless for "did we end naturally vs cancelled". Better signals: check `NextActivity is X` (a queued activity behind us implies we were replaced), or compare `attack.RequestedTarget` to our own `target` field (someone else has already set the new target if they differ).

## 2026-05-09 — Build cache occasionally skips single-file edits; touch + make to force

> **[rejected: dev-workflow anecdote (macOS `make`/`touch`, "occasionally") — low-confidence build tip, not an engine/gameplay mechanic]** (curation 2026-07-20).
- `make` reports success even when a single .cs file's edit didn't make it into the DLL. Symptoms: traces don't fire, behavior unchanged, build log says `0 errors`. Fix: `touch <file>.cs && make`. Catches incremental-build dependency-tracking misses. Cost a couple of wasted runs in the artillery debugging session before recognizing the pattern.

## 2026-05-09 — Test mode trace pattern: gate on Game.LocalTick % N == 0

> **[rejected: AUTOTEST recipe tip — a debugging technique that belongs in DOCS/recipes, not reference]** (curation 2026-07-20).
- For "I want one trace per second, not 25 per tick" diagnostics during AUTOTEST: `if (TestMode.IsActive && Game.LocalTick % 25 == 0) Console.WriteLine(...)`. Pairs with the runner stdout capture at `/private/tmp/claude-501/.../tasks/<id>.output` — grep that file post-test. Strip all of these before committing the fix.

## 2026-05-03 — GrantConditionOnPrerequisite: ownership-change crash (upstream OpenRA bug)

> **[rejected: resolved-bug changelog — the fix already landed (GrantConditionOnPrerequisite.cs:62-76 unregisters/re-registers on owner change); nothing left for a future agent to act on]** (curation 2026-07-20).
- `GrantConditionOnPrerequisiteManager` is a per-player trait — each player has their own dictionary of `{key → list of (actor, trait)}`. `GrantConditionOnPrerequisite` registers the actor with its initial owner's manager in `AddedToWorld`, but the original `OnOwnerChanged` only rebound the cached manager reference without unregistering from old / registering with new. Result: after any in-world ownership change (capture, `OwnerLostAction: ChangeOwner Owner: Neutral`, garrison transfer, scenario transfer), `RemovedFromWorld` calls `Unregister` on the wrong dictionary → `KeyNotFoundException: condition_<prerequisite>`. First seen with LOGISTICSCENTER + `global-mcv-undeploys` after a player was defeated. Fix in `engine/OpenRA.Mods.Common/Traits/Conditions/GrantConditionOnPrerequisite.cs`: `OnOwnerChanged` now unregisters from the old manager and re-registers with the new one (when in world). Also fixes a memory leak (old manager kept dangling reference) and the silent correctness bug where the new owner's tech tree wouldn't drive the actor's condition.

## 2026-03-23 — OpenRA maps MUST have `Rules: rules.yaml` in map.yaml

> **[promoted → conventions.md — "Maps must declare Rules: rules.yaml"]** (curation 2026-07-20). Verified `Map.cs:176,364`.
- Without the `Rules: rules.yaml` line at the top level of map.yaml, OpenRA silently ignores rules.yaml entirely. This means LuaScript references, AutoTarget overrides, and all rule modifications are never loaded. The map appears to work (actors spawn, terrain renders) but Lua never executes and rule overrides don't apply. The MCP map tool was missing this — now fixed in set_map_rules.

## 2026-03-23 — ReloadAmmoPool FullReloadTicks/FullReloadSteps are dead code

> **[rejected: stale/wrong against current code — `ReloadAmmoPoolInfo` has no such fields (ReloadAmmoPool.cs:18-44); `FullReloadTicks`/`FullReloadSteps` exist and are actively used + unit-tested only on `AmmoPoolInfo` (AmmoPool.cs:29,32,225-234; AmmoPoolTest.cs). No dead code to document.]** (curation 2026-07-20).
- `ReloadAmmoPoolInfo` has `FullReloadTicks` and `FullReloadSteps` fields, but they're never read in code. `ReloadAmmoPool.Tick()` calls `ammoPool.Reload(self, Info.Delay, Info.Count)` which uses `Delay` (50) and `Count` (1). The `FullReloadTicks`/`FullReloadSteps` on *AmmoPoolInfo* (not ReloadAmmoPoolInfo) ARE used inside `AmmoPool.Reload()`, but the identically-named fields on ReloadAmmoPoolInfo do nothing. Many YAML entries set these thinking they matter (e.g., `ReloadAmmoPool@1: FullReloadTicks: 200`). Either implement them or remove from YAML.

## 2026-03-23 — SupplyProvider ammo-per-cycle scaling matters

> **[rejected: superseded by economy.md's `ReloadCount` batch model — this describes an older `max(1, poolCapacity/50)` fix that the current per-batch economy replaced; changelog]** (curation 2026-07-20).
- SupplyProvider was giving 1 ammo per RearmDelay cycle regardless of pool capacity. For an AR soldier with 500 ammo capacity, this took 5+ minutes to fill. Fixed to give `max(1, poolCapacity/50)` per cycle (~50 cycles from empty). Also added MinNeedThreshold (5%) to skip nearly-full units.

## 2026-03-21 — IProductionSpeedModifier pattern

> **[rejected: already covered — architecture.md's `SupplyRouteContestation` trait row names IProductionSpeedModifier; deeper interface mechanics are implementation detail]** (curation 2026-07-20).
- Created `IProductionSpeedModifier` interface for dynamic per-tick production speed control. Unlike `IProductionTimeModifierInfo` (which only applies at production START), this uses an accumulator pattern in `ProductionQueue.TickInner` to skip ticks proportionally. Returns 0-100 (percentage). Both `ProductionQueue` and `ClassicParallelProductionQueue` support it. The modifier is queried from producing buildings (not the player actor), via `ActorsWithTrait<Production>()` iteration.

## 2026-03-21 — Supply Route contestation replaces ProximityContestable

> **[rejected: already covered — architecture.md:159 (SupplyRouteContestation trait) + supply-route.md contestation section]** (curation 2026-07-20).
- The old `ProximityContestable` trait was binary (any enemy = full production halt, no feedback). Replaced with `SupplyRouteContestation` which uses value-based force comparison, graduated depletion/recovery, and `IProductionSpeedModifier` for smooth production slowdown. Key design: bar stored as int 0-100000 for precision, depletion formula `ticksToDeplete = max(MinTicks, BaseTicks * RefValue / netSurplus)`.

## 2026-03-21 — Initial setup

> **[rejected: trivial project-setup note, no reference content]** (curation 2026-07-20).
- Created WORKSPACE/ project folder for session tracking, plans, discoveries, and bug captures.

## 2026-03-21 — MCP map actor facing

> **[rejected: already covered — conventions.md "WAngle facing" table (0=N, 256=W, 512=S, 768=E) + CLAUDE.md]** (curation 2026-07-20).
- Actor `Facing` field in map.yaml must be a WAngle integer (0-1023), not a compass string like "East". The MCP `place_actors` tool passes it through as a string, so use: **0=North, 256=West, 512=South, 768=East** (counterclockwise — see `~/.claude/projects/.../memory/feedback_facings.md` and CLAUDE.md). Using "East" crashes on map load with `FieldLoader: Cannot parse 'East' into 'value.OpenRA.WAngle'`.
- (Corrected 2026-05-06 — earlier version of this entry had the directions wrong.)

## 2026-06-18 — autotest/screenshot scripts need `python3` on PATH

> **[rejected: machine-specific environment fix (this Windows box's PATH) — not portable project reference]** (curation 2026-07-20).
- `launch-game.sh` (used by `tools/autotest/screenshot-lobby.sh`, `screenshot.sh`, etc.) requires `python3` (or `python`) — it shells out only to resolve its own realpath. On this Windows box the only `python3` on PATH was the WindowsApps Store stub, which prints "Python was not found" and exits non-zero, so every launch died with "game process exited before lobby was ready" (no logs, because it never reached engine init).
- Fix (permanent): real Python lives at `C:\Python314` (admin-protected, can't drop files there). Created `C:\Users\fredr\bin\python3.exe` (copy of `C:\Python314\python.exe` — a bare copy still finds its stdlib via the PEP 514 registry landmark) and prepended `C:\Users\fredr\bin` + `C:\Python314` to the **user** PATH *ahead of* WindowsApps. Wrote the registry value as `REG_EXPAND_SZ` to preserve the existing `%USERPROFILE%` entries. New terminals pick it up automatically; already-running processes need a restart.

## 2026-07-18 - Lobby finishing pass: three engine gotchas

> **[promoted → architecture.md — "Widget / chrome authoring gotchas"]** (curation 2026-07-20). Verified `ImageWidget.cs:31,61,78-91`, `ButtonWidget.cs:320-323`, `Widget.cs:229-231`.
- **ImageWidget draws sprites at native size** - Width/Height are layout-only; `WidgetUtils.DrawSprite(sprite, origin)` ignores widget bounds. The "flag fills height" commit (0100022f) was a silent no-op for months. Added opt-in `ScaleToBounds: True` (uniform scale, centered, 3-arg DrawSprite overload) - remember to mirror new fields in the widget copy-constructor or template clones lose them.
- **ButtonWidget silently draws nothing for missing chrome variants** - a highlighted button looks up `<Background>-highlighted` (+ `-hover`/`-pressed`/`-disabled` suffixes); if the collection is absent, `WidgetUtils.DrawPanel` early-returns with no error. Our active tabs rendered with NO fill while inactive ones kept theirs (inverted emphasis). Any custom `Background:` needs the full variant set - `lobby-button-highlighted*` added 260718.
- **Hidden widgets keep keyboard focus** - `Widget.HandleKeyPress` only checks the focus widget's OWN `IsVisible`, not its ancestors. The inline map chooser's filter TextField kept focus while its parent tab was hidden: chat field dead, and Enter could silently fire the chooser's onSelect (= change the map). Pattern: any tab-switch that hides a focused widget must hand focus off explicitly.

## 2026-07-20 — LADDER S2/S3 doc is stale post-determinism (S2 EXPAND recon)

> **[rejected: concerns WORKSPACE ladder/spec tracker docs (LADDER.md/SPEC.md), not DOCS/reference — and already RESOLVED by the S2 standup cycle per the note below. The underlying determinism fact is separately promoted → architecture.md.]** (curation 2026-07-20).

- **LADDER.md's S2/S3 rows describe a superseded map + a broken-determinism world.** Found while designing the S2 rung (`WORKSPACE/plans/260720_s2_expand_design.md`):
  1. **Map:** LADDER.md:238, :279, :341-342 assign S2 (Force Efficiency) and S3 (Win-rate) to the `tournament-experimental-vs-normal-2p` **66×34 combat stub** — the same bare, zero-capturable map (`grep -c oilb|Capturable` = 0) whose lack of POIs pinned S1's economy metric to 0/0 before the River Zeta rescope (LADDER.md:76-93). Putting S2/S3 on a *different* map than S1 contradicts the rung model ("a rung is one map", LADDER.md:33-36) and the composite gate ("one commit passes all three on that map", §6.4). The S2 design recommends moving S2/S3 onto the River Zeta rung.
  2. **Determinism:** LADDER.md:48-56 and SPEC §3.2 (SPEC.md:207-220) and REVIEW.md:133-136 still state seeds are "run labels, not reproducibility guarantees" and per-seed replay is "broken" because bots draw from an unseeded `LocalRandom`. **This is now false** — `LocalRandom` is seeded (World.cs:213-214) and same-seed→byte-identical verdict was VERIFIED (commits `2d3c8fe0` engine + `f3a61d9d` docs; REVIEW.md:55 activity log). The fixed per-index seed set (run-tournament.sh:282) now makes comparisons *paired*, which the S2 bar exploits.
- **Action:** the S2-implementing cycle should update LADDER.md's S2/S3 rows (map → River Zeta rung; metric wording) and reconcile the "seeds are labels" language in LADDER §Metric-extraction + SPEC §3.2 + REVIEW Open Questions with the shipped determinism. Not fixed in this read-only recon (would touch curated ladder/spec state mid-batch); flagged here per the knowledge-bank rule.
- **RESOLVED (2026-07-20, S2 standup cycle):** both reconciled. (1) LADDER S2 row + Scenario-registry now point to the new `tournament-s2-combat-river-zeta` (River Zeta rung, 720s clock); the 66×34 `tournament-experimental-vs-normal-2p` stub is retired from the ladder; S3 row flagged "reuse River Zeta rung, scenario TBD at standup". (2) LADDER §Metric-extraction + SPEC §3.1/§3.2 rewritten to state per-seed replay is deterministic (`2d3c8fe0`, verified byte-identical), with the anti-overfit "don't tune to the fixed 10 seeds" caveat carried in. REVIEW Open Questions left for the CALIBRATE-result update.

## 2026-07-20 — SR-contestation tunables can't live on the world `PoiMap` trait (SR-contest recon)

- **`PoiMap` is a world singleton; any SR-scoring tunable on `PoiMapInfo` is global to every bot profile.** Both `PoiOffensiveBotModule@experimental` (ai.yaml:175) and `@stable` (ai.yaml:662) consume the *same* `PoiMap.GetOffensiveTargets` output (`PoiMap.cs:279`), so raising `SupplyRouteDenyValue` or adding an `OffensiveSrPressureBias` **in `world.yaml:296` changes @stable too** — silently mutating the frozen benchmark control. This is exactly what the shared-trait-defaults rule forbids (`DOCS/reference/architecture.md:309`: behavioural Info fields on a shared trait must default to frozen behaviour and be opted in **per-profile via YAML**).
- **Consequence for @experimental-only scoring changes:** a per-profile knob must live on the per-bot trait (`PoiOffensiveBotModuleInfo`), not on `PoiMapInfo`. Pattern to mirror: `CohesionSwitchEnabled` (default `false`, flipped `true` on @experimental only, `PoiOffensiveBotModule.cs:87/:424`). For SR pressure specifically, a single per-bot `SrPressureScoreMultiplier` (x100, default 100 = inert) applied to `PoiAction.Pressure` axes after `GetOffensiveTargets` reproduces a global `value 120→250` + `bias 80→100` change with multiplier `(250·100)/(120·80)=260`, while leaving @stable byte-identical. Verified against constants on `1594ffa1` (`SupplyRouteDenyValue=120`, threat 100/40/10, `OffensiveEnemyAttackBias=80`, `DistanceHalfLifeCells=20`): frozen SR mild 6.528M ×2.604 = 17.0M, safe 42.5M, hostile 4.25M.
- **Deny-only invariant re-confirmed on current main:** `SUPPLYROUTE` has no `CaptureManager` (`PoiMap.cs:219-222`); Pressure emits `AttackMove` to the SR cell (`PoiOffensiveBotModule.cs:467`), not `CaptureActor`; `GetCaptureTargets` (`PoiMap.cs:257-260`) filters Pressure out of the capture layer. The dispersion cohesion switch is action-agnostic (`:424-425`, gates on distance only), so it applies to a Pressure axis with no special-casing.
