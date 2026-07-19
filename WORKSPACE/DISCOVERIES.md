# Discoveries

> Patterns, gotchas, and insights found during work. Dated entries.
> Stable, broadly applicable items should also go into CLAUDE.md.

## 2026-07-20 — `CohesionMoveModifier` is a cover-aware intent system, NOT a simple offset system; and it DOES fire for bot orders

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
- Researching a foundation for an **autonomous AI benchmark** (many unsupervised bot-vs-bot games, metrics from logs) surfaced that most of it is **already built**: `tools/autotest/run-tournament.sh` + `loop-tournament.sh` + `aggregate-tournament.sh` run N seeded matches, aggregate to CSV/JSON, and drive a milestone loop (winrate/budget stop-conditions). Engine side: `BotVsBotMatchWatcher` (world trait) writes a per-match JSON verdict (winner, win_reason, duration, per-player score_total + components); `WeightedComponentMatchScorer` already reads live `PlayerStatistics.ArmyValue/KillsCost` + `PlayerResources.Earned` (the `tournament.yaml` "only army_value" note is stale). 7 tournament scenarios exist incl. v2-vs-normal.
- **Two blockers for the user's Windows goal:** (1) the whole harness is `.sh` + `uname` Darwin/Linux branches + `osascript` focus mitigation — **Windows is unhandled**; (2) **no headless mode** — only one `IPlatform` (`DefaultPlatform`), the SDL window is always shown (`Sdl2PlatformWindow.cs:227`, no `SDL_WINDOW_HIDDEN`), and on Windows it **steals focus** with no mitigation. The dedicated server can't substitute — it's order-relay only (`OpenRA.Server/Program.cs:100-109`, no `World`); bots tick client-side (`ModularBot.cs:86`).
- A true headless/null renderer was **explicitly rejected** (`WORKSPACE/ai/archive/PITFALLS.md §17`) as "days of work, risk of breaking determinism" — but that call was made for macOS where `osascript` already tamed focus. On Windows the calculus flips. **Cheapest fix: ~10-line `OPENRA_WINDOW_HIDDEN=1` env flag adding `SDL_WINDOW_HIDDEN` at window creation** — no-window + no-focus-theft in one stroke, keeps a real GL context (unlike a null platform).
- **Speed:** `GameSpeed` caps at 2×; real lever is `Test.SpeedMultiplier` (1–16, lowers `world.Timestep`), 4–6× practical with render on (renderer is the ceiling; ~30s fixed init dominates short matches). **Seeds:** `Test.RandomSeed` override makes matches reproducible per seed (`PITFALLS §15`); vary for a sample, fix to reproduce.
- **Riskiest unverified assumption:** that a hidden SDL window ticks the sim to completion on Windows with identical (deterministic) results. Retire with one bounded run after the flag lands.
- Full report + effort estimates: [`plans/260719_ai_benchmark_substrate_findings.md`](plans/260719_ai_benchmark_substrate_findings.md).

## 2026-07-19 — SUPPLYROUTE is NOT capturable today; the doc's "capture → Neutral" is a misread of OwnerLostAction
- The game-model docs (`DOCS/reference/supply-route.md` §Capture, `game-model.md`) state an enemy SR can be captured by an engineer/technician and flips to Neutral. **This does not work in-game.** SUPPLYROUTE has **no `Capturable` and no `CaptureManager`** — not in its own block (`mods/ww3mod/rules/ingame/structures.yaml:202-343`), not in any template it inherits (`^ExistsInWorld`, `^SpriteActor`, `^SelectableBuilding` — all clean; `defaults.yaml:2-13, 772-775`), and not patched by any map/world/ai/campaign rules (checked). The Phase-2 AI worker's report was correct.
- **The doc conflates two unrelated mechanisms.** `OwnerLostAction: ChangeOwner → Neutral` (structures.yaml:227-229) does NOT fire on capture. `OwnerLostAction` implements `INotifyOwnerLost` (`engine/OpenRA.Mods.Common/Traits/OwnerLostAction.cs:20,42` — "when the actor's owner is **defeated**"), and `OnOwnerLost` is called **only** from `ConquestVictoryConditions.cs:109-110` and `StrategicVictoryConditions.cs:152-153`, both iterating the actors of a just-defeated player. So an SR goes Neutral **only when its owning player loses the game**, never via an engineer.
- **Capturer side is fully wired, target side is not.** TECN inherits `^CapturesNeutralBuildings` = `CaptureManager` + `Captures{CaptureTypes: building-neutral}` (`infantry.yaml:2164, 897-904`); soldiers get `^CapturesOccupiedBuildings` (`building-occupied`, 885-896). Capturable tech buildings (OILB/FCOM/BIO…) get the matching side via `^BasicBuilding → ^NeutralOrOccupiedCapturable` (`structures.yaml:2-10, 149-157`: `Capturable@neutral: building-neutral` + `Capturable@occupied: building-occupied`). **SUPPLYROUTE inherits none of that chain**, so there is no capture-type to intersect — TECN literally has nothing to enter/capture on an SR (neutral or enemy).
- **Verdicts:** (a) enemy SR → capturable → flips Neutral = **ABSENT**. (b) neutral SR → capturable by a player (gain a 2nd reinforcement lane) = **ABSENT** — the harness `NeutralSR` (`test-v2-poi-harness/map.yaml:173`) is a plain `supplyroute`/Neutral actor with no Capturable; its `rules.yaml` adds none.
- **Gap to match the stated design:** add `CaptureManager` + a `Capturable` to SUPPLYROUTE. Note two subtleties: (1) neutral-SR capture needs `Types: building-neutral`; enemy-SR needs `building-occupied`. (2) **Standard `Captures`/`Capturable` transfers to the CAPTURER, not to Neutral** — so the "capturer can never use it, it just goes Neutral" design cannot be done with vanilla capture traits alone; it needs a custom on-capture hook (or `OwnerLostAction`-style flip triggered by capture). The commented-out `CaptureNotification` at structures.yaml:216-217 is unrelated and wires nothing.
- No live test was needed — the YAML+C# reading is unambiguous (no Capturable anywhere on the actor). A run could only confirm the negative.

