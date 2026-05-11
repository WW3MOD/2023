-- AUTO TEST: Mi-28 fires its Ataka ATGM at a t90 and damages it.
--
-- Verifies the asymmetric ATGM split (260511): Mi-28 swapped from
-- Hellfire to the new Ataka weapon (SACLOS, range-scaled scatter,
-- tandem HEAT Pen 900). Apache regression covered by
-- test-heli-vs-heli-missile.
--
-- Setup: Mi-28 spawns airborne at cell 12,17. Stationary t90 at 30,17.
-- Distance 18 cells — inside Ataka Range 22c, outside MinRange 3c.
-- Force-attack so the Mi-28 commits its secondary armament (Ataka).
--
-- Pass: secondary-ammo decrements (missile fired) AND the t90 takes
-- >= 2000 damage on impact. Ataka TargetDamage Damage 10000 with
-- Penetration 900 vs t90's heavy armor should easily clear the
-- threshold; the test is checking that the missile arrived, not that
-- damage scales any particular way.
--
-- Fail: timeout without firing, OR target untouched after impact
-- (would indicate the missile mis-rendered or used wrong projectile).

local DeadlineSeconds = 15
local MinTargetDamage = 2000

local State = "wait_fire"
local Mi28 = nil
local Target = nil
local TargetStartHP = 0

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

WorldLoaded = function()
	local RUSSIA = Player.GetPlayer("Russia")
	local USA = Player.GetPlayer("USA")
	if RUSSIA == nil or USA == nil then
		Test.Fail("required players not found")
		return
	end

	Mi28 = Actor.Create("mi28", true, {
		Owner = RUSSIA,
		CenterPosition = cellPos(12, 17, 1280),
		Facing = Angle.East,
	})

	if Mi28 == nil then
		Test.Fail("could not spawn Mi-28 (mi28)")
		return
	end

	-- Find the t90 placed by map.yaml.
	for _, a in ipairs(USA.GetActors()) do
		if a.Type == "t90" then
			Target = a
			break
		end
	end

	if Target == nil then
		Test.Fail("could not find t90 target placed by map.yaml")
		return
	end

	TestHarness.FocusBetween(Mi28, Target)
	TestHarness.Select(Mi28)

	-- Target sits still — don't run away or kill the Mi-28 first.
	Target.Stance = "HoldFire"
	TargetStartHP = Target.Health

	-- Force-attack: secondary (Ataka) is the only weapon in range at 18c.
	-- 30mm.Heli range is 18c0 too — to make sure we exercise the secondary
	-- specifically, we check secondary-ammo decrement below.
	Mi28.Attack(Target, false, true)

	local startingAmmo = Mi28.AmmoCount("secondary-ammo")
	local deadlineTicks = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)
	local elapsed = 0

	local tick
	tick = function()
		elapsed = elapsed + 1

		if Mi28.IsDead then
			Test.Fail("Mi-28 died unexpectedly (t90 was HoldFire)")
			return
		end

		if Target.IsDead then
			Test.Pass(string.format("t90 destroyed at tick %d", elapsed))
			return
		end

		if State == "wait_fire" then
			if Mi28.AmmoCount("secondary-ammo") < startingAmmo
				and Test.GetActiveMissileCount() > 0 then
				State = "wait_impact"
			end
		elseif State == "wait_impact" then
			-- Wait for the missile to land or expire.
			if Test.GetActiveMissileCount() == 0 then
				local dmg = TargetStartHP - Target.Health
				if dmg >= MinTargetDamage then
					Test.Pass(string.format(
						"Ataka impact: t90 took %d damage (start %d → %d)",
						dmg, TargetStartHP, Target.Health))
					return
				end
				Test.Fail(string.format(
					"Ataka fired but t90 took only %d damage (need >= %d). " ..
					"Likely scatter miss or wrong projectile wiring.",
					dmg, MinTargetDamage))
				return
			end
		end

		if elapsed >= deadlineTicks then
			Test.Fail(string.format(
				"timeout (state=%s) — fired=%d, missiles aloft=%d, t90 HP %d/%d",
				State,
				startingAmmo - Mi28.AmmoCount("secondary-ammo"),
				Test.GetActiveMissileCount(),
				Target.Health, TargetStartHP))
			return
		end

		Trigger.AfterDelay(1, tick)
	end

	Trigger.AfterDelay(1, tick)
end
