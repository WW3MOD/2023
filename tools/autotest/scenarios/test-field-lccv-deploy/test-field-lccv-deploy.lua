-- AUTO TEST — an LCCV must be able to deploy its logistics centre on crop-field cells,
-- and must still refuse on a cell held by a real building.
--
-- ww3mod tiles fields as one 1x1 Building actor per cell. 73996d96 added
-- World.BlockingActorsAt to make the ActorMap-based occupancy tests see through them, and
-- routed BuildingUtils.IsCellBuildable through it. But fields are Building actors, so they
-- also sit in the SEPARATE BuildingInfluence layer, and the next line of IsCellBuildable
-- asked BuildingInfluence.AnyBuildingAt — which re-blocked exactly the actors the new
-- helper had just filtered out. The swap was a no-op at that call site and deploying onto
-- a field stayed refused (Transforms.CanDeploy -> CanPlaceBuilding -> IsCellBuildable).
--
-- UPDATED 2026-09-05: LOGISTICSCENTER is now 2x2 with Footprint "++ =+" (all four cells are still
-- in Tiles(), so the placement test is unchanged in kind) and Transforms.Offset is still -1,-1 — so
-- the tested block is the 2x2 whose BOTTOM-RIGHT cell is the LCCV's own, not a 3x3 centred on it.
-- Every site below still tests what it says:
--
--   A LccvField  at 22,16 — footprint 21,15..22,16, all four cells field actors -> MUST deploy.
--   B LccvBlocked at 44,16 — footprint 43,15..44,16, and the v19 Oil Pump at 44,15 is INSIDE it
--                            (it was a '+' cell of the old 3x3 and is a '+' cell of the 2x2)
--                            -> MUST still refuse.
--   C LccvBare   at 52,16 — nothing in the footprint -> MUST deploy.
--
-- C exists so that B cannot pass for the wrong reason. If the bare ground out at x=44..53
-- were unbuildable for some terrain reason, B would refuse without the Oil Pump doing any
-- work and the over-fix guard would be measuring nothing; C fails and says so instead.

local WINDOW = 30                     -- harness seconds for turn + transform + make anim
-- 2 cells is still comfortably enough: at 2x2 the LC's centre is the CORNER up-left of the LCCV's
-- cell, i.e. 724 units away, not 0 as it was when the building centred on that cell.
local RADIUS = WDist.FromCells(2)

local SITES = {
	{ name = "A(field)",   cell = CPos.New(22, 16), mustDeploy = true },
	{ name = "B(blocked)", cell = CPos.New(44, 16), mustDeploy = false },
	{ name = "C(bare)",    cell = CPos.New(52, 16), mustDeploy = true },
}

local function deployed(site)
	return #Map.ActorsInCircle(Map.CenterOfCell(site.cell), RADIUS, function(a)
		return a.Type == "logisticscenter"
	end) > 0
end

WorldLoaded = function()
	TestHarness.FocusBetween(LccvField, LccvField)
	TestHarness.Select(LccvField)

	-- The command bar's Deploy button, through IIssueDeployOrder — the same path a player
	-- takes. Transforms.DeployTransform consults CanDeploy and plays BuildingCannotPlaceAudio
	-- instead of transforming when it refuses.
	Test.IssueDeploy(LccvField)
	Test.IssueDeploy(LccvBlocked)
	Test.IssueDeploy(LccvBare)

	local ticks = 0
	local deadline = (WINDOW - 2) * TestHarness.TicksPerSecond

	TestHarness.AssertWithin(WINDOW, function()
		ticks = ticks + 1

		-- Over-fix guard, checked every tick so it cannot be masked by ordering: a real
		-- building in the footprint must never admit the transform.
		if deployed(SITES[2]) then
			return "fail: B(blocked) deployed a logisticscenter at 44,16 despite a v19 Oil " ..
				"Pump occupying 44,15 — the ground-cover filter is letting REAL buildings " ..
				"through, not just fields"
		end

		if deployed(SITES[1]) and deployed(SITES[3]) then
			return true
		end

		-- Self-diagnosing timeout, so the recorded verdict names which site failed and what
		-- that particular failure means.
		if ticks >= deadline then
			local a, c = deployed(SITES[1]), deployed(SITES[3])
			if not a and not c then
				return string.format(
					"fail: neither A(field) nor C(bare) deployed within %ds. C is on empty " ..
					"bare ground, so this is NOT the field bug — the deploy path is broken " ..
					"for every cell (or the LCCVs never got the order)", WINDOW - 2)
			end
			if not c then
				return string.format(
					"fail: C(bare) did not deploy within %ds on an empty footprint. The " ..
					"control failed, so B(blocked)'s refusal proves nothing this run",
					WINDOW - 2)
			end
			return string.format(
				"fail: A(field) did not deploy within %ds while C(bare) did — THE BUG. The " ..
				"only difference between the two sites is field actors in A's 2x2 footprint, " ..
				"so IsCellBuildable is still counting ground cover as an occupant " ..
				"(BuildingInfluence.AnyBuildingAt is the half that does it)", WINDOW - 2)
		end

		return false
	end, string.format("LCCV field-deploy assertions unresolved within %ds", WINDOW))
end
