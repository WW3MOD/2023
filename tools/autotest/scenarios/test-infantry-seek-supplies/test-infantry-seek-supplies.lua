-- AUTO TEST — an idle, low-ammo rifleman seeks supplies and comes home.
--
-- Setup (map.yaml): rifleman (Customer, e3) at 18,16; full supply truck (Truck)
-- at 30,16, i.e. 12 cells east. The truck's push aura is 5c0, so it cannot
-- reach him where he stands; the hunt leash is 20 cells, so it is worth walking
-- to. rules.yaml flips AutoSeekSupplies.Enabled on for ^Soldier.
--
-- The drain is deliberately PARTIAL (10 of 100 rifle rounds, RPG left loaded).
-- A unit with every pool empty is already walked to a resupplier by the
-- pre-existing AmmoPool.AutoRearmIfAllEmpty path, which would make this test
-- pass with the new trait switched off. At 10% with the RPG still loaded that
-- path is inert, so the only thing that can move him is the behaviour under test.
--
--   PASS = rifle ammo climbs above the drained baseline (he reached the aura),
--          and he then ends up back within 2 cells of where he started.
--   FAIL = he never rearms (never left, or never arrived), or he rearms but
--          never comes home (the return leg is the point of the activity).

local WINDOW = 60          -- seconds for the full round trip
local HOME_TOLERANCE = 2   -- cells; the return move settles for a nearby cell if 18,16 is taken

WorldLoaded = function()
	TestHarness.FocusBetween(Customer, Truck)
	TestHarness.Select(Customer)

	local home = { X = Customer.Location.X, Y = Customer.Location.Y }

	-- Leave 10 of 100 rifle rounds: below the 25% seek threshold, above empty.
	Customer.Reload("primary-ammo", -90)
	local baseline = Customer.AmmoCount("primary-ammo")

	local rearmed = false

	TestHarness.AssertWithin(WINDOW, function()
		if Customer.IsDead then return "fail: Customer died during the supply run" end
		if Truck.IsDead then return "fail: Truck died during the supply run" end

		if not rearmed then
			if Customer.AmmoCount("primary-ammo") > baseline then
				rearmed = true
			end

			return false
		end

		local dx = math.abs(Customer.Location.X - home.X)
		local dy = math.abs(Customer.Location.Y - home.Y)
		return dx <= HOME_TOLERANCE and dy <= HOME_TOLERANCE
	end, string.format(
		"rifleman did not complete the supply run within %ds (baseline %d) — " ..
		"he either never walked to the truck, or rearmed and never returned home",
		WINDOW, baseline))
end
