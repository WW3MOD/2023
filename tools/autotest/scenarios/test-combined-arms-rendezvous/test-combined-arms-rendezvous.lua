-- AUTO TEST: the bot's infantry must reach the front WITH its armour, not be
-- delivered to a cell the armour was never going to.
--
-- WHAT THIS MEASURES: a positional relationship, checked only once the armour
-- has actually left the Supply Route.
--
--   (a) the tank has advanced at least AdvanceCells from the SR   -- "we are at
--       the front, not still on the start line"; and
--   (b) at least MinTogether riflemen are within TogetherCells of the tank.
--
-- BOTH clauses are load-bearing and (a) is the one that makes the test honest.
-- Without it the predicate is TRUE on tick 0 — everything starts stacked around
-- the SR — so it would pass before the behaviour under test had a chance to run
-- and would keep passing if the fix were reverted. That is precisely the
-- "control that passed when it had to fail" failure this project has hit before,
-- so the guard is not defensive dressing; it is the test.
--
-- EXPECTED RED (RendezvousWithOffensiveStaging off): the tank stages forward down
-- the control-field gradient toward the massed enemy armour (bottom-right) while
-- the ferry drops its passengers on the SR->enemy-SR lerp (top-right). Clause (a)
-- goes true, clause (b) does not.
--
-- EXPECTED GREEN (rendezvous on): the drop-off IS the armour's staging anchor, so
-- the riflemen dismount on top of the tank and (b) goes true shortly after (a).

local DeadlineSeconds = 120

-- Own SR cell, mirrored from map.yaml. Used only for the "has left home" guard.
local SrX, SrY = 6, 16

local AdvanceCells = 8   -- tank must be this far from the SR before we judge
local TogetherCells = 7  -- riflemen this close to the tank count as "with" it
local MinTogether = 2    -- ...and this many must be, so one straggler is not a pass

local Riflemen = { BotRifle1, BotRifle2, BotRifle3, BotRifle4 }

-- Chebyshev (king-move) distance, matching RendezvousMath.CellDistance so the
-- test measures in the same metric the code reasons in.
local function CellDistance(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

-- Diagnostics carried to the failure message. A bare timeout cannot distinguish
-- "the ferry delivered them somewhere else" (the defect) from "nothing moved at
-- all" (a scenario that measured nothing) -- and those demand opposite responses,
-- so the reason string has to say which one happened.
local BestTankAdvance = 0
local BestTogether = 0
local BestInfantryGap = 9999

WorldLoaded = function()
	TestHarness.FocusBetween(BotTank, BotCarrier)

	local sr = CPos.New(SrX, SrY)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if BotTank.IsDead then
			return "fail: the bot's tank died before the rendezvous could be judged"
		end

		local tankAdvance = CellDistance(BotTank.Location, sr)
		if tankAdvance > BestTankAdvance then BestTankAdvance = tankAdvance end

		-- Clause (a): do not judge anything until the armour has left the SR.
		if tankAdvance < AdvanceCells then
			return false
		end

		-- Clause (b): count riflemen standing with the armour. Mounted passengers
		-- are out of world and simply do not count -- which is correct: the claim
		-- is about where they ARRIVE, not where they are carried.
		local together = 0
		local nearestGap = 9999
		for i = 1, #Riflemen do
			local r = Riflemen[i]
			if r ~= nil and not r.IsDead and r.IsInWorld then
				local gap = CellDistance(r.Location, BotTank.Location)
				if gap < nearestGap then nearestGap = gap end
				if gap <= TogetherCells then together = together + 1 end
			end
		end

		if together > BestTogether then BestTogether = together end
		if nearestGap < BestInfantryGap then BestInfantryGap = nearestGap end

		return together >= MinTogether
	end, "infantry never reached the armour: best tank advance from SR = " .. BestTankAdvance ..
		" cells (needed " .. AdvanceCells .. "), best riflemen within " .. TogetherCells ..
		" cells of the tank = " .. BestTogether .. " (needed " .. MinTogether ..
		"), closest any rifleman ever got = " .. BestInfantryGap .. " cells")
end
