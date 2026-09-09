-- ASSERTING AUTOTEST — does a strike aimed BEHIND the player's own Supply Route still arrive
-- from behind it?
--
-- WHAT IS UNDER TEST, and why its sibling cannot see it. test-missile-strike-power asserts that a
-- salvo arrives along the line from the launching player's own position through the aim point, and
-- every aim point it can reach is INWARD of Russia's Supply Route. Inward is exactly the half of
-- the map on which the pre-2026-09-08 bearing rule was correct, so that scenario went green
-- through the whole life of this bug.
--
-- THE BUG, as the user reported it on 2026-09-08: "When I target a strike close to my own SR the
-- missile comes in from the other side, instead of from behind my own SR as it otherwise does."
-- MissileStrikeApproach.Bearing read the azimuth straight off home -> salvo centroid, and
-- SpawnPosition walks BACKWARD up that azimuth by a standoff longer than the map diagonal. The
-- birth point therefore always landed on the opposite side of home from the aim point -- which is
-- "behind my own SR" only while the aim point is inward of it. Put the aim point on the OUTWARD
-- side and home -> centroid points off the map, so the backward walk put the missile on the far
-- side of the world.
--
-- THE READING, and it is NOT the sibling's. TestHarness.MeasureApproach measures against the
-- home -> target axis, which on this layout IS the bug's own axis: a missile born 90 cells the
-- wrong way along it reads as a clean along/lateral pair and passes. The corridor reading below is
-- computed inline for that reason, and is deliberately not promoted into test-helpers.lua until a
-- second scenario needs it.
--
-- EXPECTED GEOMETRY, derived from the shipped MissileStrikeApproach and restated so a failure is
-- readable without opening map.yaml:
--   standoff = ISqrt((1024*66)^2 + (1024*34)^2) + ApproachMargin 16384 = 92408 wdist = 90.2 cells
--   Russia home 8,17;  map centre 33,17;  corridor due EAST;  outward due WEST
--   aim 2,17 -- six cells due WEST of Russia's own SR, i.e. DIRECTLY ASTERN of it
--   OLD RULE:  spawn cell  92,17  -- 28 cells past the map's own EAST edge, beyond the American
--                                    SR at 58,17; the warhead crossed the whole board
--   SHIPPED:   spawn cell -88,17  -- 96.2 cells behind Russia's SR, on the SR's own row
--   The lean is ZERO here: the aim bearing is 180 degrees off the corridor and
--   MissileStrikeApproach tapers the lean back to zero as the aim bearing swings to astern rather
--   than holding it at MaxCorridorDeviation, which would have brought the warhead in over Russia's
--   shoulder at 56.6 degrees -- inside the invariant and side-on to anyone watching.
--   Flight: 92408 / 2000 = 46 ticks at KinzhalMissile's Speed 2000.
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * Interception. There is no SAM on this map, so the flight is uncontested and the tick budget
--     stays readable. test-sam-vs-kinzhal covers the contested case.
--   * The beacon, the minimap ping and the reveal camera: client-side render state with no
--     observable the Lua API can read.
--   * The MissileDelay band. Its sibling pins that, on the same power, from the same order path;
--     asserting it twice would make two scenarios fail for one cause.

local OrderKey = "KinzhalStrike"
local BuyProxy = "power.kinzhal"
-- 2,17: six cells due WEST of OwnSR at 8,17, and the entire scenario. At 20,17 -- the inward
-- mirror of it -- the old rule and the shipped rule give the SAME spawn cell and the run proves
-- nothing.
local TargetX, TargetY = 2, 17
local MissileType = "kinzhalmissile"

-- The map centre in CELLS, from MapSize 66,34 and not from Bounds: MissileStrikePower passes
-- map.MapSize.X / 2 to MissileStrikeApproach.For, and Bounds 1,1,64,32 would give 33,17 by
-- coincidence here and a different answer on any map whose border is not symmetric.
local MapCentreX, MapCentreY = 33, 17

-- THE CORRIDOR TOLERANCE. cos(60 degrees) = 0.5, matching MissileStrikeApproach.MaxCorridorDeviation
-- (170 raw WAngle units = 59.8 degrees). This is not an allowance on a distance -- it is the
-- shipped bound on how far round the side of the launcher an approach may sit, so a reading past
-- it is a real fault and not slack running out. The expected value here is 1.0, dead astern.
local MinOutwardCosine = 0.5
-- Distance from home to the birth point. Expected 96.2 cells = standoff 90.2 + the 6 cells the aim
-- point sits beyond home. The band exists to catch two regressions that BOTH satisfy the cosine:
--   * a return to ChooseClosestEdgeCell(home), which lands an on-map edge cell ~7 cells out;
--   * a flight target defaulting to WPos.Zero, which reads as an entry near the map's top-left.
local MinEntryToHome, MaxEntryToHome = 70, 115
-- Measured from WORLD ENTRY, not from the order: MissileDelay 150 would otherwise dominate and the
-- reading would say nothing about Speed. Expected ~46 at Speed 2000 over the 90.2-cell standoff.
local MaxFlightTicks = 90
local ObserveTicks = 400

