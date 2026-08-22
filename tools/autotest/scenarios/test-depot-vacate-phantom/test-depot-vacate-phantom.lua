-- AUTO TEST: the "hidden extra order" the player reported — a vehicle that finishes servicing at
-- the Logistics Centre shoves itself off the dock cell with no order behind it — AND the queueing
-- behaviour that shove exists to protect.
--
-- The chain, all static-verified (WORKSPACE/DISCOVERIES.md, 2026-08-22):
--   1. LOGISTICSCENTER is Footprint: =+= +++ =+= (structures.yaml:366). Its centre cell is
--      '+' = FootprintCellType.OccupiedPassableTransitOnly (Building.cs:26).
--   2. Locomotor.CanStayInCell (Locomotor.cs:368-374) is PURELY !CellFlag.HasTransitOnlyActor,
--      set only from a Building's '+' cells (:565-569). The dock is passable but NOT stayable.
--   3. Resupply.cs:274 docks a Repairable unit on the host CENTRE via MoveOntoTarget ->
--      MoveOntoAndTurn : MoveOnto : MoveAdjacentTo. The base picks candidates through
--      `CanStayInCell(cell) && CanEnterCell(cell)` (MoveAdjacentTo.cs:129), but MoveOnto OVERRIDES
--      that method with a single unfiltered cell — the host centre (MoveOnto.cs:41-58). The docking
--      activity overrides away the very filter its own base class applies.
--   4. Servicing ends, nothing is queued, the unit goes idle ON that cell, and
--      Mobile.OnBecomingIdle (Mobile.cs:945) issues the correction the player reads as a phantom
--      order — which is exactly what Mobile.cs:944's HACK comment is complaining about.
--
-- WHY THIS IS NOT FIXED BY MAKING THE DOCK STAYABLE, and why Tank2 is here. The bounce is
-- LOAD-BEARING: it is what keeps the dock free by construction for a docking system that has no
-- queue and no reservation (the LC carries no Reservable). With a stayable dock, Tank would park
-- forever and Tank2 would wait forever — MoveOnto.CalculatePathToTarget returns NoPath and waits
-- when the target cell is occupied, and the LC's isCloseEnough is WDist.Zero, so exact coincidence
-- with the building centre is required and there is no near-enough fallback. Tank2 passing is the
-- evidence that the shove is doing a job, not just misbehaving.
--
-- The fix that DID land is legibility, not suppression: wt/heal-legibility paints every self-issued
-- dispatch in AutomaticOrder.LineColor and exempts automatic lines from the display timeout. This
-- scenario is the only place in the suite that checks that wiring at the idle-cell correction site
-- specifically — their own verification is a colour-collision pin, not a per-site one.

local DeadlineSeconds = 60 -- TestHarness.TicksPerSecond is 25 but the mod runs Timestep 60
local SettleTicks = 30 -- (16.67 tps), so this is ~90s of wall clock. Generous on purpose.
local DamagedPercent = 30

-- LOGISTICSCENTER placed at 32,16 with Dimensions 3,3 covers 32..34 x 16..18.
local DepotMinX, DepotMaxX = 32, 34
local DepotMinY, DepotMaxY = 16, 18

local dockCell = nil -- FIRST cell seen on the depot footprint, i.e. where Tank docked
local completedCell = nil -- Tank's cell on the tick servicing finished
local settledCell = nil -- its cell SettleTicks later, with no order issued in between
local lineCells = nil -- all target-line nodes seen once the vacate move was running
local autoLineCells = nil -- the subset of those painted AutomaticOrder.LineColor

local function OnDepot(c)
	return c ~= nil
		and c.X >= DepotMinX and c.X <= DepotMaxX
		and c.Y >= DepotMinY and c.Y <= DepotMaxY
end

local function Serviced(a)
	return a.Health == a.MaxHealth
		and a.AmmoCount("primary-ammo") == a.MaximumAmmoCount("primary-ammo")
end

local function Wreck(a)
	a.Health = math.floor(a.MaxHealth * DamagedPercent / 100)
	a.Reload("primary-ammo", -a.MaximumAmmoCount("primary-ammo"))
end

