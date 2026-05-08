-- AUTO TEST: Paladin fires on the t90 within a deadline.
-- Pure self-verifying — no human verdict needed. Demonstrates Tier 2.
--
-- Strategy:
--   - Wait 25 ticks (1s) for the world to settle (autotarget scan, etc.)
--   - Then assert that the Paladin's primary ammo count drops below max
--     within 8 seconds. The only way that happens is if the armament fired.

local DeadlineSeconds = 8

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin, Target)
	TestHarness.Select(Paladin)

	local startingAmmo = Paladin.AmmoCount("primary")

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Paladin.IsDead then
			return "fail: Paladin died before firing"
		end
		return Paladin.AmmoCount("primary") < startingAmmo
	end, "Paladin did not fire within " .. DeadlineSeconds .. "s")
end
