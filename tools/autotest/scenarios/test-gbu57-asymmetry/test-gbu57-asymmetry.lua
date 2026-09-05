-- ASSERTING AUTOTEST — is the GBU-57 actually a bunker buster, or a Kinzhal with a new cameo?
--
-- WHAT IS UNDER TEST. Not the delivery: MissileStrikePower is unchanged since
-- test-missile-strike-power passed at 894e8dc4. What is under test is the single argument that
-- chose the GBU-57 MOP over an LRHW reskin (proposal §4b) — "the US strike hits structures far
-- harder and units far less" — expressed in the MOPPenetration warhead. If the shipped numbers do
-- not produce a visible difference in what the power is good against, the code path exists and the
-- feature does not.
--
-- SO THE TANK SURVIVING IS AN ASSERTION, NOT A TOLERANCE. Check 5 below fails a run in which the
-- GBU-57 destroys the Abrams, and that is the check most likely to be "fixed" by someone who reads
-- it as a bug. It is not: a strike that one-shots armour AND flattens buildings is exactly the
-- option the proposal rejected, and it would make the two factions' powers interchangeable.
--
-- HOW THE TWO FIGURES ARE READ, and what is exact versus bounded.
--   * The TANK figure is exact: it survives, so startHp - endHp is the damage delivered.
--   * The STRUCTURE figure is a FLOOR, not a measurement, because the building dies. All that can
--     be observed is "it took at least its full 60,000 HP". That asymmetry in the evidence is
--     unavoidable — you cannot read the health of a thing that is gone — and it is why the modelled
--     19.3:1 is checked in NUnit (MissilePowerAsymmetryTest, over the shipped YAML) while this run
--     checks the weaker but INDEPENDENT claim that the engine agrees on both signs.
--   Both numbers go into the verdict string regardless, because the harness's other artefacts are
--   not trustworthy: under --hidden, result.json names screenshots that were never written and
--   copied debug.log files have come from a different game entirely.
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * The 3000-tick ChargeInterval. rules.yaml overrides it; the shipped value is a placeholder for
--     the buy model, and it is also what lets one run fire twice.
--   * Flight speed. The MOP is deliberately 4x slower than the Kinzhal (Speed 500 vs 2000), but
--     that is a feel decision, not a correctness one, so the tick budget is loose.
--   * The beacon, the minimap ping and the reveal camera — client-side render state with no
--     observable the Lua API can read.
--
-- EXPECTED GEOMETRY, derived in map.yaml: America home 6,17 -> edge cell 1,17; Logistics Center
-- centred on 36,17 (35 cells from entry); Abrams at 48,17 (47 cells). Speed 500 -> 72 and 96
-- flight ticks.

local OrderKey = "GBU57Strike"
local MissileType = "gbu57bomb"
-- 36,17 is the CENTRE of the Logistics Center, whose map Location is 35,16 -- a building's
-- Location is its top-left cell, not its centre (see map.yaml). Aim at the centre so the geometry
-- printed in the verdict is the geometry that was fired at.
local StructX, StructY = 36, 17
local TankX, TankY = 48, 17

-- Tolerances. Loose on purpose everywhere EXCEPT the two damage signs, which are the point.
local MaxEntryToHome = 15     -- cells. Expected 5. Enemy edge would be 59, map corner 17.
local MinEntryToTarget = 25   -- cells. Expected 35. Spawned-on-target would be 0.
-- RAISED ON 2026-09-05 when MissileDelay 300 shipped on this power. One strike is now 300 ticks of
-- wait plus ~72-96 of flight, so the old 300-tick budget would have advanced phase 1 before the
-- structure died and ended phase 2 before the tank was hit -- the scenario would have failed
-- without the shipped behaviour being wrong.
local StrikeBudget = 550      -- ticks allowed for one strike to land. Expected ~400.
local ObserveTicks = 1400     -- whole-run budget, both strikes.

