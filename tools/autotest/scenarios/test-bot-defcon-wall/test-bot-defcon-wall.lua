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

-- The advance FLOOR. The tanks start at x 6..9 and the band starts at 43, so 30 is comfortably past
-- "twitched off the spawn" and comfortably short of "must reach the band exactly". Both arms clear
-- it (measured) -- DRIFT is what separates them; see the note on HOLD_ORDER_BUDGET below.
local ADVANCE_X = 30

local RUN_SECONDS = 70
local HOLD_WINDOW_SECONDS = 20
local HOLD_DRIFT_CELLS = 2

-- THE MODULE UNDER TEST, and the budget is scoped to it rather than to the bot.
--
-- MEASURED 2026-09-13 (run 260913_233400): the first version of this budget counted EVERY order the
-- bot queued and allowed 120. The window carried 554 — and the axis was not the reason. Across the
-- five evaluations inside it (ticks 1314/1414/1514/1614/1714) debug.log holds exactly ONE
-- `[exp-offense] order` line: the staging clamp and the repath guard were working, and the axis
-- re-ordered its destination once, not five times. The 554 were the rest of the bot: 43
-- `[composition]` production lines on a map where its unit count went 8 -> 43 during the run, the
-- supply-fleet and capture lanes, and 20 `[exp-transport] delivery-move-reissued` lines from two
-- carriers stuck against the border (a real defect, but in MountedTransportBotModule, not here).
--
-- So the old assertion could only ever have measured the bot's overall busyness while its failure
-- text blamed the axis. Scoped to PoiOffensiveBotModule the question is answerable.
--
-- THE BUDGET IS PROVISIONAL and derived from log lines rather than from a direct per-module count,
-- which did not exist when the run was taken. The module's share of that window was one grouped
-- AttackMove, four reinforcement joins, one fires-anchor move and a SetCohesion per newly joined
-- unit (joined=3,3,2,2) — order 20, call it 40 with the orders no log line names. 150 is several
-- times that and still far below the ~220 a genuine per-scan re-issue of a 40-unit axis would cost.
-- Re-derive it from Test.BotOrdersQueued(USAbot, HOLD_ORDER_MODULE) the first time this passes.
--
-- DRIFT, NOT THIS, IS THE PRIMARY HOLD PREDICATE. The RED arm fails on drift (measured: 18 cells
-- against a 2-cell bound) and that is what discriminates; this is the belt to that braces.
local HOLD_ORDER_MODULE = "PoiOffensiveBotModule"
local HOLD_ORDER_BUDGET = 150

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
	local totalStartOrders = nil

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

		holdStartOrders = Test.BotOrdersQueued(USAbot, HOLD_ORDER_MODULE)
		totalStartOrders = Test.BotOrdersQueued(USAbot)
	end)

	Trigger.AfterDelay(RUN_SECONDS * TicksPerSecond, function()
		if furthestX < ADVANCE_X then
			-- A FLOOR, NOT THE DISCRIMINATOR. The first RED run passed this (measured, 260913_233651):
			-- the fires/echelon/staging anchors resolve to reachable cells on our own side and walk the
			-- army up the map whether or not the axis destination is legal. Reaching here means
			-- something much more basic is wrong -- no axis formed, or the army never left the SR at
			-- all -- so the message says that rather than blaming the border.
			Test.Fail(string.format(
				"bot never advanced: furthest tank reached x=%d (need x>=%d). The RED arm clears this "
				.. "bar, so this is not the border clamp failing -- look for an axis that never formed "
				.. "or a free pool that was never recruited",
				furthestX, ADVANCE_X))
			return
		end

		if holdStart == nil or holdStartOrders == nil or totalStartOrders == nil then
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

		local issued = Test.BotOrdersQueued(USAbot, HOLD_ORDER_MODULE) - holdStartOrders
		if issued > HOLD_ORDER_BUDGET then
			Test.Fail(string.format(
				"%s queued %d orders during the %ds hold window (budget %d): the axis is churning "
				.. "-- re-offering a destination it cannot reach every scan. NOTE this counts that "
				.. "module alone, so production and supply traffic cannot be the cause.",
				HOLD_ORDER_MODULE, issued, HOLD_WINDOW_SECONDS, HOLD_ORDER_BUDGET))
			return
		end

		Test.Pass(string.format(
			"advanced to x=%d, held at the border, %d %s orders in the hold window (total bot orders %d)",
			furthestX, issued, HOLD_ORDER_MODULE, Test.BotOrdersQueued(USAbot) - totalStartOrders))
	end)
end
