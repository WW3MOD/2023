-- AUTO TEST: Group Scatter (Shift-G) must replay the cell the PLAYER CLICKED for an
-- attack-move, not the per-unit cell that click was relocated to.
--
-- THE MECHANISM. AttackMoveActivity.OriginalDestination is what GroupScatterHotkeyLogic
-- replays as "the MAIN points the player clicked". It used to be INFERRED: the activity ran
-- the move closure once at construction and read the resulting Move.Destination back out. At
-- the player order site that closure relocates through Mobile.NearestMoveableCell first, and
-- that function answers PER UNIT -- own location, own locomotor, own reachability domain
-- (Mobile.cs:850-871). So one click recorded a different cell for each unit.
--
-- WHY THAT IS VISIBLE AT ALL. Shift-G compares each unit's order chain by (Cell, OrderType)
-- and redistributes only the LONGEST COMMON SUFFIX, preserving each unit's unique prefix.
-- Divergent records break that comparison, and the two arms take different code paths:
--
--   * recorded honestly -> both chains are [clicked, far]; the suffix is the WHOLE chain, so
--     there is no unique prefix, the legacy pool runs, and the two waypoints are handed out
--     one per unit. Rifle (listed first, and nearer) takes the clicked cell; Truck takes far.
--   * recorded per-unit -> chains are [clicked, far] and [relocated, far]; the suffix is just
--     [far], every chain is longer than it, so the SUFFIX path runs instead: each unit gets
--     its own prefix re-issued and then BOTH are sent to the far cell. Nobody ends on the
--     clicked cell at all.
--
-- So the observable is not a cell or two of drift -- it is whether a unit is standing on the
-- clicked cell at the end, or twenty cells away at the far one.
--
-- WHY THE CHECK IS A SINGLE LATE ONE AND NOT AssertWithin. In the broken arm the rifleman
-- WALKS THROUGH the clicked cell on its way to the far cell, because that cell is its own
-- preserved prefix. A predicate polled every tick would catch it there in passing and report
-- a pass. The verdict is therefore taken once, after everything has settled.

local ClickedCell = { X = 16, Y = 16 }
local FarCell = { X = 32, Y = 26 }

-- The sandbag block, inclusive. Ring 1 and ring 2 around ClickedCell are entirely inside it,
-- so a unit that cannot enter sandbag cells cannot answer nearer than ring 3.
local Patch = { MinX = 14, MaxX = 18, MinY = 14, MaxY = 18 }

-- Ticks. Only has to land after the four orders have resolved (tick 1-2) and before either
-- unit has reached its first waypoint -- both start at x=12 and the nearest first waypoint is
-- four cells away, which neither covers in eight ticks.
local ScatterAtTick = 8

-- Seconds. Must exceed the BROKEN arm's full journey (rifleman: 4 cells to the clicked cell,
-- then ~19 more to the far cell, at Locomotor@FOOT speed 25 -> roughly 1.7s per cell), or the
-- verdict would be taken while it is still in transit and merely near neither cell.
local SettleSeconds = 55

local function InPatch(actor)
	local c = actor.Location
	return c.X >= Patch.MinX and c.X <= Patch.MaxX and c.Y >= Patch.MinY and c.Y <= Patch.MaxY
end

local function Drift(actor, cell)
	return TestHarness.CellDrift(actor.Location.X, actor.Location.Y, cell.X, cell.Y)
end

local function Where(actor)
	return "(" .. actor.Location.X .. "," .. actor.Location.Y .. ")"
end

