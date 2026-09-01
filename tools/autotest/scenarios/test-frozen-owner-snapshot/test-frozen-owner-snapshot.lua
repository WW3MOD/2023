-- ASSERTION SCENARIO: a frozen ghost's recorded owner must not follow an ownership
-- change that happened while the viewer was not looking.
--
-- =====================================================================================
-- THE CLAIM UNDER TEST
-- =====================================================================================
-- Several fog-correctness arguments in this repo rest on "FrozenActor.Owner is a
-- snapshot" -- frozen at the viewer's last observation, and therefore safe to read
-- inside a fog-gated predicate. FrozenActor.Owner is written only by RefreshState()
-- (FrozenActorLayer.cs:122-141), and TWO paths call it while the viewer is NOT looking:
--
--   1. FrozenUnderFog's INotifyOwnerChanged.OnOwnerChanged (FrozenUnderFog.cs:217-223),
--      the narrow one, tested here.
--   2. FrozenUnderFogUpdatedByGps.OnOwnerChanged (Mods.Cnc/.../:67-70), which refreshes
--      EVERY player's ghost -- but only for players holding an active GPS
--      (GpsWatcher.Granted && GrantedAllies, :98-100). Nobody holds one in this
--      scenario, which is what makes path 1 the only thing under test. If a GPS power
--      is ever granted by default, this scenario starts failing for a legitimate
--      reason and the fix is to assert the GPS state, not to widen the expectation.
--
-- Path 1 is deliberately narrow. It refreshes frozenStates[oldOwnerIndex] -- the
-- ghost belonging to the player who just LOST the actor, so their own tooltip stops
-- naming them as owner. It does NOT touch any third party's ghost. This scenario pins
-- that boundary from the outside: USA is a third party, USA saw the building while it
-- was Russian, and USA must still believe it is Russian after it changes hands.
--
-- If someone ever "fixes" OnOwnerChanged to loop all players -- which reads like a
-- consistency improvement -- this goes red, and the failure text says why that is a fog
-- leak rather than a cleanup.
--
-- =====================================================================================
-- THE SECOND THING THIS PINS: THE FROZEN CURSOR IS REACHABLE AT ALL
-- =====================================================================================
-- Test.ClickCursor builds Target.FromActor, so it resolves CanTargetActor and can never
-- reach the CanTargetFrozenActor arm. The only other producer of a frozen Target is
-- UnitOrderGenerator.TargetForInput (:39), which needs a live mouse position AND a
-- non-null RenderPlayer -- and TestModeLogic.cs:31 nulls RenderPlayer. That is why the
-- 2579ca0a enter-cursor fix had to be defended by an IL byte-scan instead of a scenario.
--
-- Test.FrozenClickCursor bypasses TargetForInput and calls OrdersForSelection directly,
-- which reads no RenderPlayer. So phase 3 below is the first behavioural read of a
-- frozen cursor in this suite, and it needs NO launch flag.
--
-- DOES NOT NEED Test.KeepRenderPlayer=true. Every binding used here
-- (FrozenActorState / FrozenActorOwner / FrozenClickCursor / GetVisibility) reads the
-- VIEWER's own MapLayers or the frozen layer, never world.RenderPlayer. Passing the flag
-- anyway should not change the verdict; if it does, that is itself a finding.
--
-- =====================================================================================
-- HOW THIS CAN LOSE
-- =====================================================================================
-- The whole scenario is vacuous unless the ghost genuinely exists, so the state readout
-- is asserted as a SETUP control at every phase before any verdict is read from it. A
-- run in which USA never observed the building, or never lost sight of it, reports SETUP
-- FAULT rather than a green -- "the owner did not change" is trivially true when there
-- is no ghost at all.

