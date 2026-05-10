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
	-- allowMove=false: keep helis hovering in place. The heli duel is also
	-- harness-deterministic (whoever Attack()s first wins 100%-0% — confirmed
	-- by swap-order rerun on 260510); real game has autotarget jitter that
	-- breaks this artifact. See WORKSPACE/balancing/260510_balance_recommendations.md §C.8.
	BalanceHarness.ForceEngage(teamA, teamB, false)
	BalanceHarness.ForceEngage(teamB, teamA, false)
	BalanceHarness.RunDuel("Apache", teamA, "Mi-28", teamB, 60)
end
