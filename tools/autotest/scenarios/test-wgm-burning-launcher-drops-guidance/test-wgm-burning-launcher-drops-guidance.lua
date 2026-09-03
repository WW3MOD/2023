-- AUTO TEST: a wire-guided missile loses tracking while its launcher is BURNING.
--
-- The rule under test (Missile.GuidanceLost, engine/OpenRA.Mods.Common/Projectiles/Missile.cs):
-- a missile whose weapon sets ManualGuidance stops being steered once its launcher reaches
-- DamageState.Heavy or worse -- HP below 50%, the `heavy-damage-attained` band -- and not only
-- when the launcher dies. The fiction is that the crew is bailing out, which the mod already
-- models at exactly that line (VehicleCrew.EjectionDamageState defaults to Heavy).
--
-- VOCABULARY: "critically damaged" here means DamageState.Heavy, NOT the `critical-damage`
-- condition, which is a separate 25% marker. This scenario crosses the 50% line and stops well
-- above 25%, so a build that keyed the drop on `critical-damage` instead would FAIL the test lane
-- (its missile would still track and hit). That is deliberate -- it is the discriminator between
-- the two candidate thresholds.
--
-- WHY THE TARGETS MOVE: losing guidance makes the missile fly BALLISTICALLY ON (Missile.FreefallTick
-- keeps the current velocity and adds gravity) -- it is not removed and it does not stop. Against a
-- STATIONARY target a ballistic missile already pointed at it would still land on top of it and the
-- test would not discriminate at all. Against a target moving across the missile's path, dropping
-- guidance is immediately visible as a miss. This is the whole reason both t90s are ordered to
-- drive perpendicular to the missile's flight before either Bradley fires.
--
-- WHAT COUNTS AS PASS:
--   Control lane (healthy Bradley) -- min_dist <= close_enough, i.e. the missile closed to
--     detonation range of the moving t90. This proves the rig can hit a moving target at all;
--     without it, "the test lane missed" would be worth nothing.
--   Test lane (Bradley crippled to Heavy mid-flight) -- ALL of:
--     * min_dist > close_enough        (never came within detonation range of the target)
--     * damage_to_target == 0          (the t90 it was launched at took nothing from it)
--     * outcome == "detonated"         (it flew on and ended somewhere -- it did NOT vanish,
--                                       and it was not a dud removed before arming)
--     * reason is NOT close_enough / segment_closest  (it did not end by reaching its target)
--     * the Bradley is STILL ALIVE when the missile ends
--
-- That last clause is the one that makes this test about the new behaviour rather than the old one.
-- Losing guidance on launcher DEATH already shipped. If the crippled Bradley bled out before its
-- missile landed, the lane would pass via the pre-existing path and prove nothing. 45% HP is chosen
-- to keep it alive: ChangesHealth bleeds 1% per 5 ticks, so 45% is ~225 ticks from death while the
-- remaining flight is on the order of 75, and the guard below fails loudly if that ever stops holding.
--
-- THRESHOLDS ARE DERIVED, NOT GUESSED: every comparison is against `close_enough`, which is read
-- out of the missile's own trace record (Missile.CloseEnough for this weapon). No distance constant
-- is hard-coded in this file.

local CRIPPLE_HEALTH_PERCENT = 45   -- inside Heavy (25-50), clear of Critical (<25) at both ends
local CRIPPLE_DELAY_TICKS = 10      -- after the missile is seen airborne; Arm is 2 ticks, so it is armed
local FIRE_DEADLINE_TICKS = 500     -- turret turn + AimingDelay 50 + autotarget scan interval
local FLIGHT_DEADLINE_TICKS = 500   -- 22 cells at Speed 300 is ~75 ticks; fuel-out bounds the rest
local OVERALL_DEADLINE_TICKS = 2500

local elapsed = 0
local state = "control_fire"
local phaseStart = 0
local controlRec, testRec = nil, nil
local recordsBefore = 0
local airborneTick = -1
local crippledAtHealth = -1
local testLauncherAliveAtEnd = nil

