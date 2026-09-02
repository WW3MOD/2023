-- Are trees indestructible, and does shelling one leave a stump?
--
-- The change under test (decoration.yaml, ^TreeIndestructible + one `Inherits@Indestructible:`
-- line on ^Tree) was committed with its headline claim untested: "I never launched the game...
-- I have not watched a tree survive a shell." This scenario is that claim, asserted.
--
-- =====================================================================================
-- WHAT IT ASSERTS
-- =====================================================================================
-- One Abrams shells cell 20,17 for ~19 seconds. Three shipped tree actors stand in the blast,
-- identical except for their damage multiplier:
--
--   Sponge  t03 @ 19,17   Modifier 100, HP 6000000, no husk    shell counter, cannot die
--   Guard   t01 @ 20,17   Modifier 0 (the change under test)   must be ALIVE at 2500/2500
--   Witness t02 @ 21,17   Modifier 100, husk intact            must be DEAD, one husk
--
-- Then:
--   A. the guard tree is alive AND at full HP, and
--   B. no `t01.husk` exists anywhere on the map -- the census finds exactly one husk and it is
--      the witness's `t02.husk`.
--
-- B is the owner's actual requirement ("stop all burnt trees from appearing") and is the half
-- most likely to be violated quietly later: SpawnActorOnDeath is still present and dormant on
-- every T##, so anything that re-enables tree death restores stumps without touching a line
-- that mentions husks.
--
-- =====================================================================================
-- WHY IT CANNOT PASS VACUOUSLY -- read this before trusting a green
-- =====================================================================================
-- A tree standing at the end of a run is worth nothing on its own. It is equally consistent
-- with the gun never firing, with the shell being unable to damage a `Trees`-targetable actor,
-- with the geometry missing, and with the tank driving off. Each of those is closed here by a
-- SETUP failure rather than by a comment:
--
--   no shell landed          -> sponge HP unchanged           -> SETUP fail
--   shell cannot hurt trees  -> witness tree still alive      -> SETUP fail
--   gun died / left          -> gun dead                      -> SETUP fail
--   map is not this map      -> guard MaxHealth /= 2500        -> SETUP fail
--
-- The witness is the load-bearing one, and it has to be a TREE. A warhead that quietly lost
-- `Trees` from its ValidTargets leaves every non-tree witness bleeding normally while every
-- tree on the map sits untouched at full HP -- and the scenario would then report "trees are
-- indestructible" for a reason that has nothing to do with the change. Only a tree that DOES
-- die under the same shell rules that out.
--
-- =====================================================================================
-- WHAT THIS SCENARIO OWNS, AND WHAT IT DOES NOT
-- =====================================================================================
-- engine/OpenRA.Test/TreeIndestructibleScopeTest.cs is a STATIC fixture (4 tests, ~140 ms, no
-- launch) that reads the YAML text and asserts: ^TreeIndestructible exists exactly once and
-- says Modifier: 0; it is not on the shared ^Tree template; all 22 husk-defining decorations
-- reach it; nothing without a husk does. That is the WIRING and the LITERAL VALUE.
--
-- It cannot see, and this scenario exists for, the next link in the chain: that Modifier: 0
-- ACTUALLY PRODUCES ZERO HP LOSS when a real warhead impacts, and that no husk actor
-- materialises. If DamageMultiplier stopped being registered as IDamageModifier, if the
-- modifier loop in Health.InflictDamage changed, or if some warhead path bypassed modifiers,
-- all four static tests would still pass and this one would go red.
--
-- So: static fixture owns "which actors are wired and what the YAML says". This owns "and it
-- actually works when a shell lands". When something breaks, read the static fixture first --
-- it is 140 ms and it will usually tell you which of the two layers moved.
--
-- =====================================================================================
-- HOW TO PRODUCE THE RED
-- =====================================================================================
-- In mods/ww3mod/rules/ingame/decoration.yaml, set the ^TreeIndestructible template's Modifier
-- to 100 -- DamageMultiplier's own documented default, so the trait stays present and enabled
-- and becomes a true no-op (Health.cs skips the multiply entirely when modifier == 100):
--
--   perl -0pi -e 's{(\^TreeIndestructible:\n\tDamageMultiplier\@Indestructible:\n\t\tModifier: )0\n}{${1}100\n}' \
--     mods/ww3mod/rules/ingame/decoration.yaml
--   git diff --numstat -- mods/ww3mod/rules/ingame/decoration.yaml   # must print exactly:  1  1
--
-- No rebuild needed; mod YAML is read from the source tree. Revert with `git checkout --`.
-- Expected: FAIL naming the guard tree as dead, with a `t01.husk` in the census.
--
-- WHY THE MODIFIER AND NOT THE INHERIT. Deleting the `Inherits@Indestructible:` line was the
-- original recipe and it is now the wrong instrument twice over. It changes TWO things at once
-- -- the trait becomes absent AND its value goes away -- so a red proves only that the template
-- as a whole matters. Flipping the number holds the wiring fixed and varies exactly one thing,
-- which is the single claim this scenario uniquely owns. And a missing inherit is precisely
-- what the static fixture above catches in 140 ms, so spending a game slot to re-prove it is
-- paying the expensive instrument to answer the cheap instrument's question.
--
-- The anchor is also the only one that survives both layouts of the opt-in list: it matched
-- exactly one line both before and after the 2026-09-02 scope change moved the inherit off
-- ^Tree and onto 22 individual tree actors (verified against both revisions of the file).

