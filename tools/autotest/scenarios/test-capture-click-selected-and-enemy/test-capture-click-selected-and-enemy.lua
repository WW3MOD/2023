-- AUTO TEST: the capture-dispatch right-click must survive the target being SELECTED, must reach
-- ENEMY structures, and must still yield to a selection that can answer the click itself.
--
-- THE BUG THIS PINS. The gesture used to be gated on `world.Selection.Actors.Count == 0`, so the
-- most natural way a player expresses "that building" -- click it, then right-click it -- was
-- exactly the way that did nothing. The gate is now the number of orders the selection RESOLVED.
-- Selecting a structure the local player does not own resolves zero orders, because
-- UnitOrderGenerator.OrderForUnit returns null for any actor the player does not own, so the
-- selected-structure case and the nothing-selected case now reach the dispatcher identically.
--
-- WHY THAT ALSO FIXES ENEMY STRUCTURES. Nothing in CaptureDispatchManager ever filtered on the
-- target's owner: CaptureManager.CanTarget already admits an enemy-held building for a technician
-- (its Captures trait has ValidRelationships Neutral|Enemy, and every structure carries an
-- unconditional Capturable@neutral offering building-neutral). The empty-selection gate was the
-- whole of the restriction in practice.
--
-- WHAT Test.CaptureClick MEASURES. It resolves the click through UnitOrderGenerator.DispatchOrders
-- -- the same function the mouse reaches -- and returns the number of capture-dispatch orders
-- issued. It deliberately does NOT call CaptureDispatchManager.DispatchAt directly: that would
-- bypass the selection gate, which is the entire subject of this test.
--
-- ORDERING IS LOAD-BEARING. A successful dispatch COMMITS the technician it picked, and a committed
-- technician is correctly unavailable to the next click. So the negative assertion runs FIRST,
-- while all three technicians are still free -- otherwise a 0 from it would be indistinguishable
-- from "everybody was busy", and the test would pass for the wrong reason.
--
-- ------------------------------------------------------------------------------------------------
-- WHY THE FIRST RUN FAILED (2026-09-03), and it was this file rather than the code.
--
-- The gate fix worked: all three clicks issued their orders. The verdict was "dispatched at but
-- never captured", which reads like a defect in the capture activity and was reported as one. It
-- was the BUDGET. Derricks were 20 cells away and the allowance was 900 ticks; at ^Infantry Speed
-- 25 (=> 40.96 ticks per cell) a 19-cell approach alone is 778 ticks, and with CaptureDelay 20 and
-- the move into the target the technicians needed ~860. They were still walking.
--
-- Two changes came out of it. The gaps are now 6 cells against a 600-tick budget (a 2.1x margin
-- instead of 4%), and the final check no longer returns on the first unowned derrick: it reports
-- ALL THREE, because "one derrick failed" and "none of them completed" are different diagnoses and
-- the first version could not tell them apart.

