-- ASSERTING AUTOTEST -- an unaffordable reservation goes DORMANT, and comes back.
--
-- THE QUESTION: what happens to a banked strike when the player cannot pay its upkeep?
--
-- The obvious answer -- lapse it and refund -- cannot be implemented honestly. PlayerResources.Upkeep
-- is a single pooled float billed as ONE number, so nothing in the engine knows which line went
-- unpaid and the game would have to invent a victim (cheapest? newest? the one being saved for?).
-- Worse, the refund itself pushes cash back up, so the game could destroy a purchase that the refund
-- would have made affordable. So the answer is dormancy, and dormancy is only worth anything if it is
-- REVERSIBLE -- which is the second half of this run and the half most likely to be wrong.
--
-- FOUR RUNGS:
--   1. SHORTFALL IS RECORDED. PlayerResources.ChangeCash already clamped a negative change at the
--      player's whole purse; what is new is capturing the clamped return instead of discarding it.
--      GetUpkeepShortfall must go non-zero, or nothing downstream can possibly work.
--   2. THE POWER GOES DORMANT, NOT HIDDEN AND NOT ABSENT. `hidden` would mean the bin dropped the
--      icon (what a lobby-gated power looks like); `absent` would mean the manager lost the power
--      entirely. `dormant` is the one reading that means "still yours, still visible, cannot fire" --
--      the shipped ON HOLD overlay.
--   3. NOTHING WAS DESTROYED. The proxy actor is still alive and the icon is still in the bin.
--   4. IT COMES BACK BY ITSELF. Solvency is restored mid-run and the power must return to `ready`
--      with no further purchase and no player action.
--
-- HOW THE SHORTFALL IS FORCED. rules.yaml sets PassiveIncome to 0 -- the shipped 100 per interval
-- comfortably covers a 20-per-interval reservation and no shortfall would ever occur -- and the run
-- then sets the purse to zero directly. With no income, the next payday cannot pay 20 out of 0.
-- Restoring solvency is the same lever in reverse.
--
-- ADVANCING ON OBSERVABLES, NOT ON TIMERS. Every phase below waits for the READING it is about
-- (shortfall non-zero, state == dormant, state == ready) with a generous deadline, rather than
-- sleeping a computed number of ticks. The economy tick is every 50 and the condition it drives is
-- granted on the tick after that, so any fixed settle would be a guess sitting one tick from wrong.

local ProxyType = "powerproxy.kinzhal"
local PowerPrefix = "KinzhalStrike"
local Price = 4000
local ExpectedUpkeep = 20      -- 4000 * PermilleCost 5 / 1000
local Interval = 50            -- PassiveIncomeInterval

local BuyDeadline = 400
local DormantDeadline = 4 * Interval
local RecoverDeadline = 4 * Interval
local Solvent = 5000
local ObserveTicks = 1200

local tick = 0
local Russia
local phase = "buying"
local finished = false

local buildStatus = "never-called"
local boughtKey = nil
local bankTick = nil
local stateAtBank = "never-read"
local upkeepBanked = -1

local brokeTick = nil
local shortfall = -1
local dormantTick = nil
local stateDormant = "never-read"
local binDormant = "never-read"
local proxiesDormant = -1
local fireWhileDormant = "never-called"

local solventTick = nil
local recoveredTick = nil
local stateRecovered = "never-read"
local shortfallRecovered = -1

local function n(v)
	if v == nil then
		return "none"
	end

	return tostring(v)
end

local function findKey(bin)
	return string.match(bin, "(" .. PowerPrefix .. "_%d+)")
end

