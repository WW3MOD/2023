-- REGRESSION GUARD — the cover shuffle is off for human players (2026-08-30).
--
-- Proves a NEGATIVE, so the design is about ruling out vacuous passes. The geometry is copied from
-- test-stance-positioning, where the same AR in the same spot DOES relocate to the y=17 treeline
-- edge; the only difference is that this scenario's rules.yaml does not re-add the enablement. So a
-- pass here means "the executor was not enrolled", not "the geometry happened not to fire".
--
-- THE THREE WAYS THIS TEST COULD PASS WITHOUT PROVING ANYTHING, and what rules each out:
--   1. The AR is below FireAtWill. The executor relinquishes any unit under FireAtWill
--      (StancePositioningExecutor.cs:318), so a HoldFire unit sits still for an unrelated reason.
--      => we SET FireAtWill explicitly and re-assert it every poll.
--   2. The sighted enemy dies or is never sighted. MinThreatIntensity (40) gates the executor on
--      real data in the sighting field; with no threat bearing it declines and the unit holds.
--      => we fail loudly if the enemy dies, and it is HoldFire so nothing shoots it.
--   3. A locally persisted per-type stance default makes the AR HoldPosition. => -UnitDefaultsManager
--      in rules.yaml, same as the sibling scenarios.
--
-- RED PROCEDURE: paste the ^Combatant enablement block documented at the bottom of rules.yaml and
-- re-run. The AR walks north to the cover edge and this test must fail with "cover shuffle is
-- ACTIVE". A run that passes with that block present is not evidence of anything.

local HOME = { X = 13, Y = 19 }

-- Tick counts, not seconds. The real rate is 16.67 tps (CLAUDE.md), so 700 ticks is ~42 s — well
-- past the 375-tick deadline within which the sibling test requires the executor to have moved, and
-- ~23 EvaluateCooldown (30-tick) opportunities for it to act.
local WATCH_TICKS = 700
local SHOT_AT = 350

WorldLoaded = function()
	local unit = Rifle
	local foe = Enemy

	TestHarness.FocusBetween(unit, foe)
	TestHarness.Select(unit)

	-- FireAtWill on the unit under test is load-bearing (see 1. above): it is the stance in which the
	-- executor WOULD act, so holding still is attributable to the gate and nothing else. The enemy is
	-- silenced from its own side so there is no incoming fire (no suppression, which is a separate
	-- reason the executor declines) and, with NoAutoTarget in rules.yaml, no chase.
	if not unit.IsDead then unit.Stance = "FireAtWill" end
	if not foe.IsDead then foe.Stance = "HoldFire" end

	local elapsed = 0
	local shot = false

	local poll
	poll = function()
		elapsed = elapsed + 1

		if unit.IsDead then
			Test.Fail("precondition lost: the AR died at tick " .. elapsed)
			return
		end

		if foe.IsDead then
			Test.Fail("precondition lost: the sighted enemy died at tick " .. elapsed ..
				", so the threat bearing is gone and a stationary AR would prove nothing")
			return
		end

		-- Re-assert rather than trust: if anything flipped the unit below FireAtWill mid-run, the
		-- executor would decline by stance and this test would pass for the wrong reason.
		if unit.Stance ~= "FireAtWill" then
			Test.Fail("precondition lost: AR stance became " .. tostring(unit.Stance) ..
				" at tick " .. elapsed .. "; the executor declines below FireAtWill")
			return
		end

		local loc = unit.Location
		if loc.X ~= HOME.X or loc.Y ~= HOME.Y then
			Test.Fail("cover shuffle is ACTIVE: AR left its spawn cell " .. HOME.X .. "," .. HOME.Y ..
				" for " .. loc.X .. "," .. loc.Y .. " at tick " .. elapsed ..
				" — a human-owned unit must not be enrolled in StancePositioningExecutor")
			return
		end

		if not shot and elapsed == SHOT_AT then
			Test.Screenshot("held-spawn-cell",
				"expects: AR still on its spawn cell south of the treeline, NOT hull-down at the " ..
				"y=17 cover edge; enemy tank visible further south")
			shot = true
		end

		if elapsed >= WATCH_TICKS then
			Test.Pass()
			return
		end

		Trigger.AfterDelay(1, poll)
	end

	Trigger.AfterDelay(1, poll)
end
