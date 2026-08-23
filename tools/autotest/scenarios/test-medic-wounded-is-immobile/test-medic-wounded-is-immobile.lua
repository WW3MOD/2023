-- AUTO TEST: a medic below 50% health can still treat the man at his elbow, and
-- cannot reach a man 6 cells away — because his speed is zero, not reduced.
--
-- The user's question is whether a wounded medic is "genuinely useless rather
-- than merely slow". Those two have the same look on screen (a medic standing
-- still is prone either way — InfantryStates.ProneCondition includes `!moving`)
-- and completely different consequences, so this separates them by outcome:
--
--   Elbow treated  => his armament is live; he is not switched off.
--   Across ignored => his legs are gone; he cannot bring that ability anywhere.
--
-- Both together mean a wounded medic is not a degraded medic. He is a fixed
-- installation that heals whatever is already touching him.
--
-- He is also bleeding the whole time (ChangesHealth@BleedOut, StartIfBelow 50)
-- and cannot treat himself — HealerAutoTarget.FindBestTarget skips `a == self`
-- (HealerAutoTarget.cs:331). Nothing in this test rescues him.
--
-- PITFALL: TestHarness.TicksPerSecond is 25, but the mod runs at Timestep 60 —
-- 16.67 ticks/second. Budget in ticks and convert.
--
-- PITFALL: no assertion here keys off pulse count. His cadence is multiplied by
-- BurstWaitMultiplier@HeavyDamage (300) and multiplied again if he bleeds into
-- Critical, so the only stable quantities are "reached full" and "never rose".
local BudgetTicks = 900 -- ~54 real seconds
local MedicStartPercent = 40
local PatientStartPercent = 70

local elapsed = 0

WorldLoaded = function()
	TestHarness.FocusBetween(Medic, Across)

	Medic.Health = math.floor(Medic.MaxHealth * MedicStartPercent / 100)
	Elbow.Health = math.floor(Elbow.MaxHealth * PatientStartPercent / 100)
	Across.Health = math.floor(Across.MaxHealth * PatientStartPercent / 100)

	local acrossBaseline = Across.Health

	local function percent(actor)
		if actor.IsDead then
			return -1
		end

		return math.floor(actor.Health * 100 / actor.MaxHealth)
	end

	TestHarness.AssertWithin(BudgetTicks / TestHarness.TicksPerSecond, function()
		-- He bleeds from 40% at 1% per 50 ticks, so over this budget he loses
		-- ~18 points and ends around 22%. If he dies the run is inconclusive
		-- rather than informative, so say which happened.
		if Medic.IsDead then
			return "fail: the wounded medic bled to death after " .. elapsed
				.. " ticks — inconclusive about his reach, but note he could not treat himself"
		end

		if Elbow.IsDead or Across.IsDead then
			return "fail: a patient died"
		end

		elapsed = elapsed + 1

		-- The refuting outcome: if he reaches the far man, he is slow, not frozen,
		-- and the premise of this test is wrong. Report it as the finding it is.
		if Across.Health > acrossBaseline then
			return "fail: premise wrong — the wounded medic DID reach and treat a man 6 cells away,"
				.. " so sub-50% movement is a slow rather than the full stop SpeedMultiplier: 0 implies"
		end

		if Elbow.Health >= Elbow.MaxHealth then
			-- He finished the adjacent man without ever touching the far one.
			-- That is the whole claim: hands working, legs gone.
			return true
		end

		if elapsed >= BudgetTicks then
			return "fail: the wounded medic did not finish even the man at his elbow in "
				.. BudgetTicks .. " ticks — elbow " .. percent(Elbow) .. "% (from "
				.. PatientStartPercent .. "), medic himself " .. percent(Medic) .. "% (from "
				.. MedicStartPercent .. "). His armament may be gated too, not just his legs"
		end

		return false
	end, "wounded-medic assertion did not resolve within " .. BudgetTicks .. " ticks")
end
