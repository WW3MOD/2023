-- SHARED BODY for the DEFCON 2 ambush-by-proxy pair:
--
--     tools/autotest/scenarios/test-defcon2-holdfire-ambush            (Escalation, opens at 2 -- PASS)
--     tools/autotest/scenarios/test-defcon2-holdfire-ambush-skirmish   (Skirmish             -- FAIL)
--
-- READ SITE 6 of 6 -- AutoTarget.TriggerNearbyAmbushAllies. It springs OTHER units by writing their
-- ambushTriggered latch directly, without consulting their stance and without touching their fire
-- path, so one shot ambusher can light off a whole lane. This scenario pulls it through the DAMAGE
-- trigger: INotifyDamage.Damaged sets the latch and calls the proxy when the victim is in Ambush
-- (AutoTarget.cs:709-713), and the whole handler returns at its FIRST LINE while the hold is on.
--
-- THE SHAPE OF THE RUN.
--   SETTLE    baselines, and the two attribution controls -- the bait must be DETECTED by USA and
--             all three allies must NOT be. Re-checked every tick thereafter.
--   TRIGGER   the abrams is CLICKED onto the bait (a shot somebody ordered is exempt from the hold
--             at either level, which is what lets both arms share this script). The bait must take
--             damage -- if it never does, the spring was never attempted and nothing below means
--             anything. That is the "a bug that cannot fire is indistinguishable from a bug that
--             does not exist" guard, and it is why the damage is asserted rather than assumed.
--   OBSERVE   OBSERVE_TICKS in which NO ALLY may fire. That is the verdict.
--
-- WHY ALLY AMMUNITION IS THE VERDICT AND THE BAIT'S IS NOT.
--
-- An ally fires only from AmbushTickIdle's ungated stock branch, `if (isSpotted || ambushTriggered)`.
-- `isSpotted` is self.CanBeViewedByPlayer(targetOwner) and the scenario's geometry holds it FALSE
-- for all three allies (rules.yaml raises their Detectable.Vision to 7; the probe stands where USA
-- reads strength 6). `ambushTriggered` on an ally is written by nothing on this map except the
-- proxy. So an ally that fires has been sprung by read site 6 and by nothing else -- which is the
-- whole reason for the fog and the vision numbers.
--
-- The BAIT, by contrast, is plainly visible and would spring on its own the moment it had a target,
-- so its ammunition cannot attribute anything. It is still checked -- return fire from the bait at
-- DEFCON 2 is read site 2 failing -- but only ONCE, at the END of the observation window, so that
-- the control arm always runs long enough to reach the ally verdict rather than tripping on the
-- bait a few ticks earlier and never demonstrating the proxy at all.
--
-- WHY THE LEVEL IS READ AND NOT ASSERTED TO BE 2: this body runs in both arms. Only 2 (the hold)
-- and 0 (Skirmish) are acceptable openings and the level must stay where it opened. 3 is the
-- cease-fire rung, where a different rule refuses the CLICK itself and the trigger could never be
-- delivered; 1 is open war.

local TicksPerSecond = TestHarness.TicksPerSecond

local HOLD_FIRE_LEVEL = 2
local SKIRMISH_LEVEL = 0

local SETTLE_TICKS = 20     -- the vision layers are rebuilt on tick; detection is not readable at 0
local TRIGGER_TICKS = 250   -- turret slew plus 12 cells of Bullet flight at Speed 1200
local OBSERVE_TICKS = 200   -- an ally rescans every 3-8 ticks and slews at most ~26; fourfold margin

local DEADLINE_TICKS = 1000
local DEADLINE_SECONDS = DEADLINE_TICKS / TicksPerSecond

