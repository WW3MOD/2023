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
--     t=243   THE FIREBALL LIGHTS. First maximum of the double flash, 21 cells of white.
--     t=244   flash (3 stacked palette warheads, white through ~t=313), mushroom cloud at 700%.
--     t=246   the DIP -- the shock front has gone opaque, so the light dims to 0.9 and turns orange
--             while its radius keeps growing. Blast wave is born here too, at the fireball surface
--             (6.8 cells) travelling at Mach 4.05, not at a point travelling at Mach 1.
--     t=284   *** SECOND MAXIMUM. Intensity 7.0 across 124 cells: the whole map is white. ***
--     t=404   fireball out on a dull red, 161 ticks after it lit. Fire ignition also finishes
--             staging outward here, to 124 cells -- past the blast wave's own 102.
--     t=657   thermal radiation pulse ends (413 ticks of it, 51 damage pulses).
--     t=840   blast wave reaches its full 102-cell radius. Everything on the map has now been hit.
--     t=933   last suppression band lands.
--     t=1743  innermost fires burn out (1500 ticks).
--     t=2043  the strike camera is removed (CameraRemoveDelay 1800).
--
-- (Every figure from t=243 onward was re-derived on 2026-09-06, when this warhead was relabelled
-- from ~800 kt to ~6 Mt and every radius and delay in it recomputed from that yield. The blast wave
-- is faster than it was -- 597 ticks to full radius against 714 -- because it now leaves the
-- fireball supersonic instead of crawling from a point at the speed of sound.)
--
-- SO: the interesting window is t=243 to t=840 -- 14.6 s to 50.4 s after the map loads. Screenshot
-- anywhere in it. The single best frame is t=284, the fireball's second maximum. After that the
-- most legible is around t=380 to t=450, when the wavefront is 35-48 cells out: the inner rings are
-- already gone, the middle band is burning, and the outer rings are still intact and untouched, so
-- all three states are on screen at once.
--
-- THE CAMERA CANNOT BE ZOOMED FROM LUA. CameraGlobal exposes Position and nothing else
-- (CameraGlobal.cs), so this centres on ground zero and leaves zoom to whoever is watching. On a
-- 128x128 map the default zoom shows perhaps a third of the playfield; zoom out to see the whole
-- blast. That is a limitation of the scripting API, not of the staging.
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

	-- Pre-selected so the support-power bin is on screen without the viewer clicking first.
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