local function summary()
	return "BUY " .. buildStatus .. " key=" .. n(boughtKey) .. "@t" .. n(bankTick)
		.. " state=" .. stateAtBank .. " upkeep=" .. n(upkeepBanked)
		.. " | BROKE@t" .. n(brokeTick) .. " shortfall=" .. n(shortfall)
		.. " | DORMANT@t" .. n(dormantTick) .. " state=" .. stateDormant
		.. " bin=[" .. binDormant .. "] proxies=" .. n(proxiesDormant)
		.. " fire=" .. fireWhileDormant
		.. " | SOLVENT@t" .. n(solventTick) .. " recovered@t" .. n(recoveredTick)
		.. " state=" .. stateRecovered .. " shortfall=" .. n(shortfallRecovered)
		.. " | cash=" .. Russia.Cash .. " observed=" .. tick .. "t"
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

	if phase == "buying" then
		if tick == 5 then
			if Russia.Build({ ProxyType }) then
				buildStatus = "queued"
			else
				buildStatus = "refused"
				finish(true, "the Powers queue REFUSED " .. ProxyType .. ", so there is no reservation"
					.. " to starve. Check SUPPLYROUTE's Production@Local still lists `Powers`.")
			end
		elseif tick > 5 then
			local key = findKey(Test.GetSupportPowerBin(Russia))
			if key ~= nil then
				boughtKey = key
				bankTick = tick
				stateAtBank = Test.GetSupportPowerState(Russia, key)
				upkeepBanked = Test.GetUpkeep(Russia)

				if stateAtBank ~= "ready" then
					finish(true, "the purchase banked a power in state '" .. stateAtBank .. "' rather"
						.. " than 'ready', so this run cannot tell a dormant power from one that was"
						.. " never awake.")
					return
				end

				if upkeepBanked < ExpectedUpkeep then
					finish(true, "the banked strike registered " .. upkeepBanked .. " upkeep, not "
						.. ExpectedUpkeep .. ". With nothing to bill there is nothing to fail to pay.")
					return
				end

				-- Take the purse to zero. With PassiveIncome 0 (rules.yaml) nothing refills it, so
				-- the next payday must try to bill 20 against 0 and clamp.
				Russia.Cash = 0
				Russia.Resources = 0
				brokeTick = tick
				phase = "starving"
			elseif tick > BuyDeadline then
				finish(true, "the purchase was queued and no support power icon ever appeared within "
					.. BuyDeadline .. " ticks.")
			end
		end

		return
	end

	if phase == "starving" then
		local s = Test.GetUpkeepShortfall(Russia)
		local state = Test.GetSupportPowerState(Russia, boughtKey)

		if s > 0 and state == "dormant" then
			shortfall = s
			dormantTick = tick
			stateDormant = state
			binDormant = Test.GetSupportPowerBin(Russia)
			proxiesDormant = #Russia.GetActorsByType(ProxyType)
			fireWhileDormant = Test.ActivateSupportPower(Russia, boughtKey, CPos.New(44, 17))

			if proxiesDormant ~= 1 then
				finish(true, "the reservation went dormant and " .. proxiesDormant .. " proxy actors"
					.. " remain (expected 1). Dormancy must not destroy anything -- that is the whole"
					.. " reason it was chosen over lapsing.")
				return
			end

			if findKey(binDormant) == nil then
				finish(true, "the reservation went dormant and its icon LEFT THE BIN. Dormant is not"
					.. " hidden: the player must be able to see the thing they are being told they"
					.. " cannot afford, wearing the ON HOLD overlay. An icon that vanishes reads as a"
					.. " power that was taken away.")
				return
			end

			if fireWhileDormant == "issued" then
				finish(true, "a DORMANT strike still fired when ordered. Dormancy is enforced through"
					.. " PauseOnCondition -> IsTraitPaused -> SupportPowerInstance.Active, and"
					.. " Activate returns early when not Ready -- if the order got through, one of"
					.. " those links is missing and unpayable upkeep costs the player nothing.")
				return
			end

			-- Pay the bill. The next payday must settle, the shortfall must clear, and the condition
			-- must be revoked without anyone buying anything.
			Russia.Cash = Solvent
			solventTick = tick
			phase = "recovering"
		elseif tick - brokeTick > DormantDeadline then
			shortfall = s
			stateDormant = state
			finish(true, "the purse was emptied at t" .. brokeTick .. " and after " .. DormantDeadline
				.. " ticks (" .. (DormantDeadline / Interval) .. " economy intervals) the reservation"
				.. " reads shortfall=" .. n(s) .. " state='" .. state .. "'. A shortfall of 0 means"
				.. " PlayerResources never recorded the clamp -- check PassiveIncome really is 0 in"
				.. " rules.yaml, because with income the bill is simply paid. A non-zero shortfall"
				.. " with a non-dormant state means the signal exists and nothing consumes it:"
				.. " GrantConditionOnUpkeepShortfall on the proxy, PauseOnCondition on the power.")
		end

		return
	end

	if phase == "recovering" then
		local state = Test.GetSupportPowerState(Russia, boughtKey)
		if state == "ready" then
			recoveredTick = tick
			stateRecovered = state
			shortfallRecovered = Test.GetUpkeepShortfall(Russia)

			if shortfallRecovered ~= 0 then
				finish(true, "the power is ready again but the recorded shortfall is still "
					.. shortfallRecovered .. ". The flag latches for a whole interval by design; if it"
					.. " never clears it will re-pause the power on the next tick that reads it.")
				return
			end

			finish(false, "an unaffordable reservation went DORMANT rather than being destroyed: the"
				.. " shortfall was recorded (" .. n(shortfall) .. "), the power reported 'dormant',"
				.. " its icon stayed in the bin, its proxy stayed alive, a fire order was refused"
				.. " with '" .. fireWhileDormant .. "', and paying the bill brought it back to 'ready'"
				.. " " .. (recoveredTick - solventTick) .. " ticks later with no further purchase.")
		elseif tick - solventTick > RecoverDeadline then
			stateRecovered = state
			finish(true, "the purse was refilled to " .. Solvent .. " at t" .. n(solventTick)
				.. " and after " .. RecoverDeadline .. " ticks the power still reads '" .. state
				.. "'. Dormancy that does not reverse is lapsing with extra steps -- and it is worse"
				.. " than lapsing, because the player is still being billed for a strike they can"
				.. " never fire.")
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
