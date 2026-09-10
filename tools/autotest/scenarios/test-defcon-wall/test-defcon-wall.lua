-- TEST: the DEFCON 3 dividing wall holds against aircraft.
--
-- Layout and intent live in description.txt. This file stages the three airframes,
-- issues the orders, and keeps the running invariant.
--
-- WHY THE ORDERS GO THROUGH Test.IssueMove AND NOT actor.Move. Layer 1 of the wall
-- lives in Aircraft.ResolveOrder, which only a real ORDER reaches. The activity-direct
-- Lua `actor.Move` constructs the activity itself and never consults the order layer at
-- all -- and in any case it is bound on MobileProperties, which Requires<MobileInfo>, so
-- an aircraft does not have it. Test.IssueMove issues the same Order a player's click
-- produces, which is the only thing that can prove the refusal.
--
-- WHAT THIS SCENARIO CANNOT DO, stated rather than hidden. Layer 3 is a refusal inside
-- Aircraft.SetPosition, and there is no Lua binding that teleports an aircraft: the
-- Teleport binding is on MobileProperties too. So layer 3 is asserted as an INVARIANT --
-- nothing is ever observed across the line -- rather than triggered directly. That is
-- the honest shape of it: layers 1 and 2 sit in front of layer 3 and are supposed to
-- mean it never fires, so a run in which it never fires is the expected run.

local TicksPerSecond = TestHarness.TicksPerSecond

-- The authored line, from rules.yaml. A cell at x >= WALL_X is on Russia's side or in
-- the wall band itself; x < WALL_X is USA's.
local WALL_X = 44

-- Budgets, in ticks. Multiples of 25 only -- AssertWithin recovers a budget with
-- math.floor and some integers come back a tick short, but multiples of 25 are exact.
local ORDER_TICK = 25
local VERDICT_TICK = 500

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

