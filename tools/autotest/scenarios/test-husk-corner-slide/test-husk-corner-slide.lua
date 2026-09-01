-- TEST: does a wreck point down the line it is sliding along while it finishes the move that killed it?
--
-- WHAT THE VERDICT IS AND IS NOT. The pass/fail here is a LIVENESS check only: every lane reached its kill
-- point, died there, and completed its screenshot burst. It is deliberately not the finding, and it cannot
-- go RED on the bug -- the wreck settles on its cell centre either way, which is exactly why the symptom is
-- a facing problem and not a position one. The evidence is the screenshot burst (the crab is a thing you
-- SEE) and the `[husk-settle]` lines in debug.log (the crab as a number). A green run with a 0-byte
-- lua.log, or no screenshots in result.json, means this measured nothing.
--
-- TWO THINGS TO LOOK FOR IN THE FRAMES, and the second is a regression risk rather than the reported bug.
-- Lanes A/B/C die inside a turn and should stop crabbing. Lane D dies on a dead-straight leg where the fix
-- must do NOTHING -- if it does something, the wreck snaps to a new heading at the instant of death, and a
-- straight-leg kill is something players see on nearly every kill in the game. Read lane D first.
--
-- ONE Move PER LANE, WHICH IS LOAD-BEARING. Two queued Move orders do NOT produce a corner arc: the first
-- completes at its waypoint, the unit settles on the cell centre, and the second Move turns it in place
-- before setting off (Move.cs:209-215). The arc -- and with it the ToCell retargeting that causes the crab
-- -- only happens BETWEEN CELLS OF ONE PATH, in MoveFirstHalf's chained-turn branch (Move.cs:709-722). An
-- earlier draft of this scenario used two Moves and would have produced a confident green having never
-- once cornered.
--
-- THE TURN IS FOUND, NOT GUESSED. Each arc lane is given a single diagonal-offset destination, so the path
-- contains a direction change, but WHERE that change falls is the pathfinder's business and not something
-- to hard-code. Each lane watches its own Location and fires on the first tick the step vector between
-- consecutive cells differs from the previous one -- that is the tick the arc opens, read off sim state.
--
-- TICK DOMAIN. Every delay is in TICKS via Trigger.AfterDelay, never TestHarness seconds: the helper's
-- TicksPerSecond is 25 against a mod running at 16.67, and this scenario turns on landing inside an arc
-- that lasts about twenty ticks. There is no Trigger.OnTick in this engine (a self-rescheduling
-- AfterDelay(1) is the idiom), and CPos has no Lua equality binding, so cells compare field-wise. Both of
-- those cost a run slot here on 2026-09-01.

-- Lanes are staggered in time so only one truck is dying at a time and the camera can snap to it.
local STAGGER = 140

local LANES = {
	{ name = "A", actor = nil, y = 6, destX = 40, destDY = 4, killAfter = 3, kind = "arc" },
	{ name = "B", actor = nil, y = 14, destX = 40, destDY = 4, killAfter = 7, kind = "arc" },
	{ name = "C", actor = nil, y = 22, destX = 40, destDY = 4, killAfter = 11, kind = "arc" },
	{ name = "D", actor = nil, y = 29, destX = 48, destDY = 0, killAfter = 6, kind = "straight" },
}

-- Ticks from the kill. The husk drag runs roughly 20-25 ticks at truck speed, so this brackets it: the
-- moment of death, across the slide, and after it has settled.
local BURST = { 0, 3, 6, 9, 12, 16, 20, 26 }

-- Cell advances to wait for on the straight control lane before killing it. Far enough in that the truck
-- is at full speed on a settled heading.
local STRAIGHT_ADVANCES = 6

local bursts = 0
local burstsExpected = #LANES * #BURST

-- Self-rescheduling per-tick poll. Fires `fn` once, on the first tick `pred` returns true.
local function onceWhen(pred, fn)
	local check
	check = function()
		if pred() then
			fn()
			return
		end

		Trigger.AfterDelay(1, check)
	end

	Trigger.AfterDelay(1, check)
end

local function shootBurst(lane)
	for _, offset in ipairs(BURST) do
		Trigger.AfterDelay(offset, function()
			TestHarness.Screenshot(
				"lane" .. lane.name .. "-" .. lane.kind .. "-t" .. offset,
				"lane " .. lane.name .. " (" .. lane.kind .. "), " .. offset ..
					" ticks after the kill: wreck should point down its slide, not across it")
			bursts = bursts + 1
		end)
	end
end

local function killNow(lane, why)
	local truck = lane.actor
	if truck.IsDead then return end

	local at = truck.Location
	print("[slide] lane " .. lane.name .. " (" .. lane.kind .. ") killed at " ..
		at.X .. "," .. at.Y .. " -- " .. why)

	-- Snap the camera to the truck itself. The turn is found at runtime, so its map position is not known
	-- when the scenario is written and a fixed framing could miss the wreck entirely.
	Camera.Position = truck.CenterPosition
	truck.Kill()
	shootBurst(lane)
