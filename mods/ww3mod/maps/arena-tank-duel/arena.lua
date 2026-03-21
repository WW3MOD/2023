-- Arena Shellmap: Tank Duel - 3 Abrams vs 3 T-90
-- AutoTarget with AttackAnything + ScanRadius 40 handles targeting
-- Lua just moves them toward each other; AutoTarget does the shooting

ticks = 0

Tick = function()
	ticks = ticks + 1

	-- Slow camera pan right across the battlefield
	if ticks > 100 and ticks < 1500 then
		Camera.Position = Camera.Position + WVec.New(18, 0, 0)
	end
end

WorldLoaded = function()
	-- Center camera on USA side
	Camera.Position = WPos.New(1024 * 12, 1024 * 16, 0)

	-- Move both sides toward each other using Patrol
	-- Patrol keeps them moving and AutoTarget fires when in range
	Trigger.AfterDelay(DateTime.Seconds(1), function()
		Abrams1.Patrol({ CPos.New(40, 14), CPos.New(52, 14) }, false, 0)
		Abrams2.Patrol({ CPos.New(40, 16), CPos.New(52, 16) }, false, 0)
		Abrams3.Patrol({ CPos.New(40, 18), CPos.New(52, 18) }, false, 0)

		T90_1.Patrol({ CPos.New(24, 14), CPos.New(12, 14) }, false, 0)
		T90_2.Patrol({ CPos.New(24, 16), CPos.New(12, 16) }, false, 0)
		T90_3.Patrol({ CPos.New(24, 18), CPos.New(12, 18) }, false, 0)
	end)
end
