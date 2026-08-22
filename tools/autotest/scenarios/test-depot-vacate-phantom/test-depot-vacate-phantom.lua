-- AUTO TEST: the "hidden extra order" the player reported — a vehicle that finishes
-- servicing at the Logistics Centre shoves itself off the dock cell with no order behind it.
--
-- The chain, all of it static-verified (WORKSPACE/DISCOVERIES.md, 2026-08-22):
--   1. LOGISTICSCENTER is Footprint: =+= +++ =+= (structures.yaml:366). Its centre cell is
--      '+' = FootprintCellType.OccupiedPassableTransitOnly (Building.cs:26).
--   2. Locomotor.CanStayInCell (Locomotor.cs:368-374) is PURELY !CellFlag.HasTransitOnlyActor,
--      and that flag is set only from a Building's '+' cells (Locomotor.cs:565-569). So the
--      dock cell is passable but NOT stayable.
--   3. Resupply.cs:274 docks a Repairable unit on the host CENTRE via MoveOntoTarget ->
--      LocalMoveIntoTarget, which contains no CanStayInCell test anywhere — it drives the unit
--      in with raw SetCenterPosition. Its own comment concedes the point: "HACK: Repairable
--      needs the actor to move to host center."
--   4. Servicing ends, nothing is queued, the unit goes idle ON that cell, and
--      Mobile.OnBecomingIdle (Mobile.cs:945) fires the correction the player sees as a phantom
--      order. Its comment is the other half of the same argument: "HACK: activities should be
--      making sure that this can't happen in the first place!"
--
-- Ordinary move orders cannot produce this: they pass evaluateNearestMovableCell and are
-- rewritten through Mobile.NearestMoveableCell, which filters on CanStayInCell. Docking is the
-- path that skips it, which is why this scenario resupplies rather than right-clicking.

local DeadlineSeconds = 45
local SettleTicks = 30 -- the specified observation gap
local DamagedPercent = 30 -- low enough that RepairsUnits has real work to do

-- LOGISTICSCENTER placed at 32,16 with Dimensions 3,3 covers 32..34 x 16..18.
local DepotMinX, DepotMaxX = 32, 34
local DepotMinY, DepotMaxY = 16, 18

local dockCell = nil -- the FIRST cell seen on the depot footprint, i.e. where it docked
local completedCell = nil -- the tank's cell on the tick servicing finished
local settledCell = nil -- its cell SettleTicks later, with no order issued in between
local movingLineCells = nil -- target lines drawn for the tank while that move runs

local function OnDepot(c)
	return c ~= nil
		and c.X >= DepotMinX and c.X <= DepotMaxX
		and c.Y >= DepotMinY and c.Y <= DepotMaxY
end

local function Serviced()
	return Tank.Health == Tank.MaxHealth
		and Tank.AmmoCount("primary-ammo") == Tank.MaximumAmmoCount("primary-ammo")
end

WorldLoaded = function()
	TestHarness.FocusBetween(Tank, Depot)
	TestHarness.Select(Tank)

	-- Render order lines so a screenshot of this run is meaningful. It does NOT affect
	-- Test.GetTargetLineCells below, which walks the activity queue rather than the renderer.
	Test.ShowTargetLinesAlways()

	-- Give the depot actual work, BEFORE the first assert tick. Without this the tank is
	-- already full, Serviced() is true at tick 0, and the test would capture the start cell
	-- and verdict on nothing.
	-- math.floor, not `//`: OpenRA scripts run on Lua 5.1, where `//` is a syntax error.
	Tank.Health = math.floor(Tank.MaxHealth * DamagedPercent / 100)
	Tank.Reload("primary-ammo", -Tank.MaximumAmmoCount("primary-ammo"))

	-- The real RESUPPLY command-bar order (TestGlobal.cs:538). This is a genuine player-issued
	-- errand — the point of the test is that the UNORDERED move comes AFTER it completes.
	Test.IssueResupply(Tank)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Tank.IsDead then return "fail: the tank died before it finished servicing" end

		-- Latch the docking cell the first tick it is seen on the footprint, and compare against
		-- THIS rather than against completedCell. Serviced() is a health/ammo poll, and if repair
		-- happens to top out a few ticks before Resupply's activity actually ends, the bounce could
		-- already be under way when it first reads full — comparing completion-cell to settle-cell
		-- would then quietly measure nothing and report a false negative. The dock cell cannot drift.
		if dockCell == nil and OnDepot(Tank.Location) then
			dockCell = Tank.Location
		end

		if completedCell == nil then
			if not Serviced() then return false end

			completedCell = Tank.Location

			-- Guard the guard. If the tank were healed anywhere other than on the depot
			-- footprint, a pathing failure would be indistinguishable from the bounce and
			-- this run would be reporting on something else entirely.
			if not OnDepot(completedCell) then
				return "fail: servicing finished at (" .. completedCell.X .. "," .. completedCell.Y ..
					"), which is off the depot footprint 32..34 x 16..18 — the tank never docked, " ..
					"so this run says nothing about the transit-only bounce"
			end

			Trigger.AfterDelay(SettleTicks, function()
				if not Tank.IsDead then
					settledCell = Tank.Location
					movingLineCells = Test.GetTargetLineCells(Tank, false)
				end
			end)

			return false
		end

		if settledCell == nil then return false end

		if settledCell.X == dockCell.X and settledCell.Y == dockCell.Y then
			return "fail: the tank was still on its dock cell (" .. dockCell.X .. "," .. dockCell.Y ..
				") " .. SettleTicks .. " ticks after servicing finished at (" .. completedCell.X ..
				"," .. completedCell.Y .. "). No unordered move occurred, so either that cell is " ..
				"stayable after all or OnBecomingIdle never fired"
		end

		-- Second claim, and the one that decides whether waypoint markers would cover this:
		-- Mobile.cs:946 queues the correction with NO targetLineColor, and Move.TargetLineNodes
		-- (Move.cs:450-455) yields nothing without one. A marker system keyed on target lines
		-- therefore cannot see this move. If this branch ever fires, that premise is wrong and
		-- the legibility half of the fix is already solved elsewhere.
		if movingLineCells ~= nil and #movingLineCells > 0 then
			return "fail: the tank DID vacate unordered (" .. dockCell.X .. "," .. dockCell.Y ..
				") -> (" .. settledCell.X .. "," .. settledCell.Y .. "), but the move drew " ..
				#movingLineCells .. " target-line node(s) — it is already legible, contradicting " ..
				"Mobile.cs:946 passing no targetLineColor"
		end

		return true
	end, "The tank never finished servicing at the depot within " .. DeadlineSeconds .. "s")
end
