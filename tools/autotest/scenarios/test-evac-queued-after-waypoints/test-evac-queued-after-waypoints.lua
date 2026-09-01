-- AUTO TEST: a shift-queued Evacuate must run at the END of the order queue.
--
-- Reported by the user: "Evacuation should be possible to queue, now it seems not to be
-- possible." The wording matters -- "not possible", not "runs at the wrong time" -- and it is
-- accurate. Holding Shift did not merely produce an unqueued evacuation; it produced NOTHING.
--
-- WHY IT NEEDED THE REAL WIDGET CHAIN. The defect was entirely in the command bar, upstream of
-- anything a simulation-level test can see. DeliversCash.ResolveOrder already honoured
-- order.Queued and passed it to QueueActivity (DeliversCash.cs:84,130), so a scenario that
-- built its own Order("Evacuate", unit, true) would have gone GREEN against the broken build
-- and proved nothing. Two things were wrong, both in CommandBarLogic:
--   1. the button constructed its order with a hardcoded `false` (:255), where every queueable
--      sibling -- Scatter, Resupply, Deploy -- reads the Shift modifier first; and
--   2. `evacuateButton` was absent from the MODIFIER_OVERRIDES `noShiftButtons` array (:298).
--      HotkeyReference.IsActivatedBy compares modifiers for EQUALITY, so Shift+E does not match
--      the unshifted `Evacuate: E` binding and ButtonWidget.HandleKeyPress rejects it before
--      OnKeyPress. That array is the only thing that strips Shift and retries. Being absent from
--      it is why the key did nothing whatsoever.
-- (2) is the one the user actually hit, so the press is driven through Test.PressHotkey, which
-- dispatches via Ui.HandleKeyPress and walks the real widget chain in the real order.
--
-- THE SHAPE: waypoints EAST, evacuation WEST. RotateToEdge sends a ground unit to the edge
-- nearest the owner's SpawnArea, staged at 8,16 -- seven cells from the western bound, nearer
-- than any other. So the two dispositions travel in opposite directions and cannot be confused:
--   * ran immediately -> never east of x=20, exits west, disposed
--   * ran queued      -> reaches x=38 first, THEN turns around and exits west
--
-- WHY IsDead IS THE "IT EVACUATED" DETECTOR. Nothing on this map can shoot either humvee (there
-- are no enemy units at all) and both are staged at full health, so ChangesHealth@CriticalDamage
-- never starts. Actor.IsDead is `Disposed || health.IsDead` (Actor.cs:76); with damage excluded
-- it can only mean RotateToEdge called self.Dispose. IsDead and IsInWorld sit on
-- BaseActorProperties, which is [ExposedForDestroyedActors], so they stay callable afterwards.
-- Location does NOT -- it reads OccupiesSpace -- so every read of it below is guarded by IsDead.
--
-- THE CONTROL, AND WHY THIS TEST IS WORTHLESS WITHOUT IT. The bare-E humvee is not decoration.
-- The failure mode this scenario is most likely to suffer is "the Evacuate hotkey never reaches
-- the command bar in the harness at all" -- wrong chrome, disabled button, key swallowed
-- upstream -- and that looks IDENTICAL to the bug under test: one humvee sitting still. The
-- control fails in that world too, which converts a false accusation into an obviously broken
-- scenario. Both arms differ by exactly one thing: the Shift modifier on the press.
--
-- A THIRD ARM, DELIBERATELY WEAKER: Shift+R. resupplyButton was missing from the same
-- noShiftButtons array by the same mechanism, so Shift+R was dead in the same way, and its
-- OnClick has read the Shift modifier since it was written (:187) -- queued resupply was always
-- intended and the hotkey could simply never deliver it. This scenario asserts ONLY that the
-- keypress now reaches the button, with a bare-R control beside it. It does NOT exercise what a
-- queued Resupply then does; nothing anywhere does. Read the Shift+R pass as "the key is no
-- longer swallowed", never as "queued resupply works".

-- Budget in TICKS and divide back through the harness constant. TestHarness.TicksPerSecond is
-- 25 while the mod runs at Timestep 60 = 16.67 ticks/second; the constant is deliberately wrong
-- and is pinned by AutotestTickRateTest.cs, so anything sized in "seconds" here would silently
-- mean something else. 1500 ticks is ~90 real seconds.
local function ticks(t) return t / TestHarness.TicksPerSecond end

local DeadlineTicks = 1500
local FirstWaypointX = 24
local LastWaypointX = 38

-- East of here, only a queued move can have put a humvee there: both start at x=8 and every
-- evacuation drives west. Four cells clear of the start and sixteen clear of the last waypoint,
-- so neither a settling wheeled vehicle nor a pathing detour can straddle it.
local EastLineX = 20

local QueuedRow = 14
local ImmediateRow = 18

local PressQueuedAtTick = 10
local PressImmediateAtTick = 20
local PressResupplyBareAtTick = 30
local PressResupplyShiftAtTick = 40
local ScreenshotTicks = 260

