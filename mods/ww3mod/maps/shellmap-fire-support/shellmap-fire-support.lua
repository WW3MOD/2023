--[[
   Shellmap: Fire Support
   NATO (10,000): 1 Abrams, 2 Bradleys, 1 M109 SPG + 12 infantry
   Russia (10,000): 1 T-90, 2 BMP-2, 1 Giatsint SPG + 10 infantry
   Phased: Screening force advances through tree line, artillery stays back
   Sniper provides overwatch, AT infantry holds center
]]

ticks = 0
phase = 0

Tick = function()
	ticks = ticks + 1

	-- Camera pans from bottom-left across the battlefield
	if ticks < 700 then
		Camera.Position = Camera.Position + WVec.New(28, -15, 0)
	end

	-- Phase 1 (tick 150): Screening force advances to tree line
	if ticks == 150 and phase < 1 then
		phase = 1

		-- NATO Bradleys advance as screen
		NatoBradley1.AttackMove(NatoAdvance.Location, 2)
		NatoBradley2.AttackMove(NatoAdvance.Location, 2)

		-- Russia BMPs advance as screen
		RussiaBMP_1.AttackMove(RussiaAdvance.Location, 2)
		RussiaBMP_2.AttackMove(RussiaAdvance.Location, 2)
	end

	-- Phase 2 (tick 400): Main battle tanks commit
	if ticks == 400 and phase < 2 then
		phase = 2

		if not NatoAbrams1.IsDead then
			NatoAbrams1.AttackMove(NatoAdvance.Location, 2)
		end

		if not RussiaT90_1.IsDead then
			RussiaT90_1.AttackMove(RussiaAdvance.Location, 2)
		end

		-- Infantry begins advancing behind armor
		local natoScreen = { NatoE3_1, NatoE3_2, NatoTL_1, NatoAR_1 }
		for _, unit in ipairs(natoScreen) do
			if not unit.IsDead then
				unit.AttackMove(NatoAdvance.Location, 3)
			end
		end

		local russiaScreen = { RussiaE3_1, RussiaE3_2, RussiaAT_1, RussiaAT_2 }
		for _, unit in ipairs(russiaScreen) do
			if not unit.IsDead then
				unit.AttackMove(RussiaAdvance.Location, 3)
			end
		end
	end

	-- Phase 3 (tick 700): AT teams and remaining infantry push
	if ticks == 700 and phase < 3 then
		phase = 3

		local natoAT = { NatoAT_1, NatoAT_2, NatoAT_3, NatoAT_4, NatoE3_3, NatoE3_4,
			NatoAR_2, NatoTL_2, NatoMedi1 }
		for _, unit in ipairs(natoAT) do
			if not unit.IsDead then
				unit.AttackMove(Center.Location, 2)
			end
		end

		local russiaAT = { RussiaAT_3, RussiaAT_4, RussiaE3_3, RussiaE3_4, RussiaMedi1 }
		for _, unit in ipairs(russiaAT) do
			if not unit.IsDead then
				unit.AttackMove(Center.Location, 2)
			end
		end

		-- Sniper holds position (already in overwatch stance from rules)
	end

	-- Phase 4 (tick 1000): Artillery repositions forward, everything pushes
	if ticks == 1000 and phase < 4 then
		phase = 4

		-- M109 moves up slightly (still behind the line)
		if not NatoM109.IsDead then
			NatoM109.Move(NatoAdvance.Location, 3)
		end

		-- Giatsint repositions
		if not RussiaGiatsint.IsDead then
			RussiaGiatsint.Move(RussiaAdvance.Location, 3)
		end

		-- All remaining units converge
		local allNato = nato.GetActorsByTypes({ "abrams", "bradley", "m109",
			"E3.america", "AT.america", "AR.america", "TL.america", "SN.america", "MEDI.america" })
		for _, unit in ipairs(allNato) do
			if not unit.IsDead then
				unit.AttackMove(Center.Location, 2)
			end
		end

		local allRussia = russia.GetActorsByTypes({ "t90", "bmp2", "giatsint",
			"E3.russia", "AT.russia", "MEDI.russia" })
		for _, unit in ipairs(allRussia) do
			if not unit.IsDead then
				unit.AttackMove(Center.Location, 2)
			end
		end
	end
end

WorldLoaded = function()
	nato = Player.GetPlayer("NATO")
	russia = Player.GetPlayer("Russia")

	-- Start camera at bottom-left
	Camera.Position = WPos.New(1024 * 12, 1024 * 40, 0)
end
