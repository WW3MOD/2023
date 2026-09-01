-- AUTO TEST: a ground reinforcement must enter at the TRUE nearest map-edge cell to its
-- Supply Route, not at the north end of a floored-distance tie band.
--
-- Map.ChooseClosestMatchingEdgeCell ordered the perimeter by CVec.Length, which is
-- Exts.ISqrt(LengthSquared) with ISqrtRoundMode.Floor (CVec.cs:50, Exts.cs:305-306). A floored
-- sort key merges cells that are not equidistant into one tie; OrderBy is stable and
-- UpdateEdgeCells appends the west column with v ascending (Map.cs:1943-1952), so the winner was
-- always the band's lowest row. See map.yaml for the geometry and the full derivation.
--
-- WHY THIS TEST EXISTS SEPARATELY FROM test-evac-queued-line. That one pins the EVACUATION
-- caller. This one pins ProductionFromMapEdge's legacy ground branch
-- (ProductionFromMapEdge.cs:117-132), which is where the same defect decided the entry cell of
-- every reinforcement on a map with no spawnarea actor.
--
-- =====================================================================================
-- READ THE ENTRY CELL IN OnProduction. DO NOT POLL Actor.Location FOR IT.
-- =====================================================================================
-- This scenario's first run (260902_000616) failed reporting 2,16, and the reason is a trap
-- worth stating at length because it invalidates the obvious way to write this test.
--
-- `Actor.Location` is `OccupiesSpace.TopLeft` (Actor.cs:78) and for a Mobile actor
-- `TopLeft => ToCell` (Mobile.cs:314) -- the cell being moved INTO. ToCell is set when a move
-- BEGINS, not when it completes, which is also what Mobile.IsLeavingCell encodes
-- (`ToCell != location && FromCell == location`). So Actor.Location LEADS the unit's travel by
-- one cell: it does not lag until a cell is traversed, and unit speed is irrelevant to how fast
-- it changes.
--
-- ProductionFromMapEdge queues a MoveTo toward the rally (here, the SR) on the produced unit
-- (:194-196). On the unit's very first tick that move begins and ToCell steps one cell along the
-- path. Polling `Actor.Location` even 1 tick later therefore reads the SECOND cell of the walk,
-- not the entry cell. 2,16 was exactly that: entry at 1,16, then one step east toward the SR at
-- 14,16.
--
-- 2,16 CANNOT BE AN ENTRY CELL AT ALL, which is the independent confirmation: with Bounds
-- 1,1,64,32 the perimeter is x=1, x=64, y=1 and y=32, so 2,16 is not in Map.AllEdgeCells and
-- ChooseClosestMatchingEdgeCell could not have returned it.
--
-- Trigger.OnProduction fires from INotifyProduction.UnitProduced (ScriptTriggers.cs:156-168),
-- which ProductionFromMapEdge raises at :199-200 -- inside the same frame-end task, after
-- CreateActor and after the MoveTo is QUEUED but before any activity has TICKED. ToCell is still
-- the spawn cell there. That is the only correct latch point.

local SRCell = { X = 14, Y = 16 }

-- d = 13 to the west boundary; band = +/-floor(sqrt(2*13)) = +/-5, so rows 11..21 all floored
-- to 13 under the old key and the stable sort returned the lowest.
local ExpectedCell = { X = 1, Y = 16 }
local OldBiasedCell = { X = 1, Y = 11 }
local BandTop, BandBottom = 11, 21

local ProducedType = "e1.america"
local DeadlineSeconds = 10

local entryCell = nil    -- latched in OnProduction; the ONLY value the verdict may judge
local polledCell = nil   -- what Actor.Location reads later; diagnostic context only
local produceTick = nil
local sightingDelay = nil

local function CellStr(c)
	return c.X .. "," .. c.Y
end

