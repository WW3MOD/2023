-- BALANCE: 3 AT infantry vs 1 MOVING humvee, ~10c0 cross-track.
--
-- Exists because the static model cannot see this. The combat-sim can compute
-- the inaccuracy roll against the humvee's 440x1000 hitshape, but not:
--   * lead prediction (Missile.cs CalculateLeadTarget) against a real mover,
--   * AimingDelay resetting to 15 on every target re-acquisition
--     (Armament.cs:347-350) as the humvee crosses in and out of stance scans,
--   * the terminal CloseEnough=298 sample against Speed=300 -- the missile can
--     straddle the check in one tick and overfly (Missile.cs:1179 PITFALL).
--
-- The humvee patrols perpendicular to the AT line, which is the worst case:
-- it maximises cross-track motion AND keeps the narrow 220 half-width axis
-- presented to the missile. Movement orders are re-issued so it never stops
-- to shoot back -- this measures whether AT can kill a mover, not who wins.
--
-- Expectation after ATGM PerCellIncrement 25 (was flat Absolute 512): at ~10c
-- the static model puts a single missile at 98.6% connect / 39.9% outright
-- kill, so 3 AT with 9 missiles between them should kill it well inside the
-- deadline. If this stalemates, lead/aim-reset is dominating and the static
-- model is understating the problem.

local patrolNorth = CPos.New(26, 8)
local patrolSouth = CPos.New(26, 26)

WorldLoaded = function()
	TestHarness.FocusBetween(AtA, Target)
	TestHarness.Select(AtA)

	local teamA = { AtA, AtB, AtC }
	local teamB = { Target }

	local toNorth = true
	local patrol
	patrol = function()
		if not Target.IsDead then
			Target.Move(toNorth and patrolNorth or patrolSouth)
			toNorth = not toNorth
		end
		Trigger.AfterDelay(8 * TestHarness.TicksPerSecond, patrol)
	end
	patrol()

	-- allowMove=false: the AT specialists hold their line and shoot from where
	-- they stand. Letting them chase a speed-150 humvee at speed 25 would
	-- measure pursuit, not gunnery.
	BalanceHarness.ForceEngage(teamA, teamB, false)

	BalanceHarness.RunDuel("3xAT.inf", teamA, "1xHumvee(moving)", teamB, 60)
end
