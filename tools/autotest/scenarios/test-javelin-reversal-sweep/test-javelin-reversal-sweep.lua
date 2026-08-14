-- AUDIT 6.1 — "shallow Javelin vs. reversing Humvee".
--
-- Question: can a Javelin miss its target, survive, and stay in the world?
-- The audit (WORKSPACE/audit/javelin-terminal-geometry.md) says survival needs two things at once:
--
--   (A) the swept path stays >= CloseEnough (298) from the AIM point every tick, and
--   (B) the flyStraight latch is taken at vFacing >= 0, so the frozen heading is level or
--       climbing rather than descending into the dirt.
--
-- (B) is bought by geometry: engaging at 4-6 cells keeps the missile in cruise for only a handful
-- of ticks, so it enters Hitting at ~150-400 above terrain instead of 500-800 and the terminal
-- pitch is 2-3 degrees, which cannot reach the ground inside the fuel budget.
--
-- (A) is bought by a moving target. The aim point carries a lead term of
-- targetVelocity * (D / missileSpeed); reversing a 150-speed Humvee swings that aim point by up to
-- 2*150*D/300, against a lateral correction budget of only D^2/1630. The audit's table says the
-- swing wins for D <= 1500. Each lane holds one reversal range so a lane's launch position
-- identifies which D produced each record.
--
-- Lane 1 is a CONTROL: same geometry, same patrolling Humvee, no reversal ever ordered. It is what
-- makes a positive result readable. If the control lane also produces the survival fingerprint,
-- the reversal is not the cause and the sweep has measured nothing.

-- Audit 6.1: sweep reversal timing over 800-2000 wdist in ~200-wdist steps.
local TRIGGERS = { 0, 800, 1000, 1200, 1400, 1600, 1800, 2000 }
local RunSeconds = 170
local MinMissiles = 40

local function reverse(lane)
	lane.moveDir = -lane.moveDir
	lane.target.Move(CPos.New(lane.trackX, lane.row + lane.half * lane.moveDir))
end

WorldLoaded = function()
	if not JavelinProbe.Build(TRIGGERS, 3) then
		return
	end

	JavelinProbe.Drive(reverse, RunSeconds, MinMissiles)
end
