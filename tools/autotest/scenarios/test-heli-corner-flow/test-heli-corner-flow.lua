-- TEST: does a helicopter keep flying through an INTERMEDIATE waypoint, or stop on it?
--
-- THIS ONE CAN GO RED, WHICH IS UNUSUAL HERE AND IS THE REASON IT IS WORTH A SLOT. The reported symptom is
-- a SPEED -- "helicopters are stopping at every waypoint" -- and speed is readable from sim state as the
-- per-tick change in CenterPosition. So unlike the husk-corner scenario next door, the verdict is the
-- finding and not just liveness. The frames are corroboration.
--
-- THE MEASUREMENT. Each lane is polled every tick. Inside a CORNER WINDOW -- from the tick the airframe
-- first comes within CORNER_WINDOW of the corner waypoint until it has travelled the same distance up the
-- outbound leg -- the poll records the minimum per-tick displacement. That minimum is the number the whole
-- test turns on:
--
--   * LaneOff (the control, WaypointReleaseAggression 0) decelerates onto the waypoint and stops on it, so
--     its minimum is at or near ZERO.
--   * LaneOn (the shipped default) never reaches the waypoint. It drops it about four cells short and arcs
--     onto the outbound leg. Its speed still DIPS -- the airframe is a point mass being driven at a new
--     velocity vector, and the chord between two 245-length vectors 90 degrees apart passes within
--     245*cos(45 deg) = 173 of the origin -- but it never approaches zero. Roughly 71% of cruise is the
--     geometric floor for a right-angle corner and no tuning can beat it.
--
-- PASS CRITERIA, both of which must hold:
--
--   1. LaneOn's minimum speed through the corner window is at least MIN_CORNER_SPEED (122 WDist/tick, 50%
--      of the 245 cruise). Set at 50% rather than at the 71% the geometry predicts to leave room for the
--      dip being sampled a tick off its floor and for the speed clamp; a real regression to the old
--      behaviour reads as near-zero, so the gap between 50% and 0% is where the discrimination lives, not
--      between 50% and 71%.
--   2. LaneOn's REJOIN ERROR -- how far it is from the outbound leg's line once it is CORNER_WINDOW up
--      that leg -- is at most MAX_REJOIN_ERROR. This is the user's "ends up on a perfect trajectory
--      towards the next waypoint" and it is a GUARD, not the discriminator.
--
-- CRITERION 2 IS UNSIGNED, AND THAT IS THE WHOLE POINT. An earlier draft measured only `at.X - cornerX`,
-- east of the line, which is one-sided: it catches a release that fires too LATE (the airframe carries
-- residual eastward velocity past the line) and is blind to one that fires too EARLY (the airframe cuts
-- the corner and is still far WEST of the line when the window closes, having flown a visibly different
-- path). Modelled at aggression 200 the one-sided form reads 0 -- a clean pass -- while the airframe sits
-- 2.8 cells inside the line. Distance from the line is the honest metric in both directions.
--
-- WHAT EACH CRITERION CAN AND CANNOT CATCH. Criterion 1 discriminates: the arms genuinely differ on it
-- (180 against 5 in the model). Criterion 2 does NOT discriminate and must not be read as if it does --
-- the control scores a perfect 0, because an airframe that stopped dead on the corner and set off due
-- north is exactly on the line. It exists to catch the way THIS FIX could fail rather than the way the
-- old code did. If criterion 1 fails, the feature is not running at all; if criterion 2 fails, it is
-- running and mistuned.
--
-- WHERE THE THRESHOLDS COME FROM, because a guessed threshold is how the first draft of this file ended
-- up failing its own criterion at the shipped default, 7 WDist over, having never been run.
-- tools/heli-corner-model/model.py replicates this exact geometry against the engine's own integer path
-- -- the WAngle tables rather than atan2, Exts.ISqrt rather than math.sqrt, C# truncating division, and
-- semi-implicit Euler ordering. Rerun it after any change to AircraftCornerMath. It reports, for this
-- geometry at the shipped default: minSpeed 180, rejoin error 516. The thresholds sit at 122 and 1024,
-- so both carry roughly 1.5x and 2x margin. Model fidelity is corroborated three ways: it computes the
-- release distance as 4243 against the unit test's asserted 4244, it reproduces the control stopping dead,
-- and it lands the terminal waypoint at 0 error.
--
-- CALIBRATION. The release distance is derived geometry that has never been watched in game. The knob is
-- Aircraft.WaypointReleaseAggression and THE SIGN OF THE REJOIN ERROR SAYS WHICH WAY TO TURN IT, which is
-- why this file reports the signed value and not just its magnitude:
--
--   * still EAST of the line (positive)  -> released too LATE  -> RAISE the aggression
--   * still WEST of the line (negative)  -> released too EARLY -> LOWER the aggression
--
-- Modelled sweep, rejoin error against aggression: 25 -> 2733 east, 50 -> 1929 east, 100 -> 516 east,
-- 110 -> 97 east, 120 -> 306 west, 150 -> 1239 west, 200 -> 2823 west. It is a V with its floor near 110,
-- and criterion 2 passes for aggression 83..140. Note the direction: MORE aggression means an EARLIER
-- release and LESS overshoot. An earlier draft of this file advised the opposite and would have sent
-- anyone reading a red exactly the wrong way.
--
-- TICK DOMAIN. Every delay is in TICKS via Trigger.AfterDelay, never TestHarness seconds: the helper runs
-- 25 ticks per second against a mod at 16.67 and this scenario measures a corner lasting about 35 ticks.
-- There is no Trigger.OnTick in this engine; a self-rescheduling AfterDelay(1) is the idiom.

