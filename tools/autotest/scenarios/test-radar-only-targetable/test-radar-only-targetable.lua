-- ASSERTION SCENARIO: if the player can see a unit, the player can target it.
--
-- =====================================================================================
-- THE REPORT
-- =====================================================================================
--   "When I see helicopters but only with radar coverage (no visual) I cannot target
--    them until I have both. Even though I can see them."
--
-- =====================================================================================
-- THE MECHANISM THIS PINS
-- =====================================================================================
-- Radar and vision are different layers. Radar increments MapLayers.radarCount, a plain
-- binary per-cell counter (MapLayers.cs:534-537); vision writes graded strengths into
-- ResolvedVisibility. Detectable.IsVisibleInner ORs the radar clause in, so
-- CanBeViewedByPlayer answers YES for a radar-only contact and the helicopter draws.
--
-- The mouse paths then asked a SECOND question that radar structurally cannot answer:
-- !World.FogObscures(CenterPosition), i.e. ResolvedVisibility > 1 on one cell
-- (World.cs:111). No radar contact has ever satisfied it. So the actor was filtered out
-- of the mouse-target list and the right-click fell through to Target.FromCell -- the AA
-- unit got a Move order, and the attack cursor never appeared.
--
-- =====================================================================================
-- WHY THIS SCENARIO DOES NOT ASSERT THE OBVIOUS THINGS
-- =====================================================================================
-- Two natural assertions here are FALSE GREENS, both of them reachable against the exact
-- defect this scenario exists for:
--
--   * Test.IsDetectedBy(heli, USA) -- CanBeViewedByPlayer. It is true for a radar-only
--     contact WITH OR WITHOUT the fix; that is the whole point of the bug, that the two
--     predicates disagree. Asserted below only as a SETUP CONTROL, never as the verdict.
--
--   * heli.Attack(...) from Lua, or watching the MANPAD's ammo drop. Lua orders go
--     through CombatProperties.Attack, which gates on CanBeViewedByPlayer
--     (CombatProperties.cs:97) and never touches UnitOrderGenerator at all. A Lua attack
--     order fires happily on the broken build. The bug lives in the MOUSE path, and Lua
--     cannot press a mouse button, so the engagement is not evidence about it.
--
--   * Test.ClickOrder, despite being the helper that exists for exactly this job. It
--     delegates honestly to UnitOrderGenerator.OrderForUnit -- but it takes the target as
--     an Actor and builds Target.FromActor(target) itself (TestGlobal.cs:555), so it
--     starts one step AFTER TargetForInput, and TargetForInput is where the actor is
--     filtered out. Same shape as the 2026-08-20 ClickOrder discovery, one layer further
--     up the pipeline: the helper reproduces everything except the branch with the bug.
--
-- What is asserted instead is Test.IsMouseTargetable -- the real predicate the
-- right-click runs (MouseTargetVisibility.IsRevealedForMouseInput), the same function
-- object UnitOrderGenerator.TargetForInput calls, not a re-derivation of it.
--
-- The end-to-end proof -- a human right-clicking the contact and seeing the attack
-- cursor -- is not automatable through this harness. This scenario pins the predicate;
-- confirming the cursor is a PLAYTEST.
--
-- =====================================================================================
-- HOW THIS CAN LOSE
-- =====================================================================================
-- DarkHeli is the load-bearing rung. "Radar-only contact is targetable" passes just as
-- happily if the fix made EVERYTHING targetable, which is precisely the over-broad
-- change the narrow fix was chosen to avoid. A helicopter revealed by nothing at all
-- must stay unclickable, and it is asserted on the same tick as the positive rung.
--
-- DarkHeli also catches a nil RenderPlayer: World.FogObscures returns false for every
-- actor when RenderPlayer is nil, which would report the whole map as clickable and turn
-- the positive rung into a meaningless pass.
--
-- The two visibility controls are what make the positive rung mean anything. If either
-- helicopter's cell reads unfogged, the run is a SETUP FAULT and not a verdict: a
-- contact in plain sight is targetable for reasons that have nothing to do with radar.

local RadarHeliX, RadarHeliY = 45, 10
local DarkHeliX, DarkHeliY = 63, 2
local Altitude = 1280

