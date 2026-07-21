-- AUTOTEST: CohesionMoveModifier count-aware footprint cap (Phase 0).
--
-- Regression guard for the "units spread out way too much" bug. A 24-unit squad
-- in SPREAD cohesion is issued a single grouped Move to an open (cover-free)
-- cell, so CohesionMoveModifier takes the Open box path (ComputeBoxSlots).
--
-- Before the fix, ComputeBoxSlots offsets scaled unbounded with spacing x count:
-- a 24-unit Spread box spans ~18 cells wide x ~7.5 deep -> a ~19-20 cell diagonal,
-- a map-spanning scatter line. The count-aware cap shrinks per-slot spacing so the
-- Spread box stays ~13 x ~7 cells (~15 cell diagonal).
--
-- Two assertions:
--   1) Assigned slots (pathing-independent, computed at order time) fit under the
--      cap. This is the RED lever: old slots span ~19.5 cells and fail CapCells.
--   2) After the units march to their slots, their ACTUAL positions also close up
--      under the cap (regroup-on-arrival via the bounded slots + CohesionSlotMemory
--      leash), not a strung-out line.

local CapCells = 17          -- max allowed pairwise Euclidean distance (cells)
local ArrivalDeadline = 22   -- seconds to let the squad reach the bounded box

local squad = {}

local function collectSquad()
	local names = {
		U01, U02, U03, U04, U05, U06, U07, U08, U09, U10, U11, U12,
		U13, U14, U15, U16, U17, U18, U19, U20, U21, U22, U23, U24,
	}
	for _, u in ipairs(names) do
		squad[#squad + 1] = u
	end
end

-- Max pairwise Euclidean distance (in cells) over a list of {x=, y=} points.
local function maxPairwise(points)
	local worst = 0
	for i = 1, #points - 1 do
		for j = i + 1, #points do
			local dx = points[i].x - points[j].x
			local dy = points[i].y - points[j].y
			local d = math.sqrt(dx * dx + dy * dy)
			if d > worst then worst = d end
		end
	end
	return worst
end

WorldLoaded = function()
	collectSquad()

	for _, u in ipairs(squad) do
		Test.SetCohesion(u, "Spread")
	end

	TestHarness.FocusBetween(U01, U24)

	-- Grouped Move to an open, cover-free cell -> Open intent -> box formation.
	Test.GroupMove(squad, CPos.New(34, 20))

	-- Assertion 1: assigned slots must fit under the cap (RED lever).
	Trigger.AfterDelay(12, function()
		local slots = {}
		for _, u in ipairs(squad) do
			local s = Test.GetCohesionSlot(u)
			if s.X == 0 and s.Y == 0 then
				Test.Fail(string.format("%s has no assigned slot", tostring(u)))
				return
			end
			slots[#slots + 1] = { x = s.X, y = s.Y }
		end

		local slotSpan = maxPairwise(slots)
		print(string.format("[extent-cap] slot span = %.1f cells (cap %d)", slotSpan, CapCells))
		if slotSpan > CapCells then
			Test.Fail(string.format("assigned slots span %.1f cells > cap %d (formation not bounded)",
				slotSpan, CapCells))
			return
		end
	end)

	-- Assertion 2: after marching, actual positions close up under the cap too.
	TestHarness.AssertAfter(ArrivalDeadline, function()
		local pts = {}
		for _, u in ipairs(squad) do
			if not u.IsDead and u.IsInWorld then
				local loc = u.Location
				pts[#pts + 1] = { x = loc.X, y = loc.Y }
			end
		end

		local span = maxPairwise(pts)
		print(string.format("[extent-cap] arrival span = %.1f cells (cap %d)", span, CapCells))
		return span <= CapCells
	end, "squad did not close up under the extent cap after arrival (spread beyond bounded box)")
end
