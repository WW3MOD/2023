-- AUTO TEST: a spent strategic launcher ignores a stocked Logistics Centre four cells away and
-- leaves the map.
--
-- THE RULING (user, 2026-08-30): "Iskander and HIMARS should not be rearmable, they must be
-- evacuated". HIMARS therefore ships with no Rearmable and no ExternalCondition@VehicleReplenish --
-- the pull and push halves of rearm enrolment respectively -- and with
-- InitialResupplyBehavior/AI: Evacuate.
--
-- WHAT NEEDED A GAME RATHER THAN AN ARGUMENT, and it is the whole reason this scenario exists:
-- removing Rearmable is NOT sufficient to make the unit leave, and the insufficiency is silent.
-- AmmoPool.AutoRearmIfDry switches on the actor's ResupplyBehavior. On the shipped default (Auto)
-- it calls SupplyHuntMath.DecideAutoDisposition, which returns HoldAndFlag at SupplyHuntMath.cs:269
-- for any actor naming no rearm actors -- a guard that is correct for ^CrewMember (a unit that never
-- had a depot does not have a missing one) and catastrophic here. A HIMARS stripped of Rearmable and
-- left on Auto stands on the field forever: combat-inert, never disposed, raising NeedsResupply for
-- a rescue nothing in this ruleset can perform, because that flag's only reader is a Hunt-stance
-- SupplyProvider that has to DRIVE to the client and the Centre is a building.
--
-- The explicit Evacuate stance takes the other arm, whose ChooseResupplier returns null with no
-- RearmableInfo (AmmoPool.RearmCandidates returns empty on a null rearmInfo) and falls straight
-- through to EvacuateForRefund. The RED arm in rules.yaml sabotages exactly that stance, so the run
-- that proves this test works is the one where the launcher STANDS STILL.
--
-- WHY THE DEPOT IS STOCKED AND FOUR CELLS AWAY. A launcher that left because there was no depot
-- would prove nothing about the ruling -- that is the pre-existing behaviour and is already pinned
-- by test-dry-evac-drops-queued-order. Putting a full, own Logistics Centre within arm's reach
-- removes every alternative explanation: no leash term, no affordability term and no path term can
-- account for the unit declining it. Only the absent Rearmable can.
--
-- THE VERDICT IS NOT DISTANCE, deliberately. Two hard-won warnings from the branch that shipped
-- 9e46f141 apply directly here: nothing moves an undocked ground vehicle away from a Logistics
-- Centre, and a distance-based verdict on that branch once scored a wedged unit as HEALTHIER than
-- its repair. So the two observables are:
--   * Actor.IsDead -- with nothing on the map able to shoot it, the only route to IsDead is
--     RotateToEdge disposing it at the map edge (RotateToEdge.cs:407). This is "it left".
--   * the depot's supply, unchanged at its full 2250. This is "it took nothing on the way out".
-- Both must hold. IsDead alone would not catch a launcher that topped up and then evacuated; the
-- supply reading alone would not catch one that stood still holding nothing.

local DeadlineTicks = 1200
local FullLoad = 2250
local FireAtCell = CPos.New(32, 16)   -- 22 cells east: inside Range 50c0, outside MinRange 16c0
local Pool = "primary-ammo"

local function ticks(t) return t / TestHarness.TicksPerSecond end

WorldLoaded = function()
	TestHarness.FocusBetween(Launcher, Depot)

	Test.SetSupply(Depot, FullLoad)

	-- SETUP GUARDS. Each turns a would-be answer into "measured nothing", and the first one is the
	-- named trap: a pin on the 9e46f141 branch resolved a value to zero through inheritance and
	-- passed happily with the bug sabotaged in. A launcher starting at 0 rounds evacuates from
	-- OnBecomingIdle without ever firing, which would satisfy every assertion below for a reason
	-- that has nothing to do with the ruling.
	local startAmmo = Launcher.AmmoCount(Pool)
	if startAmmo ~= 1 then
		Test.Fail(string.format(
			"setup failed: the launcher holds %d round(s), want exactly 1. At 0 it would evacuate " ..
			"without firing and this run would pass while measuring nothing; above 1 it never runs " ..
			"dry on one shot and the dry path is never entered", startAmmo))
		return
	end

	if Test.GetSupply(Depot) ~= FullLoad then
		Test.Fail(string.format(
			"setup failed: the depot holds %d supply rather than %d, so 'the depot was not drawn " ..
			"down' would be unfalsifiable", Test.GetSupply(Depot), FullLoad))
		return
	end

	-- Force-fire at empty ground. forceAttack is implied by AttackGround (CombatProperties.cs:109)
	-- and is REQUIRED: HIMARS ships InitialStance: HoldFire and its Armament@1 is force-fire-only,
	-- so an ordinary attack order would be filtered out by ChooseArmamentsForTarget
	-- (AttackBase.cs:460) and the launcher would silently never fire.
	-- allowMove = false: the target is already in range, and letting it reposition would muddy which
	-- activity the evacuation cancelled.
	Launcher.AttackGround(FireAtCell, false, false)

	-- The deadline is enforced from INSIDE the predicate, and AssertWithin's own timeout is given
	-- slack so it never fires first. AssertWithin builds its timeout string at CALL time, so any
	-- live value interpolated into it would report the SETUP state (1 round, 2250 supply) no matter
	-- what actually happened -- a failure message that quietly describes the wrong world. The
	-- sibling scenario test-dry-evac-drops-queued-order carries the same warning at its own tail.
	local elapsed = 0

	TestHarness.AssertWithin(ticks(DeadlineTicks * 2), function()
		elapsed = elapsed + 1
		local supply = Test.GetSupply(Depot)

		-- Checked BEFORE the IsDead pass so a launcher that rearmed and THEN evacuated fails rather
		-- than passing on the strength of having left. Ordering matters: the ruling is "cannot be
		-- rearmed AND must leave", not "must leave".
		if supply ~= FullLoad then
			return string.format(
				"fail: the depot dropped from %d to %d supply, so the launcher drew a rearm from it. " ..
				"It is still enrolled with the Centre -- check that BOTH the Rearmable (pull) and the " ..
				"ExternalCondition@VehicleReplenish (push) are absent; removing only one leaves the " ..
				"other live", FullLoad, supply)
		end

		-- THE PASS. Nothing on this map can damage the launcher and it is staged at full health, so
		-- IsDead can only mean RotateToEdge disposed it at the map edge.
		if Launcher.IsDead then
			return true
		end

		if elapsed >= DeadlineTicks then
			local held = Launcher.AmmoCount(Pool)
			return string.format(
				"fail: the launcher is still on the map after %d ticks, holding %d round(s) beside a " ..
				"depot still holding %d. It neither rearmed nor left. If it holds 0 this is the " ..
				"HoldAndFlag trap -- with no Rearmable, DecideAutoDisposition returns HoldAndFlag " ..
				"(SupplyHuntMath.cs:269) unless the actor carries an explicit " ..
				"InitialResupplyBehavior/AI: Evacuate, so check that stance is still set on HIMARS. " ..
				"If it holds 1 it never fired, and nothing about the dry path was observed at all",
				elapsed, held, supply)
		end

		return false
	end, "unreachable: the in-predicate deadline at " .. DeadlineTicks .. " ticks fires first")
end
