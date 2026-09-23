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
-- ==== WHAT RUN 260923_084012 COST US, AND WHY THIS FILE NO LONGER DERIVES ANYTHING ====
-- The first live run of this scenario declared all three of its own shots INVALID, and every one of
-- those verdicts was the instrument's fault rather than the weapon's. The first draft computed the
-- impact tick as `MissileDelay + standoff / Speed` = 158 and opened `deton1` there. Two of the three
-- terms in that sum were wrong:
--
--   * MissileDelay CONTRIBUTED NOTHING. This scenario must enable the powers sandbox to reach a
--     game-ender at all (see rules.yaml), and PowersLobbyOptionsInfo.SandboxRemovesLaunchDelay
--     defaults TRUE -- so MissileStrikePower.Activate takes `baseMissileDelay = 0`
--     (MissileStrikePower.cs:624-626) and the 60 in rules.yaml was never spent. The derivation was
--     60 ticks late on a 98-tick flight.
--   * THE SALVO IS FOUR WARHEADS, NOT SIX. For anything NuclearGameEnders.Is() accepts, the count is
--     DoomsdayStrike.PackageSize and not the YAML AimPoints (MissileStrikePower.AimPointsFor, :296).
--     That is round(playableCells / CellsPerImpact) clamped to [2,6] = round(96*96 / 2400) = 4 here.
--     So the salvo spans 3 * AimPointInterval = 36 ticks and not 60.
--
-- The run's own census shows the cost of that. `flight1`, which the file claims is the quiet stretch
-- BEFORE anything lands, opened on 80 USA ground attackers and carried p99 67 ms, max 74.75 ms and
-- the only four over-budget ticks in the entire run; `deton1` opened 60 ticks later on ONE, with
-- p50 6 and max 31.5. The detonation was measured by the window named for the flight, and the
-- window named for the detonation measured the silence afterwards. tools/nuke-perf/README.md
-- records those numbers, labelled for what they are.
--
-- SO THE ANCHOR IS NOW OBSERVED. Test.GetBallisticMissileImpactCount("sarmatmissile") is a running
-- count of warheads that COMPLETED THEIR FLIGHT -- BallisticMissileFly reaching horizontalProgress
-- >= 1 and killing its own actor, which is the call that fires the Explodes payload. Exactly one
-- per warhead, keyed by actor type so that the bots' HIMARS and Iskander rounds (also
-- BallisticMissile actors, fired all game) cannot move it. Every detonation window below opens on
-- the tick that counter first rises after its own order, and the derived offset is still computed
-- and still printed -- as a number to COMPARE the observation against, never as a window bound.
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
-- nominally asks for. RUN 260923_084012 MEASURED ~35 ON THIS SCENARIO -- 10493 ticks in ~5 minutes
-- -- which is faster than either, and the reason is almost certainly that a salvo wipes the field
-- armies this scenario is otherwise paying to simulate. Size the timeout off the SLOW row anyway:
-- the pair now waits for a rebuilt army, so a run that never rebuilds is the long one.
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
-- THE INSTRUMENT IS INSIDE THE MEASUREMENT, AND IT COSTS TWO BINDING CALLS PER TICK, not one as of
-- 2026-09-23: Test.GetTickTimeMs and Test.GetBallisticMissileImpactCount. Both are static field
-- reads on the C# side -- the arrival counter walks NOTHING, which is the whole reason it was added
-- as a counter rather than as a world scan. EVERY window pays it equally, including `build` and
-- both prefires, so it cancels out of every delta this file is read for. It does not cancel out of
-- the ABSOLUTE numbers, and that is one more reason they are a floor.
-- GetGroundAttackers is the expensive one and is deliberately NOT per tick: it walks
-- ActorsHavingTrait<AttackBase>, and it is called on the census beat, at window edges, at a fire
-- tick and on the pair's 100-tick watch.
--
-- ==== THE SCHEDULE ====
--     tick   300   the `build` window opens -- the whole bot build-up, as a control
--     tick  7000   arming for shot 1 begins (EnsurePower). STOPS as soon as the magazine banks, so
--                  no production property is touched inside the baseline window that follows.
--     tick  7700   `prefire1` opens: 300 ticks of a populated late game with nothing incoming.
--                  THIS IS THE BASELINE EVERY DETONATION NUMBER IS READ AGAINST.
--     tick  8000   SHOT 1 ordered, retried every tick to 8400. Ground zero is computed HERE, from
--                  the live centroid of USA-bot's ground attackers -- see aimAt below.
--   OBSERVED       `flight1` runs from the order to the tick before the first warhead ARRIVES;
--                  `deton1` opens ON that arrival and runs 400 ticks, which outlasts
--                  ShockwaveEffect's ~400-tick sweep; `recover1` is the 300 after that. The
--                  arrival tick is read off Test.GetBallisticMissileImpactCount, NOT derived.
--     tick  9200   the pair's rebuild watch opens -- see THE PAIR WAITS FOR AN ARMY below. When the
--                  target's ground army reaches PairArmyTarget (or at PairWatchDeadline, whichever
--                  comes first) `prefire2` opens for 300 ticks and the pair is fired at its close.
--   OBSERVED       SHOT 2, then SHOT 3 as soon as the magazine re-arms -- one purchase is one shot
--                  (test-helpers.lua), so the gap is whatever a 5-tick load costs and is RECORDED
--                  rather than pinned. `pair` opens on the first arrival after shot 2's order and
--                  runs 430 ticks, long enough to hold both salvos and both shockwave sweeps;
--                  `recover2` follows.
--   OBSERVED       hard end, five ticks after the last window closes. Test.Skip either way.
--
-- ==== THE PAIR WAITS FOR AN ARMY, AND RUN 260923_084012 IS WHY ====
-- One Sarmat salvo does not thin a field army, it deletes it: USA went from 80 ground attackers at
-- the order to 1 by the derived impact tick 158 ticks later. The first draft then fired the pair on
-- a FIXED tick 1600 later, into the 20 units USA had managed to rebuild -- a quarter of the
-- battlefield the single shot was measured on, which makes the two readings incomparable and makes
-- the pair, the more expensive of the two events, the one measured on the emptier map.
--
-- So the pair is no longer scheduled: it is TRIGGERED, on the target army climbing back to
-- PairArmyTarget. Observed rebuild in that run was ~14 attackers per 1000 ticks, so 40 is reached
-- around tick 11000 and PairWatchDeadline 11600 is the cap. If the deadline fires first the run
-- SAYS SO and prints the army it settled for; a thin pair is a recorded fact, not a silent one.
--
-- WHAT WAS NOT DONE, and it is worth saying which way round the first run aimed. Russia-bot fires
-- and USA-bot is aimed at, always: `aimAt(Target)`, ground zero 75,40 at the order, 80 USA
-- attackers within 48 cells of it. That is Russia nuking its enemy and it was correct -- the run
-- was not a self-nuke, and the 80 -> 1 in USA's census is the weapon working. The pair is not
-- re-pointed at Russia's own concentration to find a fuller target: a perf reading does not care
-- who owns the actors under a fireball, but a scenario in which Russia nukes itself is a shape no
-- player will ever produce, and this file's whole claim is that it measures a real match.
--
-- ==== WHAT MAKES A RUN INVALID, CHECKED AND PRINTED BY THIS FILE ====
--   * a shot never issued                                  -> NUKEPOP invalid shot=N ...
--   * a shot whose warheads NEVER ARRIVED -- the arrival counter did not move between the order and
--     the deadline -- so its windows were never opened at all  -> NUKEPOP invalid shot N ...
--   * either army below ArmyFloor at a fire tick            -> NUKEPOP underpopulated ...
--   * zero enemy actors under the aim                       -> NUKEPOP underpopulated aim=...
-- An empty map is indistinguishable from a cheap detonation, which is demo-nuke-perf's own hardest
-- lesson (its run 1 produced a clean 1000-tick log of nothing happening). So every window carries
-- its own census and every failure above names itself in lua.log.
--
-- NOTE, NOT INVALID, FOR THE THINGS THAT ARE MERELY WEAK: fewer arrivals than the map's package
-- size (a warhead was shot down), an observed impact tick far from the derived one, a pair fired on
-- a materially thinner army than the single shot. Each is printed with its numbers. The first draft
-- graded a shot invalid on an arithmetic comparison and was wrong three times out of three; a check
-- that can only be satisfied by a derivation nobody has verified is worse than no check.

