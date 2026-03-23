--[[
   Shellmap: Fire Support
   NATO (10,000): 1 Abrams, 2 Bradleys, 1 M109 SPG + 12 infantry
   Russia (10,000): 1 T-90, 2 BMP-2, 1 Giatsint SPG + 10 infantry
   Phased: IFV screen, MBTs commit, infantry push, artillery repositions
]]

ticks = 0

Tick = function()
	ticks = ticks + 1

	-- Camera pans from bottom-left across the battlefield
	if ticks < 700 then
		Camera.Position = Camera.Position + WVec.New(28, -15, 0)
	end

	-- Phase 1 (tick 100): IFV screening force advances
	if ticks == 100 then
		NatoBradley1.Patrol({ NatoAdvance.Location }, false, 0)
		NatoBradley2.Patrol({ NatoAdvance.Location }, false, 0)

		RussiaBMP_1.Patrol({ RussiaAdvance.Location }, false, 0)
		RussiaBMP_2.Patrol({ RussiaAdvance.Location }, false, 0)
	end

	-- Phase 2 (tick 300): MBTs commit with forward infantry
	if ticks == 300 then
		if not NatoAbrams1.IsDead then
			NatoAbrams1.Patrol({ NatoAdvance.Location, Center.Location }, false, 0)
		end
		if not RussiaT90_1.IsDead then
			RussiaT90_1.Patrol({ RussiaAdvance.Location, Center.Location }, false, 0)
		end

		local natoFwd = { NatoE3_1, NatoE3_2, NatoTL_1, NatoAR_1 }
		for _, unit in ipairs(natoFwd) do
			if not unit.IsDead then
				unit.Patrol({ NatoAdvance.Location }, false, 0)
			end
		end

		local russiaFwd = { RussiaE3_1, RussiaE3_2, RussiaAT_1, RussiaAT_2 }
		for _, unit in ipairs(russiaFwd) do
			if not unit.IsDead then
				unit.Patrol({ RussiaAdvance.Location }, false, 0)
			end
		end
	end

	-- Phase 3 (tick 550): AT teams and remaining infantry push to center
	if ticks == 550 then
		local natoAT = { NatoAT_1, NatoAT_2, NatoAT_3, NatoAT_4,
			NatoE3_3, NatoE3_4, NatoAR_2, NatoTL_2, NatoMedi1, NatoSN_1 }
		for _, unit in ipairs(natoAT) do
			if not unit.IsDead then
				unit.Patrol({ Center.Location }, false, 0)
			end
		end

		local russiaAT = { RussiaAT_3, RussiaAT_4, RussiaE3_3, RussiaE3_4, RussiaMedi1 }
		for _, unit in ipairs(russiaAT) do
			if not unit.IsDead then
				unit.Patrol({ Center.Location }, false, 0)
			end
		end
	end

	-- Phase 4 (tick 800): Everyone hunts, artillery repositions
	if ticks == 800 then
		if not NatoM109.IsDead then
			NatoM109.Move(NatoAdvance.Location, 3)
		end
		if not RussiaGiatsint.IsDead then
			RussiaGiatsint.Move(RussiaAdvance.Location, 3)
		end

		local allUnits = { NatoAbrams1, NatoBradley1, NatoBradley2,
			NatoE3_1, NatoE3_2, NatoE3_3, NatoE3_4,
			NatoAT_1, NatoAT_2, NatoAT_3, NatoAT_4,
			NatoAR_1, NatoAR_2, NatoTL_1, NatoTL_2, NatoSN_1, NatoMedi1,
			RussiaT90_1, RussiaBMP_1, RussiaBMP_2,
			RussiaE3_1, RussiaE3_2, RussiaE3_3, RussiaE3_4,
			RussiaAT_1, RussiaAT_2, RussiaAT_3, RussiaAT_4, RussiaMedi1 }
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
	Camera.Position = WPos.New(1024 * 12, 1024 * 40, 0)
end
