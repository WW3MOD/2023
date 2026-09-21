-- AUTO TEST: can a player's riflemen garrison a NEUTRAL civilian building by walking in?
--
-- WHY THIS EXISTS. In the 2026-09-15 garrison-lineup demo run
-- (260915_210535_p58516_demo-garrison-lineup) the three squads ordered into USA-owned
-- emplacements boarded normally and all three ordered into NEUTRAL civilian buildings did not
-- move at all -- capture 006 shows ten riflemen still standing in their start files four cells
-- from the church. The demo was changed to teleport its men in with LoadPassenger so the
-- capture could proceed, which sidesteps the question rather than answering it.
--
-- IT MATTERS BEYOND THE DEMO. Walking into a neutral civilian building is HOW A PLAYER
-- GARRISONS ONE. If this is broken, the civilian half of the garrison feature is unreachable in
-- ordinary play and only the three built emplacements work.
--
-- ===================================================================================
-- REWRITTEN 2026-09-21 after the first run (260921_145733_p18476) came back
--     "neutral v08: 0 aboard, 2 still outside, 0 dead; USA-owned v08: 2 aboard, 0 still
--      outside, 0 dead"
-- and that verdict could not be acted on, because the two-lane instrument conflated three
-- different things. What changed and why:
--
--   1. THE BOARDING FILTER IS NOT THE CAUSE, AND THE FIRST RUN COULD NOT SAY SO. Since
--      8ca4b926 GarrisonManager implements ICargoCanLoadFilter and Cargo.CanLoad consults it
--      (Cargo.cs:522-533), so the "only filter is SupplyProvider" line in the 09-15 DISCOVERIES
--      entry is stale. But the filter ADMITS a neutral building on purpose:
--      GarrisonManager.CanLoadPassenger reads `claimedOwner ?? self.Owner` and hands it to
--      GarrisonBoardingMath.MayBoard (GarrisonManager.cs:341-352), which returns true for Ally
--      and Neutral and false only for Enemy (GarrisonBoardingMath.cs:44-47). The map's Neutral
--      player lists no Allies and no Enemies and is not Playable, so every branch of
--      CreateMapPlayers.SetupPlayerMasks (:140-199) falls through to the default and
--      Player.RelationshipWith returns Neutral (Player.cs:276-292). GarrisonBoardingTest pins
--      exactly this and is green. Test.CargoLoadFilterReport is now printed on failure so a run
--      SHOWS that instead of it having to be re-derived from four files.
--
--   2. `PassengerCount` IS NOT "DID HE GARRISON". A garrisoned soldier who deploys to a firing
--      port is REMOVED from Cargo and put back in the world at the port
--      (GarrisonManager.DeployToPort, :413-476) -- so a man who garrisoned correctly and then
--      manned a port reads as "0 aboard, 1 still outside", which is the exact shape of the
--      failure text the first run emitted. test-garrison-suppression-readout counts ports and
--      shelter separately for this reason; this scenario did not. Both are counted now.
--
--   3. "STILL OUTSIDE" DID NOT SAY WHETHER THEY MOVED, and that is the single most diagnostic
--      bit available. A man who never left his start cell was refused BEFORE the approach (the
--      Enter activity gave up on its first tick -- Enter.cs:103-130, the
--      `useLastVisibleTarget && lastVisibleTarget.Type == Invalid` return); a man standing at
--      the door was refused AT boarding (RideTransport.OnEnterComplete honouring CanLoad,
--      :69-87). Those are different bugs in different files. Cells moved is now reported.
--
--   4. LANE C SEPARATES OWNER FROM ENTRY PATH. Every neutral house successfully garrisoned in
--      this repo was ordered through the ORDER LAYER (hostile-cogarrison 260915_204522, six men
--      into a neutral v01 in suppression-readout 260915_204746, both issued at WorldLoaded);
--      every neutral house that failed was ordered through the Lua MobileProperties.EnterTransport
--      call, which queues a RideTransport directly. The owned control lane uses that same Lua
--      call and works -- so the two variables have never been held still at once, and "it is
--      about ownership" is at best half the statement. Lane C is a neutral house entered through
--      Test.ClickOrder; it is the cell of the table nobody has filled in.
--
-- HOW TO READ THE RESULT. A = neutral via Lua, B = USA-owned via Lua (control), C = neutral via
-- the order layer. A fails alone           -> the Lua entry path is broken for a non-allied owner.
-- A and C both fail                        -> neutral entry is broken outright; the owner is the
--                                             variable and the entry path is not.
-- B fails                                  -> the scenario is broken, not the game; ignore A and C.
-- ===================================================================================
--
-- Every v08 is capacity 4 (civilian.yaml V08) against a squad of 2, so no lane can fail for want
-- of space -- and the first run proved that directly, since the control lane loaded 2 into one.

local Deadline = 12

local Lanes = {}

local function StartCells(squad)
	local cells = {}
	for i, s in ipairs(squad) do
		cells[i] = { x = s.Location.X, y = s.Location.Y }
	end

	return cells
end

-- A man counts as garrisoned if he is in the shelter OR holding a firing port. Test.GarrisonPortOf
-- returns "none" for a man who is not at a port, so the second arm never double-counts a shelter
-- occupant -- a deployed man is not in Cargo at all.
local function Census(lane)
	local inside, atPort, walking, moved, dead = 0, 0, 0, 0, 0

	for i, s in ipairs(lane.squad) do
		if s.IsDead then
			dead = dead + 1
		elseif Test.IsLoadedInto(s, lane.house) then
			inside = inside + 1
		elseif s.IsInWorld then
			if Test.GarrisonPortOf(s, lane.house) ~= "none" then
				atPort = atPort + 1
			else
				walking = walking + 1
			end

			local from = lane.start[i]
			moved = moved + TestHarness.CellDrift(from.x, from.y, s.Location.X, s.Location.Y)
		end
	end

	return inside, atPort, walking, moved, dead
