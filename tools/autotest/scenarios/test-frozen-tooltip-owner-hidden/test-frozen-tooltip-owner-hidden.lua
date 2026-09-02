-- ASSERTION SCENARIO: when a building is captured while its owner cannot see it, the
-- owner's ghost must stop short of naming the captor -- while still treating the
-- building as hostile.
--
-- =====================================================================================
-- THE CASE NOBODY HAD EVER OBSERVED
-- =====================================================================================
-- FrozenUnderFog.OnOwnerChanged (FrozenUnderFog.cs) refreshes exactly one player's
-- ghost: frozenStates[oldOwnerIndex], belonging to the player who just LOST the actor.
-- test-frozen-owner-snapshot pins the OUTSIDE of that boundary -- its viewer is a third
-- party, so the handler's body never runs for the player it asserts on. Nothing in the
-- suite watched the old owner's own ghost, which means the branch this scenario tests
-- had never been executed under assertion. That gap is the reason this file exists.
--
-- What the old owner used to be told: on a 6-player FFA (river-zeta-ww3 ships 6
-- mpspawns; seventh-woods, twin-rivers and x-lake ship 4 each with no fixed teams) the
-- refresh wrote the captor into FrozenActor.TooltipOwner, and WorldTooltipLogic.cs:82 --
-- the field's only reader engine-wide, confirmed 2026-09-02 by [Obsolete] + CS0618
-- census across all 11 projects, not by grep -- drew the captor's name and colour on a
-- ghost sitting on ground the player has never scouted. Which of five opponents took it,
-- for free, from a hover.
--
-- =====================================================================================
-- WHY THIS ASSERTS TWO FIELDS AND A CURSOR, NOT ONE FIELD
-- =====================================================================================
-- There were two candidate fixes and only one is acceptable. The rejected one froze
-- FrozenActor.Owner. That would hide the captor too, but Owner is what every
-- relationship predicate reads, and Player.RelationshipWith(null) returns Ally
-- (Player.cs:254-255) -- so a frozen or nulled Owner makes an enemy ghost invisible to
-- every `!= Enemy` test: AutoTarget, cursors, weapon validity, bot perception.
--
-- A scenario that asserted only "the tooltip no longer says Russia" would pass just as
-- happily on the rejected fix as on the shipped one. So phase 3 reads all three:
--
--   TooltipOwner  must be USA     -- the captor is not named
--   Owner         must be Russia  -- the capture IS reflected where it must be
--   frozen cursor must be non-empty -- and a real consumer still resolves through it
--
-- Drop any one of the three and the scenario stops distinguishing the two fixes.
--
-- =====================================================================================
-- WHAT IS DELIBERATELY NOT ASSERTED
-- =====================================================================================
-- That the capture is hidden. It is not, and cannot be: USA's units must keep treating
-- the building as an enemy, which is observable in cursors and autotargeting. Only the
-- captor's identity is separable. If a future change tries to hide the fact as well, the
-- Owner and cursor arms below are what will catch it.
--
-- SightingIntelOverlay (:187) rebuilds the GPS-dot palette from fa.Owner.InternalName
-- every frame and is a SECOND identity channel, left standing on purpose as a separate
-- decidable item. It is not asserted here. If someone later closes it, this scenario is
-- unaffected -- it reads the tooltip field, not the dot.
--
-- =====================================================================================
-- HOW THIS CAN LOSE
-- =====================================================================================
-- Vacuous unless USA genuinely cannot see the cell when the verdict is read, so the
-- state readout is a SETUP control at every phase. A run in which USA still observes
-- 8,16, or in which the ownership change silently failed, reports SETUP FAULT rather
-- than green.

