-- AUTO TEST: BEFORE baseline for the unit-indicator work. Identical scene and identical
-- setup to test-unit-indicators; its rules.yaml strips the two new decorations so these
-- captures show what the same units looked like before the branch.
--
-- There is no state assertion worth making here — the indicators are render-only and Lua
-- cannot query a decoration. The run exists to produce two captures to look at:
--
--   01-cluster — seven USA units at realistic spacing, every drawable stance represented,
--                with a loaded + damaged + suppressed + selected transport in the middle so
--                the new glyphs are judged against cargo, damage and suppression rows that
--                are already competing for the same box.
--   02-probe   — the asymmetry case. Probe is inside Watcher's 20-cell vision but Watcher is
--                outside Probe's, so Watcher should not be on screen and Probe should carry
--                NO spotted mark despite genuinely being seen.
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
		TestHarness.Screenshot("01-cluster-before",
			"expects: the SAME 7 USA units and the SAME selected m113 as the after-run, with NO red '!' " ..
			"and NO stance letters anywhere. This is the baseline the added glyphs get judged against.")
	end)

	Trigger.AfterDelay(125, function()
		TestHarness.FocusBetween(Probe, Probe)
	end)

	Trigger.AfterDelay(175, function()
		TestHarness.Screenshot("02-probe-before",
			"expects: one lone blue USA rifleman, no Russian unit in frame, no marks of any kind.")
	end)

	Trigger.AfterDelay(225, function()
		Test.Pass("captured BEFORE baseline")
	end)
end
