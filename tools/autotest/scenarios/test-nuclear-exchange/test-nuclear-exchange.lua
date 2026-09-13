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
-- It also folds in the half of the feature a state read would miss. Every nuclear power in the mod
-- ships RequiresPurchase (nuclear-arsenal.yaml:108 and nine more) — and decision 02 BYPASSES that
-- in Escalation, where a band is a free power on a regeneration timer and nothing nuclear is
-- purchasable at all. The bin is what shows the difference: a bought power with an empty magazine
-- reads `hidden`, a free one on a timer reads `charging:<ticks>`. With DefaultCash: 0 nobody could
-- buy anything even if the bypass failed, so every `ready` below can only mean the exchange itself
-- loaded the warhead.
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
--   * Nuclear Posture. It scales the regeneration timers, and this run leaves it at Flexible —
--     the identity multiplier — so the compressed 300 ticks in rules.yaml is the number that
--     actually applies. NuclearExchangeStateTest pins the three multipliers without a world.
--   * The BUY TAB. SupportPowerProductionQueue filters its items on SupportPowerInstance.Purchasable
--     and there is no Lua binding that reads a production queue's contents, so "the nuclear powers
--     are absent from the shop" is asserted by NuclearExchangeStateTest and by the one line in
--     SupportPowerInstance's constructor that disables the bank, not from here.

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

-- rules.yaml compresses the 1 kt band's regeneration from the shipped 3000 (3:00) to this.
local REGEN_TICKS = 300

-- Phase boundaries, in ticks from t=0. Generous: every one of them is "well after the thing it is
-- waiting for", never a measurement of when that thing happened.
local RELEASE_CHECK_TICK = 90               -- release is at tick 10; 80 ticks of slack
local FIRE_TICK = 120
local PARITY_CHECK_TICK = FIRE_TICK + 60    -- the grant crosses a world trait, a player trait and
                                            -- a condition; NuclearExchangeInfo.GrantRetryTicks is 30
