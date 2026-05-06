# Discoveries

> Patterns, gotchas, and insights found during work. Dated entries.
> Stable, broadly applicable items should also go into CLAUDE.md.

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
- Created CLAUDE/ project folder for session tracking, plans, discoveries, and bug captures.

## 2026-03-21 — MCP map actor facing
- Actor `Facing` field in map.yaml must be a WAngle integer (0-1023), not a compass string like "East". The MCP `place_actors` tool passes it through as a string, so use: **0=North, 256=West, 512=South, 768=East** (counterclockwise — see `~/.claude/projects/.../memory/feedback_facings.md` and CLAUDE.md). Using "East" crashes on map load with `FieldLoader: Cannot parse 'East' into 'value.OpenRA.WAngle'`.
- (Corrected 2026-05-06 — earlier version of this entry had the directions wrong.)
