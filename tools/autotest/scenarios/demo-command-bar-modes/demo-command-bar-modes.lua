-- DEMO / EYEBALL: does a HELD command-bar mode mark its glyph, not just its panel?
--
-- A button's IsHighlighted does two things. The panel swaps via
-- ButtonWidget.DrawBackground (ButtonWidget.cs:315-320) and always worked. The glyph
-- swaps via WidgetUtils.GetCachedStatefulImage (WidgetUtils.cs:44-54), which appends
-- "-highlighted" to the COLLECTION name, for buttons that opted in through
-- WidgetUtils.BindButtonIcon (WidgetUtils.cs:324-330). The command bar opts in for every
-- button, and command-icons-highlighted is `Inherits: command-icons` with no regions, so
-- override and fallback resolved to the same rectangle and the glyph never changed. The
-- six HELD modes now draw command-mode-icons, whose twin points at amber recolours.
--
-- HISTORY, because it decides what this file may assume: the first version of this demo
-- died on its second frame. Engaging a mode sets World.OrderGenerator, and Test.PressHotkey
-- dispatched Ui.HandleKeyPress bare from inside the synced Lua tick, so Sync.AssertUnsynced
-- threw and shots 02-04 never happened. PressHotkey now wraps the dispatch in
-- Sync.RunUnsynced exactly as DefaultInputHandler.OnKeyInput does (InputHandler.cs:40).
-- That is what makes every line below reachable -- before it, NO autotest could engage any
-- command-bar mode at all.
--
-- Each press records whether a widget consumed it and says so in that shot's note, because
-- an unconsumed press produces a frame that looks exactly like an inert change.
--
-- Not a pass/fail test -- it stages each mode and Skips (exit code 2) once the last capture
-- has flushed.

local TPS = TestHarness.TicksPerSecond

local function consumedNote(ok)
	if ok then
		return " [keypress consumed: yes]"
	end

	return " [keypress consumed: NO -- the mode did not engage, so this frame proves nothing " ..
		"about the glyph either way]"
end

WorldLoaded = function()
	TestHarness.FocusBetween(Rifle1, Apc)
	TestHarness.Select(Rifle1)

	-- Control shot. Nothing engaged, so every command glyph must be its normal grey.
	-- If anything is amber here the recolours landed on the base regions, not the
	-- highlighted twin, and the rest of the run means nothing.
	TestHarness.ScreenshotAfter(2, "01-no-mode-control",
		"expects, bottom-left: the full command bar with a rifleman selected and NO " ..
		"mode engaged. Every glyph grey, every panel dark. This is the baseline the " ..
		"other shots are read against -- any amber in this frame is a bug.")

	-- Left command panel, 8 buttons: Attack Move, Force Move, Force Attack, Guard,
	-- Deploy, Scatter, Stop, Waypoint. Right panel, 4: Resupply, Patrol, Auto-Enter,
	-- Evacuate. Each mode replaces the previous one's order generator, so exactly one
	-- glyph should be amber in each of the shots below.
	local steps = {
		{ key = "AttackMove", label = "02-attack-move",
		  note = "expects: ATTACK_MOVE (1st button, LEFT panel) amber; everything else grey." },
		{ key = "ForceMove", label = "03-force-move",
		  note = "expects: FORCE_MOVE (2nd, LEFT) amber. Reached through the sticky " ..
			"ForceModifiersOrderGenerator its OnClick sets, NOT through live modifier keys -- " ..
			"PressHotkey cannot press Ctrl." },
		{ key = "ForceAttack", label = "04-force-attack",
		  note = "expects: FORCE_ATTACK (3rd, LEFT) amber and FORCE_MOVE grey again. If BOTH " ..
			"are amber the two force modes are not distinguishing their modifier sets." },
		{ key = "Guard", label = "05-guard",
		  note = "expects: GUARD (4th, LEFT) amber, everything else grey." },
		{ key = "Patrol", label = "06-patrol-vs-auto-enter",
		  note = "THE DISCRIMINATING SHOT. PATROL (2nd, RIGHT) and AUTO_ENTER (3rd, RIGHT) sit " ..
			"side by side and draw the IDENTICAL `guard` glyph, but only PATROL is a held mode " ..
			"and only PATROL moved to command-mode-icons. Expects PATROL AMBER, AUTO_ENTER GREY. " ..
			"Both grey = the change is inert. Both amber = the mode/momentary split failed." },
		{ key = "WaypointMode", label = "07-waypoint",
		  note = "expects: WAYPOINT (8th and last, LEFT panel) amber, PATROL grey again." },
	}

	for i, step in ipairs(steps) do
		Trigger.AfterDelay((i + 1) * 4 * TPS, function()
			local ok = Test.PressHotkey(step.key)
			TestHarness.ScreenshotAfter(1.5, step.label, step.note .. consumedNote(ok))
		end)
	end

	Trigger.AfterDelay((#steps + 2) * 4 * TPS, function()
		Test.Skip("eyeball: held command-bar modes mark their glyph, not just their panel")
	end)
end
