-- AUTO TEST: a truck whose restock Centre is captured mid-drive must take nothing from it.
--
-- The defect (robustness scout 261007 §11): RestockSupply re-validated its host on arrival only for
-- IsDead / !IsInWorld, while its own comment claimed to guard "captured mid-drive". LOGISTICSCENTER is
-- capturable with OwnerLostAction: ChangeOwner, so a Centre that changes hands is neither dead nor out
-- of the world -- and the truck refilled from the ENEMY's stock. DeliverSupply, the documented mirror,
-- always carried the ownership term. Fix: both now go through SupplyTransferMath.HostStillServes.
--
-- WHY THE OWNER SETTER STAGES THE REAL THING: `Depot.Owner = x` calls Actor.ChangeOwner, the same hop a
-- capture takes. The actor object survives, stays in the world, and the truck's activity still holds a
-- reference to it -- which is exactly the state the missing term had to refuse. A truck cannot retake the
-- Centre by driving up to it: LC capture needs a Captures unit, and TRUK has none.
--
-- Setup is the cancel scenario's: 40 supply is below RestockThreshold (50) and the truck is on Auto, so the
-- restock drive starts on its own on tick one. At FlipAtTick the Centre goes to Russia.
--
-- PASS = the truck reaches the Centre, its restock errand has ENDED (idle, and stationary for longer than
--        the errand's settle could take), and the Centre's supply never dropped.
-- FAIL = the Centre's supply drops after the flip (pre-fix behaviour), or a SETUP guard trips.
-- A TIMEOUT means the truck never arrived: the arrival was never staged, so it is INCONCLUSIVE about the
-- fix, not a red on it.
--
-- WHEN THE PRE-FIX TRANSFER LANDS, which is what the pass has to wait out. RestockSupply transfers on
-- the tick after MoveTo completes AND a Wait(RestockWaitTicks = 25) runs out. Crossing DepotLine is
-- NOT that moment: the truck still has ~4 cells to drive (~55 ticks at Speed 75), and it can sit inside
-- its final cell for up to ~14 ticks before MoveTo completes. A first version started a 66-tick clock
-- at DepotLine and could pass at ~tick 66 while the broken build's transfer lands ~tick 80 -- green on
-- broken code. So the clock now starts only when the truck STOPS CHANGING CELL, runs StillTicks (75 >=
-- 14 + 25 + 1 with a wide margin), AND the truck must be idle -- direct evidence the RestockSupply
-- activity has ended, which is the only place a transfer can happen. Post-fix nothing re-queues: the
-- truck's own TryRestock only picks hosts with a.Owner == self.Owner, and none is left.
--
-- RED RUN. In engine/OpenRA.Mods.Common/Traits/SupplyTransferMath.cs, HostStillServes, change
--     return !hostIsDead && hostIsInWorld && validRelationships.HasRelationship(relationshipToHost);
-- to
--     return !hostIsDead && hostIsInWorld;
-- then `make all` and run this once. Expected: "fail: the Centre lost N supply AFTER it changed hands
-- (truck 40 -> M) -- the truck refilled from the enemy's stock", N = M - 40. Revert and rebuild after.
-- (The same edit also turns SupplyTransferMathTest.ACentreCapturedMidDriveNoLongerServes red.)

local DeadlineSeconds = 45
local FlipAtTick = 40     -- the drive is under way and nowhere near the depot
local DepotLine = 46      -- the 3x3 depot occupies x=50..52; a truck alongside it sits around x=49
local StillTicks = 75     -- stationary this long after arriving: >= in-cell drift (~14) + RestockWaitTicks (25) + 1
local StartCell = CPos.New(10, 16) -- Truck's map.yaml Location
local FlipGraceTicks = 25 -- the flip lands at FRAME END, not when the setter returns; see below

local flipped = false
local flipTick = nil
local setupError = nil
local depotAtFlip = -1
local truckAtFlip = -1
local lastCell = nil
local stillSince = nil

WorldLoaded = function()
	local russia = Player.GetPlayer("Russia")
	TestHarness.FocusBetween(Truck, Depot)
	TestHarness.Select(Truck)

	Trigger.AfterDelay(FlipAtTick, function()
		if Truck.IsDead or Depot.IsDead then return end

		if Truck.Location.X >= DepotLine then
			setupError = "fail: SETUP -- the truck reached the depot at x=" .. Truck.Location.X
				.. " before the Centre changed hands, so nothing was captured mid-drive"
			return
		end

		-- The drive must really be under way, or the Centre changes hands with no errand in flight and
		-- both builds time out identically -- a non-result dressed as INCONCLUSIVE.
		if Truck.IsIdle and Truck.Location == StartCell then
			setupError = "fail: SETUP -- at the flip (tick " .. FlipAtTick .. ") the truck was idle on its start cell "
				.. tostring(StartCell) .. ", so no restock drive was under way"
			return
		end

		depotAtFlip = Test.GetSupply(Depot)
		truckAtFlip = Test.GetSupply(Truck)
		Depot.Owner = russia
		flipTick = DateTime.GameTime
		flipped = true
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if setupError then return setupError end
		if Truck.IsDead then return "fail: the truck died" end
		if Depot.IsDead then return "fail: the depot died" end
		if not flipped then return false end

		-- THE OWNER SETTER IS DEFERRED. It calls Actor.ChangeOwner, which only queues ChangeOwnerSync as a
		-- FrameEndTask (Actor.cs:519-522), so in the tick the setter runs -- and this predicate can run in that
		-- same tick, after the flip trigger -- Owner still reads USA. The first run of this scenario failed
		-- SETUP on exactly that ("owner=FreadyFish": the USA seat's resolved display name). So wait a short
		-- grace for the flip to land, and compare InternalName (the PlayerReference name), never Name, which
		-- resolves to whoever occupies the seat.
		if Depot.Owner.InternalName ~= "Russia" then
			if DateTime.GameTime - flipTick <= FlipGraceTicks then return false end
			return "fail: SETUP -- the owner flip did not land within " .. FlipGraceTicks .. " ticks (owner="
				.. Depot.Owner.InternalName .. ")"
		end

		-- A Centre with nothing to give would make "unchanged" true of the broken build too.
		if depotAtFlip <= 0 then
			return "fail: SETUP -- the depot held " .. depotAtFlip .. " at the flip, so a refusal is unobservable"
		end

		local depotNow = Test.GetSupply(Depot)
		if depotNow < depotAtFlip then
			return "fail: the Centre lost " .. (depotAtFlip - depotNow) .. " supply AFTER it changed hands (truck "
				.. truckAtFlip .. " -> " .. Test.GetSupply(Truck) .. ") -- the truck refilled from the enemy's stock"
		end

		if Truck.Location.X < DepotLine then
			lastCell = nil
			stillSince = nil
			return false
		end

		-- Stationary clock: restarts every time the truck changes cell.
		if lastCell == nil or Truck.Location ~= lastCell then
			lastCell = Truck.Location
			stillSince = DateTime.GameTime
			return false
		end

		if DateTime.GameTime - stillSince < StillTicks or not Truck.IsIdle then
			return false
		end

		return "pass: truck idle at x=" .. Truck.Location.X .. " for " .. (DateTime.GameTime - stillSince)
			.. " ticks; Centre " .. depotAtFlip .. " -> " .. depotNow
			.. ", truck " .. truckAtFlip .. " -> " .. Test.GetSupply(Truck) .. " -- nothing taken from a captured Centre"
	end, "INCONCLUSIVE: the truck never reached the captured Centre and went idle there, so the arrival check "
		.. "was never shown to have run")
end
