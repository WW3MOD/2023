-- NUKE PERF ON A POPULATED LATE GAME -- two @experimental bots, Polar Disorder, three Sarmat salvos.
--
-- WHAT THIS ANSWERS AND WHAT demo-nuke-perf ALREADY ANSWERS. demo-nuke-perf measures a detonation
-- with everything that is not a detonation removed -- 448 stationary actors, no bots, no
-- production, no combat, no fog -- and its own README calls the absolute numbers "a floor for what
-- a live game would pay, not an estimate of it". The 2026-09-21 release-readiness audit files that
-- as P1/P3 and asks for this (Part 3 row 11): a nuke fired into a real match, with tick_time p50
-- and max read for windows before, during and after. THE VERDICT IS A READING. Nothing here
-- asserts: there is no AssertWithin, no Test.Pass and no Test.Fail, and no number below is a
-- threshold anybody has agreed. Neither the audit nor tools/nuke-perf names one.
--
-- ==== TICKS, NOT SECONDS ====
-- Timestep is 60 ms, so 16.667 ticks/s -- NOT the 25 that TestHarness.TicksPerSecond carries (a
-- preserved harness convention for AssertWithin budgets, documented in test-helpers.lua, and not
-- the tick rate; CLAUDE.md records that error live at ten sites). Every constant below is RAW
-- TICKS and nothing here routes through a seconds helper.
--
-- ==== WHY THE TICK COUNTS SURVIVE --speed AND THE WALL CLOCK DOES NOT ====
-- run-test.sh --speed N divides world.Timestep in TestModeSpeedMultiplier at IWorldLoaded, so every
-- tick count here is invariant and only wall time moves. THE WALL TIME IS THE EXPENSIVE PART AND IT
-- IS MEASURED, NOT NOMINAL: test-escalation-full-match measured ~12 ticks/s at --speed 4 on an idle
-- machine and ~5.5 on a loaded one, on THIS map with two bots, against the 66.7 that --speed 4
-- nominally asks for. The engine is the bottleneck, not the timestep, so --speed 8 does not double
-- it. HardEndTick below is 10800, which is ~900 s at 12 ticks/s and ~1965 s at 5.5. The run line in
-- description.txt therefore says --timeout 2400, sized off the SLOW row. Sizing off the nominal is
-- what cost test-escalation-full-match its first two runs.
--
-- ==== IT ENDS ON Test.Skip, AGAINST DEMO.md's "no verdict", FOR demo-nuke-perf's REASON ====
-- A watchdog kill does not flush Log's buffered writers, so a run that ends by timeout can lose
-- the very lua.log these readings live in. Ending on a pinned tick makes the exit clean. A `skip`
-- verdict is not a claim that anything passed -- there is nothing here to pass.
--
-- ==== HOW A READING FROM THIS FILE IS DIFFERENT FROM A BENCHMARK CSV ====
-- Test.GetTickTimeMs() reads PerfItem.LastValue off PerfHistory.Items["tick_time"] -- byte for byte
-- the quantity Benchmark.Tick writes to nukeperf-tick_time.csv (Benchmark.cs:31). The difference is
-- that Launch.Benchmark ALSO sets PerfHistory.Sampling (Game.cs:886), and TerrainLighting refuses
-- its parallel sweep while that flag is true (TerrainLighting.cs:316-318) -- so a benchmarked run
-- measures the SERIAL relight, which is not the build the mod ships. This scenario takes no
-- benchmark argument at all and reads the shipped configuration.
--
-- THE READING LAGS TWO TICKS. PerfHistory.Tick() publishes the accumulated total and zeroes it at
-- Game.cs:826; the tick_time sample only adds this tick's cost when its using block closes at :833.
-- A Trigger.OnTick callback runs inside world.Tick() at :824 -- before the publish -- so what it
-- reads is the cost of tick N-2. Every sample below is therefore filed under `tick - 2`, which is
-- the tick it actually measures. (A CSV row lags by ONE, because Benchmark.Tick runs at :834.)
--
-- ==== WHY Trigger.OnTick AND NOT AfterDelay(1, step) ====
-- The usual self-rescheduling loop allocates a DelayedAction and a frame-end task EVERY TICK, and
-- this scenario is measuring per-tick cost. Trigger.OnTick runs on the same World.Tick for free.
--
-- ==== THE SCHEDULE ====
--     tick   300   the `build` window opens -- the whole bot build-up, as a control
--     tick  7000   arming for shot 1 begins (EnsurePower). STOPS as soon as the magazine banks, so
--                  no production property is touched inside the baseline window that follows.
--     tick  7700   `prefire1` opens: 300 ticks of a populated late game with nothing incoming.
--                  THIS IS THE BASELINE EVERY DETONATION NUMBER IS READ AGAINST.
--     tick  8000   SHOT 1 ordered, retried every tick to 8400. Ground zero is computed HERE, from
--                  the live centroid of USA-bot's ground attackers -- see aimAt below.
--      +158        first impact, DERIVED (see ExpectedImpactOffset) and CROSS-CHECKED against
--                  Test.GetImpactEffectCount. `deton1` opens there and runs 400 ticks, which
--                  outlasts ShockwaveEffect's ~400-tick sweep; `recover1` is the 300 after that.
--     tick  9000   arming for shot 2 begins, again stopping on bank.
--     tick  9300   `prefire2` opens.
--     tick  9600   SHOT 2 ordered, then SHOT 3 as soon as the magazine re-arms -- one purchase is
--                  one shot (test-helpers.lua), so the gap is whatever a 5-tick load costs and is
--                  RECORDED rather than pinned. This is the back-to-back pair.
--      +158        `pair` opens there and runs 430 ticks, long enough to hold both salvos' impacts
--                  and both shockwave sweeps; `recover2` follows.
--     tick 10800   hard end. Test.Skip either way.
--
-- ==== WHAT MAKES A RUN INVALID, CHECKED AND PRINTED BY THIS FILE ====
--   * a shot never issued            -> NUKEPOP invalid shot=N ... and the windows are of nothing
--   * a salvo that does not raise the gated-impact counter above the combat rate at all -> same
--   * either army below ArmyFloor at a fire tick -> NUKEPOP underpopulated ...
--   * zero enemy actors under the aim -> NUKEPOP underpopulated aim=...
-- An empty map is indistinguishable from a cheap detonation, which is demo-nuke-perf's own hardest
-- lesson (its run 1 produced a clean 1000-tick log of nothing happening). So every window carries
-- its own census and every failure above names itself in lua.log.

