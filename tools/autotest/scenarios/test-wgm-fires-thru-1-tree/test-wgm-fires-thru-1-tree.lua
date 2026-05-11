-- AUTO TEST: 1 tree on the line is "free" — Bradley should still fire its
-- WGM. Density-miss formula: excess = density (1) - FreeLineDensity (1) = 0,
-- so no miss roll. Blockable=false on the projectile means the missile passes
-- the tree in flight. Verifies the gate's free-pass tier.

local DeadlineSeconds = 12

WorldLoaded = function()
	TestHarness.FocusBetween(Bradley, Target)
	TestHarness.Select(Bradley)

	Target.Stance = "HoldFire"
	Bradley.Attack(Target, false, false)

	local startingAmmo = Bradley.AmmoCount("secondary-ammo")
	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Bradley.IsDead then
			return "fail: Bradley died before firing"
		end
		return Bradley.AmmoCount("secondary-ammo") < startingAmmo
	end, "Bradley did not fire WGM within " .. DeadlineSeconds .. "s")
end