local Grace = 40
local Phase = 1
local Ticks = 0
local SeenOwner = nil
local FrozenCursor = nil

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	local russia = Player.GetPlayer("Russia")
	local neutral = Player.GetPlayer("Neutral")
	if usa == nil or russia == nil or neutral == nil then
		Test.Fail("SETUP: could not resolve players USA / Russia / Neutral")
		return
	end

	if Scout == nil or Observer == nil or Box == nil then
		Test.Fail("SETUP: map actors Scout/Observer/Box did not all resolve")
		return
	end

	TestHarness.FocusBetween(Scout, Box)
	TestHarness.Select(Observer)

	TestHarness.AssertWithin(30, function()
		Ticks = Ticks + 1
		if Ticks < Grace then return false end

		if Box.IsDead then
			return "fail: SETUP -- Box died; nothing here should be able to shoot"
		end

		local cell = Box.Location
		local vis = Test.GetVisibility(usa, cell)
		local state = Test.FrozenActorState(usa, Box)
		local snapshotOwner = Test.FrozenActorOwner(usa, Box)
		-- InternalName, not Name: Name is ResolvedPlayerName (the display string, which a
		-- lobby can rewrite), and Test.FrozenActorOwner returns InternalName. Comparing the
		-- two forms would be a false red the moment a display name diverges.
		local liveOwner = Box.Owner.InternalName

		print(string.format(
			"[frozen-owner] phase=%d tick=%d cell=%d:%d vis=%d state=%s snapshotOwner=%s liveOwner=%s",
			Phase, Ticks, cell.X, cell.Y, vis, state, snapshotOwner, liveOwner))

		-- ---- phase 1: USA must actually observe the building first ----
		if Phase == 1 then
			if state == "live" then
				SeenOwner = snapshotOwner
				if SeenOwner ~= "Russia" then
					return "fail: SETUP -- at the moment of observation USA's ghost records " ..
						"owner '" .. tostring(SeenOwner) .. "', not 'Russia'. The snapshot is " ..
						"wrong before anything interesting has happened; fix that before reading " ..
						"anything below"
				end

				Scout.Kill()
				Phase = 2
				return false
			end

			if Ticks > Grace + 120 then
				return "fail: SETUP -- after " .. Ticks .. " ticks USA's ghost of Box still reads " ..
					"state '" .. state .. "' (cell visibility " .. vis .. "), never 'live'. The " ..
					"Scout at 5,16 is 3 cells away and should see it outright. If state is " ..
					"'shrouded' the Scout never gained vision; if 'none' then Box has no " ..
					"FrozenUnderFog trait and this scenario is testing nothing"
			end

			return false
		end

		-- ---- phase 2: losing sight must produce the FROZEN state, not shroud ----
		if Phase == 2 then
			if state == "frozen" then
				Phase = 3
				return false
			end

			if Ticks > Grace + 300 then
				return "fail: SETUP -- the Scout is dead but USA's ghost of Box reads state '" ..
					state .. "' at cell visibility " .. vis .. ", never 'frozen'. 'live' means " ..
					"something still grants USA vision of 8,16 -- check that Observer at 20,30 and " ..
					"OwnSR at 2,30 are really out of range. 'shrouded' means the cell fell to " ..
					"visibility 0, so the explored bit was lost and this is raw shroud rather " ..
					"than the explored-then-fogged state a ghost needs"
			end

			return false
		end

		-- ---- phase 3: the frozen cursor is readable, and ownership has not moved yet ----
		if Phase == 3 then
			if state ~= "frozen" then
				return "fail: SETUP -- state regressed to '" .. state .. "' before the capture " ..
					"was applied; the ghost did not hold still long enough to test"
			end

			FrozenCursor = Test.FrozenClickCursor({ Observer }, usa, Box)
			print("[frozen-owner] frozen cursor over the ghost = '" .. FrozenCursor .. "'")

			if FrozenCursor == "" then
				return "fail: the frozen ghost of an ENEMY building resolves no cursor for a " ..
					"selected Abrams. FrozenActorState says 'frozen', so a ghost exists and is " ..
					"drawable -- meaning either the ghost failed the ITargetable/HasRenderables " ..
					"filter inside Test.FrozenClickCursor, or no CanTargetFrozenActor arm on the " ..
					"Abrams accepts a frozen enemy structure. Read the printed state line above " ..
					"before assuming the binding is broken"
			end

			Box.Owner = neutral
			Phase = 4
			Ticks = 0
			return false
		end

		-- ---- phase 4: THE VERDICT ----
		-- Give the owner change a few ticks to propagate before reading, so a pass cannot
		-- be an artefact of reading the snapshot before any notification could have fired.
		if Ticks < 25 then return false end

		if liveOwner ~= "Neutral" then
			return "fail: SETUP -- Box.Owner was set to Neutral but reads '" .. liveOwner ..
				"'. The ownership change did not take, so the snapshot not moving proves nothing"
		end

		if state ~= "frozen" then
			return "fail: SETUP -- after the ownership change USA's ghost reads state '" .. state ..
				"'. USA must still be unable to see the building for the verdict to mean anything"
		end

		if snapshotOwner ~= "Russia" then
			return "fail: USA's frozen ghost of Box now records owner '" .. snapshotOwner ..
				"', but USA last observed it as '" .. tostring(SeenOwner) .. "' and has not seen " ..
				"it since. The building changed hands under fog and the snapshot followed, so " ..
				"every ally-gated predicate keyed on FrozenActor.Owner -- cursors, tooltips, " ..
				"ghost colour -- now reflects information this player has not earned. The narrow " ..
				"upstream exception at FrozenUnderFog.cs:217-223 refreshes ONLY the OLD OWNER's " ..
				"ghost; if that loop was widened to all players, this is the leak it caused. The " ..
				"other candidate is FrozenUnderFogUpdatedByGps.OnOwnerChanged, which DOES refresh " ..
				"every player -- but only while that player holds an active GPS, which nobody " ..
				"does in this scenario"
		end

		return true
	end, "frozen-owner snapshot check never completed within 30s")
end
