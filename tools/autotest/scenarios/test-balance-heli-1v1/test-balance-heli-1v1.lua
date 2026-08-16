-- BALANCE: Apache vs Mi-28 1v1 to the death @ 22c0 airborne.
-- Spawned at altitude 1280 mirroring test-heli-vs-heli-missile.
--
-- THIS SCENARIO CANNOT TELL YOU WHETHER EITHER HELI FIRED, and 22c0 is why.
-- Recorded 260817, after a run here returned `WINNER=Apache | ttk=2.9s |
-- hp=800/800 (100%)` and that verdict was briefly read as "the Mi-28's new air
-- mount does not work". It is not evidence of that. The duel is decided by time of
-- flight: Hellfire crosses 22c0 in ~51 ticks, Ataka.AA in ~61, and the kill lands
-- at ~48 — so a Mi-28 missile fired on the very first tick could not have arrived,
-- and a full-HP winner is produced identically by "never acquired", "fired and
-- still in the air", and "acquired too late to fire".
--
-- The fired-check therefore lives in test-mi28-engages-air, not here. Bolting it on
-- at this spacing would be flaky rather than merely weak: 22c0 is exactly
-- Ataka.AA's Range AND — ScanRadius being unpinned for these actors — exactly the
-- Mi-28's derived AutoTarget scan radius, so the assertion's colour would turn on
-- range-boundary semantics and hover phase. Leave this scenario measuring what it
-- is good at (who wins, how fast, at what HP) and ask the functional question where
-- it can be answered.
--
-- Second finding from the same investigation, deliberately NOT acted on because
-- changing it would move this test's numbers with no baseline to compare against:
-- the ^Combatant AutoTarget block in this folder's rules.yaml (ScanRadius 30, scan
-- interval 16/32, EngagementStance Hunt) is DEAD CONFIG here. Neither `heli` nor
-- `mi28` inherits ^Combatant — their chain is ^Helicopter -> ^Airborne ->
-- ^NeutralAirborne — so both run on the engine AutoTarget defaults (scan interval
-- 3/8) and on a scan radius derived from weapon range. The same block DOES bite in
-- sibling scenarios whose target is a vehicle: t90 inherits ^Combatant.

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
