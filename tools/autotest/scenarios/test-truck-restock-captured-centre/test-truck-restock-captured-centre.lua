-- AUTO TEST: a truck whose restock Centre changes hands before the transfer must take nothing from it.
--
-- The defect (robustness scout 261007 §11): RestockSupply re-validated its host on arrival only for
-- IsDead / !IsInWorld, while its own comment claimed to guard "captured mid-drive". LOGISTICSCENTER is
-- capturable with OwnerLostAction: ChangeOwner, so a Centre that changes hands is neither dead nor out
-- of the world -- and the truck refilled from the ENEMY's stock. DeliverSupply, the documented mirror,
-- always carried the ownership term. Fix: both now go through SupplyTransferMath.HostStillServes.
--
-- WHY THE OWNER SETTER STAGES THE REAL THING: `Depot.Owner = x` calls Actor.ChangeOwner, the same hop a
-- capture takes. The actor object survives, stays in the world, and the truck's activity still holds a
-- reference to it -- which is exactly the state the missing term had to refuse. It is DEFERRED: the
-- owner changes at frame end (Actor.cs:519-522), so the owner is read back only after a grace, and by
-- InternalName -- Name resolves to the seat occupant ("FreadyFish"), which cost the first run.
--
-- Setup is the cancel scenario's: 40 supply is below RestockThreshold (50) and the truck is on Auto, so
-- the restock drive starts on its own on tick one.
--
-- WHEN THE FLIP HAPPENS -- AND WHY IT MOVED. The first RED run flipped at tick 40, mid-drive, with the
-- guard sabotaged, and the truck NEVER completed the errand: no transfer, no "[supply] restock-refused"
-- line in debug.log (which RestockSupply writes whenever it reaches its arrival check without having
-- arrived), so the activity never got past its MoveTo/Wait children. What ends the drive was NOT found
-- by reading (ruled out: Target generation -- RestockSupply moves to a CELL and uses Target only for the
-- target line; Locomotor blocking -- owner-independent for an ignoreActor building; SmartMove -- the
-- truck has no armament to interrupt with; DropsSupplyCache's dry-move cancel -- exempt on a supply
-- errand; SupplyProvider -- returns early while Restocking). So this file now flips in the window the
-- guard actually exists for: AFTER arrival, while RestockSupply's child is the Wait(RestockWaitTicks=25)
-- settle, i.e. when Test.ActivityChain(Truck) first reads "RestockSupply>Wait". The transfer runs when
-- that Wait runs out, ~25 ticks after the flip. FlipMode = "drive" restores the old mid-drive flip as a
-- DIAGNOSTIC run (the trace below will name what replaced the errand); it is not the verdict mode.
--
-- WHAT A PASS MUST PROVE: not just "nothing was taken" -- a CANCELLED errand also takes nothing, and
-- would go green on broken code. So the errand must be seen to run its settle out: it has to stay in
-- RestockSupply for >= MinSettleTicks after the Wait was first seen, which is only possible if the Wait
-- ran to completion and RestockSupply.Tick then reached its transfer code. An earlier end is reported as
-- INCONCLUSIVE (cancelled), never as a pass.
--
-- PASS = errand ran its full settle after the Centre became Russian, and the Centre lost nothing.
-- FAIL = the Centre lost supply after the flip (pre-fix behaviour), or a SETUP guard trips.
-- INCONCLUSIVE (a fail note starting "INCONCLUSIVE") = the errand never reached the settle, or ended
-- early; the guard was never exercised either way.
--
-- RED RUN. In engine/OpenRA.Mods.Common/Traits/SupplyTransferMath.cs, HostStillServes, change
--     return !hostIsDead && hostIsInWorld && validRelationships.HasRelationship(relationshipToHost);
-- to
--     return !hostIsDead && hostIsInWorld;
-- then `make all` and run this once. Expected note: "fail: the Centre lost 710 supply AFTER it changed
-- hands (truck 40 -> 750) -- the truck refilled from the enemy's stock" (710 = TRUK capacity 750 - 40,
-- assuming the Centre holds >= 710 at the flip; otherwise N = all it held). Revert and rebuild after.
-- (The same edit also turns SupplyTransferMathTest.ACentreCapturedMidDriveNoLongerServes red.)
--
-- lua.log carries a [captured-centre] trace: position, activity chain and both supply levels every
-- TraceEvery ticks, at the flip, and when the errand ends -- so a non-verdict says what the truck did.

local FlipMode = "settle"  -- "settle" (the verdict mode) or "drive" (diagnostic: flip at FlipAtTick)
local DeadlineSeconds = 45
local FlipAtTick = 40      -- drive mode only
local FlipGraceTicks = 25  -- the flip lands at FRAME END, not when the setter returns
local MinSettleTicks = 20  -- of the Wait's 25: an errand ending sooner was cancelled, not completed
local TraceEvery = 50
local StartCell = CPos.New(10, 16) -- Truck's map.yaml Location
local TruckStart = 40              -- Truck's map.yaml Supply

local russia = nil
local flipped = false
local flipTick = nil
local waitSeenAt = nil
local endedAt = nil
local setupError = nil
local depotAtFlip = -1
local truckAtFlip = -1
local lastTrace = -TraceEvery

