# Session — Experimental AI POI strategy, Phase 3 (spread offense)

Started: 2026-07-19 13:32
Mode: EXPERIMENTAL
Plan: WORKSPACE/plans/260719_experimental_ai_poi_strategy.md

## Task
Phase 3: spread-out offense via FULLY SCORE-FLOATING attack axes for v2. Split the
v2 ground army across axes chosen purely by PoiMap score (capture-escort handled
elsewhere; here: deny/attack axes + enemy SR pressure + enemy base competing on
score, no hardcoded base-beeline — decision #3). Resolve the fixed-wing
SquadManager unit-claim conflict (Phase 2 finding #1). v2-gated throughout;
Normal/Rush/Turtle byte-identical. SR capturability UNRESOLVED — SRs are
attack/pressure POIs only.

## Design delivered
- **PoiMap.GetOffensiveTargets(perspective)** — enemy-owned POIs projected as army
  objectives: enemy income/utility = Attack, enemy SR = Pressure. Scored
  value×distance×threat from own SR, reusing existing helpers. Enemy SR action
  reclassified Capture/Deny → Pressure (added PoiAction.Attack).
- **PoiOffensiveBotModule** (new, ~330 LOC incl. pure PoiOffenseMath) — v2-gated
  IBotTick. Pipeline: GetOffensiveTargets → DesiredAxisCount → sticky top-k
  (hysteresis) → AllocateProportional → per-axis AttackMove, each unit committed
  through the shared PoiGoalGuard ledger ("offense:<targetId>"). Live axes persist
  across reevals (sticky); RepathThresholdCells gates re-issuing orders. All
  constants are Info fields (YAML-tunable). Emits `[v2-offense]` reeval/axis/order/
  retire lines.
- **PoiOffenseMath** (pure, v3-portable): DesiredAxisCount, AllocateProportional
  (largest-remainder, min-size, tail-drop), ScoreBeatsByThreshold (hysteresis).

## Unit-claim conflict resolution (Phase 2 finding #1)
- Engine `SquadManagerBotModuleInfo.IgnoreGroundUnits` (bool, default false →
  legacy byte-identical). When true, FindNewUnits skips non-air/non-naval units
  without claiming them (not added to activeUnits) so they stay a free pool.
- Both v2 fixed-wing SquadManagers set `IgnoreGroundUnits: true` → they keep only
  air squads (MIG/FROG, A10/F16); the ground pool is handed to
  PoiOffensiveBotModule. ExcludeFromSquadsTypes retained as belt-and-suspenders.
- **Shared claim = the single PoiGoalGuard.Ledger** (minimal §5.6 blackboard):
  capture commits TECNs ("capture:<id>"), offense commits combat units
  ("offense:<id>"); each module skips units committed by anyone. CaptureCoordinator
  escort/defender recruit now also skips ledger-committed units (was a poach gap).

## Tests
- `PoiOffenseTest.cs` — 15 NUnit cases (DesiredAxisCount caps, proportional split
  sum/min/tail-drop/determinism, hysteresis threshold, spread invariant).
- Full suite: **258 green** (was 243 baseline; +15). Build passes.

## Status
- [x] SquadManager IgnoreGroundUnits flag + v2 wiring
- [x] PoiMap offensive query + PoiAction.Attack
- [x] PoiOffensiveBotModule + PoiOffenseMath
- [x] CaptureCoordinator ledger-aware escort/defender recruit
- [x] NUnit PoiOffenseTest (15), suite 258 green
- [x] ai.yaml PoiOffensiveBotModule@v2 wiring
- [x] Commit 1 (module+claim+tests+wiring)
- [x] Live harness: extended test-v2-poi-harness (16 pre-placed + prod ramp → 25
      offensive units, 3 enemy POIs). ONE run (PASS). Log shows the spread live:
      started 2 axes (pool=20,k=2), opened a 3rd as the pool grew to 25 (k=3).
      Three concurrent axes, distinct targets, proportional split by score:
        fcom@50,22  Attack   score=21.7M  units=11   (highest score → most units)
        supplyroute@58,16 Pressure score=12.96M units=7
        oilb@50,10  Attack   score=10.85M units=7
      free=0 (all units claimed, none stolen/idle). Stable across ticks 784–1084
      (hysteresis holds — no axis thrash). DECISION #3 CONFIRMED LIVE: the enemy
      fcom OUTSCORES the enemy SR, so the base/SR is not privileged — the derrick
      pulls the biggest axis. This is the "spread not death-ball" behaviour.

## Mid-task steer (2026-07-19) — opening = secure income first
User reframed the opening: most games should START by spreading out to capture ALL
money POIs (closest/highest first, secure income); offensive pushes come after/
alongside; SR wiring stays DEFERRED (SRs = ordinary scored pressure POIs, no
special handling). Endgame "should I attack?" layer is backlogged (not mine).

Fold-in (no separate opening mode — emerges from scoring):
- GetOffensiveTargets now also emits NEUTRAL money POIs as `Secure` axes (army
  screens+holds), plus the existing enemy `Attack`/`Pressure`. New PoiMap Info
  fields `OffensiveIncomeSecureBias` (150) and `OffensiveEnemyAttackBias` (80)
  bias the opening toward income; both YAML-tunable. Pure `PoiScoring.ApplyBias`.
- As money POIs are captured they become own → drop from the set → their Secure
  axis retires → ranking shifts to the enemy: offense emerges AFTER income secured.
- 3 new NUnit cases (ApplyBias, opening neutral-income > distant enemy base,
  closest-neutral-first). Suite 261 green (was 258).

VERIFIED (2nd harness run, PASS): opening axes are ALL `action=Secure` on the
NEUTRAL money — bio@32,10 (96.75M) then fcom@32,22 (64.5M), oilb@22,16 (42.75M)
joining as the pool grew. Enemy SR/base attack axes damped OUT of the top-k while
neutral money exists. Ordering is value×distance (bio $150 first, not the closer
$50 oilb) — the natural value×distance×threat ranking; DistanceHalfLifeCells is
the lever if pure-closest is ever wanted. Matches the steer.

## Notes / Phase 4 hooks
- LayeredDefence is NOT yet ledger-aware — offense may contend with frontline
  reserves once contact forms. Pre-contact (no frontline) offense owns the pool
  cleanly. Phase 4 (defense/garrisons) should make LayeredDefence + garrison
  consult the same ledger.
- SR still not capturable (Phase 2 finding #3) — Pressure only, per constraint.
- TECN limit 3 unchanged.
