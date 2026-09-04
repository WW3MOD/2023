-- ASSERTING AUTOTEST — buy, bank, fire. Does a bodiless proxy actually produce?
--
-- THE QUESTION, verbatim from proposal §8 item 4: "Does a bodiless proxy really produce? The whole
-- buy loop is traced statically and never run. THE ANSWER: does the cameo complete and an icon
-- appear top-left." It has been open since the proposal was written.
--
-- The static trace says yes, and names the line: Production.Produce takes its bodiless branch when
-- `!producee.HasTraitInfo<IOccupySpaceInfo>()` (Production.cs:126-131), which is deliberate rather
-- than accidental — ProximityExternalCondition opens with an explicit
-- `if (produced.OccupiesSpace == null) return;` guard written for exactly this case. What has never
-- been observed is the chain AFTER that branch: DoProduction's CreateActor putting a spaceless
-- actor in the world, SupportPowerManager.ActorAdded picking it up, and the power becoming an icon.
--
-- READ map.yaml's HEADER FOR WHAT THIS DOES AND DOES NOT PROVE. In short: the ENGINE mechanism, yes,
-- first time. That the shipped mod has a buy menu, no — it does not, and this scenario defines its
-- own proxy in rules.yaml rather than pretending otherwise.
--
-- FOUR RUNGS, in the order a player would experience them:
--   1. BUY   — the item enters the Defense queue and cash is deducted.
--   2. BANK  — production completes, a proxy reaches the world, and the bin gains an icon. This is
--              §8 item 4's literal question.
--   3. FIRE  — the banked power delivers a missile from the map edge and kills its target.
--   4. SPEND — OneShot removes the icon afterwards, so the purchase is consumed rather than becoming
--              a permanent ability. Without this rung "buy" and "unlock" are indistinguishable.
--
-- WHY THE POWER KEY IS MATCHED OUT OF A STRING RATHER THAN NAMED. The proxy's power sets
-- `AllowMultiple: true`, so SupportPowerManager keys it `BoughtStrike_<ActorID>`
-- (SupportPowerManager.cs:48-51) — the ActorID is not knowable in advance. That is not an
-- inconvenience to work around, it is the §6 property that makes N purchases into N icons, so the
-- scenario reads the key list from Test.GetSupportPowerBin and matches the key out of it. A run
-- that instead named a fixed key would be quietly testing AllowMultiple: false.
--
-- THAT IS WHAT GetSupportPowerBin IS FOR, and it is why this scenario reads it directly rather than
-- harvesting it off a state string. The state binding used to append the key list to every return,
-- so this file called GetSupportPowerState with a key it did not care about purely to get at the
-- suffix — and the same decoration silently broke exact-token comparisons in two sibling scenarios.
-- The two questions are now two bindings: one power's state, or the set of drawn keys.
--
-- THE SHIPPED KINZHAL IS THE CONTROL. The player is Russia, so MissileStrikePower@Kinzhal is on the
-- Player actor throughout. It must be in the bin before the purchase and still there after, which
-- is what makes "the bin grew by one" a statement about the purchase rather than about the bin.

local ProxyType = "powerproxy.strike"
local BoughtPrefix = "BoughtStrike"
local ControlKey = "KinzhalStrike"
local MissileType = "kinzhalmissile"
local TargetX, TargetY = 44, 17

local BuyDeadline = 400      -- ticks to complete a 25-tick build. Enormously loose on purpose.
local FireDeadline = 200     -- ticks from the order to the kill. Expected ~26 at Speed 2000.
local ObserveTicks = 800

local tick = 0
local Russia
local cashBefore = -1
local cashAfterQueue = -1
local buildStarted = false
local buildStatus = "never-called"
local binBefore = "never-read"
local controlBefore = "never-read"
local binAfterProduce = "never-read"
local binAfterFire = "never-read"
local boughtKey = nil
local boughtKeyTick = nil
local proxySeen = 0
local orderStatus = "never-called"
local orderTick = nil
local firstCell = nil
local firstSeenTick = nil
local impactTick = nil
local victimStartHealth = 0
local finished = false

local function cellDist(ax, ay, bx, by)
	local dx = ax - bx
	local dy = ay - by
	return math.floor(math.sqrt(dx * dx + dy * dy) + 0.5)
end

local function n(v)
	if v == nil then
		return "none"
	end

	return tostring(v)
end

