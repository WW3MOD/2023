-- AUTO TEST — the capture ferry fills its spare seats, and the escort does NOT ruin the capture.
--
-- Setup (map.yaml): identical to test-tecn-ride (one TECN, one bradley, a neutral derrick ~39 cells
-- east, past the 12-cell ferry gate) plus FOUR riflemen standing near the USA SR. The bradley seats
-- five. Before this change the ferry reserved the whole carrier for the technician and drove out
-- with four seats empty while the riflemen stood at the SR.
--
-- WHAT THIS MEASURES, AND WHY IT IS ATTRIBUTABLE. The observable is the PEAK PASSENGER COUNT of one
-- named carrier — a loading fact, not a positional one. "Infantry ended up near the objective" would
-- be worthless here: several modules walk infantry toward the same believed front, so that predicate
-- passes whether or not this code runs.
--
-- Peak rather than current, per game-model.md: a count sampled at one instant is a coin flip once
-- units start dismounting or dying, and no threshold stabilises that.
--
-- THE PEAK IS FROZEN AT DISMOUNT, and that is not a detail — the first cut of this test did not do
-- it and PASSED while measuring nothing. Attribution only holds WHILE the ferry owns the carrier:
-- TryAssignNewTasks skips any carrier already in carrierTasks and any carrier that is not empty, so
-- during the ferry leg the spare-seat fill is the only code that can add a passenger. Once the ferry
-- task is torn down the bradley returns to the general pool, and the ordinary frontline delivery
-- path will happily load riflemen into it — which is exactly what raised the peak to >= 2 in the
-- run that passed with the ferry itself carrying one technician and nothing else (log: boarded=0,
-- depart aboard=1). Freezing at dismount confines the measurement to the ferry leg.
--
-- Two assertions, both required:
--   1. FILLED     — peak passengers >= 2, i.e. the technician did not travel alone.
--   2. CAPTURED   — the derrick still flips to USA-bot.
-- (2) is not a bonus, it is the guard on the landmine. The escorts are riflemen, and a rifleman
-- handed CaptureActor walks in and NEUTRALISES the building instead of taking it (soldiers clear,
-- only technicians own). If the CaptureActor hand-back on unload ever goes to the whole passenger
-- list again rather than to CarrierTask.Capturer alone, the derrick goes Neutral and never reads
-- USA-bot — so this test fails RED on exactly the regression that would otherwise ship silently.
--
-- RED: set CaptureFerryEscortSeats to 0 on MountedTransportBotModule@experimental in
-- mods/ww3mod/rules/ai/ai.yaml — peak stays 1 and assertion (1) times out.

local DERRICK_X = 48
local DERRICK_Y = 15
local DROP = 6          -- carrier counts as "delivered" within this many cells of the derrick

WorldLoaded = function()
	TestHarness.FocusBetween(Tecn, Derrick)
	TestHarness.Select(Tecn)

	local peakPax = 0
	local mounted = false
	local delivered = false
	local dismounted = false

	TestHarness.AssertWithin(120, function()
		if Carrier.IsDead then
			return "fail: carrier died before completing the ferry-capture (peak pax " .. peakPax .. ")"
		end

		-- Peak load ON THE FERRY LEG ONLY. Latched every tick so a passenger that boards and later
		-- dismounts or dies cannot walk the number back down, but STOPPED once the ferry has unloaded:
		-- past that point the carrier is back in the general pool and any load it picks up belongs to
		-- the frontline delivery path, not to the spare-seat fill under test.
		if not dismounted then
			local pax = Carrier.PassengerCount
			if pax > peakPax then peakPax = pax end
		end

		if Carrier.HasPassengers then mounted = true end

		if mounted and not delivered then
			local dx = Carrier.Location.X - DERRICK_X
			local dy = Carrier.Location.Y - DERRICK_Y
			if (dx * dx + dy * dy) <= DROP * DROP then delivered = true end
		end

		if delivered and not Carrier.HasPassengers then dismounted = true end

		-- PASS needs BOTH: the ferry travelled fuller than one, and the capture still completed.
		if dismounted and peakPax >= 2 and not Derrick.IsDead and Derrick.Owner.Name == "USA-bot" then
			return true
		end

		return false
	end, "capture ferry did not deliver a filled load and complete the capture within 120s. "
		.. "Want peak passengers >= 2 (technician + escort). If peak is 1 the spare seats were never "
		.. "filled. If peak reached >= 2 but the derrick never flipped to USA-bot, check whether an "
		.. "escorting RIFLEMAN was handed CaptureActor and neutralised the derrick instead of the "
		.. "technician capturing it.")
end
