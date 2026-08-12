-- AUTO TEST: a shift-queued deploy must execute at the END of the order queue.
--
-- Reported by the user: "when I hold shift and try to queue up a deploy order for the supply
-- truck it instantly drops it, instead of queueing it up after the waypoints." The player's
-- intent — drive there, THEN unload — is discarded and the load lands under the truck's wheels
-- at the beachhead, which is the one place it is worth nothing.
--
-- Test.IssueDeploy, not a direct activity queue: the defect lives entirely on the order path.
-- The deploy order carries a queued flag from the Shift modifier all the way through the
-- targeter, the order constructor and the wire; whether it survives ResolveOrder is exactly
-- what this asks. A test that queued the activity itself would bypass the only layer in
-- question and pass no matter what.
--
-- WHY THE CRATE'S CELL IS THE VERDICT AND NOT A TIMER. "Did it fire too early" is a race to
-- measure; "where did the crate land" is a fact that survives on the map for the rest of the
-- run. An immediate deploy drops at the truck's issue-time cell (8,16); a correctly queued one
-- drops at the last waypoint (28,16). Twenty cells is far past any tolerance either answer
-- could plausibly need, so the two verdicts can never be confused for one another.
--
-- TWO ASSERTIONS, ANSWERING DIFFERENT QUESTIONS — both must hold.
--   1. WHERE THE CRATE LANDS. Behaviour: did the deploy wait for the waypoints.
--   2. WHERE THE MARKER IS DRAWN. Promise: does the ghosted crate icon tell the player the
--      truth about where it will land, BEFORE it lands.
-- Neither implies the other. The icon is drawn from DropsSupplyCache.PredictedDropCell, a
-- separate walk of the queue that runs at issue time; it could point at the wrong waypoint
-- while the drop itself still lands correctly, and that regression is visible only to someone
-- who looks at a screenshot. Test.GetTargetLineCells is what makes it machine-checkable.
--
-- WHY A WAYPOINT IS APPENDED AFTER THE DEPLOY. PredictedDropCell captures the queue's last
-- waypoint AS IT STANDS AT ISSUE TIME, on purpose (see its doc comment): anything the player
-- queues afterwards runs after the drop, so dragging the marker onto it would promise a cell
-- the truck reaches only once the crate is already on the ground. Without a post-deploy
-- waypoint on the map, "captured at issue time" and "recomputed while rendering" produce the
-- same cell and the assertion cannot tell them apart. With one, the marker's cell discriminates
-- three ways: 18,16 = predicting the head of the queue, 38,16 = recomputing at render time,
-- 28,16 = correct.

local DeadlineSeconds = 60
local FirstWaypoint = { X = 18, Y = 16 }
local DropWaypoint = { X = 28, Y = 16 }
local AppendedWaypoint = { X = 38, Y = 16 }
local StartCell = { X = 8, Y = 16 }

-- The truck stops on the ordered cell on clear ground, but a wheeled vehicle settling against
-- a lane bias can finish a cell or two off. Three cells absorbs that and still leaves a
-- seventeen-cell gap to the immediate-drop answer.
local ToleranceCells = 3

-- ~6s: far enough in that the truck has visibly left its start cell, early enough that it is
-- still short of the FIRST waypoint, so both markers and the deploy icon are ahead of it in frame.
local ScreenshotTicks = 100

local function FindCache()
	local found = nil
	Utils.Do(Map.ActorsInWorld, function(a)
		if found == nil and a.Type == "supplycache" then
			found = a
		end
	end)

	return found
end

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

-- Set by the marker check below; read by the verdict poller. The marker only exists while the
-- UnloadSupplyCache activity is still queued, so it has to be sampled DURING the drive, whereas
-- the crate's cell can only be read after the drop. Two beats, one verdict: the poller refuses
-- to pass until the marker has been sampled, so neither assertion can go unrun.
local markerChecked = false
local markerFailure = nil

