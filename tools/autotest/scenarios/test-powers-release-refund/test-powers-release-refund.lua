-- ASSERTING AUTOTEST -- release a banked strike: full refund, and the upkeep stops.
--
-- THIS IS NOT THE PRODUCTION QUEUE'S CANCEL, and the distinction is the whole reason the scenario
-- exists. ProductionQueue refunds an item IN PROGRESS; the instant Produce succeeds, EndProduction
-- removes it and the queue has no further record of it. Everything measured here happens AFTER that
-- line -- a reservation that is bought, banked, and sitting in the bin -- and it is served by new
-- code (SupportPowerManager.Release), not by the queue.
--
-- FOUR RUNGS:
--   1. REFUND IS THE WHOLE PRICE. `refunded == spent`, exactly. Not "approximately", and not "at
--      least": any fraction below 100% would be discontinuous at completion, because cancelling one
--      tick before the bar fills already returns everything paid (ProductionQueue.cs:638). A partial
--      refund makes holding every purchase at 99% the optimal play forever.
--   2. THE UPKEEP REGISTER CLEARS. Measured by name out of GetUpkeepBreakdown as well as by total,
--      because a total returning to baseline could also mean two errors cancelling.
--   3. THE PROXY IS GONE. A release that refunded the cash and left the actor would look perfect for
--      as long as anyone watched the cash counter and bill 20 per interval forever afterwards.
--   4. THE POWER IS UNFIREABLE. The bin must not still draw it, and ActivateSupportPower must not
--      report `issued` -- otherwise a player could reclaim the money and keep the strike.
--
-- ORDERING TRAP, AND WHY THE RUN SETTLES BEFORE READING. The refund lands SYNCHRONOUSLY inside order
-- resolution; the disposal is queued to the frame-end batch. So there is a window of one tick in
-- which cash has risen and upkeep has not yet fallen. A poll that read both in the same tick would
-- see a half-applied release and report a leak that does not exist.

local ProxyType = "powerproxy.kinzhal"
local PowerPrefix = "KinzhalStrike"
local Price = 4000
local ExpectedUpkeep = 20      -- 4000 * PermilleCost 5 / 1000

local PaydayWindow = 100       -- exactly two paydays at PassiveIncomeInterval 50
local BuyDeadline = 400
local ReleaseDeadline = 60
local SettleTicks = 8
local ObserveTicks = 900

local BaselineStart = 10
local BuyTick = BaselineStart + PaydayWindow

local tick = 0
local Russia
local phase = "baseline"
local finished = false

local cashBaseline0, cashBaseline1 = -1, -1
local baselineNet = -1
local upkeepBaseline = -1

local cashBeforeBuy = -1
local buildStatus = "never-called"
local boughtKey = nil
local bankTick = nil
local cashAtBank = -1
local spent = -1
local upkeepBanked = -1
local breakdownBanked = "never-read"

local releaseStatus = "never-called"
local releaseTick = nil
local cashAfterRelease = -1
local refunded = -1
local upkeepAfter = -1
local breakdownAfter = "never-read"
local proxiesAfter = -1
local binAfter = "never-read"
local refireStatus = "never-called"

local afterWindowStart = nil
local cashAfter0, cashAfter1 = -1, -1
local afterNet = -1

local function n(v)
	if v == nil then
		return "none"
	end

	return tostring(v)
end

local function purse()
	return Russia.Cash + Russia.Resources
end

local function findKey(bin)
	return string.match(bin, "(" .. PowerPrefix .. "_%d+)")
end

local function summary()
	return "BASE net/interval=" .. n(baselineNet) .. " upkeep=" .. n(upkeepBaseline)
		.. " | BUY " .. buildStatus .. " cash " .. n(cashBeforeBuy) .. "->" .. n(cashAtBank)
		.. " (spent " .. n(spent) .. ") key=" .. n(boughtKey) .. "@t" .. n(bankTick)
		.. " upkeep=" .. n(upkeepBanked) .. " breakdown=[" .. breakdownBanked .. "]"
		.. " | RELEASE " .. releaseStatus .. "@t" .. n(releaseTick)
		.. " cash->" .. n(cashAfterRelease) .. " (refunded " .. n(refunded) .. " of " .. n(spent) .. ")"
		.. " upkeep=" .. n(upkeepAfter) .. " breakdown=[" .. breakdownAfter .. "]"
		.. " proxies=" .. n(proxiesAfter) .. " bin=[" .. binAfter .. "]"
		.. " refire=" .. refireStatus .. " net/interval=" .. n(afterNet)
		.. " | observed=" .. tick .. "t"
end

local function finish(fail, why)
	finished = true
	if fail then
		Test.Fail(why .. " || " .. summary())
	else
		Test.Pass(why .. " || " .. summary())
	end
end

