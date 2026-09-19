-- ASSERTING AUTOTEST — the bot fires only when it is losing, and takes the biggest band it may.
--
-- Layout and intent live in description.txt and in map.yaml's header. This file drives the clock.
--
-- THE CLAIM IN ONE LINE: the user's rule for bots (2026-09-13) is "It only fires if it is losing, so
-- it never escalates unnecessarily". NuclearBotModule is that sentence; this is the run that shows
-- it holding when the bot has every reason to shoot and nothing making it.
--
-- UPDATED FOR EXCHANGE v2 (2026-09-15). The bot's rule is unchanged and so is every phase up to the
-- launch; what changed is the model underneath. v1: a retaliation window granted one band up for a
-- minute and the bot preferred it over its permanent band. v2: the bot fires the highest band its
-- SIDE'S LEVEL allows, and firing puts that side's WHOLE ARSENAL on one cooldown. Two consequences
-- for this file -- the reason the module reports for the launch is now `HighestAllowed` where it was
-- `Retaliation`, and there are two new phases (E and F) asserting the cooldown lands and lifts.
--
-- UPDATED AGAIN FOR THE IMPACT-DEFERRED ESCALATION (2026-09-19). This file used to set
-- `EscalationDelayTicks: -1` in rules.yaml, which restored the pre-2026-09-16 timing where a
-- victim's level rose on the tick the enemy CLICKED. That override is gone and the scenario now
-- runs the shipped path: the level rises at the first warhead's IMPACT plus
-- NuclearExchangeInfo.EscalationDelayTicks (50 = 3.0 s), and the flight in between is however long
-- the B61-12 takes -- MissileDelay 200 before its arc is even counted, plus the map diagonal and
-- ApproachMargin over the missile's Speed. THAT NUMBER IS NOT WRITTEN DOWN ANYWHERE HERE, and it
-- must not be: every phase from the launch onward is anchored on an OBSERVED event instead.
--
--   the explosion   Test.GetImpactEffectCount, snapshotted at the launch. It counts
--                   CreateEffectWarhead impacts that passed the validity gates, and the B61's
--                   Warhead@Fireball is one (weapons-nuclear-arsenal.yaml). Nothing else on this
--                   map can detonate during the flight: no unit fires (^Combatant is HoldFire), the
--                   warhead is aimed at empty ground, and the humvees are not killed until later.
--   the escalation  Russia's 20 kt leaving `hidden`. That is SupportPowerInstance.Disabled going
--                   false, i.e. the band condition reaching the power.
--
-- TWO ASSERTIONS COME OUT OF THAT PAIR, AND THEY ARE THE COVERAGE THIS FILE ADDS:
--   (a) Russia's 20 kt stays HIDDEN on every tick between the launch and the explosion. This is
--       the user's ruling stated as a test -- it goes RED the moment the escalation is applied at
--       the click, which is exactly what the deleted override restored.
--   (b) Russia's 20 kt is drawn within EscalationDelayTicks + a grant allowance of the explosion.
--       It goes RED if the deferral never fires at all (a dropped pending record, an impact never
--       reported) -- the failure mode (a) alone cannot see, because a level that never rises is
--       also a level that never rises early.
--
-- WHY THE LAUNCH COUNT IS A BINDING AND NOT A BIN READ. A launch appears in the support power bin
-- only as a band going `ready` -> `charging:`, which a side cooldown and a launch both produce — so "exactly once" is not a question the bin can answer.
-- Test.GetBotNuclearState reads NuclearBotModule.LaunchCount, the count of orders the module
-- actually queued. The bin IS still read, in phases A and B, for the one thing it is the right
-- instrument for: proving the bot was holding a LOADED warhead when it declined. A zero launch
-- count off an empty bin would prove nothing at all.
--
-- TICK DOMAIN. Every boundary below is in TICKS, never TestHarness seconds: that helper runs 25
-- ticks per second against a mod at 16.67. THERE IS NO Trigger.OnTick IN THIS ENGINE; a
-- self-rescheduling Trigger.AfterDelay(1) is the idiom, and it is what the phase machine is built
-- on. Copied from test-nuclear-exchange rather than reinvented.

local RU_1KT = "Ru9M729Strike"       -- 9M729, 1000 t   -> Kiloton band
local RU_20KT = "RuIskanderStrike"   -- Iskander, 10000 t -> TwentyKiloton band
local USA_1KT = "B61LowStrike"       -- B61-12 low dial, 300 t -> Kiloton band

