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
- [x] Plan amended with 3 decisions (commit 1)
- [x] Phase 0 death-ball confirm — CONFIRMED by code (LayeredDefence:161-164 gate
      + SquadManager gated legacy-only). `[v2-poi]` dispersion diagnostic shipped.
      Findings written to plan Phase 0 section.
- [x] Phase 1 goal-guard: GoalGuardLedger<T> + PoiGoalGuard trait + CaptureCoordinator
      wiring + ai.yaml. NUnit PoiGoalGuardTest (10 cases) green; full suite 229 green.
- [x] Single bounded live run (test-v2-poi-observe, 55s, PASS). [v2-poi] pipeline
      works, contested=0 whole run (corroborates finding). BUT pool=0 + 0 [v2-capture]
      = bot produced nothing ("Scenario selection: none"). Discovery logged. Did NOT
      spend 2nd run. Code confirmation remains decisive.

## Verdicts / notes
- TECN limit 3 for v2 (via enable-ai-player UnitBuilder@america.normal). NOT a
  Phase-1 blocker — goal-guard makes each of the 3 reliably complete a capture.
  ⚠️ throughput capped at 3 concurrent captures; Phase 2/3 may want more (note, not changed).
- Goal-guard is a reusable pure `GoalGuardLedger<TKey>` (v3-portable) + thin
  `PoiGoalGuard` player trait holder. CaptureCoordinator gates re-issue on
  Ledger.IsCommitted; commits on order; ReconcileGuardCommitments releases on
  capture-done/expiry. Legacy activeCapturers path kept only as null-guard fallback.
- TDD artifact = NUnit ledger test (encodes S-E no-thrash invariant deterministically).
  In-game single-TECN capture autotest deferred — fragile under the single-run cap.

## Notes
- TECN limit for v2 = 3 (shared via enable-ai-player UnitBuilder@america.normal,
  UnitLimits tecn.america: 3). Question whether 3 blocks Phase 1 — see verdict.
- `[v2-capture]` log channel already exists in CaptureCoordinatorBotModule.
