-- PERF RIG -- ONE FULL SIX-RV SARMAT SALVO
--
-- THIS IS A MEASUREMENT SCENARIO, NOT A DEMO TO LOOK AT. It fires one nuclear order as early as it
-- can and then does nothing at all for the rest of the run, so that every millisecond after the
-- impact is a millisecond the engine spent on the detonation. Nothing here asserts: there is no
-- AssertWithin and no Test.Pass.
--
-- ==== RUSSIA FIRES. NOT USA. THIS IS THE WHOLE REASON THE RIG SPENT THREE RUNS FIRING NOTHING ====
-- MissileStrikePower@Sarmat declares `Prerequisites: powers.event, player.russia`
-- (player.yaml:238-239), and prerequisites are ANDed (TechTree.cs:65-70). A `player.<faction>` name
-- is an IDENTITY: player.yaml:227-230 states it is "provided by faction alone ... and NEVER by the
-- sandbox option". So no lobby setting, no condition and no amount of cash can hand the Sarmat to
-- an america player -- the SupportPowerInstance stays Disabled, GetSupportPowerState reports
-- `hidden` forever, and TestHarness.EnsurePower buys into a bin that will never draw it.
--
-- There is a shipped test whose whole job is to forbid what this rig was doing:
-- test-nuclear-ender-level annotates SarmatStrike as "powers.event + player.russia" (:80) and
-- fails with "USA WAS HANDED RUSSIA'S WARHEAD" (:544) if america ever gets it.
--
-- Russia is NOT `Playable: True` in map.yaml and does not need to be -- test-nuclear-ender-level
-- and test-nuclear-exchange both fire powers from a non-playable Russia the same way. It owns a
-- Supply Route at 10,10 (map.yaml `OpponentSR`), which is the producer its Powers queue needs, and
-- PlayerResources.DefaultCash is set on the `Player:` node so it applies to every player.
--
-- RUSSIA NUKES ITS OWN ARMY, ON PURPOSE. All 448 combat actors are Russia's. Warhead.
-- ValidRelationships defaults to Ally | Neutral | Enemy (Warhead.cs:36) and AffectsParent excludes
-- only the firing ACTOR, so ownership changes nothing about what the detonation costs -- which is
-- the only quantity this scenario exists to produce. Do not "fix" it by re-owning the army; that
-- would change the actor count, which is an input to every number taken against this rig.
--
-- The missile now enters from Russia's home (10,10) rather than USA's (64,118), so the approach
-- bearing is different. The FLIGHT TIME IS NOT: the spawn standoff is mapDiagonal + ApproachMargin
-- = 201747 wdist regardless of bearing, so 201747 / Speed 1600 is still 126 ticks and every tick in
-- the schedule below is unchanged.
--
-- IT ENDS ON Test.Skip, AND THAT IS DELIBERATE AGAINST DEMO.md's "no verdict". Two reasons, both
-- mechanical:
--   1. The benchmark CSVs this rig exists to produce are written from World.Dispose ->
--      Game.FinishBenchmark (World.cs:726), i.e. only on a CLEAN exit. A run the watchdog kills
--      writes NOTHING, and a killed process does not flush Log's buffered writers either.
--   2. The detonation window has to be the same length every time or the per-tick distributions
--      are not comparable between a before and an after.
-- A `skip` verdict is not a claim that anything passed.
--
-- ---- TICKS, NOT SECONDS -----------------------------------------------------------------
-- Timestep is 60 ms, so 16.67 ticks/s -- NOT the 25 that TestHarness.TicksPerSecond carries (a
-- preserved harness convention for AssertWithin budgets, documented in test-helpers.lua, and not
-- the tick rate). Every number below is RAW TICKS.
--
-- ---- WHY THIS FILE DIAGNOSES ITSELF ------------------------------------------------------
-- Run 1 produced a clean 1000-tick log of NOTHING HAPPENING, which is the worst failure this rig
-- can have: an empty map is indistinguishable from a cheap detonation. Run 3 added the state probe
-- and cost one more slot to learn `state=hidden queue_remaining=-1 bin=[empty]` -- enough to rule
-- out the queue and the magazine, but not enough to name WHICH gate was shut. The probe now prints
-- the two prerequisites by name, so a hidden power says its own reason:
--
--     prereq_event=false      the event tier is unmet -- check PowersSandboxCheckboxEnabled.
--     prereq_faction=false    THIS PLAYER IS THE WRONG FACTION. No setting fixes it.
--     both true, hidden       the `nuclear-release-gameender` condition is the blocker --
--                             check the NuclearUnlockClock pin in rules.yaml.
--     queue_remaining=-1      nothing queued: Build accepted and dropped, or no producer.
--     queue_remaining>=0      it IS building and counting down.
--
-- ---- THE SCHEDULE -----------------------------------------------------------------------
--     tick    5   earliest a production property may be touched (TestHarness.ProductionWarmupTicks;
--                 getting this wrong fails PERMANENTLY, not transiently -- the Powers queue is
--                 snapshotted on first access and a tick-1 touch captures an empty map).
--     tick   60   FIRST ORDER ATTEMPT, retried every tick until it is accepted.
--     +186        FIRST IMPACT, measured from whichever tick the order was accepted on.
--                 MissileDelay 60 + PreLaunchTicks 0 + flight 126.
--     +186..+246   the six RVs land, 12 ticks apart (AimPointInterval 12 x 5 = 60).
--     +940        Test.Skip. Verdict SKIP, exit 2.
--
-- 940 ticks past the order outlasts the longest thing a detonation starts -- @FireballLight runs
-- 307 ticks from impact and @BlastWave's ShockwaveEffect ~400. The remainder is quiet tail, and
-- the quiet tail is the baseline the detonation window is measured against.
--
-- analyse.py reads the ACTUAL order tick out of the markers below rather than assuming this
-- schedule, so a late shot is still windowed correctly.

