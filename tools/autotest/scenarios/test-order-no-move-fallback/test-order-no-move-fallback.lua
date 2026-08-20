-- AUTO TEST: an attack order a unit cannot execute leaves that unit alone.
--
-- The click is issued through Test.ClickOrder, which resolves the real IIssueOrder chain in
-- UnitOrderGenerator — including the terrain retry that used to turn a refused attack into a move.
-- Naming an order instead (Rejector.Attack / Test.IssueMove) would skip the routing decision, which
-- IS the thing under test.
--
-- THREE OBSERVABLES, and they fail for different reasons on purpose:
--   1. routing  — ClickOrder's return value. Only this code path produces it, so it is the one
--                 observable nothing else in the sim can satisfy. "Attack" for the Gunner, nil for
--                 the Rejector.
--   2. custody  — the Rejector must still be executing the move it already had, proved by four
--                 cells of WESTWARD travel. This is what separates "did not move" from "had no
--                 chance to move": a unit that was never ordered anywhere, or that had arrived
--                 already, cannot produce it.
--   3. the positive half — the Gunner's ammo must drop, or the cheapest way to pass this test
--                 would be to break attack orders outright. Attributable because the Gunner is on
--                 HoldFire, so autotarget cannot acquire the t90 on its own and the only thing that
--                 can make it shoot is the click.

local DeadlineSeconds = 25
local WaypointCell = CPos.New(26, 16)
local WestwardCellsRequired = 4

local startX
local startAmmo
local gunnerOrder = "<unissued>"
local rejectorOrder = "<unissued>"

WorldLoaded = function()
	TestHarness.FocusBetween(Rejector, Enemy)
	TestHarness.Select(Rejector)

	-- The enemy stands still; the Gunner does not acquire on its own. Neither unit under test is
	-- silenced by this — the Rejector's stance is irrelevant to a ground target it can never shoot,
	-- and the Gunner still obeys an explicit order under HoldFire (AttackBase.ResolveOrder does not
	-- consult stance). See AUTOTEST.md gotcha 9 for why this is the enemy-side idiom.
	Enemy.Stance = "HoldFire"
	Gunner.Stance = "HoldFire"
	Rejector.Stance = "HoldFire"

	startX = Rejector.Location.X
	startAmmo = Gunner.AmmoCount("primary-ammo")

	-- Give the Rejector something to be interrupted OUT of, heading away from the enemy.
	Trigger.AfterDelay(25, function()
		Test.IssueMove(Rejector, WaypointCell)
	end)

	-- ...then the group attack click, one unit at a time through the real routing. Fired while the
	-- move is genuinely in flight, so a surviving activity is a fact about this click and not about
	-- the unit having nothing to do.
	Trigger.AfterDelay(50, function()
		gunnerOrder = Test.ClickOrder(Gunner, Enemy) or "<refused>"
		rejectorOrder = Test.ClickOrder(Rejector, Enemy) or "<refused>"
	end)

	-- Live counters go to lua.log, never into the failure string: AssertWithin's third argument is
	-- evaluated once at registration, so anything interpolated there reports its value from before
	-- the run started (AUTOTEST.md §Two Lua traps).
	local ticks = 0
	Trigger.AfterDelay(1, function()
		local report
		report = function()
			ticks = ticks + 1
			if ticks % 25 == 0 then
				print("[order-fallback] t=" .. ticks
					.. " gunner=" .. gunnerOrder
					.. " rejector=" .. rejectorOrder
					.. " rejectorX=" .. tostring(Rejector.IsDead and -1 or Rejector.Location.X)
					.. " startX=" .. tostring(startX)
					.. " gunnerAmmo=" .. tostring(Gunner.IsDead and -1 or Gunner.AmmoCount("primary-ammo")))
			end

			Trigger.AfterDelay(1, report)
		end

		report()
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Rejector.IsDead then
			return "fail: the AA specialist died before the check completed"
		end

		if Gunner.IsDead then
			return "fail: the Abrams died before the check completed"
		end

		-- Nothing to judge until the click has actually been made.
		if gunnerOrder == "<unissued>" then
			return false
		end

		if rejectorOrder ~= "<refused>" then
			return "fail: the AA specialist accepted a '" .. rejectorOrder
				.. "' order for a target it can never engage — it must reject the click, not be moved"
		end

		if gunnerOrder ~= "Attack" then
			return "fail: the Abrams got '" .. gunnerOrder .. "' instead of Attack — the order it CAN execute was lost"
		end

		if Rejector.Location.X > startX then
			return "fail: the AA specialist was diverted EAST toward the enemy tank it cannot shoot — "
				.. "its westward move order was replaced by a move fallback"
		end

		return Rejector.Location.X <= startX - WestwardCellsRequired
			and Gunner.AmmoCount("primary-ammo") < startAmmo
	end, "the AA specialist did not carry on west with the move it already had, or the Abrams never fired "
		.. "-- see the [order-fallback] lines in lua.log for which half stalled")
end
