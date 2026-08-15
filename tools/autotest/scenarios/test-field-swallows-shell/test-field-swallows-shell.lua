-- AUTO TEST: an artillery shell landing on a Field actor must still detonate.
--
-- Fields (^CivField) are tiled one 1x1 actor per cell over whole map regions, and
-- ^1x1Shape gives each one a HitShape covering its ENTIRE cell. A field carries no
-- Targetable trait, so every warhead is invalid against it -- which made
-- CreateEffectWarhead.ActorTypeAtImpact classify any impact inside a field cell as
-- ImpactActorType.Invalid and return before spawning the explosion or the sound.
-- A field must read as bare ground to a weapon, so the shell must detonate normally.
--
-- The aim cell is deliberately EMPTY of units: the field has to be the only actor
-- whose hitshape contains the impact point, or a unit standing there would make the
-- impact Valid and the test would pass without the fix.

local DeadlineSeconds = 30
local DeadlineTicks = DeadlineSeconds * TestHarness.TicksPerSecond

-- Centre of the 11x11 field patch. The Paladin at 33,10 is 16 cells away, inside
-- ArtilleryRound's 10c0..40c0 band, and the shell's 2c0 inaccuracy keeps every
-- round at least 3 cells inside the patch.
local AimCell = CPos.New(33, 26)

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin)
	TestHarness.Select(Paladin)

	local startingAmmo = Paladin.AmmoCount("primary-ammo")
	local effectsBefore = Test.GetImpactEffectCount()

	Paladin.AttackGround(AimCell, false, false)

	local ticks = 0
	local everFired = false

	TestHarness.AssertWithin(DeadlineSeconds + 5, function()
		ticks = ticks + 1

		if Paladin.IsDead then
			return "fail: Paladin died before the shell landed"
		end

		local ammo = Paladin.AmmoCount("primary-ammo")
		if ammo < startingAmmo then
			everFired = true
		end

		local effects = Test.GetImpactEffectCount() - effectsBefore
		if effects > 0 then
			return true
		end

		if ticks >= DeadlineTicks then
			-- Control first: if nothing was ever fired the scenario never exercised the
			-- impact path at all, and this is a broken test rather than the bug.
			if not everFired then
				return "fail: CONTROL FAILED - Paladin never fired (ammo still " .. ammo ..
					"), so the impact path was never exercised. Fix the scenario before trusting this."
			end

			return "fail: shell fired (ammo " .. startingAmmo .. " -> " .. ammo ..
				") but 0 impact effects in " .. DeadlineSeconds ..
				"s - the field actor swallowed the detonation"
		end

		return false
	end, "unreachable: the predicate owns the deadline")
end
