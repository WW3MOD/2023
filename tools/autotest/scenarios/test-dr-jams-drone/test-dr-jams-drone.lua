-- AUTO TEST: Drone operator (DR) infantry must autotarget an enemy
-- quadcopterdrone in range with its secondary DroneJammer armament.
--
-- Setup:
--   USA DR ("Operator") sits idle at (12,17), Stance FireAtWill.
--   Russian DR ("Enemy") at (35,17) is force-ordered to fire its
--   DroneTargeter at cell (22,17), which deploys a Russian-owned
--   quadcopterdrone. The drone flies toward (22,17) — passing ~10
--   cells in front of the USA DR, well inside the DroneJammer's
--   20c0 range.
--
-- The DroneJammer does 3 damage/shot to "Drone"-typed targets with
-- BurstWait 1, so even a single autotarget volley drops drone HP.
-- Pre-fix expectation (the bug from RELEASE_V1 Phase B "Drone fixes"):
-- USA DR sits and does nothing.
--
-- Pass: drone spawns AND its HP < starting HP within DeadlineSeconds.
-- Fail (bug): drone airborne but unscratched when deadline expires.

local DeadlineSeconds = 15

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("required players not found")
		return
	end

	local Operator = Actor.Create("dr", true, {
		Owner = USA,
		Location = CPos.New(12, 17),
		Facing = Angle.East,
	})

	local Enemy = Actor.Create("dr", true, {
		Owner = RUSSIA,
		Location = CPos.New(35, 17),
		Facing = Angle.West,
	})

	if Operator == nil or Enemy == nil then
		Test.Fail("could not spawn DR actors")
		return
	end

	-- Enemy DR holds fire so it doesn't take pot-shots at the USA DR with
	-- its own DroneJammer (would muddy the verdict).
	Enemy.Stance = "HoldFire"

	TestHarness.FocusBetween(Operator, Enemy)
	TestHarness.Select(Operator)

	-- Force-fire the enemy DroneTargeter at the midpoint cell. Per
	-- DR YAML, primary armament has FireDelay 50 and the drone is
	-- deployed via the carrier-master attacking-event path
	-- (CarrierMaster.Attacking → SpawnIntoWorld). The slave then
	-- moves to the target cell, passing within range of Operator.
	Enemy.AttackGround(CPos.New(22, 17), false, false)

	local startingHP = nil
	local droneSeen = false
	local Drone = nil

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Operator.IsDead then return "fail: USA DR died unexpectedly" end

		-- Wait for the drone to be launched. Find it via FindActorsInCircle
		-- centered between the two DRs.
		if Drone == nil or Drone.IsDead then
			local hits = Map.ActorsInBox(
				WPos.New(10 * 1024, 14 * 1024, 0),
				WPos.New(40 * 1024, 20 * 1024, 5000),
				function(a) return a.Type == "quadcopterdrone" and a.Owner == RUSSIA end)
			if #hits > 0 then
				Drone = hits[1]
				droneSeen = true
				startingHP = Drone.Health
			end
			return false
		end

		if startingHP == nil then return false end
		return Drone.Health < startingHP
	end, string.format("USA DR did not damage the enemy drone within %ds (droneSeen=%s)",
		DeadlineSeconds, tostring(droneSeen)))
end
