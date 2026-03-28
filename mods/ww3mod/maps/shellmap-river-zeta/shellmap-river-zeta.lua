-- Shellmap: River Zeta Frontline
-- NATO vs Russia — full combined arms across the river
-- Tactical deployment: armor forward, infantry in fire teams, artillery echeloned
-- Both sides cautiously advance, probing for weaknesses before committing

ticks = 0

Tick = function()
	ticks = ticks + 1

	-- Phase 1 (tick 50): Armor vanguard pushes toward river, maintaining spread
	if ticks == 50 then
		NatoAbrams1.AttackMove(CPos.New(36, 28), 0)
		NatoAbrams2.AttackMove(CPos.New(34, 44), 0)
		NatoBradley1.AttackMove(CPos.New(38, 22), 0)
		NatoBradley2.AttackMove(CPos.New(37, 36), 0)
		NatoHumvee1.AttackMove(CPos.New(36, 14), 0)
		NatoHumvee2.AttackMove(CPos.New(35, 52), 0)
		NatoM113.AttackMove(CPos.New(33, 48), 0)
		NatoShorad.AttackMove(CPos.New(28, 46), 0)

		RussiaT90_1.AttackMove(CPos.New(62, 30), 0)
		RussiaT90_2.AttackMove(CPos.New(64, 42), 0)
		RussiaBMP_1.AttackMove(CPos.New(60, 24), 0)
		RussiaBMP_2.AttackMove(CPos.New(61, 36), 0)
		RussiaBTR_1.AttackMove(CPos.New(62, 16), 0)
		RussiaBTR_2.AttackMove(CPos.New(63, 52), 0)
		RussiaTunguska.AttackMove(CPos.New(68, 48), 0)
	end

	-- Phase 2 (tick 250): Infantry advances in fire teams, not as a blob
	if ticks == 250 then
		-- NATO north fire team: pushes toward river crossing
		local natoNorth = { NatoE3_1, NatoE3_2, NatoAR_1, NatoTL_1, NatoE1_1, NatoE2_1 }
		for _, unit in ipairs(natoNorth) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(36, 24), 2)
			end
		end

		-- NATO center fire team: advances behind Abrams
		local natoCenter = { NatoE3_3, NatoAR_2, NatoTL_2, NatoE1_2, NatoSN, NatoDR }
		for _, unit in ipairs(natoCenter) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(37, 36), 2)
			end
		end

		-- NATO south fire team: flanking approach
		local natoSouth = { NatoE3_4, NatoAR_3, NatoAT_2, NatoE2_2, NatoE1_3, NatoE4 }
		for _, unit in ipairs(natoSouth) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(34, 48), 2)
			end
		end

		-- NATO support stays back: AT, medics, engineer, mortar
		local natoSupport = { NatoAT_1, NatoMT, NatoMedi1, NatoMedi2, NatoE6, NatoTecn, NatoAA }
		for _, unit in ipairs(natoSupport) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(30, 35), 2)
			end
		end

		-- SF probes forward alone
		if not NatoSF.IsDead then
			NatoSF.AttackMove(CPos.New(40, 44), 0)
		end

		-- Russia north fire team
		local russiaNorth = { RussiaE3_1, RussiaE3_2, RussiaAR_1, RussiaTL_1, RussiaE1_1, RussiaE2_1 }
		for _, unit in ipairs(russiaNorth) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(62, 24), 2)
			end
		end

		-- Russia center fire team
		local russiaCenter = { RussiaE3_3, RussiaAR_2, RussiaTL_2, RussiaE1_2, RussiaSN, RussiaDR, RussiaShok }
		for _, unit in ipairs(russiaCenter) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(61, 36), 2)
			end
		end

		-- Russia south fire team
		local russiaSouth = { RussiaE3_4, RussiaAR_3, RussiaAT_2, RussiaE2_2, RussiaE1_3, RussiaE4 }
		for _, unit in ipairs(russiaSouth) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(64, 48), 2)
			end
		end

		-- Russia support stays back
		local russiaSupport = { RussiaAT_1, RussiaMT, RussiaMedi1, RussiaMedi2, RussiaE6, RussiaTecn, RussiaAA }
		for _, unit in ipairs(russiaSupport) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(68, 35), 2)
			end
		end

		-- SF probes forward
		if not RussiaSF.IsDead then
			RussiaSF.AttackMove(CPos.New(58, 44), 0)
		end
	end

	-- Phase 3 (tick 450): Helicopters fly in — attack helis sweep, transports reposition
	if ticks == 450 then
		if not NatoHeli.IsDead then
			NatoHeli.AttackMove(CPos.New(44, 34), 0)
		end
		if not NatoLittlebird.IsDead then
			NatoLittlebird.AttackMove(CPos.New(42, 22), 0)
		end
		if not NatoTran.IsDead then
			NatoTran.Move(CPos.New(28, 38), 0)
		end

		if not RussiaMi28.IsDead then
			RussiaMi28.AttackMove(CPos.New(54, 34), 0)
		end
		if not RussiaHind.IsDead then
			RussiaHind.AttackMove(CPos.New(56, 22), 0)
		end
		if not RussiaHalo.IsDead then
			RussiaHalo.Move(CPos.New(70, 38), 0)
		end
	end

	-- Phase 4 (tick 700): Artillery repositions forward, everyone hunts
	if ticks == 700 then
		if not NatoM109.IsDead then
			NatoM109.Move(CPos.New(18, 28), 0)
		end
		if not NatoM270.IsDead then
			NatoM270.Move(CPos.New(20, 38), 0)
		end
		if not NatoHimars.IsDead then
			NatoHimars.Move(CPos.New(18, 48), 0)
		end

		if not RussiaGiatsint.IsDead then
			RussiaGiatsint.Move(CPos.New(80, 28), 0)
		end
		if not RussiaGrad.IsDead then
			RussiaGrad.Move(CPos.New(78, 38), 0)
		end
		if not RussiaTos.IsDead then
			RussiaTos.Move(CPos.New(80, 48), 0)
		end

		-- All combat units go full hunt
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
	Camera.Position = WPos.New(1024 * 49, 1024 * 34, 0)
end