-- Latched every tick while each humvee is still alive; read by the verdict poller after it dies.
local queuedWentEast = false
local immediateWentEast = false

-- Set by the two presses. Test.PressHotkey returns whether ANY widget consumed the key, which is
-- the single most diagnostic fact this test can capture: pre-fix the Shift+E press returned false
-- (no widget matched a Shift-bearing event) while the bare-E press returned true. That separates
-- "the order was issued and ran at the wrong time" from "no order was ever issued" without
-- waiting for a timeout to imply it.
local queuedPressConsumed = nil
local immediatePressConsumed = nil

-- The Shift+R arm. resupplyButton was absent from noShiftButtons by the identical mechanism, so
-- Shift+R was dead the same way -- but this pair of flags is ALL that is measured for it: whether
-- the keypress reaches the button. The behaviour of a queued Resupply is NOT exercised here and
-- has never been exercised anywhere; see the commit message. Bare R is its own control, so a
-- rejected Shift+R cannot be confused with "the R hotkey does not work in this harness".
local resupplyBarePressConsumed = nil
local resupplyShiftPressConsumed = nil

local function TrackEast()
	if not QueuedUnit.IsDead and QueuedUnit.Location.X >= EastLineX then
		queuedWentEast = true
	end

	if not ImmediateUnit.IsDead and ImmediateUnit.Location.X >= EastLineX then
		immediateWentEast = true
	end
end

local function Where(unit)
	if unit.IsDead then
		return "evacuated"
	end

	return unit.Location.X .. "," .. unit.Location.Y
end

