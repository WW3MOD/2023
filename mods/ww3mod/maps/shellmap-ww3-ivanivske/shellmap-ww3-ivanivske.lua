--[[
   Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]

BindActorTriggers = function(a)
	if a.HasProperty("HasPassengers") then
		Trigger.OnDamaged(a, function()
			if a.HasPassengers then
				a.Stop()
				a.UnloadPassengers()
			end
		end)
	end
end

SetupNatoUnits = function() end

ticks = 0
Tick = function()
	ticks = ticks + 1

	if ticks < 800 then
		Camera.Position = Camera.Position + WVec.New(25, -45, 0)
	elseif ticks == 1000 then
		MSLO1.ActivateNukePower(CPos.New(50, 55))
	end
end

WorldLoaded = function()
	nato = Player.GetPlayer("NATO")
	russia = Player.GetPlayer("Russia")

	Camera.Position = WPos.New(1024 * 32, 1024 * 85, 0)
	viewportOrigin = Camera.Position

	-- Media.DisplayMessage("Message")
	-- Media.DisplaySystemMessage("System")
	-- Media.FloatingText("Floating", viewportOrigin)

	-- SetupNatoUnits()

	Reinforcements.ReinforceWithTransport(nato,
		"bradley",
		{ "ar", "e3", "e3", "at", "e2", "tl" },
		{ WestRoad0.Location, WestRoad1.Location, WestRoad2.Location, WestRoad3.Location, WestRoad4.Location, WestRoad5.Location, WestRoad6.Location, WestRoadDrop1.Location },
		nil,
		nil,
		function() end,
		3
	)

	-- ReinforceWithTransport(
	-- 	Player owner,
	-- 	string actorType,
	-- 	String[] cargoTypes,
	-- 	CPos[] entryPath,
	-- 	CPos[] exitPath = nil,
	-- 	LuaFunction actionFunc = nil,
	-- 	LuaFunction exitFunc = nil,
	-- 	int dropRange = 3
	-- )

end

-- SendUnits = function(path, unitTypes, interval)
-- 	local units = Reinforcements.Reinforce(nato, unitTypes, path, interval)
-- 	Utils.Do(units, function(unit)
-- 		BindActorTriggers(unit)
-- 	end)
-- 	Trigger.OnAllKilled(units, function() SendUnits(path, unitTypes, interval) end)
-- end
