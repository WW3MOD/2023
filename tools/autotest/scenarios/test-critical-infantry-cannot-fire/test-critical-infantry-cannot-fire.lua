-- AUTO TEST: a critically wounded soldier must not fire.
--
-- THE BUG, in the user's words: "soldiers that are critically wounded can still
-- fire, and they should not. Similarly to how vehicles become disabled when
-- critically damaged they should also."
--
-- MECHANISM. Vehicles stop shooting because every vehicle Armament carries
-- `heavy-damage-attained` in its PauseOnCondition (vehicles-russia.yaml:69 and
-- friends), pinned by test-arty-no-fire-at-critical. Infantry had the whole
-- degradation ladder — speed 0, vision 10%, burst 10%, burst-wait 400%,
-- inaccuracy 400% at critical (infantry.yaml:1008-1022) — but no cutoff, so a
-- man at 1% HP still put rounds downrange, slowly. The fix pauses AttackFrontal
-- on `critical-damage` for ^Soldier.
--
-- This also repairs a premise the engine was already asserting: AutoTarget's
-- break-off skip is justified in a comment reading "critical damage in WW3MOD
-- means the unit can't fight and will bleed out to 0" (AutoTarget.cs:1441-1442).
-- For infantry the first half of that sentence was simply not true.
--
-- WHY THE CONTROL EXISTS. This asserts a NEGATIVE — "no shot was fired" — and a
-- negative is what a scenario that never built its world also reports. The
-- Control is an identical conscript at an identical distance from the same
-- target: it shares range, line of sight, stance, ammo pool and engagement
-- rules, and the ONLY difference is the health the Lua drains. If the Control
-- does not fire, the observation window proves nothing and the run says
-- SETUP INVALID instead of passing. It is also the timeout message, so the
-- failure mode cannot be silently absorbed.
--
-- LATCHED, NOT SAMPLED. Both counters are latched on every tick rather than
-- compared once at the end: an AmmoPool reloads (ReloadAmmoPool), so a
-- start-vs-end comparison can read equal across a burst that was fired and
-- replaced.

local DeadlineSeconds = 30

-- How long the wounded man must be observed holding fire, counted from the
-- moment the Control proves the setup is live. Critical burst-wait is 400% of
-- 20 ticks = 80 ticks (3.2s), so this is ~4 firing opportunities.
local ObservationSeconds = 13

local CriticalFraction = 15

WorldLoaded = function()
	TestHarness.FocusBetween(Wounded, Control, Victim)
	TestHarness.Select(Wounded)

	-- Silence the VICTIM, never the units under test. Putting an attacker on
	-- HoldFire to keep the scenario quiet is the trap recorded as AUTOTEST.md
	-- gotcha 9 — it switches off the very trait being measured.
	Victim.Stance = "HoldFire"

	Wounded.Health = math.floor(Wounded.MaxHealth * CriticalFraction / 100)

	local woundedAmmo = Wounded.AmmoCount("primary-ammo")
	local controlAmmo = Control.AmmoCount("primary-ammo")
	local woundedShots = 0
	local controlEverFired = false
	local observedTicks = 0

	local observationTicks = math.floor(ObservationSeconds * TestHarness.TicksPerSecond)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Victim.IsDead then
			return "fail: SETUP INVALID: the victim died, so the attackers lost their target"
		end

		if Control.IsDead then
			return "fail: SETUP INVALID: the control soldier died"
		end

		if Wounded.IsDead then
			-- He bled out. A dead man not firing is not evidence about the gate,
			-- so this is a setup fault rather than a pass.
			return "fail: SETUP INVALID: the wounded soldier died before the " ..
				"observation window closed"
		end

		if Wounded.Health * 100 >= Wounded.MaxHealth * 25 then
			return "fail: SETUP INVALID: the wounded soldier is not at critical " ..
				"damage (HP " .. Wounded.Health .. "/" .. Wounded.MaxHealth .. ")"
		end

		-- Latch both sides.
		local wNow = Wounded.AmmoCount("primary-ammo")
		if wNow < woundedAmmo then
			woundedShots = woundedShots + (woundedAmmo - wNow)
		end
		woundedAmmo = wNow

		local cNow = Control.AmmoCount("primary-ammo")
		if cNow < controlAmmo then
			controlEverFired = true
		end
		controlAmmo = cNow

		if woundedShots > 0 then
			return "fail: the critically wounded soldier fired " .. woundedShots ..
				" round(s) — critical damage must stop him shooting, as it does " ..
				"for vehicles"
		end

		if controlEverFired then
			observedTicks = observedTicks + 1
			if observedTicks >= observationTicks then
				return true
			end
		end

		return false
	end, "SETUP INVALID: the healthy control soldier never fired within " ..
		DeadlineSeconds .. "s, so this run never demonstrated that an identical " ..
		"soldier WOULD engage — the wounded man's silence is unattributable")
end
