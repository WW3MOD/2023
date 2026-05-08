-- AUTO TEST: Paladin fires on the t90 within a deadline.
-- No human input — TestHarness.AssertWithin polls until Pass or timeout-Fail.

local DeadlineSeconds = 12

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin, Target)
	TestHarness.Select(Paladin)

	-- Target stands still (Paladin's auto-target is slow vs T90's gun, the
	-- t90 would otherwise outshoot the setup-aim cycle).
	Target.Stance = "HoldFire"

	-- Force-engage so we test the firing pipeline (turret rotate, setup-aim,
	-- fire) in isolation from autotarget's scan-interval timing.
	Paladin.Attack(Target, true, false)

	local startingAmmo = Paladin.AmmoCount("primary-ammo")
	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Paladin.IsDead then
			return "fail: Paladin died before firing"
		end
		return Paladin.AmmoCount("primary-ammo") < startingAmmo
	end, "Paladin did not fire within " .. DeadlineSeconds .. "s")
end
