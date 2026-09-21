-- ASSERTING AUTOTEST — reaching LEVEL 5 hands over a game-ender the player can actually pick up and
-- fire, and hands over only the one that player's faction owns.
--
-- RENAMED FROM test-nuclear-ender-window AT EXCHANGE v2 (2026-09-15). The bug it pins is unchanged
-- and so is every assertion about WHAT the top rung grants; what changed is how a side gets there.
-- v1: a retaliation window opened one band above whatever you were last hit with, so END was a
-- one-minute grant. v2: your LEVEL is raised permanently to min(b + 1, 5) by every enemy launch, so
-- END is reached by being hit with a 100 kt and is then yours for the rest of the match -- gated
-- only by the side cooldown, which this file waits out rather than racing.
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
-- UPDATED AGAIN FOR THE IMPACT-DEFERRED ESCALATION (2026-09-19). This file used to set
-- `EscalationDelayTicks: -1` in rules.yaml, the escape hatch that restores the pre-2026-09-16
-- timing where a victim's level rose on the tick the enemy CLICKED. It was set because the phase
-- schedule below was a column of constants that fired and then read a level 40 ticks later, and
-- the shipped rise lands a whole missile flight after the click. THE OVERRIDE IS GONE AND THE
-- CONSTANTS ARE GONE WITH IT: every phase from the first launch onward hangs off an OBSERVED
-- event, so this file has no opinion about how long a B61, an Iskander or a Kalibr takes to
-- arrive -- which is what let those constants be wrong in the first place.
--
-- THE TWO OBSERVATIONS, per launch:
--   the explosion   Test.GetImpactEffectCount, snapshotted on the order tick. It counts
--                   CreateEffectWarhead impacts that passed the validity gates, and every warhead
--                   in the arsenal carries exactly one such warhead (Warhead@Fireball). Nothing
--                   else on this map can detonate: no unit exists but two Supply Routes, nothing
--                   fires, and the aim point is empty ground.
--   the escalation  the victim's newly granted band leaving `hidden`, which is
--                   SupportPowerInstance.Disabled going false.
--
-- AND TWO ASSERTIONS COME OUT OF THE PAIR, ON EVERY ONE OF THE FIVE LAUNCHES:
--   (a) the band stays HIDDEN on every tick between the launch and the explosion. RED the instant
--       an escalation is applied at the click -- which is what the deleted override restored, and
--       what the whole 2026-09-16 ruling is about.
--   (b) the band is drawn within EscalationDelayTicks + a grant allowance of that explosion. RED
--       if the deferral never fires -- a dropped pending record, an impact never reported by
--       MissileStrikePower. (a) cannot see that: a level that never rises never rises early
--       either.
--
-- AND ONE READING IS NOT A BIN READ, DELIBERATELY. Phase G calls Test.ActivateSupportPower, which
-- goes through the real order path (SupportPowerManager.ResolveOrder). A drawn cameo and an
-- accepted order are different claims and the user made both: "I should be able to choose the game
-- ender AND place the targets".
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * THE SIDE COOLDOWN AS A GATE. test-nuclear-exchange's subject, and it asserts the top rung's
--     half of it directly (a game-ender DRAWN and NOT FIREABLE inside its side's cooldown). Here
--     the cooldowns are compressed to 60 ticks and every launch is now a whole missile flight
--     after the previous one by the SAME side, so nothing below is near an edge. Phase G2 is the
--     one exception and it is read off the LAUNCH, where a 60-tick cooldown is still running.
--     This file is about WHAT the top rung grants and to WHOM.
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
local USA_ENDER  = "TridentStrike"      -- 455000 t band 5  GameEnder, powers.event + player.america
local RU_20KT    = "RuIskanderStrike"   -- 10000 t   band 2  TwentyKiloton
local RU_100KT   = "RuKalibrStrike"     -- 100000 t  band 4  HundredKiloton
local RU_ENDER   = "SarmatStrike"       -- 750000 t  band 5  GameEnder, powers.event + player.russia

-- THE SECOND NEGATIVE CONTROL, and the one that pins the user's ruling of 2026-09-14: NATIONAL
-- ENDER ONLY, exactly one END cameo per side. This power is band 5 like the other two and declares
-- `powers.event` with NO `player.*` beside it (player.yaml:842), so it names no owner and neither
-- arming path may hand it over. Asserted `hidden` in phases A, F and H.
--
-- IT USED TO NEED A LOBBY CHECKBOX PINNED ON AND LOCKED IN rules.yaml TO MAKE PHASES F AND H
-- EVIDENCE: with `high-yield-nuke` off, this power's RequiresCondition was unsatisfied and `hidden`
-- was true for a reason that had nothing to do with attribution. THAT CHECKBOX WAS RETIRED ON
-- 2026-09-15 and the pin went with it, which STRENGTHENS those two rather than weakening them --
-- the gate it used to hold open is now permanently open, so at an OPEN END LEVEL this power's
-- RequiresCondition (`nuclear-release-gameender`, now its only one) is SATISFIED when the assertion
-- is read, and `hidden` can only be attribution.
--
-- PHASE A IS AND ALWAYS WAS THE WEAKER OF THE THREE, and the checkbox never changed that. It reads
-- at RELEASE, which is level 1 for everybody, so the game-ender band is not granted and `hidden` is
-- true there for the band alone. That is deliberate -- phase A is the BASELINE the other two are
-- measured against, not a second attribution reading.
local UNOWNED_ENDER = "HighYieldNukeStrike" -- 6000000 t band 5 GameEnder, powers.event only

-- NEGATIVE CONTROL, and the only band-2 power in the mod on the event tier (player.yaml:711). It
-- must stay dark at a level 2 that lights RuIskander beside it.
--
-- WHAT HOLDS IT DARK CHANGED ON 2026-09-15 AND THE CHANGE IS WORTH READING BEFORE TRUSTING THIS
-- LINE. It used to be the `tactical-nuke` host checkbox, OFF at shipped default and locked there by
-- rules.yaml, so the control caught "a fix that forced readiness without re-checking the power's
-- own RequiresCondition". That checkbox is retired; this power's RequiresCondition is now
-- `nuclear-release-20kt` alone and IS satisfied at level 2. What refuses it instead is the EVENT
-- TIER: NuclearExchange arms a band on SupportPowerInstance.Permitted, which ANDs prereqsAvailable,
-- and NuclearExchangeInfo.OverriddenPrerequisites is read ONLY on the GameEnder rung
-- (NuclearExchange.ArmableAtTopRung) -- this is band 2, so `powers.event` is never waived for it and
-- rules.yaml keeps sandbox off. THE CONTROL STILL BINDS, ON A DIFFERENT AND NARROWER CLAIM: it now
-- catches a fix that widened the prerequisite override below the top rung, and no longer catches one
-- that skipped the power's own condition. If you need the old claim back it needs a different power.
local TACNUKE    = "TacNukeStrike"       -- 20000 t  band 2  TwentyKiloton, powers.event only

-- Empty ground, far from both Supply Routes. See map.yaml.
local AIM_X, AIM_Y = 32, 8

-- ---- THE DEFERRAL'S OWN CONSTANTS, RESTATED FROM THE ENGINE -----------------------------------
-- NuclearExchangeInfo.EscalationDelayTicks, the SHIPPED value. Deliberately not overridden in
-- rules.yaml: a scenario that retuned it would stop measuring what a player sees.
local ESCALATION_DELAY_TICKS = 50
-- What the rise is allowed to take ON TOP of the delay. NuclearExchangeInfo.GrantRetryTicks is 30
-- -- the budget ReconcileGrants gives the band condition to cross from the World actor to the
-- Player actor -- plus ten ticks for the skew between the ESTIMATED impact tick MissileStrikePower
-- reports and the tick the warhead's CreateEffectWarhead actually runs on.
local ESCALATION_SLACK_TICKS = 40
-- WATCHDOG, NOT A MEASUREMENT. The slowest warhead this file fires is the Kalibr: MissileDelay 400
-- plus ceil((diagonal + 16c0) / Speed 450) ~= 605 ticks on a 66x34 map. 1200 is twice that. No
-- assertion reads it and widening it cannot turn a red run green.
local FLIGHT_BUDGET_TICKS = 1200

-- ==== EVERY CHECK IS 12 TICKS AFTER THE BAND APPEARS, AND THAT NUMBER IS AN ASSERTION ====
-- IT IS NOT THE OLD "rise + 40", and the difference is the anchor rather than the number. Until
-- 2026-09-19 the anchor was a CONSTANT chosen to sit 40 ticks past a rise this file predicted; it
-- is now the tick the band was SEEN to leave `hidden`. Both bounds move with it:
--     > 1    the band leaving `hidden` IS its condition arriving, so the only thing still owed is
--            one ServicePendingReady pass. The old lower bound of 30 (GrantRetryTicks) existed
--            because the old anchor was the rise, BEFORE the condition had crossed. 12 is a dozen
--            times the one tick that remains.
--     < 60   rules.yaml's compressed side cooldown, and the bound that keeps every `ready` below
--            NON-VACUOUS. SupportPowerInstance.Tick pins a DISABLED power's countdown back to full
--            on every tick (SupportPowerManager.cs:399-401), so a band whose grant reached the
--            CONDITION layer and never reached MakeBandsReady starts its own 60-tick interval
--            running at the rise -- and would read `charging:48` here. The 2026-09-15 review found
--            four phases of this file vacuous at rise+65 for exactly that reason; 12 restores the
--            margin with room to spare rather than merely squeaking under 60.
local RISE_SETTLE_TICKS = 12
--
-- AND EVERY RECIPIENT IS OFF COOLDOWN WHEN IT IS ESCALATED, so every check below still reads the
-- cooldown-ZERO case: granted means READY, full stop. That is now true by a much wider margin than
-- it was -- each launch is a full missile flight (257..605 ticks) after the previous one, against
-- a 60-tick cooldown -- rather than by the 140-tick spacing the old constants arranged by hand.
local RELEASE_CHECK_TICK = 60
local L1_TICK            = 80    -- USA band 1 -> RU level 2. The one tick this file still chooses.

-- Every boundary below is SET FROM AN OBSERVATION, never written down; -1 is "not scheduled yet",
-- which no tick can equal. The ladder they drive is unchanged:
--     L2  RU  band 2 -> USA level 3        L3  USA band 3 -> RU  level 4
--     L4  RU  band 4 -> USA level 5 = END  L5  USA band 4 -> RU  level 5
local L2_CHECK_TICK      = -1    -- rise 1 + 12
local L2_TICK            = -1
local L3_CHECK_TICK      = -1    -- rise 2 + 12
local L3_TICK            = -1
local L4_CHECK_TICK      = -1    -- rise 3 + 12
local L4_TICK            = -1
local END_CHECK_TICK     = -1    -- rise 4 + 12  <<< THE ASSERTION THE USER'S BUG IS
local L5_TICK            = -1
-- THE FIRER'S OWN COOLDOWN IS READ OFF THE LAUNCH, NOT OFF A RISE, and that is the honest anchor
-- for it: "firing costs the firer its whole arsenal" is settled the instant the button is pressed
-- and has nothing to do with when the warhead lands. 40 is inside the 60-tick cooldown (so the
-- reading is `charging:20`) and past GrantRetryTicks. It used to be folded into phase H, which
-- read at rise+40 back when a rise was 40 ticks after the launch; with the rise now a flight away
-- that phase would have found the cooldown long expired and asserted nothing.
local FIRER_COOLDOWN_TICK = -1   -- L5 + 40
-- AND THE RATCHET IS READ ONCE THAT COOLDOWN HAS RUN OUT: 80 > 60, so `ready` here says two things
-- at once -- USA did not lose its END level by firing, AND its ender came back on the SIDE
-- cooldown like every other band rather than on one of its own.
local RATCHET_CHECK_TICK = -1    -- L5 + 80
local END2_CHECK_TICK    = -1    -- rise 5 + 12: RUSSIA's ender, on a side that owes nothing
local FIRE_ENDER_TICK    = -1
-- HARD BACKSTOP in absolute ticks, so a run whose watches all somehow resolve but whose phases do
-- not can still reach a verdict. The five flights total about 2300 ticks; this is well past that.
local BUDGET_TICK        = 4000

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

	-- `charging:<n>` is the one token in the vocabulary that carries a value, so it is matched by
	-- PREFIX where every other reading is compared exactly.
	local function expectCharging(player, who, key, why)
		local got = state(player, key)
		if got:sub(1, 9) ~= "charging:" then
			fault("%s's %s reads %q, expected a `charging:<ticks>` reading. %s", who, key, got, why)
			return false
		end

		return true
	end

	-- ==== THE ESCALATION WATCH ====================================================================
	-- One record per release order, from the click to the victim's level rise. `sentinel` is the
	-- band the rise must GRANT -- chosen per launch so that a failure names the rung rather than
	-- timing out anonymously; see each launch site.
	local watch = nil
	local risesSeen = 0

	-- Fire, and treat anything but "issued" as fatal to everything downstream. A climb that stalls
	-- at rung N makes every later reading meaningless rather than merely wrong, so say so.
	--
	-- ARMS THE WATCH ON THE SAME TICK, and the order matters: the impact-effect baseline is taken
	-- BEFORE the order is issued, which is what makes "the count moved" mean "THIS warhead
	-- detonated" and not "some warhead has detonated at some point this run".
	local function launch(player, who, key, why, victim, victimWho, sentinel)
		local effects0 = Test.GetImpactEffectCount()
		local result = Test.ActivateSupportPower(player, key, CPos.New(AIM_X, AIM_Y))
		if result ~= "issued" then
			fault("%s could not fire %s: %q. %s Nothing after this point is evidence either way",
				who, key, result, why)
			return false
		end

		if state(victim, sentinel) ~= "hidden" then
			fault("%s's %s was already drawn at t%d, BEFORE %s fired. The no-early-escalation"
				.. " reading for this launch can only mean something if the band starts dark",
				victimWho, sentinel, tick, who)
			return false
		end

		watch = {
			orderTick = tick, effects0 = effects0, impactTick = nil, riseTick = nil,
			firer = who, firedKey = key,
			victim = victim, victimWho = victimWho, sentinel = sentinel,
		}

		return true
	end

	-- Drive the watch one tick. nil while the warhead is still on its way, the TICK the sentinel
	-- band appeared on once the escalation has landed, or -1 on a fault.
	local function pollWatch()
		local w = watch
		local drawn = state(w.victim, w.sentinel) ~= "hidden"

		if w.impactTick == nil then
			-- ---- (a) THE LEVEL MUST NOT RISE BEFORE THE EXPLOSION. -------------------------------
			if drawn then
				fault("%s WAS ESCALATED AT THE LAUNCH, NOT AT THE DETONATION. Its %s left `hidden`"
					.. " at t%d, %d ticks after %s fired %s at t%d and BEFORE any warhead impact"
					.. " had been counted (Test.GetImpactEffectCount is still %d). The 2026-09-16"
					.. " user ruling is that the level-up lands with the explosion, so"
					.. " NuclearExchange.ReportNuclearRelease must RECORD the escalation and"
					.. " ServicePendingEscalations must apply it at the first impact plus"
					.. " EscalationDelayTicks. This is what a negative EscalationDelayTicks, or a"
					.. " deferral that was never wired, looks like",
					w.victimWho, w.sentinel, tick, tick - w.orderTick, w.firer, w.firedKey,
					w.orderTick, w.effects0)
				return -1
			end

			if Test.GetImpactEffectCount() > w.effects0 then
				w.impactTick = tick
				return nil
			end

			if tick - w.orderTick > FLIGHT_BUDGET_TICKS then
				fault("NO WARHEAD EVER DETONATED. %s's %s was issued at t%d and"
					.. " Test.GetImpactEffectCount has not moved off %d in %d ticks. The warhead"
					.. " never left (MissileStrikePower.Activate bailed), never arrived, or its"
					.. " CreateEffectWarhead impact was discarded at the validity gates",
					w.firer, w.firedKey, w.orderTick, w.effects0, tick - w.orderTick)
				return -1
			end

			return nil
		end

		-- ---- (b) AND IT MUST RISE SHORTLY AFTER IT. ---------------------------------------------
		if drawn then
			w.riseTick = tick
			return tick
		end

		if tick > w.impactTick + ESCALATION_DELAY_TICKS + ESCALATION_SLACK_TICKS then
			fault("THE DEFERRED ESCALATION NEVER FIRED. %s's warhead detonated at t%d and %s's %s"
				.. " is STILL %q at t%d, %d ticks later, against an EscalationDelayTicks of %d plus"
				.. " a %d-tick grant allowance. The pending record was dropped, no impact was"
				.. " reported for it (MissileStrikePower -> NuclearExchange.NotifyNuclearImpact),"
				.. " or the rise reached the state and not the condition layer. CHECK debug.log FOR"
				.. " `NUCLEAR ESCALATION`: a launch line saying \"escalation deferred to impact +\""
				.. " with no matching landing line is the pending record going missing",
				w.firer, w.impactTick, w.victimWho, w.sentinel, state(w.victim, w.sentinel), tick,
				tick - w.impactTick, ESCALATION_DELAY_TICKS, ESCALATION_SLACK_TICKS)
			return -1
		end

		return nil
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

		-- ---- THE WATCH RUNS FIRST, EVERY TICK, from each launch until the level it caused rises.
		-- It is the only thing in this file that reads a tick it did not choose, and it is what
		-- schedules the check phase that follows each launch. See the file header for (a) and (b).
		if watch ~= nil and watch.riseTick == nil then
			local risen = pollWatch()
			if risen == -1 then
				verdict()
				return
			end

			if risen ~= nil then
				risesSeen = risesSeen + 1
				note(true, "L%d landed: fired t%d, detonated t%d (+%d), %s's %s drawn t%d (+%d)",
					risesSeen, watch.orderTick, watch.impactTick,
					watch.impactTick - watch.orderTick, watch.victimWho, watch.sentinel,
					risen, risen - watch.impactTick)

				if risesSeen == 1 then
					L2_CHECK_TICK = risen + RISE_SETTLE_TICKS
				elseif risesSeen == 2 then
					L3_CHECK_TICK = risen + RISE_SETTLE_TICKS
				elseif risesSeen == 3 then
					L4_CHECK_TICK = risen + RISE_SETTLE_TICKS
				elseif risesSeen == 4 then
					END_CHECK_TICK = risen + RISE_SETTLE_TICKS
				else
					END2_CHECK_TICK = risen + RISE_SETTLE_TICKS
				end
			end
		end

		-- ---- PHASE A. THE BASELINE, and it is what makes every later `ready` mean something.
		-- Release hands both sides the 1 kt band and nothing else. If a game-ender were already
		-- readable here, the whole climb below would be measuring nothing.
		if tick == RELEASE_CHECK_TICK then
			local ok = expect(USA, "USA", USA_1KT, "ready",
				"the release gate opened at tick 10 and must hand USA a loaded 1 kt warhead."
				.. " CHECK debug.log FOR THE `NUCLEAR EXCHANGE sides:` LINE FIRST: a side missing"
				.. " from it was never registered and nothing downstream of that can work")
			ok = expect(USA, "USA", USA_ENDER, "hidden",
				"A GAME-ENDER IS READABLE AT RELEASE. Release is level 1 for everybody; level 5 is"
				.. " reached only by being hit with a 100 kt. Every `ready` later in this file is"
				.. " worthless if this one is not `hidden`") and ok
			ok = expect(Russia, "Russia", RU_ENDER, "hidden",
				"same rule, Russia's side of it") and ok
			ok = expect(USA, "USA", UNOWNED_ENDER, "hidden",
				"the 6 Mt strategic strike is band 5 like the other two and must be dark at release."
				.. " This reading is the BAND alone -- its `nuclear-release-gameender` condition is"
				.. " unsatisfied at level 1, and since the `high-yield-nuke` checkbox was retired"
				.. " (2026-09-15) that condition is the only gate it has besides attribution."
				.. " Phases F and H assert it is STILL dark at an OPEN END level, where the"
				.. " condition IS satisfied and only attribution can explain it; this is the"
				.. " baseline those two are measured against") and ok

			note(ok, "baseline ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE B. L1: USA fires 1 kt. Raises Russia to level 2 WHEN IT LANDS.
		-- SENTINEL RU_20KT: the one band this rise grants, so a watch timeout names the rung.
		if tick == L1_TICK then
			if not launch(USA, "USA", USA_1KT, "This is rung 1 of 4 and the shot release just handed it.",
				Russia, "Russia", RU_20KT) then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE C. Russia is at level 2, plus the SCOPE control.
		if tick == L2_CHECK_TICK then
			local ok = expect(Russia, "Russia", RU_20KT, "ready",
				"being hit by 1 kt must raise Russia one band up, ready immediately -- Russia did"
				.. " not fire, so Russia is on no cooldown")
			-- THE CONTROL. TacNuke is also band 2 and also `powers.event`. Its RequiresCondition IS
			-- satisfied here -- see the long note at TACNUKE above, which this used to read the
			-- other way round -- so what must refuse it is the EVENT TIER, and that is exactly the
			-- reason the prerequisite override is scoped to the top rung rather than applied to
			-- every band.
			ok = expect(Russia, "Russia", TACNUKE, "hidden",
				"THE 20 KT LEVEL LEAKED INTO AN EVENT-TIER POWER NOBODY MAY BUY OR BE HANDED."
				.. " TacNukeStrike is gated on `nuclear-release-20kt` (satisfied here) AND on"
				.. " `Prerequisites: powers.event`, which no faction provides and which sandbox is"
				.. " locked OFF for in rules.yaml. NuclearExchangeInfo.OverriddenPrerequisites"
				.. " waives that tier on the GameEnder rung ONLY (ArmableAtTopRung); if this went"
				.. " ready, the override reached a band below the top") and ok

			note(ok, "level-2 ok at t%d", tick)
			L2_TICK = tick + 10
			Trigger.AfterDelay(1, step)
			return
		end

		-- SENTINEL USA_50KT: USA goes from level 1 to level 3, so bands 2 and 3 are both new and
		-- either would do; the 50 kt is the one phase D reads.
		if tick == L2_TICK then
			if not launch(Russia, "Russia", RU_20KT, "Rung 2 of 4, fired from the level L1 opened.",
				USA, "USA", USA_50KT) then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE D. USA is at level 3.
		if tick == L3_CHECK_TICK then
			local ok = expect(USA, "USA", USA_50KT, "ready",
				"being hit by 20 kt must raise USA to 50 kt. USA's own cooldown from L1 (60 ticks"
				.. " from t" .. L1_TICK .. ") is long over -- a whole Iskander flight has passed"
				.. " since -- so this must be `ready` and not `charging:`")
			note(ok, "level-3 ok at t%d", tick)
			L3_TICK = tick + 10
			Trigger.AfterDelay(1, step)
			return
		end

		-- SENTINEL RU_100KT: Russia goes from level 2 to level 4, and the 100 kt is the top of the
		-- newly granted range and the band phase E reads.
		if tick == L3_TICK then
			if not launch(USA, "USA", USA_50KT, "Rung 3 of 4.", Russia, "Russia", RU_100KT) then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE E. Russia is at level 4. The last rung below the top.
		if tick == L4_CHECK_TICK then
			local ok = expect(Russia, "Russia", RU_100KT, "ready",
				"being hit by 50 kt must raise Russia to 100 kt -- the band whose reply is the ender")
			note(ok, "level-4 ok at t%d", tick)
			L4_TICK = tick + 10
			Trigger.AfterDelay(1, step)
			return
		end

		-- SENTINEL USA_100KT, DELIBERATELY NOT USA_ENDER. This rise takes USA from level 3 to level
		-- 5 and grants bands 4 AND 5. Watching the ENDER would turn the user's reported bug -- the
		-- END level granting no cameo -- into an anonymous watch timeout; watching the 100 kt lets
		-- the rise be DETECTED and then lets phase F fail on the ender with the message that names
		-- the defect. The 100 kt is also the band phase G fires.
		if tick == L4_TICK then
			if not launch(Russia, "Russia", RU_100KT,
				"Rung 4 of 4: the 100 kt shot whose reply is a game-ender.",
				USA, "USA", USA_100KT) then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE F. THE USER'S BUG, AND THE WHOLE POINT OF THE FILE.
		-- USA has been hit by 100 kt. Its LEVEL is NuclearRung.GameEnder, the ledger's END box is
		-- lit, and the question is whether a cameo appeared.
		if tick == END_CHECK_TICK then
			local ok = expect(USA, "USA", USA_ENDER, "ready",
				"THE END LEVEL GRANTED NOTHING THE PLAYER CAN SEE -- this is the reported bug."
				.. " USA's level is NuclearRung.GameEnder (the ledger's END box is lit) and"
				.. " TridentStrike must be a cameo USA can click."
				.. " `hidden` means SupportPowerInstance.Disabled is true, which folds in Permitted,"
				.. " which folds in prereqsAvailable -- and the Trident declares `powers.event`, a"
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
				.. " player.russia` (player.yaml:236); reaching END may override the event tier"
				.. " and may NOT override the faction. A blanket prerequisite bypass gives exactly"
				.. " this reading and undoes c8cadc8a") and ok

			-- NATIONAL ENDER ONLY -- USER RULING, 2026-09-14. Exactly one END cameo per side. The
			-- 6 Mt strategic strike is band 5 and inside the yield ceiling, and its RequiresCondition
			-- IS satisfied here -- every gate but one is open, and since `high-yield-nuke` was
			-- retired (2026-09-15) that is now true unconditionally rather than because rules.yaml
			-- pinned a checkbox ON. The one gate that is shut is attribution: `powers.event` and no
			-- `player.*` beside it
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
				.. " national ender per side -- Trident for USA, Sarmat for Russia. `ready` here means"
				.. " NuclearGameEnders.ArmableBy treated an empty owner list as 'everybody owns it'"
				.. " rather than 'nobody does'. Its RequiresCondition is satisfied at this level and"
				.. " it has no lobby gate left (`high-yield-nuke` retired 2026-09-15), so this"
				.. " reading is about attribution and not about the condition") and ok

			-- Needed by L5 below, and worth its own message so a failure names the rung.
			ok = expect(USA, "USA", USA_100KT, "ready",
				"a level is CUMULATIVE: at level 5 USA holds every band at or below it, and L5"
				.. " fires the 100 kt. `hidden` means the condition layer granted only the top"
				.. " band rather than every band up to it") and ok

			-- Russia holds no ender yet: its band-4 window was SPENT by L4 and its permanent level
			-- is 3. This is what makes phase H a measurement rather than a restatement.
			ok = expect(Russia, "Russia", RU_ENDER, "hidden",
				"Russia has not been hit by 100 kt yet -- it FIRED the 100 kt shot, and firing"
				.. " raises the other side's level rather than your own. A `ready` here is decision"
				.. " 06's shared ladder coming back") and ok

			note(ok, "END level ok at t%d (100 kt fired t%d)", tick, L4_TICK)
			L5_TICK = tick + 10
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE G. L5: USA fires the 100 kt its own level 5 allows, which takes Russia to END.
		-- Firing costs USA a cooldown and NOT its level: levels never fall, so USA keeps its own
		-- game-ender through this and phase H asserts exactly that.
		-- SENTINEL RU_ENDER, and here there is no alternative: level 4 to level 5 grants exactly
		-- one band and it IS the ender. A watch timeout at this stage therefore means the same
		-- thing phase F's fault text spells out, on Russia's side of it.
		if tick == L5_TICK then
			if not launch(USA, "USA", USA_100KT,
				"USA's band 4, fired to take Russia to END.", Russia, "Russia", RU_ENDER) then
				verdict()
				return
			end

			FIRER_COOLDOWN_TICK = tick + 40
			RATCHET_CHECK_TICK = tick + 80

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE G2. THE FIRER PAYS, AT THE TOP RUNG, AND IT PAYS AT THE CLICK.
		-- USA fired 40 ticks ago against a 60-tick cooldown, so the honest reading is `charging:`.
		-- READ OFF THE LAUNCH AND NOT OFF A RISE, which is what this assertion always wanted and
		-- could not have while it lived inside phase H: availability is settled the instant the
		-- button is pressed (NuclearExchange.ReportNuclearRelease sets the side cooldown on the
		-- COUNTED edge), and since the 2026-09-16 ruling the victim's rise is a whole missile
		-- flight away -- by which time a 60-tick cooldown has long expired and this would assert
		-- nothing at all.
		if tick == FIRER_COOLDOWN_TICK then
			local ok = expectCharging(USA, "USA", USA_ENDER,
				"USA FIRED AT LEVEL 5 AND ITS OWN ENDER IS NOT ON THE SIDE COOLDOWN. `ready` means"
				.. " the firer escaped the lockout it just paid for; `hidden` means it lost the"
				.. " LEVEL by firing, which is v1's one-shot window coming back under a new name."
				.. " The ratchet half is asserted at t" .. RATCHET_CHECK_TICK)

			note(ok, "firer-paid ok at t%d (fired t%d)", tick, L5_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE H. THE RUSSIAN HALF, and the mirror of every faction assertion in phase F.
		-- IT NOW LANDS AFTER PHASE H2 RATHER THAN BEFORE IT, because Russia's END level arrives
		-- with USA's warhead rather than with USA's click. Neither claim depends on the other.
		if tick == END2_CHECK_TICK then
			local ok = expect(Russia, "Russia", RU_ENDER, "ready",
				"Russia's END level granted nothing. Same defect as phase F, Russia's side of it:"
				.. " SarmatStrike declares `powers.event, player.russia` and Russia holds the"
				.. " faction half")
			ok = expect(Russia, "Russia", USA_ENDER, "hidden",
				"RUSSIA WAS HANDED AMERICA'S WARHEAD. TridentStrike declares `powers.event,"
				.. " player.america` (player.yaml:238)") and ok
			ok = expect(Russia, "Russia", UNOWNED_ENDER, "hidden",
				"A SECOND END CAMEO APPEARED IN RUSSIA'S COLUMN. Russia's half of the 2026-09-14"
				.. " ruling: the unowned 6 Mt strike is withheld from both sides, not from one") and ok

			-- AND USA STILL HOLDS ITS OWN ENDER. The `charging:` half of this reading moved to
			-- phase G2, which reads it off the launch where it belongs; what is left here is the
			-- claim that survives a whole missile flight -- USA did not lose the LEVEL by firing,
			-- which is v1's one-shot window coming back under a new name.
			ok = expect(USA, "USA", USA_ENDER, "ready",
				"USA LOST ITS END LEVEL BY FIRING. Levels never fall under v2; its cooldown from"
				.. " t" .. L5_TICK .. " expired 60 ticks later and phase H2 already read it back"
				.. " at t" .. RATCHET_CHECK_TICK) and ok

			note(ok, "both enders ok at t%d", tick)
			FIRE_ENDER_TICK = tick + 10
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE H2. THE RATCHET, READ ONCE THE FIRER'S COOLDOWN HAS RUN OUT.
		-- Two claims in one reading: USA still HOLDS level 5 after firing (levels never fall), and
		-- its ender came back on the SIDE cooldown like every other band rather than on one of its
		-- own. Under v1 the first claim needed an argument about the window SPEND rule; under v2
		-- there is nothing to spend, which is itself the thing worth pinning.
		if tick == RATCHET_CHECK_TICK then
			local ok = expect(USA, "USA", USA_ENDER, "ready",
				string.format("USA's ender did not come back %d ticks after it fired, against the"
					.. " 60-tick cooldown in rules.yaml. `charging:` means the top rung is on a"
					.. " longer clock than the rest of the side's arsenal -- the cooldown is the"
					.. " SIDE's and every band shares it. `hidden` means USA LOST ITS END LEVEL BY"
					.. " FIRING, and levels never fall", tick - L5_TICK))

			ok = expect(USA, "USA", RU_ENDER, "hidden",
				"the faction lock must survive a cooldown cycle as well as a grant") and ok

			note(ok, "ratchet ok at t%d (fired t%d)", tick, L5_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE I. A DRAWN CAMEO IS NOT A FIREABLE WEAPON. The user's sentence has two halves
		-- and this is the second: the order must be ACCEPTED by the real path, not merely drawn.
		if tick == FIRE_ENDER_TICK then
			enderFireResult = Test.ActivateSupportPower(USA, USA_ENDER, CPos.New(AIM_X, AIM_Y))
			if enderFireResult ~= "issued" then
				fault("USA could not FIRE %s at level 5: %q. The cameo being drawn and"
					.. " the order being accepted are different claims -- SupportPowerInstance.Ready"
					.. " is `Active && RemainingTicks == 0` and the bin filters on Disabled, so a"
					.. " power can draw and still refuse the order", USA_ENDER, enderFireResult)
			end

			note(enderFireResult == "issued", "ender fired at t%d", tick)
			verdict()
			return
		end

		if tick >= BUDGET_TICK then
			fault("ran out of budget at tick %d without reaching the ender launch (scheduled for"
				.. " t%d; -1 means it was never scheduled). %d of the 5 escalations had landed",
				tick, FIRE_ENDER_TICK, risesSeen)
			verdict()
			return
		end

		Trigger.AfterDelay(1, step)
	end

	Trigger.AfterDelay(1, step)
end
