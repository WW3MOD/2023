-- DIAGNOSTIC: which intent fires for various click positions on real
-- river-zeta-ww3. Issues group-move at multiple cells and logs the
-- [Cohesion] line for each to debug.log.
--
-- The cluster I'm probing around: bucket (20-30, 30-39) — a dense forest
-- with 41+ t10..t15 trees. Spawn squad just west of it.

WorldLoaded = function()
	TestHarness.FocusBetween(A1, A2, A3, A4)

	local squad = { A1, A2, A3, A4 }

	-- Battery of click positions to probe. Each tuple is (x, y, label).
	-- 0 = on cluster center, 1 = at near edge, 2 = 2 cells out, 4 = 4 cells out,
	-- 8 = 8 cells out (well into open).
	local probes = {
		{ x = 25, y = 35, label = "in-cluster-center" },
		{ x = 23, y = 35, label = "in-cluster-west-side" },
		{ x = 27, y = 35, label = "in-cluster-east-side" },
		{ x = 21, y = 35, label = "near-edge-1cell-west" },
		{ x = 19, y = 35, label = "edge-3cells-west" },
		{ x = 17, y = 35, label = "open-5cells-west" },
		{ x = 13, y = 35, label = "open-9cells-west" },
		{ x = 50, y = 50, label = "far-open" },
	}

	-- Issue each move ~150 ticks (6s) apart so the [Cohesion] log entries are
	-- spread out and the squad has time to receive each before the next.
	for i, p in ipairs(probes) do
		Trigger.AfterDelay(20 + (i - 1) * 150, function()
			print(string.format("[probe] issuing move to (%d,%d) [%s]", p.x, p.y, p.label))
			Test.GroupMove(squad, CPos.New(p.x, p.y))
		end)
	end

	-- Pass after all probes have had time to fire.
	Trigger.AfterDelay(20 + #probes * 150 + 50, function()
		Test.Pass(string.format("%d probes issued — see debug.log [Cohesion] lines", #probes))
	end)
end
