--[[
	WIP CAPTURE RIG -- R3 of the 2026-09-19 Escalation review. NOT a gate scenario; delete it once
	the frames have been read.

	WHAT IT IS FOR. The review's §2.7 lists three questions about the bots under Escalation that
	could only be READ and not SEEN:
	    1. does the border staging produce a LINE or a CLUMP?
	    2. is artillery held BEHIND the staged line at standoff, or parked in it?
	    3. do the slack minutes of the no-rush clock go on DEFENSES or on more armour at the border?
	The manager's screenshot channel drives TestModeScreenshots through a cmd file that accepts
	`screenshot` / `click` / `hover` only -- it cannot move the camera. So the camera is moved from
	here, and the run is otherwise byte-for-byte test-escalation-full-match (see rules.yaml).

	THE VERDICT MEASURES NOTHING ABOUT THE MODE. It says only that each DEFCON 3 capture was taken
	while the match was actually at DEFCON 3 with the wall standing. That guard is not ceremony: if
	Escalation failed to apply, the level reads 0, the border is never drawn, and twelve photographs
	of an ordinary Skirmish would be indistinguishable from twelve photographs of the thing we
	wanted.
	Calling Test.Pass is also what flushes the PNGs -- TestGlobal.ExitWhenCapturesFlushed polls
	AllCapturesFlushed() before exiting, and a run killed by --timeout instead can lose captures
	that are still mid-encode on the ThreadPool.

	============================================================================================
	THE GEOMETRY, DERIVED RATHER THAN EYEBALLED
	============================================================================================
	Measured offline by decoding this scenario's own map.bin through tools/nav-guard/modload.py
	(no build, no launch). map.bin is md5 90824f8f1840b1c7ce4fc628da067db5, identical to
	mods/ww3mod/maps/polar-disorder-ww3/map.bin.

	The two homes are 93,16 (USA-bot, north-east) and 4,81 (Russia-bot, south-west), 110.2 cells
	apart, and DefconWall derives the border as their perpendicular bisector -- so it passes through
	48.5,48.5 running north-west to south-east, direction (0.590, 0.808), with the unit normal
	toward the USA side (0.808, -0.590).

	THE BORDER IS NOT ONE OPEN LINE, AND THIS IS THE FACT THE FRAMES MUST BE READ AGAINST. Walking
	the bisector cell by cell, it crosses three impassable belts:

	    along  -60..-50   OPEN     cells (13,0)..(19,8)      Clear
	    along  -49..-38   blocked  cells (20,9)..(26,18)     Rock + Water
	    along  -37..-33   open     cells (27,19)..(29,22)    Clear + Bridge
	    along  -32..-24   blocked  cells (30,23)..(34,29)    Rock + Water
	    along  -23.. -3   OPEN     cells (35,30)..(47,46)    Clear, Road, Beach, Rough   <-- NW GAP
	    along   -2.. +1   blocked  cells (47,47)..(49,49)    Cliffs
	    along   +2..+23   OPEN     cells (50,50)..(62,67)    Clear, Road, Beach, Rough   <-- SE GAP
	    along  +24..+30   blocked  cells (63,68)..(66,73)    Rock + Water
	    along  +31..+37   open     cells (67,74)..(70,78)    Clear, Road, Beach
	    along  +38..+49   blocked  cells (71,79)..(77,88)    Rock + Water
	    along  +50..+60   OPEN     cells (78,89)..(84,97)    Clear

	So the midpoint itself is a FOUR-CELL CLIFF PLUG, and the two 21-cell open stretches either
	side of it are the only wide crossings near the direct axis between the two Supply Routes.
	Their centres -- 41,38 and 56,59, both Clear -- are where the close frames are aimed.

	THE CONSEQUENCE FOR JUDGING THE IMAGES, AND IT IS THE WHOLE REASON THIS TABLE IS HERE:
	A CLUMP AT A GAP IS CORRECT BEHAVIOUR, NOT A DEFECT. Both gaps carry a Road, so a force that
	masses on one of them is funnelling through the only crossing the terrain offers, which is what
	an army does. What would be wrong is a clump on OPEN ground with twenty cells of unoccupied
	frontage beside it, or a force split thinly across a belt it cannot cross. Read shape against
	the table above, never against an intuition about spacing.
--]]

