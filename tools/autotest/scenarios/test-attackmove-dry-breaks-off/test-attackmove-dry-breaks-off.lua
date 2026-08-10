-- AUTO TEST: an Automatic Rifleman that empties its magazine mid attack-move must
-- break off the order and go idle, so the resupply handoff can see him.
--
-- The bug: the attack activities never consult ammo. A dry unit closes to range,
-- reports Attacking, and CheckFire silently declines every tick because the armament
-- is ammo-paused — so the activity never ends. Actor.IsIdle is CurrentActivity == nil,
-- so the man is never idle, and AmmoPool's INotifyBecomingIdle resupply dispatch is
-- never reached. He stands in front of an enemy aiming a weapon he cannot fire.
--
-- Why the ammo is drained MID-ORDER rather than starting at zero: a unit that is
-- already dry when the order arrives is caught by the issue-time refusal in
-- AttackMove.ResolveOrder, which would make this test pass without ever exercising the
-- running-order abort that is the actual fix. Draining after the march is under way is
-- the only way to put the guard in Attack.Tick / AttackMoveActivity.Tick on the hook.
--
-- Why no supply truck on the map: AutoSeekSupplies.ReturnWhenEmpty already breaks a
-- soldier off a live order when a rearm host is within its 30-cell leash. With no host
-- it takes its documented decline branch — flag NeedsResupply and "leave the unit's
-- current order alone" — which is exactly the hole this fix fills. A truck here would
-- mask the defect entirely.
--
-- Geometry: Hunter (8,16), Target (24,16) = 16 cells, outside the AR's 14c0 reach but
-- inside the 30c scan. Destination (50,16) is far past the target, so a unit that is
-- merely still walking has not passed the test either.

local DeadlineSeconds = 15
local DrainAfterTicks = 25 -- 1s: order is running, still ~2 cells short of firing range

-- Two men, two different guards, because one does not imply the other:
--   Hunter  — attack-move. Aborted by AttackMoveActivity, the PARENT activity.
--   Shooter — direct attack order, no attack-move parent, so the guard inside
--             Attack.Tick is the only thing that can end it.
-- The parent cancels its attack child before that child's guard ever runs, so
-- Hunter on his own says nothing about Activities/Attack.cs.

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, Target)
	TestHarness.Select(Hunter)

	-- The target is a prop, not an opponent: it must survive to keep both men engaged.
	Target.Stance = "HoldFire"

	Hunter.AttackMove(CPos.New(50, 16))
	Shooter.Attack(Target)

	Trigger.AfterDelay(DrainAfterTicks, function()
		for _, man in ipairs({ Hunter, Shooter }) do
			if not man.IsDead then
				man.Reload("primary-ammo", -man.MaximumAmmoCount("primary-ammo"))
			end
		end
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Hunter.IsDead then return "fail: Hunter died first" end
		if Shooter.IsDead then return "fail: Shooter died first" end

		-- Ignore everything before the drain. Both men are legitimately idle for the tick
		-- or two before their orders resolve, and passing on that would be a verdict about
		-- order latency rather than about ammo.
		if Hunter.AmmoCount("primary-ammo") > 0 then return false end
		if Shooter.AmmoCount("primary-ammo") > 0 then return false end

		return Hunter.IsIdle and Shooter.IsIdle
	end, "A dry man never went idle: he kept an attack order he could not carry out "
		.. "(Hunter = attack-move / AttackMoveActivity guard, Shooter = direct attack / Attack.Tick guard)")
end
