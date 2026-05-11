-- AUTO TEST: Group Scatter (Shift-G) must NOT redistribute waypoints to a
-- unit whose only "order" is an autotarget engagement.
--
-- Setup: InfA on the west with a Russian enemy 4 cells away (in autotarget
-- range) — InfA has no human-issued orders, only its autotarget activity.
-- InfB sits next to InfA with two human-queued Move waypoints to the east.
--
-- Pre-fix bug: spread harvests InfA's autotarget AttackActivity as an
-- attack waypoint, sees 2 selected units and >=2 waypoints in the pool, and
-- assigns one of InfB's moves to InfA. InfA marches east, abandoning the
-- engagement that the human never told it to leave.
--
-- Post-fix: ExtractWaypoint filters AttackSource.AutoTarget activities, so
-- InfA contributes zero user waypoints and is excluded from the spread.
-- Only InfB receives the redistributed moves; InfA stays put.
--
-- Predicate: InfB walks east past x=18 (spread fired) AND InfA's column
-- stays west of x=14 (not redistributed). Pre-fix: InfA marches east too.

local WaypointE1 = CPos.New(20, 14)
local WaypointE2 = CPos.New(20, 20)

WorldLoaded = function()
	TestHarness.FocusBetween(InfA, Foe)

	-- Wait for InfA to acquire Foe via autotarget (scan interval is 16-32
	-- ticks). THEN queue InfB's moves and immediately trigger the spread,
	-- so InfB's first Move hasn't started walking — its Destination still
	-- points at WaypointE1 rather than at a path-step cell.
	Trigger.AfterDelay(50, function()
		InfB.Move(WaypointE1)
		InfB.Move(WaypointE2)
		Test.GroupScatter({ InfA, InfB })
	end)

	TestHarness.AssertWithin(20, function()
		if InfA.IsDead then
			return "fail: InfA died — test setup broken (Foe was supposed to be HoldFire)"
		end

		-- Pre-fix: InfA gets a Move waypoint to x=28 and walks east. After
		-- a few seconds of marching it's well past x=14. Treat anything at
		-- or past x=14 as a clear redistribution event.
		if InfA.Location.X >= 13 then
			return "fail: InfA was redistributed a Move waypoint despite having no human-issued order (autotarget should not feed the spread pool)"
		end

		-- Confirm InfB actually walked east — that's the legitimate spread
		-- behaviour. Without this check the test could pass spuriously if
		-- spread silently did nothing. InfB starts at x=10, waypoints are at
		-- x=20 — passing x=14 means it's clearly walking toward the goal.
		if InfB.Location.X >= 14 then
			return true
		end

		return false
	end, "InfB never reached the east-side waypoint — spread did not fire")
end
