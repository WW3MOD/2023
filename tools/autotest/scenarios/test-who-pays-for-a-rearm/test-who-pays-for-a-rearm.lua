-- AUTO TEST: two questions about who pays for a rearm, answered in one run.
--
-- Both were derived from reading tick loops and both shape a design decision about metering the
-- dock path, so both are measured before that design is fixed.
--
-- QUESTION 1 — is the infantry proximity trickle free? LOGISTICSCENTER carries
-- ProximityExternalCondition@ReplenishSoldiers (Range 4c0) with no supply term, and every soldier
-- declares ReloadAmmoPool gated on the condition it grants. ReloadAmmoPool is stock OpenRA: a timed
-- Reload with no range check and no supply accounting. If that reading is right, a rifleman inside
-- four cells of a Centre holding NOTHING refills anyway. The Centre's other infantry arm — the
-- SupplyProvider aura — is metered and provably cannot deliver here: SupplyProvider.cs:968 skips
-- any pool whose SupplyValue exceeds currentSupply, and the rifleman's SupplyValue is 1 against a
-- depot at 0. So ammunition arriving at all means an unmetered source exists.
--
-- QUESTION 2 — while docked, do BOTH arms deliver to a himars? It is one of two actors declaring
-- replenish-vehicles, so the Centre's metered push arm can select it; and it is also on a Resupply
-- activity, whose RearmTick hands out ammunition free. If both run concurrently the unit is today
-- double-REARMED, and metering the dock path without separating them would convert that into
-- double-CHARGING on two independent cadences.
--
-- THE DISCRIMINATOR IS ARITHMETIC, NOT TIMING, which is what makes one run enough. himars: Ammo 2,
-- ReloadCount 1, SupplyValue 1500. The Centre holds 2250. The push arm can afford exactly ONE round
-- (2250 -> 750, and 750 < 1500 stops it). So:
--     rounds gained == rounds paid for   -> only the push arm delivers. No double-serve.
--     rounds gained >  rounds paid for   -> the surplus was unpaid. Double-serve confirmed.
-- "Rounds paid for" is read from the depot itself: spent/1500 batches, one round each.
--
-- WHAT WOULD MAKE THIS RUN WORTHLESS, guarded below rather than hoped for: a rifleman standing
-- outside 4c0 gains nothing and reads as a clean refutation; a himars that never reaches the Centre
-- gains nothing for a reason that has nothing to do with either arm; and a drained depot that is
-- not actually drained makes question 1 meaningless. All three are asserted at evaluation time.

local EvalTicks = 1100
local SnapshotEvery = 25

local FullLoad = 2250
local HimarsBatchCost = 1500
local HimarsRoundsPerBatch = 1
local AuraCells = 4

local pollCount = 0
local peakHimarsDistance = 0
local himarsEverDocked = false
local himarsErrandEnded = false

local function chebyshev(a, b)
	local dx = a.X - b.X
	local dy = a.Y - b.Y
	if dx < 0 then dx = -dx end
	if dy < 0 then dy = -dy end
	return dx > dy and dx or dy
end

