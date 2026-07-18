# Discoveries

> Patterns, gotchas, and insights found during work. Dated entries.
> Stable, broadly applicable items should also go into CLAUDE.md.

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
