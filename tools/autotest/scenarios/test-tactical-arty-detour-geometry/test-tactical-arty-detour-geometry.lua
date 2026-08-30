-- AUTO TEST: a dry tactical MLRS detours to a Logistics Centre only when the Centre beats the exit.
--
-- WHAT COMMIT 28795dca ACTUALLY CHANGED. grad, m270 and tos gained
-- `Rearmable: RearmActors: logisticscenter` and kept `InitialResupplyBehavior: Evacuate`. So their
-- automatic behaviour when dry is NOT "seek a depot" -- it is the Evacuate arm of
-- AmmoPool.AutoRearmIfDry, which takes a DETOUR first and only if four gates all pass:
--   1. ChooseResupplier finds an own host with CurrentSupply > 0
--   2. HostCanAffordSomethingWeNeed
--   3. WithinCellBudget against DryRearmLeashCells (30, chessboard)
--   4. SupplyHuntMath.ResupplyBeatsExit -- strict <, ties go to leaving
-- Gate 4 is the whole content of the feature and the only one this scenario is about. A dry grad
-- will NOT cross the map to a depot behind it; it goes home instead. That is the design.
--
-- WHY TWO SUBJECTS AND NOT ONE. Gate 4 is a COMPARISON, so a single unit can only ever show one
-- side of it, and a passing one-sided test is indistinguishable from a test where the comparison was
-- skipped entirely -- which is exactly what happens when no `spawnarea` exists on the map and
-- `anchor == null` makes the detour unconditional (see map.yaml). Two subjects on one map, differing
-- ONLY in where they stand relative to the same depot and the same anchor, pin both directions:
--
--   GradNear (27,12): depot 3 cells, anchor 21 -> 3 < 21  -> DETOUR and rearm
--   GradFar  (14,20): depot 16 cells, anchor 8 -> 16 < 8 is false -> EVACUATE
--
-- GradFar is the one that carries the test. Its depot is real, own, stocked, affordable and 16 cells
-- away -- comfortably inside the 30-cell leash, so gate 3 passes and cannot be what sends it home.
-- Only gate 4 can.
--
-- OBSERVABLES ARE PER-UNIT, deliberately, so neither verdict depends on attributing a shared
-- reading. GradNear: alive AND holding rounds it did not start with. GradFar: IsDead, which with
-- nothing on this map able to shoot it can only mean RotateToEdge disposed it at the map edge
-- (RotateToEdge.cs:407). The depot drawdown is asserted too, but only as CORROBORATION for
-- GradNear -- the pass does not rest on it.

local DeadlineTicks = 2000
local FullLoad = 2250
local Pool = "primary-ammo"

local function ticks(t) return t / TestHarness.TicksPerSecond end

WorldLoaded = function()
	TestHarness.FocusBetween(GradFar, Depot)

	Test.SetSupply(Depot, FullLoad)

	-- SETUP GUARDS. Each turns a would-be answer into "measured nothing".
	if GradNear.AmmoCount(Pool) ~= 0 or GradFar.AmmoCount(Pool) ~= 0 then
		Test.Fail(string.format(
			"setup failed: subjects hold %d and %d rounds, want 0 and 0. A subject that starts armed " ..
			"never enters the dry path at all, and its standing still would look like a decision",
			GradNear.AmmoCount(Pool), GradFar.AmmoCount(Pool)))
		return
	end

	if Test.GetSupply(Depot) ~= FullLoad then
		Test.Fail(string.format(
			"setup failed: the depot holds %d supply rather than %d, so neither 'it rearmed here' nor " ..
			"'it drew nothing' would be readable", Test.GetSupply(Depot), FullLoad))
		return
	end

	-- A one-cell nudge on each, purely to guarantee a busy -> idle TRANSITION. Actor.Tick fires
	-- INotifyBecomingIdle only on `!wasIdle && IsIdle` (Actor.cs:317-323), and AmmoPool implements
	-- neither ITick nor INotifyIdle, so a unit that is simply standing there is not guaranteed to
	-- re-enter the dispatch. Both destinations are chosen so the comparison outcome is unchanged:
	--   GradNear 27,11 -> depot 3, anchor 21  (still detours)
	--   GradFar  14,21 -> depot 16, anchor 8  (still evacuates)
	GradNear.Move(CPos.New(27, 11))
	GradFar.Move(CPos.New(14, 21))

	-- The deadline is enforced INSIDE the predicate; AssertWithin's own timeout gets slack so it
	-- never fires first. AssertWithin builds its timeout string at CALL time, so any live value
	-- interpolated there would report the setup state no matter what happened.
	local elapsed = 0

	TestHarness.AssertWithin(ticks(DeadlineTicks * 2), function()
		elapsed = elapsed + 1

		local nearDead = GradNear.IsDead
		local farDead = GradFar.IsDead
		local supply = Test.GetSupply(Depot)

		-- GradNear evacuating is a hard, immediate failure: the depot was three cells away and it
		-- went home instead. Checked eagerly because it is terminal -- waiting cannot undo it.
		if nearDead then
			return string.format(
				"fail: GradNear evacuated instead of detouring. Its depot was 3 cells away against an " ..
				"anchor at 21, so ResupplyBeatsExit should have been true. Either the detour branch is " ..
				"not reached at all (check the Rearmable added in 28795dca is still present, and that " ..
				"the depot is own-player and stocked), or the comparison is inverted. Depot holds %d",
				supply)
		end

		local nearAmmo = GradNear.AmmoCount(Pool)

		-- THE PASS: opposite dispositions from the same stance, the same depot and the same anchor.
		if farDead and nearAmmo > 0 then
			if supply >= FullLoad then
				return string.format(
					"fail: GradNear holds %d round(s) but the depot is still at %d. It gained ammunition " ..
					"without paying for it, so an unmetered rearm route survives -- Rearmable.RearmTick " ..
					"is supposed to charge via AmmoPool.TryServeBatch", nearAmmo, supply)
			end

			return true
		end

		if elapsed >= DeadlineTicks then
			return string.format(
				"fail: after %d ticks GradNear holds %d round(s) (alive) and GradFar is %s, depot at %d " ..
				"of %d. Wanted GradNear rearmed and GradFar evacuated. If GradFar is ALIVE it took the " ..
				"detour its geometry forbids -- depot 16 cells vs anchor 8 -- which means " ..
				"ResupplyBeatsExit was not consulted; the usual cause is no `spawnarea` actor on the " ..
				"map, since a null anchor makes the detour unconditional. If GradNear holds 0 it never " ..
				"reached the depot, and nothing about the comparison was observed on either subject",
				elapsed, nearAmmo, farDead and "gone" or "still on the map", supply, FullLoad)
		end

		return false
	end, "unreachable: the in-predicate deadline at " .. DeadlineTicks .. " ticks fires first")
end
