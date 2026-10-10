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
-- PASS = the truck reaches the Centre, sits past the restock settle, and the Centre's supply never drops.
-- FAIL = the Centre's supply drops after the flip (pre-fix behaviour), or a SETUP guard trips.
-- A TIMEOUT means the truck never arrived: the arrival was never staged, so it is INCONCLUSIVE about the
-- fix, not a red on it.
--
-- RED RUN. In engine/OpenRA.Mods.Common/Traits/SupplyTransferMath.cs, HostStillServes, change
--     return !hostIsDead && hostIsInWorld && alliedWithHost;
-- to
--     return !hostIsDead && hostIsInWorld;
-- then `make all` and run this once. Expected: "fail: the Centre lost N supply AFTER it changed hands
-- (truck 40 -> M) -- the truck refilled from the enemy's stock", N = M - 40. Revert and rebuild after.
-- (The same edit also turns SupplyTransferMathTest.ACentreCapturedMidDriveNoLongerServes red.)

local DeadlineSeconds = 45
local FlipAtTick = 40     -- the drive is under way and nowhere near the depot
local DepotLine = 46      -- the 3x3 depot occupies x=50..52; a truck alongside it sits around x=49
local SettleSeconds = 4   -- RestockWaitTicks is 25; the transfer happens on the tick after that settle
local FlipGraceTicks = 25 -- the flip lands at FRAME END, not when the setter returns; see below

local flipped = false
local flipTick = nil
local setupError = nil
local depotAtFlip = -1
local truckAtFlip = -1
local arrivedAt = nil

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
			arrivedAt = nil
			return false
		end

		arrivedAt = arrivedAt or DateTime.GameTime
		if DateTime.GameTime - arrivedAt < TestHarness.TicksForSeconds(SettleSeconds) then
			return false
		end

		return "pass: truck at x=" .. Truck.Location.X .. " past the settle; Centre " .. depotAtFlip .. " -> " .. depotNow
			.. ", truck " .. truckAtFlip .. " -> " .. Test.GetSupply(Truck) .. " -- nothing taken from a captured Centre"
	end, "INCONCLUSIVE: the truck never reached the captured Centre, so the arrival check was never exercised")
end