-- NuclearRung values. The integers are load-bearing in C# (NuclearReleaseLadder's header says so)
-- and the binding reports the raw number, so they are restated here rather than inferred.
local BAND_KILOTON = 1
local BAND_TWENTY_KILOTON = 2

-- Empty ground, far from both Supply Routes and from both armies. USA's shot is not aimed at
-- anything: the exchange arms the other side wherever it lands.
local AIM_X, AIM_Y = 32, 4

-- The module's ai.yaml settings, restated. A change to either without a change here turns a real
-- failure into a timing artefact, so they are named rather than folded into the tick constants.
local EVALUATION_INTERVAL = 50
local LOSING_STREAK_REQUIRED = 3
local MIN_TICKS_BETWEEN_LAUNCHES = 900

-- Phase boundaries, in ticks from t=0. Every one is "well after the thing it waits for", never a
-- measurement of when that thing happened.
-- rules.yaml compresses all four side cooldowns to this, flat, from the shipped 5000/7000/9000/
-- 12000. See that file for why 300 and not something rounder: it has to end BETWEEN phases E and F
-- and well before the rate limit, or phase G stops being about the rate limit.
local COOLDOWN_TICKS = 300

local ARMED_CHECK_TICK = 90                        -- release is at tick 10; 80 ticks of slack
local HOLDING_CHECK_TICK = 280                     -- ~4 more evaluations of not firing
local USA_FIRE_TICK = 300

-- ---- THE DEFERRAL'S OWN CONSTANTS, RESTATED FROM THE ENGINE -----------------------------------
-- NuclearExchangeInfo.EscalationDelayTicks. The SHIPPED value, deliberately not overridden in
-- rules.yaml: a scenario that retuned it would stop measuring what a player sees.
local ESCALATION_DELAY_TICKS = 50
-- What the level rise is allowed to take ON TOP of the delay, and every term of it is named.
-- NuclearExchangeInfo.GrantRetryTicks is 30, which is the budget ReconcileGrants gives the band
-- condition to cross from the World actor to the Player actor; the engine's own note says a
-- correct grant lands one or two ticks after the rise. 40 is that budget plus ten ticks for the
-- one-tick skew between the ESTIMATED impact tick MissileStrikePower reports and the tick the
-- warhead's CreateEffectWarhead actually runs on.
local ESCALATION_SLACK_TICKS = 40
-- How long the warhead is allowed to be in the air before this file calls it lost. The B61-12's
-- shipped flight on a 66x34 map is MissileDelay 200 + ceil((diagonal + 16c0) / Speed 900) ~= 305
-- ticks; 900 is three times that. IT IS A WATCHDOG, NOT A MEASUREMENT -- no assertion below reads
-- it, and widening it cannot make a failing run pass.
local FLIGHT_BUDGET_TICKS = 900
-- Ticks between the victim's band being DRAWN and the reading that asserts it is also LOADED.
-- BOUNDED AT BOTH ENDS, and the lower bound is different from the one this file used to carry.
--   > 1    the anchor is the tick the band left `hidden`, which is the tick its condition arrived
--          -- so all that is left is one ServicePendingReady pass. It is NOT anchored on the rise,
--          which is what forced the old gap above GrantRetryTicks.
--   < 300  rules.yaml's compressed side cooldown, and what keeps the `ready` reading NON-VACUOUS:
--          SupportPowerInstance.Tick pins a disabled power's countdown to full every tick
--          (SupportPowerManager.cs:399-401), so a band whose grant never armed it starts its OWN
--          300-tick interval at the rise and would read `charging:288` here.
local RISE_SETTLE_TICKS = 12

