--[[
	A move order onto an UNREACHABLE cell must clamp to the nearest reachable cell.

	THE DISCRIMINATOR, and why it has two terms.

	Pre-fix, Mobile.NearestMoveableCell tests CanEnterCell && CanStayInCell with no
	reachability term, so a target inside a sealed pocket passes immediately, Move finds no
	path, and Move.Tick sets destination = ToCell and COMPLETES. The activity finishes
	cleanly and the unit is alive and idle at its start cell.

	So every cheap assertion passes on the bug:
	  - "did the activity finish?"        -> yes, it finished
	  - "is the unit alive?"              -> yes
	  - "did it move?" via distance alone -> 22 both before and after, if you only compare
	                                          against a fixed anchor rather than the start

	The two terms that DO separate them:
	  1. final distance to target < initial distance to target   (it got nearer)
	  2. final cell ~= initial cell                              (it actually travelled)

	Term 2 is the one that looks redundant and is not: the bug's signature is returning the
	START CELL, so a predicate that only asks "are we nearer" against a mis-chosen anchor,
	or that tolerates zero movement, scores the bug as healthy. Keep both.

	Geometry, measured with nav-guard before this was written (locomotor `wheeled`):
	  start  (21,30)  in the largest component (5331 cells)
	  target (43,36)  inside a 34-cell pocket, bbox x42..51 y36..47
	  nearest reachable cell to target: (41,34), chessboard distance 2
	  chessboard start->target: 22
	Fixed: ends ~2 from the target. Broken: stays at 22, on the start cell.

	Distances are chessboard (max of |dx|,|dy|) on integer cell coordinates — integer only,
	no floating point, so the verdict cannot drift between machines.
]]

local TargetCell = { X = 43, Y = 36 }

local function chebyshev(a, b)
	local dx = a.X - b.X
	local dy = a.Y - b.Y
	if dx < 0 then dx = -dx end
	if dy < 0 then dy = -dy end
	if dx > dy then return dx end
	return dy
end

WorldLoaded = function()
	local start = Rig.Location
	local startDist = chebyshev(start, TargetCell)

	TestHarness.FocusBetween(Rig)
	TestHarness.Select(Rig)

	-- Test.IssueMoveOrder, NOT Rig.Move. The Lua Move API queues the Move activity directly
	-- with evaluateNearestMovableCell FALSE, so NearestMoveableCell — the whole subject of
	-- this test — is never reached and the scenario is RED on the fixed build too. This was
	-- not theory: the first GREEN attempt failed for exactly that reason.
	Trigger.AfterDelay(5, function()
		Test.IssueMoveOrder(Rig, CPos.New(TargetCell.X, TargetCell.Y))
	end)

	-- 40 harness-seconds; the harness constant makes that 60 real seconds, ample for a
	-- 22-cell drive. Sized generously on purpose: this test is about WHETHER the unit
	-- relocates at all, not how fast.
	TestHarness.AssertWithin(40, function()
		if Rig.IsDead then
			return "fail: Rig died before it could move"
		end

		local here = Rig.Location
		local dist = chebyshev(here, TargetCell)

		-- Term 2 first: the bug returns the start cell, so this is the term that fails on it.
		if here.X == start.X and here.Y == start.Y then
			return false
		end

		-- Term 1: and it has to be nearer than it began, not merely elsewhere.
		if dist >= startDist then
			return false
		end

		return true
	-- The message states exactly the two things the predicate tests, and no more. It used to
	-- add "expected it to end about 2 cells from the target, at roughly (41,34)" — true of the
	-- fixed build, but NOT something asserted here, and a failure message that claims more
	-- than its assertion sends the next reader looking for a defect that was never measured.
	end, "Rig never clamped to a reachable cell: ordered to the unreachable ("
		.. TargetCell.X .. "," .. TargetCell.Y .. ") from (" .. start.X .. "," .. start.Y
		.. ") at chessboard distance " .. startDist
		.. ", it either never left its start cell or never ended nearer the target than it began.")
end
