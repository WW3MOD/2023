-- AUTO TEST: the SR's spawn-candidate SET must be the five edge cells nearest the spawnarea, in
-- nearest-first order, not the five lowest rows of a floored-distance tie band.
--
-- Map.GetSpawnCandidatesOnSameEdge ordered candidates by CVec.Length, which is
-- Exts.ISqrt(LengthSquared) with ISqrtRoundMode.Floor (CVec.cs:50, Exts.cs:305-306). See map.yaml
-- for the full derivation and for why d=20 was chosen over any shipped map's geometry.
--
-- WHY THE SEQUENCE, NOT JUST THE FIRST CELL. ProductionFromMapEdge round-robins the returned
-- array BY INDEX (:105-107 for aircraft, :146-150 for ground): each Produce takes candidates[idx]
-- and advances idx. So five consecutive produces read out the array in order, which makes the
-- whole set observable rather than only its head. That also proves the array holds five DISTINCT
-- cells -- though note the .Distinct() guard added alongside this fix is NOT exercised here (the
-- window is rows 30..34, far from any corner); map.yaml says why.
--
-- The five Produce calls are made in the same tick deliberately. ProductionFromMapEdge.Produce
-- resolves its cell and advances nextCandidateIndex SYNCHRONOUSLY, then defers only actor
-- creation to a frame-end task, so five back-to-back calls take five consecutive indices. Each
-- one's CanEnterCell test (:147) therefore runs against an edge that is still empty, and no
-- produced unit can block a later one's cell.
--
-- =====================================================================================
-- READ THE ENTRY CELLS IN OnProduction. DO NOT POLL Actor.Location FOR THEM.
-- =====================================================================================
-- `Actor.Location` is `OccupiesSpace.TopLeft` (Actor.cs:78) and for a Mobile actor
-- `TopLeft => ToCell` (Mobile.cs:314) -- the cell being moved INTO, assigned when a move BEGINS.
-- It therefore LEADS the unit by one cell, and unit speed does not buy a poll any margin: the
-- first tick of the queued MoveTo is enough. Run 260902_000616 failed exactly this way on
-- test-sr-entry-cell, reporting 2,16 for a unit that had entered at 1,16 and taken one step.
--
-- Trigger.OnProduction fires from INotifyProduction.UnitProduced (ScriptTriggers.cs:156-168),
-- which ProductionFromMapEdge raises at :199-200 -- after CreateActor and after the MoveTo is
-- QUEUED, but before any activity has TICKED, so ToCell is still the spawn cell. Only latch there.
--
-- The independent tell that a reading is an instrument fault rather than an engine one: with
-- Bounds 1,1,64,64 the perimeter is x=1, x=64, y=1, y=64. Any reported entry cell with x ~= 1
-- and y not in {1,64} is not in Map.AllEdgeCells at all and cannot have been returned by the
-- method under test.

local HintCell = { X = 21, Y = 32 }        -- the spawnarea; d = 20 to the west boundary
local SRCell = { X = 27, Y = 32 }

-- The five nearest cells to the hint, nearest first. Round-robin emits them in this order.
local Expected = { { 1, 32 }, { 1, 31 }, { 1, 33 }, { 1, 30 }, { 1, 34 } }

-- What the floored key returned: the five LOWEST rows of the 26..38 tie band, in v order.
local OldBiased = { { 1, 26 }, { 1, 27 }, { 1, 28 }, { 1, 29 }, { 1, 30 } }

local BandTop, BandBottom = 26, 38
local EdgeColumn = 1
local ProducedType = "e1.america"
local ProduceCount = 5
local DeadlineSeconds = 12

local entryCells = {}    -- latched in OnProduction; the ONLY values the verdict may judge
local produceTick = nil

local function CellStr(c)
	return c.X .. "," .. c.Y
end

local function PairStr(p)
	return p[1] .. "," .. p[2]
end

