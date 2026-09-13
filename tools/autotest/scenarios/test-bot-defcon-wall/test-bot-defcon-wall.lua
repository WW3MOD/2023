-- TEST: the bot advances to the DEFCON 3 border and holds there.
--
-- Layout and intent live in description.txt. This file places the bot's armour, keeps the
-- no-crossing invariant every tick, and renders the verdict at the end of the run.
--
-- WHY THE UNITS ARE PLACED HERE AND NOT IN map.yaml. They must be owned by the bot and be in the
-- free pool when PoiOffensiveBotModule first evaluates. Actor.Create at WorldLoaded puts them in
-- the world before the bot's first eval (which is LocalRandom.Next(0, 100) ticks in), and keeps
-- the placement next to the geometry it is chosen against.
--
-- EVERY PER-UNIT TABLE IS KEYED BY INDEX INTO `Tanks`, never by the actor. ActorID is C#-only and
-- the wrapper carries no __tostring, so an actor cannot key a Lua table reliably --
-- test-rank-accumulation:237 records the same finding. The index is ours and stable for the run.

local TicksPerSecond = TestHarness.TicksPerSecond

-- The authored line, from rules.yaml. HalfWidth is the 1024 default, so the impassable band is
-- columns 43..45 and x >= 43 is already illegal ground for a west-side unit.
local BAND_WEST_EDGE = 43

-- The advance bar. The tanks start at x 6..9 and the band starts at 43, so 30 is comfortably past
-- "twitched off the spawn" and comfortably short of "must reach the band exactly".
local ADVANCE_X = 30

local RUN_SECONDS = 70
local HOLD_WINDOW_SECONDS = 20
local HOLD_DRIFT_CELLS = 2

-- Orders the bot may still issue during the hold window without it counting as churn. A parked axis
-- still runs every other module on its own cadence (production, supply, garrison), so this is not
-- zero; it is well under what a per-scan re-offer of a whole axis would produce.
local HOLD_ORDER_BUDGET = 120

Tanks = {}

WorldLoaded = function()
	local USAbot = Player.GetPlayer("USA-bot")

	local placements = {
		{ 6, 26 }, { 6, 28 }, { 6, 30 },
		{ 8, 26 }, { 8, 28 }, { 8, 30 },
		{ 9, 27 }, { 9, 29 },
	}

	for _, p in ipairs(placements) do
		Tanks[#Tanks + 1] = Actor.Create("abrams", true, {
			Owner = USAbot,
			Location = CPos.New(p[1], p[2]),
			Facing = Angle.East,
		})
	end

	TestHarness.Select(Tanks[1])

	-- Running state for the verdict.
	local crossed = nil
	local furthestX = 0
	local tick = 0

	-- Positions at the start of the hold window, and the order count there. Both nil until the
	-- window opens, which is what distinguishes "not measured yet" from "measured as zero".
	local holdStart = nil
	local holdStartOrders = nil

	local function forEachLiveTank(fn)
		for i, t in ipairs(Tanks) do
			if not t.IsDead and t.IsInWorld then
				fn(i, t)
			end
		end
	end

	local function sample()
		tick = tick + 1

		forEachLiveTank(function(_, t)
			local x = t.Location.X
			if x > furthestX then
				furthestX = x
			end

			-- THE INVARIANT. One observation inside or beyond the band is a failure even if the
			-- unit comes back: the border is a barrier, not a rubber band.
			if crossed == nil and x >= BAND_WEST_EDGE then
				crossed = string.format("bot unit %s reached x=%d at tick %d (band starts at x=%d) -- "
					.. "the border did not hold", t.Type, x, tick, BAND_WEST_EDGE)
			end
		end)

		if crossed ~= nil then
			Test.Fail(crossed)
			return
		end

		Trigger.AfterDelay(1, sample)
	end

	sample()

	-- Open the hold window: snapshot where everything is and how many orders have been issued.
	Trigger.AfterDelay((RUN_SECONDS - HOLD_WINDOW_SECONDS) * TicksPerSecond, function()
		holdStart = {}
		forEachLiveTank(function(i, t)
			holdStart[i] = { t.Location.X, t.Location.Y }
		end)

		holdStartOrders = Test.BotOrdersQueued(USAbot)
	end)

	Trigger.AfterDelay(RUN_SECONDS * TicksPerSecond, function()
		if furthestX < ADVANCE_X then
			Test.Fail(string.format(
				"bot never advanced: furthest tank reached x=%d (need x>=%d); it is still parked on "
				.. "its Supply Route -- the axis ordered through the border and the order did nothing",
				furthestX, ADVANCE_X))
			return
		end

		if holdStart == nil or holdStartOrders == nil then
			Test.Fail("hold window never opened -- the run budget and the window are inconsistent")
			return
		end

		local drifted = nil
		forEachLiveTank(function(i, t)
			local was = holdStart[i]
			if drifted == nil and was ~= nil then
				local drift = TestHarness.CellDrift(was[1], was[2], t.Location.X, t.Location.Y)
				if drift > HOLD_DRIFT_CELLS then
					drifted = string.format(
						"bot unit %s drifted %d cells during the hold window (max %d): the axis is "
						.. "still re-ordering instead of holding at the border",
						t.Type, drift, HOLD_DRIFT_CELLS)
				end
			end
		end)

		if drifted ~= nil then
			Test.Fail(drifted)
			return
		end

		local issued = Test.BotOrdersQueued(USAbot) - holdStartOrders
		if issued > HOLD_ORDER_BUDGET then
			Test.Fail(string.format(
				"bot queued %d orders during the %ds hold window (budget %d): the axis is churning "
				.. "-- re-offering a destination it cannot reach every scan",
				issued, HOLD_WINDOW_SECONDS, HOLD_ORDER_BUDGET))
			return
		end

		Test.Pass(string.format("advanced to x=%d, held at the border, %d orders in the hold window",
			furthestX, issued))
	end)
end
