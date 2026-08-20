-- CAPTURE SCENARIO: does the concealment ring tell the truth?
--
-- This is the shot that separates the fixed tier ladder from the old one. Everything else
-- in the gauge suite shows that a ring is drawn and that it changes size; only this one
-- shows whether the size is RIGHT.
--
-- =====================================================================================
-- THE PREDICTION, AND WHY THE TWO NUMBERS SHOULD COINCIDE
-- =====================================================================================
-- Rifle is map-placed and never ordered anywhere, so he is not moving (no -1) and never
-- gets `dugin` either — the still-timer is armed only by a stop transition, and he has
-- never had one (GrantConditionOnMovement.cs:53-70). He sits on tier 3 for the whole run.
--
--   drawn ring   tier 3 -> the ^StandardVision S3 band's outer range -> 25c0
--   real reveal  needs observer strength >= 3 (MapLayers.IsDetected), i.e. the S3 band,
--                which reaches 25c0
--
-- Same 25 cells. So the red "!" should switch on as the observer crosses the drawn circle.
-- BOTH numbers moved out one band when the reveal comparison went non-strict, and they
-- moved TOGETHER — which is the point of this scenario. A disagreement of about three
-- cells means one of the two halves was changed without the other.
--
-- =====================================================================================
-- HOW TO READ THE FIVE FRAMES — this is the whole verdict
-- =====================================================================================
-- The observer is teleported to five known separations along the centre row. In each
-- frame BOTH things are visible at once: the grey circle round Rifle, and whether Rifle
-- carries a red "!". The question is never "how many cells is the ring" — it is whether
-- the two agree:
--
--   CORRECT   the last frame WITHOUT a "!" has the observer OUTSIDE the circle, and the
--             first frame WITH a "!" has him just INSIDE it. The mark switches on across
--             the drawn boundary. (Predicted: off at 27 and 26, on from 24 inward.)
--   BROKEN    the observer is already inside the circle in a frame that has no "!" — the
--             ring is claiming he can be seen from further out than he really can, which
--             is the one-band-too-wide ladder.
--
-- 25 cells is deliberately never sampled. AUTOTEST.md gotcha #8: an assertion sitting
-- exactly on a vision-band edge flips between runs of an identical scenario on posture
-- alone.
--
-- Captures are armed one per Trigger.AfterDelay with a full second of quiet after each,
-- because Test.Screenshot only ARMS a grab that samples at the end of the next RenderTick
-- — a teleport on the following line would be photographed under the previous label.

local Rifle_ExpectedTier = 3
local RingCells = 25

-- Cells east of Rifle. 25 omitted on purpose (band edge). 26 and 24 straddle the boundary
-- by one cell each, which is where the answer lives.
local Ladder = {
	{ cells = 27, mark = "OFF", why = "S2 strength 2 is below tier 3; observer OUTSIDE the ring" },
	{ cells = 26, mark = "OFF", why = "still S2; observer just OUTSIDE the ring" },
	{ cells = 24, mark = "ON",  why = "S3 strength 3 MATCHES tier 3, and a match now reveals; just INSIDE the ring" },
	{ cells = 21, mark = "ON",  why = "S4, comfortably inside" },
	{ cells = 16, mark = "ON",  why = "S5/S6, deep inside" },
}

local RifleX = 32
local RifleY = 16

WorldLoaded = function()
	print("[truth] zoom = " .. tostring(Test.SetZoom(1)) .. "x MinZoom")

	-- Terrain shadow is subtracted from vision strength per cell (Map.SetShadowLayer, fed
	-- by DensityLayer), and it would move the reveal distance without moving the drawn
	-- ring — i.e. it would manufacture exactly the disagreement this scenario reports as a
	-- bug. Assert the sightline is bare rather than trusting the copied map.bin.
	for x = RifleX, RifleX + 28 do
		local d = Test.GetDensity(CPos.New(x, RifleY))
		if d ~= 0 then
			Test.Fail("terrain density is " .. tostring(d) .. " at cell " .. tostring(x) ..
				"," .. tostring(RifleY) .. " — shadow reduces vision strength along the " ..
				"sightline, so the reveal distance would not be the one predicted here")
			return
		end
	end

	TestHarness.Select(Rifle)
	Camera.Position = Rifle.CenterPosition

	local step
	step = function(index)
		local entry = Ladder[index]
		if entry == nil then
			Test.Pass("walked the observer 27 -> 16 cells across a predicted " ..
				tostring(RingCells) .. "-cell ring; 5 captures")
			return
		end

		Watcher.Teleport(CPos.New(RifleX + entry.cells, RifleY))

		-- Settle: the teleport has to reach the ActorMap and the shroud, and
		-- WithSpottedDecoration caches its answer for RecalculationInterval (7 ticks).
		Trigger.AfterDelay(DateTime.Seconds(2), function()
			if Rifle.IsDead then
				Test.Fail("Rifle died during the ladder — the observer was supposed to be " ..
					"silenced by HoldFire + NoAutoTarget in rules.yaml")
				return
			end

			-- The premise of every number above. If Rifle is not on tier 3 the ring is not
			-- 22 cells and the frames cannot be read against these predictions at all.
			local actual = Test.GetVisibilityLevel(Rifle)
			if actual ~= Rifle_ExpectedTier then
				Test.Fail("Rifle is on tier " .. tostring(actual) .. ", not " ..
					tostring(Rifle_ExpectedTier) .. " — his ring is not the " ..
					tostring(RingCells) .. " cells these captures are predicated on. " ..
					"Something moved him, made him fire, or put him in cover")
				return
			end

			-- Live counters must not go in a failure string: AssertWithin-style messages are
			-- built eagerly at registration and would report their initial values forever.
			print("[truth] observer at " .. tostring(entry.cells) .. " cells, Rifle tier " ..
				tostring(actual) .. ", expecting mark " .. entry.mark)

			local label = string.format("%02d-observer-%02dc-mark-%s",
				index, entry.cells, string.lower(entry.mark))

			TestHarness.Screenshot(label,
				"expects: one blue USA rifleman at frame centre inside a grey circle of " ..
				"radius " .. tostring(RingCells) .. " cells; one red Russian rifleman due " ..
				"EAST of him at exactly " .. tostring(entry.cells) .. " cells (" ..
				entry.why .. "). The red '!' above the USA rifleman should be " ..
				entry.mark .. ". " ..
				"THE VERDICT IS NOT THIS FRAME ALONE — read the five in order and find " ..
				"where the '!' switches on. CORRECT = it switches on between the frame " ..
				"where the Russian is outside the circle and the frame where he is inside " ..
				"it. BROKEN = he is already well inside the circle in a frame with no '!', " ..
				"which means the ring claims a longer sighting range than the game gives. " ..
				"Only the USA rifleman carries a mark: the observer's own was removed in " ..
				"rules.yaml, because the harness has no render player and would otherwise " ..
				"draw marks on enemies a real player never sees. The circle is clipped top " ..
				"and bottom by the map edge; judge it by its width along the row the two " ..
				"soldiers stand on.")

			Trigger.AfterDelay(DateTime.Seconds(2), function()
				step(index + 1)
			end)
		end)
	end

	Trigger.AfterDelay(DateTime.Seconds(3), function()
		step(1)
	end)
end
