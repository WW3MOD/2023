-- test-minelayer-mode-survives-modifiers.lua
--
-- MANUAL. Staging only, no AssertWithin: the defect is triggered by a keyboard event arriving while
-- Ctrl+Alt are held, and there is no Lua binding that can synthesise one. An auto-assert here could
-- not fail for the right reason, so it is deliberately absent rather than decorative.
--
-- What to do, and what separates the two builds:
--   1. The engineer is already selected. Hold Ctrl+Alt.
--   2. Right-click bare ground a few cells away. The tiled minefield overlay appears.
--   3. KEEP HOLDING Ctrl+Alt and wait ~2 seconds, then press any key.
--        broken -> the overlay disappears on its own, cursor returns to normal
--        fixed  -> the overlay stays up, tracking the mouse, until you commit or cancel
--   4. Right-click again to commit; the engineer should walk the marked line laying mines.
--      Left-click instead to cancel; that cancel is intended and is not the bug.

WorldLoaded = function()
	TestHarness.Select(Layer)
	TestHarness.FocusBetween(Layer, Layer)

	-- Report the charge count so a "nothing was laid" verdict can be told apart from
	-- "the selector worked but the engineer had no mines to place".
	Trigger.AfterDelay(25, function()
		print("[minelayer] secondary-ammo at start = " .. tostring(Layer.AmmoCount("secondary-ammo")))
	end)
end
