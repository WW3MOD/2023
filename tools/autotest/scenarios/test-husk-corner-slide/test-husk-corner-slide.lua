-- TEST: does a wreck point down the line it is sliding along while it finishes the move that killed it?
--
-- WHAT THE VERDICT IS AND IS NOT. The pass/fail here is a LIVENESS check only: it says every lane reached
-- its kill point, died there, and left a husk. It is deliberately not the finding, and it cannot go RED on
-- the bug -- the wreck settles on its cell centre either way, which is exactly why the symptom is a facing
-- problem rather than a position one. The evidence is the screenshot burst (the crab is a thing you SEE)
-- and the `[husk-settle]` lines in debug.log (the crab as a number). A green run with a 0-byte lua.log or
-- no screenshots in result.json means this measured nothing.
--
-- THERE ARE TWO THINGS TO LOOK FOR IN THE FRAMES, and the second is a regression risk rather than the
-- reported bug. Lanes A/B/C die inside the corner arc and should stop crabbing. Lane D dies on the straight
-- approach, where the fix is supposed to do nothing -- if it does something, the wreck visibly snaps to a
-- new heading at the instant of death, and a straight-leg kill is something players see far more often than
-- the corner case that prompted this. Checking only that the crab is gone would miss it.
--
-- TICK DOMAIN. Every delay below is in TICKS via Trigger.AfterDelay, never TestHarness seconds. The helper's
-- TicksPerSecond is 25 against a mod that runs at 16.67, so a "second" here would be 1.5x what it says --
-- and the whole scenario turns on landing inside an arc that lasts about twenty ticks. See AUTOTEST.md.

local CORNER_X = 30

-- Every lane drives the same L: east along its row to (CORNER_X, y), then south. What differs is WHERE
-- it is killed, expressed as "wait for Location to first read triggerCell, then kill killAfter ticks
-- later". Location is ToCell, so the cell it reads is a statement about the move the unit has committed
-- to, which is what makes these repeatable points in the MOVE rather than on the clock.
--
--   A/B/C  trigger on (CORNER_X, y+1) -- the tick the corner arc opens, since MoveFirstHalf's
--          chained-turn branch retargets ToCell past the corner right then. Three offsets into a
--          roughly twenty-tick arc so at least one wreck reliably dies mid-arc.
--   D      trigger on (25, y) -- still on the straight approach, six cells short of the corner and
--          long before any arc. The CONTROL: the fix must do nothing visible here.
local LANES = {
	{ name = "A", actor = nil, y = 10, triggerCell = nil, killAfter = 4, kind = "arc" },
	{ name = "B", actor = nil, y = 16, triggerCell = nil, killAfter = 8, kind = "arc" },
	{ name = "C", actor = nil, y = 22, triggerCell = nil, killAfter = 12, kind = "arc" },
	{ name = "D", actor = nil, y = 13, triggerCell = nil, killAfter = 6, kind = "straight" },
}

-- Offsets into the slide, in ticks from the kill. The husk drag runs roughly 20-25 ticks at truck speed,
-- so this brackets it: one frame at the moment of death, then across the slide, then after it settles.
local BURST = { 0, 3, 6, 9, 12, 16, 20, 26 }

local bursts = 0
local burstsExpected = #LANES * #BURST

local function cellPos(x, y)
	return WPos.New(x * 1024 + 512, y * 1024 + 512, 0)
end

-- Fire `fn` on the first tick `pred` is true, then stop. Polling every tick (rather than on a timer) is what
-- makes the kill land at a repeatable point in the ARC rather than at a repeatable wall-clock moment.
local function onceWhen(pred, fn)
	local done = false
	Trigger.OnTick(function()
		if done then return end
		if pred() then
			done = true
			fn()
		end
	end)
end

local function shootBurst(lane)
	for _, offset in ipairs(BURST) do
		Trigger.AfterDelay(offset, function()
			TestHarness.Screenshot(
				"lane" .. lane.name .. "-t" .. offset,
				"lane " .. lane.name .. ", " .. offset .. " ticks after the kill: wreck should point down its slide, not across it")
			bursts = bursts + 1
		end)
	end
end

WorldLoaded = function()
	LANES[1].actor = TruckA
	LANES[2].actor = TruckB
	LANES[3].actor = TruckC
	LANES[4].actor = TruckD

	for _, lane in ipairs(LANES) do
		lane.triggerCell = lane.kind == "arc"
			and CPos.New(CORNER_X, lane.y + 1)
			or CPos.New(CORNER_X - 5, lane.y)
	end

	-- Fixed camera on the shared corner column. All three corners sit at x=30, six rows apart, so one
	-- framing covers every lane and the burst never has to chase a unit.
	Camera.Position = cellPos(CORNER_X, 16)
	TestHarness.Select(TruckB)

	for _, lane in ipairs(LANES) do
		local truck = lane.actor

		-- Nothing shoots in this scenario; the trucks are killed on cue. HoldFire keeps them from
		-- acquiring anything and leaving their lane.
		truck.Stance = "HoldFire"

		-- East along the row, then south. The single 90-degree corner at (CORNER_X, y) is the whole test.
		truck.Move(CPos.New(CORNER_X, lane.y))
		truck.Move(CPos.New(CORNER_X, lane.y + 6))

		onceWhen(
			function()
				return not truck.IsDead and truck.Location == lane.triggerCell
			end,
			function()
				Trigger.AfterDelay(lane.killAfter, function()
					if truck.IsDead then return end
					print("[slide] lane " .. lane.name .. " (" .. lane.kind .. ") killed at " ..
						tostring(truck.Location) .. " (+" .. lane.killAfter .. " ticks past trigger)")
					truck.Kill()
					shootBurst(lane)
				end)
			end)
	end

	-- Liveness only. Deadline is generous because it covers the whole drive east plus the burst tail;
	-- what it actually guards against is a scenario that never staged -- trucks that never moved, never
	-- reached a corner, or never died, all of which would leave the screenshots showing nothing.
	TestHarness.AssertWithin(60, function()
		for _, lane in ipairs(LANES) do
			if not lane.actor.IsDead then return false end
		end

		return bursts >= burstsExpected
	end, "fail: not every lane reached its kill point, died there and completed its screenshot burst")
end
