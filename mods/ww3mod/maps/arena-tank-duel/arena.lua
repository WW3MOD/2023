-- Arena: Tank Duel - 3 Abrams vs 3 T-90
-- Both sides attack-move toward each other on game start

WorldLoaded = function()
	USA = Player.GetPlayer("Multi0")
	Russia = Player.GetPlayer("Multi1")

	-- Get all units for each side
	local usaTanks = USA.GetActorsByType("abrams")
	local rusTanks = Russia.GetActorsByType("t90")

	-- Attack-move USA tanks toward Russian side
	for _, tank in ipairs(usaTanks) do
		tank.AttackMove(CPos.New(52, 16))
	end

	-- Attack-move Russian tanks toward USA side
	for _, tank in ipairs(rusTanks) do
		tank.AttackMove(CPos.New(12, 16))
	end

	-- Check for victory every second
	Trigger.OnInterval(25, function()
		local usaAlive = #USA.GetActorsByType("abrams")
		local rusAlive = #Russia.GetActorsByType("t90")

		if usaAlive == 0 and rusAlive == 0 then
			Media.DisplayMessage("Draw! Both sides eliminated.", "Arena")
		elseif usaAlive == 0 then
			Media.DisplayMessage("Russia wins! All Abrams destroyed.", "Arena")
		elseif rusAlive == 0 then
			Media.DisplayMessage("USA wins! All T-90s destroyed.", "Arena")
		end
	end)
end
