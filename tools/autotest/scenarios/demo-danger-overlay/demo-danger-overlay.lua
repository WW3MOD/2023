-- DEMO / EYEBALL: Stage-C danger overlay (control field + tri-state safety overlay).
--
-- LAYOUT (fog ON, map explored) — reuses the intel-overlay scene:
--   USA (blue):   Abrams on the left, facing east. Blue is the VIEWER (local player).
--   Russia (red): T-90s on the right (blue's front sees them) + structures/SR behind fog.
--
-- WHAT THIS STAGES
--   01-gated-off  : the dev overlay defaults OFF => no wash at all (the gate).
--   02-danger-ground: after one /danger toggle => tri-state safety on the ground channel —
--                     green where blue has verified-safe ground, RED over the sighted T-90
--                     line AND the believed-enemy-territory baseline (frontier projection),
--                     GRAY over unobserved fog.
--   03-danger-air  : second /danger => the anti-air channel isolated (the Stage-D heli window).
--   04-control     : third /danger => the DEMOTED control-field ownership wash (green=blue's
--                     believed territory, red=Russia's, gray=contested), alpha by margin.
--
-- Not a pass/fail test — stages the layers for a visual eyeball and Skips after captures flush.

WorldLoaded = function()
	TestHarness.FocusBetween(U3, U8, R3, R8)
	TestHarness.Select(U8)

	-- t=3s: overlay OFF (gate check) after vision + the influence stack have resolved.
	TestHarness.ScreenshotAfter(3, "01-gated-off",
		"expects: units + terrain visible, but NO colour wash — overlay gated off by default")

	-- /danger #1 -> DangerGround.
	Trigger.AfterDelay(100, function() Test.RunChatCommand("danger") end)
	TestHarness.ScreenshotAfter(5, "02-danger-ground",
		"expects: full-map coarse wash — RED over the sighted red T-90 line and near Russia's " ..
		"believed territory, GREEN over blue's verified-safe ground (left), GRAY over fog")

	-- /danger #2 -> DangerAir.
	Trigger.AfterDelay(150, function() Test.RunChatCommand("danger") end)
	TestHarness.ScreenshotAfter(7, "03-danger-air",
		"expects: anti-AIR channel wash — red only where an enemy weapon can hit helicopters")

	-- /danger #3 -> Control (secondary ownership wash).
	Trigger.AfterDelay(200, function() Test.RunChatCommand("danger") end)
	TestHarness.ScreenshotAfter(9, "04-control",
		"expects: control-field wash — green over blue's believed territory (left), red over " ..
		"Russia's (right), gray contested band between; brightness scales with how firmly held")

	-- Clean exit (Skip, not Pass/Fail) after the captures flush.
	Trigger.AfterDelay(275, function()
		Test.Skip("eyeball: Stage-C danger overlay staged (gated-off, ground, air, control)")
	end)
end
