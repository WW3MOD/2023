-- BALANCE: 4v4 Abrams vs T-90 to the death @ 16c0.

WorldLoaded = function()
	TestHarness.FocusBetween(A1, B1)
	TestHarness.Select(A1)

	local teamA = { A1, A2, A3, A4 }
	local teamB = { B1, B2, B3, B4 }
	BalanceHarness.ForceEngage(teamA, teamB)
	BalanceHarness.ForceEngage(teamB, teamA)
	BalanceHarness.RunDuel("4xAbrams", teamA, "4xT-90", teamB, 90)
end
