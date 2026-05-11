-- BALANCE: Paladin (M109) vs Giatsint (2S5) at 32c0.
-- Both have 40c0 range. Both must deploy/setup before firing.
-- Paladin fires Burst:3 / BurstWait 240; Giatsint single shot / BurstWait 180.

WorldLoaded = function()
	TestHarness.FocusBetween(UnitA, UnitB)
	TestHarness.Select(UnitA)

	local teamA = { UnitA }
	local teamB = { UnitB }
	BalanceHarness.ForceEngage(teamA, teamB)
	BalanceHarness.ForceEngage(teamB, teamA)
	BalanceHarness.RunDuel("Paladin", teamA, "Giatsint", teamB, 90)
end