-- Test.GetSupportPowerBin returns the comma-separated keys the bin would draw — the exact set
-- SupportPowersWidget builds its icon list from (SupportPowersWidget.cs:136), or the literal
-- "empty". Matching the bought key out of that listing is how an AllowMultiple power, whose key
-- carries a runtime ActorID, is found without guessing it.
local function findBoughtKey(bin)
	return string.match(bin, "(" .. BoughtPrefix .. "_%d+)")
end

-- The control's own state, kept separate from the bin. `charging:<n>` and `ready` both mean the
-- icon is drawn; anything else means it is not.
local function isDrawn(state)
	return state == "ready" or string.sub(state, 1, 9) == "charging:"
end

local function pollTick()
	tick = tick + 1

	-- Phase 1: buy. Not attempted on tick 1 — ClassicProductionQueueProperties.Build documents that
	-- it does not work during the first tick, and the Defense queue also has to see OwnSR's
	-- Production@Local before anyEnabledProduction is true (ProductionQueue.cs:320-328).
	if not buildStarted then
		if tick < 5 then
			return
		end

		binBefore = Test.GetSupportPowerBin(Russia)
		controlBefore = Test.GetSupportPowerState(Russia, ControlKey)
		cashBefore = Russia.Cash + Russia.Resources

		if Russia.Build({ ProxyType }) then
			buildStarted = true
			buildStatus = "queued"
		else
			-- Kept rather than failing immediately, so the verdict can distinguish "the queue
			-- refused" from "the queue accepted and nothing came out".
			buildStatus = "refused"
		end

		return
	end

	-- Phase 2: bank. Watch for the icon rather than for the actor, because the icon IS the question.
	if boughtKey == nil then
		local proxies = Russia.GetActorsByType(ProxyType)
		if #proxies > proxySeen then
			proxySeen = #proxies
		end

		local bin = Test.GetSupportPowerBin(Russia)
		local key = findBoughtKey(bin)
		if key ~= nil then
			boughtKey = key
			boughtKeyTick = tick
			binAfterProduce = bin
			cashAfterQueue = Russia.Cash + Russia.Resources
		end

		return
	end

	-- Phase 3: fire.
	if orderTick == nil then
		orderStatus = Test.ActivateSupportPower(Russia, boughtKey, CPos.New(TargetX, TargetY))
		if orderStatus == "issued" then
			orderTick = tick
		end

		return
	end

	local missiles = Russia.GetActorsByType(MissileType)
	if #missiles > 0 and firstCell == nil then
		local c = missiles[1].Location
		firstCell = { X = c.X, Y = c.Y }
		firstSeenTick = tick
	end

	if impactTick == nil and Victim.IsDead then
		impactTick = tick
		-- Phase 4: spend. Read a few ticks later in finish(); OneShot is applied when the power is
		-- activated, but the bin is re-read at the end so the reading is settled.
	end
end