-- ============================================================================================
-- WHERE THE CAMERA LOOKS
-- ============================================================================================
-- Cells, not world units; Look() converts. All three verified Clear except MIDPOINT, which is the
-- cliff plug and is used only for the fully-zoomed-out frame where the plug is the point.
local MIDPOINT = { X = 48, Y = 48 }
local NW_GAP   = { X = 41, Y = 38 }
local SE_GAP   = { X = 56, Y = 59 }

-- THREE SCALES, BECAUSE THE MANAGER RUNS THIS ONCE AND ONE SCALE IS A GAMBLE. Zoom is a MULTIPLE
-- OF THE DEFAULT level, not the engine's raw Viewport.Zoom, so the same number frames the same
-- amount of map at any resolution (CameraGlobal.cs:29-35).
--
-- Camera.MinZoom is 0.25 here and that is worth stating rather than trusting: WW3MOD leaves
-- Viewport.unlockMinZoom on for everyone (Viewport.cs:69, :91-97) and unlockedMinZoomScale is 0.25
-- (:70), so EffectiveMinZoom is a quarter of the default and the fully-out frame spans four times
-- the linear extent of zoom 1. On a 98x98 map that is the whole thing -- but the map is then SMALL
-- in the frame, which is why there is a zoom-1 frame between it and the close pair rather than a
-- choice between two extremes. 2 is the value demo-defcon-wall uses for a legible border on a
-- 98x98 map, and it calls zoom 1 "roughly half the magnification" of that.
local CLOSE_ZOOM = 2
local MID_ZOOM = 1

-- ============================================================================================
-- WHEN
-- ============================================================================================
-- RAW TICKS THROUGHOUT. test-helpers.lua:36 carries TicksPerSecond = 25 as a harness BUDGET
-- convention; the real rate is 16.67 (60 ms timestep), and ScreenshotAfter's seconds round-trip
-- does not always recover the tick exactly. Trigger.AfterDelay takes ticks, so nothing is
-- converted and nothing can be off by one.
local NORUSH_TICKS = 5000       -- NoRushDefault 5 minutes, inherited, not overridden here

local T_MID   = 2500            -- half way through Positioning: first wave should have arrived
local T_LATE  = 4800            -- 200 ticks (12 s) before the border lifts: final posture
local T_OPEN  = 5010            -- inside the cease-fire; see the window below
local T_FINISH = 5200           -- verdict, ~6 s after the last capture (T_OPEN + 90 = 5100)

-- ==== THE CEASE-FIRE WINDOW IS NOW A MEASURED NUMBER, NOT A GUESS ====
-- R1 ran this same scenario on the shipped clocks (run 260920_010605, seed -1662796604) and its
-- verdict recorded:  3 -> 2 at tick 5000 (the clock, exactly)  and  2 -> 1 at tick 5098.
-- SO DEFCON 2 LASTED 98 TICKS = 5.9 SECONDS. The review predicted about ONE tick and was wrong by
-- two orders of magnitude; 98 ticks is still far too short to be a phase a player participates in,
-- but it is long enough that something happened in it -- the bots issued 118 and 121 orders inside
-- those 98 ticks -- and that something is what these four frames are for.
--
-- GIVEN THE SAME --seed THIS RUN REPRODUCES THAT MATCH, so the four contact captures land at known
-- positions inside the phase rather than hopefully near it:
--     5010   DEFCON 2 + 10   (0.6 s in)   armies as Positioning left them
--     5040   DEFCON 2 + 40   (2.4 s in)   closing, or already shooting
--     5070   DEFCON 2 + 70   (4.2 s in)   28 ticks before the first kill
--     5100   DEFCON 1 + 2                 two ticks after it; the phase is over
-- IF THE SEED IS NOT R1's, THOSE LABELS ARE WRONG AND ONLY THE TICK NUMBERS ARE TRUE.

