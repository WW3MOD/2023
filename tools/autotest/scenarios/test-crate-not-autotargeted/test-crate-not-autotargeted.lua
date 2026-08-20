-- AUTO TEST — a dropped SUPPLYCACHE must NEVER be auto-targeted.
--
-- Setup (map.yaml): a crate (Crate, owned by Me) 4 cells WEST of an enemy MBT
-- (EnemyTank) in the default FireAtWill stance, and a supply truck (Decoy, owned by
-- Me) 4 cells EAST of the same tank. The crate carries NoAutoTarget, so its Ground
-- type no longer matches the base FireAtWill priority and the tank must ignore it.
--
-- WHY THE DECOY EXISTS. "The crate was never shot" is satisfied just as well by a
-- tank that never shot ANYTHING — out of range, no LOS, wrong stance, dead gun. That
-- run would be green and would measure nothing. The Decoy is the control: a TRUK is
-- `Ground, Vehicle, Unarmored` with no exclusion, sits at the SAME 4-cell range, and
-- so is auto-acquired through the very priority band the crate is being denied. If
-- the tank damages the truck it is demonstrably armed, in range, with LOS, in
-- FireAtWill — and its silence toward the crate is then attributable to the
-- exclusion and nothing else. If the truck is untouched too, this run is declared
-- inconclusive rather than passed.
--
-- The crate must survive the WHOLE window, not merely until the control fires:
-- passing the moment the truck takes its first hit would miss a tank that retargets
-- the crate afterwards.
--   PASS = Decoy damaged AND Crate still at full health after HOLD seconds.
--   FAIL = Crate took any damage (the bug), or the control never fired.

local HOLD = 20   -- seconds the crate must survive untouched

WorldLoaded = function()
	TestHarness.FocusBetween(Crate, EnemyTank)

	local crateFull = Crate.MaxHealth
	local decoyFull = Decoy.MaxHealth
	local controlFired = false
	local elapsed = 0
	local holdTicks = HOLD * TestHarness.TicksPerSecond

	-- Live counters go in a print, never in the failure string: AssertWithin's third
	-- argument is evaluated EAGERLY at registration, so anything interpolated there
	-- reports its value from before the run started.
	Trigger.AfterDelay(math.floor(holdTicks / 2), function()
		print(string.format("[crate-autotarget] halfway: crateHealth=%d/%d controlFired=%s",
			Crate.IsDead and 0 or Crate.Health, crateFull, tostring(controlFired)))
	end)

	TestHarness.AssertWithin(HOLD + 10, function()
		if EnemyTank.IsDead then
			return "fail: EnemyTank died before the window closed — inconclusive"
		end

		if Crate.IsDead or Crate.Health < crateFull then
			return "fail: the enemy tank auto-targeted the dropped crate — NoAutoTarget is not suppressing acquisition"
		end

		if Decoy.IsDead or Decoy.Health < decoyFull then
			controlFired = true
		end

		elapsed = elapsed + 1
		if elapsed < holdTicks then
			return false
		end

		if not controlFired then
			return "fail: control never fired — the tank did not damage the auto-targetable truck either, so this run proves nothing about the crate"
		end

		return true
	end, "crate auto-target window never resolved")
end