-- Set from the OBSERVED rise, not written down. See onRise below; -1 means "not scheduled yet",
-- which no tick can equal.
local LEVEL_HOLD_CHECK_TICK = -1
local CUT_ARMY_TICK = -1
local LAUNCH_CHECK_TICK = -1
-- THE EARLIEST THE BOT CAN FIRE is three evaluations at 50 ticks after the army cut and the latest
-- is LAUNCH_CHECK_TICK, so its cooldown runs out somewhere in a 150-tick band; these two checks sit
-- either side of that whole range rather than either side of one predicted tick. Every offset below
-- is the one this file carried before the rise became an observation, so the reasoning in each
-- phase is unchanged.
local COOLDOWN_CHECK_TICK = -1
local RECOVER_CHECK_TICK = -1
local NO_SECOND_LAUNCH_TICK = -1
-- HARD BACKSTOP, in absolute ticks, so the run cannot idle forever if the rise never arrives and
-- the watch's own budgets are somehow not reached. Tightened to the rebased schedule by onRise.
local BUDGET_TICK = USA_FIRE_TICK + FLIGHT_BUDGET_TICKS + 1200

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	local tick = 0
	local faults = {}
	local notes = {}
	local fireResult = "not-attempted"
	local killed = 0

	-- The escalation watch: nil until USA fires, then the record of one release order's journey
	-- from the click to the victim's level rise. Fields are filled in as they are OBSERVED.
	local watch = nil

	local function fault(fmt, ...)
		faults[#faults + 1] = string.format(fmt, ...)
	end

	-- The note must not lie: it is gated on every expect in its phase having returned true. A
	-- result.json carrying faults beside "ok at t90" makes whoever triages it work out which half is
	-- lying. test-nuclear-exchange learned this the hard way; the idiom is copied from it.
	local function note(ok, fmt, ...)
		notes[#notes + 1] = (ok and "" or "NOT ") .. string.format(fmt, ...)
	end

	local function botState()
		return Test.GetBotNuclearState(Russia)
	end

	-- The binding returns `launches=<n>|band=<rung>|fired=<name>|reason=<name>|streak=<n>|
	-- committed=<bool>`, or
	-- the bare token `absent`. Parsed by NAME rather than by position so a future field added to the
	-- middle of that string cannot silently shift every reading here by one.
	local function botField(name)
		local got = botState()
		if got == "absent" then
			return nil
		end

		return got:match(name .. "=([^|]+)")
	end

	local function expectLaunches(want, why)
		local got = botField("launches")
		if got == nil then
			fault("Russia has no enabled NuclearBotModule at t%d -- Test.GetBotNuclearState reads"
				.. " %q. The module is gated on enable-ai-experimental and the player is `Bot:"
				.. " experimental`, so this is a WIRING failure and nothing below it is evidence"
				.. " either way", tick, botState())
			return false
		end

		if tonumber(got) ~= want then
			fault("Russia's bot has launched %s times at t%d, expected %d. %s ;; STATE: %s",
				got, tick, want, why, botState())
			return false
		end

		return true
	end

	-- Compared EXACTLY against the bare vocabulary in TestGlobal.SupportPowerState. An earlier
	-- version of that binding appended " (bin: ...)" to every return, which made exact comparison
	-- unsatisfiable; this is deliberately not a `find`.
	local function expectPower(player, who, key, want, why)
		local got = Test.GetSupportPowerState(player, key)
		if got ~= want then
			fault("%s's %s reads %q, expected %q. %s", who, key, got, want, why)
			return false
		end

		return true
	end

	-- `charging:<n>` is the one token in the vocabulary that carries a value, so it is matched by
	-- PREFIX where every other reading is compared exactly.
	local function expectCharging(player, who, key, why)
		local got = Test.GetSupportPowerState(player, key)
		if got:sub(1, 9) ~= "charging:" then
			fault("%s's %s reads %q, expected a `charging:<ticks>` reading. %s", who, key, got, why)
			return false
		end

		return true
	end

	-- ==== THE ESCALATION WATCH ====================================================================
	-- Driven once per tick from the top of `step`, from the launch until the victim's band appears.
	-- It owns assertions (a) and (b); see the file header for what each is worth.
	--
	-- RETURNS: nil while the warhead is still on its way, the TICK the band appeared on once the
	-- escalation has landed, or -1 on a fault (the caller must then write the verdict and stop --
	-- every reading after a broken escalation is about a match that never reached the state under
	-- test).
	local function pollWatch()
		local drawn = Test.GetSupportPowerState(Russia, RU_20KT) ~= "hidden"

		if watch.impactTick == nil then
			-- ---- (a) THE LEVEL MUST NOT RISE BEFORE THE EXPLOSION. -------------------------------
			if drawn then
				fault("RUSSIA WAS ESCALATED AT THE LAUNCH, NOT AT THE DETONATION. Its %s left"
					.. " `hidden` at t%d, %d ticks after USA pressed the button at t%d and BEFORE"
					.. " any warhead impact had been counted (Test.GetImpactEffectCount is still"
					.. " %d). The 2026-09-16 user ruling is that the level-up lands with the"
					.. " explosion -- \"we see the correlation between the explosion, and after"
					.. " only a few seconds perhaps we get the message of escalation\" --"
					.. " so NuclearExchange.ReportNuclearRelease must RECORD the escalation and"
					.. " ServicePendingEscalations must apply it at the first impact plus"
					.. " EscalationDelayTicks. This reading is what a negative"
					.. " EscalationDelayTicks, or a lost deferral, looks like",
					RU_20KT, tick, tick - watch.orderTick, watch.orderTick, watch.effects0)
				return -1
			end

			if Test.GetImpactEffectCount() > watch.effects0 then
				watch.impactTick = tick
				return nil
			end

			if tick - watch.orderTick > FLIGHT_BUDGET_TICKS then
				fault("NO WARHEAD EVER DETONATED. USA's %s was issued at t%d and"
					.. " Test.GetImpactEffectCount has not moved off %d in %d ticks, against a"
					.. " shipped B61-12 flight of about 305 on this map. The warhead never left"
					.. " (MissileStrikePower.Activate bailed), never arrived, or its"
					.. " CreateEffectWarhead impact was discarded at the validity gates. Nothing"
					.. " below is evidence either way",
					USA_1KT, watch.orderTick, watch.effects0, tick - watch.orderTick)
				return -1
			end

			return nil
		end

		-- ---- (b) AND IT MUST RISE SHORTLY AFTER IT. ---------------------------------------------
		if drawn then
			watch.riseTick = tick
			return tick
		end

		if tick > watch.impactTick + ESCALATION_DELAY_TICKS + ESCALATION_SLACK_TICKS then
			fault("THE DEFERRED ESCALATION NEVER FIRED. USA's warhead detonated at t%d"
				.. " (Test.GetImpactEffectCount moved off %d) and Russia's %s is STILL %q at t%d,"
				.. " %d ticks later, against an EscalationDelayTicks of %d plus a %d-tick grant"
				.. " allowance. The pending record was dropped, no impact was reported for it"
				.. " (MissileStrikePower -> NuclearExchange.NotifyNuclearImpact), or the rise"
				.. " reached the state and not the condition layer. CHECK debug.log FOR"
				.. " `NUCLEAR ESCALATION`: a launch line saying \"escalation deferred to impact +\""
				.. " with no matching landing line is the pending record going missing",
				watch.impactTick, watch.effects0, RU_20KT,
				Test.GetSupportPowerState(Russia, RU_20KT), tick, tick - watch.impactTick,
				ESCALATION_DELAY_TICKS, ESCALATION_SLACK_TICKS)
			return -1
		end

		return nil
	end

	-- Hang the rest of the schedule off the OBSERVED rise. Every offset is the one this file used
	-- before the rise became an observation; only the anchor changed.
	local function onRise(riseTick)
		LEVEL_HOLD_CHECK_TICK = riseTick + RISE_SETTLE_TICKS
		CUT_ARMY_TICK = LEVEL_HOLD_CHECK_TICK + 20
		LAUNCH_CHECK_TICK = CUT_ARMY_TICK + EVALUATION_INTERVAL * (LOSING_STREAK_REQUIRED + 3)
		COOLDOWN_CHECK_TICK = LAUNCH_CHECK_TICK + 30
		RECOVER_CHECK_TICK = CUT_ARMY_TICK + 660
		NO_SECOND_LAUNCH_TICK = CUT_ARMY_TICK + 760
		BUDGET_TICK = NO_SECOND_LAUNCH_TICK + 100

		note(true, "escalation landed at t%d: fired t%d, detonated t%d (+%d), level rose t%d (+%d)",
			riseTick, watch.orderTick, watch.impactTick, watch.impactTick - watch.orderTick,
			riseTick, riseTick - watch.impactTick)
	end

	local function verdict()
		local summary = string.format(
			"usa-fire=%q | killed=%d | bot=%s | Russia 1kt=%s 20kt=%s | %s",
			fireResult, killed, botState(),
			Test.GetSupportPowerState(Russia, RU_1KT),
			Test.GetSupportPowerState(Russia, RU_20KT),
			table.concat(notes, " | "))

		if #faults > 0 then
			Test.Fail(table.concat(faults, " ;; ") .. " ;; READINGS: " .. summary)
		else
			Test.Pass()
		end
	end

	local step

	step = function()
		tick = tick + 1

		-- ---- THE WATCH RUNS FIRST, EVERY TICK, from the launch until the level rises. It is the
		-- only thing in this file that reads a tick it did not choose, and phases B2 onward do not
		-- exist until it says so.
		if watch ~= nil and watch.riseTick == nil then
			local risen = pollWatch()
			if risen == -1 then
				verdict()
				return
			end

			if risen ~= nil then
				onRise(risen)
			end
		end

		-- ---- PHASE A. RELEASED, ARMED, AND NOT LOSING. Both sides hold the 1 kt band permanently
		-- and the bot's reads `ready`, so the zero launch below is a DECISION rather than an empty
		-- bin. This is the user's rule, and it is the assertion that matters most in this file.
		if tick == ARMED_CHECK_TICK then
			local ok = expectPower(Russia, "Russia", RU_1KT, "ready",
				"the release gate opened at tick 10 and must hand BOTH sides a loaded 1 kt warhead."
				.. " Without this the no-launch assertion below is vacuous: a bot with nothing to"
				.. " fire cannot demonstrate restraint")

			ok = expectLaunches(0,
				"THE BOT FIRED WHILE NOT LOSING. Four humvees each is exactly 2000 v 2000 army"
				.. " value -- 100 %, against a LosingArmyRatioPercent of 60 -- and Russia's Supply"
				.. " Route is uncontested, so both halves of the losing predicate are quiet."
				.. " This is the user's whole rule for bots and nothing else in the tree checks it") and ok

			local reason = botField("reason")
			if reason ~= "NotLosing" then
				fault("Russia's bot reports reason %q at t%d, expected \"NotLosing\"."
					.. " Declining for the wrong reason reads the same in a log as declining for"
					.. " the right one -- and `NoReadyBand` here would mean the bin reading above"
					.. " and the module disagree about what is loaded", tostring(reason), tick)
				ok = false
			end

			note(ok, "armed-and-holding ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE A2. Still nothing, four evaluations later. One quiet reading could be a module
		-- that had not started ticking yet.
		if tick == HOLDING_CHECK_TICK then
			local ok = expectLaunches(0,
				string.format("the bot fired somewhere between t%d and t%d while still not losing."
					.. " That is roughly %d evaluations of a loaded 1 kt warhead and no reason to"
					.. " use it", ARMED_CHECK_TICK, tick,
					math.floor((tick - ARMED_CHECK_TICK) / EVALUATION_INTERVAL)))

			note(ok, "still-holding ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE B. USA fires the smallest warhead in the mod at empty ground. This costs USA
		-- its own arsenal for a cooldown IMMEDIATELY -- availability is settled when the button is
		-- pressed -- and raises Russia's LEVEL to 2 permanently WHEN THE WARHEAD LANDS.
		--
		-- THE BASELINE FOR THE WATCH IS TAKEN HERE AND NOT A TICK EARLIER OR LATER. A snapshot of
		-- Test.GetImpactEffectCount on the order tick is what makes "the count moved" mean "THIS
		-- warhead detonated" rather than "some warhead has detonated at some point this run".
		if tick == USA_FIRE_TICK then
			local effects0 = Test.GetImpactEffectCount()
			fireResult = Test.ActivateSupportPower(USA, USA_1KT, CPos.New(AIM_X, AIM_Y))
			if fireResult ~= "issued" then
				fault("USA could not fire %s: %q. Russia never reaches level 2 without it, so every reading"
					.. " below is about a match that never reached the state under test",
					USA_1KT, fireResult)
				verdict()
				return
			end

			if Test.GetSupportPowerState(Russia, RU_20KT) ~= "hidden" then
				fault("Russia's %s was already drawn at t%d, BEFORE USA fired anything. The"
					.. " no-early-escalation reading below can only mean something if the band"
					.. " starts dark -- check the release gate did not hand out level 2",
					RU_20KT, tick)
				verdict()
				return
			end

			watch = { orderTick = tick, effects0 = effects0, impactTick = nil, riseTick = nil }

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE B2. THE SHARPEST ASSERTION IN THE FILE. The bot has just been escalated to
		-- level 2 -- the biggest thing it has ever been permitted -- and it is STILL not losing. A
		-- level is permission, not a reason. A bot that fires here is a bot whose policy is "fire
		-- when able", which is the shape the engine's generic SupportPowerBotModule has and the
		-- shape the user's rule rejects.
		--
		-- IT IS A STRONGER TEST UNDER v2 THAN IT WAS UNDER v1, and worth saying so. A retaliation
		-- window expired, so a bot that held through one could be a bot that simply had not got
		-- round to firing. A level does not expire: this bot can take the 20 kt at any point for the
		-- rest of the match and declines every time it is asked.
		--
		-- AND IT IS STRONGER AGAIN SINCE THE ESCALATION WAS DEFERRED TO THE IMPACT. The bot now
		-- spends the WHOLE FLIGHT -- about 305 ticks, six evaluations -- holding a loaded 1 kt it
		-- may fire and knowing a warhead is inbound, and then declines the 20 kt as well.
		if tick == LEVEL_HOLD_CHECK_TICK then
			local ok = expectPower(Russia, "Russia", RU_20KT, "ready",
				string.format("being hit by 1 kt must raise Russia ONE BAND UP, and the band must be"
					.. " LOADED as well as drawn -- Russia did not fire, so Russia is on no"
					.. " cooldown and MakeBandsReady owes it a zeroed timer. `charging:` here is a"
					.. " grant that reached the CONDITION layer and not the timer: the band left"
					.. " `hidden` at t%d and SupportPowerInstance.Tick has been running its own"
					.. " 300-tick constructed interval down ever since, which is why this is read"
					.. " only %d ticks later and not a hundred", watch.riseTick, RISE_SETTLE_TICKS))

			ok = expectLaunches(0,
				"THE BOT TOOK AN ESCALATION IT HAD NO REASON TO TAKE. It is still at 100 %"
				.. " of USA's army value and its Supply Route is uncontested; the level grants"
				.. " permission and the policy needs a REASON") and ok

			note(ok, "escalated-and-holding ok at t%d (USA fired t%d, detonated t%d)",
				tick, USA_FIRE_TICK, watch.impactTick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE C. Cut Russia's army to a quarter: 500 against USA's 2000 is 25 %, against a
		-- LosingArmyRatioPercent of 60. Three kills rather than four so the bot is LOSING rather
		-- than ANNIHILATED -- a player with nothing left is a different scenario.
		if tick == CUT_ARMY_TICK then
			-- NOT ipairs OVER A TABLE LITERAL. A map actor global that failed to bind is nil, and a
			-- table constructor containing a nil TRUNCATES at it -- so `{ RuArmy1, RuArmy2, RuArmy3 }`
			-- with a missing middle entry silently becomes a one-element list and the count check
			-- below would report the wrong cause. Named one at a time, each guarded.
			local function cut(actor)
				if actor ~= nil and not actor.IsDead then
					actor.Kill()
					killed = killed + 1
				end
			end

			cut(RuArmy1)
			cut(RuArmy2)
			cut(RuArmy3)

			if killed ~= 3 then
				fault("only %d of Russia's three humvees could be killed at t%d, so the army ratio"
					.. " is not the 25 %% this scenario asserts against. Something else on the map"
					.. " destroyed them first and the run is not measuring what it means to",
					killed, tick)
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE D. EXACTLY ONE LAUNCH, AT THE HIGHEST BAND ITS LEVEL ALLOWS. The hysteresis is
		-- LOSING_STREAK_REQUIRED consecutive evaluations, so the earliest possible launch is three
		-- intervals after the kills; this check sits three further intervals past that.
		if tick == LAUNCH_CHECK_TICK then
			local ok = expectLaunches(1,
				string.format("the bot did not fire after being cut to 25 %% of USA's army value at"
					.. " t%d. Zero means the losing predicate never committed -- check `streak` and"
					.. " `reason` in the state below: `NotLosing` means the predicate itself,"
					.. " `NoReadyBand` means nothing was loaded when the %d-evaluation hysteresis"
					.. " completed, and `NoTarget` means the policy chose a band and the module"
					.. " found nothing legally visible to aim at -- which on a fog-off, pre-explored"
					.. " map would mean BeliefStore is not populating. More than one launch means"
					.. " the rate limit is not holding",
					CUT_ARMY_TICK, LOSING_STREAK_REQUIRED))

			local band = tonumber(botField("band") or "-1")
			if band ~= BAND_TWENTY_KILOTON then
				fault("Russia's bot fired band %d at t%d, expected %d (TwentyKiloton -- the highest"
					.. " band its LEVEL allows). %d (Kiloton) means it took the smallest thing it"
					.. " held: under v2 firing ANY band costs the side the same thing, its whole"
					.. " arsenal for a cooldown, so the small shot is full price for the least"
					.. " effect and the ruling's model is that the loser escalates",
					band, tick, BAND_TWENTY_KILOTON, BAND_KILOTON)
				ok = false
			end

			-- `fired`, NOT `reason`. The live reason is overwritten every evaluation and by now reads
			-- RateLimited -- correctly, the bot fired a few hundred ticks ago. `fired` is the sticky
			-- field that says why the LAUNCH happened.
			local firedReason = botField("fired")
			if firedReason ~= "HighestAllowed" then
				fault("Russia's bot reports fired=%q at t%d, expected \"HighestAllowed\"."
					.. " \"Retaliation\" is v1's window-reply reason and no longer exists;"
					.. " \"FinalExchange\" would mean something opened the apocalypse",
					tostring(firedReason), tick)
				ok = false
			end

			note(ok, "launched once at band %d by t%d (army cut t%d)", band, tick, CUT_ARMY_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE E. AND ITS WHOLE ARSENAL WENT DOWN WITH THE SHOT. The v2 rule applied to a bot,
		-- and it needs its own reading because nothing else here could distinguish "the bot chose
		-- not to fire again" from "the bot could not". Russia holds 1 kt and 20 kt at level 2; both
		-- must be silent, including the one it did NOT fire.
		if tick == COOLDOWN_CHECK_TICK then
			local ok = expectCharging(Russia, "Russia", RU_20KT,
				"the bot fired its 20 kt and its side must be on cooldown. `ready` means firing cost"
				.. " it nothing; `hidden` means the power is a BOUGHT power with an empty magazine"
				.. " and the Escalation free-timer bypass did not apply to a bot's arsenal")

			ok = expectCharging(Russia, "Russia", RU_1KT,
				"THE BOT'S 1 KT STAYED LOADED AFTER IT FIRED ITS 20 KT. A cooldown is SIDE-WIDE:"
				.. " one launch silences every band the side holds. `ready` here is v1's per-band"
				.. " regeneration, under which a losing bot works down its ladder firing one warhead"
				.. " per band before anything stops it -- which is the behaviour the 2026-09-15"
				.. " ruling exists to remove") and ok

			note(ok, "bot arsenal down at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE F. AND IT COMES BACK. Asserted so phase G means something: a bot that never
		-- recovered would also never fire again, and the rate-limit reading below would pass for a
		-- reason that has nothing to do with the rate limit.
		if tick == RECOVER_CHECK_TICK then
			local ok = expectPower(Russia, "Russia", RU_20KT, "ready",
				string.format("the bot's arsenal did not come back by t%d. It fired no later than"
					.. " t%d against a cooldown of %d, so it must be loaded by t%d at the latest."
					.. " If this reads `charging:` the applied cooldown is longer than the override"
					.. " in rules.yaml -- and then the no-second-launch reading below is measuring"
					.. " the cooldown rather than MinTicksBetweenLaunches",
					tick, LAUNCH_CHECK_TICK, COOLDOWN_TICKS, LAUNCH_CHECK_TICK + COOLDOWN_TICKS))

			note(ok, "bot arsenal back at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE G. AND NOT AGAIN -- ON THE RATE LIMIT, NOT ON THE COOLDOWN. Phase F just
		-- asserted the arsenal is BACK, so the bot is losing, loaded, permitted, and holding: the
		-- only thing left stopping it is MinTicksBetweenLaunches. That separation is why the
		-- cooldown in rules.yaml is 300 rather than something that would still be running here.
		if tick == NO_SECOND_LAUNCH_TICK then
			local ok = expectLaunches(1,
				string.format("a SECOND launch inside %d ticks of the first, against a"
					.. " MinTicksBetweenLaunches of %d. The side cooldown ended before this check"
					.. " (phase F), so the rate limit is the only thing that can have held -- a"
					.. " losing bot with a loaded arsenal fires on the first tick it may",
					tick - LAUNCH_CHECK_TICK, MIN_TICKS_BETWEEN_LAUNCHES))

			note(ok, "no second launch by t%d", tick)
			verdict()
			return
		end

		if tick >= BUDGET_TICK then
			fault("the phase machine ran past its budget at t%d without reaching a verdict", tick)
			verdict()
			return
		end

		Trigger.AfterDelay(1, step)
	end

	Trigger.AfterDelay(1, step)
end