local REGEN_CHECK_TICK = FIRE_TICK + REGEN_TICKS + 60
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

	-- THE NOTE MUST NOT LIE, and the first version of this file did. `expect` records a fault and
	-- RETURNS rather than aborting, so the run reaches every phase and reports all of them -- which
	-- is what you want. But the "ok at t%d" note was then written unconditionally, so a result.json
	-- carrying four faults also carried "release ok at t90 | parity ok at t180 | lapse ok at t1240".
	-- Whoever triages that reads three passing phases beside four failures and has to work out which
	-- half is lying. Every note is now gated on every expect in its phase having returned true.
	local function note(ok, fmt, ...)
		notes[#notes + 1] = (ok and "" or "NOT ") .. string.format(fmt, ...)
	end

	local function fault(fmt, ...)
		faults[#faults + 1] = string.format(fmt, ...)
	end

	-- Compared EXACTLY against the bare vocabulary in TestGlobal.SupportPowerState. An earlier
	-- version of that binding appended " (bin: ...)" to every return, which made exact comparison
	-- unsatisfiable in three of its four callers -- so this is deliberately not a `find`.
	-- `charging:<n>` is the one token in the vocabulary that carries a value, so it is matched by
	-- PREFIX where every other reading is compared exactly. It is also a token this scenario could
	-- not produce at all before decision 02: while nuclear powers were bought, a spent one had an
	-- empty magazine and read `hidden`. Reading `charging:` IS the free-timer economy.
	local function expectCharging(player, who, key, why)
		local got = Test.GetSupportPowerState(player, key)
		if got:sub(1, 9) ~= "charging:" then
			fault("%s's %s reads %q, expected a `charging:<ticks>` reading. %s", who, key, got, why)
			return false
		end

		return true
	end

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
			local ok = expect(USA, "USA", USA_1KT, "ready",
				"the release gate opened at tick 10 and must hand both sides a loaded 1 kt warhead"
				.. " -- with DefaultCash 0 nothing else can have loaded it")
			ok = expect(Russia, "Russia", RU_1KT, "ready",
				"release is simultaneous for both sides; only one of them got it."
				.. " CHECK debug.log FOR THE `NUCLEAR EXCHANGE sides:` LINE FIRST: a side missing"
				.. " from it was never registered, and nothing downstream of that can work") and ok
			ok = expect(USA, "USA", USA_20KT, "hidden",
				"release is the LOWEST band only; the 20 kt band must stay dark until somebody fires") and ok
			ok = expect(Russia, "Russia", RU_20KT, "hidden",
				"release opened a band above 1 kt, or opened a retaliation window nobody provoked") and ok

			note(ok, "release ok at t%d", tick)
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
			local ok = expect(Russia, "Russia", RU_20KT, "ready",
				"being hit by 1 kt must arm Russia ONE BAND UP for the window, ready to fire the"
				.. " instant it opens. 'hidden' means the grant, the condition or the loaded shot"
				.. " did not arrive")
			ok = expect(USA, "USA", USA_20KT, "hidden",
				"THE FIRER CLIMBED BY FIRING. That is decision 06's shared pressure ladder, where"
				.. " both sides always read the same rung and going first was free -- not the"
				.. " exchange, where firing arms the OTHER side and costs you the shot") and ok
			ok = expect(Russia, "Russia", RU_1KT, "ready",
				"Russia's own 1 kt band went dark when it was hit; parity is permanent and additive") and ok

			-- THE SHOT WAS SPENT AND IS REGENERATING, which is the reading that did not exist before
			-- decision 02: a bought power with an empty magazine reads `hidden`, a free one on a
			-- timer reads `charging:<ticks>`. So this single token says the power is permitted, is
			-- NOT purchasable, and is on a clock -- the whole Escalation economy in one assertion.
			ok = expectCharging(USA, "USA", USA_1KT,
				"USA fired its only 1 kt warhead and the band must now be on its regeneration timer."
				.. " `hidden` means the power is still a BOUGHT power with an empty magazine and the"
				.. " Escalation bypass did not apply; `ready` means firing cost nothing at all") and ok

			note(ok, "parity ok at t%d (fired t%d)", tick, FIRE_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE E. THE REGENERATION, and the assertion this scenario gained with decision 02.
		-- USA fired its 1 kt at t120 and nobody has any money. If it reads `ready` again now, the
		-- only thing that can have reloaded it is the band's own timer.
		if tick == REGEN_CHECK_TICK then
			local ok = expect(USA, "USA", USA_1KT, "ready",
				string.format("the 1 kt band did not come back %d ticks after it was fired, with"
					.. " DefaultCash 0. A permanent band in Escalation is a FREE power on a"
					.. " regeneration timer -- if this still reads `charging:` the timer is longer"
					.. " than the override in rules.yaml, and if it reads `hidden` the power is"
					.. " being bought rather than regenerated", REGEN_TICKS))

			-- AND THE WINDOW BAND HAS NOT QUIETLY DONE THE SAME. Russia's 20 kt is a one-shot grant,
			-- not a permanent band, and its own regeneration is left at the shipped 4000 -- so it
			-- must still be readable as the grant rather than as a band that recharged.
			ok = expect(Russia, "Russia", RU_20KT, "ready",
				"Russia's retaliation window closed early: it is a one-minute grant and this is only"
				.. " " .. tostring(REGEN_CHECK_TICK - FIRE_TICK) .. " ticks in") and ok

			note(ok, "regen ok at t%d, %d ticks after the shot", tick, tick - FIRE_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE D. The window was TIME-BOXED. One minute on, the 20 kt grant is gone and the
		-- permanent parity is not. "If the window lapses unused, B stays at Y."
		if tick == LAPSE_CHECK_TICK then
			local ok = expect(Russia, "Russia", RU_20KT, "hidden",
				"the retaliation window never lapsed. A grant that outlives its window is the"
				.. " indefinite 'draw card' the ruling exists to prevent -- a side could hold a"
				.. " reply forever and stall the match")
			ok = expect(Russia, "Russia", RU_1KT, "ready",
				"the PERMANENT band lapsed with the window. Only the window expires") and ok

			note(ok, "lapse ok at t%d, %d ticks after the shot", tick, tick - FIRE_TICK)
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
