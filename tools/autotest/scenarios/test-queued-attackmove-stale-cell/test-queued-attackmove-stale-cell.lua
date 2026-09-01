-- AUTO TEST: a shift-queued attack-move must resolve its destination when the
-- move STARTS, not when the player clicked.
--
-- THE SEQUENCE. AttackMove.ResolveOrder runs the moment the order arrives, even
-- for a queued one — the activity is constructed there and then sits in the
-- queue behind whatever is already running. So anything the resolver decides is
-- decided against the world as it stood at the click, and acted on later.
-- Mobile.ResolveOrder, the sibling player order, hands the raw cell to Move and
-- lets Move.OnFirstRun relocate it at activity start instead.
--
-- WHAT MAKES THE TWO ANSWERS DIFFERENT HERE. A solid 4x4 block of churches
-- covers x29..32, y15..18. The ordered cell (30,16) is inside it, and so is
-- every cell of Chebyshev ring 1 around it, so while the churches stand
-- NearestMoveableCell can do no better than ring 2. The churches are then
-- removed outright while the unit is still walking the first leg. From that
-- moment the ordered cell is plain open ground:
--   * decided at issue time -> a ring-2 cell, roughly two cells short;
--   * decided at move start -> the ordered cell itself.
--
-- WHAT WOULD MAKE THIS FAIL. On the pre-fix build it fails, and the failure
-- names the cell the unit actually stopped on — that observation is the whole
-- point of the scenario and it is the control for the green. Note the orders go
-- through Test.IssueMoveOrder / Test.IssueAttackMove and NOT through the
-- activity-direct Lua `unit.Move` / `unit.AttackMove`, which construct their
-- activities by hand and never enter the order layer at all. A version of this
-- written against those would be red on both builds with the code under test
-- never executed (see the PITFALL on MobileProperties.Move).

local FirstLegCell = { X = 24, Y = 16 }
local OrderedCell = { X = 30, Y = 16 }

local DeadlineSeconds = 25

-- Ticks, not seconds. Only has to land after the orders have been resolved
-- (tick 1-2) and before the unit has walked the 12 cells of the first leg,
-- which no ground vehicle in the mod does this quickly.
local ClearBlockersAtTick = 10

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, Blocker1)
	TestHarness.Select(Hunter)

	-- Same tick, same order layer a player's clicks use. The second is QUEUED,
	-- which is the entire premise: it resolves now and runs later.
	Test.IssueMoveOrder(Hunter, CPos.New(FirstLegCell.X, FirstLegCell.Y), false)
	Test.IssueAttackMove(Hunter, CPos.New(OrderedCell.X, OrderedCell.Y), true)

	Trigger.AfterDelay(ClearBlockersAtTick, function()
		-- If the first leg were already done the queued activity would have
		-- started against the blocked world and there would be nothing stale
		-- under test. Say so rather than reporting a verdict about it.
		if Hunter.IsDead or Hunter.Location.X >= FirstLegCell.X then
			Test.Fail("setup invalid: the first leg finished before the blockers were cleared, " ..
				"so the queued attack-move started while they still stood and no stale " ..
				"destination was ever exercised")
			return
		end

		for _, b in ipairs({ Blocker1, Blocker2, Blocker3, Blocker4 }) do
			if not b.IsDead then
				b.Destroy()
			end
		end
	end)

	local ticks = 0
	local budget = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond) - 2

	TestHarness.AssertWithin(DeadlineSeconds, function()
		ticks = ticks + 1

		if Hunter.IsDead then
			return "fail: Hunter died first"
		end

		if Hunter.Location.X == OrderedCell.X and Hunter.Location.Y == OrderedCell.Y then
			return true
		end

		if ticks >= budget then
			return "fail: Hunter stopped at (" .. Hunter.Location.X .. "," .. Hunter.Location.Y ..
				") rather than the ordered cell (" .. OrderedCell.X .. "," .. OrderedCell.Y ..
				"). The queued attack-move walked to the cell that was reachable when the order " ..
				"was issued, while the churches still stood — not the one reachable when the " ..
				"move actually started."
		end

		return false
	end, "Hunter never reached the ordered cell")
end
