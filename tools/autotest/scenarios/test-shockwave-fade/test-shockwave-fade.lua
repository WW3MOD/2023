-- AUTO TEST: does a shockwave ring still read solid in mid-flight, and is it gone at full radius?
--
-- Both halves of that question are visual, so the captures ARE the result. The verdict only
-- asserts that all four detonators actually blew up, because a run where nothing exploded produces
-- a folder of pictures of empty grass that reads exactly like success.
--
-- ONE FRAME, TWO CURVES. Each pair is the same ring twice: left is the shipped fade, right is the
-- curve it replaced (weapons.yaml). This is not decoration — a capture is armed one render frame
-- before its pixels are sampled, so any single shot can land a tick either side of where it was
-- aimed, and an absolute reading of one ring at "the last frame" is therefore not trustworthy on
-- its own. Both rings in a frame drift together, so the COMPARISON survives what the timing does
-- not. Read left against right; do not read either against a remembered brightness.
--
-- DO NOT MEASURE THESE WITH A RADIAL BRIGHTNESS SWEEP. The previous round of shockwave work did,
-- twice, and got confident wrong answers both times — once by centring the sweep on a neighbouring
-- crater and concluding a perfectly good ring was not rendering, once by scoring the fainter of two
-- configurations as the brighter. Zoom is pinned below precisely so the rings are large enough to
-- judge by eye. Magnify and look.
--
-- TIMING, DERIVED RATHER THAN GUESSED. Explodes fires at the frame end of the kill tick, so the
-- effect exists from kill+1. ShockwaveEffect then burns StartDelay ticks returning early, and only
-- afterwards begins expanding by 1024/WaveSpeed per tick. It is drawn while radius <= the radius
-- the ring is drawn out to, which is ShockwaveVisualRadius when set and MaxRadius otherwise:
--
--   TOS      StartDelay 1, WaveSpeed 5 (204/tick), drawn out to MaxRadius 2048
--            -> 10 drawn frames, at kill+2 .. kill+11
--   Truck b8 StartDelay 2, WaveSpeed 5 (204/tick), drawn out to ShockwaveVisualRadius 2458
--            -> 12 drawn frames, at kill+3 .. kill+14
--
-- The offsets below walk each ring from its first frames to its LAST one. That last frame is the
-- whole point of the change and the previous scenario never reached it: test-shockwave-ring-sizes
-- stopped at kill+13, which is the truck ring's eleventh frame of twelve, and so photographed the
-- ring only at radii where it was always going to still be visible.

local TosKillAt = 30
local TruckKillAt = 120

-- kill+2 is the first drawn frame and kill+11 the last; 6 is mid-flight and 9 is where the old
-- curve was still carrying a third of its alpha.
local TosCaptures = { 3, 6, 9, 11 }

-- kill+3 is the first drawn frame and kill+14 the last.
local TruckCaptures = { 3, 7, 11, 14 }

WorldLoaded = function()
	-- A ring is two to two and a half cells across. At the default zoom that is under a hundred
	-- pixels of diameter, which is enough to answer "did anything render" and not enough to answer
	-- "is this fading" — which is the question. SetZoom clamps to the viewport's own limit, so the
	-- achieved factor is logged rather than assumed.
	local zoom = Test.SetZoom(2)
	print(string.format("camera zoom set to %.2fx the default", zoom))

	-- Midway between the pair at 20,10 and 30,10.
	Camera.Position = WPos.New(25 * 1024 + 512, 10 * 1024 + 512, 0)

	TestHarness.Screenshot("01-before-any-detonation",
		"expects: two infantrymen ten cells apart on open ground, no rings, no fireballs")

	Trigger.AfterDelay(TosKillAt, function()
		TosNew.Kill()
		TosOld.Kill()
	end)

	for i, offset in ipairs(TosCaptures) do
		Trigger.AfterDelay(TosKillAt + offset, function()
			TestHarness.Screenshot(string.format("%02d-tos-plus%02d", i + 1, offset),
				"expects: LEFT ring is the shipped fade, RIGHT ring is the old one. Both at the " ..
				"same radius. Early and mid frames: left at least as visible as right. Final " ..
				"frame (plus11): left gone, right still a solid pale band at full radius")
		end)
	end

	Trigger.AfterDelay(TruckKillAt - 10, function()
		Camera.Position = WPos.New(25 * 1024 + 512, 22 * 1024 + 512, 0)
	end)

	Trigger.AfterDelay(TruckKillAt, function()
		TruckNew.Kill()
		TruckOld.Kill()
	end)

	for i, offset in ipairs(TruckCaptures) do
		Trigger.AfterDelay(TruckKillAt + offset, function()
			TestHarness.Screenshot(string.format("%02d-truck-plus%02d", i + 6, offset),
				"expects: LEFT ring is the shipped fade, RIGHT ring is the old one. Both at the " ..
				"same radius. Left holds its brightness longer through the middle; BOTH should be " ..
				"gone by the final frame (plus14), since this ring already faded to zero")
		end)
	end

	-- Well past the last capture, and past the smoke.
	Trigger.AfterDelay(TruckKillAt + 60, function()
		local alive = {}
		for name, actor in pairs({ TosNew = TosNew, TosOld = TosOld, TruckNew = TruckNew, TruckOld = TruckOld }) do
			if not actor.IsDead then
				alive[#alive + 1] = name
			end
		end

		if #alive == 0 then
			Test.Pass("all four detonators fired; captures hold each ring beside its pre-change control")
		else
			Test.Fail("these detonators never exploded, so their captures are empty ground: " ..
				table.concat(alive, ", "))
		end
	end)
end