local GroundZero = { X = 64, Y = 64 }
local PowerKey = "SarmatStrike"
local ProxyType = "power.sarmat"

-- THE FIRING PLAYER MUST BE A `russia` FACTION PLAYER. See the header.
local FirerName = "Russia"

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
local Firer
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

-- Every gate in the chain, in one line. Printed on CHANGE rather than every tick: a 1000-tick run
-- would otherwise bury the one transition that matters under a thousand identical lines.
local function probe(tag)
	if tick < TestHarness.ProductionWarmupTicks then
		return
	end

	local state = Test.GetSupportPowerState(Firer, PowerKey)
	local bin = Test.GetSupportPowerBin(Firer)
	local queued = Test.GetQueueRemainingTime(Firer, ProxyType)
	local line = "state=" .. state
		.. " prereq_event=" .. tostring(Firer.HasPrerequisites({ "powers.event" }))
		.. " prereq_faction=" .. tostring(Firer.HasPrerequisites({ "player.russia" }))
		.. " queue_remaining=" .. queued
		.. " buy=" .. buy_status
		.. " bin=[" .. bin .. "]"

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
			local ready, status = TestHarness.EnsurePower(Firer, ProxyType, PowerKey, tick)
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
			local status = Test.ActivateSupportPower(Firer, PowerKey,
				CPos.New(GroundZero.X, GroundZero.Y))
			if status == "issued" then
				fired = true
				end_tick = tick + DetonationWindowTicks
				say("order tick=" .. tick .. " arm=salvo warheads=6 firer=" .. FirerName
					.. " groundzero=" .. GroundZero.X .. "," .. GroundZero.Y)
				say("expect first_impact tick=" .. (tick + 186)
					.. " last_impact tick=" .. (tick + 246))
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
	Firer = Player.GetPlayer(FirerName)

	-- Cell centre in world coordinates is cell * 1024 + 512.
	Camera.Position = WPos.New(GroundZero.X * 1024 + 512, GroundZero.Y * 1024 + 512, 0)

	-- Framed for a human who runs this VISIBLE (profile B in tools/nuke-perf/README.md). Under
	-- --hidden nothing is drawn and this costs nothing.
	Camera.Zoom = Camera.MinZoom

	UserInterface.SetMissionText("NUKE PERF RIG -- ONE FULL SIX-RV SARMAT SALVO")
	say("loaded arm=salvo firer=" .. FirerName .. " firetick=" .. FireTick
		.. " deadline=" .. FireDeadline)

	Trigger.AfterDelay(1, step)
end