-- ---------------------------------------------------------------------------------------------
-- CONSTANTS
-- ---------------------------------------------------------------------------------------------

local PowerKey  = "SarmatStrike"
local ProxyType = "power.sarmat"

-- THE ACTOR THE WARHEADS FLY IN, and the key the arrival counter is read under.
-- MissileStrikePower@Sarmat: MissileActor: sarmatmissile (nuclear-arsenal.yaml:266). It MUST be
-- passed: twenty-one shipped actors carry BallisticMissile and two of them, himarsmissile and
-- iskandermissile, are ordinary unit armaments these two bots fire at each other all game long.
-- The unqualified counter would anchor `deton1` on somebody's rocket artillery.
local MissileType = "sarmatmissile"

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

-- ==== THE PAIR'S TRIGGER, NOT ITS TICK ====
-- Arm well before the watch opens, for the same reason ArmTick1 sits 1000 ticks before prefire1:
-- EnsurePower reaches for production properties and must not do it inside a measured window.
local PairArmTick = 8900

-- From here the target's ground army is polled every PairWatchInterval ticks. The poll walks
-- ActorsHavingTrait<AttackBase>, so it is deliberately NOT per tick and is outside every window
-- until the one it opens itself.
local PairWatchFrom     = 9200
local PairWatchInterval = 100

