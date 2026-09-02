-- AUTO TEST: a truck ordered to restock at a Logistics Centre it cannot reach must take NOTHING.
--
-- THE DEFECT THIS EXISTS FOR. RestockSupply transferred on activity completion with no arrival
-- check, while its documented mirror DeliverSupply has carried one since DeliverSupply.cs:127. A
-- Move to a cell with no route does not FAIL: PathFinder bails to NoPath and Move.Tick treats an
-- empty path as arrival (Move.cs:173-177), setting destination to the current cell and completing in
-- about two ticks. So the errand "completed" at the truck's own cell and the transfer ran from
-- wherever the truck happened to be standing — draining a Centre from anywhere on the map.
--
-- WHY THIS SCENARIO HAD TO EXIST. SupplyTransferMathTest pins the ARITHMETIC: that
-- AmountToRestock(arrived=false, ...) is 0. It cannot pin the WIRING. Replace the four lines in
-- RestockSupply.Tick that compute `arrived` with a literal `true` and all 2075 unit tests still
-- pass. That sabotage is exactly the RED arm for this scenario, and this file is the only thing in
-- the repo that can tell the difference.
--
-- THE OBSERVABLE IS AN EQUALITY, NOT AN INEQUALITY, and that is deliberate. "The walled Centre lost
-- less than the open one" would be satisfied by a partial transfer, by a slow transfer, and by a
-- truck that set off late — none of which is the property under test. The property is that NOTHING
-- moves, so the assertion is that the Centre's pool is the number it started with, to the unit, for
-- the whole run. The lowest value it ever reaches is latched every tick, so a transient dip that
-- recovered would still be caught.
--
-- WHAT WOULD MAKE THIS PASS WHILE PROVING NOTHING — the question the harness doc says to ask of
-- every green, answered rather than gestured at.
--
--   * THE TRUCK NEVER SETS OFF AT ALL. This is the big one, and it is why the southern lane exists.
--     A scenario with only the walled lane reports "Centre untouched" if the order was never issued,
--     if the truck spawned wrong, if the click resolved to some other targeter, or if the map failed
--     to load its actors — all indistinguishable from a working guard. The open lane is the same
--     truck at the same supply, the same distance from an identical Centre, ordered on the same
--     tick; it must complete a real 21-cell drive and a real transfer before this scenario is
--     allowed to pass. If the mechanism is dead the open lane times out and the run FAILS. Note the
--     direction of that failure: a broken control cannot produce a false green here, only a red.
--   * THE ORDER RESOLVED TO SOMETHING OTHER THAN RESTOCK. A truck above its RestockThreshold
--     delivers instead of receiving, and a delivery into a full Centre moves nothing either — which
--     would look identical at the Centre's pool. Both lanes therefore assert the order string is
--     literally "Restock", read back from the order chain rather than assumed from the supply level.
--   * AN EARLIER GUARD REFUSED IT INSTEAD OF THE ONE UNDER TEST. Walked deliberately, because a
--     scenario green for the wrong reason is the failure mode this class of test is prone to.
--     RestockSupply.Tick has exactly three earlier exits: IsCanceling, host dead/not-in-world, and
--     hostProvider == nil. None can fire here — nothing cancels the order, nothing shoots (the only
--     enemy actor is an unarmed SUPPLYROUTE with NoAutoTarget), and a LOGISTICSCENTER always carries
--     a SupplyProvider. Upstream of the activity, ResolveDirection is pure arithmetic over the two
--     pools and has no reachability term at all, so it cannot refuse a click for being walled in.
--     The open lane is the positive control for that whole chain: it proves those three exits let a
--     restock through, so the northern lane's null result is attributable to the arrival guard.
--   * THE WALL DID NOT ACTUALLY BLOCK. Then the northern truck arrives and transfers, and the run
--     fails on the equality — again a red, not a false green. The reverse error (a wall that blocks
--     the OPEN lane too) fails on the control. Both mistakes are loud. See map.yaml on why BRIK is
--     the only wall in this mod that can do this job.
--
-- EXECUTION MARKER. A Lua load abort writes `status: fail` with no way to tell it from a real RED.
-- Every outcome this file can produce — pass, fail, and the watchdog timeout — carries the token
-- "[restock-arrival] stage=N" with live values interpolated at call time. A result.json whose reason
-- does NOT contain that token means this script never ran, and the run says nothing about the guard.
-- The watchdog exists for the same reason: AssertWithin's third argument is evaluated EAGERLY at
-- registration, so a timeout message cannot carry live numbers. The watchdog fires just inside the
-- deadline and calls Test.Fail with a string built at that moment.

