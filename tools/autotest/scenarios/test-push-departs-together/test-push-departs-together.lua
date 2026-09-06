-- AUTO TEST: the opening push must LEAVE THE BEACHHEAD AS A BODY.
--
-- PIPELINE item 64. The user's complaint is about departure, not arrival: "that
-- initial tank pushes forward on its own". Everything about this file follows from
-- refusing to measure the easy thing instead.
--
-- WHY NOT "ARE THE INFANTRY NEAR THE TANK". Because that predicate is already known
-- to pass with the fix under test switched off. test-combined-arms-rendezvous.lua:6-13
-- records it: infantry are armed, so PoiOffensiveBotModule.StageFreePool recruits them
-- into the free pool and AttackMoves them to the SAME staging anchor as the armour, on
-- foot, from tick 3. Everyone converges on the muster eventually and proximity goes true
-- without the mechanism under test running at all. The anchor is a common destination;
-- the DEPARTURE LINE is crossed once, by each unit, at a time nothing else determines.
--
-- THE THREE NUMBERS, all read from the same event stream (per-unit line crossings).
--
-- THREE LINES, NOT ONE, AND THE FIRST REVISION OF THIS FILE GOT THAT WRONG. Run
-- 260905_183118 measured everything against x=10 and could not tell "walked to the
-- muster" from "advanced": the staging anchor is (14,16) with slots spanning x in
-- [10,18], so x=10 is INSIDE the muster ring and x=20 is outside it. A single line
-- cannot serve both clauses — sited at 10 the advance clauses count a muster walk as an
-- advance; sited at 20 the solo clause goes blind to the free-pool leak, because a
-- staged lone unit never reaches 20 and the control would PASS with the gate off.
--
--   d3  SOLO DEPARTURE, at the BEACHHEAD LINE x=10 - how many of the push crossed it
--       within SoloWindowTicks of the FIRST one. PASS >= 2.
--       This is the free-pool clause and the line is deliberately inside the muster
--       ring, because walking to the muster alone is exactly what it forbids.
--       PoiOffenseMath.DesiredAxisCount returns 0 below EarlyMinAxisSize (2), so a
--       single unit forms no axis, falls to the free pool, and StageFreePool
--       AttackMoves it forward ONE ORDER PER UNIT with no minimum count
--       (PoiOffensiveBotModule.cs:2769-2771, groupedActors: new[] { u }).
--
--   d1  ADVANCE INTERVAL, at the ADVANCE LINE x=20 - ticks between the first and last
--       crossing of it.                   PASS <= 300, AND every living spawned unit
--                                         must have advanced at all.
--       Beyond every reachable staging slot, so crossing it means the unit is going
--       somewhere, not mustering.
--
--       THE COUNT GATE IS NOT DECORATION AND THE FIRST TWO REVISIONS SHIPPED WITHOUT IT.
--       Run 260905_212326_p13005 read d1 = 0/300 and the clause counted as satisfied. It
--       was not: only the two tanks ever crossed x=20, all four riflemen read adv@never,
--       and an interval over "the units that advanced" scores a PERFECT 0 when two-thirds
--       of the push never advances. Min/max over a filtered set rewards the set being
--       empty, so the clause was unfalsifiable exactly when the push was smallest - the
--       case this item is about. The repair is a companion COUNT, not a tighter threshold.
--
--   d2  SPREAD AT FIRST MIDLINE CROSSING - Chebyshev bounding-box extent over every
--       push unit in the world at the tick the first one crosses x=33.
--                                         PASS <= 8 cells.
--
-- ALL THREE MUST PASS. The failure note carries all three plus the per-unit crossing
-- ticks, because "departed staggered" and "nothing ever crossed" demand opposite
-- responses and a bare timeout cannot tell them apart: the first is the defect, the
-- second means the axis dropped or staging is inert and this run measured nothing.
--
-- TWO LUA TRAPS THIS FILE AVOIDS, both from AUTOTEST.md:
--   * The third argument to AssertWithin is evaluated EAGERLY at registration, so a
--     counter interpolated into a plain string reports its value at setup - usually
--     zero - forever. Every diagnostic here is built INSIDE the predicate, or inside
--     the FUNCTION form of the timeout reason, which the harness evaluates at timeout.
--   * IsDead is true for a passenger inside a Cargo. Nothing here rides anything, but
--     the world-membership tests below are written on IsInWorld so the idiom does not
--     spread.

