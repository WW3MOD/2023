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
--   * that the banked rank is actually handed to a real produced actor, through the real queue,
--   * that it is SPENT rather than granted forever,
--   * that evacuating a ranked unit really returns that rank to the bank.
--
-- The buy-menu chevrons are NOT tested here and no scenario should try. They are drawn by
-- ProductionPaletteWidget, which a headless autotest never runs; a scenario "covering" them would
-- go green whether or not a single pixel appeared. That half needs a human looking at the menu.
--
-- =====================================================================================
-- WHY actor.Build AND NOT actor.Produce
-- =====================================================================================
-- ProductionProperties has both. Produce is documented as "Build a unit, IGNORING the production
-- queue" — it calls the Production trait directly and never enters ProductionQueue.BuildUnit, which
-- is exactly where the rank is peeked, attached and committed. A scenario written on Produce would
-- pass with the entire feature deleted. Build is the classic-queue path and does reach the seam.
--
-- =====================================================================================
-- THE SCHEDULE, AND WHY PHASE C IS THE ONE THAT DISCRIMINATES
-- =====================================================================================
-- abrams costs 2500, so its base build time is 2500/10 = 250 ticks. rules.yaml sets
-- Rank1IntervalMultiplier to 400, making the rank-1 interval exactly 4 x 250 = 1000 ticks. Grants
-- therefore land at ticks 1000, 2000, 3000. The cap is 3, never reached here.
--
--   Phase A, order at tick 100.  Nothing has accrued yet (first grant is at 1000).
--                                EXPECT level 0. Guards against granting rank for free.
--   Phase B, order at tick 1100. One grant has landed and is unspent.
--                                EXPECT level 1. Proves accrual -> queue -> delivered actor.
--                                This also SPENDS the bank, which sets up phase C.
--   Phase C, sell that tank, then order again at tick 1750.
--                                The bank was emptied by phase B and the next accrual grant is not
--                                until 2000. So a rank-1 tank here can ONLY have come from the
--                                evacuation credit.
--                                EXPECT level 1.
--
-- Phase C is the load-bearing one precisely because the window 1100..2000 contains no accrual
-- grant. If the evacuation credit silently did nothing, phase C yields a level-0 tank and fails.
-- Placing the third order at 1750 rather than nearer 2000 is deliberate: it leaves 250 ticks of
-- slack before an accrual grant could confound the reading.

local BuildTicks = 250          -- 2500 cost / 10
local RankInterval = 1000       -- Rank1IntervalMultiplier 400 x 250

local PhaseATick = 100
local PhaseBTick = 1100
local SellTick = 1400
local PhaseCTick = 1750
local VerdictTick = 2400

local Cash = 200000

local results = {}
local failures = {}

local rankedTank = nil

local function Note(phase, msg)
	print("[rank] " .. phase .. ": " .. msg)
end

local function Record(phase, actual, expected)
	results[phase] = actual
	if actual ~= expected then
		failures[#failures + 1] = phase .. " expected level " .. expected .. ", got " .. tostring(actual)
	end
	Note(phase, "produced abrams at level " .. tostring(actual) .. " (expected " .. expected .. ")")
end

-- The Build callback hands back the actors that were produced. Reading .Level on the first of them
-- is the entire observation: veterancy is simulation state, so this is a real assertion about the
-- world and not about the UI.
local function OrderTank(phase, expected, keep)
	if Depot.IsDead then
		failures[#failures + 1] = phase .. " could not order: the Supply Route is gone"
		return
	end

	Depot.Build({ "abrams" }, function(units)
		if #units == 0 then
			failures[#failures + 1] = phase .. " produced nothing"
			return
		end

		local tank = units[1]
		Record(phase, tank.Level, expected)
		if keep then
			rankedTank = tank
		end
	end)
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")

	-- Named map actors are exposed as Lua globals, so `Depot` is the supplyroute placed in map.yaml.
	if Depot == nil then
		Test.Fail("The map's Supply Route actor 'Depot' was not found.")
		return
	end

	-- Enough that no phase can fail for want of money rather than for want of a rank.
	USA.Cash = Cash

	Trigger.AfterDelay(PhaseATick, function()
		OrderTank("A (before any grant)", 0, false)
	end)

	Trigger.AfterDelay(PhaseBTick, function()
		OrderTank("B (one grant banked)", 1, true)
	end)

	Trigger.AfterDelay(SellTick, function()
		if rankedTank == nil or rankedTank.IsDead then
			failures[#failures + 1] = "C setup: phase B's tank was not available to evacuate"
			return
		end

		-- Sell routes a mobile unit through RotateToEdge, so this really is "walk off the map and be
		-- recovered", not an instant refund in place. INotifySold.Sold — where the rank credit lands
		-- — only fires once it gets there.
		Note("C setup", "evacuating the rank-1 abrams")
		rankedTank.Sell()
	end)

	Trigger.AfterDelay(PhaseCTick, function()
		if rankedTank ~= nil and not rankedTank.IsDead then
			failures[#failures + 1] =
				"C setup: the tank had not finished evacuating by tick " .. PhaseCTick ..
				"; the credit cannot have landed yet and this run proves nothing"
			return
		end

		OrderTank("C (credit returned by evacuation)", 1, false)
	end)

	Trigger.AfterDelay(VerdictTick, function()
		if #failures > 0 then
			for i = 1, #failures do
				print("[rank] FAIL " .. failures[i])
			end

			Test.Fail(failures[1])
		else
			Test.Pass("Rank accrued, was spent on purchase, and returned on evacuation.")
		end
	end)
end
