-- DEMO -- a smoothed missile must stop on its target, not fly through it. Six passes, two Kinzhals
-- each, one smoothed and one not. Nothing here is asserted: no AssertWithin, no Test.Pass, no
-- Test.Fail, no result.json. The viewer watches and closes the window when done.
--
-- WHAT TO LOOK AT, and it is the last quarter-second of each pass only. Both missiles arrive within
-- a tick of each other about five cells apart. Each one reaches its aim point, sits there for one
-- tick, and explodes.
--
--   THE RAW MISSILE cannot overshoot -- it is only ever drawn at simulated positions -- so wherever
--   it is when its blast appears is where a missile is supposed to be when it goes off.
--   THE SMOOTHED MISSILE must come to a DEAD STOP in the same way. If it instead glides about two
--   cells further along its track after arriving, and the explosion then appears BEHIND it, that is
--   the reported bug and this build does not have the fix.
--
-- The whole artefact lasted one inter-tick interval, ~60 ms, which is why there are six passes and
-- why the camera is zoomed in: at 60 fps that is four frames, and you want more than one look.
--
-- SECOND THING TO LOOK AT, and it is what the LAST TWO FRAMES before each blast look like. The
-- shipped Kinzhal draws 480 wdist of pale blue-white plasma bloom AHEAD of its nose
-- (WithHypersonicPlasma, five samples 96 apart). Those copies hang off a body the smoothing trait
-- has already moved, and both offsets are measured from the same simulated position, so they ADD --
-- which is why clamping the sheath against the raw remaining distance did nothing on the tick that
-- mattered. The clamp now reserves a whole tick of travel first, and on a missile this fast (2400
-- wdist/tick against 480 of sheath) that is all-or-nothing.
--
-- SO, ON THE FRAME BEFORE THE BLAST: the missile is a BARE AIRFRAME with its orange wake and NO
-- blue-white bloom at the nose. That is true for roughly the last two ticks, ~120 ms, the last six
-- or seven frames at 60 fps. It is the fixed behaviour, not a missing effect -- what must never
-- appear on those frames is any part of the bloom in FRONT of the aim point. The orange wake is
-- deliberately not clamped and should look exactly as it did.
--
-- DO NOT PAUSE TO COMPARE. Smoothing is deliberately inert while the world is paused, so paused both
-- missiles sit on identical true positions. That is correct behaviour, not the fix working.
--
-- LAYOUT, so this file can be read without map.yaml: aim points 44,51 (left) and 47,47 (right), both
-- bare cells, five cells apart perpendicular to the inbound heading and each 88-89 cells from
-- Russia's HomeLocation of 110,110. The camera sits at their midpoint and never moves.
--
-- THE SCHEDULE, in ticks from WorldLoaded. Timestep is 60 ms, so 16.67 ticks per second -- NOT the
-- 25 that TestHarness.TicksPerSecond carries (that value is a deliberately-preserved harness
-- convention for AssertWithin budgets, documented in test-helpers.lua, and is not the tick rate).
-- Every delay below is therefore written in RAW TICKS through Trigger.AfterDelay.
--
-- MissileDelay is overridden to 15 in rules.yaml and the flight is ~88 cells at 2000 WDist/tick
-- rising to 2400 past the halfway point, so each pass runs:
--
--     +0     both orders issued. Nothing is on screen yet.
--     +15    both missiles enter, far off screen -- the camera is on the impact zone, not the entry.
--     +~60   they come into frame already in the terminal dive.
--     +~74   impact. THIS is the moment the demo exists for.
--
-- Passes are 100 ticks apart, clear of the 60-tick ChargeInterval the demo overrides in, so both
-- powers are always ready when the next pair is ordered.
--
--     PASS 1  order  30      PASS 4  order 330
--     PASS 2  order 130      PASS 5  order 430
--     PASS 3  order 230      PASS 6  order 530
--
-- After pass 6 nothing further is scheduled, but both powers keep recharging on their 60-tick
-- interval, so the viewer can fire either of them anywhere from the support-power bin. The smoothed
-- one is the first icon, the reference the second.

local AimLeft = { X = 44, Y = 51 }
local AimRight = { X = 47, Y = 47 }

-- Midpoint of the two aim points, in cells. The camera never leaves it.
local CameraCell = { X = 45, Y = 49 }

local SmoothOrder = "KinzhalStrike"
local RawOrder = "KinzhalRawStrike"

-- The tracks swap sides every pass, so a difference that stays on one side of the screen is the
-- screen and not the missile.
local Passes = {
	{ tick =  30, smooth = AimLeft, raw = AimRight },
	{ tick = 130, smooth = AimRight, raw = AimLeft },
	{ tick = 230, smooth = AimLeft, raw = AimRight },
	{ tick = 330, smooth = AimRight, raw = AimLeft },
	{ tick = 430, smooth = AimLeft, raw = AimRight },
	{ tick = 530, smooth = AimRight, raw = AimLeft }
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
		-- not arrive, the thing to look at is the support-power bin.
		--
		-- BOTH ARMS ARE TIMER POWERS HERE AND NEITHER IS BOUGHT. rules.yaml explains why that is
		-- necessary for a controlled comparison and why it is not a pattern to copy.
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

	-- Zoomed in as far as the display allows. The subject is a ~56 px artefact lasting four frames,
	-- and unlike demo-subtick-smoothing there is no long approach to keep in shot -- the missiles are
	-- only interesting once they are already on top of the aim points. Camera.Zoom is a multiple of
	-- the default level and is clamped to Camera.MinZoom..Camera.MaxZoom.
	Camera.Zoom = math.min(3, Camera.MaxZoom)

	-- Pre-selected so the support-power bin is on screen without the viewer clicking first: the
	-- smoothed strike is the first icon, the unsmoothed reference the second.
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
