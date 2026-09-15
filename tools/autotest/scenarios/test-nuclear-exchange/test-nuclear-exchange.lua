-- ASSERTING AUTOTEST — the LEVEL RATCHET and the SIDE COOLDOWN (nuclear exchange v2, 2026-09-15).
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
-- in Escalation, where nothing nuclear is purchasable at all. The bin is what shows the difference:
-- a bought power with an empty magazine reads `hidden`, a free one on a timer reads
-- `charging:<ticks>`. With DefaultCash: 0 nobody could buy anything even if the bypass failed, so
-- every `ready` below can only mean the exchange itself loaded the warhead.
--
-- ==== THE THREE READINGS THAT SEPARATE v2 FROM EVERY MODEL BEFORE IT ====
--
--   PHASE C  Russia's 20 kt READY and USA's 20 kt still HIDDEN, off the same shot. Under decision
--            06's shared pressure ladder both sides always read the same rung and both would have
--            opened together — "going first is free" was that model's stated and accepted cost.
--            The ratchet inverts it: firing arms the OTHER side and never yourself.
--
--   PHASE C  USA's own 1 kt AND Russia's 1 kt both `charging:` after ONE shot each. The cooldown is
--            SIDE-WIDE: under v1 firing a 1 kt muted the 1 kt band alone and left 20/50/100 kt
--            loaded, which is the "too many nukes in flight" the user ruled against. Phase D is
--            where this bites hardest — see below.
--
--   PHASE D  USA IS ESCALATED TO 50 KT WHILE STILL RELOADING, and its brand-new 50 kt cameo must
--            read `charging:` rather than `ready`. THIS IS THE SHARPEST ASSERTION IN THE FILE. The
--            level and the cooldown are two separate gates, and the natural implementation of a
--            level rise — make the newly granted bands ready — quietly opens a free shot inside a
--            cooldown that was meant to deny it. Being shot at while you are reloading is the
--            NORMAL case in this model, not an edge one, so a build with that bug would be broken
--            in most matches and pass every other test in the tree.
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * The release gate's countdown. NuclearReleaseLadderTest pins it tick-exactly without a world;
--     rules.yaml compresses it to 10 ticks here precisely so it is not under test.
--   * The warhead. Nothing is aimed at anything — the exchange escalates the other side "wherever
--     it lands", which is the rule decision 01 chose over decision 14's damage attribution and
--     which v2 keeps, so a scenario that shot something would assert a rule this model does not have.
--   * THE ENDING ITSELF. Phase H fires a game-ender and asserts the ORDER IS ACCEPTED, not that the
--     world is annihilated: DoomsdayStrike.BeginFinalExchange returns immediately in a TestMode
--     session unless RunInTestMode is set (DoomsdayStrike.cs:458-459) and this scenario leaves it
--     false, because with the salvo live it freezes statistics, reveals the map and resolves the
--     match while this poller is still writing. demo-doomsday-deadhand is where the ending is the
--     subject. A drawn cameo and an accepted order are still two different claims and both are made.
--   * Nuclear Posture. It scales the cooldowns and this run leaves it at Flexible — the identity
--     multiplier — so the compressed values in rules.yaml are the numbers that actually apply.
--     NuclearExchangeStateTest pins the three multipliers without a world.
--   * The BUY TAB. SupportPowerProductionQueue filters its items on SupportPowerInstance.Purchasable
--     and there is no Lua binding that reads a production queue's contents, so "the nuclear powers
--     are absent from the shop" is asserted by NuclearExchangeStateTest and by the one line in
--     SupportPowerInstance's constructor that disables the bank, not from here.

-- TICK DOMAIN. Every boundary below is in TICKS, never TestHarness seconds: that helper runs 25
-- ticks per second against a mod at 16.67, and every cooldown this measures is a tick count the
-- engine took straight out of rules.yaml. THERE IS NO Trigger.OnTick IN THIS ENGINE; a
-- self-rescheduling Trigger.AfterDelay(1) is the idiom, and it is what the phase machine is built on.

