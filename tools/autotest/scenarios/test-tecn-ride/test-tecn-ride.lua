-- AUTO TEST — Bug 3.2: an experimental TECN RIDES a carrier to a distant derrick.
--
-- Setup (map.yaml): a USA experimental bot owns one TECN + one bradley near its SR;
-- a NEUTRAL oil derrick sits ~39 cells east — past the 12-cell ferry gate. The bot's
-- CaptureCoordinatorBotModule@experimental.tecn requests a ride from the experimental
-- MountedTransportBotModule twin (TryReserveCaptureFerry): the bradley boards the TECN,
-- drives it to the derrick and hands it CaptureActor on unload.
--
-- The verdict proves BOTH halves of "technicians ride first":
--   1. mounted   — the carrier carried a passenger at some point (TECN boarded), AND
--   2. delivered — the carrier reached within DROP cells of the derrick.
-- PASS the instant both hold. FAIL (RED, fix off) = the TECN walks, the carrier never
-- gets a passenger, so `mounted` never latches and the run times out.

local DERRICK_X = 48
local DERRICK_Y = 15
local DROP = 6          -- carrier counts as "delivered" within this many cells of the derrick

WorldLoaded = function()
	TestHarness.FocusBetween(Tecn, Derrick)
	TestHarness.Select(Tecn)

	local mounted = false

	TestHarness.AssertWithin(60, function()
		if Carrier.IsDead then return "fail: carrier died before delivering" end

		-- Latch the moment the carrier is carrying the TECN (proves mounting).
		if Carrier.HasPassengers then mounted = true end

		if mounted then
			local dx = Carrier.Location.X - DERRICK_X
			local dy = Carrier.Location.Y - DERRICK_Y
			if (dx * dx + dy * dy) <= DROP * DROP then
				return true
			end
		end

		return false
	end, "TECN was not mounted+delivered to the distant derrick within 60s (it walked, or the carrier never arrived)")
end
