-- AUTO TEST: Ctrl+Alt+LMB on a build-menu icon selects the player's units of that type.
--
-- The gesture is a mouse event on a sidebar icon, so it is driven here through
-- Test.ClickProductionIcon, which routes into the real ProductionPaletteWidget click
-- handler (modifier tiers included). Only SDL modifier decode and icon hit-testing
-- are bypassed.
--
-- IMPORTANT — the screenshots here cannot show the selection, and that is a property of
-- the mod, not of this test. Own units render white corner brackets and pips whether or
-- not they are selected: a full-frame pixel diff of beats 1 and 2 shows ZERO changed
-- pixels on the units (the only world-area change is the Supply Route flag animation).
-- Infantry are worse still — ^Infantry sets SelectionDecorations ShowNever, so the box
-- is explicitly skipped. Bradleys are used anyway because they carry the fewest always-on
-- decorations, but the real verification is the state assertions below, not the captures.
-- See WORKSPACE/DISCOVERIES.md 2026-08-11.
--
-- The map holds 7 Bradleys — 4 by the Supply Route inside the camera view and 3 at the
-- far east edge outside it — plus 2 Abrams next to the west group that must never be
-- caught up in the selection.
--
-- Beats:
--   BEFORE-nothing-selected                          — nothing selected
--   AFTER-ctrl-alt-click-selects-4-onscreen-bradleys — first click: the 4 on screen
--   AFTER-second-click-escalates-to-all-7-bradleys   — repeat click: all 7 map-wide
--   AFTER-click-on-unowned-type-selection-survives   — the no-op guard holds

local Target = "bradley"
local DecoyType = "abrams"
local UnownedType = "m109"

local Caption = function(text)
	UserInterface.SetMissionText(text)
end

WorldLoaded = function()
	-- Zoom in so the east group is unambiguously outside the view on any window size.
	Test.SetZoom(2)
	TestHarness.FocusBetween(WestA, DecoyB)

	Caption("BEFORE: nothing selected. 4 Bradleys on screen, 3 more off-screen east, 2 Abrams below.")

	Trigger.AfterDelay(25, function()
		if Test.GetSelectedCount() ~= 0 then
			Test.Fail("expected an empty selection at start, got " .. Test.GetSelectedCount())
			return
		end

		TestHarness.Screenshot("select-by-type-BEFORE-nothing-selected",
			"expects: 4 Bradleys and 2 Abrams near the Supply Route. NOTE: brackets are always-on in this mod, so this frame looks the same as the next one — see the caption")

		-- First click: on-screen scope.
		if not Test.ClickProductionIcon(Target, "Ctrl Alt") then
			Test.Fail("Ctrl+Alt+click found no build-menu icon for " .. Target)
			return
		end

		local onScreen = Test.GetSelectedCount()
		if onScreen ~= 4 then
			Test.Fail("first Ctrl+Alt+click should select the 4 Bradleys on screen, got " .. onScreen)
			return
		end

		if Test.GetSelectedCountOfType(DecoyType) ~= 0 then
			Test.Fail("the Abrams must not be selected by a click on the Bradley icon")
			return
		end

		if Test.GetSelectedCountOfType(Target) ~= 4 then
			Test.Fail("every selected unit should be a Bradley, got "
				.. Test.GetSelectedCountOfType(Target) .. " of 4")
			return
		end

		Caption("AFTER 1st Ctrl+Alt+LMB on the Bradley icon: 4 on-screen Bradleys selected, Abrams untouched.")

		Trigger.AfterDelay(25, function()
			TestHarness.Screenshot("select-by-type-AFTER-ctrl-alt-click-selects-4-onscreen-bradleys",
				"expects: caption confirms 4 selected; the bottom command bar lights up. The units themselves are pixel-identical to beat 1 (see file header)")

			-- Second click: escalates to the whole map.
			Test.ClickProductionIcon(Target, "Ctrl Alt")

			local acrossMap = Test.GetSelectedCount()
			if acrossMap ~= 7 then
				Test.Fail("second Ctrl+Alt+click should escalate to all 7 Bradleys, got " .. acrossMap)
				return
			end

			if Test.GetSelectedCountOfType(DecoyType) ~= 0 then
				Test.Fail("the Abrams must not be selected after escalating to the whole map")
				return
			end

			Caption("AFTER 2nd Ctrl+Alt+LMB: escalated to all 7 Bradleys map-wide (3 are off-screen east).")

			Trigger.AfterDelay(25, function()
				TestHarness.Screenshot("select-by-type-AFTER-second-click-escalates-to-all-7-bradleys",
					"expects: caption says all 7 selected map-wide; the 3 that joined are off-screen east so the frame is unchanged")

				-- The mis-click guard: a type the player owns none of must not clear the
				-- selection. The icon has to exist or this beat proves nothing, hence the
				-- handled check.
				if not Test.ClickProductionIcon(UnownedType, "Ctrl Alt") then
					Test.Fail(UnownedType .. " has no build-menu icon, so the no-op guard was never exercised")
					return
				end

				local afterNoop = Test.GetSelectedCount()
				if afterNoop ~= 7 then
					Test.Fail("clicking a type owned none of must leave the selection intact, got " .. afterNoop)
					return
				end

				Caption("AFTER Ctrl+Alt+LMB on a type owned none of: selection unchanged, still 7 Bradleys.")

				Trigger.AfterDelay(25, function()
					TestHarness.Screenshot("select-by-type-AFTER-click-on-unowned-type-selection-survives",
						"expects: caption says the selection is unchanged at 7 — the empty-type click cleared nothing")

					Test.Pass("select-by-type: 4 on screen, escalated to 7 map-wide, Abrams excluded, no-op guard held")
				end)
			end)
		end)
	end)
end
