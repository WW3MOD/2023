-- AUTO TEST: ATGM lose-track-and-fly-slow-straight bug.
--
-- Repro: Bradley fires WGM, target dies mid-flight before the missile
-- detonates. Pre-fix the missile decelerated approaching the target
-- (Hitting-state speed control), then flyStraight latched in the slow
-- speed (HomingInnerTick is skipped while flyStraight, so ChangeSpeed
-- is never called again). Result: missile crawls in a straight line
-- and only detonates many tens of seconds later when fuel runs out at
-- 1 wdist/tick.
--
-- Post-fix: missile keeps accelerating to maxSpeed even when flying
-- straight, and target-invalid no longer freezes hFacing — so the
-- missile homes on its last-known target position at full speed and
-- detonates either at CloseEnough or fuel-out within ~3-4s.
--
-- Threshold: missile must be off the world within 8s of fire (vs.
-- hundreds of seconds with the bug). Map has no other Russian unit so
-- operator retargeting can't paper over the bug.

local DeadlineSeconds = 10
local State = "wait_fire"
local KillTick = -1
local FireTick = -1

WorldLoaded = function()
	TestHarness.FocusBetween(Bradley, Target)
	TestHarness.Select(Bradley)

	Target.Stance = "HoldFire"
	-- Filler conscript far away keeps the Russian faction alive after we
	-- kill the t90 (so the game doesn't trigger Mission Accomplished).
	-- Force HoldFire so it doesn't try to engage anything.
	Filler.Stance = "HoldFire"
	Bradley.Attack(Target, false, false)

	local startingAmmo = Bradley.AmmoCount("secondary-ammo")
	local deadlineTicks = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)
	local elapsed = 0

	local tick
	tick = function()
		elapsed = elapsed + 1

		if Bradley.IsDead then
			Test.Fail("Bradley died unexpectedly")
			return
		end

		if State == "wait_fire" then
			if Bradley.AmmoCount("secondary-ammo") < startingAmmo
				and Test.GetActiveMissileCount() > 0 then
				FireTick = elapsed
				-- Kill the target the moment we see the missile in the air.
				-- The missile must still detonate — either at the last-known
				-- position or by fuel-out — within the deadline.
				if not Target.IsDead then
					Target.Health = -100000
				end
				State = "wait_detonate"
				KillTick = elapsed
			end
		elseif State == "wait_detonate" then
			if Test.GetActiveMissileCount() == 0 then
				local sinceKill = (elapsed - KillTick) / TestHarness.TicksPerSecond
				Test.Pass(string.format(
					"missile detonated %.1fs after target killed (fired at %d, killed at %d, gone at %d)",
					sinceKill, FireTick, KillTick, elapsed))
				return
			end
		end

		if elapsed >= deadlineTicks then
			Test.Fail(string.format(
				"timeout: state=%s missiles=%d (slow-crawl bug?)",
				State, Test.GetActiveMissileCount()))
			return
		end

		Trigger.AfterDelay(1, tick)
	end

	Trigger.AfterDelay(1, tick)
end
