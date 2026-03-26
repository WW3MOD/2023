--[[
	WW3MOD Scenario Helper Library
	Reusable functions for scenario scripts (frontline defense, wave assault, etc.)
	Include via rules.yaml:  LuaScript: Scripts: scenario.lua, my-scenario.lua

	Available globals after WorldLoaded:
		Scenario.players      — table of Player objects by name
		Scenario.ticks        — current tick count
		Scenario.spawned      — table of spawned actor groups by tag

	Usage in your scenario script:
		WorldLoaded = function()
			Scenario.Init()
			-- your setup here
		end
		Tick = function()
			Scenario.Tick()
			-- your tick logic here
		end
]]

Scenario = {
	ticks = 0,
	players = {},
	spawned = {},
	pendingTransfers = {},
	pendingWaves = {},
	pendingMessages = {},
	briefingShown = false,
	briefingText = nil,
	briefingDuration = 375, -- ~15 seconds at 25 tps
}

-- Initialize the scenario system. Call from WorldLoaded.
Scenario.Init = function()
	Scenario.ticks = 0
	Scenario.players = {}
	Scenario.spawned = {}
	Scenario.pendingTransfers = {}
	Scenario.pendingWaves = {}
	Scenario.pendingMessages = {}
end

-- Get a player by name. Caches the result.
Scenario.GetPlayer = function(name)
	if not Scenario.players[name] then
		Scenario.players[name] = Player.GetPlayer(name)
	end
	return Scenario.players[name]
end

-- Tick handler. Call from your Tick function.
Scenario.Tick = function()
	Scenario.ticks = Scenario.ticks + 1
end

-- ============================================================
-- SPAWNING
-- ============================================================

-- Spawn a single unit at a cell position.
-- Returns the created actor.
-- opts: { facing = WAngle, tag = "groupName" }
Scenario.SpawnUnit = function(unitType, ownerName, cellX, cellY, opts)
	opts = opts or {}
	local owner = Scenario.GetPlayer(ownerName)
	local inits = {
		Owner = owner,
		Location = CPos.New(cellX, cellY),
	}
	if opts.facing then
		inits.Facing = WAngle.New(opts.facing)
	end
	local actor = Actor.Create(unitType, true, inits)

	if opts.tag then
		if not Scenario.spawned[opts.tag] then
			Scenario.spawned[opts.tag] = {}
		end
		table.insert(Scenario.spawned[opts.tag], actor)
	end

	return actor
end

-- Spawn multiple units at positions.
-- units: array of { type, cellX, cellY, facing (optional) }
-- Returns array of actors.
Scenario.SpawnGroup = function(units, ownerName, tag)
	local actors = {}
	for _, u in ipairs(units) do
		local actor = Scenario.SpawnUnit(u.type or u[1], ownerName, u.cellX or u[2], u.cellY or u[3], {
			facing = u.facing or u[4],
			tag = tag
		})
		table.insert(actors, actor)
	end
	return actors
end

-- Spawn reinforcements from a map edge cell, marching to a rally point.
-- entryCell: { x, y } — where units appear (map edge)
-- rallyCell: { x, y } — where units march to
-- unitTypes: array of strings (e.g., { "e1", "e1", "e3" })
-- interval: ticks between each unit spawn (default 15)
-- onArrival: optional function(actor) called when each unit reaches rally
-- Returns array of actors.
Scenario.ReinforceFromEdge = function(unitTypes, ownerName, entryCell, rallyCell, opts)
	opts = opts or {}
	local owner = Scenario.GetPlayer(ownerName)
	local entry = CPos.New(entryCell[1], entryCell[2])
	local rally = CPos.New(rallyCell[1], rallyCell[2])
	local interval = opts.interval or 15
	local tag = opts.tag

	local actors = Reinforcements.Reinforce(owner, unitTypes, { entry, rally }, interval, opts.onArrival)

	if tag then
		if not Scenario.spawned[tag] then
			Scenario.spawned[tag] = {}
		end
		for _, a in ipairs(actors) do
			table.insert(Scenario.spawned[tag], a)
		end
	end

	return actors
end

-- ============================================================
-- OWNERSHIP TRANSFER
-- ============================================================

-- Transfer all living actors from one player to another immediately.
Scenario.TransferAll = function(fromName, toName)
	local from = Scenario.GetPlayer(fromName)
	local to = Scenario.GetPlayer(toName)
	local transferred = {}
	local actors = from.GetActors()
	for _, actor in ipairs(actors) do
		if not actor.IsDead and actor.IsInWorld then
			actor.Owner = to
			table.insert(transferred, actor)
		end
	end
	return transferred
end

-- Transfer specific actors to a new owner.
Scenario.TransferActors = function(actors, toName)
	local to = Scenario.GetPlayer(toName)
	for _, actor in ipairs(actors) do
		if not actor.IsDead and actor.IsInWorld then
			actor.Owner = to
		end
	end
end

-- Transfer a tagged spawn group to a new owner.
Scenario.TransferGroup = function(tag, toName)
	local group = Scenario.spawned[tag]
	if group then
		Scenario.TransferActors(group, toName)
	end
end

-- Schedule a delayed ownership transfer.
-- delaySeconds: seconds from now
-- callback: optional function() called after transfer
Scenario.ScheduleTransfer = function(fromName, toName, delaySeconds, callback)
	Trigger.AfterDelay(DateTime.Seconds(delaySeconds), function()
		local transferred = Scenario.TransferAll(fromName, toName)
		if callback then callback(transferred) end
	end)
end

-- Schedule a delayed group transfer.
Scenario.ScheduleGroupTransfer = function(tag, toName, delaySeconds, callback)
	Trigger.AfterDelay(DateTime.Seconds(delaySeconds), function()
		Scenario.TransferGroup(tag, toName)
		if callback then callback(Scenario.spawned[tag]) end
	end)
