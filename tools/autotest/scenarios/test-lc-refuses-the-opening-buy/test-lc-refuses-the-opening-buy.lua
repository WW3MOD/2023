-- THE OPENING MUST BUY NEITHER A LOGISTICS CENTRE NOR A SUPPLY TRUCK.
--
-- USER REPORT 2026-09-03: "We definitely do not need them at the start of the game, as in this game
-- they bough one almost right away. It is expensive and has no purpose in the beginning because all
-- units are fully armed... The bots now also bought two supply trucks right after they bought the
-- LC, and they are also useless in the beginning."
--
-- THIS IS A NEGATIVE ASSERTION, which has one characteristic failure mode worth naming up front: a
-- test that can only ever fail by SEEING something is passed for free by a bot that is not running
-- at all. Two guards against that, both required to pass:
--   1. The bot must still be BUYING — it has to field at least one unit it did not start with
--      inside the window. A bot whose production is dead proves nothing about a purchase gate, and
--      without this line a broken UnitBuilder, a mis-set RequiresCondition or a stalled Vehicle
--      queue would all read as a clean pass.
--   2. The forbidden purchases must not appear.
--
-- WHAT COUNTS AS THE PURCHASE. `lccv` is the thing bought; `logisticscenter` is what it becomes, and
-- the LCCV is DISPOSED by its own transform, so sampling only for `lccv` misses a Centre that was
-- bought and deployed between two samples. Both are checked every tick.
--
-- WHY 2400 TICKS. The shipped gate bought at its FIRST evaluation — ScanInterval 100, so ~6 s in —
-- and the reported game had a truck immediately after. 2400 is 24 evaluations. If the gate is going
-- to fire at all on a full-ammo army parked on its own beachhead, it has fired well inside this.
local WindowTicks = 2400

local elapsed = 0
local startingUnits = 0
local sawProduction = false

-- Every rearmable/mobile thing the bot owns, so "is it still buying" does not depend on which type
-- the composition lane happens to pick this run.
local function unitCount(p)
	return #p.GetActors()
end

WorldLoaded = function()
	local usa = Player.GetPlayer("USA-bot")
	TestHarness.FocusBetween(OwnSR, OpponentSR)

	startingUnits = unitCount(usa)

	TestHarness.AssertWithin(WindowTicks / TestHarness.TicksPerSecond, function()
		elapsed = elapsed + 1

		-- (2) THE FORBIDDEN PURCHASES. Checked first and every tick: a settled fail, since a bought
		-- LCCV does not un-buy itself, so there is no reason to burn the rest of the window.
		local lccvs = usa.GetActorsByType("lccv")
		local centers = usa.GetActorsByType("logisticscenter")
		if #lccvs > 0 or #centers > 0 then
			return "the bot bought a Logistics Center in the OPENING (lccv=" .. #lccvs
				.. " logisticscenter=" .. #centers .. ") at tick " .. elapsed
				.. ". Every unit is 2-4 cells from the SR at full ammo, so the demand model should read"
				.. " forward-value 0 / need 0 and refuse. Read debug.log for `[logistics] refuse-buy`:"
				.. " if it is ABSENT the gate never ran (check RequireDemand and that"
				.. " LogisticsCenterBotModule@experimental is enabled); if it is PRESENT read which"
				.. " term was non-zero — fwd-value, need-permille or fwd-cells."
		end

		local trucks = usa.GetActorsByType("truk")
		if #trucks > 0 then
			return "the bot bought a supply truck in the OPENING (truk=" .. #trucks .. ") at tick "
				.. elapsed .. " with every soldier at full ammo. The user's bar is"
				.. " FirstTruckNeedThreshold 0.5 — 'unless at least one soldier is below half ammo'."
				.. " Read debug.log for the `[composition] census` line: `ammo-bar=` must read 0.5"
				.. " while `held-first-truck=False`. If it reads 0.05 the override did not apply —"
				.. " check ai-america.yaml FirstTruckNeedThreshold and the blank-line/case rules for"
				.. " MiniYaml merges."
		end

		-- (1) THE LIVENESS GUARD. Without it a bot that never produced anything would pass.
		if not sawProduction and unitCount(usa) > startingUnits then
			sawProduction = true
			Media.Debug("[lc-opening-test] bot production confirmed live at tick " .. elapsed)
		end

		if elapsed >= WindowTicks then
			if not sawProduction then
				return "INCONCLUSIVE, reported as a failure on purpose: the bot bought NOTHING AT ALL"
					.. " in " .. WindowTicks .. " ticks (started with " .. startingUnits
					.. " actors and never exceeded it), so its refusal to buy a Logistics Center or a"
					.. " truck says nothing about the demand gate. Check that the USA-bot player is"
					.. " actually running the experimental profile and that its Vehicle/Infantry"
					.. " queues are delivering."
			end

			Media.Debug("[lc-opening-test] PASS no lccv, no logisticscenter and no truk in "
				.. WindowTicks .. " ticks, with production confirmed live")
			return true
		end

		return nil
	end, function()
		-- Unreachable in normal operation: the predicate returns true at WindowTicks, which is the
		-- same budget AssertWithin is given. Present so a harness/tick-rate mismatch is legible
		-- rather than silent — TicksPerSecond is 25 in the harness against an engine 16/s, so the
		-- two clocks are NOT the same and this is the line that says so if they drift.
		return "AssertWithin timed out before the scenario's own " .. WindowTicks
			.. "-tick window elapsed (reached " .. elapsed .. "). This is a harness clock mismatch,"
			.. " not a bot-behaviour result — do not read it as a pass or a fail of the gate."
	end)
end
