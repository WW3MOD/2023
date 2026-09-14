--[[
	ONE SHOT SPENDS THE WHOLE BAND, FOR THE WHOLE SIDE -- and each faction holds only its own ladder.

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

-- rules.yaml compresses the 1 kt band's regeneration from the shipped 3000 (3:00) to this.
local REGEN_TICKS = 300

-- Phase boundaries, in ticks from t=0. Generous: every one is "well after the thing it waits for",
-- never a measurement of when that thing happened.
local LOCK_CHECK_TICK = 90                   -- release is at tick 10; 80 ticks of slack
local FIRE_TICK = 120
local BAND_CHECK_TICK = FIRE_TICK + 30       -- the reset runs inside order resolution on FIRE_TICK
local REGEN_CHECK_TICK = FIRE_TICK + REGEN_TICKS + 60
local BUDGET_TICK = REGEN_CHECK_TICK + 200

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

		-- ---- PHASE C. THE BAND WENT ON THE CLOCK, AND SO DID THE TEAMMATE'S. The whole scenario.
		if tick == BAND_CHECK_TICK then
			local ok = expectCharging(USA, "USA", USA_1KT,
				"USA fired its own B61 and it must be on the band's regeneration timer. `ready`"
				.. " means firing cost nothing at all; `hidden` means the power is still a BOUGHT"
				.. " power with an empty magazine and the Escalation free-timer bypass did not apply")

			-- THIS IS THE READING THE SCENARIO EXISTS FOR. Volga did not fire, holds a DIFFERENT
			-- weapon, and is a DIFFERENT PLAYER -- but is on the same side, and the 9M729 is in the
			-- same band as the B61 that was fired (both are <= 1000 t, NuclearReleaseLadder's
			-- Kiloton ceiling). Before the 2026-09-14 ruling regeneration was per power and this
			-- read `ready`: the side got two 1 kt shots back to back for one interval.
			ok = expectCharging(Volga, "Volga", RU_1KT,
				"THE TEAMMATE'S WARHEAD IS STILL LOADED AFTER THE BAND WAS FIRED. Firing any weapon"
				.. " in a band must put that band on its timer for EVERY player on the firing side"
				.. " (NuclearExchange.PutBandOnRegen). `ready` here is the exact pre-ruling"
				.. " behaviour: per-power regeneration, so a side fires a band once per warhead it"
				.. " holds in it. If USA's own reading above is `ready` too, the reset is not"
				.. " running at all; if only THIS one is `ready`, it is running for the firer only") and ok

			-- ==== THE SIDE BOUNDARY, AND IT IS WHY THIS SCENARIO IS A 2v1 =====================
			-- THE RESET IS PER SIDE, SO IT MUST STOP AT ONE. Enemy is Team 2 and did not fire;
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
				"THE OPPONENT'S WARHEAD WENT ON COOLDOWN WHEN USA FIRED. The band reset is per SIDE"
				.. " and Enemy is Team 2. `charging:` here almost certainly means all three"
				.. " combatants were keyed onto one side -- READ debug.log's"
				.. " `NUCLEAR EXCHANGE sides:` LINE, which must say USA(1), Volga(1), Enemy(2)."
				.. " A run where it reads Enemy(1) is measuring nothing, whichever way it ends") and ok

			note(ok, "band ok at t%d, %d ticks after the shot", tick, tick - FIRE_TICK)
			Trigger.AfterDelay(1, step)
			return
		end

		-- ---- PHASE D. AND THE WHOLE BAND COMES BACK. A band-wide reset that never expired would
		-- be a side permanently disarmed by its own first shot, which is a worse bug than the one
		-- this scenario fixes -- so the recovery is asserted, not assumed.
		if tick == REGEN_CHECK_TICK then
			local ok = expect(USA, "USA", USA_1KT, "ready",
				string.format("the 1 kt band did not come back %d ticks after it was fired, with"
					.. " DefaultCash 0. If this still reads `charging:` the band timer is longer"
					.. " than the KilotonRegenTicks override in rules.yaml -- check that the reset"
					.. " assigns the BAND's interval and not something larger", REGEN_TICKS))
			ok = expect(Volga, "Volga", RU_1KT, "ready",
				"Volga's warhead was put on the band clock by USA's shot and must come off it on the"
				.. " same schedule. A teammate left charging after the firer recovered means the two"
				.. " were reset to different values, which the band model does not have") and ok

			note(ok, "regen ok at t%d, %d ticks after the shot", tick, tick - FIRE_TICK)
			verdict()
			return
		end

		if tick >= BUDGET_TICK then
			fault("ran out of budget at tick %d without reaching the regen check at %d",
				tick, REGEN_CHECK_TICK)
			verdict()
			return
		end

		Trigger.AfterDelay(1, step)
	end

	Trigger.AfterDelay(1, step)
end
