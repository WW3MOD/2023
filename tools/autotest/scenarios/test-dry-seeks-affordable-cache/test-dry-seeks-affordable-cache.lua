-- AUTO TEST: a dry unit must walk to a cache that can PAY him, not to the nearest cache that
-- merely has something in it.
--
-- THE REPORT. "I just saw a mortar soldier that was out of ammo but still autotargeted an enemy
-- instead of rearming at a nearby supply cache." Autotargeting is the symptom, not the defect:
-- every AutoTarget entry point is idle-gated (AutoTarget.cs:645, :696), so it can never interrupt a
-- resupply walk and can only ever fill the silence left by a dispatch that declined. The question
-- is therefore why dispatch declined, and this scenario stages the answer.
--
-- THE DEFECT, in three places that all ask "> 0" where they mean ">= one batch":
--   1. AutoSeekSupplies.cs -- the ReturnWhenEmpty arm chose its host with AmmoPool.ChooseResupplier,
--      which filters on CurrentSupply > 0. A cache holding 1..39 is a legal destination that can
--      never serve a mortar, whose batch costs 40.
--   2. AutoSeekSupplies.cs -- it then called AmmoPool.AutoRearm WITHOUT passing that host, so
--      AutoRearm re-picked via ChooseResupplier one call deeper. The `host` parameter's own doc
--      comment (AmmoPool.cs:882-887) exists to prevent exactly this.
--   3. SeekSupplyProvider.cs -- TargetValid and FindBest use the same "> 0" test, so even a unit
--      dispatched to the affordable cache re-picks the poor one on its next 25-tick retarget.
--
-- All three are the SAME defect the engine already fixed once on the AmmoPool arm, where
-- ChooseAffordableResupplier and SupplyHuntMath.SelectNearestAffordable were written precisely
-- because filtering the already-chosen nearest host strands a unit whenever a nearer host is too
-- poor and a farther one is not. This scenario is that case, in the arm that never got the fix.
--
-- WHERE THE UNIT ENDS UP PRE-FIX, and why it looks like "he just stood there": he walks to
-- PoorCache, gets inside its 5c0 aura, and parks in SeekSupplyProvider's in-range branch
-- (SeekSupplyProvider.cs:202-211), which stands still waiting for a push that can never come.
-- AutoSeekSupplies' stall guard cancels it after ReturnErrandStallTicks and blocks retry for
-- ReturnErrandRetryTicks, and in that gap the man is idle and dry -- so AutoTarget engages.
--
-- THE BOUNDARY IS THE MEASUREMENT. PoorCache holds 39 and RichCache 45 against a batch price of 40
-- (mt's single Essential pool, infantry.yaml). One supply either side of the price is the whole
-- difference between the two caches, so a run cannot pass by being generous about distance: the
-- nearer cache is nearer under every metric and is rejected on price alone.
--
-- THE FIRST CUT OF THIS SCENARIO PASSED PRE-FIX, and the reason is worth keeping. It put the rich
-- cache 11 cells away, inside AutoSeekSupplies' 20-cell IDLE leash. The idle seek picks on
-- affordability already (CanServe checks CurrentSupply >= SupplyValue), so it simply walked the
-- mortar east and rearmed him: the run measured the arm that was never broken and reported green on
-- the defective build. The rich cache is now at 23 cells, between the two leashes, so the correct
-- dispatcher cannot reach it. If this scenario ever goes green on a build where the break-off arm is
-- still choosing on stock alone, check that distance FIRST.
--
-- WHAT THIS SCENARIO DOES NOT PIN. It proves the three "> 0" sites together stop stranding the unit;
-- it does not isolate which one. In particular it stays green with the FindBest LEASH reverted, and
-- with AutoSeekSupplies.CanServe restored to its own inline copy of the affordability test -- both of
-- those were added as hardening after review, and neither is load-bearing for this geometry. The
-- leash in particular wants its own scenario: a host affordable but very far, re-picked mid-errand,
-- asserting the unit does NOT set off across the map. That has not been written or run.
--
-- WHAT MAKES A GREEN MEAN SOMETHING. The predicate is "ammo arrived", but ammo alone cannot say
-- WHICH cache paid, and a fix that simply made everything affordable would also turn it green. So
-- PoorCache's load is asserted to be UNTOUCHED for the whole run: at 39 against a 40 batch,
-- AmmoPool.TryServeBatch must refuse it every time it is asked. If that number ever moves, the
-- affordability gate has broken in the permissive direction and the run fails with its own message
-- rather than passing quietly.

-- WHY THIS ONE IS LONG. The rich cache has to sit outside the idle seek's 20-cell leash (map.yaml),
-- so the walk under test is inherently 20+ cells. Measured at ~50 ticks/cell for a mortar in the
-- first run of this scenario, 23 cells is ~1150 ticks ~= 69 real seconds. TestHarness seconds are
-- 1.5x real (AUTOTEST.md), so 70 here is ~105 real seconds -- about a third of margin over the walk.
-- Do not trim this toward the measured walk time: a deadline that only just covers the journey turns
-- a slow path into a red run and hides whatever actually changed.
local DeadlineSeconds = 70
local AmmoPoolName = "primary-ammo"

-- One short of the 40 a mortar batch costs: stocked, and unable to serve.
local PoorLoad = 39
-- One batch plus change: affordable, and it survives paying (RemoveBelowSupply is 1).
local RichLoad = 45

local pollCount = 0
local peakAmmo = 0
local minDistPoor = 999
local minDistRich = 999

local function ChessboardTo(actor, host)
	local dx = actor.Location.X - host.Location.X
	local dy = actor.Location.Y - host.Location.Y
	if dx < 0 then dx = -dx end
	if dy < 0 then dy = -dy end
	if dx > dy then return dx end
	return dy
end

WorldLoaded = function()
	TestHarness.FocusBetween(PoorCache, RichCache)
	TestHarness.Select(Gunner)

	Test.SetSupply(PoorCache, PoorLoad)
	Test.SetSupply(RichCache, RichLoad)

	-- Guard the guards. A silently failed binding would leave both caches holding their full 750,
	-- both affordable, and the run would measure nothing at all while still writing `pass`.
	local poor = Test.GetSupply(PoorCache)
	local rich = Test.GetSupply(RichCache)
	if poor ~= PoorLoad or rich ~= RichLoad then
		Test.Fail(string.format(
			"setup failed: caches hold %d (poor) and %d (rich), expected %d and %d -- the run never " ..
			"entered the stocked-but-unaffordable band it exists to cover", poor, rich, PoorLoad, RichLoad))
		return
	end

	local startingAmmo = Gunner.AmmoCount(AmmoPoolName)
	if startingAmmo ~= 0 then
		Test.Fail(string.format(
			"setup failed: the mortar starts with %d rounds, so it is not dry and no dispatcher ever " ..
			"runs", startingAmmo))
		return
	end

	TestHarness.AssertWithin(DeadlineSeconds, function()
		-- An out-of-world actor reads as dead, so this catches an evacuation departure as well as a
		-- real death. Either is a different disposition entirely and must not be read as an outcome
		-- of the affordability pick.
		if Gunner.IsDead then
			return "fail: the mortar left the world -- it evacuated for a refund rather than walking " ..
				"to a cache, so this run measured the evacuation fallback and not the host choice"
		end

		if PoorCache.IsDead then return "fail: the poor cache despawned" end
		if RichCache.IsDead then return "fail: the rich cache despawned" end

		local poorNow = Test.GetSupply(PoorCache)
		if poorNow < PoorLoad then
			return string.format(
				"fail: the poor cache paid out (%d, was %d). Holding one less than a batch costs, it " ..
				"must never be able to serve -- affordability has broken in the permissive direction",
				poorNow, PoorLoad)
		end

		local ammo = Gunner.AmmoCount(AmmoPoolName)
		if ammo > peakAmmo then peakAmmo = ammo end

		local dPoor = ChessboardTo(Gunner, PoorCache)
		local dRich = ChessboardTo(Gunner, RichCache)
		if dPoor < minDistPoor then minDistPoor = dPoor end
		if dRich < minDistRich then minDistRich = dRich end

		pollCount = pollCount + 1
		if pollCount % 50 == 0 then
			-- Live numbers here, never in the failure string: Lua evaluates that eagerly at
			-- registration and would report the starting values for the whole run.
			print(string.format(
				"[afford] poll=%d ammo=%d peak=%d poorSupply=%d richSupply=%d dPoor=%d minPoor=%d dRich=%d minRich=%d",
				pollCount, ammo, peakAmmo, poorNow, Test.GetSupply(RichCache),
				dPoor, minDistPoor, dRich, minDistRich))
		end

		-- Ammunition is the verdict and it is exclusive, which was verified rather than assumed. The
		-- mortar's ReloadAmmoPool is gated on `replenish-soldiers`, and a SupplyProvider grants that
		-- condition only to a client its accept test admits -- AcceptClient returns Unaffordable
		-- (SupplyProvider.cs:750) when it cannot pay for a batch of anything the client wants. So a
		-- cache too poor to serve does not hand out a free trickle either, and a round can only have
		-- come from a cache that both accepted him and paid. WHICH cache is settled by the
		-- untouched-PoorCache assertion above, not by this line.
		return ammo > 0
	end, "The dry mortar never got a round. Read the [afford] trace to tell the shapes apart. " ..
		"minPoor small (~4-5, inside the 5c0 aura) with minRich still ~23 is the DEFECT: he was " ..
		"dispatched to the nearer cache that cannot pay him and parked in SeekSupplyProvider's " ..
		"in-range branch. Both distances unchanged from ~7 and ~23 means he never set off at all, " ..
		"which is a different bug -- dispatch declined outright -- and this scenario is not measuring " ..
		"what it was written for.")
end
