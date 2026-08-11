-- AUTO TEST: a supply truck must be able to pick supplies back up off the ground.
--
-- The user asked for the inverse of the existing drop. Dropping is already both a deploy command
-- and a bot errand (DropsSupplyCache); there was no way to get the supply back into a truck at all.
-- The only recovery path in the shipped game is AbsorbsSupplyCache on a Logistics Centre, and an LC
-- is Prerequisites: ~disabled — it exists only as a Neutral capturable on three of the ten maps. So
-- on the other seven, supply put on the ground could never come back.
--
-- The click is issued through Test.ClickOrder rather than by naming an order, and that is the point
-- of the test as much as the transfer is: ClickOrder walks the same IIssueOrder/OrderPriority
-- contest UnitOrderGenerator does, so it measures where a real right-click on a crate is ROUTED.
-- Naming the order would pass even if the targeter never won the contest.
--
-- The verdict is "the crate is no longer in the world". That is sound here because the crate holds
-- 200, well inside the truck's 650 of headroom, so a correct pickup drains it to exactly 0 and its
-- own RemoveBelowSupply: 1 despawns it (SupplyProvider.cs:221) — nothing in the pickup path removes
-- it. And nothing ELSE on this map can drain it: there is no infantry anywhere to rearm off it, no
-- enemy to capture or destroy it, and no Logistics Centre to absorb it. A crate that disappears
-- here disappeared into the truck.
--
-- The x >= ArrivedLine term is not decoration. A Move to a cell with no path does not fail — the
-- pathfinder bails to NoPath and Move.Tick treats an empty path as arrival — so a transfer written
-- without an arrival check would fire with the truck still standing on its start cell. Requiring
-- the truck to be near the crate when it vanishes is what distinguishes "drove there and loaded it"
-- from "siphoned it across twenty cells".

local DeadlineSeconds = 40
local ArrivedLine = 26 -- crate is at x=30; the move stops within DropAtToleranceCells (2) of it

local issuedOrder = nil

WorldLoaded = function()
	TestHarness.FocusBetween(Truck, Cache)
	TestHarness.Select(Truck)

	-- A real right-click on the crate, resolved through the whole targeter chain.
	issuedOrder = Test.ClickOrder(Truck, Cache)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Truck.IsDead then return "fail: the truck died" end

		local crates = #Player.GetPlayer("USA").GetActorsByType("supplycache")
		if crates > 0 then return false end

		local x = Truck.Location.X
		if x < ArrivedLine then
			return "fail: the crate was drained with the truck still at x=" .. x
				.. " (expected x>=" .. ArrivedLine .. ") -- the transfer ran without the truck arriving"
		end

		return true
	end, "The truck never collected the ground cache -- right-clicking it issued '"
		.. tostring(issuedOrder) .. "', and the crate is still sitting on the map")
end
