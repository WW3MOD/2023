-- A ground unit ordered to evacuate must leave through ITS OWN SIDE of the map -- the border
-- its reinforcements arrive from -- and not through whichever wall it happens to be standing
-- nearest, which on nine of the ten shipped maps was the ENEMY's back border.
--
-- =====================================================================================
-- WHAT THIS GRADES, IN ONE SENTENCE
-- =====================================================================================
-- The single cell RotateToEdge.ChooseEdgeCell resolves for a ground unit, read back off the
-- activity queue through Test.GetTargetLineCells. Three origins are geometrically separated
-- on three different walls (full derivation in map.yaml), so the observed cell names the
-- origin the engine used, with no inference:
--
--     1,16  WEST   -> resolved from the owner's own Supply Route. THE FIX.
--     64,16 EAST   -> resolved from `self.Location`. THE PRE-FIX BEHAVIOUR: the border
--                     behind the enemy, six cells away, which is the whole defect.
--     55,1  NORTH  -> resolved from the NEAREST Supply Route regardless of owner, i.e. the
--                     `IsAlliedWith` filter in FriendlyEvacuationOrigin is not doing its job.
--
-- WHAT IT DOES NOT GRADE. It says nothing about whether the longer drive is SURVIVABLE, and
-- must never be read that way. The exit resolving correctly and the unit reaching it are two
-- different claims; the second one is a balance question that no single-unit scenario with
-- nothing shooting can answer. It also does not discriminate the owner's own Supply Route
-- from an ALLIED one, because there is no ally on this map -- see description.txt.
--
--     ./tools/autotest/run-test.sh test-evac-exits-own-side
--
-- WHY THE ASSERTION READS THE NODE RATHER THAN WATCHING THE UNIT ARRIVE. Under the fix the
-- drive is 57 cells, roughly 50 s of game time at the Abrams' Speed 70 and the mod's 60 ms
-- Timestep. Waiting for arrival would make a ~900-tick run out of a question that is settled
-- the instant the order is accepted, and would fold "did it pick the right exit" together
-- with "did it survive the trip". The node is the thing the change controls.
--
-- EVACUATE IS NOT A LUA PROPERTY. There is nothing named Evacuate in the Lua bindings; the
-- only way to issue one is through the command bar, so this selects the tank and presses the
-- hotkey exactly as test-evac-queued-line does. A press the command bar did not consume is a
-- command-bar regression rather than an edge-choice one, and is reported as a SKIP.

local StartCell = { X = 58, Y = 16 }

-- The USA Supply Route, and the origin the fix resolves from.
local OwnSRCell = { X = 6, Y = 16 }

-- Resolved from OwnSR at 6,16 over the perimeter of Bounds (1,1,64,32): 1,16 at 25 beats
-- 6,1 at 225, 6,32 at 256 and 64,16 at 3364. Strictly unique, so no tie-break and no
-- floored-sqrt regression in Map.ChooseClosestMatchingEdgeCell's sort key can move it.
local ExpectedEdgeCell = { X = 1, Y = 16 }

-- Resolved from the UNIT at 58,16: 64,16 at 36 beats 58,1 at 225 and 58,32 at 256.
local PreFixEdgeCell = { X = 64, Y = 16 }

-- Resolved from the Russia Supply Route at 55,3: 55,1 at 4 beats 64,3 at 81. Reached only if
-- FriendlyEvacuationOrigin ranks Supply Routes without testing the owner relationship.
local AnySRRefEdgeCell = { X = 55, Y = 1 }

local SelectTick = 5

-- A later tick than the selection, so the command bar's selection-hash cache has certainly
-- refreshed before it reads evacuateDisabled. Load-bearing in
-- test-evac-queued-after-waypoints and copied from there.
local PressTick = 15

-- Late enough for the order to have been accepted and the activity queued, early enough that
-- the tank is still nowhere near any border.
local AssertTick = 30

-- Roughly 14 s of game time after the order. Far too early for a 57-cell drive to finish, and
-- that is the point: this only asks which DIRECTION the tank actually went, as a guard
-- against a correct node the mover ignores.
local DirectionTick = 250

local VerdictTick = 270

local pressConsumed = nil
local assertRun = false
local failReason = nil
local skipReason = nil
local observedCells = nil
local observedCellAtAssert = nil
local observedXAtDirection = nil

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

