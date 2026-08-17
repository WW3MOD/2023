-- AUTO TEST: a shift-queued unload must dismount at the END of the order queue, and the
-- waypoint it will dismount at must be MARKED before it happens.
--
-- The feature under test is the marker. The user asked to see, while holding shift, where a
-- queued unload is going to put its passengers — previously the picture showed a movement line
-- to the last waypoint and nothing at all to say that anything would happen when the APC got
-- there. The deploy order already had this (test-deploy-queued-after-waypoints); this is the
-- same mechanism extended to Cargo.
--
-- Test.IssueDeploy, not Apc.UnloadPassengers(): Cargo implements IIssueDeployOrder and returns
-- an "Unload" order, so this is the command bar's Deploy button — the actual player path,
-- carrying the Shift modifier as order.Queued. Calling the scripting property would queue the
-- activity directly and bypass ResolveOrder, which is the only layer that computes the marker.
--
-- TWO ASSERTIONS, ANSWERING DIFFERENT QUESTIONS — both must hold.
--   1. WHERE THE SQUAD STEPS OUT. Behaviour: did the unload wait for the waypoints.
--   2. WHERE THE MARKER IS DRAWN. Promise: does the ghosted soldier tell the player the truth
--      about where they will step out, BEFORE they do.
-- Neither implies the other, and the gap between them is not luck — it is structural.
-- UnloadCargo resolves its own destination in OnFirstRun (self.Location, wherever it happens to
-- be standing) and uses the marker cell for NOTHING but the target-line node. So mispredicting
-- the cell moves the icon and leaves the dismount exactly where it was, invisible to assertion
-- 1 by construction. Test.GetTargetLineCells is what makes assertion 2 possible at all; without
-- it the marker is checkable only by looking at a screenshot.
--
-- WHY A WAYPOINT IS APPENDED AFTER THE UNLOAD. The prediction captures the queue's last
-- waypoint AS IT STANDS AT ISSUE TIME, on purpose: anything queued afterwards runs after the
-- dismount, so dragging the marker onto it would promise a cell the APC reaches only once the
-- squad is already on the ground. Without a post-unload waypoint on the map, "captured at issue
-- time" and "recomputed while rendering" produce the same cell and the assertion would lock in
-- nothing. With one, the marker's cell discriminates three ways: 18,16 = predicting the head of
-- the queue, 38,16 = recomputing at render time, 28,16 = correct.
--
-- WHAT THE MARKER DOES NOT CLAIM. It marks where the TRANSPORT will stop and open its doors,
-- not the cell any individual soldier ends up on: passengers take a shuffled pick of
-- CurrentAdjacentCells at dismount time, and that roll has not happened when the marker is
-- drawn. Hence the tolerance below is a cell wider than the deploy test's — it has to absorb an
-- adjacent-cell step the prediction cannot know about, and is not evidence of a loose
-- prediction.

local DeadlineSeconds = 60
local FirstWaypoint = { X = 18, Y = 16 }
local UnloadWaypoint = { X = 28, Y = 16 }
local AppendedWaypoint = { X = 38, Y = 16 }
local StartCell = { X = 8, Y = 16 }

local PassengerType = "e1.america"
local PassengerCount = 3

-- The APC stops on the ordered cell on clear ground, but a tracked vehicle settling against a
-- lane bias can finish a cell or two off, and each passenger then steps one cell further to an
-- adjacent tile. Four cells absorbs both and still leaves a sixteen-cell gap to the
-- immediate-dismount answer, so the two verdicts can never be confused for one another.
local ToleranceCells = 4

-- ~6s: far enough in that the APC has visibly left its start cell, early enough that it is
-- still short of the FIRST waypoint, so both waypoint markers and the unload marker are ahead
-- of it in frame.
local ScreenshotTicks = 100

local function Abs(v)
	if v < 0 then return -v end
	return v
end

local function CellsToString(cells)
	local s = ""
	for i = 1, #cells do
		if i > 1 then s = s .. " " end
		s = s .. cells[i].X .. "," .. cells[i].Y
	end
	return "[" .. s .. "]"
end