local Grace = 40
local Phase = 1
local Ticks = 0
local SeenTooltipOwner = nil
local PreCaptureCursor = nil

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	local russia = Player.GetPlayer("Russia")
	if usa == nil or russia == nil then
		Test.Fail("SETUP: could not resolve players USA / Russia")
		return
	end

	if Observer == nil or Box == nil then
		Test.Fail("SETUP: map actors Observer/Box did not both resolve")
		return
	end

	TestHarness.FocusBetween(Box)
	TestHarness.Select(Observer)

	TestHarness.AssertWithin(30, function()
		Ticks = Ticks + 1
		-- Grace applies to phase 1 only, and Ticks is reset at each transition, so every
		-- later phase's own deadline means what it says. Gating all phases on Grace (which
		-- is what a bare `Ticks < Grace` does once Ticks is reset) would silently stretch
		-- phase 3's 25-tick settle to 40 and hide that the number was never the operative one.
		if Phase == 1 and Ticks < Grace then return false end

		if Box.IsDead then
			return "fail: SETUP -- Box died; nothing here should be able to shoot"
		end

		local cell = Box.Location
		local vis = Test.GetVisibility(usa, cell)
		local state = Test.FrozenActorState(usa, Box)
		-- InternalName, not Name: Name is ResolvedPlayerName (the display string, which a
		-- lobby can rewrite), and both Test bindings return InternalName. Comparing the two
		-- forms would be a false red the moment a display name diverges.
		local snapshotOwner = Test.FrozenActorOwner(usa, Box)
		local tooltipOwner = Test.FrozenActorTooltipOwner(usa, Box)
		local liveOwner = Box.Owner.InternalName

		print(string.format(
			"[tooltip-owner] phase=%d tick=%d cell=%d:%d vis=%d state=%s owner=%s tooltipOwner=%s liveOwner=%s",
			Phase, Ticks, cell.X, cell.Y, vis, state, snapshotOwner, tooltipOwner, liveOwner))

		-- ---- phase 1: USA owns Box and can see it, via the building's own vision ----
		if Phase == 1 then
			if state == "live" then
				SeenTooltipOwner = tooltipOwner
				if SeenTooltipOwner ~= "USA" then
					return "fail: SETUP -- while USA still owns Box and can see it, USA's ghost " ..
						"records tooltip owner '" .. tostring(SeenTooltipOwner) .. "', not 'USA'. " ..
						"The snapshot is wrong before anything interesting has happened; fix that " ..
						"before reading anything below. '' means no ITooltip was enabled on PBOX " ..
						"when RefreshState last ran, so TooltipOwner was never written at all"
				end

				-- Recorded, not asserted. The ghost is not Visible while USA can see the real
				-- actor, so Test.FrozenClickCursor's own eligibility filter returns "" here by
				-- construction. It is printed so that a later non-empty read is legible as a
				-- transition rather than an absolute.
				PreCaptureCursor = Test.FrozenClickCursor({ Observer }, usa, Box)
				print("[tooltip-owner] pre-capture frozen cursor = '" .. PreCaptureCursor .. "'")

				Box.Owner = russia
				Phase = 2
				Ticks = 0
				return false
			end

			if Ticks > Grace + 120 then
				return "fail: SETUP -- after " .. Ticks .. " ticks USA's ghost of its OWN Box " ..
					"reads state '" .. state .. "' (cell visibility " .. vis .. "), never 'live'. " ..
					"USA owns Box and ^BasicBuilding mounts Vision strength 3 out to 1c0 " ..
					"(structures.yaml:14-23), so the building reveals its own footprint to its " ..
					"owner -- 'frozen' or 'shrouded' here means that ladder was removed or " ..
					"overridden. 'none' means Box has no FrozenUnderFog trait and this scenario " ..
					"is testing nothing"
			end

			return false
		end

		-- ---- phase 2: the capture must take, and must cost USA its sight of the cell ----
		if Phase == 2 then
			if state == "frozen" and liveOwner == "Russia" then
				Phase = 3
				Ticks = 0
				return false
			end

			if Ticks > 300 then
				return "fail: SETUP -- " .. Ticks .. " ticks after Box.Owner was set to Russia, " ..
					"USA's ghost reads state '" .. state .. "' at cell visibility " .. vis ..
					" with live owner '" .. liveOwner .. "'. Wanted state 'frozen' and owner " ..
					"'Russia'. If liveOwner is still 'USA' the ownership change never applied and " ..
					"nothing below means anything. If state is 'live', something USA-side still " ..
					"covers 8,16 -- Box's Vision traits should have moved to Russia with the " ..
					"actor, and the Observer at 52,16 is 44 cells away, past ^StandardVision's " ..
					"outermost 32c0 rung by 12 (defaults.yaml:95-133); read the visibility band " ..
					"back to a distance and find what is sitting at it. If state is 'shrouded' " ..
					"the explored bit at 8,16 was cleared, which MapLayers.cs:241-256 only does " ..
					"via ResetExploration on defeat -- that would be a finding about " ..
					"ConquestVictoryConditions still being live, not a geometry mistake"
			end

			return false
		end

		-- ---- phase 3: THE VERDICT ----
		-- Give the change a few more ticks to settle so a pass cannot be an artefact of
		-- reading between the ownership transfer and the notifications it triggers.
		if Ticks < 25 then return false end

		if liveOwner ~= "Russia" then
			return "fail: SETUP -- Box's live owner regressed to '" .. liveOwner .. "' after " ..
				"phase 2 accepted 'Russia'. Something is changing ownership back; the verdict " ..
				"below would be meaningless"
		end

		if state ~= "frozen" then
			return "fail: SETUP -- at verdict time USA's ghost reads state '" .. state ..
				"' (cell visibility " .. vis .. "). USA must still be unable to see the building " ..
				"for any of the three assertions below to mean anything"
		end

		-- VERDICT ARM 1: the captor must not be named.
		if tooltipOwner ~= "USA" then
			return "fail: USA's ghost of Box now prints tooltip owner '" .. tooltipOwner ..
				"', but USA last observed the building as '" .. tostring(SeenTooltipOwner) ..
				"' and has not seen the cell since it changed hands. WorldTooltipLogic.cs:82 " ..
				"reads this field to draw the owner name and colour, so on an FFA map a hover " ..
				"over unscouted ground now says which opponent took the building -- intel this " ..
				"player has not earned. The cause is FrozenUnderFog.OnOwnerChanged refreshing " ..
				"TooltipOwner for the old owner: it must pass refreshTooltipOwner: false. The " ..
				"other candidate is FrozenUnderFogUpdatedByGps.OnOwnerChanged, which DOES " ..
				"refresh every player's TooltipOwner deliberately -- but only while that player " ..
				"holds an active GPS (GpsWatcher.Granted && GrantedAllies), and nothing in mods/ " ..
				"carries GivesGps or GpsPower, so it should be unreachable. If a GPS power has " ..
				"since been added, that is a legitimate reason for this to fire and the fix is " ..
				"to assert the GPS state, not to widen the expectation here"
		end

		-- VERDICT ARM 2: the capture itself must still be reflected. This is what tells the
		-- shipped fix apart from the rejected one (freezing FrozenActor.Owner).
		if snapshotOwner ~= "Russia" then
			return "fail: USA's ghost of Box records snapshot owner '" .. snapshotOwner ..
				"' where the live owner is 'Russia'. FrozenActor.Owner must keep following the " ..
				"capture. This is the REJECTED fix, not a stricter version of the shipped one: " ..
				"Owner feeds every relationship predicate, and Player.RelationshipWith(null) " ..
				"returns Ally (Player.cs:254-255), so a stale or null Owner makes an enemy ghost " ..
				"read as friendly to AutoTarget, cursors, weapon validity and bot perception. " ..
				"Withholding the captor's NAME is separable from the fact of the capture; the " ..
				"fact is not hideable and must not be hidden"
		end

		-- VERDICT ARM 3: and a real consumer must still resolve through that Owner.
		local cursor = Test.FrozenClickCursor({ Observer }, usa, Box)
		print("[tooltip-owner] post-capture frozen cursor = '" .. cursor ..
			"' (pre-capture was '" .. tostring(PreCaptureCursor) .. "')")

		if cursor == "" then
			return "fail: the ghost of the captured Box resolves NO cursor for a selected USA " ..
				"Abrams, though its snapshot owner correctly reads 'Russia'. Arm 2 passing while " ..
				"this fails means Owner holds the right value but something downstream stopped " ..
				"reading it -- check that the ghost still satisfies Test.FrozenClickCursor's " ..
				"eligibility filter (ITargetable + Visible + HasRenderables). State reads " ..
				"'frozen' above, so a ghost exists and is drawable; HasRenderables is the " ..
				"remaining suspect, and it is set from FrozenUnderFog.TickRender when the ghost " ..
				"transitions to Visible -- which is exactly what the capture caused"
		end

		return true
	end, "frozen tooltip-owner check never completed within 30s")
end
