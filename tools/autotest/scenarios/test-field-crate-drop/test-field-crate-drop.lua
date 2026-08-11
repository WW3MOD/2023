-- AUTO TEST — a supply truck must be able to drop its cache on a crop-field cell.
--
-- ww3mod tiles fields as one 1x1 Building actor per cell. Passable/PassClasses makes them
-- driveable, because Locomotor is the only thing that reads it. Every other "is this cell
-- free" test hand-rolls an ActorMap.GetActorsAt query and sees the field actor — so the
-- truck drives onto ground it is allowed to drive onto and then refuses to unload there.
--
-- Setup (map.yaml): Truck (truk, full) on bare ground at x=12; a 9x9 patch of v14 field
-- actors covering x=18..26, y=12..20. The truck is ordered to drive to 22,16 — the centre
-- of the patch — and drop, using the same DropSupplyCacheAt errand the bot issues.
--   PASS = a supplycache actor exists on/near the drop cell within the window.
--   FAIL = no crate ever appears (CanDropCache refused the field cell), or the truck died.
--
-- The truck starting OUTSIDE the patch is deliberate: reaching x=22 proves movement over
-- the field is already fine, so a failure can only be the occupancy test on arrival.

local WINDOW = 30                    -- seconds allowed for drive-out + drop
local DROP = CPos.New(22, 16)        -- centre of the field patch
local RADIUS = WDist.FromCells(4)    -- drop tolerance is 2 cells; 4 is a generous sweep

WorldLoaded = function()
	TestHarness.FocusBetween(Truck, Truck)
	TestHarness.Select(Truck)

	local dropPos = Map.CenterOfCell(DROP)

	-- The bot's own errand: drive to the cell, then drop on arrival. The bare
	-- "DropSupplyCache" order would drop unconditionally and never consult the cell.
	Test.IssueDropSupplyCacheAt(Truck, DROP)

	local reached = false
	local ticks = 0
	local deadline = (WINDOW - 2) * TestHarness.TicksPerSecond

	TestHarness.AssertWithin(WINDOW, function()
		ticks = ticks + 1

		if Truck.IsDead then
			return "fail: truck died before it could drop (reachedField=" .. tostring(reached) .. ")"
		end

		-- Record that the drive half succeeded. Without this the verdict cannot tell
		-- "never got there" (a pathing problem — this test would be measuring nothing)
		-- apart from "got there and was refused" (the occupancy bug it exists for).
		-- The engine log cannot be relied on to settle it: Logs/ is shared between
		-- worktrees, so a concurrent run in another checkout truncates it.
		if not reached and Truck.Location.X >= 20 and Truck.Location.X <= 24 then
			reached = true
		end

		local crates = Map.ActorsInCircle(dropPos, RADIUS, function(a)
			return a.Type == "supplycache"
		end)

		if #crates > 0 then return true end

		-- Self-diagnosing timeout, so the recorded verdict names the cause.
		if ticks >= deadline then
			return string.format(
				"fail: no supplycache after %ds — truck at %d,%d, reachedFieldDropCell=%s. " ..
				"reachedFieldDropCell=true means the truck drove onto the field fine and " ..
				"CanDropCache then refused the cell (the bug); false means it never arrived " ..
				"and this run measured pathing, not occupancy",
				WINDOW - 2, Truck.Location.X, Truck.Location.Y, tostring(reached))
		end

		return false
	end, string.format("no supplycache appeared within %ds of ordering a drop on a field cell", WINDOW))
end
