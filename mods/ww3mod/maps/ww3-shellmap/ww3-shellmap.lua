--[[
   Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]

LeftRoadWaypoints = { LeftRoad1, LeftRoad2, LeftRoad3, LeftRoad4, LeftRoad5 }
RightRoadWaypoints = { RightRoad1, RightRoad2, RightRoad3, RightRoad4, RightRoad5 }

BindActorTriggers = function(a)
	if a.HasProperty("Hunt") then
		if a.Owner == NATO then
			Trigger.OnIdle(a, function(a)
				if a.IsInWorld then
					a.Hunt()
				end
			end)
		else
			Trigger.OnIdle(a, function(a)
				if a.IsInWorld then
					a.AttackMove(AlliedTechnologyCenter.Location)
				end
			end)
		end
	end

	if a.HasProperty("HasPassengers") then
		Trigger.OnPassengerExited(a, function(t, p)
			BindActorTriggers(p)
		end)

		Trigger.OnDamaged(a, function()
			if a.HasPassengers then
				a.Stop()
				a.UnloadPassengers()
			end
		end)
	end
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
speed = 5

Tick = function()
	ticks = ticks + 1

	local t = (ticks + 45) % (360 * speed) * (math.pi / 180) / speed;
	Camera.Position = viewportOrigin + WVec.New(19200 * math.sin(t), 20480 * math.cos(t), 20480)
end

WorldLoaded = function()
	NATO = Player.GetPlayer("NATO")
	russia = Player.GetPlayer("Russia")
	viewportOrigin = Camera.Position

	SetupNatoUnits()
	SendNatoUnits(LeftRoad4.Location, { "humvee", "abrams", "bradley" }, 50)
end

SendNatoUnits = function(entryCell, unitTypes, interval)
	local units = Reinforcements.Reinforce(NATO, unitTypes, { LeftRoad4.Location, LeftRoad5.Location }, interval)
	Utils.Do(units, function(unit)
		BindActorTriggers(unit)
	end)
	Trigger.OnAllKilled(units, function() SendNatoUnits(entryCell, unitTypes, interval) end)
end
