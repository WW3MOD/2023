-- ASSERTING AUTOTEST — can a heavy strike take an income structure off the map for good, while an
-- ordinary weapon still cannot touch one?
--
-- User request 2026-09-06: "I want to make money structures destroyable by powerful weapons, like
-- iskanders, strike powers and by nukes of course", amended the same day with the half that turned
-- out to be the real defect: "the destruction of money structures are not fully destroyed, they
-- leave the repairable wreck behind that can be repaired by engineer."
--
-- WHAT WAS ACTUALLY WRONG, because it changes what has to be proved. The tech buildings were never
-- invulnerable in the DamageMultiplier sense — they were UNTARGETABLE. ^TechBuilding overrode its
-- inherited Targetable to `NoAutoTarget, C4, DetonateAttack`, dropping `Ground` and `Structure`, and
-- no weapon in the mod lists any of those three except the engineer's C4. So before this feature a
-- nuclear warhead and a rifle round were in exactly the same position: neither could deal one point
-- of damage. And the ONE kill path that did work, engineer demolition, left a husk carrying
-- InfiltrateForTransform — a full restore at 10% HP for the price of one engineer.
--
-- SO THERE ARE THREE ARMS, and they fail in three different directions:
--
--   A. THE STRIKE. A tac nuke on NukedDerrick must kill it AND leave no oilb.husk. Killing it is
--      the easy half; the husk is the half the user reported. A run where the derrick dies and a
--      wreck appears is the SHIPPED-BEFORE behaviour dressed up as progress.
--
--   B. THE CONTROL THAT MUST NOT DIE. A t90 force-firing into ShelledDerrick's footprint for 250
--      ticks must leave it at full health. Opening the door for heavy ordnance must not have opened
--      it for everything, and this is the direction a mistake would most likely go — the obvious
--      "fix" for an untargetable building is to give it `Ground` back, which hands every rifle in
--      the game a derrick.
--
--      HONEST LIMIT OF THIS ARM, stated rather than glossed: AttackGround puts the shell on the
--      cell, so what it exercises is the SpreadDamage warheads' target-type filter — the same
--      filter that stops the nuke's other warheads. It does not exercise the tank's
--      Warhead@Target, which needs an actor target the tank cannot acquire. That is not a gap in
--      coverage so much as the mechanism restating itself: an armament that cannot acquire the
--      building is the first half of the same rule.
--
--   C. THE CONTROL THAT MUST LEAVE A WRECK. KilledDerrick is killed from script with NO damage
--      types, standing in for engineer demolition. Its husk MUST appear. This arm exists because
--      arm A passes just as well if someone deletes SpawnActorOnDeath from the derrick entirely —
--      which would satisfy the letter of "no wreck after a nuke" while breaking the recoverable
--      path the amendment explicitly said to preserve. Two arms, opposite directions, one field.
--
-- THE HUSK COUNT IS TAKEN GLOBALLY, off Map.ActorsInWorld, rather than by asking the dead actor
-- what it spawned — there is nothing left to ask. SpawnActorOnDeath spawns from
-- INotifyRemovedFromWorld, not from Killed, so the wreck appears a frame after the death and the
-- poll has to keep running past it. HuskSettleTicks below is that grace period.
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * Iskander, Kinzhal and GBU-57 delivery. All three carry their own Warhead@TechStructure and
--     all three are pinned statically by KillableEconomyTest; firing each would cost three launch
--     slots to re-measure a delivery path test-tacnuke-delivers and test-missile-strike-power
--     already own. The nuke is fired here because it is the one whose blast also lets arms B and C
--     double as proof that the OTHER warheads on the same weapon still cannot reach a derrick.
--   * The Atomic warhead's own shape — blast, fire, EMP, suppression. Unchanged by this work.
--   * Income arithmetic. USA's purse is REPORTED at both ends (passive income is off in rules.yaml
--     so the CashTricklers are the only thing that can move it) but not asserted: CashTrickler
--     cadence against a 250-tick control phase is a timing measurement this scenario is not about,
--     and a band loose enough to be safe would be loose enough to be meaningless.

local OrderKey = "TacNukeStrike"
local TargetX, TargetY = 40, 17
local HuskType = "oilb.husk"

