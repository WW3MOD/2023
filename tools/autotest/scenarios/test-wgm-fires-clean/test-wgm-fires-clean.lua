-- AUTO TEST: Bradley fires its WGM missile (secondary armament) at the
-- t90 within a deadline. No trees on the line — purely a regression check
-- that the new tree-density gate doesn't break the clean-shot case.

local DeadlineSeconds = 12

WorldLoaded = function()
	TestHarness.FocusBetween(Bradley, Target)
	TestHarness.Select(Bradley)

	-- Target stands still so it doesn't run away or kill the Bradley first.
	Target.Stance = "HoldFire"

	-- allowMove=false: Bradley stays put. WGM is the only in-range weapon
	-- (25mm range 20c < distance 22c < WGM range 25c).
	Bradley.Attack(Target, false, false)

	local startingAmmo = Bradley.AmmoCount("secondary-ammo")
	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Bradley.IsDead then
			return "fail: Bradley died before firing"
		end
		return Bradley.AmmoCount("secondary-ammo") < startingAmmo
	end, "Bradley did not fire WGM within " .. DeadlineSeconds .. "s")
end
