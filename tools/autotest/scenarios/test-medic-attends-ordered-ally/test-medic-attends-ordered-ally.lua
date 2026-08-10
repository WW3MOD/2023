-- AUTO TEST: a medic ordered onto an UNHURT soldier goes to him and stays.
--
-- This is the player-facing half of the medic complaint: the heal weapon can only
-- target the wounded, so clicking a healthy soldier used to produce no order at
-- all — no cursor, no move, nothing. The medic looked broken.
--
-- Both riflemen are at full health and the nearer one is already inside the
-- medic's follow distance, so nothing in the medic's own autonomy can carry him
-- to Far. Only the order can.

local DeadlineSeconds = 30

WorldLoaded = function()
	TestHarness.FocusBetween(Medic, Far)
	TestHarness.Select(Medic)

	Test.IssueAttendAlly(Medic, Far)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Medic.IsDead then
			return "fail: medic died"
		end

		local dx = Medic.Location.X - Far.Location.X
		local dy = Medic.Location.Y - Far.Location.Y

		return dx * dx + dy * dy <= 2
	end, "medic never reached the soldier he was ordered to attend within " .. DeadlineSeconds .. "s")
end
