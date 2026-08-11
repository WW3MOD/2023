-- AUTO TEST: a truck that is empty while carrying out a move order must break off.
--
-- The user's words: "if they are moving (have orders) and run out of supplies along the way, they
-- should get their move order cancelled so that the automatic return / evacuation happens."
--
-- The automatic return already exists -- DropsSupplyCache.OnBecomingIdle -- and that is exactly the
-- problem: it is IDLE-TRIGGERED, and a truck driving somewhere is never idle (Actor.IsIdle is
-- CurrentActivity == null). So an empty truck under orders drove the whole way to its destination
-- and only then remembered it had nothing to deliver. Same blind spot AutoSeekSupplies.ReturnWhenEmpty
-- was written to close for soldiers.
--
-- THE VERDICT IS GEOMETRIC, NOT TIMED, and the map is laid out to make it so. The truck starts at
-- x=10 and is ordered to x=58. Its shipped ResupplyBehavior is Evacuate, so breaking off means
-- RotateToEdge to the NEAREST edge -- from x=10 that is the west edge, ten cells away. A truck that
-- breaks off therefore leaves the map on the WEST side without ever going east; a truck that does
-- not must cross x=40 on its way to the destination. Neither outcome is reachable by the other at
-- any speed, so the deadline is only a backstop.
--
-- "Gone" is read as a live-actor count rather than off the Truck handle, because RotateToEdge ends
-- in self.Dispose() and touching a disposed actor from Lua raises. The count is checked first for
-- the same reason.

local DeadlineSeconds = 30
local DroveOnLine = 40 -- unreachable by a truck that turned west; unavoidable for one heading east

WorldLoaded = function()
	TestHarness.Select(Truck)

	-- Issued at world load, before the first tick, so the truck is under orders from the outset and
	-- the idle path never gets a chance to fire first. That ordering is the scenario: the bug is
	-- precisely that being under orders suppresses the return.
	Test.IssueMove(Truck, CPos.New(58, 16))

	TestHarness.AssertWithin(DeadlineSeconds, function()
		-- FIRST, before touching the handle: the truck evacuated and sold itself.
		if #Player.GetPlayer("USA").GetActorsByType("truk") == 0 then
			return true
		end

		if Truck.IsDead then return "fail: the truck died rather than evacuating" end

		local x = Truck.Location.X
		if x >= DroveOnLine then
			return "fail: the empty truck carried on with its move order -- it is at x=" .. x
				.. " heading for x=58, so the automatic return never fired"
		end

		return false
	end, "The empty truck neither evacuated nor drove on -- it is stuck somewhere in between")
end
