--[[
   Shellmap: Combined Arms
   NATO (10,000): 2 Abrams, 1 Bradley, 1 Humvee + 19 infantry
   Russia (10,000): 2 T-90, 1 BMP-2, 1 BTR + 21 infantry
   Phased engagement: armor advances first, infantry follows, flankers envelop
]]

ticks = 0
phase = 0

Tick = function()
	ticks = ticks + 1

	-- Camera pans from southwest toward the center of the battlefield
	if ticks < 600 then
		Camera.Position = Camera.Position + WVec.New(30, -20, 0)
	end

	-- Phase 1 (tick 200): Armor advances to center
	if ticks == 200 and phase < 1 then
		phase = 1

		-- NATO tanks push forward in wedge
		NatoAbrams1.AttackMove(NatoCenter.Location, 2)
		NatoAbrams2.AttackMove(NatoCenter.Location, 2)
		NatoBradley1.AttackMove(NatoCenter.Location, 2)

		-- Russia tanks push forward
		RussiaT90_1.AttackMove(RussiaCenter.Location, 2)
		RussiaT90_2.AttackMove(RussiaCenter.Location, 2)
		RussiaBMP_1.AttackMove(RussiaCenter.Location, 2)
	end

	-- Phase 2 (tick 500): Infantry follows armor
	if ticks == 500 and phase < 2 then
		phase = 2

		-- NATO infantry squads advance behind armor
		local natoInfantry = { NatoE3_1, NatoE3_2, NatoE3_3, NatoAT_1, NatoAT_2,
			NatoAR_1, NatoTL_1, NatoE2_1, NatoMedi1 }
		for _, unit in ipairs(natoInfantry) do
			if not unit.IsDead then
				unit.AttackMove(NatoCenter.Location, 3)
			end
		end

		local natoInfantry2 = { NatoE3_4, NatoE3_5, NatoE3_6, NatoAT_3, NatoAT_4,
			NatoAR_3, NatoTL_2, NatoE2_2, NatoMedi2 }
		for _, unit in ipairs(natoInfantry2) do
			if not unit.IsDead then
				unit.AttackMove(NatoCenter.Location, 3)
			end
		end

		-- Russia infantry squads advance
		local russiaInfantry = { RussiaE3_1, RussiaE3_2, RussiaE3_3, RussiaAT_1, RussiaAT_2,
			RussiaAR_1, RussiaTL_1, RussiaE2_1, RussiaMedi1, RussiaE1_1, RussiaE1_2 }
		for _, unit in ipairs(russiaInfantry) do
			if not unit.IsDead then
				unit.AttackMove(RussiaCenter.Location, 3)
			end
		end

		local russiaInfantry2 = { RussiaE3_4, RussiaE3_5, RussiaE3_6, RussiaAT_3, RussiaAT_4,
			RussiaAR_3, RussiaTL_2, RussiaE2_2, RussiaMedi2, RussiaMedi3, RussiaE1_3, RussiaE1_4 }
		for _, unit in ipairs(russiaInfantry2) do
			if not unit.IsDead then
				unit.AttackMove(RussiaCenter.Location, 3)
			end
		end
	end

	-- Phase 3 (tick 800): Flanking elements try to envelop
	if ticks == 800 and phase < 3 then
		phase = 3

		-- NATO humvee flanks north
		if not NatoHumvee1.IsDead then
			NatoHumvee1.AttackMove(NatoFlankN.Location, 1)
			NatoHumvee1.AttackMove(RussiaFlankN.Location, 1)
		end

		-- NATO AR gunner flanks south
		if not NatoAR_2.IsDead then
			NatoAR_2.AttackMove(NatoFlankS.Location, 2)
		end

		-- Russia BTR flanks south
		if not RussiaBTR_1.IsDead then
			RussiaBTR_1.AttackMove(RussiaFlankS.Location, 1)
			RussiaBTR_1.AttackMove(NatoFlankS.Location, 1)
		end

		-- Russia E2 grenadier flanks
		if not RussiaE2_3.IsDead then
			RussiaE2_3.AttackMove(RussiaFlankN.Location, 2)
		end
	end

	-- Phase 4 (tick 1200): Everything converges on center
	if ticks == 1200 and phase < 4 then
		phase = 4

		local allNato = nato.GetActorsByTypes({ "abrams", "bradley", "humvee",
			"E3.america", "AT.america", "AR.america", "TL.america", "E2.america", "MEDI.america" })
		for _, unit in ipairs(allNato) do
			if not unit.IsDead then
				unit.AttackMove(NatoRally.Location, 2)
			end
		end

		local allRussia = russia.GetActorsByTypes({ "t90", "bmp2", "btr",
			"E3.russia", "AT.russia", "AR.russia", "TL.russia", "E1.russia", "E2.russia", "MEDI.russia" })
		for _, unit in ipairs(allRussia) do
			if not unit.IsDead then
				unit.AttackMove(RussiaRally.Location, 2)
			end
		end
	end
end

WorldLoaded = function()
	nato = Player.GetPlayer("NATO")
	russia = Player.GetPlayer("Russia")

	-- Start camera at bottom-left, looking toward the battlefield
	Camera.Position = WPos.New(1024 * 15, 1024 * 40, 0)
end
