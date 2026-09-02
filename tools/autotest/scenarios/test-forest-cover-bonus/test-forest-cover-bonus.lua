-- Do LIVING trees grant the in-cover concealment bonus?
--
-- Before the change under test, `object-proximity` had exactly ONE emitter in the whole mod:
-- ProximityExternalCondition@ObjectProximity on ^TreeHusk (husks.yaml:155-158) -- a BURNT tree.
-- A living tree produced forest shadow but no cover bonus at all, which inverts the player's
-- instinct. The change adds the emitter to ^Tree (decoration.yaml) at Range 1024.
--
-- =====================================================================================
-- WHAT THIS ASSERTS, AND WHY IT IS A DELTA
-- =====================================================================================
-- Four riflemen, identical, map-placed, never ordered. Only the number of living trees in
-- reach differs. The receiver is ^DetectableInfantryStandard (infantry.yaml:758-770):
--
--   object-proximity == 1  ->  VisionModifier +1
--   object-proximity == 2  ->  VisionModifier +2
--   object-proximity >= 3  ->  VisionModifier +3   (ExternalCondition TotalCap: 3)
--
-- so the expectation is a clean +1 / +2 / +3 over a treeless control standing in the same
-- posture on the same row.
--
-- The absolute tier is deliberately NOT asserted. Whether a never-moved rifleman carries
-- `prone` is contested in tree: InfantryStates (infantry.yaml:315-317, reached by E1.america
-- through ^E1 -> ^CamoSoldier) grants it on `!moving`, and Actor.cs:283-284 runs every
-- variable observer once with the initial condition state, which reads as tier 4; but
-- test-visual-gauge-truth and test-visual-concealment-gauge both assert tier 3 for that same
-- unit. Only a launch settles it. A delta is true under either answer, and the control tier
-- is printed so this run reports which one is right.
--
-- =====================================================================================
-- HOW TO READ THE RESULT
-- =====================================================================================
--   pass                     living trees grant cover, at the bonus tiers the table predicts.
--   fail "+0 with N trees"   the emitter is not reaching the rifleman at all. Either the
--                            Inherits@Cover line is absent from ^Tree, or Range is too small
--                            to clear the 724-unit Building centre offset (see map.yaml).
--                            This is the exact failure the pre-change tree produces, and the
--                            exact failure Range: 384 (the husk's radius) would produce.
--   fail "expected +2, got +3" / similar
--                            the radius is reaching FURTHER than intended and a lane is
--                            picking up a tree meant for its neighbour, or the TotalCap on
--                            the receiver is not holding.
--   fail "SETUP"             the map is not the map this scenario believes in; the tier
--                            numbers below mean nothing until that is fixed.

local Lanes = {
	{ actor = nil, name = "Cover1", trees = 1, expected = 1 },
	{ actor = nil, name = "Cover2", trees = 2, expected = 2 },
	{ actor = nil, name = "Cover3", trees = 3, expected = 3 },
}

local ControlCell = { x = 10, y = 16 }

WorldLoaded = function()
	Lanes[1].actor = Cover1
	Lanes[2].actor = Cover2
	Lanes[3].actor = Cover3

	-- Give the proximity triggers a tick to register and grant. ProximityExternalCondition
	-- registers in AddedToWorld and the ActorMap only calls onEntry from its own
	-- TickFunction, so nothing is granted on frame zero.
	Trigger.AfterDelay(DateTime.Seconds(2), function()
		-- ---- SETUP checks: is this the map the numbers assume? ----------------------
		if Control.IsDead then
			Test.Fail("SETUP: the control rifleman is dead — nothing on this map should be " ..
				"able to shoot him, so the scenario is not the one described")
			return
		end

		local controlDensity = Test.GetDensity(CPos.New(ControlCell.x, ControlCell.y))
		if controlDensity ~= 0 then
			Test.Fail("SETUP: the control cell reports terrain density " ..
				tostring(controlDensity) .. ", not 0 — there is scenery next to the lane " ..
				"that is supposed to be bare, so it is not a control")
			return
		end

		local control = Test.GetVisibilityLevel(Control)
		if control < 1 then
			Test.Fail("SETUP: Test.GetVisibilityLevel returned " .. tostring(control) ..
				" for the control rifleman — the harness is not in test mode, or the unit " ..
				"carries no Detectable trait")
			return
		end

		-- The baseline must be one of the two candidate answers to the prone question. Any
		-- other value means something else is modifying this unit and the deltas below are
		-- being measured against a moving target.
		if control ~= 3 and control ~= 4 then
			Test.Fail("SETUP: control rifleman is on tier " .. tostring(control) ..
				", expected 3 (no prone) or 4 (prone). Something is modifying him that this " ..
				"scenario does not know about — rank, firing, movement or suppression")
			return
		end

		print("[cover] control tier = " .. tostring(control) ..
			" (3 => a never-moved rifleman is NOT prone; 4 => he is)")

		-- ---- The measurement -------------------------------------------------------
		local failures = 0
		for i = 1, #Lanes do
			local lane = Lanes[i]

			if lane.actor.IsDead then
				Test.Fail("SETUP: " .. lane.name .. " is dead")
				return
			end

			local tier = Test.GetVisibilityLevel(lane.actor)
			local delta = tier - control

			print("[cover] " .. lane.name .. ": " .. tostring(lane.trees) .. " tree(s), tier " ..
				tostring(tier) .. ", delta +" .. tostring(delta) ..
				", expected +" .. tostring(lane.expected))

			if delta ~= lane.expected then
				failures = failures + 1
				if delta == 0 then
					Test.Fail(lane.name .. " gained +0 with " .. tostring(lane.trees) ..
						" living tree(s) in reach (tier " .. tostring(tier) .. " vs control " ..
						tostring(control) .. "). Living trees are granting no cover at all: " ..
						"either ^Tree does not inherit ^TreeCover, or Range is below the " ..
						"724-unit offset from a 2x2 tree's Building centre to its own cells")
				else
					Test.Fail(lane.name .. " gained +" .. tostring(delta) .. " with " ..
						tostring(lane.trees) .. " living tree(s) in reach, expected +" ..
						tostring(lane.expected) .. " (tier " .. tostring(tier) ..
						" vs control " .. tostring(control) .. "). The bonus exists but the " ..
						"radius is not the one this map's geometry was laid out for")
				end
				return
			end
		end

		if failures == 0 then
			Test.Pass("living trees grant cover: +1/+2/+3 for 1/2/3 trees in reach, " ..
				"measured against a treeless control on tier " .. tostring(control))
		end
	end)
end
