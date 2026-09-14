-- TEST: a loaded bot carrier is not stranded by the DEFCON 3 border.
--
-- Layout and intent live in description.txt. This file places the carriers and their infantry, keeps
-- the no-crossing invariant every tick, and renders the verdict at the end of the run.
--
-- WHY THE UNITS ARE PLACED HERE AND NOT IN map.yaml. They must be owned by the bot and be visible to
-- MountedTransportBotModule's first scan. Actor.Create at WorldLoaded puts them in the world before
-- that scan, and keeps the placement next to the geometry it is chosen against.
--
-- EVERY PER-UNIT TABLE IS KEYED BY INDEX INTO `Carriers`, never by the actor. ActorID is C#-only and
-- the wrapper carries no __tostring, so an actor cannot key a Lua table reliably --
-- test-bot-defcon-wall and test-rank-accumulation:237 record the same finding.
--
-- BUDGETS ARE IN TICKS, per test-helpers.lua:30. Seconds are derived for the failure text only. Do
-- not re-derive a tick budget from a wall-clock figure here: TestHarness.TicksPerSecond is 25 and the
-- engine's timestep is 60 ms (16.67 t/s), so the two disagree by 1.5x. Every number below is the tick
-- count that was reasoned about; the seconds printed alongside it are the helper's convention and are
-- NOT wall-clock seconds.

-- The authored line is x 44 with the 1024 default HalfWidth, so the impassable band is columns
-- 43..45 and x >= 43 is already illegal ground for a west-side unit.
local BAND_WEST_EDGE = 43

local RUN_TICKS = 3000

-- HOW FAR A LOADED CARRIER MUST GET, measured from where it was standing when its hold first became
-- non-empty. THIS IS THE DISCRIMINATOR. With the border refusing the delivery Move outright the
-- carrier does not move one cell, so the RED arm scores 0 against this bar. With staging on it drives
-- from x ~6 to the staging cell against the band at x ~42 -- about 36 cells -- so 10 is far past
-- jitter and far short of what the fix actually achieves. It deliberately does NOT require reaching
-- the band: the staged cell is wherever the SR -> objective lane meets the border, and pinning that
-- exact column here would couple the test to the clamp's lateral relocation.
local ADVANCE_CELLS = 10

-- Ticks after first loading by which the carrier must have advanced, and by which its hold must be
-- empty again. Generous on purpose -- a delivery is a 36-cell drive plus an Unload, and this test is
-- about "ever" versus "never", not about tempo. A carrier that needs longer than this is one whose
-- Move is being refused, which is the defect.
-- ADVANCE is the tight, reliable one: the RED arm scores literally zero against it, so any positive
-- bar discriminates and 900 ticks is far more than a 10-cell drive needs.
--
-- UNLOAD is the SOFTER of the two and is the one to suspect if a GREEN arm ever fails here alone: it
-- covers a ~36-cell drive plus an Unload, and no measured figure for a bradley's traverse of that
-- distance exists yet. It is kept because it is the only assertion that would catch the ORDERED cell
-- and the MEASURED cell drifting apart -- the exact trap BotTerrain's own doc describes, and a trap
-- this fix newly exposes itself to by holding DropOff and OrderedDropOff as two fields. Re-derive
-- both from the first GREEN run rather than treating them as measured.
local ADVANCE_DEADLINE_TICKS = 900
local UNLOAD_DEADLINE_TICKS = 2000

-- THE MODULE UNDER TEST. Scoped with Test.BotOrdersQueued's module argument rather than counting the
-- whole bot, for the reason test-bot-defcon-wall's budget records at length: a whole-bot count
-- measures production and supply traffic and grows with the unit count, so it is a bar a healthy bot
-- eventually fails while the failure text blames the module.
--
-- THE BUDGET IS A BELT, NOT THE DISCRIMINATOR, and it is provisional. The stranded carrier re-issues
-- one Move per carrier per ScanInterval (50), so two carriers over the 800-tick window are about 32
-- re-issues plus the module's ordinary loading and top-up traffic. 120 sits above healthy traffic and
-- below a genuine per-scan re-issue by both carriers. Re-derive it from the first GREEN run.
local ORDER_MODULE = "MountedTransportBotModule"
local ORDER_BUDGET = 120
local ORDER_WINDOW_TICKS = 800

