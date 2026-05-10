-- AUTO TEST: 4 Bradleys fire one WGM burst each (2 missiles per burst,
-- so 8 missiles total) at a stationary sponge-HP t90. Measure damage to
-- compute hit rate. Each WGM hit ≈ 12000 damage (10000 target + 2000
-- spread); a clean miss does 0. We log the absolute damage so we can
-- see the actual hit rate when iterating on Inaccuracy / lock-on params.
--
-- Threshold: damage >= 0.80 of max (≈ 6.4 / 8 hits). With Inaccuracy = 768
-- pre-tune, this fails and surfaces the real hit rate. Goal post-tune:
-- 90 %+.

local DeadlineSeconds = 10
local Bradleys = { B0, B1, B2, B3 }
-- Per-hit accounting: WGM target warhead = 10000 (always applies on a hit
-- inside the hitshape), spread warhead = 2000 with Spread 64 (rarely
-- applies because the missile detonates ~298 wdist from target center
-- per CloseEnough — that's outside the 64 wdist spread radius). So
-- "max damage" for a clean burst is 8 × 10000, ignoring spread bonus.
local TargetDmgPerHit = 10000
local Shots = 8 -- 4 bradleys × 2 missiles
local MaxDmg = Shots * TargetDmgPerHit
-- Threshold expressed as integer percent to avoid float comparisons.
local HitThresholdPct = 70

WorldLoaded = function()
	TestHarness.FocusBetween(B0, Target)
	TestHarness.Select(B0)
	Target.Stance = "HoldFire"

	for _, b in ipairs(Bradleys) do
		b.Attack(Target, false, false)
	end

	local startHp = Target.Health
	local ticks = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)
	Trigger.AfterDelay(ticks, function()
		if Target.IsDead then
			Test.Fail("Target died — sponge HP not high enough or accuracy is way over expected")
			return
		end
		local damage = startHp - Target.Health
		local pct = math.floor(damage * 100 / MaxDmg)
		local fired = 0
		for _, b in ipairs(Bradleys) do
			fired = fired + (8 - b.AmmoCount("secondary-ammo"))
		end
		local note = string.format("dmg=%d/%d (%d%%) shots_fired=%d/%d", damage, MaxDmg, pct, fired, Shots)
		if fired < Shots then
			Test.Fail("not all shots fired: " .. note)
		elseif damage * 100 < MaxDmg * HitThresholdPct then
			Test.Fail("low accuracy: " .. note)
		else
			Test.Pass(note)
		end
	end)
end
