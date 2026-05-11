-- AUTO TEST: Paladin (m109) ordered to attack-move past a t90 must stop,
-- complete its setup countdown, and fire on the t90.
--
-- Paladin specifics:
--   • ArtilleryRound: Range 40c0, MinRange 10c0
--   • AttackTurreted: HoldFireWhileMoving=True, SetupTicks=25
--   • Turreted: RealignWhileMoving=True (locks turret while driving)
--   • Mobile.Speed=80, AimingDelay=35
-- Setup: Paladin at (8,16), target at (30,16) — distance ~22 cells (in band).
-- AttackMove dest is (50,16). Paladin should detect the t90, cancel move,
-- stop, run setup (25 ticks ≈ 1s), then fire (AimingDelay 35 ticks ≈ 1.4s).

local DeadlineSeconds = 20

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, Target)
	TestHarness.Select(Hunter)

	Target.Stance = "HoldFire"

	Hunter.AttackMove(CPos.New(50, 16))

	local startingAmmo = Hunter.AmmoCount("primary-ammo")
	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Hunter.IsDead then return "fail: Hunter died first" end
		return Hunter.AmmoCount("primary-ammo") < startingAmmo
	end, "Paladin did not engage t90 on attack-move within " .. DeadlineSeconds .. "s")
end
