-- MEASUREMENT RIG: does the FlyStraightIfMiss latch fire on HELLFIRE?
--
-- test-missile-latch-probe measured the latch on MANPAD and ATGM only. Hellfire
-- is the NATO fire-and-forget ATGM and it had never been fired through either
-- the pre-fix or the post-fix build, which matters because its Speed of 500
-- against a manoeuvring air target is the same speed ratio that produced the
-- defect on MANPAD (Speed 450).
--
-- Same four target-motion regimes as the MANPAD lanes, so the two rigs are
-- read side by side:
--
--   flee      target recedes from the launcher for the whole flight
--   approach  target closes on the launcher for the whole flight
--   reverse   target alternates toward/away every ~1.2 s, so every missile
--             meets at least one course reversal mid-flight
--   hover     target holds station (the null case)
--
-- Two carriers, because Hellfire is fielded on both sides of the air/ground
-- split and they are NOT the same weapon:
--
--   HELI (Apache) fires `Hellfire`               -> Speed 500, CloseEnough 298
--   strykershorad fires `Hellfire.strykershorad` -> Speed 400, everything else
--                                                   inherited from Hellfire
--
-- The stryker is the odd one out on purpose: it is a ground vehicle firing a
-- weapon whose CruiseAltitude is the airborne default of 512 (Hellfire never
-- sets CruiseAltitude, so it takes Missile.cs:126).
--
-- RIG CAVEAT, and it is a real one: the air launchers are AIRCRAFT, so unlike
-- the MANPAD rig's infantry they REPOSITION during the run. Launch geometry is
-- therefore not constant within a lane the way it is in the MANPAD rig. Both
-- builds are run on the same seed so the comparison stays paired, but do not
-- read an absolute Hellfire hit rate against an absolute MANPAD one.
--
-- The rig asserts nothing about hit rate. Its verdict is purely a CONTROL: fail
-- if the lanes did not actually shoot, because a rig that quietly fired nothing
-- still writes `pass` and would be read as "no latches observed". Run it with
-- tools/autotest/run-test.sh --missile-trace; the answer is in the resulting
-- .missiles.jsonl, not in this verdict.

local RunSeconds = 60
local MinMissiles = 40          -- control: below this the rig did not run

local USA, RUSSIA
local lanes = {}

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

-- One engagement. `launchers` fire at `target` and nothing else.
-- launcherAlt == nil means a ground launcher (created with Location, which is
-- what a ground actor requires); non-nil spawns an aircraft at that exact
-- altitude via CenterPosition.
local function makeLane(name, launcherType, launcherCells, launcherAlt, targetType, targetCell, targetAlt)
	local lane = { name = name, launchers = {}, target = nil }

	-- PITFALL: a GROUND actor must be created with `Location`. Created with
	-- `CenterPosition` it appears on the map and reports alive, but no ground
	-- weapon will engage it — the first cut of the MANPAD rig lost every ATGM
	-- lane that way, and the only symptom was launchers sitting on full ammo.
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
	-- direction (AUTOTEST.md gotcha 7).
	lane.target.Stance = "HoldFire"

	for _, c in ipairs(launcherCells) do
		local a
		if launcherAlt == nil then
			a = Actor.Create(launcherType, true, {
				Owner = USA,
				Location = CPos.New(c[1], c[2]),
				Facing = Angle.East,
			})
		else
			a = Actor.Create(launcherType, true, {
				Owner = USA,
				CenterPosition = cellPos(c[1], c[2], launcherAlt),
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

-- Re-issued periodically: an armament that runs dry drops its order, and an
-- aircraft that has repositioned may otherwise sit idle for the rest of the run.
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

-- Alternates the move order between two cells so the target is reversing course
-- continuously rather than once. A single scripted reversal would only catch
-- whichever missiles happened to be at the right phase of flight.
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

	-- ---- Air lanes: HELI/Hellfire vs littlebird ----
	-- Target cells and motion scripts are copied verbatim from
	-- test-missile-latch-probe's air lanes so the regimes are identical.
	-- Launchers sit 12 cells out: inside Hellfire's Range of 25c0 and well
	-- outside its MinRange of 5c0, so the attack logic has little reason to
	-- reposition far at the moment the order lands.
	local airFlee     = makeLane("hf_air_flee",     "HELI", {{6,3},{7,3},{6,4}},    HELI_ALT, "littlebird", {18,3},  HELI_ALT)
	local airApproach = makeLane("hf_air_approach", "HELI", {{6,9},{7,9},{6,10}},   HELI_ALT, "littlebird", {22,9},  HELI_ALT)
	local airReverse  = makeLane("hf_air_reverse",  "HELI", {{6,15},{7,15},{6,16}}, HELI_ALT, "littlebird", {18,15}, HELI_ALT)
	local airHover    = makeLane("hf_air_hover",    "HELI", {{6,21},{7,21},{6,22}}, HELI_ALT, "littlebird", {18,21}, HELI_ALT)

	-- ---- Ground lanes: strykershorad/Hellfire.strykershorad vs t90 ----
	-- Hellfire.strykershorad drops Air from its ValidTargets (Vehicle, Defense),
	-- so it CANNOT be run on the air lanes at all — the ground lanes are the
	-- only place this variant can be measured.
	--
	-- SIX launchers per ground lane, against three on the air lanes, and the
	-- asymmetry is deliberate. Hellfire.strykershorad sets BurstWait: 1000 —
	-- 40 seconds at 25 ticks/s — so three launchers produce only two bursts of
	-- two each inside the 60 s run, i.e. 12 missiles for the whole lane. Doubling
	-- the launcher count doubles the sample without touching the weapon's own
	-- cadence, which is the thing under measurement and must not be edited.
	local gndStatic   = makeLane("hf_gnd_static",   "strykershorad", {{40,3},{41,3},{40,4},{41,4},{40,5},{41,5}},       nil, "t90", {52,3},  nil)
	local gndFlee     = makeLane("hf_gnd_flee",     "strykershorad", {{40,15},{41,15},{40,16},{41,16},{40,17},{41,17}}, nil, "t90", {50,15}, nil)
	local gndReverse  = makeLane("hf_gnd_reverse",  "strykershorad", {{40,25},{41,25},{40,26},{41,26},{40,27},{41,27}}, nil, "t90", {52,25}, nil)

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

	-- ---- motion scripts (identical to the MANPAD rig's) ----
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
			local alive = 0
			for _, la in ipairs(lane.launchers) do
				if not la.IsDead then
					alive = alive + 1
					-- Air lanes draw from secondary-ammo, ground lanes from
					-- tertiary-ammo; whichever pool is absent just reports nil.
					for _, pool in ipairs({ "secondary-ammo", "tertiary-ammo" }) do
						local ok, k = pcall(function() return la.AmmoCount(pool) end)
						if ok and k ~= nil and k > 0 then ammo = ammo + k end
					end
				end
			end
			table.insert(parts, string.format("%s[live=%d ammoLeft=%d]", lane.name, alive, ammo))
		end

		Test.Pass(string.format("%d missile records across %d lanes (%d aloft); %s",
			n, #lanes, aloft, table.concat(parts, " ")))
	end)
end
