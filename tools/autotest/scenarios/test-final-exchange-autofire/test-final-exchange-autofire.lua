-- ASSERTING AUTOTEST -- a side that places nothing has its OWN package fired at the ENEMY, on the
-- same cascade as the side that placed.
--
-- The layout, the border and every compressed clock live in map.yaml and rules.yaml. This file
-- drives one order and reads the ledger.
--
-- ---- NO `//` ANYWHERE IN THIS FILE ------------------------------------------------------------
-- Floor division is Lua 5.3 and Eluant binds an older Lua. lua-gate resolves NAMES, never syntax,
-- and there is no interpreter on the dev machines to parse with, so `math.floor(a / b)` it is.
-- (test-escalation-full-match's header records the one time this bit.)
--
-- ---- TICKS, NOT SECONDS -----------------------------------------------------------------------
-- Timestep is 60 ms, so 16.67 ticks/s -- NOT the 25 that TestHarness.TicksPerSecond carries (a
-- deliberately-preserved harness convention for AssertWithin budgets, documented in
-- test-helpers.lua, and not the tick rate). Every number below is RAW TICKS.
--
-- ---- WHAT IS READ, AND WHY NONE OF IT IS AN ACTOR COUNT ---------------------------------------
-- A missile spends its whole MissileDelay held OUT of the world by SpawnActorEffect
-- (SpawnActorEffect.cs:44-49), so `Map.ActorsInWorld` cannot tell eight warheads in the air from
-- nothing having been fired -- and an aim point is consumed by BallisticMissileFly and stored
-- nowhere a script can reach. Both readings come from the trait instead:
--
--   Test.DoomsdayState()            phase / placements / closes / package / warheads / anchor /
--                                   last / spacing. The whole exchange, as one string.
--   Test.FinalExchangePackage(p)    impacts=<t;t;...>|auto=<x,y;...> for ONE side. `auto` is empty
--                                   for a side that placed its own, which is how the two halves of
--                                   the partition are told apart.
--
-- ---- THE SCHEDULE, ALL OF IT DERIVED FROM rules.yaml ------------------------------------------
--       100  TIME LIMIT EXPIRES -> the window opens. Both sides are armed.
--       130  USA places its Trident. Russia deliberately does not.
--       350  WINDOW CLOSES. Russia's own Sarmat is fired FOR it, at USA's half.
--       610  THE ANCHOR: 350 + FinalExchangeFlightTicks 260. USA slot 0.
--  625/640/655   USA slots 1-3.
--  670/685/700/715   Russia slots 4-7. 715 is the last impact: anchor + (2*4-1) * 15.
--       745  ANNIHILATION (last + AnnihilationDelayTicks 30).
--       760  RESOLUTION (+15). SalvoInProgress goes false here.
--       820  verdict. 60 ticks of margin past the resolution.

local TICK = 5     -- poll granularity. Nothing finer than the 250-tick window is asserted on.
local DEADLINE = 820

-- Mirrors rules.yaml. The window LENGTH is asserted against this; everything else is derived from
-- observations, so a retune of the tail cannot silently move an assertion.
local WINDOW_TICKS = 250
local PACKAGE = 4               -- CellsPerImpact 512 over 2048 playable cells
local SPACING = 15

-- USA places here: east of its own base and well clear of the band, so the placement is on
-- RUSSIA's half -- which is where a player aiming their own package would put it, and is the
-- mirror image of what the machine is about to do for Russia.
local AimPoint = { X = 46, Y = 17 }

-- The two Supply Routes, from map.yaml. Used for the geometric half of the enemy-half assertion:
-- every one of Russia's aim points must be nearer USA's than its own.
local UsaSR = { X = 6, Y = 17 }
local RusSR = { X = 58, Y = 17 }

-- The derived border, from map.yaml's note: the perpendicular bisector of the two homes is the
-- vertical line x = 32, and HalfWidth 2c0 makes the band x in [30, 34].
local BAND_MIN, BAND_MAX = 30, 34

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	-- ==== BOTH SIDES MUST BE IN THE MATCH, AND THIS FAILS AT TICK 1 RATHER THAN AT TICK 350 ====
	-- Run 260920_140352 spent a whole launch slot discovering, from fault 3 at the close, that
	-- Russia was not a side at all. There are TWO ways for that to be true and they fail
	-- differently, so they are checked separately and say different things:
	--
	--   nil Player      the map authored TWO `Playable: True` seats. run-test.sh seats ONE client,
	--                   so the second slot is empty and GetPlayer returns nil (DISCOVERIES,
	--                   2026-09-19). Dereferencing it below would be the first symptom.
	--   not a side      the map authored the second side correctly as a bare map combatant, but
	--                   something under test filters on `Player.Playable`, which is a statement
	--                   about lobby slots and false for a map combatant. THAT is what bit here.
	--
	-- PRINTED BEFORE THE GUARD, deliberately. A Test.Fail on the first line of WorldLoaded writes
	-- an empty lua.log, which is also the documented tell for "the game never launched" -- one
	-- print is what separates the two for whoever reads the run directory.
	local sides = Test.MatchSides()
	print("match sides: " .. sides)

	if USA == nil or Russia == nil then
		Test.Fail(string.format("a named player does not exist: USA=%s Russia=%s. This map must "
			.. "author exactly ONE `Playable: True` seat (the client's) and the other side as a "
			.. "bare map combatant; two playable seats leaves the second one empty and unnamed. "
			.. "Test.MatchSides() says [%s]",
			tostring(USA ~= nil), tostring(Russia ~= nil), sides))
		return
	end

	local sideCount, seen = 0, {}
	for name in sides:gmatch("[^,]+") do
		sideCount = sideCount + 1
		seen[name] = true
	end

	if sideCount ~= 2 or not seen["USA"] or not seen["Russia"] then
		Test.Fail(string.format("this scenario needs BOTH USA and Russia to be sides and "
			.. "CombatantSides.CountsAsASide reports [%s] (%d side(s)). A side missing here is a "
			.. "side nothing in the exchange can arm, aim for or fire at, and every later assertion "
			.. "would be measuring a one-sided match", sides, sideCount))
		return
	end

	local t0 = nil
	local decided = false
	local faults = {}
	local notes = {}

	local function fault(fmt, ...) faults[#faults + 1] = string.format(fmt, ...) end
	local function note(fmt, ...) notes[#notes + 1] = string.format(fmt, ...) end

	local function elapsed()
		if t0 == nil then t0 = 0 end
		return t0
	end

	-- Observations, filled by the poller.
	local endingAt = nil        -- first tick DoomsdayState left phase=0
	local closesTick = nil      -- `closes=` as read on that tick
	local phaseMax = 0
	local placementsMax = 0
	local lastState = "?"
	local salvoSeen = false     -- salvo=true was observed at some point
	local salvoCleared = nil    -- tick at which it went back to false

	local function field(s, name)
		return tonumber(s:match(name .. "=(%-?%d+)") or "")
	end

	-- "x,y;x,y" -> { {X=,Y=}, ... }. An empty string is zero entries, which is the reading for a
	-- side that placed its own.
	local function cells(s)
		local out = {}
		local list = s:match("auto=([^|]*)") or ""
		for x, y in list:gmatch("(%-?%d+),(%-?%d+)") do
			out[#out + 1] = { X = tonumber(x), Y = tonumber(y) }
		end
		return out
	end

	local function ticks(s)
		local out = {}
		local list = s:match("impacts=([^|]*)") or ""
		for v in list:gmatch("(%-?%d+)") do
			out[#out + 1] = tonumber(v)
		end
		return out
	end

	local function dist2(ax, ay, bx, by)
		return ((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by))
	end

	-- ============================== THE VERDICT ==============================
	local function verdict(why)
		if decided then return end
		decided = true

		local t = elapsed()
		local state = Test.DoomsdayState()
		local usa = Test.FinalExchangePackage(USA)
		local rus = Test.FinalExchangePackage(Russia)

		-- ---- 1. THE WINDOW RAN, AND FOR EXACTLY ITS ADVERTISED LENGTH ----
		if endingAt == nil then
			fault("the match never reached an ending by tick %d: Test.DoomsdayState() never left "
				.. "phase=0, last reading %q. READ debug.log -- `TIME LIMIT expired` absent means "
				.. "the clock never fired; `FINAL EXCHANGE: declined ... RunInTestMode is false` "
				.. "means this scenario lost that override; `declined ... lobby option is off` "
				.. "means the doomsday checkbox resolved false", t, lastState)
		else
			note("ending began at tick %d, closes=%s", endingAt, tostring(closesTick))

			-- MEASURED FROM THE OBSERVATION, NOT FROM TimeLimitTicks. The poller samples every 5
			-- ticks, so `endingAt` can be up to TICK-1 late and the comparison carries that
			-- allowance -- which is the whole error, because `closes` is read on the same sample.
			if closesTick == nil then
				fault("the exchange opened at tick %d but closes= was unreadable in %q", endingAt, lastState)
			elseif math.abs((closesTick - endingAt) - WINDOW_TICKS) > TICK then
				fault("the window is %d ticks long, not %d: it opened at tick %d and advertised "
					.. "closes=%d. Check FinalExchangeWindowTicks in this scenario's rules.yaml and "
					.. "in mods/ww3mod/rules/world.yaml -- 0 or less is the documented escape hatch "
					.. "that skips the window entirely, and would read as closes <= 0 here",
					closesTick - endingAt, WINDOW_TICKS, endingAt, closesTick)
			end

			if phaseMax < 2 then
				fault("the exchange opened at tick %d but never CLOSED: highest phase seen was %d "
					.. "and FinalExchangePhase.Closed is 2, so nothing was auto-fired. "
					.. "FinalExchangeWindow.Tick reports the closing edge exactly once and "
					.. "DoomsdayStrike.Tick hangs FirePackagesAndScheduleTheTail off it, so a window "
					.. "that opens and does not close means DoomsdayStrike stopped ticking. READ "
					.. "debug.log for `FINAL EXCHANGE closing at tick`", endingAt, phaseMax)
			end
		end

		-- ---- 2. USA PLACED ITS OWN; RUSSIA HAD ITS OWN FIRED FOR IT ----
		-- The partition, read from the two `auto` lists rather than from `placements`: a side that
		-- placed has an EMPTY auto list, and a side that did not has exactly PACKAGE entries.
		local usaAuto = cells(usa)
		local rusAuto = cells(rus)

		if placementsMax < 1 then
			fault("no side was ever recorded as having placed its own package (placements peaked at "
				.. "%d). USA's order at tick 130 should have set it: DoomsdayStrike.ReportExchangeLaunch "
				.. "records a placement for any warhead NuclearGameEnders.Is() accepts. READ "
				.. "debug.log for the return of Test.ActivateSupportPower -- `not-ready` or `hidden` "
				.. "there means the window never armed the Trident, which is the tier, the condition or "
				.. "the magazine (see DoomsdayStrike.ArmGameEnders)", placementsMax)
		end

		if #usaAuto > 0 then
			fault("USA placed its own package and %d aim point(s) were STILL chosen for it. A side "
				.. "in window.SidesThatPlaced() must be skipped by FireUnplacedPackages, or it fires "
				.. "twice. auto=%q", #usaAuto, usa)
		end

		if #rusAuto ~= PACKAGE then
			fault("Russia placed nothing, so its own package of %d should have been fired for it; "
				.. "%d aim point(s) were chosen instead. Zero means the auto-fire was skipped -- READ "
				.. "debug.log for `is not ready at the close` (the arsenal checkbox, or the banked "
				.. "shot was spent) and `holds no game-ender at all` (no national ender on that "
				.. "faction). A number between 1 and %d means FinalExchangeTargeting could not fill "
				.. "the package, which it is built never to do. rus=%q",
				PACKAGE, #rusAuto, PACKAGE - 1, rus)
		end

		-- ---- 3. EVERY ONE OF RUSSIA'S AIM POINTS IS ON USA'S HALF, AND NONE IS IN THE BAND ----
		-- THE DEFECT DEAD HAND HAD, and the reason this scenario exists. Asserted twice: once
		-- geometrically, which holds under any sane border, and once against the literal band
		-- coordinates. If the second fails while the first passes, the derivation moved -- see
		-- map.yaml for what to re-derive.
		for i, c in ipairs(rusAuto) do
			local toUsa = dist2(c.X, c.Y, UsaSR.X, UsaSR.Y)
			local toRus = dist2(c.X, c.Y, RusSR.X, RusSR.Y)
			if toUsa >= toRus then
				fault("Russia's aim point %d is at %d,%d, which is nearer its OWN Supply Route at "
					.. "%d,%d than USA's at %d,%d. An unplaced package must be aimed at the ENEMY "
					.. "half -- that is the whole of what replaced the side-blind Dead Hand salvo. "
					.. "The classifier is DefconWall.SideOf via DoomsdayStrike.Classifier; if it "
					.. "reads NoSide for the firer's anchor it falls back to spawn proximity, and "
					.. "debug.log says so with `anchor does not classify against the border`",
					i, c.X, c.Y, RusSR.X, RusSR.Y, UsaSR.X, UsaSR.Y)
			end

			if c.X >= BAND_MIN and c.X <= BAND_MAX then
				fault("Russia's aim point %d is at %d,%d, inside the border band (x in [%d, %d]). "
					.. "DefconWall.SideOf answers NoSide for a band cell and FinalExchangeTargeting "
					.. "rejects anything that is not the enemy's side, so a band cell here means the "
					.. "band is not where this file thinks it is or the classifier was bypassed",
					i, c.X, c.Y, BAND_MIN, BAND_MAX)
			end
		end

		-- ---- 4. ONE CASCADE: BOTH PACKAGES INSIDE [anchor, anchor + (2N-1) * spacing] ----
		local anchor = field(state, "anchor")
		local last = field(state, "last")
		local warheads = field(state, "warheads")
		local spacing = field(state, "spacing")
		local package = field(state, "package")

		if package ~= nil and package ~= PACKAGE then
			fault("the package is %d, not the %d this scenario's CellsPerImpact 512 over 2048 "
				.. "playable cells should give. FinalExchangePackage.SizeFor is the arithmetic and "
				.. "FinalExchangePackageTest pins it; a mismatch here means rules.yaml did not take",
				package, PACKAGE)
		end

		-- THE FULL-PACKAGE ASSERTION, TIGHTENED ON 2026-09-20 AFTER IT READ 7 INSTEAD OF 8 IN
		-- test-escalation-full-match. Both sides fired, both were national enders, nothing was
		-- vetoed -- and one of them delivered 3 warheads because NuclearBotModule sized its aim
		-- list from MissileStrikePowerInfo.AimPoints while the power sized the salvo from
		-- DoomsdayStrike.PackageSize. Two layers disagreeing about N is exactly the class of bug
		-- this redesign exists to remove, so a short package is a FAULT and not a tolerance.
		if warheads ~= nil and warheads ~= 2 * PACKAGE then
			fault("%d warhead(s) took a cascade slot, not the %d two full packages make. Every "
				.. "game-ender warhead fired inside the exchange reserves exactly one slot in "
				.. "DoomsdayStrike.ScheduleExchangeImpact; a shortfall means some of them flew on "
				.. "their own schedule, which is the defect this whole design removes",
				warheads, 2 * PACKAGE)
		end

		if anchor ~= nil and last ~= nil and spacing ~= nil and warheads ~= nil and warheads > 0 then
			local span = anchor + ((warheads - 1) * spacing)
			if last ~= span then
				fault("the cascade's last impact is tick %d, not the %d that %d slots at a spacing "
					.. "of %d from anchor %d give. The slots are sequential in launch order "
					.. "(FinalExchangeCascade), so this is arithmetic and not a tolerance",
					last, span, warheads, spacing, anchor)
			end

			if closesTick ~= nil and anchor <= closesTick then
				fault("the cascade's anchor is tick %d, at or BEFORE the window's close at %d. "
					.. "Nothing may land before the window shuts -- that is the user-visible half of "
					.. "this redesign -- and the anchor is floored at close + FinalExchangeFlightTicks "
					.. "for exactly that reason. Check FinalExchangeFlightTicks in rules.yaml",
					anchor, closesTick)
			end

			for who, s in pairs({ USA = usa, Russia = rus }) do
				for i, tick in ipairs(ticks(s)) do
					if tick < anchor or tick > span then
						fault("%s's warhead %d is scheduled to detonate at tick %d, outside the "
							.. "cascade's [%d, %d]. FinalExchangeCascadeTest pins that no placement "
							.. "time inside the window can produce this, so a failure here is the "
							.. "plumbing rather than the arithmetic -- most likely a warhead that "
							.. "never reached ScheduleExchangeImpact at all",
							who, i, tick, anchor, span)
					end
				end
			end
		end

		-- ---- 5. THE MATCH ACTUALLY RESOLVED ----
		-- WinState cannot answer this: ConquestVictoryConditions.Tick returns early under TestMode
		-- by design, objectiveID is assigned only inside it, and its NotifyTimerExpired opens with
		-- `if (objectiveID < 0) return;`. So no test-mode scenario can observe a WinState leave
		-- Undefined however completely the match ended. SalvoInProgress going false at
		-- DoomsdayStrike.Resolve is the observable.
		if not salvoSeen then
			fault("SalvoInProgress was never observed true, so the ending never ran at all. Last "
				.. "reading %q", lastState)
		elseif salvoCleared == nil then
			fault("the exchange began but never RESOLVED by tick %d: salvo= was still true. The "
				.. "verdict is applied at lastImpact + AnnihilationDelayTicks + ResolutionDelayTicks, "
				.. "which on this scenario's clocks is ~760. Last reading %q", t, lastState)
		else
			note("resolved at tick %d", salvoCleared)
		end

		local summary = string.format(
			"stop=%s tick=%d | opened@%s closes=%s phaseMax=%d placements=%d | %s | "
			.. "usa{%s} rus{%s} | resolved@%s",
			why, t, tostring(endingAt), tostring(closesTick), phaseMax, placementsMax,
			state, usa, rus, tostring(salvoCleared))

		if #faults > 0 then
			Test.Fail(table.concat(faults, " ;; ") .. " ;; READINGS: " .. summary
				.. " ;; NOTES: " .. table.concat(notes, " | "))
		else
			Test.Pass(summary .. " ;; NOTES: " .. table.concat(notes, " | "))
		end
	end

	-- ============================== THE ONE ORDER ==============================
	-- USA places. Russia deliberately does not, and that is the subject.
	--
	-- A SINGLE-TARGET ORDER, which is what this binding issues and what a bot issues. The power is
	-- a game-ender, so MissileStrikePower asks DoomsdayStrike for the package size and lays the
	-- remaining RVs on the AimPointFallbackSpread ring around this click. That path is only
	-- exercised because the binding cannot drive placement mode -- SelectMultiPowerTarget is a
	-- client-local order generator with no scripted entry point.
	local PlacementStatus = "not attempted"
	Trigger.AfterDelay(130, function()
		PlacementStatus = Test.ActivateSupportPower(USA, "TridentStrike", CPos.New(AimPoint.X, AimPoint.Y))
		note("USA placement order returned %q", PlacementStatus)
	end)

	-- ============================== THE POLL ==============================
	local function poll()
		if decided then return end

		t0 = (t0 or 0) + TICK

		local state = Test.DoomsdayState()
		lastState = state

		local phase = field(state, "phase") or 0
		if phase > phaseMax then phaseMax = phase end

		local placements = field(state, "placements") or 0
		if placements > placementsMax then placementsMax = placements end

		if phase > 0 and endingAt == nil then
			endingAt = t0
			closesTick = field(state, "closes")
		end

		if state:match("salvo=true") then
			salvoSeen = true
		elseif salvoSeen and salvoCleared == nil then
			salvoCleared = t0
		end

		if t0 >= DEADLINE then
			verdict("deadline")
			return
		end

		Trigger.AfterDelay(TICK, poll)
	end

	Trigger.AfterDelay(TICK, poll)
end
