-- CAPTURE SCENARIO: the concealment gauge — the tier ladder, and squad grouping.
--
-- Produces five PNGs. It asserts STATE only (which tier each shot photographed, how many
-- units were selected); it never asserts on appearance. Whether the rings look right is
-- read off the images against the notes, per DOCS/recipes/DEMO.md.
--
-- =====================================================================================
-- WHERE THE EXPECTED RADII COME FROM — derived, not quoted
-- =====================================================================================
-- Detectable.CurrentVisibility is the observer vision STRENGTH needed to reveal a unit;
-- higher = harder to see. Standard infantry start at Vision: 3 (infantry.yaml:95-96) and
-- ^DetectableInfantryStandard adjusts it: moving -1, dug in +1, prone +1, firing -2.
--
-- ^DetectableRangeCircles draws tier N at the OUTER range of the ^StandardVision band at
-- Strength N, because reveal needs strength greater than OR EQUAL TO the tier
-- (MapLayers.IsDetected). Bands (defaults.yaml:47-84): S10 0-4c, S9 4-7c, S8 7-10c, S7
-- 10-13c, S6 13-16c, S5 16-19c, S4 19-22c, S3 22-25c, S2 25-28c, S1 28-32c.
--
--   moving          3 - 1 = tier 2  ->  band S2 outer  ->  28c0
--   stopped         3     = tier 3  ->  band S3 outer  ->  25c0
--   stopped, dug in 3 + 1 = tier 4  ->  band S4 outer  ->  22c0
--
-- So the ladder is 28 -> 25 -> 22 cells, three cells per step. These moved out one band
-- when the reveal comparison went non-strict; the SHAPE the capture judges -- three rings
-- shrinking by three cells a step -- is unchanged, and so are the tiers asserted below. NOTE this differs from the
-- 25 / 19 / 16 in the capture request: 19 and 16 are tiers 4 and 5, and tier 5 needs a
-- SECOND +1 (prone, or one step of cover) on top of dug in, which standing still alone
-- never supplies. The SHAPE of the expectation — three radii shrinking in that order —
-- is unchanged; only the numbers are corrected.
--
-- =====================================================================================
-- THE TRAP THAT MAKES A STATIONARY RIFLEMAN LOOK LIKE A BROKEN GAUGE
-- =====================================================================================
-- `dugin` comes from GrantConditionOnMovement's still-timer, and that timer is ARMED ONLY
-- BY A STOP TRANSITION. cooldown starts at 0; Tick does `if (--cooldown == 0) grant`
-- (GrantConditionOnMovement.cs:53-60), so on a unit that has never moved it counts
-- 0 -> -1 -> -2 and never fires. cooldown is set to TimeToBeStill only in UpdateCondition's
-- stop branch (:66-70). A map-placed rifleman that is never ordered anywhere therefore
-- sits at tier 3 forever.
--
-- If this scenario had simply placed a rifleman and waited, all three shots would show one
-- 25c0 ring — which is precisely the "a ring that never changes size" signature that the
-- capture request calls BROKEN. Walker is given a real move order for exactly this reason.
--
-- TimeToBeStill is 200 ticks (infantry.yaml:139-142) at 25 ticks/s = 8.0 seconds, not the
-- 12.0 in the request.
--
-- =====================================================================================
-- CAPTURE TIMING
-- =====================================================================================
-- Test.Screenshot ARMS a grab that samples at the end of the NEXT RenderTick, so anything
-- that mutates the world on the following line is photographed under the previous label
-- (SCREENSHOT.md; this bit the project on 2026-08-17). Every shot below therefore sits
-- alone inside its own Trigger.AfterDelay, with a full second of quiet after it before
-- the next order, camera move or Test.Pass.

local ExpectedMovingTier = 2
local ExpectedStoppedTier = 3
local ExpectedDuginTier = 4

local Squad = nil

local function tier(actor)
	return Test.GetVisibilityLevel(actor)
end

-- Fail loudly when a shot is about to photograph a tier other than the one its label
-- claims. Without this the three captures could be three photographs of ONE tier and
-- nothing in the verdict would say so — the run would report `pass` and hand over three
-- identical rings, which reads as a broken gauge rather than a broken scenario.
local function requireTier(actor, expected, what)
	local actual = tier(actor)
	if actual ~= expected then
		Test.Fail(what .. ": Detectable.CurrentVisibility is " .. tostring(actual) ..
			", expected " .. tostring(expected) ..
			" — the shot would be labelled with a tier it is not showing")
		return false
	end

	return true
