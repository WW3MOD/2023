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
-- ==== RETIMED FOR THE IMPACT-DEFERRED ESCALATION (2026-09-19) ====
-- This file used to set `EscalationDelayTicks: -1` in rules.yaml — the escape hatch that restores
-- the pre-2026-09-16 timing, where a victim's level rose on the tick the enemy CLICKED. It was set
-- because the phase schedule was a column of constants that fired and then read a level 65 ticks
-- later, and the shipped rise lands a whole missile flight after the click: MissileDelay 200 on
-- the B61 before its arc is even counted, and the arc is the map diagonal plus ApproachMargin over
-- the missile's Speed. THE OVERRIDE IS GONE AND THE CONSTANTS WENT WITH IT. Every phase from the
-- first launch onward hangs off an event this file OBSERVES.
--
--   the explosion   Test.GetImpactEffectCount, snapshotted on the order tick. It counts
--                   CreateEffectWarhead impacts that passed the validity gates, and every warhead
--                   in the arsenal carries exactly one (Warhead@Fireball). Nothing else on this
--                   map can detonate: the only actors are two Supply Routes, ^Combatant is
--                   HoldFire, and the aim point is empty ground.
--   the escalation  the victim's newly granted band leaving `hidden`.
--
-- TWO NEW ASSERTIONS PER LAUNCH, and they are the coverage that was missing from the whole tree:
--   (a) the band stays HIDDEN on every tick between the launch and the explosion. RED the instant
--       an escalation is applied at the click — which is precisely what the deleted override did.
--   (b) the band is drawn within EscalationDelayTicks (50) plus a 40-tick grant allowance of the
--       explosion. RED if the deferral never fires at all — a dropped pending record, an impact
--       MissileStrikePower never reported. (a) cannot see that: a level that never rises also
--       never rises early.
--
-- AND TWO OF THE CHOREOGRAPHY'S MOVES CHANGED, because two phases need a victim who is RELOADING
-- at the moment it is escalated, and a flight is longer than a compressed cooldown. Both are built
-- the same way and neither needs a flight time: the victim fires its own warhead ON THE TICK THE
-- INCOMING EXPLOSION IS SEEN, so the escalation lands ~50 ticks into a 300- or 340-tick lockout.
-- See phases D and G2. The claim each makes is word-for-word what it made before.
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
--   * THE FLIGHT TIME ITSELF. The watch waits for the explosion; it never asserts WHEN it arrives,
--     only that the escalation is on the far side of it. MissileStrikeApproachTest pins the
--     geometry the flight is derived from, without a world.
--   * The warhead. Nothing is aimed at anything — the exchange escalates the other side "wherever
--     it lands", which is the rule decision 01 chose over decision 14's damage attribution and
--     which v2 keeps, so a scenario that shot something would assert a rule this model does not have.
--   * THE ENDING ITSELF. Phase I fires a game-ender and asserts the ORDER IS ACCEPTED, not that the
--     world is annihilated: DoomsdayStrike.BeginFinalExchange returns immediately in a TestMode
--     session unless RunInTestMode is set (DoomsdayStrike.cs:458-459) and this scenario leaves it
--     false, because with the salvo live it freezes statistics, reveals the map and resolves the
--     match while this poller is still writing. demo-doomsday-deadhand is where the ending is the
--     subject. A drawn cameo and an accepted order are still two different claims and both are made.
--   * THE GAME-ENDER'S OWN ESCALATION TIMING. A game-ender escalates IMMEDIATELY and that is not
--     configurable (NuclearExchange.ReportNuclearRelease): the match is ending and there is no
--     correlation left to see. Phase I fires one and stops, so nothing here reads it.
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
-- to these, and the recovery phases below are arithmetic on them — so a change to one without a
-- change here turns a real failure into a timing artefact.
--
-- THE FOUR ARE KEPT DISTINCT, AND THAT IS THE POINT OF NOT USING ONE NUMBER: a build that applied
-- the FIRED band's cooldown correctly and a build that applied the firer's own band, or the first
-- entry in the table, are the same run at 300/300/300/300 and different runs at these. Since
-- 2026-09-19 that claim is made DIRECTLY rather than inferred from which side recovered first: a
-- `charging:<n>` reading is taken 40 ticks after each of the four launches and compared against
-- that band's constant. See expectCooldownAbout, and the note on phase E.
local CD_1KT   = 300
local CD_20KT  = 320
local CD_50KT  = 340
local CD_100KT = 360

