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

local DeadlineSeconds = 60
local FirstWaypoint = { X = 18, Y = 16 }
local DropWaypoint = { X = 28, Y = 16 }
local StartCell = { X = 8, Y = 16 }

-- The truck stops on the ordered cell on clear ground, but a wheeled vehicle settling against
-- a lane bias can finish a cell or two off. Three cells absorbs that and still leaves a
-- seventeen-cell gap to the immediate-drop answer.
local ToleranceCells = 3

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

WorldLoaded = function()
	TestHarness.FocusBetween(Truck, OwnSR)
	TestHarness.Select(Truck)

	-- Exactly what the player does: click a waypoint, shift-click a second, shift-press deploy.
	Test.IssueMove(Truck, CPos.New(FirstWaypoint.X, FirstWaypoint.Y), false, false)
	Test.IssueMove(Truck, CPos.New(DropWaypoint.X, DropWaypoint.Y), false, true)
	Test.IssueDeploy(Truck, true)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		local cache = FindCache()
		if cache == nil then
			return false
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
