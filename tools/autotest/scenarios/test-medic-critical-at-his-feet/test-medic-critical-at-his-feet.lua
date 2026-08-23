-- AUTO TEST: a man collapses to 20% AT THE MEDIC'S FEET while the medic is
-- topping up a lighter wound. Does the medic divert to him?
--
-- This asks the one question `StabilizeThreshold` exists to answer, in exactly
-- the shape it was written for. HealerAutoTarget.SelectPatient has a
-- stabilize-and-switch block (HealerAutoTarget.cs:216-229): if the man being
-- treated is at or above StabilizeThreshold, go looking for a critical
-- unclaimed patient and hand the case over. Bystander at 20% is critical,
-- unclaimed, and one cell away. Scratch at 55% is above the threshold. Every
-- precondition that block tests for is true.
--
-- The prediction from code, which this run exists to confirm or refute:
-- it never gets the chance. SelectPatient's only caller is
-- TryGetAutoTargetOverride, whose only caller is AutoTarget.ScanForTarget, which
-- is reachable only when the medic is IDLE or has a move child running.
-- Treating is a top-level Attack activity (AttackBase.cs:740-741) and
-- Attack.Tick returns "keep running" through the whole BurstWait gap
-- (Attack.cs:194-197), so a treating medic is never idle and never scans.
-- Triage is therefore an ACQUISITION-time decision only.
--
-- Distance is removed as an explanation on purpose: both men are adjacent, so
-- DistancePenaltyPerCell (3) is worth at most 3 points either way.
--
-- PITFALL: TestHarness.TicksPerSecond is 25, but the mod runs at Timestep 60 —
-- 16.67 ticks/second. A "second" passed to AssertWithin is therefore 1.5 real
-- seconds. Budget in ticks and convert, so the numbers below mean what they say.
--
-- PITFALL: do not assert on pulse cadence or step size. The heal is being
-- reshaped from 10 HP/1.5s to 20 HP/3.0s and the HP/s is identical either way,
-- so everything here is expressed as a DURATION or as "health went up".
local TotalBudgetTicks = 900 -- ~54 real seconds, comfortably past a full 55->100 treatment
local ScratchStartPercent = 55
local BystanderWoundPercent = 20

-- A diverting medic would reach Bystander within a pulse or two of the collapse;
-- he is already standing next to him. 130 ticks is ~8 real seconds — generous for
-- a turn and a shot, and far short of the ~225 ticks a full 55->100 treatment of
-- Scratch takes. That gap is what makes this test discriminate.
local DivertBudgetTicks = 130

local elapsed = 0
local wounded = false
local woundedAtTick = 0
local bystanderBaseline = 0

WorldLoaded = function()
	TestHarness.FocusBetween(Bystander, Scratch)

	Scratch.Health = math.floor(Scratch.MaxHealth * ScratchStartPercent / 100)
	local scratchBaseline = Scratch.Health

	local function percent(actor)
		if actor.IsDead then
			return -1
		end

		return math.floor(actor.Health * 100 / actor.MaxHealth)
	end

	TestHarness.AssertWithin(TotalBudgetTicks / TestHarness.TicksPerSecond, function()
		if Medic.IsDead then
			return "fail: medic died"
		end

		if Scratch.IsDead then
			return "fail: the lightly wounded man died"
		end

		elapsed = elapsed + 1

		-- Phase 1: wait until treatment on Scratch is demonstrably under way.
		-- Only then is the medic committed, and only then does the question mean
		-- anything. Wounding Bystander before this would be an acquisition-time
		-- triage test, which the critical-first bonus already passes.
		if not wounded then
			if Scratch.Health > scratchBaseline then
				Bystander.Health = math.floor(Bystander.MaxHealth * BystanderWoundPercent / 100)
				bystanderBaseline = Bystander.Health
				wounded = true
				woundedAtTick = elapsed
			elseif elapsed >= TotalBudgetTicks then
				return "fail: the medic never started treating the lightly wounded man at all"
					.. " — scratch stuck at " .. percent(Scratch) .. "% (from " .. ScratchStartPercent .. ")"
			end

			return false
		end

		-- Phase 2: Bystander is critical and one cell away. Below 50% he also
		-- bleeds (ChangesHealth@BleedOut, -1% per 50 ticks), so his health only
		-- ever falls unless somebody treats him. Any rise is treatment.
		local waited = elapsed - woundedAtTick

		if Bystander.IsDead then
			return "fail: the critical man BLED OUT at the medic's feet after " .. waited
				.. " ticks — scratch reached " .. percent(Scratch) .. "%"
		end

		if Bystander.Health > bystanderBaseline then
			if waited <= DivertBudgetTicks then
				return true
			end

			return "fail: the medic did not divert — a man at " .. BystanderWoundPercent
				.. "% collapsed one cell away and waited " .. waited .. " ticks ("
				.. string.format("%.1f", waited / 16.67) .. " real seconds) for his first treatment."
				.. " Scratch was at " .. percent(Scratch) .. "% when it finally came (started "
				.. ScratchStartPercent .. "%). Triage cannot run while the medic is treating:"
				.. " StabilizeThreshold's stabilize-and-switch is unreachable from inside an Attack activity"
		end

		if elapsed >= TotalBudgetTicks then
			return "fail: the critical man was NEVER treated in " .. TotalBudgetTicks
				.. " ticks — he waited " .. waited .. " ticks at the medic's feet, now at "
				.. percent(Bystander) .. "% (wounded to " .. BystanderWoundPercent .. "), while scratch reached "
				.. percent(Scratch) .. "%"
		end

		return false
	end, "critical-at-his-feet assertion did not resolve within " .. TotalBudgetTicks .. " ticks")
end
