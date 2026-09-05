-- AUTO TEST: a vehicle that finishes servicing at the Logistics Centre leaves the dock by itself,
-- legibly, and the next customer then gets served on the same cell.
--
-- ============================================================================================
-- WHAT THIS SCENARIO USED TO BE, because the history is the argument for what it is now.
--
-- It was written against a 3x3 Centre (Footprint `=+= +++ =+=`, covering 32..34 x 16..18) whose
-- CENTRE cell was '+' — OccupiedPassableTransitOnly. Resupply docked a Repairable unit on the host
-- centre, so the tank came to rest on a cell Locomotor.CanStayInCell reports false for, and when
-- servicing ended Mobile.OnBecomingIdle issued the correction the player had reported as a phantom
-- order. The scenario's job was to prove that correction is DRAWN — painted in
-- AutomaticOrder.LineColor rather than looking like a command nobody gave.
--
-- Its header then argued, correctly for its time, that the bounce must not be "fixed" by making the
-- dock stayable: the shove was what kept the dock free by construction for a docking system with no
-- queue and no reservation (the LC carries no Reservable), and with a stayable dock the first tank
-- would park forever while the second waited forever — MoveOnto returns NoPath and waits when its
-- single target cell is occupied, and the arrival test has no near-enough fallback.
--
-- ============================================================================================
-- WHAT CHANGED, 2026-09-05. The Centre became 2x2 with `Footprint: ++ =+`, and exactly one cell —
-- the bottom-left, under the crane — is '=' and therefore STAYABLE. It has to be: a client is now
-- sent to that specific cell by ResupplyDock and the arrival test is cell equality, so a cell it
-- could not stand on could never be arrived at. That removes the accidental shove.
--
-- The old header's warning was right and was NOT overtaken by the resize — so the shove was
-- replaced rather than dropped. Resupply.LeaveHost now queues an EXPLICIT vacate when the host
-- declares a ResupplyDock: the serviced client moves to the nearest free stayable cell OFF THE
-- FOOTPRINT (ResupplyDock.VacateCandidates), deterministically, and then goes idle. Off the
-- BUILDING, not merely off the dock cell — the other three cells are transit-only and parking on
-- one would just re-earn the phantom shove a tick later.
--
-- So the contract this scenario now pins has four parts, and the last two are the old ones intact:
--   1. Tank docks on the crane cell 32,17 and is FULLY serviced there — health back to max, ammo
--      back to full. Health mattering at all is new: repair healed exactly zero until 2026-09-05
--      (Resupply.RepairTick read only HpPerStep, which is 0 everywhere in this mod), so this
--      scenario could not have been passing before that fix whatever else it measured.
--   2. Tank then leaves the FOOTPRINT with no player order behind it.
--   3. That move is drawn, and drawn in AutomaticOrder.LineColor — unchanged, and still the only
--      place in the suite that checks that wiring at this site.
--   4. Tank2 then docks on the same cell and is serviced. Unchanged in purpose: it is the evidence
--      that the vacate is doing a job rather than merely happening.
--
-- ============================================================================================
-- WHY THE TANKS ARE ONLY LIGHTLY DAMAGED, and it is not squeamishness about a long run.
--
-- `^Vehicle` carries ChangesHealth@CriticalDamage (vehicles.yaml:183): StartIfBelow 50,
-- PercentageStep -1, Delay 5. Below half health a vehicle burns 1% of MaxHP every 5 ticks =
-- 0.200% per tick. The depot repairs PercentageStep 3 per RepairsUnits.Interval 24 = 0.125% per
-- tick. THE BURN IS 1.6x THE REPAIR, so below 50% a vehicle at the crane loses ground and dies
-- there, and can never climb back over the threshold that started the burn.
--
-- This scenario ran at DamagedPercent 30 and went red on 2026-09-05 with "Tank died before it
-- finished servicing" — not a docking failure; the tank was under the crane being repaired, slower
-- than it was burning. 70% is deliberately clear of the threshold so that this scenario measures
-- the VACATE CONTRACT and not the damage model.
--
-- THAT IS A REAL OPEN QUESTION AND IT IS NOT SETTLED HERE. A critically damaged vehicle currently
-- cannot be saved by driving it home, which is either intended attrition or a bug depending on a
-- ruling nobody has made. Whoever makes it wants the two rates above and one of: raise
-- Repairable.PercentageStep to >= 5 (0.208%/tick, just over the burn), cut RepairsUnits.Interval to
-- <= 15 at PercentageStep 3, or pause the burn while docked (ChangesHealth is a ConditionalTrait and
-- the LC already grants `unit.docked` within 2c0). Each is one line and each changes every vehicle
-- in the game, which is why none of them is done here.

local DeadlineSeconds = 75 -- 1875 ticks. TestHarness.TicksPerSecond is 25 while the mod runs
local SettleTicks = 30     -- Timestep 60 (16.67 tps), so this is ~112s of wall clock.
                           -- A full rearm from empty is the long pole: 40 rounds / ReloadCount 5 =
                           -- 8 batches at AmmoPool.ReloadDelay 50 = 400 ticks EACH, and the two
                           -- tanks are serialised by the single dock cell. 75s is ~2x the ~950
                           -- ticks that needs; multiples of 25 are exact under AssertWithin's floor.
