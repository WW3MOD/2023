-- AUTO TEST: the two directions of the Logistics Centre supply gesture, and the cursor that tells
-- them apart.
--
-- USER RULING 2026-08-30: "the default action for trucks when ordered to an LC should be to resupply
-- the LC, unless they are empty then they are themselves resupplied. If we use 'force-move' it could
-- be inverted, so force move to a LC means it resupplies the truck. Use 'Enter' mouse icon for
-- resupplying the truck and 'Wrench' icon for resupplying the LC." A follow-up ruling the same day
-- fixed "empty" at the truck's own RestockThreshold (50) rather than literally zero, so that a truck
-- holding 20 does not dribble it into a depot and immediately need to go and refill.
--
-- WHAT THIS PINS THAT THE UNIT TESTS CANNOT. SupplyTransferMathTest covers the arbitration as pure
-- arithmetic. Everything BETWEEN that function and the player is only reachable from here: that
-- DirectionFor derives hostAbsorbs/hostDocks/alliance from real actors, that the 6/7 priorities let
-- the intended targeter win, that the cursor fields are actually wired to those targeters, and that
-- the transfer moves supply between two real pools after a real drive.
--
-- WHY THE CURSOR IS A SOUND PROXY FOR THE ROUTING, so that most observables below can be taken with
-- ClickCursor (which issues NOTHING, and so leaves every truck standing still for the next reading)
-- rather than with ClickOrderGroup (which issues, and sends trucks driving mid-measurement):
--
--     goldwrench    <=> DeliverSupply   truck fills the Centre
--     enter         <=> Restock         Centre fills the truck
--     enter-blocked <=> Repair          neither supply direction claimed the click, so it fell
--                                       through to Repairable at priority 5, which on an UNDAMAGED
--                                       truck can do nothing and says so
--
-- The three are distinct for an undamaged truck, and the cursor and the order resolve through one
-- method (UnitOrderGenerator.OrderForUnit), so they cannot disagree. Every truck here is undamaged.
--
-- The one thing NOT covered: the arrival check in DeliverSupply. Staging a Centre the truck cannot
-- path to needs terrain this map does not have, so a delivery that teleports across water would
-- still pass. Covered as arithmetic in SupplyTransferMathTest.ArrivalTolerance* only.

local DeadlineSeconds = 40
local Threshold = 50          -- TRUK's SupplyProvider.RestockThreshold
local TruckCapacity = 750

local r = { read = false }

local function shown(v)
	if v == nil then
		return "<nil>"
	end

	if v == "" then
		return "<none>"
	end

	return v
end

-- Stage the truck's load, then read what a hover would show. Supply is set immediately before each
-- reading so the number driving the observable is visible at the call site.
local function cursorWith(load, host, modifiers)
	Test.SetSupply(Truck, load)
	return Test.ClickCursor({ Truck }, host, modifiers or "")
end