-- THE SHOT IS BOUGHT, NOT CHARGED, and until 2026-09-07 this scenario could not fire it at all.
-- MissileStrikePower@TacNuke carries `RequiresPurchase: True`, which replaces the charge timer with
-- a magazine: at zero banked shots the power is Disabled, therefore not Ready, and
-- Test.ActivateSupportPower returns 'not-ready:0'. The `ChargeInterval: 1` + `StartFullyCharged`
-- this scenario's rules.yaml used to set were read and discarded by SupportPowerInstance's
-- constructor (SupportPowerManager.cs:228-229). GrantCharge has exactly one caller in the engine --
-- SupportPowerProductionQueue.BuildUnit -- so buying is the only way to load a shot. See the header
-- of TestHarness.EnsurePower in mods/ww3mod/scripts/test-helpers.lua.
--
-- The purchase runs from the first ticks of the shelling phase and is long complete by the time
-- ARM A orders the strike, so it costs this run nothing and moves no phase boundary.
local BuyProxy = "power.tacnuke"

-- Ticks the t90 shells ShelledDerrick before arm B is judged and the run moves on. 250 ticks is
-- 15 s at Timestep 60 — many full reload cycles for a tank main gun, so "no damage" here is a
-- statement about the target type and not about a slow first shot.
local ShellTicks = 250
-- Re-issued on this cadence because AttackGround completes and leaves the unit idle; one order
-- would land a couple of shells and stop.
local ReorderEvery = 40
-- Grace after a death for SpawnActorOnDeath to run from RemovedFromWorld and for the husk to be
-- added by the frame-end task. 30 ticks is far more than the one frame it needs; the cost of being
-- generous is a slightly longer run, the cost of being tight is a false PASS on arm A.
local HuskSettleTicks = 30
local ObserveTicks = 1400

local tick = 0
local phase = "shelling"
local Russia, USA
local shelledStartHealth, nukedStartHealth, killedStartHealth = 0, 0, 0
local shelledHealthAfterShelling = nil
local shooterAlive = true
local cashBefore, cashAfter = -1, -1
local killTick, killHuskTick = nil, nil
local husksAfterScriptKill = -1
local orderStatus, orderTick = "never-called", nil
local nukeDeathTick = nil
local husksAtEnd = -1
local huskCellsAtEnd = ""
local stateAtOrder = "never-read"
local buyStatus = "never-called"
local buyTick = nil
local finished = false

local function n(v)
	if v == nil then
		return "none"
	end

	return tostring(v)
end

-- Every oilb.husk currently in the world, whoever owns it. Owner is deliberately not filtered on:
-- SpawnActorOnDeath uses `OwnerType: InternalName`, so the wreck belongs to Neutral rather than to
-- the player who owned the derrick, and a per-player query would miss all of them.
local function husks()
	local found = {}
	-- Utils.Do rather than a bare ipairs, matching the one shipped precedent that walks this
	-- collection (test-deploy-queued-after-waypoints).
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
	return "shelled=" .. shelledStartHealth .. "hp -> " .. n(shelledHealthAfterShelling) .. "hp"
		.. " (t90 " .. (shooterAlive and "alive" or "DEAD") .. " after " .. ShellTicks .. "t of fire)"
		.. " | scripted kill@t" .. n(killTick) .. " husks after=" .. husksAfterScriptKill
		.. " | magazine=" .. buyStatus .. "@t" .. n(buyTick)
		.. " nuke order=" .. orderStatus .. "@t" .. n(orderTick) .. " state=" .. stateAtOrder
		.. " nuked " .. nukedStartHealth .. "hp -> "
		.. (NukedDerrick.IsDead and ("DEAD@t" .. n(nukeDeathTick)) or (NukedDerrick.Health .. "hp"))
		.. " | husks at end=" .. husksAtEnd .. " at [" .. huskCellsAtEnd .. "]"
		.. " | USA cash " .. cashBefore .. " -> " .. cashAfter
		.. " | SRs own=" .. (OwnSR.IsDead and "DEAD" or (OwnSR.Health .. "hp"))
		.. " opp=" .. (OpponentSR.IsDead and "DEAD" or (OpponentSR.Health .. "hp"))
		.. " | observed=" .. tick .. "t"
end

