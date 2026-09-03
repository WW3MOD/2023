-- GUARD. This scenario grades the mod and carries a real verdict: Test.Pass or Test.Fail.
--
-- =====================================================================================
-- WHAT IS UNDER TEST, AND WHAT IS NOT
-- =====================================================================================
-- Accumulated purchase ranks: every buildable combat type banks free veterancy over time, and the
-- next purchase of that type arrives at the highest rank banked. The arithmetic is already covered
-- by 45 NUnit cases over pure functions (RankAccrualTest). What those cannot reach is the
-- INTEGRATION, and that is the whole reason this scenario exists:
--
--   * that the timer actually runs in a live world and fills the bank,
--   * that the banked rank is handed to a real produced actor, through the real queue,
--   * that it is SPENT rather than granted forever,
--   * that evacuating a ranked unit really returns that rank to the bank.
--
-- The buy-menu chevrons are NOT tested here and no scenario should try. They are drawn by
-- ProductionPaletteWidget, which a headless autotest never runs; a scenario "covering" them would
-- go green whether or not a single pixel appeared. That half needs a human looking at the menu.
--
-- =====================================================================================
-- HISTORY: THIS SCENARIO PREVIOUSLY DIED BEFORE TESTING ANYTHING
-- =====================================================================================
-- The first version died on its first order with "Actor 'supplyroute' does not define a property
-- 'Build'", so NONE of its assertions had ever executed. Three separate stock-OpenRA scripting
-- APIs it was built on do not exist in this mod, and each had to be replaced:
--
--   1. `Depot.Build{...}` -- ProductionProperties.Build (ProductionProperties.cs:145) is an ACTOR
--      property whose constructor does self.TraitsImplementing<ProductionQueue>(), so it exists
--      only on an actor that itself holds queues. In WW3MOD the Supply Route is a spawn point and
--      the queues live on the PLAYER, so the property is simply absent from that actor. Replaced
--      with Test.QueueProduction, the mod's own seam, which also avoids the player-level Build's
--      documented "will fail to work when called during the first tick" hazard.
--   2. `tank.Sell()` -- SellableProperties requires SellableInfo, and in this mod Sellable is on
--      STRUCTURES ONLY (every hit is structures*.yaml; the naval ones are commented out). A unit
--      leaves by the Evacuate command, which is not a Lua property at all -- there is nothing
--      named Evacuate anywhere under engine/OpenRA.Mods.Common/Scripting/. It is a raw order
--      string reached through the command bar, so it is driven here the way
--      test-evac-queued-after-waypoints drives it: TestHarness.Select then Test.PressHotkey.
--   3. Reading the bank at all was impossible, so every assertion had to infer state from produced
--      unit levels. Test.GetRankStock was added for this scenario (TestGlobal.cs) and is what makes
--      the failure messages below able to name a cause instead of a symptom.
--
-- The generalisation is in WORKSPACE/DISCOVERIES.md: this is a total conversion, so a stock OpenRA
-- binding is a bad prior. Check TestGlobal.cs first.
--
-- =====================================================================================
-- READING A FAILURE: DOES A RED INDICT THE FEATURE OR THIS SCENARIO?
-- =====================================================================================
-- Every failure string below answers that on its own, because the run artefacts cannot be trusted
-- to answer it afterwards -- result.json lists screenshots that --hidden never wrote, the copied
-- debug.log has been observed to come from a different game entirely, and lua.log has come back
-- empty. Only the verdict string and live stdout survive, so everything worth knowing is either in
-- the verdict or printed by Progress() below.
--
-- Each message is prefixed with who is at fault:
--   SCENARIO / GATE n  -- this scenario or the harness is broken; the feature is NOT indicted.
--   PHASE x            -- the feature misbehaved, and the message names the code to look at.
--
-- The three gates are established BEFORE the feature claim that depends on them:
--   Gate 1  the feature is present and abrams accrues     -> else every reading is a meaningless 0
--   Gate 2  a purchase completes at all                   -> separates "no unit" from "wrong rank"
--   Gate 3  the Evacuate keypress is consumed by a widget -> separates "no order" from "no credit"
--
-- =====================================================================================
-- THE SCHEDULE
-- =====================================================================================
-- This scenario runs the SHIPPED configuration -- rules.yaml overrides nothing. abrams costs 2500,
-- so its build time is 2500/10 = 250 ticks, and at the shipped Rank1IntervalMultiplier of 500
-- percent ("every 5 units built can get rank 1", the user's rule) the rank-1 interval is
-- 5 x 250 = 1250 ticks. Grants land at 1250, 2500, 3750. The cap is 3 and is never reached here.
--
--   Phase A  order at tick 60.   Bank is empty (first grant is at 1250).  EXPECT level 0.
--                                Guards against handing out rank for free.
--   Phase B  order at tick 1300. One grant has landed, unspent.           EXPECT level 1.
--                                Proves accrual -> queue -> delivered actor, and SPENDS the bank.
--   Phase C  evacuate B's tank, then order once the credit is seen.       EXPECT level 1.
--
-- Phase C is driven by OBSERVATION, not by the clock. The first version guessed fixed ticks for
-- the evacuation, which cannot be right: RotateToEdge's duration depends on where the unit happens
-- to be standing when it starts. Instead the poller watches the bank and attributes a rise to the
-- evacuation only when it lands within three ticks of the tank's disposal -- the credit is raised
-- from INotifySold.Sold at exactly that moment. The one reading this cannot separate is an accrual
-- grant arriving simultaneously with the evacuation; the schedule keeps them apart by fitting the
-- whole of phase C inside the 1250-tick gap between grants.
--
-- ONE-TICK RACE, worth knowing before editing any assertion here. A purchase SPENDS the bank on the
-- tick the unit is produced, but DrainPending deliberately defers reading that unit's level to the
-- following poll. So for exactly one tick the bank reads 0 while `#produced` has not yet counted
-- the unit that emptied it. Any assertion phrased as "the bank is empty, therefore X" will fire
-- inside that window; phrase timer assertions against the RISE HISTORY (`stockRises`), which a
-- spend never rewrites, and spend assertions against `produced[i].bankAfter`, sampled at delivery.

-- SHIPPED values, not staged ones -- rules.yaml deliberately overrides nothing. Kept as an
-- arithmetic derivation rather than a literal so the failure strings below quote whatever is
-- actually configured; a hardcoded literal here is what made the previous run's verdict quote a
-- multiplier the mod does not ship.
local BuildTicks = 250              -- abrams: 2500 cost / 10
local Rank1Multiplier = 500         -- RankAccumulationInfo.Rank1IntervalMultiplier default
local RankInterval = BuildTicks * Rank1Multiplier / 100   -- 1250

local PhaseATick = 60

-- Shortly after the first grant lands at RankInterval.
local PhaseBTick = RankInterval + 50

-- The deadline exists to stop a hung run, not to grade timing: everything actually graded is
-- latched by the poller as it happens.
local DeadlineTicks = 5000

local Cash = 200000

-- Budget in TICKS and divide back through the harness constant. TestHarness.TicksPerSecond is 25
-- while the mod runs at Timestep 60 = 16.67 ticks/second; the constant is deliberately wrong and
-- is pinned by AutotestTickRateTest.cs, so anything sized in "seconds" here would silently mean
-- something else.
local function ticks(t) return t / TestHarness.TicksPerSecond end

local USA = nil

local now = 0

-- Sampled once at tick 0, before anything can have changed it, so it can only report static
-- wiring. -1/-2/-3 are the binding's "cannot answer" codes; see Test.GetRankStock.
local initialProbe = nil

-- Produced abrams, in order of appearance.
local produced = {}
local phaseOfProduction = { "A", "B", "C" }

local stockNow = 0
local stockPrev = 0
local stockRises = {}          -- every tick at which rank-1 stock rose, with the new value

local tankB = nil
local tankBGoneTick = nil
local evacPressConsumed = nil
local evacPressTick = nil
local creditTick = nil
local phaseCOrdered = false

local function Stock()
	return Test.GetRankStock(USA, "abrams", 1)
end

local function RisesText()
	if #stockRises == 0 then
		return "(the bank never rose at all)"
	end

	local parts = {}
	for i = 1, #stockRises do
		parts[#parts + 1] = "t=" .. stockRises[i].tick .. "->" .. stockRises[i].to
	end

	return table.concat(parts, ", ")
end

local function ProducedText()
	if #produced == 0 then
		return "(nothing was ever produced)"
	end

	local parts = {}
	for i = 1, #produced do
		local tag = phaseOfProduction[i] or ("#" .. i)
		parts[#parts + 1] = tag .. ": level " .. produced[i].level .. " at t=" .. produced[i].tick
			.. " bankAfter=" .. produced[i].bankAfter
	end

	return table.concat(parts, ", ")
end

local function EndOfRun()
	return "produced [" .. ProducedText() .. "]; bank rises [" .. RisesText() .. "]; "
		.. "evacConsumed=" .. tostring(evacPressConsumed) .. " tankGone=" .. tostring(tankBGoneTick)
		.. " creditTick=" .. tostring(creditTick)
end

-- The AssertWithin failure string is evaluated EAGERLY at registration (see AUTOTEST.md), so
-- counters interpolated into it would report their initial values forever. These prints are the
-- only honest running record of what the run actually did.
local function Progress()
	local b = "none"
	if tankB ~= nil then
		b = tankB.IsDead and "gone" or "alive"
	end

	print("[rank] t=" .. now .. " stock=" .. tostring(stockNow) .. " produced=" .. #produced
		.. " tankB=" .. b .. " evacConsumed=" .. tostring(evacPressConsumed)
		.. " creditTick=" .. tostring(creditTick))
	Trigger.AfterDelay(50, Progress)
end

-- Actors arrive through Trigger.OnProduction on the Supply Route. ProductionFromMapEdge raises
-- INotifyProduction on the producer (ProductionFromMapEdge.cs:200-202) and ScriptTriggers
-- implements it, so this fires for every Supply Route delivery. Preferred over polling
-- GetActorsByType because Lua exposes no stable actor identity to de-duplicate a poll with:
-- ActorID is C#-only and there is no __tostring on the wrapper, so a "have I seen this one"
-- table cannot be keyed reliably.
--
-- The level is read on the NEXT tick rather than inside the callback. The callback runs from
-- inside CreateActor's frame-end task, and whether GainsExperience.Created has already consumed
-- the levels init at that instant is an ordering detail this scenario should not depend on.
local pending = {}

local function OnProduced(_, unit)
	pending[#pending + 1] = unit
	print("[rank] produced #" .. (#produced + #pending) .. " at tick " .. now)
end

local function DrainPending()
	for i = 1, #pending do
		local unit = pending[i]
		-- The bank AT the moment of delivery is recorded alongside the level, because the pair is
		-- what makes a red self-diagnosing: "level 1, bank 1->0" is a working spend, "level 1, bank
		-- unchanged" is a grant that was never consumed, and "level 0, bank 1" is a delivery that
		-- never read the bank. Printing only one of the two is what made the previous run's verdict
		-- unreadable.
		local bank = Stock()
		produced[#produced + 1] = { level = unit.Level, tick = now, bankAfter = bank }
		print("[rank] unit #" .. #produced .. " delivered at tick " .. now
			.. " level=" .. unit.Level .. " bankAfterDelivery=" .. bank)

		if #produced == 2 then
			tankB = unit
		end
	end

	pending = {}
end

local CreditWindow = 3

local function TrackTankGone()
	if tankB ~= nil and tankBGoneTick == nil and tankB.IsDead then
		tankBGoneTick = now
		print("[rank] tankB disposed at tick " .. now)
	end
end

local function TrackStock()
	stockNow = Stock()
	if stockNow > stockPrev then
		stockRises[#stockRises + 1] = { tick = now, to = stockNow }
		print("[rank] bank rose to " .. stockNow .. " at tick " .. now)
	end

	stockPrev = stockNow
end

-- Attribute a bank rise to the evacuation rather than to accrual.
--
-- The window is SYMMETRIC around the disposal tick, and that is the point. RotateToEdge.DoSell
-- raises INotifySold.Sold and disposes the actor in the same simulation tick, but this poller runs
-- as a Trigger callback within that tick and its position relative to those two events is not
-- ordered. So the rise can become visible one poll BEFORE IsDead does, and a one-sided
-- "rise at or after disposal" test would silently never attribute it -- reporting "the rank was
-- never returned" for a credit that landed correctly.
--
-- Deliberately NOT "is this tick a multiple of the interval": the accrual trait counts from its own
-- first tick, offset from this poller's counter by an unknown tick or two, so a modulo test would
-- misclassify grants at the boundary. The residual ambiguity is an accrual grant landing inside the
-- window; the schedule avoids it by fitting phase C into the gap between grants.
local function AttributeCredit()
	if tankBGoneTick == nil or creditTick ~= nil then
		return
	end

	for i = 1, #stockRises do
		local t = stockRises[i].tick
		if t >= tankBGoneTick - CreditWindow and t <= tankBGoneTick + CreditWindow then
			creditTick = t
			print("[rank] attributed the bank rise at tick " .. t .. " to the evacuation")
			return
		end
	end
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")

	if Depot == nil then
		Test.Fail("SCENARIO: the map's Supply Route actor 'Depot' was not found, so nothing can be "
			.. "produced. This is a map defect, not a feature defect.")
		return
	end

	-- Enough that no phase can fail for want of money rather than for want of a rank.
	USA.Cash = Cash

	Camera.Position = WPos.New(33 * 1024, 24 * 1024, 0)

	initialProbe = Stock()

	-- Registered before the first order, so no delivery can be missed.
	Trigger.OnProduction(Depot, OnProduced)

	UserInterface.SetMissionText(
		"RANK ACCUMULATION: buy at t=" .. PhaseATick .. " (expect rank 0), at t=" .. PhaseBTick
		.. " (expect rank 1 from the t=" .. RankInterval .. " grant), then evacuate that tank and "
		.. "buy again (expect rank 1 from the credit).")

	Trigger.AfterDelay(PhaseATick, function()
		Test.QueueProduction(USA, "abrams", 1)
		print("[rank] phase A ordered at tick " .. now .. " with stock " .. tostring(Stock()))
	end)

	Trigger.AfterDelay(PhaseBTick, function()
		Test.QueueProduction(USA, "abrams", 1)
		print("[rank] phase B ordered at tick " .. now .. " with stock " .. tostring(Stock()))
	end)

	Trigger.AfterDelay(50, Progress)

	TestHarness.AssertWithin(ticks(DeadlineTicks), function()
		now = now + 1
		DrainPending()
		TrackTankGone()
		TrackStock()
		AttributeCredit()

		-- ---------------------------------------------------------------------------
		-- GATE 1: the feature is present, and abrams is a type that accrues.
		-- If this is wrong every reading below is a plain 0 that looks exactly like a
		-- correct empty bank, so it is checked before anything else.
		-- ---------------------------------------------------------------------------
		if initialProbe == -1 then
			return "GATE 1: the USA player has no RankAccumulation trait, so no rank can ever "
				.. "accrue and nothing in this run is evidence about the feature. Either the trait "
				.. "was removed from the Player actor in rules, or this scenario's rules.yaml "
				.. "overrode it away."
		end

		if initialProbe == -3 then
			return "GATE 1: 'abrams' is not a type that accrues -- RankAccumulation built no bank "
				.. "for it, meaning it is missing Buildable or GainsExperience (the two gates in "
				.. "RankAccumulation.Accrues). Every stock reading here would be a meaningless 0. "
				.. "Fix the actor or pick a different unit; this says nothing about whether accrual "
				.. "works."
		end

		if initialProbe ~= 0 then
			return "GATE 1: the bank for abrams read " .. tostring(initialProbe) .. " at tick 0 "
				.. "instead of 0. The schedule assumes it starts empty, so every expectation below "
				.. "is off by that amount and no phase can be read."
		end

		-- ---------------------------------------------------------------------------
		-- PHASE A: a purchase made before any grant must arrive unranked.
		-- ---------------------------------------------------------------------------
		if #produced < 1 then
			-- Order at 60 + 250 build + the delivery drive. Derived from the interval rather than
			-- hardcoded so it always lands BEFORE the first grant: a phase-A unit arriving after
			-- one had landed could pick up a rank it was never meant to have, quietly turning this
			-- into a different test that still went green.
			if now > RankInterval - 100 then
				return "GATE 2: no unit was produced at all by tick " .. now .. ", though phase A "
					.. "was ordered at tick " .. PhaseATick .. " and abrams builds in " .. BuildTicks
					.. " ticks. Nothing about ranks can be concluded. Most likely the order never "
					.. "entered a queue -- Test.QueueProduction returns silently when "
					.. "FindQueueForActor finds no enabled queue for the Vehicle type -- or the "
					.. "Supply Route could not place the unit. Check the player has a Vehicle queue "
					.. "and that Depot carries Production/ProductionFromMapEdge."
			end

			return false
		end

		if produced[1].level ~= 0 then
			return "PHASE A: the first abrams, bought at tick " .. PhaseATick .. " before any "
				.. "accrual grant could have landed (the first is due at tick " .. RankInterval
				.. "), arrived at level " .. produced[1].level .. " instead of 0. Rank is being "
				.. "handed out that was never banked -- look at PeekRank in the queue seam, not at "
				.. "the accrual timer."
		end

		-- ---------------------------------------------------------------------------
		-- PHASE B: the first grant must land, and must reach the next purchase.
		-- ---------------------------------------------------------------------------
		-- "Did the timer ever fire?" is answered by the RISE HISTORY, never by the instantaneous
		-- bank. This assertion used to read `stockNow == 0` and accused the timer of being dead on a
		-- run whose own log showed it firing on time -- because the phase-B purchase had legitimately
		-- SPENT the stock moments earlier. The spend and the production callback land on the same
		-- tick, but the produced unit is only counted on the FOLLOWING poll (DrainPending defers the
		-- level read by one tick on purpose), so there is a one-tick window where the bank is
		-- already zero and `#produced` has not caught up. The old condition fired inside exactly
		-- that window. A recorded rise is never removed by a spend, so this cannot recur.
		if #stockRises == 0 and now > RankInterval + 100 then
			return "PHASE B: the bank for abrams never rose at any point up to tick " .. now
				.. ", but a rank-1 grant was due at tick " .. RankInterval .. " (interval = "
				.. Rank1Multiplier .. " percent x build time " .. BuildTicks .. "). The accrual "
				.. "timer is not running, or is running at the wrong rate. This assertion is about "
				.. "the TIMER only: it fires just when NO grant was ever observed, so a bank emptied "
				.. "by a purchase can never produce this message."
		end

		if #produced < 2 then
			if now > PhaseBTick + 900 then
				return "GATE 2: phase B was ordered at tick " .. PhaseBTick .. " but no second unit "
					.. "had appeared by tick " .. now .. ". Phase A produced normally, so the queue "
					.. "itself works and something specific to the second order failed. Run state: "
					.. EndOfRun()
			end

			return false
		end

		if produced[2].level ~= 1 then
			return "PHASE B: the abrams bought at tick " .. PhaseBTick .. " arrived at level "
				.. produced[2].level .. ", expected 1. A rank-1 grant had landed and was unspent, "
				.. "so the purchase should have consumed it. This is the core delivery path: "
				.. "PeekRank at ProductionQueue.BuildUnit, the levels init, and GainsExperience "
				.. "consuming it. Bank history: " .. RisesText()
		end

		-- The purchase must have SPENT it. Read from the sample taken AT delivery rather than from
		-- the live bank, so a later accrual grant cannot refill it and mask an unspent rank.
		if produced[2].bankAfter ~= 0 then
			return "PHASE B: the abrams arrived at level 1 correctly, but the bank still read "
				.. produced[2].bankAfter .. " at the moment of delivery (tick " .. produced[2].tick
				.. ") instead of 0, and the next accrual grant was not due until tick "
				.. (RankInterval * 2) .. ". The rank was granted without being spent, so every "
				.. "future purchase of this type would also be free. Look at SpendRank being "
				.. "committed on the successful-Produce branch. Bank history: " .. RisesText()
		end

		if tankB == nil then
			return "SCENARIO: phase B's tank was produced but not captured, so it cannot be "
				.. "evacuated and phase C is unreachable. Run state: " .. EndOfRun()
		end

		-- ---------------------------------------------------------------------------
		-- PHASE C: evacuating a ranked unit returns its rank to the bank.
		-- ---------------------------------------------------------------------------
		if evacPressConsumed == nil then
			-- Press on a later tick than the production, so the command bar's selection-hash
			-- cache has certainly refreshed before it reads evacuateDisabled.
			if now < produced[2].tick + 10 then
				return false
			end

			TestHarness.Select(tankB)
			evacPressConsumed = Test.PressHotkey("Evacuate", false)
			evacPressTick = now
			print("[rank] Evacuate consumed=" .. tostring(evacPressConsumed) .. " at tick " .. now)
			return false
		end

		-- GATE 3. Consumption separates "no order was ever issued" from "the order ran but
		-- credited nothing", and those two are identical when read from the bank alone.
		if evacPressConsumed == false then
			return "GATE 3: the Evacuate keypress was consumed by NO widget, so no evacuation order "
				.. "was ever issued and this run cannot say anything about the evacuation credit. "
				.. "The button is disabled unless a selected actor has DeliversCash with Type "
				.. "Rotation (CommandBarLogic.cs:516), which abrams inherits from ^Vehicle. This is "
				.. "a harness or chrome problem, NOT evidence that the credit is broken."
		end

		-- TrackTankGone above is what latches tankBGoneTick; this only bounds the wait.
		if tankBGoneTick == nil then
			if now > evacPressTick + 1200 then
				return "GATE 3: the Evacuate order was issued (the keypress was consumed at tick "
					.. evacPressTick .. ") but the tank never left the map within 1200 ticks. "
					.. "RotateToEdge either never started or could not path to an edge. Nothing "
					.. "here indicts the rank credit, which only fires on arrival. Activity chain: "
					.. Test.ActivityChain(tankB)
			end

			return false
		end

		-- The credit lands in INotifySold.Sold, raised by RotateToEdge.DoSell at the moment of
		-- disposal, so it is expected on the same tick the tank vanishes.
		if creditTick == nil then
			if now > tankBGoneTick + 60 then
				return "PHASE C: the rank-1 abrams reached the map edge and was disposed at tick "
					.. tankBGoneTick .. ", but the bank never rose -- its rank was not returned. "
					.. "CreditsRankOnEvacuation hangs off INotifySold.Sold and requires "
					.. "GainsExperience with Level >= 1; the tank was level 1, so check the trait "
					.. "is attached (defaults.yaml, inside ^GainsExperience) and that "
					.. "CreditWholeUnit keys the bank by the same actor name. Bank history: "
					.. RisesText()
			end

			return false
		end

		if not phaseCOrdered then
			Test.QueueProduction(USA, "abrams", 1)
			phaseCOrdered = true
			print("[rank] phase C ordered at tick " .. now .. " with stock " .. tostring(stockNow))
			return false
		end

		if #produced < 3 then
			if now > tankBGoneTick + 900 then
				return "GATE 2: phase C was ordered after the evacuation credit landed at tick "
					.. creditTick .. ", but no third unit ever appeared. Run state: " .. EndOfRun()
			end

			return false
		end

		if produced[3].level ~= 1 then
			return "PHASE C: the abrams bought after the evacuation credit arrived at level "
				.. produced[3].level .. ", expected 1. The bank DID rise when the tank got home (at "
				.. "tick " .. creditTick .. "), so the credit itself landed -- but the returned rank "
				.. "was not delivered to the next purchase. That points at the spend path reading a "
				.. "different store than the credit path writes: CreditWholeUnit adds to BonusStock "
				.. "while Peek must read across both. Bank history: " .. RisesText()
		end

		print("[rank] PASS: " .. EndOfRun())
		return true
	end,
	function()
		return "Timed out after " .. DeadlineTicks .. " ticks without reaching a verdict. This is "
			.. "almost always the scenario rather than the feature: some gate above was waiting for "
			.. "something that never happened. End-of-run state: " .. EndOfRun()
	end)
end
