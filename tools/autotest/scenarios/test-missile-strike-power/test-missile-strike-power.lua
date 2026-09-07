-- ASSERTING AUTOTEST — does MissileStrikePower deliver a missile on the player's own bearing?
--
-- WHAT IS UNDER TEST. MissileStrikePower is the only genuinely new engine code in the
-- missile-strike-powers feature (WORKSPACE/proposals/260904-missile-powers.md §10), and all
-- four planned powers ride on it. It takes ONE azimuth for the salvo, from the owner's own
-- HomeLocation toward what they aimed at, walks back up it past the map boundary by the map
-- diagonal plus ApproachMargin, creates a BallisticMissile ACTOR there at altitude, hands it a
-- Target, and lets BallisticMissile.AddedToWorld queue the flight.
--
-- IT USED TO PICK Map.ChooseClosestEdgeCell(HomeLocation) -- an ON-MAP cell on the owner's own
-- border, the AirstrikePower.cs:79 precedent commit a20c8a82 established for this mod -- and then
-- face each warhead at its OWN aim point, which fanned a six-warhead MIRV outward from a single
-- point. Engine b69681d2 replaced that on 2026-09-07; the edge rule is still correct for aircraft
-- and AirstrikePower still uses it.
--
-- THE HAZARD THE GEOMETRY CHECK EXISTS FOR, and it is why "did the tank die" is NOT a
-- sufficient assertion. BallisticMissileFly's constructor reads Target.CenterPosition
-- unconditionally (BallisticMissileFly.cs:45), and it is constructed from AddedToWorld
-- (BallisticMissile.cs:218). So:
--
--   * Target set AFTER the world add  -> InvalidOperationException, the one documented at
--     MissileSpawnerMaster.cs:85-87. No missile at all: caught by check 2.
--   * A cell-shaped target on the wrong field -> the missile flies to WPos.Zero, the map's
--     top-left corner, WITH NO ERROR. Caught by check 3, and by nothing else.
--   * The SpawnActorPower shape (LocationInit at the target cell, SpawnActorPower.cs:86)
--     -> the missile appears ON the target and the tank still dies. That would PASS a
--     damage-only assertion while delivering nothing the feature is for. Caught by check 3.
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * The 3-minute ChargeInterval. rules.yaml sets StartFullyCharged; the shipped interval is
--     a placeholder for the Phase 2 buy model and waiting it out would spend the slot on a
--     timer.
--   * Interception. STALE UNTIL 2026-09-05, WHEN BOTH ITS PREMISES STOPPED BEING TRUE: the
--     SAM became buildable (structures-defenses.yaml) and KinzhalMissile was retyped
--     `Hypersonic` -> `ICBM`, so a SAM CAN now acquire this missile. The successor scenario
--     the old note asked for exists and asserts the opposite of what it predicted:
--     test-sam-vs-kinzhal, where a SAM must visibly launch AND still fail to stop the
--     strike. This scenario stays deliberately interception-free — there is no SAM on this
--     map, so the flight it measures is uncontested and its tick budgets stay readable.
--   * The beacon, the minimap ping and the reveal camera. All three are client-side render
--     state with no observable the Lua API can read.
--
-- EXPECTED GEOMETRY, derived in map.yaml and restated so a failure is readable without it:
-- Russia home 6,17; victim at 48,6, DELIBERATELY OFF HOME'S ROW; axis 43.4 cells; standoff
-- 90.2 cells (map diagonal 74.2 + ApproachMargin 16); entry cell -39,29, which is 46.8 cells
-- behind home along the home->victim ray and 0.2 cells off it; 46 flight ticks at Speed 2000
-- (92408 wdist / 2000). THE VICTIM'S ROW IS THE ASSERTION: on row 17 every candidate bearing
-- rule -- the shipped one, a faction constant, "always from the east" -- gives the same eastward
-- answer, and only the standoff distance can be read. See the GEOMETRY block in map.yaml for the
-- four wrong answers and which of the three readings each one misses.

local OrderKey = "KinzhalStrike"
-- THE SHOT IS BOUGHT, NOT CHARGED, and until 2026-09-07 this scenario could not fire at all.
-- MissileStrikePower@Kinzhal carries `RequiresPurchase: True`, which replaces the charge timer with
-- a magazine: at zero banked shots SupportPowerInstance.Disabled is true, Active is false, Ready is
-- false, and Test.ActivateSupportPower returns 'not-ready:0' for the whole budget. The
-- `ChargeInterval: 1` + `StartFullyCharged` this scenario's rules.yaml used to set were READ AND
-- DISCARDED by the instance constructor (SupportPowerManager.cs:228-229) -- staging that looked
-- deliberate and did nothing. GrantCharge has exactly one caller in the engine,
-- SupportPowerProductionQueue.BuildUnit, so buying is the only route. The whole chain is in the
-- header of TestHarness.EnsurePower (mods/ww3mod/scripts/test-helpers.lua).
--
-- The Kinzhal's tier is `powers.russia` and this player IS Russia, so no sandbox lobby option is
-- needed here -- only the cash and the shortened proxy load time, both in rules.yaml.
local BuyProxy = "power.kinzhal"
local TargetX, TargetY = 48, 17
local MissileType = "kinzhalmissile"

