-- AUTO TEST: a unit that cannot shoot must still go where the player sent it.
--
-- The reported symptom, from live play: an out-of-ammo unit gets its resupply order (the green
-- line to the supply truck is drawn) but never travels, and it ignores plain Move orders too --
-- only Force-Move makes it budge. That last detail is the diagnostic one, because in WW3MOD
-- "Move" and "ForceMove" are not two spellings of one order. Mobile.ResolveOrder wraps "Move"
-- in every IWrapMove trait (Mobile.cs:1021) and deliberately does NOT wrap "ForceMove"
-- (Mobile.cs:1032). ^Infantry carries SmartMove, so the plain-Move path alone runs inside
-- SmartMoveActivity.
--
-- The pin: SmartMoveActivity scans for an opportunistic target while moving, and asks whether an
-- armament reaches it -- but ChooseArmamentsForTarget filters on IsTraitDisabled only (there is a
-- literal "FF TODO Check ammo?" at AttackBase.cs:437), and an empty armament is PAUSED, not
-- disabled. So a dry unit still reports a weapon in range, cancels its own move child, and queues
-- an attack that AmmoPool.CannotFight ends on its first tick (Attack.cs:117). runningMoveActivity
-- stays false, so the next tick re-scans with ignoreScanInterval and does it again, forever. The
-- move is never re-queued and the activity never completes, so the unit also never goes idle --
-- which is why none of the idle-path fixes reached this.
--
-- Both men are dry, start the same distance from the same enemy, and are sent to mirrored cells.
-- Forcer is the control: his order skips the wrapper, so he must arrive whether or not the bug is
-- fixed. Mover is the subject. The asymmetry between them IS the verdict -- if the scenario ever
-- fails with BOTH men stuck, something unrelated broke and the result says nothing about this bug.
--
-- The Bait sits inside the AR's 14c0 reach for the whole trip (see map.yaml), so passing by
-- walking out of the interrupt's range is not available.

local DeadlineSeconds = 30
local IssueAfterTicks = 25 -- let shroud and targeting settle before ordering
local LineX = 5 -- both destinations are x=4; arriving means crossing this
local DiagnoseAfterPolls = 25 * 20 -- 20s: long past a 6-cell infantry walk

local moverArrived = false
local forcerArrived = false
local polls = 0

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

		if Mover.Location.X <= LineX then moverArrived = true end
		if Forcer.Location.X <= LineX then forcerArrived = true end

		-- Name the asymmetry as soon as it is unambiguous, rather than letting the run time out
		-- into a generic message. This is the sentence the bug report was describing.
		if polls > DiagnoseAfterPolls and forcerArrived and not moverArrived then
			return "fail: Force-Move arrived but the plain Move did not -- dry Mover pinned at "
				.. Mover.Location.X .. "," .. Mover.Location.Y
				.. " with the enemy in weapon range (SmartMoveActivity interrupt ignores ammo)"
		end

		return moverArrived and forcerArrived
	end, "A dry rifleman did not reach the cell he was ordered to")
end
