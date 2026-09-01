-- AUTO TEST: the TURRETED half of the dry-unit guard — AttackFollow.AttackActivity.
--
-- Every armed vehicle in WW3MOD is AttackTurreted, and AttackTurreted inherits
-- AttackFollow without overriding GetAttackActivity, so this is the attack path the
-- whole vehicle roster runs on. It is also the path with no safety net: vehicles carry
-- Rearmable but NOT AutoSeekSupplies, so nothing else in the game will ever break a dry
-- tank off an order it cannot discharge. See WORKSPACE/DISCOVERIES.md, 2026-08-10.
--
-- Shipped abrams, no rules override on the unit: 40 rounds, and that is the point —
-- running a tank dry is ordinary, not contrived.
--
-- A DIRECT attack order, not an attack-move, and that distinction is the whole reason
-- this scenario exists separately from test-attackmove-dry-breaks-off. An attack-move is
-- aborted by AttackMoveActivity, the PARENT, which cancels its attack child before that
-- child's own guard is ever consulted — so an attack-move scenario cannot say anything
-- about the guard inside the attack activity itself. With no parent, that guard is the
-- only thing that can end this order.
--
-- Without it the tank drives to 25c0, aims, and holds: ChooseArmamentsForTarget ignores
-- ammo so the target stays acquired, CheckFire declines every tick because the armament
-- is ammo-paused, and Tick returns false for good. Never firing, never idle.

local DeadlineSeconds = 15
local DrainAfterTicks = 25 -- 1s; the tank still has ~3 cells to close before it could fire

-- ASSERTS ON THE ACTIVITY, NOT ON Actor.IsIdle (changed 2026-09-01). The twin
-- (test-attackmove-dry-breaks-off) carries the full reasoning and the measurement. Short form:
-- Actor.Tick re-runs the queue in the same tick right after INotifyBecomingIdle
-- (Actor.cs:322-325), and AmmoPool's resupply disposition queues on exactly that edge, so a unit
-- whose attack order genuinely ended is still never observed idle. The twin's instrumented run
-- showed `idleTicks=0` alongside a chain that had already moved off the attack activity.
--
-- This scenario was NOT re-run after the change -- it was observed red on main under the old
-- assertion, and is predicted to pass under the new one for the same reason its twin does. The
-- abrams here has one pool and names `RearmActors: logisticscenter` with none on the map, so it
-- takes the same evacuate branch.
--
-- DIAGNOSTIC STATE below: kept so that a future red says WHICH activity was being held rather
-- than only that something was.
local idleTicks, firstIdleTick, startX = 0, -1, -1
local actOrder, actSeen = {}, {}

WorldLoaded = function()
	print("[dry-breaks-off] WorldLoaded: script is executing")

	TestHarness.FocusBetween(Gunner, Target)
	TestHarness.Select(Gunner)

	-- The t90 is a prop: it must survive to hold the Gunner in the engagement.
	Target.Stance = "HoldFire"

	Gunner.Attack(Target)

	Trigger.AfterDelay(DrainAfterTicks, function()
		if not Gunner.IsDead then
			Gunner.Reload("primary-ammo", -Gunner.MaximumAmmoCount("primary-ammo"))
		end
		print(string.format("[dry-breaks-off] drained at tick %d: Gunner ammo=%d act=%s",
			DateTime.GameTime, Gunner.AmmoCount("primary-ammo"), Test.ActivityChain(Gunner)))
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Gunner.IsDead then return "fail: Gunner died first" end

		-- Nothing counts until the racks are actually empty: the tank is briefly idle
		-- before its attack order resolves, and passing on that would be a verdict about
		-- order latency rather than about ammo.
		if Gunner.AmmoCount("primary-ammo") > 0 then return false end

		if startX < 0 then startX = Gunner.Location.X end
		if Gunner.IsIdle then
			idleTicks = idleTicks + 1
			if firstIdleTick < 0 then firstIdleTick = DateTime.GameTime end
		end

		local chain = Test.ActivityChain(Gunner)
		if actSeen[chain] == nil then
			actSeen[chain] = true
			actOrder[#actOrder + 1] = chain
		end

		-- "Dropped the attack order", not "went idle" -- see the header and the twin. A tank that
		-- lets go of the order and is then sent elsewhere by the resupply layer has satisfied this
		-- guard; one still holding an attack activity has not.
		return not TestHarness.HoldsAttackActivity(Gunner)
	end, function()
		return string.format(
			"Dry Abrams never dropped its attack order: it held an attack activity it could not discharge "
			.. "|| Gunner cell=(%d,%d) startX=%d ammo=%d cannotFight=%s idle=%s idleTicks=%d acts=[%s]",
			Gunner.Location.X, Gunner.Location.Y, startX,
			Gunner.AmmoCount("primary-ammo"), tostring(Test.CannotFight(Gunner)),
			tostring(Gunner.IsIdle), idleTicks, table.concat(actOrder, " ~ "))
	end)
end
