-- AUTO TEST: a Logistics Centre must not salvage for more than the LCCV that made it.
--
-- WHY THIS EXISTS. `logisticscenter` carries `Buildable.Prerequisites: ~disabled`, which
-- gates the build sidebar and nothing else — `Transforms.CanDeploy` (Transforms.cs:93-99)
-- never consults prerequisites, so any player can field one by deploying a 1200-credit
-- LCCV. Selling it paid the full `Valued.Cost` of 3500 in cash PLUS up to five 250-credit
-- technicians from `SpawnActorsOnSell`, i.e. ~4750 out for 1200 in, repeatable with no
-- cooldown. This test runs the whole cycle and asserts the payout is capped.
--
-- WHAT MAKES A GREEN MEANINGFUL HERE. The cap (`total <= LccvCost`) is trivially satisfied
-- by a run that never built anything, so the cap alone would be a false green. The verdict
-- is reachable only after the LC has actually existed and actually paid out: the state
-- machine cannot leave `await-lc` until a logisticscenter is in the world, cannot leave
-- `await-sold` until it has left it, and a payout of 0 fails explicitly. A scenario that
-- silently failed to deploy or to sell times out rather than passing.

local LccvCost = 1200       -- vehicles.yaml, LCCV Valued.Cost — the cap.
local TecnCost = 250        -- infantry.yaml, ^TECN Valued.Cost.
local DeadlineSeconds = 45
local SellGraceTicks = 150  -- ~6s: let the make animation finish so Sellable is enabled.
local SpawnSettleTicks = 30 -- SpawnActorsOnSell creates via AddFrameEndTask; let them land.

local USA                   -- PITFALL: players are NOT bare globals (only map ACTORS are).
                            -- Resolved via Player.GetPlayer in WorldLoaded, as every other
                            -- scenario does. A bare `USA` is nil and throws a fatal Lua error.

local ticks = 0
local phase = "await-lc"
local graceTicks = 0
local settleTicks = 0
local cashAtSell = -1
local payout = -1

local function LcCount() return #USA.GetActorsByType("logisticscenter") end
local function TecnCount() return #USA.GetActorsByType("tecn") end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	if USA == nil then
		Test.Fail("scenario setup: no player named USA")
		return
	end

	TestHarness.FocusBetween(Lccv, Lccv)
	TestHarness.Select(Lccv)

	-- queued = true, so Transforms.DeployTransform skips its CanDeploy pre-check and the
	-- transform activity is queued unconditionally.
	Lccv.Deploy()
	print(string.format("[lc-salvage] deploy ordered, cash=%d", USA.Cash))

	-- PITFALL: AssertWithin's third argument is evaluated EAGERLY at registration, so any
	-- counter interpolated into it reports its starting value forever. Keep it static and
	-- put live numbers in the periodic print below, which lands in lua.log.
	TestHarness.AssertWithin(DeadlineSeconds, function()
		ticks = ticks + 1
		if ticks % 25 == 0 then
			print(string.format("[lc-salvage] t=%d phase=%s cash=%d lc=%d tecn=%d",
				ticks, phase, USA.Cash, LcCount(), TecnCount()))
		end

		if phase == "await-lc" then
			if LcCount() == 0 then return false end
			print("[lc-salvage] logisticscenter exists — LCCV transform bypassed ~disabled")
			phase = "settle"
			return false
		end

		if phase == "settle" then
			graceTicks = graceTicks + 1
			if graceTicks < SellGraceTicks then return false end

			-- Snapshot immediately before the sale. Passive income is zeroed in rules.yaml
			-- and no vehicle or infantry is alive to accrue upkeep, so nothing else can move
			-- this number and the delta is attributable to the sale alone.
			cashAtSell = USA.Cash
			USA.GetActorsByType("logisticscenter")[1].Sell()
			print(string.format("[lc-salvage] sell ordered, cash-before=%d", cashAtSell))
			phase = "await-sold"
			return false
		end

		if phase == "await-sold" then
			if LcCount() > 0 then return false end
			if payout < 0 then
				payout = USA.Cash - cashAtSell
			end

			settleTicks = settleTicks + 1
			if settleTicks < SpawnSettleTicks then return false end

			local tecn = TecnCount()
			local total = payout + tecn * TecnCost
			print(string.format(
				"[lc-salvage] RESULT cash=%d technicians=%d tecn-value=%d total=%d cap=%d",
				payout, tecn, tecn * TecnCost, total, LccvCost))

			-- Non-vacuity: an LC that left the world without paying anything means the cycle
			-- did not happen the way this test claims to measure it.
			if payout <= 0 then
				return "fail: the LC left the world without paying a refund"
			end

			if total > LccvCost then
				return "fail: LC salvage exceeded the cost of the LCCV that made it"
			end

			return true
		end

		return false
	end, "LC deploy/sell cycle did not complete within the deadline")
end
