-- DEMO -- vaporisation inside the fireball, husks outside it. Nothing here is asserted: no
-- AssertWithin, no Test.Pass, no Test.Fail, no result.json. The viewer watches and closes the window.
--
-- This file grants one condition per site and then kills that site's probe. Everything about what the
-- effect looks like is in weapons.yaml; everything about the mechanism is in the engine.
--
-- THE SCHEDULE, in ticks from WorldLoaded. Timestep is 60ms (mods/ww3mod/mod.yaml:382), so 16.67 ticks
-- per second -- NOT the 25 that TestHarness.TicksPerSecond carries, which is a harness convention for
-- AssertWithin budgets and not the tick rate. Delays below are RAW TICKS.
--
--     t=90    LEFT site (17,18) detonates. Screen goes white for 30 ticks. Everything inside 2 cells
--             is gone by t=100 with no dissolve ever drawn; everything from 2 to 12 cells dies the
--             ordinary way and leaves a husk. By t=120 the flash has cleared and the boundary is a
--             clean circle of bare ground about four cells across, ringed by wreckage.
--
--     t=300   RIGHT site (48,18) detonates. Same weapon type, different numbers: the 6-cell radius
--             dissolves visibly over 30 ticks. t=300 to t=310 the units inside heat to white; t=310
--             to t=330 they are white silhouettes fading out; by t=330 they are gone. Outside 6
--             cells, husks as usual.
--
-- WHAT TO LOOK AT, and it is the same thing at both sites: THE EDGE. A husk one cell outside the
-- radius and bare ground one cell inside it, on the same detonation, at the same instant.
--
-- The two sites fire 210 ticks apart so the flashes do not overlap and each can be screenshotted
-- alone. Nothing else on this map moves.

local SmallTick = 90
local LargeTick = 300

WorldLoaded = function()
	-- Cell centre in world coordinates is cell * 1024 + 512. Framed between the two sites.
	Camera.Position = WPos.New(32 * 1024 + 512, 18 * 1024 + 512, 0)

	Trigger.AfterDelay(SmallTick, function()
		if not AProbe.IsDead then
			-- The condition selects which Explodes@ fires, so one probe type serves both weapons.
			AProbe.GrantCondition("demo-small")
			AProbe.Kill()
		end
	end)

	Trigger.AfterDelay(LargeTick, function()
		if not BProbe.IsDead then
			BProbe.GrantCondition("demo-large")
			BProbe.Kill()
		end
	end)
end
