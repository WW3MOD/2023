-- ASSERTING AUTOTEST — does MissileStrikePower deliver a missile from the map edge?
--
-- WHAT IS UNDER TEST. MissileStrikePower is the only genuinely new engine code in the
-- missile-strike-powers feature (WORKSPACE/proposals/260904-missile-powers.md §10), and all
-- four planned powers ride on it. It picks the map-edge cell nearest the owner's base
-- (Map.ChooseClosestEdgeCell, the AirstrikePower.cs:79 precedent that commit a20c8a82
-- deliberately established for this mod), creates a BallisticMissile ACTOR there at altitude,
-- hands it a Target, and lets BallisticMissile.AddedToWorld queue the flight.
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
-- Russia home 6,17 -> edge cell 1,17; victim at 48,17; 47 cells apart; 24 flight ticks at
-- Speed 2000 (48128 wdist / 2000 = 24 ticks, 1.44 s at Timestep 60).

local OrderKey = "KinzhalStrike"
local TargetX, TargetY = 48, 17
local MissileType = "kinzhalmissile"

-- Tolerances. All three are loose on purpose: this asserts a CLASS of behaviour (came in from
-- my own edge, crossed the map fast, hit what it was aimed at), not tuned numbers. The three
-- wrong answers listed in the header miss every one of them by a wide margin.
local MaxEntryToHome = 15    -- cells. Expected 5. Enemy edge would be 59, map corner 17.
local MinEntryToTarget = 30  -- cells. Expected 47. Spawned-on-target would be 0.
-- NOW MEASURED FROM WORLD ENTRY, NOT FROM THE ORDER, and that is a sharpening rather than a
-- loosening. MissileDelay 150 shipped on 2026-09-05, so order-to-kill is dominated by the wait and
-- would no longer say anything about Speed. Entry-to-kill is the flight and nothing else, so this
-- budget still means what its name says. Expected ~26 at Speed 2000 over 47 cells.
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

	-- Retry until the power reports ready rather than assuming it is armed on tick 1:
	-- SupportPowerInstance.Active is only set inside Tick(), and the TechTree pass that
	-- satisfies `Prerequisites: player.russia` runs on its own schedule. The last status
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
	local orderToImpact = (orderTick ~= nil and impactTick ~= nil) and (impactTick - orderTick) or -1
	local spawnDelay = (orderTick ~= nil and firstSeenTick ~= nil) and (firstSeenTick - orderTick) or -1
	local flight = (firstSeenTick ~= nil and impactTick ~= nil) and (impactTick - firstSeenTick) or -1
	local victimState = Victim.IsDead and "DEAD" or (Victim.Health .. "hp")

	local summary = "order=" .. orderStatus .. "@t" .. n(orderTick)
		.. " | entry=" .. entryX .. "," .. entryY .. "@t" .. n(firstSeenTick)
		.. " last=" .. (lastCell ~= nil and (lastCell.X .. "," .. lastCell.Y) or "none")
		.. " home=" .. home.X .. "," .. home.Y
		.. " target=" .. TargetX .. "," .. TargetY
		.. " entry->home=" .. toHome .. "c entry->target=" .. toTarget .. "c"
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

	-- 3. Did it come in from the edge nearest ITS OWN base? Checked before the kill, because a
	-- missile spawned on top of the target kills it just as dead while delivering none of the
	-- behaviour the feature exists for.
	if toTarget < MinEntryToTarget then
		Test.Fail("the missile did not fly in from anywhere: it first appeared " .. toTarget
			.. " cells from the target (needs >= " .. MinEntryToTarget .. "). A power that spawns"
			.. " the missile AT the target cell is the SpawnActorPower shape, not an off-map"
			.. " strike. || " .. summary)
		return
	end

	if toHome > MaxEntryToHome then
		Test.Fail("the missile entered " .. toHome .. " cells from Russia's own base (allowance "
			.. MaxEntryToHome .. "). ChooseClosestEdgeCell(home) is cell 1,17 for this map; an"
			.. " entry near 0,0 means the flight target defaulted to WPos.Zero, and an entry on"
			.. " the far side means the edge was picked from something other than the owner's"
			.. " HomeLocation. || " .. summary)
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

	-- 5. Was it hypersonic? 47 cells at Speed 2000 is 24 ticks; the budget is nearly 4x that,
	-- so this only fires if the missile is flying at something like aircraft speed.
	if flight > MaxFlightTicks then
		Test.Fail("the strike took " .. flight .. " ticks to cross 47 cells FROM WORLD ENTRY (budget "
			.. MaxFlightTicks .. ", expected ~26 at Speed 2000). Note this excludes MissileDelay,"
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

	Test.Pass("kinzhal waited " .. spawnDelay .. "t, then delivered from the map edge in "
		.. flight .. "t and killed its target. || " .. summary)
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
