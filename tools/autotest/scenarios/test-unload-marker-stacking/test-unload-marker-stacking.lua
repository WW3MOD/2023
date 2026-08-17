-- AUTO TEST — one dismount marker per cell, not one per soldier.
--
-- THE DEFECT. Unloading a whole class sends one UnloadCargoPassenger order per man, and
-- Cargo.ResolveOrder gives each resulting UnloadCargo a markerCell from PredictedUnloadCell.
-- Four men queued onto one cell therefore produced four IDENTICAL tile nodes at that cell,
-- and DrawLineToTarget drew every node it walked. The marker is deliberately drawn at
-- UnloadMarkerAlpha 0.6 because a ghost is how it says "this has not happened yet" — but
-- four ghosts at 0.6 composite toward fully opaque, so the marker ended up asserting the
-- exact opposite of what it exists to say. DrawLineToTarget.TileNodes now collapses exact
-- duplicates, keyed on position AND sprite so two genuinely different markers on one cell
-- both survive.
--
-- WHY THIS IS ASSERTABLE AT ALL, RATHER THAN BEING A SCREENSHOT JUDGEMENT. Alpha compositing
-- is precisely the kind of thing an agent reads wrong off a downscaled capture. But
-- Test.GetTargetLineCells(actor, true) now calls DrawLineToTarget.TileNodes — the SAME
-- enumeration the renderer draws from, not a re-implementation of it — so counting its
-- entries counts exactly the sprites that reach the screen. If that binding ever stops
-- sharing the walk, this test stops being evidence, which is why the sharing is the point.
--
-- WHAT WOULD MAKE THIS TEST VACUOUS, AND HOW IT GUARDS AGAINST IT. If markers were disabled
-- (CargoInfo.UnloadMarkerImage empty) there would be ZERO tile cells, and an "is it 1?" check
-- alone would look identical to a pass for the wrong reason. So the test requires exactly 1,
-- never 0, and separately requires at least two men still aboard when it counts — at least
-- two unload activities genuinely queued, each of which stamps its own marker.
--
-- AND IT WAS CONFIRMED RED. With the dedupe disabled (drawn.Add short-circuited to always
-- admit) this reported 4 markers for 4 queued unloads; with it restored, 1. Worth recording,
-- because a test asserting "exactly one" passes just as happily when the thing it is counting
-- never happened, and that is the failure this project keeps meeting.

local Carried = 4
local SettleSeconds = 3

WorldLoaded = function()
	Camera.Position = MenuChinook.CenterPosition

	Trigger.AfterDelay(DateTime.Seconds(SettleSeconds), function()
		if MenuChinook.PassengerCount ~= Carried then
			Test.Fail("expected " .. tostring(Carried) .. " aboard, got " ..
				tostring(MenuChinook.PassengerCount))
			return
		end

		TestHarness.Select(MenuChinook)
		if not Test.PressHotkey("UnloadMenu") then
			Test.Fail("the unload menu did not open on the Chinook")
			return
		end

		Trigger.AfterDelay(DateTime.Seconds(1), function()
			local state = Test.GetUnloadMenuState()
			if state ~= "1:1" then
				Test.Fail("expected one menu listing one class row, got '" .. tostring(state) .. "'")
				return
			end

			if not Test.ClickUnloadMenuRow(0, true) then
				Test.Fail("could not click the ALL chip on the Rifleman row")
				return
			end

			-- Counted a few TICKS after the click, not a second: the orders need a tick or two to
			-- resolve into queued activities, but the first man is out well inside a second — the
			-- first attempt at this waited a full second and found only 3 aboard. What the count
			-- needs is simply that SEVERAL unload activities are still queued, since each queued one
			-- is a marker that would stack; it does not need all four.
			Trigger.AfterDelay(6, function()
				local aboard = MenuChinook.PassengerCount
				if aboard < 2 then
					Test.Fail("only " .. tostring(aboard) .. " still aboard, so fewer than two unload " ..
						"activities remain queued and there is nothing left that could stack")
					return
				end

				local cells = Test.GetTargetLineCells(MenuChinook, true)
				local n = #cells

				if n == 0 then
					Test.Fail("no dismount marker at all — either markers are disabled for this " ..
						"transport or no unload was queued, and either way this test proves nothing")
					return
				end

				if n ~= 1 then
					Test.Fail(string.format(
						"%d dismount markers drawn for %d unloads still queued onto one cell — the " ..
						"stack is back. At UnloadMarkerAlpha 0.6 that composites toward opaque and " ..
						"stops reading as a preview.", n, aboard))
					return
				end

				Test.Pass(string.format(
					"unloading a class of %d left %d unload activities still queued onto one cell and " ..
					"drew ONE marker at %s, so the 0.6 alpha still reads as a preview instead of " ..
					"compositing toward opaque.", Carried, aboard, tostring(cells[1])))
			end)
		end)
	end)
end
