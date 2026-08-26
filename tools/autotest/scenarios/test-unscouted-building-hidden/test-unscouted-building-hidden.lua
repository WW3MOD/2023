-- ASSERTION SCENARIO: a building on ground the player has NEVER scouted must not be
-- clickable.
--
-- =====================================================================================
-- THE CLAIM UNDER TEST
-- =====================================================================================
-- FrozenUnderFog.IsVisible (Modifiers/FrozenUnderFog.cs:127) ends in an unconditional
-- `return true` tagged QUICK FIX 260503, with the real IsVisibleInner call commented out
-- beneath it. Every building in the mod carries the trait, so if that line is live then
-- Actor.CanBeViewedByPlayer answers YES for every structure, for every player, forever.
--
-- The interesting consequence is NOT the sprite. Actors are drawn before the shroud
-- overlay (WorldRenderer.Draw:349 vs :368) and ShroudRenderer paints unexplored cells at
-- alpha 1.0 (ShroudRenderer.Alpha:269, index 0), so a leaked sprite on shroud is painted
-- over. The consequence is the MOUSE path, which has no second shroud test of its own:
--
--   MouseTargetVisibility.IsRevealed(actorIsVisible, isFrozenUnderFog, ...) returns
--   actorIsVisible && (isFrozenUnderFog || positionIsUnfogged || isRadarDetected)
--
-- `isFrozenUnderFog` is a bare HasTraitInfo check, true for every building. It was added
-- (22a1ec34) as a deliberate exemption from the cell-fog veto, and it is sound ONLY
-- while `actorIsVisible` is a real answer -- the exemption delegates "has this player
-- earned sight of it" entirely to IDefaultVisibility. The quick fix removes that
-- authority, so both operands are constants and the predicate is `true && true`.
--
-- =====================================================================================
-- WHY THIS ASSERTS IsMouseTargetable AND NOT THE OBVIOUS THINGS
-- =====================================================================================
--   * Test.IsDetectedBy is Actor.CanBeViewedByPlayer -- the very function the quick fix
--     short-circuits. Asserting it would be asserting the C# I already read, not a
--     player-visible consequence. It is read below as a DIAGNOSTIC only, never as the
--     verdict.
--
--   * A Lua Attack order routes through CombatProperties.Attack, which additionally
--     special-cases FrozenUnderFog actors outright (CombatProperties.cs:97) and never
--     touches UnitOrderGenerator. It fires on the broken build and on a fixed one alike.
--
-- Test.IsMouseTargetable is IsRevealedForMouseInput itself -- the same function object
-- UnitOrderGenerator.TargetForInput and SelectionUtils call. It is the predicate that
-- decides whether the attack cursor appears.
--
-- =====================================================================================
-- HOW THIS CAN LOSE
-- =====================================================================================
-- NearBox is load-bearing in both directions. "The far building is not clickable" also
-- passes if NOTHING is clickable -- a broken render player, a scenario where USA has no
-- vision at all, a future change that vetoes buildings wholesale. NearBox is an enemy
-- building of the same actor type in plain sight, asserted clickable on the same tick.
--
-- The run MUST be launched with AUTOTEST_EXTRA_ARGS="Test.KeepRenderPlayer=true".
-- TestModeLogic:30 nulls World.RenderPlayer for a real player slot, and every
-- World.FogObscures overload returns false when RenderPlayer is null -- which reports
-- the entire map as clickable and would show this leak whether or not it exists. The
-- vis-0 setup control below is what catches that case: with a null render player the
-- cell readings still come from usa.MapLayers and stay honest, so a nulled render player
-- shows up as "vis 0 but clickable" and is called out explicitly in the failure text.

local Grace = 40

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	local russia = Player.GetPlayer("Russia")
	if usa == nil or russia == nil then
		Test.Fail("SETUP: could not resolve both players")
		return
	end

	if Scout == nil or NearBox == nil or FarBox == nil then
		Test.Fail("SETUP: map actors Scout/NearBox/FarBox did not all resolve")
		return
	end

	TestHarness.FocusBetween(Scout, NearBox)
	TestHarness.Select(Scout)

	local ticks = 0

	-- AssertWithin's failure string is concatenated EAGERLY at registration, so it can
	-- only report pre-run values. Every live number is therefore printed to lua.log or
	-- returned inside a "fail:" string, which IS evaluated at failure time.
	TestHarness.AssertWithin(20, function()
		ticks = ticks + 1
		if ticks < Grace then return false end

		if Scout.IsDead or NearBox.IsDead or FarBox.IsDead then
			return "fail: SETUP -- an actor died; nothing here should be able to shoot"
		end

		local nCell = NearBox.Location
		local fCell = FarBox.Location
		local nVis = Test.GetVisibility(usa, nCell)
		local fVis = Test.GetVisibility(usa, fCell)
		local nDetected = Test.IsDetectedBy(NearBox, usa)
		local fDetected = Test.IsDetectedBy(FarBox, usa)
		local nClickable = Test.IsMouseTargetable(NearBox)
		local fClickable = Test.IsMouseTargetable(FarBox)

		print(string.format(
			"[unscouted-building] near cell=%d:%d vis=%d detected=%s clickable=%s | " ..
			"far cell=%d:%d vis=%d detected=%s clickable=%s",
			nCell.X, nCell.Y, nVis, tostring(nDetected), tostring(nClickable),
			fCell.X, fCell.Y, fVis, tostring(fDetected), tostring(fClickable)))

		-- ---- setup controls: these decide whether the run is a verdict at all ----
		if fVis ~= 0 then
			return "fail: SETUP -- the far building's cell reads visibility " .. fVis ..
				", not 0. It is not on never-scouted ground, so this run cannot say anything " ..
				"about unscouted reveal. Check that ExploredMapCheckboxEnabled:false took and " ..
				"that no USA actor's sight band reaches 58,16"
		end

		if nVis <= 1 then
			return "fail: SETUP -- the near building's cell reads visibility " .. nVis ..
				", so the positive control is not actually in sight and a 'far one is hidden' " ..
				"result would be indistinguishable from 'everything is hidden'"
		end

		-- ---- the assertions ----
		if not nClickable then
			return "fail: CONTROL -- an enemy building 3 cells from a live scout, cell " ..
				"visibility " .. nVis .. ", is not mouse-targetable. Buildings have been vetoed " ..
				"wholesale; the negative rung below proves nothing in this state"
		end

		if fClickable then
			return "fail: an enemy building on ground this player has NEVER scouted (cell " ..
				"visibility 0, raw shroud) is mouse-targetable. The player can right-click a " ..
				"structure they have no legal knowledge of, and get its tooltip and owner. " ..
				"If detected=" .. tostring(fDetected) .. " is also true this is the " ..
				"FrozenUnderFog.IsVisible QUICK FIX 260503 short-circuit reaching " ..
				"MouseTargetVisibility through the isFrozenUnderFog exemption. If instead the " ..
				"near/far readings are identical for every field, suspect a null RenderPlayer " ..
				"(run without Test.KeepRenderPlayer=true) rather than the trait"
		end

		return true
	end, "unscouted-building visibility check never completed within 20s")
end
