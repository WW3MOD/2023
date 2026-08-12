-- AUTO TEST: the path a previous worker filed as untested when it landed 3e139294 —
-- "a player's explicit Resupply order on a partially-full unit at an LC still has no terminating
-- condition, and I did not test that path."
--
-- WHY THIS PATH NEEDS ITS OWN SCENARIO. Every other rearm dispatcher refuses a unit that still has
-- ammo: AmmoPool.AutoRearmIfAllEmpty returns unless AllPoolsEmpty, and AutoSeekSupplies' idle seek
-- queues a DIFFERENT activity (SeekSuppliesAndReturn). The only route into AutoRearmIfAnyNotFull ->
-- AutoRearm -> Resupply with a PARTIALLY full unit is the RESUPPLY command-bar button
-- (CommandBarLogic.cs:187), issued here through Test.IssueResupply. The sibling scenario
-- test-lc-errand-ends-when-rearmed-en-route covers the DRY dispatch and passes by ABANDONING the
-- errand en route — it deliberately never lets the unit arrive, so it says nothing about arrival.
--
-- THE CLAIM. Resupply's arrival gate is
--
--     isCloseEnough = (host.CenterPosition - self.CenterPosition).HorizontalLengthSquared
--                         <= closeEnough.LengthSquared               (Resupply.cs:164)
--
-- with closeEnough = WDist.Zero for the LC, because the LC carries no RearmsUnits trait for
-- AmmoPool.cs:374 to read a CloseEnough off. Zero demands EXACT horizontal coincidence with the
-- building's centre, and nothing upstream substitutes a range: the approach is move.MoveOntoTarget
-- (Resupply.cs:240), aimed at CellContaining(host centre); the MoveWithinRange(host, closeEnough)
-- line beside it is RepairableNear-only and no subject here is RepairableNear.
--
-- WHAT THE GATE ACTUALLY TURNS ON. The LC footprint is `=+= +++ =+=` — all '=' (OccupiedPassable)
-- and '+' (OccupiedPassableTransitOnly), no 'x' anywhere — so its centre cell is walk-on-able, not
-- blocked, and for an odd 3x3 the building's CenterPosition IS that cell's centre
-- (BuildingInfo.CenterOffset). The claim's premise ("the building occupies its own centre cell, so
-- nobody can stand there") is therefore false. What survives of it is the ZERO tolerance, which
-- makes arrival turn on the exact SUBCELL the visitor holds. Hence three subjects:
--
--   Bradley           full-cell vehicle                                -> offset (0,0)
--   Rifleman          lone walker, DefaultSubCell = index 3 = (0,0,0)  -> offset (0,0)
--   PairA / PairB     two soldiers sent to ONE depot from equal
--                     distances, so their arrivals overlap and the
--                     second cannot hold the subcell the first is
--                     standing in                                      -> one of them offset != 0?
--
-- The pair is the only way found to produce a non-centre subcell at a depot. Spawning a soldier
-- directly onto the depot's centre cell was tried and did not do it: at world init the building's
-- influence is not yet registered when the soldier picks a subcell, so he still gets DefaultSubCell.
--
-- MEASURING THE MECHANISM, and how the first run of this scenario got it wrong. The LC grants
-- replenish-soldiers within 4c0 (structures.yaml), enabling the infantry ReloadAmmoPool trickle: 1
-- round per 50 ticks, free, needing no docking at all. That trickle starts EN ROUTE, as soon as the
-- walker crosses into the aura — so the FIRST ammo increase is always +1 for infantry and says
-- nothing about whether a dock rearm later happened. The first run recorded exactly that and
-- mislabelled a working dock rearm as "trickle only".
--
-- So this version records the LARGEST single-tick increase instead. A dock rearm arrives one
-- ReloadCount batch at a time (AR 50, Bradley 100); the trickle never exceeds 1. The two cannot be
-- confused whichever order they happen in.
--
-- The second guard is arithmetic: each subject starts exactly TWO batches short, which the trickle
-- cannot close inside the deadline (100 rounds would need 5000 ticks; the deadline is 1000). So
-- "ended full" ALSO means a dock rearm happened, independently of the step-size reading.
--
-- The third is IsIdle. Resupply clears ResupplyType.Rearm only from INSIDE the isCloseEnough
-- branch, so if the gate never passes the activity cannot end however full the pool gets. A unit
-- standing at the LC with full ammo and a live errand is the defect wearing a disguise — and it is
-- the specific thing the filed claim predicts.
--
-- The report distinguishes the two things a reader must never confuse: a unit that never ARRIVED
-- (pathing / dispatch — reported as the closest cell it reached) from a unit that arrived and whose
-- TRANSFER never terminated (the claim). Both verdicts carry the full report, so a PASS records the
-- measurements too rather than throwing them away.
--
-- No enemy on this map and no fire stance touched anywhere (AUTOTEST.md gotcha 7).

local DeadlineSeconds = 40

local elapsed = 0
local subjects = {}

local function abs(v)
	if v < 0 then return -v end
	return v
end

