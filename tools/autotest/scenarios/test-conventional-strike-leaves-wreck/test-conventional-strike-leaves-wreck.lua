-- ASSERTING AUTOTEST — does a CONVENTIONAL strike power destroy an income structure and LEAVE the
-- restorable wreck behind?
--
-- User ruling 2026-09-11: "I think it is a bad game mechanic that money structures can be destroyed.
-- Maybe nothing can fully obliterate them, so nuke also only destroys them, and can do so further
-- out than the inner fireball but not beyond 2-3x the fireball or so."
--
-- IT IS NOT A MIRROR PAIR, and this header said it was for about an hour. An earlier ruling the same
-- day drew a conventional/nuclear line -- conventional leaves a wreck, nuclear obliterates -- and the
-- sibling scenario was built to assert the nuclear half. The user then rejected that premise rather
-- than answering it: NOTHING obliterates a money structure now. So both scenarios assert a WRECK,
-- and they differ in the weapon that leaves it, not in the outcome. If you find a comment or a test
-- name anywhere claiming a conventional/nuclear partition, it is stale and predates this ruling.
--
-- WHAT THIS ONE OWNS is the CONVENTIONAL delivery path: IskanderExplosion's Warhead@TechStructure
-- must carry `DamageTypes: ExplosionDeath` and no `HeavyOrdnanceDeath`, and no tech building may
-- declare `ExcludedDeathTypes` at all -- both sides of that are now gone from the tree, so
-- SpawnActorOnDeath fires on every death whatever killed it.
--
-- WHY A SEPARATE SCENARIO RATHER THAN A FOURTH ARM ON THE SIBLING. Two powers in one run means two
-- purchases, two tiers (the tac nuke is `powers.event` and needs the sandbox checkbox; the Kinzhal
-- is `powers.russia` and needs none) and two flight budgets in one 1400-tick window — and a failure
-- would not say which half broke. Separate runs, one claim each.
--
-- THREE ARMS, FAILING IN THREE DIRECTIONS:
--
--   A. THE STRIKE ARRIVES AND KILLS. StruckDerrick must die. 60000 damage at Penetration 2500
--      against 50000 HP, so arrival means death; if it survives, the warhead did not reach it and
--      nothing downstream means anything — arm B would then "pass" by finding no husk for a
--      building that never died, which is why A is judged first.
--
--   B. AND LEAVES A WRECK. Exactly one oilb.husk must exist at the end, up from a baseline of zero.
--      This is the assertion the scenario exists for. The wreck carries InfiltrateForTransform via
--      ^TechBuildingHusk (husks.yaml:118-126), so its presence IS the "repaired with an engineer
--      and captured again by a technician" the ruling asks for.
--
--   C. THE CONTROL MUST NOT DIE. FarDerrick, 34 cells away, must finish at full health. Without it
--      a pass could come from a strike that flattened everything — and worse, a dead control would
--      contribute a husk of its own to the global count arm B reads, forging its evidence.
--
-- THE HUSK COUNT IS TAKEN GLOBALLY off Map.ActorsInWorld rather than by asking the dead actor what
-- it spawned — there is nothing left to ask. SpawnActorOnDeath spawns from INotifyRemovedFromWorld,
-- not from Killed, so the wreck appears a frame AFTER the death and the poll must keep running past
-- it. HuskSettleTicks is that grace period, and getting it wrong here produces a false FAIL (the
-- opposite of the sibling, where tightness produces a false PASS).

local OrderKey = "KinzhalStrike"
local BuyProxy = "power.kinzhal"
local TargetX, TargetY = 40, 17
local HuskType = "oilb.husk"

-- Grace after the death for SpawnActorOnDeath to run from RemovedFromWorld and for the husk to be
-- added by the frame-end task. 30 ticks is far more than the one frame it needs.
local HuskSettleTicks = 30

-- Budget: purchase ready ~t12 (BuildDuration 5 in rules.yaml), order immediately after, MissileDelay
-- 150 ticks, then ~46 ticks of flight (90-cell standoff at Speed 2000), then the settle window.
-- Impact lands near t240, so 900 is roughly four times the budget actually needed.
local ObserveTicks = 900

local tick = 0
local phase = "buying"
local Russia, USA
local struckStartHealth, farStartHealth = 0, 0
local buyStatus, buyTick = "never-called", nil
local orderStatus, orderTick = "never-called", nil
local stateAtOrder = "never-read"
local strikeDeathTick = nil
local husksAtEnd = -1
local huskCellsAtEnd = ""
local cashBefore, cashAfter = -1, -1
local finished = false

