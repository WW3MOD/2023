-- DEMO -- the FINAL EXCHANGE. Nothing here is asserted: no AssertWithin, no Test.Pass, no Test.Fail,
-- no result.json. The viewer watches and closes the window when done.
--
-- WHAT THIS FILE EXISTS TO SHOW, and it is the one thing the demo could not show before: a warhead
-- a PLAYER placed inside the fifteen-second window, flying alongside the ones Dead Hand places for
-- the side that placed nothing. USA fires its B83; Russia does not, and Dead Hand aims for Russia.
-- Both halves of the user's ruling -- "either way the outcome is the same" -- are on screen at once.
--
-- THE ORDER GOES THROUGH THE REAL PATH. Test.ActivateSupportPower issues exactly the order
-- SelectGenericPowerTarget emits on a left-click (TestGlobal.cs:1658-1677), so what is exercised is
-- the support power a human would have clicked, not a private entry point. If the window did not
-- actually grant and arm the power, this returns 'not-ready:0' or 'hidden' and NOTHING FLIES from
-- USA -- which is the demo failing visibly rather than silently, and is why the status string is
-- printed on screen.
--
-- TICKS, NOT SECONDS. Timestep is 60 ms, so 16.67 ticks per second -- NOT the 25 that
-- TestHarness.TicksPerSecond carries (a deliberately-preserved harness convention for AssertWithin
-- budgets, documented in test-helpers.lua, and not the tick rate). Every delay below is raw ticks.

-- Ground zero for USA's own warhead: CITY EAST, which is also one of the two clusters Dead Hand
-- aims at. Deliberately the same kind of target the machine would pick, so the two placements are
-- comparable rather than one of them being obviously a demo artifact.
AimPoint = { X = 49, Y = 22 }

-- WorldTick at which the Time Limit expires and the window opens. Mirrors TimeLimitTicks in
-- rules.yaml; the two must move together.
WindowOpensTick = 250

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	Russia = Player.GetPlayer("Russia")

	-- Centre between the two contact groups, so the score-building skirmish and the eastern city
	-- are both roughly in frame at the default zoom.
	Camera.Position = WPos.New(38 * 1024 + 512, 20 * 1024 + 512, 0)

	-- Pre-selected so the support-power bin is on screen before the window opens, which is what
	-- lets a viewer SEE the B83 cameo appear rather than take its arrival on trust.
	-- UsaA is the map actor id from map.yaml; map actors are exposed to Lua under their id.
	TestHarness.Select(UsaA)

	Media.DisplayMessage("Final exchange demo: the clock runs out, both sides get 15 s to place "
		.. "their own game-enders, Dead Hand places the rest.", "DEAD HAND")

	-- Just before the window: the cameo must NOT be there yet. Both game-enders are event tier
	-- (Prerequisites: powers.event, provided by no faction) so outside the exchange the bin is
	-- empty -- which is the control that makes its appearance ten ticks later mean something.
	Trigger.AfterDelay(WindowOpensTick - 10, function()
		Media.DisplayMessage("Before the window, B83 reads: "
			.. Test.GetSupportPowerState(USA, "B83Strike"), "DEAD HAND")
	end)

	Trigger.AfterDelay(WindowOpensTick + 5, function()
		Media.DisplayMessage("Window open. B83 now reads: "
			.. Test.GetSupportPowerState(USA, "B83Strike")
			.. " -- watch the countdown band.", "DEAD HAND")
	end)

	-- USA places. Russia deliberately does not.
	Trigger.AfterDelay(WindowOpensTick + 15, function()
		local status = Test.ActivateSupportPower(USA, "B83Strike", CPos.New(AimPoint.X, AimPoint.Y))
		Media.DisplayMessage("USA places its B83 on the eastern city: " .. status, "DEAD HAND")
	end)
end
