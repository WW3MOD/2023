-- DEMO: try the new rally modifiers.
--
-- Steps:
--   1. Select the Supply Route (it's pre-selected for you).
--   2. Click the world (no modifier)        → Move waypoint, player-color line.
--      Alt+click the world                  → AttackMove waypoint, orange line.
--      Ctrl+click the world                 → ForceMove waypoint, cyan line.
--      Shift+(any of the above)              → queues to the existing path.
--   3. Queue an Abrams from the production sidebar (or use Test.QueueProduction
--      from the chat / Lua).
--   4. Watch the produced unit honor the per-segment order type:
--        Move segment   → walks past enemies.
--        AttackMove     → engages on the way.
--        ForceMove      → straight-line drive, no reversing.
--   5. Compare with the pre-spawned OwnTank: select it and use the same
--      modifier-clicks. Same colors, same behavior — that's the point.
--
-- Three decoy t90s sit along the most natural rally paths (east / north /
-- south of the SR). They're pinned to HoldFire so they don't return fire
-- and skew the comparison.

WorldLoaded = function()
	TestHarness.FocusBetween(OwnSR, DecoyEast)
	TestHarness.Select(OwnSR)

	DecoyEast.Stance = "HoldFire"
	DecoyNorth.Stance = "HoldFire"
	DecoySouth.Stance = "HoldFire"
end