## 2026-07-20 — PoiMap enemy-SR score: three factors conspire to keep it last in offensive ranking

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
- The V5 player row (`engine/mods/common/chrome/lobby-players.yaml`) keeps `DropDownButton@HANDICAP_DROPDOWN` and `Label@HANDICAP` widgets in every template, but parks them at `X: -200 W: 1 H: 1` so the C# `Get<>()` calls in `SetupEditableHandicapWidget` still resolve while nothing paints. The column was dropped in phase 5 redesign — agreed in `WORKSPACE/lobby/decisions.md` as a deliberate v1 cut.
- **Net effect:** the handicap mechanic still works (server orders, etc.) but players cannot SEE or CHANGE their handicap value from the lobby. Default applies.
- **Access path options when re-introducing** (per `IMPLEMENTATION_PLAN.md` Phase 8): right-click context menu on the player row; expandable detail row; spawn-cell dropdown overload; drop entirely if usage data shows it's unused.
- **Decision deferred to v1.1** — needs usage telemetry first. Bot-vs-bot tournaments and human skirmishes don't touch handicap today, so impact is low.

## 2026-05-18 — Empty MiniYaml values must be a bare trailing colon, not `""`
- `Separators: ""` parses as the literal 2-char string `""` (FieldLoader.ParseString returns the raw value). It then fails `IsNullOrEmpty` inside DropDownButtonWidget.Draw, and `WidgetUtils.GetCachedStatefulImage("\"\"", "separator")` throws `Sprite ""/separator was not found`.
- Correct form: `Separators:` (bare trailing colon) — the parser treats it as a null string, IsNullOrEmpty fires, the lookup is skipped.
- Applies to any chrome/widget string field where you want to disable a feature by clearing it (Background, Decorations, Separators, TooltipText).

## 2026-05-13 — CohesionMoveModifier feels broken because EdgeLine looks identical to the old box
Autotest-driven diagnosis on real river-zeta (`test-cohesion-river-zeta-actual`, 12 probes spanning open ground / sparse fringe / dense cluster / cross-map clicks) produced the [Cohesion] log lines below. Three things, in priority order:

1. **EdgeLine is the dominant intent for near-cover clicks (totalDensity 70–530), and it produces a perfectly straight perpendicular line of slots.** That visual output is indistinguishable from "spread to a line oriented along the move direction" — exactly the legacy box behavior the user thinks is broken. SpreadInside (the cluster-around-best-cover layout) only fires for clicks DEEP in dense cover (centroid offset < ~1.4 cells). Most natural clicks are at the edge of a cluster or 1–3 cells outside it — those resolve to EdgeLine.

2. **EdgeLine slot cells are picked geometrically, not by CoverScore.** `ComputeEdgeLineSlots` walks the perpendicular axis at fixed spacing and `NudgeToPassable`s impassable cells back along the gradient. There is no "of the cells near my ideal slot, pick the one with the highest CoverScore" step. So slots routinely land between trunks rather than behind them.

3. **Approach has a logic bug when the group is already adjacent to a cover patch.** `ComputeApproachSlots` walks `step = 1..maxSteps` from group centroid toward click and stops at the first cell with `CoverScore > 0`. If there's any cover immediately east of the group, Approach finds it at step 1 and anchors the formation right there — even when the click is 50+ cells away. In the river-zeta probes, clicks to (68,20), (80,75), and (10,75) all produced slots in the (22–26, 31–39) box (right next to the A cluster) because the squad was sitting on A's west edge. Units never reach the click.

