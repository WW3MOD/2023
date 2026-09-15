-- AUTO TEST: does EnterTransport actually work on a NEUTRAL civilian building?
--
-- WHY THIS EXISTS. In the 2026-09-15 garrison-lineup demo run
-- (260915_210535_p58516_demo-garrison-lineup) the three squads ordered into USA-owned
-- emplacements boarded normally and all three ordered into NEUTRAL civilian buildings did not
-- move at all -- capture 006 shows ten riflemen still standing in their start files four cells
-- from the church. The demo was changed to teleport its men in with LoadPassenger so the
-- capture could proceed, which sidesteps the question rather than answering it.
--
-- IT MATTERS BEYOND THE DEMO. Walking into a neutral civilian building is HOW A PLAYER
-- GARRISONS ONE. If this is broken, the civilian half of the garrison feature is unreachable in
-- ordinary play and only the three built emplacements work.
--
-- NOTHING FOUND SO FAR EXPLAINS IT, which is why this is a measurement and not a fix:
--   * EnterAlliedActorTargeter explicitly permits neutral targets
--     (EnterAlliedActorTargeter.cs:49-54) -- and is not even on this path, since
--     MobileProperties.EnterTransport queues a RideTransport activity directly;
--   * RideTransport / Enter carry no relationship gate at all;
--   * Cargo.LoadingBlocked is set only by HeliEmergencyLanding;
--   * Cargo's only ICargoCanLoadFilter is SupplyProvider, which civilian buildings do not have;
--   * GarrisonManager only observes entry, through INotifyPassengerEntered.
--
-- So the expected result is PASS on both lanes. A PASS retires the worry and says the demo's
-- first run failed for some other reason. A FAIL on the neutral lane alone is a live gameplay
-- defect and is worth a bug entry immediately.
--
-- Both v08 are capacity 4 against a squad of 2, so neither lane can fail for want of space.

local Deadline = 12

local function Aboard(house, squad)
	local inside = house.PassengerCount
	local walking, dead = 0, 0
	for _, s in ipairs(squad) do
		if s.IsDead then
			dead = dead + 1
		elseif s.IsInWorld then
			walking = walking + 1
		end
	end

	return inside, walking, dead
end

local function Line(label, house, squad)
	local inside, walking, dead = Aboard(house, squad)
	return label .. ": " .. inside .. " aboard, " .. walking .. " still outside, " .. dead .. " dead"
end

WorldLoaded = function()
	local neutralSquad = { N1, N2 }
	local ownedSquad = { O1, O2 }

	TestHarness.FocusBetween(NeutralHouse, OwnedHouse)
	Test.SetZoom(2)

	for _, s in ipairs(neutralSquad) do
		s.EnterTransport(NeutralHouse)
	end

	for _, s in ipairs(ownedSquad) do
		s.EnterTransport(OwnedHouse)
	end

	TestHarness.AssertWithin(Deadline, function()
		-- The CONTROL lane has to succeed too. If neither lane loads, the finding is about this
		-- scenario and not about neutrality, and a verdict that did not separate them would be
		-- read as "neutral entry is broken" on no evidence.
		local neutralIn = NeutralHouse.PassengerCount
		local ownedIn = OwnedHouse.PassengerCount

		if neutralIn >= 2 and ownedIn >= 2 then
			return true
		end

		return false
	end, function()
		local n = Line("neutral v08", NeutralHouse, neutralSquad)
		local o = Line("USA-owned v08", OwnedHouse, ownedSquad)

		if OwnedHouse.PassengerCount >= 2 and NeutralHouse.PassengerCount == 0 then
			return "NEUTRAL ENTRY IS BROKEN and the control lane proves it is about ownership: " ..
				n .. "; " .. o .. ". A player cannot garrison a civilian building by walking into it."
		end

		if OwnedHouse.PassengerCount == 0 then
			return "BOTH lanes failed to load within " .. Deadline .. "s, so this says nothing " ..
				"about neutrality -- suspect the scenario: " .. n .. "; " .. o
		end

		return "partial load within " .. Deadline .. "s: " .. n .. "; " .. o
	end)
end