-- ---- THE DEFERRAL'S OWN CONSTANTS, RESTATED FROM THE ENGINE ------------------------------------
-- NuclearExchangeInfo.EscalationDelayTicks, the SHIPPED value. Deliberately not overridden in
-- rules.yaml: a scenario that retuned it would stop measuring what a player sees.
local ESCALATION_DELAY_TICKS = 50
-- What the rise is allowed to take ON TOP of the delay. NuclearExchangeInfo.GrantRetryTicks is 30
-- — the budget ReconcileGrants gives the band condition to cross from the World actor to the
-- Player actor — plus ten ticks for the skew between the ESTIMATED impact tick MissileStrikePower
-- reports and the tick the warhead's CreateEffectWarhead actually runs on.
local ESCALATION_SLACK_TICKS = 40
-- WATCHDOG, NOT A MEASUREMENT. The slowest warhead this file fires is the Kalibr: MissileDelay 400
-- plus ceil((diagonal + 16c0) / Speed 450) ≈ 605 ticks on a 66x34 map. 1200 is twice that. No
-- assertion reads it and widening it cannot turn a red run green.
local FLIGHT_BUDGET_TICKS = 1200
-- Ticks between a victim's band being DRAWN and the reading that says what its timer holds.
-- BOUNDED AT BOTH ENDS:
--     > 1    the band leaving `hidden` IS its condition arriving, so all that is still owed is one
--            ServicePendingReady pass. (The old lower bound of GrantRetryTicks existed because the
--            old anchor was the rise itself, BEFORE the condition had crossed.)
--     < 300  the shortest compressed cooldown, which is what keeps phase D non-vacuous in the
--            other direction: SupportPowerInstance.Tick pins a DISABLED power's countdown back to
--            full every tick (SupportPowerManager.cs:399-401), so a band whose grant never armed
--            it starts its OWN interval running at the rise.
local RISE_SETTLE_TICKS = 12
-- Ticks between a LAUNCH and the reading that checks what the firer was charged for it. Inside
-- every compressed cooldown (the shortest is 300) and past GrantRetryTicks.
local LAUNCH_COOLDOWN_GAP = 40
-- Allowance on a `charging:<n>` reading compared against a constant. A grant can land a tick or
-- two after the launch that caused it; 6 is several times that and still an order of magnitude
-- below the 20-tick gap between the four cooldown constants, which is what has to be resolvable
-- for the reading to identify WHICH entry was applied.
local COOLDOWN_TOLERANCE = 6

