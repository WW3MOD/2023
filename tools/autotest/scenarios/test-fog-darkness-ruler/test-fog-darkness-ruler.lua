-- CAPTURE INSTRUMENT: one brightness sample per fog visibility band, in a single frame.
--
-- =====================================================================================
-- THIS GRADES NOTHING. IT IS NOT A GUARD.
-- =====================================================================================
-- The terminal verdict is Test.Skip, for the same reason test-screenshot-smoke's is: no
-- clause here is a pass/fail statement about the mod. The whole output is one PNG plus the
-- setup readout below, and a human decides. A Test.Pass would make this read as a
-- regression guard it is not -- that exact mislabelling was corrected on this suite on
-- 2026-09-01 and is not worth repeating.
--
-- WHAT IT IS FOR. mods/ww3mod/rules/world.yaml:241 ships FogDarkness: 1.85, which takes
-- explored-but-unseen ground from 25.6% of lit brightness to 6.4% -- 74.8% darker. That
-- arithmetic is already pinned by OpenRA.Test/FogDarknessTest.cs and needs no picture. The
-- open question, never looked at by a human, is whether the RESULT is still readable: can a
-- player make out their own surroundings, and does fogged ground still carry shape?
--
-- =====================================================================================
-- WHY ONE FRAME IS ENOUGH -- THE CAPTURE IS SELF-NORMALISING
-- =====================================================================================
-- The obvious instrument is a before/after pair at FogDarkness 1 and 1.85. It is not
-- needed, and skipping it removes the risk that the two scenarios drift apart and quietly
-- stop being comparable.
--
-- Visibility 10 draws no fog layer at all (FogDarknessTest.FullyVisibleGroundIsUnaffected),
-- so the x=9 sample IS the unfogged reference, in the same frame, on the same terrain, under
-- the same lighting. Every other sample divided by it gives that band's transmission
-- directly. The predicted ladders are far enough apart to identify the value in force:
--
--            vis:   10     9     8     7     6     5     4     3     2     1
--   FogDarkness 1:  100%  93.0  84.9  76.0  66.8  57.5  48.4  40.0  32.3  25.6
--   FogDarkness 1.85 100%  87.1  73.1  58.9  45.6  33.9  24.0  16.3  10.5   6.4
--
-- At visibility 1 those differ by a factor of four. A reader cannot confuse them.
--
-- THE TERRAIN IS WHAT MAKES THE DIVISION LEGITIMATE. The shared test map.bin is 2244 cells
-- of tile type 255 and nothing else -- one terrain type, no resources, 16 random visual
-- variants of the same Clear tile. So albedo is constant across the sample row up to
-- per-variant noise, and averaging a box of several hundred pixels cancels that noise.
-- Sampling a single pixel would not.
--
-- =====================================================================================
-- THE SETUP READOUT IS THE PART THAT MAKES THE IMAGE TRUSTWORTHY
-- =====================================================================================
-- Everything above depends on each sample cell actually sitting in the band this scenario
-- claims for it, which in turn depends on ^StandardVision's rung radii and on nothing else
-- on the map emitting vision. Rather than ask the reader to trust that arithmetic, the run
-- asks the engine: Test.GetVisibility returns the resolved 0-10 strength per cell, and every
-- sample and marker cell is read and printed before the shot. Any disagreement with the
-- prediction is carried in the Skip message itself, so it lands in result.json and cannot be
-- missed by someone who never opens lua.log.
--
-- If that readout disagrees, THE IMAGE IS NOT EVIDENCE and the brightness ladder below must
-- not be read off it. The two ways it goes wrong are worth naming: a stray vision source
-- (which reads as sample cells too BRIGHT, several bands high), and Explored being off
-- (which reads as visibility 0 -- shroud, not fog -- on the far samples, and shroud is
-- unaffected by FogDarkness, so the frame would answer a different question entirely).

-- Sample cells on Observer's own row, paired with the visibility each should resolve to.
-- Radius from Observer at 6,16 is simply x-6. Bands are 3 cells wide and each sample sits
-- in the middle of one, so a one-cell error in the reader's pixel mapping is still correct.
local SampleRow = 16
local Samples = {
	{ x = 9,  vis = 10, radius = 3 },
	{ x = 12, vis = 9,  radius = 6 },
	{ x = 15, vis = 8,  radius = 9 },
	{ x = 18, vis = 7,  radius = 12 },
	{ x = 21, vis = 6,  radius = 15 },
	{ x = 24, vis = 5,  radius = 18 },
	{ x = 27, vis = 4,  radius = 21 },
	{ x = 30, vis = 3,  radius = 24 },
	{ x = 33, vis = 2,  radius = 27 },
	{ x = 36, vis = 1,  radius = 30 },
	{ x = 46, vis = 1,  radius = 40 },
}

