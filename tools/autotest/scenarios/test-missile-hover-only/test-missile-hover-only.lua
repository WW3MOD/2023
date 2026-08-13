-- MEASUREMENT RIG: the air_hover lane, and NOTHING else in the world.
--
-- test-missile-latch-probe runs seven engagements in one world. That is fine for
-- counting latches, but it makes a per-missile comparison BETWEEN BUILDS invalid,
-- and the guidance-latch fix was signed off partly on such a comparison.
--
-- The reason is the shared simulation. All seven lanes advance in one tick loop
-- against one shared RNG stream: every missile draws from it at creation
-- (Inaccuracy) and again on each RetargetTicks re-roll of `offset`. The fix stops
-- air_reverse latching (7 -> 0), so those missiles stay alive for different
-- lengths of time, re-roll on different ticks, and detonate at different places.
-- From the first such divergence onward every later draw in the stream belongs to
-- a different missile than it did on the other build — including the draws that
-- belong to air_hover. Pairing hover missiles by id across two builds therefore
-- compares two different worlds, not two treatments of one world.
--
-- Deleting the other six lanes removes that channel. Within this rig, two builds
-- that differ only in the miss DETECTOR are bit-identical until the first tick a
-- latch actually fires, because minDistanceToTarget feeds nothing except the
-- flyStraight predicate and its recovery (Missile.cs:874-883) — no steering, no
-- speed, no RNG draw. So any difference in closest approach BEFORE the first
-- latch cannot be caused by the detector, and any difference after it can.
--
-- Same lane geometry, target and motion (none) as test-missile-latch-probe's
-- air_hover, so the 18 missiles here are the same engagement that lane measures.

local RunSeconds = 60
local MinMissiles = 12          -- control: below this the rig did not run

local USA, RUSSIA
local lanes = {}

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

local function makeLane(name, launcherType, launcherCells, targetType, targetCell, targetAlt)
	local lane = { name = name, launchers = {}, target = nil }

	if targetAlt == nil then
		lane.target = Actor.Create(targetType, true, {
			Owner = RUSSIA,
			Location = CPos.New(targetCell[1], targetCell[2]),
			Facing = Angle.East,
		})
	else
		lane.target = Actor.Create(targetType, true, {
			Owner = RUSSIA,
			CenterPosition = cellPos(targetCell[1], targetCell[2], targetAlt),
			Facing = Angle.East,
		})
	end

	lane.target.Stance = "HoldFire"

	for _, c in ipairs(launcherCells) do
		local a = Actor.Create(launcherType, true, {
			Owner = USA,
			Location = CPos.New(c[1], c[2]),
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

	local HELI_ALT = 3840   -- ^Helicopter CruiseAltitude 3c768: the real one

	-- Cells copied verbatim from test-missile-latch-probe's air_hover lane.
	-- The target is given no move order at all — that is what "hover" means here.
	local airHover = makeLane("air_hover", "aa", {{6,21},{7,21},{6,22}}, "littlebird", {18,21}, HELI_ALT)

	if airHover.target == nil then
		Test.Fail("lane air_hover failed to spawn its target")
		return
	end
	if #airHover.launchers == 0 then
		Test.Fail("lane air_hover failed to spawn any launcher")
		return
	end

	TestHarness.FocusBetween(airHover.target, airHover.launchers[1])

	pressAttack()
	Trigger.AfterDelay(50, function()
		local repress
		repress = function()
			pressAttack()
			Trigger.AfterDelay(50, repress)
		end
		repress()
	end)

	Trigger.AfterDelay(RunSeconds * TestHarness.TicksPerSecond, function()
		local n = Test.GetMissileRecordCount()
		local aloft = Test.GetActiveMissileCount()
		if n < MinMissiles then
			Test.Fail(string.format(
				"rig produced only %d missile records (need >= %d, %d still aloft) — the lane did not fire, so an empty trace is NOT evidence",
				n, MinMissiles, aloft))
			return
		end

		local ammo = 0
		for _, la in ipairs(airHover.launchers) do
			if not la.IsDead then
				local ok, k = pcall(function() return la.AmmoCount("primary-ammo") end)
				if ok and k ~= nil and k > 0 then ammo = ammo + k end
			end
		end

		Test.Pass(string.format("%d missile records in air_hover (%d aloft); ammoLeft=%d", n, aloft, ammo))
	end)
end
