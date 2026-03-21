# Discoveries

> Patterns, gotchas, and insights found during work. Dated entries.
> Stable, broadly applicable items should also go into CLAUDE.md.

## 2026-03-21 — IProductionSpeedModifier pattern
- Created `IProductionSpeedModifier` interface for dynamic per-tick production speed control. Unlike `IProductionTimeModifierInfo` (which only applies at production START), this uses an accumulator pattern in `ProductionQueue.TickInner` to skip ticks proportionally. Returns 0-100 (percentage). Both `ProductionQueue` and `ClassicParallelProductionQueue` support it. The modifier is queried from producing buildings (not the player actor), via `ActorsWithTrait<Production>()` iteration.

## 2026-03-21 — Supply Route contestation replaces ProximityContestable
- The old `ProximityContestable` trait was binary (any enemy = full production halt, no feedback). Replaced with `SupplyRouteContestation` which uses value-based force comparison, graduated depletion/recovery, and `IProductionSpeedModifier` for smooth production slowdown. Key design: bar stored as int 0-100000 for precision, depletion formula `ticksToDeplete = max(MinTicks, BaseTicks * RefValue / netSurplus)`.

## 2026-03-21 — Initial setup
- Created CLAUDE/ project folder for session tracking, plans, discoveries, and bug captures.

## 2026-03-21 — MCP map actor facing
- Actor `Facing` field in map.yaml must be a WAngle integer (0-1023), not a compass string like "East". The MCP `place_actors` tool passes it through as a string, so use: 0=North, 256=East, 512=South, 768=West. Using "East" crashes on map load with `FieldLoader: Cannot parse 'East' into 'value.OpenRA.WAngle'`.
