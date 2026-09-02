-- A queued evacuation must COMMIT ITS DESTINATION WHEN IT IS QUEUED, not when it starts.
--
-- =====================================================================================
-- THIS ONE GRADES -- BUT READ WHAT IT GRADES, BECAUSE IT IS HALF THE FEATURE
-- =====================================================================================
-- A PASS here means exactly one thing: while the tank is still driving its FIRST leg, its
-- activity queue already yields a target-line node pointing at the map edge, and that node
-- names the cell resolved from where the tank stood at ISSUE time. That is precisely what
-- adfb0f2f changed -- RotateToEdge.ChooseEdgeCell moved out of OnFirstRun and into the
-- constructor, because edgeCell is the only input to TargetLineNodes and it was null until
-- the activity became current, so the queued evac leg drew nothing until the tank reached
-- the waypoint before it ("it shows up only at the last waypoint").
--
-- A PASS DOES NOT MEAN THE LINE IS ON SCREEN, AND MUST NOT BE READ THAT WAY.
-- Test.GetTargetLineCells walks the activity queue. It does not consult
-- DrawLineToTarget.ShouldRender, the selection, the TargetLines setting, or the renderer at
-- all. Every one of those can be broken with this assertion still green. The ONLY evidence
-- about rendering is the PNG this run also produces, and the PNG is UNGRADED -- a human
-- reads it. Both halves are needed and neither substitutes for the other.
--
--     ./tools/autotest/run-test.sh --size 1600x900 test-evac-queued-line
--
-- DO NOT PASS Test.KeepRenderPlayer=true. Unlike test-evac-refund-indicator, nothing here
-- reads World.RenderPlayer: DrawLineToTarget.ShouldRender gates on
-- self.Owner.IsAlliedWith(self.World.LocalPlayer) (DrawLineToTarget.cs:80) and
-- LineTargetExts.ShowTargetLines on self.Owner == self.World.LocalPlayer, and LocalPlayer is
-- untouched by TestModeLogic. If passing the flag changes the outcome, that is itself a
-- finding and should be reported rather than absorbed.
--
-- =====================================================================================
-- WHY NO EXISTING CAPTURE COULD SHOW THIS, WHICH IS WHY THE SELECTION LOOP EXISTS
-- =====================================================================================
-- test-evac-queued-after-waypoints does take a screenshot of a queued evacuation, and that
-- PNG can never show a target line no matter what the code does. Two independent reasons,
-- both in DrawLineToTarget:
--
--   * IRenderAnnotationsWhenSelected only runs for a SELECTED actor, and by the time that
--     scenario shoots, selection has long since moved to a different unit; and
--   * ShouldRender (DrawLineToTarget.cs:96) needs `force || Game.RunTime <= lifetime ||
--     HasAutomaticNode`. `force` is a physically-held Shift key, which no script can
--     arrange. HasAutomaticNode is false because a player-issued Move is not an automatic
--     order. So the only live term is `lifetime`, which is set to Game.RunTime + 2400ms and
--     is RE-ARMED ONLY BY INotifySelected.Selected.
--
-- So a capture of this feature has to hold the selection AND keep re-arming that window
-- through the moment of the shot. Test.SelectActors is what does it: Selection.Combine
-- fires INotifySelected.Selected over its whole newSelection list unconditionally, whether
-- or not the actor was already selected (Selection.cs:120-123), so re-selecting the same
-- tank re-arms lifetime every time.
--
-- THE 2400 ms IS REAL TIME, NOT TICKS, AND THAT CUTS THE SAFE WAY. At the mod's Timestep of
-- 60 ms it is about 40 ticks; re-selecting every 3 ticks is roughly 180 ms, a 13x margin.
-- The margin only erodes if the game runs SLOWER than nominal -- a loaded machine, a
-- reduced speed setting -- and it would take 480 ms per tick, an eight-fold slowdown, to
-- break it. A game running FASTER than nominal makes the margin larger, not smaller, so the
-- usual "the harness tick rate is not what you think" trap (TestHarness.TicksPerSecond is
-- 25 while the mod runs at 16.67) cannot bite here in the dangerous direction.
--
-- THE SELECTION LOOP DELIBERATELY RUNS THROUGH THE CAPTURE. The standing rule is that a
-- capture is one frame late and nothing may touch the world between arming it and the
-- pixels being sampled. This is the one case where that is inverted: the only state the
-- loop changes is "the subject is selected and its order-line window is open", which is
-- exactly the state being photographed. Stopping the loop before the shot would risk
-- photographing the line going dark.

