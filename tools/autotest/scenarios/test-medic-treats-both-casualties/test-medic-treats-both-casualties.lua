-- AUTO TEST: an unordered medic between two wounded men finishes BOTH of them.
--
-- This is the question a player's expectation actually rests on. The medic is
-- never selected and never ordered: HealerAutoTarget picks, AutoFollowAlly
-- walks. Two men are hurt, nobody is micromanaging, and the medic is supposed
-- to work through them.
--
-- test-medic-focus-two-patients already covers the catastrophic case — it passes
-- as soon as EITHER man reaches full, because it was written to catch a medic
-- who ping-ponged and finished nobody. That makes it blind to the outcome
-- between: treat one, then stop. This test separates the three outcomes and
-- names which one happened in its failure text, so a run here is diagnostic
-- rather than just red.
--
-- Both patients start ABOVE StabilizeThreshold (50) so the critical-first bonus
-- (10000) is not silently doing the committing, and BELOW
-- MaxPatientHealthPercent (90) so the medic is willing to pick either up.
--
-- PITFALL: TestHarness.TicksPerSecond is 25, but the mod runs at Timestep 60 —
-- 16.67 ticks/second. A "second" passed to AssertWithin is therefore 1.5 real
-- seconds. Budget in ticks and convert.
--
-- PITFALL: nothing here may key off pulse count or step size. The heal is
-- 20 HP per 3.0s (BurstWait 50, DamagePercent -10) and was 10 HP per 1.5s; the
-- HP/s is identical and only durations are stable across that change. At
-- 3.33%/s, 40 points of healing is ~12 real seconds, so finishing both men is
-- ~25 seconds of treatment plus one walk between them.
local BudgetTicks = 1500 -- ~90 real seconds: ~25s of treatment, the rest slack for walking
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

		local nearDone = PatientNear.Health >= PatientNear.MaxHealth
		local farDone = PatientFar.Health >= PatientFar.MaxHealth

		if nearDone and farDone then
			return true
		end

		elapsed = elapsed + 1
		if elapsed >= BudgetTicks then
			-- Name the outcome rather than just reporting numbers: which of the
			-- three happened is the whole point of running this.
			local finished = 0
			if nearDone then
				finished = finished + 1
			end

			if farDone then
				finished = finished + 1
			end

			local verdict
			if finished == 0 then
				-- Deliberately does NOT name a cause. "Neither man finished" is
				-- produced by at least two unrelated mechanisms — a medic who
				-- ping-ponged between them, and a medic who declined to acquire
				-- either (MaxPatientHealthPercent set below their health) — and
				-- this predicate cannot tell them apart. Verified: zeroing
				-- SwitchMargin and DistancePenaltyPerCell still passes, while
				-- MaxPatientHealthPercent: 1 produces exactly this branch.
				-- Check whether either man's health moved AT ALL before blaming
				-- the ranking.
				verdict = "he finished NEITHER man. Both were left wounded with a medic between them;"
					.. " that is either a medic trading attention between the two, or one that never"
					.. " acquired either — compare the healths below against their starting values"
			else
				verdict = "he finished exactly ONE man and then stopped — the other was left wounded"
					.. " with a medic standing idle beside him, which no player would expect"
			end

			return "fail: " .. verdict .. ". Near " .. percent(PatientNear) .. "% (from "
				.. NearStartPercent .. "), far " .. percent(PatientFar) .. "% (from "
				.. FarStartPercent .. ") after " .. BudgetTicks .. " ticks"
		end

		return false
	end, "treats-both assertion did not resolve within " .. BudgetTicks .. " ticks")
end
