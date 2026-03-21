-- Arena Shellmap: Tank Duel - 3 Abrams vs 3 T-90
-- Uses tick-based commands (matching working shellmap pattern)

ticks = 0
moved = false

Tick = function()
	ticks = ticks + 1

	-- Issue move commands at tick 50 (give actors time to initialize)
	if ticks == 50 and not moved then
		moved = true

		-- USA tanks advance right toward center
		Abrams1.AttackMove(CPos.New(40, 14), 0)
		Abrams2.AttackMove(CPos.New(40, 16), 0)
		Abrams3.AttackMove(CPos.New(40, 18), 0)

		-- Russia tanks advance left toward center
		T90_1.AttackMove(CPos.New(24, 14), 0)
		T90_2.AttackMove(CPos.New(24, 16), 0)
		T90_3.AttackMove(CPos.New(24, 18), 0)
	end

	-- Slow camera pan right across the battlefield
	if ticks > 30 and ticks < 1200 then
		Camera.Position = Camera.Position + WVec.New(20, 0, 0)
	end
end

WorldLoaded = function()
	usa = Player.GetPlayer("USA")
	russia = Player.GetPlayer("Russia")

	-- Center camera on USA side to see them advance
	Camera.Position = WPos.New(1024 * 12, 1024 * 16, 0)

	Media.DisplayMessage("Arena: Tank Duel loaded", "Debug")
end
