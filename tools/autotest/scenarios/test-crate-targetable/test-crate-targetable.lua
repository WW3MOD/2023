-- AUTO TEST — a dropped SUPPLYCACHE must be auto-targetable by the enemy.
--
-- Setup (map.yaml): a crate (Crate, owned by Me) 4 cells from an enemy MBT
-- (EnemyTank) in the default FireAtWill stance. With NoAutoTarget removed from the
-- crate, its "Ground" target type matches the base auto-target priority, so the
-- enemy acquires and fires on it unaided — the truck-parity behaviour the user asked
-- for. If the crate were still NoAutoTarget the tank would ignore it and its health
-- would never drop. We assert damage taken (not full destruction) so the verdict is
-- fast and independent of the crate's large HP pool.
--   PASS = Crate has taken damage (Health < MaxHealth) within the window.
--   FAIL = Crate untouched at timeout (not auto-targetable), or the tank died first.

local WINDOW = 20   -- seconds for the tank to acquire and land a shot

WorldLoaded = function()
	TestHarness.FocusBetween(Crate, EnemyTank)

	local full = Crate.MaxHealth

	TestHarness.AssertWithin(WINDOW, function()
		if EnemyTank.IsDead then return "fail: EnemyTank died before engaging — inconclusive" end
		if Crate.IsDead then return true end
		if Crate.Health < full then return true end
		return false
	end, string.format("enemy tank never auto-targeted the crate — health stayed at full %d for %ds", full, WINDOW))
end
