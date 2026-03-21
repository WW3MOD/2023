-- Arena Shellmap: Tank Duel - 3 Abrams vs 3 T-90
-- AutoTarget with AttackAnything handles targeting automatically
-- Lua handles camera and attack-move orders

ticks = 0

Tick = function()
	ticks = ticks + 1

	-- Slow camera pan across the battlefield
	if ticks > 50 then
		Camera.Position = Camera.Position + WVec.New(15, 0, 0)
	end
end

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	local russia = Player.GetPlayer("Russia")

	-- Center camera on the battlefield
	Camera.Position = WPos.New(1024 * 32, 1024 * 16, 0)

	-- After 2 seconds, attack-move both sides toward each other
	Trigger.AfterDelay(DateTime.Seconds(2), function()
		Abrams1.AttackMove(CPos.New(52, 16))
		Abrams2.AttackMove(CPos.New(52, 16))
		Abrams3.AttackMove(CPos.New(52, 16))

		T90_1.AttackMove(CPos.New(12, 16))
		T90_2.AttackMove(CPos.New(12, 16))
		T90_3.AttackMove(CPos.New(12, 16))
	end)
end
