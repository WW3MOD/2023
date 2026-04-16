-- River Zeta — Frontline Scenario
-- Handles both shellmap (main menu background) and gameplay modes.
--
-- SHELLMAP: Choreographed combined-arms battle across the river.
--   NATO/Russia push in phases: armor → infantry → helicopters → artillery → full hunt.
--
-- GAMEPLAY: NATO vs Russia across the river. Garrison forces hold the front lines.
--   After 3 minutes, garrison units transfer to the first player on each team.
--   Enemy reinforcement waves arrive periodically from the map edges.
--   Teams are determined by lobby: Team 1 = west/NATO side, Team 2 = east/Russia side
--
-- Map geometry: 98x82, river roughly at x=48-50
-- NATO edge: x=0 (west), Russia edge: x=97 (east)

local isShellmap = false

-- Safe actor check: returns true if the actor exists and is alive
local function Alive(actor)
	return actor ~= nil and not actor.IsDead
end

-- ============================================================
-- SHELLMAP MODE
-- ============================================================

local shellmapTicks = 0

local function ShellmapInit()
	Camera.Position = WPos.New(1024 * 49, 1024 * 34, 0)
end

local function ShellmapTick()
	shellmapTicks = shellmapTicks + 1

	-- Phase 1 (tick 50): Armor vanguard pushes toward river, maintaining spread
	if shellmapTicks == 50 then
		if Alive(NatoAbrams1) then NatoAbrams1.AttackMove(CPos.New(34, 28), 0) end
		if Alive(NatoAbrams2) then NatoAbrams2.AttackMove(CPos.New(33, 44), 0) end
		if Alive(NatoBradley1) then NatoBradley1.AttackMove(CPos.New(34, 22), 0) end
		if Alive(NatoBradley2) then NatoBradley2.AttackMove(CPos.New(34, 36), 0) end
		if Alive(NatoHumvee1) then NatoHumvee1.AttackMove(CPos.New(34, 14), 0) end
		if Alive(NatoHumvee2) then NatoHumvee2.AttackMove(CPos.New(34, 52), 0) end
		if Alive(NatoM113) then NatoM113.AttackMove(CPos.New(33, 48), 0) end
		if Alive(NatoShorad) then NatoShorad.AttackMove(CPos.New(28, 46), 0) end

		if Alive(RussiaT90_1) then RussiaT90_1.AttackMove(CPos.New(62, 30), 0) end
		if Alive(RussiaT90_2) then RussiaT90_2.AttackMove(CPos.New(64, 42), 0) end
		if Alive(RussiaBMP_1) then RussiaBMP_1.AttackMove(CPos.New(60, 24), 0) end
		if Alive(RussiaBMP_2) then RussiaBMP_2.AttackMove(CPos.New(61, 36), 0) end
		if Alive(RussiaBTR_1) then RussiaBTR_1.AttackMove(CPos.New(62, 16), 0) end
		if Alive(RussiaBTR_2) then RussiaBTR_2.AttackMove(CPos.New(63, 52), 0) end
		if Alive(RussiaTunguska) then RussiaTunguska.AttackMove(CPos.New(68, 48), 0) end
	end

	-- Phase 2 (tick 250): Infantry advances in fire teams, not as a blob
	if shellmapTicks == 250 then
		-- NATO north fire team: pushes toward river crossing
		local natoNorth = { NatoE3_1, NatoE3_2, NatoAR_1, NatoTL_1, NatoE1_1, NatoE2_1 }
		for _, unit in ipairs(natoNorth) do
			if Alive(unit) then
				unit.AttackMove(CPos.New(34, 24), 2)
			end
		end

		-- NATO center fire team: advances behind Abrams
		local natoCenter = { NatoE3_3, NatoAR_2, NatoTL_2, NatoE1_2, NatoSN, NatoDR }
		for _, unit in ipairs(natoCenter) do
			if Alive(unit) then
				unit.AttackMove(CPos.New(34, 36), 2)
			end
		end

		-- NATO south fire team: flanking approach
		local natoSouth = { NatoE3_4, NatoAR_3, NatoAT_2, NatoE2_2, NatoE1_3, NatoE4 }
		for _, unit in ipairs(natoSouth) do
			if Alive(unit) then
				unit.AttackMove(CPos.New(33, 48), 2)
			end
		end

		-- NATO support stays back: AT, medics, engineer, mortar
		local natoSupport = { NatoAT_1, NatoMT, NatoMedi1, NatoMedi2, NatoE6, NatoTecn, NatoAA }
		for _, unit in ipairs(natoSupport) do
			if Alive(unit) then
				unit.AttackMove(CPos.New(30, 35), 2)
			end
		end

		-- SF probes forward alone
		if Alive(NatoSF) then
			NatoSF.AttackMove(CPos.New(34, 44), 0)
		end

		-- Russia north fire team
		local russiaNorth = { RussiaE3_1, RussiaE3_2, RussiaAR_1, RussiaTL_1, RussiaE1_1, RussiaE2_1 }
		for _, unit in ipairs(russiaNorth) do
			if Alive(unit) then
				unit.AttackMove(CPos.New(62, 24), 2)
			end
		end

		-- Russia center fire team
		local russiaCenter = { RussiaE3_3, RussiaAR_2, RussiaTL_2, RussiaE1_2, RussiaSN, RussiaDR, RussiaShok }
		for _, unit in ipairs(russiaCenter) do
			if Alive(unit) then
				unit.AttackMove(CPos.New(61, 36), 2)
			end
		end

		-- Russia south fire team
		local russiaSouth = { RussiaE3_4, RussiaAR_3, RussiaAT_2, RussiaE2_2, RussiaE1_3, RussiaE4 }
		for _, unit in ipairs(russiaSouth) do
			if Alive(unit) then
				unit.AttackMove(CPos.New(64, 48), 2)
			end
		end

		-- Russia support stays back
		local russiaSupport = { RussiaAT_1, RussiaMT, RussiaMedi1, RussiaMedi2, RussiaE6, RussiaTecn, RussiaAA }
		for _, unit in ipairs(russiaSupport) do
			if Alive(unit) then
				unit.AttackMove(CPos.New(68, 35), 2)
			end
		end

		-- SF probes forward
		if Alive(RussiaSF) then
			RussiaSF.AttackMove(CPos.New(58, 44), 0)
		end
	end

	-- Phase 3 (tick 450): Helicopters fly in — attack helis sweep, transports reposition
	if shellmapTicks == 450 then
		if Alive(NatoHeli) then
			NatoHeli.AttackMove(CPos.New(44, 34), 0)
		end
		if Alive(NatoLittlebird) then
			NatoLittlebird.AttackMove(CPos.New(42, 22), 0)
		end
		if Alive(NatoTran) then
			NatoTran.Move(CPos.New(28, 38), 0)
		end

		if Alive(RussiaMi28) then
			RussiaMi28.AttackMove(CPos.New(54, 34), 0)
		end
		if Alive(RussiaHind) then
			RussiaHind.AttackMove(CPos.New(56, 22), 0)
		end
		if Alive(RussiaHalo) then
			RussiaHalo.Move(CPos.New(70, 38), 0)
		end
	end

	-- Phase 4 (tick 700): Artillery repositions forward, everyone hunts
	if shellmapTicks == 700 then
		if Alive(NatoM109) then
			NatoM109.Move(CPos.New(18, 28), 0)
		end
		if Alive(NatoM270) then
			NatoM270.Move(CPos.New(20, 38), 0)
		end
		if Alive(NatoHimars) then
			NatoHimars.Move(CPos.New(18, 48), 0)
		end

		if Alive(RussiaGiatsint) then
			RussiaGiatsint.Move(CPos.New(80, 28), 0)
		end
		if Alive(RussiaGrad) then
			RussiaGrad.Move(CPos.New(78, 38), 0)
		end
		if Alive(RussiaTos) then
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
			if Alive(unit) then
				unit.Stop()
				unit.Hunt()
			end
		end
	end
