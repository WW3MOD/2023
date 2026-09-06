-- DEMO -- the high-yield strategic nuclear strike, fired at the centre of the largest terrain in
-- the mod. Nothing here is asserted: no AssertWithin, no Test.Pass, no Test.Fail, no result.json.
-- The viewer watches, and closes the window when done.
--
-- LAYOUT, so this file can be read without map.yaml: ground zero is cell 64,64, the exact centre of
-- a 128x128 playfield. 81 target sites are arranged in nine rings around it at 6, 12, 19, 27, 36,
-- 46, 57, 69 and 82 cells, 4 to 16 bearings per ring. Every site is the same mix -- one structure,
-- one vehicle, two infantry, two trees -- so distance from the burst is the only variable and the
-- gradient reads the same in whichever direction the camera is pointed. USA's Supply Route is on
-- the south edge at 64,118; Russia's is in the far north-west corner at 10,10. Both survive: their
-- only target type is NoAutoTarget and no AtomicHighYield warhead lists it.
--
-- THE SCHEDULE, in ticks from WorldLoaded. Timestep is 60 ms, so 16.67 ticks per second -- NOT the
-- 25 that TestHarness.TicksPerSecond carries (that value is a deliberately-preserved harness
-- convention for AssertWithin budgets, documented in test-helpers.lua, and is not the tick rate).
-- Every delay below is therefore written in RAW TICKS through Trigger.AfterDelay rather than going
-- through a seconds conversion.
--
--     t=25    order issued at cell 64,64. Beacon, minimap ping and AbombLaunchDetected fire now.
--     t=125   missile enters the world at cell 64,127, the south edge (MissileDelay 100, overridden
--             from the shipped 500 in rules.yaml).
--     t=243   DETONATION. 118 ticks of flight over 63 cells at Speed 550, and that number is exact
--             rather than estimated: this missile has Acceleration 0 and no TerminalAcceleration,
--             so BallisticMissileFly does not accelerate it. Burst is 15c0 above the aim point.
--     t=244   flash (3 stacked palette warheads, white through ~t=313), fireball at 700% scale.
--     t=248   blast wave begins expanding at 7 ticks per cell.
--     t=273   thermal radiation pulse ends (200 ticks of it, 50 damage pulses).
--     t=443   fire ignition finishes staging outward to 95 cells.
--     t=962   blast wave reaches its full 102-cell radius. Everything on the map has now been hit.
--     t=967   last suppression band lands.
--     t=1743  innermost fires burn out (1500 ticks).
--     t=2043  the strike camera is removed (CameraRemoveDelay 1800).
--
-- SO: the interesting window is t=243 to t=962 -- 14.6 s to 57.7 s after the map loads. Screenshot
-- anywhere in it. The single most legible frame is around t=500 to t=600, when the wavefront is
-- 35-50 cells out: the inner rings are already gone, the middle band is burning, and the outer
-- rings are still intact and untouched, so all three states are on screen at once.
--
-- THE DEMO ZOOMS ITSELF. Camera.Zoom is a multiple of the default level, so Camera.MinZoom is as
-- far out as this display goes -- deliberately read rather than hardcoded, because the floor is
-- derived from the viewer's resolution and viewport-distance setting. At the default level a
-- 128x128 map shows perhaps a third of the playfield, which is not enough for a blast that reaches
-- 102 cells; the viewer used to be told to zoom out by hand.
--
-- The power stays available afterwards (rules.yaml sets ChargeInterval 400 = 24 s), so the viewer
-- can fire it again anywhere they like.

local OrderTick = 25
local GroundZero = { X = 64, Y = 64 }

local tick = 0
local USA
local fired = false

local function step()
	tick = tick + 1

	if not fired and tick >= OrderTick then
		fired = true
		-- Test.ActivateSupportPower is staging, not an assertion: it is the only way to issue a
		-- support-power order from script, and its return value is deliberately not checked here.
		-- If the strike does not arrive, the thing to look at is the power bin -- a missing cameo
		-- means the GrantConditionOnLobbyOption@highyieldnuke -> RequiresCondition chain did not
		-- open. Nothing in rules.yaml forces that gate: the power's lobby checkbox ships defaulting
		-- ON, so this fires it in its shipped configuration. A missing cameo therefore means the
		-- shipped default moved, not that a scenario override failed.
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
	Camera.Zoom = Camera.MinZoom

	-- Pre-selected so the support-power bin is on screen without the viewer clicking first.
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
