-- AUTO TEST: a supply truck must stop for units that need resupplying rather than driving past them.
--
-- The user's words: "they should also automatically stop when any unit nearby is in need of
-- resupplying so they don't just drive past them ... When there are no longer any unit nearby in
-- need of supplies they will continue moving."
--
-- The truck is given an ordinary Move order that takes it straight through a column of four short
-- riflemen. Nothing about the resupply itself is broken today -- the push aura is positional, so a
-- truck driving through DOES hand out batches on the way. What it cannot do is finish: one target
-- per RearmDelay (6 ticks) and 36 batches of demand against roughly 130 ticks of aura overlap, so
-- it leaves half the column short and keeps going. That partial service is exactly why the verdict
-- has to be "all four are FULL" and not "somebody got some ammo".
--
-- The x >= DrovePastLine term is the discriminator, not a timeout guard. A truck that halts turns
-- the whole 36 batches around at x ~= 18 and has satisfied the assertion long before it resumes; a
-- truck that does not halt must pass x=34 on its way to the destination at x=58. So reaching that
-- line with anyone still short is the reported bug happening, and is failed immediately rather than
-- waited out.
--
-- There is no enemy on this map. The truck is unarmed and the riflemen are only here to be thirsty;
-- adding a threat would drag the danger-mode delivery doctrine (drop-and-leave) into a measurement
-- that is about the quiet-front case.

local DeadlineSeconds = 55
local FullAmmo = 500
local DrovePastLine = 34 -- clear of the column at x=22, well short of the destination at x=58

local Needy = { }

WorldLoaded = function()
	Needy = { NeedyA, NeedyB, NeedyC, NeedyD }

	TestHarness.FocusBetween(Truck, NeedyB)
	TestHarness.Select(Truck)

	-- A real Move order through the order layer, which is what "they have orders" means in the report.
	Test.IssueMove(Truck, CPos.New(58, 16))

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Truck.IsDead then return "fail: the truck died" end

		local short = 0
		local lowest = FullAmmo
		for _, s in ipairs(Needy) do
			if s.IsDead then return "fail: a needy rifleman died" end

			local ammo = s.AmmoCount("primary-ammo")
			if ammo < FullAmmo then
				short = short + 1
				if ammo < lowest then lowest = ammo end
			end
		end

		if short == 0 then return true end

		local x = Truck.Location.X
		if x >= DrovePastLine then
			return "fail: the truck drove past -- it is at x=" .. x .. " with " .. short
				.. " of 4 riflemen still short (lowest " .. lowest .. "/" .. FullAmmo .. " rounds)"
		end

		return false
	end, "The truck never finished resupplying the column it was ordered to drive through")
end
