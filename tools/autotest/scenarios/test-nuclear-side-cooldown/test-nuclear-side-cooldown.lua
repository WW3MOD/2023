--[[
	ONE SHOT SPENDS THE WHOLE SIDE -- and each faction holds only its own ladder.

	WHY EVERY READING IS Test.GetSupportPowerState AND NOT A TRAIT READ. There is no Lua binding on
	NuclearExchange, and there deliberately is not one: what both rulings are about is what a player
	can DO, and GetSupportPowerState reads SupportPowerInstance.Disabled / Ready -- the same
	predicates SupportPowersWidget filters and draws its icon list on. So `ready` here means a player
	would see a cameo they could click, `hidden` means the cameo is absent from the bin entirely, and
	`charging:<n>` means the bin draws it with a clock on it.

	THE THREE TOKENS ARE THE WHOLE ARGUMENT, so it is worth being exact about which one proves what:

	  hidden      the power is not Permitted. For a faction-locked warhead that is the tech tree --
	              `powers.america` is provided by faction alone and this player is not that faction.
	  ready       Permitted, enabled, timer at zero.
	  charging:n  Permitted and on a clock with n ticks left. THIS TOKEN IS THE FREE-TIMER ECONOMY:
	              a nuclear power that was still RequiresPurchase would read `hidden` after firing
	              (empty magazine), not `charging:`.

	RENAMED FROM test-nuclear-band-regen AT EXCHANGE v2 (2026-09-15), and the rule it pins got WIDER
	rather than different. v1: "firing any weapon in a BAND puts that band on its timer for the whole
	side", with three other bands left loaded. v2: "firing anything puts the whole SIDE on one
	cooldown, at every band". The same three readings hold for both -- Volga's warhead is in the same
	band as USA's AND on the same side -- so this file measures the stronger claim unchanged, and the
	only thing that had to move is the rules.yaml field name (KilotonRegenTicks -> KilotonCooldownTicks).

	WHAT WOULD MAKE THIS SCENARIO A LIE, listed because each one passes for the wrong reason:
	  * `powers-sandbox` ON. It grants all three tiers to everyone, so USA really would hold the
	    9M729 and phase A's absence assertions would be measuring the option rather than the lock.
	    rules.yaml locks it OFF.
	  * USA and Volga on different sides. Then the central reading is "firing does not reload an
	    opponent's warhead", which was never in doubt. map.yaml pins Team 1 on both.
	  * Money. A spent power that can be re-bought reads `ready` again immediately. DefaultCash 0.
]]

-- The two powers, by OrderName (SupportPowerManager keys its dictionary on it).
local USA_1KT = "B61LowStrike"     -- B61-12 low dial, 300 t  -> Kiloton band, America's ladder
local RU_1KT = "Ru9M729Strike"     -- 9M729, 1000 t           -> Kiloton band, Russia's ladder

-- Empty ground in the middle of the map, far from all three Supply Routes.
local AIM_X = 32
local AIM_Y = 17

-- rules.yaml compresses the 1 kt band's SIDE COOLDOWN from the shipped 5000 (5:00) to this.
local COOLDOWN_TICKS = 300

