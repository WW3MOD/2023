-- TEST: does a wreck point down the line it is sliding along while it finishes the move that killed it?
--
-- WHAT THE VERDICT IS AND IS NOT. The pass/fail here is a LIVENESS check only: it says all three trucks
-- reached their corners, died there, and left husks. It is deliberately not the finding, and it cannot go
-- RED on the bug -- the wreck settles on its cell centre either way, which is exactly why the symptom is a
-- facing problem rather than a position one. The evidence is the screenshot burst (the crab is a thing you
-- SEE) and the `[husk-settle]` lines in debug.log (the crab as a number). A green run with a 0-byte lua.log
-- or no screenshots in result.json means this measured nothing.
--
-- TICK DOMAIN. Every delay below is in TICKS via Trigger.AfterDelay, never TestHarness seconds. The helper's
-- TicksPerSecond is 25 against a mod that runs at 16.67, so a "second" here would be 1.5x what it says --
-- and the whole scenario turns on landing inside an arc that lasts about twenty ticks. See AUTOTEST.md.

local CORNER_X = 30
local LANES = {
	{ name = "A", actor = nil, y = 10, killAfterArc = 4 },
	{ name = "B", actor = nil, y = 16, killAfterArc = 8 },
	{ name = "C", actor = nil, y = 22, killAfterArc = 12 },
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

		-- Mobile.TopLeft is ToCell, and the chained-turn branch in MoveFirstHalf sets ToCell to the cell
		-- AFTER the corner at the moment the arc starts. So Location first reading (CORNER_X, y+1) is the
		-- arc's opening tick -- a sim-state edge, not a guess about speed.
		onceWhen(
			function()
				return not truck.IsDead and truck.Location == CPos.New(CORNER_X, lane.y + 1)
			end,
			function()
				Trigger.AfterDelay(lane.killAfterArc, function()
					if truck.IsDead then return end
					print("[slide] lane " .. lane.name .. " killed mid-arc at " ..
						tostring(truck.Location) .. " (+" .. lane.killAfterArc .. " ticks into the arc)")
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
	end, "fail: not all three trucks reached a corner, died there and completed their screenshot burst")
end
