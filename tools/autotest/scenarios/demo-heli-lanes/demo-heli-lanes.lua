-- DEMO: two heli lanes — littlebird vs Hind, infantry first, then an Apache.
--
-- Nothing here asserts anything and nothing issues orders. You fly it.
-- Pause with space, change speed, press End to restart.
--
-- LAYOUT (100x60 map, two lanes stacked so you can compare them)
--
--   LANE 1, y 15-17   littlebird (yours, x 8)  ->  10x e1 (x 38-41)  ->  Apache (enemy, x 80)
--   LANE 2, y 39-41   Hind       (yours, x 8)  ->  10x e1 (x 38-41)  ->  Apache (enemy, x 80)
--
-- Both squads are the same ten riflemen in the same tight block, so whatever
-- differs between the lanes is the helicopter, not the target.
--
-- HOW IT IS MEANT TO BE FLOWN
--   1. Select your heli on the left, attack the infantry block in the middle.
--      Watch how long the block takes to break and how the squad spreads.
--   2. When the infantry are done, take the same heli east and attack the
--      Apache. That is the interesting half: the littlebird's air-to-air is
--      deliberately weak and its 160 rounds do not reload away from a helipad.
--   3. Run the other lane and compare.
--
-- WHAT IS WORTH WATCHING FOR
--   - The littlebird's guns only started working at all in this build. Every
--     air-to-air number it has was modelled against an airframe that dealt zero
--     damage, so lane 1 versus the Apache has never actually been observed.
--     If it feels too strong, that is the thing to say.
--   - Nothing chases: engagement stance is HoldPosition map-wide. The Apaches
--     hover at x 80 until you come to them, and shoot back when you do. They
--     are spawned AIRBORNE from Lua for that reason — see the comment below.
--   - The Hind carries a gunner; the littlebird does not. That is exactly the
--     difference that was zeroing the littlebird's damage before this build.

local TicksPerSecond = TestHarness.TicksPerSecond

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	-- ALL FOUR HELICOPTERS ARE SPAWNED HERE, AIRBORNE, AND THAT IS DELIBERATE.
	-- ^Airborne sets `Aircraft: TakeOffOnCreation: False`, so a helicopter
	-- placed in map.yaml with a Location starts PARKED ON THE GROUND. Your own
	-- helis hide this — they take off the moment you order them to move — but
	-- the enemy Apaches are on HoldPosition and never get that order, so the
	-- first version of this demo had you shooting at two landed aircraft that
	-- could not manoeuvre or fight back. Spawning with an explicit CenterPosition
	-- altitude is what test-littlebird-strafe does, and it is the only reason
	-- the Apache in that test ever shot back.
	-- Named constants, not raw numbers. WAngle runs COUNTERCLOCKWISE here
	-- (conventions.md): East is 768 and West is 256, so the plausible-looking
	-- 192/704 pair points north-west and south-east — everyone facing outward,
	-- away from the fight, which is exactly how this demo first shipped.
	local function heli(actorType, owner, cx, cy, facing)
		return Actor.Create(actorType, true, {
			Owner = owner,
			CenterPosition = cellPos(cx, cy, 1280),
			Facing = facing,
		})
	end

	local L1HELI   = heli("littlebird", USA,    8, 16, Angle.East)
	local L1APACHE = heli("heli",       Russia, 80, 16, Angle.West)
	local L2HELI   = heli("hind",       USA,    8, 40, Angle.East)
	local L2APACHE = heli("heli",       Russia, 80, 40, Angle.West)

	-- Frame both lanes, then drop the camera on the littlebird and pre-select
	-- it so the first thing you can do is give it an order.
	TestHarness.FocusBetween(L1HELI, L1APACHE, L2HELI, L2APACHE)
	TestHarness.Select(L1HELI)

	Media.DisplayMessage("LANE 1 (top): littlebird.  LANE 2 (bottom): Hind.", "Demo")
	Media.DisplayMessage("Attack the infantry block first, then the Apache on the right.", "Demo")

	-- Count what is left in each lane every few seconds. Purely informational —
	-- a demo reports, it does not judge.
	local lane1 = { L1I01, L1I02, L1I03, L1I04, L1I05, L1I06, L1I07, L1I08, L1I09, L1I10 }
	local lane2 = { L2I01, L2I02, L2I03, L2I04, L2I05, L2I06, L2I07, L2I08, L2I09, L2I10 }

	local function alive(list)
		local n = 0
		for _, a in ipairs(list) do
			if a ~= nil and not a.IsDead and a.IsInWorld then n = n + 1 end
		end
		return n
	end

	local function hpOf(a)
		if a == nil or a.IsDead or not a.IsInWorld then return 0 end
		return a.Health
	end

	-- Altitude is in the readout on purpose. The first cut of this demo had all
	-- four helicopters spawned from map.yaml and therefore PARKED, and nothing
	-- in a hp/count line would have shown it — the Apaches simply sat there
	-- being shot. `alt` is the check that the fix is actually holding: a flying
	-- helicopter reports a non-zero Z.
	local function altOf(a)
		if a == nil or a.IsDead or not a.IsInWorld then return -1 end
		return a.CenterPosition.Z
	end

	local function report()
		print(string.format(
			"[lanes] L1 littlebird hp=%d alt=%d infantry=%d/10 apache=%d alt=%d | L2 hind hp=%d alt=%d infantry=%d/10 apache=%d alt=%d",
			hpOf(L1HELI), altOf(L1HELI), alive(lane1), hpOf(L1APACHE), altOf(L1APACHE),
			hpOf(L2HELI), altOf(L2HELI), alive(lane2), hpOf(L2APACHE), altOf(L2APACHE)))
		Trigger.AfterDelay(5 * TicksPerSecond, report)
	end
	Trigger.AfterDelay(5 * TicksPerSecond, report)
end
