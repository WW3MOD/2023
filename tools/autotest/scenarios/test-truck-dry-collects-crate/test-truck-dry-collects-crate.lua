-- AUTO TEST (control): the dry break-off must spare a crate-collection errand.
--
-- The second half of "never cancel a move that exists to stop being empty", and the half that is
-- easy to miss, because the collection order was added on this same branch. An EMPTY truck sent to
-- fetch a crate is the natural use of that order -- it is what a player does the moment a truck runs
-- out -- so an implementation that cancels any move on an empty truck would make the new order
-- useless in exactly the situation it was built for, and would sell the truck instead.
--
-- The truck is on TRUK's shipped Evacuate stance, so the disposition competing with the errand is
-- the strongest one there is: rotate to the map edge and sell. It is empty from tick 0 and stays
-- empty for the whole twenty-cell drive, so every dry scan along the way is a chance to cancel.
--
-- Verdict: the crate is gone (drained into the truck, then despawned by its own RemoveBelowSupply)
-- AND the truck is still on the map. Both halves are needed -- a truck that evacuated with the crate
-- untouched also leaves no truck, and a crate that vanished with no truck left would be the failure
-- reported as a pass.

local DeadlineSeconds = 40

local issuedOrder = nil

WorldLoaded = function()
	TestHarness.FocusBetween(Truck, Cache)
	TestHarness.Select(Truck)

	issuedOrder = Test.ClickOrder(Truck, Cache)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		local trucks = #Player.GetPlayer("USA").GetActorsByType("truk")
		if trucks == 0 then
			return "fail: the truck evacuated instead of collecting the crate -- the break-off "
				.. "cancelled an errand whose whole purpose was to stop it being empty"
		end

		if Truck.IsDead then return "fail: the truck died" end

		return #Player.GetPlayer("USA").GetActorsByType("supplycache") == 0
	end, "The empty truck never collected the crate -- right-clicking it issued '"
		.. tostring(issuedOrder) .. "'")
end