WorldLoaded = function()
	TestHarness.FocusBetween(Truck, StockedLC)

	Trigger.AfterDelay(50, function()
		-- Restated here rather than trusted from map.yaml: these three are what make each refusal
		-- below attributable to the pool it is testing.
		Test.SetSupply(StockedLC, 1000)   -- room to receive AND stock to give
		Test.SetSupply(FullLC, 2250)      -- no room
		Test.SetSupply(DrainedLC, 0)      -- no stock

		-- The four cases the ruling names, against a Centre that can go either way.
		r.loadedNormal = cursorWith(TruckCapacity, StockedLC)
		r.partLoadedNormal = cursorWith(TruckCapacity / 2, StockedLC)
		r.lowNormal = cursorWith(10, StockedLC)
		r.partLoadedForce = cursorWith(TruckCapacity / 2, StockedLC, "Ctrl")

		-- The threshold boundary, inclusive on the receiving side.
		r.atThreshold = cursorWith(Threshold, StockedLC)
		r.aboveThreshold = cursorWith(Threshold + 1, StockedLC)

		-- The host's pool decides too. Both of these drew a promising cursor and drove the truck the
		-- whole way for nothing before the headroom/stock terms existed.
		r.loadedOnFull = cursorWith(TruckCapacity, FullLC)
		r.lowOnDrained = cursorWith(10, DrainedLC)

		-- Force-move on an EXACTLY full truck: neither direction can act, so the click falls through
		-- to Repairable. This is the whole remaining reach of repair-by-click on a Centre.
		r.fullForce = cursorWith(TruckCapacity, StockedLC, "Ctrl")

		-- Issued LAST, because it is the only reading that moves anything: the real delivery, routed
		-- through the same order chain the mouse uses.
		Test.SetSupply(Truck, TruckCapacity)
		r.deliverOrder = Test.ClickOrderGroup({ Truck }, DrainedLC)[1]
		r.read = true
	end)

	-- Live values go to lua.log, never into a failure string: AssertWithin's third argument is
	-- evaluated once at registration, so anything interpolated there reports its pre-run value.
	local ticks = 0
	Trigger.AfterDelay(1, function()
		local report
		report = function()
			ticks = ticks + 1
			if ticks % 25 == 0 then
				print("[lc-refill] t=" .. ticks
					.. " loadedNormal=" .. shown(r.loadedNormal)
					.. " partLoadedNormal=" .. shown(r.partLoadedNormal)
					.. " lowNormal=" .. shown(r.lowNormal)
					.. " partLoadedForce=" .. shown(r.partLoadedForce)
					.. " atThreshold=" .. shown(r.atThreshold)
					.. " aboveThreshold=" .. shown(r.aboveThreshold)
					.. " loadedOnFull=" .. shown(r.loadedOnFull)
					.. " lowOnDrained=" .. shown(r.lowOnDrained)
					.. " fullForce=" .. shown(r.fullForce)
					.. " deliverOrder=" .. shown(r.deliverOrder)
					.. " | truck=" .. Test.GetSupply(Truck)
					.. " drainedLC=" .. Test.GetSupply(DrainedLC))
			end

			Trigger.AfterDelay(1, report)
		end

		report()
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Truck.IsDead or StockedLC.IsDead or FullLC.IsDead or DrainedLC.IsDead then
			return "fail: an actor died before the readings resolved -- nothing in this scenario should be shooting"
		end

		if not r.read then
			return false
		end

		-- 1. A loaded truck GIVES, and says so with the wrench.
		if r.loadedNormal ~= "goldwrench" then
			return "fail: a full truck hovering a Centre with room showed '" .. shown(r.loadedNormal)
				.. "' instead of goldwrench; the default click on a loaded truck must be a DELIVERY, which is the"
				.. " polarity this whole change exists to reverse"
		end

		if r.partLoadedNormal ~= "goldwrench" then
			return "fail: a half-loaded truck showed '" .. shown(r.partLoadedNormal)
				.. "' instead of goldwrench; 'has supply worth giving' is the test, not 'is full' -- gating delivery"
				.. " on a full load is what made the old delivery order unreachable for every truck that had served anybody"
		end

		-- 2. A truck with nothing worth giving RECEIVES, on the same plain click.
		if r.lowNormal ~= "enter" then
			return "fail: a truck holding 10 showed '" .. shown(r.lowNormal)
				.. "' instead of enter; below the threshold the same gesture must invert to the Centre serving the truck"
		end

		-- 3. Force-move inverts it.
		if r.partLoadedForce ~= "enter" then
			return "fail: force-move on a half-loaded truck showed '" .. shown(r.partLoadedForce)
				.. "' instead of enter; force-move must always mean the Centre serves the truck"
		end

		if r.partLoadedNormal == r.partLoadedForce then
			return "fail: the SAME truck showed '" .. shown(r.partLoadedNormal)
				.. "' with and without force-move; the modifier is what inverts the direction, so the two cannot look alike"
		end

		-- 4. The threshold boundary the user set, inclusive on the receiving side.
		if r.atThreshold ~= "enter" then
			return "fail: a truck holding EXACTLY the threshold showed '" .. shown(r.atThreshold)
				.. "' instead of enter; the ruling is 'at or under 50 receives', and an off-by-one here turns the last"
				.. " 50 supply of every truck into a dribble-and-refill loop"
		end

		if r.aboveThreshold ~= "goldwrench" then
			return "fail: a truck one above the threshold showed '" .. shown(r.aboveThreshold)
				.. "' instead of goldwrench; above 50 the truck gives"
		end

		-- 5. The host's pool decides too, in both directions.
		if r.loadedOnFull == "goldwrench" then
			return "fail: a loaded truck hovering a FULL Centre still promised a delivery (goldwrench)."
				.. " A Centre starts full, so this is the first thing a player tries -- and the transfer would move nothing"
		end

		if r.lowOnDrained == "enter" then
			return "fail: an almost-empty truck hovering a DRAINED Centre still promised service (enter);"
				.. " the Centre has nothing to give, so the truck would drive the whole way for nothing"
		end

		-- 6. Neither direction can act on a full truck under force-move, so the click must reach
		--    Repairable rather than being claimed and wasted.
		if r.fullForce ~= "enter-blocked" then
			return "fail: force-move on an exactly-full truck showed '" .. shown(r.fullForce)
				.. "' instead of enter-blocked; neither supply direction can act, so the click must fall through to"
				.. " Repairable at priority 5 -- a supply targeter that claims it silently vetoes repair"
		end

		-- 7. The routing, read as an order string rather than inferred from the cursor.
		if r.deliverOrder ~= "DeliverSupply" then
			return "fail: the delivery click produced order '" .. shown(r.deliverOrder)
				.. "' instead of DeliverSupply; the cursor and the order resolve through one method, so a mismatch here"
				.. " means the priority contest is going somewhere the cursor did not predict"
		end

		-- 8. And the transfer itself, after a real 20-cell drive.
		local delivered = Test.GetSupply(DrainedLC)
		if delivered < TruckCapacity then
			return false
		end

		if delivered > TruckCapacity then
			return "fail: the Centre gained more than the truck was carrying, so supply is being created"
		end

		return true
	end, "the Centre never received the truck's load")
end
