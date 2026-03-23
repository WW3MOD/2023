--[[
   Shellmap: Glass Cannon (Quality vs Quantity)
   NATO (10,000): 3 Abrams, 1 Bradley + 6 infantry — heavy elite force
   Russia (10,000): 1 T-90, 2 BMP-2, 2 BTR + 31 infantry — swarm tactics
   NATO holds center while Russia floods from multiple directions
]]

ticks = 0
phase = 0

Tick = function()
	ticks = ticks + 1

	-- Camera pans from south-center looking at the wide battlefield
	if ticks < 600 then
		Camera.Position = Camera.Position + WVec.New(20, -25, 0)
	end

	-- Phase 1 (tick 150): NATO advances as tight armored group
	if ticks == 150 and phase < 1 then
		phase = 1

		-- Abrams advance in line abreast
		NatoAbrams1.AttackMove(NatoAdvance.Location, 2)
		NatoAbrams2.AttackMove(NatoAdvance.Location, 2)
		NatoAbrams3.AttackMove(NatoAdvance.Location, 2)
		NatoBradley1.AttackMove(NatoAdvance.Location, 2)

		-- NATO infantry follows close behind
		local natoInf = { NatoAT_1, NatoAT_2, NatoE3_1, NatoE3_2, NatoE3_3, NatoE3_4 }
		for _, unit in ipairs(natoInf) do
			if not unit.IsDead then
				unit.AttackMove(NatoAdvance.Location, 3)
			end
		end
	end

	-- Phase 2 (tick 300): Russia sends center force
	if ticks == 300 and phase < 2 then
		phase = 2

		-- T-90 and BMPs push center
		RussiaT90_1.AttackMove(RussiaAdvance.Location, 2)
		RussiaBMP_1.AttackMove(RussiaAdvance.Location, 2)
		RussiaBMP_2.AttackMove(RussiaAdvance.Location, 2)

		-- Center infantry wave
		local russiaCenterInf = { RussiaAT_1, RussiaAT_2, RussiaAT_3, RussiaAT_4, RussiaAT_5, RussiaAT_6,
			RussiaE3_3, RussiaE3_4, RussiaE3_5, RussiaE3_6, RussiaAR_2, RussiaAR_5 }
		for _, unit in ipairs(russiaCenterInf) do
			if not unit.IsDead then
				unit.AttackMove(RussiaAdvance.Location, 3)
			end
		end
	end

	-- Phase 3 (tick 600): Russia flanks from north and south
	if ticks == 600 and phase < 3 then
		phase = 3

		-- BTR north flank with infantry
		if not RussiaBTR_1.IsDead then
			RussiaBTR_1.AttackMove(RussiaFlankN.Location, 1)
			RussiaBTR_1.AttackMove(Center.Location, 1)
		end

		local russiaFlankN = { RussiaE3_9, RussiaE3_1, RussiaE3_2, RussiaE1_1, RussiaE1_2, RussiaE1_3,
			RussiaE1_9, RussiaAR_1 }
		for _, unit in ipairs(russiaFlankN) do
			if not unit.IsDead then
				unit.AttackMove(RussiaFlankN.Location, 2)
				unit.AttackMove(Center.Location, 2)
			end
		end

		-- BTR south flank with infantry
		if not RussiaBTR_2.IsDead then
			RussiaBTR_2.AttackMove(RussiaFlankS.Location, 1)
			RussiaBTR_2.AttackMove(Center.Location, 1)
		end

		local russiaFlankS = { RussiaE3_10, RussiaE3_7, RussiaE3_8, RussiaE1_4, RussiaE1_5, RussiaE1_6,
			RussiaE1_10, RussiaAR_3, RussiaAR_4 }
		for _, unit in ipairs(russiaFlankS) do
			if not unit.IsDead then
				unit.AttackMove(RussiaFlankS.Location, 2)
				unit.AttackMove(Center.Location, 2)
			end
		end
	end

	-- Phase 4 (tick 1000): Everything remaining charges the center
	if ticks == 1000 and phase < 4 then
		phase = 4

		local allNato = nato.GetActorsByTypes({ "abrams", "bradley",
			"E3.america", "AT.america" })
		for _, unit in ipairs(allNato) do
			if not unit.IsDead then
				unit.AttackMove(Center.Location, 1)
			end
		end

		local allRussia = russia.GetActorsByTypes({ "t90", "bmp2", "btr",
			"E3.russia", "AT.russia", "AR.russia", "E1.russia" })
		for _, unit in ipairs(allRussia) do
			if not unit.IsDead then
				unit.AttackMove(Center.Location, 1)
			end
		end
	end
end

WorldLoaded = function()
	nato = Player.GetPlayer("NATO")
	russia = Player.GetPlayer("Russia")

	-- Start camera at south, centered on the map
	Camera.Position = WPos.New(1024 * 30, 1024 * 42, 0)
end
