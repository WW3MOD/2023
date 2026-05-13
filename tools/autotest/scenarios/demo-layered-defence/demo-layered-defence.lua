-- DEMO: Stage B.1 — v2 layered defence positioning
--
-- USA-bot (v2, blue, left) vs Russia-bot (normal, red, right).
-- 4 neutral capturables in the middle band.
--
-- WHAT TO WATCH
--   1. Within ~30 sim-sec the v2 bot has produced its first wave of units
--      — light infantry (e3, ar, at, sn, tl, medi) and heavy units
--      (abrams, bradley, m113, mortar, aa, etc.).
--   2. Type `/frontline` to overlay the contested band.
--   3. As soon as both armies make contact (the band lights up), the
--      LayeredDefenceBotModule kicks in. Watch v2's units split:
--        - Light infantry head TOWARD the contested cells (screen).
--        - Heavies hold a standoff position 6 cells BEHIND the contested
--          cells (main line, toward USA's own SR).
--   4. The normal Russia AI doesn't have this — it'll just mob units
--      forward. Compare visually.
--
-- TIP
--   Use the speed control (+) to fast-forward through the build-up
--   phase. Pause to inspect formations.

WorldLoaded = function()
	-- Frame the camera on the contested middle so both armies and the
	-- neutral capturables are visible.
	TestHarness.FocusBetween(NeutralBio, NeutralFcom, NeutralOilb1, NeutralOilb2)
end
