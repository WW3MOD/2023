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

local EvalTicks = 700
local SnapshotEvery = 25

local FullLoad = 2250
local HimarsBatchCost = 1500
local HimarsRoundsPerBatch = 1
local AuraCells = 4

local pollCount = 0

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
			Himars.IsDead and -1 or chebyshev(Himars.Location, StockedDepot.Location)))

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

		if himarsDist > 2 then
			Test.Fail(string.format(
				"measured nothing: the himars is %d cells from the east Centre and never docked, so " ..
				"neither arm had the chance to deliver (ammo=%d spent=%d)",
				himarsDist, himarsAmmo, spent))
			return
		end

		local trickleIsFree = rifleAmmo > 0
		local doubleServe = himarsAmmo > paidRounds

		local summary = string.format(
			"rifleman ammo=%d at a depot holding %d (dist %d) | himars ammo=%d, depot spent %d = %d " ..
			"round(s) paid for (dist %d)",
			rifleAmmo, drained, rifleDist, himarsAmmo, spent, paidRounds, himarsDist)

		print("[who-pays] RESULT " .. summary)

		-- Both predictions held. The run PASSES on the predicted findings so that a future
		-- regression -- someone metering these paths -- turns this red and has to update it
		-- deliberately.
		if trickleIsFree and doubleServe then
			Test.Pass()
			return
		end

		Test.Fail(string.format(
			"AT LEAST ONE PREDICTION REFUTED, which is a finding and not a defect -- read the numbers. " ..
			"free infantry trickle: %s. docked double-serve: %s. %s",
			tostring(trickleIsFree), tostring(doubleServe), summary))
	end)
end
