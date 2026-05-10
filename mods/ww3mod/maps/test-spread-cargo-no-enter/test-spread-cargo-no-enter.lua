-- AUTO TEST: Group Scatter (Shift-G) must NOT broadcast a single
-- EnterTransport waypoint to every selected unit.
--
-- Setup: BMP (Cargo trait) + 3 infantry adjacent on the west side, with
-- queued Move waypoints further west. Inf1 has an extra RideTransport(BMP)
-- queued behind its moves (the "stale Enter" condition that arises when a
-- player accidentally clicks the BMP during queuing, or when AmmoPool
-- auto-resupply queues a RideTransport).
--
-- Pre-fix bug: spread harvests Inf1's RideTransport activity as an
-- EnterTransport waypoint, finds 1 waypoint vs 4 units in the distributor's
-- spread branch, and assigns ALL units to that one waypoint — so every
-- infantry ends up entering the BMP.
--
-- Post-fix: CollectWaypoints filters Enter-derived activities, so the
-- spread only redistributes the Move waypoints. No infantry get loaded.

local Waypoints = { CPos.New(28, 13), CPos.New(28, 19) }

WorldLoaded = function()
	TestHarness.FocusBetween(Bmp, Inf1)

	-- Queue identical Move waypoints on every infantry. The BMP stays still
	-- so its Cargo isn't LoadingBlocked while we issue the EnterTransport
	-- order below (the `notmobile` LoadingCondition gates loading on the
	-- transport being stopped). This mirrors the user's scenario closely
	-- enough — selection with cargo vehicle + infantry + queued waypoints.
	for _, unit in ipairs({ Inf1, Inf2, Inf3 }) do
		for _, cell in ipairs(Waypoints) do
			unit.Move(cell)
		end
	end

	-- Inf1 additionally has a RideTransport(BMP) tail-queued via the real
	-- order pipeline (so the activity has its target-line color set, mirroring
	-- a player right-click). Without the fix this leaks into the spread pool
	-- as an EnterTransport waypoint that gets broadcast to every unit.
	-- The order is processed on the next tick, so we wait one tick before
	-- triggering the spread to ensure the RideTransport is in the chain.
	Test.IssueEnterTransport(Inf1, Bmp, true)

	Trigger.AfterDelay(8, function()
		-- Trigger the spread directly on the four actors as if the user had
		-- selected them all and pressed Shift-G.
		Test.GroupScatter({ Bmp, Inf1, Inf2, Inf3 })

		-- Wait long enough for the bug to manifest (Move ~1-2s, then
		-- RideTransport walk-and-enter ~3-5s) AND for the fix's behaviour to
		-- settle (infantry arrive at waypoint and idle). Then check once.
		-- Pre-fix: at least one infantry has entered the BMP — IsInWorld false.
		-- Post-fix: every infantry stays in world and is at one of the waypoints.
		TestHarness.AssertAfter(10, function()
			if not Inf1.IsInWorld then return false end
			if not Inf2.IsInWorld then return false end
			if not Inf3.IsInWorld then return false end
			return true
		end, "fail: at least one infantry was loaded into the BMP — spread broadcast an EnterTransport waypoint to units that didn't intend to enter")
	end)
end