local function n(v)
	if v == nil then
		return "none"
	end

	return tostring(v)
end

-- Every oilb.husk in the world, whoever owns it. Owner is deliberately NOT filtered on:
-- SpawnActorOnDeath uses `OwnerType: InternalName`, so the wreck belongs to Neutral rather than to
-- the player who owned the derrick, and a per-player query would miss all of them.
local function husks()
	local found = {}
	Utils.Do(Map.ActorsInWorld, function(a)
		if a.Type == HuskType then
			found[#found + 1] = a
		end
	end)

	return found
end

local function huskCells()
	local parts = {}
	for _, h in ipairs(husks()) do
		parts[#parts + 1] = h.Location.X .. "," .. h.Location.Y
	end

	if #parts == 0 then
		return "none"
	end

	return table.concat(parts, " ")
end

local function summary()
	return "magazine=" .. buyStatus .. "@t" .. n(buyTick)
		.. " | order=" .. orderStatus .. "@t" .. n(orderTick) .. " state=" .. stateAtOrder
		.. " | struck " .. struckStartHealth .. "hp -> "
		.. (StruckDerrick.IsDead and ("DEAD@t" .. n(strikeDeathTick)) or (StruckDerrick.Health .. "hp"))
		.. " | far " .. farStartHealth .. "hp -> "
		.. (FarDerrick.IsDead and "DEAD" or (FarDerrick.Health .. "hp"))
		.. " | husks at end=" .. husksAtEnd .. " at [" .. huskCellsAtEnd .. "]"
		.. " | Russia cash " .. cashBefore .. " -> " .. cashAfter
		.. " | SRs own=" .. (OwnSR.IsDead and "DEAD" or (OwnSR.Health .. "hp"))
		.. " opp=" .. (OpponentSR.IsDead and "DEAD" or (OpponentSR.Health .. "hp"))
		.. " | observed=" .. tick .. "t"
end

local function finish()
	husksAtEnd = #husks()
	huskCellsAtEnd = huskCells()
	cashAfter = Russia.Cash
	local s = summary()

	-- ARM C first: a dead control forges the evidence arm B reads, so it is checked before B is
	-- believed.
	if FarDerrick.IsDead then
		Test.Fail("the control derrick 34 cells from the aim point was DESTROYED. IskanderExplosion's"
			.. " Warhead@TechStructure is Spread 1c0 with Falloff 100,100,0 -- nothing at all past two"
			.. " cells -- so this means a warhead on this weapon has gained a much wider Spread or a"
			.. " wider ValidTargets. Every husk count below is unreliable while this is true, because a"
			.. " dead control spawns a wreck of its own. || " .. s)
		return
	end

	if FarDerrick.Health < farStartHealth then
		Test.Fail("the control derrick took damage at 34 cells: " .. farStartHealth .. " -> "
			.. FarDerrick.Health .. "hp. The conventional strike is meant to be a point weapon; check"
			.. " Spread and Falloff on IskanderExplosion's warheads. || " .. s)
		return
	end

	-- ARM A: did the strike arrive at all?
	if orderStatus ~= "issued" then
		Test.Fail("the Kinzhal order was refused: " .. orderStatus .. " (bin state at order: "
			.. stateAtOrder .. "). READ THE MAGAZINE FIELD FIRST: if it is not 'ready' the shot was"
			.. " never bought and the fault is in the shop -- check Russia's purse against the 5000"
			.. " price (power.kinzhal, rules/powers.yaml). If the magazine IS ready, the fault is the"
			.. " tier: `powers.russia` comes from ProvidesPrerequisite@PowersRussia gated on faction"
			.. " russia (player.yaml:160-162), and this map locks Russia to that faction, so 'refused'"
			.. " means that wiring moved. This power has NO lobby gate to check. || " .. s)
		return
	end

	if not StruckDerrick.IsDead then
		Test.Fail("the Kinzhal landed on the derrick and it is still standing at "
			.. StruckDerrick.Health .. "hp of " .. struckStartHealth .. ". IskanderExplosion's"
			.. " Warhead@TechStructure is 60000 against a 50000hp oilb, so arrival means death: either"
			.. " that warhead is missing from weapons-heavy-ordnance.yaml, or that file is no longer"
			.. " LAST in mod.yaml's Weapons list and its merge is being overwritten, or ^TechBuilding"
			.. " no longer advertises `TechStructure`. || " .. s)
		return
	end

	-- ARM B, and the assertion this scenario exists for.
	if husksAtEnd < 1 then
		Test.Fail("the conventional strike destroyed the derrick and left NO wreck. That is the"
			.. " NUCLEAR behaviour, on a conventional weapon. User ruling 2026-09-11: only nukes may"
			.. " obliterate an income structure; a conventional strike must destroy it the way engineer"
			.. " C4 does, leaving the husk an engineer restores and a technician then captures. Check"
			.. " that IskanderExplosion's Warhead@TechStructure has NOT regained `HeavyOrdnanceDeath`"
			.. " in its DamageTypes (weapons-heavy-ordnance.yaml), and that the derrick still carries"
			.. " SpawnActorOnDeath at all (structures-neutral.yaml:62-65) -- deleting that trait"
			.. " outright breaks repair-by-engineer as a mechanic and fails this arm the same way."
			.. " || " .. s)
		return
	end

	if husksAtEnd > 1 then
		Test.Fail("expected exactly one " .. HuskType .. " and found " .. husksAtEnd .. " at ["
			.. huskCellsAtEnd .. "]. Only StruckDerrick was meant to die, so a second wreck means the"
			.. " control died too and arm C did not catch it. || " .. s)
		return
	end

	-- Re-read here for the same reason the sibling re-reads it: this work edited a warhead on this
	-- weapon, and a widened ValidTargets is exactly how the Supply Route guarantee would be lost.
	if OwnSR.IsDead or OpponentSR.IsDead then
		Test.Fail("a Supply Route was destroyed by the Kinzhal. SUPPLYROUTE's only target type is"
			.. " NoAutoTarget and no IskanderExplosion warhead may list it -- if Warhead@TechStructure"
			.. " has picked up a wider ValidTargets, every missile power in the mod is now a win"
			.. " button. || " .. s)
		return
	end

	Test.Pass("a conventional strike destroyed the derrick and LEFT the restorable wreck; the control"
		.. " 34 cells away finished untouched; Supply Routes intact. || " .. s)
end

local function step()
	tick = tick + 1

	-- Fill the magazine. Stateless and idempotent, so it is simply called until it reports ready.
	if buyTick == nil then
		local ready, status = TestHarness.EnsurePower(Russia, BuyProxy, OrderKey, tick)
		buyStatus = status
		if ready then
			buyTick = tick
		end

		return
	end

	if phase == "buying" then
		cashBefore = Russia.Cash
		stateAtOrder = Test.GetSupportPowerState(Russia, OrderKey)
		orderStatus = Test.ActivateSupportPower(Russia, OrderKey, CPos.New(TargetX, TargetY))
		if orderStatus == "issued" then
			orderTick = tick
		end

		phase = "strike"

		return
	end

	if phase == "strike" then
		if orderStatus ~= "issued" then
			finished = true
			finish()
			return
		end

		if strikeDeathTick == nil and StruckDerrick.IsDead then
			strikeDeathTick = tick
		end

		-- Deliberately keeps polling past the death for HuskSettleTicks: the wreck arrives a frame
		-- AFTER the kill (SpawnActorOnDeath runs from RemovedFromWorld), so a run that stopped on
		-- IsDead would report "no husk" for every possible implementation and FAIL regardless.
		if strikeDeathTick ~= nil and tick - strikeDeathTick >= HuskSettleTicks then
			finished = true
			finish()
		end

		return
	end
end

local function loop()
	if not finished then
		step()
	end

	if not finished and tick >= ObserveTicks then
		finished = true
		finish()
		return
	end

	if not finished then
		Trigger.AfterDelay(1, loop)
	end
end

WorldLoaded = function()
	Russia = Player.GetPlayer("Russia")
	USA = Player.GetPlayer("USA")
	if Russia == nil or USA == nil then
		Test.Fail("Russia or USA player not found")
		return
	end

	if StruckDerrick == nil or FarDerrick == nil or OwnSR == nil or OpponentSR == nil then
		Test.Fail("a named actor is missing from the map: StruckDerrick/FarDerrick/OwnSR/OpponentSR")
		return
	end

	struckStartHealth = StruckDerrick.Health
	farStartHealth = FarDerrick.Health

	-- Asserted at setup rather than left to be discovered in a confusing verdict: the whole scenario
	-- reads husk COUNTS, so a map that started with one would silently shift every comparison -- and
	-- here it would manufacture a PASS, since this run succeeds by finding a husk.
	if #husks() > 0 then
		Test.Fail("the map already contains an " .. HuskType .. " before anything has died; every"
			.. " husk-count assertion in this scenario is measured against a baseline of zero.")
		return
	end

	TestHarness.FocusBetween(StruckDerrick, OpponentSR)
	TestHarness.Select(StruckDerrick)

	Trigger.AfterDelay(1, loop)
end
