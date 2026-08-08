-- AUTO TEST — every soldier should have a purpose and be fulfilling it.
--
-- Setup (map.yaml): a USA experimental bot owns ONE technician at (20,16), 14 cells
-- forward of its SR at (6,16). A neutral civilian house sits adjacent at (21,17). There
-- is no derrick and no enemy within reach, so the bot has no capture target and no
-- believed danger anywhere near the house.
--
-- The verdict has two halves and BOTH must hold, because fixing either alone makes
-- things worse:
--
--   (a) the house must NOT be garrisoned. Garrisoning a neutral building transfers it to
--       the entering soldier's owner (GarrisonManager.DynamicOwnership), so an owner flip
--       to USA-bot is a positive, unambiguous detector — and it is terminal: no bot module
--       anywhere issues Unload at a garrison building, so pre-fix the technician was gone
--       for the match while the blackboard claim was handed back as if it were free.
--
--   (b) the technician must be given a disposition and execute it. Merely NOT garrisoning
--       would leave it standing at (20,16) forever, which is the same waste with better
--       optics. CaptureCoordinator now musters an undispatchable capturer at the reserve
--       anchor behind the believed frontier, which from this SR is rearward of the start
--       cell — so the technician walks WEST.
--
-- Why the westward test is honest rather than a coincidence of tuning: the reserve anchor
-- is a steepest descent on the frontier-distance gradient halting ReserveStandoffCells
-- (10 coarse cells = 20 map cells) short of the believed line. The line here is the
-- Voronoi midline between the two SRs at x~32, so the anchor lands well west of 20 and
-- comfortably east of the SR. ARRIVE_X is set loosely enough that it pins "it withdrew
-- to a reserve" and not the exact cell the descent picks.

local START_X = 20
local ARRIVE_X = 17     -- must get at least this far back to count as "executed the move"

WorldLoaded = function()
	TestHarness.FocusBetween(Tecn, House)
	TestHarness.Select(Tecn)

	local withdrew = false

	TestHarness.AssertWithin(60, function()
		if Tecn.IsDead then return "fail: the technician died — nothing on this map shoots" end

		-- (a) Terminal failure. Checked every tick rather than at the end because once the
		-- technician is inside, it is out of the world and never comes back on its own.
		if not House.IsDead and House.Owner.Name == "USA-bot" then
			return "fail: the technician garrisoned the civilian house. There is no enemy "
				.. "anywhere on this map, so there was no reason to take cover, and nothing "
				.. "would ever have unloaded it."
		end

		-- (b) It was given somewhere to be, and it is getting there.
		if Tecn.Location.X <= ARRIVE_X then withdrew = true end

		if withdrew then return true end

		return false
	end, "the technician never withdrew to the reserve muster: it is still sitting at its "
		.. "start cell with no capture target, no enemy, and no order. Not garrisoning is "
		.. "only half the fix — an unassignable capturer still has to be given a purpose.")
end
