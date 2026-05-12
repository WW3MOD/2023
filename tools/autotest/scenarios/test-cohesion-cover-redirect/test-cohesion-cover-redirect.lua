-- AUTOTEST: cohesion bidder actively redirects toward cover.
--
-- Click target (22, 15) is 3 cells west of the nearest trunk column. Box
-- formation alone would land every unit on cells at chebyshev ≥ 3 from any
-- trunk — i.e. NOT adjacent. Only the cover-aware slot bidder can pull the
-- squad to cells within chebyshev 1 of a trunk.
--
-- If this test ever passes with the bidder removed, the assertion is too lax
-- or the click distance is too small. Compare against test-cohesion-cover-bid
-- (which clicks inside the cluster where box-formation alone already lands
-- units adjacent — a positive smoke test, not a discrimination test).

local DeadlineSeconds = 30

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

	Test.GroupMove({ A1, A2, A3, A4 }, CPos.New(22, 15))

	Trigger.AfterDelay(50, function()
	TestHarness.AssertWithin(DeadlineSeconds, function()
		for _, unit in ipairs(squad) do
			if unit.IsDead then return "fail: " .. tostring(unit) .. " died" end
			if not unit.IsIdle then return false end
		end

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

		return "fail: " .. #misses .. "/4 units not adjacent to a trunk (box formation " ..
			"alone would land all 4 in open ground — bidder failed to redirect): " ..
			table.concat(misses, "; ")
	end, "squad did not settle near forest within " .. DeadlineSeconds .. "s")
	end)
end
