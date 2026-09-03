-- THE BOT MUST SEND A TRUCK TO REFILL ITS OWN DRAINED LOGISTICS CENTER.
--
-- USER RULING 2026-09-03: "Bots needs to learn how to resupply the LC (I think that should work now by
-- sending a truck to it, to transfer supplies to the LC from the truck. But make sure that works both
-- for bots and humans)."
--
-- WHAT IS ACTUALLY NEW. The transfer already worked for a human: LOGISTICSCENTER carries
-- AbsorbsSupplyCache so DropsSupplyCache.ResolveOrder accepts a DeliverSupply order, TRUK carries
-- DropsSupplyCache so it can issue one, and Activities/DeliverSupply.cs performs the transfer straight
-- into the Centre's stock. No bot module ever issued that order. The bot now issues THE SAME ORDER, so
-- this scenario exercises the human path as a side effect of testing the bot's decision — which is the
-- whole reason the bot shares the order rather than mirroring it.
--
-- PASS, in two parts, and the second part is the one that makes this worth running:
--   1. The Centre's stock RISES above where the drain left it. That is the transfer landing.
--   2. The NEAR truck is the one that moved. The dispatch ranks distance-dominant, and a run where the
--      far truck went instead is a ranking regression that "the Centre got refilled" would hide.
--
-- WHY THE DRAIN IS SCRIPTED. SupplyProvider has no starting-stock override in YAML, and a Centre is
-- born FULL (2250/2250). A scenario that waited for real consumption to draw it below half would be
-- measuring the ammo economy, not this dispatch. Test.SetSupply exists for exactly this staging.

-- Drain the Centre to 200 of 2250 (~89 per mille) — well under
-- CenterRestockThresholdPerMille (500), and low enough that a full 750 truck has ample headroom to
-- deliver into, so MinDeliverySupply (250) cannot be what refuses.
local DrainTo = 200

-- Must cover: up to one ScanInterval (100 ticks) before LogisticsCenterBotModule evaluates, the
-- DeliverSupply order resolving into a drive, the ~4-cell drive itself, and RestockWaitTicks (25) of
-- settling before the transfer executes. 1800 is generous against that chain; the scenario passes as
-- soon as the stock rises, so a comfortable budget costs nothing on a good run.
local WindowTicks = 1800

local elapsed = 0
local nearStart = nil
local farStart = nil
local nearMoved = false
local farMoved = false
local drained = false

WorldLoaded = function()
	local usa = Player.GetPlayer("USA-bot")
	TestHarness.FocusBetween(OwnSR, BotCentre)

	nearStart = NearTruck.Location
	farStart = FarTruck.Location

	-- STAGE THE NEED. Asserted rather than assumed: if SetSupply silently did nothing (test mode off,
	-- trait missing) the Centre stays full, the bot correctly declines to dispatch, and the run would
	-- look like a dispatch failure. Checking the drain took is what separates those two.
	Test.SetSupply(BotCentre, DrainTo)

	Trigger.AfterDelay(2, function()
		local now = Test.GetSupply(BotCentre)
		if now < 0 then
			Test.Fail("Test.GetSupply returned -1 for the Logistics Center — it has no SupplyProvider,"
				.. " or test mode is not active. The scenario never reached its own premise.")
			return
		end

		if now > DrainTo + 50 then
			Test.Fail("the Centre was not drained: Test.SetSupply(" .. DrainTo .. ") left it at " .. now
				.. ". Without the drain the Centre is above CenterRestockThresholdPerMille and the bot is"
				.. " RIGHT to send nothing, so this run would measure nothing.")
			return
		end

		drained = true
		Media.Debug("[lc-restock-test] Centre drained to " .. now .. "/2250; watching for a dispatch")
	end)

	TestHarness.AssertWithin(WindowTicks / TestHarness.TicksPerSecond, function()
		elapsed = elapsed + 1

		if not drained then
			return nil
		end

		-- Which truck actually left its start cell. Recorded independently of the refill so the timeout
		-- message can separate "nobody was dispatched" from "the wrong truck was dispatched" — two very
		-- different defects that look identical from the Centre's stock alone.
		if not nearMoved and not NearTruck.IsDead and NearTruck.Location ~= nearStart then
			nearMoved = true
			Media.Debug("[lc-restock-test] the NEAR truck is moving at tick " .. elapsed)
		end

		if not farMoved and not FarTruck.IsDead and FarTruck.Location ~= farStart then
			farMoved = true
			Media.Debug("[lc-restock-test] the FAR truck is moving at tick " .. elapsed)
		end

		local now = Test.GetSupply(BotCentre)
		if now > DrainTo + 50 then
			-- (2) THE RANKING. The refill happened; now check it was the right truck. Distance dominates
			-- the dispatch rank, and the near truck is 4 cells out against the far one's ~12.
			if farMoved and not nearMoved then
				return "the Centre was refilled to " .. now .. ", but the FAR truck made the delivery and"
					.. " the near one never moved. The transfer works; the dispatch RANKING does not."
					.. " LogisticsCenterRestockMath.DispatchRank is distance-dominant, so the near truck"
					.. " should always win here. Read debug.log for `[logistics] deliver truck=` and"
					.. " compare the actor id against the near truck's."
			end

			Media.Debug("[lc-restock-test] PASS Centre rose from " .. DrainTo .. " to " .. now
				.. " at tick " .. elapsed .. "; near-moved=" .. tostring(nearMoved)
				.. " far-moved=" .. tostring(farMoved))
			return true
		end

		if elapsed >= WindowTicks then
			if not nearMoved and not farMoved then
				return "NO TRUCK WAS EVER DISPATCHED in " .. WindowTicks .. " ticks, with the Centre at "
					.. now .. "/2250 — far below CenterRestockThresholdPerMille (500 per mille = 1125)."
					.. " Read debug.log for `[logistics] deliver truck=`: if it is ABSENT the dispatch"
					.. " never fired (check RestockCenters, SupplyTruckActorTypes and that the trucks"
					.. " are not claimed by SupplyFollowerBotModule — a claim by ANOTHER module makes"
					.. " them ineligible here by design). If it is PRESENT the order was issued but the"
					.. " drive never happened, which points at DropsSupplyCache.ResolveOrder rejecting"
					.. " it — the host must carry AbsorbsSupplyCache."
			end

			return "a truck was dispatched (near-moved=" .. tostring(nearMoved) .. " far-moved="
				.. tostring(farMoved) .. ") but the Centre never rose above " .. DrainTo .. " (now "
				.. now .. ") within " .. WindowTicks .. " ticks. The DECISION works and the TRANSFER did"
				.. " not. Check Activities/DeliverSupply.cs: SupplyTransferMath.AmountToDeliver returns 0"
				.. " when the truck never `arrived`, and DeliverSupply logs `[supply] deliver-refused ..."
				.. " reason=never-arrived` in that case."
		end

		return nil
	end, function()
		return "AssertWithin timed out before the scenario's own " .. WindowTicks
			.. "-tick window elapsed (reached " .. elapsed .. "). Harness clock mismatch, not a"
			.. " bot-behaviour result."
	end)
end