`Open` is rare and not the user's complaint — it only fires when totalDensity in the 9×9 window is 0, which on river-zeta is genuinely-open ground. The classifier itself is calibrated reasonably; the issue is the **slot bidders downstream of the classifier**.

Other notes: DensityLayer is populated correctly (trees contribute density=10 to one trunk cell via `Building.Density`; `BlocksSight` has `IDensityInfo` commented out — only Buildings contribute). The `IModifyGroupOrder` dispatch works for every Test.GroupMove probe (the older "1 of 8" datapoint must predate a fix). Diagnostic log line restored at the bottom of `CohesionMoveModifier.ModifyGroupOrder` (idx==0) — strip when the feel issue is resolved.

## 2026-05-09 — AttackTurreted overrides CanAttack and short-circuits before base
- `AttackTurreted.CanAttack(self, target)` returns `turretReady && base.CanAttack(self, target)`. When `turretReady = FaceTarget(target)` is false (turret mid-rotation), `base.CanAttack` is never reached. So traces / breakpoints in `AttackBase.CanAttack` won't fire if the turret hasn't finished aiming. If you're trying to debug "why isn't this unit firing", check `AttackTurreted.cs` first — the answer is often "turret hasn't pointed at the target yet".

## 2026-05-09 — Activity.IsCanceling is always false inside OnLastRun
- `Activity.TickOuter` sets `State = ActivityState.Done` *before* calling `OnLastRun(self)`. `IsCanceling` is `State == ActivityState.Canceling`, so by the time OnLastRun runs, the cancel flag has been cleared. Useless for "did we end naturally vs cancelled". Better signals: check `NextActivity is X` (a queued activity behind us implies we were replaced), or compare `attack.RequestedTarget` to our own `target` field (someone else has already set the new target if they differ).

## 2026-05-09 — Build cache occasionally skips single-file edits; touch + make to force
- `make` reports success even when a single .cs file's edit didn't make it into the DLL. Symptoms: traces don't fire, behavior unchanged, build log says `0 errors`. Fix: `touch <file>.cs && make`. Catches incremental-build dependency-tracking misses. Cost a couple of wasted runs in the artillery debugging session before recognizing the pattern.

## 2026-05-09 — Test mode trace pattern: gate on Game.LocalTick % N == 0
- For "I want one trace per second, not 25 per tick" diagnostics during AUTOTEST: `if (TestMode.IsActive && Game.LocalTick % 25 == 0) Console.WriteLine(...)`. Pairs with the runner stdout capture at `/private/tmp/claude-501/.../tasks/<id>.output` — grep that file post-test. Strip all of these before committing the fix.

## 2026-05-03 — GrantConditionOnPrerequisite: ownership-change crash (upstream OpenRA bug)
- `GrantConditionOnPrerequisiteManager` is a per-player trait — each player has their own dictionary of `{key → list of (actor, trait)}`. `GrantConditionOnPrerequisite` registers the actor with its initial owner's manager in `AddedToWorld`, but the original `OnOwnerChanged` only rebound the cached manager reference without unregistering from old / registering with new. Result: after any in-world ownership change (capture, `OwnerLostAction: ChangeOwner Owner: Neutral`, garrison transfer, scenario transfer), `RemovedFromWorld` calls `Unregister` on the wrong dictionary → `KeyNotFoundException: condition_<prerequisite>`. First seen with LOGISTICSCENTER + `global-mcv-undeploys` after a player was defeated. Fix in `engine/OpenRA.Mods.Common/Traits/Conditions/GrantConditionOnPrerequisite.cs`: `OnOwnerChanged` now unregisters from the old manager and re-registers with the new one (when in world). Also fixes a memory leak (old manager kept dangling reference) and the silent correctness bug where the new owner's tech tree wouldn't drive the actor's condition.

## 2026-03-23 — OpenRA maps MUST have `Rules: rules.yaml` in map.yaml
- Without the `Rules: rules.yaml` line at the top level of map.yaml, OpenRA silently ignores rules.yaml entirely. This means LuaScript references, AutoTarget overrides, and all rule modifications are never loaded. The map appears to work (actors spawn, terrain renders) but Lua never executes and rule overrides don't apply. The MCP map tool was missing this — now fixed in set_map_rules.

## 2026-03-23 — ReloadAmmoPool FullReloadTicks/FullReloadSteps are dead code
- `ReloadAmmoPoolInfo` has `FullReloadTicks` and `FullReloadSteps` fields, but they're never read in code. `ReloadAmmoPool.Tick()` calls `ammoPool.Reload(self, Info.Delay, Info.Count)` which uses `Delay` (50) and `Count` (1). The `FullReloadTicks`/`FullReloadSteps` on *AmmoPoolInfo* (not ReloadAmmoPoolInfo) ARE used inside `AmmoPool.Reload()`, but the identically-named fields on ReloadAmmoPoolInfo do nothing. Many YAML entries set these thinking they matter (e.g., `ReloadAmmoPool@1: FullReloadTicks: 200`). Either implement them or remove from YAML.