local DamagedPercent = 70  -- see the burn-vs-repair note above; must stay > 50

-- LOGISTICSCENTER placed at 32,16 is Dimensions 2,2 and covers 32..33 x 16..17. Its dock is the
-- bottom-left cell of the footprint (ResupplyDock.Offset -512,512,0 from a centre that sits on the
-- shared corner), i.e. 32,17.
local DepotMinX, DepotMaxX = 32, 33
local DepotMinY, DepotMaxY = 16, 17
local ExpectedDockCell = CPos.New(32, 17)

local dockCell = nil       -- FIRST cell seen on the depot footprint, i.e. where Tank docked
local completedCell = nil  -- Tank's cell on the tick servicing finished
local settledCell = nil    -- its cell SettleTicks later, with no order issued in between
local lineCells = nil      -- all target-line nodes seen once the vacate move was running
local autoLineCells = nil  -- the subset of those painted AutomaticOrder.LineColor
local tank2DockCell = nil  -- where Tank2 came to rest, which must be the cell Tank freed

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

local function Where(a)
	if a.IsDead then return "<dead>" end
	return a.Location.X .. "," .. a.Location.Y
end

WorldLoaded = function()
	TestHarness.FocusBetween(Tank, Depot)
	TestHarness.Select(Tank)

	-- Close enough to see a tank-sized thing move one cell. The default zoom is the viewport's
	-- MINIMUM, at which a 2x2 building is a ~50 px speck.
	Test.SetZoom(4)

	-- Render order lines so a screenshot of this run is meaningful. Does NOT affect the
	-- Test.GetTargetLineCells readings below, which walk the activity queue, not the renderer.
	Test.ShowTargetLinesAlways()

	-- Give the depot real work, BEFORE the first assert tick. Without this both tanks are already
	-- full, Serviced() is true at tick 0, and the test would capture start cells and verdict on
	-- nothing.
	Wreck(Tank)
	Wreck(Tank2)

	-- The real RESUPPLY command-bar order. Genuine player-issued errands — the whole point is that
	-- the vacate move comes after one of them completes, with nothing queued behind it.
	Test.IssueResupply(Tank)
	Test.IssueResupply(Tank2)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Tank.IsDead then
			return string.format(
				"fail: Tank died before it finished servicing, at %d%% health. If it was under 50%% it " ..
				"was BURNING faster than the depot repairs — ChangesHealth@CriticalDamage is 1%% of " ..
				"MaxHP per 5 ticks (0.200%%/tick) against the depot's 3%% per 24 (0.125%%/tick). " ..
				"DamagedPercent is meant to keep this run clear of that threshold; if it is still 70, " ..
				"something else damaged the tank",
				math.floor(Tank.Health * 100 / Tank.MaxHealth))
		end
		if Tank2.IsDead then return "fail: Tank2 died before it finished servicing" end

		-- Latch the docking cell the first tick it is seen on the footprint, and compare against
		-- THIS rather than completedCell. Serviced() is a health/ammo poll, and if repair tops out
		-- a few ticks before Resupply's activity ends, the vacate could already be under way when
		-- it first reads full — comparing completion-cell to settle-cell would then measure nothing
		-- and report a false negative. The dock cell cannot drift.
		if dockCell == nil and OnDepot(Tank.Location) then
			dockCell = Tank.Location

			-- New since the 2x2 resize: there is exactly ONE stayable cell on the footprint now, so
			-- the dock is nameable rather than merely "somewhere on the building". Getting this wrong
			-- means the vacate below is being measured from the wrong place.
			if not (dockCell.X == ExpectedDockCell.X and dockCell.Y == ExpectedDockCell.Y) then
				return string.format(
					"fail: Tank came to rest on the footprint at %d,%d, but the only stayable cell is " ..
					"%d,%d — the crane cell named by ResupplyDock.Offset (-512,512,0). The other three " ..
					"are '+' transit-only, so a tank resting on one is about to be shoved off by the " ..
					"idle handler and this run is measuring that, not the vacate",
					dockCell.X, dockCell.Y, ExpectedDockCell.X, ExpectedDockCell.Y)
			end
		end

		if completedCell == nil then
			if not Serviced(Tank) then return false end

			completedCell = Tank.Location

			-- Guard the guard. If Tank were serviced anywhere but on the depot footprint, a pathing
			-- failure would be indistinguishable from the vacate and this run would be reporting on
			-- something else entirely.
			if not OnDepot(completedCell) then
				return string.format(
					"fail: servicing finished at %d,%d, off the depot footprint %d..%d x %d..%d — Tank " ..
					"never docked, so this run says nothing about the vacate",
					completedCell.X, completedCell.Y, DepotMinX, DepotMaxX, DepotMinY, DepotMaxY)
			end

			TestHarness.Screenshot("1-serviced-on-dock",
				"expects: Tank at full health and ammo, still standing ON the crane cell 32,17, with " ..
				"its vacate move not yet run")

			Trigger.AfterDelay(SettleTicks, function()
				if not Tank.IsDead then settledCell = Tank.Location end
			end)

			return false
		end

		-- Latch the line nodes on the FIRST tick any exist after servicing. Reading them at the
		-- settle point instead would be a race the other way: the vacate is a short move and can
		-- finish inside SettleTicks, leaving nothing to see and failing for the wrong reason.
		if lineCells == nil then
			local seen = Test.GetTargetLineCells(Tank, false)
			if #seen > 0 then
				lineCells = seen
				autoLineCells = Test.GetAutomaticTargetLineCells(Tank)
			end
		end

		if settledCell == nil then return false end

		-- THE CONTRACT, and it is stricter than the old one. The old bounce only had to get the tank
		-- off the single transit-only cell it was standing on; the explicit vacate has to get it off
		-- the BUILDING, because every other footprint cell is transit-only too and stopping on one
		-- would simply re-earn the shove.
		if OnDepot(settledCell) then
			return string.format(
				"fail: Tank was still on the depot footprint at %d,%d, %d ticks after servicing " ..
				"finished at %d,%d (docked at %d,%d). Resupply.LeaveHost is supposed to queue an " ..
				"explicit move to a free stayable cell OFF the footprint when the host declares a " ..
				"ResupplyDock. Sitting on the DOCK cell means that move never queued; sitting on one " ..
				"of the other three means it picked a transit-only cell and the idle handler is about " ..
				"to move it again",
				settledCell.X, settledCell.Y, SettleTicks, completedCell.X, completedCell.Y,
				dockCell.X, dockCell.Y)
		end

		TestHarness.Screenshot("2-vacated",
			"expects: Tank standing OFF the building entirely, adjacent to it, with the dock cell " ..
			"32,17 empty and ready for Tank2")

		-- The vacate happened. Now: is it LEGIBLE? This is the cross-branch check on
		-- wt/heal-legibility's wiring at this specific site, and it is unchanged by the rewrite —
		-- the move is a different move now, but it is still one the player never ordered.
		if lineCells == nil then
			return string.format(
				"fail: Tank vacated %d,%d -> %d,%d but drew NO target-line node at any point. The " ..
				"vacate is queued without a targetLineColor, so the move is invisible and the player " ..
				"reads it as a hidden order",
				dockCell.X, dockCell.Y, settledCell.X, settledCell.Y)
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
		-- If this is what times out, the vacate is happening but not freeing the cell that matters.
		if tank2DockCell == nil and OnDepot(Tank2.Location) then
			tank2DockCell = Tank2.Location
		end

		if not Serviced(Tank2) then return false end

		if tank2DockCell == nil or not (tank2DockCell.X == ExpectedDockCell.X
			and tank2DockCell.Y == ExpectedDockCell.Y) then
			return string.format(
				"fail: Tank2 was serviced but never stood on the crane cell %d,%d (seen at %s). It was " ..
				"served from beside the depot, which means the arrival gate is looser than the dock " ..
				"contract claims and the queueing this scenario exists to prove was never exercised",
				ExpectedDockCell.X, ExpectedDockCell.Y, tank2DockCell and
					(tank2DockCell.X .. "," .. tank2DockCell.Y) or "never on the footprint")
		end

		TestHarness.Screenshot("3-second-customer",
			"expects: Tank2 now on the crane cell 32,17 being serviced, and Tank parked clear of the " ..
			"building — the whole point of the vacate in one frame")

		return true
	end, function()
		return string.format(
			"depot vacate assertions unresolved within %ds. Tank at %s hp=%d/%d ammo=%d; Tank2 at %s " ..
			"hp=%d/%d ammo=%d. docked=%s serviced=%s settled=%s tank2Dock=%s. A Tank that reached the " ..
			"dock and never completed is a SERVICE problem (rearm is the long pole at 400 ticks); a " ..
			"Tank2 that never reached it after Tank settled off the footprint is the QUEUEING half " ..
			"failing",
			DeadlineSeconds,
			Where(Tank), Tank.IsDead and -1 or Tank.Health, Tank.IsDead and -1 or Tank.MaxHealth,
			Tank.IsDead and -1 or Tank.AmmoCount("primary-ammo"),
			Where(Tank2), Tank2.IsDead and -1 or Tank2.Health, Tank2.IsDead and -1 or Tank2.MaxHealth,
			Tank2.IsDead and -1 or Tank2.AmmoCount("primary-ammo"),
			dockCell and (dockCell.X .. "," .. dockCell.Y) or "no",
			completedCell and "yes" or "no",
			settledCell and (settledCell.X .. "," .. settledCell.Y) or "no",
			tank2DockCell and (tank2DockCell.X .. "," .. tank2DockCell.Y) or "no")
	end)
end
