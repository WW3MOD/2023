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
-- WHY THIS TEST WAS REWRITTEN, 2026-08-19. The previous revision asserted only
-- "the HIND takes damage within 110 ticks of its arrival". That outcome is reached
-- by TWO different mechanisms, and the assertion sat downstream of both, so it could
-- not tell them apart. The control run of 2026-08-12 (f910ac7d) proved it: with the
-- fix disabled the test STILL PASSED. A sabotaged control that passes is measuring
-- nothing, and the green that stood beside it was never evidence for preemption.
--
--   MECHANISM A, the fix: TickPreemption (AutoTarget.cs) runs on a NON-idle unit and
--     hands the engagement over while the incumbent is still held.
--   MECHANISM B, the unaided break: the attack activity ends, the t90 is promoted to
--     a persistent OpportunityTarget, and then AttackFollow.cs:176 opportunity-fire
--     overwrites it and sets opportunityTargetIsPersistentTarget = FALSE. With the
--     persistent flag gone, TryGetAutoTargetOverride declines, the next scan runs a
--     free ChooseTarget, and THAT finds the helicopter. No preemption involved.
--
-- Mechanism B is not slow. The ordinary scan cadence in this scenario's rules.yaml is
-- MinimumScanTimeInterval 16 / MaximumScanTimeInterval 32 — the same order as
-- PreemptScanInterval 25. No tick budget in that neighbourhood can separate them, which
-- is why the 110-tick deadline never had a chance of discriminating and why widening or
-- narrowing it cannot rescue this test. Timing is the wrong instrument here.
--
-- THE DISCRIMINATING OBSERVABLE. The two mechanisms differ in a way that survives:
-- mechanism B must pass through a state where the unit holds NO commitment at all —
-- no live RequestedTarget and no persistent OpportunityTarget — because that is the
-- only condition under which a free ChooseTarget runs. Mechanism A never does; it
-- swaps target while the incumbent is still held. Test.GetUncommittedScanCount latches
-- exactly that state in the simulation.
--
--   Pass: the HIND takes damage within DeadlineTicks of its own arrival AND the
--         SHORAD's uncommitted-scan count has not moved since the HIND arrived.
--   Fail: the deadline passes untouched, OR the HIND is engaged but only after the
--         count rose — i.e. the unit re-acquired after losing its grip, which is the
--         pre-fix behaviour and must not be scored as a pass.
--
-- WHY THE COUNT AND NOT Shorad.IsIdle. IsIdle looks like the natural oracle — preemption
-- requires a non-idle unit — but it CANNOT be sampled from Lua. Actor.Tick (Actor.cs:285-302)
-- ends the activity and re-issues a replacement inside a SINGLE tick, while Lua trigger
-- callbacks run later, in the trait phase (World.cs:506-508). The lapse is therefore
-- invisible to per-tick polling and an "it never went idle" assertion would pass on the
-- broken build too — a second false control. The counter is latched in the simulation at
-- the moment it happens, so sampling granularity cannot hide it.
--
-- WHY THE DEADLINE IS STILL SHORT. It no longer carries the discrimination — the count
-- does — but it keeps the test honest about responsiveness. Budgeted from what the FIX
-- costs to respond:
--     up to  25 ticks  PreemptScanInterval cadence (defaults.yaml)
--     up to ~26 ticks  turret slew, Turreted TurnSpeed 20 over at most a half turn
--     up to ~40 ticks  Stinger launch ramp + flight (Speed 600, ~10 cells)
--     ------------------
--          ~91 ticks  worst case; 110 is that plus headroom and no more.
--
-- PITFALL: EVERY duration in this scenario is in TICKS on purpose. Three different
-- second→tick bases exist in this codebase and mixing them silently stretches or
-- shrinks a budget:
--   * DateTime.Seconds uses the ENGINE rate, 1000 / Timestep, in INTEGER division
--     (DateTimeGlobal.cs:31). mod.yaml's default speed is Timestep 60, so that is
--     16 ticks/s — DateTime.Seconds(5) is 80 ticks, BELOW the 91-tick budget below,
--     i.e. a deadline that fails a working fix.
--   * TestHarness.TicksPerSecond is 25 and governs AssertWithin's outer timeout.
--   * The two disagree by 25/16, so a scenario that expresses one side of a
--     comparison in each has a pass condition that moves when either changes.
--
-- This scenario used to do exactly that, and it was the reason it could not survive a
-- correction of the harness constant: the outer timeout was the seconds literal 10
-- while the two quantities it has to cover — the spawn delay and DeadlineTicks — were
-- both on the engine base. 10 harness-seconds is 250 ticks at 25 but only 160 at the
-- engine's real 16, against the 174 the run needs (64 spawn + 110 deadline). The
-- scenario would NOT have gone red at 16: a healthy build engages ~91 ticks after
-- arrival, at poll ~155, so it would keep passing with five ticks to spare while
-- DeadlineTicks — the responsiveness budget this whole comment block derives — became
-- unreachable and stopped being enforced at all. A silently toothless green.
--
-- So the spawn delay is now a raw tick count rather than DateTime.Seconds, and the
-- outer timeout is budgeted in ticks and converted back through the same constant
-- AssertWithin multiplies by (exact round-trip, checked at both 25 and 16). Nothing
-- here depends on either rate any more. Do not reintroduce a seconds literal.
-- 110 ticks is ~6.9s of game time at the default speed.