local tick = 0
local Russia
local orderStatus = "never-called"
local orderTick = nil
local buyStatus = "never-called"
local buyTick = nil
local firstSeenTick = nil
local firstCell = nil
local lastCell = nil
local impactTick = nil
local victimStartHealth = 0
local finished = false

local function cellDist(ax, ay, bx, by)
	local dx = ax - bx
	local dy = ay - by
	return math.sqrt(dx * dx + dy * dy)
end

local function n(v)
	if v == nil then
		return "none"
	end

	return tostring(v)
end

-- The corridor reading. Returns the cosine of the angle between (entry - home) and the launching
-- player's own OUTWARD axis (home - map centre), and the entry-to-home distance in cells. A cosine
-- of 1 is dead astern; 0 is square off the flank; -1 is the reported bug, the warhead born on the
-- map-interior side of its own launcher.
local function corridorReading(entryX, entryY, homeX, homeY)
	local ox, oy = homeX - MapCentreX, homeY - MapCentreY
	local olen = math.sqrt(ox * ox + oy * oy)
	local ex, ey = entryX - homeX, entryY - homeY
	local elen = math.sqrt(ex * ex + ey * ey)

	if olen == 0 or elen == 0 then
		return nil, elen
	end

	return ((ex * ox) + (ey * oy)) / (olen * elen), elen
end

local function pollTick()
	tick = tick + 1

	-- Fill the magazine. Stateless and idempotent, so it is simply called every tick until the shot
	-- is banked (about t=12 at the 5-tick BuildDuration rules.yaml sets). The status token is
	-- carried into the verdict because 'refused' and 'not-ready:0' look identical from the order's
	-- side and are completely different faults.
	if buyTick == nil then
		local ready, status = TestHarness.EnsurePower(Russia, BuyProxy, OrderKey, tick)
		buyStatus = status
		if ready then
			buyTick = tick
		end
	end

	-- Retry until the power reports ready rather than assuming it is armed the moment it is paid
	-- for: SupportPowerInstance.Active is only set inside Tick(), and the TechTree pass that
	-- satisfies `Prerequisites: powers.russia` runs on its own schedule.
	if orderTick == nil then
		orderStatus = Test.ActivateSupportPower(Russia, OrderKey, CPos.New(TargetX, TargetY))
		if orderStatus == "issued" then
			orderTick = tick
		end

		return
	end

	local missiles = Russia.GetActorsByType(MissileType)
	if #missiles > 0 then
		local c = missiles[1].Location
		if firstCell == nil then
			firstCell = { X = c.X, Y = c.Y }
			firstSeenTick = tick
		end

		lastCell = { X = c.X, Y = c.Y }
	end

	if impactTick == nil and Victim.IsDead then
		impactTick = tick
	end
end

