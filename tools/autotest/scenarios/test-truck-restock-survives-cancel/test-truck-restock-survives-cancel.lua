-- AUTO TEST: cancelling a truck's restock drive must not disable the truck.
--
-- The defect (filed in WORKSPACE/bugs/discovered.md as [high]): SupplyProvider.TryRestock set a
-- private `restocking` bool and queued MoveTo + Wait + CallFunc, and the flag was cleared at exactly
-- ONE line -- inside that tail CallFunc. Activity.Cancel nulls NextActivity, so ANY pre-emption (a
-- player Move, an evacuation, a bot re-task) drops the tail and the flag latches TRUE FOREVER. Since
-- both CanServeNow and ShouldSelfRestock are gated on it, a truck interrupted once on the way to an
-- LC stops serving infantry, stops being chosen as a resupply destination, and never restocks again
-- -- while looking perfectly healthy, because it still has supply, its bar is amber not red, and
-- CountsAsEmpty is false so nothing disposes of it either.
--
-- WHAT IS MEASURED, and why this observable and not "does it still serve". The latch is one flag
-- read by both gates, so either consequence pins it; restocking is by far the cleaner of the two to
-- stage. It needs no soldiers, no unaffordable customer and no timing -- 40 supply is below
-- RestockThreshold, so the drive starts on the first tick by itself -- whereas arranging for a truck
-- to be serveable-but-not-serving requires a second soldier type and a carefully-timed arrival, and
-- every one of those moving parts is another way for a red to mean something else.
--
-- The pre-emption is an ordinary player Move order, i.e. the most mundane thing that can happen to a
-- truck, which is the point: this is not an exotic failure.
--
-- Note that in the fixed build the player's Move barely runs at all -- the moment the truck is no
-- longer restocking, Tick sees it is still below the threshold and re-queues the drive, which cancels
-- the Move in turn. That is correct and pre-existing (an Auto truck below its reserve insists on
-- refilling); the assertion is deliberately about REACHING the depot, not about which order won.

local DeadlineSeconds = 45
local PreemptAtTick = 40 -- ~2.4s: long enough that the drive is genuinely under way
local DepotLine = 46 -- the 3x3 depot occupies x=50..52; a truck alongside it sits around x=49

local preempted = false

WorldLoaded = function()
	TestHarness.FocusBetween(Truck, Depot)
	TestHarness.Select(Truck)

	Trigger.AfterDelay(PreemptAtTick, function()
		if Truck.IsDead then return end

		preempted = true
		Test.IssueMove(Truck, CPos.New(10, 20))
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Truck.IsDead then return "fail: the truck died" end
		if Depot.IsDead then return "fail: the depot died" end

		if not preempted then
			-- Guard against a green that never staged the bug at all. If the truck had already got
			-- to the depot before the interruption, nothing was ever cancelled.
			if Truck.Location.X >= DepotLine then
				return "fail: SETUP -- the truck reached the depot at x=" .. Truck.Location.X
					.. " before the move order pre-empted it, so no restock drive was cancelled"
			end

			return false
		end

		return Truck.Location.X >= DepotLine
	end, "The truck never went back to the depot after its restock drive was cancelled -- "
		.. "the restocking state latched and it will not restock, or serve, for the rest of the match")
end