local function finish()
	husksAtEnd = #husks()
	huskCellsAtEnd = huskCells()
	cashAfter = USA.Cash
	local s = summary()

	-- ARM B first: if an ordinary weapon can hurt a derrick, nothing downstream means anything —
	-- the nuke killing it would prove only that damage reaches it, which is then true of everything.
	if shelledHealthAfterShelling == nil then
		Test.Fail("the shelling phase never completed, so the light-weapon control was never read."
			.. " || " .. s)
		return
	end

	if shelledHealthAfterShelling < shelledStartHealth then
		Test.Fail("a t90 main gun damaged an oil derrick: " .. shelledStartHealth .. " -> "
			.. shelledHealthAfterShelling .. "hp. The economy is now raidable by ordinary manoeuvre,"
			.. " which is exactly what the heavy/light split exists to prevent. Check that"
			.. " ^TechBuilding's Targetable has not regained `Ground` or `Structure`"
			.. " (structures.yaml) -- that is the single edit that would cause this. || " .. s)
		return
	end

	-- ARM C: conventional death must still leave a restorable wreck.
	if killTick == nil then
		Test.Fail("KilledDerrick was never killed from script, so the recoverable-death control did"
			.. " not run. || " .. s)
		return
	end

	if husksAfterScriptKill < 1 then
		Test.Fail("killing a derrick with NO damage types left no husk. ExcludedDeathTypes is a"
			.. " deny-list keyed on HeavyOrdnanceDeath and must not catch an ordinary death --"
			.. " engineer demolition is deliberately still recoverable. If SpawnActorOnDeath was"
			.. " removed from the derrick outright, repair-by-engineer is broken as a mechanic,"
			.. " which the user asked not to happen. || " .. s)
		return
	end

	-- ARM A, first half: did the strike arrive at all?
	if orderStatus ~= "issued" then
		Test.Fail("the tactical nuke order was refused: " .. orderStatus .. " (bin state at order: "
			.. stateAtOrder .. "). READ THE MAGAZINE FIELD FIRST: if it is not 'ready' the shot was"
			.. " never bought and the fault is in the shop -- 'refused' means the Powers queue"
			.. " rejected the order (check PowersSandboxCheckboxEnabled, which provides this"
			.. " power's powers.event tier, and Russia's DefaultCash against the 15000 price)."
			.. " If the magazine IS ready, the lobby gate is forced open in rules.yaml, so 'hidden'"
			.. " here means the GrantConditionOnLobbyOption@tacnuke chain has changed. || " .. s)
		return
	end

	if not NukedDerrick.IsDead then
		Test.Fail("the tactical nuke landed on the derrick and it is still standing at "
			.. NukedDerrick.Health .. "hp of " .. nukedStartHealth .. ". Atomic's"
			.. " Warhead@TechStructure is 200000 against a 50000hp oilb, so arrival means death:"
			.. " either the warhead is missing from weapons-heavy-ordnance.yaml, or that file is no"
			.. " longer LAST in mod.yaml's Weapons list and its merge is being overwritten, or"
			.. " ^TechBuilding no longer advertises `TechStructure`. || " .. s)
		return
	end

	-- ARM A, second half, and the assertion this scenario exists for.
	if husksAtEnd > husksAfterScriptKill then
		Test.Fail("the nuke destroyed the derrick and LEFT A WRECK: husk count went from "
			.. husksAfterScriptKill .. " to " .. husksAtEnd .. " at [" .. huskCellsAtEnd .. "]."
			.. " That wreck carries InfiltrateForTransform via ^TechBuildingHusk, so one engineer"
			.. " walking into it restores the whole income structure at 10% HP -- the attacker spent"
			.. " a nuclear warhead and the defender spends one engineer. Check that Atomic's"
			.. " Warhead@TechStructure still carries `HeavyOrdnanceDeath` in its DamageTypes AND"
			.. " that the derrick's SpawnActorOnDeath still carries"
			.. " `ExcludedDeathTypes: HeavyOrdnanceDeath`. Damage without permanence is the ORIGINAL"
			.. " reported bug, not a partial fix. || " .. s)
		return
	end

	-- Same guarantee test-tacnuke-delivers asserts, re-read here because this feature ADDED a
	-- warhead to the nuke and a new warhead is exactly how that guarantee would get broken.
	if OwnSR.IsDead or OpponentSR.IsDead then
		Test.Fail("a Supply Route was destroyed by the tactical nuke. SUPPLYROUTE's only target type"
			.. " is NoAutoTarget and no Atomic warhead may list it -- if the new"
			.. " Warhead@TechStructure has picked up a wider ValidTargets, every missile power in"
			.. " the mod is now a win button. || " .. s)
		return
	end

	Test.Pass("heavy strike erased the derrick with no wreck left behind; a t90 could not scratch a"
		.. " second one; a scripted no-damage-type death still left a restorable husk; Supply Routes"
		.. " intact. || " .. s)