-- ---------------------------------------------------------------------------------------------
-- CONSTANTS
-- ---------------------------------------------------------------------------------------------

local PowerKey  = "SarmatStrike"
local ProxyType = "power.sarmat"

-- THE FIRING PLAYER MUST BE A `russia` FACTION PLAYER. MissileStrikePower@Sarmat declares
-- `Prerequisites: powers.event, player.russia` (player.yaml:238-239); prerequisites are ANDed
-- (TechTree.cs:65-70) and `player.<faction>` is an identity no lobby option can hand out
-- (player.yaml:227-230). demo-nuke-perf spent three runs firing nothing before this was understood.
local FirerName  = "Russia-bot"
local TargetName = "USA-bot"

local BuildWindowOpen = 300    -- skip the load transient; the build-up control starts here

-- Begin buying shot 1 here. Well before prefire1 opens (7700), so the ~6 ticks of production
-- properties EnsurePower touches land in the broad `build` control window and NOT in the baseline
-- the detonation is read against. arm() stops calling it the moment the shot banks.
local ArmTick1   = 7000
local FireTick1  = 8000
local FireDeadline1 = 8400

local ArmTick2   = 9000
local FireTick2  = 9600
local FireDeadline2 = 10000

-- ==== WHERE THE IMPACT TICK COMES FROM, AND WHY IT IS DERIVED RATHER THAN DETECTED ====
-- demo-nuke-perf can anchor on an observed impact because its map is inert: the ONLY thing that
-- can move Test.GetImpactEffectCount there is the salvo. Here two bot armies are shooting at each
-- other, so that counter climbs every few ticks from ordinary combat and the first rise after an
-- order is very unlikely to be a warhead. Anchoring the windows on it would put `deton1` wherever
-- the nearest tank round happened to land.
--
-- So the offset is COMPUTED from the shipped geometry, which is fully determined:
--     entry standoff = mapDiagonal + ApproachMargin        (MissileStrikePower.ApproachFor, and
--                                                           TestHarness.ApproachStandoffCells,
--                                                           which takes MapSize and NOT Bounds)
--     flight ticks   = standoff (wdist) / Speed            (SarmatMissile BallisticMissile Speed
--                                                           1600, nuclear-arsenal.yaml:958)
--     impact         = MissileDelay + PreLaunchTicks + flight
-- MissileDelay is pinned to 60 in this scenario's rules.yaml and PreLaunchTicks is 0, so on this
-- 98x98 map: standoff = sqrt(98^2 + 98^2) + 16 = 154.6 cells = 158303 wdist; 158303 / 1600 = 98
-- ticks of flight; first impact at order + 158. The same arithmetic on demo-nuke-perf's 128x128
-- map gives 201747 / 1600 = 126 and that file's header records exactly 126, which is the check
-- that this derivation is the right one.
--
-- THE OBSERVED COUNTER IS STILL READ, but as a COUNT and not as a timestamp, because a timestamp
-- is exactly the thing a live match cannot supply. Test.GetImpactEffectCount is sampled at three
-- ticks per shot -- (impact - SalvoSpanTicks), impact, and (impact + SalvoSpanTicks) -- which gives
-- the number of gated impacts during the salvo and the number during an equally long stretch of
-- ordinary combat immediately before it. Six RVs must raise the first above the second by at least
-- WarheadsPerSalvo. If they do not, either the salvo did not land where the derivation says or it
-- landed on nothing, and every window for that shot is measuring something else. THE FIRST DRAFT OF
-- THIS FILE RECORDED THE FIRST RISE AFTER THE ORDER INSTEAD, and the offline driver showed exactly
-- why that is worthless here: the synthetic combat rate alone satisfied it, 22 ticks early, on
-- every shot.
local MapCellsX, MapCellsY = 98, 98      -- MapSize from map.yaml. NOT Bounds.
local MissileSpeedWDist = 1600           -- SarmatMissile: BallisticMissile: Speed
local MissileDelayTicks = 60             -- pinned in this scenario's rules.yaml
-- The six RVs land AimPointInterval (12) ticks apart, so the salvo's impacts occupy the 60 ticks
-- from the first one. 72 gives that a tick of slack at each end.
local SalvoSpanTicks = 72
local WarheadsPerSalvo = 6

