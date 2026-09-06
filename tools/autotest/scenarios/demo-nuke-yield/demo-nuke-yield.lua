-- DEMO -- the two nuclear warheads, rebuilt 2026-09-06 so that every radius and delay is derived
-- from a stated yield. Nothing here is asserted: no AssertWithin, no Test.Pass, no Test.Fail, no
-- result.json. The viewer watches and closes the window when done.
--
-- This file does exactly four things: it grants two external conditions and it kills two marker
-- vehicles. Everything about what the weapons look like lives in
-- mods/ww3mod/rules/weapons/weapons-superweapons.yaml, and everything about the map in map.yaml.
--
-- Timestep is 60 ms (mods/ww3mod/mod.yaml:382), so 16.67 ticks per second -- NOT the 25 that
-- TestHarness.TicksPerSecond carries (a deliberately-preserved harness convention for AssertWithin
-- budgets, documented in test-helpers.lua, and not the tick rate). Every delay below is therefore
-- written in RAW TICKS through Trigger.AfterDelay.
--
-- THE DETONATION TICK IS EXACT because the weapon is fired by killing a probe rather than by flying
-- a missile at it: tick 20 for the tactical and tick 200 for the strategic, and every number below
-- is those two plus the warhead's own derived delays.
--
-- ===========================================================================================
-- TACTICAL -- Atomic, ~20 kt, at cell 26,104. DETONATES t=20.
-- ===========================================================================================
--   t=20   FIRST MAXIMUM of the double flash, and the fireball's whole first pulse. At 20 kt the
--          real double flash is over in 0.15 s -- the dip is at 0.19 TICKS -- so what you get is a
--          three-sample reconstruction at the Nyquist limit of a 60 ms timestep. Bright, 6.5 cells.
--   t=21   the dip. Dimmer AND redder (FFC878): during it you are looking at an opaque shock front
--          at a few thousand K rather than the fireball behind it.
--   t=22   *** SECOND MAXIMUM. Intensity 7.0, radius 11.25 cells, pure white. ***
--   t=24   cooling to amber, radius 12 cells -- the thermal radius, which is where the light stops.
--   t=27   orange.
--   t=33   fireball out, on a dull red. Thirteen ticks end to end, which is 0.79 s, which is
--          t = 0.2104 * Y^0.44 seconds at 20 kt.
--   t=22   blast wave passes 2 cells -- it was BORN at 0.695 cells (the fireball surface) travelling
--          at Mach 5.5, so it crosses the inner cells almost instantly.
--   t=46   wavefront at 6 cells. t=74 at 10 cells. t=109 at its full 15 cells, now sonic.
--   t=26   crater (3-cell radius). t=53 inner scorch (7). t=88 outer scorch (12).
--   t=123  last suppression band. The tactical strike is over.
--
-- ===========================================================================================
-- STRATEGIC -- AtomicHighYield, ~6 Mt, at cell 71,64. DETONATES t=200.
-- ===========================================================================================
--   t=200  first maximum, radius 21 cells, and three stacked palette flashes begin.
--   t=203  *** THE DIP, and at this yield it is fully resolved rather than sub-tick. *** Intensity
--          falls to 0.9 and the colour goes ORANGE (FF9040) while the radius keeps growing to 43
--          cells. This is the one frame that shows what the tactical weapon cannot: the shock front
--          has gone opaque and is hiding its own fireball.
--   t=220  climbing again, 5.4, radius 92 cells.
--   t=241  *** SECOND MAXIMUM. Intensity 7.0 -- the SAME peak as the tactical weapon, because
--          fireball surface temperature does not scale with yield -- at radius 124 cells. The whole
--          map is white. This is the single frame to screenshot. ***
--   t=275  amber, still 124 cells.
--   t=315  orange, 118 cells.
--   t=361  fireball out on a dull red, 161 ticks end to end = 9.7 s. The user's note that the
--          duration is "surprisingly long" is the point: this is not a flash, it is a second sun.
--   t=221  blast wave passes 15 cells (born at 6.8 cells at Mach 4.05, three ticks after the flash).
--   t=369  wavefront at 41 cells. t=474 at 56. t=580 at 71. t=692 at 87.
--   t=797  wavefront reaches its full 102 cells. 36 s after the flash.
--   t=234  crater (18). t=302 inner scorch (31). t=411 outer scorch (47).
--   t=890  last suppression band.
--
-- ===========================================================================================
-- THE FRAMES WORTH STOPPING ON
-- ===========================================================================================
--   t=22   the tactical second maximum -- a 12-cell disc of white.
--   t=203  the strategic DIP. Only this weapon is slow enough to show it.
--   t=241  the strategic second maximum -- 124 cells of white. Same brightness, ten times the reach.
--   t=315  cooling. The colour ramp is readable here and nowhere near the peaks.
--   t=474  the wavefront halfway out, with the inner field already gone and the outer field intact.
--   t=450+ ZOOM OUT. Both permanent scorch discs are on the ground from t=411: a 24-cell-wide disc
--          at 26,104 and a 94-cell-wide one at 71,64, 60 cells apart and not overlapping. That is
--          the size comparison, and it stays on screen for the rest of the run.
--
-- THE CAMERA CANNOT BE ZOOMED FROM LUA -- CameraGlobal exposes Position and nothing else. It starts
-- on the tactical burst, moves to the strategic one at t=150, and pulls back to the midpoint of the
-- two at t=400 so both scorch discs are in frame. On a 128x128 map the default zoom shows perhaps a
-- third of the playfield; zoom out by hand for the comparison frames.

local TacticalTick = 20
local StrategicTick = 200
local GrantAdvance = 10

local TacticalGZ = { X = 26, Y = 104 }
local StrategicGZ = { X = 71, Y = 64 }

-- Cell centre in world coordinates is cell * 1024 + 512.
local function Look(x, y)
	Camera.Position = WPos.New(x * 1024 + 512, y * 1024 + 512, 0)
end

-- The condition is granted GrantAdvance ticks before the kill and never revoked. Granting and
-- killing on the same tick would race the trait's enable: external conditions are applied in
-- Actor.Tick's update pass, so the Explodes would still be disabled at the moment Killed fires.
local function Detonate(grantTick, probe, condition)
	Trigger.AfterDelay(grantTick, function()
		if not probe.IsDead then
			probe.GrantCondition(condition)
		end
	end)

	Trigger.AfterDelay(grantTick + GrantAdvance, function()
		if not probe.IsDead then
			probe.Kill()
		end
	end)
end

WorldLoaded = function()
	Look(TacticalGZ.X, TacticalGZ.Y)

	-- Map actor names are bound as globals by the time WorldLoaded runs, so TacProbe and StratProbe
	-- are the two vehicles placed in map.yaml. Named one per line rather than looked up from a table
	-- of strings: this Lua runtime is Eluant with a restricted global environment.
	Detonate(TacticalTick - GrantAdvance, TacProbe, "nuke-probe-tactical")
	Detonate(StrategicTick - GrantAdvance, StratProbe, "nuke-probe-strategic")

	Trigger.AfterDelay(150, function() Look(StrategicGZ.X, StrategicGZ.Y) end)

	-- Midway between the two ground zeros (48,84), so both scorch discs are in frame once the
	-- strategic weapon's outer scorch lands at t=411. Written as literals rather than computed:
	-- this runtime is Eluant, which is Lua 5.1, and `//` is a 5.3 operator that would not parse.
	Trigger.AfterDelay(400, function() Look(48, 84) end)
end