-- ---------------------------------------------------------------------------
-- Geometry. Every constant is derived in map.yaml's header; read it before editing.
-- ---------------------------------------------------------------------------

local BeachheadLineX = 10       -- the SR's own forward edge; INSIDE the muster ring (slots x in [10,18])
local AdvanceLineX = 20         -- outside it: past here a unit is advancing, not mustering
local MidlineX = 33             -- perpendicular bisector of OwnSR(8,16) -> OpponentSR(58,16)

local PassSoloDeparture = 2     -- d3: units across the BEACHHEAD line in the first departure's window
local PassAdvanceInterval = 300 -- d1: ticks, first to last crossing of the ADVANCE line
local PassSpreadCells = 8       -- d2: Chebyshev extent at the first midline crossing

-- d3 counts everyone who leaves WITH the first unit, and "with" has to be a window rather
-- than an instant: two tanks released by one grouped order start from adjacent cells and
-- will not cross a column on the same tick. 50 ticks is chosen against the module's own
-- clock — ReevaluateInterval is 100, so units released on ONE eval land inside this window
-- and units released on CONSECUTIVE evals cannot. Measuring at the exact tick instead would
-- report d3=1 for a push that departed perfectly together, i.e. a false RED on the clause
-- the free-pool gate is meant to flip.
local SoloWindowTicks = 50

-- The measurement window closes once everyone has spawned AND the first midline crossing
-- has been seen AND every living unit has advanced, or at HardCloseTick, whichever comes
-- first. THE ADVANCE TERM IS PART OF THE CLOSE FOR THE SAME REASON THE COUNT GATE EXISTS:
-- closing at the first crossing reads d1 at the instant the FASTEST unit arrives, which is
-- necessarily before the slowest one has advanced, so the interval would be measured over a
-- population the close condition itself truncated. An abrams is Speed 90 and a rifleman is
-- Speed 25 (3.6x), so that truncation is not a corner case here - it is the normal run. The AssertWithin deadline is
-- strictly larger, so reaching IT means the predicate never got as far as its own
-- close condition - a scenario bug, reported as such rather than as a defect.
-- Sized from run 260905_183118, not from feel. There the axis was ordered to the objective
-- at t519 and was still 48 cells short at t819 — an abrams is Speed: 90 (~0.088 cells/tick
-- nominal, ~0.05 measured once turning and the axis/staging beat are paid), so ~29 cells
-- from the spawn line to the midline is a 600-900 tick walk that does not START until the
-- axis forms. 1200 cut the run off before anything could cross and produced the "NOTHING
-- EVER CROSSED THE MIDLINE" diagnosis, which was true and useless.
local HardCloseTick = 2400
local DeadlineSeconds = 100     -- 100 * TestHarness.TicksPerSecond(25) = 2500 ticks