local StartCell = { X = 8, Y = 16 }
local Waypoints = {
	{ X = 18, Y = 16 },
	{ X = 28, Y = 16 },
	{ X = 38, Y = 16 },
}

-- Resolved from the ISSUE-time position 8,16 over the perimeter of Bounds (1,1,64,32):
-- 1,16 at 7 cells beats 8,1 at 15 and 8,32 at 16. See map.yaml for the full derivation and
-- for what a LATE resolution would have produced instead.
--
-- 1,16 is only reachable because ChooseClosestMatchingEdgeCell sorts on LengthSquared. It
-- used to sort on CVec.Length, which is a FLOORED integer sqrt, so 1,13 through 1,19 all
-- scored 7 and the stable sort handed the win to the lowest row — this test's first run
-- returned 1,13. A regression to a floored key lands on the WEST edge with the right X and
-- a row up to 3 north, which is what WrongRowOnCorrectEdge names below.
local ExpectedEdgeCell = { X = 1, Y = 16 }
local LateResolutionCell = { X = 38, Y = 1 }

-- Frame the whole 37-cell span from the west edge to the last waypoint. At 1600x900 and
-- zoom 1 that is 66 cells across, so centring on 20 puts x = -13..53 in view: the map's
-- west boundary, every waypoint, and the tank.
local CameraCellX = 20
local CameraCellY = 16
local TargetZoom = 1.0

local IssueTick = 5

-- The press must land on a LATER tick than the selection, so the command bar's
-- selection-hash cache has certainly refreshed before it reads evacuateDisabled. Copied
-- from test-evac-queued-after-waypoints, where it is load-bearing.
local PressTick = 15

local ReselectFromTick = 16
local ReselectEveryTicks = 3
local ReselectUntilTick = 60

-- Late enough that the queue has certainly settled after the shifted press, early enough
-- that an Abrams starting at x=8 cannot have reached the first waypoint at x=18. If it
-- somehow has, the run says so and SKIPS rather than grading -- see AssertNodes.
local AssertTick = 30

local ShotTick = 45
local VerdictTick = 70

local pressConsumed = nil
local assertRun = false
local failReason = nil
local skipReason = nil
local observedCells = nil
local observedCellAtAssert = nil
local selectedAtShot = nil

local function CellsToString(cells)
	local s = ""
	for i = 1, #cells do
		if i > 1 then s = s .. " " end
		s = s .. cells[i].X .. "," .. cells[i].Y
	end
	return "[" .. s .. "]"
end

local function Same(cell, x, y)
	return cell.X == x and cell.Y == y
end

