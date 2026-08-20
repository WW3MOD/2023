-- AUTO TEST: an ORDINARY attack order on a critically wounded soldier must fire.
--
-- THE BUG, in the user's words: "even when I manually click an attack order to
-- them my soldiers refused to fire."
--
-- MECHANISM. AutoTargetInfo.BreakOffCondition ("critical-damage") is meant to
-- express a PREFERENCE — shoot the healthy man first — but Attack.TickAttack
-- read it as target validity and returned UnableToAttack for every non-force
-- attack on such a target. A player's ordinary attack order reaches that line as
-- AttackSource.Default / forceAttack=false (AttackBase.cs:495), so the order was
-- accepted by the targeting layer, the soldier closed to range, and then never
-- fired. Only Ctrl+click worked. Measured independently in
-- test-aa-breakoff-critical on 2026-08-10: "auto ----, normal manual ----,
-- force attack FIRE".
--
-- WHAT MUST STILL BE TRUE AFTER THE FIX: deprioritisation is the behaviour the
-- user wants KEPT, and it lives in AutoTarget.ChooseTarget, which still skips
-- critically damaged candidates. That is also what makes this measurement
-- attributable — see below.
--
-- ATTRIBUTION. Who else could drop the shooter's ammo? Nobody. The wounded
-- conscript is the only attackable enemy on the map (the Russian supply route
-- carries NoAutoTarget), and the shooter cannot auto-acquire him precisely
-- because the ChooseTarget skip is untouched by this fix. So the only path to a
-- fired round is the manual order this test issues.

local DeadlineSeconds = 15

-- 15% of max: comfortably inside the Critical band (HP*100 < MaxHP*25,
-- Health.cs:95) with room for the bleed-out ramp to keep eating into it.
local CriticalFraction = 15

WorldLoaded = function()
	TestHarness.FocusBetween(Shooter, Wounded)
	TestHarness.Select(Shooter)

	Wounded.Health = math.floor(Wounded.MaxHealth * CriticalFraction / 100)

	local startingAmmo = Shooter.AmmoCount("primary-ammo")

	-- The whole point of the test: forceAttack = false. This is the plain click,
	-- NOT Ctrl+click, and it is the one the user reported as ignored.
	Shooter.Attack(Wounded, true, false)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Shooter.IsDead then
			return "fail: SETUP INVALID: the shooter died before firing"
		end

		-- Prove the setup actually took, rather than assuming it. A target that
		-- is NOT critical is attacked fine on both builds, so this scenario would
		-- pass while measuring nothing at all. Checked every tick because the
		-- bleed-out ramp keeps him moving — he must stay inside the band.
		if not Wounded.IsDead and Wounded.Health * 100 >= Wounded.MaxHealth * 25 then
			return "fail: SETUP INVALID: the target is not at critical damage " ..
				"(HP " .. Wounded.Health .. "/" .. Wounded.MaxHealth .. "), so the " ..
				"break-off rule under test was never engaged"
		end

		return Shooter.AmmoCount("primary-ammo") < startingAmmo
	end, "shooter did not fire a single round at the critically wounded target within " ..
		DeadlineSeconds .. "s despite an explicit attack order (break-off is being " ..
		"applied to a player-issued order)")
end
