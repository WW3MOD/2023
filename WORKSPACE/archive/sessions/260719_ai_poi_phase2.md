# Session — Experimental AI POI strategy, Phase 2

Started: 2026-07-19 15:00
Mode: EXPERIMENTAL
Plan: WORKSPACE/plans/260719_experimental_ai_poi_strategy.md
Prior session (Phase 0+1): WORKSPACE/archive/sessions/active_260719_1224_ai_poi_phase01.md

## Task (two deliverables, in order)

**PART A — Scenario-fed AI observation harness.** Phase 0 discovered bot
production is effectively unobservable in a headless autotest window (v2 bot
produced nothing in 55s: `pool=0`, zero `[v2-capture]` lines). Build a reusable
autotest scenario where the v2 bot ACTUALLY has a force to observe — pre-placed
starting force (TECN + escort + army) + capturable furniture (OILB/FCOM/BIO at
varying distance + a neutral SR). Emit the existing `[v2-poi]`/`[v2-capture]`
channels so behavior is log-assertable. Include the deferred in-game capture
autotest: escorted TECN captures a derrick, assert capture completes + no
order-thrashing.

**PART B — PoiMap: POI discovery + scoring.** v2-gated world trait that
discovers POIs (money capturables, neutral+enemy SR deny-targets, enemy base),
scores value×distance×threat (reuse InfluenceMap), exposes the scored list.
CaptureCoordinator switches target selection to PoiMap ordering. Pure scoring
separable (v3-portable, like GoalGuardLedger). NUnit deterministic scoring tests.

## Key constraints
- All new behavior gated `enable-ai-v2`; Normal/Rush/Turtle untouched control.
- TECN limit 3 stays (⚠️ flag if bottleneck, don't change).
- HARD RULE autotests: max ONE run per distinct test, twice same test, no batch.
- Full NUnit suite green; build passes (locked-DLL on Win = game running, move on).

## Status
- [x] PART B: PoiMap (`Traits/World/PoiMap.cs` ~330 LOC w/ pure PoiScoring) +
      CaptureCoordinator rewire (PoiMap-ordered targets, legacy fallback) +
      world.yaml wiring + 14 NUnit PoiScoring cases. Suite 243 green. Live-verified.
- [x] PART A: `test-v2-poi-harness` (observation) + `test-v2-poi-capture` (capture
      assertion, PASS). Both reuse the observe map.bin terrain.
- [x] Single live runs (3 total across 2 distinct tests, within cap): harness
      observation + focused capture (fail → root-caused → fix → PASS).
- [x] Capture-steal root-caused + FIXED (see below).

## Notes / findings

**PoiMap works end-to-end (live):** `targets=6, top=bio@… score=64.5M`, capture
order issued off PoiMap score, correct value-weighting (BIO $150 > near OILB $50 —
value dominates distance, the score-floating behaviour decision #3 wants).
Goal-guard held: `committed=True commitN=1` (no thrash).

**Harness solved Phase 0's blocker:** pre-placed force → `[v2-poi] pool=6→16`
(was 0), all v2 channels flow, behaviour observable from tick 0.

**Root-caused a latent bug the harness exposed — v2 capture never completed.**
`SquadManagerBotModule` is NOT air-only: `FindNewUnits` recruits every
IPositionable not in `ExcludeFromSquadsTypes` into ground attack squads. The
`@{fac}.fixedwing` managers (enable-ai-any, EMPTY exclude list) scooped the TECN
into a ground squad and attack-moved it at the enemy, overriding CaptureActor.
→ **Corrects Phase 0's "v2 has no offensive brain"** — the fixed-wing SquadManager
IS the ground brain / death-ball source.
**Fix (v2-only):** re-gate fixed-wing SquadManagers to enable-ai-legacy-only + add
enable-ai-v2 variants excluding tecn/e6/truk. Verified: capture completes ~20s,
`activity=CaptureActor`, `commitN=1`. Normal/Rush/Turtle byte-identical.

**SR not capturable today** (no CaptureManager) — PoiMap still discovers/scores it
as deny/pressure POI (capture consumer skips it harmlessly). decision #2's
capture→neutralize→hold needs a CaptureManager added to SUPPLYROUTE first.

**Phase 3-4 adjustments** (also in plan Phase 2 FINDINGS): (1) PoiOffensiveBotModule
must compete-with/replace the fixed-wing SquadManager's ground squads, not fill a
vacuum. (2) Need a GENERAL unit-claim (goal-guard/BotBlackboard consulted by all
modules) — ExcludeFromSquadsTypes is a blunt per-type patch. (3) SR pressure ready;
SR deny-capture needs the game-model CaptureManager decision. (4) TECN limit 3 not
a bottleneck now.

## Commits
- 8aca8462 PART B: PoiMap discovery+scoring, wired into v2 capture
- 8939649d fix: fixed-wing SquadManager stealing the TECN (capture now completes)
- 786f7e0a PART A: observation harness + capture assertion scenarios
