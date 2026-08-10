-- AUTO TEST: a medic on Hunt stance trails the squad instead of standing still.
--
-- Hunt is the stance a player picks for "go be aggressive", and it is the one
-- stance in which a medic has nothing to be aggressive with: his only armament
-- targets Heal, so with the squad at full health there is no target to chase.
-- If following is gated to Defensive he stands in place and the squad walks off
-- without him.

local DeadlineSeconds = 30
local CloseEnoughCells = 4

WorldLoaded = function()
	TestHarness.FocusBetween(Medic, Squad)
	TestHarness.Select(Medic)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Medic.IsDead then
			return "fail: medic died"
		end

		local dx = Medic.Location.X - Squad.Location.X
		local dy = Medic.Location.Y - Squad.Location.Y

		return dx * dx + dy * dy <= CloseEnoughCells * CloseEnoughCells
	end, "medic on Hunt never closed to within " .. CloseEnoughCells .. " cells of the squad")
end