local function chain()
	return Test.ActivityChain(Truck)
end

local function trace(tag)
	print("[captured-centre] " .. tag .. " tick=" .. DateTime.GameTime .. " truck@" .. tostring(Truck.Location)
		.. " supply=" .. Test.GetSupply(Truck) .. " centre=" .. Test.GetSupply(Depot)
		.. " owner=" .. Depot.Owner.InternalName .. " chain=" .. chain())
end

local function inErrand()
	return string.find(chain(), "RestockSupply", 1, true) ~= nil
end

local function inSettle()
	return string.find(chain(), "RestockSupply>Wait", 1, true) ~= nil
end

local function flip()
	depotAtFlip = Test.GetSupply(Depot)
	truckAtFlip = Test.GetSupply(Truck)
	Depot.Owner = russia
	flipTick = DateTime.GameTime
	flipped = true
	trace("flip(" .. FlipMode .. ")")
end

WorldLoaded = function()
	russia = Player.GetPlayer("Russia")
	TestHarness.FocusBetween(Truck, Depot)
	TestHarness.Select(Truck)

	-- The drive must really be under way at FlipAtTick, or both builds time out identically.
	Trigger.AfterDelay(FlipAtTick, function()
		if Truck.IsDead or Depot.IsDead then return end
		trace("drive-check")

		if not inErrand() or Truck.Location == StartCell then
			setupError = "fail: SETUP -- at tick " .. FlipAtTick .. " the truck was not on a restock drive (at "
				.. tostring(Truck.Location) .. ", chain=" .. chain() .. ")"
			return
		end

		if FlipMode == "drive" then flip() end
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if setupError then return setupError end
		if Truck.IsDead then return "fail: the truck died" end
		if Depot.IsDead then return "fail: the depot died" end

		local now = DateTime.GameTime
		if now - lastTrace >= TraceEvery then
			lastTrace = now
			trace("periodic")
		end

		if waitSeenAt == nil and inSettle() then
			waitSeenAt = now
			trace("settle-start")
		end

		if not flipped then
			-- An allied transfer before the flip would mean the window was missed, not that the guard held.
			if Test.GetSupply(Truck) > TruckStart then
				return "fail: SETUP -- the truck refilled (" .. TruckStart .. " -> " .. Test.GetSupply(Truck)
					.. ") before the Centre changed hands, so the flip came too late"
			end

			if FlipMode == "settle" and waitSeenAt ~= nil then flip() end
			return false
		end

		if Depot.Owner.InternalName ~= "Russia" then
			if now - flipTick <= FlipGraceTicks then return false end
			return "fail: SETUP -- the owner flip did not land within " .. FlipGraceTicks .. " ticks (owner="
				.. Depot.Owner.InternalName .. ")"
		end

		-- A Centre with nothing to give would make "unchanged" true of the broken build too.
		if depotAtFlip <= 0 then
			return "fail: SETUP -- the depot held " .. depotAtFlip .. " at the flip, so a refusal is unobservable"
		end

		local depotNow = Test.GetSupply(Depot)
		if depotNow < depotAtFlip then
			trace("drained")
			return "fail: the Centre lost " .. (depotAtFlip - depotNow) .. " supply AFTER it changed hands (truck "
				.. truckAtFlip .. " -> " .. Test.GetSupply(Truck) .. ") -- the truck refilled from the enemy's stock"
		end

		if inErrand() then return false end

		-- The errand is over. Either its settle ran out (and the transfer code ran and refused), or it was
		-- ended early by something else -- in which case nothing has been tested.
		if endedAt == nil then
			endedAt = now
			trace("errand-ended")
		end

		if waitSeenAt == nil then
			return "fail: INCONCLUSIVE -- the restock errand ended at tick " .. endedAt .. " without ever reaching its "
				.. "settle (it was cancelled or replaced mid-drive, " .. (endedAt - flipTick) .. " ticks after the flip); "
				.. "the arrival guard was never reached. Now: " .. chain()
		end

		local settled = endedAt - waitSeenAt
		if settled < MinSettleTicks then
			return "fail: INCONCLUSIVE -- the restock errand ended " .. settled .. " ticks into its 25-tick settle: it was "
				.. "cancelled, not completed, so the arrival guard was never reached. Now: " .. chain()
		end

		return "pass: settle ran " .. settled .. " ticks (seen tick " .. waitSeenAt .. ", ended tick " .. endedAt
			.. ") with the Centre Russian since tick " .. flipTick .. "; Centre " .. depotAtFlip .. " -> " .. depotNow
			.. ", truck " .. truckAtFlip .. " -> " .. Test.GetSupply(Truck) .. " -- the arrival guard refused the transfer"
	end, function()
		return "INCONCLUSIVE: the restock errand never ended inside the deadline (flipped=" .. tostring(flipped)
			.. ", settle seen=" .. tostring(waitSeenAt) .. ", truck at " .. tostring(Truck.Location) .. ", chain="
			.. (Truck.IsDead and "(dead)" or chain()) .. ") -- see the [captured-centre] trace in lua.log"
	end)
end
