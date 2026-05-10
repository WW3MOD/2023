-- AUTO TEST: Heli→heli missile silent-vanish bug.
--
-- Repro: Apache (HELI) fires Hellfire at an airborne Mi-28 (MI28). The
-- bug: the missile detonates near the target but the player perceives it
-- as "silently vanishing" — no kill, no visible damage.
--
-- Actual root cause (260510 investigation): heli HitShape is Circle
-- Radius 32 wdist, very small. Hellfire's PerCellIncrement Inaccuracy 16
-- puts a 22-cell shot ~100 wdist off centre on average, well outside
-- TargetDamage's tight default 1-wdist Spread. The missile flies on its
-- offset trajectory, detonates at line 1051 (relTarDist < CloseEnough)
-- ~|offset| wdist from the heli, and only the SpreadDamage falloff
-- applies. With default Penetration 1 vs Heavy heli Thickness 20 that
-- damage is divided by 20 — single-digit damage per shot, perceived as
-- "missile vanished".
--
-- Fix combo (260510):
--   1. Engine: mid-tick segment-target proximity check so fast missiles
--      (Speed 500 > CloseEnough 298) don't fly past target between ticks.
--   2. Engine: gate airburst trigger on !flyStraight (per the original
--      diagnosis — prevents bogus low-altitude detonations once the
--      missile has already overshot).
--   3. YAML: Hellfire SpreadDamage Penetration 1→20 so the falloff isn't
--      crippled by Heavy heli armor.
--
-- Test design: spawn Apache + Mi-28 both airborne, ~22 cells apart.
-- Mi-28 is set HoldFire so it doesn't kill Apache first. Apache attacks.
-- After the first missile lands, require meaningful per-shot damage —
-- pre-fix yields 5-50 damage (graze), post-fix should yield 500+ (real
-- hit). Threshold set conservatively at 200 damage.
--
-- Pass: per-shot damage >= 200 OR Mi-28 dies within the deadline.
-- Fail (bug): per-shot damage < 200 (silent graze).

local PerShotMinDamage = 200

local DeadlineSeconds = 30
local State = "wait_fire"
local FireTick = -1
local Apache = nil
local Target = nil
local TargetStartHP = 0

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("required players not found")
		return
	end

	-- Cruising altitude 1280 matches what other heli scenarios use; both
	-- helis sit at the same Z so any altitude-related bugs surface as
	-- "missile passes target horizontally without exploding".
	Apache = Actor.Create("heli", true, {
		Owner = USA,
		CenterPosition = cellPos(12, 17, 1280),
		Facing = Angle.East,
	})
	Target = Actor.Create("mi28", true, {
		Owner = RUSSIA,
		CenterPosition = cellPos(34, 17, 1280),
		Facing = Angle.West,
	})

	if Apache == nil or Target == nil then
		Test.Fail("could not spawn helis (heli/mi28)")
		return
	end

	TestHarness.FocusBetween(Apache, Target)
	TestHarness.Select(Apache)

	-- Mi-28 holds fire so it can't kill the Apache first; it stays in the
	-- air doing nothing.
	Target.Stance = "HoldFire"
	TargetStartHP = Target.Health

	-- Force-attack so the Apache fires Hellfire (secondary). allowMove=false
	-- keeps the Apache hovering; force=true bypasses ammo-only auto checks.
	Apache.Attack(Target, false, true)

	local startingAmmo = Apache.AmmoCount("secondary-ammo")
	local deadlineTicks = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)
	local elapsed = 0

	local tick
	tick = function()
		elapsed = elapsed + 1

		if Apache.IsDead then
			Test.Fail("Apache died unexpectedly (Mi-28 was meant to hold fire)")
			return
		end

		if Target.IsDead then
			Test.Pass(string.format("Mi-28 destroyed at tick %d", elapsed))
			return
		end

		if State == "wait_fire" then
			if Apache.AmmoCount("secondary-ammo") < startingAmmo
				and Test.GetActiveMissileCount() > 0 then
				FireTick = elapsed
				State = "wait_detonate"
			end
		elseif State == "wait_detonate" then
			if Test.GetActiveMissileCount() == 0 then
				-- Missile is gone. How much damage did the target take?
				local dmg = TargetStartHP - Target.Health
				if dmg >= PerShotMinDamage then
					Test.Pass(string.format(
						"Mi-28 took %d damage on first missile (start %d → now %d)",
						dmg, TargetStartHP, Target.Health))
					return
				end

				Test.Fail(string.format(
					"silent vanish: 1st missile did %d damage (need >= %d). " ..
					"Mi-28 HP %d/%d. Pre-fix typical graze is 5-50 damage.",
					dmg, PerShotMinDamage, Target.Health, TargetStartHP))
				return
			end
		end

		if elapsed >= deadlineTicks then
			Test.Fail(string.format(
				"timeout (state=%s) — fired=%d, missiles aloft=%d, target HP %d/%d",
				State,
				startingAmmo - Apache.AmmoCount("secondary-ammo"),
				Test.GetActiveMissileCount(),
				Target.Health, TargetStartHP))
			return
		end

		Trigger.AfterDelay(1, tick)
	end

	Trigger.AfterDelay(1, tick)
end