local MarkerRow = 22
local MarkerXs = { 6, 12, 18, 24, 30, 36, 42 }

-- Centre of the span the samples occupy (x=9..46), so the ruler sits mid-frame with margin
-- on both sides rather than running off the right edge on a narrow window.
local CameraCellX = 26
local CameraCellY = 16

-- Zoom is pinned rather than inherited so the cell->pixel mapping in description.txt is a
-- statement about this run and not about whatever zoom the harness happened to start at.
local TargetZoom = 1.0

local SettleTicks = 40
local ShotTicks = 75
local VerdictTicks = 130

local setupNotes = {}

local function ReadLadder(usa)
	local mismatches = 0

	for _, s in ipairs(Samples) do
		local actual = Test.GetVisibility(usa, CPos.New(s.x, SampleRow))
		local flag = ""
		if actual ~= s.vis then
			flag = "  <== MISMATCH, predicted " .. s.vis
			mismatches = mismatches + 1
		end

		print(string.format("[fog-ruler] sample x=%d (r=%dc) visibility=%d%s",
			s.x, s.radius, actual, flag))
	end

	for _, mx in ipairs(MarkerXs) do
		print(string.format("[fog-ruler] marker x=%d,y=%d visibility=%d",
			mx, MarkerRow, Test.GetVisibility(usa, CPos.New(mx, MarkerRow))))
	end

	return mismatches
end

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	if usa == nil then
		Test.Skip("SETUP FAULT: could not resolve the USA player, so no visibility could be read")
		return
	end

	if Observer == nil then
		Test.Skip("SETUP FAULT: map actor Observer did not resolve, so the map has no vision source")
		return
	end

	Camera.Position = WPos.New(CameraCellX * 1024, CameraCellY * 1024, 0)

	-- SetZoom returns what was actually applied, which is not necessarily what was asked for.
	-- The reader needs the applied value to convert cells to pixels, so it is logged rather
	-- than assumed.
	local appliedZoom = Test.SetZoom(TargetZoom)
	print(string.format("[fog-ruler] camera centred on cell %d,%d; zoom requested %.2f applied %.2f",
		CameraCellX, CameraCellY, TargetZoom, appliedZoom))

	if math.abs(appliedZoom - TargetZoom) > 0.001 then
		setupNotes[#setupNotes + 1] = string.format(
			"zoom was clamped to %.2f (asked %.2f), so one cell is %.1f px, not 24",
			appliedZoom, TargetZoom, 24 * appliedZoom)
	end

	UserInterface.SetMissionText(
		"FOG RULER: one brightness sample per vision band along the tank's row, x=9 to x=46.")

	-- Vision is resolved on the MapLayers tick, so nothing is read until it has settled.
	-- Reading in WorldLoaded would report the pre-tick state.
	Trigger.AfterDelay(SettleTicks, function()
		local mismatches = ReadLadder(usa)
		if mismatches > 0 then
			setupNotes[#setupNotes + 1] = string.format(
				"%d of %d sample cells did NOT resolve to their predicted visibility band -- "
				.. "the brightness ladder must NOT be read off this image; see lua.log for which",
				mismatches, #Samples)
		end
	end)

	-- The shot gets its own delay, and the verdict gets another one after it. Test.Screenshot
	-- only ARMS a capture -- the pixels are sampled at the end of the NEXT RenderTick -- so
	-- anything that touches the world in the same closure is photographed instead of the state
	-- that was asserted. Nothing here changes state, but the gap is kept anyway: the one time
	-- this suite skipped it, the resulting mislabelled frame was caught only by diffing two
	-- images against each other.
	Trigger.AfterDelay(ShotTicks, function()
		TestHarness.Screenshot("fog-ruler",
			"expects: an M1 Abrams left of centre on open grass, with brightness falling off "
			.. "smoothly to its right along its own row; a line of seven pillboxes six cells "
			.. "below that row, the leftmost clearly lit and the rightmost two nearly black and "
			.. "INDISTINGUISHABLE FROM EACH OTHER. No muzzle flash, no movement, no enemy unit. "
			.. "Read the brightness ladder off the tank's row, not off the pillbox row.")
	end)

	Trigger.AfterDelay(VerdictTicks, function()
		if #setupNotes > 0 then
			Test.Skip("capture taken, but SETUP IS SUSPECT: " .. table.concat(setupNotes, "; "))
		else
			Test.Skip("capture taken; every sample cell resolved to its predicted visibility band")
		end
	end)
end
