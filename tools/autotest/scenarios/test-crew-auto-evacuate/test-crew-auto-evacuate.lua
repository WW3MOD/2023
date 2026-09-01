-- AUTO TEST: ejected crew evacuate themselves, and a player order cancels that evacuation.
--
-- User request (2026-09-01): "Crew and pilots should auto evacuate as soon as they are out, if
-- another order is given it is canceled, so it is a one time order given when they spawn (exit the
-- vehicle/aircraft)."
--
-- BOTH HALVES OF THAT SENTENCE ARE ASSERTED HERE, and the second is the one worth a scenario. That
-- the evacuation is ONE-SHOT and freely overridable is a property of HOW it is queued — a plain
-- top-level activity, which any unqueued order truncates — and nothing in a unit test can see it.
-- The failure it guards against is the obvious wrong implementation: a standing mode, or an
-- INotifyIdle re-issue, either of which would re-evacuate the man the moment his new move ended and
-- make the crew impossible for a player to keep. That bug would leave half A passing and half B
-- failing, which is exactly why they are asserted together.
--
-- Half A: crew that are left alone leave the world (RotateToEdge walks them past the map edge and
--         disposes them, banking the refund).
-- Half B: the ONE crew member handed a Move order the moment he appears is still in the world at
--         the deadline, standing where he was sent.
--
-- RED before the change: half A fails outright, because nothing evacuated crew AT EJECTION. Stated
-- precisely, because two other evacuation paths do exist and neither covers this case:
--   * PoiOffensiveBotModule.SweepEjectedCrew is bot-only (IsEjectedCrewSweepCandidate rejects any
--     actor whose Owner is not the module's own player) and is gated OFF on @stable.
--   * AmmoPool.EvacuateForRefund (AmmoPool.cs:823-830) would eventually take a crew member, but only
--     once he has run DRY — that is 24 pistol rounds away and has nothing to do with dismounting.
-- So a player's crew stood next to the wreck indefinitely, which is what this scenario pins.

local DeadlineSeconds = 70
local HullX = 33
local HullY = 27

-- Where the overridden man is sent: a few cells west of the hull, well clear of the wreck and
-- nowhere near an edge, so "he is still here" cannot be confused with "he is mid-evacuation".
local KeepX = 27
local KeepY = 27

local CrewTypes = { "crew.commander.america", "crew.gunner.america", "crew.driver.america" }

local kept = nil
local keptOrdered = false

local function LiveCrew(owner)
	local all = {}
	for _, t in ipairs(CrewTypes) do
		for _, a in ipairs(owner.GetActorsByType(t)) do
			if not a.IsDead and a.IsInWorld then
				all[#all + 1] = a
			end
		end
	end

	return all
end

WorldLoaded = function()
	TestHarness.FocusBetween(Tank)
	TestHarness.Select(Tank)

	local owner = Tank.Owner

	-- ~40% HP: past EjectionDamageState (Heavy = HP < 50%) so the whole crew bails, short of lethal
	-- so nobody dies on the way out. Same step as test-crew-rear-dismount.
	Tank.Health = math.floor(Tank.MaxHealth * 4 / 10)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		local crew = LiveCrew(owner)

		-- As soon as anyone is out, claim the FIRST man and override his evacuation with an
		-- ordinary Move — the player action the user described. Unqueued, so it truncates the
		-- activity queue that the one-shot RotateToEdge is sitting in.
		if not keptOrdered and #crew > 0 then
			kept = crew[1]
			kept.Move(CPos.New(KeepX, KeepY))
			keptOrdered = true
		end

		if not keptOrdered then
			return false
		end

		if kept.IsDead or not kept.IsInWorld then
			return "fail: the crew member given a Move order left the world anyway — the " ..
				"auto-evacuation was not cancelled by the order, so it is a standing mode " ..
				"rather than the one-shot the user asked for"
		end

		-- Half A: everyone else must be gone. Anyone still standing about has not evacuated.
		for _, c in ipairs(crew) do
			if c ~= kept then
				return false
			end
		end

		-- Half B: the kept man is where he was sent, not drifting toward an edge.
		if TestHarness.CellDrift(kept.Location.X, kept.Location.Y, KeepX, KeepY) > 2 then
			return "fail: the overridden crew member is at " .. kept.Location.X .. "," ..
				kept.Location.Y .. " rather than near his ordered cell " .. KeepX .. "," .. KeepY
		end

		return true
	end, function()
		local crew = LiveCrew(owner)
		local note = "crew did not reach the expected disposition within " .. DeadlineSeconds ..
			"s; live crew=" .. #crew .. " ordered=" .. tostring(keptOrdered)
		for _, c in ipairs(crew) do
			note = note .. " [" .. c.Location.X .. "," .. c.Location.Y ..
				" idle=" .. tostring(c.IsIdle) .. (c == kept and " KEPT" or "") .. "]"
		end

		if #crew > 1 then
			note = note .. " (more than the overridden man is still present — the auto-evacuation " ..
				"either never started or is slower than the deadline; check whether RotateToEdge " ..
				"was queued at all before assuming it is a timing problem)"
		end

		note = note .. " hull=" .. HullX .. "," .. HullY

		return note
	end)
end