local DeadlineSeconds = 90            -- TestHarness.TicksPerSecond is 25, so 2250 ticks
local WatchdogTicks = 2100            -- inside the deadline above, so this reports before it
local ReportEvery = 50

local TruckStart = 50                 -- exactly TRUK's SupplyProvider.RestockThreshold
local TruckCapacity = 750             -- TRUK SupplyProvider.TotalSupply
local CentreStart = 2250              -- LOGISTICSCENTER SupplyProvider.TotalSupply
local ExpectedTransfer = TruckCapacity - TruckStart          -- 700
local OpenExpectedAfter = CentreStart - ExpectedTransfer     -- 1550

-- The walled Centre's centre cell, which is what RestockSupply drives at: a 3x3 at 30,10 occupies
-- 30..32 x 10..12. Stated so the failure text can report the gap the guard was measuring.
local WalledCentreCell = "31,11"
local ToleranceCells = 3              -- footprint radius 1 + DropAtToleranceCells 2

local r = {
	stage = 0,
	walledMin = CentreStart,
	orderWalled = nil,
	orderOpen = nil,
	controlDone = false,
	breach = nil,
	ticks = 0,
}

-- PITFALL: a SOLD actor leaves the world without being "dead", and Test.GetSupply throws on it
-- ("Attempted to get trait from destroyed object"). IsDead alone is NOT enough. The northern truck
-- is expected to be disposed of eventually — it is stuck at 50 supply, which it cannot spend, so
-- DropsSupplyCache's evacuate path will eventually sell it — and every read here must survive that.
local function gone(actor)
	return actor == nil or actor.IsDead or not actor.IsInWorld
end

local function supplyOf(actor)
	if gone(actor) then
		return -1
	end

	return Test.GetSupply(actor)
end

local function whereIs(actor)
	if gone(actor) then
		return "<gone>"
	end

	return actor.Location.X .. "," .. actor.Location.Y
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

-- Every outcome routes through here, so the marker cannot be omitted by accident.
local function state()
	return "[restock-arrival] stage=" .. r.stage
		.. " t=" .. r.ticks
		.. " | WALLED centre=" .. supplyOf(WalledLC) .. " (min seen " .. r.walledMin
		.. ", started " .. CentreStart .. ")"
		.. " truck=" .. supplyOf(TruckWalled) .. "@" .. whereIs(TruckWalled)
		.. " order=" .. shown(r.orderWalled)
		.. " | OPEN centre=" .. supplyOf(OpenLC)
		.. " truck=" .. supplyOf(TruckOpen) .. "@" .. whereIs(TruckOpen)
		.. " order=" .. shown(r.orderOpen)
end

