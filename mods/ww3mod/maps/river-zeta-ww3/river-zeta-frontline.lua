-- River Zeta — Frontline Scenario
-- NATO vs Russia across the river. Garrison forces hold the front lines.
-- After 3 minutes, garrison units transfer to player control.
-- Enemy reinforcement waves arrive periodically from the map edges.
--
-- Player Multi0 = NATO (left/west side)
-- Player Multi1 = Russia (right/east side)
-- NATOGarrison = allied frontline troops (non-playable, transfer at 3min)
-- RussiaGarrison = allied frontline troops (non-playable, transfer at 3min)

-- Map geometry: 98x82, river roughly at x=48-50
-- NATO edge: x=0 (west), Russia edge: x=97 (east)

local garrisonTransferTime = 180 -- seconds
local firstWaveTime = 120        -- seconds
local waveInterval = 150          -- seconds between waves
local totalWaves = 5

local natoObjective
local russiaObjective
local garrisonTransferred = false

-- Difficulty scaling
local waveScaling = {
	easy = { multiplier = 0.6, waveInterval = 180, totalWaves = 3 },
	normal = { multiplier = 1.0, waveInterval = 150, totalWaves = 5 },
	hard = { multiplier = 1.4, waveInterval = 120, totalWaves = 7 },
}

local difficulty

-- ============================================================
-- WAVE DEFINITIONS
-- ============================================================

-- Russia waves: Russia-faction units come from east edge, push west across river to attack NATO
local russiaAttackWaves = {
	-- Wave 1: Light probe
	{ unitTypes = { "e3.russia", "e3.russia", "e3.russia", "e1.russia", "e1.russia" },
	  entry = { 97, 38 }, rally = { 35, 38 } },
	-- Wave 2: Mechanized
	{ unitTypes = { "btr", "e3.russia", "e3.russia", "ar.russia", "at.russia" },
	  entry = { 97, 30 }, rally = { 33, 30 } },
	-- Wave 3: Armor push
	{ unitTypes = { "t90", "bmp2", "e3.russia", "e3.russia", "ar.russia", "ar.russia" },
	  entry = { 97, 42 }, rally = { 32, 42 } },
	-- Wave 4: Combined arms
	{ unitTypes = { "t90", "t90", "bmp2", "btr", "e3.russia", "e3.russia", "at.russia", "at.russia" },
	  entry = { 97, 35 }, rally = { 30, 35 } },
	-- Wave 5: Full assault
	{ unitTypes = { "t90", "t90", "bmp2", "bmp2", "btr", "e3.russia", "e3.russia", "e3.russia", "ar.russia", "ar.russia", "at.russia", "sn.russia" },
	  entry = { 97, 40 }, rally = { 28, 40 } },
	-- Wave 6-7 (hard only): Escalation
	{ unitTypes = { "t90", "t90", "t90", "bmp2", "bmp2", "e3.russia", "e3.russia", "ar.russia", "at.russia", "at.russia" },
	  entry = { 97, 32 }, rally = { 26, 32 } },
	{ unitTypes = { "t90", "t90", "t90", "bmp2", "bmp2", "btr", "btr", "e3.russia", "e3.russia", "e3.russia", "e3.russia", "ar.russia", "ar.russia" },
	  entry = { 97, 44 }, rally = { 25, 44 } },
}

-- NATO waves: NATO-faction units come from west edge, push east across river to attack Russia
local natoAttackWaves = {
	-- Wave 1: Light probe
	{ unitTypes = { "e3.america", "e3.america", "e3.america", "e1.america", "e1.america" },
	  entry = { 0, 38 }, rally = { 61, 38 } },
	-- Wave 2: Mechanized
	{ unitTypes = { "m113", "e3.america", "e3.america", "ar.america", "at.america" },
	  entry = { 0, 30 }, rally = { 63, 30 } },
	-- Wave 3: Armor push
	{ unitTypes = { "abrams", "bradley", "e3.america", "e3.america", "ar.america", "ar.america" },
	  entry = { 0, 42 }, rally = { 64, 42 } },
	-- Wave 4: Combined arms
	{ unitTypes = { "abrams", "abrams", "bradley", "m113", "e3.america", "e3.america", "at.america", "at.america" },
	  entry = { 0, 35 }, rally = { 66, 35 } },
	-- Wave 5: Full assault
	{ unitTypes = { "abrams", "abrams", "bradley", "bradley", "m113", "e3.america", "e3.america", "e3.america", "ar.america", "ar.america", "at.america", "sn.america" },
	  entry = { 0, 40 }, rally = { 68, 40 } },
	-- Wave 6-7 (hard only)
	{ unitTypes = { "abrams", "abrams", "abrams", "bradley", "bradley", "e3.america", "e3.america", "ar.america", "at.america", "at.america" },
	  entry = { 0, 32 }, rally = { 70, 32 } },
	{ unitTypes = { "abrams", "abrams", "abrams", "bradley", "bradley", "m113", "m113", "e3.america", "e3.america", "e3.america", "e3.america", "ar.america", "ar.america" },
	  entry = { 0, 44 }, rally = { 71, 44 } },
}

