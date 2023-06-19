--[[
   Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]

-- LeftRoadWaypoints = { LeftRoad1, LeftRoad2, LeftRoad3, LeftRoad4, LeftRoad5 }
-- RightRoadWaypoints = { RightRoad1, RightRoad2, RightRoad3, RightRoad4, RightRoad5 }

BindActorTriggers = function(a)
	-- if a.HasProperty("Hunt") then
	-- 	if a.Owner == NATO then
	-- 		Trigger.OnIdle(a, function(a)
	-- 			if a.IsInWorld then
	-- 				a.Hunt()
	-- 			end
	-- 		end)
	-- 	else
	-- 		Trigger.OnIdle(a, function(a)
	-- 			if a.IsInWorld then
	-- 				a.AttackMove(AlliedTechnologyCenter.Location)
	-- 			end
	-- 		end)
	-- 	end
	-- end

	-- if a.HasProperty("HasPassengers") then
	-- 	Trigger.OnPassengerExited(a, function(t, p)
	-- 		BindActorTriggers(p)
	-- 	end)

	-- 	Trigger.OnDamaged(a, function()
	-- 		if a.HasPassengers then
	-- 			a.Stop()
	-- 			a.UnloadPassengers()
	-- 		end
	-- 	end)
	-- end
end

SetupNatoUnits = function()
	Utils.Do(Map.NamedActors, function(a)
		if a.Owner == NATO and a.HasProperty("AcceptsCondition") and a.AcceptsCondition("unkillable") then
			a.GrantCondition("unkillable")
			a.Stance = "Defend"
		end
	end)
end

ticks = 0
speed = 1

Tick = function()
	ticks = ticks + 1

	local t = (ticks + 45) % (360 * speed) * (math.pi / 180) / speed;

	Camera.Position = viewportOrigin + WVec.New(-1920 * math.sin(t), -2048 * math.cos(t), 0)

	if ticks == 1000 then
		MSLO1.ActivateNukePower(CPos.New(40,49))
	end
end

WorldLoaded = function()
	NATO = Player.GetPlayer("NATO")
	russia = Player.GetPlayer("Russia")
	viewportOrigin = Camera.Position

	Media.DisplayMessage("Message")
	Media.DisplaySystemMessage("System")
	Media.FloatingText("Floating", viewportOrigin)

	-- SetupNatoUnits()
	-- SendUnits(
	-- 	{LeftRoadEntry.Location, LeftRoad1.Location, LeftRoad2.Location, LeftRoad3.Location, LeftRoad4.Location, LeftRoad5.Location },
	-- 	{ "humvee", "abrams", "bradley" }, 20
	-- )
end

SendUnits = function(path, unitTypes, interval)
	-- local units = Reinforcements.Reinforce(NATO, unitTypes, path, interval)
	-- Utils.Do(units, function(unit)
	-- 	BindActorTriggers(unit)
	-- end)
	-- Trigger.OnAllKilled(units, function() SendUnits(path, unitTypes, interval) end)
end
