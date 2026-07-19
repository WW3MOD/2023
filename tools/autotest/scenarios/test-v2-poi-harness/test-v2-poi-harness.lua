-- OBSERVATION HARNESS (not an assertion). POI-strategy Phase 2 (PART A).
--
-- Phase 0 could not observe v2 AI behaviour live: on a bare map the bot produced
-- nothing in the headless window (pool=0, zero [v2-capture] lines). This map
-- PRE-PLACES a v2 force (TECN + escorts + tanks, see map.yaml) plus capturables
-- at varying distance + a neutral SR, so the AI has a real army + capture target
-- from tick 0. It runs a fixed window and Test.Pass()es so the runner can read
-- the accumulated logs from AppData/Roaming/OpenRA/Logs/debug.log:
--
--   [v2-poi] disperse ... pool=N ...          -> Phase 0 pooling / dispersion
--   [v2-capture] poimap-scan ... targets=N top=... -> PoiMap discovery + scoring
--   [v2-capture] issue ... -> <poi> score=..   -> PoiMap-ordered target pick
--   [v2-capture] pre-scan ... commitN=1        -> goal-guard no-thrash
--
-- The DETERMINISTIC capture assertion lives in the companion scenario
-- test-v2-poi-capture (close, safe, single derrick) — capture completion is
-- fragile on THIS map because the highest-scored POI (BIO) sits deep in the
-- contested midfield where a forming frontline can strand the TECN (plan
-- risk #4). Here we only OBSERVE; there we ASSERT.

-- PHASE 3 (offense) observation: this map now also pre-places a ~16-unit USA
-- ground pool + TWO enemy-owned structures (EnemyFcom, EnemyOilb) beside the
-- enemy SR, so PoiOffensiveBotModule has >=2 offensive POIs to SPLIT across.
-- Read the accumulated log for the multi-axis evidence:
--   [v2-offense] reeval ... axes=N        -> N concurrent attack axes
--   [v2-offense] axis ... target=.. units=M -> per-axis assignment (M>0)
--   [v2-offense] order ... target=.. units=M -> AttackMove issued per axis
-- Two-or-more distinct `axis` targets with units>=MinAxisSize in one reeval =
-- the army spread across axes instead of one clump (assert coarsely from logs).

WorldLoaded = function()
	TestHarness.FocusBetween(EnemyOilb, EnemyFcom, OpponentSR)

	-- ~45s of simulation, then exit with a pass verdict (observation only).
	Trigger.AfterDelay(45 * TestHarness.TicksPerSecond, function()
		Test.Pass()
	end)
end
