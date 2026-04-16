-- Shellmap: Open Field
-- NATO vs Russia — full combined arms on open terrain with tree clusters
-- NATO (~35,700): 2 Abrams, 2 Bradley, 2 Humvee, M113, Stryker SHORAD, M109, M270, HIMARS, TRUK + 26 infantry + Chinook, Littlebird, Apache
-- Russia (~36,800): 2 T-90, 2 BMP-2, 2 BTR, Tunguska, Giatsint, Grad, TOS, Iskander, TRUK + 27 infantry + Halo, Hind, Mi-28

ticks = 0

-- Safe actor check: returns true if the actor exists and is alive
local function Alive(actor)
	return actor ~= nil and not actor.IsDead
end

Tick = function()
	ticks = ticks + 1

	-- Phase 1 (tick 50): Armor spearhead advances
	if ticks == 50 then
		if Alive(NatoAbrams1) then NatoAbrams1.AttackMove(CPos.New(35, 22), 0) end
		if Alive(NatoAbrams2) then NatoAbrams2.AttackMove(CPos.New(35, 30), 0) end
		if Alive(NatoBradley1) then NatoBradley1.AttackMove(CPos.New(35, 26), 0) end
		if Alive(NatoBradley2) then NatoBradley2.AttackMove(CPos.New(35, 34), 0) end
		if Alive(NatoHumvee1) then NatoHumvee1.AttackMove(CPos.New(33, 14), 0) end
		if Alive(NatoHumvee2) then NatoHumvee2.AttackMove(CPos.New(33, 46), 0) end
		if Alive(NatoM113) then NatoM113.AttackMove(CPos.New(33, 38), 0) end
		if Alive(NatoShorad) then NatoShorad.AttackMove(CPos.New(30, 48), 0) end

		if Alive(RussiaT90_1) then RussiaT90_1.AttackMove(CPos.New(55, 22), 0) end
		if Alive(RussiaT90_2) then RussiaT90_2.AttackMove(CPos.New(55, 30), 0) end
		if Alive(RussiaBMP_1) then RussiaBMP_1.AttackMove(CPos.New(55, 26), 0) end
		if Alive(RussiaBMP_2) then RussiaBMP_2.AttackMove(CPos.New(55, 34), 0) end
		if Alive(RussiaBTR_1) then RussiaBTR_1.AttackMove(CPos.New(57, 14), 0) end
		if Alive(RussiaBTR_2) then RussiaBTR_2.AttackMove(CPos.New(57, 46), 0) end
		if Alive(RussiaTunguska) then RussiaTunguska.AttackMove(CPos.New(58, 40), 0) end
	end

	-- Phase 2 (tick 250): Infantry push behind armor
	if ticks == 250 then
		local natoInf = { NatoE3_1, NatoE3_2, NatoE3_3, NatoE3_4,
			NatoAR_1, NatoAR_2, NatoAR_3,
			NatoAT_1, NatoAT_2, NatoTL_1, NatoTL_2,
			NatoE2_1, NatoE2_2, NatoE1_1, NatoE1_2, NatoE1_3,
			NatoMT, NatoAA, NatoSN, NatoSF, NatoE4, NatoE6,
			NatoTecn, NatoDR, NatoMedi1, NatoMedi2 }
		for _, unit in ipairs(natoInf) do
			if Alive(unit) then
				unit.AttackMove(CPos.New(32, 30), 2)
			end
		end

		local russiaInf = { RussiaE3_1, RussiaE3_2, RussiaE3_3, RussiaE3_4,
			RussiaAR_1, RussiaAR_2, RussiaAR_3,
			RussiaAT_1, RussiaAT_2, RussiaTL_1, RussiaTL_2,
			RussiaE2_1, RussiaE2_2, RussiaE1_1, RussiaE1_2, RussiaE1_3,
			RussiaMT, RussiaAA, RussiaSN, RussiaSF, RussiaE4, RussiaE6,
			RussiaTecn, RussiaDR, RussiaShok, RussiaMedi1, RussiaMedi2 }
		for _, unit in ipairs(russiaInf) do
			if Alive(unit) then
				unit.AttackMove(CPos.New(58, 30), 2)
			end
		end
	end

	-- Phase 3 (tick 450): Helicopters join the battle
	if ticks == 450 then
		if Alive(NatoHeli) then NatoHeli.AttackMove(CPos.New(40, 28), 0) end
		if Alive(NatoLittlebird) then NatoLittlebird.AttackMove(CPos.New(38, 20), 0) end
		if Alive(NatoTran) then NatoTran.Move(CPos.New(25, 32), 0) end

		if Alive(RussiaMi28) then RussiaMi28.AttackMove(CPos.New(50, 28), 0) end
		if Alive(RussiaHind) then RussiaHind.AttackMove(CPos.New(52, 20), 0) end
		if Alive(RussiaHalo) then RussiaHalo.Move(CPos.New(65, 32), 0) end
	end

	-- Phase 4 (tick 700): Full engagement, all units hunt
	if ticks == 700 then
		if Alive(NatoM109) then NatoM109.Move(CPos.New(16, 24), 0) end
		if Alive(NatoM270) then NatoM270.Move(CPos.New(16, 30), 0) end
		if Alive(NatoHimars) then NatoHimars.Move(CPos.New(16, 36), 0) end

		if Alive(RussiaGiatsint) then RussiaGiatsint.Move(CPos.New(74, 24), 0) end
		if Alive(RussiaGrad) then RussiaGrad.Move(CPos.New(74, 30), 0) end
		if Alive(RussiaTos) then RussiaTos.Move(CPos.New(74, 36), 0) end

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
			if Alive(unit) then
				unit.Stop()
				unit.Hunt()
			end
		end
	end
end

WorldLoaded = function()
	nato = Player.GetPlayer("NATO")
	russia = Player.GetPlayer("Russia")
	Camera.Position = WPos.New(1024 * 45, 1024 * 30, 0)
end
