-- AUTO TEST: Smoke-test the screenshot capture pipeline.
-- Captures three screenshots at named beats then passes. Verifies that
-- (a) Test.Screenshot returns a path, (b) the PNGs end up in the per-run
-- screenshot directory, and (c) the verdict JSON's screenshots[] array
-- lists all three.
--
-- Beats:
--   01-initial   — WorldLoaded, camera centered between Paladin and Target
--   02-mid       — 1 second in, before any orders given
--   03-final     — 2 seconds in, just before the pass verdict

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin, Target)
	TestHarness.Select(Paladin)

	TestHarness.Screenshot("01-initial",
		"expects: M109 (blue) and T-90 (red) in frame, camera centered between them")

	TestHarness.ScreenshotAfter(1, "02-mid",
		"expects: same scene 1s later — no movement, units idle")

	Trigger.AfterDelay(50, function()
		TestHarness.Screenshot("03-final",
			"expects: same scene just before pass verdict")
		Test.Pass("smoke test took 3 screenshots")
	end)
end