end

-- ============================================================
-- WAVE SPAWNING
-- ============================================================

-- Schedule a reinforcement wave from map edge after a delay.
-- wave: { unitTypes = {"e1","e1"}, entry = {x,y}, rally = {x,y}, owner = "Russia" }
-- delaySeconds: seconds from game start
-- onComplete: optional function(actors) after wave arrives
Scenario.ScheduleWave = function(wave, delaySeconds, onComplete)
	Trigger.AfterDelay(DateTime.Seconds(delaySeconds), function()
		local actors = Scenario.ReinforceFromEdge(
			wave.unitTypes, wave.owner,
			wave.entry, wave.rally,
			{ interval = wave.interval or 15, tag = wave.tag, onArrival = wave.onArrival }
		)
		if onComplete then onComplete(actors) end
	end)
end

-- Schedule multiple waves with increasing delays.
-- waves: array of wave tables (same format as ScheduleWave)
-- baseDelay: seconds before first wave
-- intervalSeconds: seconds between waves
Scenario.ScheduleWaves = function(waves, baseDelay, intervalSeconds)
	for i, wave in ipairs(waves) do
		local delay = baseDelay + (i - 1) * intervalSeconds
		Scenario.ScheduleWave(wave, delay)
	end
end

-- ============================================================
-- PATROL & DEFENSE
-- ============================================================

-- Order actors to attack-move to a position.
Scenario.AttackMove = function(actors, cellX, cellY, queue)
	queue = queue or 0
	local pos = CPos.New(cellX, cellY)
	for _, actor in ipairs(actors) do
		if not actor.IsDead then
			actor.AttackMove(pos, queue)
		end
	end
end

-- Order actors to patrol between waypoints (attack-move loop).
-- waypoints: array of { x, y } cell positions
Scenario.Patrol = function(actors, waypoints, queue)
	queue = queue or 0
	for _, actor in ipairs(actors) do
		if not actor.IsDead then
			for _, wp in ipairs(waypoints) do
				actor.AttackMove(CPos.New(wp[1], wp[2]), queue)
				queue = queue + 1
			end
			-- Loop: set idle trigger to restart patrol
			Trigger.OnIdle(actor, function(self)
				for _, wp in ipairs(waypoints) do
					self.AttackMove(CPos.New(wp[1], wp[2]), 0)
				end
			end)
		end
	end
end

-- Order actors to hold position and engage (Hunt stance).
Scenario.DefendPosition = function(actors)
	for _, actor in ipairs(actors) do
		if not actor.IsDead then
			actor.Hunt()
		end
	end
end

-- ============================================================
-- MESSAGING & BRIEFING
-- ============================================================

-- Display a mission message in the chat log.
-- color: optional Color (default white)
Scenario.Message = function(text, prefix, color)
	prefix = prefix or "SCENARIO"
	Media.DisplayMessage(text, prefix, color)
end

-- Display a message after a delay (seconds).
Scenario.DelayedMessage = function(text, delaySeconds, prefix, color)
	Trigger.AfterDelay(DateTime.Seconds(delaySeconds), function()
		Scenario.Message(text, prefix, color)
	end)
end

-- Set the mission text at top of screen (persistent HUD text).
Scenario.SetBriefing = function(text, color)
	UserInterface.SetMissionText(text, color)
end

-- Clear the mission text.
Scenario.ClearBriefing = function()
	UserInterface.SetMissionText("")
end

-- Play EVA speech notification for a player.
Scenario.PlaySpeech = function(playerName, notification)
	local player = Scenario.GetPlayer(playerName)
	Media.PlaySpeechNotification(player, notification)
end

-- ============================================================
-- OBJECTIVES (wraps campaign.lua patterns)
-- ============================================================

-- Add a primary objective for a player. Returns objective ID.
Scenario.AddPrimaryObjective = function(playerName, description)
	local player = Scenario.GetPlayer(playerName)
	return player.AddPrimaryObjective(description)
end

-- Add a secondary objective. Returns objective ID.
Scenario.AddSecondaryObjective = function(playerName, description)
	local player = Scenario.GetPlayer(playerName)
	return player.AddSecondaryObjective(description)
end

-- Mark an objective as completed.
Scenario.CompleteObjective = function(playerName, objectiveId)
	local player = Scenario.GetPlayer(playerName)
	player.MarkCompletedObjective(objectiveId)
end

-- Mark an objective as failed.
Scenario.FailObjective = function(playerName, objectiveId)
	local player = Scenario.GetPlayer(playerName)
	player.MarkFailedObjective(objectiveId)
end

-- ============================================================
-- UTILITY
-- ============================================================

-- Get all living actors in a tagged group.
Scenario.GetLiving = function(tag)
	local group = Scenario.spawned[tag]
	if not group then return {} end
	local living = {}
	for _, actor in ipairs(group) do
		if not actor.IsDead and actor.IsInWorld then
			table.insert(living, actor)
		end
	end
	return living
end

-- Count living actors in a tagged group.
Scenario.CountLiving = function(tag)
	return #Scenario.GetLiving(tag)
end

-- Check if all actors in a tagged group are dead.
Scenario.AllDead = function(tag)
	return Scenario.CountLiving(tag) == 0
end

-- Run a function when all actors in a tagged group are killed.
Scenario.OnGroupEliminated = function(tag, callback)
	local group = Scenario.spawned[tag]
	if group then
		Trigger.OnAllKilled(group, callback)
	end
end

-- Convenience: seconds to ticks
Scenario.Seconds = function(s)
	return DateTime.Seconds(s)
end

-- Convenience: minutes to ticks
Scenario.Minutes = function(m)
	return DateTime.Minutes(m)
end
