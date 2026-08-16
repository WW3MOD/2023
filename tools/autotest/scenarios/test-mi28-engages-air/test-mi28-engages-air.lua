-- AUTO TEST: does the Mi-28 actually FIRE at an airborne target?
--
-- Air twin of test-mi28-fires-ataka, which covers the same unit's ground shot.
-- Regression cover for the defect fixed 260817: MI28's AttackAircraft named a
-- `secondary-air` armament that was never declared, and neither remaining weapon
-- could reach anything airborne (30mm.Heli is ValidTargets: Ground, Ataka is
-- Vehicle, Defense; an airborne helicopter is Air/AirDetonateAttack/Helicopter).
-- The unit flew, acquired nothing and fired nothing, while its build tooltip read
-- "Can engage aircraft".
--
-- WHY THIS IS A SEPARATE SCENARIO AND NOT AN ASSERTION ADDED TO
-- test-balance-heli-1v1.
-- That scenario spawns the two helicopters exactly 22 cells apart. 22c0 is exactly
-- Ataka.AA's Range, and — with ScanRadius unpinned — exactly the Mi-28's derived
-- AutoTarget scan radius too, since AutoTarget falls back to
-- AttackBase.GetMaximumRange(). A fired-assertion there would be decided on that
-- boundary, so its colour would turn on inclusive-vs-exclusive range semantics and
-- on hover phase rather than on whether the armament exists. It would also be
-- racing the duel: Hellfire crosses 22c0 in ~51 ticks against Ataka.AA's ~61, so
-- the Mi-28 is dead at ~48 ticks and anything asserted there is bounded by how
-- long it survives. A flaky assertion in a shared balance test is worse than none.
-- Asked on its own terms, the question has neither problem.
--
-- WHAT MAKES THIS ONE ANSWERABLE
--  * 12 cells apart. Well inside Ataka.AA's 22c0 and clear of its 3c0 MinRange, so
--    neither the scan radius nor the weapon range is anywhere near marginal.
--    (test-mi28-fires-ataka picks 18c for the same reason on the ground side.)
--  * The Apache holds fire, so the Mi-28 is never on a clock. Silencing the TARGET
--    rather than the unit under test is AUTOTEST.md gotcha 7 — a stance set on the
--    subject can gate the very trait being measured.
--  * Force-attack AND AutoTarget both left available. Neither can rescue a broken
--    build: ChooseArmamentsForTarget (AttackBase.cs:448-452) applies
--    `a.Weapon.IsValidAgainst(target)` unconditionally, and `forceAttack` relaxes
--    only RequiresForceFire and the relationship filter — never ValidTargets. So a
--    force order cannot make an air-invalid weapon fire.
--
-- WHY THE OBSERVABLE IS ATTRIBUTABLE
-- secondary-ammo is fed by both `secondary` (Ataka) and `secondary-air`
-- (Ataka.AA). Ataka cannot target an airborne actor, and the only enemy on this map
-- IS airborne, so here a drop in that pool can only have come from Ataka.AA.
-- primary-ammo cannot move either — 30mm.Heli is ground-only. Checked against the
-- resolved ruleset rather than assumed: with Armament@2_Air present, exactly one
-- armament is valid against an airborne target (secondary-air → Ataka.AA); with it
-- removed, none are, so ChooseArmamentsForTarget returns empty and no shot is
-- possible by any path.
--
-- PASS = secondary-ammo decremented. FAIL = it never did (timeout), or the Mi-28
-- died, which it cannot do while the Apache holds fire and therefore means the
-- setup did not take. The Apache's own fate is reported in the note and is never a
-- failure condition: Ataka.AA one-shots an 800hp airframe, so the target dying is
-- the expected consequence of a pass.

local DeadlineSeconds = 20

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

	-- Altitude 1280 mirrors test-balance-heli-1v1 and test-mi28-fires-ataka.
	local Havoc = Actor.Create("mi28", true, {
		Owner = RUSSIA,
		CenterPosition = cellPos(20, 17, 1280),
		Facing = Angle.East,
	})
	local Apache = Actor.Create("heli", true, {
		Owner = USA,
		CenterPosition = cellPos(32, 17, 1280),
		Facing = Angle.West,
	})

	if Havoc == nil or Apache == nil then
		Test.Fail("setup: could not spawn helis (mi28/heli)")
		return
	end

	-- The Apache is on the Playable slot, so AutoTarget.Created applies this
	-- machine's persisted unit-defaults.yaml stance to it. Setting the stance here
	-- runs after Created and therefore wins; the Mi-28 is on a non-playable slot and
	-- so takes InitialStanceAI (FireAtWill) deterministically, unaffected by local
	-- state. Worth knowing before moving either unit to the other slot.
	Apache.Stance = "HoldFire"

	TestHarness.FocusBetween(Havoc, Apache)
	TestHarness.Select(Havoc)

	-- Assert the setup query returned something before trusting it: an empty pool
	-- would make the drop-predicate unsatisfiable and the timeout would then look
	-- exactly like a targeting failure. AmmoCount throws on an unknown pool name
	-- (AmmoPoolProperties.cs:38), so a rename surfaces loudly instead of silently.
	local startingAmmo = Havoc.AmmoCount("secondary-ammo")
	if startingAmmo <= 0 then
		Test.Fail("setup: Mi-28 secondary-ammo empty at spawn, nothing could be spent")
		return
	end
	local apacheStartHP = Apache.Health

	local deadlineTicks = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)
	local elapsed = 0
	local ordered = false

	local tick
	tick = function()
		elapsed = elapsed + 1

		-- Issue the force order one tick in rather than from WorldLoaded, because a
		-- Lua-created actor is not yet in the world while WorldLoaded runs. Note
		-- what that does and does not mean: CombatProperties.Attack logs
		-- "<t> is an invalid target for <s>!" when Target.IsValidFor fails but then
		-- calls AttackTarget anyway (CombatProperties.cs:94-101), so the line is a
		-- warning rather than a refusal and its presence proves nothing either way.
		-- test-balance-heli-1v1 emits it for BOTH directions, including the one that
		-- demonstrably fired. Delaying a tick simply removes the question.
		-- AutoTarget is the backstop if the order is declined for any other reason.
		if not ordered then
			ordered = true
			Havoc.Attack(Apache, false, true)
		end

		if not Havoc.IsDead and Havoc.AmmoCount("secondary-ammo") < startingAmmo then
			local apacheHP = Apache.IsDead and 0 or Apache.Health
			Test.Pass(string.format(
				"Mi-28 spent a secondary missile at tick %d (%d -> %d); Apache %d/%d",
				elapsed, startingAmmo, Havoc.AmmoCount("secondary-ammo"),
				apacheHP, apacheStartHP))
			return
		end

		if Havoc.IsDead then
			Test.Fail("Mi-28 died before firing - the Apache was set to HoldFire, so the setup did not take")
			return
		end

		if elapsed >= deadlineTicks then
			Test.Fail(string.format(
				"Mi-28 never spent a secondary missile in %ds: no armament fired at the airborne Apache (ammo still %d, Apache %d/%d)",
				DeadlineSeconds, Havoc.AmmoCount("secondary-ammo"),
				Apache.IsDead and 0 or Apache.Health, apacheStartHP))
			return
		end

		Trigger.AfterDelay(1, tick)
	end

	Trigger.AfterDelay(1, tick)
end
