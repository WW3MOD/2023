# Session — Experimental AI POI strategy, Phase 0 + Phase 1

Started: 2026-07-19 12:24
Mode: EXPERIMENTAL
Plan: WORKSPACE/plans/260719_experimental_ai_poi_strategy.md

## Task
Implement Phase 0 (death-ball confirm + goal-guard foundation) and Phase 1
(goal-guard component wired into CaptureCoordinator so escorted TECNs stop
thrashing and actually capture OILB/FCOM/BIO). Gate everything under
`enable-ai-v2`; Normal AI stays the untouched control.

Three user decisions to fold into the plan first:
1. Architecture = Path A (bolt POI onto live v2 + per-unit goal-guard, v3-portable).
2. Neutral SR = deny-only is PERMANENT DESIGN (realism: capturing enemy SR flips
   it Neutral = cutting their reinforcement route; capturer must NEVER reinforce
   through it). Later phases: attack SR → neutralize → hold w/ small garrison.
3. Phase 3 offense = fully score-floating axes, no dedicated enemy-base axis.
   User accepts early passive/suboptimal games.

## Intended files
- WORKSPACE/plans/260719_experimental_ai_poi_strategy.md (amend w/ decisions + Phase 0 findings)
- engine/OpenRA.Mods.Common/Traits/BotModules/PoiGoalGuard.cs (new — trait + pure ledger)
- engine/OpenRA.Mods.Common/Traits/BotModules/CaptureCoordinatorBotModule.cs (edit — consult guard)
- mods/ww3mod/rules/ai/ai.yaml (wire PoiGoalGuard under enable-ai-v2)
- engine/OpenRA.Test/... PoiGoalGuardTest.cs (new NUnit)
- (maybe) tools/autotest/scenarios/test-v2-capture-no-thrash/ (integration autotest)

## Status
- [ ] Plan amended with 3 decisions
- [ ] Phase 0 death-ball confirm (log-based)
- [ ] Phase 1 goal-guard + wiring + tests

## Notes
- TECN limit for v2 = 3 (shared via enable-ai-player UnitBuilder@america.normal,
  UnitLimits tecn.america: 3). Question whether 3 blocks Phase 1 — see verdict.
- `[v2-capture]` log channel already exists in CaptureCoordinatorBotModule.