local function finish()
	binAfterFire = Test.GetSupportPowerBin(Russia)
	local stillBanked = findBoughtKey(binAfterFire)

	local home = Russia.HomeLocation
	local entryX = firstCell ~= nil and firstCell.X or -1
	local entryY = firstCell ~= nil and firstCell.Y or -1
	local toHome = firstCell ~= nil and cellDist(entryX, entryY, home.X, home.Y) or -1
	local toTarget = firstCell ~= nil and cellDist(entryX, entryY, TargetX, TargetY) or -1
	local spent = (cashBefore >= 0 and cashAfterQueue >= 0) and (cashBefore - cashAfterQueue) or -1

	local summary = "BUY " .. buildStatus .. " cash " .. cashBefore .. "->" .. n(cashAfterQueue)
		.. " (spent " .. spent .. " of 4000)"
		.. " | BANK proxies=" .. proxySeen .. " key=" .. n(boughtKey) .. "@t" .. n(boughtKeyTick)
		.. " | FIRE order=" .. orderStatus .. "@t" .. n(orderTick)
		.. " entry=" .. entryX .. "," .. entryY .. " entry->home=" .. toHome
		.. "c entry->target=" .. toTarget .. "c impact@t" .. n(impactTick)
		.. " | SPEND still-banked=" .. n(stillBanked)
		.. " | victim " .. victimStartHealth .. "hp -> "
		.. (Victim.IsDead and "DEAD" or (Victim.Health .. "hp"))
		.. " | control=" .. controlBefore
		.. " bins before=[" .. binBefore .. "] after-produce=[" .. binAfterProduce
		.. "] after-fire=[" .. binAfterFire .. "]"
		.. " | observed=" .. tick .. "t"

	-- 0. The control. If the shipped Kinzhal is not in the bin, nothing below can be read as a
	-- statement about the purchase.
	--
	-- THIS GUARD USED TO BE UNFIREABLE. It compared the CONTROL'S STATE against 'absent' and
	-- 'no-manager' while holding a string that also carried the bin listing appended to it, so no
	-- comparison could ever match and a broken mod would have sailed past into the assertions below.
	-- It now reads the control's state from its own binding, which returns a bare token.
	if not isDrawn(controlBefore) then
		Test.Fail("the shipped Kinzhal control was not drawn in the power bin before the purchase"
			.. " (state '" .. controlBefore .. "', bin [" .. binBefore .. "]), so this run says"
			.. " nothing about the buy loop — a mod in which no support power works at all would"
			.. " also show no bought icon. || " .. summary)
		return
	end

	-- 1. BUY.
	if buildStatus ~= "queued" then
		Test.Fail("the Defense queue refused " .. ProxyType .. ". ClassicProductionQueueProperties"
			.. ".Build returns false when the queue is unknown, already busy, or the actor's"
			.. " Buildable.Queue does not resolve — check that SUPPLYROUTE's Production@Local still"
			.. " produces `Defense`. || " .. summary)
		return
	end

	-- 2. BANK — §8 item 4's literal question.
	if boughtKey == nil then
		Test.Fail("the purchase was queued but NO SUPPORT POWER ICON EVER APPEARED. This is"
			.. " proposal §8 item 4 answering NO, and it is the finding, not a flake: the buy model"
			.. " in §2.4 rests entirely on Production.Produce's bodiless branch"
			.. " (Production.cs:126-131) delivering a spaceless actor into the world where"
			.. " SupportPowerManager.ActorAdded can see it. " .. proxySeen .. " proxy actor(s) were"
			.. " observed. Zero proxies means production never completed; a proxy with no icon means"
			.. " it completed and the power did not register. || " .. summary)
		return
	end

	if spent < 3000 then
		Test.Fail("the proxy was produced but only " .. spent .. " credits were deducted for a"
			.. " 4000-credit item. A buy loop that does not charge is not a buy loop — the prices in"
			.. " §9.3 would be decoration. || " .. summary)
		return
	end

	-- 3. FIRE.
	if orderStatus ~= "issued" then
		Test.Fail("the bought power banked as '" .. boughtKey .. "' but would not fire: "
			.. orderStatus .. ". With ChargeInterval 0 and StartFullyCharged it should be ready the"
			.. " tick it arrives — that is what makes a purchase a BANKED power rather than a second"
			.. " timer. || " .. summary)
		return
	end

	if firstCell == nil then
		Test.Fail("the bought power fired but no " .. MissileType .. " entered the world."
			.. " || " .. summary)
		return
	end

	if toTarget < 25 or toHome > 15 then
		Test.Fail("the bought power delivered its missile from the wrong place: entry " .. toHome
			.. " cells from home (allowance 15) and " .. toTarget .. " from the target (needs >= 25)."
			.. " A power carried on a proxy must behave exactly like one carried on the Player actor."
			.. " || " .. summary)
		return
	end

	if impactTick == nil then
		Test.Fail("the missile entered but the Abrams is still alive after " .. tick .. " ticks."
			.. " || " .. summary)
		return
	end

	-- 4. SPEND. Without this rung, "bought" and "permanently unlocked" look identical.
	if stillBanked ~= nil then
		Test.Fail("the bought strike was fired and its icon is STILL in the bin as '" .. stillBanked
			.. "'. OneShot is what makes a purchase consumable; without it one 4000-credit buy is an"
			.. " unlimited ability and every price in §9.3 is meaningless. || " .. summary)
		return
	end

	Test.Pass("buy -> bank -> fire -> spend: a bodiless proxy produced, its power reached the bin,"
		.. " delivered from the map edge, and was consumed. §8 item 4 answers YES. || " .. summary)
end

local function step()
	pollTick()

	local done = (impactTick ~= nil and (tick - impactTick) > 10)
		or (buildStarted and boughtKey == nil and tick > BuyDeadline)
		or (orderTick ~= nil and (tick - orderTick) > FireDeadline)
		or tick >= ObserveTicks

	if not finished and done then
		finished = true
		finish()
		return
	end

	Trigger.AfterDelay(1, step)
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

	victimStartHealth = Victim.Health

	TestHarness.FocusBetween(OwnSR, Victim)
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
