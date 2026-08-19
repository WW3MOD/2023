-- AUTO TEST: aircraft had nowhere to rearm, on any map, for the whole life of the mod.
--
-- All seven armed airframes named `hpad` or `afld` as their rearm host. Both carry
-- `Buildable.Prerequisites: ~disabled` and NOTHING in the repo provides `disabled`, so neither can
-- be built; and neither is pre-placed on any of the ten shipped maps. So `RearmActors` named actors
-- that could not exist, and an aircraft flew until it was dry and then stayed dry for the match.
--
-- THE MECHANISM, and why this scenario stages a LOGISTICS CENTRE specifically.
--
-- An aircraft has exactly ONE route to a rearm host. The ground route -- AmmoPool.AutoRearm, which
-- picks SeekSupplyProvider / RideTransport / Resupply per host kind -- is closed to aircraft at
-- every entrance: AutoRearmIfAllEmpty, AutoRearmIfAnyNotFull and the bot sweep's own
-- IsOutOfAmmoSweepCandidate all refuse AircraftInfo, and Resupply would not carry one there anyway
-- because its approach block is guarded on `aircraft == null` (Resupply.cs:256). What remains is
-- ReturnToBase, and ReturnToBase.ChooseResupplier selects over
-- ActorsHavingTrait<Reservable>() (ReturnToBase.cs:45-50).
--
-- So pointing the airframes at `logisticscenter` is NOT sufficient on its own, and that is the trap
-- this scenario is really guarding. AmmoPool.ChooseResupplier matches a host on RearmActors
-- membership plus a SupplyProvider/RearmsUnits trait and never asks whether the caller can reach
-- it, so an LC without Reservable reads as a host to the readiness gates while remaining
-- unreachable in fact -- which would flip the airframe onto the strict restore-first bars with
-- nothing able to satisfy them. The LC therefore had to be given Reservable, and
-- AirframeReadiness.CountsAsRearmHost refuses the dock term for aircraft to hold the same line from
-- the other side (pinned in NUnit, AirframeReadinessTest).
--
-- WHY THE HOST IS USA-OWNED. ChooseResupplier filters `a.Owner == self.Owner`. An LC enters a match
-- only as one of the Neutral pre-placed capturables on polar-disorder, river-zeta and
-- woodland-warfare, so a USA-owned LC is the post-capture state -- the only way an aircraft host
-- ever comes to exist in this mod. On the other seven maps aircraft ammunition is still one-way,
-- which is a deliberate consequence of the fix and not an oversight.
--
-- WHY THE SCRIPT ISSUES ReturnToBase RATHER THAN WAITING. Nothing dispatches a dry aircraft on its
-- own (see the AutoRearm carve-outs above); the bot air states and the player's own deploy button
-- both produce this same order. Issuing it directly is the smallest thing that exercises the whole
-- chain -- ChooseResupplier -> Fly -> land -> Resupply -> RearmTick -- without dragging a bot squad
-- and its recruitment thresholds into the measurement.
--
-- THE ASSERTION IS DELIBERATELY TWO-PART: arrived AND refilled.
--
-- Ammo alone would not prove the fix. A landed airframe within 2c0 of an LC also trickle-refills
-- through ReloadAmmoPool@1/@2, which is gated `unit.docked && !airborne` and fed by the LC's
-- ProximityExternalCondition@UNITDOCKED -- a path that exists independently of Rearmable. Both
-- paths require the Apache to have been FLOWN THERE, which is the thing that was broken, so either
-- would be a legitimate green; but requiring arrival as well means a green can never be produced by
-- some future change that refills ammo without the aircraft ever going anywhere.

local DeadlineSeconds = 60

-- 3x3 building anchored at 10,16, so its cells run x in [10,12]. 6 is comfortably clear of it and
-- still 38 cells from where the Apache starts -- reachable only by actually making the trip.
local ArrivedWithinCells = 6

local reachedDepot = false

WorldLoaded = function()
	TestHarness.FocusBetween(Heli, Depot)
	TestHarness.Select(Heli)

	-- The one order under test. `alwaysLand` is true on this Lua entry point, so a resupplier that
	-- resolves means a landing; a resupplier that does not resolve degrades to FlyIdle and the
	-- Apache hovers where it started, which is precisely the pre-fix behaviour.
	Heli.ReturnToBase()

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Heli.IsDead then return "fail: the Apache died" end
		if Depot.IsDead then return "fail: SETUP -- the logistics centre died, so this run measures nothing" end

		local primary = Heli.AmmoCount("primary-ammo")
		local secondary = Heli.AmmoCount("secondary-ammo")

		local dx = Heli.Location.X - Depot.Location.X
		local dy = Heli.Location.Y - Depot.Location.Y
		if dx < 0 then dx = -dx end
		if dy < 0 then dy = -dy end
		local chebyshev = dx
		if dy > chebyshev then chebyshev = dy end

		if chebyshev <= ArrivedWithinCells then reachedDepot = true end

		-- Arrived but still empty is a DIFFERENT failure from never arriving, and worth separating:
		-- it means ChooseResupplier found the host and flew there but the resupply itself did not
		-- happen -- e.g. Rearmable.RearmActors not naming the host, so Resupply's constructor never
		-- set ResupplyType.Rearm and it landed for nothing.
		if reachedDepot and primary == 0 and secondary == 0 then
			return false
		end

		return reachedDepot and primary > 0 and secondary > 0
	end, "The dry Apache never reached the captured logistics centre with ammunition aboard "
		.. "(reachedDepot=" .. tostring(reachedDepot) .. ")")
end