local function pct(actor)
	return math.floor(actor.Health * 100 / actor.MaxHealth)
end

local function describe(tag, r)
	return string.format(
		"%s: min_dist=%d close_enough=%d damage_to_target=%d outcome=%s reason=%s end_tick=%d",
		tag, r.min_dist, r.close_enough, r.damage_to_target,
		tostring(r.outcome), tostring(r.reason), r.end_tick)
end

WorldLoaded = function()
	-- Must be switched on before anything fires: missiles already in flight are not retro-tracked.
	-- Explicit args rather than leaning on optional-parameter binding. tickRecords=false keeps
	-- only the one summary record per missile, which is all the assertions below read.
	Test.EnableMissileTrace("", false)
	if not Test.IsMissileTraceEnabled() then
		Test.Fail("MissileTrace did not switch on — every assertion in this scenario reads a trace record")
		return
	end

	TestHarness.FocusBetween(BradleyControl, TargetTest)
	TestHarness.Select(BradleyControl)

	-- Silence the ENEMY only, never the unit under test (AUTOTEST.md gotcha 7).
	TargetControl.Stance = "HoldFire"
	TargetTest.Stance = "HoldFire"

	local tick
	tick = function()
		elapsed = elapsed + 1

		if elapsed >= OVERALL_DEADLINE_TICKS then
			Test.Fail(string.format("overall timeout at state=%s (%d ticks)", state, elapsed))
			return
		end

		-- ---------------- CONTROL LANE: healthy launcher, missile must track ----------------
		if state == "control_fire" then
			-- Drive the target across the missile's path, then shoot at it.
			Test.IssueMoveOrder(TargetControl, CPos.New(34, 2), false)
			BradleyControl.Attack(TargetControl, false, false)
			recordsBefore = Test.GetMissileRecordCount()
			phaseStart = elapsed
			state = "control_wait_gone"

		elseif state == "control_wait_gone" then
			if Test.GetMissileRecordCount() > recordsBefore then
				if Test.GetActiveMissileCount() == 0 then
					controlRec = Test.GetMissileRecord(Test.GetMissileRecordCount())
					state = "test_fire"
				end
			elseif elapsed - phaseStart > FIRE_DEADLINE_TICKS + FLIGHT_DEADLINE_TICKS then
				Test.Fail(string.format(
					"control lane produced no missile record within %d ticks (ammo=%d, airborne=%d)",
					FIRE_DEADLINE_TICKS + FLIGHT_DEADLINE_TICKS,
					BradleyControl.AmmoCount("secondary-ammo"), Test.GetActiveMissileCount()))
				return
			end

		-- ---------------- TEST LANE: launcher crippled mid-flight ----------------
		elseif state == "test_fire" then
			Test.IssueMoveOrder(TargetTest, CPos.New(34, 31), false)
			BradleyTest.Attack(TargetTest, false, false)
			recordsBefore = Test.GetMissileRecordCount()
			phaseStart = elapsed
			state = "test_wait_airborne"

		elseif state == "test_wait_airborne" then
			if Test.GetActiveMissileCount() > 0 then
				airborneTick = elapsed
				state = "test_wait_cripple"
			elseif elapsed - phaseStart > FIRE_DEADLINE_TICKS then
				Test.Fail(string.format(
					"test lane never got a missile airborne within %d ticks (ammo=%d)",
					FIRE_DEADLINE_TICKS, BradleyTest.AmmoCount("secondary-ammo")))
				return
			end

		elseif state == "test_wait_cripple" then
			if elapsed - airborneTick >= CRIPPLE_DELAY_TICKS then
				if Test.GetActiveMissileCount() == 0 then
					Test.Fail("test missile ended before the launcher could be crippled — " ..
						"CRIPPLE_DELAY_TICKS is too long for this geometry")
					return
				end

				-- Cross the 50% line. Goes through InflictDamage, so the damage-state
				-- notification, the fire ramp and the crew bail-out all run for real.
				BradleyTest.Health = math.floor(BradleyTest.MaxHealth * CRIPPLE_HEALTH_PERCENT / 100)
				crippledAtHealth = pct(BradleyTest)

				if crippledAtHealth >= 50 or crippledAtHealth < 25 then
					Test.Fail(string.format(
						"launcher landed at %d%% HP — needs to be inside Heavy (25-50%%) for this test " ..
						"to mean what it claims", crippledAtHealth))
					return
				end

				state = "test_wait_gone"
			end

		elseif state == "test_wait_gone" then
			-- Sampled every tick so we know whether the launcher was alive while the missile flew,
			-- not merely at the moment we happen to read the record.
			if BradleyTest.IsDead and testLauncherAliveAtEnd == nil then
				testLauncherAliveAtEnd = false
			end

			if Test.GetMissileRecordCount() > recordsBefore then
				if Test.GetActiveMissileCount() == 0 then
					if testLauncherAliveAtEnd == nil then
						testLauncherAliveAtEnd = not BradleyTest.IsDead
					end

					testRec = Test.GetMissileRecord(Test.GetMissileRecordCount())
					state = "verdict"
				end
			elseif elapsed - phaseStart > FIRE_DEADLINE_TICKS + FLIGHT_DEADLINE_TICKS then
				Test.Fail(string.format(
					"test lane produced no missile record within %d ticks",
					FIRE_DEADLINE_TICKS + FLIGHT_DEADLINE_TICKS))
				return
			end

		-- ---------------- VERDICT ----------------
		elseif state == "verdict" then
			-- The guard that keeps this test about BURNING rather than the already-shipped
			-- launcher-death path.
			if testLauncherAliveAtEnd == false then
				Test.Fail("test launcher DIED before its missile ended — the lane would have passed " ..
					"via the pre-existing dead-shooter rule and proves nothing about burning. " ..
					"Raise CRIPPLE_HEALTH_PERCENT.")
				return
			end

			if controlRec.min_dist < 0 or testRec.min_dist < 0 then
				Test.Fail("a trace record carries no min_dist — " ..
					describe("control", controlRec) .. " | " .. describe("test", testRec))
				return
			end

			-- Control: guidance intact, so the missile reached detonation range of a MOVING target.
			if controlRec.min_dist > controlRec.close_enough then
				Test.Fail("control lane MISSED, so the rig cannot hit a moving target and the test " ..
					"lane's miss is not evidence — " .. describe("control", controlRec))
				return
			end

			-- Test: guidance dropped, so the missile flew on and missed.
			if testRec.min_dist <= testRec.close_enough then
				Test.Fail("test missile still tracked the moving target while the launcher was " ..
					"burning at " .. crippledAtHealth .. "% HP — " .. describe("test", testRec))
				return
			end

			if testRec.damage_to_target ~= 0 then
				Test.Fail("test missile damaged the target it was launched at — " ..
					describe("test", testRec))
				return
			end

			if testRec.reason == "close_enough" or testRec.reason == "segment_closest" then
				Test.Fail("test missile ended by REACHING its target — guidance was not dropped — " ..
					describe("test", testRec))
				return
			end

			-- It must fly on and end somewhere, not vanish.
			if testRec.outcome ~= "detonated" then
				Test.Fail("test missile did not fly on to a real detonation (outcome=" ..
					tostring(testRec.outcome) .. ") — losing guidance must not remove the missile — " ..
					describe("test", testRec))
				return
			end

			Test.Pass(string.format(
				"burning launcher (%d%% HP, alive) dropped guidance: test min_dist=%d vs close_enough=%d " ..
				"(reason=%s, damage_to_target=0); healthy control hit at min_dist=%d",
				crippledAtHealth, testRec.min_dist, testRec.close_enough,
				tostring(testRec.reason), controlRec.min_dist))
			return
		end

		Trigger.AfterDelay(1, tick)
	end

	Trigger.AfterDelay(1, tick)
end
