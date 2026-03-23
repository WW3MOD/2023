-- Shellmap: Combined Arms
-- NATO (10,000): 2 Abrams, 1 Bradley, 1 Humvee + 19 infantry
-- Russia (10,000): 2 T-90, 1 BMP-2, 1 BTR + 21 infantry

print("combined-arms.lua: Script loaded")

ticks = 0

Tick = function()
	ticks = ticks + 1

	-- Camera pans from southwest toward center
	if ticks < 600 then
		Camera.Position = Camera.Position + WVec.New(30, -20, 0)
	end

	-- Phase 1 (tick 50): Armor pushes to center
	if ticks == 50 then
		print("combined-arms.lua: Phase 1 - Armor advance")
		NatoAbrams1.AttackMove(CPos.New(35, 20), 0)
		NatoAbrams2.AttackMove(CPos.New(35, 28), 0)
		NatoBradley1.AttackMove(CPos.New(35, 24), 0)

		RussiaT90_1.AttackMove(CPos.New(45, 20), 0)
		RussiaT90_2.AttackMove(CPos.New(45, 28), 0)
		RussiaBMP_1.AttackMove(CPos.New(45, 24), 0)
	end

	-- Phase 2 (tick 300): Infantry follows armor
	if ticks == 300 then
		print("combined-arms.lua: Phase 2 - Infantry advance")
		local natoInf = { NatoE3_1, NatoE3_2, NatoE3_3, NatoE3_4, NatoE3_5, NatoE3_6,
			NatoAT_1, NatoAT_2, NatoAT_3, NatoAT_4,
			NatoAR_1, NatoAR_2, NatoAR_3, NatoTL_1, NatoTL_2,
			NatoE2_1, NatoE2_2, NatoMedi1, NatoMedi2 }
		for _, unit in ipairs(natoInf) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(30, 24), 2)
			end
		end

		local russiaInf = { RussiaE3_1, RussiaE3_2, RussiaE3_3, RussiaE3_4, RussiaE3_5, RussiaE3_6,
			RussiaAT_1, RussiaAT_2, RussiaAT_3, RussiaAT_4,
			RussiaAR_1, RussiaAR_2, RussiaAR_3, RussiaTL_1, RussiaTL_2,
			RussiaE1_1, RussiaE1_2, RussiaE1_3, RussiaE1_4,
			RussiaE2_1, RussiaE2_2, RussiaE2_3,
			RussiaMedi1, RussiaMedi2, RussiaMedi3 }
		for _, unit in ipairs(russiaInf) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(50, 24), 2)
			end
		end
	end

	-- Phase 3 (tick 550): Flanking elements
	if ticks == 550 then
		print("combined-arms.lua: Phase 3 - Flankers")
		if not NatoHumvee1.IsDead then
			NatoHumvee1.AttackMove(CPos.New(55, 15), 0)
		end
		if not RussiaBTR_1.IsDead then
			RussiaBTR_1.AttackMove(CPos.New(25, 33), 0)
		end
	end

	-- Phase 4 (tick 800): Everyone hunts
	if ticks == 800 then
		print("combined-arms.lua: Phase 4 - Hunt")
		local allUnits = { NatoAbrams1, NatoAbrams2, NatoBradley1, NatoHumvee1,
			NatoE3_1, NatoE3_2, NatoE3_3, NatoE3_4, NatoE3_5, NatoE3_6,
			NatoAT_1, NatoAT_2, NatoAT_3, NatoAT_4,
			NatoAR_1, NatoAR_2, NatoAR_3, NatoTL_1, NatoTL_2,
			NatoE2_1, NatoE2_2, NatoMedi1, NatoMedi2,
			RussiaT90_1, RussiaT90_2, RussiaBMP_1, RussiaBTR_1,
			RussiaE3_1, RussiaE3_2, RussiaE3_3, RussiaE3_4, RussiaE3_5, RussiaE3_6,
			RussiaAT_1, RussiaAT_2, RussiaAT_3, RussiaAT_4,
			RussiaAR_1, RussiaAR_2, RussiaAR_3, RussiaTL_1, RussiaTL_2,
			RussiaE1_1, RussiaE1_2, RussiaE1_3, RussiaE1_4,
			RussiaE2_1, RussiaE2_2, RussiaE2_3,
			RussiaMedi1, RussiaMedi2, RussiaMedi3 }
		for _, unit in ipairs(allUnits) do
			if not unit.IsDead then
				unit.Stop()
				unit.Hunt()
			end
		end
	end
end

WorldLoaded = function()
	print("combined-arms.lua: WorldLoaded called")
	nato = Player.GetPlayer("NATO")
	russia = Player.GetPlayer("Russia")
	Camera.Position = WPos.New(1024 * 15, 1024 * 40, 0)
end