local CELL = 1024

-- Corner window half-width, in WDist. Eight cells comfortably contains both the ~4.1-cell release and the
-- ~35-tick arc, and on the control arm it contains the whole deceleration ramp (which begins ~2.9 cells
-- out) plus the stop itself.
local CORNER_WINDOW = 8 * CELL

-- 50% of the 245 cruise speed. The model measures 180 here, so this carries ~1.5x margin while the
-- control arm sits at 5.
--
-- TIED TO THIS GEOMETRY, and do not lift it into another scenario. The speed floor through a corner is
-- v*cos(theta/2) -- 173 at 90 degrees, but only 94 at the 135-degree cap. A 135-degree corner would fail
-- this threshold on arithmetic alone, having behaved perfectly.
local MIN_CORNER_SPEED = 122

-- One cell. The model measures a 516 rejoin error at the shipped default, so this is ~2x margin, and it
-- accepts aggression 83..140 while rejecting gross mistuning either side. Deliberately NOT tighter: the
-- metric is sampled once per tick in steps of roughly 220 WDist, so a threshold within one or two steps of
-- the expected value is a coin flip rather than a gate.
local MAX_REJOIN_ERROR = CELL

local LANES = {
	{
		name = "On", actor = nil, kind = "treatment",
		startX = 6, cornerX = 26, cornerY = 28, endY = 6,
		expect = "releases the corner early and arcs through it at speed",
	},
	{
		name = "Off", actor = nil, kind = "control",
		startX = 36, cornerX = 56, cornerY = 28, endY = 6,
		expect = "decelerates onto the corner and stops on it (WaypointReleaseAggression 0)",
	},
}

local BURST = { 0, 8, 16, 24, 32, 44 }

local finished = 0
local shots = 0

local function dist(ax, ay, bx, by)
	local dx, dy = ax - bx, ay - by
	return math.floor(math.sqrt(dx * dx + dy * dy))
end

local function shootBurst(lane)
	for _, offset in ipairs(BURST) do
		Trigger.AfterDelay(offset, function()
			shots = shots + 1
			TestHarness.Screenshot(
				"lane" .. lane.name .. "-" .. lane.kind .. "-t" .. offset,
				"lane " .. lane.name .. " (" .. lane.kind .. "), " .. offset ..
					" ticks after entering the corner window: " .. lane.expect)
		end)
	end
end

