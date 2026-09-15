--[[
  TEST: does DroneJammer reach 11 cells and not 13?

  Pins the 2026-09-15 cut from 20c0 to 12c0. The verdict needs BOTH halves — the near drone
  damaged AND the far drone untouched — because either half alone is satisfied by a range that
  is badly wrong in the other direction. A "the jammer fires" test stays green at 20c0 and at
  40c0; only the far drone surviving pins the number from above.

  WHY HEALTH AND NOT A CONDITION. DroneJammer carries SpreadDamage (3) and a
  GrantExternalCondition granting `dronedisable` for 5 ticks. Lua can read Health and cannot
  read a condition, and at BurstWait 1 the damage accumulates immediately, so
  `Health < MaxHealth` is the cheapest true statement of "this drone was jammed". It is also
  monotone: once hit, it stays hit, so a sampling gap cannot lose the evidence — which a
  5-tick condition absolutely could.

  THE CONFOUND THIS SCENARIO IS BUILT AROUND IS DRIFT. Two cells separate the arms. A
  quadcopterdrone with no master is an idle aircraft (IdleSpeed 25, IdleTurnSpeed 16) and may
  not hold station. So distance is RE-MEASURED every sample rather than assumed from the
  placement, and a drone that crosses the 12-cell line it was placed on the safe side of voids
  the run as SETUP INVALID instead of quietly producing a verdict about a range it never sat at.
  That is the same discipline as test-drone-lost-track's confound guard, for the same reason:
  a silent wrong answer costs more than a loud refusal.
]]

local OperatorCell = { X = 18, Y = 45 }

-- The threshold under test, in cells: DroneJammer's Range 12c0. Ranges are horizontal
-- (Target.cs:201-202 compares HorizontalLengthSquared and says height is ignored), so cruise
-- altitude does not enter these numbers.
local JammerRangeCells = 12
local NearCell = { X = 29, Y = 45 }   -- 11 cells: inside
local FarCell  = { X = 31, Y = 45 }   -- 13 cells: outside

local SpawnTick = 25
local SampleEveryTicks = 10
local DeadlineTicks = 600   -- ~36s at the real 16.667 t/s. BurstWait is 1, so a jammer that is
                            -- going to fire has fired long before this; the rest is margin for
                            -- acquisition and for watching the far drone stay clean.

local operator, nearDrone, farDrone
local setupFaults = {}
local elapsed = 0
local samples = 0
local nearHit, farHit = false, false
local nearMinDist, nearMaxDist = 9999, -1
local farMinDist, farMaxDist = 9999, -1

local function cellDist(a, b)
	local dx = a.X - b.X
	local dy = a.Y - b.Y
	return math.floor(math.sqrt((dx * dx) + (dy * dy)))
end

local function summary()
	return string.format(
		"samples=%d range=%dc near(placed %dc)=hit:%s dist:%d-%d far(placed %dc)=hit:%s dist:%d-%d",
		samples, JammerRangeCells,
		cellDist(NearCell, OperatorCell), tostring(nearHit), nearMinDist, nearMaxDist,
		cellDist(FarCell, OperatorCell), tostring(farHit), farMinDist, farMaxDist)
end

local function finish()
	Test.Screenshot("99-verdict-reached", "scenario reached finish() and is about to emit its own verdict")

	if #setupFaults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(setupFaults, "; ") .. " || " .. summary())
		return
	end

	if samples < 5 then
		Test.Fail("only " .. samples .. " samples — too few to call anything || " .. summary())
		return
	end

	-- BOTH HALVES, AND THE MESSAGE NAMES WHICH ONE BROKE. "The jammer is wrong" is not
	-- actionable; "it reached 13 cells" and "it failed to reach 11" point at opposite edits.
	if not nearHit and farHit then
		Test.Fail("INVERTED: the far drone was damaged and the near one was not. That is not a "
			.. "range error — suspect the placement, the owner or which armament fired. || " .. summary())
		return
	end

	if not nearHit then
		Test.Fail(string.format("the jammer did NOT reach %d cells, inside its %dc range. Either the "
			.. "range was cut too far, or the operator never acquired the drone at all — check that "
			.. "^AutoTarget picked it up (defaults.yaml:848-852) before blaming the number. || %s",
			cellDist(NearCell, OperatorCell), JammerRangeCells, summary()))
		return
	end

	if farHit then
		Test.Fail(string.format("the jammer REACHED %d cells, outside its %dc range — the cut did not "
			.. "take, or something other than DroneJammer damaged it. || %s",
			cellDist(FarCell, OperatorCell), JammerRangeCells, summary()))
		return
	end

	Test.Pass("jammer reached the near drone and not the far one || " .. summary())