WorldLoaded = function()
	TestHarness.FocusBetween(OwnSR)

	local owner = OwnSR.Owner

	-- Registered BEFORE Produce so the callback is in place when the frame-end task runs.
	Trigger.OnProduction(OwnSR, function(producer, produced)
		if entryCell == nil then
			entryCell = produced.Location
		end
	end)

	produceTick = DateTime.GameTime

	-- Skip the production queue and call ProductionFromMapEdge.Produce directly -- the same
	-- entry point the queue uses. SUPPLYROUTE carries two Production traits; Production@Local
	-- makes only Building/Defense, so an infantry producee selects ProductionFromMapEdge on
	-- the BuildAtProductionType filter (ProductionProperties.cs:50-52).
	OwnSR.Produce(ProducedType)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		local units = owner.GetActorsByType(ProducedType)
		if #units == 0 then
			return false
		end

		polledCell = units[1].Location
		if sightingDelay == nil then
			sightingDelay = DateTime.GameTime - produceTick
		end

		if entryCell == nil then
			return "the rifleman exists and Actor.Location now reads " .. CellStr(polledCell)
				.. ", but Trigger.OnProduction never fired, so the entry cell was never "
				.. "latched and NOTHING here is measured. Actor.Location is Mobile.ToCell "
				.. "(Actor.cs:78, Mobile.cs:314) and leads the walk by a cell, so it is not a "
				.. "usable substitute. Check that SUPPLYROUTE still inherits ^ExistsInWorld and "
				.. "its ScriptTriggers trait (structures.yaml:223, defaults.yaml:7)"
		end

		local c = entryCell
		local drift = ""
		if polledCell.X ~= c.X or polledCell.Y ~= c.Y then
			drift = " (it has since started moving and Actor.Location now reads "
				.. CellStr(polledCell) .. "; that drift is the queued MoveTo and is not itself "
				.. "a fault)"
		end

		if c.X == ExpectedCell.X and c.Y == ExpectedCell.Y then
			return true
		end

		local diagnosis
		if c.X == ExpectedCell.X and c.Y == OldBiasedCell.Y then
			diagnosis = "the NORTH END of the tie band -- Map.ChooseClosestMatchingEdgeCell is "
				.. "sorting on a floored key again. Check that it still orders by LengthSquared "
				.. "and not by CVec.Length"
		elseif c.X == ExpectedCell.X and c.Y >= BandTop and c.Y <= BandBottom then
			diagnosis = "the correct west edge but a row INSIDE the floored tie band, so the "
				.. "perimeter sort is neither exact nor the old stable-lowest -- some third "
				.. "ordering is in play"
		elseif c.X == ExpectedCell.X then
			diagnosis = "the correct west edge but a row OUTSIDE the tie band, which no "
				.. "tie-break can explain -- suspect the search origin rather than the sort"
		else
			diagnosis = "not on the west edge at all. The nearest edge to the SR at "
				.. CellStr(SRCell) .. " is x=1 by 13 cells against 15 north and 16 south. Note "
				.. "this cell was latched in OnProduction, so unlike the 260902_000616 run it "
				.. "is NOT contaminated by the unit's own movement -- if it is not a perimeter "
				.. "cell of Bounds 1,1,64,32 then the search origin is wrong, or a spawnarea "
				.. "actor has been added and the legacy branch is no longer taken"
		end

		return "the reinforcement ENTERED at " .. CellStr(c) .. ", which is " .. diagnosis
			.. ". Expected the true nearest cell " .. CellStr(ExpectedCell) .. " for an SR at "
			.. CellStr(SRCell) .. " (d=13 to the west boundary). The old floored-sqrt tie band "
			.. "was rows " .. BandTop .. ".." .. BandBottom .. " on column x=1 and returned "
			.. CellStr(OldBiasedCell) .. ". Latched at production; first polled "
			.. sightingDelay .. " tick(s) after Produce" .. drift
	end, "no " .. ProducedType .. " ever appeared within " .. DeadlineSeconds .. "s. That is not "
		.. "this test's subject: production itself is broken, or ProductionFromMapEdge.Produce "
		.. "returned false because every perimeter cell failed CanEnterCell")
end
