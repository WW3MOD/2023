-- BALANCE: 3 AT infantry vs T-90 @ 12c0.
-- Tests whether infantry AT can crack a heavy MBT at moderate range.
-- Real-world (Javelin/Kornet vs T-90): a fire team of 3 ATGMs is expected to
-- defeat an unsupported tank. ATGM is currently Pen 100, T-90 thickness 280
-- (front), top 280*0.6=168. TopAttack flag should route around frontal armor.

WorldLoaded = function()
	TestHarness.FocusBetween(AtA, Tank)
	TestHarness.Select(AtA)

	local teamA = { AtA, AtB, AtC }
	local teamB = { Tank }
	BalanceHarness.ForceEngage(teamA, teamB)
	BalanceHarness.ForceEngage(teamB, teamA)
	BalanceHarness.RunDuel("3xAT.inf", teamA, "1xT-90", teamB, 60)
end
