-- AUTO TEST — a transport helicopter must be able to land on a crop-field cell to unload.
--
-- ww3mod tiles fields as one 1x1 Building actor per cell. Aircraft.CanLand walks
-- ActorMap.GetActorsAt and can only be un-blocked by something the aircraft would CRUSH;
-- AircraftInfo has `Crushes` but no `Passes`, and ww3mod aircraft crush only
-- crate/mine/infantry. So a field actor blocks the touchdown, and Cargo.CanUnload — which
-- calls CanLand directly — never becomes true. The transport hovers with its troops aboard.
--
-- Setup (map.yaml): Tran parked over 22,16, dead centre of a 13x13 v14 field patch; two
-- riflemen built out-of-world below and loaded straight aboard.
--   PASS = both passengers are out of the transport within the window.
--   FAIL = they are still aboard (CanLand refused the field cell), or the Tran died.
--
-- The patch is 13x13 rather than just big enough to stand on because UnloadCargo hands
-- Land a 5-cell range (Tran.UnloadPassengers defaults unloadRange = 5). With a smaller
-- patch the helicopter sidesteps to bare ground just outside the field and unloads there,
-- and the test passes without ever exercising the bug.

local WINDOW = 25   -- seconds allowed for touchdown + unload

WorldLoaded = function()
	TestHarness.FocusBetween(Tran, Tran)
	TestHarness.Select(Tran)

	-- Build the passengers OUT OF WORLD and load them straight in, so their starting
	-- cell plays no part in the verdict.
	--
	-- PITFALL: the second argument must be false. Cargo.Load (Cargo.cs:529) adds the
	-- passenger to the cargo list but never calls World.Remove — the removal normally
	-- happens on the EnterTransport path, not here. Loading an actor that IS in the
	-- world leaves it in both places, and the eventual unload re-adds it, throwing
	-- "An item with the same key has already been added" out of World.Add.
	local function board()
		local a = Actor.Create("e3", false,
			{ Owner = Tran.Owner, Location = Tran.Location, Facing = Angle.New(768) })
		Tran.LoadPassenger(a)
	end

	board()
	board()

	local loaded = Tran.PassengerCount
	if loaded < 2 then
		Test.Fail("setup: expected 2 passengers aboard, got " .. tostring(loaded))
		return
	end

	-- Unload where it is standing — over the field.
	Tran.UnloadPassengers()

	TestHarness.AssertWithin(WINDOW, function()
		if Tran.IsDead then return "fail: transport died before unloading" end
		return Tran.PassengerCount == 0
	end, string.format(
		"transport still held %d of %d passengers after %ds hovering over a field cell — " ..
		"Cargo.CanUnload never went true because Aircraft.CanLand counts the field actor as " ..
		"a blocker", Tran.PassengerCount, loaded, WINDOW))
end