-- Poll a lane every tick, measuring per-tick displacement and the lane's offset from the outbound leg's
-- line. Both observables come from the same poll and the same CenterPosition read, so they cannot disagree
-- about where the airframe was on a given tick.
local function watchLane(lane)
	local heli = lane.actor
	local cornerPosX = lane.cornerX * CELL + CELL / 2
	local cornerPosY = lane.cornerY * CELL + CELL / 2

	local prevX, prevY = nil, nil
	local inWindow = false
	local windowDone = false
	local ticks = 0

	lane.minCornerSpeed = nil
	lane.rejoinError = nil       -- SIGNED offset from the outbound line at window close; + is east
	lane.maxEast = 0             -- reported only, the old one-sided number, kept as data
	lane.maxWest = 0             -- its mirror, so a corner-cut is visible in the log too
	lane.cruiseSpeed = 0
	lane.closestApproach = nil
	lane.windowTicks = 0

	local check
	check = function()
		if windowDone or heli.IsDead then return end

		ticks = ticks + 1
		if ticks > 3000 then
			print("[corner] lane " .. lane.name .. " WARNING: poll ran 3000 ticks without closing its window")
			windowDone = true
			finished = finished + 1
			return
		end

		local at = heli.CenterPosition
		local toCorner = dist(at.X, at.Y, cornerPosX, cornerPosY)

		if lane.closestApproach == nil or toCorner < lane.closestApproach then
			lane.closestApproach = toCorner
		end

		if prevX ~= nil then
			local step = dist(at.X, at.Y, prevX, prevY)

			-- Cruise reference: the fastest tick seen anywhere on the run, used only to report the corner
			-- minimum as a fraction of what this airframe actually achieved rather than of its rule-book
			-- Speed. If the two disagree badly, something throttled the lane and the run is not readable.
			if step > lane.cruiseSpeed then
				lane.cruiseSpeed = step
			end

			if not inWindow and toCorner <= CORNER_WINDOW then
				inWindow = true
				lane.windowOpenedAt = at
				print("[corner] lane " .. lane.name .. " (" .. lane.kind .. ") entered the corner window at " ..
					at.X .. "," .. at.Y .. " -- " .. toCorner .. " WDist from the waypoint, step " .. step)
				Camera.Position = at
				shootBurst(lane)
			end

			if inWindow then
				lane.windowTicks = lane.windowTicks + 1

				if lane.minCornerSpeed == nil or step < lane.minCornerSpeed then
					lane.minCornerSpeed = step
				end

				-- Signed offset from the outbound leg, which runs due north along x = cornerX. Positive is
				-- east of it (released late, carried past) and negative is west (released early, cut the
				-- corner). Both extremes are tracked so the log shows the shape of the path and not just
				-- its endpoint.
				local off = at.X - cornerPosX
				if off > lane.maxEast then
					lane.maxEast = off
				elseif -off > lane.maxWest then
					lane.maxWest = -off
				end

				-- The window closes once the airframe is CORNER_WINDOW up the outbound leg. Measured on the
				-- y axis alone so a lane that is still off the line cannot close its window early by being
				-- far from the corner in x.
				if cornerPosY - at.Y >= CORNER_WINDOW then
					windowDone = true
					finished = finished + 1

					-- THE number criterion 2 judges: where the airframe actually ended up relative to the
					-- line it was supposed to arc onto, sampled once the turn has had room to finish.
					-- Modelled stable to within ~40 WDist across window sizes from 6 to 10 cells.
					lane.rejoinError = off

					local pct = 0
					if lane.cruiseSpeed > 0 then
						pct = lane.minCornerSpeed * 100 / lane.cruiseSpeed
					end

					print("[corner] lane " .. lane.name .. " (" .. lane.kind .. ") RESULT" ..
						"  minCornerSpeed=" .. lane.minCornerSpeed ..
						"  cruise=" .. lane.cruiseSpeed ..
						"  pct=" .. math.floor(pct) ..
						"  rejoinError=" .. lane.rejoinError ..
						" (" .. (off >= 0 and "east of the line" or "west of the line") .. ")" ..
						"  maxEast=" .. lane.maxEast ..
						"  maxWest=" .. lane.maxWest ..
						"  closestApproachToWaypoint=" .. lane.closestApproach ..
						"  windowTicks=" .. lane.windowTicks)
					return
				end
			end
		end

		prevX, prevY = at.X, at.Y
		Trigger.AfterDelay(1, check)
	end

	Trigger.AfterDelay(1, check)
end

