-- AUTO TEST — a SUPPLYCACHE holding a SMALL load must still spend it.
--
-- The sibling test-crate-rearm proves a FULL crate rearms a neighbour. This one
-- covers the band that test cannot see: a crate carrying less than the engine's
-- default RestockThreshold (50).
--
-- That threshold exists so a TRUCK can afford the drive back to a Logistics
-- Center — it stops serving and reserves the remainder. A crate has nowhere to
-- drive. With the field left unset, SUPPLYCACHE inherited the truck's reservation
-- and refused to spend its last 49 supply, while RemoveBelowSupply: 1 kept it in
-- the world until supply reached 0 — which serving was the only thing that could
-- have achieved. The crate parked forever, supply bar showing, serving nobody.
--
-- Reachable in play two ways: drained down into the band, or DROPPED into it —
-- DropsSupplyCache seeds a crate with the truck's exact remaining load, and an
-- Evacuate-stance truck serves below its own threshold before unloading.
--
-- Setup (map.yaml): a rifleman (Customer, e3) 2 cells from a SUPPLYCACHE (Crate),
-- same owner. Lua sets the crate to a load inside the band, drains the rifleman,
-- and watches for ammo to come back.
--   PASS = Customer's primary-ammo climbs above the drained baseline.
--   FAIL = ammo never rises (crate withholding a load it will never spend).
--
-- The 2-cell separation is well inside the crate's aura at either the old 4c0 or
-- the new 5c0, so this test is deliberately blind to the radius change and
-- reports only on the withholding defect.

local WINDOW = 20   -- seconds before we give up waiting for a rearm
local LOAD = 30     -- inside the stranded band: above RemoveBelowSupply (1), below RestockThreshold's default (50)

WorldLoaded = function()
	TestHarness.FocusBetween(Customer, Crate)

	Test.SetSupply(Crate, LOAD)
	local load = Test.GetSupply(Crate)

	-- Guard the guard. If the binding silently failed the crate would still be
	-- holding its full 750, this test would pass on a full crate, and it would be
	-- measuring the sibling test's scenario instead of its own.
	if load ~= LOAD then
		Test.Fail(string.format(
			"setup failed: crate holds %d supply, expected %d — the test never entered the band it exists to cover",
			load, LOAD))
		return
	end

	-- Empty the magazine this tick; sample immediately so the baseline is taken
	-- before the crate can act. Reload uses a signed delta.
	Customer.Reload("primary-ammo", -9999)
	local baseline = Customer.AmmoCount("primary-ammo")

	TestHarness.AssertWithin(WINDOW, function()
		if Customer.IsDead then return "fail: Customer died before rearm" end
		if Crate.IsDead then return "fail: Crate despawned or died before rearm" end
		if Customer.AmmoCount("primary-ammo") > baseline then return true end
		return false
	end, string.format(
		"a crate holding %d supply never rearmed the rifleman within %ds — primary-ammo stayed at " ..
		"baseline %d while the crate sat on a load it refused to spend (RestockThreshold reserving a " ..
		"drive home the crate does not have)",
		load, WINDOW, baseline))
end
