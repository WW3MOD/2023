-- DEMO -- the generic local light-event system. Nothing here is asserted: no AssertWithin, no
-- Test.Pass, no Test.Fail, no result.json. The viewer watches and closes the window when done.
--
-- This file does exactly two things: it grants six external conditions at six known ticks (each one
-- turns on one TimedLightSource on one specific soldier), and it kills one bradley whose Explodes
-- fires the one warhead-emitted light. Everything about what those lights LOOK like is in rules.yaml
-- and weapons.yaml; nothing about the envelopes is expressed here.
--
-- THE SCHEDULE, in ticks from WorldLoaded. Timestep is 60ms (mods/ww3mod/mod.yaml:382), so 16.67 ticks
-- per second -- NOT the 25 that TestHarness.TicksPerSecond carries (a deliberately-preserved harness
-- convention for AssertWithin budgets, documented in test-helpers.lua, and not the tick rate). Every
-- delay below is therefore written in RAW TICKS through Trigger.AfterDelay.
--
--     t=1     32,5   LOOPING FLICKER starts and never stops. 26-tick cycle, small warm light.
--                    It is the only thing lit until t=60, so it is what the opening frame shows.
--     t=60    8,16   SLOW PULSE begins.  40 up / 60 held / 90 down, eased. Ends t=250.
--     t=160   20,16  SHARP FLASH -- the WARHEAD. The bradley at 20,16 is killed here; its
--                    Explodes@LightDemo fires DemoLightFlash, whose LightEvent warhead emits at the
--                    impact point. Peak on the NEXT tick, essentially gone by t=170, ends t=185.
--                    Blink and you miss it: that is the envelope, not a bug.
--     t=260   32,16  DOUBLE FLASH -- the one that matters. Peak 1 at t=262, collapse at t=274,
--                    larger peak 2 at t=280, then 180 ticks cooling white -> yellow -> orange -> dull
--                    red while the radius grows 2c0 -> 22c0. Ends t=460.
--     t=480   44,16  LONG SLOW FADE. Catches by t=492 then dies away for 350 ticks; the radius
--                    SHRINKS 9c0 -> 6c0 as it goes. Ends t=830.
--     t=560   56,16  COLOURED, cyan, deliberately Falloff: Linear so the hard rim is visible.
--                    Ends t=740.
--     t=700   32,26  THE 56-CELL CEILING PROBE. Radius 60c0 -- four cells past
--                    MapGrid.MaximumTileSearchRange, which used to be a hard crash rather than a
--                    glitch. Washes the whole map pale blue for 150 ticks. Ends t=850.
--
-- SO: something is lit from t=1 to t=850, which is 51 seconds. The most informative single frame is
-- around t=280, when the double flash is at its second peak.
--
-- THE CAMERA CANNOT BE ZOOMED FROM LUA (CameraGlobal exposes Position and nothing else). This centres
-- on 32,16, the middle of the row of five sites. On a 64-wide map the default zoom shows roughly half
-- the row; zoom out one step to get all five on screen at once.

local WarheadTick = 160

-- The condition is granted with no duration, so it is never revoked. A non-looping envelope ends
-- itself when it runs off its last keyframe; the looping one runs until the window closes.
local function StartAt(tick, emitter, condition)
	Trigger.AfterDelay(tick, function()
		if not emitter.IsDead then
			emitter.GrantCondition(condition)
		end
	end)
end

WorldLoaded = function()
	-- Cell centre in world coordinates is cell * 1024 + 512.
	Camera.Position = WPos.New(32 * 1024 + 512, 16 * 1024 + 512, 0)

	-- Map actor names are bound as globals by the time WorldLoaded runs, so these are the actors placed
	-- in map.yaml. Named one per line rather than looked up from a table of strings: this Lua runtime is
	-- Eluant with a restricted global environment, and nothing else in the repo indexes _G.
	StartAt(1,   L6Emit, "ld-lamp")
	StartAt(60,  L1Emit, "ld-slowpulse")
	StartAt(260, L3Emit, "ld-doubleflash")
	StartAt(480, L4Emit, "ld-longfade")
	StartAt(560, L5Emit, "ld-coloured")
	StartAt(700, L7Emit, "ld-wide")

	Trigger.AfterDelay(WarheadTick, function()
		if not WarheadProbe.IsDead then
			WarheadProbe.Kill()
		end
	end)
end
