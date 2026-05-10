-- BALANCE: Apache vs Mi-28 1v1 to the death @ 22c0 airborne.
-- Spawned at altitude 1280 mirroring test-heli-vs-heli-missile.

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("required players not found")
		return
	end

	local Apache = Actor.Create("heli", true, {
		Owner = USA,
		CenterPosition = cellPos(12, 17, 1280),
		Facing = Angle.East,
	})
	local Havoc = Actor.Create("mi28", true, {
		Owner = RUSSIA,
		CenterPosition = cellPos(34, 17, 1280),
		Facing = Angle.West,
	})

	if Apache == nil or Havoc == nil then
		Test.Fail("could not spawn helis (heli/mi28)")
		return
	end

	TestHarness.FocusBetween(Apache, Havoc)
	TestHarness.Select(Apache)

	local teamA = { Apache }
	local teamB = { Havoc }
	-- Issue Mi-28's attack order FIRST — first run had USA-first ordering
	-- and Mi-28 won 100%/0% deterministically. If swapping produces an
	-- Apache shutout, we've identified an actor-processing-order test
	-- artifact, not a real balance asymmetry.
	BalanceHarness.ForceEngage(teamB, teamA, false)
	BalanceHarness.ForceEngage(teamA, teamB, false)
	BalanceHarness.RunDuel("Apache", teamA, "Mi-28", teamB, 60)
end
