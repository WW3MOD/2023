-- AUTO TEST: a medic with two comparable patients commits to one and finishes
-- him, instead of trading his attention between them and topping up neither.
--
-- The medic is never selected and never ordered. Everything here is his
-- autonomy: HealerAutoTarget picks the patient, AutoFollowAlly walks him over.
--
-- Both patients start ABOVE StabilizeThreshold (50) deliberately. Below it the
-- critical-first bonus is 10000, which would pin the medic to one man for
-- reasons that have nothing to do with the stickiness under test here.
--
-- The 2-point gap between them is the trap. One heal pulse is worth 5 points
-- (DamagePercent -5 on the Heal warhead), so the first pulse lifts the nearer
-- man from 60% to 65% and puts him BEHIND the man at 62%. Without hysteresis
-- the next rescan hands the case over, and the medic walks 6 cells to a man he
-- will treat exactly once before the ranking inverts again. Crossing that
-- ground takes far longer than the 50-tick reload, so almost none of his
-- healing is ever delivered.

-- PITFALL: TestHarness.TicksPerSecond is 25, but the mod runs at Timestep 60 —
-- 16.67 ticks/second. A "second" passed to AssertWithin is therefore 1.5 real
-- seconds. Budget in ticks and convert, so the number below means what it says.
local BudgetTicks = 1100 -- ~66 real seconds
local NearStartPercent = 60
local FarStartPercent = 62

local elapsed = 0

WorldLoaded = function()
	TestHarness.FocusBetween(PatientNear, PatientFar)

	PatientNear.Health = math.floor(PatientNear.MaxHealth * NearStartPercent / 100)
	PatientFar.Health = math.floor(PatientFar.MaxHealth * FarStartPercent / 100)

	local function percent(actor)
		if actor.IsDead then
			return -1
		end

		return math.floor(actor.Health * 100 / actor.MaxHealth)
	end

	TestHarness.AssertWithin(BudgetTicks / TestHarness.TicksPerSecond, function()
		if Medic.IsDead then
			return "fail: medic died"
		end

		if PatientNear.IsDead or PatientFar.IsDead then
			return "fail: a patient died"
		end

		-- Either man finished is a pass: which of the two he commits to is a
		-- tuning question, but finishing NEITHER is the bug.
		if PatientNear.Health >= PatientNear.MaxHealth or PatientFar.Health >= PatientFar.MaxHealth then
			return true
		end

		elapsed = elapsed + 1
		if elapsed >= BudgetTicks then
			return "fail: neither patient reached full health in " .. BudgetTicks .. " ticks"
				.. " — near " .. percent(PatientNear) .. "% (from " .. NearStartPercent .. ")"
				.. ", far " .. percent(PatientFar) .. "% (from " .. FarStartPercent .. ")"
				.. "; a medic that keeps changing patient tops up neither"
		end

		return false
	end, "medic focus assertion did not resolve within " .. BudgetTicks .. " ticks")
end
