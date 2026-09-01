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

-- DIAGNOSTIC STATE — see the twin (test-attackmove-dry-breaks-off) for the reasoning. Short form:
-- "it never went idle" does not say WHETHER the attack activity refused to end or ended and was
-- immediately replaced, and those two want opposite fixes.
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

		return Gunner.IsIdle
	end, function()
		return string.format(
			"Dry Abrams never went idle: it held a direct attack order it could not carry out "
			.. "|| Gunner cell=(%d,%d) startX=%d ammo=%d cannotFight=%s idle=%s idleTicks=%d acts=[%s]",
			Gunner.Location.X, Gunner.Location.Y, startX,
			Gunner.AmmoCount("primary-ammo"), tostring(Test.CannotFight(Gunner)),
			tostring(Gunner.IsIdle), idleTicks, table.concat(actOrder, " ~ "))
	end)
end
