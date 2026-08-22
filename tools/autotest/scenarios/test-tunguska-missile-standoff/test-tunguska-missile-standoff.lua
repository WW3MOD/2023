-- AUTO TEST: a Tunguska given a MANUAL attack order at a helicopter must use its
-- missiles from where it stands, not drive into 30mm range.
--
-- Reported from playtest 260822: "That same tunguska shot missiles at one helicopter,
-- then when I ordered it to shoot at a second it refused to shoot it with missiles and
-- went closer to it, to shoot with the guns instead, even though it had missiles."
--
-- Cause: Attack.TickAttack took `armaments.Min(a => a.MaxRange())` — the SHORTEST reach
-- among every armament valid against the target. Against a helicopter the Tunguska has
-- two: the 30mm AA gun at 18c0 and the 9M311 at 28c0. Min yields 18c0, so a target 24
-- cells out read as OUT OF RANGE and the unit closed until the gun could fire, with all
-- eight missiles still loaded. Fixed by AttackBase.EngageAtLongestArmamentRange, which
-- this scenario exercises end-to-end; EngagementMaxRangeTest pins the arithmetic.
--
-- Geometry (row y=17): Tunguska col 10, Littlebird col 34 — 24 cells apart, which is
-- INSIDE the 9M311's 28c0 and OUTSIDE the 30mm's 18c0. That gap is the whole test: at
-- 24 cells the missile is the only weapon that can reach, so any eastward movement is
-- the unit choosing the worse weapon.
--   Pass:  secondary-ammo drops while the Tunguska is still at col <= 14.
--   Fail:  it advances past col 14 (closing toward gun range), or never fires a missile.
--
-- The heli is held on HoldFire so it cannot damage the Tunguska; return fire would
-- trigger repositioning and confound a test whose entire signal is "did it move east".

-- NOTE: TestHarness.TicksPerSecond is 25 in mods/ww3mod/scripts/test-helpers.lua, but the
-- mod runs at Timestep 60 = 16.67 tps, so this deadline is really ~30 s of wall time. Left
-- alone deliberately — correcting the helper would move every other scenario's deadline.
local DeadlineSeconds = 20

local TunguskaCol = 10
local HeliCol = 34
local ClosingCol = 14   -- moving past this = closing toward the 18c0 gun band

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")
	if USA == nil or Russia == nil then
		Test.Fail("players not found")
		return
	end

	local Tunguska = Actor.Create("tunguska", true, {
		Owner = Russia,
		Location = CPos.New(TunguskaCol, 17),
		Facing = Angle.East,
	})
	if Tunguska == nil then
		Test.Fail("could not spawn tunguska")
		return
	end

	local Heli = Actor.Create("littlebird", true, {
		Owner = USA,
		CenterPosition = cellPos(HeliCol, 17, 1280),
		Facing = Angle.West,
	})
	if Heli == nil then
		Test.Fail("could not spawn littlebird")
		return
	end

	-- Neither unit may act on its own: the test is about ONE explicit order.
	Heli.Stance = "HoldFire"
	Tunguska.Stance = "HoldFire"

	TestHarness.FocusBetween(Tunguska, Heli)
	TestHarness.Select(Tunguska)

	local startSecondary = Tunguska.AmmoCount("secondary-ammo")
	if startSecondary == nil or startSecondary <= 0 then
		Test.Fail("tunguska spawned without missiles — the test cannot mean anything")
		return
	end

	-- The player's order, exactly as the report describes it: a plain attack click.
	Tunguska.Attack(Heli)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Tunguska.IsDead then
			return "fail: tunguska died first"
		end

		if Heli.IsDead then
			-- Killing it is the point; it can only have been the missile from out here.
			return true
		end

		if Tunguska.Location.X > ClosingCol then
			return "fail: tunguska drove east to col " .. Tunguska.Location.X ..
				" — it closed toward 30mm range instead of firing its missiles from " ..
				(HeliCol - TunguskaCol) .. " cells"
		end

		if Tunguska.AmmoCount("secondary-ammo") < startSecondary then
			return true
		end

		return false
	end, "tunguska never fired a 9M311 — it sat at " .. (HeliCol - TunguskaCol) ..
		" cells with missiles loaded and a live order")
end
