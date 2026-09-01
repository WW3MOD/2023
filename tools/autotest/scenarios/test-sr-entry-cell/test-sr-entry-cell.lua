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
-- every reinforcement on every map without a spawnarea actor -- 9 of the 10 shipped maps. The
-- evac fix and this share one line, but only this scenario can fail if that line regresses in a
-- way that spares evacuation.

local SRCell = { X = 14, Y = 16 }

-- d = 13 to the west boundary; band = +/-floor(sqrt(2*13)) = +/-5, so rows 11..21 all floored
-- to 13 under the old key and the stable sort returned the lowest.
local ExpectedCell = { X = 1, Y = 16 }
local OldBiasedCell = { X = 1, Y = 11 }
local BandTop, BandBottom = 11, 21

local ProducedType = "e1.america"
local DeadlineSeconds = 10

-- Captured on the FIRST tick the unit is visible, which is the tick after the frame-end task
-- that created it (ProductionFromMapEdge.cs:179-189). The actor is created with
-- LocationInit(entry cell), and a Mobile actor's Location only changes once it has physically
-- traversed a whole cell -- many ticks for infantry -- so a 1-tick poll cannot miss the spawn
-- cell. sightingDelay is reported anyway: if it is ever more than a couple of ticks, the reading
-- is worth distrusting rather than silently believing.
local captured = nil
local sightingDelay = nil
local produceTick = nil

local function CellStr(c)
	return c.X .. "," .. c.Y
end

WorldLoaded = function()
	TestHarness.FocusBetween(OwnSR)

	local owner = OwnSR.Owner
	produceTick = DateTime.GameTime

	-- Skip the production queue and call ProductionFromMapEdge.Produce directly -- the same
	-- entry point the queue uses. SUPPLYROUTE carries two Production traits; Production@Local
	-- makes only Building/Defense, so an infantry producee selects ProductionFromMapEdge on
	-- the BuildAtProductionType filter (ProductionProperties.cs:50-52).
	OwnSR.Produce(ProducedType)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if captured == nil then
			local units = owner.GetActorsByType(ProducedType)
			if #units == 0 then
				return false
			end

			captured = units[1].Location
			sightingDelay = DateTime.GameTime - produceTick
		end

		local c = captured
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
				.. CellStr(SRCell) .. " is x=1 by 13 cells against 15 north and 16 south, so "
				.. "this is a wrong-edge or wrong-origin fault, not a tie-break one. If a "
				.. "spawnarea actor has been added to this map the legacy branch is no longer "
				.. "taken and the test is measuring nothing"
		end

		return "the reinforcement entered at " .. CellStr(c) .. ", which is " .. diagnosis
			.. ". Expected the true nearest cell " .. CellStr(ExpectedCell) .. " for an SR at "
			.. CellStr(SRCell) .. " (d=13 to the west boundary). The old floored-sqrt tie band "
			.. "was rows " .. BandTop .. ".." .. BandBottom .. " on column x=1 and returned "
			.. CellStr(OldBiasedCell) .. ". Seen " .. sightingDelay .. " tick(s) after Produce"
	end, "no " .. ProducedType .. " ever appeared within " .. DeadlineSeconds .. "s. That is not "
		.. "this test's subject: production itself is broken, or ProductionFromMapEdge.Produce "
		.. "returned false because every perimeter cell failed CanEnterCell")
end
