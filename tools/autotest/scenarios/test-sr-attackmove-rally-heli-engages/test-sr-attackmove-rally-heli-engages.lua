-- AUTO TEST: an Alt-tagged (AttackMove) SR rally waypoint must produce a real
-- attack-move on a HELICOPTER, not a plain Move.
--
-- This is the aircraft twin of test-sr-attackmove-rally-engages, and it exists
-- because that test cannot see the bug it is named after. Its subject is an
-- abrams; the defect was a `move is Mobile` guard in
-- ProductionFromMapEdge.BuildWaypointActivity, so every GROUND unit passed while
-- every aircraft silently degraded to Move. The SR line stayed red and the
-- helicopter flew a green one.
--
-- WHY THE VERDICT IS THE ACTIVITY CHAIN AND NOT AMMO OR A KILL.
-- Ammo cannot decide this. The helicopter's flight path crosses the decoy either
-- way, and with FireAtWill its opportunity fire will engage on the way past even
-- under a plain Move — so "ammo went down" and "the decoy died" are both TRUE
-- with the bug present. Asserting on them would produce a test that is green in
-- both worlds, which is worse than no test. Test.ActivityChain names the
-- activity actually running, which is exactly the thing that differed.
--
-- KNOWN FALSE-PASS RISK, and why it does not apply here: Aircraft.cs:1526
-- queues an AttackMoveActivity of its own for rally cells, independently of the
-- waypoint type. That path is inert in this scenario on two counts — it lives in
-- AssociateWithAirfieldActivity, which returns immediately unless
-- TakeOffOnCreation (^Airborne sets it False), and it reads a RallyPointInit that
-- ProductionFromMapEdge never adds. If either of those changes this test can go
-- green for the wrong reason; RED-check it before trusting it.

local DeadlineSeconds = 90   -- aircraft spawn from the map edge and fly in; slower than a ground spawn

local chainLog = {}

WorldLoaded = function()
	TestHarness.FocusBetween(OwnSR, Decoy)
	TestHarness.Select(OwnSR)

	Decoy.Stance = "HoldFire"

	-- Set rally as AttackMove type — the same waypoint the player produces by
	-- Alt+clicking the SR, which is what draws the red line.
	OwnSR.SetRallyWaypoint(CPos.New(50, 16), "AttackMove")

	-- HELI (Apache) is produced through ProductionFromMapEdge's Helicopter type
	-- (structures.yaml). The SUPPLYROUTE grants aircraft.america itself, so no
	-- other structure is needed.
	--
	-- Lowercase deliberately: the actor is written `HELI:` in aircraft-america.yaml,
	-- but Ruleset keys every actor ToLowerInvariant (Ruleset.cs:126), and
	-- Test.QueueProduction does a plain TryGetValue on that dictionary. Passing
	-- "HELI" finds no queue and returns SILENTLY — the scenario would then time out
	-- with "never ran AttackMoveActivity", blaming the fix for a typo.
	Test.QueueProduction(OwnSR.Owner, "heli", 1)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		local helis = Utils.Where(OwnSR.Owner.GetActors(), function(a)
			return a.Type == "heli" and not a.IsDead
		end)

		if #helis == 0 then
			return false   -- still in production / not spawned yet
		end

		local chain = Test.ActivityChain(helis[1])
		if chainLog[chain] == nil then
			chainLog[chain] = true
			print(string.format("[sr-heli-attackmove] tick %d chain=%s", DateTime.GameTime, chain))
		end

		return string.find(chain, "AttackMoveActivity", 1, true) ~= nil
	end, "Produced HELI never ran AttackMoveActivity for its AttackMove rally waypoint within "
		.. DeadlineSeconds .. "s — the waypoint degraded to a plain Move")
end