## 2026-03-23 — SupplyProvider ammo-per-cycle scaling matters
- SupplyProvider was giving 1 ammo per RearmDelay cycle regardless of pool capacity. For an AR soldier with 500 ammo capacity, this took 5+ minutes to fill. Fixed to give `max(1, poolCapacity/50)` per cycle (~50 cycles from empty). Also added MinNeedThreshold (5%) to skip nearly-full units.

## 2026-03-21 — IProductionSpeedModifier pattern
- Created `IProductionSpeedModifier` interface for dynamic per-tick production speed control. Unlike `IProductionTimeModifierInfo` (which only applies at production START), this uses an accumulator pattern in `ProductionQueue.TickInner` to skip ticks proportionally. Returns 0-100 (percentage). Both `ProductionQueue` and `ClassicParallelProductionQueue` support it. The modifier is queried from producing buildings (not the player actor), via `ActorsWithTrait<Production>()` iteration.

## 2026-03-21 — Supply Route contestation replaces ProximityContestable
- The old `ProximityContestable` trait was binary (any enemy = full production halt, no feedback). Replaced with `SupplyRouteContestation` which uses value-based force comparison, graduated depletion/recovery, and `IProductionSpeedModifier` for smooth production slowdown. Key design: bar stored as int 0-100000 for precision, depletion formula `ticksToDeplete = max(MinTicks, BaseTicks * RefValue / netSurplus)`.

## 2026-03-21 — Initial setup
- Created WORKSPACE/ project folder for session tracking, plans, discoveries, and bug captures.

## 2026-03-21 — MCP map actor facing
- Actor `Facing` field in map.yaml must be a WAngle integer (0-1023), not a compass string like "East". The MCP `place_actors` tool passes it through as a string, so use: **0=North, 256=West, 512=South, 768=East** (counterclockwise — see `~/.claude/projects/.../memory/feedback_facings.md` and CLAUDE.md). Using "East" crashes on map load with `FieldLoader: Cannot parse 'East' into 'value.OpenRA.WAngle'`.
- (Corrected 2026-05-06 — earlier version of this entry had the directions wrong.)

## 2026-06-18 — autotest/screenshot scripts need `python3` on PATH
- `launch-game.sh` (used by `tools/autotest/screenshot-lobby.sh`, `screenshot.sh`, etc.) requires `python3` (or `python`) — it shells out only to resolve its own realpath. On this Windows box the only `python3` on PATH was the WindowsApps Store stub, which prints "Python was not found" and exits non-zero, so every launch died with "game process exited before lobby was ready" (no logs, because it never reached engine init).
- Fix (permanent): real Python lives at `C:\Python314` (admin-protected, can't drop files there). Created `C:\Users\fredr\bin\python3.exe` (copy of `C:\Python314\python.exe` — a bare copy still finds its stdlib via the PEP 514 registry landmark) and prepended `C:\Users\fredr\bin` + `C:\Python314` to the **user** PATH *ahead of* WindowsApps. Wrote the registry value as `REG_EXPAND_SZ` to preserve the existing `%USERPROFILE%` entries. New terminals pick it up automatically; already-running processes need a restart.

## 2026-07-18 - Lobby finishing pass: three engine gotchas
- **ImageWidget draws sprites at native size** - Width/Height are layout-only; `WidgetUtils.DrawSprite(sprite, origin)` ignores widget bounds. The "flag fills height" commit (0100022f) was a silent no-op for months. Added opt-in `ScaleToBounds: True` (uniform scale, centered, 3-arg DrawSprite overload) - remember to mirror new fields in the widget copy-constructor or template clones lose them.
- **ButtonWidget silently draws nothing for missing chrome variants** - a highlighted button looks up `<Background>-highlighted` (+ `-hover`/`-pressed`/`-disabled` suffixes); if the collection is absent, `WidgetUtils.DrawPanel` early-returns with no error. Our active tabs rendered with NO fill while inactive ones kept theirs (inverted emphasis). Any custom `Background:` needs the full variant set - `lobby-button-highlighted*` added 260718.
- **Hidden widgets keep keyboard focus** - `Widget.HandleKeyPress` only checks the focus widget's OWN `IsVisible`, not its ancestors. The inline map chooser's filter TextField kept focus while its parent tab was hidden: chat field dead, and Enter could silently fire the chooser's onSelect (= change the map). Pattern: any tab-switch that hides a focused widget must hand focus off explicitly.
