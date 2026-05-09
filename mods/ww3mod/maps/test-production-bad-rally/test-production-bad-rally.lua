-- AUTO TEST: a Supply Route with its rally point on an UNREACHABLE cell must
-- still spawn the queued unit. Pre-fix (commit 7090749a),
-- ProductionFromMapEdge.Produce gated on PathExistsForLocomotor between the
-- spawn cell and the rally; if no path existed, Produce returned false and
-- the queue retried forever at 100% complete. Fix removes that gate;
-- MoveTo(evaluateNearestMovableCell:true) now picks the closest reachable
-- cell instead.
--
-- Strategy: set OwnSR's rally point to a cell off the map (CPos -10,-10).
-- That cell is unreachable for any locomotor. Then call OwnSR.Produce — that
-- bypasses the queue but goes through the same ProductionFromMapEdge.Produce
-- entry point we want to test. If Produce returns false, the unit never
-- spawns and the assertion fails. If the fix is intact, the unit appears at
-- the nearest reachable cell.

local DeadlineSeconds = 10

WorldLoaded = function()
	TestHarness.FocusBetween(OwnSR)
	TestHarness.Select(OwnSR)

	-- Force the rally to a guaranteed-unreachable cell off the map. Pre-fix
	-- this would block production entirely.
	OwnSR.RallyPoint = CPos.New(-10, -10)

	-- Cache owner ref before queueing — Lua can read player.GetActorsByType.
	local owner = OwnSR.Owner

	-- Skip the queue: call ProductionFromMapEdge.Produce directly (same
	-- function the queue calls when an item completes).
	OwnSR.Produce("e1.america")

	TestHarness.AssertWithin(DeadlineSeconds, function()
		local rifles = owner.GetActorsByType("e1.america")
		if #rifles > 0 then
			return true
		end
		return false
	end, "Unit never spawned within " .. DeadlineSeconds .. "s — production blocked by unreachable rally?")
end
