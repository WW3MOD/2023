-- AUTO TEST: the other side of the ammo condition in SmartMoveActivity.
--
-- test-dry-move-order-obeyed asserts that a unit which CANNOT fire is not pinned in place by
-- SmartMove's opportunistic-fire interrupt. That assertion is satisfiable by deleting the
-- interrupt, which would quietly remove the feature SmartMove exists for -- so this scenario
-- pins the branch that must NOT change: a loaded man under a plain Move order still breaks
-- stride to shoot an enemy inside weapon range.
--
-- Per AUTOTEST.md, "a behaviour selected by a condition needs a test on EACH SIDE of it": these
-- two must go green together or neither number means anything.
--
-- The assertion is that ammo drops, and the deadline is what makes it honest. The Shooter is
-- ordered 6 cells west; foot infantry (Speed 25) needs roughly 41 ticks per cell, so the walk is
-- ~10s. Firing within 6s therefore cannot be explained by "he arrived and then went idle and
-- shot from a standstill" -- it can only be the mid-move interrupt.

local DeadlineSeconds = 6
local IssueAfterTicks = 25

local startingAmmo = -1

WorldLoaded = function()
	TestHarness.FocusBetween(Shooter, Bait)
	TestHarness.Select(Shooter)

	startingAmmo = Shooter.AmmoCount("primary-ammo")

	-- Silence the Bait so it cannot kill the subject; the subject stays FireAtWill because that
	-- is the behaviour under test (AUTOTEST.md gotcha 7).
	Bait.Stance = "HoldFire"

	Trigger.AfterDelay(IssueAfterTicks, function()
		if not Shooter.IsDead then
			Test.IssueMove(Shooter, CPos.New(4, 16), false)
		end
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Shooter.IsDead then return "fail: Shooter died" end

		return Shooter.AmmoCount("primary-ammo") < startingAmmo
	end, "A loaded rifleman walked past an enemy in weapon range without firing -- SmartMove's stop-and-engage is gone")
end
