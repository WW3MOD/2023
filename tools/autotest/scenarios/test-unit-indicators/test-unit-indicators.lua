-- AUTO TEST: visual evidence for the two new unit indicators.
--
-- There is no state assertion worth making here — the indicators are render-only and Lua
-- cannot query a decoration. The run exists to produce two captures to look at:
--
--   01-cluster — seven USA units at realistic spacing, every drawable stance represented,
--                with a loaded + damaged + suppressed + selected transport in the middle so
--                the new glyphs are judged against cargo, damage and suppression rows that
--                are already competing for the same box.
--   02-probe   — the asymmetry case. Probe is inside Watcher's 24-cell Strength-10 vision but
--                Watcher is outside anything Probe can resolve, so Probe should carry NO
--                spotted mark despite genuinely being seen.
--
-- Every capture gets its own delay and nothing mutates the world after one is armed:
-- Test.Screenshot only ARMS the grab, which lands at the end of the next RenderTick, so a
-- mutation on the following line is photographed under the previous label.

local function try(fn)
	local ok, err = pcall(fn)
	if not ok then
		Media.Debug("indicator-setup: " .. tostring(err))
	end
end

WorldLoaded = function()
	-- Fire-axis stances: actor.Stance is the only stance the Lua API exposes. The engagement
	-- axis comes from the e1hold / e1hunt derived types in rules.yaml.
	try(function() InfHoldFire.Stance = "HoldFire" end)
	try(function() InfAmbush.Stance = "Ambush" end)

	-- InfBoth is an e1hold (HoldPosition) that also holds fire, so it draws on both axes.
	try(function() InfBoth.Stance = "HoldFire" end)

	-- Load the transport's own decoration rows up: damage pips, a mid-tier suppression
	-- chevron. No cargo row — see map.yaml.
	try(function() Apc.Health = math.floor(Apc.MaxHealth * 0.45) end)
	try(function()
		for _ = 1, 25 do
			Apc.GrantCondition("suppressed")
		end
	end)

	-- Suppression pips are RequiresSelection: true, so the transport has to be selected for
	-- that row to be on screen at all.
	TestHarness.Select(Apc)
	TestHarness.FocusBetween(InfDefault, InfBoth)

	Trigger.AfterDelay(75, function()
		TestHarness.Screenshot("01-cluster",
			"expects: 7 blue USA units. Red '!' right of every unit the Russian scout can see. " ..
			"Stance glyphs on the row above: white X = hold fire, yellow A = ambush, " ..
			"cyan H = hold position, orange > = hunt. Default-stance unit shows NO stance glyph. " ..
			"Selected m113 in the middle also shows cargo, damage and suppression rows — check " ..
			"whether the new glyphs collide with any of them or read as clutter.")
	end)

	Trigger.AfterDelay(125, function()
		TestHarness.FocusBetween(Probe, Probe)
	end)

	Trigger.AfterDelay(175, function()
		TestHarness.Screenshot("02-probe",
			"expects: the lone USA rifleman carries NO red '!'. It IS being seen — the Watcher 22 " ..
			"cells south reveals it at Strength 10 — but Probe's own vision has decayed to Strength 4 " ..
			"at that range and cannot reveal infantry, which need 9. So we are spotted by an enemy we " ..
			"have not spotted, and the asymmetry rule draws nothing. NOTE: the harness has no render " ..
			"player, so there is no fog and enemy units render marks a real player would never see — " ..
			"judge ONLY the USA rifleman at frame centre.")
	end)

	Trigger.AfterDelay(225, function()
		Test.Pass("captured cluster + asymmetry probe")
	end)
end