-- Ticks to wait before reading anything. The `airborne` condition that gates
-- RadarDetectableCondition is granted by Aircraft after the actor is in world, radar
-- coverage is stamped by MapLayers on its own tick, and ActorMap's position bins are not
-- populated until the first ITick -- so nothing here is answerable from WorldLoaded.
local Grace = 50

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	local russia = Player.GetPlayer("Russia")
	if usa == nil or russia == nil then
		Test.Fail("SETUP: could not resolve both players")
		return
	end

	-- Spawned airborne rather than map-placed: RadarDetectableCondition is `airborne`
	-- (aircraft.yaml:44), so a grounded helicopter is not a radar contact and the
	-- scenario would be testing nothing.
	local radarHeli = Actor.Create("halo", true, {
		Owner = russia,
		CenterPosition = cellPos(RadarHeliX, RadarHeliY, Altitude),
		Facing = Angle.South,
	})
	local darkHeli = Actor.Create("halo", true, {
		Owner = russia,
		CenterPosition = cellPos(DarkHeliX, DarkHeliY, Altitude),
		Facing = Angle.South,
	})

	if radarHeli == nil or darkHeli == nil then
		Test.Fail("SETUP: could not spawn both halos")
		return
	end

	TestHarness.FocusBetween(Stryker, radarHeli)
	TestHarness.Select(Manpad)

	local ticks = 0

	-- The failure string passed to AssertWithin is concatenated EAGERLY, at registration,
	-- so it can only ever report the values these counters held before the run started
	-- (AUTOTEST.md, "Two Lua traps"). Every live number below is therefore either printed
	-- to lua.log or returned in a "fail:" string, which IS evaluated at failure time.
	TestHarness.AssertWithin(20, function()
		ticks = ticks + 1
		if ticks < Grace then return false end

		if radarHeli.IsDead or darkHeli.IsDead then
			return "fail: SETUP -- a helicopter died; it should be unshootable here"
		end

		local rCell = radarHeli.Location
		local dCell = darkHeli.Location
		local rVis = Test.GetVisibility(usa, rCell)
		local dVis = Test.GetVisibility(usa, dCell)
		local rDetected = Test.IsDetectedBy(radarHeli, usa)
		local dDetected = Test.IsDetectedBy(darkHeli, usa)
		local rClickable = Test.IsMouseTargetable(radarHeli)
		local dClickable = Test.IsMouseTargetable(darkHeli)

		print(string.format(
			"[radar-targeting] radar-heli cell=%d:%d vis=%d detected=%s clickable=%s | " ..
			"dark-heli cell=%d:%d vis=%d detected=%s clickable=%s",
			rCell.X, rCell.Y, rVis, tostring(rDetected), tostring(rClickable),
			dCell.X, dCell.Y, dVis, tostring(dDetected), tostring(dClickable)))

		-- ---- setup controls: these decide whether the run is a verdict at all ----
		if rVis > 1 then
			return "fail: SETUP -- radar helicopter's cell reads visibility " .. rVis ..
				", so it is in plain sight and this run cannot say anything about radar-only targeting"
		end

		if dVis > 1 then
			return "fail: SETUP -- dark helicopter's cell reads visibility " .. dVis ..
				", so it is not actually unrevealed and the negative rung is inert"
		end

		if not rDetected then
			return "fail: SETUP -- radar helicopter is not detected at all (cell visibility " .. rVis ..
				"). Radar coverage never reached it; check the 56c0 Radar range override and the geometry"
		end

		if dDetected then
			return "fail: SETUP -- dark helicopter IS detected, so something is revealing it. " ..
				"It must be outside radar (56c0) and outside vision (32c) from every USA actor"
		end

		-- ---- the assertions ----
		if not rClickable then
			return "fail: a helicopter held on RADAR is not mouse-targetable. The player can see it " ..
				"(detected=true) but cannot right-click it. Radar contributes nothing to " ..
				"ResolvedVisibility, so the cell-fog veto in MouseTargetVisibility refuses every " ..
				"radar contact unless the radar exemption is present"
		end

		if dClickable then
			return "fail: a helicopter revealed by NOTHING is mouse-targetable. The fix has been " ..
				"widened past radar into a wallhack, or RenderPlayer is nil and World.FogObscures " ..
				"is answering false for every actor on the map"
		end

		return true
	end, "radar-only targeting check never completed within 20s")
end
