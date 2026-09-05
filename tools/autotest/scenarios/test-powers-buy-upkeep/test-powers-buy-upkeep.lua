-- ASSERTING AUTOTEST -- buy a strike, be billed for holding it, stop being billed when it is fired.
--
-- THE THREE RUNGS, and why each is a separate question rather than one:
--
--   READY   The purchase must bank a power that is READY, not one that then charges. The three
--           strikes lost their ChargeInterval when they became purchasable (StartFullyCharged with a
--           zero interval), because a power you pay for and then wait for is gated twice for the same
--           thing. `charging:<n>` here is a FAILURE, and it is the failure the whole design exists to
--           avoid -- so it gets its own assertion with its own message rather than being folded into
--           "an icon appeared".
--   BILLED  Upkeep is MEASURED, not read out of the rules file: the run samples the player's net
--           change across a 100-tick window (exactly two paydays at PassiveIncomeInterval 50) before
--           the purchase and again while banked, and the difference between those two rates is what
--           the engine actually charged. A test asserting `GetUpkeep() == 20` would pass on a mod
--           that registered the entry and never billed it.
--   STOPPED And the bill must STOP when the strike is spent. InfersUpkeep unregisters only on
--           INotifyRemovedFromWorld, so this depends entirely on DisposeSelfOnActivate removing the
--           proxy; OneShot hides the icon and leaves the actor. A missing disposal is invisible for
--           the rest of the match and then bankrupts the player, which is why the third payday
--           window is measured rather than assumed.
--
-- WHY A 100-TICK WINDOW AND NOT A SINGLE PAYDAY. Paydays land every PassiveIncomeInterval ticks (50),
-- so ANY 100-tick window contains exactly two of them regardless of where it starts -- no alignment
-- to a payday boundary is needed, and none is attempted. Halving the delta gives the per-interval
-- net, and comparing two such nets cancels the passive income out entirely, so this measurement does
-- not care what the lobby income setting is.
--
-- THE BUY IS DELIBERATELY STARTED AT TICK 110, immediately after the payday at 100 and 40 ticks
-- before the one at 150. A 25-tick build therefore completes with NO payday inside it, which is what
-- makes `spent` a clean read of the price rather than price-minus-income.

local ProxyType = "powerproxy.kinzhal"
local PowerPrefix = "KinzhalStrike"
local MissileType = "kinzhalmissile"
local TargetX, TargetY = 44, 17
local Price = 4000
local ExpectedUpkeep = 20      -- 4000 * PermilleCost 5 / 1000

-- Ticks the missile is held OUT OF THE WORLD by MissileDelay (150 on the Kinzhal) plus room for the
-- flight that follows. Pinned against player.yaml by PowersEconomyTest -- if the shipped delay ever
-- rises above this, that unit test fails before a launch slot is spent on discovering it.
local ArrivalBudget = 260
local FlightBudget = 150
local PaydayWindow = 100       -- exactly two paydays at PassiveIncomeInterval 50
local BuyDeadline = 400
local SettleTicks = 8
local ObserveTicks = 1500

local BaselineStart = 10
local BuyTick = BaselineStart + PaydayWindow

local tick = 0
local Russia
local phase = "baseline"
local finished = false

local cashBaseline0, cashBaseline1 = -1, -1
local upkeepBaseline = -1
local baselineNet = -1
local binBefore = "never-read"

local cashBeforeBuy = -1
local buildStatus = "never-called"
local boughtKey = nil
local bankTick = nil
local stateAtBank = "never-read"
local cashAtBank = -1
local upkeepBanked = -1
local breakdownBanked = "never-read"
local spent = -1

local bankedWindowStart = nil
local cashBanked0, cashBanked1 = -1, -1
local bankedNet = -1

local orderStatus = "never-called"
local orderTick = nil
local missileTick = nil
local impactTick = nil

