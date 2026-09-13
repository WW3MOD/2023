-- ASSERTING AUTOTEST — firing arms the OTHER side, and the grant it buys them expires.
--
-- Layout and intent live in description.txt. This file drives the clock and reads the bin.
--
-- WHY EVERY READING IS Test.GetSupportPowerState AND NOT A TRAIT READ. There is no Lua binding on
-- NuclearExchange and this scenario deliberately does not add one: the ruling is about what a
-- player can DO, and GetSupportPowerState reads SupportPowerInstance.Disabled — the same predicate
-- SupportPowersWidget filters its icon list on (SupportPowersWidget.cs:136). So 'ready' here means
-- a cameo the player could click, which is the claim, rather than an int matching an int.
--
-- It also folds in the half of the feature a state read would miss. EVERY NUCLEAR POWER IN THE MOD
-- IS RequiresPurchase (nuclear-arsenal.yaml:108 and nine more), so a band that is merely PERMITTED
-- is a shop entry and a bill — SupportPowerInstance.Disabled stays true at zero banked shots. With
-- DefaultCash: 0 in rules.yaml nobody can buy anything, so 'ready' can only mean the exchange
-- loaded the warhead itself (SupportPowerInstance.MakeFireReady).
--
-- THE ONE READING THAT SEPARATES THIS MODEL FROM THE ONE IT REPLACED is the pair in phase C:
-- Russia's 20 kt READY and USA's 20 kt still HIDDEN, off the same shot. Under decision 06's shared
-- pressure ladder both sides always read the same rung, so both would have opened together —
-- "going first is free" was that model's stated and accepted cost. If a future change quietly
-- restores a shared counter, this is the assertion that catches it and nothing else in the tree
-- will.
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * The release gate's countdown. NuclearReleaseLadderTest pins it tick-exactly without a world;
--     rules.yaml compresses it to 10 ticks here precisely so it is not under test.
--   * The warhead. Nothing is aimed at anything — the exchange arms the other side "wherever it
--     lands", which is the rule decision 01 chose over decision 14's damage attribution, so a
--     scenario that shot something would be asserting a rule this model does not have.
--   * The game-ender path. Firing one calls DoomsdayStrike.BeginFinalExchange, which is a STUB on
--     this branch pending wt/deadhand-window; asserting a stub would pin the wrong behaviour.
--   * Nuclear Posture. It scales ChargeInterval, and no nuclear power in the mod has one.

-- TICK DOMAIN. Every boundary below is in TICKS, never TestHarness seconds: that helper runs 25
-- ticks per second against a mod at 16.67, and the retaliation window this measures is a tick count
-- the engine computed from minutes. THERE IS NO Trigger.OnTick IN THIS ENGINE; a self-rescheduling
-- Trigger.AfterDelay(1) is the idiom, and it is what the phase machine below is built on.

-- The four powers, by OrderName (SupportPowerManager keys its dictionary on it).
local USA_1KT = "B61LowStrike"       -- B61-12 low dial, 300 t  -> Kiloton band
local USA_20KT = "B61MidStrike"      -- B61-12 mid dial, 10000 t -> TwentyKiloton band
local RU_1KT = "Ru9M729Strike"       -- 9M729, 1000 t            -> Kiloton band
local RU_20KT = "RuIskanderStrike"   -- Iskander, 10000 t        -> TwentyKiloton band

-- Empty ground, far from both Supply Routes and from nothing in particular. See the header.
local AIM_X, AIM_Y = 32, 8

-- 1 minute at the mod's 60 ms timestep; rules.yaml sets RetaliationWindowDefault: 1.
local WINDOW_TICKS = 1000

-- Phase boundaries, in ticks from t=0. Generous: every one of them is "well after the thing it is
-- waiting for", never a measurement of when that thing happened.
local RELEASE_CHECK_TICK = 90               -- release is at tick 10; 80 ticks of slack
local FIRE_TICK = 120
local PARITY_CHECK_TICK = FIRE_TICK + 60    -- the grant crosses a world trait, a player trait and
                                            -- a condition; NuclearExchangeInfo.GrantRetryTicks is 30
