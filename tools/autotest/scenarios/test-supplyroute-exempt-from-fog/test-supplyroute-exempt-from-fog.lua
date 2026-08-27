-- ASSERTION SCENARIO: the Supply Route is exempt from fog, and ONLY the Supply Route.
--
-- =====================================================================================
-- WHAT THIS GUARDS, AND WHY IT IS PROVISIONAL
-- =====================================================================================
-- Until 2026-08-27 FrozenUnderFog.IsVisible was short-circuited to an unconditional
-- `return true`, so every structure was visible to everyone unscouted. Removing that
-- six-month regression fogs every building -- including SUPPLYROUTE, the mod's central
-- objective, which would go dark until scouted under every lobby config.
--
-- That is a balance change arriving as a side effect of a bug fix, so it was held: an
-- AlwaysVisibleRelationships override on SUPPLYROUTE (ingame/structures.yaml) pins the SR
-- at exactly its pre-fix behaviour, and the bug fix ships with no gameplay change riding
-- along. THE OVERRIDE IS PROVISIONAL AND AWAITING A RULING.
--
-- IF THE RULING IS "GO DARK": delete the override and INVERT the rungs below -- EnemySR
-- becomes must-NOT-be-visible, and a scouted enemy SR must be added as the positive
-- control so the scenario cannot pass by vetoing everything. DO NOT DELETE THIS SCENARIO.
-- Deleting the guard instead of inverting it is how the `return true` this branch removed
-- survived six months in the first place.
--
-- =====================================================================================
-- THE DISCRIMINATOR IS THE POINT
-- =====================================================================================
-- "The enemy SR is visible on unscouted ground" passes on its own against at least two
-- failures that are not the exemption working:
--
--   * The fog fix silently reverted -- IsVisible back to `return true`, everything
--     visible, SR included.
--   * The exemption landed on a shared template (^Building, ^BasicBuilding) rather than
--     on SUPPLYROUTE, so every structure is exempt and the SR's visibility is incidental.
--
-- EnemyBox is what separates those from success. It is an ordinary pillbox four cells
-- from EnemySR in the same unscouted corner, and it must stay dark. Both visible means
-- one of the two failures above; neither visible means the exemption did not take at all.
-- The pair is asserted on the same tick.
--
-- OwnSR is the wholesale-veto control: if a future change vetoes buildings outright, the
-- "EnemyBox is dark" rung passes for the wrong reason, and OwnSR going dark catches it.
--
-- WRONG-RELATIONSHIP COVERAGE. The assertion is on an ENEMY-owned SR specifically, which
-- is what makes a partial edit fail: an override of `Ally` alone (the trait default, i.e.
-- a no-op) or `Neutral` alone leaves EnemySR dark and this goes red. A reader tempted to
-- "tidy" the relationship list down to one entry gets a red test rather than a silent
-- balance change.
--
-- UNTESTED GAP, stated rather than hidden: no rung covers a NEUTRAL-owned Supply Route.
-- The mod ships one SR per player and CLAUDE.md records SR capture as designed-but-not-
-- wired (SUPPLYROUTE carries no Capturable and no CaptureManager), so a neutral SR is not
-- reachable in a real match and a rung for it would assert against a state the game cannot
-- produce. It becomes reachable the moment capture is wired, and at that point the Neutral
-- entry in the relationship list is load-bearing and untested -- add the rung then.
--
-- =====================================================================================
-- WHAT IS ASSERTED
-- =====================================================================================
-- Test.IsMouseTargetable (MouseTargetVisibility.IsRevealedForMouseInput) is the verdict --
-- the predicate a right-click actually runs. Test.IsDetectedBy (CanBeViewedByPlayer, i.e.
-- FrozenUnderFog.IsVisible itself) is read alongside it as the diagnostic that tells a
-- failing run WHICH layer broke.
--
-- MUST be run with AUTOTEST_EXTRA_ARGS="Test.KeepRenderPlayer=true": TestModeLogic.cs:30
-- nulls World.RenderPlayer for a real player slot, and every World.FogObscures overload
-- returns false when it is null, which reports the whole map as clickable and would show
-- this exemption "working" whether or not it exists.