local afterWindowStart = nil
local cashAfter0, cashAfter1 = -1, -1
local afterNet = -1
local upkeepAfter = -1
local proxiesAfter = -1
local binAfter = "never-read"

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
		.. " bin=[" .. binBefore .. "]"
		.. " | BUY " .. buildStatus .. " cash " .. n(cashBeforeBuy) .. "->" .. n(cashAtBank)
		.. " (spent " .. n(spent) .. " of " .. Price .. ") key=" .. n(boughtKey) .. "@t" .. n(bankTick)
		.. " state=" .. stateAtBank
		.. " | HOLD net/interval=" .. n(bankedNet) .. " upkeep=" .. n(upkeepBanked)
		.. " breakdown=[" .. breakdownBanked .. "]"
		.. " | FIRE order=" .. orderStatus .. "@t" .. n(orderTick)
		.. " missile@t" .. n(missileTick) .. " impact@t" .. n(impactTick)
		.. " | AFTER net/interval=" .. n(afterNet) .. " upkeep=" .. n(upkeepAfter)
		.. " proxies=" .. n(proxiesAfter) .. " bin=[" .. binAfter .. "]"
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
			binBefore = Test.GetSupportPowerBin(Russia)
		elseif tick == BuyTick then
			cashBaseline1 = purse()
			baselineNet = (cashBaseline1 - cashBaseline0) / 2
			cashBeforeBuy = cashBaseline1

			-- ClassicProductionQueueProperties.Build resolves the queue from the actor's
			-- Buildable.Queue, so this reaches the `Powers` queue by construction rather than by name.
			if Russia.Build({ ProxyType }) then
				buildStatus = "queued"
				phase = "banking"
			else
				buildStatus = "refused"
				finish(true, "the Powers queue REFUSED " .. ProxyType .. ". Build returns false when"
					.. " the queue is unknown, already busy, or the actor's Buildable.Queue does not"
					.. " resolve -- check that SUPPLYROUTE's Production@Local still lists `Powers`"
					.. " and that the proxy's Buildable.Queue still says `Powers`.")
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
			stateAtBank = Test.GetSupportPowerState(Russia, key)
			upkeepBanked = Test.GetUpkeep(Russia)
			breakdownBanked = Test.GetUpkeepBreakdown(Russia)
			bankedWindowStart = tick + SettleTicks
			phase = "holding"
		elseif tick - BuyTick > BuyDeadline then
			finish(true, "the purchase was queued and NO SUPPORT POWER ICON EVER APPEARED within "
				.. BuyDeadline .. " ticks. Either production never completed -- the Powers queue is"
				.. " not reachable from the Supply Route -- or a proxy reached the world and"
				.. " SupportPowerManager.ActorAdded did not see a SupportPower on it.")
		end

		return
	end

	if phase == "holding" then
		if tick == bankedWindowStart then
			cashBanked0 = purse()
		elseif tick == bankedWindowStart + PaydayWindow then
			cashBanked1 = purse()
			bankedNet = (cashBanked1 - cashBanked0) / 2

			-- Everything about the PURCHASE is decidable now; assert it before spending the strike, so
			-- a failure here cannot be confused with a delivery failure below.
			if stateAtBank ~= "ready" then
				finish(true, "the purchase banked a power in state '" .. stateAtBank .. "' rather than"
					.. " 'ready'. A bought power that then has to charge is gated TWICE for the same"
					.. " thing -- you pay, and then you wait again -- which is precisely the failure"
					.. " the powers economy was designed to remove. Check StartFullyCharged is True"
					.. " and that no ChargeInterval survived the move onto the proxy.")
				return
			end

			if spent < Price then
				finish(true, "only " .. spent .. " credits were deducted for a " .. Price
					.. "-credit reservation. A buy loop that does not charge is not a buy loop and"
					.. " every price in the Powers tab is decoration.")
				return
			end

			if upkeepBanked - upkeepBaseline ~= ExpectedUpkeep then
				finish(true, "the upkeep REGISTER moved by " .. (upkeepBanked - upkeepBaseline)
					.. ", not " .. ExpectedUpkeep .. ", when the strike was banked. At PermilleCost 5"
					.. " a " .. Price .. "-credit proxy is " .. ExpectedUpkeep .. " per interval. A"
					.. " reading of 0 means InfersUpkeep never registered -- note it registers on"
					.. " INotifyAddedToWorld, which World.Add fires unconditionally, so being bodiless"
					.. " is NOT a reason for it to be missing.")
				return
			end

			if baselineNet - bankedNet ~= ExpectedUpkeep then
				finish(true, "the player's net per interval fell by " .. (baselineNet - bankedNet)
					.. " when the strike was banked (" .. n(baselineNet) .. " -> " .. n(bankedNet)
					.. "), not by " .. ExpectedUpkeep .. ". The register above says the entry EXISTS;"
					.. " this says what was actually TAKEN. A gap between the two means the register"
					.. " is decoration -- PlayerResources.Tick bills `(int)Upkeep` in one line and"
					.. " nothing else charges it.")
				return
			end

			orderStatus = Test.ActivateSupportPower(Russia, boughtKey, CPos.New(TargetX, TargetY))
			orderTick = tick
			if orderStatus ~= "issued" then
				finish(true, "the banked strike would not fire: " .. orderStatus .. ". With"
					.. " StartFullyCharged and no ChargeInterval it is ready the tick it arrives.")
				return
			end

			phase = "firing"
		end

		return
	end

	if phase == "firing" then
		if missileTick == nil then
			if #Russia.GetActorsByType(MissileType) > 0 then
				missileTick = tick
			elseif tick - orderTick > ArrivalBudget then
				finish(true, "no " .. MissileType .. " entered the world within " .. ArrivalBudget
					.. " ticks of the order. MissileStrikePower holds the missile OUT OF THE WORLD for"
					.. " MissileDelay ticks and GetActorsByType filters on IsInWorld, so a stale budget"
					.. " here reads exactly like a delivery failure. Compare ArrivalBudget against"
					.. " MissileDelay in player.yaml before believing this is a bug.")
			end

			return
		end

		if Victim.IsDead then
			impactTick = tick
			afterWindowStart = tick + SettleTicks
			phase = "after"
		elseif tick - missileTick > FlightBudget then
			finish(true, "the missile entered the world at t" .. missileTick .. " and the Abrams is"
				.. " still alive " .. FlightBudget .. " ticks later. This scenario is about the"
				.. " ECONOMY, so a failure here is most likely a delivery regression belonging to the"
				.. " missile-power scenarios rather than to the purchase.")
		end

		return
	end

	if phase == "after" then
		if tick == afterWindowStart then
			cashAfter0 = purse()
			proxiesAfter = #Russia.GetActorsByType(ProxyType)
			upkeepAfter = Test.GetUpkeep(Russia)
			binAfter = Test.GetSupportPowerBin(Russia)
		elseif tick == afterWindowStart + PaydayWindow then
			cashAfter1 = purse()
			afterNet = (cashAfter1 - cashAfter0) / 2

			if proxiesAfter ~= 0 then
				finish(true, "the strike was fired and " .. proxiesAfter .. " proxy actor(s) are STILL"
					.. " IN THE WORLD. This is the leak DisposeSelfOnActivate exists to close:"
					.. " InfersUpkeep unregisters only on INotifyRemovedFromWorld, so a surviving"
					.. " proxy bills the player for a reservation that no longer exists for the rest"
					.. " of the match. OneShot hides the ICON and does not remove the ACTOR.")
				return
			end

			if upkeepAfter ~= upkeepBaseline then
				finish(true, "upkeep is " .. upkeepAfter .. " after firing, not back at the baseline "
					.. upkeepBaseline .. ". The proxy is gone, so an entry left behind means"
					.. " RemoveFromUpkeep did not run for it.")
				return
			end

			if afterNet ~= baselineNet then
				finish(true, "the player's net per interval is " .. n(afterNet) .. " after firing, not"
					.. " back at the baseline " .. n(baselineNet) .. ". The register reads clean and"
					.. " the money does not, which is the worse of the two failures: it is invisible"
					.. " in the cash tooltip and drains the player for the rest of the match.")
				return
			end

			if findKey(binAfter) ~= nil then
				finish(true, "the strike was fired and its icon is STILL in the bin. OneShot is what"
					.. " makes a purchase consumable; without it one " .. Price .. "-credit buy is an"
					.. " unlimited ability.")
				return
			end

			finish(false, "bought ready, billed " .. ExpectedUpkeep .. "/interval while banked, and"
				.. " billed nothing after firing. The purchase replaced the charge rather than"
				.. " stacking on it (state at bank: ready), the upkeep was MEASURED in the player's"
				.. " actual net (" .. n(baselineNet) .. " -> " .. n(bankedNet) .. " -> " .. n(afterNet)
				.. " per interval), and the spent proxy was disposed rather than billing forever.")
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

	if Victim == nil then
		Test.Fail("Victim actor missing from the map")
		return
	end

	TestHarness.FocusBetween(OwnSR, Victim)
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, loop)
end