local function finish()
	local home = Russia.HomeLocation
	local entryX = firstCell ~= nil and firstCell.X or -1
	local entryY = firstCell ~= nil and firstCell.Y or -1

	local cosine, entryToHome = nil, -1
	if firstCell ~= nil then
		cosine, entryToHome = corridorReading(entryX, entryY, home.X, home.Y)
	end

	local flight = (firstSeenTick ~= nil and impactTick ~= nil) and (impactTick - firstSeenTick) or -1
	local orderToImpact = (orderTick ~= nil and impactTick ~= nil) and (impactTick - orderTick) or -1
	local spawnDelay = (orderTick ~= nil and firstSeenTick ~= nil) and (firstSeenTick - orderTick) or -1
	local victimState = Victim.IsDead and "DEAD" or (Victim.Health .. "hp")

	local summary = "magazine=" .. buyStatus .. "@t" .. n(buyTick)
		.. " | order=" .. orderStatus .. "@t" .. n(orderTick)
		.. " | entry=" .. entryX .. "," .. entryY .. "@t" .. n(firstSeenTick)
		.. " last=" .. (lastCell ~= nil and (lastCell.X .. "," .. lastCell.Y) or "none")
		.. " home=" .. home.X .. "," .. home.Y
		.. " target=" .. TargetX .. "," .. TargetY
		.. " | corridor cos=" .. (cosine ~= nil and string.format("%.3f", cosine) or "n/a")
		.. " (min " .. MinOutwardCosine .. ", expected 1.000)"
		.. " entry->home=" .. string.format("%.1f", entryToHome) .. "c"
		.. " (band " .. MinEntryToHome .. ".." .. MaxEntryToHome .. ", expected 96.2)"
		.. " | spawn delay=" .. spawnDelay .. "t"
		.. " | impact@t" .. n(impactTick) .. " flight=" .. flight .. "t"
		.. " order->impact=" .. orderToImpact .. "t"
		.. " | victim " .. victimStartHealth .. "hp -> " .. victimState
		.. " | observed=" .. tick .. "t"

	-- 1. Did the order path reach the trait at all? Every non-issued status names its own cause:
	-- not-ready:<n>, unknown-power:<key> (have: ...), no-manager.
	if orderStatus ~= "issued" then
		Test.Fail("the Kinzhal power never fired, so nothing about the approach was measured. || " .. summary)
		return
	end

	-- 2. Did a missile actor reach the world? An empty result here after a successful order is the
	-- Target handshake: BallisticMissileFly reads Target.CenterPosition from AddedToWorld, so an
	-- unset Target throws there (MissileSpawnerMaster.cs:85-87) and the actor never appears.
	if firstCell == nil then
		Test.Fail("the order was accepted but no " .. MissileType .. " ever entered the world."
			.. " That is the BallisticMissile.Target handshake. || " .. summary)
		return
	end

	-- 3. THE ASSERTION THIS SCENARIO EXISTS FOR. Checked before the kill, because a missile that
	-- crosses the entire board from the wrong side kills the Abrams just as dead.
	if cosine == nil then
		Test.Fail("the birth point coincides with Russia's own Supply Route cell, so there is no"
			.. " corridor reading to take. That is a scenario fault, not a shipped one. || " .. summary)
		return
	end

	if cosine < MinOutwardCosine then
		Test.Fail("THE STRIKE CAME IN FROM THE WRONG SIDE. The warhead was born at " .. entryX .. ","
			.. entryY .. ", which is " .. string.format("%.3f", cosine) .. " on the cosine against"
			.. " Russia's own outward axis (minimum " .. MinOutwardCosine .. " = 60 degrees, expected"
			.. " 1.000 = dead astern). A NEGATIVE reading is the reported bug exactly: the bearing was"
			.. " taken from home -> aim point rather than from Russia's own corridor, so aiming BEHIND"
			.. " the Supply Route inverted the walk-back and put the missile on the far side of the"
			.. " map -- expected spawn cell -88,17, the bug gives 92,17. A reading between 0 and 0.5"
			.. " means MaxCorridorDeviation was widened, or the lean stopped tapering back to zero"
			.. " for a target directly astern. || " .. summary)
		return
	end

	if entryToHome < MinEntryToHome or entryToHome > MaxEntryToHome then
		Test.Fail("the warhead came in from the right direction but the wrong distance: "
			.. string.format("%.1f", entryToHome) .. " cells behind Russia against an expected 96.2"
			.. " (band " .. MinEntryToHome .. ".." .. MaxEntryToHome .. "). BELOW the band is a"
			.. " regression to an on-map spawn -- ChooseClosestEdgeCell(home) lands about 7 cells"
			.. " out, and a standoff taken from Bounds rather than MapSize reads the same way more"
			.. " mildly. ABOVE it, check MissileStrikeApproach.MaxStandoff: CPos packs each axis into"
			.. " 12 signed bits and wraps outside -2048..2047. || " .. summary)
		return
	end

	-- 4. Did it arrive? BallisticMissileFly ends with SetPosition(targetPos) then Kill, and
	-- IskanderExplosion puts ~62800 on a 28000hp Abrams, so arrival means death: a live Abrams here
	-- means the missile did not get there, not that it missed.
	if impactTick == nil then
		Test.Fail("the missile came in from behind Russia's Supply Route, correctly, but the Abrams"
			.. " is still alive after " .. tick .. " ticks. The flight did not complete, or Explodes"
			.. " did not fire on it. || " .. summary)
		return
	end

	-- 5. Was it still hypersonic? The 90.2-cell standoff at Speed 2000 is 46 ticks; the budget is
	-- 2x that. This is here because the fix CHANGED THE BEARING and the standoff is unchanged: a
	-- reading far off 46 would mean the flight length stopped being the standoff.
	if flight > MaxFlightTicks then
		Test.Fail("the strike took " .. flight .. " ticks from world entry to impact (budget "
			.. MaxFlightTicks .. ", expected ~46 at Speed 2000 over the 90.2-cell standoff). The"
			.. " standoff does not depend on the bearing, so a long reading here means the flight"
			.. " length stopped being the standoff. || " .. summary)
		return
	end

	Test.Pass("the strike was aimed six cells BEHIND Russia's own Supply Route and still came in"
		.. " from behind it: born at " .. entryX .. "," .. entryY .. ", cosine "
		.. string.format("%.3f", cosine) .. " on Russia's own outward axis, "
		.. string.format("%.1f", entryToHome) .. " cells astern, and killed its target in "
		.. flight .. "t. || " .. summary)
end

local function step()
	pollTick()

	if not finished and (impactTick ~= nil or tick >= ObserveTicks) then
		finished = true
		finish()
		return
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	Russia = Player.GetPlayer("Russia")
	if Russia == nil then
		Test.Fail("Russia player not found")
		return
	end

	if Victim == nil then
		Test.Fail("Victim actor missing from the map")
		return
	end

	victimStartHealth = Victim.Health

	TestHarness.FocusBetween(OwnSR, Victim)
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
