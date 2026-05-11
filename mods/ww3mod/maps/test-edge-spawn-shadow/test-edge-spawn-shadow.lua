-- AUTO TEST: Shadow-layer attenuation must work for units placed at the
-- map boundary. Reproduces the symptom that "newly-spawned units see through
-- trees for a short time" — the user's hypothesis was that the unit is not
-- yet inside the area that the shadow layer covers.
--
-- Setup:
--   Scout (e1.america) at CPos(33, 33) — bottom-most playable cell on a
--   66x34 map with Bounds=MapSize (no off-bounds buffer, mirrors river-zeta).
--   Tree (t01) at Location 33,30 → trunk at cell (33, 31).
--   Russia rifleman at CPos(33, 25), 8 cells north of scout.
--
-- Expected without bug:
--   GetVisibility(USA, RussiaCell) significantly reduced by the tree's
--   shadow density (typical t01 density attenuates 4-8 levels).
--
-- Failure mode (bug present):
--   GetVisibility(USA, RussiaCell) at full 8 — shadow lookup was bypassed
--   for the scout's selfLocation, so the obstacle didn't count.

local USA = nil
-- Both cells are 8 cells from the scout (Vision@8 band, strength 8 without
-- shadow). TargetCell is directly north of the scout with a tree on the
-- firing line. ClearCell is 8 cells west on the scout's row — same distance,
-- no obstacles. The comparison isolates the shadow term from range falloff.
local TargetCell = CPos.New(33, 25)  -- 8c north — tree at (33, 31) in between
local ClearCell = CPos.New(25, 33)   -- 8c west — clear line of sight

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	TestHarness.FocusBetween(Scout, Target)
	TestHarness.Select(Scout)

	-- Give Vision traits a couple ticks to register their AddSource calls.
	Trigger.AfterDelay(3, function()
		if Scout.IsDead then
			Test.Fail("scout died before assertion — map setup wrong?")
			return
		end

		local visBehindTree = Test.GetVisibility(USA, TargetCell)
		local visClear = Test.GetVisibility(USA, ClearCell)

		-- Sanity check: the clear cell must be visible at all (else our test
		-- setup is broken — e.g. fog isn't on, or scout has no vision).
		if visClear == 0 then
			Test.Fail("clear cell at (40,33) reports visibility=0 — fog/vision setup broken")
			return
		end

		-- Sanity check: target cell must be revealed at SOME strength
		-- (visBehindTree > 0) — if 0, the test cell isn't in vision range.
		if visBehindTree == 0 then
			Test.Fail(string.format(
				"target cell (33,25) reports visibility=0 — not in scout vision range? clear=%d",
				visClear))
			return
		end

		-- THE ASSERTION: visibility behind the tree must be LOWER than
		-- visibility of a same-distance unobstructed cell. If the bug is
		-- present, shadow is bypassed and visBehindTree == visClear (both
		-- at full strength).
		if visBehindTree >= visClear then
			Test.Fail(string.format(
				"shadow not attenuated: cell behind tree vis=%d, clear cell vis=%d — shadow lookup bypassed?",
				visBehindTree, visClear))
			return
		end

		Test.Pass(string.format("ok: behind-tree=%d clear=%d", visBehindTree, visClear))
	end)
end
