-- AUTO TEST: regression coverage for the "missile falls short" bug.
-- Bradley and t90 are on the exact same row, no trees, sponge HP. The
-- missile must reach the t90's hitshape and damage it — not detonate on
-- the ground 1.7 cells short of target.
--
-- Pre-fix (no TerrainHeightAware on WGM projectile):
--   missile detonates "GROUND" at horiz=1768 wdist short of target,
--   t90 takes 0 damage.
-- Post-fix:
--   missile detonates within or at edge of t90 hitshape, t90 takes
--   close to per-shot warhead damage.
--
-- Threshold: damage > 50 % of single-shot target damage. With Burst 2
-- (committed) the Bradley fires 2 missiles in 8 s; even if one is
-- imperfect, the other should land cleanly.

local DeadlineSeconds = 10
local TargetDmgPerHit = 10000
local MinDmgFloor = TargetDmgPerHit / 2 -- catches any non-zero hit

WorldLoaded = function()
	TestHarness.FocusBetween(Bradley, Target)
	TestHarness.Select(Bradley)

	Target.Stance = "HoldFire"
	Bradley.Attack(Target, false, false)

	local startHp = Target.Health
	local ticks = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)
	Trigger.AfterDelay(ticks, function()
		if Target.IsDead then
			Test.Pass("target killed (sponge override too low?)")
			return
		end
		local damage = startHp - Target.Health
		local fired = 8 - Bradley.AmmoCount("secondary-ammo")
		local note = string.format("dmg=%d fired=%d", damage, fired)
		if fired == 0 then
			Test.Fail("Bradley did not fire at all: " .. note)
		elseif damage < MinDmgFloor then
			Test.Fail("missile fell short or missed: " .. note)
		else
			Test.Pass(note)
		end
	end)
end