-- THE COMBAT BASELINE IS THE FLIGHT WINDOW -- the ExpectedImpactOffset ticks between the order and
-- the first impact, during which nothing this scenario fired has landed yet. It is 158 ticks on
-- this map, i.e. 2.2 salvo spans, and it is scaled down to one span before it is subtracted.
--
-- TWO THINGS THE OFFLINE DRIVER SETTLED, neither of which the first draft got right. ONE 72-tick
-- sample of a live battle is far too noisy to subtract: with a synthetic combat impact every nine
-- ticks and all six warheads landing, two adjacent 72-tick samples read 13 and 9, so the rise came
-- out at 4 against 6 warheads and a perfectly good salvo was called a miss. And the obvious fix --
-- sampling three spans BEFORE the impact -- reaches back to (impact - 216), which on this schedule
-- is 58 ticks EARLIER THAN THE ORDER: the shot record does not exist yet, the sample is never
-- taken, and the run dies on an arithmetic-on-nil at the closing sample. The flight window has
-- neither problem because it is bounded by the order at one end and the impact at the other.
--
-- WHAT IT DOES NOT FIX, stated rather than left to be discovered: shot 3 is fired seconds after
-- shot 2, so its flight window overlaps the tail of shot 2's salvo and its combat baseline is
-- inflated by shot 2's warheads. That UNDER-states shot 3's rise, which is the safe direction for
-- a check, but it means the per-shot cross-check is weakest on exactly the pair. The pair's
-- tick_time window does not depend on it.

