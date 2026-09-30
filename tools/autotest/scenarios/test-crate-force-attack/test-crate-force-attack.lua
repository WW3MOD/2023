-- AUTO TEST — the player can still MANUALLY attack a dropped SUPPLYCACHE.
--
-- The other half of the crate exclusion, and the trap it exists to catch. Denying
-- auto-acquisition is easy to get wrong in a way that also destroys the player's own
-- attack order: strip the crate's Targetable, or its `Ground` type, and the crate
-- becomes untargetable — auto-fire stops, and so does right-click. The user asked for
-- exactly the opposite ("We have to manually attack them if we want to"), so the
-- exclusion must be invisible to an explicit order.
--
-- Setup (map.yaml): an ENEMY-owned crate 4 cells from MyTank. The tank is given an
-- explicit attack order at t=0 with allowMove=false, so it fires from where it stands.
--
-- WHY allowMove IS FALSE. A ground unit ordered to close on a crate would drive into
-- proximity-capture range (ProximityCapturable, 2c0) and TAKE the crate instead of
-- shooting it — the order would end with a captured crate and zero damage, and this
-- test would fail for a reason that is not a bug. Holding the tank at 4 cells keeps
-- the two mechanisms apart so this scenario measures only the order binding.
--
-- Attribution: nothing else in the scenario can damage the crate. There is no other
-- armed actor, and auto-acquisition is precisely what NoAutoTarget forbids — so any
-- damage at all is the manual order and nothing else.
--   PASS = Crate takes damage within the window (the order bound and the gun fired).
--   FAIL = Crate untouched — the exclusion broke manual attack too.

-- 500 ticks for the tank to turn its turret and land a shot: the budget this scenario was authored and validated against, back when
-- TestHarness.TicksPerSecond was a hardcoded 25. The harness was corrected to the engine's real
-- 16.667 on 2026-09-21, which cut every seconds-literal window by a third; this is the SAME tick
-- budget re-expressed so it no longer depends on the rate at all (the division round-trips
-- exactly -- see the epsilon note on TestHarness.TicksForSeconds).
local WINDOW_TICKS = 500
local WINDOW = WINDOW_TICKS / TestHarness.TicksPerSecond

WorldLoaded = function()
	TestHarness.FocusBetween(MyTank, Crate)
	TestHarness.Select(MyTank)

	local crateFull = Crate.MaxHealth

	-- The player's own attack order on an enemy crate. allowMove=false, forceAttack=false:
	-- an ordinary right-click on a hostile actor, which is what the user described doing.
	MyTank.Attack(Crate, false, false)

	TestHarness.AssertWithin(WINDOW, function()
		if MyTank.IsDead then return "fail: MyTank died before firing — inconclusive" end
		if Crate.IsDead then return true end
		if Crate.Health < crateFull then return true end
		return false
	end, "the manual attack order never damaged the crate — excluding it from auto-target has broken the player's own attack order as well")
end
