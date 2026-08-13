-- MEASUREMENT RIG: the two cases the user actually reported.
--
--  A  Mi-28 vs a ground target at the REAL helicopter altitude. test-mi28-fires-ataka
--     spawns the Mi-28 at 1280 and passes; a real Mi-28 inherits ^Helicopter's
--     CruiseAltitude 3c768 = 3840 and overrides nothing, so the shipped altitude
--     has never been under test. Lane A flies it at 3840, lane B repeats the
--     existing 1280 as a control. Same 18-cell range, same target — altitude is
--     the only difference, so a split between the lanes localises the defect.
--
--  C/D/E  Three AA soldiers vs a Littlebird at 2, 3 and 4 cells — the user saw
--     all three miss. The launch-angle wrap has since been fixed (MaximumLaunchAngle
--     1000 -> 252, a629fee7), so this measures what is LEFT after that fix.
--
--  F  ATGM probe. In the previous rig the AT specialists spawned and were ordered
--     to fire but produced zero missiles, and no log said why. This lane carries
--     an ammo readout into the verdict note so the next run does not have to guess:
--     ammo spent > 0 means they fired, ammo untouched means the order never
--     produced a shot.
--
-- Verdict is a CONTROL on sample count, not a judgement on hit rate. The answer
-- is in the .missiles.jsonl from run-test.sh --missile-trace.

local RunSeconds = 45
local MinMissiles = 12

local USA, RUSSIA
local lanes = {}

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

local function makeLane(name, launcherType, launcherOwner, launcherCells, launcherAlt,
                        targetType, targetOwner, targetCell, targetAlt)
	local lane = { name = name, launchers = {}, target = nil, startAmmo = {} }

	-- PITFALL: a GROUND actor must be created with `Location`. Created with
	-- `CenterPosition` it appears on the map and reports alive, but no ground
	-- weapon will engage it — the first cut of this rig lost every ATGM and
	-- Ataka lane that way and the only symptom was launchers sitting on full
	-- ammo. `targetAlt == nil` means ground here; aircraft still need
	-- CenterPosition, which is the only way to set an exact altitude.
	if targetAlt == nil then
		lane.target = Actor.Create(targetType, true, {
			Owner = targetOwner,
			Location = CPos.New(targetCell[1], targetCell[2]),
			Facing = Angle.East,
		})
	else
		lane.target = Actor.Create(targetType, true, {
			Owner = targetOwner,
			CenterPosition = cellPos(targetCell[1], targetCell[2], targetAlt),
			Facing = Angle.East,
		})
	end

	lane.target.Stance = "HoldFire"

	for _, c in ipairs(launcherCells) do
		local a
		if launcherAlt ~= nil then
			a = Actor.Create(launcherType, true, {
				Owner = launcherOwner,
				CenterPosition = cellPos(c[1], c[2], launcherAlt),
				Facing = Angle.East,
			})
		else
			a = Actor.Create(launcherType, true, {
				Owner = launcherOwner,
				Location = CPos.New(c[1], c[2]),
				Facing = Angle.East,
			})
		end

		if a ~= nil then
			table.insert(lane.launchers, a)
		end
	end

	table.insert(lanes, lane)
	return lane
end

-- Ammo pool name differs by launcher: infantry fire their primary, the Mi-28
-- carries the Ataka as its secondary.
local function ammoOf(actor, poolName)
	local ok, n = pcall(function() return actor.AmmoCount(poolName) end)
	if ok and n ~= nil then return n end
	return -1
end

local function laneAmmo(lane)
	local total = 0
	for _, a in ipairs(lane.launchers) do
		if not a.IsDead then
			local n = ammoOf(a, lane.pool)
			if n > 0 then total = total + n end
		end
	end
	return total
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

	local HELI_ALT = 3840   -- ^Helicopter CruiseAltitude 3c768 — the shipped value
	local OLD_ALT = 1280    -- what test-mi28-fires-ataka has always used

	-- CROSSOVER. The first cut ran 3840 on rows 4/7 and 1280 on rows 13/16 and
	-- found 3840 markedly MORE accurate � the opposite of what was expected. Row
	-- and altitude were confounded there, so each altitude now appears twice, on
	-- widely separated rows. If altitude is the driver, A and H agree and B and G
	-- agree; if the map is, they will not. Each lane is a single Mi-28 sitting on
	-- its target's own row, so every shot is dead ahead and lateral offset is out
	-- of the comparison too.
	local a = makeLane("A_mi28_3840", "mi28", RUSSIA, {{4,4}}, HELI_ALT, "t90", USA, {22,4}, nil)
	a.pool = "secondary-ammo"

	local b = makeLane("B_mi28_1280", "mi28", RUSSIA, {{4,10}}, OLD_ALT, "t90", USA, {22,10}, nil)
	b.pool = "secondary-ammo"

	local g = makeLane("G_mi28_1280", "mi28", RUSSIA, {{4,16}}, OLD_ALT, "t90", USA, {22,16}, nil)
	g.pool = "secondary-ammo"

	local h = makeLane("H_mi28_3840", "mi28", RUSSIA, {{4,22}}, HELI_ALT, "t90", USA, {22,22}, nil)
	h.pool = "secondary-ammo"

	local c = makeLane("C_aa_2cell", "aa", USA, {{34,3},{34,4},{34,5}}, nil, "littlebird", RUSSIA, {36,4}, HELI_ALT)
	c.pool = "primary-ammo"

	local d = makeLane("D_aa_3cell", "aa", USA, {{34,12},{34,13},{34,14}}, nil, "littlebird", RUSSIA, {37,13}, HELI_ALT)
	d.pool = "primary-ammo"

	local e = makeLane("E_aa_4cell", "aa", USA, {{34,20},{34,21},{34,22}}, nil, "littlebird", RUSSIA, {38,21}, HELI_ALT)
	e.pool = "primary-ammo"

	local f = makeLane("F_atgm_probe", "at", USA, {{48,28},{49,28},{48,29}}, nil, "t90", RUSSIA, {60,28}, nil)
	f.pool = "primary-ammo"

	for _, lane in ipairs(lanes) do
		if lane.target == nil then
			Test.Fail("lane " .. lane.name .. " failed to spawn its target")
			return
		end
		if #lane.launchers == 0 then
			Test.Fail("lane " .. lane.name .. " failed to spawn any launcher")
			return
		end
		lane.ammoAtStart = laneAmmo(lane)
	end

	TestHarness.FocusBetween(a.target, e.target)

	pressAttack()
	local repress
	repress = function()
		pressAttack()
		Trigger.AfterDelay(50, repress)
	end
	Trigger.AfterDelay(50, repress)

	Trigger.AfterDelay(RunSeconds * TestHarness.TicksPerSecond, function()
		local n = Test.GetMissileRecordCount()
		local parts = {}
		for _, lane in ipairs(lanes) do
			local alive = 0
			for _, la in ipairs(lane.launchers) do
				if not la.IsDead then alive = alive + 1 end
			end
			table.insert(parts, string.format("%s[live=%d ammo %d->%d]",
				lane.name, alive, lane.ammoAtStart, laneAmmo(lane)))
		end

		local diag = table.concat(parts, " ")
		if n < MinMissiles then
			Test.Fail(string.format("only %d missile records (need >= %d) — %s", n, MinMissiles, diag))
			return
		end

		Test.Pass(string.format("%d missile records; %s", n, diag))
	end)
end