-- Tolerances. All three are loose on purpose: this asserts a CLASS of behaviour (came in from
-- over my own shoulder, crossed the map fast, hit what it was aimed at), not tuned numbers. The three
-- wrong answers listed in the header miss every one of them by a wide margin.
-- THE APPROACH TOLERANCES ARE NOT AN ALLOWANCE ON A DISTANCE -- that is what the two constants
-- they replaced were, and widening `MaxEntryToHome` from 15 to 60 would have gone green on a
-- missile arriving from any compass point at the right radius. These bound the error on a DERIVED
-- position; the derivation and all three readings are in TestHarness.ApproachFault. MapSize is
-- passed because the standoff is the map DIAGONAL plus ApproachMargin, and it is MapSize from
-- map.yaml (66,34), not Bounds.
local MapCellsX, MapCellsY = 66, 34
local MaxApproachLateral = 4  -- cells off the home->target axis. Exact answer 0; expected 0.2.
local MaxApproachLag = 8      -- cells of flight before the poller first saw it. Expected ~0.3.
-- NOW MEASURED FROM WORLD ENTRY, NOT FROM THE ORDER, and that is a sharpening rather than a
-- loosening. MissileDelay 150 shipped on 2026-09-05, so order-to-kill is dominated by the wait and
-- would no longer say anything about Speed. Entry-to-kill is the flight and nothing else, so this
-- budget still means what its name says. Expected ~46 at Speed 2000 over the 90.2-cell standoff.
local MaxFlightTicks = 90
local ObserveTicks = 400     -- whole-run budget; raised from 300 to cover the 150-tick wait.

-- The shipped MissileDelay for this power (player.yaml): 150 ticks = 9.0 s at Timestep 60, the
-- SHORTEST of the three strikes because the Kinzhal's caption promises tempo. SpawnActorEffect
-- counts down one per tick and adds on the tick the counter goes negative
-- (SpawnActorEffect.cs:44-49), installed itself by a frame-end task, so the observed gap runs a
-- couple of ticks over. Far more slack above than below for that reason.
local ExpectedSpawnDelay = 150
local MinSpawnDelay = ExpectedSpawnDelay - 5
local MaxSpawnDelay = ExpectedSpawnDelay + 40

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
	return math.floor(math.sqrt(dx * dx + dy * dy) + 0.5)
end

local function n(v)
	if v == nil then
		return "none"
	end

	return tostring(v)
end

