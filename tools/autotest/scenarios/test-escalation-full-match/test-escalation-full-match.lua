-- TEST: a whole DEFCON Escalation match, bot vs bot, on the shipped Polar Disorder map.
--
-- Layout lives in map.yaml; the lobby values and the four departures from shipped defaults live in
-- rules.yaml. This file is the poller and the verdict.
--
-- ==== WHY EVERY BUDGET HERE IS IN RAW TICKS ====
-- test-helpers.lua:36 sets TestHarness.TicksPerSecond = 25 and that is a HARNESS CONVENTION for
-- budgeting, not the tick rate. The mod runs at a 60 ms timestep (mod.yaml GameSpeeds, DefaultSpeed:
-- default), i.e. 16.667 ticks/second; CLAUDE.md records the 25 error live at ten sites and
-- DefconEscalation.cs:20-22 restates it at the top of the trait under test. Anything routed through
-- AssertWithin(seconds, ...) or ScreenshotAfter(seconds, ...) would therefore be 1.5x longer in real
-- time than its own name claims. So: no helper second-conversions below, and every constant is a
-- tick count with its shipped source named.
--
-- ==== WHY THIS SURVIVES --speed ====
-- run-test.sh --speed N divides world.Timestep in TestModeSpeedMultiplier, which is IWorldLoaded --
-- i.e. AFTER DefconEscalation's constructor has already converted the lobby's MINUTES into ticks
-- against the un-multiplied 60 ms. Every tick count below is therefore invariant under --speed;
-- only wall clock moves. That is the whole reason this scenario can run the shipped 5- and 10-minute
-- clocks in an unattended slot without compressing either.
--
-- ==== HOW "ZERO UNHANDLED EXCEPTIONS" IS CHECKED, AND WHY IT IS NOT CHECKED HERE ====
-- It is checked by the HARNESS, in two places, and adding a Test.ExceptionCount() binding would
-- have made it worse rather than better:
--   * An unhandled engine exception kills the process. ExceptionHandler.HandleFatalError writes
--     exception-<utc>.log beside debug.log, and run-test.sh:898 finds any such log NEWER THAN THIS
--     RUN'S MARKER and reports OUTCOME=CRASH, exit 3, with the first twelve lines echoed. A Lua
--     binding could not improve on that, because the process that would have to read it is dead.
--   * A fatal Lua error routes through ScriptContext.FatalError, which in test mode writes
--     Test.WriteResult("fail", ...) itself (ScriptContext.cs:273-277) and exits.
-- There is no third class to catch in the simulation path: World.cs wraps Tick in no try/catch, and
-- the only catch-and-log sites under Traits/ are MarkerLayerOverlay (map editor) and
-- UnitLifecycleLogger (an opt-in diagnostic), neither of which is on any path this scenario walks.
--
-- WHAT THAT LEAVES, AND THE ONE THING THIS FILE DOES ABOUT IT: run-test.sh only looks for a crash
-- log when NO result file exists (the hunt sits inside `if [ ! -f "${RESULT_FILE}" ]`), so a crash
-- AFTER a verdict is reported as that verdict. The countermeasure is structural -- THIS POLLER NEVER
-- PASSES EARLY. It writes on the first of (an ending observed) or (the tick deadline), and the
-- ending is the last thing in the match, so there is nothing left to crash in behind a green.

-- NO `//` ANYWHERE IN THIS FILE. Floor division is Lua 5.3 and Eluant binds an older Lua; this file
-- briefly carried one `(a * 100) // b` and it was the ONLY occurrence in any scenario script in the
-- repo, which is the tell. lua-gate did NOT catch it -- it resolves NAMES, never syntax -- and there
-- is no Lua interpreter on the dev machines to parse with, so `math.floor(a * 100 / b)` it is.
local TICK = 5 -- poll granularity, ticks. Finest thing asserted on is the 250-tick final-exchange
               -- window, so 5 is ample and costs a fortieth of a per-tick poll over 24000 ticks.

-- ==== THE SHIPPED CLOCKS, RESTATED SO THE ASSERTIONS CAN CHECK THEM ====
-- Both are what rules.yaml INHERITS rather than sets; if either default is ever retuned, this file
-- is where the run starts failing and the numbers below are what to change.
local NORUSH_TICKS  = 5000   -- DefconEscalationInfo.NoRushDefault = 5 min; 5*60*1000/60 = 5000
local RELEASE_TICKS = 10000  -- DefconEscalationInfo.FirstWarheadsDefault = 10 min, after DEFCON 1
local TIME_LIMIT    = 22000  -- rules.yaml TimeLimitManager.TimeLimitTicks