local function DismountedInfantry()
	local found = {}
	Utils.Do(Map.ActorsInWorld, function(a)
		if a.Type == PassengerType then
			found[#found + 1] = a
		end
	end)

	return found
end

-- Set by the marker check below; read by the verdict poller. The marker only exists while the
-- UnloadCargo activity is still queued, so it has to be sampled DURING the drive, whereas the
-- squad's cells can only be read after the dismount. Two beats, one verdict: the poller refuses
-- to pass until the marker has been sampled, so neither assertion can go unrun.
local markerChecked = false
local markerFailure = nil

local function CheckUnloadMarker()
	markerChecked = true

	local markers = Test.GetTargetLineCells(Apc, true)
	local waypoints = Test.GetTargetLineCells(Apc, false)

	if #markers ~= 1 then
		markerFailure = "expected exactly one target-line tile node (the queued unload's dismount "
			.. "marker), got " .. #markers .. " " .. CellsToString(markers)
			.. "; waypoint nodes were " .. CellsToString(waypoints)
		return
	end

	local m = markers[1]
	if m.X == UnloadWaypoint.X and m.Y == UnloadWaypoint.Y then
		return
	end

	local diagnosis = "an unexpected cell"
	if m.X == FirstWaypoint.X and m.Y == FirstWaypoint.Y then
		diagnosis = "the FIRST waypoint — the prediction is taking the head of the queue, not its tail"
	elseif m.X == AppendedWaypoint.X and m.Y == AppendedWaypoint.Y then
		diagnosis = "the waypoint appended AFTER the unload — the prediction is being recomputed as "
			.. "the queue changes instead of captured at issue time, so it promises a cell the APC "
			.. "only reaches once the squad is already on the ground"
	elseif m.X == StartCell.X and m.Y == StartCell.Y then
		diagnosis = "the APC's issue-time cell — the prediction is not seeing the queued waypoints at all"
	end

	markerFailure = "the queued unload's dismount marker is drawn on " .. diagnosis .. ". It sits at "
		.. m.X .. "," .. m.Y .. " but the unload will happen at the last waypoint as of issue time, "
		.. UnloadWaypoint.X .. "," .. UnloadWaypoint.Y
		.. "; waypoint nodes were " .. CellsToString(waypoints)
end

WorldLoaded = function()
	-- Frame the whole lane rather than the APC: the picture that matters is the APC, both
	-- waypoint markers and the unload marker on the far one, all in one shot.
	Camera.Position = WPos.New(19 * 1024, 16 * 1024, 0)

	-- Build the passengers OUT OF WORLD and load them straight in, so their starting cell plays
	-- no part in the verdict.
	--
	-- PITFALL: the second argument must be false. Cargo.Load adds the passenger to the cargo list
	-- but never calls World.Remove — the removal normally happens on the EnterTransport path, not
	-- here. Loading an actor that IS in the world leaves it in both places, and the eventual
	-- unload re-adds it, throwing "An item with the same key has already been added".
	for _ = 1, PassengerCount do
		local a = Actor.Create(PassengerType, false,
			{ Owner = Apc.Owner, Location = Apc.Location })
		Apc.LoadPassenger(a)
	end

	if Apc.PassengerCount < PassengerCount then
		Test.Fail("setup: expected " .. PassengerCount .. " passengers aboard, got "
			.. tostring(Apc.PassengerCount))
		return
	end

	-- Target lines default to Manual, i.e. only while a modifier key is physically held. A player
	-- queueing orders is holding Shift and so always sees them; a script cannot hold anything.
	Test.ShowTargetLinesAlways()
	TestHarness.Select(Apc)

	-- Exactly what the player does: click a waypoint, shift-click a second, shift-press deploy.
	Test.IssueMove(Apc, CPos.New(FirstWaypoint.X, FirstWaypoint.Y), false, false)
	Test.IssueMove(Apc, CPos.New(UnloadWaypoint.X, UnloadWaypoint.Y), false, true)
	Test.IssueDeploy(Apc, true)

	-- ...and then keeps shift-clicking. This one is queued AFTER the unload, so it runs after the
	-- dismount and must not move the marker. See the header note.
	Test.IssueMove(Apc, CPos.New(AppendedWaypoint.X, AppendedWaypoint.Y), false, true)

	UserInterface.SetMissionText(
		"QUEUED UNLOAD: APC -> waypoint 18,16 -> waypoint 28,16, unload, then -> 38,16. "
		.. "The dismount marker should sit on 28,16 (where the unload was queued), not on 38,16.")

	-- Mid-drive, with both waypoints still ahead of the APC. Re-select first: target lines fade
	-- 2.4s after the last ShowTargetLines, and the orders were issued at tick one.
	Trigger.AfterDelay(ScreenshotTicks, function()
		if Apc.IsDead then
			return
		end

		-- Sample the marker at this beat, not at the screenshot's: the shot is the human-readable
		-- record of the same instant the assertion measures, and they must not be able to drift.
		CheckUnloadMarker()

		-- At min zoom the whole 66-cell map fits the window and a one-cell marker is a few pixels
		-- across — legible to the engine, useless in a screenshot. 2.5x still frames the APC and
		-- both waypoints with room to spare.
		Test.SetZoom(2.5)
		Camera.Position = WPos.New(21 * 1024, 16 * 1024, 0)

		TestHarness.Select(Apc)
		TestHarness.Screenshot("unload-queue-AFTER-icon-shown-on-waypoint",
			"expects: APC mid-lane with a target line running through the waypoint markers at 18,16 "
			.. "and 28,16 on to 38,16, and a ghosted infantry icon drawn on the 28,16 marker only. "
			.. "The soldier icon must not sit on the APC, must not sit on 38,16, and must not hide "
			.. "the line.")
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if markerFailure ~= nil then
			return "fail: " .. markerFailure
		end

		if Apc.IsDead then
			return "fail: the APC died before it unloaded"
		end

		local troops = DismountedInfantry()
		if #troops < PassengerCount then
			return false
		end

		-- The squad is out. Refuse to call that a pass until the marker has actually been looked
		-- at, so a mistimed sample degrades to a timeout rather than to a silently half-run test.
		if not markerChecked then
			return "fail: the squad dismounted before the unload marker was ever sampled — the "
				.. "marker assertion did not run, so this pass would have meant nothing"
		end

		for i = 1, #troops do
			local loc = troops[i].Location
			local dx = Abs(loc.X - UnloadWaypoint.X)
			local dy = Abs(loc.Y - UnloadWaypoint.Y)
			if dx > ToleranceCells or dy > ToleranceCells then
				return "fail: the queued unload did not wait for the waypoints — a passenger stepped "
					.. "out at " .. loc.X .. "," .. loc.Y .. " (APC was issued the order at "
					.. StartCell.X .. "," .. StartCell.Y .. "); the squad should have dismounted at "
					.. "the last waypoint " .. UnloadWaypoint.X .. "," .. UnloadWaypoint.Y
			end
		end

		return true
	end, "The APC never unloaded its squad: the queued unload was swallowed entirely")
end
