-- AUTO TEST — a damaged helicopter sent to a pad heals to FULL, LEAVES the deck, and is
-- billed for it. A control helicopter, damaged identically and never ordered, does none of
-- those things.
--
-- WHAT COUNTS AS THE ANSWER. Four facts, all measured at the same tick:
--
--   1. HP. The Patient must read exactly MaxHealth. RepairTick clears ResupplyType.Repair
--      only at DamageState.Undamaged (Resupply.cs:602), which is HP == MaxHP, so "nearly
--      full" is not a pass — it is the same wedge with a smaller gap.
--   2. ALTITUDE. The Patient must be OFF the deck: CenterPosition.Z strictly ABOVE the Z
--      it was sitting at when we damaged it. A DELTA against its own start, never against
--      a literal 0, so a non-zero terrain baseline cannot fake either verdict. This is the
--      wedge check and the reason the test exists. With a zero step the helicopter reaches
--      the pad, lands, heals nothing, and Resupply's only exit (activeResupplyTypes == 0,
--      Resupply.cs:339) is unreachable — so it sits at LandAltitude indefinitely. A
--      repaired airframe takes off unconditionally: OnResupplyEnding's first branch is
--      `wasRepaired || ...` (Resupply.cs:478).
--   3. THE CONTROL. Damaged at the same tick by the same call, given no order. It must
--      still be at its damaged HP and at its start altitude. If it heals, something other
--      than the pad is healing airframes and legs 1-2 measured nothing.
--   4. CASH. The player must have been billed the modelled amount, exactly.
--
-- THE MODEL. For an airframe with MaxHP H and Valued.Cost C, at Repairable.PercentageStep
-- 3 and RepairsUnits defaults (Interval 24, ValuePercentage 20), mirroring RepairTick at
-- Resupply.cs:644-671:
--
--   step        = max(1, floor(3*H/100))                 HELI (H=800): 24 HP
--   costPerStep = max(1, floor(step*(C*20)/(H*100)))     HELI (C=6000): 36 credits
--   steps       = ceil((H - startHP)/step)               480 -> 800: ceil(320/24) = 14
--   bill        = steps * costPerStep                    14 * 36 = 504
--
-- Leg 4 is asserted EXACTLY, with no tolerance band. It can be, because rules.yaml turns
-- passive income off — see below — leaving repair as the only thing in the game that can
-- move this player's purse. Note the bill is RATE-INDEPENDENT: halving PercentageStep
-- doubles the step count and halves the per-step cost. So leg 4 pins ValuePercentage, NOT
-- the rate. The rate is what legs 1 and 2 pin, by requiring full HP in a bounded window.
--
-- WHY LEG 4 USED TO BE WRONG (2026-09-05 run 260905_171321). It reported "billed -1496" —
-- the purse went UP. PlayerResources pays PassiveIncome 100 every 50 ticks by default, so
-- the 1000-tick window handed the player 20 x 100 = 2000 while charging 504 for the one
-- repair that did complete: 2000 - 504 = 1496, to the credit. The repair was right and the
-- MEASUREMENT was wrong. rules.yaml now sets PassiveIncome: 0 (and locks it), which is what
-- lets the assertion below be exact instead of a 15% band that would have hidden it.
--
-- RED (before ^Airborne gained PercentageStep: 3) fails leg 1:
--   "Patient never healed to full: 480/800 HP 1000 ticks after being sent to the pad. ...
--    [Patient hp=480/800 alt=0 (start 0) | Control hp=480/800 alt=0 (start 0) |
--     purse 20000 -> 19959, billed 41]"
-- (RED still bills ~1 credit per 24-tick Interval, because the Math.Max(1, ...) at
-- Resupply.cs:656 applies to the COST and not to the heal.)
--
-- EVERY failure prints the FULL state of both helicopters and the purse, not just the leg
-- that tripped, and a trace line goes to lua.log every 100 ticks. The first version of this
-- file reported only its first failing leg and printed nothing: the 2026-09-05 run left a
-- 0-byte lua.log and could not say whether the helicopter had worked, so the answer had to
-- be reconstructed afterwards from the cash arithmetic. A verdict that names one actor and
-- stays silent about the other is not self-diagnosing.

