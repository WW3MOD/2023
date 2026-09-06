-- DEMO -- does the brightened fireball still stop dead at the edge of what has ever been seen?
--
-- Nothing here is asserted: no AssertWithin, no Test.Pass, no Test.Fail, no result.json. The viewer
-- watches, and closes the window when done.
--
-- THE ARRANGEMENT lives in map.yaml and rules.yaml and is described in full there. In one line:
-- the map is NOT pre-explored and fog is on, and one indestructible CAMERA twenty-five cells
-- west of ground zero puts the eastern edge of the explored disc just seven cells east of
-- the burst.
--
-- THE SCHEDULE, in ticks from WorldLoaded. Timestep is 60 ms, so 16.67 ticks per second -- NOT the
-- 25 that TestHarness.TicksPerSecond carries (that is a harness convention for AssertWithin budgets,
-- documented in test-helpers.lua, and is not the tick rate). Every delay below is therefore raw
-- ticks through Trigger.AfterDelay rather than a seconds conversion.
--
--     t=25    order issued at cell 64,64.
--     t=125   missile enters the world at the south edge (MissileDelay 100, overridden from 500).
--     t=243   DETONATION, 15c0 above the aim point. 118 ticks of flight over 63 cells at Speed 550,
--             exact rather than estimated: this missile has Acceleration 0 and no
--             TerminalAcceleration, so BallisticMissileFly does not accelerate it.
--     t=244   fireball at 700% scale. THIS IS THE FRAME THIS DEMO EXISTS FOR.
--     t=313   the three stacked flash warheads finish washing the screen white.
--
-- SO: SCREENSHOT BETWEEN t=320 AND t=500 -- 19 s to 30 s after the map loads. Earlier than t=313
-- and FlashPaletteEffect has the whole screen white, which hides the very thing being looked at;
-- the cloud is still full-height and legible well past t=500.
--
-- The camera is placed and zoomed by this script. The power stays available afterwards
-- (ChargeInterval 400 = 24 s), so the viewer can fire it again anywhere they like.

local OrderTick = 25
local GroundZero = { X = 64, Y = 64 }

local tick = 0
local USA
local fired = false

local function step()
	tick = tick + 1

	if not fired and tick >= OrderTick then
		fired = true
		-- Staging, not an assertion: Test.ActivateSupportPower is the only way to issue a
		-- support-power order from script, and its return value is deliberately not checked. If the
		-- strike never arrives, look at the support-power bin -- a missing cameo means the shipped
		-- lobby default for this power moved, since nothing in rules.yaml forces that gate open.
		Test.ActivateSupportPower(USA, "HighYieldNukeStrike",
			CPos.New(GroundZero.X, GroundZero.Y))
		return
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")

	-- Cell centre in world coordinates is cell * 1024 + 512.
	Camera.Position = WPos.New(GroundZero.X * 1024 + 512, GroundZero.Y * 1024 + 512, 0)

	-- Fully out. Camera.Zoom is a MULTIPLE OF THE DEFAULT level, so Camera.MinZoom is as far out as
	-- this display goes -- read rather than hardcoded, because the floor is derived from the
	-- viewer's resolution and viewport-distance setting. A 700%-scale mushroom cloud is wider than
	-- the default framing on a 128x128 map, and a demo about a boundary running THROUGH the cloud
	-- is worth nothing if either side of that boundary is off screen.
	Camera.Zoom = Camera.MinZoom

	-- Pre-selected so the support-power bin is on screen without the viewer clicking first.
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
