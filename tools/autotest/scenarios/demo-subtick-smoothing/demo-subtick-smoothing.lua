-- DEMO -- sub-tick motion smoothing on fast missiles. Four passes, two Kinzhals each, one smoothed
-- and one not. Nothing here is asserted: no AssertWithin, no Test.Pass, no Test.Fail, no
-- result.json. The viewer watches and closes the window when done.
--
-- WHAT TO LOOK AT. Both missiles are the same actor with one trait different. The RAW one moves in
-- discrete jumps of just under two cells, once every 60 ms; the SMOOTHED one covers the same ground
-- continuously, because it is drawn ahead of its last simulated position along its last tick's
-- velocity by however far through the current tick the frame is. Same speed, same arrival, same
-- everything else -- if you cannot tell them apart in motion, the feature is not working.
--
-- THE TRACKS SWAP SIDES. On passes 1 and 3 the smoothed missile takes track A (upper right); on
-- passes 2 and 4 it takes track B (lower left). Anything that looks different on one side of the
-- screen regardless of which missile is on it is the screen, not the missile.
--
-- DO NOT PAUSE TO COMPARE. The smoothing is deliberately inert while the world is paused -- a paused
-- world still renders and the sub-tick clock keeps sweeping, so extrapolating through a pause would
-- make the missile slide forward and snap back forever. Paused, both missiles sit on exactly the
-- same true positions and look identical. That is correct behaviour, not the feature failing.
--
-- LAYOUT, so this file can be read without map.yaml: both missiles enter the map at cell 110,127 on
-- the south edge, derived from Russia's HomeLocation of 110,110. Track A aims at 44,51 and track B
-- at 34,61, both 100 cells out to the north-west and 14 cells apart at the aim point. The camera
-- sits at 75,92 -- halfway along both tracks, where they are about 7 cells apart -- and never moves.
--
-- THE SCHEDULE, in ticks from WorldLoaded. Timestep is 60 ms, so 16.67 ticks per second -- NOT the
-- 25 that TestHarness.TicksPerSecond carries (that value is a deliberately-preserved harness
-- convention for AssertWithin budgets, documented in test-helpers.lua, and is not the tick rate).
-- Every delay below is therefore written in RAW TICKS through Trigger.AfterDelay.
--
-- Each pass runs the same profile. MissileDelay is overridden to 15 in rules.yaml and the flight is
-- 100 cells at 2000 WDist/tick, so:
--
--     +0     both orders issued. Nothing is on screen yet.
--     +15    both missiles enter at the map edge, 8 cells up, already at cruise speed.
--     +40    they reach the camera. This is the stretch worth watching.
--     +66    impact. TerminalSpeed 2400 past the halfway point pulls this in a few ticks.
--
-- ABSOLUTE TICKS. Passes are 110 apart, comfortably clear of the 60-tick ChargeInterval the demo
-- overrides in, so both powers are always ready when the next pair is ordered.
--
--     PASS 1  order  30   enter  45   camera  70   impact  96    smoothed on track A
--     PASS 2  order 140   enter 155   camera 180   impact 206    smoothed on track B
--     PASS 3  order 250   enter 265   camera 290   impact 316    smoothed on track A
--     PASS 4  order 360   enter 375   camera 400   impact 426    smoothed on track B
--
-- After pass 4 nothing further is scheduled, but both powers keep recharging on their 60-tick
-- interval, so the viewer can fire either of them anywhere from the support-power bin. The smoothed
-- one is the first icon, the control the second.

local TrackA = { X = 44, Y = 51 }
local TrackB = { X = 34, Y = 61 }
local CameraCell = { X = 75, Y = 92 }

local SmoothOrder = "KinzhalStrike"
local RawOrder = "KinzhalRawStrike"

-- Pass N fires the smoothed missile at the first entry and the control at the second.
local Passes = {
	{ tick =  30, smooth = TrackA, raw = TrackB },
	{ tick = 140, smooth = TrackB, raw = TrackA },
	{ tick = 250, smooth = TrackA, raw = TrackB },
	{ tick = 360, smooth = TrackB, raw = TrackA }
}

local tick = 0
local next_pass = 1
local Russia

local function step()
	tick = tick + 1

	local pass = Passes[next_pass]
	if pass and tick >= pass.tick then
		next_pass = next_pass + 1

		-- Test.ActivateSupportPower is staging, not an assertion, and its return value is
		-- deliberately not checked: this is a demo and there is no verdict to fail. If a pass does
		-- not arrive, the thing to look at is the support-power bin. Both powers carry
		-- `Prerequisites: player.russia` and neither is behind a lobby checkbox, so an empty bin
		-- means the player is not Russia rather than that an override here failed.
		Test.ActivateSupportPower(Russia, SmoothOrder, CPos.New(pass.smooth.X, pass.smooth.Y))
		Test.ActivateSupportPower(Russia, RawOrder, CPos.New(pass.raw.X, pass.raw.Y))

		if next_pass > #Passes then
			return
		end
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	Russia = Player.GetPlayer("Russia")

	-- Cell centre in world coordinates is cell * 1024 + 512.
	Camera.Position = WPos.New(CameraCell.X * 1024 + 512, CameraCell.Y * 1024 + 512, 0)

	-- Zoomed IN, unlike most demos. The subject is a per-tick jump of about 47 px at the default
	-- level on a sprite a couple of cells across, and it reads better larger. 1.5 is a compromise:
	-- further in makes the step more legible but shortens the stretch the fixed camera sees to well
	-- under a second. Camera.Zoom is a multiple of the default level and is clamped to
	-- Camera.MinZoom..Camera.MaxZoom, so the min() only matters on a display whose ceiling is lower.
	Camera.Zoom = math.min(1.5, Camera.MaxZoom)

	-- Pre-selected so the support-power bin is on screen without the viewer clicking first: the
	-- smoothed strike is the first icon, the unsmoothed control the second.
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
