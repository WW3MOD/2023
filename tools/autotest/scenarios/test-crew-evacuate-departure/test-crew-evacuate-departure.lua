-- CAPTURE INSTRUMENT: ejected crew walking themselves off the map, in four frames.
--
-- =====================================================================================
-- THIS GRADES NOTHING. IT IS NOT A GUARD. The guard is test-crew-auto-evacuate.
-- =====================================================================================
-- Terminal verdict is Test.Skip. Same reasoning as test-crew-dismount-pinwheel and
-- test-minimap-stance-shades: no clause here is a pass/fail statement about the mod, so a
-- Test.Pass would put a green in every run-batch.sh --all tally whether or not the capture landed.
--
-- WHY IT EXISTS. WORKSPACE/MILESTONE-260901.md grades item 2 (AutoEvacuateOnEject, 3ce18d71) as
-- ASSERTED: "No frame was read for this one. The assertion covers where the crew go, which is the
-- whole claim." The first half is the problem. test-crew-auto-evacuate is a good scenario and it
-- grades the semantics that matter most — that the evacuation is ONE-SHOT and that a real player
-- order truncates it — but the milestone's player-facing sentence is "the surviving crew walk
-- themselves off the map instead of milling around the hull waiting to be shot", and that is a
-- claim about motion over time. Nobody has watched it.
--
-- =====================================================================================
-- WHY THIS IS A SEPARATE SCENARIO AND A SEPARATE RUN FROM THE PINWHEEL
-- =====================================================================================
-- Not preference — the two cannot share a frame. The pinwheel needs all three of a hull's crew
-- standing on their fan cells at one instant. Ejections are 30 +- 15 ticks apart on abrams and each
-- man turns for the map edge the moment his 2-3 cell fan leg ends, so by the time the third man
-- reaches his cell the first has been evacuating for 60-90 ticks and is several cells away in a
-- direction the fan never chose. There is no shutter that catches both. So the pinwheel turns
-- AutoEvacuateOnEject off and this scenario leaves it at its shipped default, and they are two
-- runs.
--
-- =====================================================================================
-- WHAT MAKES THE FOUR FRAMES EVIDENCE RATHER THAN A STILL LIFE
-- =====================================================================================
-- A single frame of three men standing near a wreck is compatible with the OLD behaviour, in which
-- crew ejected and then stood there forever. The claim is a TREND, so it needs frames at different
-- times and a fixed reference to measure against. The burning hull is that reference: it does not
-- move, it is the only thing in shot that does not, and rules.yaml deliberately keeps it smoking so
-- it stays obvious.
--
-- The strongest single reading in the set is the EAST man's reversal. The hull faces north, so
-- DismountGeometry sends the three men south, east and west; the east man therefore starts by
-- walking AWAY from the boundary they will all leave by, and between frames 02 and 03 he turns
-- round and crosses back past the hull. Nothing about a crew that merely spreads out and stops can
-- produce that, and map.yaml explains why the facing was chosen to force it.
--
-- =====================================================================================
-- Actor.Location IS ToCell — THE READOUT RUNS UP TO ONE CELL AHEAD OF THE PICTURE
-- =====================================================================================
-- Unlike the pinwheel scenario, whose men are idle at every shutter, every man here is MOVING at
-- every shutter. Actor.Location is the cell being moved INTO, not the cell under the sprite, so a
-- printed position can be one cell further along the route than the pixels show. That is expected,
-- it is at most one cell, and it is always in the direction of travel. Do NOT read a one-cell
-- disagreement between the readout and the frame as the frame being wrong; a diagnosis was
-- published from exactly this confusion on 2026-08-31.

local CrewTypes = { "crew.commander.america", "crew.gunner.america", "crew.driver.america" }
local ExpectedCrew = 3

local HullX = 14
local HullY = 16

-- Camera parked left of the hull so BOTH the wreck and the west boundary are in one fixed frame,
-- and never moved again: with no camera motion there is nothing that can race the shutter, and the
-- four frames are directly comparable because they are the same view of the same ground.
local CameraX = 10
local CameraY = 16
local Zoom = 1.6