-- The poller's own deadline. 2000 ticks past the time limit: the final-exchange window is 250
-- (world.yaml:737) and the salvo adds an outlier wave, a 40-tick pause (OutlierToCityPauseTicks) and
-- a city wave on top, so ~2000 is several times the tail. Reaching this tick at all means the ending
-- never resolved, which is itself the finding.
local DEADLINE      = 24000

-- ==== SLACK, AND WHY THE TWO EARLY BOUNDS DIFFER ====
-- Being EARLY is the defect in both checks, so both early bounds are tight -- but they are tight
-- against different things, and using one number for both would have made the no-rush check
-- flaky for a reason that is not a defect.
--
--  * PHASE_EARLY_SLACK is an ABSOLUTE comparison: levelAt[2] is measured from this poller's own
--    first sample, while the no-rush clock was built in DefconEscalation's CONSTRUCTOR and starts
--    decrementing on the World actor's own ITick. The two origins are close but this file does not
--    get to assume they are the same tick, so the allowance has to cover the offset between them.
--    100 ticks (6 s) still catches every version of the real defect -- a level that moved for a
--    reason other than the clock moves by hundreds or thousands of ticks, not by tens.
--  * REL_EARLY_SLACK is a RELATIVE comparison: both endpoints of the release check (levelAt[1] and
--    releaseAt) are timestamps from this same poller, so the only error between them is poll
--    granularity and the bound can be exactly that.
--
-- The LATE side is loose in both because the release is observed THROUGH NuclearBotModule, which
-- evaluates on a 50-tick beat (EvaluationInterval), so a couple of beats of lateness is the
-- sampling rather than the clock.
local PHASE_EARLY_SLACK = 100
local REL_EARLY_SLACK   = 2 * TICK
local LATE_SLACK        = 250

-- The module that owns the @experimental bot's whole GROUND pool. Named as a constant because the
-- obvious alternative is wrong here and a future reader will reach for it: SquadManagerBotModule is
-- declared FIXED-WING ONLY on this profile (ai.yaml: SquadManagerBotModule@experimental.america.
-- fixedwing and .russia.fixedwing, with no ground instance), so "the Attack-class module" for ground
-- units in WW3MOD's experimental bot IS this one. A zero here cannot be explained by another module
-- having done the work.
local GROUND_MODULE = "PoiOffensiveBotModule"

