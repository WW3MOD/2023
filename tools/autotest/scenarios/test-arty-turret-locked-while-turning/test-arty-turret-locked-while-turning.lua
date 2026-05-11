-- AUTO TEST: Paladin's turret must stay aligned forward while the body is
-- rotating in place — Mobile.CurrentMovementTypes flags Turn separately from
-- Horizontal, and a unit pivoting to face a new heading was previously
-- considered "still" (Horizontal cleared) which started the turret realign
-- and the SetupTicks countdown the moment movement transitioned from Drive
-- to Pivot. With the fix, both flags are checked.
--
-- Setup: Paladin at (32,16) facing East. Move-target is straight West (8,16),
-- so the unit must pivot 180° before driving — pure Turn motion at the start.
-- The t90 sits due south to lure the turret if the lock is too lax.

local SampleSeconds = 6           -- enough to span the pivot window
local SpinUpTicks = 6             -- short — start sampling as soon as Move begins
local LockedTolerance = 16        -- ~5.6° around InitialFacing

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin, Target)
	TestHarness.Select(Paladin)

	Target.Stance = "HoldFire"

	-- Order a move that requires a 180° body pivot before the unit can drive.
	Paladin.Move(CPos.New(8, 16))

	Trigger.AfterDelay(SpinUpTicks, function()
		local samplesRemaining = SampleSeconds * 25
		local check
		check = function()
			if Paladin.IsDead then
				Test.Fail("Paladin died")
				return
			end

			local lf = Paladin.TurretFacing("primary")
			if lf > 512 then lf = lf - 1024 end
			local af = math.abs(lf)

			if af > LockedTolerance then
				Test.Fail("turret rotated to " .. af .. " WAngle while body was turning (max " .. LockedTolerance .. ")")
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