-- Budgeted in TICKS deliberately. TestHarness.TicksPerSecond is 25 while the game runs at 16.67 and
-- is documented as left that way on purpose (test-helpers.lua:16-25), so a "seconds" budget
-- silently buys 1.5x more real time than it reads.
--
-- 6-cell gap => 5 cells of approach = 205 ticks, + CaptureDelay 20, + entering ~60 = ~285.
-- NOTE the delay is 20 and not 500: 500 is ^CapturesOccupiedBuildings, the SOLDIER clearing an
-- enemy building. A technician carries only ^CapturesNeutralBuildings, and
-- ValidCapturesWithLowestSabotageThreshold picks from the CAPTOR's own Captures traits, so a
-- technician takes an enemy derrick on the same 20-tick path it takes a neutral one.
local SettleTicks = 600

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")

	TestHarness.FocusBetween(TechA, DerrickNeutral)

	-- (0) NEGATIVE, and it must run while every technician is still free. A conscript selected on an
	-- enemy building resolves its own order -- it clears the building -- so the dispatch has to stand
	-- down. This is what keeps capture from competing with attack without needing a modifier key:
	-- dispatch only ever fills a silence.
	local yielded = Test.CaptureClick({ Conscript }, DerrickEnemyA)
	if yielded ~= 0 then
		Test.Fail("[cc] fail: a conscript was selected on an enemy derrick and the dispatch still " ..
			"issued " .. yielded .. " capture order(s). The selection resolved an order of its own, " ..
			"so dispatch must stand down -- otherwise right-clicking an enemy building with an army " ..
			"selected silently becomes a capture instead of an attack")
		return
	end

	-- (1) ENEMY structure, nothing selected.
	local enemyUnselected = Test.CaptureClick({}, DerrickEnemyA)
	if enemyUnselected ~= 1 then
		Test.Fail("[cc] fail: right-clicking an ENEMY derrick with nothing selected issued " ..
			enemyUnselected .. " capture orders, expected exactly 1. A technician can legally " ..
			"capture an enemy-held building, so 0 here means the dispatcher is filtering on the " ..
			"target's owner somewhere it should not be")
		return
	end

	-- (2) ENEMY structure, and that structure SELECTED. Both halves of the report at once.
	local enemySelected = Test.CaptureClick({ DerrickEnemyB }, DerrickEnemyB)
	if enemySelected ~= 1 then
		Test.Fail("[cc] fail: selecting an ENEMY derrick and right-clicking it issued " ..
			enemySelected .. " capture orders, expected exactly 1. The selection holds one actor, " ..
			"but it is an actor the player does not own, so it resolves no orders and the click " ..
			"must reach the dispatcher")
		return
	end

	-- (3) NEUTRAL structure, selected. The reported bug in its original form.
	local neutralSelected = Test.CaptureClick({ DerrickNeutral }, DerrickNeutral)
	if neutralSelected ~= 1 then
		Test.Fail("[cc] fail: selecting a NEUTRAL derrick and right-clicking it issued " ..
			neutralSelected .. " capture orders, expected exactly 1. This is the exact gesture the " ..
			"old empty-selection gate swallowed; a 0 here means the gate is back")
		return
	end

	TestHarness.Screenshot("cc-dispatched",
		"three technicians hold capture orders; the conscript click issued none")

	Trigger.AfterDelay(SettleTicks, function()
		TestHarness.Screenshot("cc-settled", "all three derricks should now be USA-owned")

		-- End-to-end: the orders the clicks produced actually run and actually capture. Weaker than
		-- the counts above, and it only catches a dispatch that issues a well-formed order nobody
		-- can carry out.
		--
		-- ALL THREE are reported rather than returning on the first, because the shape of the
		-- failure is the diagnosis. Three unowned derricks means the budget or the walk; ONE unowned
		-- derrick -- especially the enemy one with the neutral one taken -- means the capture path
		-- treats enemy targets differently, which is a real defect.
		local missed = {}
		if DerrickEnemyA.Owner ~= USA then
			missed[#missed + 1] = "DerrickEnemyA(enemy, clicked unselected)=" .. tostring(DerrickEnemyA.Owner.Name)
		end

		if DerrickEnemyB.Owner ~= USA then
			missed[#missed + 1] = "DerrickEnemyB(enemy, clicked while selected)=" .. tostring(DerrickEnemyB.Owner.Name)
		end

		if DerrickNeutral.Owner ~= USA then
			missed[#missed + 1] = "DerrickNeutral(neutral, clicked while selected)=" .. tostring(DerrickNeutral.Owner.Name)
		end

		if #missed > 0 then
			Test.Fail("[cc] fail: " .. #missed .. " of 3 derricks were dispatched at but never " ..
				"captured within " .. SettleTicks .. " ticks: " .. table.concat(missed, ", ") ..
				". All three clicks issued their orders, so the dispatch gate is fine and the " ..
				"question is downstream. A 6-cell approach needs ~285 ticks at 40.96 ticks/cell, so " ..
				"the budget should not be the explanation this time -- if all three are listed, " ..
				"suspect the walk anyway (blocked path, wrong spawn cells); if only the ENEMY ones " ..
				"are listed while the neutral one succeeded, the capture path really does treat " ..
				"enemy-held targets differently and that is the defect to chase")
			return
		end

		Test.Pass("[cc] the dispatch reached a selected neutral derrick, a selected enemy derrick " ..
			"and an unselected enemy derrick, stood down for a conscript that could answer the " ..
			"click itself, and all three derricks were captured")
	end)
end
