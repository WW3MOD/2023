-- AUTO TEST: an idle medic notices a casualty he cannot reach from where he
-- stands, walks to him, and treats him.
--
-- The 5-cell gap is the whole point. The Heal weapon reaches 1c0, so a medic
-- that only ever considers patients already in weapon range will stand and
-- watch. Trailing the squad is not enough either: the follow distance parks him
-- 3 cells short, still out of reach.

local DeadlineSeconds = 30
local WoundedFraction = 40

WorldLoaded = function()
	TestHarness.FocusBetween(Medic, Wounded)
	TestHarness.Select(Medic)

	-- Drain to 40%: damaged enough to be a valid Heal target and below the
	-- medic's critical threshold, but nowhere near dying on his own.
	Wounded.Health = math.floor(Wounded.MaxHealth * WoundedFraction / 100)

	local baseline = Wounded.Health

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Medic.IsDead then
			return "fail: medic died"
		end

		if Wounded.IsDead then
			return "fail: patient died"
		end

		return Wounded.Health > baseline
	end, "medic did not heal the wounded soldier within " .. DeadlineSeconds .. "s")
end