local ExpectedImpactOffset =
	MissileDelayTicks
	+ math.floor(TestHarness.ApproachStandoffCells(MapCellsX, MapCellsY) * 1024 / MissileSpeedWDist)

local PrefireTicks   = 300     -- length of each pre-shot baseline window
local DetonTailTicks = 400     -- ShockwaveEffect sweeps ~400 ticks past its own impact
local PairTailTicks  = 430     -- two salvos, so the tail starts later relative to the first impact
local RecoverTicks   = 300

local HardEndTick = 10800

-- Census cadence outside window boundaries. GetGroundAttackers walks ActorsHavingTrait<AttackBase>,
-- which is real work inside a tick this file is measuring, so it is NOT called per tick.
local CensusInterval = 500

-- Below this many ground attackers on either side at a fire tick, the run says so in its own log.
-- NOT a threshold anybody has agreed and NOT a pass/fail -- it is the line under which the word
-- "populated" stops being honest. Pick it up from the readings and revise it once there are any.
local ArmyFloor = 12

-- Histogram resolution. 1 ms bins, which is the resolution the rest of the perf tooling already
-- works at -- PerfTimer.cs:26 formats ms as an INTEGER and tools/nuke-perf/README.md calls a 1 ms
-- floor "1.7% against a 60 ms tick budget ... fine as a floor". p50/p90/p99 are therefore reported
-- as the 1 ms bin the percentile falls in, i.e. exact to within 1 ms and biased low; `max` is
-- carried at full precision because a single worst tick is the one number that must not be rounded.
local MaxBinMs = 255

-- The tick budget a player feels. 60 ms is one timestep: a tick over it is a tick the simulation
-- could not deliver on time. Carried as a REPORTED COUNT, not a pass condition.
local BudgetMs = 60

-- ---------------------------------------------------------------------------------------------
-- STATE
-- ---------------------------------------------------------------------------------------------

local tick = 0
local Firer, Target
local windows = {}        -- ordered list of window records
local shots = {}          -- [1..3] = { ordered, expectedImpact, impacts*, aim, army... }
local armed = { [1] = false, [2] = false, [3] = false }
local armStatus = { [1] = "not-started", [2] = "not-started", [3] = "not-started" }
local invalid = {}
local notes = {}

local function say(msg) print("NUKEPOP " .. msg) end

