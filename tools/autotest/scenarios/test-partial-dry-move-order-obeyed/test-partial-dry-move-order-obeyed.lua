-- AUTO TEST: a rifleman who cannot shoot THIS target must still go where the player sent him.
--
-- This is the second half of the out-of-ammo move wedge. The first half (branch auto/ooa-wedge)
-- gated SmartMoveActivity's opportunistic-fire interrupt on AmmoPool.CannotFight, which is true
-- only when EVERY pool is empty. One pool short of that, the entire old shape survives — and one
-- pool short of that is not an exotic corner, it is ^E3, the mod's standard infantryman, in the
-- state any infantry-vs-infantry firefight leaves him in:
--
--   * ^E3 carries a DMR (primary-ammo, 100) and an RPG (secondary-ammo, 1).
--   * RPG declares InvalidTargets: Infantry, so it never had a legal target while the DMR emptied.
--   * So the DMR is spent, the RPG is loaded, and CannotFight is FALSE.
--
-- Against the infantry Bait, ChooseArmamentsForTarget offers exactly one armament — the spent DMR
-- (it filters IsTraitDisabled, and an empty armament is PAUSED, not disabled: AttackBase.cs:437,
-- the literal "FF TODO Check ammo?"). SmartMoveActivity therefore still believes a weapon reaches
-- the target, still cancels its own move child, and still queues an attack. And this time the
-- ending is WORSE than the bug already fixed: Attack.cs:117's CannotFight guard is also false, so
-- the attack activity does not end either. The man stops mid-move and aims a weapon he cannot fire
-- for as long as the target lives, never travelling and never going idle — and because
-- AutoRearmIfAllEmpty also needs every pool empty, he has no recovery path at all.
--
-- Both men are in that state, start the same distance from the same enemy, and are sent to
-- mirrored cells. Forcer is the control: "ForceMove" skips the IWrapMove wrappers entirely
-- (Mobile.cs:1032), so he must arrive whether or not the bug is fixed. Mover is the subject. The
-- asymmetry between them IS the verdict — if this ever fails with BOTH men stuck, something
-- unrelated broke and the result says nothing about this bug.
--
-- The Bait sits inside the DMR's 14c0 reach for the whole trip (see map.yaml), so passing by
-- walking out of the interrupt's range is not available.

local DeadlineSeconds = 30
local IssueAfterTicks = 25 -- let shroud and targeting settle before ordering
local LineX = 5 -- both destinations are x=4; arriving means crossing this
local DiagnoseAfterPolls = 25 * 20 -- 20s: long past a 6-cell infantry walk

local moverArrived = false
local forcerArrived = false
local polls = 0
local setupChecked = false

WorldLoaded = function()
	TestHarness.FocusBetween(Mover, Bait)
	TestHarness.Select(Mover)

	-- Silence the ENEMY, never the subjects: SmartMoveActivity opts out entirely below
	-- FireAtWill, so putting the men under test on HoldFire would disable the wrapper this
	-- test exists to exercise (AUTOTEST.md gotcha 7).
	Bait.Stance = "HoldFire"

	Trigger.AfterDelay(IssueAfterTicks, function()
		if not Mover.IsDead then
			Test.IssueMove(Mover, CPos.New(4, 14), false)
		end

		if not Forcer.IsDead then
			Test.IssueMove(Forcer, CPos.New(4, 18), true)
		end
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Mover.IsDead then return "fail: Mover died" end
		if Forcer.IsDead then return "fail: Forcer died" end

		polls = polls + 1

		-- Pin the premise before measuring anything. If the primary is not empty there is no
		-- stall to observe, and if the secondary is ALSO empty this has quietly turned into
		-- test-dry-move-order-obeyed and would pass on the already-shipped CannotFight guard
		-- while proving nothing about the partial case.
		if not setupChecked then
			setupChecked = true
			local primary = Mover.AmmoCount("primary-ammo")
			local secondary = Mover.AmmoCount("secondary-ammo")
			if primary ~= 0 or secondary <= 0 then
				return "fail: setup wrong -- need primary spent and secondary loaded, got primary="
					.. primary .. " secondary=" .. secondary
			end
		end

		if Mover.Location.X <= LineX then moverArrived = true end
		if Forcer.Location.X <= LineX then forcerArrived = true end

		-- Name the asymmetry as soon as it is unambiguous, rather than letting the run time out
		-- into a generic message.
		if polls > DiagnoseAfterPolls and forcerArrived and not moverArrived then
			return "fail: Force-Move arrived but the plain Move did not -- rifleman with a spent "
				.. "DMR and a loaded RPG pinned at " .. Mover.Location.X .. "," .. Mover.Location.Y
				.. " aiming at infantry his only valid weapon has no ammo for (SmartMoveActivity "
				.. "interrupt counts paused armaments as usable; CannotFight is false so neither "
				.. "the move guard nor Attack.cs's guard fires)"
		end

		return moverArrived and forcerArrived
	end, "A rifleman with a spent rifle and a loaded RPG did not reach the cell he was ordered to")
end
