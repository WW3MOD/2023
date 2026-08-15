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

-- A shell can land up to 2 cells off the aim point, so every cell in that disc must be
-- field. 21 cells satisfy dx^2+dy^2 <= 4; the floor is set well under that so the check
-- survives circle-inclusion rounding while still catching a patch that stopped spawning.
local ImpactRadius = WDist.FromCells(2)
local MinFieldsUnderImpact = 9

-- PITFALL: Map.ActorsInCircle / ActorsInBox return NOTHING when called from WorldLoaded.
-- They read ActorMap's position bins (ActorMap.cs:649), and map-placed actors are only
-- pushed into those bins by ActorMap.TickFunction (ActorMap.cs:478), which runs from
-- ITick -- i.e. on the first world tick, AFTER WorldLoaded has returned. Querying too
-- early reports an empty world, which looks exactly like "the actors are missing". Hence
-- the grace window below rather than a single check at load. (GetActorsAt, which is
-- cell-keyed and updated on add, does NOT have this problem -- only the position bins do.)
local SetupGraceTicks = 2 * TestHarness.TicksPerSecond

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin)
	TestHarness.Select(Paladin)

	local aimPos = Map.CenterOfCell(AimCell)
	local isField = function(a) return a.Type == "v14" end

	local ticks = 0
	local setupDone = false
	local fieldsUnderImpact = 0
	local startingAmmo = Paladin.AmmoCount("primary-ammo")
	local effectsBefore = 0
	local everFired = false

	TestHarness.AssertWithin(DeadlineSeconds + 10, function()
		ticks = ticks + 1

		if Paladin.IsDead then
			return "fail: Paladin died before the shell landed"
		end

		-- SETUP CONTROL, run before the shot is ordered.
		-- Firing proves a shell flew; it does NOT prove a FIELD was under it. If the patch
		-- ever stops spawning -- actor rename, owner change (RequiresSpecificOwners is
		-- Neutral-only), a stray map edit -- the shell lands on bare ground, detonates, and
		-- this test goes green having measured nothing at all. This scenario is globbed into
		-- every `run-batch.sh --all` sweep (run-batch.sh:117), so it must catch that unattended.
		if not setupDone then
			local atAim = Map.ActorsInCircle(aimPos, WDist.FromCells(1), isField)
			fieldsUnderImpact = #Map.ActorsInCircle(aimPos, ImpactRadius, isField)

			if #atAim == 0 or fieldsUnderImpact < MinFieldsUnderImpact then
				if ticks < SetupGraceTicks then
					return false
				end

				return "fail: SETUP FAILED - " .. #atAim .. " field actor(s) at/adjacent to the aim " ..
					"cell " .. AimCell.X .. "," .. AimCell.Y .. " and " .. fieldsUnderImpact ..
					" within 2 cells (need >=1 and >=" .. MinFieldsUnderImpact .. ") after " ..
					SetupGraceTicks .. " ticks. The shell would land on bare ground, so a pass " ..
					"would not be attributable to the fix. Fix map.yaml before trusting any verdict."
			end

			-- Snapshot the counter and fire only once the field bed is confirmed present.
			setupDone = true
			effectsBefore = Test.GetImpactEffectCount()
			startingAmmo = Paladin.AmmoCount("primary-ammo")
			Paladin.AttackGround(AimCell, false, false)
			return false
		end

		local ammo = Paladin.AmmoCount("primary-ammo")
		if ammo < startingAmmo then
			everFired = true
		end

		if Test.GetImpactEffectCount() - effectsBefore > 0 then
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
				") over " .. fieldsUnderImpact .. " field actors covering the impact area, but 0 " ..
				"impact effects in " .. DeadlineSeconds .. "s - the field actor swallowed the detonation"
		end

		return false
	end, "unreachable: the predicate owns the deadline")
end
