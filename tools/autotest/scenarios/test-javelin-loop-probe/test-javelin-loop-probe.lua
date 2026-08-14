-- AUDIT 6.3 — the loop probe. This is the one that answers the two-year-old question.
--
-- A flyStraight-latched Javelin provably cannot loop: the latch freezes both facings permanently,
-- so the trajectory is a straight line in 3D. The only code path left that could turn a missile
-- back on its target is the audit's section 5.2 turn-radius-limited orbit: a limit cycle of radius
-- R = 815 wdist about a STATIONARY aim point, on which currentDistance stays constant so the latch
-- predicate (currentDistance > minDistanceToTarget + 298) never fires and no fuse clause trips.
--
-- The orbit needs the target to be stationary at the moment of arrival — an orbit period is ~17
-- ticks and a moving target translates far enough in half of one to trip the latch immediately.
-- So: drive the Humvee at full speed to build a large lead term, then STOP it a few ticks before
-- intercept. The lead collapses to zero on the stop, throwing the aim point laterally, while the
-- target motion that would destroy the orbit is removed.
--
-- Success signature, per the audit, is machine-readable and nothing in the retained 640-record
-- corpus shows it: flystraight_latches == 0, end_tick at or near the 71-74 tick fuel ceiling, and
-- an hf series rotating through more than 128 facings (180 degrees). The analysis script
-- tools/autotest/analyze-javelin-probe.py reads exactly those three.
--
-- Lane 1 is the CONTROL: the Humvee never stops. 300 wdist per tick means "5 ticks before
-- intercept" is 1500 wdist, so the sweep is centred there.

local TRIGGERS = { 0, 900, 1200, 1500, 1800, 2100, 2400, 2700 }
local RunSeconds = 170
local MinMissiles = 40

local function halt(lane)
	lane.target.Stop()
end

WorldLoaded = function()
	if not JavelinProbe.Build(TRIGGERS, 3) then
		return
	end

	JavelinProbe.Drive(halt, RunSeconds, MinMissiles)
end
