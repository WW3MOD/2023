-- DEMO / EYEBALL: the command bar after Button@TAKE_COVER was removed.
--
-- TAKE_COVER sat at container-X 358, in the middle of the right-hand panel (CMD_BG_B).
-- Removing it pulled AUTO_ENTER and EVACUATE left by 34px, narrowed CMD_BG_B and
-- COMMAND_BAR by 34, and shifted all four right-hand panels and their stance bars
-- left by 34. Every one of those numbers is a literal in ingame-player.yaml with no
-- expression tying it to its neighbour, so the only way to know the bar still lines
-- up is to look at it.
--
-- Selecting infantry is the point: TAKE_COVER's disabled state keyed off
-- InfantryStatesInfo, so infantry-selected is exactly the state in which it used to
-- render ungreyed. If a hole was left where it stood, this is the shot that shows it.
--
-- Not a pass/fail test — it stages the bar and Skips (exit code 2) once the capture
-- has flushed.

WorldLoaded = function()
	TestHarness.FocusBetween(Rifle1, Apc)
	TestHarness.Select(Rifle1)

	-- t=2s: give the HUD a beat to settle. A capture fired in WorldLoaded can land
	-- before any frame has rendered.
	TestHarness.ScreenshotAfter(2, "01-infantry-selected",
		"expects, bottom-left: TWO command panels then four stance panels, all flush " ..
		"with no gap and no overlap. Left panel = 8 buttons. RIGHT panel = exactly " ..
		"FOUR buttons (resupply, patrol, auto-enter, evacuate) with equal margins at " ..
		"both ends -- NOT five, and no empty slot or dead space where a fifth was. " ..
		"No 'Take Cover' button anywhere. The four stance bars to the right sit " ..
		"inside their own panels with the same margin as each other.")

	Trigger.AfterDelay(100, function()
		Test.Skip("eyeball: command bar reflow after TAKE_COVER removal")
	end)
end