-- ---------------------------------------------------------------------------
-- The push. Created from Lua, not placed in map.yaml, because the reinforcement
-- DRIBBLE is the input under test - six actors placed at t=0 all enter the free pool
-- in one eval and there is nothing left to depart piecemeal.
--
-- THE FIRST GAP IS 225 TICKS AND THAT IS THE LOAD-BEARING NUMBER. PoiOffensiveBotModule
-- re-evaluates every ReevaluateInterval = 100 ticks (ai.yaml:368). A solo window shorter
-- than one interval can close before StageFreePool ever sees a pool of one, so the
-- clause the free-pool gate fixes would never be exercised and d3 would pass with the
-- gate off - a control that passes when it is required to fail.
--
-- Spawn cells are all x <= 5, i.e. BEHIND the Supply Route. Every reachable staging slot
-- is x in [10,18] (map.yaml's header derives this), so a staging order always carries a
-- unit across the departure line. Spawning on top of the SR instead lets SpreadSlot hand
-- the lone tank a cell it is already standing on, and then it does not move at all.
-- ---------------------------------------------------------------------------

local Push = {
	{ name = "tank-1",  type = "abrams",     x = 4, y = 16, tick =  25 },
	{ name = "tank-2",  type = "abrams",     x = 4, y = 18, tick = 250 },
	{ name = "rifle-1", type = "e3.america", x = 4, y = 14, tick = 320 },
	{ name = "rifle-2", type = "e3.america", x = 4, y = 20, tick = 390 },
	{ name = "rifle-3", type = "e3.america", x = 5, y = 15, tick = 460 },
	{ name = "rifle-4", type = "e3.america", x = 5, y = 19, tick = 530 },
}

-- Per-unit state, indexed alongside Push.
local Unit = {}        -- the actor once created
local SpawnedAt = {}   -- tick it entered the world
local LeftAt = {}      -- first tick its cell X reached BeachheadLineX
local AdvancedAt = {}  -- first tick its cell X reached AdvanceLineX
local CrossedAt = {}   -- first tick its cell X reached MidlineX
local DiedAt = {}

local FirstLeftTick = nil
local FirstAdvanceTick = nil
local LastAdvanceTick = nil
local SoloAtFirstDeparture = 0     -- d3
local SoloLatched = false          -- d3's window has matured; stop recounting
local FirstCrossTick = nil
local SpreadAtFirstCross = nil     -- d2
local Verdict = nil                -- set once, so the window closes exactly once

local function Chebyshev(ax, ay, bx, by)
	local dx = math.abs(ax - bx)
	local dy = math.abs(ay - by)
	if dx > dy then return dx end
	return dy
end

local function Alive(i)
	local a = Unit[i]
	return a ~= nil and not a.IsDead and a.IsInWorld
end

-- "name born@N left@M adv@K x=P" for every unit, so the note names WHICH unit left WHEN
-- and WHERE IT ENDED UP. The end position is what run 260905_183118 most wanted and did
-- not have: "crossed=0/6" is compatible with the push sitting at the muster and with it
-- being one cell short of the midline, and those are different findings.
local function DepartureRoll()
	local parts = {}
	for i = 1, #Push do
		local left = "never"
		if LeftAt[i] ~= nil then left = tostring(LeftAt[i]) end
		local adv = "never"
		if AdvancedAt[i] ~= nil then adv = tostring(AdvancedAt[i]) end
		local born = "unspawned"
		if SpawnedAt[i] ~= nil then born = tostring(SpawnedAt[i]) end
		local where = "-"
		if Unit[i] ~= nil and not Unit[i].IsDead and Unit[i].IsInWorld then
			where = Unit[i].Location.X .. "," .. Unit[i].Location.Y
		end
		local fate = ""
		if DiedAt[i] ~= nil then fate = " died@" .. DiedAt[i] end
		parts[#parts + 1] = Push[i].name .. " born@" .. born .. " left@" .. left ..
			" adv@" .. adv .. " at=" .. where .. fate
	end
	return table.concat(parts, ", ")
end

local function Report(tick)
	local left = 0
	local advanced = 0
	local crossed = 0
	for i = 1, #Push do
		if LeftAt[i] ~= nil then left = left + 1 end
		if AdvancedAt[i] ~= nil then advanced = advanced + 1 end
		if CrossedAt[i] ~= nil then crossed = crossed + 1 end
	end

	local interval = "n/a"
	if FirstAdvanceTick ~= nil and LastAdvanceTick ~= nil then
		interval = tostring(LastAdvanceTick - FirstAdvanceTick)
	end

	local spread = "n/a"
	if SpreadAtFirstCross ~= nil then spread = tostring(SpreadAtFirstCross) end

	return "tick=" .. tick ..
		"; d3 solo-departure=" .. SoloAtFirstDeparture .. "/" .. PassSoloDeparture ..
		" (units across the BEACHHEAD line x=" .. BeachheadLineX .. " within " .. SoloWindowTicks ..
		" ticks of the FIRST one, which crossed at tick " .. tostring(FirstLeftTick) .. ")" ..
		"; d1 advance-interval=" .. interval .. "/" .. PassAdvanceInterval ..
		" ticks (ADVANCE line x=" .. AdvanceLineX .. ")" ..
		"; d2 spread-at-first-midline-crossing=" .. spread .. "/" .. PassSpreadCells ..
		" cells (first crossing of x=" .. MidlineX .. " at tick " .. tostring(FirstCrossTick) .. ")" ..
		"; left=" .. left .. "/" .. #Push .. " advanced=" .. advanced .. "/" .. #Push ..
		" crossed=" .. crossed .. "/" .. #Push ..
		"; roll: " .. DepartureRoll()
end

-- Decide, once, when the window closes. Returns true (pass) or a "fail: ..." string.
local function Decide(tick)
	local why = {}

	if FirstCrossTick == nil then
		-- Nothing ever reached the midline. This is NOT the staggered-departure defect
		-- and must not be read as one: the axis dropped its target mid-approach, or
		-- staging never armed, or the push never left at all. Read the run's
		-- [exp-offense] axis-new / axis retire and [exp-staging] lines before treating
		-- any other number here as meaningful.
		why[#why + 1] = "NOTHING EVER CROSSED THE MIDLINE - this run did not measure " ..
			"departure spread at all. Check debug.log [exp-offense] axis-new/retire and " ..
			"[exp-staging] before reading anything else here"
	end

	if FirstLeftTick == nil then
		why[#why + 1] = "no unit ever crossed the beachhead line - the push never left at all"
	elseif SoloAtFirstDeparture < PassSoloDeparture then
		why[#why + 1] = "d3 THE FIRST UNIT LEFT ALONE"
	end

	if FirstAdvanceTick == nil then
		why[#why + 1] = "no unit ever crossed the ADVANCE line - the push mustered but never advanced"
	else
		-- The companion COUNT gate. Every unit that was spawned and is still alive must have
		-- advanced; a unit that died is excused (it cannot advance and its loss is not this
		-- clause's subject). Checked BEFORE the interval, because an interval computed over a
		-- short population is the number that must not be trusted, not a second opinion on it.
		local owed = 0
		for i = 1, #Push do
			if SpawnedAt[i] ~= nil and DiedAt[i] == nil and AdvancedAt[i] == nil then
				owed = owed + 1
			end
		end

		if owed > 0 then
			why[#why + 1] = "d1 " .. owed .. " LIVING UNIT(S) NEVER ADVANCED - the interval " ..
				"beside this is computed only over the units that did, so read it as a lower bound"
		end

		if LastAdvanceTick ~= nil and (LastAdvanceTick - FirstAdvanceTick) > PassAdvanceInterval then
			why[#why + 1] = "d1 advances too far apart"
		end
	end

	if SpreadAtFirstCross ~= nil and SpreadAtFirstCross > PassSpreadCells then
		why[#why + 1] = "d2 the push was strung out at the midline"
	end

	if #why == 0 then
		return true
	end

	return "fail: " .. table.concat(why, " + ") .. " || " .. Report(tick)
end

WorldLoaded = function()
	TestHarness.FocusBetween(OwnSR, OpponentSR)

	local usa = Player.GetPlayer("USA-bot")

	for i = 1, #Push do
		local p = Push[i]
		Trigger.AfterDelay(p.tick, function()
			Unit[i] = Actor.Create(p.type, true, {
				Owner = usa,
				Location = CPos.New(p.x, p.y),
			})
			SpawnedAt[i] = DateTime.GameTime
		end)
	end

	TestHarness.AssertWithin(DeadlineSeconds, function()
		local tick = DateTime.GameTime

		if Verdict ~= nil then
			return Verdict
		end

		-- 1. Latch deaths and line crossings. Monotonic: a crossing is evidence, not a
		--    state, so a unit later pushed back still counts as having left.
		for i = 1, #Push do
			local a = Unit[i]
			if a ~= nil then
				if a.IsDead then
					if DiedAt[i] == nil then DiedAt[i] = tick end
				elseif a.IsInWorld then
					local loc = a.Location
					if LeftAt[i] == nil and loc.X >= BeachheadLineX then
						LeftAt[i] = tick
					end
					if AdvancedAt[i] == nil and loc.X >= AdvanceLineX then
						AdvancedAt[i] = tick
					end
					if CrossedAt[i] == nil and loc.X >= MidlineX then
						CrossedAt[i] = tick
					end
				end
			end
		end

		-- 2. Extremes on each line. d3 reads the beachhead line, d1 the advance line.
		for i = 1, #Push do
			local l = LeftAt[i]
			if l ~= nil and (FirstLeftTick == nil or l < FirstLeftTick) then FirstLeftTick = l end

			local a2 = AdvancedAt[i]
			if a2 ~= nil then
				if FirstAdvanceTick == nil or a2 < FirstAdvanceTick then FirstAdvanceTick = a2 end
				if LastAdvanceTick == nil or a2 > LastAdvanceTick then LastAdvanceTick = a2 end
			end
		end

		-- d3. Recount every tick until the first departure's window matures, then freeze. Kept
		-- as a running count rather than a single deferred read so the number is meaningful even
		-- if the measurement window closes before the d3 window does.
		if FirstLeftTick ~= nil and not SoloLatched then
			local n = 0
			for i = 1, #Push do
				local l = LeftAt[i]
				if l ~= nil and l <= FirstLeftTick + SoloWindowTicks then n = n + 1 end
			end
			SoloAtFirstDeparture = n
			if tick >= FirstLeftTick + SoloWindowTicks then SoloLatched = true end
		end

		-- 3. The spread instant. Measured over units IN THE WORLD, which deliberately
		--    excludes any that have not spawned yet - that is generous to the code under
		--    test, so a RED here cannot be an artefact of counting absent units.
		if FirstCrossTick == nil then
			for i = 1, #Push do
				if CrossedAt[i] == tick then
					FirstCrossTick = tick
					local minX, maxX, minY, maxY = nil, nil, nil, nil
					for j = 1, #Push do
						if Alive(j) then
							local l = Unit[j].Location
							if minX == nil or l.X < minX then minX = l.X end
							if maxX == nil or l.X > maxX then maxX = l.X end
							if minY == nil or l.Y < minY then minY = l.Y end
							if maxY == nil or l.Y > maxY then maxY = l.Y end
						end
					end
					if minX ~= nil then
						SpreadAtFirstCross = Chebyshev(minX, minY, maxX, maxY)
					end
					break
				end
			end
		end

		-- 4. Live trace. The failure note is one line at one instant; this is the series,
		--    and it is what separates "my predicate never went true" from "my script never
		--    ran" when a run ends without a verdict (lua.log at 0 bytes).
		if tick % 100 == 0 then
			print("[push-departs] " .. Report(tick))
		end

		-- 5. Close the window: everyone spawned AND the first midline crossing seen, or
		--    the hard cap. Deciding early keeps a RED run short; the hard cap is what
		--    turns "nothing happened" into a diagnosis instead of a bare timeout.
		local allSpawned = true
		local allAdvanced = true
		for i = 1, #Push do
			if SpawnedAt[i] == nil then allSpawned = false end
			if SpawnedAt[i] ~= nil and DiedAt[i] == nil and AdvancedAt[i] == nil then
				allAdvanced = false
			end
		end

		if (allSpawned and allAdvanced and FirstCrossTick ~= nil) or tick >= HardCloseTick then
			print("[push-departs] WINDOW CLOSED " .. Report(tick))
			Verdict = Decide(tick)
			return Verdict
		end

		return false
	end, function()
		-- Function form: evaluated AT timeout, so these numbers are real. Reaching this at
		-- all means the predicate never hit its own close condition, which HardCloseTick
		-- above is supposed to make impossible.
		return "the measurement window never closed - HardCloseTick=" .. HardCloseTick ..
			" was not reached inside the AssertWithin deadline, which is a scenario bug, " ..
			"not a bot defect. State at timeout: " .. Report(DateTime.GameTime)
	end)
end
