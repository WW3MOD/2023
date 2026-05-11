-- AUTO TEST: Group Scatter (Shift-G) must preserve per-unit prefix waypoints
-- (an earlier spread's per-unit results) and only redistribute the trailing
-- group orders that came AFTER the prefix.
--
-- Setup mirrors a real-game flow:
--   1. User spreads N moves across N units → each unit ends up with one
--      unique Move target. (Mocked: TankA gets Move WAi, TankB gets WBi.)
--   2. User group-queues N AttackMove orders on the whole selection → both
--      units now have the SAME pair of AMs queued behind their unique
--      prefix Move. (Mocked: both tanks get AM WX, AM WY queued.)
--   3. User presses Shift-G again expecting the AMs to spread without
--      touching the prefix Moves.
--
-- Pre-fix bug: the old logic aggregated every (Cell, OrderType) waypoint
-- into one global pool, BuildSegments split it into Move/AM/Move segments,
-- and the distributor broadcast the lone Move segments to every unit. End
-- state: TankA's chain holds [Move WAi, AM picked, Move WBi] — TankA ends
-- up at TankB's prefix cell instead of at its own AM. The "first orders
-- get removed and added after the attack-move" symptom the user described.
--
-- Post-fix: PerformGroupScatter computes the longest common suffix across
-- participants and only redistributes that. The unique prefixes are
-- preserved by Stop-then-re-issue. Final chains:
--   TankA: [Move WAi, AM closer-to-A]
--   TankB: [Move WBi, AM closer-to-B]
--
-- Predicate: TankA settles north (y stays small, near WAi.Y); TankB settles
-- south (y stays large, near WBi.Y). Pre-fix: TankA's last destination is
-- TankB's prefix cell — TankA ends with y >> 12, well into TankB territory.

local PrefixA = CPos.New(8, 10)   -- unique prefix for TankA (north-west)
local PrefixB = CPos.New(8, 14)   -- unique prefix for TankB (south-west)
local SharedAM1 = CPos.New(20, 11) -- group AM closer to TankA's row
local SharedAM2 = CPos.New(20, 13) -- group AM closer to TankB's row

WorldLoaded = function()
	TestHarness.FocusBetween(TankA, TankB)

	-- Phase 1: each tank gets its unique prefix Move (mimics post-1st-spread).
	-- Phase 2: both tanks get the same two AttackMoves (mimics group-order).
	-- Synchronously, so all four activities land in the chains before we
	-- inspect them in PerformGroupScatter (no ticks elapse between queue and
	-- spread, so destinations stay pristine).
	TankA.Move(PrefixA)
	TankB.Move(PrefixB)

	TankA.AttackMove(SharedAM1)
	TankA.AttackMove(SharedAM2)
	TankB.AttackMove(SharedAM1)
	TankB.AttackMove(SharedAM2)

	Test.GroupScatter({ TankA, TankB })

	-- Wait long enough for both scenarios to settle. Post-fix path:
	-- (start) → unique prefix → assigned AM (~14 cells, ~14 s for abrams).
	-- Pre-fix path: (start) → broadcast Move(PrefixA) → assigned AM →
	-- broadcast Move(PrefixB) (~26 cells, ~26 s). At +35 s, post-fix tanks
	-- are idle at (20, 11) and (20, 13); pre-fix tanks are at (8, 14)
	-- (the LAST broadcast Move target — and notably TankA never settles at
	-- its own AM, it walks past then doubles back to TankB's prefix).
	TestHarness.AssertAfter(35, function()
		if TankA.IsDead or TankB.IsDead then return false end

		-- Post-fix expectation: both tanks settled at the east AM column
		-- with TankA in the northern lane (Y near 11) and TankB in the
		-- southern lane (Y near 13). Pre-fix lands them BOTH at (8, 14) —
		-- TankB's prefix cell — having never settled at an AM.
		return TankA.Location.X >= 18
			and TankB.Location.X >= 18
			and TankA.Location.Y <= 12
			and TankB.Location.Y >= 12
	end, "fail: tanks did not settle at their assigned AMs with prefixes preserved (X>=18, TankA.Y<=12, TankB.Y>=12) — spread re-mixed prefix Moves with the AM suffix")
end
