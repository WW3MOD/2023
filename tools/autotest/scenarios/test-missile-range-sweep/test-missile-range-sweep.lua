-- MEASUREMENT RIG: does hit probability vary with engagement range?
--
-- DOCS/reference/missiles.md I1 is the user's own ruling: "Should have the same
-- hit chance regardless of distance ... as long as the weapon can fire the
-- missile should be able to hit." Any systematic trend across a weapon's
-- permitted envelope is a defect by the spec, even where it is physically
-- realistic. This rig sweeps range and leaves everything else fixed.
--
-- Deliberately STATIONARY targets. Target motion is the other half of the
-- missile problem and it is measured by test-missile-latch-probe; mixing the
-- two here would leave any range trend unattributable. So this is the clean
-- range control, and it is the arm that I1 is actually about.
--
--   MANPAD (actor `aa`) vs a hovering Littlebird   2..22 cells (weapon Range 23c0)
--   ATGM   (actor `at`) vs a stationary t90         4..18 cells (Range 20c0, MinRange 3c0)
--
-- One lane per range, three launchers per lane, so each range gets a double-digit
-- sample from one run. Verdict is a CONTROL on sample count and on both weapons
-- having fired — a sweep where one weapon silently produced nothing would
-- otherwise read as "no range trend for that weapon".
--
-- Run with tools/autotest/run-test.sh --missile-trace-summary: the per-tick
-- stream is not needed here and this many missiles makes it large.

local RunSeconds = 60
local MinMissiles = 60

local USA, RUSSIA
local lanes = {}

local AA_RANGES = { 2, 4, 7, 10, 13, 16, 19, 22 }
local AT_RANGES = { 4, 6, 9, 12, 15, 18 }

local HELI_ALT = 3840   -- ^Helicopter CruiseAltitude 3c768

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

local function makeLane(name, launcherType, launcherX, row, targetType, targetX, targetAlt)
	local lane = { name = name, launchers = {}, target = nil }

	-- PITFALL: ground actors must be created with `Location`. With
	-- `CenterPosition` the actor exists and looks fine but no ground weapon will
	-- engage it, and the only symptom is launchers sitting on untouched ammo.
	if targetAlt == nil then
		lane.target = Actor.Create(targetType, true, {
			Owner = RUSSIA,
			Location = CPos.New(targetX, row),
			Facing = Angle.East,
		})
	else
		lane.target = Actor.Create(targetType, true, {
			Owner = RUSSIA,
			CenterPosition = cellPos(targetX, row, targetAlt),
			Facing = Angle.East,
		})
	end

	lane.target.Stance = "HoldFire"

	for i = 0, 2 do
		local a = Actor.Create(launcherType, true, {
			Owner = USA,
			Location = CPos.New(launcherX + i, row),
			Facing = Angle.East,
		})

		if a ~= nil then
			table.insert(lane.launchers, a)
		end
	end

	table.insert(lanes, lane)
	return lane
end

local function pressAttack()
	for _, lane in ipairs(lanes) do
		if lane.target ~= nil and not lane.target.IsDead then
			for _, a in ipairs(lane.launchers) do
				if not a.IsDead then
					a.Attack(lane.target, false, true)
				end
			end
		end
	end
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("required players not found")
		return
	end

	local row = 2
	for _, r in ipairs(AA_RANGES) do
		makeLane("aa_" .. r, "aa", 2, row, "littlebird", 2 + r, HELI_ALT)
		row = row + 2
	end

	for _, r in ipairs(AT_RANGES) do
		makeLane("at_" .. r, "at", 36, row, "t90", 36 + r, nil)
		row = row + 2
	end

	for _, lane in ipairs(lanes) do
		if lane.target == nil or #lane.launchers == 0 then
			Test.Fail("lane " .. lane.name .. " failed to stage")
			return
		end
	end

	TestHarness.FocusBetween(lanes[1].target, lanes[#lanes].target)

	pressAttack()
	local repress
	repress = function()
		pressAttack()
		Trigger.AfterDelay(50, repress)
	end
	Trigger.AfterDelay(50, repress)

	Trigger.AfterDelay(RunSeconds * TestHarness.TicksPerSecond, function()
		local n = Test.GetMissileRecordCount()

		-- Control: BOTH weapons must have fired. A sweep in which one weapon
		-- produced nothing is not a sweep with no trend, it is a broken rig.
		local seen = {}
		for i = 1, n do
			local rec = Test.GetMissileRecord(i)
			if rec ~= nil and rec.weapon ~= nil then
				seen[rec.weapon] = (seen[rec.weapon] or 0) + 1
			end
		end

		local parts = {}
		for w, k in pairs(seen) do
			table.insert(parts, string.format("%s=%d", w, k))
		end

		local diag = table.concat(parts, " ")
		if n < MinMissiles or seen["manpad"] == nil or seen["atgm"] == nil then
			Test.Fail(string.format(
				"sweep incomplete: %d records (need >= %d) weapons{%s} — a missing weapon is a broken rig, not a flat curve",
				n, MinMissiles, diag))
			return
		end

		Test.Pass(string.format("%d missile records; weapons{%s}", n, diag))
	end)
end