-- ---- Phase boundaries ---------------------------------------------------------------------------
-- Only the first two are chosen. Everything after them is set from an OBSERVATION; -1 means "not
-- scheduled yet", which no tick can equal.
local RELEASE_CHECK_TICK = 90
local FIRE_1KT_TICK      = 120                        -- USA b1 -> USA cooldown; RU level 2 at impact
local USA_CD1_CHECK_TICK = FIRE_1KT_TICK + LAUNCH_COOLDOWN_GAP
local RATCHET_CHECK_TICK = -1                         -- rise 1 + 12
local FIRE_20KT_TICK     = -1
local RU_CD20_CHECK_TICK = -1                         -- fire + 40
local USA_REFIRE_TICK    = -1                         -- set to the tick the 20 kt DETONATES
local LOCKED_CHECK_TICK  = -1                         -- rise 2 + 12  <<< escalated WHILE RELOADING
local RECOVER_CHECK_TICK = -1                         -- refire + CD_1KT + 40
local FIRE_50KT_TICK     = -1
local USA_CD50_CHECK_TICK = -1                        -- fire + 40
local L4_CHECK_TICK      = -1                         -- rise 3 + 12
local FIRE_100KT_TICK    = -1
local RU_CD100_CHECK_TICK = -1                        -- fire + 40
local USA_REFIRE2_TICK   = -1                         -- set to the tick the 100 kt DETONATES
local END_LOCKED_TICK    = -1                         -- rise 4 + 12  <<< at END, still reloading
local END_READY_TICK     = -1                         -- refire2 + CD_50KT + 40
local FIRE_ENDER_TICK    = -1
-- HARD BACKSTOP in absolute ticks. The four flights total about 1500 ticks and the recoveries
-- another 700; this is well past the whole run.
local BUDGET_TICK        = 4000

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	local tick = 0
	local faults = {}
	local notes = {}
	local enderFireResult = "not-attempted"

	-- One record per release order, from the click to the victim's level rise. See pollWatch.
	local watch = nil
	local risesSeen = 0

	-- ==== A WARHEAD IN THE AIR THAT NOBODY IS WATCHING, AND WHY IT HAS TO BE TRACKED ====
	-- The watch decides "this warhead detonated" from a DELTA on Test.GetImpactEffectCount, which
	-- cannot say WHICH warhead moved it. The two reloading-victim shots (phases D and G2) are fired
	-- for their cooldown alone and escalate nobody -- but they still detonate, and if one of them
	-- were still in the air when the NEXT watch took its baseline, that watch would read the wrong
	-- explosion and (b) would be measured against a tick fifty ticks too early. So an unwatched
	-- shot is recorded here and the next launch waits for the sky to clear. It always has cleared
	-- on the shipped constants -- there is a comfortable margin either way -- and this exists so
	-- that margin is not load-bearing.
	local skyPending = nil

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

	-- The ticks left on a `charging:<n>` reading, or nil for any other token.
	local function chargingTicks(player, key)
		local got = state(player, key)
		if got:sub(1, 9) ~= "charging:" then
			return nil
		end

		return tonumber(got:sub(10))
	end

	-- ==== WHICH ENTRY OF THE COOLDOWN TABLE WAS APPLIED, READ AS A NUMBER ====
	-- ADDED 2026-09-19, AND IT REPLACES AN INFERENCE WITH A MEASUREMENT. This file used to pin
	-- "the table is indexed by the band that was FIRED" the long way round: USA fired a 1 kt,
	-- Russia fired a 20 kt eighty ticks later, and a single reading at a tick where the 300 had
	-- expired and the 320 had not separated the two constants. That construction needed both
	-- launches inside one cooldown of each other, which a deferred escalation makes impossible --
	-- the second launch now waits a whole missile flight for the level that permits it.
	--
	-- SO THE CLAIM IS MADE DIRECTLY INSTEAD, once per launch: forty ticks after the shot, the
	-- firer's band must be carrying `<that band's constant> - 40`. A build that applied the firer's
	-- OWN band, the first entry in the table, or a fixed number reads a different integer here and
	-- fails by name. That is strictly stronger than the old inference and it needs no interleaving.
	local function expectCooldownAbout(player, who, key, total, firedTick, why)
		local got = chargingTicks(player, key)
		if got == nil then
			fault("%s's %s reads %q %d ticks after it fired, expected a `charging:<ticks>`"
				.. " reading. `ready` means firing cost the side nothing at all; `hidden` means the"
				.. " power is still a BOUGHT power with an empty magazine and the Escalation bypass"
				.. " did not apply. %s", who, key, state(player, key), tick - firedTick, why)
			return false
		end

		local want = total - (tick - firedTick)
		if math.abs(got - want) > COOLDOWN_TOLERANCE then
			fault("%s's %s has %d ticks left %d ticks after firing, expected about %d -- the"
				.. " rules.yaml entry for the band it FIRED is %d. The four entries here are"
				.. " 300/320/340/360 precisely so this reading can say WHICH one was applied;"
				.. " %d is not it. %s",
				who, key, got, tick - firedTick, want, total, got, why)
			return false
		end

		return true
	end

	-- ==== TWO POWERS ON ONE SIDE MUST CARRY THE SAME NUMBER, AND THAT IS THE NON-VACUOUS FORM ====
	-- ADDED 2026-09-15 AFTER REVIEW. `expectCharging` alone cannot tell "the side cooldown was
	-- applied to this band" from "this band was never granted and is counting down its own
	-- constructed interval" -- both read `charging:`, and the second is precisely the defect the
	-- first GREEN run of this file hit (a 20 kt cameo at 06:34 on a side that had fired nothing).
	--
	-- COMPARED AGAINST A SIBLING RATHER THAN AGAINST A TICK COUNT COMPUTED HERE. A side cooldown is
	-- ONE number shared by every band the side holds, so the honest assertion is that two of its
	-- powers agree -- which needs no arithmetic about when the shot happened, survives any retiming
	-- of the phases, and fails loudly if the two are on independent clocks. The tolerance is for the
	-- one or two ticks a grant can lag its trigger by, not for slop in the rule.
	local function expectSameCooldown(player, who, newKey, carrierKey, why)
		local new = chargingTicks(player, newKey)
		local carrier = chargingTicks(player, carrierKey)

		if new == nil or carrier == nil then
			fault("%s: %s reads %q and %s reads %q; both must be `charging:<ticks>` for the"
				.. " side-cooldown comparison to mean anything. %s",
				who, newKey, state(player, newKey), carrierKey, state(player, carrierKey), why)
			return false
		end

		if math.abs(new - carrier) > 3 then
			fault("%s's %s has %d ticks left but its own %s has %d -- a side cooldown is ONE clock"
				.. " shared by every band the side holds, so these must agree. %s",
				who, newKey, new, carrierKey, carrier, why)
			return false
		end

		return true
	end

	-- Fire, and treat anything but "issued" as fatal to everything downstream. A climb that stalls
	-- at rung N makes every later reading meaningless rather than merely wrong, so say so.
	--
	-- ARMS THE ESCALATION WATCH ON THE SAME TICK when a victim band is named, and the ORDER matters:
	-- the impact-effect baseline is taken BEFORE the order is issued, which is what makes "the count
	-- moved" mean "THIS warhead detonated" rather than "some warhead has detonated this run".
	--
	-- `sentinel` is the band the rise must GRANT, chosen per launch so a failure names a rung rather
	-- than timing out anonymously; see each call site. Pass nil for a shot fired only to put its own
	-- side on cooldown, where nobody's level moves.
	local function launch(player, who, key, why, victim, victimWho, sentinel)
		local effects0 = Test.GetImpactEffectCount()
		local result = Test.ActivateSupportPower(player, key, CPos.New(AIM_X, AIM_Y))
		if result ~= "issued" then
			fault("%s could not fire %s: %q. %s Nothing after this point is evidence either way",
				who, key, result, why)
			return false
		end

		if sentinel == nil then
			return true
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
					.. " user ruling is that the level-up lands with the explosion -- \"we see the"
					.. " correlation between the explosion, and after only a few seconds perhaps we"
					.. " get the message of escalation\" -- so ReportNuclearRelease must RECORD the"
					.. " escalation and ServicePendingEscalations must apply it at the first impact"
					.. " plus EscalationDelayTicks. This is what a negative EscalationDelayTicks, or"
					.. " a deferral that was never wired, looks like",
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

	-- ==== THE RELOADING-VICTIM MOVE, WRITTEN ONCE AND USED TWICE ====
	-- Phases D and G2 both need a victim who is INSIDE ITS OWN COOLDOWN at the moment an enemy
	-- warhead escalates it. Before the deferral that happened by itself: a level rose on the click,
	-- eighty ticks after the victim's own shot. It cannot happen by itself now -- the rise is a
	-- whole missile flight downstream of the click, and every compressed cooldown here is shorter
	-- than every flight.
	--
	-- SO THE VICTIM FIRES ON THE TICK IT SEES THE INCOMING EXPLOSION. The escalation is scheduled
	-- for that same explosion plus EscalationDelayTicks, so it lands about fifty ticks into a 300-
	-- or 340-tick lockout -- deep inside it, not near an edge, and with no flight time anywhere in
	-- the arithmetic. The shot itself is harmless to the ladder: it escalates the enemy to a rung
	-- the enemy already holds.
	local function refireOnImpact(key, why)
		if state(USA, key) ~= "ready" then
			fault("USA's %s reads %q at t%d and cannot be fired. This shot is what puts USA back"
				.. " on a cooldown so the escalation arriving ~%d ticks from now lands on a"
				.. " RELOADING side, which is the whole subject of the phase that follows. %s",
				key, state(USA, key), tick, ESCALATION_DELAY_TICKS, why)
			return false
		end

		if not launch(USA, "USA", key, why, nil, nil, nil) then
			return false
		end

		-- The baseline is taken AFTER the launch, on the tick the watched warhead's own impact was
		-- counted, so it already includes that bump and the next delta is this shot's.
		skyPending = { effects0 = Test.GetImpactEffectCount(), orderTick = tick, key = key }
		return true
	end

	step = function()
		tick = tick + 1

		-- ---- THE SKY, before anything else reads the impact counter.
		if skyPending ~= nil then
			if Test.GetImpactEffectCount() > skyPending.effects0 then
				skyPending = nil
			elseif tick - skyPending.orderTick > FLIGHT_BUDGET_TICKS then
				fault("USA's unwatched %s, fired at t%d to put its side back on cooldown, never"
					.. " detonated: Test.GetImpactEffectCount has not moved off %d in %d ticks."
					.. " The phase that needed the cooldown may still have passed, but the next"
					.. " watch cannot take an honest baseline while a warhead is unaccounted for",
					skyPending.key, skyPending.orderTick, skyPending.effects0,
					tick - skyPending.orderTick)
				verdict()
				return
			end
		end

		-- ---- THE WATCH RUNS FIRST, EVERY TICK, from each launch until the level it caused rises.
		-- It is the only thing in this file that reads a tick it did not choose, and it is what
		-- schedules the phase that follows each launch. See the file header for (a) and (b).
		if watch ~= nil and watch.riseTick == nil then
			local before = watch.impactTick
			local risen = pollWatch()
			if risen == -1 then
				verdict()
				return
			end

			-- THE EXPLOSION WAS SEEN ON THIS TICK. Two of the four launches hand the reloading-
			-- victim move off this edge; see refireOnImpact.
			if before == nil and watch.impactTick ~= nil then
				if risesSeen == 1 then
					USA_REFIRE_TICK = tick
					if not refireOnImpact(USA_1KT,
						"USA's own 1 kt, off cooldown since t" .. (FIRE_1KT_TICK + CD_1KT) .. ".") then
						verdict()
						return
					end

					RECOVER_CHECK_TICK = tick + CD_1KT + 40
				elseif risesSeen == 3 then
					USA_REFIRE2_TICK = tick
					if not refireOnImpact(USA_50KT,
						"USA's 50 kt, the highest band its level 3 allows.") then
						verdict()
						return
					end

					END_READY_TICK = tick + CD_50KT + 40
				end
			end

			if risen ~= nil then
				risesSeen = risesSeen + 1
				note(true, "L%d landed: fired t%d, detonated t%d (+%d), %s's %s drawn t%d (+%d)",
					risesSeen, watch.orderTick, watch.impactTick,
					watch.impactTick - watch.orderTick, watch.victimWho, watch.sentinel,
					risen, risen - watch.impactTick)

				if risesSeen == 1 then
					RATCHET_CHECK_TICK = risen + RISE_SETTLE_TICKS
				elseif risesSeen == 2 then
					LOCKED_CHECK_TICK = risen + RISE_SETTLE_TICKS
				elseif risesSeen == 3 then
					L4_CHECK_TICK = risen + RISE_SETTLE_TICKS
				else
					END_LOCKED_TICK = risen + RISE_SETTLE_TICKS
				end
			end
		end

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
		-- SENTINEL RU_20KT: the one band this rise grants, so a watch timeout names the rung.
		if tick == FIRE_1KT_TICK then
			if not launch(USA, "USA", USA_1KT,
				"This is rung 1 of 4 and release just handed it over.",
				Russia, "Russia", RU_20KT) then
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE B2. THE SIDE-WIDE COOLDOWN, READ OFF THE LAUNCH. Availability is settled the
		-- instant the button is pressed -- ReportNuclearRelease charges the firer on the COUNTED
		-- edge, minutes before anything lands -- so this half of the model is anchored on the click
		-- and is untouched by the deferral. It used to be folded into phase C.
		if tick == USA_CD1_CHECK_TICK then
			local ok = expectCooldownAbout(USA, "USA", USA_1KT, CD_1KT, FIRE_1KT_TICK,
				"USA fired its 1 kt and the SIDE is now on cooldown")
			ok = expect(Russia, "Russia", RU_1KT, "ready",
				"RUSSIA'S 1 KT WENT ON COOLDOWN WHEN USA FIRED. A cooldown belongs to the side that"
				.. " fired; `charging:` here means it is being applied to everybody") and ok
			ok = expect(Russia, "Russia", RU_20KT, "hidden",
				"RUSSIA WAS ESCALATED BY A WARHEAD STILL IN THE AIR. The watch asserts this on every"
				.. " tick of the flight; this reading is the same claim made once, where a triager"
				.. " reading the phase list will see it") and ok

			note(ok, "side cooldown ok at t%d (fired t%d)", tick, FIRE_1KT_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE C. THE RATCHET EDGE, read 12 ticks after Russia's 20 kt was drawn.
		if tick == RATCHET_CHECK_TICK then
			-- THE COOLDOWN-ZERO HALF OF THE GRANT RULE, and it is non-vacuous by arithmetic rather
			-- than by hope: a band whose grant reached the condition layer but never reached
			-- MakeBandsReady counts down its OWN interval, which rules.yaml sets to 320 for 20 kt,
			-- and SupportPowerInstance.Tick pins that to full for every tick the power is disabled
			-- -- so it starts running at the rise and would read `charging:308` here.
			local ok = expect(Russia, "Russia", RU_20KT, "ready",
				"being hit by 1 kt must raise Russia's LEVEL to 20 kt, ready to fire now -- Russia"
				.. " did not fire, so Russia is on no cooldown, and a granted band on a side that"
				.. " owes nothing must be READY. `charging:` means the grant never reached this"
				.. " power and it is running down its own 320-tick interval; `hidden` would have"
				.. " been caught by the watch rather than reaching this phase at all")
			ok = expect(USA, "USA", USA_20KT, "hidden",
				"THE FIRER CLIMBED BY FIRING. That is decision 06's shared pressure ladder, where"
				.. " both sides always read the same rung and going first was free -- not the"
				.. " ratchet, where firing raises the OTHER side's level and never your own") and ok

			note(ok, "ratchet ok at t%d (fired t%d)", tick, FIRE_1KT_TICK)
			FIRE_20KT_TICK = tick + 10
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE C2. Russia answers at 20 kt.
		-- SENTINEL USA_50KT: USA goes from level 1 to level 3, so bands 2 and 3 are both new;
		-- the 50 kt is the one phase D reads.
		if tick == FIRE_20KT_TICK then
			if not launch(Russia, "Russia", RU_20KT,
				"Rung 2 of 4, fired from the level USA's shot just handed Russia.",
				USA, "USA", USA_50KT) then
				verdict()
				return
			end

			RU_CD20_CHECK_TICK = tick + LAUNCH_COOLDOWN_GAP
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE C3. AND RUSSIA'S WHOLE ARSENAL WENT DOWN WITH ITS 20 KT, on the 20 kt entry.
		if tick == RU_CD20_CHECK_TICK then
			local ok = expectCooldownAbout(Russia, "Russia", RU_20KT, CD_20KT, FIRE_20KT_TICK,
				"Russia fired its 20 kt")
			ok = expectCharging(Russia, "Russia", RU_1KT,
				"RUSSIA'S 1 KT STAYED LOADED AFTER RUSSIA FIRED ITS 20 KT. This is v1's per-band"
				.. " regeneration: one shot muted one band and left the rest of the arsenal up,"
				.. " which is the 'too many nukes in flight' the 2026-09-15 ruling replaced") and ok
			ok = expectSameCooldown(Russia, "Russia", RU_1KT, RU_20KT,
				"a side cooldown is ONE clock; the band that was not fired must carry the band"
				.. " that was") and ok

			note(ok, "Russia's side down at t%d (fired t%d)", tick, FIRE_20KT_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE D. ESCALATED WHILE RELOADING. THE SHARPEST ASSERTION IN THE FILE.
		-- USA fired its own 1 kt on the tick Russia's 20 kt detonated (refireOnImpact), so it is
		-- about fifty ticks into a 300-tick lockout when that detonation's escalation lands. Both
		-- of its newly granted bands must be DRAWN (the level is real) and NOT FIREABLE (the
		-- cooldown is too). See the file header for why a build that gets this wrong is broken in
		-- most matches.
		if tick == LOCKED_CHECK_TICK then
			local ok = expectCharging(USA, "USA", USA_50KT,
				"USA'S BRAND-NEW 50 KT IS FIREABLE INSIDE ITS OWN COOLDOWN. Russia's 20 kt raised"
				.. " USA to level 3, and USA has been reloading since t" .. USA_REFIRE_TICK .. "."
				.. " `ready` here is NuclearExchange.MakeBandsReady zeroing the timer on a level"
				.. " rise instead of setting it to the side's REMAINING cooldown -- a free 50 kt"
				.. " shot that rule 2 exists to deny, in the most ordinary situation this model"
				.. " has. `hidden` is a different bug: the level rise did not reach the condition"
				.. " layer at all -- though the watch would have caught that first")
			ok = expectCharging(USA, "USA", USA_20KT,
				"same defect, one band down -- USA reached level 3, so 2 and 3 are both new") and ok

			-- AND THE NUMBERS MUST MATCH, WHICH IS WHAT MAKES THE TWO READINGS ABOVE EVIDENCE.
			-- USA_1KT has carried the side cooldown since USA_REFIRE_TICK and was not granted by
			-- this rise; the two new bands were. If the grant never ran, the new bands are counting
			-- down their OWN constructed intervals (320 and 340 from rules.yaml) and will not agree
			-- with it -- which is exactly how this file passed phase D on a broken build before
			-- 2026-09-15.
			ok = expectSameCooldown(USA, "USA", USA_50KT, USA_1KT,
				"THE NEW BAND IS ON A CLOCK OF ITS OWN. A level rise must leave the granted band"
				.. " carrying the SIDE's remaining cooldown, not the band's own interval and not a"
				.. " fresh full one") and ok
			ok = expectSameCooldown(USA, "USA", USA_20KT, USA_1KT,
				"same comparison, one band down") and ok

			note(ok, "locked-while-escalated ok at t%d (USA reloading since t%d)",
				tick, USA_REFIRE_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE E. AND THE WHOLE ARSENAL COMES BACK AT ONCE. A cooldown that never expired
		-- would be a side permanently disarmed by its own first shot, which is a worse bug than the
		-- one phase D catches -- so the recovery is asserted, not assumed.
		--
		-- THIS PHASE USED TO CARRY A SECOND CLAIM AND NO LONGER DOES. It read at a tick where USA's
		-- 300 had expired and Russia's 320 had not, which separated the two constants by inference
		-- and needed the two launches to sit inside one cooldown of each other. A deferred
		-- escalation makes that impossible -- Russia's shot now waits a whole flight for the level
		-- that permits it -- so the claim moved to expectCooldownAbout, which reads the applied
		-- entry as a NUMBER at each launch. Phases B2, C3, F2 and G3 carry it now, one per band.
		if tick == RECOVER_CHECK_TICK then
			local ok = expect(USA, "USA", USA_1KT, "ready",
				string.format("USA's arsenal did not come back %d ticks after it fired, against a"
					.. " KilotonCooldownTicks of %d in rules.yaml. If this still reads `charging:`"
					.. " the applied cooldown is longer than the override",
					tick - USA_REFIRE_TICK, CD_1KT))
			ok = expect(USA, "USA", USA_50KT, "ready",
				"the band USA gained WHILE reloading must come back with the rest of the arsenal,"
				.. " on the same clock. A band still charging after the firer recovered means the"
				.. " grant was set to a different value from the side's own cooldown") and ok
			ok = expect(USA, "USA", USA_20KT, "ready",
				"same claim, one band down") and ok

			note(ok, "recovery ok at t%d, %d ticks after USA fired",
				tick, tick - USA_REFIRE_TICK)
			FIRE_50KT_TICK = tick + 10
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE F. USA climbs to 50 kt. Russia goes to level 4.
		-- SENTINEL RU_100KT: the top of the newly granted range and the band phase F3 reads.
		if tick == FIRE_50KT_TICK then
			-- WAIT FOR THE SKY. USA's phase-D shot is the one warhead that could still be in the
			-- air here, and this watch's baseline has to be taken with nothing else falling.
			if skyPending ~= nil then
				FIRE_50KT_TICK = tick + 1
				Trigger.AfterDelay(1, step)
				return
			end

			if not launch(USA, "USA", USA_50KT, "Rung 3 of 4.", Russia, "Russia", RU_100KT) then
				verdict()
				return
			end

			USA_CD50_CHECK_TICK = tick + LAUNCH_COOLDOWN_GAP
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE F2. The 50 kt entry, read as a number, and the side-wide rule again.
		if tick == USA_CD50_CHECK_TICK then
			local ok = expectCooldownAbout(USA, "USA", USA_50KT, CD_50KT, FIRE_50KT_TICK,
				"USA fired its 50 kt")
			ok = expectSameCooldown(USA, "USA", USA_1KT, USA_50KT,
				"USA's 1 kt must carry the clock its 50 kt shot started") and ok
			ok = expectSameCooldown(USA, "USA", USA_20KT, USA_50KT,
				"and so must its 20 kt") and ok

			note(ok, "USA's side down at t%d on the 50 kt entry (fired t%d)", tick, FIRE_50KT_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE F3. Russia is at level 4 -- the band whose reply is the game-ender.
		if tick == L4_CHECK_TICK then
			local ok = expect(Russia, "Russia", RU_100KT, "ready",
				"being hit by 50 kt must raise Russia to level 4. Russia's own cooldown from t"
				.. tostring(FIRE_20KT_TICK) .. " ended a whole missile flight ago, so this must be"
				.. " `ready` and not `charging:`")
			note(ok, "level-4 ok at t%d", tick)
			FIRE_100KT_TICK = tick + 10
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE G. The 100 kt that takes USA to END.
		-- SENTINEL USA_100KT, DELIBERATELY NOT USA_ENDER. This rise takes USA from level 3 to level
		-- 5 and grants bands 4 AND 5. Watching the ENDER would turn the reported bug -- the END
		-- level granting no cameo -- into an anonymous watch timeout; watching the 100 kt lets the
		-- rise be DETECTED and lets phase G2 fail on the ender by name.
		if tick == FIRE_100KT_TICK then
			if not launch(Russia, "Russia", RU_100KT,
				"Rung 4 of 4: the 100 kt shot whose reply is a game-ender.",
				USA, "USA", USA_100KT) then
				verdict()
				return
			end

			RU_CD100_CHECK_TICK = tick + LAUNCH_COOLDOWN_GAP
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE G3. The 100 kt entry, read as a number. The fourth and last of the four.
		if tick == RU_CD100_CHECK_TICK then
			local ok = expectCooldownAbout(Russia, "Russia", RU_100KT, CD_100KT, FIRE_100KT_TICK,
				"Russia fired its 100 kt")
			ok = expectSameCooldown(Russia, "Russia", RU_1KT, RU_100KT,
				"Russia's 1 kt must carry the clock its 100 kt shot started") and ok

			note(ok, "Russia's side down at t%d on the 100 kt entry (fired t%d)",
				tick, FIRE_100KT_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE G2. USA IS AT END AND STILL RELOADING. Phase D's rule, at the top rung, where
		-- it runs through a DIFFERENT code path: ArmableAtTopRung overrides the `powers.event`
		-- prerequisite that no faction provides, and a fix that reached for MakeReady there without
		-- re-applying the cooldown would open the game-ender early. One tick of that is the match.
		--
		-- USA fired its 50 kt on the tick Russia's 100 kt detonated (refireOnImpact), so it is
		-- about fifty ticks into a 340-tick lockout when the escalation lands.
		if tick == END_LOCKED_TICK then
			-- THE SAME PAIR OF READINGS AS PHASE D, for the same reason: `charging:` alone cannot
			-- tell a correctly-applied side cooldown from a band that was never granted. USA_50KT
			-- has carried the cooldown since USA_REFIRE2_TICK, so the ender is compared against the
			-- carrier rather than against a tick count.
			local ok = expectCharging(USA, "USA", USA_ENDER,
				"USA'S GAME-ENDER IS FIREABLE INSIDE ITS OWN COOLDOWN. USA reached level 5 on this"
				.. " tick and has been reloading since t" .. USA_REFIRE2_TICK .. "."
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

			ok = expectSameCooldown(USA, "USA", USA_ENDER, USA_50KT,
				"THE GAME-ENDER IS ON A CLOCK OF ITS OWN. The top rung is armed through a different"
				.. " path from every band below it (ArmableAtTopRung overrides the `powers.event`"
				.. " prerequisite), and that path has to apply the side's remaining cooldown exactly"
				.. " as the ordinary one does -- at this rung, being a few hundred ticks early is"
				.. " the match") and ok

			note(ok, "END-locked ok at t%d (USA reloading since t%d)", tick, USA_REFIRE2_TICK)
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
					tick - USA_REFIRE2_TICK, CD_50KT))

			note(ok, "END-ready ok at t%d", tick)
			FIRE_ENDER_TICK = tick + 10
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
			fault("ran out of budget at tick %d without reaching the ender launch. Rises seen: %d"
				.. " of 4", tick, risesSeen)
			verdict()
			return
		end

		Trigger.AfterDelay(1, step)
	end

	Trigger.AfterDelay(1, step)
end
