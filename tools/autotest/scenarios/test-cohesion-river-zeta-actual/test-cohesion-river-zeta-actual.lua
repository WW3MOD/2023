-- DIAGNOSTIC: probe DensityLayer + cohesion classifier on the real
-- river-zeta-ww3 map.
--
-- Spawn at (17, 33-36). Dense forest cluster is at (20-30, 30-39) per
-- map.yaml analysis. We probe density at known tree locations from the
-- real map and verify the layer is populated, then issue grouped moves
-- to see what intent the classifier picks on real terrain.

WorldLoaded = function()
	TestHarness.FocusBetween(A1, A2, A3, A4)

	-- Probe density at a sampling of cells. Trees from the real map.yaml
	-- around (20-30, 30-39).
	print("[probe] === density layer at real river-zeta cells ===")
	local probes = {
		{ x = 24, y = 44, label = "Actor107-t10-loc" },     -- first tree in map.yaml
		{ x = 29, y = 48, label = "Actor122-t13-loc" },
		{ x = 22, y = 32, label = "in-dense-cluster" },
		{ x = 25, y = 35, label = "in-dense-cluster-2" },
		{ x = 28, y = 38, label = "in-dense-cluster-3" },
		{ x = 17, y = 33, label = "spawn-area" },
		{ x = 18, y = 34, label = "1-cell-east-of-spawn" },
		{ x = 50, y = 50, label = "map-middle-ish" },
		{ x = 70, y = 70, label = "map-far-side" },
	}
	local hasAnyDensity = false
	for _, p in ipairs(probes) do
		local d = Test.GetDensity(CPos.New(p.x, p.y))
		print(string.format("[probe]   (%d,%d) %s: density=%d", p.x, p.y, p.label, d))
		if d > 0 then hasAnyDensity = true end
	end

	-- Also probe a small neighborhood around (25,35) to find trunk cells
	print("[probe] === 5x5 density map around (25,35) ===")
	for dy = -2, 2 do
		local row = ""
		for dx = -2, 2 do
			local d = Test.GetDensity(CPos.New(25 + dx, 35 + dy))
			row = row .. string.format("%3d ", d)
		end
		print(string.format("[probe]   y=%d:%s", 35 + dy, row))
	end

	-- Issue grouped moves
	Trigger.AfterDelay(5, function()
		print("[probe] === MOVE 1: click into dense cluster (25, 35) ===")
		Test.GroupMove({ A1, A2, A3, A4 }, CPos.New(25, 35))

		Trigger.AfterDelay(200, function()
			print("[probe]   positions after move 1:")
			for _, u in ipairs({ A1, A2, A3, A4 }) do
				if not u.IsDead then
					print(string.format("[probe]     %s at (%d,%d) idle=%s", tostring(u),
						u.Location.X, u.Location.Y, tostring(u.IsIdle)))
				end
			end

			if hasAnyDensity then
				Test.Pass("density data IS populated on river-zeta")
			else
				Test.Fail("ALL probed cells read density=0 — shadows.bin/DensityLayer is empty on river-zeta")
			end
		end)
	end)
end
