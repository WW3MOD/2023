-- AUTO TEST: look at the two shockwave changes on this branch.
--
-- This is mostly a LOOKING test: ring size and ring density are exactly the kind of thing a
-- state query cannot answer, so the real output is the screenshots. The one thing it does
-- assert is that a rocket landed at all, because a salvo that never reaches the ground
-- produces a folder of empty captures that reads as success.
--
-- BEAT 1 — the 60% cut, side by side. RingNew (e1) carries the shipped VolatileLoad8;
-- RingOld (e3) carries VolatileLoad8Legacy, which is the same weapon with the new
-- ShockwaveVisualRadius zeroed back out. Both die on the same tick, so one frame contains
-- the shipped ring and its own control.
--
-- DO NOT READ THESE CAPTURES AS "one circle smaller than the other" — they will not show
-- that, and the first reading of them on 2026-08-30 nearly recorded the change as inert.
-- Both rings expand at the same 204 units per tick, so AT ANY SHARED TICK THEIR RADII ARE
-- EQUAL. What the cut changes is `progress`, now measured against the smaller visual radius,
-- so the shipped ring is further through its fade at the same distance and ends sooner.
-- Radial-averaged brightness above bare terrain, measured off this scenario's own captures:
--
--     age   shipped                    control
--     t7    peak 853 wdist,  +34.0     peak 917 wdist,  +43.3
--     t10   peak 1472 wdist, +36.4     peak 1450 wdist, +70.1
--     t13   peak 1941 wdist, +10.7     peak 2005 wdist, +52.9
--
-- Same radius throughout, five times dimmer by t13, gone a tick later while the control runs
-- on to 4096. Judge the FADE, not the diameter.
--
-- Timing. Explodes runs at the frame end of the kill, ShockwaveEffect then burns StartDelay
-- (2) before its first expansion, and the ring grows 1024/WaveSpeed = 204 per tick. So the
-- ring is visible for ticks ~3..12 after the kill (2458 / 204 = 12) and there is no single
-- safe instant to photograph — four captures are spread across that window instead of
-- gambling on one. Captures are also a render frame late, which is why none sits on the
-- boundary.
--
-- BEAT 2 — a TOS salvo. 24 rockets at BurstDelays 10 with flight times that vary by more
-- than the launch spacing, so the question the captures answer is whether the rings read as
-- a travelling flicker or as a wall. (Answer on 2026-08-30: a flicker. Two random mid-salvo
-- captures caught zero simultaneous rings, because each lives 10 ticks.)
--
-- READ THESE CENTRED ON THE ABRAMS, WHICH IS SCREEN CENTRE — the captures are anchored to the
-- rocket that damaged IT. Inaccuracy is 3c512, so craters scatter over ~3.5 cells and the
-- brightest fireball in frame is usually a different rocket with no live ring. Centring a
-- brightness sweep on that fireball on 2026-08-30 returned "no ring at any radius" for a ring
-- that was rendering perfectly. Magnify and look; do not trust a radial average here.

local KillAt = 25
local RingLifeCaptures = { 4, 7, 10, 13 }

WorldLoaded = function()
	TestHarness.FocusBetween(RingNew, RingOld)

	TestHarness.Screenshot("01-before-detonation",
		"expects: two infantrymen ~20 cells apart on open ground, no rings yet")

	Trigger.AfterDelay(KillAt, function()
		RingNew.Kill()
		RingOld.Kill()
	end)

	for i, offset in ipairs(RingLifeCaptures) do
		Trigger.AfterDelay(KillAt + offset, function()
			TestHarness.Screenshot(string.format("%02d-rings-t%02d", i + 1, offset),
				"expects: both rings at the SAME radius, the LEFT one (shipped, 60%) markedly " ..
				"fainter than the RIGHT (legacy, 100%) and gone first. Equal diameters are correct")
		end)
	end

	-- Well clear of the ring window, and of the smoke it leaves behind.
	Trigger.AfterDelay(90, function()
		Camera.Position = Target.CenterPosition
		Tos.Attack(Target, true, true)
	end)

	-- ANCHORED TO THE FIRST IMPACT, NOT TO A GUESSED TICK. The first run of this scenario
	-- captured 200-350 and caught nothing but rockets still in the air, because turret turn +
	-- AimingDelay 30 + a long arc over 24 cells at Speed 250 puts the first impact far later
	-- than the launch. Guessing a second time would be the same mistake with different numbers:
	-- Inaccuracy 3c512 spreads the flight times, so the window moves run to run. OnDamaged on
	-- the aim point fires the instant a rocket actually lands, and every capture is measured
	-- from there.
	local salvoSeen = false
	Trigger.OnDamaged(Target, function()
		if salvoSeen then
			return
		end

		salvoSeen = true

		-- A ring lives 10 ticks. The first three captures sit inside one ring's life, the rest
		-- walk the remaining ~230 ticks of the salvo, which is where overlaps have to be judged.
		for i, offset in ipairs({ 2, 5, 9, 40, 90, 150, 210 }) do
			Trigger.AfterDelay(offset, function()
				TestHarness.Screenshot(string.format("%02d-tos-impact-plus%03d", i + 5, offset),
					"expects: a small thin pale ring at each fresh rocket crater. Judge whether it is " ..
					"visible at all, whether it is too faint, and how many overlap at once")
			end)
		end
	end)

	-- The salvo can only end once the last of 24 rockets has flown, so this is deliberately far
	-- past the last anchored capture rather than tuned close to it.
	Trigger.AfterDelay(900, function()
		if salvoSeen then
			Test.Pass("captured the volatile-cargo ring pair and a TOS salvo")
		else
			Test.Fail("no TOS rocket ever landed on the aim point — the salvo captures are empty again")
		end
	end)
end