local function flag(fmt, ...) invalid[#invalid + 1] = string.format(fmt, ...) end
local function note(fmt, ...) notes[#notes + 1] = string.format(fmt, ...) end

-- ---------------------------------------------------------------------------------------------
-- HISTOGRAMS
-- ---------------------------------------------------------------------------------------------

local function newWindow(name)
	local w = {
		name = name,
		from = nil, to = nil,       -- inclusive bounds, in COST ticks (see the two-tick lag above)
		n = 0, sum = 0, max = 0, over = 0, overBudget = 0,
		bins = {},
		census = "unset",
	}
	windows[#windows + 1] = w
	return w
end

local function addSample(w, ms)
	w.n = w.n + 1
	w.sum = w.sum + ms
	if ms > w.max then w.max = ms end
	if ms >= BudgetMs then w.overBudget = w.overBudget + 1 end

	local b = math.floor(ms)
	if b < 0 then b = 0 end
	if b > MaxBinMs then
		w.over = w.over + 1
	else
		w.bins[b] = (w.bins[b] or 0) + 1
	end
end

-- The 1 ms bin the given percentile falls in. Returns -1 for an empty window, and MaxBinMs + 1 when
-- the percentile lands in the overflow -- both of which are readings, not errors.
local function percentile(w, pct)
	if w.n == 0 then
		return -1
	end

	local target = math.floor(w.n * pct / 100)
	if target < 1 then target = 1 end

	local seen = 0
	for b = 0, MaxBinMs do
		local c = w.bins[b]
		if c ~= nil then
			seen = seen + c
			if seen >= target then return b end
		end
	end

	return MaxBinMs + 1
end

-- ---------------------------------------------------------------------------------------------
-- CENSUS
-- ---------------------------------------------------------------------------------------------

local function armyOf(player)
	if player == nil then return -1 end
	return #player.GetGroundAttackers()
end

local function censusLine()
	return string.format("usa_army=%d rus_army=%d", armyOf(Target), armyOf(Firer))
end

-- ---------------------------------------------------------------------------------------------
-- AIM
-- ---------------------------------------------------------------------------------------------

-- Ground zero is the CENTROID OF THE TARGET'S LIVE GROUND ARMY, computed once per shot at the fire
-- tick and clamped inside the bounds.
--
-- WHY NOT A FIXED CELL, which is what demo-nuke-perf uses and what makes two of its runs
-- subtractable. Because the single thing that can silently void this scenario is a salvo that lands
-- on nothing, and on a map where two bots are manoeuvring there is no fixed cell that can be
-- promised to have an army on it at tick 8000. This scenario is a READING of what a tick costs when
-- a nuke lands on a live army, not an A/B rig -- demo-nuke-perf remains the A/B rig, and the price
-- of that choice is that two runs of THIS file are not subtractable at the millisecond. Every input
-- that makes them differ is printed with the shot, so a reader can see how far apart two runs were
-- rather than having to assume.
--
-- Bounds are 1,1,96,96. Clamped to 4..93 so the six RVs, which fall on a ring of
-- AimPointFallbackSpread (shipped 24c0) around this point, mostly stay on the playfield.
local function aimAt(player)
	local units = player.GetGroundAttackers()
	local n = #units
	if n == 0 then
		return nil, 0
	end

	local sx, sy = 0, 0
	for i = 1, n do
		local c = units[i].Location
		sx = sx + c.X
		sy = sy + c.Y
	end

	local x = math.floor(sx / n)
	local y = math.floor(sy / n)
	if x < 4 then x = 4 end
	if x > 93 then x = 93 end
	if y < 4 then y = 4 end
	if y > 93 then y = 93 end

	-- How much of that army the aim point actually covers, at two radii: 24 cells is roughly one
	-- RV's blast band, 48 is roughly the whole six-RV pattern. A shot with zero under it at 48 is a
	-- shot into empty ground however healthy the census looks.
	local near, wide = 0, 0
	for i = 1, n do
		local c = units[i].Location
		local dx, dy = c.X - x, c.Y - y
		local d2 = dx * dx + dy * dy
		if d2 <= 24 * 24 then near = near + 1 end
		if d2 <= 48 * 48 then wide = wide + 1 end
	end

	return { X = x, Y = y }, n, near, wide
end

-- ---------------------------------------------------------------------------------------------
-- WINDOWS
-- ---------------------------------------------------------------------------------------------

local wBuild    = newWindow("build")
local wPrefire1 = newWindow("prefire1")
local wFlight1  = newWindow("flight1")
local wDeton1   = newWindow("deton1")
local wRecover1 = newWindow("recover1")
local wPrefire2 = newWindow("prefire2")
local wPair     = newWindow("pair")
local wRecover2 = newWindow("recover2")

-- The three that are known before the run starts.
wBuild.from,    wBuild.to    = BuildWindowOpen, FireTick1 - PrefireTicks - 1
wPrefire1.from, wPrefire1.to = FireTick1 - PrefireTicks, FireTick1 - 1
wPrefire2.from, wPrefire2.to = FireTick2 - PrefireTicks, FireTick2 - 1

-- ---------------------------------------------------------------------------------------------
-- FIRING
-- ---------------------------------------------------------------------------------------------

-- Buy a shot into the magazine. Called every tick between its arm tick and its fire tick, and it
-- STOPS as soon as the shot banks -- EnsurePower reaches for production properties, which is work
-- inside a tick this file measures, and the baseline windows must not carry it.
local function arm(slot)
	if armed[slot] then return end

	local ready, status = TestHarness.EnsurePower(Firer, ProxyType, PowerKey, tick)
	armStatus[slot] = status
	if ready then
		armed[slot] = true
		armStatus[slot] = "banked"
		say(string.format("armed shot=%d tick=%d", slot, tick))
	end
end

local function fire(slot)
	local aim, army, near, wide = aimAt(Target)
	if aim == nil then
		flag("shot %d had no target army at all: %s owns zero ground attackers at tick %d",
			slot, TargetName, tick)
		return false
	end

	local status = Test.ActivateSupportPower(Firer, PowerKey, CPos.New(aim.X, aim.Y))
	if status ~= "issued" then
		return false
	end

	shots[slot] = {
		ordered = tick,
		expectedImpact = tick + ExpectedImpactOffset,
		impactsBefore = Test.GetImpactEffectCount(),   -- gated-impact total at the ORDER tick
		impactsAtStart = nil,    -- ... at expectedImpact
		impactsAfter = nil,      -- ... at (expectedImpact + SalvoSpanTicks)
		aim = string.format("%d,%d", aim.X, aim.Y),
		targetArmy = army,
		near = near,
		wide = wide,
		firerArmy = armyOf(Firer),
	}

	say(string.format(
		"order shot=%d tick=%d groundzero=%s expected_impact=%d target_army=%d under_r24=%d "
		.. "under_r48=%d firer_army=%d",
		slot, tick, shots[slot].aim, shots[slot].expectedImpact, army, near, wide,
		shots[slot].firerArmy))

	if army < ArmyFloor then
		flag("shot %d fired into an army of %d, below ArmyFloor %d -- this is not a late game",
			slot, army, ArmyFloor)
	end

	if wide == 0 then
		flag("shot %d aimed at %s but zero target actors lie within 48 cells of it",
			slot, shots[slot].aim)
	end

	return true
end

-- ---------------------------------------------------------------------------------------------
-- THE TICK
-- ---------------------------------------------------------------------------------------------

-- Called the moment a shot is accepted. Every bound is arithmetic on the order tick, so the
-- windows exist before anything lands and a failure to detect an impact cannot move them.
local function openDetonationWindows(slot)
	local impactTick = shots[slot].expectedImpact

	if slot == 1 then
		wFlight1.from = shots[slot].ordered
		wFlight1.to   = impactTick - 1
		wDeton1.from  = impactTick
		wDeton1.to    = impactTick + DetonTailTicks
		wRecover1.from = wDeton1.to + 1
		wRecover1.to   = wRecover1.from + RecoverTicks - 1
	elseif slot == 2 then
		wPair.from = impactTick
		wPair.to   = impactTick + PairTailTicks
		wRecover2.from = wPair.to + 1
		wRecover2.to   = wRecover2.from + RecoverTicks - 1
	end
end

local function report()
	say(string.format(
		"readings tickrate_note=60ms_timestep budget_ms=%d bins_ms=1 impact_offset=%d "
		.. "standoff_cells=%.1f",
		BudgetMs, ExpectedImpactOffset,
		TestHarness.ApproachStandoffCells(MapCellsX, MapCellsY)))

	for i = 1, #windows do
		local w = windows[i]
		local mean = -1
		if w.n > 0 then mean = math.floor(w.sum * 10 / w.n) / 10 end

		say(string.format(
			"window name=%s from=%s to=%s n=%d p50=%d p90=%d p99=%d max=%.3f mean=%.1f "
			.. "over_budget=%d over_bins=%d census=%s",
			w.name,
			tostring(w.from), tostring(w.to),
			w.n, percentile(w, 50), percentile(w, 90), percentile(w, 99),
			w.max, mean, w.overBudget, w.over, w.census))
	end

	for slot = 1, 3 do
		local s = shots[slot]
		if s == nil then
			say(string.format("shot slot=%d NOT-FIRED arm_status=%s", slot, armStatus[slot]))
			flag("shot %d was never issued (arming ended at '%s'); every window that depends on it "
				.. "is a window of nothing", slot, armStatus[slot])
		else
			local sampled = s.impactsAfter ~= nil and s.impactsAtStart ~= nil
			local salvo, combat, rise = -1, -1, -1
			if sampled then
				salvo = s.impactsAfter - s.impactsAtStart
				combat = (s.impactsAtStart - s.impactsBefore)
					* SalvoSpanTicks / ExpectedImpactOffset
				rise = salvo - combat
			end

			say(string.format(
				"shot slot=%d ordered=%d expected_impact=%d salvo_impacts=%d combat_per_span=%.1f "
				.. "rise=%.1f groundzero=%s target_army=%d under_r24=%d under_r48=%d",
				slot, s.ordered, s.expectedImpact, salvo, combat, rise,
				s.aim, s.targetArmy, s.near, s.wide))

			-- GRADED, NOT BINARY, and the grading is the honest part. A six-RV salvo should raise
			-- the gated-impact count by about six over the combat rate, but the combat rate is
			-- estimated from one stretch of a battle and the battle does not hold still. So a rise
			-- at or below zero is called invalid -- the salvo added nothing measurable and the
			-- windows are measuring something else -- while a positive rise short of six is a note
			-- that says the evidence is weak rather than a claim that the shot missed.
			if not sampled then
				flag("shot %d: the impact counter was never sampled across its salvo window (the "
					.. "run ended first, at tick %d) -- nothing cross-checks where its detonation "
					.. "landed", slot, tick)
			elseif rise <= 0 then
				flag("shot %d: %d gated impacts across its derived salvo window against a combat "
					.. "rate of %.1f per span (scaled from its own flight window), a rise of %.1f "
					.. "for a %d-warhead salvo. Either it did not land where the derived offset %d "
					.. "says, or it landed on nothing -- every window for this shot is then "
					.. "measuring something else",
					slot, salvo, combat, rise, WarheadsPerSalvo, ExpectedImpactOffset)
			elseif rise < WarheadsPerSalvo then
				note("shot %d rose by only %.1f gated impacts for a %d-warhead salvo (salvo %d "
					.. "against a combat rate of %.1f per span). The salvo landed, but this is weak "
					.. "evidence about WHERE: the combat baseline is a sample of a live battle",
					slot, rise, WarheadsPerSalvo, salvo, combat)
			end
		end
	end

	if shots[2] ~= nil and shots[3] ~= nil then
		say(string.format("pair gap_ticks=%d", shots[3].ordered - shots[2].ordered))
	else
		flag("the back-to-back pair is incomplete: shot 2 %s, shot 3 %s",
			shots[2] and "fired" or "NOT fired", shots[3] and "fired" or "NOT fired")
	end

	for i = 1, #notes do say("note " .. notes[i]) end

	if #invalid == 0 then
		say("valid yes -- three salvos into a populated match, every window populated")
	else
		for i = 1, #invalid do
			say("invalid " .. invalid[i])
		end
	end
end

local function step()
	tick = tick + 1

	-- ---- record, first, so nothing below is charged to a window it does not belong in ----
	-- The value read now is the cost of tick-2; see the header. A window's bounds are in those
	-- COST ticks, which is why the sample is filed under costTick and not under `tick`.
	local costTick = tick - 2
	if costTick >= 1 then
		local ms = Test.GetTickTimeMs()
		for i = 1, #windows do
			local w = windows[i]
			if w.from ~= nil and w.to ~= nil and costTick >= w.from and costTick <= w.to then
				addSample(w, ms)
			end
		end
	end

	-- ---- impact CROSS-CHECK: three counter samples per shot. Never a window bound ----
	-- See ExpectedImpactOffset for why this counts rather than timestamps. Sampled on exactly three
	-- ticks per shot, so the counter read costs nothing steady-state.
	for slot = 1, 3 do
		local s = shots[slot]
		if s ~= nil then
			if tick == s.expectedImpact then
				s.impactsAtStart = Test.GetImpactEffectCount()
				say(string.format("salvo-window-opens shot=%d tick=%d %s",
					slot, tick, censusLine()))
			elseif tick == s.expectedImpact + SalvoSpanTicks then
				s.impactsAfter = Test.GetImpactEffectCount()
				local salvo = s.impactsAfter - s.impactsAtStart
				local combat = (s.impactsAtStart - s.impactsBefore)
					* SalvoSpanTicks / ExpectedImpactOffset
				say(string.format("salvo-window-closes shot=%d tick=%d salvo_impacts=%d "
					.. "combat_per_span=%.1f rise=%.1f",
					slot, tick, salvo, combat, salvo - combat))
			end
		end
	end

	-- ---- census, on its own beat ----
	if tick % CensusInterval == 0 then
		say(string.format("census tick=%d %s", tick, censusLine()))
	end

	-- ---- window census stamps, one per window, taken on the tick it opens ----
	-- Every window carries the army sizes it was measured over, because the one thing that silently
	-- voids a number here is a window that looks like a late game and is not one.
	for i = 1, #windows do
		local w = windows[i]
		if w.from ~= nil and tick >= w.from and w.census == "unset" then
			w.census = censusLine()
		end
	end

	-- ---- shot 1 ----
	if shots[1] == nil then
		if tick >= ArmTick1 and tick < FireTick1 then
			arm(1)
		elseif tick >= FireTick1 and tick <= FireDeadline1 then
			arm(1)
			if armed[1] then
				if fire(1) then openDetonationWindows(1) end
			end
		elseif tick == FireDeadline1 + 1 then
			flag("shot 1 never issued by its deadline %d (arm status '%s')",
				FireDeadline1, armStatus[1])
		end
	end

	-- ---- shot 2, then shot 3 as soon as the magazine re-arms: the back-to-back pair ----
	if shots[2] == nil then
		if tick >= ArmTick2 and tick < FireTick2 then
			arm(2)
		elseif tick >= FireTick2 and tick <= FireDeadline2 then
			arm(2)
			if armed[2] then
				if fire(2) then openDetonationWindows(2) end
			end
		elseif tick == FireDeadline2 + 1 then
			flag("shot 2 never issued by its deadline %d (arm status '%s')",
				FireDeadline2, armStatus[2])
		end
	elseif shots[3] == nil and tick <= FireDeadline2 + 400 then
		arm(3)
		if armed[3] then fire(3) end
	elseif shots[3] == nil and tick == FireDeadline2 + 401 then
		flag("shot 3 (the second half of the pair) never issued (arm status '%s')", armStatus[3])
	end

	-- ---- the end ----
	local lastClose = HardEndTick
	if wRecover2.to ~= nil and wRecover2.to + 5 < lastClose then
		lastClose = wRecover2.to + 5
	end

	if tick >= lastClose then
		-- Every window is closed by now, so the cost of THIS tick -- which includes the percentile
		-- walks below -- is in none of them.
		report()
		Test.Skip(string.format(
			"populated nuke perf reading finished at tick %d; shots=%d/3; readings are the "
			.. "'NUKEPOP window' lines in lua.log, not this verdict",
			tick, (shots[1] and 1 or 0) + (shots[2] and 1 or 0) + (shots[3] and 1 or 0)))
	end
end

WorldLoaded = function()
	Firer = Player.GetPlayer(FirerName)
	Target = Player.GetPlayer(TargetName)

	-- Framed for a human who runs this visible. Under --hidden nothing is drawn and this costs
	-- nothing. Centre of the 96x96 playfield; the salvos go wherever the armies are.
	Camera.Position = WPos.New(48 * 1024 + 512, 48 * 1024 + 512, 0)
	Camera.Zoom = Camera.MinZoom

	UserInterface.SetMissionText("NUKE PERF -- POPULATED LATE GAME, THREE SARMAT SALVOS")

	say(string.format(
		"loaded firer=%s target=%s fire1=%d fire2=%d end=%d army_floor=%d impact_offset=%d",
		FirerName, TargetName, FireTick1, FireTick2, HardEndTick, ArmyFloor,
		ExpectedImpactOffset))
	note("NuclearBotModule is inert outside Escalation (NuclearBotModule.cs:278) and no bot in this "
		.. "mod instantiates SupportPowerBotModule (ai.yaml:2899), so the only nuclear detonations "
		.. "in this run are the three ordered above")

	Trigger.OnTick(step)
end
