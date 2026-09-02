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
-- VehicleCrew reads the hull's IFacing at all, whether the fan index actually varies per man, and
-- whether the resulting MoveTo survives contact with the pathfinder. All three can be wrong while
-- every unit test stays green, and each produces the symptom the user reported — men appearing in
-- front of the tank, or all on one cell.
--
-- Facing: 0 in map.yaml is NORTH, so the rear is SOUTH, which is +Y (OpenRA screen space is
-- north = -Y). WAngle is counterclockwise and this is easy to get backwards; map.yaml carries the
-- full derivation.
--
-- GEOMETRY THIS EXPECTS, and it is fully determined — there is no RNG in the bearing. EjectionOrder
-- is Commander, Gunner, Driver (vehicles-america.yaml:472) and the fan index is the ejection
-- ordinal, so slot 0 goes straight astern, slot 1 to one flank and slot 2 to the other. For a
-- north-facing hull at 33,16 that is one man due south at Y = 18 or 19, and two abeam at Y = 16.
-- Only the walk DISTANCE is rolled (2-3 cells).
--
-- Hence the three assertions, which together are stronger than "not in front":
--   * nobody at Y < 16          — no man came out of the FRONT. This is the +-90 degree bound.
--   * at least one at Y > 16    — somebody actually went straight astern rather than everyone
--                                 drifting sideways, which is what a facing-blind fan would give.
--   * three distinct cells      — the fan fanned; they did not stack or follow one lane.
--
-- RED before the change: the walk direction was a uniform SharedRandom.Next(8) compass roll, so
-- roughly 3 in 8 men finished north of the hull and any two could roll the same heading.
--
-- WHY THE DEADLINE IS GENEROUS AND WHY THE STAGING STRIPS SO MUCH. The 2026-09-01 RED (seed
-- -2084768515) failed with "live crew=1 ... tank died": the hull bleeds out below 50% HP by design
-- and cooked off the crew, so the geometry never got a sample. rules.yaml removes the bleed, the
-- finishing-shot damage and the inherited fire — staging only, the assertions below are unchanged.

-- Budget in TICKS and divide back through the harness constant. TestHarness.TicksPerSecond is 25
-- while the mod runs at Timestep 60 = 16.67 ticks/second; the constant is deliberately wrong and is
-- pinned by AutotestTickRateTest.cs, so anything sized in "seconds" here would silently mean
-- something else. 900 ticks is ~54 real seconds — generous against an eject sequence that completes
-- in ~50 ticks plus a 2-3 cell walk.
local function ticks(t) return t / TestHarness.TicksPerSecond end

local DeadlineTicks = 900
local HullX = 33
local HullY = 16
local ExpectedCrew = 3

local CrewTypes = { "crew.commander.america", "crew.gunner.america", "crew.driver.america" }

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

	-- Drop the tank to ~40% HP: past EjectionDamageState (Heavy = HP < 50%) so the whole crew bails.
	-- With CrewDamageThresholdPercent raised to 100 in rules.yaml the finishing shot cannot wound
	-- them, and with the bleed removed the hull now survives instead of cooking them off.
	Tank.Health = math.floor(Tank.MaxHealth * 4 / 10)

	TestHarness.AssertWithin(ticks(DeadlineTicks), function()
		local crew = LiveCrew(owner)
		if #crew < ExpectedCrew then
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
		local strictlyAstern = 0

		for _, c in ipairs(crew) do
			local loc = c.Location

			if loc.Y < HullY then
				return "fail: crew member finished at " .. loc.X .. "," .. loc.Y ..
					" which is NORTH of a north-facing hull at " .. HullX .. "," .. HullY ..
					" — he came out of the FRONT of the tank"
			end

			if loc.Y > HullY then
				strictlyAstern = strictlyAstern + 1
			end

			local key = loc.X .. "," .. loc.Y
			if cells[key] then
				return "fail: two crew members both finished on cell " .. key ..
					" — the dismount did not fan, they stacked"
			end

			cells[key] = true
		end

		if strictlyAstern == 0 then
			return "fail: all three crew finished level with the hull at Y=" .. HullY ..
				" — nobody took the straight-astern fan slot, which is what a facing-blind " ..
				"dismount would look like"
		end

		return true
	end, function()
		-- Function form so the timeout note reports the state that actually obtained. "The crew
		-- never settled" is compatible with opposite causes — nobody ejected, or three ejected and
		-- one is still pathing — and those want different next steps.
		local crew = LiveCrew(owner)
		local note = "crew did not settle within " .. DeadlineTicks .. " ticks; live crew=" .. #crew ..
			" of " .. ExpectedCrew
		for _, c in ipairs(crew) do
			note = note .. " [" .. c.Location.X .. "," .. c.Location.Y ..
				" idle=" .. tostring(c.IsIdle) .. "]"
		end

		if Tank.IsDead then
			note = note .. " (tank died — the -ChangesHealth@CriticalDamage override in rules.yaml " ..
				"is not taking effect; check that block first, not the geometry)"
		else
			note = note .. " (tank alive at HP=" .. Tank.Health .. "/" .. Tank.MaxHealth .. ")"
		end

		if #crew >= ExpectedCrew then
			note = note .. " — all crew are out and the cells above are the answer this scenario " ..
				"wanted; if they are all Y>=" .. HullY .. " the geometry is right and only IsIdle " ..
				"never came true"
		end

		return note
	end)
end
