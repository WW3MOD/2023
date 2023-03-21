--[[
   Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]

UnitsEvacuatedThreshold =
{
	hard = 200,
	normal = 100,
	easy = 50
}

AttackAtFrame =
{
	hard = 500,
	normal = 500,
	easy = 600
}

MinAttackAtFrame =
{
	hard = 100,
	normal = 100,
	easy = 150
}

MaxSovietYaks =
{
	hard = 4,
	normal = 2,
	easy = 0
}

SovietParadrops =
{
	hard = 40,
	normal = 20,
	easy = 0
}

SovietParadropTicks =
{
	hard = DateTime.Minutes(17),
	normal = DateTime.Minutes(20),
	easy = DateTime.Minutes(20)
}

SovietUnits2Ticks =
{
	hard = DateTime.Minutes(12),
	normal = DateTime.Minutes(15),
	easy = DateTime.Minutes(15)
}

SovietEntryPoints =
{
	SovietEntryPoint1, SovietEntryPoint2, SovietEntryPoint3, SovietEntryPoint4, SovietEntryPoint5, SovietEntryPoint6
}

SovietRallyPoints =
{
	SovietRallyPoint1, SovietRallyPoint2, SovietRallyPoint3, SovietRallyPoint4, SovietRallyPoint5, SovietRallyPoint6
}

SovietAirfields =
{
	SovietAirfield1, SovietAirfield2, SovietAirfield3, SovietAirfield4,
	SovietAirfield5, SovietAirfield6, SovietAirfield7, SovietAirfield8
}

MountainEntry = { CPos.New(25, 45), CPos.New(25, 46), CPos.New(25, 47), CPos.New(25, 48), CPos.New(25, 49) }

BridgeEntry = { CPos.New(25, 29), CPos.New(26, 29), CPos.New(27, 29), CPos.New(28, 29) }

MobileConstructionVehicle = { "mcv" }
Yak = { "yak" }

ReinforcementsTicks1 = DateTime.Minutes(5)
Reinforcements1 =
{
	"strykershorad", "abrams", "abrams", "abrams", "abrams", "bradley", "bradley",
	"humvee", "humvee", "e1", "e1", "e1", "e1", "e3", "e3"
}

ReinforcementsTicks2 = DateTime.Minutes(10)
Reinforcements2 =
{
	"strykershorad", "abrams", "abrams", "abrams", "abrams", "truk", "truk", "truk",
	"truk",	"truk", "truk", "bradley", "bradley", "humvee", "humvee"
}

SovietUnits1 =
{
	"t72", "t72", "t72", "t72", "t72", "t72", "grad", "grad", "tunguska",
	"bmp2", "e1", "e1", "e2", "e3", "e3", "e4"
}

SovietUnits2 =
{
	"tos", "tos", "tos", "tos", "t72", "t72", "t72", "t72", "grad",
	"grad", "tunguska", "bmp2", "e1", "e1", "e2", "e3", "e3", "e4"
}

CurrentReinforcement1 = 0
CurrentReinforcement2 = 0
SpawnAlliedUnit = function(units)
	Reinforcements.Reinforce(america1, units, { America1EntryPoint.Location, America1MovePoint.Location })

	if america2 then
		Reinforcements.Reinforce(america2, units, { America2EntryPoint.Location, America2MovePoint.Location })
	end

	Utils.Do(humans, function(player)
		Trigger.AfterDelay(DateTime.Seconds(2), function()
			Media.PlaySpeechNotification(player, "AlliedReinforcementsNorth")
		end)
	end)

	if CurrentReinforcement1 < #Reinforcements1 then
		CurrentReinforcement1 = CurrentReinforcement1 + 1
		Trigger.AfterDelay(ReinforcementsTicks1, function()
			reinforcements1 = { Reinforcements1[CurrentReinforcement1] }
			SpawnAlliedUnit(reinforcements1)
		end)
	end

	if CurrentReinforcement2 < #Reinforcements2 then
		CurrentReinforcement2 = CurrentReinforcement2 + 1
		Trigger.AfterDelay(ReinforcementsTicks2, function()
			reinforcements2 = { Reinforcements2[CurrentReinforcement2] }
			SpawnAlliedUnit(reinforcements2)
		end)
	end
end

SovietGroupSize = 5
SpawnSovietUnits = function()

	local units = SovietUnits1
	if DateTime.GameTime >= SovietUnits2Ticks[Difficulty] then
		units = SovietUnits2
	end

	local unitType = Utils.Random(units)
	local spawnPoint = Utils.Random(SovietEntryPoints)
	local rallyPoint = Utils.Random(SovietRallyPoints)
	local actor = Actor.Create(unitType, true, { Owner = russias, Location = spawnPoint.Location })
	actor.AttackMove(rallyPoint.Location)
	IdleHunt(actor)

	local delay = math.max(attackAtFrame - 5, minAttackAtFrame)
	Trigger.AfterDelay(delay, SpawnSovietUnits)
end

SovietParadrop = 0
SendSovietParadrop = function()
	local russiaParadrops = SovietParadrops[Difficulty]

	if (SovietParadrop > russiaParadrops) then
		return
	end

	SovietParadrop = SovietParadrop + 1

	Utils.Do(humans, function(player)
		Media.PlaySpeechNotification(player, "SovietForcesApproaching")
	end)

	local x = Utils.RandomInteger(ParadropBoxTopLeft.Location.X, ParadropBoxBottomRight.Location.X)
	local y = Utils.RandomInteger(ParadropBoxBottomRight.Location.Y, ParadropBoxTopLeft.Location.Y)

	local randomParadropCell = CPos.New(x, y)
	local lz = Map.CenterOfCell(randomParadropCell)

	local powerproxy = Actor.Create("powerproxy.paratroopers", false, { Owner = russias })
	powerproxy.TargetParatroopers(lz)
	powerproxy.Destroy()

	Trigger.AfterDelay(russiaParadropTicks, SendSovietParadrop)
end

AircraftTargets = function(yak)
	local targets = Utils.Where(Map.ActorsInWorld, function(a)
		return (a.Owner == america1 or a.Owner == america2) and a.HasProperty("Health") and yak.CanTarget(a)
	end)

	-- Prefer mobile units
	table.sort(targets, function(a, b) return a.HasProperty("Move") and not b.HasProperty("Move") end)

	return targets
end

YakAttack = function(yak, target)
	if not target or target.IsDead or (not target.IsInWorld) or (not yak.CanTarget(target)) then
		local targets = AircraftTargets(yak)
		if #targets > 0 then
			target = Utils.Random(targets)
		end
	end

	if target and yak.AmmoCount() > 0 and yak.CanTarget(target) then
		yak.Attack(target)
	else
		-- Includes yak.Resupply()
		yak.ReturnToBase()
	end

	yak.CallFunc(function()
		YakAttack(yak, target)
	end)
end

ManageSovietAircraft = function()
	if america1.IsObjectiveCompleted(destroyAirbases) then
		return
	end

	local maxSovietYaks = MaxSovietYaks[Difficulty]
	local russiaYaks = russias.GetActorsByType('yak')
	if #russiaYaks < maxSovietYaks then
		russias.Build(Yak, function(units)
			local yak = units[1]
			YakAttack(yak)
		end)
	end
end

UnitsEvacuated = 0
EvacuateAlliedUnit = function(unit)
	if (unit.Owner == america1 or unit.Owner == america2) and unit.HasProperty("Move") then
		unit.Stop()
		unit.Owner = america

		if unit.Type == 'strykershorad' then
			Utils.Do(humans, function(player)
				if player then
					player.MarkCompletedObjective(evacuateMgg)
				end
			end)
		end

		UnitsEvacuated = UnitsEvacuated + 1
		if unit.HasProperty("HasPassengers") then
			UnitsEvacuated = UnitsEvacuated + unit.PassengerCount
		end

		local exitCell = Map.ClosestEdgeCell(unit.Location)
		Trigger.OnIdle(unit, function()
			unit.ScriptedMove(exitCell)
		end)

		local exit = Map.CenterOfCell(exitCell)
		Trigger.OnEnteredProximityTrigger(exit, WDist.FromCells(1), function(a)
			a.Destroy()
		end)

		UserInterface.SetMissionText(UnitsEvacuated .. "/" .. unitsEvacuatedThreshold .. " units evacuated.", TextColor)

		if UnitsEvacuated >= unitsEvacuatedThreshold then
			Utils.Do(humans, function(player)
				if player then
					player.MarkCompletedObjective(evacuateUnits)
				end
			end)
		end
	end
end

Tick = function()
	if DateTime.GameTime % 100 == 0 then
		ManageSovietAircraft()

		Utils.Do(humans, function(player)
			if player and player.HasNoRequiredUnits() then
				russias.MarkCompletedObjective(russiaObjective)
			end
		end)
	end
end

WorldLoaded = function()
	-- NPC
	neutral = Player.GetPlayer("Neutral")
	america = Player.GetPlayer("America")
	russias = Player.GetPlayer("Russia")

	-- Player controlled
	america1 = Player.GetPlayer("America1")
	america2 = Player.GetPlayer("America2")

	humans = { america1, america2 }
	Utils.Do(humans, function(player)
		if player and player.IsLocalPlayer then
			InitObjectives(player)
			TextColor = player.Color
		end
	end)

	unitsEvacuatedThreshold = UnitsEvacuatedThreshold[Difficulty]
	UserInterface.SetMissionText(UnitsEvacuated .. "/" .. unitsEvacuatedThreshold .. " units evacuated.", TextColor)
	Utils.Do(humans, function(player)
		if player then
			evacuateUnits = player.AddObjective("Evacuate " .. unitsEvacuatedThreshold .. " units.")
			destroyAirbases = player.AddObjective("Destroy the nearby Soviet airbases.", "Secondary", false)
			evacuateMgg = player.AddObjective("Evacuate at least one mobile gap generator.", "Secondary", false)
		end
	end)

	Trigger.OnAllKilledOrCaptured(SovietAirfields, function()
		Utils.Do(humans, function(player)
			if player then
				player.MarkCompletedObjective(destroyAirbases)
			end
		end)
	end)

	russiaObjective = russias.AddObjective("Eradicate all allied troops.")

	if not america2 or america1.IsLocalPlayer then
		Camera.Position = America1EntryPoint.CenterPosition
	else
		Camera.Position = America2EntryPoint.CenterPosition
	end

	if not america2 then
		america1.Cash = 10000
		Media.DisplayMessage("Transferring funds.", "Co-Commander is missing")
	end

	SpawnAlliedUnit(MobileConstructionVehicle)

	minAttackAtFrame = MinAttackAtFrame[Difficulty]
	attackAtFrame = AttackAtFrame[Difficulty]
	Trigger.AfterDelay(attackAtFrame, SpawnSovietUnits)

	russiaParadropTicks = SovietParadropTicks[Difficulty]
	Trigger.AfterDelay(russiaParadropTicks, SendSovietParadrop)

	Trigger.OnEnteredFootprint(MountainEntry, EvacuateAlliedUnit)
	Trigger.OnEnteredFootprint(BridgeEntry, EvacuateAlliedUnit)
end
