-- DEMO: 7 Bradleys force-attack 7 t90s at fixed range. Trees on each
-- firing line ramp from 0 to 6 (top to bottom).
--
-- What you should see (with current ClearSightThreshold = 3):
--   Row 0 (top, 0 trees)  → Bradley fires; missile flies clean to t90
--   Row 1 (1 tree)         → Bradley fires; tree is "free" (FreeLineDensity=1)
--   Row 2 (2 trees)        → Bradley fires; ~15 % per-shot miss chance
--   Row 3 (3 trees)        → Bradley fires; ~30 % per-shot miss chance
--   Row 4 (4 trees)        → Bradley HOLDS — density 4 > threshold 3
--   Row 5 (5 trees)        → Bradley HOLDS
--   Row 6 (bottom, 6 trees)→ Bradley HOLDS
--
-- On a "miss" roll the missile re-targets one of the trees on the line so
-- it visibly clips foliage rather than magically curving through.
--
-- Press End to restart this scenario at any time.

WorldLoaded = function()
	-- Camera to mid-map so all 7 rows visible at once
	TestHarness.FocusBetween(B0, T6)

	-- t90s stay put so the tree-gate is what you're observing
	for _, t in ipairs({ T0, T1, T2, T3, T4, T5, T6 }) do
		t.Stance = "HoldFire"
	end

	-- Force-attack the assigned t90 on each Bradley so each pair holds its row
	-- (otherwise autotarget can pick a neighbour t90 and confuse the demo).
	-- allowMove=false: stay put. forceAttack=false (default attack — gate still applies).
	B0.Attack(T0, false, false)
	B1.Attack(T1, false, false)
	B2.Attack(T2, false, false)
	B3.Attack(T3, false, false)
	B4.Attack(T4, false, false)
	B5.Attack(T5, false, false)
	B6.Attack(T6, false, false)

	-- Pre-select the row-3 (3 trees, will fire) Bradley so you can watch
	-- its missile fly through the trees without clicking.
	TestHarness.Select(B3)
end
