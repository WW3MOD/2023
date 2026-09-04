-- ASSERTING AUTOTEST — with the lobby gate ON, does the tactical nuclear strike deliver?
--
-- WHAT IS UNDER TEST, and what is not. The delivery trait (MissileStrikePower) is unchanged since
-- test-missile-strike-power passed at 894e8dc4, and the Atomic warhead is reused unchanged from
-- mslo's NukePower. What is new and unobserved is the WIRING: a lobby checkbox, a granted
-- condition, a RequiresCondition on the power, and a third BallisticMissile actor
-- (tacnukemissile) carrying Atomic on a plain Explodes.
--
-- SO THE FIRST ASSERTION IS THE BIN, NOT THE KILL. Test.GetSupportPowerState reads the same
-- predicate SupportPowersWidget filters its icon list on (SupportPowersWidget.cs:136), which is the
-- only way to tell a power the host ENABLED from one that is merely still charging — a disabled
-- power is still a key in SupportPowerManager.Powers, so ActivateSupportPower reports both as
-- 'not-ready'. Its sibling test-tacnuke-lobby-gated-off asserts the same reading comes back
-- 'hidden' when the option is left at its shipped default of OFF.
--
-- The binding returns ONE BARE TOKEN; Test.GetSupportPowerBin gives the key list separately. An
-- earlier version appended " (bin: ...)" to the state, which made every exact comparison against it
-- unsatisfiable — the assertion below was one of four sites carrying that bug and would have failed
-- this scenario without the shipped behaviour being wrong at all.
--
-- WHY A LIVE ICON IS NOT ENOUGH ON ITS OWN: `RequiresCondition: !tacnuke-disabled` could be
-- satisfied while the missile actor, its sequence or its Explodes payload are broken, and the run
-- would still show a cameo. So the kill is asserted too, and the entry cell with it — a nuke that
-- detonates on the target while having been spawned there is the SpawnActorPower shape, not an
-- off-map strike.
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * The Atomic warhead's shape — its 30-cell blast, its fire stacks, its EMP, its suppression
--     bands. None of that is new work, all of it belongs to mslo's power, and a t90 at the aim
--     point is vaporised by the innermost warhead alone (ThermalVaporize, 200000 at Spread 3c0)
--     long before any of it matters.
--   * The 11250-tick ChargeInterval. rules.yaml overrides it; the shipped value is a placeholder
--     encoding the approved 15000-credit price.
--   * The beacon, minimap ping and 20-cell reveal camera — client-side render state with no
--     observable the Lua API can read.
--
-- EXPECTED GEOMETRY, derived in map.yaml: Russia home 6,17 -> edge cell 1,17; victim at 40,17;
-- 39 cells apart; 44 flight ticks at Speed 900 (39936 wdist / 900), 2.7 s at Timestep 60.

local OrderKey = "TacNukeStrike"
local TargetX, TargetY = 40, 17
local MissileType = "tacnukemissile"

-- Tolerances, all loose: this asserts a CLASS of behaviour (the host turned it on, it came in from
-- my own edge, it went off where it was aimed), not tuned numbers.
local MaxEntryToHome = 15    -- cells. Expected 5. Enemy edge would be 51, map corner 17.
local MinEntryToTarget = 25  -- cells. Expected 39. Spawned-on-target would be 0.
local MaxFlightTicks = 150   -- ticks from the order to the kill. Expected ~46.
local ObserveTicks = 400     -- whole-run budget.

local tick = 0
local Russia
local stateAtStart = "never-read"
local binAtStart = "never-read"
local orderStatus = "never-called"
local orderTick = nil
local firstSeenTick = nil
local firstCell = nil
local impactTick = nil
local victimStartHealth = 0
local finished = false

-- "Would the bin draw this?" spelled once. Test.GetSupportPowerState returns a bare token; the two
-- drawn states are `ready` and `charging:<n>`, the second carrying a value so it is matched by
-- prefix. Every other token ('hidden', 'absent', 'no-manager') means no icon.
local function isDrawn(state)
	return state == "ready" or string.sub(state, 1, 9) == "charging:"
