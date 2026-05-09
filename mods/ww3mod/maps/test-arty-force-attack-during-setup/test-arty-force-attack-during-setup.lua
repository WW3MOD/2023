-- AUTO TEST — Bug repro: artillery force-attack during setup-ticks
--
-- Sequence:
--   t=0       Paladin auto-targets t90 (HoldFire) — setup-ticks countdown begins.
--   t=15      Lua issues a force-attack-ground at a different cell.
--   t=0..15s  Assert that the Paladin's primary ammo drops, i.e. the firing
--             pipeline produced *something*. With the bug present, the
--             force-attack-ground replaces t90 as the request, but the old
--             AttackActivity's OnLastRun calls ClearRequestedTarget which
--             wipes the ground target → no fire.
--
-- Expected verdicts:
--   PRE-FIX:  Fail with "no fire within 15s" (timeout).
--   POST-FIX: Pass within ~3-5s (force-attack-ground fires after setup
--             completes on the ground target).

local DeadlineSeconds = 15
local GroundCell = CPos.New(20, 20) -- well to the west, off-axis from t90

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin, Target)
	TestHarness.Select(Paladin)
	Target.Stance = "HoldFire"

	local startingAmmo = Paladin.AmmoCount("primary-ammo")

	-- Step 1: Paladin engages t90 (autotarget would do this anyway, but
	-- forcing it makes the test deterministic across scan-tick timing).
	Paladin.Attack(Target, true, false)

	-- Step 2: Mid-setup (15 ticks ≈ 0.6 s into the 25-tick countdown), issue
	-- a force-attack-ground at a different location. With the bug, this
	-- order is silently eaten.
	Trigger.AfterDelay(15, function()
		if not Paladin.IsDead then
			Paladin.AttackGround(GroundCell)
		end
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Paladin.IsDead then
			return "fail: Paladin died before firing"
		end
		return Paladin.AmmoCount("primary-ammo") < startingAmmo
	end, "Paladin did not fire within " .. DeadlineSeconds .. "s — force-attack order was likely eaten")
end