-- ---- THE TWO LADDERS, BY OrderName (SupportPowerManager keys its dictionary on it) ----
-- Yields are tons of TNT and the band is NuclearReleaseLadder.RungForYield's answer for that yield.
local USA_1KT    = "B61LowStrike"       -- 300 t     band 1  Kiloton
local USA_20KT   = "B61MidStrike"       -- 10000 t   band 2  TwentyKiloton
local USA_50KT   = "B61MaxStrike"       -- 50000 t   band 3  FiftyKiloton
local USA_100KT  = "W76Strike"          -- 100000 t  band 4  HundredKiloton
local USA_ENDER  = "B83Strike"          -- 1200000 t band 5  GameEnder, powers.event + player.america
local RU_1KT     = "Ru9M729Strike"      -- 1000 t    band 1  Kiloton
local RU_20KT    = "RuIskanderStrike"   -- 10000 t   band 2  TwentyKiloton
local RU_100KT   = "RuKalibrStrike"     -- 100000 t  band 4  HundredKiloton
local RU_ENDER   = "SarmatStrike"       -- 750000 t  band 5  GameEnder, powers.event + player.russia

-- Empty ground, far from both Supply Routes and from nothing in particular. See map.yaml.
local AIM_X, AIM_Y = 32, 8

-- ---- THE COMPRESSED COOLDOWNS, RESTATED FROM rules.yaml ----------------------------------------
-- The SHIPPED values are 5000/7000/9000/12000 ticks (5/7/9/12 minutes). rules.yaml compresses them
-- to these, and every phase boundary below is arithmetic on them — so a change to one without a
-- change here turns a real failure into a timing artefact.
--
-- THE FOUR ARE KEPT DISTINCT, AND THAT IS THE POINT OF NOT USING ONE NUMBER: a build that applied
-- the FIRED band's cooldown correctly and a build that applied the firer's own band, or the first
-- entry in the table, are the same run at 300/300/300/300 and different runs at these.
local CD_1KT   = 300
local CD_20KT  = 320
local CD_50KT  = 340
local CD_100KT = 360