local function track(name, actor, depot, pool, batch)
	local s = {
		Name = name,
		Actor = actor,
		Depot = depot,
		Pool = pool,
		Batch = batch,
		Full = actor.MaximumAmmoCount(pool),
		Start = actor.AmmoCount(pool),
		Last = actor.AmmoCount(pool),
		ClosestSq = -1,
		ClosestDx = 0,
		ClosestDy = 0,
		ClosestCells = 999,
		BiggestRise = 0,
		TicksToFull = -1,
	}

	subjects[#subjects + 1] = s
	return s
end

local function poll(s, tick)
	local ammo = s.Actor.AmmoCount(s.Pool)
	local rise = ammo - s.Last
	if rise > s.BiggestRise then s.BiggestRise = rise end
	s.Last = ammo

	if s.TicksToFull < 0 and ammo >= s.Full then s.TicksToFull = tick end

	-- The gate's own quantity, measured the way the gate measures it.
	local here = s.Actor.CenterPosition
	local there = s.Depot.CenterPosition
	local dx = here.X - there.X
	local dy = here.Y - there.Y
	local distSq = dx * dx + dy * dy

	if s.ClosestSq < 0 or distSq < s.ClosestSq then
		s.ClosestSq = distSq
		s.ClosestDx = dx
		s.ClosestDy = dy
	end

	local cx = abs(s.Actor.Location.X - s.Depot.Location.X - 1)
	local cy = abs(s.Actor.Location.Y - s.Depot.Location.Y - 1)
	local cells = cx
	if cy > cx then cells = cy end
	if cells < s.ClosestCells then s.ClosestCells = cells end

	return ammo >= s.Full and s.Actor.IsIdle
end

local function report(s)
	local ammo = s.Actor.AmmoCount(s.Pool)

	local where
	if s.ClosestCells > 0 then
		where = "NEVER reached the depot centre cell (closest " .. s.ClosestCells
			.. " cells) -- an APPROACH failure, not an arrival-gate failure"
	elseif s.ClosestDx == 0 and s.ClosestDy == 0 then
		where = "stood on the depot centre cell at offset (0,0) from the building centre, so the "
			.. "WDist.Zero gate WAS satisfiable"
	else
		where = "stood on the depot centre cell but at offset (" .. s.ClosestDx .. ","
			.. s.ClosestDy .. ") from the building centre, so the WDist.Zero gate can NEVER pass"
	end

	local how
	if s.BiggestRise <= 0 then
		how = "no ammo ever arrived"
	elseif s.BiggestRise >= s.Batch then
		how = "biggest single-tick gain " .. s.BiggestRise .. " = a DOCK rearm (ReloadCount "
			.. s.Batch .. ")"
	else
		how = "biggest single-tick gain only " .. s.BiggestRise
			.. ", never a whole batch -- the ReloadAmmoPool TRICKLE and never a dock rearm"
	end

	local filled
	if s.TicksToFull >= 0 then
		filled = "full after " .. s.TicksToFull .. " ticks"
	else
		filled = "NEVER filled"
	end

	local doing
	if s.Actor.IsIdle then
		doing = "errand FINISHED"
	else
		doing = "errand STILL RUNNING"
	end

	return s.Name .. ": " .. where .. "; ammo " .. s.Start .. "->" .. ammo .. " of " .. s.Full
		.. "; " .. how .. "; " .. filled .. "; " .. doing
end

local function reportAll()
	local out = ""
	for i, s in ipairs(subjects) do
		if i > 1 then out = out .. "  ||  " end
		out = out .. report(s)
	end

	return out
end

WorldLoaded = function()
	TestHarness.FocusBetween(Rifleman, InfDepot)
	TestHarness.Select(Rifleman)

	track("Bradley[full-cell vehicle]", Bradley, VehDepot, "primary-ammo", 100)
	track("Rifleman[lone walker, centre subcell]", Rifleman, InfDepot, "primary-ammo", 50)
	track("PairA[shares a depot with PairB]", PairA, OffsetDepot, "primary-ammo", 50)
	track("PairB[shares a depot with PairA]", PairB, OffsetDepot, "primary-ammo", 50)

	-- The player's own order, on units that are PARTIALLY full. Nothing else on this map can
	-- dispatch any of them (see rules.yaml), so everything after this is the order's doing.
	Test.IssueResupply(Bradley)
	Test.IssueResupply(Rifleman)
	Test.IssueResupply(PairA)
	Test.IssueResupply(PairB)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Bradley.IsDead or Rifleman.IsDead or PairA.IsDead or PairB.IsDead then
			return "fail: SETUP -- a subject died"
		end

		if InfDepot.IsDead or VehDepot.IsDead or OffsetDepot.IsDead then
			return "fail: SETUP -- a depot died"
		end

		elapsed = elapsed + 1

		local allDone = true
		for _, s in ipairs(subjects) do
			if not poll(s, elapsed) then allDone = false end
		end

		-- Carry the measurements out on BOTH verdicts. AssertWithin's own Pass() drops them.
		if allDone then
			Test.Pass(reportAll())
			return false
		end

		if elapsed >= math.floor((DeadlineSeconds - 1) * TestHarness.TicksPerSecond) then
			return "fail: " .. reportAll()
		end

		return false
	end, "resupply-on-explicit-order never completed (no diagnostic captured)")
end