WorldLoaded = function()
	TestHarness.FocusBetween(Rifleman, DrainedDepot)

	Test.SetSupply(DrainedDepot, 0)
	Test.SetSupply(StockedDepot, FullLoad)

	if Test.GetSupply(DrainedDepot) ~= 0 or Test.GetSupply(StockedDepot) ~= FullLoad then
		Test.Fail(string.format(
			"setup failed: depots read %d (want 0) and %d (want %d)",
			Test.GetSupply(DrainedDepot), Test.GetSupply(StockedDepot), FullLoad))
		return
	end

	if Rifleman.AmmoCount("primary-ammo") ~= 0 or Himars.AmmoCount("primary-ammo") ~= 0 then
		Test.Fail("setup failed: a subject did not start dry, so ammunition appearing later is not " ..
			"necessarily ammunition it was given")
		return
	end

	-- The himars is sent to the depot BY NAME. Every ordinary route runs ChooseResupplier first,
	-- and this is about what happens once docked rather than about how it got there.
	Test.IssueResupplyAt(Himars, StockedDepot)

	-- Self-rescheduling snapshot. There is NO Trigger.OnTick in this engine — TriggerGlobal exposes
	-- AfterDelay and a list of event hooks, and nothing per-tick — so a periodic trace has to be a
	-- delay that re-arms itself. A run was spent discovering that; do not "simplify" this back.
	local function snapshot()
		pollCount = pollCount + SnapshotEvery
		print(string.format(
			"[who-pays] tick=%d | rifle ammo=%d drained=%d dist=%d | himars ammo=%d stocked=%d dist=%d",
			pollCount,
			Rifleman.IsDead and -1 or Rifleman.AmmoCount("primary-ammo"),
			Test.GetSupply(DrainedDepot),
			Rifleman.IsDead and -1 or chebyshev(Rifleman.Location, DrainedDepot.Location),
			Himars.IsDead and -1 or Himars.AmmoCount("primary-ammo"),
			Test.GetSupply(StockedDepot),
			Himars.IsDead and -1 or chebyshev(Himars.Location, StockedDepot.Location),
			tostring(not Himars.IsDead and Himars.IsIdle)))

		local hd = Himars.IsDead and -1 or chebyshev(Himars.Location, StockedDepot.Location)
		if hd >= 0 and hd <= 2 then himarsEverDocked = true end
		if himarsEverDocked and hd > peakHimarsDistance then peakHimarsDistance = hd end

		-- THE DISCRIMINATOR. Distance cannot serve here: nothing moves an undocked ground vehicle away
		-- from a Logistics Centre. Resupply.OnResupplyEnding takes the rally path only when
		-- rp.Path.Count > 0, LOGISTICSCENTER declares a bare `RallyPoint:` and RallyPointInfo.Path
		-- defaults to empty, and vehicles are Repairable rather than RepairableNear — so it falls to
		-- MoveToTarget(self, host), which moves the unit TOWARD the depot. A correct run therefore ends
		-- with the himars about a cell away, and a distance test would have called that the wedge.
		-- IsIdle separates them cleanly: a wedged client never leaves Resupply.Tick so it never goes
		-- idle; a client whose errand ENDED does, whether it then walks off or holds position.
		if himarsEverDocked and not Himars.IsDead and Himars.IsIdle then himarsErrandEnded = true end

		if pollCount < EvalTicks then
			Trigger.AfterDelay(SnapshotEvery, snapshot)
		end
	end

	Trigger.AfterDelay(SnapshotEvery, snapshot)

	Trigger.AfterDelay(EvalTicks, function()
		if Rifleman.IsDead or Himars.IsDead then
			Test.Fail("a subject left the world before evaluation, so neither question was measured")
			return
		end

		local rifleAmmo = Rifleman.AmmoCount("primary-ammo")
		local rifleDist = chebyshev(Rifleman.Location, DrainedDepot.Location)
		local drained = Test.GetSupply(DrainedDepot)

		local himarsAmmo = Himars.AmmoCount("primary-ammo")
		local himarsDist = chebyshev(Himars.Location, StockedDepot.Location)
		local stocked = Test.GetSupply(StockedDepot)
		local spent = FullLoad - stocked
		local paidRounds = math.floor(spent / HimarsBatchCost) * HimarsRoundsPerBatch

		-- Guards first. Each of these turns an apparent answer into "measured nothing".
		if rifleDist > AuraCells then
			Test.Fail(string.format(
				"measured nothing: the rifleman is %d cells from the Centre, outside the %d-cell aura, " ..
				"so gaining no ammunition says nothing about whether the trickle is free",
				rifleDist, AuraCells))
			return
		end

		if drained ~= 0 then
			Test.Fail(string.format(
				"measured nothing: the west Centre holds %d supply rather than 0, so the rifleman may " ..
				"simply have been served by the metered aura", drained))
			return
		end

		-- Reads the LATCH, not the current distance: a himars far from the depot at the deadline is
		-- the PASS case now, and asking "is it near" would fail the very outcome under test. What
		-- still ruins the run is one that never got there at all.
		if not himarsEverDocked then
			Test.Fail(string.format(
				"measured nothing: the himars never came within 2 cells of the east Centre, so neither " ..
				"arm had the chance to deliver and nothing about undocking was observed (dist=%d ammo=%d spent=%d)",
				himarsDist, himarsAmmo, spent))
			return
		end

		-- RE-POINTED 2026-08-27. This scenario used to assert the two FREE routes existed, which was
		-- the right question while they did. Both are now closed, so the question that matters is the
		-- one the charging change can get wrong: does a unit that has taken everything the depot can
		-- pay for LEAVE, or does it stand at the dock forever?
		--
		-- That wedge is not hypothetical. The first cut of the fix had Rearmable.RearmTick defer to
		-- SupplyProvider.CanSelect, and CanSelect carried no supply term -- so a himars that had taken
		-- its one affordable round was still "owned" by the push arm, deferred forever, and never
		-- exited. Once docked, RearmTick returning true is the ONLY way out (Resupply.cs:301); the
		-- SelfAssignedErrandIsOver escape at Resupply.cs:240 is gated on !actualResupplyStarted and is
		-- unreachable after arrival. So "still docked at the deadline" IS the defect, and undocking is
		-- the repair. One run catches both.
		--
		-- THE HIMARS IS THE SUBJECT because the wedge needs a client the push arm owns AND cannot
		-- finish serving. Its pool costs 1500 a batch against a 2250 depot: one round is affordable,
		-- the second never is, so it is guaranteed to end non-full at a depot holding 750.
		local himarsLeft = himarsErrandEnded
		local riflemanStayedDry = rifleAmmo == 0

		local summary = string.format(
			"rifleman ammo=%d at a depot holding %d (dist %d) | himars ammo=%d, depot spent %d = %d " ..
			"round(s) paid for, dist %d, peak-dist %d",
			rifleAmmo, drained, rifleDist, himarsAmmo, spent, paidRounds, himarsDist, peakHimarsDistance)
		summary = summary .. string.format(" errand-ended=%s idle=%s",
			tostring(himarsErrandEnded), tostring(not Himars.IsDead and Himars.IsIdle))

		print("[who-pays] RESULT " .. summary)

		-- The charged world, both halves. The rifleman must gain NOTHING at a depot holding nothing
		-- (the free trickle is gone), and the himars must take what it can pay for and then DEPART.
		if riflemanStayedDry and himarsLeft and himarsAmmo == paidRounds then
			Test.Pass()
			return
		end

		local why
		if not riflemanStayedDry then
			why = "the rifleman gained ammunition at a depot holding zero, so a free infantry route survives"
		elseif himarsAmmo ~= paidRounds then
			why = string.format(
				"the himars holds %d round(s) but the depot only paid for %d, so an unmetered route survives",
				himarsAmmo, paidRounds)
		else
			why = "the himars took its affordable round and its Resupply errand NEVER ENDED -- it is " ..
				"wedged at the depot, combat-inert and withheld from every bot module by " ..
				"StarvingRecruitGate. This is the CanSelect-without-affordability defect, or its return. " ..
				"NOTE it is expected to remain NEAR the depot even when correct; the verdict is IsIdle, " ..
				"not distance"
		end

		Test.Fail(why .. ". " .. summary)
	end)
end
