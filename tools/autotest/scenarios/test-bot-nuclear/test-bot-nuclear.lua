-- ASSERTING AUTOTEST — the bot fires only when it is losing, and takes the window when it does.
--
-- Layout and intent live in description.txt and in map.yaml's header. This file drives the clock.
--
-- THE CLAIM IN ONE LINE: the user's rule for bots (2026-09-13) is "It only fires if it is losing, so
-- it never escalates unnecessarily". NuclearBotModule is that sentence; this is the run that shows
-- it holding when the bot has every reason to shoot and nothing making it.
--
-- WHY THE LAUNCH COUNT IS A BINDING AND NOT A BIN READ. A launch appears in the support power bin
-- only as a band going `ready` -> `charging:`, which a regeneration timer, a lapsing window and a
-- launch all produce — so "exactly once" is not a question the bin can answer.
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
local ARMED_CHECK_TICK = 90                       -- release is at tick 10; 80 ticks of slack
local HOLDING_CHECK_TICK = 280                    -- ~4 more evaluations of not firing
local USA_FIRE_TICK = 300
local WINDOW_HOLD_CHECK_TICK = USA_FIRE_TICK + 80 -- the grant crosses a world trait, a player trait
                                                  -- and a condition; GrantRetryTicks is 30
local CUT_ARMY_TICK = 400
local LAUNCH_CHECK_TICK = CUT_ARMY_TICK + EVALUATION_INTERVAL * (LOSING_STREAK_REQUIRED + 3)
local NO_SECOND_LAUNCH_TICK = LAUNCH_CHECK_TICK + 300
local BUDGET_TICK = NO_SECOND_LAUNCH_TICK + 100

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	local tick = 0
	local faults = {}
	local notes = {}
	local fireResult = "not-attempted"
	local killed = 0

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

		-- ---- PHASE B. USA fires the smallest warhead in the mod at empty ground. This arms Russia
		-- at 1 kt permanently (it already had that) and opens a 20 kt RETALIATION WINDOW.
		if tick == USA_FIRE_TICK then
			fireResult = Test.ActivateSupportPower(USA, USA_1KT, CPos.New(AIM_X, AIM_Y))
			if fireResult ~= "issued" then
				fault("USA could not fire %s: %q. No window opens without it, so every reading"
					.. " below is about a match that never reached the state under test",
					USA_1KT, fireResult)
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE B2. THE SHARPEST ASSERTION IN THE FILE. The bot now holds a 20 kt retaliation
		-- grant -- the biggest thing it has ever been permitted -- and it is STILL not losing. A
		-- window is permission, not a reason. A bot that fires here is a bot whose policy is "fire
		-- when able", which is the shape the engine's generic SupportPowerBotModule has and the
		-- shape the user's rule rejects.
		if tick == WINDOW_HOLD_CHECK_TICK then
			local ok = expectPower(Russia, "Russia", RU_20KT, "ready",
				"being hit by 1 kt must arm Russia ONE BAND UP for the window, ready to fire the"
				.. " instant it opens (decision 01). `hidden` means the window never opened -- and"
				.. " then the no-launch reading below is measuring nothing")

			ok = expectLaunches(0,
				"THE BOT TOOK A RETALIATION WINDOW IT HAD NO REASON TO TAKE. It is still at 100 %"
				.. " of USA's army value and its Supply Route is uncontested; the window grants"
				.. " permission and the policy needs a REASON") and ok

			note(ok, "window-open-and-holding ok at t%d (USA fired t%d)", tick, USA_FIRE_TICK)
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

		-- ---- PHASE D. EXACTLY ONE LAUNCH, AT THE WINDOW'S BAND. The hysteresis is
		-- LOSING_STREAK_REQUIRED consecutive evaluations, so the earliest possible launch is three
		-- intervals after the kills; this check sits three further intervals past that.
		if tick == LAUNCH_CHECK_TICK then
			local ok = expectLaunches(1,
				string.format("the bot did not fire after being cut to 25 %% of USA's army value at"
					.. " t%d. Zero means the losing predicate never committed -- check `streak` and"
					.. " `reason` in the state below: `NotLosing` means the predicate itself, and"
					.. " `NoReadyBand` means the window lapsed before the %d-evaluation hysteresis"
					.. " completed, and `NoTarget` means the policy chose a band and the module"
					.. " found nothing legally visible to aim at -- which on a fog-off, pre-explored"
					.. " map would mean BeliefStore is not populating. More than one launch means"
					.. " the rate limit is not holding",
					CUT_ARMY_TICK, LOSING_STREAK_REQUIRED))

			local band = tonumber(botField("band") or "-1")
			if band ~= BAND_TWENTY_KILOTON then
				fault("Russia's bot fired band %d at t%d, expected %d (TwentyKiloton -- the"
					.. " RETALIATION WINDOW's band). %d (Kiloton) means it spent the beat on its"
					.. " permanent band instead: the window grant is ONE SHOT that vanishes when"
					.. " the window does, so firing under it throws the reply away, and the"
					.. " ruling's model is that the loser escalates",
					band, tick, BAND_TWENTY_KILOTON, BAND_KILOTON)
				ok = false
			end

			-- `fired`, NOT `reason`. The live reason is overwritten every evaluation and by now reads
			-- RateLimited -- correctly, the bot fired a few hundred ticks ago. `fired` is the sticky
			-- field that says why the LAUNCH happened.
			local firedReason = botField("fired")
			if firedReason ~= "Retaliation" then
				fault("Russia's bot reports fired=%q at t%d, expected \"Retaliation\"."
					.. " \"Permanent\" with the right band would mean the window was not what"
					.. " authorised the shot", tostring(firedReason), tick)
				ok = false
			end

			note(ok, "launched once at band %d by t%d (army cut t%d)", band, tick, CUT_ARMY_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE E. AND NOT AGAIN. MinTicksBetweenLaunches is 900 and the window grant was one
		-- shot; Russia is still losing, still has a 1 kt permanent band, and must still hold.
		if tick == NO_SECOND_LAUNCH_TICK then
			local ok = expectLaunches(1,
				string.format("a SECOND launch inside %d ticks of the first, against a"
					.. " MinTicksBetweenLaunches of %d. A losing bot with a regenerating permanent"
					.. " band will empty every band it has if the rate limit does not hold",
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
