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
	end

	if ticks == 150 then
		NatoTLatBMP.EnterTransport(NatoBMP1)
		NatoARatBMP.EnterTransport(NatoBMP1)
		NatoATatBMP.EnterTransport(NatoBMP1)
		NatoE2atBMP.EnterTransport(NatoBMP1)
		NatoE3atBMP.EnterTransport(NatoBMP1)
	end

	if ticks == 200 then
		NatoBMP1.Move(WestHiddenExit1.Location, 0)
		NatoBMP1.Move(WestHiddenExit2.Location, 0)
		NatoBMP1.Move(WestHiddenExit3.Location, 0)
		NatoBMP1.Move(WestRoad2.Location, 0)
		NatoBMP1.Move(WestRoad3.Location, 0)
		NatoBMP1.Move(WestRoad4.Location, 0)
		NatoBMP1.Move(WestRoad5.Location, 0)
		NatoBMP1.Move(WestRoad6.Location, 0)
		NatoBMP1.UnloadPassengers(SouthVehicleLane1.Location, 1)
		-- NatoBMP1.Wait(30)
		NatoBMP1.Patrol({SouthVehicleLane2.Location, SouthVehicleLane3.Location, SouthVehicleLane4.Location, SouthVehicleLane5.Location }, false, 50)
	end

	if ticks == 200 then

	end

	if ticks == 800 then
		RussiaT90Tank1.Patrol({EastEntry1_1.Location, EastEntry1_2.Location }, false, 0)
		RussiaT90Tank2.Patrol({EastEntry2_1.Location, EastEntry2_2.Location }, false, 0)
		RussiaT90Tank3.Patrol({EastEntry3_1.Location }, false, 0)
		RussiaT90Tank4.Patrol({EastEntry4_1.Location, EastEntry4_2.Location }, false, 0)
	end

	if ticks == 10000 then
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
