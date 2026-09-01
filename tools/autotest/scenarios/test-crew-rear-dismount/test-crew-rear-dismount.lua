-- AUTO TEST: crew bailing out of a damaged Abrams leave from the REAR and fan out.
--
-- User request (2026-09-01): "Can we make exiting a vehicle happen from the rear of a vehicle, so
-- it actually looks like a dismounting from a real vehicle? [...] I would like them to exit and
-- spread out as fast as possible, some going left, some going right, some going forward (from the
-- direction they are exiting, which is behind the vehicle)."
--
-- WHAT THIS CATCHES THAT THE UNIT TESTS CANNOT. DismountGeometryTest pins the arithmetic — that a
-- north-facing hull fans its first three men south, east and west, and that no fan slot at any
-- facing points within 90 degrees of the nose. What it cannot see is the WIRING: whether
-- VehicleCrew reads the hull's IFacing at all, whether it passes a fan index that actually varies
-- per man, and whether the resulting MoveTo survives contact with the pathfinder. All three of
-- those can be wrong while every unit test stays green, and each produces the same symptom the
-- user reported — men appearing in front of the tank, or all on one cell.
--
-- RED before the change: the ejection walk picked a uniformly random compass direction
-- (SharedRandom.Next(8)), so roughly 3 in 8 men finished NORTH of a north-facing hull and any two
-- men could roll the same heading. GREEN after: the walk bearing is the hull's facing plus half a
-- turn, fanned by +-90 degrees, so Y < 16 is unreachable and the three slots are distinct.
--
-- Facing: 0 in map.yaml is NORTH, so the rear is SOUTH, which is +Y. See map.yaml for the full
-- derivation — it is the counterclockwise WAngle convention and it is easy to get backwards.

local DeadlineSeconds = 25
local HullX = 33
local HullY = 16

local CrewTypes = { "crew.commander.america", "crew.gunner.america", "crew.driver.america" }

-- Every crew actor this player owns, whatever slot it came from.
local function LiveCrew(owner)
	local all = {}
	for _, t in ipairs(CrewTypes) do
		for _, a in ipairs(owner.GetActorsByType(t)) do
			if not a.IsDead and a.IsInWorld then
				all[#all + 1] = a
			end
		end
	end

	return all
end

WorldLoaded = function()
	TestHarness.FocusBetween(Tank)
	TestHarness.Select(Tank)

	local owner = Tank.Owner

	-- Drop the tank to ~40% HP. That is past EjectionDamageState (Heavy = HP < 50%) so the whole
	-- crew bails, and it is well short of lethal so nobody is killed on the way out: the finishing
	-- damage is ~60% of MaxHP against a 25% threshold, which scales to roughly a third of a crew
	-- member's own MaxHP.
	Tank.Health = math.floor(Tank.MaxHealth * 4 / 10)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		local crew = LiveCrew(owner)
		if #crew < 3 then
			return false
		end

		-- All three are out. Wait for them to stop walking before reading a cell: a man still in
		-- transit is not yet where the fan sent him.
		for _, c in ipairs(crew) do
			if not c.IsIdle then
				return false
			end
		end

		local cells = {}
		for _, c in ipairs(crew) do
			local loc = c.Location

			if loc.Y < HullY then
				return "fail: crew member finished at " .. loc.X .. "," .. loc.Y ..
					" which is NORTH of a north-facing hull at " .. HullX .. "," .. HullY ..
					" — he came out of the FRONT of the tank"
			end

			local key = loc.X .. "," .. loc.Y
			if cells[key] then
				return "fail: two crew members both finished on cell " .. key ..
					" — the dismount did not fan, they stacked"
			end

			cells[key] = true
		end

		return true
	end, function()
		-- Function form so the timeout note reports the state that actually obtained. "The crew
		-- never settled" is compatible with opposite causes — nobody ejected at all, or three men
		-- ejected and one is still pathing — and those want different next steps.
		local crew = LiveCrew(owner)
		local note = "crew did not settle within " .. DeadlineSeconds .. "s; live crew=" .. #crew
		for _, c in ipairs(crew) do
			note = note .. " [" .. c.Location.X .. "," .. c.Location.Y .. " idle=" .. tostring(c.IsIdle) .. "]"
		end

		if Tank.IsDead then
			note = note .. " (tank died — the damage step was lethal, which this scenario does not intend)"
		end

		return note
	end)
end
