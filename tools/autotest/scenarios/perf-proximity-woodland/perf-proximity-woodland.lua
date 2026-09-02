-- PERF CAPTURE, not a behavioural test. Holds a byte-faithful copy of
-- woodland-warfare-ww3 (all 1223 actors, of which 583 are TC01-TC05 tree clumps
-- carrying a non-zero ProximityExternalCondition Offset) open for a fixed number
-- of ticks while ProximityPerf writes one JSONL record per tick, then passes.
--
-- Test.Pass() here means "the capture ran to completion", NOT "the code is
-- correct". There is no assertion in this file and there must not be one: the
-- verdict is decided by reading the emitted .proximity.jsonl, not by the harness.
-- A green result with no JSONL beside it means the Test.ProximityPerfLog arg
-- never reached the game and the run measured nothing.
--
-- Nothing moves (see rules.yaml). Every proximity-trigger rebuild counted is
-- therefore attributable to the cache comparison in ProximityExternalCondition
-- and to nothing else.

local CaptureTicks = 3000   -- 120 s of simulated time at 25 ticks/s

WorldLoaded = function()
	Trigger.AfterDelay(CaptureTicks, function()
		Test.Pass()
	end)
end