local function AssertEdgeCell()
	assertRun = true

	local cells = Test.GetTargetLineCells(Runner, false)
	observedCells = CellsToString(cells)
	observedCellAtAssert = Runner.Location.X .. "," .. Runner.Location.Y

	print("[evac-own-side] at tick " .. AssertTick .. " Runner is at " .. observedCellAtAssert
		.. " and its target-line nodes are " .. observedCells)

	if #cells == 0 then
		failReason = "the tank has NO target-line nodes at all while standing at "
			.. observedCellAtAssert .. " -- the evacuation was never accepted, so there is no "
			.. "edge choice to grade. Activity chain: " .. Test.ActivityChain(Runner)
		return
	end

	-- An immediate (unqueued) Evacuate should contribute exactly one node. More than one means
	-- something else is also in the queue and the tail may not be the evacuation's cell, so
	-- refuse to attribute it rather than grade the wrong node.
	if #cells ~= 1 then
		skipReason = "NO VERDICT: expected exactly ONE target-line node (the evacuation's edge "
			.. "cell) but the chain is " .. observedCells .. ". Something other than the "
			.. "evacuation is in the activity queue, so the tail node cannot be attributed to "
			.. "RotateToEdge. Activity chain: " .. Test.ActivityChain(Runner)
		return
	end

	local edge = cells[1]
	if Same(edge, ExpectedEdgeCell.X, ExpectedEdgeCell.Y) then
		return
	end

	if Same(edge, PreFixEdgeCell.X, PreFixEdgeCell.Y) then
		failReason = "THE BUG: the evacuation exit is " .. edge.X .. "," .. edge.Y
			.. ", the EAST wall -- the border six cells behind the tank, which on this map is the "
			.. "enemy's side. That is Map.ChooseClosestMatchingEdgeCell sorted from the UNIT's "
			.. "own position, i.e. RotateToEdge's ground branch still reads `?? self.Location`. "
			.. "The owner's Supply Route is at " .. OwnSRCell.X .. "," .. OwnSRCell.Y
			.. " and the exit resolved from it is " .. ExpectedEdgeCell.X .. ","
			.. ExpectedEdgeCell.Y .. ", 63 cells away on the opposite wall"
		return
	end

	if Same(edge, AnySRRefEdgeCell.X, AnySRRefEdgeCell.Y) then
		failReason = "the evacuation exit is " .. edge.X .. "," .. edge.Y
			.. ", the NORTH wall directly above the RUSSIA Supply Route at 55,3. The search is "
			.. "anchoring on the nearest Supply Route of ANY owner: EnemySR is 13.3 cells from "
			.. "the tank against OwnSR's 52, so it wins the distance ranking and only the "
			.. "relationship test excludes it. FriendlyEvacuationOrigin's "
			.. "`self.Owner.IsAlliedWith(a.Owner)` filter is missing, inverted, or being applied "
			.. "after the MinByOrDefault instead of before it"
		return
	end

	if edge.X == ExpectedEdgeCell.X then
		failReason = "the evacuation exit is on the CORRECT west wall but the wrong row: "
			.. edge.X .. "," .. edge.Y .. " against " .. ExpectedEdgeCell.X .. ","
			.. ExpectedEdgeCell.Y .. ". The owner-side anchor is working and the feature under "
			.. "test is fine; what is broken is the tie-break inside "
			.. "Map.ChooseClosestMatchingEdgeCell. Sorting the perimeter on CVec.Length floors "
			.. "the distance and merges rows that are not equidistant into one tie, which the "
			.. "stable sort then resolves by enumeration order. Check that the sort key there is "
			.. "still LengthSquared"
		return
	end

	failReason = "the evacuation exit is " .. edge.X .. "," .. edge.Y
		.. ", which matches none of the three derived origins: " .. ExpectedEdgeCell.X .. ","
		.. ExpectedEdgeCell.Y .. " for the owner's Supply Route (the fix), " .. PreFixEdgeCell.X
		.. "," .. PreFixEdgeCell.Y .. " for the unit's own position (pre-fix), "
		.. AnySRRefEdgeCell.X .. "," .. AnySRRefEdgeCell.Y .. " for the nearest Supply Route of "
		.. "any owner. Tank at " .. observedCellAtAssert .. ", activity chain: "
		.. Test.ActivityChain(Runner)
end

