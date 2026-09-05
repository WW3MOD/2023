-- AUTO TEST — a damaged airframe sent to a pad heals to FULL, LEAVES the deck, and is
-- billed for it. A control airframe, damaged identically and never ordered, does none of
-- those things.
--
-- WHAT COUNTS AS THE ANSWER. Four facts, all measured at the same tick:
--
--   1. HP. Each patient must read exactly MaxHealth. RepairTick clears
--      ResupplyType.Repair only at DamageState.Undamaged (Resupply.cs:602), which is
--      HP == MaxHP, so "nearly full" is not a pass — it is the same wedge with a
--      smaller gap.
--   2. ALTITUDE. Each patient must be OFF the deck (CenterPosition.Z > 0). This is the
--      wedge check and the reason the test exists. With a zero step the airframe
--      reaches the pad, lands, heals nothing, and Resupply's only exit
--      (activeResupplyTypes == 0, Resupply.cs:339) is unreachable, so it sits parked at
--      LandAltitude indefinitely. A repaired airframe takes off unconditionally:
--      OnResupplyEnding's first branch is `wasRepaired || ...` (Resupply.cs:478).
--   3. THE CONTROL. Damaged at the same tick by the same call, given no order. It must
--      still be at its damaged HP and still on the ground. If it heals, something other
--      than the pad is healing airframes and legs 1-2 measured nothing.
--   4. CASH. The player must have been billed close to the modelled amount.
--
-- THE MODEL, so a failure can be read without opening the engine. For an airframe with
-- MaxHP H and Valued.Cost C, at Repairable.PercentageStep 3 and RepairsUnits defaults
-- (Interval 24, ValuePercentage 20), mirroring RepairTick at Resupply.cs:644-671:
--
--   step        = max(1, floor(3*H/100))                 HELI/A10 (H=800): 24 HP
--   costPerStep = max(1, floor(step*(C*20)/(H*100)))     HELI/A10 (C=6000): 36 credits
--   steps       = ceil((H - startHP)/step)               480 -> 800: ceil(320/24) = 14
--   bill        = steps * costPerStep                    14 * 36 = 504
--
-- Modelled total for the two patients: 1008. Note the bill is RATE-INDEPENDENT — halving
-- PercentageStep doubles the step count and halves the per-step cost — so leg 4 pins
-- ValuePercentage, NOT the rate. The rate is what legs 1 and 2 pin, by requiring full HP
-- inside a bounded window.
--
-- RED (before ^Airborne gained PercentageStep: 3) fails leg 1 on whichever patient is
-- reached first:
--   "Plane never healed to full: 480/800 HP (60%) 1000 ticks after being sent to its
--    pad, alt=0, player billed 41. A zero repair step heals nothing AND never clears
--    ResupplyType.Repair, so the airframe is wedged on the deck."
-- (RED still bills ~1 credit per 24-tick interval, because the Math.Max(1, ...) at
-- Resupply.cs:656 applies to the COST and not to the heal — hence "billed 41" for a
-- repair that did nothing.)

-- Budgeted in TICKS and passed straight to Trigger.AfterDelay, which takes ticks. Nothing
-- here goes through TestHarness.TicksPerSecond, so the whole file is immune to that
-- value and to any future correction of it.
local START_TICKS   = 25    -- damage + orders land here, not in WorldLoaded
local VERDICT_TICKS = 1025  -- 1000 ticks of simulation after the start

-- The modelled repair needs 14 steps * Interval 24 = 336 ticks once docked, plus a
-- 16-cell transit. 1000 is roughly triple that: generous on purpose, because a LATE pass
-- is still a pass while a wedge never finishes at any budget.

local DAMAGED_PCT = 60      -- above the 50% critical-damage floor (user ruling 2026-09-05)
local CASH_TOLERANCE_PCT = 15

-- Valued.Cost per patient. There is no Lua binding for Valued.Cost, so these are copied
-- from the rules and must be updated with them: A10 aircraft-america.yaml:453, HELI
-- aircraft-america.yaml:315 (both `Cost: 6000` as of 2026-09-05). Only leg 4 reads
-- them; legs 1-3 are unaffected if they drift.
local COST = { Plane = 6000, Heli = 6000 }

-- Ordered lists, never pairs(): iteration order is part of what a reader reproduces.
local PATIENTS = { { "Plane", "PlanePad" }, { "Heli", "HeliPad" } }
local DAMAGED  = { "Plane", "Heli", "Control" }

local ACTOR = {}            -- name -> actor, filled in WorldLoaded
local start = {}            -- name -> HP we damaged it to
local cashAtStart = nil

local function pct(a)
	if not a or a.IsDead then return -1 end
	return math.floor(a.Health * 100 / a.MaxHealth)
end

local function alt(a)
	if not a or a.IsDead then return -1 end
	return a.CenterPosition.Z
end

local function purse(p) return p.Cash + p.Resources end

-- Modelled bill for one airframe healed from `from` HP to full. Integer division included,
-- so a mismatch is a real disagreement with RepairTick and not a rounding artefact.
local function modelledBill(a, from, cost)
	local h = a.MaxHealth
	local step = math.max(1, math.floor(3 * h / 100))
	local costPerStep = math.max(1, math.floor(step * (cost * 20) / (h * 100)))
	local steps = math.ceil((h - from) / step)
	return steps * costPerStep
