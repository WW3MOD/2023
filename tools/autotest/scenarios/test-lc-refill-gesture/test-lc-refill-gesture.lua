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
-- TWO THINGS THIS SCENARIO DOES NOT COVER, stated here so a green run is not read as covering them:
--
--   * THE ARRIVAL CHECK in DeliverSupply. This map has no water and no wall, so the Centre is always
--     reachable — delete the guard and this scenario still passes. Only its arithmetic is pinned,
--     in SupplyTransferMathTest.ArrivalTolerance*.
--   * THE ALLIANCE RE-CHECK for a Centre captured mid-drive. Nothing here stages a capture, and
--     that line has never run.
--
-- THE CURSORS ARE ASSERTED BY NAME AND CANNOT BE SCREENSHOTTED, which is a property of the harness
-- and not a shortcut taken here. Test.ClickCursor RESOLVES what a hover would produce; it does not
-- move a pointer, and the harness has no hook that positions or renders one — so at capture time
-- there is no cursor on screen to photograph. No scenario in this repo screenshots a cursor, and the
-- two that verify cursors (test-order-cursor-and-force-fire, test-sr-evacuate-cursor) both do it by
-- walking the order pipeline exactly as this one does. That is sound rather than second-best: the
-- real UnitOrderGenerator.GetCursor calls CursorForOrders(OrdersForSelection(...)), which is
-- precisely what ClickCursor calls, through an overload deliberately sharing ResolveSelection with
-- the mouse path "so the two entry points cannot drift". What a screenshot would add is only that
-- the NAME resolves to the intended artwork, and goldwrench is already in live use elsewhere
-- (infantry.yaml:931) with [CursorReference] linting the name.
--
-- WHAT IT DOES REPORT BUT DOES NOT JUDGE: the truck evacuating after a complete delivery. That is
-- the user's ruling of 2026-08-30 (an empty truck has done its job; its value returns to the player,
-- as it already does for artillery and dry units), so it is INTENDED and is not asserted on either
-- way. It is observed and handed to Test.Pass as a note so the reading lands in the run's own
-- result.json rather than in the global lua.log, which carries no run identity and can only be tied
-- to a run by mtime.

local DeadlineSeconds = 90
local Threshold = 50          -- TRUK's SupplyProvider.RestockThreshold
local TruckCapacity = 750

-- How long to keep watching the emptied truck for the sale that ends its evacuation, and how often
-- to look. Budgeted in ticks; the cap is generous because the drive to the map edge is the variable
-- part and a truck still driving at the cap is itself a reportable reading, not a failure.
local EvacWatchCap = 500
local EvacPollTicks = 5
local EvacMidShotTicks = 100

-- PITFALL: players are NOT bare globals — only map ACTORS are. A bare `USA` is nil and throws.
local USA

local r = { read = false, settling = false }

-- PITFALL: a SOLD actor leaves the world without being "dead", and Test.GetSupply throws on it —
-- "Attempted to get trait from destroyed object". `IsDead` alone is NOT enough to guard the read,
-- which is what broke the first run of this scenario with screenshots: the periodic trace below
-- sampled the truck's supply every 25 ticks and blew up the moment the evacuation completed. Any
-- actor this scenario touches after the delivery can be gone, so every read goes through here.
-- Same guard demo-heli-lanes uses, for the same reason.
local function supplyOf(actor)
	if actor == nil or actor.IsDead or not actor.IsInWorld then
		return -1
	end

	return Test.GetSupply(actor)
end

local function gone(actor)
	return actor == nil or actor.IsDead or not actor.IsInWorld
end

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

local function passWithReading()
	Test.Pass("delivered=" .. r.deliveredAmount
		.. " centre=" .. supplyOf(DrainedLC)
		.. " | " .. r.fate
		.. " | cash " .. r.cashAtDelivery .. " -> " .. r.cashAfter
		.. " (refund " .. (r.cashAfter - r.cashAtDelivery) .. ")"
		.. " | evacuation is the 2026-08-30 ruling, reported not asserted")
end

