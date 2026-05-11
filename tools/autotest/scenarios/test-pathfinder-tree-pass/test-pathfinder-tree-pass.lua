-- AUTO TEST: Infantry must walk through a tree wall to reach a goal.
--
-- Layout: tree wall at x=30 covers the full playable y range, blocking the
-- direct east-west route for FOOT locomotor. The infantry can pass through
-- trees (Passable trait + FOOT locomotor's `Passes: tree`), so the path is
-- valid; HPF must not flag the tree-cells as blocking in its abstract graph.
--
-- Pre-fix bug: HierarchicalPathFinder.ActorIsBlocking only checked the
-- locomotor's Crushes set, not Passes. Passable actors with PassClasses=tree
-- got marked blocking, the abstract domain west-of-wall was severed from
-- east-of-wall, FindPath returned NoPath, and the move order was rejected —
-- the unit just stood there.
--
-- Post-fix: ActorIsBlocking checks PassableClasses (Passes ∪ Crushes), so
-- the tree wall is transparent in the abstract graph and the unit walks
-- through to reach the goal cell.

-- Infantry on FOOT walks ~32 ticks/cell on Clear (Speed=32, Cost=Clear=90).
-- 40 cells from start to goal → ~50s under ideal conditions; tree-cell terrain
-- and minor steering overhead push the realistic figure to ~75s. 90s leaves
-- enough margin without making a regression silently slow.
local DeadlineSeconds = 90
local GoalX, GoalY = 50, 16

WorldLoaded = function()
	TestHarness.FocusBetween(Infantry)
	TestHarness.Select(Infantry)

	Infantry.Move(CPos.New(GoalX, GoalY))

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Infantry.IsDead then
			return "fail: infantry died before reaching goal"
		end
		local loc = Infantry.Location
		return loc.X == GoalX and loc.Y == GoalY
	end, "Infantry did not reach (" .. GoalX .. "," .. GoalY .. ") within " .. DeadlineSeconds .. "s")
end
