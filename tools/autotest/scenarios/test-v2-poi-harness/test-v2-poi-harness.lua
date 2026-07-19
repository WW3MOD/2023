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

WorldLoaded = function()
	TestHarness.FocusBetween(NearOilb, MidFcom, FarBio, NeutralSR)

	-- ~45s of simulation, then exit with a pass verdict (observation only).
	Trigger.AfterDelay(45 * TestHarness.TicksPerSecond, function()
		Test.Pass()
	end)
end
