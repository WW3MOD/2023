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
-- THIS IS A CHARACTERIZATION TEST AND IT PINS BEHAVIOUR WE DO NOT WANT.
-- Measured on main @ 96f47c47, twice, identical both times: the critical man
-- waited 250 ticks — 15.0 real seconds — and the medic finished the lighter
-- wound to 100% first. That is the documented reality, so this test asserts it
-- rather than sitting red in the tree.
--
-- What SHOULD happen is the other branch: a man at 20% one cell away is diverted
-- to within a pulse or two. If someone makes triage preemptible, THIS TEST WILL
-- GO RED — and that red is correct and welcome. Invert it: assert the divert
-- happens inside DivertBudgetTicks and delete this notice. Do not "fix" the test
-- by widening the wait.
--
-- The prediction from code, which the run confirmed: it never gets the chance.
-- SelectPatient's only caller is
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
			-- The documented behaviour: he did NOT divert, and the wait ran past
			-- the point a diverting medic would have arrived. Pinning this.
			if waited > DivertBudgetTicks then
				return true
			end

			-- The good outcome. If this fires, triage became preemptible and this
			-- test has served its purpose — see the notice at the top of the file
			-- and invert the assertion rather than relaxing it.
			return "fail: the medic DIVERTED to the critical man after only " .. waited
				.. " ticks, inside the " .. DivertBudgetTicks .. "-tick budget. That is BETTER than the"
				.. " behaviour this test was written to pin (250 ticks / 15.0s on main @ 96f47c47)."
				.. " Triage is now preemptible — invert this test to assert the divert"
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
