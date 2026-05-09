-- AUTO TEST: Abrams ordered to AttackMove past a t90 must engage and fire on
-- the t90 before reaching its destination.
--
-- Setup: Hunter (abrams, FireAtWill) at (12,16). Target (t90, HoldFire) at
-- (22,16) — directly on the AttackMove path. Abrams range = 25 cells, so
-- the t90 is in weapon range from the start.

local DeadlineSeconds = 12

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, Target)
	TestHarness.Select(Hunter)

	Target.Stance = "HoldFire"

	-- AttackMove past the t90 (Lua's AttackMove maps to AttackMoveActivity).
	Hunter.AttackMove(CPos.New(40, 16))

	local startingAmmo = Hunter.AmmoCount("primary-ammo")
	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Hunter.IsDead then return "fail: Hunter died first" end
		return Hunter.AmmoCount("primary-ammo") < startingAmmo
	end, "Abrams did not engage t90 on attack-move within " .. DeadlineSeconds .. "s")
end