local ImpactX, ImpactY = 20, 17

-- ^Tree Health: HP 2500 (decoration.yaml). Asserted, not assumed: if it moves, the multiples
-- printed in the verdict stop meaning what they say.
local TreeMaxHP = 2500

-- rules.yaml overrides. PerShellDamage is the warhead's Damage; it is exact rather than
-- approximate because Falloff `100, 100` over Spread 2c0 is flat across the whole band all
-- three trees sit in, and the projectile has no Inaccuracy. See weapons.yaml.
local SpongeMaxHP = 6000000
local PerShellDamage = 12000

-- Budgeted in TICKS and converted where a helper wants seconds, per test-helpers.lua. Firing
-- starts late enough for the turret to have traversed; 475 ticks of fire at ReloadDelay 10 is
-- more than the Abrams' 40-round AmmoPool can supply, so the run is ammo-bound (~40 shells)
-- rather than clock-bound, and the shell count does not drift with harness timing.
local FireStartTick = 25
local VerdictTick = 500

local function HuskCensus()
	local found = {}
	Utils.Do(Map.ActorsInWorld, function(a)
		local t = a.Type
		-- Type first, Location second, always. Map.ActorsInWorld includes the world actor and
		-- the player actors, which carry no IOccupySpace -- reading .Location on those throws.
		-- Husks are Buildings, so by the time we ask, the actor is known to have a cell.
		if t ~= nil and string.find(string.lower(t), "%.husk$") ~= nil then
			local lt = string.lower(t)
			found[#found + 1] = {
				name = lt,
				where = lt .. "@" .. tostring(a.Location.X) .. "," .. tostring(a.Location.Y),
			}
		end
	end)
	return found
end

local function CensusText(husks)
	if #husks == 0 then
		return "(none)"
	end
	local parts = {}
	for i = 1, #husks do
		parts[i] = husks[i].where
	end
	return table.concat(parts, " ")
end

