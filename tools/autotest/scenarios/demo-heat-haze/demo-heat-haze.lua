-- DEMO -- the generic heat-haze system: a screen-space refraction shimmer over hot air. Nothing here
-- is asserted: no AssertWithin, no Test.Pass, no Test.Fail, no result.json. The viewer watches and
-- closes the window when done.
--
-- This file does three things: it grants two external conditions (each starting one TimedHeatSource
-- on one specific soldier) and it kills one bradley whose Explodes fires the one warhead-emitted
-- heat event. Everything about what the shimmer LOOKS like is in rules.yaml and weapons.yaml.
--
-- Timestep is 60 ms, so 16.67 ticks per second -- NOT the 25 that TestHarness.TicksPerSecond carries
-- (a deliberately-preserved harness convention for AssertWithin budgets, documented in
-- test-helpers.lua, and not the tick rate). Every delay below is in RAW TICKS.
--
-- NOTHING IS DAMAGED AND NOTHING DIES except the one probe bradley, which is the point: refraction
-- is only visible against contrast, so the 487-actor starburst has to survive to be distorted.
--
-- ===========================================================================================
-- THE SCHEDULE
-- ===========================================================================================
--     t=1     94,103   BURNING WRECK starts and never stops. 1.25-cell radius, ~2.8 px peak, a
--                      70-tick looping cycle. It is the only thing shimmering until t=20, so it is
--                      what the opening frames show, and it is still going at the end.
--     t=20    26,104   TACTICAL, ~20 kt. Radius grows 0.7 -> 2.78 cells, peak 9 px at t=30, gone by
--                      t=92. Four and a half seconds, start to finish.
--     t=200   71,64    STRATEGIC, ~6 Mt. The probe bradley is killed here; its Explodes@HeatDemo
--                      fires DemoStrategicHeat, whose HeatEvent warhead emits at the impact point.
--                      Radius grows 6.8 -> 27 cells, peak 9 px at t=324, gone by t=1440.
--
-- ===========================================================================================
-- THE FRAMES WORTH CAPTURING -- and read this before screenshotting, because a still frame of an
-- animated distortion is easy to take badly.
-- ===========================================================================================
-- WHAT YOU ARE LOOKING FOR is straight edges that have stopped being straight. The starburst is
-- full of guard towers, pillboxes, helipads and airfields, all of which have hard horizontal and
-- vertical lines; inside a heat event those lines acquire a slow vertical waviness and the units
-- behind them appear to swim. There is no colour change, no brightness change and no sprite: if you
-- are looking for something drawn, you will not see it.
--
--     t=30    the TACTICAL event at its 9 px peak, centred on 26,104. The whole distorted disc is
--             5.6 cells across. This is the frame that shows the feature is worth having: at 20 kt
--             the FIREBALL is 1.4 cells across and no sprite can express it, but a 5.6-cell wobble
--             over the buildings around it reads immediately.
--     t=324   the STRATEGIC event at its 9 px peak, centred on 71,64, 54 cells across. Same peak
--             strength as the tactical one at ten times the radius -- that is the intended
--             comparison, and it is why the two are staged at the same peak value.
--     t=600   the strategic event decaying: still 24+ cells wide, about 4 px of displacement. The
--             long tail is the physically interesting part -- the flash it belongs to would have
--             been over 400 ticks ago.
--     ANY     the burning wreck at 94,103, at any tick from 1 onward. 1.25 cells of fast fine
--             shimmer, next to a strategic event 45 cells away that is 22 times its radius and 3
--             times its strength, both drawn by the same code.
--
-- TWO FRAMES ~10 TICKS APART AT THE SAME CAMERA POSITION are worth more than one, because the
-- shimmer is a MOTION. Comparing t=320 and t=330 shows the pattern climbing; a single frame shows
-- only that the edges are bent.
--
-- THE CAMERA CANNOT BE ZOOMED FROM LUA -- CameraGlobal exposes Position and nothing else. It starts
-- on the tactical site, moves to the strategic one at t=150 and stays. On a 128x128 map the default
-- zoom shows about a third of the playfield, which frames the 54-cell strategic disc reasonably;
-- zoom IN one step for the tactical event, which is small.

local WreckTick = 1
local TacticalTick = 20
local StrategicTick = 200
local GrantAdvance = 10

local TacticalSite = { X = 26, Y = 104 }
local StrategicSite = { X = 71, Y = 64 }

-- Cell centre in world coordinates is cell * 1024 + 512.
local function Look(x, y)
	Camera.Position = WPos.New(x * 1024 + 512, y * 1024 + 512, 0)
end

local function StartAt(tick, emitter, condition)
	Trigger.AfterDelay(tick, function()
		if not emitter.IsDead then
			emitter.GrantCondition(condition)
		end
	end)
end

WorldLoaded = function()
	Look(TacticalSite.X, TacticalSite.Y)

	-- Map actor names are bound as globals by the time WorldLoaded runs. Named one per line rather
	-- than looked up from a table of strings: this Lua runtime is Eluant with a restricted global
	-- environment, and nothing else in the repo indexes _G.
	StartAt(WreckTick, WreckEmitter, "hh-wreck")
	StartAt(TacticalTick, TacticalEmitter, "hh-tactical")

	-- The condition is granted GrantAdvance ticks before the kill and never revoked. Granting and
	-- killing on the same tick would race the trait's enable: external conditions are applied in
	-- Actor.Tick's update pass, so the Explodes would still be disabled when Killed fires.
	StartAt(StrategicTick - GrantAdvance, StrategicProbe, "hh-strategic")
	Trigger.AfterDelay(StrategicTick, function()
		if not StrategicProbe.IsDead then
			StrategicProbe.Kill()
		end
	end)

	Trigger.AfterDelay(150, function() Look(StrategicSite.X, StrategicSite.Y) end)
end