-- ============================================================================================
-- ABSOLUTE TICK SCHEDULE, and frame 02 is the one that had to be placed carefully
-- ============================================================================================
-- Inputs: PostStopDelay 20 +- 15 to the first man, EjectionDelay 30 +- 15 to each of the next two
-- (vehicles-america.yaml:474-475); fan leg 2-3 cells; Mobile Speed 25 (infantry.yaml:47) = 1024/25
-- = ~41 ticks per cell. Ejection order is Commander, Gunner, Driver and the fan index is the
-- ejection ordinal, so man 1 goes SOUTH (astern), man 2 EAST, man 3 WEST.
--
-- FRAME 02 IS A WINDOW, NOT A DEADLINE, AND IT IS BRACKETED BY TWO SHOTS. The man this capture is
-- built around walks the wrong way only briefly, and the window MOVES by ~180 ticks across the RNG
-- range, so a single shot is a bet. Man 2 ejects at DamageTick + [20, 80] = [140, 200] and walks 2-3
-- cells east (82-123 ticks) before turning back, so he is east of the hull over
-- [eject + ~20, eject + 2 x walk] — [160, 304] on the fastest branch and [220, 446] on the slowest.
-- Those overlap in [220, 304] and nowhere else, which is 84 ticks wide. Man 3 is out by 245 at the
-- very latest, so the usable interval is [245, 304]: FIFTY-NINE ticks.
--
-- 275 sits in the middle of it, with ~30 ticks of margin on each side, and 335 brackets the slow
-- branches where man 2 is east until 446. The pair costs one extra PNG and removes the only
-- single-number bet in this scenario. USE WHICHEVER OF THE TWO SHOWS A MAN RIGHT OF THE HULL; they
-- are deliberately near-duplicates and only one of them has to land.
--
-- An earlier draft of this file put frame 02 at DamageTick + 270 on the reasoning that later is
-- safer, which is the correct instinct everywhere else in this suite and is wrong here: it would
-- have photographed man 2 already most of the way home and thrown away the clearest reading in the
-- set. It also mis-derived the window by starting it at his ARRIVAL at the east cell rather than at
-- his departure from the hull, which made the branches look non-overlapping when they overlap by 84
-- ticks.
--
-- FRAME 03 at 600: man 2 is back west of the hull by 446 even on the slowest branch, and the
-- earliest anyone crosses the boundary is ~647, so all three are in open ground heading west.
--
-- FRAME 04 at 780 IS DELIBERATELY NOT SYNCHRONISED, and cannot be. The fan gives the three men
-- different head starts, so they reach the edge at different times — ~647-819 for man 3, ~740-811
-- for man 1, ~837-979 for man 2, who has the extra distance of his reversal to walk. There is no
-- tick at which all three are at the boundary. Man 2 is the one reliably still in shot. The frame
-- is authored to accept whatever is left and the readout says how many that is; do not "fix" it by
-- moving the tick.
local ReferenceShotTick = 60
local DamageTick = 120
local Frame02aTick = 275
local Frame02bTick = 335
local Frame03Tick = 600
local Frame04Tick = 780
local TextLead = 20
local VerdictTick = Frame04Tick + 60

local setupNotes = {}