-- Spawned airborne, deliberately. ^Airborne sets Aircraft: TakeOffOnCreation: False, so
-- a helicopter placed in map.yaml starts PARKED, and a parked airframe under a refused
-- order is indistinguishable from one that never took off.
local function heli(actorType, owner, cx, cy, facing)
	return Actor.Create(actorType, true, {
		Owner = owner,
		CenterPosition = cellPos(cx, cy, 1280),
		Facing = facing,
	})
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	Probe = heli("littlebird", USA, 20, 28, Angle.East)
	Approach = heli("littlebird", USA, 36, 28, Angle.East)
	EastProbe = heli("heli", Russia, 70, 28, Angle.West)

	TestHarness.FocusBetween(Probe, Approach)
	TestHarness.Select(Probe)

	local probeStartX = Probe.Location.X
	local eastStartX = EastProbe.Location.X

	-- Running state for the verdict.
	local crossings = 0
	local crossingNote = ""
	local sawReturn = false
	local legalCursor, illegalCursor = "", ""
	local tick = 0

	local function sample()
		-- THE LAYER 3 INVARIANT. Checked every tick for every airframe, from the owner's
		-- own side. One observation on the wrong side is a failure even if the airframe
		-- comes back, because the wall is supposed to be a barrier and not a rubber band.
		for _, a in ipairs({ Probe, Approach }) do
			if not a.IsDead and a.Location.X >= WALL_X and crossings == 0 then
				crossings = crossings + 1
				crossingNote = string.format("a USA airframe reached x=%d (wall at %d) on tick %d",
					a.Location.X, WALL_X, tick)
			end
		end

		if not EastProbe.IsDead and EastProbe.Location.X <= WALL_X and crossings == 0 then
			crossings = crossings + 1
			crossingNote = string.format("the Russia airframe reached x=%d (wall at %d) on tick %d",
				EastProbe.Location.X, WALL_X, tick)
		end

		-- Layer 2 announces itself by wrapping the trip home in DefconWallReturn, which is
		-- the only reason that wrapper activity exists.
		if not Approach.IsDead and Test.ActivityChain(Approach):find("DefconWallReturn", 1, true) then
			sawReturn = true
		end
	end

	local function verdict()
		local faults = {}

		if crossings > 0 then
			faults[#faults + 1] = "THE WALL WAS CROSSED: " .. crossingNote
				.. ". All three layers failed together, or the line resolved in the wrong place --"
				.. " check that both Supply Routes are placed, since DefconWall takes each player's"
				.. " side from HomeLocation and two players on one side make the wall one-directional"
		end

		-- Layer 1: a refused order starts nothing, so the probe should not have travelled.
		-- The allowance is idle drift only; an ACCEPTED order would have sent it to x 70.
		local probeDrift = Probe.Location.X - probeStartX
		if probeDrift > 4 then
			faults[#faults + 1] = string.format("LAYER 1 DID NOT REFUSE THE ORDER: the probe was told"
				.. " to move to x 70, beyond the line, and travelled %d cells east (x %d -> %d)."
				.. " Aircraft.ResolveOrder should have dropped the Move before queuing anything",
				probeDrift, probeStartX, Probe.Location.X)
		end

		local eastDrift = eastStartX - EastProbe.Location.X
		if eastDrift > 4 then
			faults[#faults + 1] = string.format("LAYER 1 IS ONE-DIRECTIONAL: the USA probe was"
				.. " refused but the Russia one travelled %d cells west (x %d -> %d). The wall is"
				.. " meant to be symmetric", eastDrift, eastStartX, EastProbe.Location.X)
		end

		-- The cursor promise. ResolveOrder refuses the cell, so the targeter must have
		-- painted the blocked cursor for it -- otherwise the player is invited to issue an
		-- order that is then silently dropped, which reads as the unit disobeying.
		if legalCursor == illegalCursor then
			faults[#faults + 1] = string.format("THE CURSOR DOES NOT WARN: hovering a cell beyond the"
				.. " line shows %q, the same cursor as a legal cell. AircraftMoveOrderTargeter should"
				.. " paint BlockedCursor for a destination ResolveOrder will refuse",
				illegalCursor)
		end

		-- Layer 2: the approach order was LEGAL, so only the ticked trait can have moved it.
		if not sawReturn then
			faults[#faults + 1] = string.format("LAYER 2 NEVER FIRED: the approach airframe was sent"
				.. " to x 42, inside the turn-back margin on its own side, and no DefconWallReturn"
				.. " ever appeared in its activity chain (ended at x %d, chain %q). Layer 1 has no"
				.. " reason to refuse that order, so nothing else would have turned it back",
				Approach.Location.X, Test.ActivityChain(Approach))
		end

		local summary = string.format("probe x %d->%d | approach x 36->%d (returned=%s) | east x %d->%d"
			.. " | crossings=%d | cursors legal=%q illegal=%q",
			probeStartX, Probe.Location.X, Approach.Location.X, tostring(sawReturn),
			eastStartX, EastProbe.Location.X, crossings, legalCursor, illegalCursor)

		if #faults > 0 then
			Test.Fail(table.concat(faults, " || ") .. " || " .. summary)
		else
			Test.Pass(summary)
		end
	end

	local step
	step = function()
		tick = tick + 1
		sample()

		if tick == ORDER_TICK then
			-- Read both cursors before issuing anything: ClickCursorAtCell issues nothing, and
			-- taking the readings first keeps them independent of what the orders then do.
			legalCursor = Test.ClickCursorAtCell({ Probe }, CPos.New(24, 28))
			illegalCursor = Test.ClickCursorAtCell({ Probe }, CPos.New(70, 28))

			Test.IssueMove(Probe, CPos.New(70, 28))
			Test.IssueMove(Approach, CPos.New(42, 28))
			Test.IssueMove(EastProbe, CPos.New(20, 28))

			Media.DisplayMessage("Orders issued: probe east (illegal), approach to x42 (legal).", "Test")
		end

		if tick >= VERDICT_TICK then
			verdict()
			return
		end

		Trigger.AfterDelay(1, step)
	end

	Trigger.AfterDelay(1, step)
end