-- Fire the pair once the target has rebuilt to this many ground attackers. 40 is HALF what shot 1
-- was measured on (80 in run 260923_084012) and is a judgement about comparability, not a
-- threshold: below it the file prints a note saying the pair's numbers are not the single shot's.
-- The observed rebuild in that run was ~14 attackers per 1000 ticks from a standing start of 1, so
-- 40 is expected around tick 11000.
local PairArmyTarget = 40

-- ... and the cap, so a match that never rebuilds still produces a pair reading and an honest
-- statement of what it was taken on, rather than a run that quietly ends having fired once.
local PairWatchDeadline = 11600

-- ==== WHERE THE IMPACT TICK COMES FROM: OBSERVED, WITH THE DERIVATION KEPT AS A CROSS-CHECK ====
-- Test.GetBallisticMissileImpactCount(MissileType) counts warheads that COMPLETED THEIR FLIGHT --
-- BallisticMissileFly reaching horizontalProgress >= 1 and killing its own actor, which is the call
-- that fires the Explodes payload (BallisticMissileFly.cs:370). Exactly one per warhead, and a
-- warhead shot down on the way in does not count, because it never arrived.
--
-- THE DERIVATION IS STILL COMPUTED AND STILL PRINTED, because a difference between it and the
-- observation is information: it is how run 260923_084012's 60-tick error would have announced
-- itself on tick 8099 instead of being reconstructed from a census afterwards. It is NEVER a
-- window bound again.
--
--     entry standoff = mapDiagonal + ApproachMargin        (MissileStrikePower.ApproachFor, and
--                                                           TestHarness.ApproachStandoffCells,
--                                                           which takes MapSize and NOT Bounds)
--     flight ticks   = standoff (wdist) / Speed            (SarmatMissile BallisticMissile Speed
--                                                           1600, nuclear-arsenal.yaml:958;
--                                                           Acceleration 0, so EstimateArcTicks is
--                                                           exactly hDist / Speed)
--     impact         = launchDelay + PreLaunchTicks + flight
--
-- AND launchDelay IS ZERO HERE, WHICH IS THE WHOLE OF THE 2026-09-23 CORRECTION. rules.yaml pins
-- MissileDelay to 60 and that line is INERT: this scenario enables the powers sandbox to reach an
-- event-tier power at all, PowersLobbyOptionsInfo.SandboxRemovesLaunchDelay defaults true, and
-- MissileStrikePower.Activate therefore takes `baseMissileDelay = 0` (:624-626). PreLaunchTicks is
-- 0 because LaunchRiseTicks is (BallisticMissile.cs:114). Both forms are printed below so that a
-- future run with the sandbox off is readable against this one.
local MapCellsX, MapCellsY = 98, 98      -- MapSize from map.yaml. NOT Bounds.
local MissileSpeedWDist = 1600           -- SarmatMissile: BallisticMissile: Speed
local MissileDelayTicks = 60             -- rules.yaml's value -- DROPPED by the powers sandbox

local FlightTicks =
	math.floor(TestHarness.ApproachStandoffCells(MapCellsX, MapCellsY) * 1024 / MissileSpeedWDist)

-- What the engine will actually do, given the sandbox: no launch delay at all.
local DerivedImpactOffset = FlightTicks
-- What it would do with the sandbox off, kept so the two are never confused again.
local DerivedImpactOffsetWithLaunchDelay = MissileDelayTicks + FlightTicks

