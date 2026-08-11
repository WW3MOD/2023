-- AUTO TEST: AutoTarget re-evaluates a low-priority target when a higher-priority one appears.
--
-- The user-reported bug: a Stryker SHORAD keeps shooting ground units while an
-- enemy helicopter sits in range unengaged. The YAML is already correct —
-- ^AutoTargetAAIFV declares Helicopter 5 / Aircraft 4 / Vehicle 3 / Infantry 2 —
-- and ChooseTarget's priority math is already categorical. The bug is that the
-- priority table is consulted once and then BYPASSED: when an attack activity
-- ends, AttackFollow.ClearRequestedTarget does not clear but PROMOTES the target
-- to a persistent OpportunityTarget, and TryGetAutoTargetOverride hands that back
-- to every later AutoTarget scan ahead of ChooseTarget. The unit sits idle,
-- firing at the tank, structurally unable to see the helicopter.
--
-- Geometry (row y=17): SHORAD col 20, t90 col 28 (8 cells east, band 3 — inside
-- both ground weapons: 25mm 20c0, Hellfire 25c0/min 5c0). The HIND (band 5) is
-- spawned airborne 10 cells NORTH once the SHORAD is committed, well inside
-- Stinger range (28c0).
--
--   Pass: the HIND takes damage within DeadlineSeconds OF ITS OWN ARRIVAL.
--   Fail: the deadline passes with the HIND untouched (the pre-fix behaviour).
--
-- WHY THE DEADLINE IS SHORT, AND WHY IT MUST STAY SHORT.
-- The lock is not permanent. CanAttack returns false while an armament reloads
-- (AttackBase.cs:274, reloadingIsInvalid: true), which drops IsAiming, and the
-- opportunity-fire branch (AttackFollow.cs:161) clears the persistent flag — so
-- the SHORAD eventually rescans and finds the helicopter WITHOUT the fix. An
-- earlier 22-second version of this test therefore passed with the fix disabled
-- and proved nothing. That unaided break is a per-tick race against the idle
-- rescan re-establishing the stale target, so it has no clean closed form and
-- must NOT be "derived" into a comfortable-looking number.
--
-- The deadline is instead budgeted from what the FIX costs to respond:
--     up to  25 ticks  PreemptScanInterval cadence (defaults.yaml)
--     up to ~26 ticks  turret slew, Turreted TurnSpeed 20 over at most a half turn
--     up to ~40 ticks  Stinger launch ramp + flight (Speed 600, ~10 cells)
--     ------------------
--          ~91 ticks  ≈ 3.6s worst case, so 5s carries headroom and no more.
-- The RED control (PreemptScanInterval pinned to 0) at THIS SAME deadline is what
-- proves the unaided break does not beat it. Widening this deadline destroys that
-- discrimination and silently restores the meaningless version of the test.

local SpawnHeliAfterSeconds = 4
local DeadlineSeconds = 5

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

WorldLoaded = function()
	local Russia = Player.GetPlayer("Russia")
	if Russia == nil then
		Test.Fail("Russia player not found")
		return
	end

	TestHarness.FocusBetween(Shorad, GroundTarget)
	TestHarness.Select(Shorad)

	-- The tank holds fire: nothing may kill the SHORAD or confound the geometry.
	GroundTarget.Stance = "HoldFire"

	local Heli = nil
	local heliStartHealth = nil
	local ticksSinceSpawn = 0

	-- NOTE: deliberately NO "the SHORAD must not be idle" guard. An earlier revision
	-- had one, under the wrong theory that the bug needed a non-idle unit. It is the
	-- opposite: idle-while-firing-at-a-persistent-target IS the locked state, so such
	-- a guard would fail the very path being tested.
	Trigger.AfterDelay(DateTime.Seconds(SpawnHeliAfterSeconds), function()
		if Shorad.IsDead then
			return
		end

		Heli = Actor.Create("HIND", true, {
			Owner = Russia,
			CenterPosition = cellPos(20, 7, 1280),
			Facing = Angle.South,
		})

		if Heli == nil then
			Test.Fail("could not spawn HIND")
			return
		end

		Heli.Stance = "HoldFire"
		heliStartHealth = Heli.Health
	end)

	TestHarness.AssertWithin(SpawnHeliAfterSeconds + DeadlineSeconds + 2, function()
		if Shorad.IsDead then
			return "fail: SHORAD died first"
		end

		-- The t90 must outlive the test: its death would end the engagement and let a
		-- plain idle rescan find the helicopter, which is not what this measures. It is
		-- Heavy armour under a 25mm autocannon, so it comfortably survives.
		if GroundTarget.IsDead then
			return "fail: t90 died — the engagement would end on its own, so this no longer isolates the fix"
		end

		if Heli == nil then
			return false
		end

		if Heli.IsDead or Heli.Health < heliStartHealth then
			return true
		end

		ticksSinceSpawn = ticksSinceSpawn + 1
		if ticksSinceSpawn >= DateTime.Seconds(DeadlineSeconds) then
			return string.format(
				"fail: SHORAD did not engage the band-5 helicopter within %ds of its arrival — "
				.. "it stayed on the band-3 t90 (heli HP %d/%d)",
				DeadlineSeconds, Heli.Health, heliStartHealth)
		end

		return false
	end, "helicopter was never engaged")
end