local FRAME_LEAD = 4            -- ticks between moving the camera and taking the frame

-- "At the border" for the census below, in CELLS. Deliberately compared as integer cell distance
-- rather than as (a.CenterPosition - centre).Length: WVec arithmetic is available in C# but the Lua
-- binding surface for it is not something this rig should be the first scenario to rely on, and
-- CPos.X / CPos.Y are plain numbers that every scenario in the tree already reads.
local BORDER_BAND_CELLS = 12

local USA, RUS

WorldLoaded = function()
	USA = Player.GetPlayer("USA-bot")
	RUS = Player.GetPlayer("Russia-bot")

	local notes = {}
	local faults = {}
	local function note(fmt, ...) notes[#notes + 1] = string.format(fmt, ...) end
	local function fault(fmt, ...) faults[#faults + 1] = string.format(fmt, ...) end

	local function Look(c, zoom)
		Camera.Position = WPos.New(c.X * 1024 + 512, c.Y * 1024 + 512, 0)
		Camera.Zoom = zoom
	end

	-- HOW MANY GROUND ATTACKERS EACH SIDE HAS WITHIN 12 CELLS OF A GAP, SO THE FRAMES CARRY
	-- NUMBERS AS WELL AS SHAPES. A photograph cannot be counted reliably at a glance and a
	-- reviewer should not have to try: "line or clump" is a question about how N units are
	-- arranged, and N belongs in the verdict text next to the image that shows the arrangement.
	-- GetGroundAttackers is ActorsHavingTrait<AttackBase> filtered to Mobile (PlayerProperties.cs
	-- :83-90), so it counts tanks, IFVs, infantry and artillery and excludes the Supply Route.
	local function CountNear(player, c)
		local n = 0
		local limit = BORDER_BAND_CELLS * BORDER_BAND_CELLS
		for _, a in ipairs(player.GetGroundAttackers()) do
			local dx = a.Location.X - c.X
			local dy = a.Location.Y - c.Y
			if dx * dx + dy * dy <= limit then
				n = n + 1
			end
		end

		return n
	end

	local function Census(tag)
		note("%s: USA ground near NW/SE gap %d/%d, RUS %d/%d; total ground USA %d RUS %d; "
			.. "level %s wall %s",
			tag,
			CountNear(USA, NW_GAP), CountNear(USA, SE_GAP),
			CountNear(RUS, NW_GAP), CountNear(RUS, SE_GAP),
			#USA.GetGroundAttackers(), #RUS.GetGroundAttackers(),
			tostring(Test.DefconLevel()), tostring(Test.DefconWallActive()))
	end

	-- A DEFCON 3 capture is only interpretable if the phase was really running, so each of the two
	-- Positioning capture ticks checks the level and the wall rather than trusting the clock.
	local function RequirePositioning(tick)
		local level = Test.DefconLevel()
		if level ~= 3 then
			fault("tick %d was meant to be inside Positioning and DEFCON read %s instead -- "
				.. "the frames at this tick photograph a different phase and must not be read as "
				.. "staging. If it reads 0 the Escalation mode did not apply at all.", tick, tostring(level))
		elseif not Test.DefconWallActive() then
			fault("tick %d was at DEFCON 3 but Test.DefconWallActive() was false, so the border was "
				.. "not standing and there was nothing for either side to stage against. READ "
				.. "debug.log FOR `no line derived from N combatant home(s)`.", tick)
		end
	end

	-- ---- the frames ------------------------------------------------------------------------
	-- Three per Positioning tick and two at the opening, all from the same three viewpoints, so
	-- any pair of frames with the same suffix is directly comparable.

	local function CaptureSet(tick, prefix, label)
		-- 1. FULLY OUT. Camera.MinZoom is as far out as this resolution allows, so on a 98x98 map
		-- this is the whole border including all three impassable belts and both Supply Routes.
		-- It is the insurance frame: if neither side massed on the direct axis, the close frames
		-- come back empty and only this one says where they went instead.
		Trigger.AfterDelay(tick - FRAME_LEAD, function() Look(MIDPOINT, Camera.MinZoom) end)
		Trigger.AfterDelay(tick, function()
			Test.Screenshot(prefix .. "-a-wide-whole-border",
				label .. " -- FULLY ZOOMED OUT, centred on the bisector midpoint 48,48 (which is "
				.. "itself the four-cell cliff plug). expects: the amber hatched border running "
				.. "north-west to south-east through the middle of the map, and both armies "
				.. "visible on their own sides of it. THE QUESTION THIS FRAME ANSWERS IS *WHERE*: "
				.. "which of the border's open stretches each side chose, and whether the force is "
				.. "spread along one or collected at a point. The two wide open stretches are "
				.. "cells (35,30)..(47,46) north-west of the plug and (50,50)..(62,67) south-east "
				.. "of it; everything else on the line is Rock, Water or Cliffs. NOT a defect: a "
				.. "force gathered at one gap, because both gaps carry a Road and the belts either "
				.. "side are uncrossable. IS a defect: units strung along a blocked belt they can "
				.. "never cross, or a force still sitting at its Supply Route with the clock "
				.. "nearly out.")
		end)

		-- 2 and 3. THE TWO GAPS, CLOSE. Centred ON the border rather than to one side of it, so
		-- each frame shows both armies' near-border ground at once -- which is what the standoff
		-- question needs: artillery held back reads as a second rank with a gap in front of it,
		-- and that gap is only measurable against the line it is behind.
		-- 2. THE MIDDLE SCALE, AND THE ONE MOST LIKELY TO BE THE USABLE FRAME. Zoom 1 centred on the
		-- cliff plug covers both open stretches at once at ordinary playing magnification, so the
		-- two forces can be compared against each other and against the belts in a single image
		-- without either being four times too small to count.
		Trigger.AfterDelay(tick + 30 - FRAME_LEAD, function() Look(MIDPOINT, MID_ZOOM) end)
		Trigger.AfterDelay(tick + 30, function()
			Test.Screenshot(prefix .. "-b-mid-both-gaps",
				label .. " -- ZOOM 1 (ordinary playing magnification) on 48,48, so both open "
				.. "crossings and the cliff plug between them are in one frame. THE PRIMARY FRAME "
				.. "FOR THE LINE-VS-CLUMP QUESTION: at this scale a 21-cell open stretch is wide "
				.. "enough to see whether a force fills it or sits at one end of it, and both sides "
				.. "are visible simultaneously so their shapes can be compared rather than judged "
				.. "one at a time. If the fully-out frame above is too small to count units and the "
				.. "gap frames below are too tight to show the frontage, this is the one to read.")
		end)

		Trigger.AfterDelay(tick + 60 - FRAME_LEAD, function() Look(NW_GAP, CLOSE_ZOOM) end)
		Trigger.AfterDelay(tick + 60, function()
			Test.Screenshot(prefix .. "-c-nw-gap",
				label .. " -- the NORTH-WEST crossing, centred on the border at 41,38 at zoom "
				.. CLOSE_ZOOM .. ". USA-bot's ground is up and to the RIGHT of the amber line, "
				.. "Russia-bot's down and to the LEFT. THREE THINGS TO JUDGE. (a) SHAPE: a LINE is "
				.. "units abreast along the border with roughly even spacing and their frontage "
				.. "filling the open stretch; a CLUMP is a single knot with empty frontage beside "
				.. "it. (b) ARTILLERY STANDOFF: Paladin/M270 on the USA side, Giatsint/Grad/TOS on "
				.. "Russia's -- they should sit as a SECOND RANK several cells behind the line "
				.. "units, not mixed into the front row and not touching the band. Artillery in "
				.. "the front rank is the failure the Fires doctrine work was supposed to end. "
				.. "(c) DEPTH: is there anything behind the front rank at all, or is the whole "
				.. "force in one row? FAIL if any unit is standing INSIDE the amber band -- the "
				.. "band is impassable while the wall stands, so a unit in it means the seal leaked.")
		end)

		Trigger.AfterDelay(tick + 90 - FRAME_LEAD, function() Look(SE_GAP, CLOSE_ZOOM) end)
		Trigger.AfterDelay(tick + 90, function()
			Test.Screenshot(prefix .. "-d-se-gap",
				label .. " -- the SOUTH-EAST crossing, centred on the border at 56,59 at zoom "
				.. CLOSE_ZOOM .. ". Same three judgements as the north-west frame. Reading the "
				.. "PAIR is what answers the shape question honestly: a bot that put a tidy line "
				.. "on one gap and nothing on the other has made a choice, which is good play; a "
				.. "bot that put half a clump on each has split its force across two crossings it "
				.. "cannot support between, which is not.")
		end)
	end

	-- HALF WAY THROUGH POSITIONING. The review's derivation predicts a starting-cash wave in
	-- position at 2:32 on this map (2533 ticks, d = 58 cells), and this scenario's players carry
	-- StartingUnitsClass: motorized -- so they ALSO have three vehicles and fifteen infantry from
	-- tick 0, which have only to drive. By tick 2500 the front should exist.
	Trigger.AfterDelay(T_MID - FRAME_LEAD - 1, function() RequirePositioning(T_MID) end)
	Trigger.AfterDelay(T_MID - FRAME_LEAD - 1, function() Census("t2500") end)
	CaptureSet(T_MID, "01-t2500", "HALF WAY THROUGH POSITIONING (tick 2500 of 5000)")

	-- TWELVE SECONDS BEFORE THE BORDER LIFTS. The review argues minutes 3-5 of the clock are
	-- fortify-and-mine rather than idle, and that the alternative is stacking more armour on the
	-- line. The difference between this set and the 2500 set is the evidence: compare the wide
	-- frames for new DEFENSES behind each side, and the gap frames for a thicker front rank.
	Trigger.AfterDelay(T_LATE - FRAME_LEAD - 1, function() RequirePositioning(T_LATE) end)
	Trigger.AfterDelay(T_LATE - FRAME_LEAD - 1, function() Census("t4800") end)
	CaptureSet(T_LATE, "02-t4800", "TWELVE SECONDS BEFORE THE BORDER LIFTS (tick 4800 of 5000)")

	-- TEN TICKS AFTER IT LIFTS. THIS IS THE FRAME THE REVIEW'S CENTRAL CLAIM RESTS ON: §2.4 argues
	-- DEFCON 2 lasts about one tick because Positioning parks both armies within weapon range
	-- across a one-cell band, so the first casualty is taken before anyone has read the banner.
	-- No level assertion here on purpose -- if the claim is right the level may ALREADY be 1 by
	-- this tick, and that is the finding rather than a fault.
	Trigger.AfterDelay(T_OPEN - FRAME_LEAD, function() Look(NW_GAP, CLOSE_ZOOM) end)
	Trigger.AfterDelay(T_OPEN, function()
		Census("t5010")
		Test.Screenshot("03-t5010-nw-contact",
			"DEFCON 2 + 10 TICKS (0.6 s into the cease-fire), north-west crossing. expects: the "
			.. "amber band GONE -- ActiveLevels is { 3 }, so the wall comes down the instant the "
			.. "phase changes -- and both front ranks standing wherever Positioning left them. "
			.. "THE MEASUREMENT THIS FRAME IS FOR: how many CELLS separate the nearest opposing "
			.. "units. R1 measured the cease-fire at 98 ticks, so there was 5.9 s of it; this frame "
			.. "says whether that was 5.9 s of CLOSING (open ground here, and the separation is "
			.. "what the DMZ proposal §B1b would widen) or 5.9 s of ACQUIRING AND SHOOTING at a "
			.. "range they already had (units two or three cells apart, and §B1b buys nothing "
			.. "because nobody had to move). That distinction is the whole of the §B1 ruling and it "
			.. "cannot be read off a tick count.")
	end)

	Trigger.AfterDelay(T_OPEN + 30 - FRAME_LEAD, function() Look(MIDPOINT, MID_ZOOM) end)
	Trigger.AfterDelay(T_OPEN + 30, function()
		Test.Screenshot("03-t5040-mid-contact",
			"DEFCON 2 + 40 TICKS (2.4 s in) AT ZOOM 1, matching the -b- frames above. expects: no "
			.. "amber line anywhere along the former border. Compare directly against 02-t4800-b, "
			.. "which is the same viewpoint 3.5 s earlier: the two forces should be in the same "
			.. "places minus the band between them. IF THEY HAVE VISIBLY MOVED, note which way -- "
			.. "both sides advancing into the vacated band is what collapses the phase, and at 40 "
			.. "ticks (about 2.5 cells of tank movement) a visible advance means they were NOT "
			.. "already in range and the 98 ticks were spent closing.")
	end)

	Trigger.AfterDelay(T_OPEN + 60 - FRAME_LEAD, function() Look(SE_GAP, CLOSE_ZOOM) end)
	Trigger.AfterDelay(T_OPEN + 60, function()
		Test.Screenshot("03-t5070-se-contact",
			"DEFCON 2 + 70 TICKS (4.2 s in), south-east crossing -- TWENTY-EIGHT TICKS BEFORE THE "
			.. "FIRST KILL, if the seed matches R1's. This is the closest frame to the moment the "
			.. "mode is built around. expects: muzzle flashes, or units visibly engaged, at "
			.. "whichever crossing the kill happened on. Same cell-separation measurement as the "
			.. "north-west frame. The PAIR matters because the two gaps may differ: one crossing "
			.. "contested and the other empty is a different opening from both at once, and it also "
			.. "tells us where to look for the casualty.")
	end)

	Trigger.AfterDelay(T_OPEN + 90 - FRAME_LEAD, function() Look(MIDPOINT, Camera.MinZoom) end)
	Trigger.AfterDelay(T_OPEN + 90, function()
		Census("t5100")
		Test.Screenshot("04-t5100-wide-contact",
			"DEFCON 1 + 2 TICKS -- the cease-fire is OVER by this frame (R1: 2 -> 1 at tick 5098), "
			.. "so this is the first picture of open war. Fully out. expects: no amber "
			.. "line anywhere, and the two armies where the frames above left them. This is the "
			.. "control for the two wide Positioning frames -- put the three side by side and the "
			.. "whole opening reads as one sequence: where each side went, what it had built by the "
			.. "end of the clock, and what the map looked like the instant the rule lifted.")
	end)

	-- ---- verdict -----------------------------------------------------------------------------
	Trigger.AfterDelay(T_FINISH, function()
		note("finish tick %d: level %s, wall %s", T_FINISH,
			tostring(Test.DefconLevel()), tostring(Test.DefconWallActive()))
		note("recorded transitions: 3@%s 2@%s 1@%s (clock %d)",
			tostring(Test.DefconLevelReachedTick(3)), tostring(Test.DefconLevelReachedTick(2)),
			tostring(Test.DefconLevelReachedTick(1)), NORUSH_TICKS)

		local summary = "R3 capture rig -- NO MEASUREMENT, the frames are the output. "
			.. table.concat(notes, " | ")

		if #faults > 0 then
			Test.Fail(table.concat(faults, " ;; ") .. " ;; " .. summary)
		else
			Test.Pass(summary)
		end
	end)
end
