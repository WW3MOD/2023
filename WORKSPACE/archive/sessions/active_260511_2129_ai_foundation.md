# Session: AI Foundation → Tournament Harness Phase 1

**Date:** 2026-05-11 21:29 — present
**Status:** in-progress (Phase 1 implementation underway; first smoke-test run TBD)
**Task chain:**
1. ✅ Lay out AI foundation (techniques, architecture, phasing) — `WORKSPACE/ai/foundation_260511.md`
2. ✅ User clarification: SR mental model — `DOCS/reference/supply-route.md`
3. ✅ Tournament harness plan — `WORKSPACE/plans/260511_ai_tournament_harness.md`
4. 🚧 Tournament harness Phase 1 — engine + YAML + scenario + shell scripts, smoke-test

## Files created this session

### Docs
- `WORKSPACE/ai/README.md` — project home (links foundation, plan, swap guide)
- `WORKSPACE/ai/foundation_260511.md` — basics doc (survey + architecture + phases)
- `WORKSPACE/ai/tournament_swap_guide.md` — every swap point and how to replace it
- `WORKSPACE/ai/PITFALLS.md` — traps already hit; preventive list for future agents
- `WORKSPACE/plans/260511_ai_tournament_harness.md` — concrete implementation plan
- `DOCS/reference/supply-route.md` — canonical SR mental model (after user correction)

### Engine (Phase 1)
- `engine/OpenRA.Mods.Common/Tournament/` — new module
  - `MatchTypes.cs` — common types (MatchScoreSnapshot, MatchTrackingState, MatchVerdict)
  - `IMatchScorer.cs` — scorer plug-in interface
  - `IWinRuleEvaluator.cs` — win-rule plug-in interface
  - `MatchHarness.cs` — static registry of scorer/win-rule factories
  - `TournamentConfig.cs` — MiniYaml-driven config schema
  - `Scorers/WeightedComponentMatchScorer.cs` — default scorer
  - `WinRules/TimeOrSrCaptureWinRule.cs` — default win rule
- `engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs` — world trait
- `engine/OpenRA.Game/TestMode.cs` — added Test.TournamentConfig parsing

### YAML / scenarios
- `mods/ww3mod/rules/ai/ai.yaml` — refactored with `ModularBot@v2` + swap-point conditions (`enable-ai-v2`, `enable-ai-legacy-only`)
- `mods/ww3mod/rules/world.yaml` — registered BotVsBotMatchWatcher trait
- `tools/autotest/scenarios/tournament-arena-skirmish-2p/` — first scenario
  - `map.yaml` — 66×34, 2 non-playable bot combatants, 1 spectator Observer
  - `rules.yaml` — minimal overrides (DefaultCash: 7500)
  - `tournament.yaml` — match config (12-min limit, score-or-SR-capture rule)
  - `tournament-smoke.yaml` — 30s variant for smoke testing
  - `map.bin`, `map.png` — copied from arena-tank-duel

### Shell harness
- `tools/autotest/run-tournament.sh` — batch runner (iterates seeds, kicks off matches)
- `tools/autotest/aggregate-tournament.sh` — CSV + summary.json from per-match results

## Decisions locked (260511 conversation)

1. **Dual ModularBot in one binary** — `@normal` + `@v2` side-by-side, feature-flag via YAML conditions
2. **Hybrid score-or-SR-capture win rule** — score time-limited, SR capture instant-decisive with bonus
3. **Headless + parallel runner together** (Phase 2+3) — deferred until MVP green
4. **Milestone-driven autonomous loop** (Phase 4) — deferred

## Phase 1 status

| Step | Status |
|---|---|
| 1.1 Dual ModularBot YAML | ✅ committed |
| 1.2 Tournament scenario format | ✅ first scenario landed |
| 1.3 Engine trait `BotVsBotMatchWatcher` | ✅ committed |
| 1.4 `run-tournament.sh` | ✅ committed |
| 1.5 Sanity check (legacy-vs-legacy 40–60% noise band) | 🚧 smoke test in progress |

## Open items / future-session hooks

- **Smoke test result pending** — once it lands, decide whether Phase 1 is done or there are bugs to fix.
- **Sanity check needs 20 seeds at 720s** — long batch (~hour wall-clock at 1× speed). Will need headless renderer (Phase 2 of plan) before this becomes practical.
- **`Player.BotType` is readonly** — limits matchup flexibility. Documented in PITFALLS.md as Phase 4 candidate.
- **Capture income + kills value tracking not yet wired** — only `army_value` populated. WeightedComponentMatchScorer reads `MatchTrackingState.CaptureIncome / KillsValue` but nothing increments them yet. Phase 2-ish polish.
- **Foundation doc §7 open questions** — still unanswered (difficulty levels, honest fog, Waypoint-mode coupling). Doesn't block Phase 1 but blocks Phase 2 (Strategic Planner).

## How a future agent should pick this up

1. Read `WORKSPACE/ai/README.md` for the project home.
2. Read `WORKSPACE/plans/260511_ai_tournament_harness.md` for the plan.
3. Read `WORKSPACE/ai/tournament_swap_guide.md` to understand what's swappable.
4. Read `WORKSPACE/ai/PITFALLS.md` to avoid known traps.
5. Read this session log for "what's done vs in flight."
6. Run `./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p --seeds 1 --config .../tournament-smoke.yaml --max-wall-secs 120` to verify the harness still works.
7. Check `git log --oneline --grep='tournament'` for the commit chain.