end

-- ============================================================
-- GAMEPLAY MODE
-- ============================================================

local garrisonTransferTime = 180 -- seconds
local firstWaveTime = 120        -- seconds
local waveInterval = 150          -- seconds between waves
local totalWaves = 5

local objectives = {}
local garrisonTransferred = false
local team1Players = {}
local team2Players = {}

-- Difficulty scaling
local waveScaling = {
	easy = { multiplier = 0.6, waveInterval = 180, totalWaves = 3 },
	normal = { multiplier = 1.0, waveInterval = 150, totalWaves = 5 },
	hard = { multiplier = 1.4, waveInterval = 120, totalWaves = 7 },
}

local difficulty

-- Wave definitions: Russia waves attack west, NATO waves attack east
local russiaAttackWaves = {
	{ unitTypes = { "e3.russia", "e3.russia", "e3.russia", "e1.russia", "e1.russia" },
	  entry = { 97, 38 }, rally = { 35, 38 } },
	{ unitTypes = { "btr", "e3.russia", "e3.russia", "ar.russia", "at.russia" },
	  entry = { 97, 30 }, rally = { 33, 30 } },
	{ unitTypes = { "t90", "bmp2", "e3.russia", "e3.russia", "ar.russia", "ar.russia" },
	  entry = { 97, 42 }, rally = { 32, 42 } },
	{ unitTypes = { "t90", "t90", "bmp2", "btr", "e3.russia", "e3.russia", "at.russia", "at.russia" },
	  entry = { 97, 35 }, rally = { 30, 35 } },
	{ unitTypes = { "t90", "t90", "bmp2", "bmp2", "btr", "e3.russia", "e3.russia", "e3.russia", "ar.russia", "ar.russia", "at.russia", "sn.russia" },
	  entry = { 97, 40 }, rally = { 28, 40 } },
	{ unitTypes = { "t90", "t90", "t90", "bmp2", "bmp2", "e3.russia", "e3.russia", "ar.russia", "at.russia", "at.russia" },
	  entry = { 97, 32 }, rally = { 26, 32 } },
	{ unitTypes = { "t90", "t90", "t90", "bmp2", "bmp2", "btr", "btr", "e3.russia", "e3.russia", "e3.russia", "e3.russia", "ar.russia", "ar.russia" },
	  entry = { 97, 44 }, rally = { 25, 44 } },
}