local START_TICKS   = 25    -- damage + order land here, not in WorldLoaded
local VERDICT_TICKS = 1025  -- 1000 ticks of simulation after the start
local TRACE_EVERY   = 100   -- a state line to lua.log this often

-- The modelled repair needs 14 steps * Interval 24 = 336 ticks once docked, plus a 16-cell
-- transit. 1000 is roughly triple that: generous on purpose, because a LATE pass is still a
-- pass while a wedge never finishes at any budget.

local DAMAGED_PCT = 60      -- above the 50% critical-damage floor (user ruling 2026-09-05)

-- Valued.Cost. There is no Lua binding for it, so this is copied from the rules and must be
-- updated with them: HELI aircraft-america.yaml:315 (Cost: 6000 as of 2026-09-05). Only
-- leg 4 reads it; legs 1-3 are unaffected if it drifts.
local HELI_COST = 6000

local NAMES = { "Patient", "Control" }

local ACTOR = {}
local start = {}            -- name -> HP we damaged it to
local startAlt = {}         -- name -> CenterPosition.Z while grounded at setup
local cashAtStart = nil

local function alt(a)
	if not a or a.IsDead then return -1 end
	return a.CenterPosition.Z
end

local function purse(p) return p.Cash + p.Resources end

-- Full state of the run on one line. Appended to every failure message and printed
-- periodically, so lua.log alone answers "what did the other leg do".
local function state(me)
	local parts = {}
	for _, n in ipairs(NAMES) do
		local a = ACTOR[n]
		if a == nil or a.IsDead then
			parts[#parts + 1] = string.format("%s DEAD", n)
		elseif not a.IsInWorld then
			parts[#parts + 1] = string.format("%s GONE-FROM-WORLD", n)
		else
			parts[#parts + 1] = string.format("%s hp=%d/%d alt=%d (start %s)",
				n, a.Health, a.MaxHealth, alt(a), tostring(startAlt[n]))
		end
	end

	local billed = "n/a"
	if cashAtStart ~= nil then
		billed = string.format("purse %d -> %d, billed %d", cashAtStart, purse(me), cashAtStart - purse(me))
	end

	return "[" .. table.concat(parts, " | ") .. " | " .. billed .. "]"
end

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
	ACTOR = { Patient = Patient, Control = Control, Pad = Pad }

	TestHarness.FocusBetween(Patient, Pad)

	Trigger.AfterDelay(START_TICKS, function()
		for _, name in ipairs({ "Patient", "Control", "Pad" }) do
			if ACTOR[name].IsDead then
				Test.Skip(name .. " died during setup — inconclusive")
				return
			end
		end

		for _, name in ipairs(NAMES) do
			local a = ACTOR[name]
			a.Health = math.floor(a.MaxHealth * DAMAGED_PCT / 100)
			start[name] = a.Health
			startAlt[name] = a.CenterPosition.Z
		end

		-- Preconditions. If the damage did not take, the run proves nothing, so SKIP rather
		-- than report a verdict that is really a broken setup.
		for _, name in ipairs(NAMES) do
			local a = ACTOR[name]
			local p = math.floor(a.Health * 100 / a.MaxHealth)
			if p >= 100 or p <= 50 then
				Test.Skip(string.format(
					"%s is at %d%% after the setup damage — wanted %d%%, and strictly between 50 and 100 so " ..
					"that neither the critical-damage burn nor an already-full airframe is in play",
					name, p, DAMAGED_PCT))
				return
			end
		end

		cashAtStart = purse(me)
		print(string.format("[air-repair] t=%d setup done, ordering Patient to Pad %s",
			START_TICKS, state(me)))

		-- ReturnToBase(dest) with an explicit destination lands and resupplies
		-- unconditionally (alwaysLand short-circuits ShouldLandAtBuilding,
		-- ReturnToBase.cs:73-76), so this drives the repair branch without depending on
		-- ChooseResupplier's Rearmable-keyed host search. Control gets NO order.
		Patient.ReturnToBase(Pad)
	end)

	-- Periodic trace. Cheap, and it is the difference between a failed run that explains
	-- itself and one that has to be reconstructed from arithmetic afterwards.
	for t = START_TICKS + TRACE_EVERY, VERDICT_TICKS, TRACE_EVERY do
		Trigger.AfterDelay(t, function()
			print(string.format("[air-repair] t=%d %s", t, state(me)))
		end)
	end

	Trigger.AfterDelay(VERDICT_TICKS, function()
		if cashAtStart == nil then
			Test.Skip("setup never ran — no baseline to measure against")
			return
		end

		local now = state(me)
		local spent = cashAtStart - purse(me)
		local elapsed = VERDICT_TICKS - START_TICKS
		print(string.format("[air-repair] t=%d VERDICT %s", VERDICT_TICKS, now))

		-- Leg 1 + 2. The Patient must be FULL and OFF THE DECK. Reported together because
		-- the wedge produces both symptoms and naming one alone misdiagnoses it.
		if Patient.IsDead or not Patient.IsInWorld then
			Test.Fail(string.format(
				"Patient is gone — it was damaged to %d%% and sent to a pad, not into a fight. %s",
				DAMAGED_PCT, now))
			return
		end

		if Patient.Health < Patient.MaxHealth then
			Test.Fail(string.format(
				"Patient never healed to full: %d/%d HP %d ticks after being sent to the pad. A zero repair " ..
				"step heals nothing AND never clears ResupplyType.Repair (cleared only at " ..
				"DamageState.Undamaged), so the airframe is wedged on the deck. %s",
				Patient.Health, Patient.MaxHealth, elapsed, now))
			return
		end

		if alt(Patient) <= startAlt.Patient then
			Test.Fail(string.format(
				"Patient healed to full but is still ON the deck: alt=%d, unchanged from the %d it was " ..
				"grounded at, %d ticks later. Resupply's only exit is activeResupplyTypes == 0, and a " ..
				"repaired airframe must take off via OnResupplyEnding's wasRepaired branch — full HP with " ..
				"no take-off is a stuck activity. %s",
				alt(Patient), startAlt.Patient, elapsed, now))
			return
		end

		-- Leg 3. THE CONTROL: same damage, no order, no pad.
		if Control.IsDead or not Control.IsInWorld then
			Test.Fail("Control is gone — a damaged helicopter with no orders must sit where it was left. " .. now)
			return
		end

		if Control.Health ~= start.Control then
			Test.Fail(string.format(
				"Control went from %d HP to %d without being sent anywhere. Something other than the pad is " ..
				"changing airframe health, so the Patient result proves nothing about repair. %s",
				start.Control, Control.Health, now))
			return
		end

		if alt(Control) ~= startAlt.Control then
			Test.Fail(string.format(
				"Control changed altitude on its own (%d -> %d). It was given no order, so the Patient's " ..
				"altitude is not evidence that it left a pad. %s",
				startAlt.Control, alt(Control), now))
			return
		end

		-- Leg 4. THE BILL, exactly — passive income is off, so repair is the only thing that
		-- can move this purse.
		local expected = modelledBill(Patient, start.Patient, HELI_COST)
		if spent ~= expected then
			Test.Fail(string.format(
				"repair bill was %d credits, modelled exactly %d. The Patient reached full HP, so the heal " ..
				"works and the CHARGE is what disagrees. Check in this order: (a) is passive income really " ..
				"off — a bill smaller than modelled, or a negative one, means income is still being paid and " ..
				"rules.yaml's PassiveIncome: 0 did not take; (b) has HELI been repriced away from Cost 6000, " ..
				"which is hardcoded as HELI_COST at the top of this file; (c) has " ..
				"RepairsUnits.ValuePercentage moved off 20. %s",
				spent, expected, now))
			return
		end

		Test.Pass()
	end)
end
