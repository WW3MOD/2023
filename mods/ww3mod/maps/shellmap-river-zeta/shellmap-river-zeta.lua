-- Shellmap: River Zeta Frontline
-- NATO vs Russia — full combined arms across the river
-- NATO (~35,700): 2 Abrams, 2 Bradley, 2 Humvee, M113, Stryker SHORAD, M109, M270, HIMARS, TRUK + 26 infantry + Chinook, Littlebird, Apache
-- Russia (~36,800): 2 T-90, 2 BMP-2, 2 BTR, Tunguska, Giatsint, Grad, TOS, Iskander, TRUK + 27 infantry + Halo, Hind, Mi-28

ticks = 0

Tick = function()
	ticks = ticks + 1

	-- Phase 1 (tick 50): Armor vanguard pushes toward river
	if ticks == 50 then
		NatoAbrams1.AttackMove(CPos.New(38, 28), 0)
		NatoAbrams2.AttackMove(CPos.New(38, 38), 0)
		NatoBradley1.AttackMove(CPos.New(38, 32), 0)
		NatoBradley2.AttackMove(CPos.New(38, 42), 0)
		NatoHumvee1.AttackMove(CPos.New(36, 18), 0)
		NatoHumvee2.AttackMove(CPos.New(36, 56), 0)
		NatoM113.AttackMove(CPos.New(36, 48), 0)
		NatoShorad.AttackMove(CPos.New(34, 54), 0)

		RussiaT90_1.AttackMove(CPos.New(60, 28), 0)
		RussiaT90_2.AttackMove(CPos.New(60, 38), 0)
		RussiaBMP_1.AttackMove(CPos.New(60, 32), 0)
		RussiaBMP_2.AttackMove(CPos.New(60, 42), 0)
		RussiaBTR_1.AttackMove(CPos.New(62, 18), 0)
		RussiaBTR_2.AttackMove(CPos.New(62, 56), 0)
		RussiaTunguska.AttackMove(CPos.New(64, 48), 0)
	end

	-- Phase 2 (tick 250): Infantry advances behind armor
	if ticks == 250 then
		local natoInf = { NatoE3_1, NatoE3_2, NatoE3_3, NatoE3_4,
			NatoAR_1, NatoAR_2, NatoAR_3,
			NatoAT_1, NatoAT_2, NatoTL_1, NatoTL_2,
			NatoE2_1, NatoE2_2, NatoE1_1, NatoE1_2, NatoE1_3,
			NatoMT, NatoAA, NatoSN, NatoSF, NatoE4, NatoE6,
			NatoTecn, NatoDR, NatoMedi1, NatoMedi2 }
		for _, unit in ipairs(natoInf) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(35, 38), 2)
			end
		end

		local russiaInf = { RussiaE3_1, RussiaE3_2, RussiaE3_3, RussiaE3_4,
			RussiaAR_1, RussiaAR_2, RussiaAR_3,
			RussiaAT_1, RussiaAT_2, RussiaTL_1, RussiaTL_2,
			RussiaE2_1, RussiaE2_2, RussiaE1_1, RussiaE1_2, RussiaE1_3,
			RussiaMT, RussiaAA, RussiaSN, RussiaSF, RussiaE4, RussiaE6,
			RussiaTecn, RussiaDR, RussiaShok, RussiaMedi1, RussiaMedi2 }
		for _, unit in ipairs(russiaInf) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(63, 38), 2)
			end
		end
	end

	-- Phase 3 (tick 450): Helicopters fly into the fray
	if ticks == 450 then
		if not NatoHeli.IsDead then
			NatoHeli.AttackMove(CPos.New(42, 35), 0)
		end
		if not NatoLittlebird.IsDead then
			NatoLittlebird.AttackMove(CPos.New(40, 25), 0)
		end
		if not NatoTran.IsDead then
			NatoTran.Move(CPos.New(30, 40), 0)
		end

		if not RussiaMi28.IsDead then
			RussiaMi28.AttackMove(CPos.New(56, 35), 0)
		end
		if not RussiaHind.IsDead then
			RussiaHind.AttackMove(CPos.New(58, 25), 0)
		end
		if not RussiaHalo.IsDead then
			RussiaHalo.Move(CPos.New(68, 40), 0)
		end
	end

	-- Phase 4 (tick 700): Artillery repositions, everyone hunts
	if ticks == 700 then
		if not NatoM109.IsDead then
			NatoM109.Move(CPos.New(20, 30), 0)
		end
		if not NatoM270.IsDead then
			NatoM270.Move(CPos.New(20, 36), 0)
		end
		if not NatoHimars.IsDead then
			NatoHimars.Move(CPos.New(20, 42), 0)
		end

		if not RussiaGiatsint.IsDead then
			RussiaGiatsint.Move(CPos.New(78, 30), 0)
		end
		if not RussiaGrad.IsDead then
			RussiaGrad.Move(CPos.New(78, 36), 0)
		end
		if not RussiaTos.IsDead then
			RussiaTos.Move(CPos.New(78, 42), 0)
		end

		local allCombat = { NatoAbrams1, NatoAbrams2, NatoBradley1, NatoBradley2,
			NatoHumvee1, NatoHumvee2, NatoM113, NatoShorad,
			NatoE3_1, NatoE3_2, NatoE3_3, NatoE3_4,
			NatoAR_1, NatoAR_2, NatoAR_3,
			NatoAT_1, NatoAT_2, NatoTL_1, NatoTL_2,
			NatoE2_1, NatoE2_2, NatoE1_1, NatoE1_2, NatoE1_3,
			NatoMT, NatoAA, NatoSN, NatoSF, NatoE4, NatoE6,
			NatoTecn, NatoDR, NatoMedi1, NatoMedi2,
			NatoHeli, NatoLittlebird,
			RussiaT90_1, RussiaT90_2, RussiaBMP_1, RussiaBMP_2,
			RussiaBTR_1, RussiaBTR_2, RussiaTunguska,
			RussiaE3_1, RussiaE3_2, RussiaE3_3, RussiaE3_4,
			RussiaAR_1, RussiaAR_2, RussiaAR_3,
			RussiaAT_1, RussiaAT_2, RussiaTL_1, RussiaTL_2,
			RussiaE2_1, RussiaE2_2, RussiaE1_1, RussiaE1_2, RussiaE1_3,
			RussiaMT, RussiaAA, RussiaSN, RussiaSF, RussiaE4, RussiaE6,
			RussiaTecn, RussiaDR, RussiaShok, RussiaMedi1, RussiaMedi2,
			RussiaMi28, RussiaHind }
		for _, unit in ipairs(allCombat) do
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
	Camera.Position = WPos.New(1024 * 49, 1024 * 38, 0)
end