end

local tick
tick = function()
	elapsed = elapsed + 1

	if elapsed == SpawnTick then
		local russia = Player.GetPlayer("Russia")
		if russia == nil then
			setupFaults[#setupFaults + 1] = "Russia player not found"
			finish()
			return
		end

		nearDrone = Actor.Create("quadcopterdrone", true,
			{ Owner = russia, Location = CPos.New(NearCell.X, NearCell.Y) })
		farDrone = Actor.Create("quadcopterdrone", true,
			{ Owner = russia, Location = CPos.New(FarCell.X, FarCell.Y) })

		if nearDrone == nil or nearDrone.IsDead then
			setupFaults[#setupFaults + 1] = "near drone did not spawn"
		end

		if farDrone == nil or farDrone.IsDead then
			setupFaults[#setupFaults + 1] = "far drone did not spawn"
		end
	end

	if elapsed > SpawnTick and (elapsed % SampleEveryTicks) == 0 then
		-- A DEAD DRONE IS STILL A RESULT, AND A DIFFERENT ONE. 50 HP against 3 damage a burst
		-- means the near drone can be killed outright inside this window; that is "hit" taken to
		-- its conclusion, not a fault. The far drone dying is a fault, and the range check below
		-- cannot run on a corpse, so it is recorded here rather than inferred later.
		if nearDrone ~= nil and not nearDrone.IsDead and nearDrone.IsInWorld then
			local d = cellDist(nearDrone.Location, OperatorCell)
			if d < nearMinDist then nearMinDist = d end
			if d > nearMaxDist then nearMaxDist = d end
			if nearDrone.Health < nearDrone.MaxHealth then
				nearHit = true
			end

			-- Drift across the line it was placed inside of makes this sample meaningless.
			if d > JammerRangeCells then
				setupFaults[#setupFaults + 1] = string.format(
					"the NEAR drone drifted to %dc, outside the %dc range it was placed inside "
					.. "(at %d:%d, t%d) — its result is no longer about being in range",
					d, JammerRangeCells, nearDrone.Location.X, nearDrone.Location.Y, elapsed)
				finish()
				return
			end
		elseif nearDrone ~= nil and nearDrone.IsDead then
			nearHit = true
		end

		if farDrone ~= nil and not farDrone.IsDead and farDrone.IsInWorld then
			local d = cellDist(farDrone.Location, OperatorCell)
			if d < farMinDist then farMinDist = d end
			if d > farMaxDist then farMaxDist = d end
			if farDrone.Health < farDrone.MaxHealth then
				farHit = true
			end

			if d <= JammerRangeCells then
				setupFaults[#setupFaults + 1] = string.format(
					"the FAR drone drifted to %dc, inside the %dc range it was placed outside "
					.. "(at %d:%d, t%d) — its survival would prove nothing",
					d, JammerRangeCells, farDrone.Location.X, farDrone.Location.Y, elapsed)
				finish()
				return
			end
		elseif farDrone ~= nil and farDrone.IsDead then
			farHit = true
		end

		samples = samples + 1
	end

	if elapsed >= DeadlineTicks then
		finish()
		return
	end

	Trigger.AfterDelay(1, tick)
end

WorldLoaded = function()
	Test.Screenshot("00-script-loaded", "scenario entered WorldLoaded; no actor has been queried yet")

	local usa = Player.GetPlayer("USA")
	if usa == nil then
		Test.Fail("USA player not found at load")
		return
	end

	local ops = usa.GetActorsByType("dr.america")
	if #ops == 0 then
		Test.Fail("SETUP INVALID: no drone operator (dr.america) on the map")
		return
	end

	operator = ops[1]

	-- Pin the bracket before measuring anything. If the placements do not straddle the range
	-- the run cannot answer the question, and saying so here is far cheaper than a verdict that
	-- looks like a range finding.
	local dn = cellDist(NearCell, OperatorCell)
	local df = cellDist(FarCell, OperatorCell)
	if dn > JammerRangeCells or df <= JammerRangeCells then
		Test.Fail(string.format("SETUP INVALID: the placements do not bracket %dc — near is %dc and "
			.. "far is %dc. Both arms must sit on opposite sides of the range.",
			JammerRangeCells, dn, df))
		return
	end

	if cellDist(operator.Location, OperatorCell) ~= 0 then
		Test.Fail(string.format("SETUP INVALID: operator is at %d:%d, not the %d:%d every distance "
			.. "here is measured from", operator.Location.X, operator.Location.Y,
			OperatorCell.X, OperatorCell.Y))
		return
	end

	Trigger.AfterDelay(1, tick)
end