local LAPSE_CHECK_TICK = FIRE_TICK + WINDOW_TICKS + 120
local BUDGET_TICK = LAPSE_CHECK_TICK + 200

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	local tick = 0
	local faults = {}
	local notes = {}
	local fireResult = "not-attempted"

	local function state(player, key)
		return Test.GetSupportPowerState(player, key)
	end

	local function note(fmt, ...)
		notes[#notes + 1] = string.format(fmt, ...)
	end

	local function fault(fmt, ...)
		faults[#faults + 1] = string.format(fmt, ...)
	end

	-- Compared EXACTLY against the bare vocabulary in TestGlobal.SupportPowerState. An earlier
	-- version of that binding appended " (bin: ...)" to every return, which made exact comparison
	-- unsatisfiable in three of its four callers -- so this is deliberately not a `find`.
	local function expect(player, who, key, want, why)
		local got = state(player, key)
		if got ~= want then
			fault("%s's %s reads %q, expected %q. %s", who, key, got, want, why)
			return false
		end

		return true
	end

	local function verdict()
		local summary = string.format(
			"fire=%q | USA 1kt=%s 20kt=%s | Russia 1kt=%s 20kt=%s | %s",
			fireResult, state(USA, USA_1KT), state(USA, USA_20KT),
			state(Russia, RU_1KT), state(Russia, RU_20KT),
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

		-- ---- PHASE A. Release gave BOTH sides the 1 kt band, permanently, and gave nobody a
		-- window. Release is parity and not a provocation: a window opened here would hand both
		-- sides the 20 kt band for a minute having been fired at by nobody.
		if tick == RELEASE_CHECK_TICK then
			expect(USA, "USA", USA_1KT, "ready",
				"the release gate opened at tick 10 and must hand both sides a loaded 1 kt warhead"
				.. " -- with DefaultCash 0 nothing else can have loaded it")
			expect(Russia, "Russia", RU_1KT, "ready",
				"release is simultaneous for both sides; only one of them got it")
			expect(USA, "USA", USA_20KT, "hidden",
				"release is the LOWEST band only; the 20 kt band must stay dark until somebody fires")
			expect(Russia, "Russia", RU_20KT, "hidden",
				"release opened a band above 1 kt, or opened a retaliation window nobody provoked")

			note("release ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE B. USA fires the smallest warhead in the mod at empty ground.
		if tick == FIRE_TICK then
			fireResult = Test.ActivateSupportPower(USA, USA_1KT, CPos.New(AIM_X, AIM_Y))
			if fireResult ~= "issued" then
				fault("USA could not fire %s: %q. Nothing downstream of this means anything,"
					.. " so the parity and window readings below are not evidence either way",
					USA_1KT, fireResult)
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE C. THE PARITY + WINDOW EDGE, and the whole point of the scenario.
		if tick == PARITY_CHECK_TICK then
			expect(Russia, "Russia", RU_20KT, "ready",
				"being hit by 1 kt must arm Russia ONE BAND UP for the window, ready to fire the"
				.. " instant it opens. 'hidden' means the grant, the condition or the loaded shot"
				.. " did not arrive")
			expect(USA, "USA", USA_20KT, "hidden",
				"THE FIRER CLIMBED BY FIRING. That is decision 06's shared pressure ladder, where"
				.. " both sides always read the same rung and going first was free -- not the"
				.. " exchange, where firing arms the OTHER side and costs you the shot")
			expect(Russia, "Russia", RU_1KT, "ready",
				"Russia's own 1 kt band went dark when it was hit; parity is permanent and additive")

			note("parity ok at t%d (fired t%d)", tick, FIRE_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE D. The window was TIME-BOXED. One minute on, the 20 kt grant is gone and the
		-- permanent parity is not. "If the window lapses unused, B stays at Y."
		if tick == LAPSE_CHECK_TICK then
			expect(Russia, "Russia", RU_20KT, "hidden",
				"the retaliation window never lapsed. A grant that outlives its window is the"
				.. " indefinite 'draw card' the ruling exists to prevent -- a side could hold a"
				.. " reply forever and stall the match")
			expect(Russia, "Russia", RU_1KT, "ready",
				"the PERMANENT band lapsed with the window. Only the window expires")

			note("lapse ok at t%d, %d ticks after the shot", tick, tick - FIRE_TICK)
			verdict()
			return
		end

		if tick >= BUDGET_TICK then
			fault("ran out of budget at tick %d without reaching the lapse check at %d",
				tick, LAPSE_CHECK_TICK)
			verdict()
			return
		end

		Trigger.AfterDelay(1, step)
	end

	Trigger.AfterDelay(1, step)
end
