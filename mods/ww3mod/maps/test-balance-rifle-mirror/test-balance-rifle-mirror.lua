-- BALANCE: 4 USA rifles vs 4 RUS rifles @ 8c0. Symmetric infantry templates
-- should be near-equal — if not, faction stats have drifted.

WorldLoaded = function()
	TestHarness.FocusBetween(A1, B1)
	TestHarness.Select(A1)

	local teamA = { A1, A2, A3, A4 }
	local teamB = { B1, B2, B3, B4 }
	-- Swap: Russian side issues attacks first to test whether the previous
	-- USA shutout was a Lua call-order artifact (as confirmed by heli test).
	BalanceHarness.ForceEngage(teamB, teamA)
	BalanceHarness.ForceEngage(teamA, teamB)
	BalanceHarness.RunDuel("4xE3.USA", teamA, "4xE3.RUS", teamB, 60)
end