local function CheckDirection()
	if Runner.IsDead then
		return
	end

	observedXAtDirection = Runner.Location.X
	print("[evac-own-side] at tick " .. DirectionTick .. " Runner is at "
		.. Runner.Location.X .. "," .. Runner.Location.Y)

	-- Only an UNAMBIGUOUS contradiction fails here. Not having got far enough west is not
	-- graded at all: turn time and pathing make "how far by tick 250" a soft number, and the
	-- node assertion above is the real verdict. Having gone EAST is not soft -- it means the
	-- tank is driving to a wall the node did not name.
	if failReason == nil and observedXAtDirection > StartCell.X then
		failReason = "the edge node was correct but the tank drove the wrong way: it started at "
			.. StartCell.X .. "," .. StartCell.Y .. " and by tick " .. DirectionTick
			.. " it was at " .. Runner.Location.X .. "," .. Runner.Location.Y
			.. ", EAST of where it began, while its evacuation node names the west wall at "
			.. ExpectedEdgeCell.X .. "," .. ExpectedEdgeCell.Y .. ". The destination and the "
			.. "movement disagree. Activity chain: " .. Test.ActivityChain(Runner)
	end
end

local function Verdict()
	local trailer = " Nodes at tick " .. AssertTick .. ": " .. tostring(observedCells)
		.. "; tank was at " .. tostring(observedCellAtAssert)
		.. "; x at tick " .. DirectionTick .. " was " .. tostring(observedXAtDirection)
		.. "; Evacuate consumed=" .. tostring(pressConsumed) .. "."

	if pressConsumed == false then
		Test.Skip("NO VERDICT: the Evacuate press was not consumed by the command bar, so no "
			.. "evacuation was ever ordered and there was no edge choice to grade. That is a "
			.. "command-bar regression rather than an edge-choice one." .. trailer)
		return
	end

	if not assertRun then
		Test.Skip("NO VERDICT: the edge-cell check never ran." .. trailer)
		return
	end

	if skipReason ~= nil then
		Test.Skip(skipReason .. "." .. trailer)
		return
	end

	if failReason ~= nil then
		Test.Fail(failReason .. "." .. trailer)
		return
	end

	Test.Pass("the ground evacuation exits through the OWNER'S OWN SIDE: the tank stood at "
		.. StartCell.X .. "," .. StartCell.Y .. ", six cells from the east wall and 52 from its "
		.. "own Supply Route at " .. OwnSRCell.X .. "," .. OwnSRCell.Y .. ", and its evacuation "
		.. "resolved to " .. ExpectedEdgeCell.X .. "," .. ExpectedEdgeCell.Y .. " on the WEST "
		.. "wall -- the exit derived from the Supply Route, not from the unit, and not from the "
		.. "nearer ENEMY Supply Route at 55,3. THIS DOES NOT CERTIFY THAT THE DRIVE IS "
		.. "SURVIVABLE: nothing on this map shoots, and the run ends long before the 57-cell "
		.. "trip would finish. It certifies the destination only." .. trailer)
end

WorldLoaded = function()
	if Runner == nil then
		Test.Skip("SETUP FAULT: map actor Runner did not resolve, so there is nothing to order")
		return
	end

	if OwnSR == nil or OwnSR.IsDead then
		Test.Skip("SETUP FAULT: map actor OwnSR did not resolve, so there is no owner-side "
			.. "Supply Route for the evacuation to anchor on and the whole geometry is void")
		return
	end

	if EnemySR == nil or EnemySR.IsDead then
		Test.Skip("SETUP FAULT: map actor EnemySR did not resolve, so the ally-filter negative "
			.. "control is absent and a pass could not distinguish `nearest friendly Supply "
			.. "Route` from `nearest Supply Route`")
		return
	end

	UserInterface.SetMissionText(
		"OWN-SIDE EVAC: tank at 58,16 is 6 cells from the EAST wall and 52 from its own Supply "
		.. "Route at 6,16. Its evacuation must resolve to 1,16 on the WEST wall.")

	Trigger.AfterDelay(SelectTick, function()
		Test.SelectActors({ Runner })
	end)

	Trigger.AfterDelay(PressTick, function()
		pressConsumed = Test.PressHotkey("Evacuate", false)
		print("[evac-own-side] Evacuate consumed=" .. tostring(pressConsumed))
	end)

	Trigger.AfterDelay(AssertTick, AssertEdgeCell)
	Trigger.AfterDelay(DirectionTick, CheckDirection)
	Trigger.AfterDelay(VerdictTick, Verdict)
end
