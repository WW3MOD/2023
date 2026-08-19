-- CAPTURE SCENARIO: the command bar after TAKE_COVER was deleted, and the nine renamed
-- garrison buttons.
--
-- Two frames, two selections, one launch:
--
--   01  three riflemen selected -> the reflowed command bar along the bottom
--   02  a garrison with eight manned ports selected -> GARRISON_PANEL with eight "Out"
--       buttons and "Unload All"
--
-- The other two of the eleven renamed buttons — UNLOAD_ALL_TROOPS and DROP_SUPPLY — are
-- in CARGO_PANEL and are photographed by test-visual-cargo-panel, which is where a loaded
-- transport already exists.
--
-- demo-command-bar-reflow stages the first of these already, but it is a demo: it Skips
-- rather than passing and is not picked up unattended. This scenario is the runnable
-- version and it also covers the garrison half.
--
-- Captures are one per delay with quiet either side — Test.Screenshot arms a grab that
-- samples at the end of the NEXT RenderTick, so a selection change on the following line
-- would be photographed under the previous label.

local ExpectedGarrison = 8

WorldLoaded = function()
	TestHarness.FocusBetween(Rifle1, House)

	Trigger.AfterDelay(DateTime.Seconds(2), function()
		Test.SelectActors({ Rifle1, Rifle2, Rifle3 })
	end)

	-- ---- Frame 1: the command bar ---------------------------------------------------
	Trigger.AfterDelay(DateTime.Seconds(4), function()
		local selected = Test.GetSelectedCount()
		if selected ~= 3 then
			Test.Fail("selection is " .. tostring(selected) .. " actors, not the 3 riflemen — " ..
				"the command bar's button states depend on what is selected, so this frame " ..
				"would be a picture of some other state")
			return
		end

		TestHarness.Screenshot("01-command-bar-infantry-selected",
			"expects, BOTTOM LEFT: two command panels then four stance panels, all flush " ..
			"with each other — no gap and no overlap anywhere along the row. " ..
			"CORRECT = the LEFT panel holds 8 buttons; the RIGHT panel holds exactly FOUR " ..
			"(resupply, patrol, auto-enter, evacuate) with equal margins at both ends; no " ..
			"'Take Cover' button anywhere; no empty slot or dead space where a fifth used " ..
			"to be. The four stance bars to the right each sit inside their own panel with " ..
			"the same margin as each other. " ..
			"BROKEN = a visible hole mid-row, panels overlapping at a seam, or the right " ..
			"panel wider than its four buttons need. " ..
			"MERELY PRESENT = the bar is drawn but you cannot tell where one panel ends and " ..
			"the next begins — say so, that is a finding about the seams, not a pass.")
	end)

	-- ---- Frame 2: the garrison panel --------------------------------------------------
	Trigger.AfterDelay(DateTime.Seconds(7), function()
		TestHarness.Select(House)
		Camera.Position = House.CenterPosition
	end)

	Trigger.AfterDelay(DateTime.Seconds(10), function()
		if House.IsDead then
			Test.Fail("the garrison building died before the capture")
			return
		end

		if Test.GetSelectedCount() ~= 1 then
			Test.Fail("selection is " .. tostring(Test.GetSelectedCount()) ..
				" actors, not 1 — GarrisonPanelLogic only draws for a single owned garrison")
			return
		end

		TestHarness.Screenshot("02-garrison-panel-eight-ports",
			"expects, BOTTOM RIGHT: the garrison panel for the selected building, with up " ..
			"to " .. tostring(ExpectedGarrison) .. " firing-position rows. " ..
			"CORRECT = every row's button reads 'Out' (NOT 'X'), and the button at the " ..
			"bottom reads 'Unload All' (NOT 'Eject All'). Each 'Out' sits at the right-hand " ..
			"edge of its row, on the same 220px content line as the header, with its text " ..
			"fully inside the button and not clipped at either side. The port name label to " ..
			"its left may be long — note whether it runs into the button. " ..
			"BROKEN = any button still reading a bare 'X', or 'Out' clipped to 'Ou'/'ut', or " ..
			"a label overlapping a button. " ..
			"MERELY PRESENT = fewer than eight rows. That is NOT a labelling failure — it " ..
			"means GarrisonManager had not finished moving men from the shelter to the " ..
			"ports when the shutter fired, and the rows that ARE there still answer the " ..
			"question. Report the count you see so the settle time can be raised.")
	end)

	Trigger.AfterDelay(DateTime.Seconds(12), function()
		Test.Pass("captured the reflowed command bar and the renamed garrison buttons")
	end)
end
