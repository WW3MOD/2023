-- AUTO TEST: when the SR rally is set with AttackMove type, the produced
-- unit should run AttackMoveActivity and engage enemies on its path. The
-- decoy t90 sits at (30,16) — directly between the SR (12,16) and the
-- rally point (50,16). Abrams range = 25 cells, so the t90 is in weapon
-- range as soon as the abrams is spawned at the map edge and starts moving.

local DeadlineSeconds = 60   -- ProductionFromMapEdge spawn-from-edge takes a while

WorldLoaded = function()
	TestHarness.FocusBetween(OwnSR, Decoy)
	TestHarness.Select(OwnSR)

	Decoy.Stance = "HoldFire"

	-- Set rally as AttackMove type (the path the user walks via Alt+click).
	OwnSR.SetRallyWaypoint(CPos.New(50, 16), "AttackMove")

	-- Produce one Abrams. ProductionFromMapEdge spawns it at the map edge
	-- and queues per-waypoint activity (AttackMoveActivity for our waypoint).
	Test.QueueProduction(OwnSR.Owner, "abrams", 1)

	local startingAmmo = nil
	TestHarness.AssertWithin(DeadlineSeconds, function()
		local abrams = Utils.Where(OwnSR.Owner.GetActors(), function(a)
			return a.Type == "abrams" and not a.IsDead
		end)
		if #abrams == 0 then
			return false   -- still in production / not spawned yet
		end

		local hunter = abrams[1]
		if startingAmmo == nil then
			startingAmmo = hunter.AmmoCount("primary-ammo")
		end

		if hunter.AmmoCount("primary-ammo") < startingAmmo then
			return true
		end

		return false
	end, "Produced abrams did not engage Decoy on AttackMove rally within " .. DeadlineSeconds .. "s")
end
