-- DEMO / EYEBALL: Phase 1 intel overlay (§3a sighting layer + §3d overlay)
--
-- LAYOUT (fog ON, map explored)
--   USA (blue):   10 Abrams, cols 23/26, facing east.
--   Russia (red): 10 T-90s,  cols 30/33, facing west (blue's front sees them).
--   Russia GTWRs @ col 40 + SR @ 60,30 — behind fog => frozen ghosts => GPS dots.
--
-- WHAT THIS PROVES
--   01-gated-off : dev switch OFF and Space not held => overlay hidden (the gate).
--   02-overlay-on: after /intel toggles the dev switch on => BoP wash
--                  (green over blue, red over the sighted red line, computed gray
--                  band between) + GPS dots on the frozen enemy structures.
--   Skip note carries sampled §3a intensities as a numeric cross-check.
--
-- Not a pass/fail test — it stages the layers for a visual + numeric eyeball
-- and Skips (exit code 2) after the captures flush.

WorldLoaded = function()
	TestHarness.FocusBetween(U3, U8, R3, R8)
	TestHarness.Select(U8)

	-- t=2s: capture with the dev switch OFF (and Space not held) AFTER vision has
	-- resolved — units + terrain visible, but the overlay must be absent (gate check).
	TestHarness.ScreenshotAfter(2, "01-gated-off",
		"expects: units + terrain visible, but NO color wash and NO GPS dots — " ..
		"overlay gated off (dev switch off, Space not held)")

	-- t~2.5s: enable the dev always-on switch via the /intel chat command.
	Trigger.AfterDelay(63, function()
		Test.RunChatCommand("intel")
	end)

	-- t=4s: overlay on and the §3a layer has run several recomputes.
	TestHarness.ScreenshotAfter(4, "02-overlay-on",
		"expects: green wash over blue Abrams (left), red wash over the sighted T-90 line, " ..
		"computed GRAY band between; GPS dots on the frozen Russia GTWRs (~col 40) under fog")

	-- t~5.6s: numeric cross-check + clean exit (Skip, not Pass/Fail) after captures flush.
	Trigger.AfterDelay(140, function()
		local blue = U1.Owner
		local redThreat = Test.GetThreatIntensity(blue, R3.Location)
		local blueFriendly = Test.GetFriendlyIntensity(blue, U8.Location)
		local dir = Test.GetThreatDirection(blue, U8.Location)
		Test.Skip("eyeball: redThreat@R3=" .. redThreat ..
			" blueFriendly@U8=" .. blueFriendly ..
			" threatDir@U8=" .. dir .. " (768=east toward enemy)")
	end)
end
