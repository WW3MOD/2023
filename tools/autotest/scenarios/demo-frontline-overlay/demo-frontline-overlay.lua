-- DEMO: Stage A frontline overlay
--
-- LAYOUT
--   USA (left):     2 cols × 7 rows of Bradleys @ cols 22, 26
--   Russia (right): 2 cols × 7 rows of BMP-2s   @ cols 38, 42
--   Gap: 12 cells between the inner-most columns.
--
-- WHAT TO DO
--   1. Press Enter (or click the chat box) and type:  /frontline
--   2. A band of orange filled circles appears between the two armies —
--      that's the InfluenceMap saying "this is where both sides have
--      influence overlap."
--   3. Move some of your Bradleys forward — the band shifts right.
--   4. Move them back — the band retreats with them.
--   5. Toggle the overlay off by typing /frontline again.
--
-- Units start in HoldFire stance so they don't immediately kill each
-- other while you're inspecting the band. Select a Bradley and
-- right-click toward the BMP line to push and watch the band move.

WorldLoaded = function()
	TestHarness.FocusBetween(U1, U14, R1, R14)
	TestHarness.Select(U7)
end
