-- AUTO TEST: 7 Bradley/t90 pairs, each at a different tree count on its
-- firing line (0..6). Force-attacks all 7 simultaneously. After the deadline
-- we check each Bradley's secondary-ammo: < 8 means it fired, == 8 means
-- the gate denied.
--
-- Expected with the current tuning (ClearSightThreshold = 3, FreeLineDensity = 1):
--   trees 0..3  → fired (density 0..3 ≤ threshold 3)
--   trees 4..6  → did NOT fire (density 4..6 > threshold 3)
--
-- The 8s window covers AimingDelay (50 ticks ≈ 2s) + first burst, so a
-- firing Bradley should drop at least 1 missile.

local DeadlineSeconds = 8

local pairs_data = {
	{ b = B0, t = T0, trees = 0 },
	{ b = B1, t = T1, trees = 1 },
	{ b = B2, t = T2, trees = 2 },
	{ b = B3, t = T3, trees = 3 },
	{ b = B4, t = T4, trees = 4 },
	{ b = B5, t = T5, trees = 5 },
	{ b = B6, t = T6, trees = 6 },
}

WorldLoaded = function()
	TestHarness.FocusBetween(B0, T6)
	TestHarness.Select(B3)

	-- Targets stand still. Bradleys force-attack their assigned t90 so each
	-- sticks to its row regardless of cross-row autotarget temptation.
	for _, p in ipairs(pairs_data) do
		p.t.Stance = "HoldFire"
		-- allowMove=false: stay put. forceAttack=false (default attack).
		p.b.Attack(p.t, false, false)
	end

	local ticks = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)
	Trigger.AfterDelay(ticks, function()
		local fails = {}
		local report = {}
		for _, p in ipairs(pairs_data) do
			if p.b.IsDead then
				table.insert(fails, p.trees .. "t: Bradley died (inconclusive)")
			else
				local ammo = p.b.AmmoCount("secondary-ammo")
				local fired = ammo < 8
				local expected_fire = p.trees <= 3
				table.insert(report, p.trees .. "t=" .. (fired and "F" or "-") .. "(a" .. ammo .. ")")
				if fired ~= expected_fire then
					table.insert(fails, p.trees .. "t expected " .. (expected_fire and "FIRE" or "DENY") .. " got ammo " .. ammo)
				end
			end
		end
		local summary = "ladder: " .. table.concat(report, " ")
		if #fails > 0 then
			Test.Fail(table.concat(fails, "; ") .. " | " .. summary)
		else
			Test.Pass()
		end
	end)
end
