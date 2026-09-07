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
-- WHAT WAS ADDED ON 2026-09-05, and why it belongs here rather than in a new scenario. Three
-- changes landed on how these strikes ARRIVE, and this run already fires the one power all three
-- touch, so it reads all three off the same launch:
--
--   * THE AIRBURST. The nuke now detonates 6c256 above the aim point, the same height mslo's
--     NukePower has always used, so the two deliveries of the identical `Atomic` warhead finally
--     agree. Asserted as a NUMBER, not a screenshot: the missile's CenterPosition.Z is sampled
--     every tick, and the last live reading is the detonation point because BallisticMissileFly
--     does SetPosition(targetPos) and only then QUEUES the kill (BallisticMissileFly.cs:216-221) --
--     so the actor sits at exactly the burst position for one full tick before it dies. That Z is
--     printed against the ground Z read off the victim, which stands at the aim point.
--
--   * THE SPAWN DELAY. MissileDelay 500 = 30.0 s at Timestep 60, so the missile no longer enters
--     the map on the tick the order is issued. Order tick and world-entry tick are both printed
--     and their difference is asserted.
--
--   * THE TIMER LIST. Test.GetSupportPowerTimerLines reports exactly what the top-of-screen
--     SupportPowerTimerWidget would draw. The nuke must still be in it for BOTH players -- it is
--     the one power keeping `DisplayTimerRelationships: Ally, Neutral, Enemy` -- while the Kinzhal
--     and the GBU-57, now at None, must be gone from it entirely.
--
-- EXPECTED GEOMETRY, REWRITTEN ON 2026-09-07. This scenario used to assert that the warhead
-- entered within 15 cells of Russia's own base, at `ChooseClosestEdgeCell(home)` = cell 1,17. That
-- rule is gone (engine b69681d2) and the assertion FAILED ON CORRECT BEHAVIOUR: a live run
-- reported entry -49,17, fully 55 cells from home, with the tank dead and both Supply Routes
-- intact. The spawn is now deliberately far off-map.
--
-- The shipped rule is `entry = aimPoint - unit(home -> centroid) * standoff`, and for this map:
--   standoff = mapDiagonal + ApproachMargin = sqrt(66^2 + 34^2) + 16 = 90.2 cells
--   axis     = |home 6,17 -> victim 40,17| = 34 cells
--   entry    = 40 - 90.2 = cell -50,17, which is 56.2 cells BEHIND home ON the home->target ray
-- The observed -49,17 is that spawn plus one tick of flight, and 55 against an expected 56.2 is
-- the whole discrepancy. The contract is asserted through TestHarness.ApproachFault; the reasoning
-- for all three of its readings lives in one place, in test-helpers.lua.
--
-- NOTE that home, victim and entry are all on row 17, so on THIS map the bearing reading cannot
-- distinguish the shipped rule from any other eastward one -- what fails the old edge-cell rule
-- here is the DISTANCE reading, by 51 cells. test-missile-strike-power carries the sharp version
-- of the bearing test; its victim was moved off Russia's row for exactly that reason.
--
-- FLIGHT. The missile now covers the whole standoff rather than the on-map gap, so the flight is
-- ~100 ticks (92408 wdist / Speed 900) where it used to be ~44, and it is the SAME length wherever
-- the player aims -- the deliberate balance change in b69681d2, which stopped a strike beside your
-- own base from giving the enemy the least warning. The live run measured 96. The flight is still
-- unchanged by the airburst: EstimateArcTicks is driven by HORIZONTAL distance
-- (BallisticMissileFly.cs:51), which raising the target Z does not move.

local OrderKey = "TacNukeStrike"
local TargetX, TargetY = 40, 17
local MissileType = "tacnukemissile"

-- THE SHOT IS BOUGHT, NOT CHARGED, and until 2026-09-07 this scenario could not fire at all.
-- MissileStrikePower@TacNuke carries `RequiresPurchase: True`, which replaces the charge timer with
-- a magazine: at zero banked shots SupportPowerInstance.Disabled is true, Active is false, Ready is
-- false, and Test.ActivateSupportPower returns 'not-ready:0' for the whole budget. The
-- `ChargeInterval: 1` + `StartFullyCharged` this scenario's rules.yaml used to set were read and
-- discarded by the instance constructor (SupportPowerManager.cs:228-229). GrantCharge has exactly
-- one caller in the engine -- SupportPowerProductionQueue.BuildUnit -- so buying is the only route.
--
-- BUYING IS ALSO THE RIGHT SHAPE FOR THIS TEST rather than a concession to it. Everything below
-- asserts that a nuke a HOST ENABLED behaves correctly; under the shipped rules a player reaches
-- that nuke by paying for it, so a run that skipped the shop would be asserting about a state
-- nobody can occupy. rules.yaml supplies the cash, the sandbox lobby option for the power's
-- `powers.event` tier, and a 5-tick BuildDuration on the proxy.
local BuyProxy = "power.tacnuke"