WorldLoaded = function()
	TestHarness.FocusBetween(Rifle, Truck)
	Test.SelectActors({ Rifle, Truck })

	-- SETUP CONTROLS, ordered first so they have the whole run to settle. These do not take
	-- part in the scatter. They exist to make a run that proves nothing SAY so: the entire
	-- scenario rests on the two units disagreeing about what the clicked cell means, and that
	-- disagreement exists only because infantry passes sandbag cells and vehicles do not. If
	-- either half of that stops being true, both units record the same cell, the broken build
	-- behaves exactly like the fixed one, and this scenario would otherwise go green having
	-- exercised nothing.
	Test.IssueMoveOrder(SentinelInf, CPos.New(15, 18), false)
	Test.IssueMoveOrder(SentinelVeh, CPos.New(17, 14), false)

	-- The orders under test. Two clicks, each given to both units: one unqueued on the cell
	-- inside the block, one shift-queued on open ground far away. Issued through the ORDER
	-- LAYER (Test.IssueAttackMove), not the activity-direct Lua unit.AttackMove, which builds
	-- its activity by hand and never runs AttackMove.ResolveOrder -- the code under test.
	Test.IssueAttackMove(Rifle, CPos.New(ClickedCell.X, ClickedCell.Y), false)
	Test.IssueAttackMove(Truck, CPos.New(ClickedCell.X, ClickedCell.Y), false)
	Test.IssueAttackMove(Rifle, CPos.New(FarCell.X, FarCell.Y), true)
	Test.IssueAttackMove(Truck, CPos.New(FarCell.X, FarCell.Y), true)

	Trigger.AfterDelay(ScatterAtTick, function()
		if Rifle.IsDead or Truck.IsDead then
			Test.Fail("[gsw] setup invalid: a unit under test died before the scatter ran")
			return
		end

		-- If either unit had already reached its first waypoint, its chain would be one
		-- waypoint shorter and the suffix comparison would be measuring something other than
		-- what this scenario is about. Say so rather than returning a verdict about it.
		if Rifle.Location.X >= 14 or Truck.Location.X >= 14 then
			Test.Fail("[gsw] setup invalid: a unit reached its first waypoint before the " ..
				"scatter ran (Rifle " .. Where(Rifle) .. ", Truck " .. Where(Truck) ..
				"), so both chains were no longer two waypoints long")
			return
		end

		-- EXECUTION MARKER. Recorded in the verdict's screenshots[] the moment it is called.
		-- A Lua load abort or an early crash also reports status: fail, and is otherwise
		-- indistinguishable from a real RED -- if this label is absent from result.json, the
		-- script did not get this far and the verdict is not about the code under test.
		TestHarness.Screenshot("gsw-armed",
			"both units hold a two-waypoint attack-move chain; Group Scatter fires next")

		Test.GroupScatter({ Rifle, Truck })
	end)

	Trigger.AfterDelay(math.floor(SettleSeconds * TestHarness.TicksPerSecond), function()
		TestHarness.Screenshot("gsw-settled", "final positions; verdict taken now")

		if Rifle.IsDead or Truck.IsDead then
			Test.Fail("[gsw] setup invalid: a unit under test died before the verdict")
			return
		end

		-- The premise, checked before the verdict so a broken premise can never be read as a
		-- statement about the bug.
		if not InPatch(SentinelInf) then
			Test.Fail("[gsw] setup invalid: the infantry control ended at " ..
				Where(SentinelInf) .. ", outside the sandbag block (x" .. Patch.MinX .. "-" ..
				Patch.MaxX .. ", y" .. Patch.MinY .. "-" .. Patch.MaxY .. "). Infantry is " ..
				"supposed to pass sandbag cells (Locomotor@FOOT lists sandbag in Passes), " ..
				"and this scenario only distinguishes the two builds because it does. Both " ..
				"units would record the same cell and the run proves nothing either way")
			return
		end

		if InPatch(SentinelVeh) then
			Test.Fail("[gsw] setup invalid: the vehicle control ended at " ..
				Where(SentinelVeh) .. ", INSIDE the sandbag block. Vehicles are supposed to " ..
				"be blocked by it (every vehicle locomotor is Passes: field only, and these " ..
				"sandbags are Neutral so CrushedByRelationships: Enemy cannot fire). With " ..
				"nothing blocking it, both units record the clicked cell and the run proves " ..
				"nothing either way")
			return
		end

		-- THE VERDICT.
		local rifleDrift = Drift(Rifle, ClickedCell)
		local truckDrift = Drift(Truck, FarCell)

		if rifleDrift > 2 then
			Test.Fail("[gsw] fail: nobody is standing on the clicked cell. The rifleman is at " ..
				Where(Rifle) .. ", " .. rifleDrift .. " cells from the cell that was clicked (" ..
				ClickedCell.X .. "," .. ClickedCell.Y .. "); the truck is at " .. Where(Truck) ..
				". Group Scatter recorded that one click as TWO different waypoints -- the " ..
				"cell for the rifleman, the relocated cell for the truck -- so the shared " ..
				"suffix collapsed to the far cell alone and both units were sent there, each " ..
				"keeping its own first cell as a private prefix. Expected the rifleman left " ..
				"on the clicked cell and only the truck sent to (" .. FarCell.X .. "," ..
				FarCell.Y .. ")")
			return
		end

		if truckDrift > 4 then
			Test.Fail("[gsw] fail: the rifleman is on the clicked cell but the truck is at " ..
				Where(Truck) .. ", " .. truckDrift .. " cells from the far cell (" ..
				FarCell.X .. "," .. FarCell.Y .. ") it should have been given. The two " ..
				"waypoints were not handed out one per unit as the honest chain requires")
			return
		end

		Test.Pass("[gsw] one click stayed one waypoint: rifleman held on the clicked cell " ..
			Where(Rifle) .. ", truck sent to the far cell " .. Where(Truck))
	end)
end
