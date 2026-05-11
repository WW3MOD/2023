-- AUTO TEST: Paladin's turret must stay aligned forward while driving.
-- Without the fix, AttackTurreted's auto-target keeps swinging the turret to
-- track the t90 even though HoldFireWhileMoving prevents firing — the user
-- sees a turret yawing while the body rolls. With the fix
-- (Turreted.RealignWhileMoving=True) the turret holds InitialFacing during
-- motion and only rotates when the unit is stationary.
--
-- Test plan: order Paladin to drive a long way East. After a 1-second
-- spin-up, sample the turret's local facing every tick for ~5 seconds.
-- Tolerance: ~28° (small transient slack only). Bug case: turret rotates
-- north toward the t90 well past the tolerance within the sample window.

local SampleSeconds = 8
local SpinUpTicks = 25            -- 1.0s for movement to fully begin
-- With the fix, turret stays exactly at LocalFacing 0 while moving (the
-- realign target IS InitialFacing 0, so MoveTurret is a no-op). Any spurious
-- rotation triggered by AttackTurreted.FaceTarget would show up here, even a
-- single tick at TurnSpeed 5 = 5 WAngle. Tolerance 16 (~5.6°) gives a tiny
-- safety margin while still catching the bug case (autotarget kicks in
-- within ~1s, turret then rotates a few WAngle/tick toward the t90).
local LockedTolerance = 16

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin, Target)
	TestHarness.Select(Paladin)

	-- The t90 just gives the Paladin's AutoTarget a target to track during
	-- motion. Pin it so it can't drift out of scan range.
	Target.Stance = "HoldFire"

	-- Drive far East — keeps the body moving for the entire sample window.
	Paladin.Move(CPos.New(50, 16))

	Trigger.AfterDelay(SpinUpTicks, function()
		local samplesRemaining = SampleSeconds * 25
		local check
		check = function()
			if Paladin.IsDead then
				Test.Fail("Paladin died")
				return
			end

			-- Local turret facing, normalized to signed angular delta from InitialFacing (0).
			local lf = Paladin.TurretFacing("primary")
			if lf > 512 then lf = lf - 1024 end
			local af = math.abs(lf)

			if af > LockedTolerance then
				Test.Fail("turret rotated to " .. af .. " WAngle while moving (max " .. LockedTolerance .. ")")
				return
			end

			samplesRemaining = samplesRemaining - 1
			if samplesRemaining <= 0 then
				Test.Pass()
				return
			end

			Trigger.AfterDelay(1, check)
		end
		Trigger.AfterDelay(1, check)
	end)
end
