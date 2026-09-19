-- AUTO TEST: a World is constructed, finishes loading, and ticks.
--
-- This asserts the narrowest possible thing on purpose. Reaching WorldLoaded means every
-- trait on the world actor was constructed and every INotifyCreated.Created ran without
-- throwing; ticking past that means the sim is actually running. That is the entire bug
-- class: on 2026-09-10 DefconWall threw a NullReferenceException in Created, no match would
-- start at all, and six static gates stayed green because none of them builds a World.
--
-- There is nothing about gameplay here and there should not be. If this scenario ever grows
-- an assertion about a unit, a weapon or a bot, it has stopped being a smoke gate.

local SmokeTicks = 30

WorldLoaded = function()
	-- If world construction threw, this function is never reached, no verdict is written,
	-- and the harness watchdog reports TIMEOUT-FAIL or CRASH. Silence is the failure mode,
	-- which is exactly why the runner has a watchdog and why `make.ps1 smoke` must never
	-- read "no output" as success.
	Trigger.AfterDelay(SmokeTicks, function()
		Test.Pass("world constructed, loaded and ticked " .. SmokeTicks .. " times")
	end)
end