-- Tolerances, all loose: this asserts a CLASS of behaviour (the host turned it on, it came in over
-- my own shoulder, it went off where it was aimed), not tuned numbers.
--
-- THE APPROACH TOLERANCES ARE NOT AN ALLOWANCE ON A DISTANCE. They bound the error on a DERIVED
-- position -- see TestHarness.ApproachFault -- so widening them does not weaken the direction or
-- the side, only the precision of the standoff. The map size is passed because the standoff is
-- the map DIAGONAL plus ApproachMargin, and it is MapSize from map.yaml (66,34), not Bounds.
local MapCellsX, MapCellsY = 66, 34
local MaxApproachLateral = 4  -- cells off the home->target axis. Exact answer 0; observed 0.0.
local MaxApproachLag = 8      -- cells of flight before the poller first saw it. Observed ~1.2.
-- MEASURED FROM WORLD ENTRY, NOT FROM THE ORDER, and that is a sharpening rather than a loosening.
-- MissileDelay 500 shipped on 2026-09-05, so order-to-kill is now dominated by the wait and would
-- say nothing about Speed. Entry-to-kill is the flight and nothing else. Expected ~100 at Speed
-- 900 -- the missile now covers the whole 90.2-cell standoff, not the 39-cell on-map gap, and the
-- live run of 2026-09-07 measured 96. The 150 budget already covered that and is left alone.
-- over 39 cells; the delay is asserted separately below.
local MaxFlightTicks = 150
local ObserveTicks = 900     -- whole-run budget; raised from 400 to cover the 500-tick wait.

-- The shipped MissileDelay for this power (player.yaml). SpawnActorEffect counts the delay down
-- one per tick and adds on the tick the counter goes negative (SpawnActorEffect.cs:44-49), and the
-- effect itself is installed by a frame-end task, so the observed gap runs a couple of ticks over
-- the nominal value. The band is one-sided-tight for that reason: far more slack above than below.
local ExpectedSpawnDelay = 500
local MinSpawnDelay = ExpectedSpawnDelay - 5
local MaxSpawnDelay = ExpectedSpawnDelay + 40

-- The shipped DetonationAltitude, 6c256 = 6400. The band is wide because this asserts a CLASS --
-- "it burst well above the deck" -- rather than a tuned number. The floor matters most: a ground
-- burst reads a few hundred at worst, since the missile covers ~900-1100 wdist per tick and the
-- last sample before death would still be near zero.
local ExpectedBurstAltitude = 6400
local MinBurstAltitude = 4000
local MaxBurstAltitude = 9000

