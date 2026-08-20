-- ATGM HIT RATE vs HUMVEE MOTION STATE.
--
-- This rig asks a different question from the four javelin scenarios it shares a library with.
-- Those hunt a missile that misses and SURVIVES; they sweep a perturbation RANGE and their
-- verdict is a flight-outcome fingerprint. 556 flights answered that question with a flat zero.
--
-- The open question is a RATE: how often does an ATGM actually damage a humvee, and how much of
-- the variation is explained by what the humvee was doing at the moment of intercept? So the swept
-- axis here is MOTION STATE, not range. Every lane fires at the same trigger range and each one
-- pins a different manoeuvre, so a lane's launcher position identifies its condition.
--
-- WHAT THE VERDICT DOES AND DOES NOT MEAN. As with the other four, the answer is in
-- result.missiles.jsonl and NOT in the pass/fail line. A pass here means only "the rig fired
-- enough missiles to be worth analysing". Read it with:
--
--     tools/autotest/analyze-atgm-hit-rate.py <run-dir> --by-launcher
--
-- which reports landed% and killed% per launcher cell, i.e. per condition.
--
-- WHY THESE FOUR CONDITIONS. tools/combat-sim/scripts/atgm-terminal-hit-rate.py simulates the
-- terminal geometry from the shipped rules and predicts a large spread across exactly these
-- states -- roughly 77% kill against a stationary humvee falling to ~52% against one running in a
-- straight line at full speed, with turning near 55%. That prediction is SIMULATED and has never
-- been measured. This rig exists to confirm or destroy it. If the measured spread is flat, the
-- simulation's terminal model is wrong and every number derived from it should be discarded.
--
-- LANE MAP (JavelinProbe.LaneCell: lanes 1-4 are column A at launcher x=4, 5-8 column B at x=34):
--   lane 1  CONTROL      never perturbed -- the humvee patrols across the line of sight, which IS
--                        the "straight line at speed" condition. It is also the baseline every
--                        other lane must be compared against; a spread that shows up in the
--                        perturbed lanes but not against this one has measured nothing.
--   lanes 2,3  STOPPED   Stop() at the trigger -- the stationary condition.
--   lanes 4,5  REVERSED  flip the patrol leg -- the aim point's lead term swings hardest here.
--   lanes 6,7,8  TURNING drive to a cell off the patrol axis, forcing a sustained turn rather
--                        than the instantaneous velocity flip a reversal produces.
--
-- TRIGGER RANGE. 1400 wdist for every perturbed lane. The audit's correction-budget arithmetic
-- (D^2/1630 against a swing of 2*Vt*D/300) puts 1400 just inside the range where the missile
-- cannot null the swing, so the manoeuvre lands with maximum effect and the conditions separate as
-- far as they are going to. It is deliberately NOT swept: with 8 lanes there is budget for one
-- axis, and motion state is the one that has never been measured.
--
-- KNOWN RISKS -- this scenario has NOT been run. Read before trusting a green.
--   * The library only fires a perturbation when the target is already moving at >= 80 wdist/tick
--     (javelin-probe-lib.lua, the `atSpeed` gate). A humvee that has just been stopped or has just
--     turned may not re-reach that speed before the next missile arrives, so the STOPPED and
--     TURNING lanes may perturb less often than the reversal lanes. `perturbs` is reported per
--     lane in the verdict note; if a lane shows near-zero, its rate is measuring the control
--     condition and must not be read as its nominal one.
--   * A turn ordered off the patrol axis leaves the humvee off-track. The library's patrol() only
--     re-issues a Move when the unit is Idle, so it recovers on the next leg, but the engagement
--     range for the shot in between is outside the audit's 4c0-6c0 band. That widens the range
--     spread on the TURNING lanes specifically.
--   * The library comment on spawnTarget still says the humvee is left at "its shipped 8000 HP".
--     That is stale -- Health.HP is 4000 as of ff14ece3 -- so targets die to a wider band of
--     impacts than the comment implies and respawn more often. Only the cadence changes, not the
--     measurement.

local TRIGGER = 1400
local TRIGGERS = { 0, TRIGGER, TRIGGER, TRIGGER, TRIGGER, TRIGGER, TRIGGER, TRIGGER }
local RunSeconds = 170
local MinMissiles = 40

-- Off-axis cells for the turning lanes. Two cells downrange of the patrol track keeps the humvee
-- clear of the launcher's MinRange (3c0) and well inside the map bounds in both columns.
local TURN_OFFSET = 2

local function manoeuvre(lane)
	local i = lane.index
	if i == 2 or i == 3 then
		lane.target.Stop()
	elseif i == 4 or i == 5 then
		lane.moveDir = -lane.moveDir
		lane.target.Move(CPos.New(lane.trackX, lane.row + lane.half * lane.moveDir))
	else
		-- Drive to a cell off the patrol axis: the humvee must turn to reach it, so its velocity
		-- vector rotates over several ticks instead of flipping in one. That is a different shape
		-- of lead-term error from a reversal and the simulation predicts a different hit rate.
		lane.target.Move(CPos.New(lane.trackX + TURN_OFFSET,
			lane.row + lane.half * lane.moveDir))
	end
end

WorldLoaded = function()
	if not JavelinProbe.Build(TRIGGERS, 3) then
		return
	end

	JavelinProbe.Drive(manoeuvre, RunSeconds, MinMissiles)
end
