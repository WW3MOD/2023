-- AUTO TEST (control): the dry break-off must spare a move whose PURPOSE is to stop being empty.
--
-- The rule the break-off implements is "cancel a move that is invalidated by being empty; never
-- cancel a move that exists to stop being empty". test-truck-dry-move-cancelled pins the first
-- clause. Without this one, the obvious over-broad implementation -- cancel any move on an empty
-- truck -- passes it, and quietly livelocks the restock: the drive is cancelled, the truck falls
-- idle, OnBecomingIdle queues the drive again, the next scan cancels it again, and the truck never
-- covers the forty cells to the depot. It would be permanently empty AND permanently busy, which is
-- worse than the bug being fixed.
--
-- Nothing is issued from here. An empty Auto-stance truck sends itself to the depot through the
-- shipped idle path, so the scenario measures the real chain end to end. The truck is empty for the
-- WHOLE drive, so every single dry scan along the way is a chance to wrongly cancel it -- which is
-- what makes a passive assertion meaningful here.

local DeadlineSeconds = 45
local DepotLine = 46 -- the 3x3 depot occupies x=50..52; a truck alongside it sits around x=49

WorldLoaded = function()
	TestHarness.FocusBetween(Truck, Depot)
	TestHarness.Select(Truck)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Depot.IsDead then return "fail: the depot died" end

		if #Player.GetPlayer("USA").GetActorsByType("truk") == 0 then
			return "fail: the truck evacuated instead of restocking -- its drive to the depot was "
				.. "cancelled by the dry break-off, which is the move that exists to stop it being empty"
		end

		if Truck.IsDead then return "fail: the truck died" end

		return Truck.Location.X >= DepotLine
	end, "The empty truck never reached the depot -- its restock drive is being cancelled and re-issued")
end
