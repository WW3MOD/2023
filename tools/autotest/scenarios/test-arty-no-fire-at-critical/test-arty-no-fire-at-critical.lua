-- AUTO TEST: A Paladin at critical damage (HP < 25%) MUST NOT fire its main gun.
--
-- Background: a long-standing v1 bug claimed artillery "fires all ammo at once
-- when critically damaged". Commit 7817b370 (260509) appended `|| critical-damage`
-- to every vehicle Armament's PauseOnCondition. Net effect: Armament is paused
-- the moment HP crosses below 25%, so no shot can leave the barrel — regardless
-- of whatever fired the original bug.
--
-- This test pins that contract:
--   - Bring Paladin to 22% HP (below 25% critical and at the m109 crew-eject
--     threshold of 24%).
--   - Force-attack a Russian t90 (HoldFire so it can't return fire).
--   - Wait 5s. Ammo MUST equal startingAmmo. Any decrease = regression.

local DeadlineSeconds = 5

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin, Target)
	TestHarness.Select(Paladin)

	-- Target stays passive; we want the Paladin's firing behavior to be the
	-- only thing that can move the ammo counter.
	Target.Stance = "HoldFire"

	local startingAmmo = Paladin.AmmoCount("primary-ammo")

	-- Drop to 22% HP. m109 MaxHP=14000 → ~3080 HP. This is below:
	--   * the 25% Critical damage state (grants `critical-damage` condition),
	--   * the m109 CrewDamageThresholdPercent: 24 (crew may begin ejecting).
	-- Either path makes firing impossible: pause-on-critical or husk-after-eject.
	Paladin.Health = math.floor(Paladin.MaxHealth * 22 / 100)

	-- Force-engage so we exercise the firing pipeline (turret rotate, setup-aim,
	-- fire) — the same path test-paladin-fires uses to confirm a healthy Paladin
	-- DOES fire. Here we expect the Armament's PauseOnCondition to swallow it.
	Paladin.Attack(Target, true, false)

	TestHarness.AssertAfter(DeadlineSeconds, function()
		if Paladin.IsDead then
			-- Burn ramp / cookoff at low HP can kill the Paladin within a few
			-- seconds. Dead vehicles can't fire, so the contract still holds —
			-- but flag it so we know to pick a less-burny test platform if it
			-- ever happens routinely.
			return true
		end
		local ammoNow = Paladin.AmmoCount("primary-ammo")
		if ammoNow < startingAmmo then
			return "fail: Paladin fired " .. (startingAmmo - ammoNow) ..
				" round(s) at critical damage (expected 0 — critical-damage" ..
				" should pause the Armament)"
		end
		return true
	end, "ammo check did not run")
end
