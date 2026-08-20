-- AUTO TEST — walking near an enemy crate captures it, with no order to do so.
--
-- "Getting close to one should capture it for ourselves instantly, so we can use the
-- resources ourselves." (live play, 2026-08-20)
--
-- Setup (map.yaml): an ENEMY-owned SUPPLYCACHE at (20,16). A rifleman (Walker, owned
-- by Me) starts 6 cells west at (14,16) and is given ONE plain Move order, to (26,16)
-- — six cells PAST the crate. That is the whole point of the destination: the unit is
-- never ordered to touch, attack, enter or collect the crate. It is ordered to walk
-- somewhere else and happens to pass close by. If the crate changes hands, proximity
-- alone did it.
--
-- ATTRIBUTION. Nothing else in this scenario can change the crate's owner: there is no
-- Logistics Center to absorb it, no supply truck to issue PickupSupply, no capture
-- engineer, and DefaultCash is 0 so neither side can produce one. The crate is also
-- NoAutoTarget now, so it cannot be destroyed into some other state on the way.
--
-- SETUP GUARD. The crate must START owned by Enemy. A scenario that silently handed it
-- to Me at load would satisfy "Crate.Owner.Name == 'Me'" on tick one and report a
-- confident pass having measured nothing.
--   PASS = Crate owner flips Me while Walker is merely passing.
--   FAIL = still Enemy when the window closes, or the setup guard trips.

local WINDOW = 30   -- seconds for the rifleman to walk 6 cells and trigger capture

WorldLoaded = function()
	TestHarness.FocusBetween(Walker, Crate)
	TestHarness.Select(Walker)

	if Crate.Owner.Name ~= "Enemy" then
		Test.Fail("setup guard: crate did not start enemy-owned (owner=" .. Crate.Owner.Name ..
			"), so a captured verdict would prove nothing")
		return
	end

	-- One plain move order, to a cell well beyond the crate. No interaction is requested.
	Walker.Move(CPos.New(26, 16))

	-- Live values go in a print, never in the failure string: AssertWithin's third
	-- argument is evaluated EAGERLY at registration and would report load-time values.
	local ticks = 0
	local function trace()
		if Walker.IsDead then return end
		print(string.format("[crate-capture] t=%ds walker=(%d,%d) crateOwner=%s",
			ticks, Walker.Location.X, Walker.Location.Y, Crate.Owner.Name))
		ticks = ticks + 5
		Trigger.AfterDelay(5 * TestHarness.TicksPerSecond, trace)
	end
	Trigger.AfterDelay(5 * TestHarness.TicksPerSecond, trace)

	TestHarness.AssertWithin(WINDOW, function()
		if Walker.IsDead then
			return "fail: Walker died before reaching the crate — inconclusive"
		end

		if Crate.IsDead then
			return "fail: the crate was destroyed rather than captured"
		end

		return Crate.Owner.Name == "Me"
	end, "the rifleman walked past the enemy crate without capturing it — proximity capture did not fire")
end
