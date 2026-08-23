-- AUTO TEST: a medic ordered onto a specific patient treats THAT patient, and is
-- not re-pointed by his own ranking at someone better off on the way.
--
-- Without the patient lock this fails in the WALK. AttendAlly queues an
-- AttackMoveActivity around a follow; that activity rescans every 10 ticks while
-- its move child runs and hands the attack layer whatever HealerAutoTarget names.
-- Bait at 55% outranks Ordered at 70% by a wide margin, so the first rescan after
-- the medic steps into range of Bait cancels the march and treats him instead.
--
-- The steal window is the march on purpose. Treating is a top-level Attack
-- activity that keeps the medic non-idle for the whole treatment and cannot be
-- preempted, so nothing is ever stolen mid-pulse — a test that only watched a
-- medic finish someone would pass with no lock at all, on the strength of that
-- incidental uninterruptibility rather than the feature.
--
-- PITFALL: TestHarness.TicksPerSecond is 25, but the mod runs at Timestep 60 --
-- 16.67 ticks/second. A "second" passed to AssertWithin is therefore 1.5 real
-- seconds. Budget in ticks and convert, so the number below means what it says.
local BudgetTicks = 1200 -- ~72 real seconds
local OrderedStartPercent = 70
local BaitStartPercent = 55

local elapsed = 0

WorldLoaded = function()
	TestHarness.FocusBetween(Medic, Ordered)

	Ordered.Health = math.floor(Ordered.MaxHealth * OrderedStartPercent / 100)
	Bait.Health = math.floor(Bait.MaxHealth * BaitStartPercent / 100)

	TestHarness.Select(Medic)
	Test.IssueAttendAlly(Medic, Ordered)

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

		if Ordered.IsDead or Bait.IsDead then
			return "fail: a patient died"
		end

		if Ordered.Health >= Ordered.MaxHealth then
			return true
		end

		elapsed = elapsed + 1
		if elapsed >= BudgetTicks then
			local stolen = percent(Bait) > BaitStartPercent
			local diverted = ""
			if stolen then
				diverted = "; the medic treated the man he was NOT ordered to — the explicit order"
					.. " was overruled by the automatic ranking during the walk"
			end

			return "fail: the ordered patient did not reach full health in " .. BudgetTicks
				.. " ticks — ordered " .. percent(Ordered) .. "% (from " .. OrderedStartPercent
				.. "), bait " .. percent(Bait) .. "% (from " .. BaitStartPercent .. ")" .. diverted
		end

		return false
	end, "ordered-patient assertion did not resolve within " .. BudgetTicks .. " ticks")
end