-- Phase boundaries, in ticks from t=0. Generous: every one is "well after the thing it waits for",
-- never a measurement of when that thing happened.
local LOCK_CHECK_TICK = 90                   -- release is at tick 10; 80 ticks of slack
local FIRE_TICK = 120
local SIDE_CHECK_TICK = FIRE_TICK + 30      -- the write runs inside order resolution on FIRE_TICK
local RECOVER_CHECK_TICK = FIRE_TICK + COOLDOWN_TICKS + 60
local BUDGET_TICK = RECOVER_CHECK_TICK + 200

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Volga = Player.GetPlayer("Volga")
	local Enemy = Player.GetPlayer("Enemy")

	local tick = 0
	local faults = {}
	local notes = {}
	local fireResult = "not-attempted"

	local function state(player, key)
		return Test.GetSupportPowerState(player, key)
	end

	-- THE NOTE MUST NOT LIE. `expect` records a fault and RETURNS rather than aborting, so the run
	-- reaches every phase and reports all of them -- but a note written unconditionally would then
	-- sit in result.json claiming a phase passed beside the fault that says it did not. Every note
	-- is gated on every expect in its phase having returned true.
	local function note(ok, fmt, ...)
		notes[#notes + 1] = (ok and "" or "NOT ") .. string.format(fmt, ...)
	end

	local function fault(fmt, ...)
		faults[#faults + 1] = string.format(fmt, ...)
	end

	-- Compared EXACTLY against the bare vocabulary in TestGlobal.SupportPowerState.
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

	-- The ticks left on a `charging:<n>` reading, or nil for any other token.
	local function chargingTicks(player, key)
		local got = state(player, key)
		if got:sub(1, 9) ~= "charging:" then
			return nil
		end

		return tonumber(got:sub(10))
	end

	-- TWO PLAYERS ON ONE SIDE MUST CARRY THE SAME NUMBER. A side cooldown is ONE clock; two
	-- teammates reading different values means each is on a timer of its own, which is the per-power
	-- economy v2 replaced. Compared against each other rather than against a tick count computed
	-- here, so the assertion needs no arithmetic about when the shot happened.
	local function expectSameCooldown(aPlayer, aWho, aKey, bPlayer, bWho, bKey, why)
		local a = chargingTicks(aPlayer, aKey)
		local b = chargingTicks(bPlayer, bKey)

		if a == nil or b == nil then
			fault("%s's %s reads %q and %s's %s reads %q; both must be `charging:<ticks>` for the"
				.. " side-cooldown comparison to mean anything. %s",
				aWho, aKey, state(aPlayer, aKey), bWho, bKey, state(bPlayer, bKey), why)
			return false
		end

		if math.abs(a - b) > 3 then
			fault("%s's %s has %d ticks left but %s's %s has %d -- one side, one cooldown, so these"
				.. " must agree. %s", aWho, aKey, a, bWho, bKey, b, why)
			return false
		end

		return true
	end

	local function verdict()
		local summary = string.format(
			"fire=%q | USA b61=%s 9m729=%s | Volga 9m729=%s b61=%s | Enemy 9m729=%s | %s",
			fireResult, state(USA, USA_1KT), state(USA, RU_1KT),
			state(Volga, RU_1KT), state(Volga, USA_1KT), state(Enemy, RU_1KT),
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

		-- ---- PHASE A. THE FACTION LOCK, read off the bin. Release has opened the 1 kt band for
		-- both sides, so each player holds its OWN faction's 1 kt warhead and NOT the other's.
		if tick == LOCK_CHECK_TICK then
			local ok = expect(USA, "USA", USA_1KT, "ready",
				"the release gate opened at tick 10 and must hand USA a loaded B61 -- with"
				.. " DefaultCash 0 nothing else can have loaded it. If this reads `hidden` the"
				.. " problem is upstream of the faction lock: check debug.log's"
				.. " `NUCLEAR EXCHANGE sides:` line before reading anything below as a lock failure")
			ok = expect(Volga, "Volga", RU_1KT, "ready",
				"Volga is Russia and must hold Russia's 1 kt warhead. Release is simultaneous for"
				.. " both players on a side") and ok

			-- THE LOCK ITSELF, and it is an ABSENCE. `hidden` and not `absent`: the trait is on
			-- every player actor, so the key IS registered on the manager -- what the unmet
			-- `powers.russia` prerequisite takes away is Permitted, which is what removes the cameo
			-- from the bin (SupportPowerManager.cs:167-175).
			ok = expect(USA, "USA", RU_1KT, "hidden",
				"AN AMERICA PLAYER IS HOLDING RUSSIA'S 9M729. `powers.russia` is provided by faction"
				.. " alone (player.yaml ProvidesPrerequisite@PowersRussia) and `powers-sandbox` is"
				.. " locked off in rules.yaml, so the only ways this reads `ready` are a lost"
				.. " prerequisite on the power or a sandbox provider that is granting anyway") and ok
			ok = expect(Volga, "Volga", USA_1KT, "hidden",
				"A RUSSIA PLAYER IS HOLDING AMERICA'S B61. Same gate, other direction") and ok

			-- The opponent is Russia too and holds the same warhead Volga does. Read here so the
			-- phase C reading below is a CHANGE from a known starting point rather than a bare
			-- value -- if Enemy were already unarmed at t90 the side assertion there would pass
			-- for a reason that has nothing to do with sides.
			ok = expect(Enemy, "Enemy", RU_1KT, "ready",
				"the opponent did not get the 1 kt band at release. Release is simultaneous for"
				.. " every side; check debug.log's `NUCLEAR EXCHANGE sides:` line") and ok

			note(ok, "faction lock ok at t%d", tick)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE B. USA fires the smallest warhead in the mod at empty ground.
		if tick == FIRE_TICK then
			fireResult = Test.ActivateSupportPower(USA, USA_1KT, CPos.New(AIM_X, AIM_Y))
			if fireResult ~= "issued" then
				fault("USA could not fire %s: %q. Nothing downstream of this means anything, so the"
					.. " band readings below are not evidence either way", USA_1KT, fireResult)
				verdict()
				return
			end

			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE C. THE WHOLE SIDE WENT ON THE CLOCK. The whole scenario.
		--
		-- ==== THE FIRER'S OWN READING IS NOT EVIDENCE FOR SetSideCooldown, AND THAT COST A RED ====
		-- ESTABLISHED 2026-09-15 by a sabotage that should have failed this file and did not: with
		-- SetSideCooldown skipping every player but the side's first combatant -- which on this map
		-- is VOLGA, not USA (debug.log: `Volga(1), Enemy(2), USA(1)`) -- all three readings below
		-- still passed.
		--
		-- THE CAUSE IS THE ENGINE, NOT THE TRAIT. SupportPowerInstance.Activate assigns
		-- `remainingSubTicks = TotalTicks * 100` to the power that FIRED, immediately after calling
		-- into MissileStrikePower (SupportPowerManager.cs). So the fired cameo goes on a clock whether
		-- or not this mod's trait touched it -- and the number is the same either way, because the
		-- side cooldown for a launch at band B and that power's own constructed interval are THE SAME
		-- TABLE ENTRY. There is no configuration of this scenario in which USA's own B61 can tell the
		-- two writes apart.
		--
		-- SO VOLGA'S READING IS THE ONLY LOAD-BEARING ONE IN THIS FILE, and any RED aimed at
		-- SetSideCooldown must skip VOLGA specifically -- skipping the firer is invisible by
		-- construction. USA's reading below is kept because it is still worth knowing the firer is on
		-- a clock at all (a `hidden` there is the purchase economy leaking back in), but it proves
		-- nothing about the side-wide rule on its own.
		if tick == SIDE_CHECK_TICK then
			local ok = expectCharging(USA, "USA", USA_1KT,
				"USA fired its own B61 and must be on a clock. NOTE this reading cannot fail for a"
				.. " SetSideCooldown defect -- SupportPowerInstance.Activate puts the FIRED power on"
				.. " its own interval regardless -- so `ready` here means something further upstream:"
				.. " `hidden` means the power is still a BOUGHT power with an empty magazine and the"
				.. " Escalation free-timer bypass did not apply")

			-- THIS IS THE READING THE SCENARIO EXISTS FOR. Volga did not fire, holds a DIFFERENT
			-- weapon, and is a DIFFERENT PLAYER -- but is on the same side. Under v1 the cooldown
			-- was per BAND and this still held (the 9M729 and the B61 share the Kiloton band); under
			-- v2 it holds for a wider reason, which is that a cooldown is not a property of a band
			-- at all. Before EITHER ruling this read `ready` and the side got two shots back to back.
			ok = expectCharging(Volga, "Volga", RU_1KT,
				"THE TEAMMATE'S WARHEAD IS STILL LOADED AFTER THE SIDE FIRED. One launch must put"
				.. " EVERY player on the firing side on the cooldown"
				.. " (NuclearExchange.SetSideCooldown). `ready` here is the pre-ruling behaviour:"
				.. " per-power or per-firer regeneration, so a team of two fires twice for one"
				.. " interval. THIS IS THE READING THE RED MUST TARGET: a sabotage that skips the"
				.. " FIRER instead is invisible here, because the engine puts the fired power on a"
				.. " clock by itself (see the note at the top of this phase)") and ok

			-- AND THE TWO MUST CARRY THE SAME NUMBER. One side, one clock. This is what separates
			-- "the side cooldown reached Volga" from "Volga is on a timer of its own that happens to
			-- be running" -- the second is the per-power economy v2 replaced, and it would show up
			-- here as two different remainders. rules.yaml compresses ONLY the 1 kt entry (300) and
			-- leaves the other three shipped (7000/9000/12000), so a write that indexed the wrong
			-- band would miss by thousands rather than by a rounding error.
			ok = expectSameCooldown(USA, "USA", USA_1KT, Volga, "Volga", RU_1KT,
				"THE TEAMMATES ARE ON SEPARATE CLOCKS. A side cooldown is one number written to every"
				.. " nuclear power the side holds; two different remainders mean each power kept an"
				.. " interval of its own") and ok

			-- ==== THE SIDE BOUNDARY, AND IT IS WHY THIS SCENARIO IS A 2v1 =====================
			-- THE COOLDOWN IS PER SIDE, SO IT MUST STOP AT ONE. Enemy is Team 2 and did not fire;
			-- its own 1 kt warhead must still be loaded.
			--
			-- THIS ASSERTION EXISTS BECAUSE THE SCENARIO ONCE PASSED WITHOUT IT. The 2026-09-14
			-- GREEN run logged `NUCLEAR EXCHANGE sides: Volga(1), Enemy(1), USA(1)` -- all three
			-- combatants on ONE side, so the match was a 3v0 and "the teammate's warhead went on
			-- cooldown" was true of everybody on the board, trivially. Nothing in the readings
			-- above could tell that apart from the feature working. This one can: on the broken
			-- keying Enemy is a teammate and reads `charging:` here.
			--
			-- Preferred to reading the side keys directly because there is no Lua binding on
			-- NuclearExchange, and because a behavioural assertion cannot be satisfied by a
			-- binding that reports the number it was told rather than the number in use.
			ok = expect(Enemy, "Enemy", RU_1KT, "ready",
				"THE OPPONENT'S WARHEAD WENT ON COOLDOWN WHEN USA FIRED. The cooldown is per SIDE"
				.. " and Enemy is Team 2. `charging:` here almost certainly means all three"
				.. " combatants were keyed onto one side -- READ debug.log's"
				.. " `NUCLEAR EXCHANGE sides:` LINE, which must say USA(1), Volga(1), Enemy(2)."
				.. " A run where it reads Enemy(1) is measuring nothing, whichever way it ends") and ok

			note(ok, "side cooldown ok at t%d, %d ticks after the shot", tick, tick - FIRE_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE D. AND THE WHOLE SIDE COMES BACK AT ONCE. A cooldown that never expired would
		-- be a side permanently disarmed by its own first shot, which is a worse bug than the one
		-- this scenario fixes -- so the recovery is asserted, not assumed.
		if tick == RECOVER_CHECK_TICK then
			local ok = expect(USA, "USA", USA_1KT, "ready",
				string.format("the side did not come off cooldown %d ticks after it fired, with"
					.. " DefaultCash 0. If this still reads `charging:` the applied cooldown is"
					.. " longer than the KilotonCooldownTicks override in rules.yaml -- check that"
					.. " the side takes the FIRED band's entry and not a larger one", COOLDOWN_TICKS))
			ok = expect(Volga, "Volga", RU_1KT, "ready",
				"Volga's warhead was put on the side clock by USA's shot and must come off it on the"
				.. " SAME tick. A teammate left charging after the firer recovered means the two"
				.. " were written different values, which a single side-wide cooldown does not have") and ok

			note(ok, "recovery ok at t%d, %d ticks after the shot", tick, tick - FIRE_TICK)
			verdict()
			return
		end

		if tick >= BUDGET_TICK then
			fault("ran out of budget at tick %d without reaching the recovery check at %d",
				tick, RECOVER_CHECK_TICK)
			verdict()
			return
		end

		Trigger.AfterDelay(1, step)
	end

	Trigger.AfterDelay(1, step)
end
