-- BALANCE: Bradley vs BMP-2 1v1 to the death @ 18c0.
-- ATGM range 25c0, autocannon range 18-19c0 — both can use ATGMs from start.

WorldLoaded = function()
	TestHarness.FocusBetween(UnitA, UnitB)
	TestHarness.Select(UnitA)

	local teamA = { UnitA }
	local teamB = { UnitB }
	BalanceHarness.ForceEngage(teamA, teamB)
	BalanceHarness.ForceEngage(teamB, teamA)
	BalanceHarness.RunDuel("Bradley", teamA, "BMP-2", teamB, 90)
end
