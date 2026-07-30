-- DEMO / EYEBALL: Territory overlay v2 (Stage-C control field, player-facing view)
--
-- LAYOUT (fog ON, map explored) — reuses the intel-overlay battlefield:
--   USA (blue):   10 Abrams, cols 23/26, facing east. Home SR @ 4,4.
--   Russia (red): 10 T-90s,  cols 30/33, facing west. SR @ 60,30 + GTWRs @ col 40,
--                 behind fog => frozen ghosts => believed-enemy anchors.
--
-- WHAT THIS PROVES
--   01-gated-off  : ShowTerritory key not held and dev switch OFF => overlay hidden.
--   02-territory-on: after /territory forces the dev switch on => the WHOLE map reads
--                    green (blue's held left) / red (enemy right, incl. the fogged SR
--                    corner) / gray (contested midline), and diagonal STALENESS STRIPES
--                    hatch the controlled cells — clean around blue's units/vision,
--                    heavy over the unseen enemy half (never verified => max stripe).
--
-- Not a pass/fail test — it stages the field for a visual eyeball and Skips (exit
-- code 2) after the captures flush.

WorldLoaded = function()
	-- Frame the front + the enemy half so the green->gray->red transition AND the
	-- stripe boundary (eyes vs. fog) are both in shot.
	TestHarness.FocusBetween(U8, R8, G1)
	TestHarness.Select(U8)

	-- t=2s: capture with the overlay gated OFF (key not held, dev switch off) AFTER
	-- vision has resolved — units + terrain visible, but NO wash (the gate check).
	TestHarness.ScreenshotAfter(2, "01-gated-off",
		"expects: units + terrain visible, but NO color wash and NO stripes — " ..
		"overlay gated off (dev switch off, ShowTerritory key not held)")

	-- t~2.5s: force the dev always-on switch via the /territory chat command.
	Trigger.AfterDelay(63, function()
		Test.RunChatCommand("territory")
	end)

	-- t=4s: overlay on and the control field has run several recomputes.
	TestHarness.ScreenshotAfter(4, "02-territory-on",
		"expects: full-map wash — GREEN over blue's left, RED over the enemy right " ..
		"(incl. the fogged SR corner), GRAY contested midline; diagonal STALENESS STRIPES " ..
		"over controlled cells — light/clean around blue's units, heavy over the unseen enemy half; " ..
		"and a bright YELLOW FRONTLINE CONTOUR line running top-to-bottom down the green/red divide")

	-- t~5.6s: clean exit (Skip, not Pass/Fail) after the captures flush.
	Trigger.AfterDelay(140, function()
		Test.Skip("eyeball: territory overlay v2 — control wash + staleness stripes")
	end)
end