Carriers = {}
Riflemen = {}

WorldLoaded = function()
	local USAbot = Player.GetPlayer("USA-bot")

	for _, p in ipairs({ { 6, 26 }, { 6, 30 } }) do
		Carriers[#Carriers + 1] = Actor.Create("bradley", true, {
			Owner = USAbot,
			Location = CPos.New(p[1], p[2]),
			Facing = Angle.East,
		})
	end

	-- Inside the 14-cell reserve bubble around the Supply Route at 3,28 and within a couple of cells
	-- of a carrier, so boarding is a short walk: the load has to finish well inside the run budget for
	-- the delivery it precedes to be the thing under test.
	for _, p in ipairs({ { 4, 25 }, { 5, 27 }, { 7, 27 }, { 4, 31 }, { 5, 29 }, { 7, 29 } }) do
		Riflemen[#Riflemen + 1] = Actor.Create("e3.america", true, {
			Owner = USAbot,
			Location = CPos.New(p[1], p[2]),
			Facing = Angle.East,
		})
	end

	TestHarness.Select(Carriers[1])

	-- Running state for the verdict, all keyed by index into Carriers.
	local loadedAtTick = {}     -- tick the hold first became non-empty
	local loadedAtX = {}        -- where it was standing at that moment
	local advancedBy = {}       -- best eastward gain since then, in cells
	local unloadedAfter = {}    -- ticks from loading to the hold being empty again
	local everLoaded = false

	local crossed = nil
	local tick = 0

	local function forEachLiveCarrier(fn)
		for i, c in ipairs(Carriers) do
			if not c.IsDead and c.IsInWorld then
				fn(i, c)
			end
		end
	end

	local function sample()
		tick = tick + 1

		forEachLiveCarrier(function(i, c)
			local x = c.Location.X

			-- THE INVARIANT. One observation inside or beyond the band is a failure even if the
			-- carrier comes back: the border is a barrier, not a rubber band. A LOADED carrier over
			-- the line would also be the worst possible version of this bug -- infantry teleported
			-- past the rule rather than merely stalled behind it.
			if crossed == nil and x >= BAND_WEST_EDGE then
				crossed = string.format(
					"bot carrier %s reached x=%d at tick %d (band starts at x=%d) -- the border did "
					.. "not hold, and it was carrying %d passengers",
					c.Type, x, tick, BAND_WEST_EDGE, c.PassengerCount)
			end

			if c.PassengerCount > 0 then
				if loadedAtTick[i] == nil then
					loadedAtTick[i] = tick
					loadedAtX[i] = x
					advancedBy[i] = 0
					everLoaded = true
				end

				local gained = x - loadedAtX[i]
				if gained > advancedBy[i] then
					advancedBy[i] = gained
				end
			elseif loadedAtTick[i] ~= nil and unloadedAfter[i] == nil then
				-- Empty again after having been loaded: the delivery completed. Recorded once so a
				-- later reload for a second trip cannot overwrite the first trip's answer.
				unloadedAfter[i] = tick - loadedAtTick[i]
			end
		end)

		if crossed ~= nil then
			Test.Fail(crossed)
			return
		end

		Trigger.AfterDelay(1, sample)
	end

	sample()

	-- Order-count window, opened late so the count covers a stretch in which every carrier is past
	-- loading and is either delivering or stranded.
	local windowStartOrders = nil
	local totalStartOrders = nil
	Trigger.AfterDelay(RUN_TICKS - ORDER_WINDOW_TICKS, function()
		windowStartOrders = Test.BotOrdersQueued(USAbot, ORDER_MODULE)
		totalStartOrders = Test.BotOrdersQueued(USAbot)
	end)

	Trigger.AfterDelay(RUN_TICKS, function()
		-- SCENARIO HEALTH, NOT THE DEFECT, and the distinction is the whole reason this is checked
		-- first and says so. If no carrier ever took a passenger aboard then the delivery this test is
		-- about was never attempted, and a pass here would be vacuous in exactly the direction that
		-- hides a regression. The likely causes are the offensive module poaching the riflemen into
		-- its free pool before boarding was ordered, or the drop cell resolving somewhere that made
		-- the module decline the task -- both scenario tuning, neither the border.
		if not everLoaded then
			Test.Fail(string.format(
				"no carrier ever loaded a passenger in %d ticks: the delivery under test was never "
				.. "attempted, so this run says nothing about the border. Look for [exp-transport] "
				.. "task-created / no-task lines in debug.log -- this is scenario tuning, not the fix",
				RUN_TICKS))
			return
		end

		local stranded = nil
		local held = nil
		for i = 1, #Carriers do
			if loadedAtTick[i] ~= nil then
				local elapsed = tick - loadedAtTick[i]

				-- THE DEFECT. A loaded carrier that has not moved is one whose delivery Move was
				-- refused at the border and re-issued, refused and re-issued, since it loaded.
				if stranded == nil and elapsed >= ADVANCE_DEADLINE_TICKS and advancedBy[i] < ADVANCE_CELLS then
					stranded = string.format(
						"carrier %d loaded at tick %d (x=%d) and advanced only %d cells in %d ticks "
						.. "(need %d): its delivery Move is being refused at the DEFCON 3 border and "
						.. "re-issued every scan -- look for repeated [exp-transport] "
						.. "delivery-move-reissued lines for this carrier in debug.log",
						i, loadedAtTick[i], loadedAtX[i], advancedBy[i], elapsed, ADVANCE_CELLS)
				end

				-- Advanced but never put the infantry down: dead weight further east. Distinguished
				-- from the above so the two causes are never confused in a report.
				if held == nil and unloadedAfter[i] == nil and elapsed >= UNLOAD_DEADLINE_TICKS then
					held = string.format(
						"carrier %d still holds %d passengers %d ticks after loading (advanced %d "
						.. "cells): it reached a destination it never reported arriving at, so the "
						.. "cell it was ordered to and the cell arrival is measured against have "
						.. "drifted apart",
						i, Carriers[i].PassengerCount, elapsed, advancedBy[i])
				end
			end
		end

		if stranded ~= nil then
			Test.Fail(stranded)
			return
		end

		if held ~= nil then
			Test.Fail(held)
			return
		end

		if windowStartOrders == nil or totalStartOrders == nil then
			Test.Fail("order window never opened -- the run budget and the window are inconsistent")
			return
		end

		local issued = Test.BotOrdersQueued(USAbot, ORDER_MODULE) - windowStartOrders
		if issued > ORDER_BUDGET then
			Test.Fail(string.format(
				"%s queued %d orders in the last %d ticks (budget %d): the delivery is churning -- "
				.. "re-offering a destination it cannot reach every scan. NOTE this counts that "
				.. "module alone, so production and supply traffic cannot be the cause",
				ORDER_MODULE, issued, ORDER_WINDOW_TICKS, ORDER_BUDGET))
			return
		end

		local summary = {}
		for i = 1, #Carriers do
			if loadedAtTick[i] ~= nil then
				summary[#summary + 1] = string.format("carrier %d advanced %d cells, unloaded after %s ticks",
					i, advancedBy[i], unloadedAfter[i] ~= nil and tostring(unloadedAfter[i]) or "n/a")
			end
		end

		Test.Pass(string.format("%s; %d %s orders in the last %d ticks (total bot orders %d)",
			table.concat(summary, "; "), issued, ORDER_MODULE, ORDER_WINDOW_TICKS,
			Test.BotOrdersQueued(USAbot) - totalStartOrders))
	end)
end