-- Phase boundaries, in ticks from t=0. Every CHECK is "well after the thing it waits for" — never a
-- measurement of when that thing happened — EXCEPT the two that straddle a cooldown boundary on
-- purpose (D and E), which are the whole subject of the file. NuclearExchangeInfo.GrantRetryTicks
-- is 30, so 65 ticks between a launch and the check that reads its grant is twice that budget.
local RELEASE_CHECK_TICK = 90
local FIRE_1KT_TICK      = 120                        -- USA b1 -> USA cooldown to 420; RU level 2
local RATCHET_CHECK_TICK = 185
local FIRE_20KT_TICK     = 200                        -- RU  b2 -> RU cooldown to 520;  USA level 3
local LOCKED_CHECK_TICK  = 265                        -- <<< USA escalated WHILE RELOADING
local RECOVER_CHECK_TICK = 450                        -- USA's own cooldown ended at 420
local FIRE_50KT_TICK     = 560                        -- USA b3 -> USA cooldown to 900; RU level 4
local L4_CHECK_TICK      = 625
local FIRE_100KT_TICK    = 640                        -- RU  b4 -> RU cooldown to 1000; USA level 5
local END_LOCKED_TICK    = 705                        -- <<< USA at END, still reloading
local END_READY_TICK     = 970                        -- USA's cooldown ended at 900
local FIRE_ENDER_TICK    = 980
local BUDGET_TICK        = 1120

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

	-- THE NOTE MUST NOT LIE, and the first version of this file did. `expect` records a fault and
	-- RETURNS rather than aborting, so the run reaches every phase and reports all of them -- which
	-- is what you want. But the "ok at t%d" note was then written unconditionally, so a result.json
	-- carrying four faults also carried "release ok at t90 | ratchet ok at t185". Whoever triages
	-- that reads passing phases beside failures and has to work out which half is lying. Every note
	-- is gated on every expect in its phase having returned true.
	local function note(ok, fmt, ...)
		notes[#notes + 1] = (ok and "" or "NOT ") .. string.format(fmt, ...)
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

	-- `charging:<n>` is the one token in the vocabulary that carries a value, so it is matched by
	-- PREFIX where every other reading is compared exactly. It is also a token this scenario could
	-- not produce at all before decision 02: while nuclear powers were bought, a spent one had an
	-- empty magazine and read `hidden`. Reading `charging:` IS the free-timer economy.
	local function expectCharging(player, who, key, why)
		local got = state(player, key)
		if got:sub(1, 9) ~= "charging:" then
			fault("%s's %s reads %q, expected a `charging:<ticks>` reading. %s", who, key, got, why)
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
			"ender-fire=%q | USA: 1kt=%s 20kt=%s 50kt=%s 100kt=%s END=%s | Russia: 1kt=%s 20kt=%s 100kt=%s END=%s | %s",
			enderFireResult,
			state(USA, USA_1KT), state(USA, USA_20KT), state(USA, USA_50KT),
			state(USA, USA_100KT), state(USA, USA_ENDER),
			state(Russia, RU_1KT), state(Russia, RU_20KT), state(Russia, RU_100KT), state(Russia, RU_ENDER),
			table.concat(notes, " | "))

		-- THE WHOLE BIN, BOTH SIDES, printed rather than asserted. An exact-set assertion would be
		-- brittle -- bands come and go on the side cooldown -- but a triager reading a failure wants
		-- to see WHICH cameos were drawn rather than infer it from nine per-power tokens.
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

		-- ---- PHASE A. RELEASE gave BOTH sides level 1 and NOTHING else. Release is parity and not
		-- a provocation: anything above 1 kt lit here would mean a side was escalated by nobody.
		if tick == RELEASE_CHECK_TICK then
			local ok = expect(USA, "USA", USA_1KT, "ready",
				"the release gate opened at tick 10 and must hand both sides a loaded 1 kt warhead"
				.. " -- with DefaultCash 0 nothing else can have loaded it")
			ok = expect(Russia, "Russia", RU_1KT, "ready",
				"release is simultaneous for both sides; only one of them got it."
				.. " CHECK debug.log FOR THE `NUCLEAR EXCHANGE sides:` LINE FIRST: a side missing"
				.. " from it was never registered, and nothing downstream of that can work") and ok
			ok = expect(USA, "USA", USA_20KT, "hidden",
				"release is LEVEL 1 only; every band above it must stay dark until somebody fires") and ok
			ok = expect(Russia, "Russia", RU_20KT, "hidden",
				"release opened a band above 1 kt for a side nobody has shot at") and ok
			ok = expect(USA, "USA", USA_ENDER, "hidden",
				"A GAME-ENDER IS READABLE AT RELEASE. Level 5 is reached only by being hit with a"
				.. " 100 kt; every `ready` later in this file is worthless if this one is not `hidden`") and ok

			note(ok, "release ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE B. USA fires the smallest warhead in the mod at empty ground.
		if tick == FIRE_1KT_TICK then
			if not launch(USA, "USA", USA_1KT,
				"This is rung 1 of 4 and release just handed it over.") then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE C. THE RATCHET EDGE AND THE SIDE-WIDE COOLDOWN, together.
		if tick == RATCHET_CHECK_TICK then
			local ok = expect(Russia, "Russia", RU_20KT, "ready",
				"being hit by 1 kt must raise Russia's LEVEL to 20 kt, ready to fire now -- Russia"
				.. " did not fire, so Russia is on no cooldown. `hidden` means the level rise, the"
				.. " condition or the loaded shot did not arrive")
			ok = expect(USA, "USA", USA_20KT, "hidden",
				"THE FIRER CLIMBED BY FIRING. That is decision 06's shared pressure ladder, where"
				.. " both sides always read the same rung and going first was free -- not the"
				.. " ratchet, where firing raises the OTHER side's level and never your own") and ok

			-- THE SIDE COOLDOWN, AND IT IS THE HALF v1 DID NOT HAVE. USA fired its 1 kt, so USA's
			-- whole arsenal is down -- not just the band it fired from.
			ok = expectCharging(USA, "USA", USA_1KT,
				"USA fired its 1 kt and the SIDE is now on cooldown. `hidden` means the power is"
				.. " still a BOUGHT power with an empty magazine and the Escalation bypass did not"
				.. " apply; `ready` means firing cost nothing at all") and ok

			ok = expect(Russia, "Russia", RU_1KT, "ready",
				"RUSSIA'S 1 KT WENT ON COOLDOWN WHEN USA FIRED. A cooldown belongs to the side that"
				.. " fired; `charging:` here means it is being applied to everybody") and ok

			note(ok, "ratchet ok at t%d (fired t%d)", tick, FIRE_1KT_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE C2. Russia answers at 20 kt. USA's level goes to 3 -- WHILE USA IS RELOADING.
		if tick == FIRE_20KT_TICK then
			if not launch(Russia, "Russia", RU_20KT,
				"Rung 2 of 4, fired from the level USA's shot just handed Russia.") then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE D. ESCALATED WHILE RELOADING. THE SHARPEST ASSERTION IN THE FILE.
		-- USA is at level 3 from t200 and its own cooldown from the 1 kt runs to t420. Both of its
		-- newly granted bands must be DRAWN (the level is real) and NOT FIREABLE (the cooldown is
		-- too). See the file header for why a build that gets this wrong is broken in most matches.
		if tick == LOCKED_CHECK_TICK then
			local ok = expectCharging(USA, "USA", USA_50KT,
				"USA'S BRAND-NEW 50 KT IS FIREABLE INSIDE ITS OWN COOLDOWN. Russia's 20 kt raised"
				.. " USA to level 3 at t" .. FIRE_20KT_TICK .. ", and USA has been reloading since"
				.. " t" .. FIRE_1KT_TICK .. ". `ready` here is NuclearExchange.MakeBandsReady zeroing"
				.. " the timer on a level rise instead of setting it to the side's REMAINING"
				.. " cooldown -- a free 50 kt shot that rule 2 exists to deny, in the most ordinary"
				.. " situation this model has. `hidden` is a different bug: the level rise did not"
				.. " reach the condition layer at all")
			ok = expectCharging(USA, "USA", USA_20KT,
				"same defect, one band down -- USA reached level 3, so 2 and 3 are both new") and ok

			-- AND THE FIRER OF THE 20 KT IS NOW DOWN TOO, at every band including the one it did not
			-- fire. Russia holds 1 kt and 20 kt at level 2; both must be silent.
			ok = expectCharging(Russia, "Russia", RU_20KT,
				"Russia fired its 20 kt and must be on its own cooldown") and ok
			ok = expectCharging(Russia, "Russia", RU_1KT,
				"RUSSIA'S 1 KT STAYED LOADED AFTER RUSSIA FIRED ITS 20 KT. This is v1's per-band"
				.. " regeneration: one shot muted one band and left the rest of the arsenal up,"
				.. " which is the 'too many nukes in flight' the 2026-09-15 ruling replaced") and ok

			note(ok, "locked-while-escalated ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE E. AND THE WHOLE ARSENAL COMES BACK AT ONCE. A cooldown that never expired
		-- would be a side permanently disarmed by its own first shot, which is a worse bug than the
		-- one phase D catches -- so the recovery is asserted, not assumed.
		if tick == RECOVER_CHECK_TICK then
			local ok = expect(USA, "USA", USA_1KT, "ready",
				string.format("USA's arsenal did not come back %d ticks after it fired, against a"
					.. " KilotonCooldownTicks of %d in rules.yaml. If this still reads `charging:`"
					.. " the applied cooldown is longer than the override -- check that the side"
					.. " takes the FIRED band's entry and not a larger one",
					tick - FIRE_1KT_TICK, CD_1KT))
			ok = expect(USA, "USA", USA_50KT, "ready",
				"the band USA gained WHILE reloading must come back with the rest of the arsenal,"
				.. " on the same clock. A band still charging after the firer recovered means the"
				.. " grant was set to a different value from the side's own cooldown") and ok

			-- RUSSIA IS STILL DOWN, and that is what makes the reading above a measurement rather
			-- than a restatement: Russia fired 80 ticks after USA did and its 20 kt cooldown is
			-- LONGER, so at t450 it must still be reloading.
			ok = expectCharging(Russia, "Russia", RU_20KT,
				string.format("Russia fired at t%d against a TwentyKilotonCooldownTicks of %d, so it"
					.. " must still be reloading at t%d. `ready` here means the 20 kt band took the"
					.. " 1 kt band's cooldown -- the table is being indexed by something other than"
					.. " the band that was fired", FIRE_20KT_TICK, CD_20KT, tick)) and ok

			note(ok, "recovery ok at t%d, %d ticks after USA fired", tick, tick - FIRE_1KT_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE F. USA climbs to 50 kt. Russia goes to level 4.
		if tick == FIRE_50KT_TICK then
			if not launch(USA, "USA", USA_50KT, "Rung 3 of 4.") then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		if tick == L4_CHECK_TICK then
			local ok = expect(Russia, "Russia", RU_100KT, "ready",
				"being hit by 50 kt must raise Russia to level 4 -- the band whose reply is the"
				.. " game-ender. Russia's own cooldown ended at t"
				.. tostring(FIRE_20KT_TICK + CD_20KT) .. ", so this must be `ready` and not `charging:`")
			note(ok, "level-4 ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE G. The 100 kt that takes USA to END.
		if tick == FIRE_100KT_TICK then
			if not launch(Russia, "Russia", RU_100KT,
				"Rung 4 of 4: the 100 kt shot whose reply is a game-ender.") then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE G2. USA IS AT END AND STILL RELOADING. Phase D's rule, at the top rung, where
		-- it runs through a DIFFERENT code path: ArmableAtTopRung overrides the `powers.event`
		-- prerequisite that no faction provides, and a fix that reached for MakeReady there without
		-- re-applying the cooldown would open the game-ender early. One tick of that is the match.
		if tick == END_LOCKED_TICK then
			local ok = expectCharging(USA, "USA", USA_ENDER,
				"USA'S GAME-ENDER IS FIREABLE INSIDE ITS OWN COOLDOWN. USA reached level 5 at t"
				.. FIRE_100KT_TICK .. " and has been reloading since t" .. FIRE_50KT_TICK .. "."
				.. " `ready` means the top-rung arming path (NuclearExchange.ArmableAtTopRung ->"
				.. " MakeReady) skipped the cooldown that every other band honours -- and at this"
				.. " rung that is one click from ending the match. `hidden` is the OTHER bug, the"
				.. " one reported from a real match: the END level granted a cameo nobody can see")

			-- NATIONAL ENDER ONLY -- USER RULING, 2026-09-14. Exactly one END cameo per side.
			-- SarmatStrike declares `powers.event, player.russia`; only the FIRST of those is ever
			-- overridable, and a blanket prerequisite bypass gives USA a Russian warhead.
			ok = expect(USA, "USA", RU_ENDER, "hidden",
				"USA WAS HANDED RUSSIA'S WARHEAD. SarmatStrike declares `powers.event,"
				.. " player.russia` (player.yaml:236); reaching level 5 may override the event tier"
				.. " and may NOT override the faction. This undoes c8cadc8a") and ok

			-- Russia holds no ender: it FIRED the 100 kt, and firing raises the other side.
			ok = expect(Russia, "Russia", RU_ENDER, "hidden",
				"Russia reached level 5 by firing. A side can only be escalated by being shot at;"
				.. " `ready` here is decision 06's shared ladder coming back at the top rung") and ok

			note(ok, "END-locked ok at t%d (100 kt fired t%d)", tick, FIRE_100KT_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE H. THE ENDER COMES UP WHEN THE COOLDOWN ENDS, and can be fired.
		if tick == END_READY_TICK then
			local ok = expect(USA, "USA", USA_ENDER, "ready",
				string.format("USA's game-ender did not come up %d ticks after its 50 kt shot,"
					.. " against a FiftyKilotonCooldownTicks of %d. If this still reads `charging:`"
					.. " the END band is on a longer clock than the rest of the side's arsenal --"
					.. " the cooldown is the SIDE's and every band shares it",
					tick - FIRE_50KT_TICK, CD_50KT))

			note(ok, "END-ready ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE I. A DRAWN CAMEO IS NOT A FIREABLE WEAPON. Two different claims, and the
		-- second is what opens the final exchange: MissileStrikePower.Activate is what calls
		-- NuclearExchange.ReportNuclearRelease, which is what calls DoomsdayStrike.BeginFinalExchange.
		if tick == FIRE_ENDER_TICK then
			enderFireResult = Test.ActivateSupportPower(USA, USA_ENDER, CPos.New(AIM_X, AIM_Y))
			if enderFireResult ~= "issued" then
				fault("USA could not FIRE %s at level 5 and off cooldown: %q. The cameo being drawn"
					.. " and the order being accepted are different claims --"
					.. " SupportPowerInstance.Ready is `Active && RemainingTicks == 0` and the bin"
					.. " filters on Disabled, so a power can draw and still refuse the order."
					.. " NOTHING BEGINS THE FINAL EXCHANGE unless this order is accepted",
					USA_ENDER, enderFireResult)
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
