-- ASSERTION SCENARIO: with "Pre-captured Structures" ON, a neutral capturable structure is owned
-- at match start by the player on ITS OWN side of the DefconWall border, and one standing inside
-- the border band is owned by nobody.
--
-- =====================================================================================
-- THE CLAIM UNDER TEST
-- =====================================================================================
-- PreCapturedStructures is an IWorldLoaded on the world actor. When the lobby option is on it
-- asks DefconWall where the border is; on a map that HAS one -- which this map now does, see the
-- DefconWall block in rules.yaml -- a structure with any footprint cell inside the band stays
-- Neutral, and every other structure goes to the nearest contender whose own home is on the same
-- side of the border. On a map with no border it falls back to the original distance-ratio rule
-- against MiddleBandPercent.
--
-- THIS SCENARIO IS THE BORDER ARM. rules.yaml authors a two-cell vertical band at x=31,32, and
-- the middle derrick at 31,15 is a 2x2 whose whole footprint sits inside it. So the expected
-- verdict USA / Neutral / Russia is reached by the BORDER rule, and MiddleBandPercent is never
-- consulted on this map. The distances still decide the two flanking derricks: each has exactly
-- one contender on its side, and that contender is also the nearer one.
--
-- The three derricks are ALSO at 545%, 3.6% and 458% margins, so the ratio rule would reach the
-- same verdict. That is deliberate belt-and-braces, not a weakness: it means a regression that
-- silently reverted to the ratio would not red this scenario, and the thing that catches THAT is
-- the debug.log line the trait writes -- `PreCapturedStructures: border = Russia:side1, USA:side0`
-- on a working run, `border = (none -- falling back to the 10% ratio rule)` on a reverted one.
-- See map.yaml for the distance arithmetic; it is all right triangles with a half-cell rise.
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

	-- Printed BEFORE the guard so a nil is diagnosable from lua.log alone. The first run of this
	-- scenario failed here with a zero-byte lua.log precisely because the guard fired before
	-- anything was printed, and a zero-byte lua.log is also the tell for "the game never
	-- launched" -- two very different findings that looked identical from outside.
	print("[precaptured] USA=" .. tostring(usa ~= nil) .. " Russia=" .. tostring(russia ~= nil))

	if usa == nil or russia == nil then
		Test.Fail("SETUP: could not resolve players USA / Russia. A side is missing a Player object. " ..
			"CreateMapPlayers builds one per non-playable map player and one per OCCUPIED lobby slot, " ..
			"skipping empty slots (CreateMapPlayers.cs:93-121), and run-test.sh seats exactly one " ..
			"client -- so a second `Playable: True` side in map.yaml is a slot nobody fills and no " ..
			"Player is created for it. Russia must stay a MAP PLAYER here; check map.yaml")
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
			return "fail: the derrick at 11,15 is owned by '" .. near .. "'. Its footprint is cells " ..
				"11-12 x 15-16, entirely WEST of the band at x=31,32, and USA is the only contender " ..
				"whose anchor is on that side (its Supply Route centre is cell 4,16). Nothing east " ..
				"of the band can own it at any distance"
		end

		if far ~= "Russia" then
			return "fail: the derrick at 51,15 is owned by '" .. far .. "'. Its footprint is cells " ..
				"51-52 x 15-16, entirely EAST of the band, and Russia is the only contender whose " ..
				"anchor is on that side (Supply Route centre cell 60,16)"
		end

		if mid ~= "Neutral" then
			return "fail: the derrick at 31,15 was handed to '" .. mid .. "'. It is a 2x2 at 31,15, so " ..
				"its footprint is exactly cells 31-32 x 15-16, and rules.yaml authors a DefconWall " ..
				"band over x=31,32 for the whole height of Bounds -- every one of its four cells is " ..
				"border. A structure with ANY footprint cell in the band must stay Neutral. Read the " ..
				"`PreCapturedStructures: border = ...` line in debug.log first: if it says `(none)` " ..
				"the region was rejected as degenerate or the RegionCells list was lost, and the " ..
				"trait fell back to the ratio rule -- which on these distances (27.5 vs 28.5 cells, " ..
				"a 3.6% margin, inside MiddleBandPercent) would ALSO say Neutral, so a wrong owner " ..
				"here means neither rule ran. If it names two sides, the footprint test is broken"
		end

		return true
	end, "pre-captured ownership check never completed within 30s")
end