local function CheckDeployMarker()
	markerChecked = true

	local markers = Test.GetTargetLineCells(Truck, true)
	local waypoints = Test.GetTargetLineCells(Truck, false)

	if #markers ~= 1 then
		markerFailure = "expected exactly one target-line tile node (the queued deploy's crate marker), got "
			.. #markers .. " " .. CellsToString(markers)
			.. "; waypoint nodes were " .. CellsToString(waypoints)
		return
	end

	local m = markers[1]
	if m.X == DropWaypoint.X and m.Y == DropWaypoint.Y then
		return
	end

	local diagnosis = "an unexpected cell"
	if m.X == FirstWaypoint.X and m.Y == FirstWaypoint.Y then
		diagnosis = "the FIRST waypoint — the prediction is taking the head of the queue, not its tail"
	elseif m.X == AppendedWaypoint.X and m.Y == AppendedWaypoint.Y then
		diagnosis = "the waypoint appended AFTER the deploy — the prediction is being recomputed as the "
			.. "queue changes instead of captured at issue time, so it promises a cell the truck only "
			.. "reaches once the crate is already on the ground"
	elseif m.X == StartCell.X and m.Y == StartCell.Y then
		diagnosis = "the truck's issue-time cell — the prediction is not seeing the queued waypoints at all"
	end

	markerFailure = "the queued deploy's crate marker is drawn on " .. diagnosis .. ". It sits at "
		.. m.X .. "," .. m.Y .. " but the deploy will fire at the last waypoint as of issue time, "
		.. DropWaypoint.X .. "," .. DropWaypoint.Y
		.. "; waypoint nodes were " .. CellsToString(waypoints)
end

WorldLoaded = function()
	-- Frame the whole lane rather than the truck: the picture that matters is the truck, both
	-- waypoint markers and the deploy marker on the far one, all in one shot.
	Camera.Position = WPos.New(19 * 1024, 16 * 1024, 0)

	-- Target lines default to Manual, i.e. only while a modifier key is physically held. A player
	-- queueing orders is holding Shift and so always sees them; a script cannot hold anything.
	Test.ShowTargetLinesAlways()
	TestHarness.Select(Truck)

	-- Exactly what the player does: click a waypoint, shift-click a second, shift-press deploy.
	Test.IssueMove(Truck, CPos.New(FirstWaypoint.X, FirstWaypoint.Y), false, false)
	Test.IssueMove(Truck, CPos.New(DropWaypoint.X, DropWaypoint.Y), false, true)
	Test.IssueDeploy(Truck, true)

	-- ...and then keeps shift-clicking. This one is queued AFTER the deploy, so it runs after the
	-- drop and must not move the marker. See the header note.
	Test.IssueMove(Truck, CPos.New(AppendedWaypoint.X, AppendedWaypoint.Y), false, true)

	UserInterface.SetMissionText(
		"QUEUED DEPLOY: truck -> waypoint 18,16 -> waypoint 28,16, unload, then -> 38,16. "
		.. "The crate marker should sit on 28,16 (where the deploy was queued), not on 38,16.")

	-- Mid-drive, with both waypoints still ahead of the truck. Re-select first: target lines fade
	-- 2.4s after the last ShowTargetLines, and the orders were issued at tick one.
	Trigger.AfterDelay(ScreenshotTicks, function()
		if Truck.IsDead then
			return
		end

		-- Sample the marker at this beat, not at the screenshot's: the shot is the human-readable
		-- record of the same instant the assertion measures, and they must not be able to drift.
		CheckDeployMarker()

		-- At min zoom the whole 66-cell map fits the window and a one-cell marker is a few pixels
		-- across — legible to the engine, useless in a screenshot. 2.5x still frames the truck and
		-- both waypoints with room to spare.
		Test.SetZoom(2.5)
		Camera.Position = WPos.New(21 * 1024, 16 * 1024, 0)

		TestHarness.Select(Truck)
		TestHarness.Screenshot("deploy-queue-AFTER-icon-shown-on-waypoint",
			"expects: truck mid-lane with a target line running through the waypoint markers at 18,16 "
			.. "and 28,16 on to 38,16, and a ghosted supply-crate icon drawn on the 28,16 marker only. "
			.. "The crate icon must not sit on the truck, must not sit on 38,16, and must not hide the line.")
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if markerFailure ~= nil then
			return "fail: " .. markerFailure
		end

		local cache = FindCache()
		if cache == nil then
			return false
		end

		-- The crate landed. Refuse to call that a pass until the marker has actually been looked at,
		-- so a mistimed sample degrades to a timeout rather than to a silently half-run test.
		if not markerChecked then
			return "fail: the crate landed before the deploy marker was ever sampled — the marker "
				.. "assertion did not run, so this pass would have meant nothing"
		end

		local dx = Abs(cache.Location.X - DropWaypoint.X)
		local dy = Abs(cache.Location.Y - DropWaypoint.Y)
		if dx <= ToleranceCells and dy <= ToleranceCells then
			return true
		end

		return "fail: the queued deploy did not wait for the waypoints — crate landed at "
			.. cache.Location.X .. "," .. cache.Location.Y .. " (truck was issued the order at "
			.. StartCell.X .. "," .. StartCell.Y .. "); it should have landed at the last waypoint "
			.. DropWaypoint.X .. "," .. DropWaypoint.Y
	end, "No supplycache was ever dropped: the queued deploy was swallowed entirely")
end
