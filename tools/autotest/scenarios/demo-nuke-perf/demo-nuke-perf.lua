-- PERF RIG -- ONE FULL SIX-RV SARMAT SALVO
--
-- THIS IS A MEASUREMENT SCENARIO, NOT A DEMO TO LOOK AT. It fires one nuclear order at a pinned
-- tick and then does nothing at all for the rest of the run, so that every millisecond the engine
-- spends after that tick is spent on the detonation and not on this script. Nothing here asserts:
-- there is no AssertWithin and no Test.Pass.
--
-- IT DOES END ON Test.Skip, AND THAT IS DELIBERATE AGAINST DEMO.md's "no verdict". Two reasons,
-- both mechanical:
--   1. The benchmark CSVs this rig exists to produce are written from World.Dispose ->
--      Game.FinishBenchmark (World.cs:726), i.e. only on a CLEAN exit. A run the 300 s watchdog
--      kills writes NOTHING, and a killed process does not flush Log's buffered writers either.
--   2. The run has to be the SAME LENGTH every time or the per-tick distributions are not
--      comparable between a before and an after.
-- A `skip` verdict is not a claim that anything passed -- it is the run saying "I finished, the
-- numbers are on disk, a person decides". demo-doomsday-deadhand, demo-danger-overlay and
-- demo-territory-overlay all end this way for the same reason.
--
-- ---- TICKS, NOT SECONDS -----------------------------------------------------------------
-- Timestep is 60 ms, so 16.67 ticks/s -- NOT the 25 that TestHarness.TicksPerSecond carries (a
-- preserved harness convention for AssertWithin budgets, documented in test-helpers.lua, and not
-- the tick rate). Every number below is RAW TICKS.
--
-- ---- THE SCHEDULE, AND WHY EVERY NUMBER IN IT IS PINNED ---------------------------------
-- The analysis script slices the log into a BEFORE window and a DETONATION window, and it can only
-- do that if the impact tick is a constant. It is:
--
--     tick    5   earliest a production property may be touched (TestHarness.ProductionWarmupTicks;
--                 getting this wrong fails PERMANENTLY, not transiently -- the Powers queue is
--                 snapshotted on first access and a tick-1 touch captures an empty map).
--     tick   ~7   power.sarmat purchased; BuildDuration is 5 in rules.yaml.
--     tick   60   ORDER ISSUED. Pinned, not "as soon as ready", so the window is a constant.
--     tick  246   FIRST IMPACT. 60 + MissileDelay 60 + PreLaunchTicks 0 + flight 126.
--                 Flight is standoff / Speed with no TerminalAcceleration on this body:
--                 standoff = mapDiagonal + ApproachMargin = 185363 + 16384 = 201747 wdist,
--                 Speed 1600 -> 126 ticks. (Engine b69681d2; the derivation is written out in
--                 demo-nuke-arsenal's map.yaml header.)
--     tick  246-306   the six RVs land, 12 ticks apart (AimPointInterval 12 x 5 = 60).
--     tick 1000   Test.Skip. Verdict SKIP, exit 2.
--
-- 1000 is chosen to outlast the longest thing a detonation starts, not to be round:
-- @FireballLight runs 307 ticks from impact and @BlastWave's ShockwaveEffect ~400, so the last
-- effect of the last warhead is done by ~~706. The remainder is quiet tail, and the quiet tail
-- is not waste -- it is the baseline the detonation window is measured against.
--
-- IF YOU CHANGE ANY TICK IN HERE, change tools/nuke-perf/analyse.py's defaults in the same edit.

local GroundZero = { X = 64, Y = 64 }

-- PINNED. Not "fire when ready" -- see the schedule note above.
local FireTick = 60

-- Pinned end of run.
local EndTick = 1000

-- Tick from which the purchase is attempted. Must be >= TestHarness.ProductionWarmupTicks.
local BuyTick = 5

-- How long the order keeps retrying past FireTick before the run gives up and finishes anyway. A
-- rig that hangs forever on an unbuyable power is worse than one that returns a flat log and says
-- why, so this is short and the reason is printed.
local FirePatience = 120

local tick = 0
local USA
local fired = false
local gave_up = false
local buy_status = "not-started"

-- Everything this rig has to say goes to lua.log via print(), NOT to the chat log: the analysis
-- script reads it, and a run nobody watched still has to be diagnosable. The NUKEPERF prefix is
-- what analyse.py greps for -- keep it if you change these lines.
local function say(msg)
	print("NUKEPERF " .. msg)
end

local function step()
	tick = tick + 1

	if not fired and not gave_up then
		if tick >= BuyTick then
			local ready, status = TestHarness.EnsurePower(USA, "power.sarmat", "SarmatStrike", tick)
			buy_status = status
			if ready then
				buy_status = "banked"
			end
		end

		if tick >= FireTick then
			-- The only way to issue a support-power order from script. Its result IS read: with the
			-- power bought rather than charged there are two separate ways for nothing to happen --
			-- an empty magazine and a closed gate -- and a flat perf log looks identical to both.
			local status = Test.ActivateSupportPower(USA, "SarmatStrike",
				CPos.New(GroundZero.X, GroundZero.Y))
			if status == "issued" then
				fired = true
				say("order tick=" .. tick .. " arm=salvo warheads=6 groundzero="
					.. GroundZero.X .. "," .. GroundZero.Y)
				say("expect first_impact tick=" .. (tick + 186) .. " last_impact tick=" .. (tick + 246))
			elseif tick >= FireTick + FirePatience then
				gave_up = true
				-- 'refused' means the buy tab would not take the order (check
				-- PowersSandboxCheckboxEnabled and DefaultCash in rules.yaml), 'absent' means the
				-- OrderName is wrong, 'not-ready:<n>' means the magazine is still empty.
				say("NOT-FIRED status=" .. status .. " magazine=" .. buy_status
					.. " -- every number from this run is a number about an empty map")
			end
		end
	end

	if tick >= EndTick then
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

	UserInterface.SetMissionText("NUKE PERF RIG -- ONE FULL SIX-RV SARMAT SALVO")
	say("loaded arm=salvo firetick=" .. FireTick .. " endtick=" .. EndTick)

	Trigger.AfterDelay(1, step)
end
