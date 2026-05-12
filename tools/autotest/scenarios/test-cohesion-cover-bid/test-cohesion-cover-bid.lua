-- AUTOTEST: cohesion cover-aware slot bidder.
--
-- Forest cluster (6 t01 trees) sits at columns 25 & 27, rows 13/15/17 — a sparse
-- 3x3 dotted pattern that leaves passable cells between every tree. Riflemen
-- start at (10, 14..17) and receive a group-move targeting (26, 15), the dead
-- center of the cluster.
--
-- Without the cover-aware bidder, the legacy box formation centers ~4 cells
-- around (26, 15) and units end up in open ground beyond the trees. With the
-- bidder, each unit's box slot redirects toward a high-CoverScore cell — a
-- passable cell adjacent to a tree — so every unit settles inside the cluster.

local DeadlineSeconds = 30

-- t01 has Footprint `__ x_` Dimensions 2x2 with Density `0,0, 10,0` — the
-- trunk (density cell) sits at row 1 col 0 of the footprint, i.e. (Location.X,
-- Location.Y + 1). For our 6 placements at (25/27, 13/15/17) that gives trunk
-- cells (25,14), (27,14), (25,16), (27,16), (25,18), (27,18).
local TrunkCells = {
	{ x = 25, y = 14 },
	{ x = 27, y = 14 },
	{ x = 25, y = 16 },
	{ x = 27, y = 16 },
	{ x = 25, y = 18 },
	{ x = 27, y = 18 },
}

local function chebyshev(ax, ay, bx, by)
	local dx = ax - bx
	if dx < 0 then dx = -dx end
	local dy = ay - by
	if dy < 0 then dy = -dy end
	if dx > dy then return dx end
	return dy
end

local function adjacentToTrunk(loc)
	for _, trunk in ipairs(TrunkCells) do
		if chebyshev(loc.X, loc.Y, trunk.x, trunk.y) <= 1 then
			return true
		end
	end
	return false
end

WorldLoaded = function()
	local squad = { A1, A2, A3, A4 }

	TestHarness.FocusBetween(A1, A2, A3, A4)
	for _, unit in ipairs(squad) do
		UserInterface.Select(unit)
		break
	end

	-- Select the whole squad
	local toSelect = {}
	for _, unit in ipairs(squad) do
		toSelect[#toSelect + 1] = unit
	end

	-- Issue a REAL grouped Move order via Test.GroupMove. unit.Move() queues a Move
	-- activity directly and bypasses the order pipeline, so it would miss the
	-- IModifyGroupOrder dispatch we need to exercise.
	Test.GroupMove({ A1, A2, A3, A4 }, CPos.New(26, 15))

	-- Give the order a few ticks to propagate through the net layer before we
	-- start polling for settled state. Without this, "all units idle" can be true
	-- at tick 1 (before they receive the order) and we'd false-fail immediately.
	Trigger.AfterDelay(50, function()
	TestHarness.AssertWithin(DeadlineSeconds, function()
		for _, unit in ipairs(squad) do
			if unit.IsDead then return "fail: " .. tostring(unit) .. " died" end
			if not unit.IsIdle then return false end
		end

		-- All units settled. Check each one is adjacent to a tree trunk.
		local misses = {}
		for _, unit in ipairs(squad) do
			if not adjacentToTrunk(unit.Location) then
				misses[#misses + 1] = string.format("%s at (%d,%d)", tostring(unit),
					unit.Location.X, unit.Location.Y)
			end
		end

		if #misses == 0 then
			return true
		end

		return "fail: " .. #misses .. "/4 units not adjacent to a tree: " ..
			table.concat(misses, "; ")
	end, "squad did not settle near forest within " .. DeadlineSeconds .. "s")
	end)
end
