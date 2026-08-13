-- MEASUREMENT RIG: what actually trips the FlyStraightIfMiss latch?
--
-- Missile.cs:852-853 latches `flyStraight` when
--     currentDistance > minDistanceToTarget + CloseEnough
-- and freezes BOTH steering axes for the rest of the flight (:860-863).
-- Three readings of the trigger are on record and they disagree, so this rig
-- stops arguing and varies the one quantity they disagree about: how the
-- TARGET is moving.
--
--   flee      target recedes from the launcher for the whole flight
--   approach  target closes on the launcher for the whole flight
--   reverse   target alternates toward/away every ~1.2 s, so every missile
--             meets at least one course reversal mid-flight
--   hover     target holds station (the null case)
--
-- Same four regimes are run for an air weapon (MANPAD, CloseEnough 192) and,
-- minus the approach case, for a ground weapon (ATGM/Javelin, CloseEnough 298).
-- Launch geometry is held constant inside a regime so the only thing that
-- differs between lanes is target motion.
--
-- The rig asserts nothing about hit rate. Its verdict is purely a CONTROL:
-- fail if the lanes did not actually shoot, because a rig that quietly fired
-- nothing still writes `pass` and would be read as "no latches observed".
-- Run it with tools/autotest/run-test.sh --missile-trace; the answer is in the
-- resulting .missiles.jsonl, not in this verdict.

local RunSeconds = 60
local MinMissiles = 40          -- control: below this the rig did not run

local USA, RUSSIA
local lanes = {}

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

-- One engagement. `launchers` fire at `target` and nothing else; the lane's
-- motion script is what the whole rig is varying.
local function makeLane(name, launcherType, launcherCells, targetType, targetCell, targetAlt)
	local lane = { name = name, launchers = {}, target = nil }

	-- PITFALL: a GROUND actor must be created with `Location`. Created with
	-- `CenterPosition` it appears on the map and reports alive, but no ground
	-- weapon will engage it — the first cut of this rig lost every ATGM lane
	-- that way, and the only symptom was launchers sitting on full ammo.
	-- `targetAlt == nil` means ground; aircraft still need CenterPosition,
	-- which is the only way to set an exact altitude.
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

	-- The target is the ENEMY here, so silencing it with HoldFire is the safe
	-- direction (AUTOTEST.md gotcha 7). Without this the littlebirds shoot the
	-- launchers and lanes start losing shooters.
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

-- Re-issued periodically: an armament that runs dry drops its order, and the
-- ReloadAmmoPool refill would otherwise leave the launcher idle for the rest
-- of the run.
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

local function moveTo(actor, cx, cy)
	if actor ~= nil and not actor.IsDead then
		actor.Move(CPos.New(cx, cy))
	end
end

-- Alternates the move order between two cells so the target is reversing
-- course continuously rather than once. A single scripted reversal would only
-- catch whichever missiles happened to be at the right phase of flight.
local function oscillate(actor, cxA, cxB, cy, periodTicks)
	local toB = true
	local step
	step = function()
		if actor == nil or actor.IsDead then return end
		if toB then moveTo(actor, cxB, cy) else moveTo(actor, cxA, cy) end
		toB = not toB
		Trigger.AfterDelay(periodTicks, step)
	end
	step()
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("required players not found")
		return
	end

	local HELI_ALT = 3840   -- ^Helicopter CruiseAltitude 3c768: the real one

	-- ---- Air lanes: MANPAD (actor `aa`) vs littlebird ----
	local airFlee     = makeLane("air_flee",     "aa", {{6,3},{7,3},{6,4}},     "littlebird", {18,3},  HELI_ALT)
	local airApproach = makeLane("air_approach", "aa", {{6,9},{7,9},{6,10}},    "littlebird", {22,9},  HELI_ALT)
	local airReverse  = makeLane("air_reverse",  "aa", {{6,15},{7,15},{6,16}},  "littlebird", {18,15}, HELI_ALT)
	local airHover    = makeLane("air_hover",    "aa", {{6,21},{7,21},{6,22}},  "littlebird", {18,21}, HELI_ALT)

	-- ---- Ground lanes: ATGM/Javelin (actor `at`) vs t90 ----
	local gndStatic   = makeLane("gnd_static",   "at", {{40,3},{41,3},{40,4}},   "t90", {52,3},  nil)
	local gndFlee     = makeLane("gnd_flee",     "at", {{40,15},{41,15},{40,16}},"t90", {50,15}, nil)
	local gndReverse  = makeLane("gnd_reverse",  "at", {{40,25},{41,25},{40,26}},"t90", {52,25}, nil)

	for _, lane in ipairs(lanes) do
		if lane.target == nil then
			Test.Fail("lane " .. lane.name .. " failed to spawn its target")
			return
		end
		if #lane.launchers == 0 then
			Test.Fail("lane " .. lane.name .. " failed to spawn any launcher")
			return
		end
	end

	TestHarness.FocusBetween(airReverse.target, gndStatic.target)

	-- ---- motion scripts ----
	moveTo(airFlee.target, 30, 3)         -- recedes 18 -> 30 cells out
	moveTo(airApproach.target, 9, 9)      -- closes 22 -> 9
	oscillate(airReverse.target, 14, 26, 15, 30)
	-- airHover: no order at all

	moveTo(gndFlee.target, 62, 15)
	oscillate(gndReverse.target, 46, 60, 25, 30)

	pressAttack()
	Trigger.AfterDelay(50, function()
		local repress
		repress = function()
			pressAttack()
			Trigger.AfterDelay(50, repress)
		end
		repress()
	end)

	-- AssertWithin is deliberately not used: its timeout verdict is Fail, which
	-- is the wrong answer for a rig whose job is to run the clock out. The
	-- verdict below is a control on sample count, nothing more.
	Trigger.AfterDelay(RunSeconds * TestHarness.TicksPerSecond, function()
		local n = Test.GetMissileRecordCount()
		local aloft = Test.GetActiveMissileCount()
		if n < MinMissiles then
			Test.Fail(string.format(
				"rig produced only %d missile records (need >= %d, %d still aloft) — lanes did not fire, so an empty trace is NOT evidence",
				n, MinMissiles, aloft))
			return
		end

		local parts = {}
		for _, lane in ipairs(lanes) do
			local ammo = 0
			for _, la in ipairs(lane.launchers) do
				if not la.IsDead then
					local ok, k = pcall(function() return la.AmmoCount("primary-ammo") end)
					if ok and k ~= nil and k > 0 then ammo = ammo + k end
				end
			end
			table.insert(parts, string.format("%s[ammoLeft=%d]", lane.name, ammo))
		end

		Test.Pass(string.format("%d missile records across %d lanes (%d aloft); %s",
			n, #lanes, aloft, table.concat(parts, " ")))
	end)
end
