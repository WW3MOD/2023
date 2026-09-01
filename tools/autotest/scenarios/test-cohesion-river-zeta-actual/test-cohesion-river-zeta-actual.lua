-- DIAGNOSTIC: which intent fires for various click positions on real
-- river-zeta-ww3. Issues group-move at multiple cells and logs the
-- [Cohesion] line for each to debug.log.
--
-- River-zeta map is 98x82. Visible tree clusters per map.png:
--   A: (20-30, 30-40)  upper-left cluster — main probe site
--   B: (15-25, 55-70)  lower-left cluster
--   C: (60-75, 55-75)  lower-right cluster
--   D: (60-75, 15-25)  upper-right cluster
-- Center and SE/SW corners are open ground/river.
--
-- For each probe we want to know: does the classifier resolve to
-- Open/SpreadInside/EdgeLine/Approach? With what totalDensity?
-- The diagnostic log line in CohesionMoveModifier reports both.

WorldLoaded = function()
	TestHarness.FocusBetween(A1, A2, A3, A4)

	local squad = { A1, A2, A3, A4 }

	-- Pre-probe density at a sparse grid so the lua.log shows the actual
	-- density landscape — lets us spot whether the click sites we're
	-- testing fall on dense, sparse, or no-cover terrain.
	print("[density-grid] sampling 12 cells across river-zeta ...")
	local grid = {
		{ 25, 35, "A-cluster-center" },
		{ 22, 35, "A-cluster-west-trunk" },
		{ 21, 35, "A-cluster-west-edge" },
		{ 19, 35, "A-cluster-3w" },
		{ 17, 35, "A-cluster-5w" },
		{ 12, 35, "A-open-far-west" },
		{ 20, 60, "B-cluster-lower-left" },
		{ 70, 65, "C-cluster-lower-right" },
		{ 68, 20, "D-cluster-upper-right" },
		{ 50, 40, "M-center-open" },
		{ 80, 75, "M-se-corner-open" },
		{ 10, 75, "M-sw-corner-open" },
	}
	for _, g in ipairs(grid) do
		local d = Test.GetDensity(CPos.New(g[1], g[2]))
		print(string.format("[density-grid] (%d,%d) = %d  [%s]", g[1], g[2], d, g[3]))
	end

	-- Click probes — same 12 cells as the density grid. Spaced 50 ticks
	-- (2s) apart so each Move order completes its dispatch before the
	-- next one fires. We don't need the squad to physically arrive
	-- between probes; only the dispatch matters for [Cohesion] logging.
	local probes = grid -- reuse — each probe triggers a GroupMove to that cell

	for i, p in ipairs(probes) do
		Trigger.AfterDelay(30 + (i - 1) * 50, function()
			print(string.format("[probe %d/%d] move to (%d,%d) [%s]",
				i, #probes, p[1], p[2], p[3]))
			Test.GroupMove(squad, CPos.New(p[1], p[2]))
		end)
	end

	-- Skip after all probes have dispatched. Final delay sized for the
	-- last probe to complete its order resolution. Skip, not Pass: this is a
	-- diagnostic that grades nothing, and the verdict must say so.
	Trigger.AfterDelay(30 + #probes * 50 + 75, function()
		Test.Skip(string.format("%d probes issued — see debug.log [Cohesion] lines and lua.log [density-grid]", #probes))
	end)
end
