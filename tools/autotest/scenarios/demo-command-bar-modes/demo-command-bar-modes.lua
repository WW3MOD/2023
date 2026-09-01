-- DEMO / EYEBALL: does a HELD command-bar mode mark its glyph, not just its panel?
--
-- A button's IsHighlighted does two things. The panel swaps via
-- ButtonWidget.DrawBackground (ButtonWidget.cs:315-320) and always worked. The glyph
-- swaps via WidgetUtils.GetCachedStatefulImage (WidgetUtils.cs:44-54), which appends
-- "-highlighted" to the COLLECTION name -- but only for buttons that opted in through
-- WidgetUtils.BindButtonIcon (WidgetUtils.cs:324-330). The command bar does opt in for
-- every button, and command-icons-highlighted is `Inherits: command-icons` with no
-- regions, so the override and the fallback resolved to the same rectangle and the
-- glyph never changed. wt/cmdbar-highlight moves the six HELD modes onto a
-- command-mode-icons collection whose -highlighted twin points at amber recolours.
--
-- The fourth shot is the one that matters. PATROL (X 324) and AUTO_ENTER (X 358) are
-- adjacent buttons drawing the SAME `guard` glyph, but PATROL is a held mode and
-- AUTO_ENTER is a momentary flash. Only PATROL moved collections, so with Patrol
-- engaged the two must differ: amber on the left, grey on the right. If they match,
-- either the change is inert (both grey) or the collection split failed (both amber).
--
-- Not a pass/fail test -- it stages each mode and Skips (exit code 2) once the last
-- capture has flushed.

local TPS = TestHarness.TicksPerSecond

WorldLoaded = function()
	TestHarness.FocusBetween(Rifle1, Apc)
	TestHarness.Select(Rifle1)

	-- Control shot. Nothing engaged, so every command glyph must be its normal grey.
	-- If anything is amber here the recolours landed on the base regions, not the
	-- highlighted twin, and the rest of the run means nothing.
	TestHarness.ScreenshotAfter(2, "01-no-mode-control",
		"expects, bottom-left: the full command bar with a rifleman selected and NO " ..
		"mode engaged. Every glyph grey, every panel dark. This is the baseline the " ..
		"other three shots are read against -- any amber in this frame is a bug.")

	Trigger.AfterDelay(4 * TPS, function()
		Test.PressHotkey("AttackMove")
		TestHarness.ScreenshotAfter(1.5, "02-attack-move",
			"expects: ATTACK_MOVE (leftmost button of the left command panel) drawn " ..
			"with an AMBER glyph on the lighter highlighted panel. Every other glyph " ..
			"on the bar stays grey. Before this branch the panel lightened but the " ..
			"glyph stayed grey -- that is what an inert change looks like here.")
	end)

	Trigger.AfterDelay(8 * TPS, function()
		Test.PressHotkey("Guard")
		TestHarness.ScreenshotAfter(1.5, "03-guard",
			"expects: GUARD (4th button, left panel) amber; ATTACK_MOVE back to grey " ..
			"because setting the guard order generator replaces the attack-move one. " ..
			"Two amber glyphs at once would mean the modes are not mutually exclusive.")
	end)

	Trigger.AfterDelay(12 * TPS, function()
		Test.PressHotkey("Patrol")
		TestHarness.ScreenshotAfter(1.5, "04-patrol-vs-auto-enter",
			"THE DISCRIMINATING SHOT. PATROL and AUTO_ENTER sit side by side in the " ..
			"RIGHT command panel and draw the identical `guard` glyph. Expects PATROL " ..
			"AMBER and AUTO_ENTER still GREY. Both grey = the change is inert. Both " ..
			"amber = the mode/momentary split failed and command-icons got recoloured " ..
			"wholesale. GUARD (left panel) should also be grey again by now.")
	end)

	Trigger.AfterDelay(16 * TPS, function()
		Test.Skip("eyeball: held command-bar modes mark their glyph, not just their panel")
	end)
end
