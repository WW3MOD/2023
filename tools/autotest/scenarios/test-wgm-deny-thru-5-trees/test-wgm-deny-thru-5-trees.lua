-- AUTO TEST: With 5 trees on the firing line, total shadow density is 5,
-- above WGM ClearSightThreshold = 4. The targeting gate must refuse to fire
-- and Bradley's secondary-ammo must stay full for the whole window.

local DeadlineSeconds = 8

WorldLoaded = function()
	TestHarness.FocusBetween(Bradley, Target)
	TestHarness.Select(Bradley)

	Target.Stance = "HoldFire"
	Bradley.Attack(Target, false, false)

	local startingAmmo = Bradley.AmmoCount("secondary-ammo")
	local ticks = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)
	Trigger.AfterDelay(ticks, function()
		if Bradley.IsDead then
			Test.Fail("Bradley died — test inconclusive")
		elseif Bradley.AmmoCount("secondary-ammo") < startingAmmo then
			Test.Fail("Bradley fired WGM through 5 trees (ammo "
				.. Bradley.AmmoCount("secondary-ammo")
				.. " < " .. startingAmmo .. ")")
		else
			Test.Pass()
		end
	end)
end
