-- SHARED BODY for the DEFCON 2 hold-fire contact pair:
--
--     tools/autotest/scenarios/test-defcon2-holdfire-contact            (Escalation, opens at 2 -- PASS)
--     tools/autotest/scenarios/test-defcon2-holdfire-contact-skirmish   (Skirmish             -- FAIL)
--
-- ONE BODY, TWO DIRECTORIES, ON PURPOSE. The two arms must differ by exactly one quantity or a
-- difference in outcome has more than one available explanation. Keeping the script here rather than
-- copying it into both folders means the only thing that CAN differ is the rules.yaml stanza, which
-- is the arrangement tools/autotest/scenarios/test-drone-lost-track/CONTROL-ARM.md argues for.
-- (`drone-lost-track-lib.lua` and `javelin-probe-lib.lua` are the existing instances; lua-gate
-- resolves a declared script in mods/ww3mod/scripts as readily as in the scenario folder.)
--
-- WHAT IS MEASURED, and why one scenario covers three of the six read sites.
--
--   PHASE 1, THE HOLD. Two enemy tanks stand ten cells apart, both on FireAtWill, both able to see
--   and hit the other, and neither is given an order. For HOLD_TICKS nothing may fire. That window
--   is ~40 autotarget scans wide (AutoTargetInfo.Min/MaximumScanTimeInterval are 3 and 8 ticks), so
--   "it did not fire" cannot be "it had not looked yet".
--       * READ SITE 1 -- AutoTarget.ChooseTarget returns Invalid while the hold is on, so the idle
--         rescan never produces a target.
--       * READ SITE 4 -- AttackFollow's persistent-opportunity fire has no target to promote.
--
--   PHASE 2, THE ORDER. The abrams is clicked onto the t90. DEFCON 2 deliberately permits this: the
--   rule is about provenance, and an order is provenance. The t90 must take damage.
--
--   PHASE 3, THE RETURN FIRE. From the tick the t90 is first hit, RETURN_TICKS in which the t90 --
--   shot, on FireAtWill, with the shooter in range and in the open -- must not shoot back.
--       * READ SITE 2 -- AutoTarget.INotifyDamage.Damaged returns at its first line while the hold
--         is on. Nothing downstream of that line runs, including the retaliation Attack.
--
-- FOUR INDEPENDENT OBSERVABLES FOR "A SHOT HAPPENED", because one of them silently ceasing to work
-- would otherwise turn this into a test that measures nothing:
--     ammo      either tank's primary-ammo falling below its opening count. Direct, and it counts a
--               MISS as well as a hit. Latched, because AmmoPool reloads over time.
--     health    either tank below its opening HP.
--     impacts   Test.GetImpactEffectCount, the engine's own count of warhead impacts that passed the
--               impact validity gates. Global rather than per-actor, so it also catches a shot from
--               something this script never names.
--     line      Test.GetAutomaticTargetLineCells, non-empty exactly when a unit acquired a target BY
--               ITSELF (AttackBase paints that colour only when IsAutoAcquiredSource holds,
--               AttackBase.cs:786-787). This one fires BEFORE a shot does, so a hold that is failing
--               reports acquisition rather than damage.
--
-- WHY THE LEVEL IS READ AND NOT ASSERTED TO BE 2. This body runs in both arms, so it cannot hardcode
-- the level it expects; what it does instead is stricter. Only 2 (the hold) and 0 (Skirmish, no
-- DefconEscalation in play) are acceptable openings, and the level is required to stay where it
-- opened for the whole run. 3 would be the CEASE-FIRE rung, where a completely different rule
-- (DefconFireDiscipline.PermitsWeapon, a function of the ARMAMENT) silences everything including
-- ordered fire -- a scenario that drifted to 3 would pass phase 1 for the wrong reason and fail
-- phase 2 for a reason that has nothing to do with hold-fire. 1 is open war. Both are named and both
-- are loud.

local TicksPerSecond = TestHarness.TicksPerSecond

local HOLD_FIRE_LEVEL = 2
local SKIRMISH_LEVEL = 0