end

-- Watch a lane's Location and report the first change of STEP DIRECTION (the arc opening), plus a running
-- count of cell advances. Both observables come from the same poll so they cannot disagree.
local function watchPath(lane, onTurn, onAdvance)
	local truck = lane.actor
	local prevX, prevY = nil, nil
	local stepX, stepY = nil, nil
	local advances = 0
	local finished = false

	local check
	check = function()
		if finished or truck.IsDead then return end

		local at = truck.Location
		if prevX ~= nil and (at.X ~= prevX or at.Y ~= prevY) then
			advances = advances + 1
			local sx, sy = at.X - prevX, at.Y - prevY

			if stepX ~= nil and (sx ~= stepX or sy ~= stepY) then
				finished = true
				onTurn(advances, sx, sy)
				return
			end

			stepX, stepY = sx, sy

			if onAdvance ~= nil and onAdvance(advances) then
				finished = true
				return
			end
		end

		prevX, prevY = at.X, at.Y
		Trigger.AfterDelay(1, check)
	end

	Trigger.AfterDelay(1, check)
end

WorldLoaded = function()
	-- A SCENARIO THAT DIES ON LOAD REPORTS `fail`, AND SO DOES A RED ARM. On 2026-09-01 this scenario
	-- aborted at load on a Lua error and wrote a bare `fail` with no frames and no angles; read at a
	-- glance, that is indistinguishable from a successful RED, and banking it would have "verified" the
	-- fix against a test that never ran a tick. Everything below exists to make the two look different:
	--
	--   * this banner in lua.log            -- absent => the script never ran
	--   * a "staged" screenshot immediately -- a run dir with ZERO frames => the script never ran
	--   * STAGING-FAILURE: on every failure this file raises itself, so a real staging failure cannot
	--     be confused with the engine's own "Fatal Lua Error" abort either
	--
	-- And note what a `pass` here does NOT mean: the verdict is liveness only and cannot go red on the
	-- crab. Read the frames and the [husk-settle] lines, never the status.
	print("[slide] ==== test-husk-corner-slide staged; verdict is LIVENESS ONLY, crab lives in frames + [husk-settle] ====")

	LANES[1].actor = TruckA
	LANES[2].actor = TruckB
	LANES[3].actor = TruckC
	LANES[4].actor = TruckD

	for _, lane in ipairs(LANES) do
		if lane.actor == nil then
			Test.Fail("STAGING-FAILURE: lane " .. lane.name .. " has no truck actor -- map.yaml and the Lua disagree")
			return
		end
	end

	Camera.Position = TruckA.CenterPosition
	TestHarness.Select(TruckA)

	-- Proof-of-life frame. If result.json lists no screenshots at all, this scenario did not run, whatever
	-- its status says.
	TestHarness.Screenshot("staged", "all four lanes placed, before any truck has moved")

	for i, lane in ipairs(LANES) do
		local truck = lane.actor

		-- Nothing shoots here; the trucks are killed on cue. HoldFire keeps them from acquiring anything
		-- and wandering out of their lane.
		truck.Stance = "HoldFire"

		Trigger.AfterDelay((i - 1) * STAGGER, function()
			if truck.IsDead then return end

			-- ONE Move. See the header: two would remove the arc this test exists to catch.
			truck.Move(CPos.New(lane.destX, lane.y + lane.destDY))

			if lane.kind == "arc" then
				watchPath(lane,
					function(advances)
						Trigger.AfterDelay(lane.killAfter, function()
							killNow(lane, "+" .. lane.killAfter .. " ticks into the turn at advance " .. advances)
						end)
					end,
					nil)
			else
				watchPath(lane,
					function(advances)
						-- The control lane is supposed to be dead straight. If the pathfinder turned it,
						-- the control is not controlling anything and the frames must not be read as one.
						print("[slide] lane " .. lane.name ..
							" WARNING: straight control lane turned at advance " .. advances ..
							" -- it is NOT a straight-leg control in this run")
						killNow(lane, "unexpected turn on the straight control lane")
					end,
					function(advances)
						if advances < STRAIGHT_ADVANCES then return false end

						Trigger.AfterDelay(lane.killAfter, function()
							killNow(lane, "+" .. lane.killAfter .. " ticks past advance " .. advances .. " on a straight leg")
						end)

						return true
					end)
			end
		end)
	end

	-- Liveness only. What this actually guards against is a scenario that never staged -- trucks that never
	-- moved, never reached a kill point, or never died, all of which would leave the frames showing nothing.
	TestHarness.AssertWithin(60, function()
		for _, lane in ipairs(LANES) do
			if not lane.actor.IsDead then return false end
		end

		return bursts >= burstsExpected
	end, "STAGING-FAILURE: not every lane reached its kill point, died there and completed its burst. " ..
		"This is NOT a crab verdict -- this scenario cannot go red on the bug. Check lua.log for the " ..
		"[slide] banner and per-lane kill lines to see how far staging got.")
end
