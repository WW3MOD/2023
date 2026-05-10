-- BALANCE: 1 Abrams ($2500) vs 2 BMP-2 ($2600). Cost-equivalent comparison —
-- does heavy-frontal armor beat IFV ATGM swarm at near range?

WorldLoaded = function()
	TestHarness.FocusBetween(UnitA, UnitB1)
	TestHarness.Select(UnitA)

	local teamA = { UnitA }
	local teamB = { UnitB1, UnitB2 }
	BalanceHarness.ForceEngage(teamA, teamB)
	BalanceHarness.ForceEngage(teamB, teamA)
	BalanceHarness.RunDuel("1xAbrams", teamA, "2xBMP-2", teamB, 90)
end
