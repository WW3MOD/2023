-- AUTO TEST: a depot too poor to afford a batch is still worth driving to, because the trip is free.
--
-- THE FAILURE THIS PINS. e36ab29a taught AmmoPool.AutoRearmIfDry's Auto arm to pick the nearest
-- AFFORDABLE host rather than the nearest stocked one. For a truck or a cache that is right: those
-- are served by SupplyProvider's metered delivery path, which really does refuse a batch it cannot
-- pay for. For the Logistics Centre it is wrong, because a rearm there is FREE — Resupply.cs:131
-- sets its rearm branch from Rearmable.RearmActors membership alone and Rearmable.RearmTick hands
-- out ammunition with no supply consulted (measured in test-vehicle-rearms-at-empty-depot).
--
-- So a dry tank beside a Centre holding less than one batch was left STANDING STILL, flagged for a
-- supply truck that can never serve a vehicle at all, next to a depot that would have refilled it
-- completely. That is stranding — the exact bug the branch which introduced the pick was written to
-- fix — and this scenario is that case.
--
-- WHY 10 SUPPLY. The abrams' single pool is SupplyValue 30 (vehicles-america.yaml), so 1..29 is the
-- band where a depot is stocked yet unaffordable. 10 sits in the middle of it. It must be ABOVE
-- zero: ChooseResupplier filters candidates on CurrentSupply > 0 upstream of everything here, so a
-- zeroed depot is not a candidate on either arm and the run would measure that filter instead.
--
-- WHY THERE IS NO TRUCK ON THE MAP. The pre-fix disposition is HoldAndFlag, which means "stand still
-- and raise NeedsResupply so a Hunt-stance truck comes to me". A truck would answer the flag and
-- rearm the tank, turning the RED arm green for a reason that has nothing to do with the pick. Its
-- absence is also the honest case: no truck in this mod names a vehicle clientele, so for armour
-- that flag is never answered by anything.
--
-- RED IS THE PRE-FIX ENGINE, not a YAML pin. See rules.yaml for why, and for what RED looks like.
--
-- BOTH ARMS MUST BE RE-RUN AFTER THE PRIMING MOVE WAS ADDED. The first pair predates it and proves
-- nothing: neither arm ever dispatched, because neither tank ever became idle.

-- THE PRIMING MOVE, and why the scenario is worthless without it. AmmoPool's idle dispatcher hangs
-- off INotifyBecomingIdle, which Actor.Tick raises only on the !wasIdle -> IsIdle TRANSITION — and
-- `wasIdle` is recomputed from IsIdle at the top of every tick (Actor.cs:318). A unit placed on the
-- map with no activity is therefore idle ALREADY on its first tick, never transitions, and never
-- asks for resupply even once. The first cut of this scenario did exactly that: both arms sat at
-- ammo=0 moved=false, and the RED was right for entirely the wrong reason.
--
-- So the tank is given one cell of movement to finish. Ending it is a real transition into idle,
-- which is the state a vehicle reaches in ordinary play when it completes an order dry.
local PrimeCell = CPos.New(18, 16)

local DeadlineSeconds = 45
local AmmoPoolName = "primary-ammo"
local PoorLoad = 10

local pollCount = 0
local peakAmmo = 0
local minSupply = PoorLoad
local minDepotDistance = 999

WorldLoaded = function()
	TestHarness.FocusBetween(Tank, Depot)
	TestHarness.Select(Tank)

	Test.SetSupply(Depot, PoorLoad)

	local load = Test.GetSupply(Depot)
	if load ~= PoorLoad then
		Test.Fail(string.format(
			"setup failed: the depot holds %d supply, not the %d this test needs to sit inside the " ..
			"stocked-but-unaffordable band", load, PoorLoad))
		return
	end

	local startingAmmo = Tank.AmmoCount(AmmoPoolName)
	if startingAmmo ~= 0 then
		Test.Fail(string.format(
			"setup failed: the tank starts with %d rounds, so it is not dry and AutoRearmIfDry never " ..
			"runs at all", startingAmmo))
		return
	end

	-- One cell east, AWAY from the depot, so finishing it cannot be mistaken for the errand.
	Tank.Move(PrimeCell)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		-- A tank that has left the world has EVACUATED, which is a different disposition entirely and
		-- must not be mistaken for either outcome. Note IsDead is true for an out-of-world actor, so
		-- this catches the refund departure as well as an actual death.
		if Tank.IsDead then
			return "fail: the tank left the world -- it evacuated for a refund rather than driving to " ..
				"the depot, so this run measured the evacuation fallback and not the affordability pick"
		end

		if Depot.IsDead then return "fail: the depot died or despawned" end

		local ammo = Tank.AmmoCount(AmmoPoolName)
		local supply = Test.GetSupply(Depot)
		local cell = Tank.Location

		-- Chessboard distance to the depot. The corroborating observable: a tank that was dispatched
		-- closes on it, a stranded one never gets nearer than the priming cell.
		local dx = cell.X - Depot.Location.X
		local dy = cell.Y - Depot.Location.Y
		if dx < 0 then dx = -dx end
		if dy < 0 then dy = -dy end
		local dist = dx > dy and dx or dy

		if ammo > peakAmmo then peakAmmo = ammo end
		if supply < minSupply then minSupply = supply end
		if dist < minDepotDistance then minDepotDistance = dist end

		pollCount = pollCount + 1
		if pollCount % 50 == 0 then
			-- Live numbers here, never in the failure string, which Lua evaluates eagerly at
			-- registration and would report the starting values for the whole run.
			print(string.format("[poor-depot] poll=%d ammo=%d peak=%d supply=%d minSupply=%d dist=%d minDist=%d",
				pollCount, ammo, peakAmmo, supply, minSupply, dist, minDepotDistance))
		end

		-- Ammunition alone is the verdict, and it is already exclusive: the tank cannot be reached by
		-- the depot's metered push arm (it does not declare replenish-vehicles) and has no
		-- ReloadAmmoPool of its own, so a round can only have come from Resupply's rearm branch —
		-- which means it was dispatched and it arrived. minDist in the trace is the corroboration,
		-- deliberately NOT part of the predicate: the priming move makes a bare "did it move" test
		-- true for free, and a distance threshold would be a second thing to get wrong.
		return ammo > 0
	end, "The dry tank never drove to the depot. Pre-fix that is exactly right and expected: " ..
		"ChooseAffordableResupplier returns null for a depot holding less than one batch, so " ..
		"DecideAutoDisposition falls through to HoldAndFlag and the tank stands still beside a depot " ..
		"that would have rearmed it for nothing. Post-fix the DockedCondition early-out in " ..
		"HostCanAffordSomethingWeNeed makes that depot serviceable and the trip happens.")
end
