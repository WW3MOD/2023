-- ASSERTING AUTOTEST — the retaliation window at the TOP rung hands over a game-ender the player
-- can actually pick up and fire, and hands over only the ones that player's faction owns.
--
-- Layout, the five-launch climb and the lobby pinning live in map.yaml and rules.yaml. This file
-- drives the clock and reads the bin.
--
-- WHY EVERY READING IS Test.GetSupportPowerState. Same reason test-nuclear-exchange gives, and it
-- matters more here than it does there: the user's report is "I could see the countdown ... but I
-- never got those weapons available", which is a claim about THE SUPPORT POWER COLUMN and not
-- about any internal level. GetSupportPowerState reads SupportPowerInstance.Disabled, the same
-- predicate SupportPowersWidget filters its icon list on (SupportPowersWidget.cs:136), so `ready`
-- here means a cameo the player would see and could click.
--
-- AND ONE READING IS NOT A BIN READ, DELIBERATELY. Phase G calls Test.ActivateSupportPower, which
-- goes through the real order path (SupportPowerManager.ResolveOrder). A drawn cameo and an
-- accepted order are different claims and the user made both: "I should be able to choose the game
-- ender AND place the targets".
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * The window LAPSING. test-nuclear-exchange's subject; every reply here lands with ~900 ticks
--     of a 1000-tick window to spare, so nothing below is near an edge.
--   * The release gate's countdown. NuclearReleaseLadderTest pins it without a world.
--   * THE ENDING ITSELF. Firing a game-ender calls DoomsdayStrike.BeginFinalExchange, which returns
--     immediately in a TestMode session unless RunInTestMode is set (DoomsdayStrike.cs:458-459) and
--     this scenario leaves it false. So phase G asserts the order is ACCEPTED, not that the world
--     is annihilated -- with the salvo live, BeginFinalExchange freezes statistics, reveals the map
--     and resolves the match while this poller is still writing. demo-doomsday-deadhand is where
--     the ending is the subject.
--   * The warhead. Nothing is aimed at anything; the exchange arms the other side wherever it lands.

-- TICK DOMAIN. Every boundary below is in TICKS, never TestHarness seconds. There is no
-- Trigger.OnTick in this engine; a self-rescheduling Trigger.AfterDelay(1) is the idiom.

-- ---- THE TWO LADDERS, BY OrderName (SupportPowerManager keys its dictionary on it) ----
-- Yields are tons of TNT and the band is NuclearReleaseLadder.RungForYield's answer for that yield.
local USA_1KT    = "B61LowStrike"       -- 300 t     band 1  Kiloton
local USA_50KT   = "B61MaxStrike"       -- 50000 t   band 3  FiftyKiloton
local USA_100KT  = "W76Strike"          -- 100000 t  band 4  HundredKiloton
local USA_ENDER  = "B83Strike"          -- 1200000 t band 5  GameEnder, powers.event + player.america
local RU_20KT    = "RuIskanderStrike"   -- 10000 t   band 2  TwentyKiloton
local RU_100KT   = "RuKalibrStrike"     -- 100000 t  band 4  HundredKiloton
local RU_ENDER   = "SarmatStrike"       -- 750000 t  band 5  GameEnder, powers.event + player.russia

-- THE SECOND NEGATIVE CONTROL, and the one that pins the user's ruling of 2026-09-14: NATIONAL
-- ENDER ONLY, exactly one END cameo per side. This power is band 5 like the other two and declares
-- `powers.event` with NO `player.*` beside it (player.yaml:842), so it names no owner and neither
-- arming path may hand it over. Asserted `hidden` in phases A, F and H.
--
-- ITS LOBBY CHECKBOX IS ON AND LOCKED IN rules.yaml, WHICH IS WHAT MAKES THAT ASSERTION EVIDENCE.
-- With HighYieldNukeCheckboxEnabled false its RequiresCondition would be unsatisfied and `hidden`
-- would be true for a reason that has nothing to do with attribution. Do not "tidy" that pin away.
local UNOWNED_ENDER = "HighYieldNukeStrike" -- 6000000 t band 5 GameEnder, powers.event only

