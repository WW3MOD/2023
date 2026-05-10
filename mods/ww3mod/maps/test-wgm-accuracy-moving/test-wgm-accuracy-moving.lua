-- AUTO TEST: 4 Bradleys fire WGM at a t90 moving perpendicular to the
-- firing line. Worst-case for guided missile tracking. Measures hit rate
-- to gauge HorizontalRateOfTurn / lead-target effectiveness.

local DeadlineSeconds = 12
local Bradleys = { B0, B1, B2, B3 }
local TargetDmgPerHit = 10000
local Shots = 8
local MaxDmg = Shots * TargetDmgPerHit
-- Moving target is harder to hit — accept a lower threshold but still
-- demonstrate tracking is working at all (>50%).
local HitThresholdPct = 50

WorldLoaded = function()
	TestHarness.FocusBetween(B0, Target)
	TestHarness.Select(B0)
	-- t90 walks straight North at full speed. Force-attack the SE-corner
	-- so the path is predictable; with HoldFire it won't shoot back.
	Target.Stance = "HoldFire"
	Target.Move(CPos.New(8, 1))

	for _, b in ipairs(Bradleys) do
		b.Attack(Target, false, false)
	end

	local startHp = Target.Health
	local ticks = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)
	Trigger.AfterDelay(ticks, function()
		if Target.IsDead then
			Test.Fail("Target died — HP override missing or accuracy way over expected")
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
