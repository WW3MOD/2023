-- AUTO TEST: the reported bug, staged as the user described it.
--
-- "Units that are out of ammo get an order to go to the nearest supply truck. I can see the green
-- line towards it, as if the order is correctly given, but they are stuck."
--
-- Nothing here issues an order. The rifleman starts with every pool empty, and the shipped traits
-- do the rest: AmmoPool.AutoRearmIfAllEmpty / AutoSeekSupplies.ReturnWhenEmpty pick the truck and
-- queue SeekSupplyProvider, which draws the green line and calls Mobile.MoveWithinRange. That
-- helper goes through WrapMove (Mobile.cs:677/683) exactly like a player's Move order does, so the
-- errand's own movement runs inside SmartMoveActivity and is pinned by the same ammo-blind
-- interrupt -- the target line is drawn and the man never travels.
--
-- The single reason test-queued-attackmove-survives-resupply passes today while this one does not
-- is that it has no enemy anywhere on its map. With nothing to acquire, SmartMove's scan returns
-- Invalid, the move child is queued normally, and the walk works. The enemy here is the variable.
--
-- Deadline: the walk is ~9 cells at roughly 41 ticks per cell (~15s), plus the errand's own
-- dispatch cadence (EmptyScanInterval 25) and RearmDelay. 45s is generous for a trip that is
-- either made promptly or not at all.

local DeadlineSeconds = 45

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, Supply)
	TestHarness.Select(Hunter)

	-- The Bait is scenery whose only job is to be acquirable. It must not shoot the subject of
	-- the measurement out from under it, and the subject stays FireAtWill because that is what
	-- arms the interrupt under test.
	Bait.Stance = "HoldFire"

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Hunter.IsDead then return "fail: Hunter died before reaching the truck" end
		if Supply.IsDead then return "fail: the supply truck died" end

		-- Ammo arriving is the only proof that he actually closed to within the truck's 5c0 push
		-- aura. Position alone would not distinguish "walked there" from "nudged".
		return Hunter.AmmoCount("primary-ammo") > 0
	end, "The dry rifleman never reached his supply truck -- resupply order issued, no travel")
end