local natoAttackWaves = {
	{ unitTypes = { "e3.america", "e3.america", "e3.america", "e1.america", "e1.america" },
	  entry = { 0, 38 }, rally = { 61, 38 } },
	{ unitTypes = { "m113", "e3.america", "e3.america", "ar.america", "at.america" },
	  entry = { 0, 30 }, rally = { 63, 30 } },
	{ unitTypes = { "abrams", "bradley", "e3.america", "e3.america", "ar.america", "ar.america" },
	  entry = { 0, 42 }, rally = { 64, 42 } },
	{ unitTypes = { "abrams", "abrams", "bradley", "m113", "e3.america", "e3.america", "at.america", "at.america" },
	  entry = { 0, 35 }, rally = { 66, 35 } },
	{ unitTypes = { "abrams", "abrams", "bradley", "bradley", "m113", "e3.america", "e3.america", "e3.america", "ar.america", "ar.america", "at.america", "sn.america" },
	  entry = { 0, 40 }, rally = { 68, 40 } },
	{ unitTypes = { "abrams", "abrams", "abrams", "bradley", "bradley", "e3.america", "e3.america", "ar.america", "at.america", "at.america" },
	  entry = { 0, 32 }, rally = { 70, 32 } },
	{ unitTypes = { "abrams", "abrams", "abrams", "bradley", "bradley", "m113", "m113", "e3.america", "e3.america", "e3.america", "e3.america", "ar.america", "ar.america" },
	  entry = { 0, 44 }, rally = { 71, 44 } },
}

