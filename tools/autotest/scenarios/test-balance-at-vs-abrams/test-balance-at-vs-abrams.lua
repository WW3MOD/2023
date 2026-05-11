-- BALANCE: 3 AT infantry vs Abrams @ 12c0. Cross-check vs test-balance-at-vs-t90
-- to compare effective AT performance against the two MBTs (Abrams has 2.5x
-- the frontal thickness of T-90).

WorldLoaded = function()
	TestHarness.FocusBetween(AtA, Tank)
	TestHarness.Select(AtA)

	local teamA = { AtA, AtB, AtC }
	local teamB = { Tank }
	BalanceHarness.ForceEngage(teamA, teamB)
	BalanceHarness.ForceEngage(teamB, teamA)
	BalanceHarness.RunDuel("3xAT.inf", teamA, "1xAbrams", teamB, 60)
end
