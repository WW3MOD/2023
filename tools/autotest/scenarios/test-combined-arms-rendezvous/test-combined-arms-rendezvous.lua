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

-- WHY IT HAS NEVER PRODUCED A VERDICT, as of 2026-09-06 (`main @ b6207b9b`). Three
-- runs, three "the bot's tank died". map.yaml's header guesses the cause is the two
-- belief-source t90s and advises pushing them further right or dropping to one.
-- THAT ADVICE IS WRONG AND WOULD WASTE A RUN. The tank is not killed on the way to
-- an anchor: LaneAmbushBotModule claims it at tick 200 and posts it, with one
-- companion, to a static picket at 28,12 -- 22 cells out, 40% of the way to the
-- ENEMY Supply Route (PostFractionPct) -- where it sits until it dies, while a
-- dozen friendly units stand at the SR. Full log-by-log derivation in
-- WORKSPACE/pipeline/items/64-combined-arms-push.md, "DIAGNOSIS 2026-09-06".

local DeadlineSeconds = 200

-- Own SR cell, mirrored from map.yaml. Used only for the "has left home" guard.
local SrX, SrY = 6, 16

local AdvanceCells = 8   -- tank must be this far from the SR before we judge
local TogetherCells = 7  -- a dismounted rifleman this close to the tank is "with" it
local MinTogether = 2    -- ...and this many must be, so one straggler is not a pass

-- POPULATED IN WorldLoaded, NOT HERE (changed 2026-09-06). A file-scope
-- `{ BotRifle1, ... }` is a table of four nils if map-actor globals are not yet
-- bound when this chunk runs, and then `#Riflemen == 0`, every loop below
-- silently does nothing and every count stays 0 forever -- no error, no warning.
-- wip-transport-delivers/test-transport-delivers.lua:38-42 names that as the
-- suspected cause of its own everCarried=0 reading on 2026-08-15. Reading
-- ScriptContext.cs:210-234 says the binding IS in place first (MapGlobal's
-- constructor registers map actors while the global tables are built, which is
-- before runtime.DoBuffer executes this file), so the hazard may not be real --
-- but capturing late costs nothing and removes it from the list of things a red
-- run could mean.
local Riflemen = {}

-- Per-rifleman record of "this one was inside the carrier at some point".
local WasCarried = {}

-- ---------------------------------------------------------------------------
-- POSITIONAL TRACE (added 2026-09-06). NOT part of the verdict.
--
-- WHY IT EXISTS. Three runs in a row -- 260905_180211, 260905_212607 and
-- 260906_002507 -- failed with the bare string "the bot's tank died before the
-- rendezvous could be judged" and a ZERO-BYTE lua.log. The verdict was correct
-- and told nobody anything: it does not say where the tank was, whether it was
-- alone, or whether it had left the Supply Route at all. Answering that from
-- debug.log alone costs a chain of inference across [exp-ambush], [exp-ledger]
-- and [exp-clog], because NO bot module logs which ACTOR it sent where
-- (LayeredDefenceBotModule.cs:545 says so in as many words). One print per 100
-- ticks replaces that chain with a reading, and DateTime.GameTime is the same
-- counter debug.log stamps its lines with, so the two files line up tick for
-- tick.
--
-- The tank's cell is latched every tick because it is not readable once the
-- actor is gone (see Where below). The death message quotes the latch.
local RollEveryTicks = 100
local LastTankCell = "unspawned"

-- Position, or the raw flags when there isn't one.
--
-- IT PRINTS `IsDead` RATHER THAN INTERPRETING IT, ON PURPOSE. Two in-tree
-- documents give incompatible accounts of what IsDead reports for a boarded
-- passenger: AUTOTEST.md:331 states as measured that it is TRUE (and names THIS
-- scenario as carrying the broken idiom), while test-transport-delivers.lua:38-42
-- attributes the same 2026-08-15 observation to an empty actor table instead.
-- Actor.cs:76 -- `Disposed || health.IsDead` -- supports neither cleanly. So this
-- emits the flag verbatim and the next run settles it for free.
--
-- Location is read only while the actor is in world: GeneralProperties carries
-- [ExposedForDestroyedActors] so the property stays BOUND after disposal, but it
-- resolves OccupiesSpace.TopLeft and there is no OccupiesSpace to resolve.
local function Where(a)
	if a == nil then return "nil" end
	if a.IsInWorld then return a.Location.X .. "," .. a.Location.Y end
	return "oow/dead=" .. tostring(a.IsDead)