local function step()
	tick = tick + 1

	if phase == "baseline" then
		if tick == BaselineStart then
			cashBaseline0 = purse()
			upkeepBaseline = Test.GetUpkeep(Russia)
		elseif tick == BuyTick then
			cashBaseline1 = purse()
			baselineNet = (cashBaseline1 - cashBaseline0) / 2
			cashBeforeBuy = cashBaseline1

			if Russia.Build({ ProxyType }) then
				buildStatus = "queued"
				phase = "banking"
			else
				buildStatus = "refused"
				finish(true, "the Powers queue REFUSED " .. ProxyType .. ", so there is nothing to"
					.. " release. Check SUPPLYROUTE's Production@Local still lists `Powers`.")
			end
		end

		return
	end

	if phase == "banking" then
		local key = findKey(Test.GetSupportPowerBin(Russia))
		if key ~= nil then
			boughtKey = key
			bankTick = tick
			cashAtBank = purse()
			spent = cashBeforeBuy - cashAtBank
			upkeepBanked = Test.GetUpkeep(Russia)
			breakdownBanked = Test.GetUpkeepBreakdown(Russia)

			if upkeepBanked - upkeepBaseline ~= ExpectedUpkeep then
				finish(true, "the strike banked but upkeep moved by "
					.. (upkeepBanked - upkeepBaseline) .. " rather than " .. ExpectedUpkeep
					.. ". There is no point testing that a release STOPS a bill that never started.")
				return
			end

			releaseStatus = Test.ReleaseSupportPower(Russia, boughtKey)
			releaseTick = tick
			if releaseStatus ~= "released" then
				finish(true, "the banked strike could not be released: " .. releaseStatus
					.. ". 'not-releasable' means the power did not opt in with"
					.. " SupportPowerInfo.Releasable, which is what the right-click in the bin also"
					.. " tests before issuing its order -- so the UI would silently do nothing too.")
				return
			end

			phase = "releasing"
		elseif tick - BuyTick > BuyDeadline then
			finish(true, "the purchase was queued and no support power icon ever appeared within "
				.. BuyDeadline .. " ticks, so there was never a reservation to release.")
		end

		return
	end

	if phase == "releasing" then
		-- The order resolves a tick or two after it is issued, and the disposal is a frame-end task
		-- after that. Advance on the OBSERVABLE -- the proxy actually leaving the world -- rather than
		-- on a fixed settle, so this does not silently start reading a half-applied release if the
		-- order path ever gains a tick.
		if #Russia.GetActorsByType(ProxyType) == 0 then
			cashAfterRelease = purse()
			refunded = cashAfterRelease - cashAtBank
			upkeepAfter = Test.GetUpkeep(Russia)
			breakdownAfter = Test.GetUpkeepBreakdown(Russia)
			proxiesAfter = 0
			binAfter = Test.GetSupportPowerBin(Russia)
			refireStatus = Test.ActivateSupportPower(Russia, boughtKey, CPos.New(44, 17))
			afterWindowStart = tick + SettleTicks
			phase = "after"
		elseif tick - releaseTick > ReleaseDeadline then
			proxiesAfter = #Russia.GetActorsByType(ProxyType)
			cashAfterRelease = purse()
			refunded = cashAfterRelease - cashAtBank
			finish(true, "the release order was accepted but the proxy is STILL IN THE WORLD "
				.. ReleaseDeadline .. " ticks later (" .. proxiesAfter .. " alive). The refund may"
				.. " well have landed -- cash moved by " .. n(refunded) .. " -- which is the"
				.. " dangerous half of this failure: the player is paid AND keeps paying upkeep,"
				.. " and nothing on screen says so.")
		end

		return
	end

	if phase == "after" then
		if tick == afterWindowStart then
			cashAfter0 = purse()
		elseif tick == afterWindowStart + PaydayWindow then
			cashAfter1 = purse()
			afterNet = (cashAfter1 - cashAfter0) / 2

			if refunded ~= spent then
				finish(true, "releasing refunded " .. refunded .. " of the " .. spent
					.. " credits actually paid. The refund must be the WHOLE price, and that is"
					.. " arithmetic rather than generosity: cancelling one tick before the purchase"
					.. " completes already returns everything paid, so any lower figure destroys money"
					.. " at the instant the bar fills and makes holding at 99% optimal forever.")
				return
			end

			if upkeepAfter ~= upkeepBaseline then
				finish(true, "upkeep is " .. upkeepAfter .. " after the release, not back at the"
					.. " baseline " .. upkeepBaseline .. ". The register still reads ["
					.. breakdownAfter .. "].")
				return
			end

			if afterNet ~= baselineNet then
				finish(true, "the player's net per interval is " .. n(afterNet) .. " after the"
					.. " release, not back at the baseline " .. n(baselineNet) .. ". The register"
					.. " looks clean and the money does not, which is the version of this bug nobody"
					.. " would notice until the match was lost.")
				return
			end

			if findKey(binAfter) ~= nil then
				finish(true, "the reservation was refunded in full and its icon is STILL in the bin."
					.. " That is a free strike: the player has their money back and the power too.")
				return
			end

			if refireStatus == "issued" then
				finish(true, "the released strike still FIRED when ordered. The icon may be gone from"
					.. " the bin while the power remains reachable by its key -- and every order"
					.. " source, including the bot path, goes through that key rather than the icon.")
				return
			end

			finish(false, "a banked reservation was released for a full refund of " .. refunded
				.. " credits, its upkeep entry cleared (" .. n(upkeepBanked) .. " -> "
				.. n(upkeepAfter) .. "), its proxy left the world, its icon left the bin, and a"
				.. " re-fire attempt was refused with '" .. refireStatus .. "'. The player reclaimed"
				.. " the allocation and did not reclaim the time it sat reserved.")
		end

		return
	end
end

local function loop()
	if not finished then
		step()
	end

	if not finished and tick >= ObserveTicks then
		finish(true, "the run reached its whole-run budget of " .. ObserveTicks .. " ticks in phase '"
			.. phase .. "' without reaching a verdict.")
	end

	if not finished then
		Trigger.AfterDelay(1, loop)
	end
end

WorldLoaded = function()
	Russia = Player.GetPlayer("Russia")
	if Russia == nil then
		Test.Fail("Russia player not found")
		return
	end

	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, loop)
end