end

WorldLoaded = function()
	Squad = { Squad1, Squad2, Squad3, Squad4, Squad5 }

	-- A 25-cell radius is 50 cells across, so pull all the way out. SetZoom's scale is a
	-- multiple of the viewport's MinZoom, which IS the fully-zoomed-out end, so 1 is as
	-- wide as the viewport goes and anything below it clamps back to the same value.
	-- Logged rather than assumed, because how much map that shows depends on the WINDOW:
	-- run these captures at a large --size if the rings come back cut off left and right.
	print("[gauge] zoom = " .. tostring(Test.SetZoom(1)) .. "x MinZoom")

	-- Terrain shadow subtracts from vision strength per cell (Map.SetShadowLayer, fed by
	-- DensityLayer), which would move every radius in this file. Assert the ground is bare
	-- rather than trusting the copied map.bin.
	local density = Test.GetDensity(Walker.Location)
	if density ~= 0 then
		Test.Fail("terrain density at Walker's cell is " .. tostring(density) ..
			", not 0 — shadow reduces vision strength and every radius below is void")
		return
	end

	if tier(Walker) < 0 then
		Test.Fail("Walker has no Detectable trait — nothing here can draw a concealment ring")
		return
	end

	TestHarness.Select(Walker)
	Camera.Position = Walker.CenterPosition

	-- 6 cells west. Infantry Speed is 25 WDist/tick (infantry.yaml Mobile), i.e. ~1.6s per
	-- cell, so this is a ~10s walk: long enough that he is unambiguously still moving at
	-- t=5s, short enough not to stretch the run. The STOP at the far end is what arms the
	-- still-timer that makes tier 4 reachable at all.
	Test.IssueMove(Walker, CPos.New(44, 16))

	-- ---- Shot 1: moving, tier 2, expected 25c0 ------------------------------------
	Trigger.AfterDelay(DateTime.Seconds(4), function()
		Camera.Position = Walker.CenterPosition
	end)

	Trigger.AfterDelay(DateTime.Seconds(5), function()
		if not requireTier(Walker, ExpectedMovingTier, "shot 01 (moving)") then return end

		TestHarness.Screenshot("01-gauge-moving-28c",
			"expects: ONE selected rifleman, centred, inside a single thin grey circle. " ..
			"He is walking, so this is the WIDEST of the three rings — radius 28 cells, " ..
			"i.e. 56 cells across. CORRECT = a grey ring is drawn at all and it is visibly " ..
			"wider than shots 02 and 03 taken at the same zoom with the same unit centred. " ..
			"MERELY PRESENT = a ring is there but shots 01/02/03 are the same size, which " ..
			"means the prone/dugin/moving modifiers are not reaching the visibility level. " ..
			"Compare the three by eye at the sprite: he is the same size in all three.")
	end)

	-- ---- Wait for arrival, then shots 2 and 3 -------------------------------------
	local waitForHalt
	local waited = 0
	waitForHalt = function()
		waited = waited + 1
		if waited > DateTime.Seconds(40) then
			Test.Fail("Walker never finished his 6-cell move — no stop transition, so the " ..
				"still-timer was never armed and tier 4 is unreachable")
			return
		end

		if not Walker.IsIdle then
			Trigger.AfterDelay(1, waitForHalt)
			return
		end

		-- Halted. `moving` drops on the stop transition and the 200-tick still-timer starts
		-- from the same moment.
		Trigger.AfterDelay(DateTime.Seconds(2), function()
			if not requireTier(Walker, ExpectedStoppedTier, "shot 02 (stopped)") then return end

			TestHarness.Screenshot("02-gauge-stopped-25c",
				"expects: the same rifleman, same camera, same zoom, now STOPPED and not yet " ..
				"dug in. His ring should be NOTICEABLY TIGHTER than shot 01 — radius 25 cells " ..
				"against 28, an 11% shrink in radius. CORRECT = smaller than 01 and larger " ..
				"than 03. BROKEN = identical to 01.")
		end)

		-- 200 still ticks = 8.0s to `dugin`, plus margin for the two-second settle above.
		Trigger.AfterDelay(DateTime.Seconds(13), function()
			if not requireTier(Walker, ExpectedDuginTier, "shot 03 (dug in)") then return end

			TestHarness.Screenshot("03-gauge-dugin-22c",
				"expects: same rifleman, same camera, same zoom, now dug in (still for over " ..
				"8 seconds). TIGHTEST of the three — radius 22 cells. CORRECT = the three " ..
				"rings read 28 > 25 > 22 in that order, each step clearly smaller than the " ..
				"last. BROKEN = 03 is the same size as 02, meaning `dugin` is granted but " ..
				"its +1 never reaches the drawn radius.")
		end)

		-- ---- Shot 4: five riflemen, one merged outline ------------------------------
		Trigger.AfterDelay(DateTime.Seconds(15), function()
			Test.SelectActors(Squad)
			TestHarness.FocusBetween(Squad1, Squad2, Squad3, Squad4, Squad5)
		end)

		Trigger.AfterDelay(DateTime.Seconds(17), function()
			if Test.GetSelectedCount() ~= 5 then
				Test.Fail("selected " .. tostring(Test.GetSelectedCount()) ..
					" actors, expected the 5 squad riflemen — grouping cannot be photographed " ..
					"with a selection this scenario did not build")
				return
			end

			-- RangeCircleGrouping collects peers only at EQUAL radius, so a squad that is not
			-- all on one tier would draw separate rings for a reason that has nothing to do
			-- with the grouping code. Pin the premise before photographing it.
			for i, s in ipairs(Squad) do
				if tier(s) ~= ExpectedStoppedTier then
					Test.Fail("Squad" .. tostring(i) .. " is on tier " .. tostring(tier(s)) ..
						", expected " .. tostring(ExpectedStoppedTier) ..
						" — the five are not on one radius, so nothing could merge anyway")
					return
				end
			end

			TestHarness.Screenshot("04-squad-merged-outline",
				"expects: FIVE selected riflemen in a tight clump, all on the same tier, so " ..
				"all five circles have the SAME 22-cell radius and overlap almost entirely. " ..
				"CORRECT = they read as ONE outline: a single grey boundary around the group, " ..
				"with the arcs that fall inside a neighbour's circle visibly DIMMER than the " ..
				"outer boundary. MERELY PRESENT = five equally-bright complete rings stacked " ..
				"on each other, crossing at their intersection points — that is the ungrouped " ..
				"look and it means RangeCircleGrouping collected no peers.")
		end)

		-- ---- Shot 5: one of the five walks; its ring must separate -------------------
		Trigger.AfterDelay(DateTime.Seconds(19), function()
			-- IssueMove goes through the order pipeline and does not touch the selection, so
			-- all five stay selected while one of them walks.
			Test.IssueMove(Squad3, CPos.New(30, 15))
		end)

		Trigger.AfterDelay(DateTime.Seconds(22), function()
			if Test.GetSelectedCount() ~= 5 then
				Test.Fail("the selection dropped to " .. tostring(Test.GetSelectedCount()) ..
					" while Squad3 was moving; all five must stay selected for this shot")
				return
			end

			if not requireTier(Squad3, ExpectedMovingTier, "shot 05 (Squad3 moving)") then return end
			if not requireTier(Squad1, ExpectedStoppedTier, "shot 05 (Squad1 still stopped)") then return end

			TestHarness.Screenshot("05-squad-one-moving-separates",
				"expects: same five still selected, but Squad3 is now WALKING east and is on " ..
				"tier 2 while the other four are on tier 3. CORRECT = TWO distinct boundaries: " ..
				"the four stationary men still read as one merged outline at 22 cells, and the " ..
				"walker carries his own visibly WIDER 25-cell ring that does not merge with " ..
				"them. MERELY PRESENT = one outline for all five, or five separate rings — " ..
				"either would mean radius is not what decides merging.")
		end)

		Trigger.AfterDelay(DateTime.Seconds(24), function()
			Test.Pass("captured the 25/22/19 tier ladder and the squad grouping pair")
		end)
	end

	Trigger.AfterDelay(DateTime.Seconds(6), waitForHalt)
end