WorldLoaded = function()
	-- Frame the whole lane: both humvees, both waypoints and the western exit in one shot.
	Camera.Position = WPos.New(23 * 1024, 16 * 1024, 0)
	Test.ShowTargetLinesAlways()

	-- Exactly what the player does: click a waypoint, shift-click a second. Both humvees get the
	-- SAME queue, so the only difference between the arms is the modifier on the press below.
	Test.IssueMove(QueuedUnit, CPos.New(FirstWaypointX, QueuedRow), false, false)
	Test.IssueMove(QueuedUnit, CPos.New(LastWaypointX, QueuedRow), false, true)
	Test.IssueMove(ImmediateUnit, CPos.New(FirstWaypointX, ImmediateRow), false, false)
	Test.IssueMove(ImmediateUnit, CPos.New(LastWaypointX, ImmediateRow), false, true)

	UserInterface.SetMissionText(
		"QUEUED EVACUATE: both humvees -> 24 -> 38 east. Top gets Shift+E (must finish the "
		.. "waypoints first), bottom gets bare E (must leave west at once).")

	-- The press has to happen on a later tick than the selection so the command bar's
	-- selection-hash cache has certainly refreshed before it reads evacuateDisabled.
	Trigger.AfterDelay(PressQueuedAtTick, function()
		TestHarness.Select(QueuedUnit)
		queuedPressConsumed = Test.PressHotkey("Evacuate", true)
		print("[evac-queue] Shift+E consumed=" .. tostring(queuedPressConsumed))
	end)

	Trigger.AfterDelay(PressImmediateAtTick, function()
		TestHarness.Select(ImmediateUnit)
		immediatePressConsumed = Test.PressHotkey("Evacuate", false)
		print("[evac-queue] bare E consumed=" .. tostring(immediatePressConsumed))
	end)

	-- Both R presses go to the SAME humvee: consumption is a property of the widget chain, not of
	-- the unit, so the bare press cannot influence whether the shifted one is matched.
	Trigger.AfterDelay(PressResupplyBareAtTick, function()
		TestHarness.Select(ResupplyUnit)
		resupplyBarePressConsumed = Test.PressHotkey("Resupply", false)
		print("[evac-queue] bare R consumed=" .. tostring(resupplyBarePressConsumed))
	end)

	Trigger.AfterDelay(PressResupplyShiftAtTick, function()
		TestHarness.Select(ResupplyUnit)
		resupplyShiftPressConsumed = Test.PressHotkey("Resupply", true)
		print("[evac-queue] Shift+R consumed=" .. tostring(resupplyShiftPressConsumed))
	end)

	-- Mid-drive: the queued humvee should be well east, the immediate one already gone or nearly.
	Trigger.AfterDelay(ScreenshotTicks, function()
		Test.SetZoom(1.6)
		TestHarness.Screenshot("evac-queue-AFTER-queued-drives-east",
			"expects: the TOP humvee is east of its start, still driving toward the 38,14 waypoint "
			.. "with its target line ahead of it. The BOTTOM humvee has already left westward or is "
			.. "gone. A top humvee heading WEST, or absent, is the bug this test exists for.")
	end)

	-- Live state to lua.log. The AssertWithin failure string is evaluated EAGERLY at registration
	-- (see AUTOTEST.md), so counters interpolated into it would report their initial values
	-- forever -- these prints are the only honest running record of what the run actually did.
	local function LogProgress()
		print("[evac-queue] queued=" .. Where(QueuedUnit) .. " wentEast=" .. tostring(queuedWentEast)
			.. " | immediate=" .. Where(ImmediateUnit) .. " wentEast=" .. tostring(immediateWentEast))
		Trigger.AfterDelay(50, LogProgress)
	end

	Trigger.AfterDelay(50, LogProgress)

	-- ORDERING IS LOAD-BEARING HERE. The control has to be fully DEMONSTRATED -- press consumed,
	-- stayed west, actually evacuated -- before any verdict is returned about the queued arm.
	-- Otherwise the interesting failure is unreachable: in the broken build the queued humvee never
	-- evacuates, so anything gated behind `QueuedUnit.IsDead` can only ever time out, and the run
	-- would report the generic deadline message instead of naming the rejected keypress.
	TestHarness.AssertWithin(ticks(DeadlineTicks), function()
		TrackEast()

		-- 1. Harness sanity. If a bare E was consumed by no widget, the Evacuate hotkey is not
		-- reaching the command bar in this run at all and NOTHING here is evidence about queuing.
		if immediatePressConsumed == false then
			return "fail: the CONTROL press was rejected -- a bare E (no modifier) was consumed by "
				.. "no widget, so the Evacuate hotkey is not reaching the command bar in this run. "
				.. "This scenario cannot say anything about queuing until that is fixed; it is not "
				.. "evidence about the Shift+E path."
		end

		-- 2. The control must DISCARD its waypoints, or the two dispositions are indistinguishable
		-- and a queued-arm pass would prove nothing.
		if immediateWentEast then
			return "fail: the CONTROL humvee drove EAST past x=" .. EastLineX .. " before "
				.. "evacuating. A bare E must REPLACE the queued waypoints, not append to them. "
				.. "Both arms are behaving as if queued, so the test can no longer tell the two "
				.. "dispositions apart."
		end

		-- 3. Wait for the control to finish. Reaching past here means: the hotkey works, an
		-- unqueued Evacuate discards the queue, and the exit really is westward.
		if not ImmediateUnit.IsDead then
			return false
		end

		-- 4. THE REPORTED BUG. Decided as soon as the control has vouched for the path -- not
		-- gated on the queued humvee evacuating, because in the broken build it never does.
		if queuedPressConsumed == false then
			return "fail: Shift+E was consumed by NO widget, so no Evacuate order was ever issued "
				.. "-- the queued evacuation did not run early, it did not run at all. This is the "
				.. "reported bug: HotkeyReference.IsActivatedBy compares modifiers for equality, so "
				.. "a Shift-bearing event never matches the unshifted `Evacuate: E` binding, and "
				.. "CommandBarLogic's noShiftButtons array (the only thing that strips Shift and "
				.. "retries) does not list evacuateButton. The bare-E control WAS consumed on the "
				.. "same map moments earlier, which proves the hotkey path itself is healthy."
		end

		-- 5. The order was issued. Did it wait?
		if not QueuedUnit.IsDead then
			return false
		end

		if not queuedWentEast then
			return "fail: the shift-queued Evacuate did not wait for the waypoints -- the humvee "
				.. "evacuated without ever reaching x=" .. EastLineX .. ", having been ordered east to "
				.. LastWaypointX .. "," .. QueuedRow .. " first. The press WAS consumed, so the order "
				.. "was issued and merely carried the wrong queued flag: CommandBarLogic built it with "
				.. "a hardcoded `false` instead of reading the Shift modifier."
		end

		-- 6. The Evacuate arm is satisfied. The Shift+R arm is checked LAST, on purpose: it is the
		-- weaker assertion (reachability only) and it must never be able to mask or pre-empt the
		-- evacuate verdict this scenario primarily exists for.
		if resupplyBarePressConsumed == false then
			return "fail: the RESUPPLY CONTROL press was rejected -- a bare R was consumed by no "
				.. "widget, so the Resupply hotkey is not reaching the command bar in this run and "
				.. "the Shift+R result below cannot be interpreted. Check the humvee still carries an "
				.. "AmmoPool, which is what un-disables the button."
		end

		if resupplyShiftPressConsumed == nil then
			return false
		end

		if resupplyShiftPressConsumed == false then
			return "fail: Shift+R was consumed by NO widget, so the Resupply hotkey is still dead "
				.. "while the queue modifier is held -- the same defect as Shift+E, on the button one "
				.. "line away in the same noShiftButtons array. Bare R WAS consumed moments earlier, "
				.. "so the hotkey path itself is healthy. NOTE this arm asserts REACHABILITY only; it "
				.. "says nothing about whether a queued Resupply then behaves correctly."
		end

		return true
	end, "Neither humvee ever evacuated: no Evacuate order took effect at all within the deadline")
end