-- ==== HOW MANY WARHEADS, AND WHY IT IS NOT SIX ====
-- For any power NuclearGameEnders.Is() accepts -- the Sarmat is one -- the salvo size is NOT the
-- YAML AimPoints. MissileStrikePower.AimPointsFor (:296-302) hands the question to
-- DoomsdayStrike.PackageSizeFor, which is FinalExchangePackage.SizeFor(playableCells,
-- CellsPerImpact, MinPackage, MaxPackage): round(Bounds.Width * Bounds.Height / 2400) clamped to
-- [2, 6]. Bounds here are 1,1,96,96, so 9216 / 2400 rounds to FOUR. The first draft of this file
-- assumed six and sized its salvo span for six.
--
-- IT IS AN EXPECTATION AND NOT A CHECK. The arrival counter observes the real number, and a
-- shortfall is a NOTE naming this arithmetic -- a warhead may legitimately have been shot down.
local BoundsW, BoundsH = 96, 96
local CellsPerImpact = 2400              -- DoomsdayStrike: CellsPerImpact (world.yaml:769)
local MinPackage, MaxPackage = 2, 6      -- world.yaml:772, :775

local function packageSize()
	local raw = math.floor((BoundsW * BoundsH + math.floor(CellsPerImpact / 2)) / CellsPerImpact)
	if raw < MinPackage then return MinPackage end
	if raw > MaxPackage then return MaxPackage end
	return raw
end

local ExpectedWarheads = packageSize()

-- The warheads of one salvo arrive AimPointInterval (12) ticks apart, so a package of N occupies
-- (N-1) * 12 ticks. A salvo is declared COMPLETE when the arrival counter has been still for this
-- long -- three intervals, so a straggler cannot close it early. Observed, not assumed: the salvo
-- span this file reports is last-arrival minus first-arrival.
local ArrivalQuietTicks = 36

-- How long after an order this file waits for a first arrival before concluding that nothing is
-- coming. Generous on purpose: it must comfortably exceed BOTH derived offsets above, so that a
-- future run with the powers sandbox off (launch delay 60 back in play) still keys on its real
-- impact rather than timing out and calling a good salvo missing.
local ArrivalDeadlineTicks = 500

local PrefireTicks   = 300     -- length of each pre-shot baseline window
local DetonTailTicks = 400     -- ShockwaveEffect sweeps ~400 ticks past its own impact
local PairTailTicks  = 430     -- two salvos, so the tail starts later relative to the first impact
local RecoverTicks   = 300

-- ABSOLUTE CEILING, not the schedule. The run now ends five ticks after its last window closes,
-- which on the expected path is ~10300 and on the deadline path ~12750. This is the backstop for a
-- match in which the pair never fires at all, and it is what the --timeout in description.txt is
-- sized against.
local HardEndCap = 13000

-- Census cadence outside window boundaries. GetGroundAttackers walks ActorsHavingTrait<AttackBase>,
-- which is real work inside a tick this file is measuring, so it is NOT called per tick.
local CensusInterval = 500

-- Below this many ground attackers on either side at a fire tick, the run says so in its own log.
-- NOT a threshold anybody has agreed and NOT a pass/fail -- it is the line under which the word
-- "populated" stops being honest. Run 260923_084012 fired shot 1 into 80 and the pair into 20.
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
local shots = {}          -- [1..3] = { ordered, aim, army, arrivals... }
local armed = { [1] = false, [2] = false, [3] = false }
local armStatus = { [1] = "not-started", [2] = "not-started", [3] = "not-started" }
local invalid = {}
local notes = {}

-- Arrival tracking, read once per tick from a static counter (a field read; it walks nothing).
local arrivals = 0
local lastArrivalTick = nil
local lastArrivalEngineTick = nil

-- The pair's trigger, resolved at run time.
local FireTick2 = nil
local FireDeadline2 = nil
local pairTriggerTick = nil
local pairTriggerArmy = nil
local pairTriggerReason = "not-triggered"

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
		censusClose = "unset",
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
-- Bounds are 1,1,96,96. Clamped to 4..93 so the warheads, which fall on a ring of
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
	-- warhead's blast band, 48 is roughly the whole pattern. A shot with zero under it at 48 is a
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
local wFlight2  = newWindow("flight2")
local wPair     = newWindow("pair")
local wRecover2 = newWindow("recover2")