-- Observe (never judge) what becomes of a truck that has just emptied itself into a Centre. Polls
-- for the sale that ends the evacuation rather than sampling at one fixed moment, because the drive
-- to the map edge is the variable part -- an earlier fixed 150-tick sample caught the truck still
-- mid-drive and so could say nothing about whether the refund ever arrived.
--
-- The reading goes to Test.Pass's note, which is surfaced in the verdict JSON: that lands it in the
-- run's own result.json rather than the global lua.log, which carries no run identity.
local function observeEvacuation(ticksWaited)
	-- KEEP THE CAMERA ON THE TRUCK. The first capture attempt left the camera at its opening focus
	-- (the midpoint of the truck's start and the Centre it was NOT sent to), so the evacuation drove
	-- out of frame and the two screenshots could not answer the question they were taken to answer:
	-- whether the departure and its refund are legible to a player. A screenshot of the place the
	-- subject used to be is not evidence about the subject.
	if not gone(Truck) then
		Camera.Position = Truck.CenterPosition
	end

	-- One frame partway through the drive, so "does it visibly leave" has a picture and not only a
	-- pair of endpoints.
	if ticksWaited == EvacMidShotTicks and not gone(Truck) then
		Test.Screenshot("03-evacuating",
			"expects: the emptied truck driving away from the Centre it just filled, toward the map"
			.. " edge. Look for whether anything marks it as leaving to be sold rather than merely moving")
	end

	if gone(Truck) then
		-- RotateToEdge ends in the sale, which removes the actor: the expected end state for a
		-- complete delivery under TRUK's Evacuate stance.
		r.fate = "truck SOLD after ~" .. ticksWaited .. " ticks of evacuating"
		r.cashAfter = USA.Cash
		Test.Screenshot("04-after-sale",
			"expects: no truck on the map; player cash risen by the evacuation refund. The question is"
			.. " whether anything on screen connects this sale to the delivery the player just ordered")
		Trigger.AfterDelay(EvacPollTicks, passWithReading)
		return
	end

	if ticksWaited >= EvacWatchCap then
		local moved = TestHarness.CellDrift(r.truckX, r.truckY, Truck.Location.X, Truck.Location.Y)
		r.fate = "truck STILL DRIVING after " .. ticksWaited .. " ticks, at "
			.. Truck.Location.X .. "," .. Truck.Location.Y
			.. " (" .. moved .. " cells from where it delivered), supply=" .. supplyOf(Truck)
		r.cashAfter = USA.Cash
		Test.Screenshot("04-still-evacuating",
			"expects: an empty truck driving toward the map edge, having not yet reached it")
		Trigger.AfterDelay(EvacPollTicks, passWithReading)
		return
	end

	Trigger.AfterDelay(EvacPollTicks, function() observeEvacuation(ticksWaited + EvacPollTicks) end)
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	TestHarness.FocusBetween(Truck, StockedLC)

	-- Zoomed in for the capture frames: at default zoom a truck is a few pixels on a 66x34 map and
	-- the first attempt's screenshots were unreadable as evidence about it.
	Test.SetZoom(2)

	-- EVERY READING BELOW HAPPENS IN ONE TICK, and that is load-bearing rather than incidental.
	-- No time passes between the SetSupply that stages a case and the ClickCursor that reads it, nor
	-- between one case and the next -- so no aura, no serve cadence and no idle path can move either
	-- pool underneath the walk. In particular the truck cannot be nudged across the 50 threshold
	-- between two beats, which would silently change what the boundary observables mean.
	--
	-- ClickCursor is what makes this possible: it issues NOTHING (it resolves the targeter chain and
	-- returns the cursor), so ten readings leave the world exactly as they found it. If a future
	-- edit needs a reading that ISSUES, it belongs after this block, not inside it -- and the one
	-- that does (the delivery) is deliberately last.
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

		-- THE PRECONDITION THE WHOLE CURSOR METHOD RESTS ON, recorded at the reading beat rather than
		-- reasoned about. Every "enter" assertion below is only evidence for Restock while the truck
		-- is UNDAMAGED: Repairable's EnterCursor is also "enter", and it is only the undamaged case
		-- that pushes Repairable to "enter-blocked" and makes the three outcomes distinguishable. A
		-- chipped truck would turn a broken Restock into a passing "enter" reading. Nothing here
		-- shoots and the enemy SUPPLYROUTE is unarmed, so this should hold trivially -- which is
		-- exactly why it is worth one line to check rather than to assume.
		r.truckIntact = (not gone(Truck)) and Truck.Health == Truck.MaxHealth

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
					.. " | truck=" .. supplyOf(Truck)
					.. " drainedLC=" .. supplyOf(DrainedLC))
			end

			Trigger.AfterDelay(1, report)
		end

		report()
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		-- THE TRUCK IS EXPECTED TO DISAPPEAR ONCE THE DELIVERY LANDS. A complete delivery empties it,
		-- and an empty truck evacuates and is sold (user ruling 2026-08-30) -- so this guard must stop
		-- applying to the truck the moment the settle window opens, or the scenario fails the run for
		-- the intended behaviour it exists to observe. The Centres are guarded throughout: none of
		-- them has any reason to die at any point.
		if StockedLC.IsDead or FullLC.IsDead or DrainedLC.IsDead then
			return "fail: a Logistics Centre died -- nothing in this scenario should be shooting"
		end

		if not r.settling and gone(Truck) then
			return "fail: the truck died before the delivery landed -- nothing in this scenario should be shooting"
		end

		if not r.read then
			return false
		end

		-- Checked BEFORE any cursor assertion, because it is what gives them their meaning.
		if not r.truckIntact then
			return "fail: the truck was damaged at the reading beat, so 'enter' no longer distinguishes"
				.. " Restock from Repairable -- every cursor observable below is void, and a green run"
				.. " here would have proved nothing"
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
		-- ASSERTED POSITIVELY, not as "anything but goldwrench". A refusal here means neither supply
		-- direction claimed the click, so it falls through to Repairable at priority 5, which on an
		-- undamaged truck is exactly enter-blocked. Testing for the absence of goldwrench would also
		-- accept an empty cursor -- i.e. it would pass with the gesture ripped out entirely, which is
		-- the shape of vacuous assertion this scenario is being audited for.
		if r.loadedOnFull ~= "enter-blocked" then
			return "fail: a loaded truck hovering a FULL Centre showed '" .. shown(r.loadedOnFull)
				.. "' instead of enter-blocked. A Centre starts full, so this is the first thing a player tries;"
				.. " the delivery must be refused and the click must reach Repairable rather than being claimed"
		end

		if r.lowOnDrained ~= "enter-blocked" then
			return "fail: an almost-empty truck hovering a DRAINED Centre showed '" .. shown(r.lowOnDrained)
				.. "' instead of enter-blocked; the Centre has nothing to give, so the truck would drive the whole"
				.. " way for nothing and the click must be refused"
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

		-- NOT `return true`. Passing here would exit the game the instant the supply lands, which is
		-- the one moment before the thing worth watching happens. Hand off to the settle window
		-- instead, which observes the evacuation and passes WITH a note; if that path breaks, this
		-- predicate keeps returning false and AssertWithin fails on the deadline, which is the honest
		-- outcome rather than a silent pass.
		if not r.settling then
			r.settling = true
			r.deliveredAmount = delivered
			r.cashAtDelivery = USA.Cash
			-- Scalars, not the CPos: the truck may be sold before the watch ends, and reading X/Y off
			-- a stored handle then is a question about a removed actor.
			r.truckX = Truck.Location.X
			r.truckY = Truck.Location.Y

			-- On the truck, not on the opening focus: this frame is evidence about the truck.
			Camera.Position = Truck.CenterPosition
			Test.Screenshot("02-delivered",
				"expects: the truck standing at the Centre it just filled, Centre at 750, truck empty."
				.. " This is the frame immediately before it leaves")

			observeEvacuation(0)
		end

		return false
	end, "the Centre never received the truck's load")
end