local Grace = 40

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	local russia = Player.GetPlayer("Russia")
	if usa == nil or russia == nil then
		Test.Fail("SETUP: could not resolve both players")
		return
	end

	if Scout == nil or OwnSR == nil or EnemySR == nil or EnemyBox == nil then
		Test.Fail("SETUP: map actors Scout/OwnSR/EnemySR/EnemyBox did not all resolve")
		return
	end

	TestHarness.FocusBetween(Scout, OwnSR)
	TestHarness.Select(Scout)

	local ticks = 0

	-- AssertWithin's failure string is concatenated EAGERLY at registration, so it can only
	-- report pre-run values. Every live number is therefore printed to lua.log or returned
	-- inside a "fail:" string, which IS evaluated at failure time.
	TestHarness.AssertWithin(20, function()
		ticks = ticks + 1
		if ticks < Grace then return false end

		if EnemySR.IsDead or EnemyBox.IsDead or OwnSR.IsDead then
			return "fail: SETUP -- an actor died; nothing here should be able to shoot"
		end

		local srCell = EnemySR.Location
		local boxCell = EnemyBox.Location
		local srVis = Test.GetVisibility(usa, srCell)
		local boxVis = Test.GetVisibility(usa, boxCell)
		local srDetected = Test.IsDetectedBy(EnemySR, usa)
		local boxDetected = Test.IsDetectedBy(EnemyBox, usa)
		local srClickable = Test.IsMouseTargetable(EnemySR)
		local boxClickable = Test.IsMouseTargetable(EnemyBox)
		local ownClickable = Test.IsMouseTargetable(OwnSR)

		print(string.format(
			"[sr-exempt] enemySR cell=%d:%d vis=%d detected=%s clickable=%s | " ..
			"enemyBox cell=%d:%d vis=%d detected=%s clickable=%s | ownSR clickable=%s",
			srCell.X, srCell.Y, srVis, tostring(srDetected), tostring(srClickable),
			boxCell.X, boxCell.Y, boxVis, tostring(boxDetected), tostring(boxClickable),
			tostring(ownClickable)))

		-- ---- setup controls: these decide whether the run is a verdict at all ----
		if srVis ~= 0 then
			return "fail: SETUP -- the enemy Supply Route's cell reads visibility " .. srVis ..
				", not 0. It is not on never-scouted ground, so its visibility says nothing " ..
				"about the fog exemption. Check ExploredMapCheckboxEnabled:false took and that " ..
				"no USA sight band reaches 58,16"
		end

		if boxVis ~= 0 then
			return "fail: SETUP -- the enemy pillbox's cell reads visibility " .. boxVis ..
				", not 0. The discriminator is inert: a visible pillbox proves nothing about " ..
				"whether the exemption is correctly scoped"
		end

		-- ---- wholesale-veto control ----
		if not ownClickable then
			return "fail: CONTROL -- the player's OWN Supply Route is not mouse-targetable. " ..
				"Buildings are being vetoed wholesale, so the 'enemy pillbox is dark' rung " ..
				"below would pass for entirely the wrong reason"
		end

		-- ---- the exemption ----
		if not srClickable then
			return "fail: an enemy Supply Route on never-scouted ground is NOT visible " ..
				"(detected=" .. tostring(srDetected) .. "). The provisional fog exemption on " ..
				"SUPPLYROUTE is not taking. Most likely the AlwaysVisibleRelationships " ..
				"override in ingame/structures.yaml was removed or narrowed -- note that " ..
				"`Ally` alone is the trait default and therefore a no-op, and `Neutral` alone " ..
				"does not match an enemy-owned SR. If the GO-DARK ruling has landed, this " ..
				"scenario must be INVERTED, not deleted; see the header"
		end

		-- ---- the discriminator ----
		if boxClickable then
			return "fail: an enemy PILLBOX on never-scouted ground is also visible, so the " ..
				"Supply Route rung above proves nothing. Either FrozenUnderFog.IsVisible has " ..
				"been short-circuited back to `return true` (see " ..
				"test-unscouted-building-hidden, which pins exactly that), or the " ..
				"AlwaysVisibleRelationships override was applied to a shared template such as " ..
				"^Building or ^BasicBuilding instead of to SUPPLYROUTE alone"
		end

		return true
	end, "supplyroute fog-exemption check never completed within 20s")
end