local function AssertNodes()
	assertRun = true

	local cells = Test.GetTargetLineCells(Runner, false)
	observedCells = CellsToString(cells)
	observedCellAtAssert = Runner.Location.X .. "," .. Runner.Location.Y

	print("[evac-line] at tick " .. AssertTick .. " Runner is at " .. observedCellAtAssert
		.. " and its target-line nodes are " .. observedCells)

	-- INSTRUMENT FAULT, NOT A VERDICT. If the tank has already eaten the first waypoint the
	-- head node is legitimately gone, and the remaining three would be indistinguishable in
	-- COUNT from the three the pre-fix build produced. Reading that as a failure would be a
	-- false accusation, so it skips instead.
	if #cells > 0 and not Same(cells[1], Waypoints[1].X, Waypoints[1].Y) then
		if Runner.Location.X >= Waypoints[1].X then
			skipReason = "NO VERDICT: by tick " .. AssertTick .. " the tank had already reached "
				.. observedCellAtAssert .. ", past the first waypoint at "
				.. Waypoints[1].X .. "," .. Waypoints[1].Y .. ", so the head node had legitimately "
				.. "been consumed and \"the leg is drawn while still on leg one\" could not be "
				.. "tested. Nodes were " .. observedCells .. ". Lower AssertTick"
			return
		end
	end

	if #cells == 0 then
		failReason = "the tank has NO target-line nodes at all while standing at "
			.. observedCellAtAssert .. " -- it is idle, so neither the waypoints nor the queued "
			.. "evacuation were accepted. Activity chain: " .. Test.ActivityChain(Runner)
		return
	end

	if #cells == #Waypoints then
		local tail = cells[#cells]
		if Same(tail, Waypoints[#Waypoints].X, Waypoints[#Waypoints].Y) then
			failReason = "THE BUG: the tank is still on its first leg at " .. observedCellAtAssert
				.. " and its target-line nodes are only the three move waypoints, " .. observedCells
				.. " -- the queued evacuation contributes NO node. RotateToEdge.TargetLineNodes "
				.. "yields nothing while edgeCell is null, so this is edgeCell being resolved in "
				.. "OnFirstRun (when the activity becomes current) rather than in the constructor "
				.. "(when the order is queued). That is exactly the defect adfb0f2f fixed"
			return
		end
	end

	if #cells ~= #Waypoints + 1 then
		failReason = "expected " .. (#Waypoints + 1) .. " target-line nodes (three move waypoints "
			.. "then the evacuation's edge cell) but got " .. #cells .. ": " .. observedCells
			.. ". Tank at " .. observedCellAtAssert .. ", activity chain: " .. Test.ActivityChain(Runner)
		return
	end

	for i = 1, #Waypoints do
		if not Same(cells[i], Waypoints[i].X, Waypoints[i].Y) then
			failReason = "node " .. i .. " is " .. cells[i].X .. "," .. cells[i].Y .. " but waypoint "
				.. i .. " was ordered at " .. Waypoints[i].X .. "," .. Waypoints[i].Y
				.. ". Full chain " .. observedCells .. "; the evacuation leg is not the thing at "
				.. "fault here, the move queue is"
			return
		end
	end

	local edge = cells[#cells]
	if Same(edge, ExpectedEdgeCell.X, ExpectedEdgeCell.Y) then
		return
	end

	local diagnosis = "an unexpected cell"
	if Same(edge, LateResolutionCell.X, LateResolutionCell.Y) then
		diagnosis = "the NORTH edge above the last waypoint -- which is what "
			.. "Map.ChooseClosestMatchingEdgeCell returns for a unit standing at "
			.. Waypoints[#Waypoints].X .. "," .. Waypoints[#Waypoints].Y .. ". The destination is "
			.. "being resolved from where the tank will END UP rather than from where it stood "
			.. "when the order was queued, so the line promises an exit the player never asked for"
	elseif Same(edge, StartCell.X, StartCell.Y) then
		diagnosis = "the tank's own issue-time cell -- the edge search is returning its origin, "
			.. "i.e. Map.AllEdgeCells is empty or the match predicate rejected every perimeter cell"
	elseif edge.X == ExpectedEdgeCell.X then
		diagnosis = "the CORRECT west edge but the wrong row. The issue-time reference is fine and "
			.. "the feature under test works; what is broken is the tie-break in "
			.. "Map.ChooseClosestMatchingEdgeCell. Sorting the perimeter on CVec.Length floors the "
			.. "distance, so every row within 3 of " .. ExpectedEdgeCell.Y .. " scores the same 7 and "
			.. "the stable sort returns the lowest -- 1,13. Check that the sort key there is still "
			.. "LengthSquared"
	end

	failReason = "the queued evacuation's edge node is " .. diagnosis .. ". It sits at "
		.. edge.X .. "," .. edge.Y .. " but the exit resolved from the issue-time position "
		.. StartCell.X .. "," .. StartCell.Y .. " is " .. ExpectedEdgeCell.X .. ","
		.. ExpectedEdgeCell.Y .. ". Full chain " .. observedCells
end

local function Reselect(atTick)
	if atTick > ReselectUntilTick or Runner.IsDead then
		return
	end

	Test.SelectActors({ Runner })
	Trigger.AfterDelay(ReselectEveryTicks, function() Reselect(atTick + ReselectEveryTicks) end)
end

local function TakeShot()
	selectedAtShot = Test.GetSelectedCount()
	print("[evac-line] at the shot, selection holds " .. selectedAtShot .. " actor(s)")

	TestHarness.Screenshot("evac-queued-line",
		"expects: ONE tank near the left, still short of x=18, with FOUR order legs drawn on "
		.. "its own row. Three are WHITE move legs running east: tank -> 18,16 -> 28,16 -> "
		.. "38,16. The fourth is the EVACUATION leg and is a distinct AMBER/GOLD (ARGB "
		.. "180,255,200,80), and it runs from the far waypoint at 38,16 all the way BACK WEST "
		.. "to the map's left boundary at 1,16 -- so it overlays the white legs along row 16 "
		.. "and continues past the tank to the edge. THE AMBER LEG BEING PRESENT AT ALL, WHILE "
		.. "THE TANK IS STILL ON ITS FIRST LEG, IS THE WHOLE FEATURE. If you see only three "
		.. "white legs and no amber one, the line is not rendering even though the node exists "
		.. "-- report that as a RENDERING failure, distinct from the node-chain assertion which "
		.. "is graded separately in the verdict. An amber leg running NORTH from 38,16 to the "
		.. "top edge instead would mean the destination was resolved late.")
end

local function Verdict()
	local trailer = "Nodes at tick " .. AssertTick .. ": " .. tostring(observedCells)
		.. "; tank was at " .. tostring(observedCellAtAssert)
		.. "; Shift+E consumed=" .. tostring(pressConsumed)
		.. "; selection at the shot held " .. tostring(selectedAtShot) .. " actor(s)."

	if pressConsumed == false then
		Test.Skip("NO VERDICT: the Shift+E press was not consumed by the command bar, so no "
			.. "evacuation was ever queued and there was nothing to draw. That is a command-bar "
			.. "regression rather than a target-line one -- test-evac-queued-after-waypoints is "
			.. "the scenario that grades it. " .. trailer)
		return
	end

	if not assertRun then
		Test.Skip("NO VERDICT: the node-chain check never ran. " .. trailer)
		return
	end

	if skipReason ~= nil then
		Test.Skip(skipReason .. ". " .. trailer)
		return
	end

	if failReason ~= nil then
		Test.Fail(failReason)
		return
	end

	if selectedAtShot ~= 1 then
		Test.Skip("NO VERDICT: the node chain was correct, but at the moment of the capture the "
			.. "selection held " .. tostring(selectedAtShot) .. " actors rather than 1, so "
			.. "IRenderAnnotationsWhenSelected may not have run for the tank and the PNG cannot "
			.. "be read as evidence about rendering. The re-selection loop is what maintains "
			.. "this. " .. trailer)
		return
	end

	Test.Pass("the queued evacuation's edge node is committed at ISSUE time: while the tank was "
		.. "still on its first leg at " .. tostring(observedCellAtAssert) .. " its target-line "
		.. "chain already read " .. tostring(observedCells) .. ", ending at the exit resolved from "
		.. "its issue-time cell. THIS DOES NOT CERTIFY THAT THE LINE RENDERS -- "
		.. "Test.GetTargetLineCells walks the activity queue and never consults "
		.. "DrawLineToTarget.ShouldRender, the selection or the TargetLines setting. The capture "
		.. "is the only evidence about rendering and it is ungraded; read the PNG.")
end

WorldLoaded = function()
	if Runner == nil then
		Test.Skip("SETUP FAULT: map actor Runner did not resolve, so there is nothing to order")
		return
	end

	Camera.Position = WPos.New(CameraCellX * 1024, CameraCellY * 1024, 0)

	local appliedZoom = Test.SetZoom(TargetZoom)
	print(string.format("[evac-line] camera at cell %d,%d; zoom requested %.2f applied %.2f",
		CameraCellX, CameraCellY, TargetZoom, appliedZoom))

	-- Target lines default to Manual, i.e. drawn only while a modifier key is physically held.
	-- A player queueing orders is holding Shift and so always sees them; a script cannot hold
	-- anything. Called BEFORE any selection, because DrawLineToTarget.ShowTargetLines returns
	-- early when the setting is below Automatic, so an earlier selection would arm nothing.
	Test.ShowTargetLinesAlways()

	UserInterface.SetMissionText(
		"QUEUED EVAC LINE: tank -> 18,16 -> 28,16 -> 38,16, then Shift+E. The amber evac leg "
		.. "must already run from 38,16 back to the west edge while the tank is on leg one.")

	Trigger.AfterDelay(IssueTick, function()
		-- Exactly what the player does: click a waypoint, then shift-click two more.
		Test.IssueMove(Runner, CPos.New(Waypoints[1].X, Waypoints[1].Y), false, false)
		Test.IssueMove(Runner, CPos.New(Waypoints[2].X, Waypoints[2].Y), false, true)
		Test.IssueMove(Runner, CPos.New(Waypoints[3].X, Waypoints[3].Y), false, true)
		Test.SelectActors({ Runner })
	end)

	Trigger.AfterDelay(PressTick, function()
		pressConsumed = Test.PressHotkey("Evacuate", true)
		print("[evac-line] Shift+E consumed=" .. tostring(pressConsumed))
	end)

	Trigger.AfterDelay(ReselectFromTick, function() Reselect(ReselectFromTick) end)
	Trigger.AfterDelay(AssertTick, AssertNodes)
	Trigger.AfterDelay(ShotTick, TakeShot)
	Trigger.AfterDelay(VerdictTick, Verdict)
end
