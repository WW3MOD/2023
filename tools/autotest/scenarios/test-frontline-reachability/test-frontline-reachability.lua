-- frontline-influence Phase 1 — BOUNDED OBSERVATION (not a Lua assertion).
--
-- The @experimental bot (USA, left SR at 14,45) plays real River Zeta with
-- ReachabilityGatingEnabled ON. We run a fixed window so the offense module's
-- reachability logs accumulate, then Test.Skip() so the runner exits and the
-- agent can read AppData/Roaming/OpenRA/Logs/debug.log:
--   [exp-reach]  player=USA-bot target=<oilb>@<cell> reach=<class> mul=<x100> ...
--   [exp-offense] axis player=USA-bot target=<name>@<cell> action=<..> units=<n>
--
-- Behavior bar: the axis set should no longer be pure-central — far-bank POIs are
-- reachability-classified (Same near-bank vs RepairableCrossing/AmphibiousOnly/
-- Unreachable across the river) and their scores reshaped accordingly, so at least
-- one non-central axis (or a damped far-bank target) is visible in the log.

WorldLoaded = function()
	-- Center the camera on the contested middle so a --visible run is watchable.
	TestHarness.FocusBetween(OwnSR, OpponentSR)

	-- ~100s of simulation: the offense module re-evaluates on its interval many
	-- times, so [exp-reach] fires across the discovered POI set as the bot's army
	-- forms and pushes. Then skip so the runner writes a verdict and exits —
	-- skip rather than pass because nothing here is graded.
	Trigger.AfterDelay(100 * TestHarness.TicksPerSecond, function()
		Test.Skip("frontline-reachability observation window elapsed — read debug.log [exp-reach] / [exp-offense] axis lines")
	end)
end