end

local function step()
	tick = tick + 1

	-- Fill the magazine, from the first ticks of the run and regardless of phase. Stateless and
	-- idempotent, so it is simply called until it reports ready; at BuildDuration 5 that is around
	-- t=12, hundreds of ticks before ARM A orders the strike.
	if buyTick == nil then
		local ready, status = TestHarness.EnsurePower(Russia, BuyProxy, OrderKey, tick)
		buyStatus = status
		if ready then
			buyTick = tick
		end
	end

	if phase == "shelling" then
		if not Shooter.IsDead and tick % ReorderEvery == 1 then
			Shooter.Stop()
			-- allowMove: false so the tank holds its firing position and the 3-cell geometry above
			-- stays true; queued: false so each re-order replaces the last rather than stacking.
			-- 3 cells sits between ^TankRound's MinRange 1c512 and its Range 24c0.
			Shooter.AttackGround(ShelledDerrick.Location, false, false)
		end

		if tick >= ShellTicks then
			shooterAlive = not Shooter.IsDead
			shelledHealthAfterShelling = ShelledDerrick.IsDead and 0 or ShelledDerrick.Health
			if not Shooter.IsDead then
				Shooter.Stop()
			end

			-- ARM C fires here rather than at t=0 so its husk is already on the board and counted
			-- BEFORE the nuke goes off. The end-state assertion is then a comparison against a known
			-- baseline rather than against an absolute number, which stays correct if a future edit
			-- adds another husk-spawner to the map.
			KilledDerrick.Kill()
			killTick = tick
			phase = "husk-settle"
		end

		return
	end

	if phase == "husk-settle" then
		if killHuskTick == nil then
			killHuskTick = tick
		end

		if tick - killHuskTick >= HuskSettleTicks then
			husksAfterScriptKill = #husks()
			cashBefore = USA.Cash
			stateAtOrder = Test.GetSupportPowerState(Russia, OrderKey)
			orderStatus = Test.ActivateSupportPower(Russia, OrderKey, CPos.New(TargetX, TargetY))
			if orderStatus == "issued" then
				orderTick = tick
			end

			phase = "strike"
		end

		return
	end

	if phase == "strike" then
		if orderStatus ~= "issued" then
			finished = true
			finish()
			return
		end

		if nukeDeathTick == nil and NukedDerrick.IsDead then
			nukeDeathTick = tick
		end

		-- Deliberately keeps polling past the death for HuskSettleTicks: the wreck arrives a frame
		-- AFTER the kill (SpawnActorOnDeath runs from RemovedFromWorld), so a run that stopped on
		-- IsDead would report "no husk" for every possible implementation and pass regardless.
		if nukeDeathTick ~= nil and tick - nukeDeathTick >= HuskSettleTicks then
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

	if NukedDerrick == nil or ShelledDerrick == nil or KilledDerrick == nil or Shooter == nil then
		Test.Fail("a named actor is missing from the map: NukedDerrick/ShelledDerrick/"
			.. "KilledDerrick/Shooter")
		return
	end

	nukedStartHealth = NukedDerrick.Health
	shelledStartHealth = ShelledDerrick.Health
	killedStartHealth = KilledDerrick.Health

	-- Asserted at setup rather than left to be discovered in a confusing verdict: the whole scenario
	-- reads husk COUNTS, so a map that started with one would silently shift every comparison.
	if #husks() > 0 then
		Test.Fail("the map already contains an " .. HuskType .. " before anything has died; every"
			.. " husk-count assertion in this scenario is measured against a baseline of zero.")
		return
	end

	TestHarness.FocusBetween(Shooter, ShelledDerrick)
	TestHarness.Select(Shooter)

	Trigger.AfterDelay(1, loop)
end
