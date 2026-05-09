-- AUTO TEST: paused production item must NOT build.
--
-- Phase 1 (paused): queue an e3.america, immediately pause it, wait 250 ticks
-- (~10s, much longer than the e3 build time). Assert no e3.america spawned.
-- If the bug exists ("parallel queues build paused units"), the unit appears
-- and we fail RED.
--
-- Phase 2 (unpaused control): unpause, wait another 250 ticks. Assert one
-- e3.america did spawn — proves the queue was capable of producing and our
-- assertion in phase 1 wasn't merely catching a stalled queue.

WorldLoaded = function()
	TestHarness.FocusBetween(OwnSR)

	local usa = Player.GetPlayer("USA")
	if usa == nil then
		Test.Fail("USA player not found")
		return
	end

	-- Plenty of cash so production never stalls on funds.
	usa.Cash = 100000

	-- Give the queue a tick to discover the SR's Production trait (Enabled=true)
	-- before queueing.
	Trigger.AfterDelay(5, function()
		Test.QueueProduction(usa, "e3.america", 1)
		Test.PauseProduction(usa, "e3.america", true)

		-- Phase 1: paused → must NOT build within 250 ticks (10s).
		Trigger.AfterDelay(250, function()
			local pausedUnits = usa.GetActorsByType("e3.america")
			if #pausedUnits > 0 then
				Test.Fail("fail: " .. #pausedUnits ..
					" e3.america spawned despite item being paused (bug confirmed)")
				return
			end

			local remaining = Test.GetQueueRemainingTime(usa, "e3.america")
			if remaining <= 0 then
				Test.Fail("fail: paused queue item missing or RemainingTime=0 " ..
					"(remaining=" .. tostring(remaining) .. ")")
				return
			end

			-- Phase 2: unpause → must build within next 250 ticks. Sanity check
			-- that the queue mechanics actually work in this test setup, so
			-- a phase-1 false-positive (stall on cash, missing prereqs) gets
			-- caught here.
			Test.PauseProduction(usa, "e3.america", false)

			Trigger.AfterDelay(250, function()
				local builtUnits = usa.GetActorsByType("e3.america")
				if #builtUnits == 0 then
					Test.Fail("fail: unpaused e3.america did not spawn within 10s — " ..
						"queue stalled, test setup is invalid (not the pause bug)")
					return
				end

				Test.Pass()
			end)
		end)
	end)
end
