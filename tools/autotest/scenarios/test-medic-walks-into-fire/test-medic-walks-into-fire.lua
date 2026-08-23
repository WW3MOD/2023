-- AUTO TEST: a medic crosses ground covered by an enemy rifleman to reach a
-- casualty. Does he arrive, and what state is he in when he gets there?
--
-- Two facts meet here and the combination is the point:
--   1. Nothing in his approach avoids danger. AutoFollowAlly paths on distance
--      (AutoFollowAlly.cs:146, :191-207) and HealerAutoTarget scores on health
--      and distance (HealerAutoTarget.cs:295-317). No threat term anywhere.
--   2. Below 50% his SpeedMultiplier is 0 (infantry.yaml:1082-1084).
-- So the fire that wounds him is also the fire that traps him: he freezes in
-- the open, under the gun that just shot him, and cannot be recovered.
--
-- THIS IS A CHARACTERIZATION TEST AND IT PINS BEHAVIOUR WE DO NOT WANT.
-- Measured on main @ 96f47c47 at two seeds: 1017 left the medic at 2%, frozen,
-- with the casualty dead; 4242 killed the medic outright at 125 ticks. The
-- invariant across both is that HE DOES NOT COME BACK INTACT, and that is what
-- is asserted here — a stable claim, unlike the exact health, which hit rolls
-- decide.
--
-- What SHOULD happen is that a medic can run one casualty errand across ground
-- covered by a single rifleman and survive it. If danger-aware pathing, a
-- retreat-when-hurt rule, or a softer sub-50% speed floor lands, THIS TEST WILL
-- GO RED — and that red is correct. Invert it to assert the errand completes
-- with the medic still mobile, and delete this notice.
--
-- It deliberately does NOT assert that he refuses to cross: a medic who will not
-- go to wounded men is not a better medic.
--
-- PITFALL: this scenario contains live combat, so unlike the other medic
-- scenarios its result is seed-sensitive — hit rolls decide how much damage the
-- crossing costs. Treat a single run as one sample. The failure text reports the
-- medic's health and position so repeated runs can be compared rather than just
-- counted.
--
-- PITFALL: TestHarness.TicksPerSecond is 25, but the mod runs at Timestep 60 —
-- 16.67 ticks/second. Budget in ticks and convert.
local BudgetTicks = 1200 -- ~72 real seconds
local CasualtyStartPercent = 40

local elapsed = 0
local sawMedicBelowHalf = false
local frozenAtPercent = 0

WorldLoaded = function()
	TestHarness.FocusBetween(Medic, Casualty)

	Casualty.Health = math.floor(Casualty.MaxHealth * CasualtyStartPercent / 100)
	local casualtyBaseline = Casualty.Health

	local function percent(actor)
		if actor.IsDead then
			return -1
		end

		return math.floor(actor.Health * 100 / actor.MaxHealth)
	end

	TestHarness.AssertWithin(BudgetTicks / TestHarness.TicksPerSecond, function()
		-- Killed outright: one of the two measured outcomes. The errand cost the
		-- medic his life, which is the behaviour being pinned.
		if Medic.IsDead then
			return true
		end

		elapsed = elapsed + 1

		-- Record the first moment he crosses the 50% line. From here his speed
		-- multiplier is zero: wherever he is standing is where he stays.
		if not sawMedicBelowHalf and percent(Medic) < 50 then
			sawMedicBelowHalf = true
			frozenAtPercent = percent(Medic)
		end

		-- Crippled: the other measured outcome. Below 50% his SpeedMultiplier is
		-- 0, so wherever the fire caught him is where he stays.
		if sawMedicBelowHalf then
			return true
		end

		-- The casualty dying while the medic is still healthy would mean the
		-- errand failed for some reason other than the one under test.
		if Casualty.IsDead then
			return "fail: the casualty died while the medic was still above 50% — the errand failed"
				.. " for a reason other than the crossing. Medic at " .. percent(Medic) .. "%"
		end

		-- The good outcome: he crossed covered ground, finished the man, and is
		-- still mobile. See the notice at the top of this file.
		if Casualty.Health >= Casualty.MaxHealth then
			return "fail: the medic completed the errand and stayed above 50% throughout. That is"
				.. " BETTER than the behaviour this test pins (medic dead at seed 4242, at 2% and"
				.. " frozen at seed 1017, on main @ 96f47c47). Something now protects him —"
				.. " invert this test to assert the errand succeeds"
		end

		if elapsed >= BudgetTicks then
			return "fail: inconclusive — in " .. BudgetTicks .. " ticks the medic was never hurt"
				.. " below 50% and never finished the casualty (casualty " .. percent(Casualty)
				.. "%, medic " .. percent(Medic) .. "%). The shooter may not be engaging;"
				.. " check the geometry before reading anything into this"
		end

		return false
	end, "walks-into-fire assertion did not resolve within " .. BudgetTicks .. " ticks")
end