local function SeqStr(list, fmt)
	local parts = {}
	for i = 1, #list do
		parts[#parts + 1] = fmt(list[i])
	end
	return table.concat(parts, " ")
end

local function ObservedStr()
	return SeqStr(entryCells, CellStr)
end

local function ExpectedStr()
	return SeqStr(Expected, PairStr)
end

local function MatchesSequence(want)
	if #entryCells ~= #want then
		return false
	end

	for i = 1, #want do
		if entryCells[i].X ~= want[i][1] or entryCells[i].Y ~= want[i][2] then
			return false
		end
	end

	return true
end

WorldLoaded = function()
	TestHarness.FocusBetween(OwnSR)

	-- Registered BEFORE any Produce so the callback is in place when the frame-end tasks run.
	Trigger.OnProduction(OwnSR, function(producer, produced)
		entryCells[#entryCells + 1] = produced.Location
	end)

	produceTick = DateTime.GameTime

	for _ = 1, ProduceCount do
		OwnSR.Produce(ProducedType)
	end

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if #entryCells < ProduceCount then
			return false
		end

		if MatchesSequence(Expected) then
			return true
		end

		local observed = ObservedStr()

		-- Every cell that is on the west edge but inside the tie band, i.e. explicable by a
		-- floored key rather than by a wrong edge or a wrong search origin.
		local inBand = 0
		local offEdge = 0
		for i = 1, #entryCells do
			local c = entryCells[i]
			if c.X ~= EdgeColumn then
				offEdge = offEdge + 1
			elseif c.Y >= BandTop and c.Y <= BandBottom then
				inBand = inBand + 1
			end
		end

		local diagnosis
		if MatchesSequence(OldBiased) then
			diagnosis = "EXACTLY the old floored-key set. entry cell " .. CellStr(entryCells[1])
				.. " expected " .. PairStr(Expected[1]) .. ". Map.GetSpawnCandidatesOnSameEdge is "
				.. "sorting on CVec.Length again -- check it still orders by LengthSquared"
		elseif offEdge > 0 then
			diagnosis = offEdge .. " of " .. #entryCells .. " cells are not on column x="
				.. EdgeColumn .. " at all. The nearest edge to the spawnarea at " .. PairStr({ HintCell.X, HintCell.Y })
				.. " is x=1 by 20 cells against 31 north, 32 south and 43 east, an 11-cell margin, "
				.. "so no tie-break can pick another edge. These were latched in OnProduction and "
				.. "are NOT contaminated by unit movement, so suspect the search origin: either the "
				.. "spawnarea actor is gone (which drops ProductionFromMapEdge onto its legacy "
				.. "no-spawnarea branch at :123-132, a DIFFERENT method) or Bounds changed"
		elseif inBand == #entryCells then
			diagnosis = "all on the west edge and all inside the floored tie band rows " .. BandTop
				.. ".." .. BandBottom .. ", but matching neither the exact set nor the old stable-lowest "
				.. "one -- some third ordering is in play. entry cell " .. CellStr(entryCells[1])
				.. " expected " .. PairStr(Expected[1])
		else
			diagnosis = "on the west edge but at least one row is OUTSIDE the tie band " .. BandTop
				.. ".." .. BandBottom .. ", which no tie-break among equal floored keys can explain. "
				.. "Suspect SpawnCandidateCount (expected 5) or the GetSameEdgeCells filter, not the sort"
		end

		local dupNote = ""
		local seen = {}
		for i = 1, #entryCells do
			local k = CellStr(entryCells[i])
			if seen[k] then
				dupNote = " NOTE: cell " .. k .. " was used TWICE, so the candidate array contains a "
					.. "duplicate -- that is the corner-duplication case (UpdateEdgeCells appends each "
					.. "corner twice) and means .Distinct() before .Take() is missing or ineffective"
			end
			seen[k] = true
		end

		return "the five reinforcements entered at " .. observed .. ", which is " .. diagnosis
			.. ". Expected " .. ExpectedStr() .. " -- the five cells nearest the spawnarea at "
			.. PairStr({ HintCell.X, HintCell.Y }) .. " (d=20 to the west boundary), emitted "
			.. "nearest-first by round-robin. The old floored-sqrt band was rows " .. BandTop .. ".."
			.. BandBottom .. " on column x=" .. EdgeColumn .. " and returned " .. SeqStr(OldBiased, PairStr)
			.. ". All cells latched in Trigger.OnProduction" .. dupNote
	-- Function form: evaluated AT timeout, so it can report how many actually arrived. A string
	-- built here would freeze #entryCells at 0 and claim nothing was produced even when four were.
	end, function()
		return "only " .. #entryCells .. " of " .. ProduceCount .. " " .. ProducedType
			.. " were produced within " .. DeadlineSeconds .. "s"
			.. (#entryCells > 0 and (", entering at " .. ObservedStr()) or "")
			.. ". That is not this test's subject: either production is broken outright, or "
			.. "ProductionFromMapEdge.Produce returned false because every one of the five candidate "
			.. "cells failed CanEnterCell -- check that the west edge of Bounds 1,1,64,64 is still "
			.. "clear terrain, and that SUPPLYROUTE at " .. CellStr(SRCell) .. " still inherits "
			.. "^ExistsInWorld and its ScriptTriggers trait (structures.yaml:223, defaults.yaml:7)"
	end)
end
