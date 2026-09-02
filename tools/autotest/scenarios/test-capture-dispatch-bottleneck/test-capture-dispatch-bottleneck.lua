-- AUTO TEST: dispatching technicians at several structures at once must minimise the LAST
-- capture, not the first one.
--
-- THE MECHANISM. Technicians walk in parallel, so the time until every selected structure is
-- taken is the SINGLE LONGEST walk, not the sum of the walks. CaptureDispatchMath.Assign
-- minimises that longest walk exactly (bottleneck assignment). The intuitive alternative --
-- repeatedly commit the globally closest technician/structure pair -- optimises the sum and
-- can strand a technician, which is what this scenario is here to catch.
--
-- THE GEOMETRY, and why the observable is an IDENTITY rather than a stopwatch. Distances in
-- cells, all four actors on row 16 so walking distance and straight-line distance agree:
--
--     TechFar  -> DerrickNear = 14     TechFar  -> DerrickFar = 38
--     TechNear -> DerrickNear =  2     TechNear -> DerrickFar = 26
--
-- Greedy grabs TechNear -> DerrickNear because 2 is the smallest number on the board, and by
-- doing so hands TechFar a 38-cell walk. The correct pairing sends TechFar -- the one that
-- starts FURTHER from DerrickNear -- at DerrickNear anyway, because TechNear is the only one
-- that can reach DerrickFar without a trek, and the last derrick then lands at 26 instead
-- of 38.
--
-- BOTH ARMS EVENTUALLY CAPTURE BOTH DERRICKS. A scenario asserting only "both captured" would
-- go green against the greedy implementation and prove nothing whatever. So the verdict is
-- taken on WHICH technician was sent WHERE, read straight off the activity queue.
--
-- WHY THE IDENTITY IS READ EARLY. A successful capture CONSUMES the technician
-- (^CapturesNeutralBuildings sets ConsumedByCapture: true, infantry.yaml:930), so by the time
-- the derricks change hands there is no technician left to ask. The identity check therefore
-- runs while both are still walking; the ownership check at the end is a separate, weaker
-- statement that the orders actually execute.

-- Budgeted in TICKS deliberately. TestHarness.TicksPerSecond is 25 while the game runs at
-- 16.67, and is documented as left that way on purpose (test-helpers.lua:16-25) -- so a
-- "seconds" budget silently buys 1.5x more real time than it reads. Ticks round-trip exactly.
local IdentityAtTick = 15          -- orders resolve on tick 1-2; nobody has arrived anywhere
local SettleTicks = 1400           -- ~84 s real. TechNear's 26-cell walk is the long pole.

local function Where(actor)
	if actor.IsDead then
		return "(dead)"
	end

	return "(" .. actor.Location.X .. "," .. actor.Location.Y .. ")"
end

local function DispatchedAt(tech)
	if Test.IsCommittedTo(tech, DerrickNear) then
		return "DerrickNear"
	end

	if Test.IsCommittedTo(tech, DerrickFar) then
		return "DerrickFar"
	end

	if Test.CommittedCaptureTarget(tech) ~= 0 then
		return "something that is neither derrick"
	end

	return "nothing"
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")

	TestHarness.FocusBetween(TechFar, TechNear)

	-- The gesture under test: both structures selected, Deploy pressed. Test.DispatchCapture is
	-- the same CaptureDispatchManager.DispatchAcross call the F key reaches through
	-- CommandBarLogic, so this exercises the assignment and the order issuing, but NOT the key
	-- binding or the selection filter -- those have no seam a scenario can reach and are checked
	-- by hand in game.
	local issued = Test.DispatchCapture({ DerrickNear, DerrickFar })

	if issued ~= 2 then
		Test.Fail("[cdb] setup invalid: the dispatch issued " .. issued .. " capture orders, " ..
			"expected exactly 2 (one per technician). With a different number of orders the " ..
			"identity check below is not measuring the assignment at all")
		return
	end

	Trigger.AfterDelay(IdentityAtTick, function()
		if TechFar.IsDead or TechNear.IsDead then
			Test.Fail("[cdb] setup invalid: a technician died before the identity check")
			return
		end

		TestHarness.Screenshot("cdb-dispatched",
			"both technicians hold a capture order; verdict on which one is taken now")

		-- THE VERDICT. Greedy produces exactly the mirror image of this, so the failure message
		-- names both what happened and what it means.
		local farGot = DispatchedAt(TechFar)
		local nearGot = DispatchedAt(TechNear)

		if farGot ~= "DerrickNear" or nearGot ~= "DerrickFar" then
			Test.Fail("[cdb] fail: TechFar was sent at " .. farGot .. " and TechNear at " ..
				nearGot .. ". Expected TechFar -> DerrickNear (14 cells) and TechNear -> " ..
				"DerrickFar (26 cells), so the LAST derrick is taken at 26. The pairing " ..
				"reported here is what nearest-first greedy produces: it takes TechNear -> " ..
				"DerrickNear because 2 is the smallest distance on the board, and strands " ..
				"TechFar with a 38-cell walk. Technicians move in parallel, so that is a " ..
				"strictly worse answer to 'capture both as fast as possible'")
			return
		end

		Trigger.AfterDelay(SettleTicks, function()
			TestHarness.Screenshot("cdb-settled", "both derricks should now be player-owned")

			-- Weaker end-to-end statement: the orders the assignment produced actually run and
			-- actually capture. This cannot distinguish the two algorithms and is not trying to.
			if DerrickNear.Owner ~= USA then
				Test.Fail("[cdb] fail: the near derrick was never captured -- it is still owned " ..
					"by " .. tostring(DerrickNear.Owner.Name) .. ". The assignment was correct " ..
					"at tick " .. IdentityAtTick .. ", so the order was issued but did not " ..
					"complete: look at the capture activity, not at the dispatch")
				return
			end

			if DerrickFar.Owner ~= USA then
				Test.Fail("[cdb] fail: the far derrick was never captured -- it is still owned " ..
					"by " .. tostring(DerrickFar.Owner.Name) .. ". TechNear was correctly sent " ..
					"at it and had " .. SettleTicks .. " ticks for a 26-cell walk, so either " ..
					"the walk was interrupted or the budget is too tight. TechNear is at " ..
					Where(TechNear))
				return
			end

			Test.Pass("[cdb] the dispatch sent the further technician at the near derrick so the " ..
				"nearer one could take the far one, and both derricks were captured")
		end)
	end)
end
