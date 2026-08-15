-- AUTO TEST: infantry that RIDE to the front must be set down with the armour,
-- not at a cell the armour was never going to.
--
-- WHAT THIS MEASURES, and why it is phrased the way it is.
--
-- The obvious observable -- "are the riflemen near the tank" -- DOES NOT WORK, and
-- a previous revision of this file proved it by passing with the fix disabled.
-- Infantry are armed, so PoiOffensiveBotModule.StageFreePool recruits them into
-- the free pool and AttackMoves them to the SAME staging anchor it sends the tank
-- to. They arrive next to the armour under their own feet, the predicate goes
-- true, and the rendezvous under test is never exercised. That is a control that
-- passed when it was required to fail.
--
-- So the assertion is narrowed to units that were actually CARRIED:
--
--   (a) the tank has advanced >= AdvanceCells from the SR   -- we are judging at
--       the front, not on the start line; and
--   (b) >= MinTogether riflemen have been observed OUT OF WORLD (i.e. loaded into
--       the carrier) and, having returned to the world, are within TogetherCells
--       of the tank.
--
-- Clause (b) is the discriminator. A rifleman that walks the whole way is never
-- out of world, so it can never satisfy it no matter where it ends up. Only a
-- passenger that was set down counts, and where it is set down is exactly what
-- the rendezvous changes: the legacy path drops it on the SR->enemy-SR lerp
-- (top-right bearing), the rendezvous drops it on the armour's control-field
-- staging anchor (bottom-right bearing, where the tank is).

local DeadlineSeconds = 200

-- Own SR cell, mirrored from map.yaml. Used only for the "has left home" guard.
local SrX, SrY = 6, 16

local AdvanceCells = 8   -- tank must be this far from the SR before we judge
local TogetherCells = 7  -- a dismounted rifleman this close to the tank is "with" it
local MinTogether = 2    -- ...and this many must be, so one straggler is not a pass

local Riflemen = { BotRifle1, BotRifle2, BotRifle3, BotRifle4 }

-- Per-rifleman record of "this one was inside the carrier at some point".
local WasCarried = {}

-- Chebyshev (king-move) distance, matching RendezvousMath.CellDistance so the
-- test measures in the same metric the code reasons in.
local function CellDistance(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

-- Diagnostics carried into the failure message. A bare timeout cannot tell
-- "the ferry ran and set them down in the wrong place" (the defect) from "the
-- ferry never ran at all" (a scenario that measured nothing), and those demand
-- opposite responses -- so the reason string has to say which happened.
local BestTankAdvance = 0
local EverCarried = 0
local BestTogether = 0
local BestCarriedGap = 9999

WorldLoaded = function()
	TestHarness.FocusBetween(BotTank, BotCarrier)

	local sr = CPos.New(SrX, SrY)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if BotTank.IsDead then
			return "fail: the bot's tank died before the rendezvous could be judged"
		end

		-- Latch "was carried" every tick, independently of the clauses below, so a
		-- rifleman that boards and dismounts before the tank has advanced is still
		-- counted. The flag is monotonic: boarding is evidence, not a state.
		for i = 1, #Riflemen do
			local r = Riflemen[i]
			if r ~= nil and not r.IsDead and not r.IsInWorld and not WasCarried[i] then
				WasCarried[i] = true
				EverCarried = EverCarried + 1
			end
		end

		local tankAdvance = CellDistance(BotTank.Location, sr)
		if tankAdvance > BestTankAdvance then BestTankAdvance = tankAdvance end

		-- Clause (a).
		if tankAdvance < AdvanceCells then
			return false
		end

		-- Clause (b): only riflemen that RODE count.
		local together = 0
		for i = 1, #Riflemen do
			local r = Riflemen[i]
			if r ~= nil and WasCarried[i] and not r.IsDead and r.IsInWorld then
				local gap = CellDistance(r.Location, BotTank.Location)
				if gap < BestCarriedGap then BestCarriedGap = gap end
				if gap <= TogetherCells then together = together + 1 end
			end
		end

		if together > BestTogether then BestTogether = together end

		return together >= MinTogether
	end, "carried infantry never joined the armour: tank advanced " .. BestTankAdvance ..
		"/" .. AdvanceCells .. " cells from SR; riflemen ever carried = " .. EverCarried ..
		" (0 here means the FERRY NEVER RAN and this run measured nothing about the " ..
		"rendezvous); best carried-and-with-armour = " .. BestTogether .. "/" .. MinTogether ..
		"; closest a carried rifleman got to the tank = " .. BestCarriedGap .. " cells")
end