-- ============================================================
-- HELPER FUNCTIONS
-- ============================================================

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

	-- Scale unit count by difficulty
	local scale = waveScaling[difficulty].multiplier
	local unitCount = math.max(1, math.floor(#wave.unitTypes * scale + 0.5))
	local types = {}
	for i = 1, unitCount do
		types[i] = wave.unitTypes[((i - 1) % #wave.unitTypes) + 1]
	end

	local actors = Reinforcements.Reinforce(owner, types, { entry, rally }, 20, HuntAfterArrival)
	return actors
end

-- ============================================================
-- SCENARIO LOGIC
-- ============================================================

WorldLoaded = function()
	Scenario.Init()

	difficulty = Map.LobbyOption("difficulty")
	if not waveScaling[difficulty] then difficulty = "normal" end

	waveInterval = waveScaling[difficulty].waveInterval
	totalWaves = waveScaling[difficulty].totalWaves

	local nato = Scenario.GetPlayer("Multi0")
	local russia = Scenario.GetPlayer("Multi1")
	local natoGar = Scenario.GetPlayer("NATOGarrison")
	local russiaGar = Scenario.GetPlayer("RussiaGarrison")

	-- Initialize objectives
	InitObjectives(nato)
	InitObjectives(russia)

	natoObjective = nato.AddPrimaryObjective("Defend the river crossing and eliminate all enemy forces.")
	russiaObjective = russia.AddPrimaryObjective("Defend the river crossing and eliminate all enemy forces.")

	-- Briefing text
	Scenario.SetBriefing("Frontline: Hold the river. Garrison reinforcements incoming in 3:00")
	Scenario.Message("Allied garrison forces are holding the front lines.", "EVA")
	Scenario.Message("You will gain control of them in " .. garrisonTransferTime .. " seconds.", "EVA")
	Scenario.Message("Enemy reinforcement waves will begin in " .. firstWaveTime .. " seconds.", "EVA")

	-- Camera to center
	Camera.Position = WPos.New(1024 * 49, 1024 * 38, 0)

	-- Order garrison units to defend their positions
	local natoGarUnits = natoGar.GetActors()
	for _, unit in ipairs(natoGarUnits) do
		if unit.HasProperty("Hunt") then
			unit.Hunt()
		end
	end
	local russiaGarUnits = russiaGar.GetActors()
	for _, unit in ipairs(russiaGarUnits) do
		if unit.HasProperty("Hunt") then
			unit.Hunt()
		end
	end

	-- Schedule garrison transfer
	Trigger.AfterDelay(DateTime.Seconds(garrisonTransferTime), function()
		garrisonTransferred = true
		Scenario.TransferAll("NATOGarrison", "Multi0")
		Scenario.TransferAll("RussiaGarrison", "Multi1")
		Scenario.Message("Garrison forces are now under your command!", "EVA")
		Media.PlaySpeechNotification(nato, "ReinforcementsArrived")
		Media.PlaySpeechNotification(russia, "ReinforcementsArrived")
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
			Media.PlaySpeechNotification(nato, "EnemyUnitsApproaching")
			Media.PlaySpeechNotification(russia, "EnemyUnitsApproaching")

			-- Waves attack BOTH sides — each side gets attacked by the other faction
			SpawnWave(russiaAttackWaves, i, "Multi1")
			SpawnWave(natoAttackWaves, i, "Multi0")

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

	-- Victory/defeat conditions
	Trigger.OnPlayerLost(nato, function()
		if not russia.IsObjectiveCompleted(russiaObjective) then
			russia.MarkCompletedObjective(russiaObjective)
		end
	end)
	Trigger.OnPlayerLost(russia, function()
		if not nato.IsObjectiveCompleted(natoObjective) then
			nato.MarkCompletedObjective(natoObjective)
		end
	end)
end

Tick = function()
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