-- The shipped MissileDelay for this power (player.yaml): 300 ticks = 18.0 s at Timestep 60, DOUBLE
-- the Kinzhal's, because a Massive Ordnance Penetrator is a scheduled demolition rather than a
-- weapon of surprise. SpawnActorEffect counts down one per tick and adds on the tick the counter
-- goes negative (SpawnActorEffect.cs:44-49), installed itself by a frame-end task, so the observed
-- gap runs a couple of ticks over -- hence far more slack above than below.
local ExpectedSpawnDelay = 300
local MinSpawnDelay = ExpectedSpawnDelay - 5
local MaxSpawnDelay = ExpectedSpawnDelay + 40

-- The modelled figure for the tank is 9000 (MOPPenetration Warhead@Surface, Penetration 2500
-- against Thickness 700, so undivided; falloff 100% at the aim point). The band is wide because
-- this asserts a CLASS of outcome — "hurt, not killed" — rather than a tuned number. What it does
-- catch is the two ways the design breaks: zero damage (the surface warhead stopped applying at
-- all, so the power does literally nothing to units and the crater is a lie) and lethal damage.
local MinTankDamage = 2000
local MaxTankDamage = 20000

local tick = 0
local America
local phase = 1               -- 1 = striking the structure, 2 = striking the tank
local phaseStartTick = 0

local structOrderStatus = "never-called"
local structOrderTick = nil
local structStartHp = 0
local structEndHp = nil
local structDeadTick = nil

local tankOrderStatus = "never-called"
local tankOrderTick = nil
local tankStartHp = 0
local tankEndHp = nil
local tankImpactTick = nil

local firstCell = nil
local firstSeenTick = nil
local seenMissiles = 0
local finished = false

local function cellDist(ax, ay, bx, by)
	local dx = ax - bx
	local dy = ay - by
	return math.floor(math.sqrt(dx * dx + dy * dy) + 0.5)
end

local function n(v)
	if v == nil then
		return "none"
	end

	return tostring(v)
end

local function trackMissile()
	local missiles = America.GetActorsByType(MissileType)
	if #missiles > 0 then
		if #missiles > seenMissiles then
			seenMissiles = #missiles
		end

		if firstCell == nil then
			local c = missiles[1].Location
			firstCell = { X = c.X, Y = c.Y }
			firstSeenTick = tick
		end
	end
end

local function pollTick()
	tick = tick + 1
	trackMissile()

	if phase == 1 then
		-- Retry until the power reports ready rather than assuming it is armed on tick 1:
		-- SupportPowerInstance.Active is only set inside Tick(), and the TechTree pass that
		-- satisfies `Prerequisites: player.america` runs on its own schedule.
		if structOrderTick == nil then
			structOrderStatus = Test.ActivateSupportPower(America, OrderKey, CPos.New(StructX, StructY))
			if structOrderStatus == "issued" then
				structOrderTick = tick
			end

			return
		end

		if StructVictim.IsDead then
			structEndHp = 0
			structDeadTick = tick
		else
			-- Kept fresh every tick so that if the building somehow SURVIVES, the verdict carries
			-- the real figure rather than its starting health.
			structEndHp = StructVictim.Health
		end

		if structDeadTick ~= nil or (tick - phaseStartTick) > StrikeBudget then
			phase = 2
			phaseStartTick = tick
		end

		return
	end

	-- Phase 2: the tank.
	if tankOrderTick == nil then
		tankOrderStatus = Test.ActivateSupportPower(America, OrderKey, CPos.New(TankX, TankY))
		if tankOrderStatus == "issued" then
			tankOrderTick = tick
		end

		return
	end

	if TankVictim.IsDead then
		tankEndHp = 0
		if tankImpactTick == nil then
			tankImpactTick = tick
		end

		return
	end

	local hp = TankVictim.Health
	if hp < tankStartHp and tankImpactTick == nil then
		tankImpactTick = tick
	end

	tankEndHp = hp
end

