-- CAPTURE SCENARIO: is the cargo panel drawn over by the production sidebar?
--
-- RUN THIS TWICE, at two window sizes. The overlap is arithmetic on WINDOW_HEIGHT, not a
-- property of the build, so a single run answers nothing:
--
--   ./tools/autotest/run-test.sh --size 1024x768  test-visual-cargo-panel   <- should collide
--   ./tools/autotest/run-test.sh --size 1920x1080 test-visual-cargo-panel   <- should not
--
-- CARGO_PANEL is at Y = WINDOW_HEIGHT - 260 with Height 240; SIDEBAR_PRODUCTION is at a
-- fixed Y 300 with Height 250, so its bottom edge is the constant 550. They overlap
-- whenever WINDOW_HEIGHT - 260 < 550, i.e. below 810px of window. At 768 the overlap is
-- 42px — the panel header and its first two manifest rows — and production is declared
-- later in ingame-player.yaml (:1149 vs :797), so production draws on top.
--
-- The qualifier that decides whether this is a defect at all: GARRISON_PANEL has occupied
-- this exact rectangle since before the cargo readout was restored. If a garrison renders
-- acceptably at a short window then cargo will too, and the finding is "pre-existing and
-- accepted" rather than "regression".
--
-- =====================================================================================
-- THE WAY THIS SHOT LIES IF YOU LET IT
-- =====================================================================================
-- An EMPTY production sidebar cannot overlap anything. A capture of a scenario whose
-- player has no buildable units comes back looking clean at 1024x768 and would be read as
-- "no collision" — a false negative manufactured by the setup. So the run asserts it can
-- click a real production icon before it photographs anything, and the first capture is
-- taken with NOTHING selected so the sidebar's own extent is on record independently.

local ProbeIcon = "e3.america"
local ExpectedClasses = 24

WorldLoaded = function()
	Camera.Position = BigTransport.CenterPosition

	Trigger.AfterDelay(DateTime.Seconds(3), function()
		if BigTransport.IsDead then
			Test.Fail("the Chinook died before anything could be photographed")
			return
		end

		-- The hold is built by Cargo InitialUnits. A short count means a name in rules.yaml
		-- did not resolve and the manifest under test was never assembled — the panel would
		-- still draw, and still photograph, with fewer rows.
		local carried = BigTransport.PassengerCount
		if carried ~= ExpectedClasses then
			Test.Fail("the Chinook holds " .. tostring(carried) .. " passengers, expected " ..
				tostring(ExpectedClasses) .. " — one man per class, so the manifest is short " ..
				"and the ten-row layout is not the one being photographed")
			return
		end

		-- Proof the sidebar has something in it. Without this the 1024x768 shot could come
		-- back clean because there was nothing to collide with.
		if not Test.ClickProductionIcon(ProbeIcon) then
			Test.Fail("no build-menu icon for " .. ProbeIcon .. " — the production sidebar " ..
				"is empty, so neither capture can say anything about it overlapping the " ..
				"cargo panel")
			return
		end

		if Test.GetSelectedCount() ~= 0 then
			Test.Fail("something is selected before the control frame; the sidebar-only " ..
				"capture would already contain a cargo panel")
			return
		end
	end)

	-- ---- Control frame: the sidebar alone -------------------------------------------
	Trigger.AfterDelay(DateTime.Seconds(5), function()
		TestHarness.Screenshot("01-sidebar-only-nothing-selected",
			"CONTROL FRAME, no unit selected. expects: the production sidebar down the " ..
			"right-hand side with unit icons in it, at least one showing a build clock. " ..
			"Note where its BOTTOM edge falls — that edge is at a fixed y=550 whatever the " ..
			"window height. There must be NO cargo panel in this frame. If the sidebar is " ..
			"empty or absent, stop: the next capture cannot say anything about an overlap " ..
			"and the run should be reported as inconclusive rather than clean.")
	end)

	Trigger.AfterDelay(DateTime.Seconds(7), function()
		TestHarness.Select(BigTransport)
	end)

	-- ---- The shot ---------------------------------------------------------------------
	Trigger.AfterDelay(DateTime.Seconds(9), function()
		if Test.GetSelectedCount() ~= 1 then
			Test.Fail("selection is " .. tostring(Test.GetSelectedCount()) ..
				" actors, not 1 — CargoPanelLogic only draws for a single owned transport")
			return
		end

		TestHarness.Screenshot("02-cargo-panel-vs-sidebar",
			"expects, BOTTOM RIGHT: the cargo panel for the selected Chinook — a header, " ..
			"then TEN manifest rows, each a class name on the left with a right-aligned " ..
			"count. Nine are named classes and the tenth reads '+15 more' with 'x15'. " ..
			"Below them two buttons labelled 'Unload All Troops' and 'Drop All Supply'. " ..
			"CORRECT = all ten rows legible, no production icon art drawn across any of " ..
			"them, and both button labels readable without clipping. " ..
			"BROKEN = the top of the panel — header and first rows — is covered by unit " ..
			"icons from the production sidebar above it. " ..
			"MERELY PRESENT = the panel is there but you cannot count ten rows, or rows are " ..
			"blank, or it stops after nine names as if that were the whole hold. " ..
			"Compare against the control frame: the sidebar's bottom edge does not move " ..
			"between the two, so anything of the panel above that line is what is at risk.")
	end)

	Trigger.AfterDelay(DateTime.Seconds(11), function()
		Test.Pass("captured the sidebar alone and the cargo panel beneath it")
	end)
end