local tick = 0
local Russia
local USA
local stateAtStart = "never-read"
local binAtStart = "never-read"
local orderStatus = "never-called"
local orderTick = nil
local firstSeenTick = nil
local firstCell = nil
local impactTick = nil
local victimStartHealth = 0
local groundZ = 0
local burstZ = nil        -- CenterPosition.Z on the LAST tick the missile was seen alive.
local minAltitude = nil   -- lowest height above groundZ ever sampled, as a cross-check on burstZ.
local timerLinesOwn = "never-read"
local timerLinesEnemy = "never-read"
local buyStatus = "never-called"
local buyTick = nil
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

	-- Fill the magazine. Stateless and idempotent, so it is simply called every tick until it
	-- reports ready; the order attempt below already retries on its own schedule and picks the shot
	-- up the moment it lands (about t=12 at BuildDuration 5). The status token is carried into the
	-- verdict because 'refused' and 'not-ready:0' are the same silence from the order's point of
	-- view and completely different faults.
	if buyTick == nil then
		local ready, status = TestHarness.EnsurePower(Russia, BuyProxy, OrderKey, tick)
		buyStatus = status
		if ready then
			buyTick = tick
		end
	end

	if orderTick == nil then
		-- Read the bin BEFORE firing, and keep the last reading: if the order never issues, the
		-- verdict then says whether the icon was there at all, which separates "the gate is wrong"
		-- from "the power is wired wrong".
		stateAtStart = Test.GetSupportPowerState(Russia, OrderKey)
		binAtStart = Test.GetSupportPowerBin(Russia)

		-- Refreshed alongside them, and for the same reason: SupportPowerInstance.Disabled is only
		-- assigned inside Tick(), so a read taken before the world has ticked can report a power as
		-- live that is about to be gated off. Taking the last pre-order reading settles that.
		timerLinesOwn = Test.GetSupportPowerTimerLines(Russia)
		if USA ~= nil then
			timerLinesEnemy = Test.GetSupportPowerTimerLines(USA)
		end

		orderStatus = Test.ActivateSupportPower(Russia, OrderKey, CPos.New(TargetX, TargetY))
		if orderStatus == "issued" then
			orderTick = tick
		end

		return
	end

	local missiles = Russia.GetActorsByType(MissileType)
	if #missiles > 0 then
		if firstCell == nil then
			local c = missiles[1].Location
			firstCell = { X = c.X, Y = c.Y }
			firstSeenTick = tick
		end

		-- Sampled every tick rather than once at the end, because the actor is REMOVED on
		-- detonation: there is nothing left to ask afterwards. The final sample is the burst
		-- position for the SetPosition-then-queue-kill reason in the header.
		burstZ = missiles[1].CenterPosition.Z
		local altitude = burstZ - groundZ
		if minAltitude == nil or altitude < minAltitude then
			minAltitude = altitude
		end
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

	-- THE APPROACH CONTRACT. toHome and toTarget stay in the summary as raw diagnostics -- they are
	-- what a reader eyeballs against the entry cell -- but neither is asserted any more. toHome in
	-- particular was the assertion this scenario got wrong: it read 55 on a correct strike.
	local approach = nil
	local approachLine = "approach=no entry"
	if firstCell ~= nil then
		approach = TestHarness.MeasureApproach(entryX, entryY, home.X, home.Y,
			TargetX, TargetY, MapCellsX, MapCellsY)
		approachLine = TestHarness.ApproachSummary(approach)
	end
	local orderToImpact = (orderTick ~= nil and impactTick ~= nil) and (impactTick - orderTick) or -1
	local flight = (firstSeenTick ~= nil and impactTick ~= nil) and (impactTick - firstSeenTick) or -1
	local spawnDelay = (orderTick ~= nil and firstSeenTick ~= nil) and (firstSeenTick - orderTick) or -1
	local burstAltitude = burstZ ~= nil and (burstZ - groundZ) or -1
	local victimState = Victim.IsDead and "DEAD" or (Victim.Health .. "hp")

	local summary = "lobby=ON(locked)+sandbox magazine=" .. buyStatus .. "@t" .. n(buyTick)
		.. " state=" .. stateAtStart .. " bin=[" .. binAtStart .. "]"
		.. " | order=" .. orderStatus .. "@t" .. n(orderTick)
		.. " | entry=" .. entryX .. "," .. entryY .. "@t" .. n(firstSeenTick)
		.. " home=" .. home.X .. "," .. home.Y
		.. " target=" .. TargetX .. "," .. TargetY
		.. " entry->home=" .. toHome .. "c entry->target=" .. toTarget .. "c"
		.. " | " .. approachLine
		.. " | spawn delay=" .. spawnDelay .. "t (shipped " .. ExpectedSpawnDelay .. ")"
		.. " | impact@t" .. n(impactTick) .. " flight=" .. flight .. "t"
		.. " order->impact=" .. orderToImpact .. "t"
		.. " | burstZ=" .. n(burstZ) .. " groundZ=" .. groundZ
		.. " burst altitude=" .. burstAltitude .. " (shipped " .. ExpectedBurstAltitude .. ")"
		.. " minAlt=" .. n(minAltitude)
		.. " | timers own=[" .. timerLinesOwn .. "] enemy=[" .. timerLinesEnemy .. "]"
		.. " | victim t90 " .. victimStartHealth .. "hp -> " .. victimState
		.. " | SRs own=" .. (OwnSR.IsDead and "DEAD" or (OwnSR.Health .. "hp"))
		.. " opp=" .. (OpponentSR.IsDead and "DEAD" or (OpponentSR.Health .. "hp"))
		.. " | observed=" .. tick .. "t"

	-- 1. Did the lobby gate open AND did the purchase land? This is the assertion the scenario
	-- exists for; everything below it is the delivery check that makes an open gate mean something.
	--
	-- `stateAtStart` is the LAST reading taken before the order issued, so under the purchase model
	-- it is 'ready' — the shot is in the magazine. `charging:<n>` is still accepted because it is
	-- equally a live icon and would be the reading if this power were ever put back on a timer;
	-- matching that value-carrying token by prefix rather than spelling one instance of it is the
	-- point, since an earlier version compared against the literal "charging:0" and would have
	-- missed charging:1.
	--
	-- 'hidden' NOW HAS TWO CAUSES AND THE MAGAZINE READING IN THE SUMMARY SAYS WHICH. An unbought
	-- power is hidden exactly as a lobby-gated one is: SupportPowerInstance.Disabled is
	-- `!bank.IconVisible(Permitted)`, and an empty bank fails it just as an unmet condition does.
	if not isDrawn(stateAtStart) then
		Test.Fail("the tactical nuclear strike was not in the power bin when the order went out:"
			.. " state '" .. stateAtStart .. "'. READ THE MAGAZINE FIELD IN THE SUMMARY FIRST."
			.. " If it is not 'ready', the shot was never bought and the fault is in the shop --"
			.. " 'refused' means the Powers queue rejected the order (check"
			.. " PowersSandboxCheckboxEnabled, which provides this power's powers.event tier, and"
			.. " DefaultCash against the 15000 price), 'loading' means the purchase never"
			.. " completed. If the magazine IS ready and the state is still 'hidden', the lobby"
			.. " gate closed: check that the option id 'tactical-nuke' matches on both sides and"
			.. " that GrantWhenOptionDisabled is still true (the polarity is deliberate; see"
			.. " player.yaml). 'absent' means the trait is not on the Player actor at all."
			.. " || " .. summary)
		return
	end

	if orderStatus ~= "issued" then
		Test.Fail("the icon was live but the order was refused: " .. orderStatus .. ". If the"
			.. " magazine reading above is not 'ready' the shot was never bought, and the buy is"
			.. " what to fix: 'refused' means the Powers queue would not take the order (check"
			.. " PowersSandboxCheckboxEnabled and DefaultCash in rules.yaml), 'absent' means the"
			.. " OrderName is wrong. || " .. summary)
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

	-- 3. Did it fly the shipped approach -- in over Russia's OWN shoulder, from off-map, on the
	-- bearing Russia's own position gives? Checked before the kill: a warhead this large kills its
	-- target just as dead from a spawn placed on top of it, so damage alone cannot tell an off-map
	-- strike from the SpawnActorPower shape. A spawn-on-target reads as `along` positive here and
	-- is caught by the wrong-side branch, so the old `toTarget >= 25` guard is subsumed rather
	-- than dropped.
	local approachFault = TestHarness.ApproachFault(approach, MaxApproachLateral, MaxApproachLag)
	if approachFault ~= nil then
		Test.Fail("the tactical nuke did not fly the shipped approach. " .. approachFault
			.. ". || " .. summary)
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
		Test.Fail("the strike took " .. flight .. " ticks to cross the 90.2-cell standoff FROM WORLD"
			.. " ENTRY (budget " .. MaxFlightTicks .. ", expected ~100 at Speed 900, measured 96)."
			.. " This excludes MissileDelay, which is"
			.. " reported separately, so a long reading here really is a slow missile. || " .. summary)
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

	-- 6. THE SPAWN DELAY. The user's words were "the strike needs to be delayed, so that it doesnt
	-- enter the map exactly when we click". `spawnDelay` is exactly that gap, in ticks, from the
	-- accepted order to the first tick a tacnukemissile existed in the world. A reading near 0 is
	-- the shipped-before behaviour: either MissileDelay was dropped from player.yaml, or
	-- MissileStrikePower stopped routing a non-zero delay through SpawnActorEffect and went back to
	-- the bare `w.Add(missile)` branch.
	if spawnDelay < MinSpawnDelay or spawnDelay > MaxSpawnDelay then
		Test.Fail("the nuke entered the map " .. spawnDelay .. " ticks after the order (band "
			.. MinSpawnDelay .. ".." .. MaxSpawnDelay .. ", shipped MissileDelay "
			.. ExpectedSpawnDelay .. " = 30.0 s at Timestep 60). Near-zero means the delay is not"
			.. " being applied at all; far over means something other than SpawnActorEffect is"
			.. " holding the actor back. || " .. summary)
		return
	end

	-- 7. THE AIRBURST, as a number rather than a look. burstZ is the missile's own
	-- CenterPosition.Z on the last tick it was alive, groundZ is the victim's Z at the aim point,
	-- and the difference is how far above the deck the warhead went off.
	--
	-- WHY THIS IS THE WHOLE VISUAL ASSERTION: CreateEffectWarhead spawns its sprite at the impact
	-- position and only forces it to ground level when ForceDisplayAtGroundLevel is set
	-- (CreateEffectWarhead.cs:140-150), which Atomic's Warhead@Fireball does not. So the mushroom
	-- cloud is drawn wherever this number says the warhead detonated, and a correct number here IS
	-- a correctly-placed animation -- no screenshot required.
	if burstZ == nil then
		Test.Fail("the missile was never sampled alive, so the burst altitude is unknown even"
			.. " though the target died. The per-tick poll should see the actor for the whole"
			.. " flight. || " .. summary)
		return
	end

	if burstAltitude < MinBurstAltitude then
		Test.Fail("the nuke detonated " .. burstAltitude .. " above the ground (floor "
			.. MinBurstAltitude .. ", shipped DetonationAltitude " .. ExpectedBurstAltitude
			.. " = 6c256). A near-zero reading is a GROUND burst, which is what the user reported"
			.. " as 'the nuclear explosion animation sits too low' -- check that"
			.. " MissileStrikePower@TacNuke still carries DetonationAltitude and that"
			.. " MissileStrikePower.Activate still adds it to the missile's Target rather than"
			.. " aiming at the bare aim point. || " .. summary)
		return
	end

	if burstAltitude > MaxBurstAltitude then
		Test.Fail("the nuke detonated " .. burstAltitude .. " above the ground (ceiling "
			.. MaxBurstAltitude .. ", shipped " .. ExpectedBurstAltitude .. "). THIS IS THE"
			.. " DANGEROUS DIRECTION: every Atomic warhead carries AirThreshold: 10c0 = 10240, and"
			.. " above that the engine substitutes `Air` for the terrain target types"
			.. " (Warhead.cs:41-45) and the nuke silently stops affecting the ground -- it would"
			.. " still fly, still be announced, and do nothing. || " .. summary)
		return
	end

	-- 8. THE TIMER LIST. The user: "We dont need to see a countdown of ally players powers, it is
	-- just distracting. Maybe we can keep it for nukes, but not for other powers." Both readings
	-- are taken because the nuke is the one power whose DisplayTimerRelationships still includes
	-- Enemy, so the enemy view is a real assertion and not a duplicate of the owner's.
	if string.find(timerLinesOwn, "Kinzhal", 1, true) ~= nil
		or string.find(timerLinesEnemy, "Kinzhal", 1, true) ~= nil
		or string.find(timerLinesOwn, "GBU-57", 1, true) ~= nil
		or string.find(timerLinesEnemy, "GBU-57", 1, true) ~= nil then
		Test.Fail("a conventional strike is still drawing a countdown in the top-of-screen timer"
			.. " list. Both must carry `DisplayTimerRelationships: None`, which drops them in"
			.. " SupportPowerTimerWidget.Candidates before the per-viewer test runs. || " .. summary)
		return
	end

	if string.find(timerLinesOwn, "Tactical Nuclear Strike", 1, true) == nil then
		Test.Fail("the tactical nuclear strike has no countdown line for its OWN player. The nuke"
			.. " is the exception the user asked to keep; if this is gone, None was applied to it"
			.. " too. || " .. summary)
		return
	end

	if string.find(timerLinesEnemy, "Tactical Nuclear Strike", 1, true) == nil then
		Test.Fail("the tactical nuclear strike has no countdown line for the ENEMY player. It keeps"
			.. " `Ally, Neutral, Enemy` precisely so a launch is public -- a deterrent nobody can"
			.. " see the clock on is not a deterrent. || " .. summary)
		return
	end

	Test.Pass("tactical nuke: lobby gate open, flown in off-map over Russia's own shoulder after a "
		.. spawnDelay .. "t wait, burst " .. burstAltitude .. " above the deck, target vaporised,"
		.. " Supply Routes intact, only the nuke on the public timer. || " .. summary)
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

	-- Read BEFORE anything is fired and before the victim can die: the t90 stands on the aim point,
	-- so its Z is the terrain Z the burst altitude is measured against.
	groundZ = Victim.CenterPosition.Z

	-- Held for the enemy-side timer reading taken in pollTick. Nil is tolerated there rather than
	-- failed on, so a map edit that renames the player degrades to a reported "never-read" instead
	-- of masking the delivery assertions behind a setup error.
	USA = Player.GetPlayer("USA")

	TestHarness.FocusBetween(OwnSR, Victim)
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