end

WorldLoaded = function()
	local me = Player.GetPlayer("Me")
	ACTOR = { Plane = Plane, Heli = Heli, Control = Control, PlanePad = PlanePad, HeliPad = HeliPad }

	TestHarness.FocusBetween(Heli, HeliPad)

	Trigger.AfterDelay(START_TICKS, function()
		for _, name in ipairs({ "Plane", "Heli", "Control", "PlanePad", "HeliPad" }) do
			if ACTOR[name].IsDead then
				Test.Skip(name .. " died during setup — inconclusive")
				return
			end
		end

		for _, name in ipairs(DAMAGED) do
			local a = ACTOR[name]
			a.Health = math.floor(a.MaxHealth * DAMAGED_PCT / 100)
			start[name] = a.Health
		end

		-- Preconditions. If the damage did not take, the run proves nothing, so SKIP
		-- rather than report a verdict that is really a broken setup.
		for _, name in ipairs(DAMAGED) do
			local p = pct(ACTOR[name])
			if p >= 100 or p <= 50 then
				Test.Skip(string.format(
					"%s is at %d%% after the setup damage — wanted %d%%, and strictly between 50 and 100 so " ..
					"that neither the critical-damage burn nor an already-full airframe is in play",
					name, p, DAMAGED_PCT))
				return
			end
		end

		cashAtStart = purse(me)

		-- ReturnToBase(dest) with an explicit destination lands and resupplies
		-- unconditionally (alwaysLand short-circuits ShouldLandAtBuilding,
		-- ReturnToBase.cs:73-76), so this drives the repair branch without depending on
		-- ChooseResupplier's Rearmable-keyed host search. Control gets NO order.
		for _, leg in ipairs(PATIENTS) do
			ACTOR[leg[1]].ReturnToBase(ACTOR[leg[2]])
		end
	end)

	Trigger.AfterDelay(VERDICT_TICKS, function()
		if cashAtStart == nil then
			Test.Skip("setup never ran — no baseline to measure against")
			return
		end

		local spent = cashAtStart - purse(me)
		local elapsed = VERDICT_TICKS - START_TICKS

		-- Legs 1 and 2, reported together: the wedge produces both symptoms, and naming
		-- one of them alone misdiagnoses it.
		for _, leg in ipairs(PATIENTS) do
			local name = leg[1]
			local a = ACTOR[name]

			if a.IsDead or not a.IsInWorld then
				Test.Fail(string.format(
					"%s is gone (dead=%s) — it was damaged to %d%% and sent to a pad, not into a fight.",
					name, tostring(a.IsDead), DAMAGED_PCT))
				return
			end

			if a.Health < a.MaxHealth then
				Test.Fail(string.format(
					"%s never healed to full: %d/%d HP (%d%%) %d ticks after being sent to its pad, alt=%d, " ..
					"player billed %d. A zero repair step heals nothing AND never clears ResupplyType.Repair " ..
					"(cleared only at DamageState.Undamaged), so the airframe is wedged on the deck.",
					name, a.Health, a.MaxHealth, pct(a), elapsed, alt(a), spent))
				return
			end

			if alt(a) <= 0 then
				Test.Fail(string.format(
					"%s healed to full (%d/%d) but is still ON the deck at alt=%d after %d ticks. Resupply's " ..
					"only exit is activeResupplyTypes == 0, and a repaired airframe must take off via " ..
					"OnResupplyEnding's wasRepaired branch — full HP with no take-off is a stuck activity.",
					name, a.Health, a.MaxHealth, alt(a), elapsed))
				return
			end
		end

		-- Leg 3. THE CONTROL: same damage, no order, no pad.
		if Control.IsDead or not Control.IsInWorld then
			Test.Fail("Control is gone — a damaged airframe with no orders must simply sit where it was left.")
			return
		end

		if Control.Health ~= start.Control then
			Test.Fail(string.format(
				"Control went from %d HP to %d without being sent anywhere. Something other than the pad is " ..
				"changing airframe health, so the Plane and Heli results prove nothing about repair.",
				start.Control, Control.Health))
			return
		end

		if alt(Control) > 0 then
			Test.Fail(string.format(
				"Control took off on its own (alt=%d). It was given no order, so the patients' altitudes are " ..
				"not evidence that they left a pad.", alt(Control)))
			return
		end

		-- Leg 4. THE BILL.
		local expected = modelledBill(Plane, start.Plane, COST.Plane)
			+ modelledBill(Heli, start.Heli, COST.Heli)
		local slack = math.floor(expected * CASH_TOLERANCE_PCT / 100)
		if spent < expected - slack or spent > expected + slack then
			Test.Fail(string.format(
				"repair bill was %d credits, modelled %d (+/-%d). Both airframes reached full HP, so the heal " ..
				"works and the CHARGE is what disagrees. RepairsUnits.ValuePercentage is 20, i.e. 20%% of an " ..
				"airframe's Valued.Cost over a full zero-to-max repair; if A10 or HELI has been repriced, " ..
				"update COST at the top of this file.",
				spent, expected, slack))
			return
		end

		Test.Pass()
	end)
end