local SpawnHeliAfterTicks = 64                      -- 4s at the engine's 16 ticks/s
local DeadlineTicks = 110
local OuterTicks = SpawnHeliAfterTicks + DeadlineTicks + 51   -- 225: the 174 needed, plus headroom
local OuterSeconds = OuterTicks / TestHarness.TicksPerSecond

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
	local uncommittedAtSpawn = nil
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
	Trigger.AfterDelay(SpawnHeliAfterTicks, function()
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

		-- Baseline AFTER the SHORAD is committed to the t90, so the free scan that
		-- acquired the t90 in the first place is excluded. Everything counted from
		-- here on is a lapse that happened while the helicopter was already available.
		uncommittedAtSpawn = Test.GetUncommittedScanCount(Shorad)
	end)

	-- Outer timeout must comfortably exceed the spawn delay plus DeadlineTicks: 225 > 174,
	-- and it is 225 ticks at any TestHarness.TicksPerSecond (see the PITFALL block above).
	TestHarness.AssertWithin(OuterSeconds, function()
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
			local lapses = Test.GetUncommittedScanCount(Shorad) - uncommittedAtSpawn
			reported = true

			-- ATTRIBUTION, not just outcome. Damage alone is reached by the unaided
			-- rescan too — that is exactly how the 2026-08-12 control passed — so a
			-- switch that happened after the unit lost its grip is NOT a pass here.
			if lapses > 0 then
				return string.format(
					"fail: SHORAD engaged the band-5 helicopter %d ticks after its arrival, but only after "
					.. "its commitment to the band-3 t90 lapsed (%d uncommitted scan(s) since the helicopter "
					.. "arrived). That is the unaided re-acquisition via AttackFollow.cs:176 clearing the "
					.. "persistent-target flag, not target preemption — preemption hands over while the "
					.. "incumbent is still held and never raises this count.",
					ticksSinceSpawn, lapses)
			end

			-- Report the MARGIN as well as the attribution. A bare pass cannot be compared
			-- against the PreemptScanInterval: 0 control, and a control that also passes
			-- leaves nothing to reason about — which is what happened on 2026-08-12.
			Test.Pass(string.format(
				"preempted mid-engagement: engaged the band-5 helicopter %d ticks after its arrival "
				.. "(deadline %d, margin %d) with 0 uncommitted scans — the SHORAD never lost its grip "
				.. "on the t90, so only preemption can have made the switch",
				ticksSinceSpawn, DeadlineTicks, DeadlineTicks - ticksSinceSpawn))
			return false
		end

		ticksSinceSpawn = ticksSinceSpawn + 1
		if ticksSinceSpawn >= DeadlineTicks then
			return string.format(
				"fail: SHORAD did not engage the band-5 helicopter within %d ticks of its arrival — "
				.. "it stayed on the band-3 t90 (heli HP %d/%d, %d uncommitted scan(s) since arrival)",
				DeadlineTicks, Heli.Health, heliStartHealth,
				Test.GetUncommittedScanCount(Shorad) - uncommittedAtSpawn)
		end

		return false
	end, "helicopter was never engaged")
end
