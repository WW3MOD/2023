-- BALANCE: Abrams vs T-90 1v1 to the death.
-- Range 18c0 (well within both ranges: Abrams 25c0, T-90 24c0).
-- Both stances FireAtWill; force-attack at t=0 to skip scan-interval lag.

WorldLoaded = function()
	TestHarness.FocusBetween(UnitA, UnitB)
	TestHarness.Select(UnitA)

	local teamA = { UnitA }
	local teamB = { UnitB }
	BalanceHarness.ForceEngage(teamA, teamB)
	BalanceHarness.ForceEngage(teamB, teamA)
	BalanceHarness.RunDuel("Abrams", teamA, "T-90", teamB, 60)
end
