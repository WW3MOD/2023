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
-- THIS SCENARIO IS RED AND THE CAUSE IS NOT YET ESTABLISHED (2026-09-01). Read
-- WORKSPACE/bugs/discovered.md under that date before editing anything here. Two candidate
-- stories remain, and they want OPPOSITE fixes:
--
--   (A) the ammo guard fires, the attack order really does end, and something re-tasks the
--       unit within the same tick — Actor.Tick deliberately re-runs the queue after
--       INotifyBecomingIdle (Actor.cs:322-325), so an idle edge consumed by a handler is
--       invisible to Lua and IsIdle is a broken proxy;
--   (B) the guard does not release the unit at all, the original 2026-08-10 bug report is
--       still true, and this scenario has been correctly red the whole time.
--
-- A first attempt at (A) — pinning ResupplyBehavior to Hold so the resupply disposition
-- layer queued nothing — was committed, RUN, and DID NOT FIX IT. That pin has been reverted:
-- the scenario is back on shipped defaults. Do not re-apply it without new evidence.
--
-- The diagnostics below exist to settle A vs B in one run: they report the unit's cell,
-- whether it moved, AmmoPool.CannotFight (the guards' own predicate), and the ACTIVITY CHAIN.
-- Under (A) the chain is a Move/RotateToEdge and the men have walked west; under (B) it is
-- still an attack activity at engagement range.
--
-- Do NOT widen or re-point the assertion to make this pass. A test named `...-dry-breaks-off`
-- must fail when and only when the break-off guard fails.
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
--
-- EVIDENCE PROVENANCE, because the two halves are not equally proven. The Hunter
-- assertion was verified RED against the pre-fix engine and then GREEN. Shooter was
-- added afterwards, so his half has only ever been observed GREEN — his RED is an
-- argument (pre-fix, Attack.Tick contains no ammo test at all and TickAttack reports
-- Attacking forever), not a measurement. The turreted twin of that guard IS
-- RED-verified, in test-attackfollow-dry-breaks-off, but that exercises
-- AttackFollow.cs, a different file. If you are re-verifying this branch, observing
-- Shooter fail on the pre-fix engine is the cheapest gap left to close.

-- DIAGNOSTIC STATE. The verdict this scenario emitted for its whole life said only "he never went
-- idle", which is compatible with two opposite root causes -- the attack activity refusing to end
-- (engine bug), or the activity ending and something re-tasking the unit immediately (test-proxy
-- bug). One wrong diagnosis has already been published off that ambiguity. Everything below exists
-- so the failure note NAMES which one, and so a reader can tell the run executed at all.
local seen = {}

local function track(name, man)
	local s = seen[name]
	if s == nil then
		s = { idleTicks = 0, firstIdleTick = -1, startX = man.Location.X, acts = {}, actOrder = {} }
		seen[name] = s
	end

	if man.IsIdle then
		s.idleTicks = s.idleTicks + 1
		if s.firstIdleTick < 0 then s.firstIdleTick = DateTime.GameTime end
	end

	-- Distinct activity chains observed after the drain, in first-seen order. An oscillation
	-- (guard ends the order, something re-issues it) shows up here as two entries alternating,
	-- which a single end-of-run sample would miss entirely.
	local chain = Test.ActivityChain(man)
	if s.acts[chain] == nil then
		s.acts[chain] = true
		s.actOrder[#s.actOrder + 1] = chain
	end
end

local function report(name, man)
	local s = seen[name] or { idleTicks = 0, firstIdleTick = -1, startX = -1, actOrder = {} }
	return string.format("%s cell=(%d,%d) startX=%d ammo=%d cannotFight=%s idle=%s idleTicks=%d acts=[%s]",
		name, man.Location.X, man.Location.Y, s.startX,
		man.AmmoCount("primary-ammo"), tostring(Test.CannotFight(man)),
		tostring(man.IsIdle), s.idleTicks, table.concat(s.actOrder, " ~ "))
end

WorldLoaded = function()
	-- EXECUTION MARKER. This scenario has no screenshots and writes only result.json, so a 0-byte
	-- lua.log was previously indistinguishable from "the script never ran". print() reaches
	-- lua.log via ScriptContext.LogDebugMessage.
	print("[dry-breaks-off] WorldLoaded: script is executing")

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
		print(string.format("[dry-breaks-off] drained at tick %d: Hunter ammo=%d act=%s | Shooter ammo=%d act=%s",
			DateTime.GameTime, Hunter.AmmoCount("primary-ammo"), Test.ActivityChain(Hunter),
			Shooter.AmmoCount("primary-ammo"), Test.ActivityChain(Shooter)))
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Hunter.IsDead then return "fail: Hunter died first" end
		if Shooter.IsDead then return "fail: Shooter died first" end

		-- Ignore everything before the drain. Both men are legitimately idle for the tick
		-- or two before their orders resolve, and passing on that would be a verdict about
		-- order latency rather than about ammo.
		if Hunter.AmmoCount("primary-ammo") > 0 then return false end
		if Shooter.AmmoCount("primary-ammo") > 0 then return false end

		track("Hunter", Hunter)
		track("Shooter", Shooter)

		return Hunter.IsIdle and Shooter.IsIdle
	end, function()
		return "A dry man never went idle: he kept an attack order he could not carry out "
			.. "(Hunter = attack-move / AttackMoveActivity guard, Shooter = direct attack / Attack.Tick guard) "
			.. "|| " .. report("Hunter", Hunter) .. " || " .. report("Shooter", Shooter)
	end)
end