end

local function Garrisoned(lane)
	local inside, atPort = Census(lane)

	return inside + atPort
end

local function Line(lane)
	local inside, atPort, walking, moved, dead = Census(lane)

	return lane.label .. ": " .. inside .. " in shelter, " .. atPort .. " at ports, " ..
		walking .. " still outside (" .. moved .. " cells moved between them), " .. dead .. " dead"
end

-- The filter roster and its answers for one man of a failing lane. This is the question the first
-- run left open -- "which filters were actually registered and what did each one say" -- and it is
-- answerable in-sim rather than by reading four files.
local function FilterLine(lane)
	local probe = nil
	for _, s in ipairs(lane.squad) do
		if not s.IsDead and s.IsInWorld then
			probe = s
			break
		end
	end

	if probe == nil then
		return lane.label .. " filter: no man left outside to probe with"
	end

	return lane.label .. " filter: " .. Test.CargoLoadFilterReport(lane.house, probe)
end

WorldLoaded = function()
	Lanes = {
		{ label = "A neutral/Lua", house = NeutralHouse, squad = { N1, N2 } },
		{ label = "B owned/Lua", house = OwnedHouse, squad = { O1, O2 } },
		{ label = "C neutral/order", house = ClickHouse, squad = { CL1, CL2 } },
	}

	for _, lane in ipairs(Lanes) do
		lane.start = StartCells(lane.squad)
	end

	TestHarness.FocusBetween(NeutralHouse, OwnedHouse)
	Test.SetZoom(2)

	-- A and B: the Lua path, queueing a RideTransport activity directly.
	for _, s in ipairs(Lanes[1].squad) do
		s.EnterTransport(NeutralHouse)
	end

	for _, s in ipairs(Lanes[2].squad) do
		s.EnterTransport(OwnedHouse)
	end

	-- C: the real order layer. The order string is captured because "the targeter refused to offer
	-- EnterTransport at all" and "the order was offered and the activity then gave up" are different
	-- findings, and without this the lane would report the same silence for both.
	local refusedOrders = {}
	for _, s in ipairs(Lanes[3].squad) do
		local issued = Test.ClickOrder(s, ClickHouse)
		if issued ~= "EnterTransport" then
			refusedOrders[#refusedOrders + 1] = tostring(issued)
		end
	end

	local function Verdict()
		local a, b, c = Garrisoned(Lanes[1]), Garrisoned(Lanes[2]), Garrisoned(Lanes[3])
		local rows = Line(Lanes[1]) .. "; " .. Line(Lanes[2]) .. "; " .. Line(Lanes[3])

		if #refusedOrders > 0 then
			rows = rows .. "; lane C targeter returned " .. table.concat(refusedOrders, ",") ..
				" instead of EnterTransport"
		end

		-- The CONTROL lane has to succeed. If it did not, the finding is about this scenario and
		-- not about neutrality, and a verdict that did not separate them would be read as "neutral
		-- entry is broken" on no evidence.
		if b < 2 then
			return "CONTROL LANE FAILED, so this run says nothing about neutrality -- suspect the " ..
				"scenario: " .. rows
		end

		if a < 2 and c < 2 then
			return "NEUTRAL ENTRY IS BROKEN AND IT IS THE OWNER, NOT THE ENTRY PATH: a neutral v08 " ..
				"refused both the Lua path and the order layer while the identical USA-owned v08 " ..
				"took both. " .. rows .. ". " .. FilterLine(Lanes[1])
		end

		if a < 2 then
			return "NEUTRAL ENTRY IS BROKEN ONLY ON THE LUA PATH: the same neutral v08 that refused " ..
				"MobileProperties.EnterTransport accepted men ordered in through the order layer, so " ..
				"the variable is the entry path and not the building's owner. " .. rows .. ". " ..
				FilterLine(Lanes[1])
		end

		return "ORDER-LAYER ENTRY INTO A NEUTRAL BUILDING FAILED while the Lua path into the same " ..
			"owner succeeded -- the inverse of the 09-15 observation, and worth re-running before " ..
			"anything is built on it: " .. rows .. ". " .. FilterLine(Lanes[3])
	end

	-- Hand-rolled rather than TestHarness.AssertWithin, for ONE reason: AssertWithin calls
	-- Test.Pass() with no note, and a bare "pass" here would throw away the census that says WHICH
	-- lanes garrisoned and whether anybody ended up at a port rather than in the shelter. That
	-- census is the whole output of this scenario on a good day. Deadline semantics are AssertWithin's
	-- verbatim -- seconds * TestHarness.TicksPerSecond, polled once per tick.
	local elapsed = 0
	local timeoutTicks = math.floor(Deadline * TestHarness.TicksPerSecond)
	local check
	check = function()
		if Garrisoned(Lanes[1]) >= 2 and Garrisoned(Lanes[2]) >= 2 and Garrisoned(Lanes[3]) >= 2 then
			Test.Pass("ALL THREE LANES GARRISONED: a neutral civilian building takes a player's " ..
				"riflemen by both entry paths, and the owned control took them too. " ..
				Line(Lanes[1]) .. "; " .. Line(Lanes[2]) .. "; " .. Line(Lanes[3]))
			return
		end

		elapsed = elapsed + 1
		if elapsed >= timeoutTicks then
			Test.Fail(Verdict())
			return
		end

		Trigger.AfterDelay(1, check)
	end

	Trigger.AfterDelay(1, check)
end
