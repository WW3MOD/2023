-- AUTO TEST: AutoTarget preempts a low-priority target for a higher-priority one.
--
-- The user-reported bug: a Stryker SHORAD keeps shooting ground units while an
-- enemy helicopter sits in range unengaged. The YAML is already correct —
-- ^AutoTargetAAIFV declares Helicopter 5 / Aircraft 4 / Vehicle 3 / Infantry 2 —
-- and ChooseTarget's priority math is already categorical. The bug was that the
-- scan never RUNS again once the unit is engaged: ChooseTarget is reachable only
-- from INotifyIdle.TickIdle, and an engaged actor is never idle. Fixed by
-- AutoTargetInfo.PreemptScanInterval (defaults.yaml), which rescans on a cadence
-- while engaged and switches only on a strictly higher priority band.
--
-- Geometry (row y=17): SHORAD col 20, t90 col 28 (8 cells east, band 3).
-- The HIND (band 5) is spawned airborne 10 cells NORTH after the SHORAD is
-- already committed to the t90, well inside Stinger range (28c0).
--
--   Pass: the HIND takes damage before the deadline.
--   Fail: deadline with the HIND untouched (the pre-fix behaviour), or either
--         invalidating condition below trips.
--
-- Two guards keep a green from being reachable the wrong way — both would let
-- the ORDINARY idle scan find the heli and mask the preemption path entirely:
--   * the t90 must stay alive (its death would idle the SHORAD). It is Heavy
--     armour under a 25mm autocannon, so it comfortably outlives the deadline.
--   * the SHORAD must actually be engaged at the moment the HIND appears.

local SpawnHeliAfterSeconds = 4
local DeadlineSeconds = 22

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

	-- Both enemies hold fire: nothing may kill the SHORAD or confound the test
	-- by making the heli manoeuvre.
	GroundTarget.Stance = "HoldFire"

	local Heli = nil
	local heliStartHealth = nil

	Trigger.AfterDelay(DateTime.Seconds(SpawnHeliAfterSeconds), function()
		if Shorad.IsDead then
			return
		end

		-- The whole point of the test is preemption of an IN-PROGRESS engagement.
		-- If the SHORAD were idle here, the ordinary idle scan would acquire the
		-- heli and the test would pass without exercising the fix at all.
		if Shorad.IsIdle then
			Test.Fail("SHORAD was idle when the helicopter spawned — the idle scan would mask the preemption path")
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

	-- Continuous idle guard. A ONE-SHOT check at spawn is not enough: if the SHORAD
	-- drops its engagement at any point while the heli is up, the ORDINARY idle scan
	-- acquires the heli and the test greens without preemption ever running (observed
	-- 2026-08-11 — a PreemptScanInterval:0 control still passed against the one-shot
	-- form). Preemption itself cancels and requeues, which can leave CurrentActivity
	-- null for a tick or two, so only a SUSTAINED idle gap counts as a mask.
	local MaxIdleTicks = 5
	local idleRun = 0
	local worstIdleRun = 0
	local elapsed = 0

	-- Self-fail one tick BEFORE AssertWithin's own timeout so the verdict can carry live
	-- diagnostics. AssertWithin's failReason is a plain string built at setup time, so any
	-- counter interpolated into it would be frozen at its initial value.
	local DeadlineTicks = DateTime.Seconds(DeadlineSeconds) - 1

	TestHarness.AssertWithin(DeadlineSeconds, function()
		elapsed = elapsed + 1
		if Shorad.IsDead then
			return "fail: SHORAD died first"
		end

		-- A dead ground target would idle the SHORAD, so a pass after this point
		-- would prove nothing about preemption.
		if GroundTarget.IsDead then
			return "fail: t90 died — SHORAD would go idle and rescan, so the test can no longer isolate preemption"
		end

		if Heli == nil then
			return false
		end

		if Shorad.IsIdle then
			idleRun = idleRun + 1
			if idleRun > worstIdleRun then
				worstIdleRun = idleRun
			end

			if idleRun > MaxIdleTicks then
				return string.format(
					"fail: SHORAD sat idle for %d consecutive ticks while the helicopter was up — "
					.. "the idle scan, not preemption, would acquire it, so this scenario cannot isolate the fix",
					idleRun)
			end
		else
			idleRun = 0
		end

		if Heli.IsDead then
			return true
		end

		if Heli.Health < heliStartHealth then
			return true
		end

		if elapsed >= DeadlineTicks then
			return string.format(
				"fail: SHORAD never damaged the helicopter in %ds — it stayed on the band-3 t90 "
				.. "(heli HP %d/%d, longest idle gap %d ticks)",
				DeadlineSeconds, Heli.Health, heliStartHealth, worstIdleRun)
		end

		return false
	end, "SHORAD never damaged the helicopter within " .. DeadlineSeconds .. "s")
end