end

local function Roll()
	local parts = { "tank=" .. Where(BotTank), "carrier=" .. Where(BotCarrier) }
	for i = 1, #Riflemen do
		parts[#parts + 1] = "rifle-" .. i .. "=" .. Where(Riflemen[i]) ..
			(WasCarried[i] and "(rode)" or "")
	end

	return table.concat(parts, " ")
end

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
	Riflemen = { BotRifle1, BotRifle2, BotRifle3, BotRifle4 }

	TestHarness.FocusBetween(BotTank, BotCarrier)

	local sr = CPos.New(SrX, SrY)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		local tick = DateTime.GameTime
		if tick % RollEveryTicks == 0 then
			print("[rendezvous] tick=" .. tick .. "; " .. Roll())
		end

		if BotTank.IsDead then
			return "fail: the bot's tank died before the rendezvous could be judged" ..
				" (tick=" .. tick .. "; tank last seen at " .. LastTankCell ..
				", SR is " .. SrX .. "," .. SrY .. "; " .. Roll() .. ")"
		end

		LastTankCell = Where(BotTank)

		-- Latch "was carried" every tick, independently of the clauses below, so a
		-- rifleman that boards and dismounts before the tank has advanced is still
		-- counted. The flag is monotonic: boarding is evidence, not a state.
		for i = 1, #Riflemen do
			local r = Riflemen[i]
			-- NO `not r.IsDead` TERM (dropped 2026-09-06). Whether a boarded passenger
			-- reports dead is the disputed reading above, and if AUTOTEST.md:331 is
			-- right this latch was unsatisfiable for exactly the units it exists to
			-- catch -- EverCarried stuck at 0 all match and clause (b) unreachable, so
			-- the scenario could never go green. Latching on out-of-world alone is the
			-- repair AUTOTEST.md prescribes and it is safe here because clause (b)
			-- separately requires the unit to be back IN world, which a genuinely dead
			-- one never is. The cost is that a rifleman KILLED in the open also latches,
			-- so EverCarried is now an upper bound -- read the roll, where a corpse
			-- prints `oow/dead=true(rode)` and a passenger `oow/dead=false(rode)`.
			if r ~= nil and not r.IsInWorld and not WasCarried[i] then
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

	-- FUNCTION FORM, NOT A STRING, AND THAT IS THE WHOLE POINT (fixed 2026-09-06).
	-- The third argument to AssertWithin is evaluated EAGERLY at registration
	-- (test-helpers.lua:102 only calls it if it is a function), so the string this
	-- used to be was concatenated at tick 0 with every counter still at its initial
	-- value. It reported "tank advanced 0/8", "riflemen ever carried = 0" and
	-- "closest ... = 9999" on EVERY timeout no matter what happened -- including the
	-- sentence "0 here means the FERRY NEVER RAN and this run measured nothing",
	-- which would have been printed verbatim after a run where the ferry worked
	-- perfectly. A diagnostic that cannot be false is worse than none, and this one
	-- pointed at the wrong module. The trap is documented at
	-- test-push-departs-together.lua:58-61; this file was a live instance of it.
	end, function()
		return "carried infantry never joined the armour: tank advanced " .. BestTankAdvance ..
			"/" .. AdvanceCells .. " cells from SR; riflemen ever carried = " .. EverCarried ..
			" (0 here means the FERRY NEVER RAN and this run measured nothing about the " ..
			"rendezvous); best carried-and-with-armour = " .. BestTogether .. "/" .. MinTogether ..
			"; closest a carried rifleman got to the tank = " .. BestCarriedGap ..
			" cells; final roll: " .. Roll()
	end)
end