-- NEGATIVE CONTROL, and the only band-2 power in the mod on the event tier (player.yaml:711). The
-- host checkbox that gates it is OFF at shipped default and rules.yaml locks it there, so it must
-- stay dark through a band-2 window that lights RuIskander beside it. A fix that forced readiness
-- without re-checking the power's own RequiresCondition would light this up.
local TACNUKE    = "TacNukeStrike"       -- 20000 t  band 2  TwentyKiloton, powers.event only

-- Empty ground, far from both Supply Routes. See map.yaml.
local AIM_X, AIM_Y = 32, 8

-- Phase boundaries, in ticks from t=0. Every one is "well after the thing it waits for", never a
-- measurement of when that thing happened. NuclearExchangeInfo.GrantRetryTicks is 30, so 65 ticks
-- between a launch and the check that reads its grant is twice the budget the engine gives itself.
local RELEASE_CHECK_TICK = 60
local L1_TICK            = 80    -- USA  band 1  -> RU  window 2
local L2_CHECK_TICK      = 145
local L2_TICK            = 150   -- RU   band 2  -> USA window 3
local L3_CHECK_TICK      = 215
local L3_TICK            = 220   -- USA  band 3  -> RU  window 4
local L4_CHECK_TICK      = 285
local L4_TICK            = 290   -- RU   band 4  -> USA window 5 = GameEnder
local END_CHECK_TICK     = 355   -- <<< THE ASSERTION THE USER'S BUG IS
local L5_TICK            = 360   -- USA  band 4 (its PERMANENT one) -> RU window 5 = GameEnder
local END2_CHECK_TICK    = 425
local FIRE_ENDER_TICK    = 430
local BUDGET_TICK        = 520

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	local tick = 0
	local faults = {}
	local notes = {}
	local enderFireResult = "not-attempted"

	local function state(player, key)
		return Test.GetSupportPowerState(player, key)
	end

	local function fault(fmt, ...)
		faults[#faults + 1] = string.format(fmt, ...)
	end

	-- Gated on every expect in its phase, for the reason test-nuclear-exchange records: an
	-- unconditional "ok at t%d" beside a fault makes a result.json that lies about half of itself.
	local function note(ok, fmt, ...)
		notes[#notes + 1] = (ok and "" or "NOT ") .. string.format(fmt, ...)
	end

	-- Compared EXACTLY against the bare vocabulary in TestGlobal.SupportPowerState. That binding
	-- returns one bare token with no decoration; do not turn this into a `find`.
	local function expect(player, who, key, want, why)
		local got = state(player, key)
		if got ~= want then
			fault("%s's %s reads %q, expected %q. %s", who, key, got, want, why)
			return false
		end

		return true
	end

	-- Fire, and treat anything but "issued" as fatal to everything downstream. A climb that stalls
	-- at rung N makes every later reading meaningless rather than merely wrong, so say so.
	local function launch(player, who, key, why)
		local result = Test.ActivateSupportPower(player, key, CPos.New(AIM_X, AIM_Y))
		if result ~= "issued" then
			fault("%s could not fire %s: %q. %s Nothing after this point is evidence either way",
				who, key, result, why)
			return false
		end

		return true
	end

	local function verdict()
		local summary = string.format(
			"ender-fire=%q | USA: %s=%s %s=%s %s=%s | Russia: %s=%s %s=%s | %s",
			enderFireResult,
			USA_ENDER, state(USA, USA_ENDER),
			UNOWNED_ENDER, state(USA, UNOWNED_ENDER),
			RU_ENDER, state(USA, RU_ENDER),
			RU_ENDER, state(Russia, RU_ENDER),
			USA_ENDER, state(Russia, USA_ENDER),
			table.concat(notes, " | "))

		-- THE WHOLE BIN, BOTH SIDES, printed rather than asserted. An exact-set assertion would be
		-- brittle -- the lower bands come and go on their regeneration timers -- but "exactly one
		-- END cameo per side" is the ruling, and a triager reading a failure wants to see WHICH
		-- cameos were drawn rather than infer it from four per-power tokens.
		summary = summary .. " | BIN USA: " .. Test.GetSupportPowerBin(USA)
			.. " | BIN Russia: " .. Test.GetSupportPowerBin(Russia)

		if #faults > 0 then
			Test.Fail(table.concat(faults, " ;; ") .. " ;; READINGS: " .. summary)
		else
			Test.Pass()
		end
	end

	local step

	step = function()
		tick = tick + 1

		-- ---- PHASE A. THE BASELINE, and it is what makes every later `ready` mean something.
		-- Release hands both sides the 1 kt band and nothing else. If a game-ender were already
		-- readable here, the whole climb below would be measuring nothing.
		if tick == RELEASE_CHECK_TICK then
			local ok = expect(USA, "USA", USA_1KT, "ready",
				"the release gate opened at tick 10 and must hand USA a loaded 1 kt warhead."
				.. " CHECK debug.log FOR THE `NUCLEAR EXCHANGE sides:` LINE FIRST: a side missing"
				.. " from it was never registered and nothing downstream of that can work")
			ok = expect(USA, "USA", USA_ENDER, "hidden",
				"A GAME-ENDER IS READABLE AT RELEASE. Decision 01: 'game-enders exist only as a"
				.. " retaliation grant ... there is no indefinite draw card'. Every `ready` later"
				.. " in this file is worthless if this one is not `hidden`") and ok
			ok = expect(Russia, "Russia", RU_ENDER, "hidden",
				"same rule, Russia's side of it") and ok
			ok = expect(USA, "USA", UNOWNED_ENDER, "hidden",
				"the 6 Mt strategic strike is band 5 like the other two and must be dark at release"
				.. " despite its lobby checkbox being ON (its shipped default, pinned in rules.yaml)."
				.. " Phases F and H assert it is STILL dark inside an open END window; this is the"
				.. " baseline those two are measured against") and ok

			note(ok, "baseline ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE B. L1: USA fires 1 kt. Arms Russia at band 2.
		if tick == L1_TICK then
			if not launch(USA, "USA", USA_1KT, "This is rung 1 of 4 and the shot release just handed it.") then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE C. Russia's band-2 window, plus the SCOPE control.
		if tick == L2_CHECK_TICK then
			local ok = expect(Russia, "Russia", RU_20KT, "ready",
				"being hit by 1 kt must arm Russia one band up, ready the instant the window opens")
			-- THE CONTROL. TacNuke is also band 2 and also `powers.event`, but its host checkbox is
			-- OFF at shipped default -- so its RequiresCondition is unsatisfied and no grant path
			-- may reach it. This is what a fix that forced readiness without re-checking the
			-- power's own condition would break, and it is the reason the override below is scoped
			-- to the top rung rather than applied to every band.
			ok = expect(Russia, "Russia", TACNUKE, "hidden",
				"THE 20 KT WINDOW LEAKED INTO A POWER THE HOST SWITCHED OFF. TacNukeStrike is gated"
				.. " on `!tacnuke-disabled && nuclear-release-20kt` and the first conjunct is false"
				.. " here (TacticalNukeCheckboxEnabled is false and locked in rules.yaml), so no"
				.. " band grant may make it readable") and ok

			note(ok, "band-2 window ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		if tick == L2_TICK then
			if not launch(Russia, "Russia", RU_20KT, "Rung 2 of 4, fired from the window L1 opened.") then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE D. USA's band-3 window.
		if tick == L3_CHECK_TICK then
			local ok = expect(USA, "USA", USA_50KT, "ready",
				"being hit by 20 kt must arm USA at 50 kt")
			note(ok, "band-3 window ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		if tick == L3_TICK then
			if not launch(USA, "USA", USA_50KT, "Rung 3 of 4.") then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE E. Russia's band-4 window. The last rung below the top.
		if tick == L4_CHECK_TICK then
			local ok = expect(Russia, "Russia", RU_100KT, "ready",
				"being hit by 50 kt must arm Russia at 100 kt -- the band whose reply is the ender")
			note(ok, "band-4 window ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		if tick == L4_TICK then
			if not launch(Russia, "Russia", RU_100KT,
				"Rung 4 of 4: the 100 kt shot whose reply is a game-ender.") then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE F. THE USER'S BUG, AND THE WHOLE POINT OF THE FILE.
		-- USA has been hit by 100 kt. Its retaliation window is at NuclearRung.GameEnder, the
		-- ledger's END box is lit with its countdown, and the question is whether a cameo appeared.
		if tick == END_CHECK_TICK then
			local ok = expect(USA, "USA", USA_ENDER, "ready",
				"THE END WINDOW GRANTED NOTHING THE PLAYER CAN SEE -- this is the reported bug."
				.. " USA's retaliation window is at NuclearRung.GameEnder (the ledger's END box is"
				.. " lit and counting down) and B83Strike must be a cameo USA can click."
				.. " `hidden` means SupportPowerInstance.Disabled is true, which folds in Permitted,"
				.. " which folds in prereqsAvailable -- and B83 declares `powers.event`, a"
				.. " prerequisite NO faction provides (player.yaml:144, :238). The grant loop in"
				.. " NuclearExchange.MakeBandsReady skips any power that is not already Permitted,"
				.. " so the one call that could clear that flag (SupportPowerInstance.MakeReady)"
				.. " is never reached. Compare DoomsdayStrike.ArmGameEnders, which solves exactly"
				.. " this by asking NuclearGameEnders.ArmableBy instead")

			-- THE FACTION LOCK, which the 2026-09-14 ruling (c8cadc8a) put on the three enders and
			-- which nothing in the tree could see until now. A USA human must never be offered the
			-- Sarmat: its prerequisites are `powers.event, player.russia`, and only the FIRST of
			-- those is the window's to override.
			ok = expect(USA, "USA", RU_ENDER, "hidden",
				"USA WAS HANDED RUSSIA'S WARHEAD. SarmatStrike declares `powers.event,"
				.. " player.russia` (player.yaml:236); the END window may override the event tier"
				.. " and may NOT override the faction. A blanket prerequisite bypass gives exactly"
				.. " this reading and undoes c8cadc8a") and ok

			-- NATIONAL ENDER ONLY -- USER RULING, 2026-09-14. Exactly one END cameo per side. The
			-- 6 Mt strategic strike is band 5 and inside the yield ceiling, its lobby checkbox is ON
			-- and locked, and its RequiresCondition IS satisfied here -- every gate but one is open,
			-- and the one that is shut is attribution: `powers.event` and no `player.*` beside it
			-- (player.yaml:842), so it names no owner and NuclearGameEnders.ArmableBy refuses it.
			--
			-- THIS IS THE ASSERTION THAT WOULD CATCH THE RULING BEING UNDONE BY A TIDY-UP. The
			-- natural "simplification" of ArmableBy is to let an all-overridden prerequisite list
			-- mean "owned by everyone", which is what it meant before the ruling and what every
			-- ordinary HasPrerequisites-style check means. That single polarity flip puts a second
			-- cameo in both columns and nothing else in the tree would notice.
			ok = expect(USA, "USA", UNOWNED_ENDER, "hidden",
				"A SECOND END CAMEO APPEARED. The 6 Mt strategic strike names no owner"
				.. " (`Prerequisites: powers.event` alone, player.yaml:842) and the ruling is one"
				.. " national ender per side -- B83 for USA, Sarmat for Russia. `ready` here means"
				.. " NuclearGameEnders.ArmableBy treated an empty owner list as 'everybody owns it'"
				.. " rather than 'nobody does'. Its lobby checkbox is ON and locked in rules.yaml,"
				.. " so this reading is about attribution and not about the condition") and ok

			-- Needed by L5 below, and worth its own message so a failure names the rung.
			ok = expect(USA, "USA", USA_100KT, "ready",
				"being hit by 100 kt gives USA that band PERMANENTLY as well as the window above it"
				.. " (NuclearExchangeState.ReportLaunch's permanentBand), and L5 fires it") and ok

			-- Russia holds no ender yet: its band-4 window was SPENT by L4 and its permanent level
			-- is 3. This is what makes phase H a measurement rather than a restatement.
			ok = expect(Russia, "Russia", RU_ENDER, "hidden",
				"Russia has not been hit by 100 kt yet -- it FIRED the 100 kt shot, and firing arms"
				.. " the other side rather than yourself. A `ready` here is decision 06's shared"
				.. " ladder coming back") and ok

			note(ok, "END window ok at t%d (100 kt fired t%d)", tick, L4_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE G. L5: USA fires its PERMANENT 100 kt, which arms Russia's END window without
		-- spending USA's own -- ReportLaunch only spends a window when `band >= firer.WindowLevel`
		-- (NuclearExchangeState.cs:464) and 4 >= 5 is false.
		if tick == L5_TICK then
			if not launch(USA, "USA", USA_100KT,
				"USA's permanent band 4, fired to open Russia's END window.") then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE H. THE RUSSIAN HALF, and the mirror of every faction assertion in phase F.
		if tick == END2_CHECK_TICK then
			local ok = expect(Russia, "Russia", RU_ENDER, "ready",
				"Russia's END window granted nothing. Same defect as phase F, Russia's side of it:"
				.. " SarmatStrike declares `powers.event, player.russia` and Russia holds the"
				.. " faction half")
			ok = expect(Russia, "Russia", USA_ENDER, "hidden",
				"RUSSIA WAS HANDED AMERICA'S WARHEAD. B83Strike declares `powers.event,"
				.. " player.america` (player.yaml:238)") and ok
			ok = expect(Russia, "Russia", UNOWNED_ENDER, "hidden",
				"A SECOND END CAMEO APPEARED IN RUSSIA'S COLUMN. Russia's half of the 2026-09-14"
				.. " ruling: the unowned 6 Mt strike is withheld from both sides, not from one") and ok

			-- USA'S OWN WINDOW SURVIVED L5, which is the one reading that pins the spend rule.
			ok = expect(USA, "USA", USA_ENDER, "ready",
				"USA LOST ITS END WINDOW BY FIRING A LOWER BAND. ReportLaunch spends the firer's"
				.. " window only when the band fired is at or above the window's own"
				.. " (NuclearExchangeState.cs:464): 'a side sitting on a 50 kt window that fires its"
				.. " permanent 1 kt has not used the grant and keeps it'") and ok

			note(ok, "both enders ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE I. A DRAWN CAMEO IS NOT A FIREABLE WEAPON. The user's sentence has two halves
		-- and this is the second: the order must be ACCEPTED by the real path, not merely drawn.
		if tick == FIRE_ENDER_TICK then
			enderFireResult = Test.ActivateSupportPower(USA, USA_ENDER, CPos.New(AIM_X, AIM_Y))
			if enderFireResult ~= "issued" then
				fault("USA could not FIRE %s inside its END window: %q. The cameo being drawn and"
					.. " the order being accepted are different claims -- SupportPowerInstance.Ready"
					.. " is `Active && RemainingTicks == 0` and the bin filters on Disabled, so a"
					.. " power can draw and still refuse the order", USA_ENDER, enderFireResult)
			end

			note(enderFireResult == "issued", "ender fired at t%d", tick)
			verdict()
			return
		end

		if tick >= BUDGET_TICK then
			fault("ran out of budget at tick %d without reaching the ender launch at %d",
				tick, FIRE_ENDER_TICK)
			verdict()
			return
		end

		Trigger.AfterDelay(1, step)
	end

	Trigger.AfterDelay(1, step)
end