local function HuntAfterArrival(actor)
	Trigger.OnIdle(actor, function(self)
		self.Hunt()
	end)
end

local function SpawnWave(waveList, waveNum, ownerName)
	local wave = waveList[waveNum]
	if not wave then return end

	local owner = Scenario.GetPlayer(ownerName)
	local entry = CPos.New(wave.entry[1], wave.entry[2])
	local rally = CPos.New(wave.rally[1], wave.rally[2])

	local scale = waveScaling[difficulty].multiplier
	local unitCount = math.max(1, math.floor(#wave.unitTypes * scale + 0.5))
	local types = {}
	for i = 1, unitCount do
		types[i] = wave.unitTypes[((i - 1) % #wave.unitTypes) + 1]
	end

	local actors = Reinforcements.Reinforce(owner, types, { entry, rally }, 20, HuntAfterArrival)
	return actors
end

local function NotifyAllPlayers(players, notification)
	for _, p in ipairs(players) do
		Media.PlaySpeechNotification(p, notification)
	end
end

local function GameplayInit()
	Scenario.Init()

	difficulty = Map.LobbyOption("difficulty")
	if not waveScaling[difficulty] then difficulty = "normal" end

	waveInterval = waveScaling[difficulty].waveInterval
	totalWaves = waveScaling[difficulty].totalWaves

	local natoGar = Scenario.GetPlayer("NATOGarrison")
	local russiaGar = Scenario.GetPlayer("RussiaGarrison")

	-- Find all human players and sort by team
	local allPlayers = Player.GetPlayers(function(p)
		return p.InternalName:match("^Multi%d+$") and not p.IsNonCombatant
	end)

	for _, p in ipairs(allPlayers) do
		if p.Team == 1 then
			table.insert(team1Players, p)
		elseif p.Team == 2 then
			table.insert(team2Players, p)
		end
	end

	-- Initialize objectives for all active players
	for _, p in ipairs(team1Players) do
		InitObjectives(p)
		objectives[p.InternalName] = p.AddPrimaryObjective("Defend the river crossing and eliminate all enemy forces.")
	end
	for _, p in ipairs(team2Players) do
		InitObjectives(p)
		objectives[p.InternalName] = p.AddPrimaryObjective("Defend the river crossing and eliminate all enemy forces.")
	end

	-- Briefing text
	Scenario.SetBriefing("Frontline: Hold the river. Garrison reinforcements incoming in 3:00")
	Scenario.Message("Allied garrison forces are holding the front lines.", "EVA")
	Scenario.Message("You will gain control of them in " .. garrisonTransferTime .. " seconds.", "EVA")
	Scenario.Message("Enemy reinforcement waves will begin in " .. firstWaveTime .. " seconds.", "EVA")

	-- Camera to center
	Camera.Position = WPos.New(1024 * 49, 1024 * 38, 0)

	-- Order garrison units to defend their positions
	for _, unit in ipairs(natoGar.GetActors()) do
		if unit.HasProperty("Hunt") then
			unit.Hunt()
		end
	end
	for _, unit in ipairs(russiaGar.GetActors()) do
		if unit.HasProperty("Hunt") then
			unit.Hunt()
		end
	end

	-- Schedule garrison transfer — transfer to the first player on each team
	Trigger.AfterDelay(DateTime.Seconds(garrisonTransferTime), function()
		garrisonTransferred = true

		if #team1Players > 0 then
			Scenario.TransferAll("NATOGarrison", team1Players[1].InternalName)
		end
		if #team2Players > 0 then
			Scenario.TransferAll("RussiaGarrison", team2Players[1].InternalName)
		end

		Scenario.Message("Garrison forces are now under your command!", "EVA")
		NotifyAllPlayers(team1Players, "ReinforcementsArrived")
		NotifyAllPlayers(team2Players, "ReinforcementsArrived")
		Scenario.SetBriefing("Frontline: Garrison forces transferred. Defend the line!")
	end)

	-- Schedule countdown messages
	Trigger.AfterDelay(DateTime.Seconds(garrisonTransferTime - 60), function()
		Scenario.Message("Garrison transfer in 60 seconds.", "EVA")
		Scenario.SetBriefing("Frontline: Garrison transfer in 1:00")
	end)
	Trigger.AfterDelay(DateTime.Seconds(garrisonTransferTime - 30), function()
		Scenario.Message("Garrison transfer in 30 seconds.", "EVA")
		Scenario.SetBriefing("Frontline: Garrison transfer in 0:30")
	end)
	Trigger.AfterDelay(DateTime.Seconds(garrisonTransferTime - 10), function()
		Scenario.Message("Garrison transfer in 10 seconds.", "EVA")
		Scenario.SetBriefing("Frontline: Garrison transfer in 0:10")
	end)

	-- Schedule enemy reinforcement waves
	for i = 1, totalWaves do
		local waveDelay = firstWaveTime + (i - 1) * waveInterval
		Trigger.AfterDelay(DateTime.Seconds(waveDelay), function()
			Scenario.Message("Enemy reinforcement wave " .. i .. " of " .. totalWaves .. " approaching!", "EVA")
			NotifyAllPlayers(team1Players, "EnemyUnitsApproaching")
			NotifyAllPlayers(team2Players, "EnemyUnitsApproaching")

			SpawnWave(russiaAttackWaves, i, "RussiaGarrison")
			SpawnWave(natoAttackWaves, i, "NATOGarrison")

			if i == totalWaves then
				Trigger.AfterDelay(DateTime.Seconds(10), function()
					Scenario.Message("Final wave deployed. Eliminate all remaining forces to win.", "EVA")
					Scenario.SetBriefing("Frontline: Final wave. Destroy all enemies!")
				end)
			else
				Scenario.SetBriefing("Frontline: Wave " .. i .. "/" .. totalWaves .. " — Next wave in " .. waveInterval .. "s")
			end
		end)
	end

	-- Victory/defeat: when all players on one team are defeated, the other team wins
	for _, p in ipairs(team1Players) do
		Trigger.OnPlayerLost(p, function()
			local allLost = true
			for _, tp in ipairs(team1Players) do
				if not tp.HasNoRequiredUnits() then
					allLost = false
					break
				end
			end
			if allLost then
				for _, tp in ipairs(team2Players) do
					if objectives[tp.InternalName] and not tp.IsObjectiveCompleted(objectives[tp.InternalName]) then
						tp.MarkCompletedObjective(objectives[tp.InternalName])
					end
				end
			end
		end)
	end
	for _, p in ipairs(team2Players) do
		Trigger.OnPlayerLost(p, function()
			local allLost = true
			for _, tp in ipairs(team2Players) do
				if not tp.HasNoRequiredUnits() then
					allLost = false
					break
				end
			end
			if allLost then
				for _, tp in ipairs(team1Players) do
					if objectives[tp.InternalName] and not tp.IsObjectiveCompleted(objectives[tp.InternalName]) then
						tp.MarkCompletedObjective(objectives[tp.InternalName])
					end
				end
			end
		end)
	end
end

local function GameplayTick()
	Scenario.Tick()

	-- Update briefing timer during garrison countdown
	if not garrisonTransferred and Scenario.ticks > 0 then
		local remaining = garrisonTransferTime - math.floor(Scenario.ticks / 25)
		if remaining > 0 and remaining <= garrisonTransferTime and Scenario.ticks % 25 == 0 then
			local mins = math.floor(remaining / 60)
			local secs = remaining % 60
			local timeStr = string.format("%d:%02d", mins, secs)
			Scenario.SetBriefing("Frontline: Garrison transfer in " .. timeStr)
		end
	end
end

-- ============================================================
-- ENTRY POINTS
-- ============================================================

WorldLoaded = function()
	isShellmap = Map.IsShellmap
	if isShellmap then
		ShellmapInit()
	else
		GameplayInit()
	end
end

Tick = function()
	if isShellmap then
		ShellmapTick()
	else
		GameplayTick()
	end
end