WorldLoaded = function()
	local USA = Player.GetPlayer("USA-bot")
	local RUS = Player.GetPlayer("Russia-bot")
	local bots = { { name = "USA-bot", p = USA }, { name = "Russia-bot", p = RUS } }
	local byName = { ["USA-bot"] = USA, ["Russia-bot"] = RUS }

	local startTick = DateTime.GameTime
	local faults = {}
	local notes = {}
	local decided = false

	local function fault(fmt, ...) faults[#faults + 1] = string.format(fmt, ...) end
	local function note(fmt, ...) notes[#notes + 1] = string.format(fmt, ...) end

	local function elapsed() return DateTime.GameTime - startTick end

	-- ==== RUNNING STATE ====
	local levelAt = {}          -- [level] = elapsed tick the level was FIRST observed at
	local levelSeen = {}        -- [level] = true
	local lastLevel = nil

	-- WALL COVERAGE, SAMPLED PER LEVEL RATHER THAN ONCE. Two reasons it is not a single check at
	-- the top: DefconWall derives at IWorldLoaded and so does this script, and the ordering between
	-- two IWorldLoaded traits is not ours to assume -- so the first sample may legitimately predate
	-- the derivation. And a wall that flickers is a different defect from one that never stood, which
	-- a single boolean cannot tell apart. Counting samples answers both.
	local wallSamples = {}      -- [level] = samples taken at that level
	local wallActive  = {}      -- [level] = samples at that level with the wall standing

	local releaseAt = nil       -- first elapsed tick either bot's nuclear reason left NotReleased
	-- THE ENDING, READ FROM THE ENDING ITSELF AND NOT FROM WinState.
	--
	-- WinState CANNOT ANSWER THIS IN TEST MODE, and it is structural rather than a quirk of timing:
	-- ConquestVictoryConditions.Tick returns early whenever TestMode.IsActive (:63-64, deliberately
	-- -- "the test harness owns the verdict"), `objectiveID` is assigned ONLY inside that method
	-- (:73-74), and its NotifyTimerExpired opens with `if (objectiveID < 0) return;` (:92-95). So no
	-- test-mode scenario can EVER see a WinState leave Undefined, however completely the match
	-- ended. Run 260914_191513 asserted on it anyway and reported "never reached an ending" for a
	-- match that had very likely ended perfectly well.
	--
	-- A HUMAN LOBBY IS NOT TEST MODE, so none of that applies to a real game: the tick guard is off,
	-- the objective is registered on the first tick, and the time limit resolves a winner normally.
	-- The lobby feature this scenario is checking was never broken; the detector was.
	local endingAt = nil        -- first elapsed tick Test.DoomsdayState() left phase=0
	local endingState = "?"     -- last DoomsdayState reading
	local endingSeenState = nil -- the reading at the tick the ending was first seen
	local finalExchangeSeen = false

	-- Order tallies snapshotted at each phase boundary, per bot. Keyed by bot NAME, never by the
	-- player wrapper: an actor/player wrapper carries no __tostring and cannot key a Lua table
	-- reliably (test-rank-accumulation:237, test-bot-defcon-wall's header).
	local snap = {}             -- snap[phase][botName] = { total = n, ground = n }
	local phaseOrder = {}       -- ordered list of phase names as they actually opened

	local nuclear = {}          -- nuclear[botName] = last GetBotNuclearState string
	local launches = {}         -- launches[botName] = highest launch count seen
	local reason = {}           -- reason[botName]   = last reason token seen
	for _, b in ipairs(bots) do launches[b.name] = 0; reason[b.name] = "?"; nuclear[b.name] = "?" end

	local function tally(b)
		return {
			total  = Test.BotOrdersQueued(b.p),
			ground = Test.BotOrdersQueued(b.p, GROUND_MODULE),
		}
	end

	local function openPhase(name)
		if snap[name] ~= nil then return end
		snap[name] = {}
		phaseOrder[#phaseOrder + 1] = name
		for _, b in ipairs(bots) do snap[name][b.name] = tally(b) end
	end

	-- Orders a bot queued between the opening of `name` and the opening of `nextName` (or now, for
	-- the phase still running). Returns nil when the phase never opened, which is what lets the
	-- verdict distinguish "measured as zero" from "never reached".
	local function delta(name, nextName, botName, field)
		if snap[name] == nil then return nil end
		local from = snap[name][botName][field]
		local to
		if nextName ~= nil and snap[nextName] ~= nil then
			to = snap[nextName][botName][field]
		else
			to = tally({ p = byName[botName] })[field]
		end
		return to - from
	end

	-- `reason=` is a NuclearBotReason enum name; TestGlobal interpolates the enum, so the token is
	-- e.g. NotReleased / NotLosing / RateLimited / Permanent / Retaliation / FinalExchange.
	-- "absent" is the whole-string answer when the module is not on the player at all.
	local function readNuclear(b)
		local s = Test.GetBotNuclearState(b.p)
		nuclear[b.name] = s
		if s == "absent" then
			reason[b.name] = "absent"
			return
		end

		local r = s:match("reason=([^|]*)")
		if r ~= nil then reason[b.name] = r end

		local n = tonumber(s:match("launches=(%d+)") or "")
		if n ~= nil and n > launches[b.name] then launches[b.name] = n end

		if r == "FinalExchange" then finalExchangeSeen = true end
	end

	-- ============================== THE VERDICT ==============================
	local function verdict(why)
		if decided then return end
		decided = true

		local t = elapsed()

		-- ---- 1. THE PHASES HAPPENED, IN ORDER, ON THEIR OWN CLOCKS ----
		if levelAt[3] == nil then
			fault("the match never opened at DEFCON 3: first level read was %s at tick %d. "
				.. "A 0 means DefconEscalation is holding NoLevel -- the mode resolved to Skirmish "
				.. "rather than Escalation, and EVERY assertion below would then pass vacuously",
				tostring(lastLevel), t)
		end

		if levelAt[2] == nil then
			fault("DEFCON never left 3 in %d ticks. The no-rush clock is the shipped 5 minutes = %d "
				.. "ticks and nothing else moves 3 -> 2, so either DefconEscalation is not ticking or "
				.. "its clock was not built from the lobby default", t, NORUSH_TICKS)
		else
			if levelAt[2] < NORUSH_TICKS - PHASE_EARLY_SLACK then
				fault("DEFCON 3 -> 2 at tick %d, EARLIER than the %d-tick no-rush clock. Something "
					.. "other than the clock moved the level; 3 -> 2 has no other trigger",
					levelAt[2], NORUSH_TICKS)
			elseif levelAt[2] > NORUSH_TICKS + LATE_SLACK then
				fault("DEFCON 3 -> 2 at tick %d, %d ticks LATE against the %d-tick no-rush clock",
					levelAt[2], levelAt[2] - NORUSH_TICKS, NORUSH_TICKS)
			else
				note("3->2 at tick %d (clock %d)", levelAt[2], NORUSH_TICKS)
			end
		end

		if levelAt[1] == nil then
			fault("DEFCON never left 2 by tick %d. 2 -> 1 has NO clock -- it is driven solely by "
				.. "DefconCasualtyObserver reporting a qualifying enemy-caused death -- so this is "
				.. "either the two bots never killing anything across the whole cease-fire, or the "
				.. "casualties not qualifying (friendly fire and neutral victims are rejected)", t)
		elseif levelAt[2] ~= nil and levelAt[1] <= levelAt[2] then
			fault("DEFCON reached 1 at tick %d, not after 3 -> 2 at tick %d: the phases did not "
				.. "advance in order", levelAt[1], levelAt[2])
		elseif levelAt[1] ~= nil then
			-- The transition IS the evidence of an enemy-caused death: the state machine has no
			-- 2 -> 1 clock, so there is no other way to reach level 1 from level 2.
			note("2->1 at tick %d (+%d after the wall fell; no clock, so a qualifying kill)",
				levelAt[1], levelAt[1] - (levelAt[2] or 0))
		end

		-- ---- 1b. THE WALL ACTUALLY STOOD DURING DEFCON 3 ----
		-- ADDED AFTER RUN 260914_141246, WHICH WOULD OTHERWISE HAVE PASSED THIS PHASE WALL-LESS.
		-- Every other DEFCON 3 assertion -- the level reading 3, the clock firing at 5000, both bots
		-- queueing orders -- was satisfied in that run while the border was down for the entire
		-- phase, because nothing here was looking at the wall. A no-rush period with no border is
		-- not a slow DEFCON 3; it is a Skirmish opening wearing the label.
		local s3, a3 = wallSamples[3] or 0, wallActive[3] or 0
		if s3 == 0 then
			fault("no sample was ever taken at DEFCON 3, so the wall could not be checked")
		elseif a3 == 0 then
			fault("THE DEFCON 3 WALL NEVER STOOD: %d of %d samples during the phase had "
				.. "Test.DefconWallActive() false. READ debug.log FOR `no line derived from N "
				.. "combatant home(s) in N alliance group(s)` FIRST -- N is the whole diagnosis. "
				.. "N=3 on a two-bot map means a non-combatant slot is being counted as a side, "
				.. "which is what runs 260914_141246 and 260914_181212 both hit via the Observer; "
				.. "that cause is now EXCLUDED BY CONSTRUCTION (CombatantSides.CountsAsASide reads "
				.. "the authored PlayerReference flags, not just the runtime ones), so N=3 here "
				.. "means that predicate REGRESSED and is a real engine defect, not this map. N=2 "
				.. "with no line means the two homes are coincident -- check the pinned "
				.. "HomeLocations. N<2 means a combatant was dropped entirely",
				a3, s3)
		elseif a3 * 100 < s3 * 90 then
			fault("the DEFCON 3 wall stood for only %d of %d samples (%d%%): it is flickering rather "
				.. "than standing, which is a different defect from never deriving -- the geometry "
				.. "resolved, so look at DefconWall.Apply's level gate",
				a3, s3, math.floor(a3 * 100 / s3))
		else
			note("wall up for %d/%d DEFCON 3 samples", a3, s3)
		end

		-- The contract's other half, free to check while we are here: DefconWallInfo.ActiveLevels is
		-- { 3 }, so the wall must be DOWN once the phase has passed. Asserted at level 1 only -- a
		-- sample at level 2 can legitimately catch the tick of the transition itself.
		local s1, a1 = wallSamples[1] or 0, wallActive[1] or 0
		if s1 > 0 and a1 > 0 then
			fault("the wall was still standing for %d of %d samples at DEFCON 1: ActiveLevels is "
				.. "{ 3 }, so the border must be down once open war starts", a1, s1)
		elseif s1 > 0 then
			note("wall down for all %d DEFCON 1 samples", s1)
		end

		-- ---- 2. THE NUCLEAR RELEASE OPENED, ON THE SHIPPED GATE ----
		for _, b in ipairs(bots) do
			if reason[b.name] == "absent" then
				fault("%s has no enabled NuclearBotModule at all (GetBotNuclearState reads "
					.. "\"absent\"), so nothing on that side could evaluate a launch", b.name)
			end
		end

		if levelAt[1] == nil then
			note("release gate not assessed: DEFCON 1 never arrived, so its %d-tick clock never started",
				RELEASE_TICKS)
		elseif releaseAt == nil then
			fault("the nuclear release never opened: both bots' NuclearBotModule still reads "
				.. "reason=NotReleased at tick %d. The gate is the shipped 10 minutes = %d ticks after "
				.. "DEFCON 1 (reached at %d), so it was due at %d",
				t, RELEASE_TICKS, levelAt[1], levelAt[1] + RELEASE_TICKS)
		else
			local due = levelAt[1] + RELEASE_TICKS
			if releaseAt < due - REL_EARLY_SLACK then
				fault("the nuclear release opened at tick %d, EARLIER than the %d-tick gate due at "
					.. "%d. Warheads were handed out before the clock ran", releaseAt, RELEASE_TICKS, due)
			elseif releaseAt > due + LATE_SLACK then
				fault("the nuclear release was observed at tick %d, %d ticks after the gate was due "
					.. "at %d. NuclearBotModule evaluates every 50 ticks, so more than %d ticks of "
					.. "lateness is the gate, not the sampling", releaseAt, releaseAt - due, due, LATE_SLACK)
			else
				note("release at tick %d (due %d)", releaseAt, due)
			end
		end

		-- ---- 3. BOTH BOTS ACTED IN EVERY PHASE THEY REACHED ----
		for i, name in ipairs(phaseOrder) do
			local nextName = phaseOrder[i + 1]
			for _, b in ipairs(bots) do
				local d = delta(name, nextName, b.name, "total")
				if d ~= nil and d <= 0 then
					fault("%s queued NO orders at all during %s: the bot stopped acting for a whole "
						.. "phase. Counted at ModularBot.QueueOrder across every module, so this is "
						.. "the bot going quiet rather than one lane going quiet", b.name, name)
				end
			end
		end

		-- The ground axis specifically, over DEFCON 3 -- the phase whose whole content is staging at
		-- the wall. See GROUND_MODULE for why SquadManagerBotModule is not the alternative it looks.
		for _, b in ipairs(bots) do
			local d = delta("defcon3", "defcon2", b.name, "ground")
			if d ~= nil and d <= 0 then
				fault("%s's %s queued 0 orders across the whole DEFCON 3 phase: the offensive axis "
					.. "never formed, or the free pool was never recruited. On @experimental this "
					.. "module owns the entire ground pool -- SquadManagerBotModule is declared "
					.. "fixed-wing only -- so a zero here is not another module having done the work",
					b.name, GROUND_MODULE)
			end
		end

		-- ---- 4. THE MATCH REACHED AN ENDING ----
		if endingAt == nil then
			fault("the match never reached an ending by tick %d: Test.DoomsdayState() never left "
				.. "phase=0, last reading %q. The time limit is %d ticks, so the final exchange was "
				.. "due at %d. READ debug.log -- both halves of the path now log. `TIME LIMIT "
				.. "expired` absent means the clock never fired (check the TimeLimitTicks override "
				.. "merged). Present, with `FINAL EXCHANGE: declined ... RunInTestMode is false`, "
				.. "means this scenario lost that override. Present with `declined ... lobby option "
				.. "is off` means the doomsday checkbox resolved false. `FINAL EXCHANGE opening` "
				.. "present and this fault still firing means the window opened and the state "
				.. "projection is wrong",
				t, endingState, TIME_LIMIT, TIME_LIMIT)
		elseif endingAt < TIME_LIMIT - LATE_SLACK then
			fault("the ending began at tick %d, well before the %d-tick time limit. Nothing else "
				.. "should open it in this scenario -- a bot firing a game-ender would, but neither "
				.. "holds one outside a retaliation window. State at the time: %q",
				endingAt, TIME_LIMIT, endingSeenState)
		else
			note("ending began at tick %d (time limit %d), state %s", endingAt, TIME_LIMIT, endingSeenState)
		end

		-- ---- 5. LAUNCHES ARE A READING, NOT AN ASSERTION ----
		-- NuclearBotModule fires only when LOSING -- army value under 60 % of the strongest enemy's,
		-- or its own SR control bar under 40 % -- and only after 3 consecutive agreeing evaluations.
		-- Two evenly-matched bots can therefore reach the end with neither ever committed, and that
		-- is the mechanism working, not a fault. The ledger says which it was either way.
		local totalLaunches = launches["USA-bot"] + launches["Russia-bot"]
		if totalLaunches == 0 then
			note("NO LAUNCHES -- %s", table.concat({
				string.format("USA-bot reason=%s", reason["USA-bot"]),
				string.format("Russia-bot reason=%s", reason["Russia-bot"]),
			}, ", "))
		else
			note("launches USA-bot=%d Russia-bot=%d", launches["USA-bot"], launches["Russia-bot"])
		end
		note("final-exchange placement seen=%s", tostring(finalExchangeSeen))

		-- ==== THE READINGS LINE ====
		local orders = {}
		for i, name in ipairs(phaseOrder) do
			local nextName = phaseOrder[i + 1]
			local parts = {}
			for _, b in ipairs(bots) do
				parts[#parts + 1] = string.format("%s tot=%s %s=%s", b.name,
					tostring(delta(name, nextName, b.name, "total")),
					GROUND_MODULE, tostring(delta(name, nextName, b.name, "ground")))
			end
			orders[#orders + 1] = name .. "[" .. table.concat(parts, "; ") .. "]"
		end

		local summary = string.format(
			"stop=%s tick=%d level=%s | phases 3@%s 2@%s 1@%s release@%s ending@%s | "
			.. "wall 3=%d/%d 2=%d/%d 1=%d/%d | "
			.. "doomsday{%s} | win(inert in test mode) USA=%s RUS=%s | "
			.. "orders %s | nuclear USA{%s} RUS{%s}",
			why, t, tostring(lastLevel),
			tostring(levelAt[3]), tostring(levelAt[2]), tostring(levelAt[1]),
			tostring(releaseAt), tostring(endingAt),
			wallActive[3] or 0, wallSamples[3] or 0,
			wallActive[2] or 0, wallSamples[2] or 0,
			wallActive[1] or 0, wallSamples[1] or 0,
			endingState,
			USA.WinState, RUS.WinState,
			table.concat(orders, " "),
			nuclear["USA-bot"], nuclear["Russia-bot"])

		if #faults > 0 then
			Test.Fail(table.concat(faults, " ;; ") .. " ;; READINGS: " .. summary
				.. " ;; NOTES: " .. table.concat(notes, " | "))
		else
			Test.Pass(summary .. " ;; NOTES: " .. table.concat(notes, " | "))
		end
	end

	-- ============================== THE POLL ==============================
	local function poll()
		if decided then return end

		local t = elapsed()
		local level = Test.DefconLevel()
		lastLevel = level

		if not levelSeen[level] then
			levelSeen[level] = true
			levelAt[level] = t
		end

		wallSamples[level] = (wallSamples[level] or 0) + 1
		if Test.DefconWallActive() then
			wallActive[level] = (wallActive[level] or 0) + 1
		end

		-- Phase boundaries, opened in the order the levels actually arrive.
		if level == 3 then openPhase("defcon3") end
		if level == 2 then openPhase("defcon2") end
		if level == 1 then openPhase("defcon1-prerelease") end

		for _, b in ipairs(bots) do readNuclear(b) end

		endingState = Test.DoomsdayState()
		if endingAt == nil then
			local phase = tonumber(endingState:match("phase=(%d+)") or "")
			if phase ~= nil and phase > 0 then
				endingAt = t
				endingSeenState = endingState
			end
		end

		if releaseAt == nil
			and reason["USA-bot"] ~= "NotReleased" and reason["USA-bot"] ~= "?" and reason["USA-bot"] ~= "absent" then
			releaseAt = t
		end
		if releaseAt == nil
			and reason["Russia-bot"] ~= "NotReleased" and reason["Russia-bot"] ~= "?" and reason["Russia-bot"] ~= "absent" then
			releaseAt = t
		end
		if releaseAt ~= nil then openPhase("defcon1-released") end

		-- NO EARLY VERDICT ON THE ENDING ANY MORE. The old code wrote the verdict the instant a
		-- WinState moved, to avoid racing a session teardown. Nothing moves a WinState in test mode,
		-- and the ending itself is NOT terminal for the poller -- the window is 250 ticks and the
		-- salvo runs on after it -- so the run now goes to its deadline and reports the whole tail.
		-- That also keeps the "never pass early" property the exception check depends on.
		if t >= DEADLINE then
			verdict("deadline")
			return
		end

		Trigger.AfterDelay(TICK, poll)
	end

	poll()
end