local SETTLE_TICKS = 15     -- every World trait has ticked and the ActorMap position bins are filled
local HOLD_TICKS = 300      -- ~18 s of wall clock at Timestep 60; ~40 autotarget scan intervals
local ORDER_TICKS = 200     -- budget for turret slew + 10 cells of Bullet flight at Speed 1200
local RETURN_TICKS = 75     -- return-fire window; TankRound.Abrams BurstWait is 130, so the abrams'
                            -- SECOND round cannot land inside it and confuse the reading

-- Backstop only: every phase above owns its own budget and reports its own reason, so a timeout here
-- means the predicate itself stopped advancing. 1000 is a multiple of 25 and therefore round-trips
-- through AssertWithin's math.floor exactly (test-helpers.lua's note on that).
local DEADLINE_TICKS = 1000
local DEADLINE_SECONDS = DEADLINE_TICKS / TicksPerSecond

WorldLoaded = function()
	TestHarness.FocusBetween(Probe, Contact)
	TestHarness.Select(Probe)

	local ticks = 0
	local phase = "settle"

	local openingLevel = nil
	local probeAmmo0, contactAmmo0 = 0, 0
	local probeHp0, contactHp0 = 0, 0
	local effects0 = 0
	local damageTick = 0
	local orderIssuedTick = 0

	local function Ammo(a)
		return a.AmmoCount("primary-ammo")
	end

	local function LineOf(a)
		local cells = Test.GetAutomaticTargetLineCells(a)
		if #cells == 0 then
			return "(empty)"
		end

		local parts = {}
		for i, c in ipairs(cells) do
			parts[i] = c.X .. "," .. c.Y
		end

		return table.concat(parts, " ")
	end

	-- Every number the verdict could want, read LIVE. Never interpolated into an AssertWithin
	-- timeout string at registration time: that argument is evaluated eagerly, so a counter put
	-- there reports its value at tick 0 forever (AUTOTEST.md, "The failure message is evaluated
	-- EAGERLY"). Used only from inside the predicate and from the timeout FUNCTION.
	local function Census()
		return string.format(
			"t=%d phase=%s defcon=%d(opened %s) | probe ammo %d->%d hp %d/%d line %s act %s | "
			.. "contact ammo %d->%d hp %d/%d line %s act %s | impacts %d->%d",
			ticks, phase, Test.DefconLevel(), tostring(openingLevel),
			probeAmmo0, Probe.IsDead and -1 or Ammo(Probe),
			Probe.IsDead and 0 or Probe.Health, Probe.MaxHealth,
			Probe.IsDead and "(dead)" or LineOf(Probe),
			Probe.IsDead and "(dead)" or Test.ActivityChain(Probe),
			contactAmmo0, Contact.IsDead and -1 or Ammo(Contact),
			Contact.IsDead and 0 or Contact.Health, Contact.MaxHealth,
			Contact.IsDead and "(dead)" or LineOf(Contact),
			Contact.IsDead and "(dead)" or Test.ActivityChain(Contact),
			effects0, Test.GetImpactEffectCount())
	end

	-- THE HOLD WINDOW'S CHECK -- all four observables. Only valid while NOTHING is supposed to be
	-- shooting; once the abrams has been ordered to fire, three of the four move legitimately.
	local function NothingFired()
		if Ammo(Probe) < probeAmmo0 then
			return "fail: HOLD WINDOW -- the ABRAMS fired. Its primary-ammo fell "
				.. probeAmmo0 .. " -> " .. Ammo(Probe) .. " without an order having been given. "
				.. Census()
		end

		if Probe.Health < probeHp0 then
			return "fail: HOLD WINDOW -- the ABRAMS took damage, so something on this map fired at it. "
				.. Census()
		end

		if Ammo(Contact) < contactAmmo0 then
			return "fail: HOLD WINDOW -- the T90 fired. Its primary-ammo fell "
				.. contactAmmo0 .. " -> " .. Ammo(Contact) .. ", and it was never given an order. "
				.. Census()
		end

		if Test.GetImpactEffectCount() > effects0 then
			return "fail: HOLD WINDOW -- a warhead detonated somewhere on the map. "
				.. "Test.GetImpactEffectCount rose " .. effects0 .. " -> "
				.. Test.GetImpactEffectCount() .. ". " .. Census()
		end

		if #Test.GetAutomaticTargetLineCells(Probe) > 0 then
			return "fail: HOLD WINDOW -- the ABRAMS acquired a target by itself. The automatic-coloured "
				.. "target line is the colour AttackBase paints only when "
				.. "AutoTarget.IsAutoAcquiredSource holds (AttackBase.cs:786-787). " .. Census()
		end

		if #Test.GetAutomaticTargetLineCells(Contact) > 0 then
			return "fail: HOLD WINDOW -- the T90 acquired a target by itself. " .. Census()
		end

		return nil
	end

	-- THE RETURN-FIRE WINDOW'S CHECK, and it is deliberately a DIFFERENT set. The abrams is under
	-- orders and firing by now, so its ammunition, the global impact counter and the t90's health all
	-- move legitimately -- asserting on any of them here would fail the treatment for doing exactly
	-- what phase 2 asked it to do. What is left is everything the T90 would have to do to retaliate:
	-- spend a round, acquire a target by itself, or land a hit on the abrams.
	local function ContactHeld()
		if Ammo(Contact) < contactAmmo0 then
			return "fail: RETURN-FIRE WINDOW -- the T90 shot back. Its primary-ammo fell "
				.. contactAmmo0 .. " -> " .. Ammo(Contact) .. " after it was hit. "
				.. "AutoTarget.INotifyDamage.Damaged is supposed to return at its FIRST LINE while the "
				.. "hold is on -- that is read site 2. " .. Census()
		end

		if #Test.GetAutomaticTargetLineCells(Contact) > 0 then
			return "fail: RETURN-FIRE WINDOW -- the T90 acquired a target by itself after being hit. "
				.. "Nothing had to be fired for this to be read site 2 failing; the retaliation "
				.. "Attack was issued and painted automatic. " .. Census()
		end

		if Probe.Health < probeHp0 then
			return "fail: RETURN-FIRE WINDOW -- the abrams took damage after it opened fire, so the t90 "
				.. "retaliated. " .. Census()
		end

		return nil
	end

	TestHarness.AssertWithin(DEADLINE_SECONDS, function()
		ticks = ticks + 1

		if Probe.IsDead then
			return "fail: the abrams died. Nothing on this map should be able to do that inside the "
				.. "phase. " .. Census()
		end

		if Contact.IsDead then
			return "fail: the t90 died. Its HP is raised tenfold in rules.yaml precisely so it cannot, "
				.. "and a death here also drops DEFCON 2 to 1 and un-holds every gun on the map. "
				.. Census()
		end

		-- ========== SETTLE: the setup controls, once, then the baselines ==========
		if phase == "settle" then
			if ticks < SETTLE_TICKS then
				return false
			end

			local level = Test.DefconLevel()
			if level ~= HOLD_FIRE_LEVEL and level ~= SKIRMISH_LEVEL then
				return string.format(
					"fail: SETUP -- the match opened at DEFCON %d. Only %d (the hold-fire rung, this "
					.. "directory) and %d (Skirmish, the -skirmish twin) are arms of this pair. 3 is "
					.. "the CEASE-FIRE rung, where DefconFireDiscipline.PermitsWeapon silences every "
					.. "weapon including ordered fire -- the hold window below would pass for a reason "
					.. "that is not the rule under test. 1 is open war.", level, HOLD_FIRE_LEVEL,
					SKIRMISH_LEVEL)
			end

			openingLevel = level

			-- The abrams must be ABLE to attack the t90, or "it did not shoot" is worth nothing.
			-- GetTargetOrder walks the same IIssueOrder/IOrderTargeter chain the mouse cursor walks
			-- and issues nothing, so this is a control rather than a nudge.
			local offered = Test.GetTargetOrder(Probe, Contact)
			if offered ~= "Attack" then
				return "fail: SETUP -- a right-click from the abrams onto the t90 resolves to "
					.. tostring(offered) .. ", not Attack. The two are not a live targeting pair, so "
					.. "neither the hold window nor the ordered shot means anything. " .. Census()
			end

			local dx = Contact.Location.X - Probe.Location.X
			local dy = Contact.Location.Y - Probe.Location.Y
			local sep = math.sqrt(dx * dx + dy * dy)
			if sep > 24 or sep < 2 then
				return string.format(
					"fail: SETUP -- the two tanks are %.1f cells apart. TankRound.Abrams is "
					.. "25c0/MinRange 1c512 and TankRound.T90 is 24c0/1c512, so the pair must sit "
					.. "between 2 and 24 cells for BOTH to be able to shoot. %s", sep, Census())
			end

			probeAmmo0 = Ammo(Probe)
			contactAmmo0 = Ammo(Contact)
			probeHp0 = Probe.Health
			contactHp0 = Contact.Health
			effects0 = Test.GetImpactEffectCount()

			if probeAmmo0 <= 0 or contactAmmo0 <= 0 then
				return "fail: SETUP -- a tank opened with no ammunition (abrams " .. probeAmmo0
					.. ", t90 " .. contactAmmo0 .. "), so an unchanged ammo count proves nothing. "
					.. Census()
			end

			print("[defcon2-contact] settled. " .. Census())
			phase = "hold"
			return false
		end

		-- The level is not allowed to move under the run in either arm. In the Escalation arm a move
		-- to 1 means something was destroyed and the hold was lifted mid-measurement.
		if Test.DefconLevel() ~= openingLevel then
			return string.format(
				"fail: DEFCON moved %d -> %d during the run, so the phase the rest of this scenario "
				.. "asserts about is no longer the phase it is running in. %s",
				openingLevel, Test.DefconLevel(), Census())
		end

		-- ========== HOLD: nothing fires, for HOLD_TICKS ==========
		if phase == "hold" then
			local fired = NothingFired()
			if fired ~= nil then
				return fired
			end

			if ticks % 50 == 0 then
				print("[defcon2-contact] hold. " .. Census())
			end

			if ticks < SETTLE_TICKS + HOLD_TICKS then
				return false
			end

			-- THE ORDER. Through the real click resolver rather than through Actor.Attack, so the
			-- returned OrderString is itself an assertion: at DEFCON 3 AttackBase's targeter refuses
			-- the click outright (RefusedByDefconCeaseFire), and at 2 it must not.
			local issued = Test.ClickOrder(Probe, Contact)
			if issued ~= "Attack" then
				return "fail: the click onto the t90 produced " .. tostring(issued) .. " rather than "
					.. "Attack. DEFCON 2 is the rung that PERMITS an ordered shot; only the DEFCON 3 "
					.. "cease-fire refuses the order itself. " .. Census()
			end

			orderIssuedTick = ticks
			print("[defcon2-contact] hold complete, order issued. " .. Census())
			phase = "ordered"
			return false
		end

		-- ========== ORDERED: the shot somebody asked for must land ==========
		if phase == "ordered" then
			if Contact.Health < contactHp0 then
				damageTick = ticks

				if Ammo(Probe) >= probeAmmo0 then
					return "fail: the t90 lost health but the abrams' ammo did not move ("
						.. probeAmmo0 .. "), so whatever hit it was not the ordered shot. " .. Census()
				end

				print("[defcon2-contact] ordered shot landed. " .. Census())
				phase = "return"
				return false
			end

			if ticks >= orderIssuedTick + ORDER_TICKS then
				return string.format(
					"fail: the ORDERED attack never landed. The click was accepted as an Attack order "
					.. "at t=%d and %d ticks later the t90 is untouched. At DEFCON 2 a shot somebody "
					.. "ordered is exempt from the hold -- if this is the failure, the rule is "
					.. "refusing fire it is supposed to permit, which is a worse defect than the one "
					.. "this scenario was written for. %s", orderIssuedTick, ORDER_TICKS, Census())
			end

			return false
		end

		-- ========== RETURN: the tank that was shot does not shoot back ==========
		local held = ContactHeld()
		if held ~= nil then
			return held
		end

		if ticks < damageTick + RETURN_TICKS then
			return false
		end

		print("[defcon2-contact] PASS. " .. Census())
		return true
	end, function()
		return "fail: the predicate never reached a verdict inside its backstop deadline, which means "
			.. "it stopped advancing rather than that any phase overran -- every phase owns its own "
			.. "budget and its own reason. " .. Census()
	end)
end