WorldLoaded = function()
	r.stage = 1
	TestHarness.FocusBetween(TruckWalled, WalledLC)
	Test.SetZoom(2)

	-- Proves the script ran even if everything after this throws: lua.log at 0 bytes is the tell for
	-- a scenario whose rules.yaml was never loaded.
	print(state() .. " -- WorldLoaded, about to issue both restock orders")

	Trigger.AfterDelay(50, function()
		-- BOTH ORDERS ON ONE TICK, through the real order chain the mouse uses, so neither lane can
		-- be advantaged by timing. Each returns the order string it resolved to, which is read back
		-- below rather than assumed.
		r.orderWalled = Test.ClickOrderGroup({ TruckWalled }, WalledLC)[1]
		r.orderOpen = Test.ClickOrderGroup({ TruckOpen }, OpenLC)[1]
		r.stage = 2
		print(state() .. " -- orders issued")
	end)

	-- Live values go to lua.log; never into an AssertWithin message, which is built once at
	-- registration and would report these as their starting values forever.
	local function report()
		r.ticks = r.ticks + 1

		-- LATCHED EVERY TICK, not sampled at the end. The walled Centre must hold its starting value
		-- for the entire run, so a dip that later recovered is still a breach of the property.
		local walledNow = supplyOf(WalledLC)
		if walledNow >= 0 and walledNow < r.walledMin then
			r.walledMin = walledNow

			if r.breach == nil then
				r.breach = {
					at = r.ticks,
					value = walledNow,
					truck = supplyOf(TruckWalled),
					where = whereIs(TruckWalled),
				}
				print(state() .. " -- BREACH: the walled Centre lost supply")
			end
		end

		if r.ticks % ReportEvery == 0 then
			print(state())
		end

		Trigger.AfterDelay(1, report)
	end

	Trigger.AfterDelay(1, report)

	-- The timeout path, carrying live numbers that AssertWithin's own message cannot.
	Trigger.AfterDelay(WatchdogTicks, function()
		if r.stage >= 4 then
			return
		end

		Test.Fail(state()
			.. " -- TIMED OUT. The control lane never completed its restock, so this run says NOTHING"
			.. " about the arrival guard: a walled Centre that kept its supply is only evidence when an"
			.. " identical reachable one demonstrably lost its own. Expected OPEN centre "
			.. OpenExpectedAfter .. " and OPEN truck " .. TruckCapacity .. ".")
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if WalledLC.IsDead or OpenLC.IsDead then
			return "fail: " .. state()
				.. " -- a Logistics Centre died; nothing in this scenario should be shooting"
		end

		-- THE DEFECT, reported the moment it happens rather than at the end, with the numbers that
		-- identify it. Checked before the control so the RED arm names the right thing.
		if r.breach ~= nil then
			return "fail: " .. state()
				.. " -- THE WALLED CENTRE WAS DRAINED WITHOUT BEING REACHED. It fell to "
				.. r.breach.value .. " at tick " .. r.breach.at
				.. " while the truck stood at " .. r.breach.where
				.. " holding " .. r.breach.truck
				.. ", against a centre cell of " .. WalledCentreCell
				.. " and an arrival tolerance of " .. ToleranceCells .. "c."
				.. " A Move with no route completes at the mover's own cell (Move.cs:173-177), so"
				.. " RestockSupply's arrival guard is the only thing standing between that and a"
				.. " free refill from anywhere on the map. It is not running."
		end

		if r.stage < 2 then
			return false
		end

		-- Both lanes must have resolved to the order under test. A truck above its RestockThreshold
		-- would DELIVER instead, and a delivery into a full Centre also moves nothing — which would
		-- look identical at the pool being asserted on.
		if r.orderWalled ~= "Restock" then
			return "fail: " .. state()
				.. " -- the walled lane resolved to order '" .. shown(r.orderWalled)
				.. "' rather than Restock, so nothing here exercised RestockSupply at all"
		end

		if r.orderOpen ~= "Restock" then
			return "fail: " .. state()
				.. " -- the control lane resolved to order '" .. shown(r.orderOpen)
				.. "' rather than Restock, so it is not a control for the walled lane"
		end

		-- THE CONTROL. Nothing is allowed to pass until an identical truck has completed a real drive
		-- to an identical but reachable Centre and moved the supply. Both halves are asserted: the
		-- Centre lost it and the truck gained it, so a bookkeeping error on either side is caught.
		if not r.controlDone then
			local openCentre = supplyOf(OpenLC)
			local openTruck = supplyOf(TruckOpen)

			if openCentre > OpenExpectedAfter or openTruck < TruckCapacity then
				return false
			end

			if openCentre < OpenExpectedAfter then
				return "fail: " .. state()
					.. " -- the control Centre gave away MORE than the truck could hold (expected "
					.. OpenExpectedAfter .. "), so supply is being destroyed and the control is not sound"
			end

			r.controlDone = true
			r.stage = 3
			print(state() .. " -- control lane complete; the restock mechanism is live")
		end

		-- The control has proved a restock works at 21 cells. The walled Centre has been watched every
		-- tick since the order and has not moved. That pair is the result.
		local walledFinal = supplyOf(WalledLC)
		if walledFinal ~= CentreStart or r.walledMin ~= CentreStart then
			return "fail: " .. state()
				.. " -- the walled Centre did not hold its starting value of " .. CentreStart
		end

		r.stage = 4
		Test.Screenshot("01-both-lanes",
			"expects: NORTH, a truck still at its start cell 21 cells west of a Centre sealed inside a"
			.. " concrete ring, both at their starting supply. SOUTH, an identical truck parked at an"
			.. " identical open Centre, full, the Centre drawn down. The two lanes differ only by the wall")

		Test.Pass(state()
			.. " -- walled Centre held " .. CentreStart .. " for the whole run while an identical truck"
			.. " restocked from an identical reachable Centre (" .. CentreStart .. " -> "
			.. OpenExpectedAfter .. ", truck " .. TruckStart .. " -> " .. TruckCapacity .. ")")

		return false
	end, "the control lane never restocked, so the walled lane's null result proves nothing")
end
