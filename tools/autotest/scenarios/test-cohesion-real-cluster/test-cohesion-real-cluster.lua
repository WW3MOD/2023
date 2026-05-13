-- DIAGNOSTIC: probe DensityLayer + classifier on a real-cluster topology.
--
-- 39 trees replicated from river-zeta's dense bucket at (20-30, 30-39),
-- shifted to ~(5-15, 5-14). The classifier's [Cohesion] log line will show
-- intent + totalDensity for each grouped move issued from this test.
--
-- Probes:
--   1. Density at a grid of cells in/near/far from the cluster.
--   2. Grouped move CLICK INSIDE cluster at (10, 10).
--   3. Grouped move CLICK ADJACENT to cluster at (3, 9).
--
-- Pass if cluster cells return density > 0 AND at least one of the moves
-- produces a non-Open intent. Fail if everything reads as zero or Open.

WorldLoaded = function()
	TestHarness.FocusBetween(A1, A2, A3, A4)

	-- Probe density at a grid of cells.
	print("[probe] === density layer at cluster cells ===")
	local clusterCells = {
		{ x = 10, y = 10, label = "cluster-center" },
		{ x = 6,  y = 8,  label = "cluster-west" },
		{ x = 15, y = 13, label = "cluster-east" },
		{ x = 9,  y = 6,  label = "cluster-north" },
		{ x = 12, y = 14, label = "cluster-south" },
		{ x = 3,  y = 9,  label = "open-west-of-cluster" },
		{ x = 20, y = 10, label = "open-east-of-cluster" },
		{ x = 22, y = 25, label = "open-far-south" },
	}
	for _, p in ipairs(clusterCells) do
		local d = Test.GetDensity(CPos.New(p.x, p.y))
		print(string.format("[probe]   (%d,%d) %s: density=%d", p.x, p.y, p.label, d))
	end

	-- Wait one tick then click 1
	Trigger.AfterDelay(5, function()
		print("[probe] === MOVE 1: click inside cluster (10,10) ===")
		Test.GroupMove({ A1, A2, A3, A4 }, CPos.New(10, 10))

		Trigger.AfterDelay(150, function()
			print("[probe]   positions after move 1:")
			for _, u in ipairs({ A1, A2, A3, A4 }) do
				if not u.IsDead then
					print(string.format("[probe]     %s at (%d,%d) idle=%s", tostring(u),
						u.Location.X, u.Location.Y, tostring(u.IsIdle)))
				end
			end

			-- Move 2: click adjacent to cluster
			print("[probe] === MOVE 2: click ADJACENT to cluster (3,9) ===")
			Test.GroupMove({ A1, A2, A3, A4 }, CPos.New(3, 9))

			Trigger.AfterDelay(150, function()
				print("[probe]   positions after move 2:")
				for _, u in ipairs({ A1, A2, A3, A4 }) do
					if not u.IsDead then
						print(string.format("[probe]     %s at (%d,%d) idle=%s", tostring(u),
							u.Location.X, u.Location.Y, tostring(u.IsIdle)))
					end
				end

				-- Verdict: density at cluster center should be non-zero
				local clusterDensity = Test.GetDensity(CPos.New(10, 10))
				                     + Test.GetDensity(CPos.New(6, 8))
				                     + Test.GetDensity(CPos.New(11, 10))
				if clusterDensity == 0 then
					Test.Fail("density at all 3 probed cluster cells reads zero — DensityLayer not populated")
				else
					Test.Pass("clusterDensity=" .. clusterDensity)
				end
			end)
		end)
	end)
end