local function pollTick()
	tick = tick + 1

	-- Fill the magazine. Stateless and idempotent, so it is simply called every tick until the shot
	-- is banked (about t=12 at the 5-tick BuildDuration this scenario's rules.yaml sets). The order
	-- retry below picks it up on the tick it lands. The status token is carried into the verdict
	-- because 'refused' and 'not-ready:0' look identical from the order's side and are completely
	-- different faults.
	if buyTick == nil then
		local ready, status = TestHarness.EnsurePower(Russia, BuyProxy, OrderKey, tick)
		buyStatus = status
		if ready then
			buyTick = tick
		end
	end

	-- Retry until the power reports ready rather than assuming it is armed the moment it is paid for:
	-- SupportPowerInstance.Active is only set inside Tick(), and the TechTree pass that
	-- satisfies `Prerequisites: powers.russia` runs on its own schedule. The last status
	-- string is kept and printed either way, so "never fired" always says WHY.
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
	local toHome = firstCell ~= nil and cellDist(entryX, entryY, home.X, home.Y) or -1
	local toTarget = firstCell ~= nil and cellDist(entryX, entryY, TargetX, TargetY) or -1

	-- THE APPROACH CONTRACT. toHome and toTarget stay in the summary as raw diagnostics, but
	-- neither is asserted any more: toHome was the assertion this scenario got wrong, and it reads
	-- ~47 on a correct strike against the 15 it used to demand.
	local approach = nil
	local approachLine = "approach=no entry"
	if firstCell ~= nil then
		approach = TestHarness.MeasureApproach(entryX, entryY, home.X, home.Y,
			TargetX, TargetY, MapCellsX, MapCellsY)
		approachLine = TestHarness.ApproachSummary(approach)
	end

	local orderToImpact = (orderTick ~= nil and impactTick ~= nil) and (impactTick - orderTick) or -1
	local spawnDelay = (orderTick ~= nil and firstSeenTick ~= nil) and (firstSeenTick - orderTick) or -1
	local flight = (firstSeenTick ~= nil and impactTick ~= nil) and (impactTick - firstSeenTick) or -1
	local victimState = Victim.IsDead and "DEAD" or (Victim.Health .. "hp")

	local summary = "magazine=" .. buyStatus .. "@t" .. n(buyTick)
		.. " | order=" .. orderStatus .. "@t" .. n(orderTick)
		.. " | entry=" .. entryX .. "," .. entryY .. "@t" .. n(firstSeenTick)
		.. " last=" .. (lastCell ~= nil and (lastCell.X .. "," .. lastCell.Y) or "none")
		.. " home=" .. home.X .. "," .. home.Y
		.. " target=" .. TargetX .. "," .. TargetY
		.. " entry->home=" .. toHome .. "c entry->target=" .. toTarget .. "c"
		.. " | " .. approachLine
		.. " | spawn delay=" .. spawnDelay .. "t (shipped " .. ExpectedSpawnDelay .. ")"
		.. " | impact@t" .. n(impactTick) .. " flight=" .. flight .. "t"
		.. " order->impact=" .. orderToImpact .. "t"
		.. " | victim " .. victimStartHealth .. "hp -> " .. victimState
		.. " | observed=" .. tick .. "t"

	-- 1. Did the order path reach the trait at all? Every non-issued status names its own
	-- cause: not-ready:<n>, unknown-power:<key> (have: ...), no-manager.
	if orderStatus ~= "issued" then
		Test.Fail("the Kinzhal power never fired, so nothing about delivery was measured. || " .. summary)
		return
	end

	-- 2. Did a missile actor reach the world? An empty result here after a successful order is
	-- the Target-handshake exception: BallisticMissileFly threw inside AddedToWorld.
	if firstCell == nil then
		Test.Fail("the order was accepted but no " .. MissileType .. " ever entered the world."
			.. " That is the BallisticMissile.Target handshake: BallisticMissileFly reads"
			.. " Target.CenterPosition from AddedToWorld, so an unset Target throws there"
			.. " (MissileSpawnerMaster.cs:85-87) and the actor never appears. || " .. summary)
		return
	end

	-- 3. Did it fly the shipped approach -- on the bearing Russia's OWN position gives, in over
	-- Russia's own shoulder, from off-map? Checked before the kill, because a missile spawned on
	-- top of the target kills it just as dead while delivering none of the behaviour the feature
	-- exists for. That case is not dropped: a spawn on the target reads `along` POSITIVE and is
	-- caught by the wrong-side branch, which names it.
	--
	-- This is the scenario whose layout makes all three readings live -- the victim is off home's
	-- row precisely so that the BEARING is falsifiable here and not merely stated. The three
	-- siblings that assert the same contract (test-tacnuke-delivers, test-gbu57-asymmetry,
	-- test-power-buy-loop) sit on east-west layouts where only the distance reading has teeth.
	local approachFault = TestHarness.ApproachFault(approach, MaxApproachLateral, MaxApproachLag)
	if approachFault ~= nil then
		Test.Fail("the Kinzhal did not fly the shipped approach. " .. approachFault
			.. ". An entry near 0,0 means the flight target defaulted to WPos.Zero; an entry on the"
			.. " far side of the map means the bearing was taken from something other than the"
			.. " owner's HomeLocation. || " .. summary)
		return
	end

	-- 4. Did it arrive? Precision is free on this path -- BallisticMissileFly sets position
	-- exactly to targetPos and there is no projectile to scatter -- so a live Abrams here means
	-- the missile did not get there, not that it missed.
	if impactTick == nil then
		Test.Fail("the missile entered correctly but the Abrams is still alive after " .. tick
			.. " ticks. BallisticMissileFly ends with SetPosition(targetPos) then Kill, and"
			.. " IskanderExplosion puts ~62800 on a 28000hp Abrams, so arrival means death:"
			.. " the flight did not complete, or Explodes did not fire on it. || " .. summary)
		return
	end

	-- 5. Was it hypersonic? The 90.2-cell standoff at Speed 2000 is 46 ticks; the budget is 2x that,
	-- so this only fires if the missile is flying at something like aircraft speed.
	if flight > MaxFlightTicks then
		Test.Fail("the strike took " .. flight .. " ticks to cross the 90.2-cell standoff FROM WORLD"
			.. " ENTRY (budget " .. MaxFlightTicks .. ", expected ~46 at Speed 2000, and now the"
			.. " SAME wherever the player aims). Note this excludes MissileDelay,"
			.. " which is reported separately -- so a long reading here really is a slow missile."
			.. " || " .. summary)
		return
	end

	-- 6. THE SPAWN DELAY. The user's words: "the strike needs to be delayed, so that it doesnt
	-- enter the map exactly when we click". This is that gap, from the accepted order to the first
	-- tick a kinzhalmissile existed in the world. Near-zero is the shipped-before behaviour --
	-- either MissileDelay was dropped from player.yaml, or MissileStrikePower stopped routing a
	-- non-zero delay through SpawnActorEffect and took the bare `w.Add(missile)` branch instead.
	if spawnDelay < MinSpawnDelay or spawnDelay > MaxSpawnDelay then
		Test.Fail("the Kinzhal entered the map " .. spawnDelay .. " ticks after the order (band "
			.. MinSpawnDelay .. ".." .. MaxSpawnDelay .. ", shipped MissileDelay "
			.. ExpectedSpawnDelay .. " = 9.0 s at Timestep 60). || " .. summary)
		return
	end

	Test.Pass("kinzhal waited " .. spawnDelay .. "t, then flew in off-map on Russia's own bearing"
		.. " in " .. flight .. "t and killed its target. || " .. summary)
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
