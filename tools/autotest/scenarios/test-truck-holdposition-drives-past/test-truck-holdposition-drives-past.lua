-- AUTO TEST: the OTHER side of the serving halt -- HoldPosition turns it off.
--
-- The user asked for the halt to be switchable by stance ("unless we switch the stance"). A halt
-- that cannot be switched off is a different feature, and a single scenario cannot tell the two
-- apart: test-truck-halts-to-serve goes green either way. This is the control that makes the pair
-- mean something (AUTOTEST.md, "A behaviour selected by a condition needs a test on EACH SIDE").
--
-- THE VERDICT IS NOT A TIMING ONE, on purpose. "Arrived within N seconds" would be a weak
-- discriminator here: a wrongly-halting truck still arrives, roughly 250 ticks later, so the pass
-- would hang on the deadline being tuned between the two travel times. The logical discriminator is
-- used instead -- when the truck reaches the far end, is anybody still short? A truck that halted
-- left the column FULL, by definition, because that is the only thing that releases it. A truck
-- that drove past left it short, because the aura falls behind at x ~= 27 and nothing tops them up
-- afterwards. So "arrived AND somebody is still short" cannot be produced by a halting truck at any
-- speed.

local DeadlineSeconds = 70
local FullAmmo = 500
local ArrivedLine = 56 -- destination is x=58; MoveTo's stop tolerance leaves a couple of cells

WorldLoaded = function()
	local needy = { NeedyA, NeedyB, NeedyC, NeedyD }

	TestHarness.FocusBetween(Truck, NeedyB)
	TestHarness.Select(Truck)

	Test.IssueMove(Truck, CPos.New(58, 16))

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Truck.IsDead then return "fail: the truck died" end

		local short = 0
		for _, s in ipairs(needy) do
			if s.IsDead then return "fail: a needy rifleman died" end
			if s.AmmoCount("primary-ammo") < FullAmmo then short = short + 1 end
		end

		if Truck.Location.X < ArrivedLine then return false end

		if short == 0 then
			return "fail: the truck reached x=" .. Truck.Location.X
				.. " with the whole column topped up -- it stopped to serve them despite HoldPosition, "
				.. "so the stance does not switch the halt off"
		end

		return true
	end, "A HoldPosition truck never completed the move order it was given")
end
