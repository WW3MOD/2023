-- Shellmap: Fire Support
-- NATO (10,000): 1 Abrams, 2 Bradleys, 1 M109 SPG + 12 infantry
-- Russia (10,000): 1 T-90, 2 BMP-2, 1 Giatsint SPG + 10 infantry

print("fire-support.lua: Script loaded")

ticks = 0

Tick = function()
	ticks = ticks + 1

	-- Phase 1 (tick 50): IFV screen advances
	if ticks == 50 then
		print("fire-support.lua: Phase 1 - IFV screen")
		NatoBradley1.AttackMove(CPos.New(33, 20), 0)
		NatoBradley2.AttackMove(CPos.New(33, 28), 0)

		RussiaBMP_1.AttackMove(CPos.New(47, 20), 0)
		RussiaBMP_2.AttackMove(CPos.New(47, 28), 0)
	end

	-- Phase 2 (tick 250): MBTs commit with forward infantry
	if ticks == 250 then
		print("fire-support.lua: Phase 2 - MBTs + infantry")
		if not NatoAbrams1.IsDead then
			NatoAbrams1.AttackMove(CPos.New(35, 24), 0)
		end
		if not RussiaT90_1.IsDead then
			RussiaT90_1.AttackMove(CPos.New(45, 24), 0)
		end

		local natoFwd = { NatoE3_1, NatoE3_2, NatoTL_1, NatoAR_1 }
		for _, unit in ipairs(natoFwd) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(30, 24), 2)
			end
		end

		local russiaFwd = { RussiaE3_1, RussiaE3_2, RussiaAT_1, RussiaAT_2 }
		for _, unit in ipairs(russiaFwd) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(50, 24), 2)
			end
		end
	end

	-- Phase 3 (tick 500): AT teams push center
	if ticks == 500 then
		print("fire-support.lua: Phase 3 - AT push")
		local natoAT = { NatoAT_1, NatoAT_2, NatoAT_3, NatoAT_4,
			NatoE3_3, NatoE3_4, NatoAR_2, NatoTL_2, NatoMedi1, NatoSN_1 }
		for _, unit in ipairs(natoAT) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(40, 24), 2)
			end
		end

		local russiaAT = { RussiaAT_3, RussiaAT_4, RussiaE3_3, RussiaE3_4, RussiaMedi1 }
		for _, unit in ipairs(russiaAT) do
			if not unit.IsDead then
				unit.AttackMove(CPos.New(40, 24), 2)
			end
		end
	end

	-- Phase 4 (tick 750): Everyone hunts, artillery repositions
	if ticks == 750 then
		print("fire-support.lua: Phase 4 - Hunt")
		if not NatoM109.IsDead then
			NatoM109.Move(CPos.New(20, 24), 2)
		end
		if not RussiaGiatsint.IsDead then
			RussiaGiatsint.Move(CPos.New(60, 24), 2)
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
	print("fire-support.lua: WorldLoaded called")
	nato = Player.GetPlayer("NATO")
	russia = Player.GetPlayer("Russia")
	Camera.Position = WPos.New(1024 * 41, 1024 * 26, 0)
end
