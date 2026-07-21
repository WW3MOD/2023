-- AUTO TEST — Bug 3.2: an experimental TECN RIDES a carrier to a distant derrick.
--
-- Setup (map.yaml): a USA experimental bot owns one TECN + one bradley near its SR;
-- a NEUTRAL oil derrick sits ~39 cells east — past the 12-cell ferry gate. The bot's
-- CaptureCoordinatorBotModule@experimental.tecn requests a ride from the experimental
-- MountedTransportBotModule twin (TryReserveCaptureFerry): the bradley boards the TECN,
-- drives it to the derrick and hands it CaptureActor on unload.
--
-- The verdict proves the FULL ferry-capture chain, not just carriage:
--   1. mounted    — the carrier carried a passenger at some point (TECN boarded), AND
--   2. delivered  — the carrier reached within DROP cells of the derrick, AND
--   3. dismounted — the carrier UNLOADED (HasPassengers went true→false at the drop-off), AND
--   4. captured   — the neutral derrick's owner flipped to USA-bot (the TECN is consumed on
--                   capture: ConsumedByCapture, CaptureDelay 20 — so a completed capture is the
--                   only proof the dismounted TECN actually did its job).
-- PASS the instant all four hold. This closes the old blind spot: the earlier predicate PASSed
-- on carriage+arrival alone (mounted+delivered), so a carrier that arrived but never unloaded
-- (the "UnloadCargo" wrong-order-string bug) shipped GREEN. Now, mount-without-dismount can
-- never pass — `dismounted` never latches and the run times out RED.

local DERRICK_X = 48
local DERRICK_Y = 15
local DROP = 6          -- carrier counts as "delivered" within this many cells of the derrick

WorldLoaded = function()
	TestHarness.FocusBetween(Tecn, Derrick)
	TestHarness.Select(Tecn)

	local mounted = false
	local delivered = false
	local dismounted = false

	TestHarness.AssertWithin(100, function()
		if Carrier.IsDead then return "fail: carrier died before completing the ferry-capture" end

		-- 1. Latch the moment the carrier is carrying the TECN (proves mounting).
		if Carrier.HasPassengers then mounted = true end

		-- 2. Latch arrival within DROP cells of the derrick (only meaningful once mounted).
		if mounted and not delivered then
			local dx = Carrier.Location.X - DERRICK_X
			local dy = Carrier.Location.Y - DERRICK_Y
			if (dx * dx + dy * dy) <= DROP * DROP then delivered = true end
		end

		-- 3. Latch the UNLOAD: after delivering, the carrier's passengers must drain to empty.
		-- This is the assertion the old test lacked — a carrier that arrives but never issues a
		-- resolvable Unload keeps HasPassengers true forever and can never reach this latch.
		if delivered and not Carrier.HasPassengers then dismounted = true end

		-- 4. PASS only when the dismounted TECN has actually captured the derrick (owner flip).
		if dismounted and not Derrick.IsDead and Derrick.Owner.Name == "USA-bot" then
			return true
		end

		return false
	end, "TECN ferry-capture chain did not complete within 100s (mount→deliver→UNLOAD→capture). "
		.. "If it mounted+arrived but never unloaded, the carrier sat loaded (the unload-order bug).")
end