-- The two that are known before the run starts. prefire2's bounds are set by the pair's trigger.
wBuild.from,    wBuild.to    = BuildWindowOpen, FireTick1 - PrefireTicks - 1
wPrefire1.from, wPrefire1.to = FireTick1 - PrefireTicks, FireTick1 - 1

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
		derivedImpact = tick + DerivedImpactOffset,
		arrivalsBefore = arrivals,
		impactsBefore = Test.GetImpactEffectCount(),   -- gated-impact total at the ORDER tick
		firstArrival = nil,          -- scenario tick of this shot's first warhead arrival
		firstArrivalEngine = nil,    -- ... as World.WorldTick, read off the binding
		impactsAtArrival = nil,      -- gated-impact total at that tick
		salvoLast = nil,             -- scenario tick of the last arrival of this salvo group
		salvoArrivals = 0,           -- warheads that arrived in this salvo group
		salvoClosed = false,
		impactsAfterSalvo = nil,
		aim = string.format("%d,%d", aim.X, aim.Y),
		targetArmy = army,
		near = near,
		wide = wide,
		firerArmy = armyOf(Firer),
	}

	say(string.format(
		"order shot=%d tick=%d groundzero=%s derived_impact=%d target_army=%d under_r24=%d "
		.. "under_r48=%d firer_army=%d",
		slot, tick, shots[slot].aim, shots[slot].derivedImpact, army, near, wide,
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

-- Opened the moment a shot is ACCEPTED, with the flight window running to a deadline rather than to
-- a computed impact. Closing it is openDetonationWindows' job, below, and that happens on an
-- observation. Nothing here predicts when anything lands.
local function openFlightWindow(slot)
	if slot == 1 then
		wFlight1.from = shots[slot].ordered
		wFlight1.to   = shots[slot].ordered + ArrivalDeadlineTicks
	elseif slot == 2 then
		wFlight2.from = shots[slot].ordered
		wFlight2.to   = shots[slot].ordered + ArrivalDeadlineTicks
	end
end

-- Called ON the tick this shot's first warhead arrived. Retro-closing the flight window here is
-- safe and is not a correction: samples are filed under `tick - 2`, so at the moment this runs the
-- newest sample taken is cost-tick (impactTick - 2), and every tick this line removes from the
-- flight window is one that has not been sampled yet.
local function openDetonationWindows(slot, impactTick)
	if slot == 1 then
		wFlight1.to   = impactTick - 1
		wDeton1.from  = impactTick
		wDeton1.to    = impactTick + DetonTailTicks
		wRecover1.from = wDeton1.to + 1
		wRecover1.to   = wRecover1.from + RecoverTicks - 1
	elseif slot == 2 then
		wFlight2.to = impactTick - 1
		wPair.from  = impactTick
		wPair.to    = impactTick + PairTailTicks
		wRecover2.from = wPair.to + 1
		wRecover2.to   = wRecover2.from + RecoverTicks - 1
	end
end

-- Shot 3 is fired one tick behind shot 2 and flies the same actor type, so no counter can separate
-- their warheads. The two are ONE salvo group for arrival purposes -- which is what the `pair`
-- window means anyway -- and this says so rather than letting a reader assume two independent
-- records. Shot 1 is its own group.
local function salvoGroupOf(slot)
	if slot == 3 then return 2 end
	return slot
end

local function report()
	say(string.format(
		"readings tickrate_note=60ms_timestep budget_ms=%d bins_ms=1 standoff_cells=%.1f "
		.. "flight_ticks=%d derived_impact_offset=%d derived_offset_if_launch_delay_applied=%d "
		.. "expected_warheads=%d",
		BudgetMs, TestHarness.ApproachStandoffCells(MapCellsX, MapCellsY),
		FlightTicks, DerivedImpactOffset, DerivedImpactOffsetWithLaunchDelay, ExpectedWarheads))

	say(string.format("pair_trigger reason=%s tick=%s army=%s fire_tick=%s",
		pairTriggerReason, tostring(pairTriggerTick), tostring(pairTriggerArmy),
		tostring(FireTick2)))

	for i = 1, #windows do
		local w = windows[i]
		local mean = -1
		if w.n > 0 then mean = math.floor(w.sum * 10 / w.n) / 10 end

		say(string.format(
			"window name=%s from=%s to=%s n=%d p50=%d p90=%d p99=%d max=%.3f mean=%.1f "
			.. "over_budget=%d over_bins=%d census_open=%s census_close=%s",
			w.name,
			tostring(w.from), tostring(w.to),
			w.n, percentile(w, 50), percentile(w, 90), percentile(w, 99),
			w.max, mean, w.overBudget, w.over, w.census, w.censusClose))
	end

	for slot = 1, 3 do
		local s = shots[slot]
		if s == nil then
			say(string.format("shot slot=%d NOT-FIRED arm_status=%s", slot, armStatus[slot]))
			flag("shot %d was never issued (arming ended at '%s'); every window that depends on it "
				.. "is a window of nothing", slot, armStatus[slot])
		else
			local group = salvoGroupOf(slot)
			local g = shots[group]

			say(string.format(
				"shot slot=%d group=%d ordered=%d derived_impact=%d observed_impact=%s "
				.. "engine_impact=%s derived_minus_observed=%s groundzero=%s target_army=%d "
				.. "under_r24=%d under_r48=%d",
				slot, group, s.ordered, s.derivedImpact,
				tostring(g.firstArrival), tostring(g.firstArrivalEngine),
				g.firstArrival ~= nil and tostring(s.derivedImpact - g.firstArrival) or "nil",
				s.aim, s.targetArmy, s.near, s.wide))

			-- THE ONE HARD FAILURE LEFT, and it is an observation rather than a comparison: the
			-- arrival counter did not move between the order and the deadline. Nothing this shot
			-- fired reached its aim point, so its windows were never opened and there is nothing to
			-- read. Everything weaker than that is a note; see the header for why.
			if g.firstArrival == nil then
				flag("shot %d: no warhead of %s ever arrived -- "
					.. "Test.GetBallisticMissileImpactCount(\"%s\") did not move in the %d ticks "
					.. "after the order at %d. Its detonation windows were never opened",
					slot, MissileType, MissileType, ArrivalDeadlineTicks, s.ordered)
			end
		end
	end

	-- Salvo groups, reported once each rather than once per shot, because the arrival counter
	-- cannot attribute a warhead to shot 2 or shot 3 and this file will not pretend it can.
	for _, group in ipairs({ 1, 2 }) do
		local g = shots[group]
		if g ~= nil and g.firstArrival ~= nil then
			local span = (g.salvoLast or g.firstArrival) - g.firstArrival
			local salvo, combatRate, combatInSpan, rise = -1, -1, -1, -1
			local flightTicks = g.firstArrival - g.ordered

			if g.impactsAfterSalvo ~= nil and flightTicks > 0 then
				salvo = g.impactsAfterSalvo - g.impactsAtArrival
				combatRate = (g.impactsAtArrival - g.impactsBefore) / flightTicks
				combatInSpan = combatRate * (span + ArrivalQuietTicks)
				rise = salvo - combatInSpan
			end

			say(string.format(
				"detonation group=%d observed_tick=%d engine_tick=%d derived_tick=%d "
				.. "derived_minus_observed=%d arrivals=%d expected_warheads=%d last_arrival=%s "
				.. "salvo_span=%d gated_impacts=%d combat_per_tick=%.2f rise=%.1f",
				group, g.firstArrival, g.firstArrivalEngine, g.derivedImpact,
				g.derivedImpact - g.firstArrival, g.salvoArrivals, ExpectedWarheads,
				tostring(g.salvoLast), span, salvo, combatRate, rise))

			-- NOTES, every one of them. The single thing that invalidates a group is zero arrivals,
			-- checked above.
			if g.salvoArrivals < ExpectedWarheads and group == 1 then
				note("group %d delivered %d of the %d warheads this map's package size gives "
					.. "(round(%d*%d / %d) clamped to [%d,%d]); the missing ones were shot down or "
					.. "never spawned, and the detonation window holds fewer fireballs than a full "
					.. "salvo", group, g.salvoArrivals, ExpectedWarheads, BoundsW, BoundsH,
					CellsPerImpact, MinPackage, MaxPackage)
			end

			if math.abs(g.derivedImpact - g.firstArrival) > 4 then
				note("group %d arrived %d ticks from where the shipped geometry says it should "
					.. "(observed %d, derived %d). The windows are on the OBSERVATION; this note is "
					.. "about the derivation, which is either stale or looking at a different "
					.. "launch-delay path -- with the powers sandbox off the derived offset would "
					.. "be %d", group, g.derivedImpact - g.firstArrival, g.firstArrival,
					g.derivedImpact, DerivedImpactOffsetWithLaunchDelay)
			end

			if rise >= 0 and rise < ExpectedWarheads then
				note("group %d raised the gated-impact counter by only %.1f across its observed "
					.. "salvo span against a combat rate of %.2f per tick. THIS IS NOT A CLAIM THAT "
					.. "IT MISSED -- %d warheads demonstrably arrived. It means the gated-impact "
					.. "counter is a weak corroboration in a live battle, which is why the windows "
					.. "are no longer keyed on it", group, rise, combatRate, g.salvoArrivals)
			end
		end
	end

	if shots[2] ~= nil and shots[3] ~= nil then
		say(string.format("pair gap_ticks=%d", shots[3].ordered - shots[2].ordered))

		-- COMPARABILITY, stated rather than left for a reader to notice. One salvo deletes a field
		-- army; if the pair went in on materially less than the single shot did, its numbers are
		-- not the single shot's numbers and no amount of percentile arithmetic makes them so.
		if shots[1] ~= nil and shots[2].targetArmy * 2 < shots[1].targetArmy then
			note("the pair was fired into %d target ground attackers against the single shot's %d "
				.. "-- less than half. Read the pair's window as a reading on a thinner "
				.. "battlefield, NOT as two-salvos-versus-one on the same one",
				shots[2].targetArmy, shots[1].targetArmy)
		end
	else
		flag("the back-to-back pair is incomplete: shot 2 %s, shot 3 %s",
			shots[2] and "fired" or "NOT fired", shots[3] and "fired" or "NOT fired")
	end

	for i = 1, #notes do say("note " .. notes[i]) end

	if #invalid == 0 then
		say("valid yes -- three salvos into a populated match, every window keyed on an observed "
			.. "detonation")
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

	-- ---- arrivals: one static-field read, every tick, walking nothing ----
	-- This is the anchor. It must be read BEFORE the window bookkeeping below, because the tick a
	-- warhead arrives on is the tick `deton1` opens on.
	local seen = Test.GetBallisticMissileImpactCount(MissileType)
	if seen > arrivals then
		local added = seen - arrivals
		arrivals = seen
		lastArrivalTick = tick
		lastArrivalEngineTick = Test.GetLastBallisticMissileImpactTick(MissileType)

		for slot = 1, 3 do
			local s = shots[slot]
			if s ~= nil and s.firstArrival == nil and tick > s.ordered then
				s.firstArrival = tick
				s.firstArrivalEngine = lastArrivalEngineTick
				s.impactsAtArrival = Test.GetImpactEffectCount()
				say(string.format("detonation-observed shot=%d tick=%d engine_tick=%d "
					.. "derived_tick=%d derived_minus_observed=%d %s",
					slot, tick, s.firstArrivalEngine, s.derivedImpact,
					s.derivedImpact - tick, censusLine()))
			end
		end

		for _, group in ipairs({ 1, 2 }) do
			local g = shots[group]
			if g ~= nil and g.firstArrival ~= nil and not g.salvoClosed then
				g.salvoArrivals = g.salvoArrivals + added
				g.salvoLast = tick
			end
		end

		-- Opened here and not in fire(): the window bound IS the observation.
		for _, group in ipairs({ 1, 2 }) do
			local g = shots[group]
			if g ~= nil and g.firstArrival == tick then
				openDetonationWindows(group, tick)
			end
		end
	end

	-- ---- close a salvo group once the arrivals have stopped, and sample the counter there ----
	for _, group in ipairs({ 1, 2 }) do
		local g = shots[group]
		if g ~= nil and g.firstArrival ~= nil and not g.salvoClosed
			and g.salvoLast ~= nil and tick >= g.salvoLast + ArrivalQuietTicks then
			g.salvoClosed = true
			g.impactsAfterSalvo = Test.GetImpactEffectCount()
			say(string.format("salvo-complete group=%d first=%d last=%d span=%d arrivals=%d %s",
				group, g.firstArrival, g.salvoLast, g.salvoLast - g.firstArrival,
				g.salvoArrivals, censusLine()))
		end
	end

	-- ---- census, on its own beat ----
	if tick % CensusInterval == 0 then
		say(string.format("census tick=%d %s", tick, censusLine()))
	end

	-- ---- window census stamps, at each window's open AND its close ----
	-- Every window carries the army sizes it was measured over, because the one thing that silently
	-- voids a number here is a window that looks like a late game and is not one. BOTH ends, because
	-- a detonation window opens on a full battlefield and closes on an empty one, and one stamp
	-- cannot say that.
	for i = 1, #windows do
		local w = windows[i]
		if w.from ~= nil and tick >= w.from and w.census == "unset" then
			w.census = censusLine()
		end

		if w.to ~= nil and tick >= w.to and w.censusClose == "unset" and w.census ~= "unset" then
			w.censusClose = censusLine()
		end
	end

	-- ---- shot 1 ----
	if shots[1] == nil then
		if tick >= ArmTick1 and tick < FireTick1 then
			arm(1)
		elseif tick >= FireTick1 and tick <= FireDeadline1 then
			arm(1)
			if armed[1] then
				if fire(1) then openFlightWindow(1) end
			end
		elseif tick == FireDeadline1 + 1 then
			flag("shot 1 never issued by its deadline %d (arm status '%s')",
				FireDeadline1, armStatus[1])
		end
	end

	-- ---- the pair's trigger: a rebuilt army, or the deadline ----
	if FireTick2 == nil and tick >= PairWatchFrom and tick % PairWatchInterval == 0 then
		local army = armyOf(Target)
		if army >= PairArmyTarget then
			pairTriggerReason = "army-rebuilt"
		elseif tick >= PairWatchDeadline then
			pairTriggerReason = "deadline"
		end

		if pairTriggerReason ~= "not-triggered" then
			pairTriggerTick = tick
			pairTriggerArmy = army
			wPrefire2.from = tick
			wPrefire2.to   = tick + PrefireTicks - 1
			FireTick2 = tick + PrefireTicks
			FireDeadline2 = FireTick2 + 400
			say(string.format("pair-triggered tick=%d reason=%s target_army=%d target=%d "
				.. "prefire2=%d..%d fire=%d",
				tick, pairTriggerReason, army, PairArmyTarget,
				wPrefire2.from, wPrefire2.to, FireTick2))

			if pairTriggerReason == "deadline" then
				note("the pair fired on its deadline %d with the target army at %d, short of the "
					.. "%d this file waits for. The battlefield it was measured on is the one that "
					.. "existed, not the one the single shot had", PairWatchDeadline, army,
					PairArmyTarget)
			end
		end
	end

	-- ---- shot 2, then shot 3 as soon as the magazine re-arms: the back-to-back pair ----
	if tick >= PairArmTick and shots[2] == nil and FireTick2 == nil then
		arm(2)
	elseif FireTick2 ~= nil and shots[2] == nil then
		if tick < FireTick2 then
			arm(2)
		elseif tick <= FireDeadline2 then
			arm(2)
			if armed[2] then
				if fire(2) then openFlightWindow(2) end
			end
		elseif tick == FireDeadline2 + 1 then
			flag("shot 2 never issued by its deadline %d (arm status '%s')",
				FireDeadline2, armStatus[2])
		end
	elseif shots[2] ~= nil and shots[3] == nil and tick <= FireDeadline2 + 400 then
		arm(3)
		if armed[3] then fire(3) end
	elseif shots[2] ~= nil and shots[3] == nil and tick == FireDeadline2 + 401 then
		flag("shot 3 (the second half of the pair) never issued (arm status '%s')", armStatus[3])
	end

	-- ---- the end ----
	-- Five ticks past the last window that has actually been opened, capped. NOT a pinned schedule
	-- any more: the pair's tick is decided at run time, so the end has to be too.
	local lastClose = HardEndCap
	if wRecover2.to ~= nil and wRecover2.to + 5 < lastClose then
		lastClose = wRecover2.to + 5
	end

	if tick >= lastClose then
		-- Every window is closed by now, so the cost of THIS tick -- which includes the percentile
		-- walks below -- is in none of them.
		report()
		Test.Skip(string.format(
			"populated nuke perf reading finished at tick %d; shots=%d/3; arrivals=%d; readings "
			.. "are the 'NUKEPOP window' lines in lua.log, not this verdict",
			tick, (shots[1] and 1 or 0) + (shots[2] and 1 or 0) + (shots[3] and 1 or 0),
			arrivals))
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
		"loaded firer=%s target=%s missile=%s fire1=%d pair_watch=%d..%d pair_army_target=%d "
		.. "cap=%d army_floor=%d derived_offset=%d expected_warheads=%d",
		FirerName, TargetName, MissileType, FireTick1, PairWatchFrom, PairWatchDeadline,
		PairArmyTarget, HardEndCap, ArmyFloor, DerivedImpactOffset, ExpectedWarheads))
	note("NuclearBotModule is inert outside Escalation (NuclearBotModule.cs:278) and no bot in this "
		.. "mod instantiates SupportPowerBotModule (ai.yaml:2899), so the only nuclear detonations "
		.. "in this run are the three ordered above. The arrival counter is keyed on "
		.. MissileType .. " so the bots' HIMARS and Iskander rounds cannot move it either")

	Trigger.OnTick(step)
end
