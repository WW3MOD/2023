-- AUTO TEST: force-moving ONE garrisoned soldier takes him, and only him, out of the building.
--
-- The user's action, verbatim: select a garrisoned soldier, force-move him, and he should leave
-- the building in the direction ordered and walk to the destination — instead of having to
-- evacuate the whole garrison to get one man out.
--
-- BOTH HALVES ARE THE ASSERTION, and the second is what makes the first mean anything: ejecting
-- everybody would satisfy "R1 arrived" perfectly well. So R2 and R3 are measured too, and the run
-- fails if either of them ends up outside the tower.
--
-- EXPECTED RED on a build without the fix, and it is a specific one rather than a generic timeout:
-- R1 gets a real ForceMove order today — MoveOrderTargeter accepts the click regardless of pause
-- (Mobile.cs:1174) — and Mobile queues a Move activity that then never advances, because Mobile
-- carries PauseOnCondition: garrisoned-at-port (infantry.yaml:53) and nothing revokes it. So he
-- sits at the tower cell with a pending move and the run reports "R1 never left the tower cell",
-- with the census naming the state he was actually found in.

local Destination = { X = 24, Y = 18 }   -- due WEST of the tower at 33,18, on clear ground
local ArriveWithin = 20                  -- seconds allowed for the walk once the order is issued
local ArriveTolerance = 2                -- cells; Move stops "near enough", it does not land exactly

local FirstExitCell = nil                -- latched the first tick R1 is in world and off the tower cell

local function CellsApart(a, b)
	local dx, dy = a.X - b.X, a.Y - b.Y
	return math.floor(math.sqrt((dx * dx) + (dy * dy)))
end

local function OnTowerCell(soldier)
	-- DeployToPort does SetPosition(soldier, self.Location), so a soldier manning a port occupies
	-- the BUILDING's own cell while in-world. That is what "still inside" looks like positionally.
	return soldier.Location.X == Tower.Location.X and soldier.Location.Y == Tower.Location.Y
end

-- "Outside" is the only unambiguous state here, and it is deliberately the one the verdict rests on.
--
-- PITFALL: IsDead reads TRUE for a soldier sitting in a Cargo hold, so a shelter occupant cannot be
-- told apart from a casualty by these properties (DOCS/recipes/AUTOTEST.md). That ambiguity is fine
-- for us only because we never assert on the shelter count — we assert that R2/R3 are NOT out
-- walking around, which is a state both of those readings exclude.
local function IsOutside(soldier)
	return not soldier.IsDead and soldier.IsInWorld and not OnTowerCell(soldier)
end

local function Census(label, squad)
	local atPorts, goneOrSheltered, outside = 0, 0, 0
	for _, s in ipairs(squad) do
		if s.IsDead or not s.IsInWorld then
			goneOrSheltered = goneOrSheltered + 1
		elseif OnTowerCell(s) then
			atPorts = atPorts + 1
		else
			outside = outside + 1
		end
	end

	return label .. ": " .. atPorts .. " on the tower cell, " .. goneOrSheltered ..
		" in shelter or dead, " .. outside .. " outside"
end

WorldLoaded = function()
	local Squad = { R1, R2, R3 }
	local Others = { R2, R3 }

	TestHarness.FocusBetween(Tower, Enemy)

	for _, s in ipairs(Squad) do
		s.EnterTransport(Tower)
	end

	-- 10s to walk in and man the ports, matching the sibling garrison scenario's budget.
	Trigger.AfterDelay(250, function()
		if R1.IsDead then
			Test.Fail("R1 died before he could be ordered out — " .. Census("squad", Squad))
			return
		end

		if not R1.IsInWorld then
			-- He is in the SHELTER, not at a port. This scenario deliberately covers only the
			-- port case: a shelter soldier is out of the world, so he is not selectable and not
			-- clickable, and force-move cannot reach him at all. Skip rather than fail — the
			-- feature is not claimed to work for him.
			Test.Skip("R1 ended up in the tower's shelter rather than at a port, so there was no " ..
				"in-world soldier to force-move — " .. Census("squad", Squad))
			return
		end

		-- Selectability is a PRECONDITION of the whole interaction, so measure it rather than
		-- assuming it. A garrisoned port soldier is added back to the world by DeployToPort and
		-- his Selectable trait carries no garrisoned-at-port gate, so the selection system should
		-- take him. If that ever stops being true this fails here, loudly, instead of surfacing as
		-- a confusing "the order did nothing" further down.
		--
		-- Deliberately the single-actor UserInterface.Select path rather than Test.SelectActors:
		-- one man is all this needs, and that path is exercised by scenarios across the corpus,
		-- so a count of 0 here points at the soldier rather than at an untried binding. Which
		-- matters, because a harness failure at this gate would otherwise read as the finding
		-- "garrisoned soldiers cannot be selected" — an absence manufactured by the instrument.
		TestHarness.Select(R1)
		if Test.GetSelectedCount() ~= 1 then
			Test.Fail("a garrisoned soldier at a port could not be selected (selected count " ..
				Test.GetSelectedCount() .. "). Either UserInterface.Select rejected an in-world " ..
				"port soldier — which would make the eject half of this feature unreachable — or " ..
				"the harness failed to select at all; check a sibling scenario's selection before " ..
				"reading this as a garrison finding. " .. Census("squad", Squad))
			return
		end

		-- The gesture under test: Ctrl+click on open ground west of the tower.
		Test.IssueMove(R1, CPos.New(Destination.X, Destination.Y), true, false)

		TestHarness.AssertWithin(ArriveWithin, function()
			if R1.IsDead then
				return "R1 died after being ordered out, before reaching the destination — " ..
					Census("squad", Squad)
			end

			-- Half two, checked every tick rather than once at the end: nobody else may leave.
			for _, s in ipairs(Others) do
				if IsOutside(s) then
					return "force-moving ONE soldier emptied more of the garrison than it should " ..
						"have — a soldier who was never ordered out is now outside the tower. " ..
						Census("squad", Squad)
				end
			end

			if R1.IsInWorld and not OnTowerCell(R1) and FirstExitCell == nil then
				FirstExitCell = { X = R1.Location.X, Y = R1.Location.Y }
			end

			if not R1.IsInWorld then
				return false
			end

			if CellsApart(R1.Location, Destination) > ArriveTolerance then
				return false
			end

			-- Arrived. The last thing to check is the direction he left by: the order was due WEST,
			-- so the cell he first stepped onto must be west of the tower. Without this a build that
			-- ejected him out of the far side and let the pathfinder walk him round the building
			-- would pass, and "he exits in the direction we told him to go" would be untested.
			if FirstExitCell == nil then
				return "R1 reached the destination but was never observed leaving the tower cell, " ..
					"so the exit side could not be read — " .. Census("squad", Squad)
			end

			if FirstExitCell.X >= Tower.Location.X then
				return "R1 arrived, but left by the wrong side: first cell outside the tower was " ..
					FirstExitCell.X .. "," .. FirstExitCell.Y .. " and the tower is at " ..
					Tower.Location.X .. "," .. Tower.Location.Y .. " with the order pointing west"
			end

			return true
		end, "R1 never left the tower cell and reached the destination within " .. ArriveWithin ..
			"s of being force-moved")
	end)
end