WorldLoaded = function()
	-- A scenario that dies on load writes `fail` with no frames, and read at a glance that is
	-- indistinguishable from a genuine RED. This banner, the staged frame below, and the STAGING-FAILURE:
	-- prefix on every failure this file raises are what separate the two. Same guard as
	-- test-husk-corner-slide, and for the same reason: a load abort once got banked as a result.
	print("[corner] ==== test-heli-corner-flow staged; verdict IS the finding, read minCornerSpeed in the [corner] RESULT lines ====")

	LANES[1].actor = LaneOn
	LANES[2].actor = LaneOff

	for _, lane in ipairs(LANES) do
		if lane.actor == nil then
			Test.Fail("STAGING-FAILURE: lane " .. lane.name .. " has no helicopter actor -- map.yaml and the Lua disagree")
			return
		end
	end

	Camera.Position = LaneOn.CenterPosition
	TestHarness.Select(LaneOn)

	-- Proof-of-life frame. A run directory with zero screenshots means this script never ran, whatever the
	-- status says.
	TestHarness.Screenshot("staged", "both lanes placed at the west end of their east legs, before any order")

	for _, lane in ipairs(LANES) do
		-- HoldFire so neither airframe acquires anything and breaks off. An engagement is a speed change
		-- and this test reads speed.
		lane.actor.Stance = "HoldFire"

		-- TWO QUEUED MOVES, WHICH IS THE WHOLE POINT. AircraftProperties.Move queues a Fly rather than
		-- replacing, so the first call is the intermediate waypoint and the second is the terminal one.
		-- One Move would produce no intermediate waypoint and this scenario would verify nothing --
		-- the mirror image of the mistake test-husk-corner-slide made in the opposite direction, where
		-- two Moves destroyed the arc it needed. Which arrangement stages the state under test depends
		-- entirely on the mechanism; neither is a house style.
		lane.actor.Move(CPos.New(lane.cornerX, lane.cornerY))
		lane.actor.Move(CPos.New(lane.cornerX, lane.endY))

		watchLane(lane)
	end

	-- ONE VERDICT PATH, DELIBERATELY. TestHarness.AssertWithin calls Test.Pass() itself the moment its
	-- predicate returns true (test-helpers.lua:91-93), so a scenario that uses it for liveness AND then
	-- judges separately afterwards has already passed before its own verdict runs. The judgement therefore
	-- lives INSIDE the predicate: false keeps waiting, a returned STRING fails with that string, true
	-- passes. Nothing else in this file calls Test.Pass or Test.Fail on the success path.
	TestHarness.AssertWithin(90, function()
		if finished < #LANES or shots < #LANES * #BURST then
			return false
		end

		local on = LANES[1]
		local off = LANES[2]

		if on.minCornerSpeed == nil then
			return "STAGING-FAILURE: the treatment lane closed its window without recording a speed"
		end

		-- The control's number is reported alongside rather than asserted on -- it is expected to stop
		-- dead, and asserting it would turn the control into a second treatment. But if the control did
		-- NOT slow, the two arms are not comparable whatever the treatment did, and that is worth saying
		-- out loud instead of leaving it for whoever reads the log.
		if off.minCornerSpeed ~= nil and off.minCornerSpeed > MIN_CORNER_SPEED then
			print("[corner] WARNING: the control lane did NOT slow at its corner (minCornerSpeed=" ..
				off.minCornerSpeed .. "). Either WaypointReleaseAggression: 0 did not apply, or the " ..
				"corner window never contained the deceleration. The arms are not comparable and a " ..
				"pass below is not evidence the feature did anything.")
		end

		if on.minCornerSpeed < MIN_CORNER_SPEED then
			return "Helicopter slowed to " .. on.minCornerSpeed .. " WDist/tick at an INTERMEDIATE " ..
				"waypoint (floor is " .. MIN_CORNER_SPEED .. ", cruise was " .. on.cruiseSpeed ..
				"). Control arm recorded " .. tostring(off.minCornerSpeed) .. ". The airframe is still " ..
				"stopping on the corner instead of arcing through it."
		end

		if on.rejoinError == nil then
			return "STAGING-FAILURE: the treatment lane closed its window without recording a rejoin error"
		end

		local magnitude = on.rejoinError >= 0 and on.rejoinError or -on.rejoinError
		if magnitude > MAX_REJOIN_ERROR then
			-- The SIGN carries the remediation, which is why it is reported rather than abs()'d away.
			-- More aggression means an EARLIER release and therefore LESS eastward overshoot; this is the
			-- opposite of what an earlier draft of this file said.
			local remedy = on.rejoinError > 0
				and "still EAST of the line, so it released too LATE -- RAISE Aircraft.WaypointReleaseAggression"
				or "still WEST of the line, so it released too EARLY and cut the corner -- LOWER Aircraft.WaypointReleaseAggression"
			return "Helicopter kept its speed but finished " .. magnitude .. " WDist off the outbound " ..
				"leg's line (limit is " .. MAX_REJOIN_ERROR .. "). It is " .. remedy ..
				". Modelled band for this geometry is aggression 83..140."
		end

		print("[corner] VERDICT pass -- treatment held " .. on.minCornerSpeed .. " WDist/tick (cruise " ..
			on.cruiseSpeed .. "), control " .. tostring(off.minCornerSpeed) .. ", rejoin error " ..
			on.rejoinError .. ", closest approach to the waypoint it never had to reach " ..
			tostring(on.closestApproach))

		return true
	end, function()
		-- Function form, evaluated at the moment of timeout, so the note carries end-of-run state. A
		-- verdict saying only "a lane did not finish" is compatible with opposite causes.
		return "STAGING-FAILURE: not every lane opened and closed its corner window and completed its " ..
			"frame burst (finished=" .. finished .. "/" .. #LANES .. ", shots=" .. shots .. "/" ..
			(#LANES * #BURST) .. "). This is NOT a speed verdict -- check lua.log for the [corner] " ..
			"banner and any per-lane entry lines to see how far staging got."
	end)
end