end

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

	if orderTick == nil then
		-- Read the bin BEFORE firing, and keep the last reading: if the order never issues, the
		-- verdict then says whether the icon was there at all, which separates "the gate is wrong"
		-- from "the power is wired wrong".
		stateAtStart = Test.GetSupportPowerState(Russia, OrderKey)
		binAtStart = Test.GetSupportPowerBin(Russia)

		orderStatus = Test.ActivateSupportPower(Russia, OrderKey, CPos.New(TargetX, TargetY))
		if orderStatus == "issued" then
			orderTick = tick
		end

		return
	end

	local missiles = Russia.GetActorsByType(MissileType)
	if #missiles > 0 and firstCell == nil then
		local c = missiles[1].Location
		firstCell = { X = c.X, Y = c.Y }
		firstSeenTick = tick
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
	local flight = (orderTick ~= nil and impactTick ~= nil) and (impactTick - orderTick) or -1
	local victimState = Victim.IsDead and "DEAD" or (Victim.Health .. "hp")

	local summary = "lobby=ON(locked) state=" .. stateAtStart .. " bin=[" .. binAtStart .. "]"
		.. " | order=" .. orderStatus .. "@t" .. n(orderTick)
		.. " | entry=" .. entryX .. "," .. entryY .. "@t" .. n(firstSeenTick)
		.. " home=" .. home.X .. "," .. home.Y
		.. " target=" .. TargetX .. "," .. TargetY
		.. " entry->home=" .. toHome .. "c entry->target=" .. toTarget .. "c"
		.. " | impact@t" .. n(impactTick) .. " flight=" .. flight .. "t"
		.. " | victim t90 " .. victimStartHealth .. "hp -> " .. victimState
		.. " | SRs own=" .. (OwnSR.IsDead and "DEAD" or (OwnSR.Health .. "hp"))
		.. " opp=" .. (OpponentSR.IsDead and "DEAD" or (OpponentSR.Health .. "hp"))
		.. " | observed=" .. tick .. "t"

	-- 1. Did the lobby gate open? This is the assertion the scenario exists for; everything below
	-- it is the delivery check that makes an open gate mean something.
	-- Accepts BOTH drawn tokens. `ready` is expected here (rules.yaml sets ChargeInterval 1 and
	-- StartFullyCharged), but `charging:<n>` is equally a live icon and is what the very first tick
	-- can report: SupportPowerInstance.Ready is `Active && RemainingTicks == 0`, and Active is only
	-- assigned inside Tick(), so a read before the first tick gives charging:0 rather than ready.
	-- Matching the value-carrying token by prefix rather than spelling one instance of it is the
	-- point — an earlier version compared against the literal "charging:0" and would have missed
	-- charging:1.
	if not isDrawn(stateAtStart) then
		Test.Fail("the host enabled the tactical nuclear strike and the power bin still would not"
			.. " draw it: state '" .. stateAtStart .. "'. 'hidden' means the"
			.. " GrantConditionOnLobbyOption@tacnuke -> RequiresCondition chain did not open —"
			.. " check the option id 'tactical-nuke' matches on both sides, and that"
			.. " GrantWhenOptionDisabled is still true (the polarity is deliberate; see player.yaml)."
			.. " 'absent' means the trait is not on the Player actor at all. || " .. summary)
		return
	end

	if orderStatus ~= "issued" then
		Test.Fail("the icon was live but the order was refused: " .. orderStatus .. ". || " .. summary)
		return
	end

	-- 2. Did a missile actor reach the world? An empty result after a successful order is the
	-- Target-handshake exception: BallisticMissileFly reads Target.CenterPosition from
	-- AddedToWorld, so an unset Target throws there (MissileSpawnerMaster.cs:85-87).
	if firstCell == nil then
		Test.Fail("the order was accepted but no " .. MissileType .. " ever entered the world."
			.. " || " .. summary)
		return
	end

	-- 3. Did it fly in from the edge nearest ITS OWN base? Checked before the kill: a warhead this
	-- large kills its target just as dead from a spawn placed on top of it, so damage alone cannot
	-- tell an off-map strike from the SpawnActorPower shape.
	if toTarget < MinEntryToTarget then
		Test.Fail("the missile did not fly in from anywhere: it first appeared " .. toTarget
			.. " cells from the target (needs >= " .. MinEntryToTarget .. "). || " .. summary)
		return
	end

	if toHome > MaxEntryToHome then
		Test.Fail("the missile entered " .. toHome .. " cells from Russia's own base (allowance "
			.. MaxEntryToHome .. "). ChooseClosestEdgeCell(home) is cell 1,17 for this map. || " .. summary)
		return
	end

	-- 4. Did it go off? Atomic's innermost warhead alone is 200000 at Penetration 5000 against a
	-- 24000hp t90, so arrival means death: a live tank here means the flight did not complete or
	-- Explodes did not fire on the actor.
	if impactTick == nil then
		Test.Fail("the missile entered correctly but the t90 is still alive after " .. tick
			.. " ticks. BallisticMissileFly ends with SetPosition(targetPos) then Kill, and Atomic's"
			.. " ThermalVaporize puts 200000 on a 24000hp t90 — so arrival means death. Either the"
			.. " flight did not complete, or Explodes did not fire. || " .. summary)
		return
	end

	if flight > MaxFlightTicks then
		Test.Fail("the strike took " .. flight .. " ticks to cross 39 cells (budget "
			.. MaxFlightTicks .. ", expected ~46 at Speed 900). || " .. summary)
		return
	end

	-- 5. The Supply Routes must be untouched, and NOT because they are far away — both are inside
	-- the 30-cell blast. They survive because SUPPLYROUTE's only target type is NoAutoTarget, which
	-- no Atomic warhead lists. This is the assertion behind proposal §5a's claim that no missile
	-- power can be a win button, and it is the one thing here that would be a balance emergency.
	if OwnSR.IsDead or OpponentSR.IsDead then
		Test.Fail("a Supply Route was destroyed by the tactical nuke. §5a's guarantee that no"
			.. " missile power can delete a player's production rests on SUPPLYROUTE carrying"
			.. " `Targetable.TargetTypes: NoAutoTarget` and no warhead listing it — if that has"
			.. " changed, every missile power in the mod is now a win button. || " .. summary)
		return
	end

	Test.Pass("tactical nuke: lobby gate open, delivered from the map edge, target vaporised,"
		.. " Supply Routes intact. || " .. summary)
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
