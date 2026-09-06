-- DEMO -- the ballistic missile pitch model. Two Iskander shots on different bearings, so the nose
-- can be compared against the smoke trail behind it. Nothing here is asserted: no AssertWithin, no
-- Test.Pass, no Test.Fail, no result.json. The viewer watches and closes the window when done.
--
-- WHAT TO LOOK AT. The smoke trail is ground truth -- LeavesTrailsCA drops its puffs at the
-- missile's own CenterPosition, so the trail marks exactly where the missile has been. Put the nose
-- and the trail on the same line in your eye. If the nose sits further from horizontal than the
-- trail does, the missile is flying belly first, which is the defect this scenario shows the fix
-- for. At the apex, where the arc is momentarily flat, the nose must sit exactly along the ground
-- heading -- that one is the easiest frame to judge and the model guarantees it exactly.
--
-- LAYOUT, so this file can be read without map.yaml: launcher A is at cell 79,80 and fires
-- north-west at an Abrams on 55,56; launcher B is at 81,66 and fires due west at an Abrams on
-- 47,66. Both shots are 34 cells. Russia's Supply Route is at 110,110, USA's at 18,18; neither is
-- involved. The camera sits at 66,67 for the whole demo and never moves, because both arcs pass
-- over the middle of the map.
--
-- THE SCHEDULE, in ticks from WorldLoaded. Timestep is 60 ms, so 16.67 ticks per second -- NOT the
-- 25 that TestHarness.TicksPerSecond carries (that value is a deliberately-preserved harness
-- convention for AssertWithin budgets, documented in test-helpers.lua, and is not the tick rate).
-- Every delay below is therefore written in RAW TICKS through Trigger.AfterDelay.
--
-- Each shot runs the same profile, because both are 34 cells with identical launcher stats. Offsets
-- are from the moment the Attack order is issued, and the flight numbers come from
-- BallisticMissileFly.EstimateArcTicks rather than from a stopwatch:
--
--     +0     order issued. The missile actor spawns immediately and begins erecting.
--     +0..80 ERECTION. PreLaunchTicks is LaunchRiseTicks 60 + PostErectionWaitTicks 20. The missile
--            tilts from flat to its launch attitude and then holds it. No trail yet -- LeavesTrailsCA
--            is gated on the `ignited` condition, so nothing smokes until the motor lights.
--     +80    IGNITION. Motor lights, trail starts, the arc begins.
--     +139   progress 0.15 -- CLIMB. Best frame for the climbing half.
--     +156   progress 0.25 -- still climbing, arc slope about 0.4.
--     +188   APEX, progress 0.50. Arc slope is zero: the nose must be exactly on the ground heading.
--     +215   progress 0.85 -- DESCENT. Best frame for the diving half.
--     +223   impact.
--
-- The climb takes 108 of the 143 flight ticks and the dive only 35, because the missile starts from
-- rest (InitialSpeedPercent 0, Acceleration 3) and then gets TerminalAcceleration 10 past the apex.
-- So the descending half goes past roughly three times faster than the climbing half -- if you are
-- pausing to look, the descent is the one that needs catching.
--
-- ABSOLUTE TICKS, which is what a screenshot schedule actually needs:
--
--     SHOT A (north-west, facing 128 -- the diagonal, where the old model over-tilted most)
--       t=30    order        t=110  ignition
--       t=169   CLIMB        t=218  apex        t=245  DESCENT      t=253  impact
--
--     SHOT B (due west, facing 256 -- where the old model under-tilted instead)
--       t=320   order        t=400  ignition
--       t=459   CLIMB        t=508  apex        t=535  DESCENT      t=543  impact
--
-- Shot B is ordered 67 ticks after shot A lands, so the two never share the screen.
--
-- THE DEMO ZOOMS ITSELF, before the first shot rather than during it. Each arc spans 34 cells
-- horizontally and about 7 vertically, which at the default level sits close to the edge of the
-- viewport on a 128x128 map, so this halves the zoom. Camera.Zoom is a multiple of the default
-- level; it is clamped to Camera.MinZoom..Camera.MaxZoom, so an unreachable value is applied as
-- far as it goes rather than raising.

local ShotATick = 30
local ShotBTick = 320
local CameraCell = { X = 66, Y = 67 }

local tick = 0
local firedA = false
local firedB = false

local function step()
	tick = tick + 1

	-- Actor.Attack(target, allowMove, forceAttack). allowMove is false on purpose: both targets are
	-- inside IskanderTargeter's Range 50c0 and outside its MinRange 16c0, so a launcher that decides
	-- it needs to drive somewhere is telling you the geometry in map.yaml has moved, and driving off
	-- would be a far more confusing symptom than simply not firing. forceAttack is false because the
	-- armament no longer carries RequiresForceFire -- a plain attack order is the shipped path.
	if not firedA and tick >= ShotATick then
		firedA = true
		if LauncherA and not LauncherA.IsDead and LauncherA.IsInWorld
			and TargetA and not TargetA.IsDead and TargetA.IsInWorld then
			LauncherA.Attack(TargetA, true, false)
		end
	end

	if not firedB and tick >= ShotBTick then
		firedB = true
		if LauncherB and not LauncherB.IsDead and LauncherB.IsInWorld
			and TargetB and not TargetB.IsDead and TargetB.IsInWorld then
			LauncherB.Attack(TargetB, true, false)
		end

		return
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	-- Cell centre in world coordinates is cell * 1024 + 512.
	Camera.Position = WPos.New(CameraCell.X * 1024 + 512, CameraCell.Y * 1024 + 512, 0)
	Camera.Zoom = 0.5

	-- Pre-selected so the launcher's range circle is on screen without the viewer clicking first.
	TestHarness.Select(LauncherA)

	Trigger.AfterDelay(1, step)
end