WorldLoaded = function()
	local Allies = { Ally1, Ally2, Ally3 }

	-- Captured once, rather than read off an actor each time. The detection helpers below are
	-- called from inside failure messages that fire when an actor has just died, and reaching for
	-- a player through a possibly-disposed actor is exactly where that goes wrong.
	local USA = Player.GetPlayer("USA")
	if USA == nil then
		Test.Fail("SETUP -- no player named USA on this map, so detection cannot be read at all.")
		return
	end

	TestHarness.FocusBetween(Probe, Bait)
	TestHarness.Select(Probe)

	local ticks = 0
	local phase = "settle"

	local openingLevel = nil
	local allyAmmo0 = {}
	-- BOTH of the bmp2's pools, and that is not belt-and-braces. Its primary is 30mm.BMP2
	-- (ValidTargets: Infantry, Vehicle, Defense) and its secondary is WGM (Vehicle, Defense), so the
	-- abrams is a legal target for EITHER -- a retaliation spent entirely out of `secondary-ammo`
	-- would leave `primary-ammo` untouched and a single-pool check would report the bait as having
	-- held. Actor.AmmoCount THROWS a LuaException on an unknown pool name, so naming a pool an actor
	-- does not have is loud rather than silent; both of these exist on bmp2
	-- (vehicles-russia.yaml AmmoPool@1 / AmmoPool@2).
	local baitAmmo0, baitAmmo2_0 = 0, 0
	local baitHp0 = 0
	local damageTick = 0
	local orderIssuedTick = 0

	local function Ammo(a)
		return a.AmmoCount("primary-ammo")
	end

	local function BaitSecondary()
		return Bait.AmmoCount("secondary-ammo")
	end

	local function AllyTrace()
		local parts = {}
		for i, a in ipairs(Allies) do
			if a.IsDead then
				parts[i] = "dead"
			else
				parts[i] = string.format("%d->%d seen=%s", allyAmmo0[i], Ammo(a),
					tostring(Test.IsDetectedBy(a, USA)))
			end
		end

		return table.concat(parts, " | ")
	end

	-- Live. Never interpolated into the AssertWithin timeout argument, which is built once at
	-- registration and would report tick-0 values forever (AUTOTEST.md, "The failure message is
	-- evaluated EAGERLY").
	local function Census()
		return string.format(
			"t=%d phase=%s defcon=%d(opened %s) | bait ammo %d->%d/%d->%d hp %d/%d seen=%s act %s | "
			.. "allies [%s] | probe hp %d/%d act %s",
			ticks, phase, Test.DefconLevel(), tostring(openingLevel),
			baitAmmo0, Bait.IsDead and -1 or Ammo(Bait),
			baitAmmo2_0, Bait.IsDead and -1 or BaitSecondary(),
			Bait.IsDead and 0 or Bait.Health, Bait.MaxHealth,
			Bait.IsDead and "(dead)" or tostring(Test.IsDetectedBy(Bait, USA)),
			Bait.IsDead and "(dead)" or Test.ActivityChain(Bait),
			AllyTrace(),
			Probe.IsDead and 0 or Probe.Health, Probe.MaxHealth,
			Probe.IsDead and "(dead)" or Test.ActivityChain(Probe))
	end

	-- THE ATTRIBUTION CONTROL, re-read every tick rather than once. An ally that becomes visible to
	-- USA mid-run could spring itself through `isSpotted`, and the run would no longer be able to
	-- blame read site 6 for anything -- in either direction.
	local function AttributionHolds()
		if not Test.IsDetectedBy(Bait, USA) then
			return "fail: ATTRIBUTION -- USA cannot see the bait, so the abrams cannot be clicked "
				.. "onto it and the damage trigger can never be delivered. Its Detectable.Vision is "
				.. "the ^Vehicle default 2 (+1 stationary) against strength 7 at twelve cells, so "
				.. "this should not be reachable. " .. Census()
		end

		for i, a in ipairs(Allies) do
			if not a.IsDead and Test.IsDetectedBy(a, USA) then
				return "fail: ATTRIBUTION -- ally " .. i .. " is VISIBLE to USA, so "
					.. "AmbushTickIdle's `isSpotted` term is live for it and it could spring "
					.. "itself without the proxy ever running. The verdict below would then be "
					.. "about detection rather than about read site 6. rules.yaml raises the t90s' "
					.. "Detectable.Vision to 7 precisely to stop this; check the geometry has not "
					.. "moved. " .. Census()
			end
		end

		return nil
	end

	local function AllyFired()
		for i, a in ipairs(Allies) do
			if not a.IsDead and Ammo(a) < allyAmmo0[i] then
				return "fail: ally " .. i .. " OPENED FIRE without an order: primary-ammo "
					.. allyAmmo0[i] .. " -> " .. Ammo(a) .. ". It is undetectable to USA, so "
					.. "AmbushTickIdle's `isSpotted` term is false for it and the only thing on "
					.. "this map that can have set its ambushTriggered latch is "
					.. "AutoTarget.TriggerNearbyAmbushAllies -- read site 6, sprung by the bait "
					.. "being shot. " .. Census()
			end
		end

		return nil
	end

	TestHarness.AssertWithin(DEADLINE_SECONDS, function()
		ticks = ticks + 1

		if Probe.IsDead or Bait.IsDead then
			return "fail: the abrams or the bait died. Both have their HP raised tenfold in "
				.. "rules.yaml precisely so they cannot, and a death also drops DEFCON 2 to 1 and "
				.. "un-holds every gun on the map. " .. Census()
		end

		for i, a in ipairs(Allies) do
			if a.IsDead then
				return "fail: ally " .. i .. " died. Nothing on this map should be able to do that. "
					.. Census()
			end
		end

		-- ========== SETTLE ==========
		if phase == "settle" then
			if ticks < SETTLE_TICKS then
				return false
			end

			local level = Test.DefconLevel()
			if level ~= HOLD_FIRE_LEVEL and level ~= SKIRMISH_LEVEL then
				return string.format(
					"fail: SETUP -- the match opened at DEFCON %d. Only %d (the hold-fire rung, this "
					.. "directory) and %d (Skirmish, the -skirmish twin) are arms of this pair. 3 is "
					.. "the cease-fire rung, where the CLICK itself is refused and the trigger could "
					.. "never be delivered.", level, HOLD_FIRE_LEVEL, SKIRMISH_LEVEL)
			end

			openingLevel = level

			baitAmmo0 = Ammo(Bait)
			baitAmmo2_0 = BaitSecondary()
			baitHp0 = Bait.Health
			for i, a in ipairs(Allies) do
				allyAmmo0[i] = Ammo(a)
				if allyAmmo0[i] <= 0 then
					return "fail: SETUP -- ally " .. i .. " opened with no ammunition, so an "
						.. "unchanged ammo count proves nothing about it. " .. Census()
				end
			end

			local broken = AttributionHolds()
			if broken ~= nil then
				return broken
			end

			-- The allies must be ABLE to shoot the probe, or their silence is worth nothing. This
			-- is the attribution control's other half: undetectable AND in range.
			for i, a in ipairs(Allies) do
				local dx = Probe.Location.X - a.Location.X
				local dy = Probe.Location.Y - a.Location.Y
				local sep = math.sqrt(dx * dx + dy * dy)
				if sep > 24 or sep < 2 then
					return string.format(
						"fail: SETUP -- ally %d is %.1f cells from the abrams. TankRound.T90 is "
						.. "Range 24c0 / MinRange 1c512, so an ally outside that band could not "
						.. "have fired even if the proxy had sprung it. %s", i, sep, Census())
				end

				local db = math.sqrt((Bait.Location.X - a.Location.X) ^ 2
					+ (Bait.Location.Y - a.Location.Y) ^ 2)
				if db > 10 then
					return string.format(
						"fail: SETUP -- ally %d is %.1f cells from the bait, outside "
						.. "AutoTarget.AmbushCoordinationRadius (10). The proxy could not reach it, "
						.. "so its silence would say nothing about read site 6. %s", i, db, Census())
				end
			end

			local offered = Test.GetTargetOrder(Probe, Bait)
			if offered ~= "Attack" then
				return "fail: SETUP -- a right-click from the abrams onto the bait resolves to "
					.. tostring(offered) .. ", not Attack, so the damage trigger cannot be "
					.. "delivered. " .. Census()
			end

			local issued = Test.ClickOrder(Probe, Bait)
			if issued ~= "Attack" then
				return "fail: the click onto the bait produced " .. tostring(issued) .. " rather "
					.. "than Attack. A shot somebody ORDERED is exempt from the DEFCON 2 hold; only "
					.. "the DEFCON 3 cease-fire refuses the order itself. " .. Census()
			end

			orderIssuedTick = ticks
			print("[defcon2-ambush] settled, abrams clicked onto the bait. " .. Census())
			phase = "trigger"
			return false
		end

		if Test.DefconLevel() ~= openingLevel then
			return string.format(
				"fail: DEFCON moved %d -> %d during the run, so the phase the rest of this scenario "
				.. "asserts about is no longer the phase it is running in. %s",
				openingLevel, Test.DefconLevel(), Census())
		end

		local broken = AttributionHolds()
		if broken ~= nil then
			return broken
		end

		-- No ally may fire at any point after the order goes out, in either phase.
		local fired = AllyFired()
		if fired ~= nil then
			return fired
		end

		-- ========== TRIGGER: the bait must actually be hit ==========
		if phase == "trigger" then
			if Bait.Health < baitHp0 then
				damageTick = ticks
				print("[defcon2-ambush] bait hit, ambush trigger delivered. " .. Census())
				phase = "observe"
				return false
			end

			if ticks >= orderIssuedTick + TRIGGER_TICKS then
				return string.format(
					"fail: the ORDERED shot never hit the bait in %d ticks, so the ambush trigger "
					.. "was never delivered and the silence below measures nothing at all. A bug "
					.. "that cannot fire is indistinguishable from a bug that does not exist -- "
					.. "this is that case, and it is a scenario fault rather than a finding. %s",
					TRIGGER_TICKS, Census())
			end

			return false
		end

		-- ========== OBSERVE ==========
		if ticks % 50 == 0 then
			print("[defcon2-ambush] observing. " .. Census())
		end

		if ticks < damageTick + OBSERVE_TICKS then
			return false
		end

		-- ONCE, at the end: the bait itself must not have returned fire either. This is read site 2
		-- again rather than read site 6, so it is deliberately not allowed to end the run early --
		-- the control arm has to reach the ally verdict above before anything else can trip.
		if Ammo(Bait) < baitAmmo0 or BaitSecondary() < baitAmmo2_0 then
			return "fail: the BAIT returned fire (primary-ammo " .. baitAmmo0 .. " -> "
				.. Ammo(Bait) .. ", secondary-ammo " .. baitAmmo2_0 .. " -> " .. BaitSecondary()
				.. "). The allies held, so read site 6 did its job, but "
				.. "AutoTarget.INotifyDamage.Damaged is supposed to return at its FIRST LINE while "
				.. "the hold is on -- this is read site 2 failing. " .. Census()
		end

		print("[defcon2-ambush] PASS. " .. Census())
		return true
	end, function()
		return "fail: the predicate never reached a verdict inside its backstop deadline, which means "
			.. "it stopped advancing rather than that any phase overran -- every phase owns its own "
			.. "budget and its own reason. " .. Census()
	end)
end