WorldLoaded = function()
	TestHarness.FocusBetween(Tank, Depot)
	TestHarness.Select(Tank)

	-- Render order lines so a screenshot of this run is meaningful. Does NOT affect the
	-- Test.GetTargetLineCells readings below, which walk the activity queue, not the renderer.
	Test.ShowTargetLinesAlways()

	-- Give the depot real work, BEFORE the first assert tick. Without this both tanks are already
	-- full, Serviced() is true at tick 0, and the test would capture start cells and verdict on
	-- nothing.
	Wreck(Tank)
	Wreck(Tank2)

	-- The real RESUPPLY command-bar order (TestGlobal.cs:538). Genuine player-issued errands — the
	-- whole point is that the UNORDERED move comes after one of them completes.
	Test.IssueResupply(Tank)
	Test.IssueResupply(Tank2)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Tank.IsDead then return "fail: Tank died before it finished servicing" end
		if Tank2.IsDead then return "fail: Tank2 died before it finished servicing" end

		-- Latch the docking cell the first tick it is seen on the footprint, and compare against
		-- THIS rather than completedCell. Serviced() is a health/ammo poll, and if repair tops out
		-- a few ticks before Resupply's activity ends, the bounce could already be under way when
		-- it first reads full — comparing completion-cell to settle-cell would then measure nothing
		-- and report a false negative. The dock cell cannot drift.
		if dockCell == nil and OnDepot(Tank.Location) then
			dockCell = Tank.Location
		end

		if completedCell == nil then
			if not Serviced(Tank) then return false end

			completedCell = Tank.Location

			-- Guard the guard. If Tank were healed anywhere but on the depot footprint, a pathing
			-- failure would be indistinguishable from the bounce and this run would be reporting on
			-- something else entirely.
			if not OnDepot(completedCell) then
				return "fail: servicing finished at (" .. completedCell.X .. "," .. completedCell.Y ..
					"), off the depot footprint 32..34 x 16..18 — Tank never docked, so this run says " ..
					"nothing about the transit-only bounce"
			end

			Trigger.AfterDelay(SettleTicks, function()
				if not Tank.IsDead then settledCell = Tank.Location end
			end)

			return false
		end

		-- Latch the line nodes on the FIRST tick any exist after servicing. Reading them at the
		-- settle point instead would be a race the other way: the vacate is a one-cell move and can
		-- finish inside SettleTicks, leaving nothing to see and failing for the wrong reason.
		if lineCells == nil then
			local seen = Test.GetTargetLineCells(Tank, false)
			if #seen > 0 then
				lineCells = seen
				autoLineCells = Test.GetAutomaticTargetLineCells(Tank)
			end
		end

		if settledCell == nil then return false end

		if settledCell.X == dockCell.X and settledCell.Y == dockCell.Y then
			return "fail: Tank was still on its dock cell (" .. dockCell.X .. "," .. dockCell.Y ..
				") " .. SettleTicks .. " ticks after servicing finished at (" .. completedCell.X ..
				"," .. completedCell.Y .. "). No unordered move occurred, so either that cell is " ..
				"stayable after all or OnBecomingIdle never fired"
		end

		-- The vacate happened. Now: is it LEGIBLE? This is the cross-branch check on
		-- wt/heal-legibility's wiring at this specific site.
		if lineCells == nil then
			return "fail: Tank vacated (" .. dockCell.X .. "," .. dockCell.Y .. ") -> (" ..
				settledCell.X .. "," .. settledCell.Y .. ") but drew NO target-line node at any point. " ..
				"Mobile.cs:946 is queueing the correction without a targetLineColor again, so the move " ..
				"is invisible and the player reads it as a hidden order"
		end

		if #lineCells ~= 1 then
			return "fail: expected exactly one target-line node for the vacate move, got " .. #lineCells
		end

		if autoLineCells == nil or #autoLineCells ~= 1 then
			return "fail: the vacate move drew a line but it is NOT AutomaticOrder.LineColor (" ..
				#lineCells .. " node(s), " .. (autoLineCells and #autoLineCells or 0) .. " automatic). " ..
				"Painted in an ordinary order colour it still renders, but reads to the player as a " ..
				"command they never gave — which is the bug, not the fix"
		end

		-- Finally the load-bearing half: the dock was vacated, so the SECOND customer gets served.
		-- If this is what times out, the queueing is broken rather than the bounce.
		return Serviced(Tank2)
	end, "Tank2 never finished servicing — the dock was vacated but the next customer was not served, " ..
		"or neither tank reached the depot at all")
end
