-- ASSERTION SCENARIO: with "Pre-captured Structures" ON, a neutral capturable structure is owned
-- by the nearer player at match start, and one that nobody is meaningfully nearer to is not.
--
-- =====================================================================================
-- THE CLAIM UNDER TEST
-- =====================================================================================
-- PreCapturedStructures is an IWorldLoaded on the world actor. When the lobby option is on it
-- measures each neutral capturable structure against every playable player's anchor -- their
-- Supply Route, falling back to their spawn cell -- and hands it to the nearest, UNLESS the
-- nearest player they are not allied with is within MiddleBandPercent (10) of that distance.
--
-- The three derricks on this map are placed at 545%, 3.6% and 458% margins, so the expected
-- verdict is USA / Neutral / Russia. See map.yaml for the arithmetic; it is all right triangles
-- with a half-cell rise and can be checked without a calculator.
--
-- =====================================================================================
-- WHY THE WAIT, AND WHY IT IS NOT A RACE
-- =====================================================================================
-- The transfer goes through Actor.ChangeOwner, which QUEUES a FrameEndTask (Actor.cs:519-522)
-- rather than reassigning in place. That is deliberate -- the sync variant is documented as
-- callable only from inside an existing FrameEndTask and WorldLoaded is not one -- and it means
-- ownership has not moved yet on tick 0. It lands at the end of the first tick. The grace below
-- is thirty ticks, roughly thirty times the margin needed, so a green here is not a lucky read.
--
-- Reading EARLIER than the grace would be the interesting failure, so the scenario does not do
-- it: a "still Neutral" read at tick 0 is the correct state, not a bug, and asserting on it
-- would red the whole feature for working as designed.
--
-- =====================================================================================
-- HOW THIS CAN LOSE WITHOUT MEANING ANYTHING
-- =====================================================================================
-- Two ways, both guarded as SETUP faults rather than verdicts:
--
--   1. The option never took. If all three derricks read Neutral, the trait either did not run
--      or read the option as off -- which is the sibling scenario's expected result, not this
--      one's. That is reported distinctly, because "everything is Neutral" and "the middle one
--      is Neutral" are one keystroke apart in a diff and worlds apart as findings.
--   2. The anchors resolved somewhere unexpected. If NearUSA and NearRussia swap, the anchors
--      are not the Supply Routes -- the likeliest cause being that AnchorFor fell through to
--      HomeLocation, which on this map is whatever the harness assigned rather than the cells
--      the SRs sit on.

local Grace = 30
local Ticks = 0

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	local russia = Player.GetPlayer("Russia")
	if usa == nil or russia == nil then
		Test.Fail("SETUP: could not resolve players USA / Russia")
		return
	end

	if NearUSA == nil or Middle == nil or NearRussia == nil then
		Test.Fail("SETUP: map actors NearUSA/Middle/NearRussia did not all resolve")
		return
	end

	TestHarness.FocusBetween(NearUSA, NearRussia)

	TestHarness.AssertWithin(30, function()
		Ticks = Ticks + 1
		if Ticks < Grace then return false end

		if NearUSA.IsDead or Middle.IsDead or NearRussia.IsDead then
			return "fail: SETUP -- a derrick died; nothing in this scenario should be able to shoot"
		end

		local near = NearUSA.Owner.InternalName
		local mid = Middle.Owner.InternalName
		local far = NearRussia.Owner.InternalName

		print(string.format("[precaptured] tick=%d NearUSA=%s Middle=%s NearRussia=%s",
			Ticks, near, mid, far))

		-- ---- setup control: the option must actually have been read as ON ----
		if near == "Neutral" and mid == "Neutral" and far == "Neutral" then
			return "fail: SETUP -- all three derricks are still Neutral " .. Ticks .. " ticks in, so " ..
				"the option was read as OFF or the trait never ran. That is the EXPECTED result of " ..
				"test-precaptured-structures-off, not of this scenario. Check that rules.yaml here " ..
				"still carries `PreCapturedStructures: CheckboxEnabled: true` under World, and that " ..
				"the trait is still declared on the world actor in mods/ww3mod/rules/world.yaml"
		end

		-- ---- setup control: the anchors must be the Supply Routes ----
		if near == "Russia" and far == "USA" then
			return "fail: SETUP -- the two flanking derricks are owned by the FAR player each " ..
				"(NearUSA=Russia, NearRussia=USA). The distances did not come from the Supply " ..
				"Routes at 3,15 and 59,15. The likely cause is AnchorFor falling through to " ..
				"HomeLocation, which this map does not control -- check that both SUPPLYROUTE " ..
				"actors still exist and still carry the BaseBuilding trait"
		end

		-- ---- verdict ----
		if near ~= "USA" then
			return "fail: the derrick at 11,15 is owned by '" .. near .. "'. It sits 7.5 cells from " ..
				"USA's Supply Route and 48.5 from Russia's -- a 545% margin, six times the middle " ..
				"band. Nothing about this one is close"
		end

		if far ~= "Russia" then
			return "fail: the derrick at 51,15 is owned by '" .. far .. "'. It sits 8.5 cells from " ..
				"Russia's Supply Route and 47.5 from USA's -- a 458% margin"
		end

		if mid ~= "Neutral" then
			return "fail: the derrick at 31,15 was handed to '" .. mid .. "'. It sits 27.5 cells from " ..
				"USA's Supply Route and 28.5 from Russia's, a margin of 3.6%, which is inside " ..
				"PreCapturedStructuresInfo.MiddleBandPercent (10) and must therefore stay Neutral. " ..
				"Either the band shrank below 3.6 -- check the field, and re-run " ..
				"tools/precaptured-calibration/precaptured_calibration.py before changing this " ..
				"expectation -- or the middle test is not being applied at all, in which case the " ..
				"woodland-warfare nuclear reactor is now owned by somebody too"
		end

		return true
	end, "pre-captured ownership check never completed within 30s")
end
