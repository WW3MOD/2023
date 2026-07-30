-- AUTO TEST — a dropped SUPPLYCACHE must rearm a nearby friendly unit.
--
-- Setup (map.yaml): a rifleman (Customer, e3) 2 cells from a full SUPPLYCACHE
-- (Crate), same owner. The Lua drains Customer's primary-ammo to empty in the
-- same tick, records that as the baseline, then watches for the crate to push
-- ammo back in. A working cache behaves like the truck it was dropped from.
--   PASS = Customer's primary-ammo climbs above the drained baseline.
--   FAIL = ammo never rises (crate inert), or the unit/crate dies. The failure
--          message reports the baseline so a no-op drain (baseline == full) is
--          distinguishable from a genuinely inert crate (baseline == 0).

local WINDOW = 20   -- seconds before we give up waiting for a rearm

WorldLoaded = function()
	TestHarness.FocusBetween(Customer, Crate)

	-- Empty the rifle magazine this tick; sample the drained level immediately so
	-- the baseline is taken before the crate can act. Reload uses a signed delta.
	Customer.Reload("primary-ammo", -9999)
	local baseline = Customer.AmmoCount("primary-ammo")

	TestHarness.AssertWithin(WINDOW, function()
		if Customer.IsDead then return "fail: Customer died before rearm" end
		if Crate.IsDead then return "fail: Crate died before rearm" end
		if Customer.AmmoCount("primary-ammo") > baseline then return true end
		return false
	end, string.format(
		"crate did not rearm the rifleman within %ds — primary-ammo stayed at baseline %d",
		WINDOW, baseline))
end
