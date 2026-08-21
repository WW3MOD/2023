-- AUTO TEST: a dry rifleman must walk to a dropped SUPPLYCACHE, not just to a truck.
--
-- USER RULING, 2026-08-21: infantry should seek crates. Before it, `Rearmable.RearmActors` named
-- only `truk, logisticscenter`, and that list is the sole filter in AmmoPool.ChooseResupplier — the
-- only host-discovery path in the engine. A crate was therefore invisible to every seek: the scan
-- returned null, AutoSeekSupplies raised NeedsResupply and left the man standing, and the crate
-- served only whoever happened to already be inside its aura.
--
-- DELIBERATE SIBLING of test-dry-resupply-reaches-truck. Same map, same players, same geometry,
-- same dry AR, same HoldFire Bait — ONE actor differs, `truk` becoming `supplycache`. That is the
-- whole point: the two run as a matched pair, so if the truck arm is green and this one is red, the
-- difference is the crate and nothing else. Do not "simplify" this scenario by deleting the Bait or
-- the enemy; they are what make it comparable to the test that already passes.
--
-- Nothing here issues an order. The rifleman starts with every pool empty and the shipped traits do
-- the rest: AmmoPool.AutoRearmIfAllEmpty / AutoSeekSupplies.ReturnWhenEmpty pick the host and queue
-- SeekSupplyProvider.
--
-- Deadline: ~9 cells at roughly 41 ticks per cell (~15s), plus the errand's dispatch cadence
-- (EmptyScanInterval 25) and RearmDelay. 45s matches the sibling; the trip is either made promptly
-- or not at all.

local DeadlineSeconds = 45

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, Supply)
	TestHarness.Select(Hunter)

	-- Scenery whose only job is to be acquirable; it must not shoot the subject of the measurement
	-- out from under it. The Hunter stays FireAtWill, as in the sibling.
	Bait.Stance = "HoldFire"

	-- Guard the guard. A crate is born at TotalSupply, but if it ever spawned empty ChooseResupplier
	-- would skip it on `CurrentSupply > 0` and this test would fail for a reason that has nothing to
	-- do with the seek list it exists to cover.
	local load = Test.GetSupply(Supply)
	if load <= 0 then
		Test.Fail(string.format("setup failed: the crate holds %d supply, so it is not a candidate host at all", load))
		return
	end

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Hunter.IsDead then return "fail: Hunter died before reaching the crate" end
		if Supply.IsDead then return "fail: the supply crate died or despawned" end

		-- Ammo arriving is the only proof he actually closed to within the crate's push aura.
		-- Position alone would not distinguish "walked there" from "nudged".
		return Hunter.AmmoCount("primary-ammo") > 0
	end, "The dry rifleman never reached the dropped supply crate -- if the truck sibling is green, " ..
		"`supplycache` is missing from infantry Rearmable.RearmActors and the crate is invisible to the seek")
end
