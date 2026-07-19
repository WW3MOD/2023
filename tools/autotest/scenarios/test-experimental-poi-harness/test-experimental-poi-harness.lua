-- OBSERVATION HARNESS (not an assertion). POI-strategy Phase 2 (PART A).
--
-- Phase 0 could not observe experimental AI behaviour live: on a bare map the bot produced
-- nothing in the headless window (pool=0, zero [experimental-capture] lines). This map
-- PRE-PLACES a experimental force (TECN + escorts + tanks, see map.yaml) plus capturables
-- at varying distance + a neutral SR, so the AI has a real army + capture target
-- from tick 0. It runs a fixed window and Test.Pass()es so the runner can read
-- the accumulated logs from AppData/Roaming/OpenRA/Logs/debug.log:
--
--   [experimental-poi] disperse ... pool=N ...          -> Phase 0 pooling / dispersion
--   [experimental-capture] poimap-scan ... targets=N top=... -> PoiMap discovery + scoring
--   [experimental-capture] issue ... -> <poi> score=..   -> PoiMap-ordered target pick
--   [experimental-capture] pre-scan ... commitN=1        -> goal-guard no-thrash
--
-- The DETERMINISTIC capture assertion lives in the companion scenario
-- test-experimental-poi-capture (close, safe, single derrick) — capture completion is
-- fragile on THIS map because the highest-scored POI (BIO) sits deep in the
-- contested midfield where a forming frontline can strand the TECN (plan
-- risk #4). Here we only OBSERVE; there we ASSERT.

-- PHASE 3 (offense) observation: this map now also pre-places a ~16-unit USA
-- ground pool + TWO enemy-owned structures (EnemyFcom, EnemyOilb) beside the
-- enemy SR, so PoiOffensiveBotModule has >=2 offensive POIs to SPLIT across.
-- Read the accumulated log for the multi-axis evidence:
--   [experimental-offense] reeval ... axes=N        -> N concurrent attack axes
--   [experimental-offense] axis ... target=.. units=M -> per-axis assignment (M>0)
--   [experimental-offense] order ... target=.. units=M -> AttackMove issued per axis
-- Two-or-more distinct `axis` targets with units>=MinAxisSize in one reeval =
-- the army spread across axes instead of one clump (assert coarsely from logs).

-- PHASE 4 (hold captured money) assertion: HeldOilb is USA-bot-owned from tick 0
-- (a pre-captured derrick) with a small Russia raid beside it. Read the log for
-- the garrison pipeline:
--   [experimental-garrison] reeval ... held=N garrisons=N   -> held POIs promoted to garrisons
--   [experimental-garrison] garrison ... poi=oilb units=M   -> garrison sized by value (M 1-3)
--   [experimental-garrison] order ... poi=oilb              -> AttackMove-to-hold issued
-- The derrick must still belong to USA-bot at the end (the garrison held it). This
-- is the one hard verdict on this otherwise-observational map.

WorldLoaded = function()
	TestHarness.FocusBetween(HeldOilb, EnemyFcom, OpponentSR)

	-- ~45s of simulation, then verify the held derrick survived under USA-bot.
	Trigger.AfterDelay(45 * TestHarness.TicksPerSecond, function()
		local usa = Player.GetPlayer("USA-bot")
		if HeldOilb.IsDead or HeldOilb.Owner ~= usa then
			Test.Fail("Phase 4: held derrick was lost — experimental garrison did not hold it")
		else
			Test.Pass()
		end
	end)
end