local function Note(fmt, ...)
	setupNotes[#setupNotes + 1] = string.format(fmt, ...)
end

local function LiveCrew()
	local all = {}
	for _, t in ipairs(CrewTypes) do
		for _, a in ipairs(Tank.Owner.GetActorsByType(t)) do
			if not a.IsDead and a.IsInWorld then
				all[#all + 1] = a
			end
		end
	end

	return all
end

-- Print the state at a shutter so the image can be cross-checked against it. As in the pinwheel
-- scenario this is NOT the verification — test-crew-auto-evacuate already grades where the crew end
-- up, and treating a printed distance as the answer would relabel that ASSERTED result rather than
-- adding a SEEN one. Its job is to catch the image and the engine disagreeing.
local function Snapshot(label)
	local crew = LiveCrew()
	local sum = 0

	print(string.format("[evac-departure] %s: live crew=%d of %d", label, #crew, ExpectedCrew))
	for _, c in ipairs(crew) do
		local loc = c.Location
		local fromHull = TestHarness.CellDrift(HullX, HullY, loc.X, loc.Y)
		sum = sum + fromHull
		print(string.format("[evac-departure]   %-26s at %d,%d  %d cells from the hull, %d columns "
			.. "from the west boundary", c.Type, loc.X, loc.Y, fromHull, loc.X - 1))
	end

	if #crew > 0 then
		print(string.format("[evac-departure]   mean distance from hull = %.1f cells", sum / #crew))
	end

	return #crew
end

WorldLoaded = function()
	if Tank == nil or Tank.IsDead then
		Test.Skip("SETUP FAULT: the Tank actor did not resolve, so there is nothing to photograph")
		return
	end

	-- The direction words in every note below are read off this. WAngle is counterclockwise and 0 is
	-- north; if the hull is not actually facing north then the fan does not send a man east, the
	-- reversal the capture is built around does not happen, and the frames answer nothing.
	local facing = Tank.Facing.Angle
	print(string.format("[evac-departure] hull at %d,%d facing=%d (authored 0 = north)",
		Tank.Location.X, Tank.Location.Y, facing))
	if facing ~= 0 then
		Test.Skip(string.format("SETUP FAULT: hull reports facing %d, not the authored 0 (north) — "
			.. "every direction in the capture notes is wrong", facing))
		return
	end

	Camera.Position = WPos.New(CameraX * 1024, CameraY * 1024, 0)
	local applied = Test.SetZoom(Zoom)
	print(string.format("[evac-departure] camera on cell %d,%d; zoom requested %.2f applied %.2f",
		CameraX, CameraY, Zoom, applied))
	if math.abs(applied - Zoom) > 0.001 then
		Note("zoom clamped to %.2f (asked %.2f) — more or less ground is in frame than intended; "
			.. "if the west boundary is off-screen the later frames cannot be read", applied, Zoom)
	end

	UserInterface.SetMissionText(
		"CREW EVACUATE - FRAME 1 of 5: hull intact, no crew. The west map boundary is to the LEFT.")

	Trigger.AfterDelay(ReferenceShotTick, function()
		TestHarness.Screenshot("01-hull-intact-reference",
			"REFERENCE FRAME. expects: ONE intact Abrams, right of centre, nose pointing UP the "
			.. "screen, on empty grass. NO infantry anywhere. The left-hand edge of the playable "
			.. "ground — the boundary the crew will later leave by — is visible at the left of the "
			.. "frame. Nothing is burning yet. The camera does not move again for the rest of the "
			.. "run, so all four frames are the same view of the same ground and can be compared "
			.. "directly.")
	end)

	Trigger.AfterDelay(DamageTick, function()
		-- ~40% HP: past EjectionDamageState (Heavy = HP < 50%) so all three bail. Same value as
		-- test-crew-rear-dismount and test-crew-dismount-pinwheel on purpose.
		Tank.Health = math.floor(Tank.MaxHealth * 4 / 10)
		print("[evac-departure] hull dropped to ~40% HP; crew bail from here")
	end)

	Trigger.AfterDelay(Frame02aTick - TextLead, function()
		UserInterface.SetMissionText(
			"FRAME 2 of 5: crew just out. Note the man EAST (right) of the hull - watch him in frame 4.")
	end)

	-- 02a and 02b bracket the short window in which man 2 is still east of the hull; see the tick
	-- schedule above for the arithmetic. Only one of them has to land.
	local function JustOutShot(label, which)
		local n = Snapshot(label)
		if n ~= ExpectedCrew then
			Note("%s has %d crew in the world, expected %d", label, n, ExpectedCrew)
		end

		TestHarness.Screenshot(label,
			"expects: the same Abrams, now SMOKING and stationary, with THREE small blue infantry "
			.. "around it — roughly one BELOW it, one to its RIGHT and one to its LEFT, each a few "
			.. "cells out. That spread is the rear fan of a north-facing hull and is the subject of "
			.. "test-crew-dismount-pinwheel, not of this run; here it only matters that the men "
			.. "START near the wreck and that ONE OF THEM IS TO THE RIGHT OF IT, i.e. on the far "
			.. "side from the boundary they are all about to leave by. Fix his position in your mind "
			.. "— frame 04 is about him. THIS IS " .. which .. " OF TWO NEAR-DUPLICATE FRAMES taken "
			.. "60 ticks apart on purpose: the man is only east of the hull for about 3 seconds and "
			.. "the exact seconds move with the ejection RNG, so the pair brackets it. Use whichever "
			.. "one actually shows a man to the RIGHT of the hull and ignore the other. If NEITHER "
			.. "does, the reversal reading is lost for this run but frames 03 and 04 still carry the "
			.. "departure; the printed cell positions say which case you are in.")
	end

	Trigger.AfterDelay(Frame02aTick, function() JustOutShot("02a-crew-just-out", "THE EARLIER") end)
	Trigger.AfterDelay(Frame02bTick, function() JustOutShot("02b-crew-just-out", "THE LATER") end)

	Trigger.AfterDelay(Frame03Tick - TextLead, function()
		UserInterface.SetMissionText(
			"FRAME 4 of 5: all three now moving LEFT toward the boundary - including the one that started right.")
	end)

	Trigger.AfterDelay(Frame03Tick, function()
		local n = Snapshot("frame 03")
		if n ~= ExpectedCrew then
			Note("frame 03 has %d crew in the world, expected %d", n, ExpectedCrew)
		end

		TestHarness.Screenshot("03-crew-departing",
			"THE LOAD-BEARING FRAME. expects: the hull has NOT moved and is still smoking in the "
			.. "same place, and all three men are now clearly LEFT of where they were in 02a/02b, "
			.. "heading for the west boundary, spread along the route rather than in a clump. THE "
			.. "ONE THING TO CHECK: the man who was to the RIGHT of the hull in 02a/02b is no "
			.. "longer there. He has turned round and is now at or past the hull on its left. That "
			.. "reversal is the evacuation overriding the direction the dismount sent him, and "
			.. "nothing that merely spreads crew out and stops can produce it. WHAT FAILURE LOOKS "
			.. "LIKE: three men still standing exactly where 02a/02b put them, with the wreck "
			.. "unchanged — that is the pre-3ce18d71 behaviour, crew milling at the hull. Note the "
			.. "printed cell positions run up to one cell ahead of the sprites, because "
			.. "Actor.Location is the cell being moved into.")
	end)

	Trigger.AfterDelay(Frame04Tick - TextLead, function()
		UserInterface.SetMissionText(
			"FRAME 5 of 5: stragglers at the west boundary. FEWER THAN THREE MEN IS THE EXPECTED RESULT.")
	end)

	Trigger.AfterDelay(Frame04Tick, function()
		local n = Snapshot("frame 04")

		TestHarness.Screenshot("04-crew-at-boundary",
			"expects: BETWEEN ZERO AND THREE men, whoever is left, at or very close to the LEFT-HAND "
			.. "boundary of the playable ground and far from the hull — which is still burning in "
			.. "exactly the place it occupied in all three earlier frames. A COUNT BELOW THREE IS "
			.. "THE EXPECTED OUTCOME, NOT A FAILURE: the fan gives the three men different head "
			.. "starts, so they reach the edge tens of seconds apart and the early ones have already "
			.. "crossed and been disposed. The man who started EAST is the one most likely to still "
			.. "be in shot, because his reversal gave him several extra cells to walk. The run's "
			.. "printed [evac-departure] frame 04 readout says "
			.. "how many were still in the world at the shutter; read it with the image, and read "
			.. "the frame 03 readout too, because 'empty because they left' and 'empty because they "
			.. "died' look identical here and only the earlier count separates them. THE ACTUAL "
			.. "FAILURE to look for is men standing STILL in mid-field on the same cells frame 03 "
			.. "showed — that is a stalled evacuation, and it is the one outcome the count alone "
			.. "cannot distinguish from a slow one.")

		if n == 0 then
			print("[evac-departure] frame 04 is empty of crew: everyone crossed the boundary and was "
				.. "disposed before the shutter. Expected outcome, see the note on that frame.")
		end
	end)

	Trigger.AfterDelay(VerdictTick, function()
		if #setupNotes > 0 then
			Test.Skip("captures taken, but SETUP IS SUSPECT: " .. table.concat(setupNotes, "; "))
		else
			Test.Skip("captures taken; hull held its authored north facing and the crew readout is in "
				.. "the log at each of the three post-ejection shutters. NOTHING HERE IS GRADED — the "
				.. "auto-evacuate guard is test-crew-auto-evacuate; this run answers only whether the "
				.. "departure is visible.")
		end
	end)
end
