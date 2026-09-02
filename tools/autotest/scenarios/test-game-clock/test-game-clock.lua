-- AUTO TEST: the in-game clock must report GAMETIME, not wall-clock.
--
-- Run this with an accelerated timestep (./run-test.sh --speed 8 test-game-clock).
-- Test.SpeedMultiplier divides world.Timestep exactly like the in-game debug speed
-- button does, so it reproduces the same mutation the clock bug was sensitive to.
--
-- ww3mod's configured "default" GameSpeed is 60ms/tick => 1000 ticks per displayed
-- minute. The clock must read the tick count at that baseline no matter what
-- world.Timestep has been changed to. The old code formatted with world.Timestep,
-- so at 8x it showed roughly an eighth of the elapsed time.
--
-- Verdict is visual: read the HUD clock (top-right of the sidebar) in each PNG.

local TicksPerDisplayedMinute = 1000

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin, Target)

	TestHarness.Screenshot("01-tick-0",
		"expects: HUD clock top-right reads 00:00")

	Trigger.AfterDelay(TicksPerDisplayedMinute, function()
		TestHarness.Screenshot("02-tick-1000",
			"expects: HUD clock reads 01:00 (1000 ticks x 60ms), NOT a speed-scaled fraction of it")
	end)

	Trigger.AfterDelay(2 * TicksPerDisplayedMinute, function()
		TestHarness.Screenshot("03-tick-2000",
			"expects: HUD clock reads 02:00 — exactly double the 01:00 shot, proving the mapping is linear in ticks")
		Test.Skip("clock screenshots captured at ticks 0 / 1000 / 2000")
	end)
end
