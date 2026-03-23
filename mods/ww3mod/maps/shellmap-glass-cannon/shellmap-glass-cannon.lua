--[[
   Shellmap: Glass Cannon (Quality vs Quantity)
   NATO (10,000): 3 Abrams, 1 Bradley + 6 infantry (10 units)
   Russia (10,000): 1 T-90, 2 BMP-2, 2 BTR + 31 infantry (36 units)
   NATO holds center while Russia floods from 3 directions
]]

ticks = 0

Tick = function()
	ticks = ticks + 1

	-- Camera pans from south toward center
	if ticks < 600 then
		Camera.Position = Camera.Position + WVec.New(20, -25, 0)
	end

	-- Phase 1 (tick 100): NATO armor advances as tight group
	if ticks == 100 then
		NatoAbrams1.Patrol({ NatoAdvance.Location }, false, 0)
		NatoAbrams2.Patrol({ NatoAdvance.Location }, false, 0)
		NatoAbrams3.Patrol({ NatoAdvance.Location }, false, 0)
		NatoBradley1.Patrol({ NatoAdvance.Location }, false, 0)

		local natoInf = { NatoAT_1, NatoAT_2, NatoE3_1, NatoE3_2, NatoE3_3, NatoE3_4 }
		for _, unit in ipairs(natoInf) do
			if not unit.IsDead then
				unit.Patrol({ NatoAdvance.Location }, false, 0)
			end
		end
	end

	-- Phase 2 (tick 250): Russia center force pushes
	if ticks == 250 then
		RussiaT90_1.Patrol({ RussiaAdvance.Location, Center.Location }, false, 0)
		RussiaBMP_1.Patrol({ RussiaAdvance.Location }, false, 0)
		RussiaBMP_2.Patrol({ RussiaAdvance.Location }, false, 0)

		local russiaCtr = { RussiaAT_1, RussiaAT_2, RussiaAT_3, RussiaAT_4, RussiaAT_5, RussiaAT_6,
			RussiaE3_3, RussiaE3_4, RussiaE3_5, RussiaE3_6, RussiaAR_2, RussiaAR_5 }
		for _, unit in ipairs(russiaCtr) do
			if not unit.IsDead then
				unit.Patrol({ RussiaAdvance.Location }, false, 0)
			end
		end
	end

	-- Phase 3 (tick 500): Russia flanks north and south
	if ticks == 500 then
		-- North flank
		if not RussiaBTR_1.IsDead then
			RussiaBTR_1.Patrol({ RussiaFlankN.Location, Center.Location }, false, 0)
		end
		local flankN = { RussiaE3_9, RussiaE3_1, RussiaE3_2,
			RussiaE1_1, RussiaE1_2, RussiaE1_3, RussiaE1_9, RussiaAR_1 }
		for _, unit in ipairs(flankN) do
			if not unit.IsDead then
				unit.Patrol({ RussiaFlankN.Location, Center.Location }, false, 0)
			end
		end

		-- South flank
		if not RussiaBTR_2.IsDead then
			RussiaBTR_2.Patrol({ RussiaFlankS.Location, Center.Location }, false, 0)
		end
		local flankS = { RussiaE3_10, RussiaE3_7, RussiaE3_8,
			RussiaE1_4, RussiaE1_5, RussiaE1_6, RussiaE1_10, RussiaAR_3, RussiaAR_4 }
		for _, unit in ipairs(flankS) do
			if not unit.IsDead then
				unit.Patrol({ RussiaFlankS.Location, Center.Location }, false, 0)
			end
		end
	end

	-- Phase 4 (tick 900): Everyone hunts
	if ticks == 900 then
		local allUnits = { NatoAbrams1, NatoAbrams2, NatoAbrams3, NatoBradley1,
			NatoAT_1, NatoAT_2, NatoE3_1, NatoE3_2, NatoE3_3, NatoE3_4,
			RussiaT90_1, RussiaBMP_1, RussiaBMP_2, RussiaBTR_1, RussiaBTR_2,
			RussiaAT_1, RussiaAT_2, RussiaAT_3, RussiaAT_4, RussiaAT_5, RussiaAT_6,
			RussiaE3_1, RussiaE3_2, RussiaE3_3, RussiaE3_4, RussiaE3_5, RussiaE3_6,
			RussiaE3_7, RussiaE3_8, RussiaE3_9, RussiaE3_10,
			RussiaE1_1, RussiaE1_2, RussiaE1_3, RussiaE1_4, RussiaE1_5,
			RussiaE1_6, RussiaE1_7, RussiaE1_8, RussiaE1_9, RussiaE1_10,
			RussiaAR_1, RussiaAR_2, RussiaAR_3, RussiaAR_4, RussiaAR_5 }
		for _, unit in ipairs(allUnits) do
			if not unit.IsDead then
				unit.Stop()
				unit.Hunt()
			end
		end
	end
end

WorldLoaded = function()
	nato = Player.GetPlayer("NATO")
	russia = Player.GetPlayer("Russia")
	Camera.Position = WPos.New(1024 * 30, 1024 * 42, 0)
end
