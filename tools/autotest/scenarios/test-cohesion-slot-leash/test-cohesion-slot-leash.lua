-- AUTOTEST: CohesionSlotMemory integration with CohesionMoveModifier.
--
-- Step 1: issue a grouped Move on a 4-unit squad. CohesionMoveModifier should
-- call CohesionSlotMemory.Assign(slot, tick) on each subject.
-- Step 2: query Test.GetCohesionSlot(actor) for each unit; verify a slot is
-- recorded and is a sensible cell near the click target.
--
-- This is the state-level test for the Phase 4 v1 leash. The actual return-
-- to-slot behavior under displacement runs continuously in the engine
-- (INotifyIdle + INotifyBlockingMove) — easier validated visually in-game
-- than in the autotest harness, where timing depends on bot AI activity,
-- pathfinder details, and Move activity sequencing.

local DeadlineSeconds = 10

WorldLoaded = function()
	local squad = { A1, A2, A3, A4 }

	TestHarness.FocusBetween(A1, A2, A3, A4)

	-- Before the cohesion order, no slot should be set.
	for _, u in ipairs(squad) do
		local s = Test.GetCohesionSlot(u)
		if s.X ~= 0 or s.Y ~= 0 then
			Test.Fail(string.format("%s already has a slot (%d,%d) before order", tostring(u), s.X, s.Y))
			return
		end
	end

	-- Issue the cohesion order.
	Test.GroupMove({ A1, A2, A3, A4 }, CPos.New(20, 15))

	-- Wait a few ticks for the order to round-trip through the network layer
	-- and the modifier to fire.
	Trigger.AfterDelay(10, function()
		local missing = {}
		for _, u in ipairs(squad) do
			local s = Test.GetCohesionSlot(u)
			if s.X == 0 and s.Y == 0 then
				missing[#missing + 1] = tostring(u)
			else
				-- Slots must be reasonably close to the click target (20, 15).
				-- The Loose-box layout for n=4 puts slots within ~3 cells.
				local dx = math.abs(s.X - 20)
				local dy = math.abs(s.Y - 15)
				if dx > 4 or dy > 4 then
					missing[#missing + 1] = string.format("%s: slot (%d,%d) far from click",
						tostring(u), s.X, s.Y)
				end
				print(string.format("[leash] %s slot=(%d,%d)", tostring(u), s.X, s.Y))
			end
		end

		if #missing == 0 then
			Test.Pass("all 4 squad members have leash slots assigned")
		else
			Test.Fail("missing/bad slots: " .. table.concat(missing, "; "))
		end
	end)
end
