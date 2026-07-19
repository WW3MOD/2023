-- BOUNDED OBSERVATION (not an assertion). POI-strategy Phase 0/1.
--
-- Runs the v2 (USA, left) vs Normal (Russia, right) capture skirmish for a
-- fixed window so the engine's [v2-poi] dispersion + [v2-capture] commitment
-- logs accumulate, then Test.Pass() so the game auto-exits and the runner can
-- read them. Read AppData/Roaming/OpenRA/Logs/debug.log afterward:
--   [v2-poi] disperse ...   -> Phase 0 death-ball pooling evidence
--   [v2-capture] pre-scan ... committed=.. commitN=..  -> Phase 1 no-thrash
-- A healthy goal-guard keeps commitN == 1 per TECN per target (no overwriting).

WorldLoaded = function()
	TestHarness.FocusBetween(NeutralBio, NeutralFcom, NeutralOilb1, NeutralOilb2)

	-- ~55s of simulation, then exit with a pass verdict (observation only).
	Trigger.AfterDelay(55 * TestHarness.TicksPerSecond, function()
		Test.Pass()
	end)
end
