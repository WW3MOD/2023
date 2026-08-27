-- DEMO: the resupply stance bar after the command-icons swap.
--
-- No verdict. Nothing here asserts, and there is deliberately no Test.Pass / Test.Fail — a demo
-- stages a thing to look at and stops, and the user closes the window (DEMO.md).
--
-- WHY THIS EXISTS AT ALL: the resupply bar has never rendered under any gate in this branch.
-- `--dump-balance-json` loads rules, not chrome/, and NUnit does not draw. Every claim about
-- these three buttons so far — that `stop`/`resupply`/`force-move` read as distinct, that a
-- 24x24 icon centres in a 34x26 button at X:5,Y:1, that the selected stance is still legible now
-- that only the button background carries the highlight — is arithmetic off the YAML.
--
-- ONE FRAME PER STANCE, driven through the real widget chain. Test.PressHotkey dispatches via
-- Ui.HandleKeyPress, so it walks the same path a player's keypress does rather than poking the
-- trait directly: the three buttons declare Key: ResupplyHold / ResupplyAuto / ResupplyEvacuate
-- (ingame-player.yaml) and those are bound at engine/mods/common/hotkeys/game.yaml:147/152/157.
-- That means these captures also exercise the highlight path, not just the idle icons.
--
-- The iskander is the subject because ResupplyBehaviorSelectorLogic only populates the bar for
-- selected actors owned by the local player that carry an AmmoPool or a SupplyProvider.
--
-- FIRST CAPTURE IS DELAYED ON PURPOSE. A shot fired from WorldLoaded can land blank because no
-- frame has been rendered yet (SCREENSHOT.md). Every capture below sits behind a delay, and the
-- tell for a blank frame is FILE SIZE, not the image — ~59 KB is a black frame, a real one is
-- megabytes.

local function ticks(t) return t end -- delays here are already in ticks; named for intent

WorldLoaded = function()
	-- FocusBetween is the only centring helper (there is no TestHarness.Focus); passing the same
	-- actor twice centres on it.
	TestHarness.FocusBetween(Launcher, Launcher)
	TestHarness.Select(Launcher)

	-- Shipped default: defaults.yaml gives every unit InitialResupplyBehavior: Auto, so the
	-- middle button starts highlighted with no input from us.
	Trigger.AfterDelay(ticks(40), function()
		TestHarness.Screenshot("01-auto-default",
			"expects: resupply bar bottom-right of the stance row; MIDDLE button highlighted; "
			.. "three DISTINCT glyphs left-to-right (stop / resupply / force-move), none of them "
			.. "repeating the four stance-icons glyphs used by the three bars to its left")
	end)

	Trigger.AfterDelay(ticks(80), function() Test.PressHotkey("ResupplyHold") end)
	Trigger.AfterDelay(ticks(110), function()
		TestHarness.Screenshot("02-hold",
			"expects: LEFT button now highlighted and the middle one not; the left glyph is a "
			.. "stop symbol, NOT the guard shield the Guard/Patrol/Auto-Enter command buttons use")
	end)

	Trigger.AfterDelay(ticks(150), function() Test.PressHotkey("ResupplyEvacuate") end)
	Trigger.AfterDelay(ticks(180), function()
		TestHarness.Screenshot("03-evacuate",
			"expects: RIGHT button now highlighted and the other two not — this is the frame that "
			.. "shows whether the highlight is legible at all, since command-icons-highlighted "
			.. "carries no brighter glyph variant and only the button background changes")
	end)
end
