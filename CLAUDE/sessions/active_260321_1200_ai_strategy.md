# Session: AI Overhaul — Full Implementation

**Date:** 2026-03-21
**Status:** in-progress (pausing for playtest)
**Task:** Implement AI overhaul from strategy document

## Completed This Session

### Tier 0: Quick Wins (YAML + minor C#)
- **0.1**: AI builds defenses (BaseBuilderBotModule)
- **0.2**: Already done — ExcludeFromSquadsTypes was pre-existing
- **0.3**: CaptureManagerBotModule tracks active capture targets; SquadManagerBotModule skips them
- **0.4**: Attack squads set Hunt engagement stance; fleeing squads reset to Defensive
- **0.5**: GroundUnitsAttackState checks AmmoPool, sends empty units to ReturnToBase
- **0.6**: 3 AI personalities — Normal, Rush, Turtle with distinct build priorities and squad behavior

### Tier 1: Foundation Systems (new C# traits)
- **1.1**: ThreatMapManager world trait — coarse 8x8 influence grid, military/economic layers, query API
- **1.2**: BotBlackboard per-player trait — task posting/claiming, unit claims, intel sharing
- **1.3**: ScoutBotModule — sends humvee/BTR to explore map, posts enemy intel to blackboard

### Tier 2: Tactical Layer
- **2.1**: Smart retreat using ThreatMapManager + RegroupState. Squads flee toward friendly influence, regroup, and re-engage if safe
- **2.2**: Multi-axis attacks — splits large armies into 2 squads attacking different weak points via ThreatMapManager.FindAttackTargets()
- **2.3**: GarrisonBotModule — AI garrisons defense structures (GTWR, PBOX, HBOX) with infantry, prioritizes buildings near enemy threats
- **2.4**: SupplyFollowerBotModule — supply trucks follow army clusters instead of idling at base, find safe positions behind front line

### Tier 3: Strategic Layer
- **3.1**: AdaptiveProductionBotModule — reads enemy composition from scouts/visibility, requests counter-units (AT vs vehicles, AA vs aircraft)

## New Files Created
- `engine/.../BotModules/ScoutBotModule.cs` — map exploration + enemy intel reporting
- `engine/.../BotModules/GarrisonBotModule.cs` — defensive building garrisoning
- `engine/.../BotModules/SupplyFollowerBotModule.cs` — supply truck field logistics
- `engine/.../BotModules/AdaptiveProductionBotModule.cs` — counter-composition production

## Files Modified
- `mods/ww3mod/rules/ai/ai.yaml` — all new modules configured
- `engine/.../BotModules/ThreatMapManager.cs` — added FindAttackTargets() multi-target query
- `engine/.../BotModules/SquadManagerBotModule.cs` — multi-axis squad splitting in CreateAttackForce()
- `engine/.../Squads/Squad.cs` — added ApproachWaypoint for flanking
- `engine/.../Squads/States/GroundStates.cs` — approach waypoint consumption in idle state

## Next Steps (after playtest)
- Tier 3.2: Economy scoring for build decisions
- Tier 4: Personality-specific tactical preferences
- Tier 5: Map-specific strategy (choke points, terrain analysis)
- LCCV strategic deployment (deferred until logistics infrastructure matures)