local function finish()
	local home = America.HomeLocation
	local entryX = firstCell ~= nil and firstCell.X or -1
	local entryY = firstCell ~= nil and firstCell.Y or -1
	local toHome = firstCell ~= nil and cellDist(entryX, entryY, home.X, home.Y) or -1
	local toTarget = firstCell ~= nil and cellDist(entryX, entryY, StructX, StructY) or -1
	local spawnDelay = (structOrderTick ~= nil and firstSeenTick ~= nil)
		and (firstSeenTick - structOrderTick) or -1

	local structState = StructVictim.IsDead and "DEAD" or (n(structEndHp) .. "hp")
	local structFloor = StructVictim.IsDead and structStartHp or (structStartHp - (structEndHp or structStartHp))
	local tankFinal = tankEndHp or tankStartHp
	local tankDamage = tankStartHp - tankFinal
	local tankState = TankVictim.IsDead and "DEAD" or (tankFinal .. "hp")

	-- The two figures the whole scenario exists to report, side by side, with the ratio spelled
	-- out so nobody has to divide them by hand off a terminal.
	local ratio = "n/a"
	if tankDamage > 0 then
		ratio = string.format("%.1f", structFloor / tankDamage)
	end

	local summary = "STRUCTURE logisticscenter " .. structStartHp .. "hp -> " .. structState
		.. " (took >=" .. structFloor .. ")"
		.. " | UNIT abrams " .. tankStartHp .. "hp -> " .. tankState
		.. " (took " .. tankDamage .. ")"
		.. " | observed ratio >=" .. ratio .. ":1 (modelled 19.3:1)"
		.. " || orders struct=" .. structOrderStatus .. "@t" .. n(structOrderTick)
		.. " tank=" .. tankOrderStatus .. "@t" .. n(tankOrderTick)
		.. " | entry=" .. entryX .. "," .. entryY .. "@t" .. n(firstSeenTick)
		.. " spawn delay=" .. spawnDelay .. "t (shipped " .. ExpectedSpawnDelay .. ")"
		.. " home=" .. home.X .. "," .. home.Y
		.. " entry->home=" .. toHome .. "c entry->struct=" .. toTarget .. "c"
		.. " | struct dead@t" .. n(structDeadTick) .. " tank hit@t" .. n(tankImpactTick)
		.. " | bombs seen=" .. seenMissiles .. " observed=" .. tick .. "t"

	-- 1. Did the order path reach the trait at all, both times? Every non-issued status names its
	-- own cause: not-ready:<n>, unknown-power:<key> (have: ...), no-manager.
	if structOrderStatus ~= "issued" then
		Test.Fail("the GBU-57 power never fired at the structure, so nothing was measured. || " .. summary)
		return
	end

	if tankOrderStatus ~= "issued" then
		Test.Fail("the GBU-57 fired at the structure but never at the tank, so only half the"
			.. " comparison exists. The power re-arms in 1 tick under this scenario's rules, so a"
			.. " not-ready here means the recharge override did not apply. || " .. summary)
		return
	end

	-- 2. Did a bomb actor reach the world? An empty result after a successful order is the
	-- Target-handshake exception: BallisticMissileFly reads Target.CenterPosition from
	-- AddedToWorld, so an unset Target throws there (MissileSpawnerMaster.cs:85-87).
	if firstCell == nil then
		Test.Fail("the orders were accepted but no " .. MissileType .. " ever entered the world."
			.. " || " .. summary)
		return
	end

	-- 3. Did it come in from the edge nearest ITS OWN base? Checked before the damage, because a
	-- bomb spawned on top of the target does the same damage while delivering none of the
	-- behaviour the feature exists for.
	if toTarget < MinEntryToTarget then
		Test.Fail("the bomb did not fly in from anywhere: it first appeared " .. toTarget
			.. " cells from the structure (needs >= " .. MinEntryToTarget .. "). || " .. summary)
		return
	end

	if toHome > MaxEntryToHome then
		Test.Fail("the bomb entered " .. toHome .. " cells from America's own base (allowance "
			.. MaxEntryToHome .. "). ChooseClosestEdgeCell(home) is cell 1,17 for this map. || " .. summary)
		return
	end

	-- 4. THE FIRST HALF OF THE DESIGN CLAIM: one MOP ends a hardened structure.
	if not StructVictim.IsDead then
		Test.Fail("the GBU-57 left a " .. structStartHp .. "hp Logistics Center standing at "
			.. n(structEndHp) .. "hp. MOPPenetration puts a modelled 174000 on it — 165000 from"
			.. " Warhead@Penetrate plus 9000 surface — so surviving means Warhead@Penetrate did not"
			.. " apply. Two likely causes, in order: its `ValidTargets: Structure` failing to match"
			.. " (check ^BasicBuilding still grants `Structure`, structures.yaml:27-28), or"
			.. " Warhead@Penetrate having been changed from SpreadDamage back to TargetDamage, which"
			.. " reintroduces centre-proximity scaling and would deliver only ~33% on a corner hit."
			.. " || " .. summary)
		return
	end

	-- 5. THE SECOND HALF, AND THE ONE THAT IS THE WHOLE POINT: it must NOT do that to armour.
	if TankVictim.IsDead then
		Test.Fail("the GBU-57 destroyed a " .. tankStartHp .. "hp Abrams. THIS IS THE FAILURE THE"
			.. " SCENARIO EXISTS FOR, and it is not a tolerance to widen: a strike that flattens"
			.. " buildings AND one-shots armour is the LRHW reskin the proposal rejected (§4a), and"
			.. " it makes America's power and Russia's interchangeable. Expected ~9000 damage, from"
			.. " MOPPenetration's Warhead@Surface only — a vehicle carries no `Structure` target"
			.. " type, so the 165000 penetrating charge must never reach it. Check whether"
			.. " Warhead@Penetrate's ValidTargets widened. || " .. summary)
		return
	end

	if tankDamage < MinTankDamage then
		Test.Fail("the GBU-57 put only " .. tankDamage .. " on an Abrams it landed directly on"
			.. " (floor " .. MinTankDamage .. ", expected ~9000). 'Weak against armour' should still"
			.. " be a crater; near-zero means Warhead@Surface stopped applying, so the power does"
			.. " literally nothing to units. || " .. summary)
		return
	end

	if tankDamage > MaxTankDamage then
		Test.Fail("the GBU-57 put " .. tankDamage .. " on an Abrams (ceiling " .. MaxTankDamage
			.. ", expected ~9000). The tank survived, but the anti-armour figure has drifted far"
			.. " enough that the asymmetry is eroding. || " .. summary)
		return
	end

	-- 6. THE SPAWN DELAY, measured on the FIRST strike because that is the one whose order tick and
	-- first-sighting tick are both recorded. The user's words: "the strike needs to be delayed, so
	-- that it doesnt enter the map exactly when we click". Near-zero means either MissileDelay was
	-- dropped from player.yaml or MissileStrikePower stopped routing it through SpawnActorEffect.
	if spawnDelay < MinSpawnDelay or spawnDelay > MaxSpawnDelay then
		Test.Fail("the GBU-57 entered the map " .. spawnDelay .. " ticks after the order (band "
			.. MinSpawnDelay .. ".." .. MaxSpawnDelay .. ", shipped MissileDelay "
			.. ExpectedSpawnDelay .. " = 18.0 s at Timestep 60). || " .. summary)
		return
	end

	Test.Pass("GBU-57 asymmetry holds: after a " .. spawnDelay .. "t wait it ended the hardened"
		.. " structure and the tank walked away. || " .. summary)
end

local function step()
	pollTick()

	local done = (tankImpactTick ~= nil and (tick - tankImpactTick) > 5)
		or (tankOrderTick ~= nil and (tick - tankOrderTick) > StrikeBudget)
		or tick >= ObserveTicks

	if not finished and done then
		finished = true
		finish()
		return
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	America = Player.GetPlayer("USA")
	if America == nil then
		Test.Fail("USA player not found")
		return
	end

	if StructVictim == nil or TankVictim == nil then
		Test.Fail("StructVictim or TankVictim missing from the map")
		return
	end

	structStartHp = StructVictim.Health
	tankStartHp = TankVictim.Health

	TestHarness.FocusBetween(StructVictim, TankVictim)
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