local function Verdict()
	local census = HuskCensus()
	local censusText = CensusText(census)

	-- ---- SETUP: did this run measure anything at all? --------------------------------
	if Gun.IsDead then
		Test.Fail("SETUP: the Abrams is dead. Nothing on this map can shoot it, so the " ..
			"scenario is not the one described and no conclusion about trees follows.")
		return
	end

	if Sponge.IsDead then
		Test.Fail("SETUP: the sponge tree died. It carries 6000000 HP against 12000 per " ..
			"shell and 40 rounds of ammunition, so this is not overkill -- either the " ..
			"rules.yaml Health override did not apply or the warhead is not the one in " ..
			"weapons.yaml. The shell count below it would have produced is unusable.")
		return
	end

	-- spongeLost is MEASURED; shells is derived from it. If the sponge ever sat outside the flat
	-- 100% band the division would under-count, which makes every number below a LOWER BOUND on
	-- what the guard absorbed (the guard is at falloff 100 by construction, the sponge at most
	-- equal to it). Conservative in the safe direction: it can never turn a real death into a pass.
	local spongeLost = SpongeMaxHP - Sponge.Health
	local shells = math.floor(spongeLost / PerShellDamage)
	local damageAtGuardCell = shells * PerShellDamage

	if spongeLost <= 0 then
		Test.Fail("SETUP: not one shell landed -- the sponge tree at 19,17 is still at " ..
			tostring(Sponge.Health) .. "/" .. tostring(SpongeMaxHP) .. " HP after " ..
			tostring(VerdictTick - FireStartTick) .. " ticks of ordered fire. The gun never " ..
			"delivered, so the guard tree standing proves nothing. Check that the Abrams " ..
			"held an attack activity (Test.ActivityChain) and that TreeGuardShell's 12c0 " ..
			"range covers the 7 cells from 20,24 to 20,17.")
		return
	end

	if not Witness.IsDead then
		Test.Fail("SETUP: the DESTRUCTIBLE control tree (t02 at 21,17, DamageMultiplier " ..
			"forced to 100 in rules.yaml) survived " .. tostring(shells) .. " shell(s) " ..
			"totalling " .. tostring(spongeLost) .. " damage at its own falloff band, and it " ..
			"only has " .. tostring(TreeMaxHP) .. " HP. This shell cannot kill a tree at all " ..
			"-- most likely its SpreadDamage warhead lost `Trees` from ValidTargets, which " ..
			"defaults to Ground,Water and would spare every tree on the map. Until this " ..
			"control dies, a surviving guard tree is not evidence of anything. Husks: " ..
			censusText)
		return
	end

	-- ---- ASSERTION A: the guarded tree survived the shelling --------------------------
	if Guard.IsDead then
		Test.Fail("TREES ARE DESTRUCTIBLE AGAIN: the guard tree (t01 at 20,17) was killed by " ..
			tostring(shells) .. " shell(s) = " .. tostring(damageAtGuardCell) ..
			" damage delivered at its own cell, against " .. tostring(TreeMaxHP) .. " HP. " ..
			"Either ^TreeIndestructible no longer says Modifier: 0, or T01 no longer reaches " ..
			"it, or Modifier: 0 has stopped zeroing damage at runtime. RUN " ..
			"TreeIndestructibleScopeTest FIRST (~140ms, no launch): it settles the first two " ..
			"statically, so if it is GREEN the answer is the third and the fault is in the " ..
			"engine's damage path, not the YAML. Husks now on the map: " .. censusText)
		return
	end

	if Guard.Health ~= TreeMaxHP then
		Test.Fail("The guard tree survived but is DAMAGED: " .. tostring(Guard.Health) .. "/" ..
			tostring(TreeMaxHP) .. " HP after " .. tostring(shells) .. " shell(s). " ..
			"Modifier: 0 means damage is multiplied to exactly zero (Health.cs:180-196 " ..
			"multiplies in decimal then casts to int), so any HP loss at all means the " ..
			"multiplier is no longer 0 -- a partially-weakened tree still dies to enough " ..
			"shells and is not what was asked for.")
		return
	end

	-- ---- ASSERTION B: the guarded tree left no stump ----------------------------------
	-- Checked separately from A because it is the requirement that can regress on its own:
	-- SpawnActorOnDeath is dormant, not deleted, so a future edit re-enabling tree death
	-- restores stumps without ever mentioning them.
	local guardHusks = 0
	for i = 1, #census do
		if string.find(census[i].name, "^t01%.husk") ~= nil then
			guardHusks = guardHusks + 1
		end
	end

	if guardHusks > 0 then
		Test.Fail("BURNT TREES ARE BACK: " .. tostring(guardHusks) .. " t01.husk on the map " ..
			"even though the guard tree is alive at full HP. Something spawned a stump " ..
			"without going through tree death -- a second t01 authored somewhere, or a " ..
			"SpawnActorOnDeath firing off a path that is not Health. Census: " .. censusText)
		return
	end

	if #census ~= 1 or string.find(census[1].name, "^t02%.husk") == nil then
		Test.Fail("Husk census is not the expected single t02.husk from the destructible " ..
			"control -- got " .. tostring(#census) .. ": " .. censusText .. ". Either an " ..
			"extra husk appeared (read its type: that is the actor that died) or the witness " ..
			"left none, which would mean SpawnActorOnDeath is broken mod-wide and this " ..
			"census can no longer see a stump even when one is due. Assertion B is blind " ..
			"until that is fixed.")
		return
	end

	TestHarness.Screenshot("tree-indestructible-verdict",
		"expects: the middle tree (20,17) standing undamaged between a stump at 21,17 and an " ..
		"intact tree at 19,17, with the Abrams 7 cells south")

	Test.Pass("guard tree t01 at " .. tostring(Guard.Health) .. "/" .. tostring(TreeMaxHP) ..
		" HP after " .. tostring(shells) .. " shells; an identical DESTRUCTIBLE tree one cell " ..
		"away absorbed " .. tostring(spongeLost) .. " damage from the same detonations, so at " ..
		"least " .. tostring(damageAtGuardCell) .. " landed on the guard's own cell = " ..
		tostring(math.floor(damageAtGuardCell / TreeMaxHP)) .. "x its " .. tostring(TreeMaxHP) ..
		" HP; the destructible control t02 in that same blast DIED, leaving exactly one husk (" ..
		censusText .. "); zero t01.husk anywhere on the map")
end

-- Keep the gun on the cell. The order is re-issued only when the attack activity has actually
-- gone, rather than on a timer: an unconditional re-issue replaces the running activity and
-- would reset the aim every time, which would make the shell count a function of the poll
-- period instead of the reload delay.
local function KeepFiring()
	if Gun.IsDead then
		return
	end

	if not TestHarness.HoldsAttackActivity(Gun) then
		Gun.AttackGround(CPos.New(ImpactX, ImpactY), false, false)
	end

	Trigger.AfterDelay(25, KeepFiring)
end

WorldLoaded = function()
	TestHarness.FocusBetween(Guard, Gun)
	TestHarness.Select(Gun)

	-- Staging faults that are visible at t=0 are reported at t=0, so a broken map does not cost
	-- the full run before saying so.
	if Guard.IsDead or Witness.IsDead or Sponge.IsDead or Gun.IsDead then
		Test.Fail("SETUP: one of Guard / Witness / Sponge / Gun is already dead at " ..
			"WorldLoaded. The map did not stage.")
		return
	end

	if Guard.MaxHealth ~= TreeMaxHP then
		Test.Fail("SETUP: the guard tree's MaxHealth is " .. tostring(Guard.MaxHealth) ..
			", not the " .. tostring(TreeMaxHP) .. " this scenario's damage multiples are " ..
			"written against. ^Tree's Health was retuned; update TreeMaxHP in this file before " ..
			"reading any verdict from it.")
		return
	end

	if Sponge.MaxHealth ~= SpongeMaxHP then
		Test.Fail("SETUP: the sponge tree's MaxHealth is " .. tostring(Sponge.MaxHealth) ..
			", not " .. tostring(SpongeMaxHP) .. " -- the t03 Health override in rules.yaml " ..
			"did not apply, so the shell counter would be wrong and every damage number in " ..
			"the verdict with it.")
		return
	end

	local startingHusks = HuskCensus()
	if #startingHusks ~= 0 then
		Test.Fail("SETUP: " .. tostring(#startingHusks) .. " husk(s) already on the map " ..
			"before a shot is fired: " .. CensusText(startingHusks) .. ". The census baseline " ..
			"is zero; with a pre-existing husk the post-run count cannot attribute anything.")
		return
	end

	print("[tree] guard t01 @ " .. tostring(ImpactX) .. "," .. tostring(ImpactY) .. " at " ..
		tostring(Guard.Health) .. "/" .. tostring(Guard.MaxHealth) ..
		" HP; witness t02 and sponge t03 flanking it at the same falloff; 0 husks at start")

	TestHarness.Screenshot("tree-indestructible-before",
		"expects: three intact trees in a row at 19/20/21,17 and an Abrams 7 cells south, " ..
		"nothing yet fired")

	Trigger.AfterDelay(FireStartTick, function()
		Gun.AttackGround(CPos.New(ImpactX, ImpactY), false, false)
		KeepFiring()
	end)

	Trigger.AfterDelay(VerdictTick, Verdict)
end
