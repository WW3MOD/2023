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
--   Pass: the HIND takes damage within DeadlineTicks OF ITS OWN ARRIVAL.
--   Fail: the deadline passes with the HIND untouched (the pre-fix behaviour).
--
-- WHY THE DEADLINE IS SHORT, AND WHY IT MUST STAY SHORT.
-- The lock is not permanent. CanAttack returns false while an armament reloads
-- (AttackBase.cs:274, reloadingIsInvalid: true), which drops IsAiming, and the
-- opportunity-fire branch (AttackFollow.cs:176) clears the persistent flag — so
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
--          ~91 ticks  worst case; 110 is that plus headroom and no more.
-- CONTROL ESTABLISHED 2026-08-12, AND IT DID NOT GO RED. THIS TEST DOES NOT CURRENTLY
-- DISCRIMINATE THE FIX. Both runs PASSED at this deadline:
--   * PreemptScanInterval pinned to 0 on the ACTOR (seed 1298325022) -> pass
--   * shipped default, 25                        (seed -1641486964) -> pass
-- The pin was verified to have resolved, not assumed: a temporary trace in
-- AutoTarget.Created printed `strykershorad PreemptScanInterval=0` on the control run
-- and `=25` on the confirm run, with t90 reading 25 in both.
--
-- So the unaided break described above BEATS 110 ticks, which is exactly what the
-- budget below was supposed to rule out. Read together with the merge that shipped the
-- fix (68b627ce), this says the green here is not evidence for preemption.
--
-- TWO THINGS THIS DOES *NOT* ESTABLISH, both of which matter before anyone acts on it:
--   1. It does not show the preempt scan is inert in play. PreemptScanInterval: 0 is NOT
--      a clean revert of 68b627ce — that merge also repaired an inverted OnlyTargets
--      clause in HasValidTargetPriority which does NOT sit behind this flag, so the
--      control still carries half the merge. The unaided rescan finding the helicopter
--      may itself be that repair working.
--   2. It does not say by how much either configuration beat the deadline. Both runs
--      predate the margin reporting added below, so there is no with/without number to
--      compare. That instrumentation is IN but UNEXERCISED — the first person to run
--      this gets the figure for free, and should take it for BOTH configurations.
-- Widening the deadline would destroy what discrimination remains; tighten it against
-- measured margins instead, once there are some.
--
-- PITFALL: this deadline is in TICKS on purpose — there are two different time
-- bases in play and mixing them silently halves or doubles the budget.
--   * DateTime.Seconds uses the ENGINE rate, 1000 / Timestep. mod.yaml's default
--     speed is Timestep 60, so that is 16 ticks/s and DateTime.Seconds(5) is 80
--     ticks — BELOW the 91-tick budget, i.e. a deadline that fails a working fix.
--   * TestHarness.TicksPerSecond is 25 and applies only to AssertWithin's outer
--     timeout, which is why that one is still expressed in seconds below.
-- 110 ticks is ~6.9s of game time at the default speed.

local SpawnHeliAfterSeconds = 4
local DeadlineTicks = 110

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

	-- PITFALL: Test.Pass is NOT idempotent — TestGlobal.ExitWhenCapturesFlushed writes the
	-- verdict on every call, last one wins. Returning `true` from an AssertWithin predicate
	-- makes the helper call Test.Pass() with an EMPTY note, silently erasing a note the
	-- predicate just wrote (this ate the margin figure once, 2026-08-12). So report the note
	-- and return false; `reported` then keeps every later poll inert so the deadline branch
	-- cannot overwrite a pass with a fail while the deferred exit is in flight.
	local reported = false

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

	-- Outer timeout is on TestHarness's 25 ticks/s base and must comfortably exceed the
	-- spawn delay (4s = 64 engine ticks) plus DeadlineTicks: 10 * 25 = 250 > 174.
	TestHarness.AssertWithin(10, function()
		if reported then
			return false
		end

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
			-- Report the MARGIN, not just the boolean. A bare pass cannot be compared
			-- against the PreemptScanInterval: 0 control, so a control that also passes
			-- leaves nothing to reason about — which is exactly what happened on
			-- 2026-08-12 (see the header note below).
			reported = true
			Test.Pass(string.format("engaged the band-5 helicopter %d ticks after its arrival (deadline %d)",
				ticksSinceSpawn, DeadlineTicks))
			return false
		end

		ticksSinceSpawn = ticksSinceSpawn + 1
		if ticksSinceSpawn >= DeadlineTicks then
			return string.format(
				"fail: SHORAD did not engage the band-5 helicopter within %d ticks of its arrival — "
				.. "it stayed on the band-3 t90 (heli HP %d/%d)",
				DeadlineTicks, Heli.Health, heliStartHealth)
		end

		return false
	end, "helicopter was never engaged")
end
