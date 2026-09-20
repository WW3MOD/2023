-- PERF RIG -- ONE NukeSarmatRV -- the single-warhead arm
--
-- THIS IS A MEASUREMENT SCENARIO, NOT A DEMO TO LOOK AT. It fires one nuclear order as early as it
-- can and then does nothing at all for the rest of the run, so that every millisecond after the
-- impact is a millisecond the engine spent on the detonation. Nothing here asserts: there is no
-- AssertWithin and no Test.Pass.
--
-- IT DOES END ON Test.Skip, AND THAT IS DELIBERATE AGAINST DEMO.md's "no verdict". Two reasons,
-- both mechanical:
--   1. The benchmark CSVs this rig exists to produce are written from World.Dispose ->
--      Game.FinishBenchmark (World.cs:726), i.e. only on a CLEAN exit. A run the watchdog kills
--      writes NOTHING, and a killed process does not flush Log's buffered writers either.
--   2. The detonation window has to be the same length every time or the per-tick distributions
--      are not comparable between a before and an after.
-- A `skip` verdict is not a claim that anything passed. demo-doomsday-deadhand,
-- demo-danger-overlay and demo-territory-overlay all end this way for the same reason.
--
-- ---- TICKS, NOT SECONDS -----------------------------------------------------------------
-- Timestep is 60 ms, so 16.67 ticks/s -- NOT the 25 that TestHarness.TicksPerSecond carries (a
-- preserved harness convention for AssertWithin budgets, documented in test-helpers.lua, and not
-- the tick rate). Every number below is RAW TICKS.
--
-- ---- WHY THIS FILE KEEPS TRYING, AND DIAGNOSES ITSELF ------------------------------------
-- The first run of this rig produced a perfectly clean 1000-tick log of NOTHING HAPPENING: the
-- power stayed hidden, the magazine never banked, and the run reported an empty map. That is the
-- worst failure this rig can have, because an empty map looks exactly like a cheap detonation --
-- and it cost a launch slot to learn one word ("not-ready").
--
-- So: the buy/fire loop now runs until FireDeadline instead of giving up 120 ticks in; the END OF
-- THE RUN MOVES WITH THE ORDER (a late shot still gets its full measurement window rather than
-- being clipped); and every change in the power's state, its queue and the support-power bin is
-- printed. If it fails again, the log says WHICH of the four links broke instead of leaving the
-- next reader to guess:
--
--     state=hidden        the SupportPowerInstance is Disabled -- either the
--                         `nuclear-release-gameender` condition is ungranted (see the
--                         NuclearUnlockClock block in rules.yaml) or the magazine is empty.
--                         `bin` disambiguates: a hidden power is not drawn at all.
--     queue_remaining=-1  the purchase is NOT in the queue. Build was accepted and dropped, or
--                         the Powers queue has no producer (the Supply Route).
--     queue_remaining>=0  it IS building; the number counts down. If it reaches 0 and the state
--                         stays hidden, the bank is filling and the CONDITION is the blocker.
--     buy=refused         the proxy is not in BuildableItems: tier prerequisite unmet (event tier
--                         without the sandbox option), or no producer.
--
-- ---- THE SCHEDULE -----------------------------------------------------------------------
--     tick    5   earliest a production property may be touched (TestHarness.ProductionWarmupTicks;
--                 getting this wrong fails PERMANENTLY, not transiently -- the Powers queue is
--                 snapshotted on first access and a tick-1 touch captures an empty map).
--     tick   60   FIRST ORDER ATTEMPT, retried every tick until it is accepted.
--     +186        FIRST IMPACT, measured from whichever tick the order was accepted on.
--                 MissileDelay 60 + PreLaunchTicks 0 + flight 126. Flight is standoff / Speed with
--                 no TerminalAcceleration on this body: standoff = mapDiagonal + ApproachMargin =
--                 185363 + 16384 = 201747 wdist, Speed 1600 -> 126 ticks. (Engine b69681d2; the
--                 derivation is written out in demo-nuke-arsenal's map.yaml header.)
--     +186        the one RV lands. AimPointInterval is not read at AimPoints 1.
--     +940        Test.Skip. Verdict SKIP, exit 2.
--
-- 940 ticks past the order outlasts the longest thing a detonation starts -- @FireballLight runs
-- 307 ticks from impact and @BlastWave's ShockwaveEffect ~400, so the last effect of the last
-- warhead is done well inside it. The remainder is quiet tail, and the quiet tail is not waste:
-- it is the baseline the detonation window is measured against.
--
-- analyse.py reads the ACTUAL order tick out of the markers below rather than assuming this
-- schedule, so a late shot is still windowed correctly.

local GroundZero = { X = 64, Y = 64 }
local PowerKey = "SarmatStrike"
local ProxyType = "power.sarmat"

-- First tick an order is attempted. Not "as soon as ready": a constant makes two runs comparable.
local FireTick = 60

-- Give up ordering after this. Generous, because a run that fires late still measures a real
-- detonation while a run that never fires measures nothing at all.
local FireDeadline = 560

-- Ticks to keep running after the order is accepted.
local DetonationWindowTicks = 940

-- End tick if nothing is ever fired -- still a usable quiet-map baseline.
local EmptyRunEndTick = 1000

-- Tick from which the purchase is attempted. Must be >= TestHarness.ProductionWarmupTicks.
local BuyTick = 5

local tick = 0
local USA
local fired = false
local end_tick = EmptyRunEndTick
local buy_status = "not-started"
local last_probe = ""

-- Everything this rig has to say goes to lua.log via print(), NOT to the chat log: the analysis
-- script reads it, and a run nobody watched still has to be diagnosable. The NUKEPERF prefix is
-- what analyse.py greps for -- keep it if you change these lines.
local function say(msg)
	print("NUKEPERF " .. msg)
end

-- The four links in the chain, in one line. Printed on CHANGE rather than every tick: a 1000-tick
-- run would otherwise bury the one transition that matters under a thousand identical lines.
local function probe(tag)
	if tick < TestHarness.ProductionWarmupTicks then
		return
	end

	local state = Test.GetSupportPowerState(USA, PowerKey)
	local bin = Test.GetSupportPowerBin(USA)
	local queued = Test.GetQueueRemainingTime(USA, ProxyType)
	local line = "state=" .. state .. " queue_remaining=" .. queued
		.. " buy=" .. buy_status .. " bin=[" .. bin .. "]"

	if line ~= last_probe or tag ~= "watch" then
		last_probe = line
		say(tag .. " tick=" .. tick .. " " .. line)
	end
end

local function step()
	tick = tick + 1

	if not fired and tick <= FireDeadline then
		if tick >= BuyTick then
			-- Stateless and idempotent: reads the power's own state, queues a purchase when the
			-- magazine is empty, reports 'loading' while one is in flight.
			local ready, status = TestHarness.EnsurePower(USA, ProxyType, PowerKey, tick)
			buy_status = status
			if ready then
				buy_status = "banked"
			end
		end

		probe("watch")

		if tick >= FireTick then
			-- The only way to issue a support-power order from script. Its result IS read: with the
			-- power bought rather than charged there are two separate ways for nothing to happen --
			-- an empty magazine and a closed gate -- and a flat perf log looks identical to both.
			local status = Test.ActivateSupportPower(USA, PowerKey,
				CPos.New(GroundZero.X, GroundZero.Y))
			if status == "issued" then
				fired = true
				end_tick = tick + DetonationWindowTicks
				say("order tick=" .. tick .. " arm=single warheads=1 groundzero="
					.. GroundZero.X .. "," .. GroundZero.Y)
				say("expect first_impact tick=" .. (tick + 186)
					.. " last_impact tick=" .. (tick + 186))
				say("endtick tick=" .. end_tick)
			end
		end
	elseif not fired and tick == FireDeadline + 1 then
		probe("gave-up")
		say("NOT-FIRED deadline=" .. FireDeadline .. " magazine=" .. buy_status
			.. " -- every number from this run is a number about an empty map")
	end

	if tick >= end_tick then
		say("end tick=" .. tick .. " fired=" .. tostring(fired))
		Test.Skip("perf rig finished at tick " .. tick .. "; fired=" .. tostring(fired)
			.. "; read the benchmark CSVs and perf.log, not this verdict")
		return
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")

	-- Cell centre in world coordinates is cell * 1024 + 512.
	Camera.Position = WPos.New(GroundZero.X * 1024 + 512, GroundZero.Y * 1024 + 512, 0)

	-- Framed for a human who runs this VISIBLE (profile B in tools/nuke-perf/README.md). Under
	-- --hidden nothing is drawn and this costs nothing.
	Camera.Zoom = Camera.MinZoom

	UserInterface.SetMissionText("NUKE PERF RIG -- ONE NukeSarmatRV -- the single-warhead arm")
	say("loaded arm=single firetick=" .. FireTick .. " deadline=" .. FireDeadline)

	Trigger.AfterDelay(1, step)
end
