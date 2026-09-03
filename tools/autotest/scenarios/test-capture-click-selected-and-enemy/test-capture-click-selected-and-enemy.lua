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
-- (the technician's Captures trait has ValidRelationships Neutral|Enemy, and every structure
-- carries an unconditional Capturable@neutral offering building-neutral). The empty-selection gate
-- was the whole of the restriction in practice, because a player looking at an enemy building
-- virtually always has something selected.
--
-- WHAT Test.CaptureClick MEASURES. It resolves the click through UnitOrderGenerator.DispatchOrders
-- -- the same function the mouse reaches -- and returns the number of capture-dispatch orders
-- issued. It deliberately does NOT call CaptureDispatchManager.DispatchAt directly: that would
-- bypass the selection gate, which is the entire subject of this test, and would go green no
-- matter what the gate said.
--
-- ORDERING IS LOAD-BEARING. A successful dispatch COMMITS the technician it picked, and a
-- committed technician is correctly unavailable to the next click. So the negative assertion runs
-- FIRST, while all three technicians are still free -- otherwise a 0 from it would be
-- indistinguishable from "everybody was busy", and the test would pass for the wrong reason.

-- Budgeted in TICKS deliberately. TestHarness.TicksPerSecond is 25 while the game runs at 16.67
-- and is documented as left that way on purpose (test-helpers.lua:16-25), so a "seconds" budget
-- silently buys 1.5x more real time than it reads. Ticks round-trip exactly.
local SettleTicks = 900            -- ~54 s real; a 20-cell walk plus the 20-tick neutral capture.

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")

	TestHarness.FocusBetween(TechB, DerrickEnemyB)

	-- (0) NEGATIVE, and it must run while every technician is still free. A conscript selected on
	-- an enemy building resolves its own order -- it clears the building -- so the dispatch has to
	-- stand down. This is what keeps capture from competing with attack without needing a modifier
	-- key: dispatch only ever fills a silence.
	local yielded = Test.CaptureClick({ Conscript }, DerrickEnemyA)
	if yielded ~= 0 then
		Test.Fail("[cc] fail: a conscript was selected on an enemy derrick and the dispatch still " ..
			"issued " .. yielded .. " capture order(s). The selection resolved an order of its " ..
			"own, so dispatch must stand down -- otherwise right-clicking an enemy building with " ..
			"an army selected silently becomes a capture instead of an attack")
		return
	end

	-- (1) ENEMY structure, nothing selected. This is the arm that was already expected to work and
	-- is asserted so a regression here is not mistaken for the selection fix below failing.
	local enemyUnselected = Test.CaptureClick({}, DerrickEnemyA)
	if enemyUnselected ~= 1 then
		Test.Fail("[cc] fail: right-clicking an ENEMY derrick with nothing selected issued " ..
			enemyUnselected .. " capture orders, expected exactly 1. A technician can legally " ..
			"capture an enemy-held building (Captures.ValidRelationships is Neutral|Enemy and " ..
			"every structure carries an unconditional Capturable@neutral), so 0 here means the " ..
			"dispatcher is filtering on the target's owner somewhere it should not be")
		return
	end

	-- (2) ENEMY structure, and that structure SELECTED. Both halves of the user's report at once.
	local enemySelected = Test.CaptureClick({ DerrickEnemyB }, DerrickEnemyB)
	if enemySelected ~= 1 then
		Test.Fail("[cc] fail: selecting an ENEMY derrick and right-clicking it issued " ..
			enemySelected .. " capture orders, expected exactly 1. This is the reported bug in " ..
			"its enemy form: the selection holds one actor, but it is an actor the player does " ..
			"not own, so it resolves no orders and the click must reach the dispatcher")
		return
	end

	-- (3) NEUTRAL structure, selected. The reported bug in its original form.
	local neutralSelected = Test.CaptureClick({ DerrickNeutral }, DerrickNeutral)
	if neutralSelected ~= 1 then
		Test.Fail("[cc] fail: selecting a NEUTRAL derrick and right-clicking it issued " ..
			neutralSelected .. " capture orders, expected exactly 1. This is the exact gesture " ..
			"the old empty-selection gate swallowed; a 0 here means the gate is back")
		return
	end

	TestHarness.Screenshot("cc-dispatched",
		"three technicians hold capture orders; the conscript click issued none")

	Trigger.AfterDelay(SettleTicks, function()
		TestHarness.Screenshot("cc-settled", "all three derricks should now be USA-owned")

		-- End-to-end: the orders the clicks produced actually run and actually capture. Weaker
		-- than the counts above and not trying to distinguish anything -- it only catches a
		-- dispatch that issues a well-formed order nobody can carry out.
		local function CheckCaptured(structure, label)
			if structure.Owner ~= USA then
				Test.Fail("[cc] fail: " .. label .. " was dispatched at but never captured -- it " ..
					"is still owned by " .. tostring(structure.Owner.Name) .. ". The click " ..
					"issued its order, so look at the capture activity rather than at the " ..
					"dispatch gate")
				return false
			end

			return true
		end

		if not CheckCaptured(DerrickEnemyA, "the enemy derrick clicked with nothing selected") then return end
		if not CheckCaptured(DerrickEnemyB, "the enemy derrick clicked while selected") then return end
		if not CheckCaptured(DerrickNeutral, "the neutral derrick clicked while selected") then return end

		Test.Pass("[cc] the dispatch reached a selected neutral derrick, a selected enemy derrick " ..
			"and an unselected enemy derrick, stood down for a conscript that could answer the " ..
			"click itself, and all three derricks were captured")
	end)
end
