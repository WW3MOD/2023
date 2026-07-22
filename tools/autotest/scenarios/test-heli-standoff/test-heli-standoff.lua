-- AUTO TEST: attack-heli standoff / no overflight.
--
-- A Hover attack heli (Apache/HELI; Hellfire range 25c, 30mm range 18-20c) is
-- ordered to AttackMove to a cell FAR PAST a stationary enemy tank sitting on
-- the path. Correct standoff behaviour: AutoTarget picks the tank up at weapon
-- range, FlyAttack holds at missile standoff and fires — the heli must NOT bore
-- toward the distant destination and overfly the tank.
--
-- Lua's AttackMove maps to AttackMoveActivity, the exact activity the Stage-0
-- bot fix now queues for heli squads: HelicopterSquadBotModule@experimental
-- issues AttackMove (StandoffEngagement) instead of a bare Attack on a single
-- distant TargetActor, so squads engage the nearest threat at standoff rather
-- than flying over it. The bot FSM itself can't run in the single-human test
-- harness; this locks the underlying AttackMove standoff contract the fix rides
-- on, and would go RED if a plain Attack on the far destination were used
-- (the heli would cross the tank's line).
--
-- Geometry (row y=17): Apache start col 12, tank col 26 (14 cells east, inside
-- Hellfire range), AttackMove destination col 55 (29 cells past the tank).
--   Pass:  Apache fires (ammo drops) while still >= 8 cells short of the tank.
--   Fail:  Apache advances to within 5 cells of the tank (overflight), or dies.

local DeadlineSeconds = 20

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	if USA == nil then
		Test.Fail("USA player not found")
		return
	end

	local Apache = Actor.Create("heli", true, {
		Owner = USA,
		CenterPosition = cellPos(12, 17, 1280),
		Facing = Angle.East,
	})
	if Apache == nil then
		Test.Fail("could not spawn heli")
		return
	end

	TestHarness.FocusBetween(Apache, Target)
	TestHarness.Select(Apache)

	-- Tank holds fire so it can't damage the Apache (damage would trigger a
	-- flee/reposition and confound the geometry test).
	Target.Stance = "HoldFire"

	local tankCol = Target.Location.X
	local overflightCol = tankCol - 5   -- reaching within 5 cells = overflight
	local standoffCol = tankCol - 8     -- must fire from >= 8 cells short

	-- AttackMove PAST the tank (col 55). AttackMoveActivity + AutoTarget must
	-- engage the tank at standoff instead of flying to the distant destination.
	Apache.AttackMove(CPos.New(55, 17))

	local startPrimary = Apache.AmmoCount("primary-ammo")
	local startSecondary = Apache.AmmoCount("secondary-ammo")

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Apache.IsDead then
			return "fail: Apache died first"
		end

		if Apache.Location.X > overflightCol then
			return "fail: Apache overflew the tank (advanced within 5 cells of it)"
		end

		local fired = Apache.AmmoCount("primary-ammo") < startPrimary
			or Apache.AmmoCount("secondary-ammo") < startSecondary
		if fired and Apache.Location.X <= standoffCol then
			return true
		end

		return false
	end, "Apache did not engage the tank from standoff within " .. DeadlineSeconds .. "s")
end
