-- TEST: a forward-deployed package never lands in the DEFCON 3 band.
--
-- Layout, arithmetic and why this map is the one that shows it: description.txt and map.yaml.
-- This file does three things in order, and each has its own named failure:
--
--   1. GUARDS. The lobby really opened on `motorized`, the wall really stands, and the derived
--      band really is columns 31-33. Every one of these is a way for the run to look green over
--      an empty map or an absent border, which is the failure mode that banks a fix that never
--      executed -- so each is checked BEFORE the thing it would hide.
--
--   2. THE BAND CENSUS. Every unit the trait placed, read two ways: the terrain under its cell
--      (the wall's own CustomTerrain write, which is what makes the ground impassable) and its
--      column against the band's known extent (which also catches a unit placed clean THROUGH
--      the band, on Russia's side of a closed border).
--
--   3. PATHABILITY. Everything is ordered home. A unit standing in the band would still move --
--      the crossing guard allows any step that reduces depth -- so this is not a second way of
--      asking (2); it is the pocket check, and it is what catches a unit whose escape route the
--      border closed even though the unit itself is clear of it.
--
-- WHAT WOULD MAKE THIS RED. Revert the ForbidsPlacement filter in SpawnStartingUnits and the
-- census fails: the motorized annulus around x 24 reaches x 31, the band's first column, and the
-- shuffle puts a unit there. The band-extent guard in (1) is what tells the two apart -- a red
-- census with a green band guard is the bug; a red band guard is the derivation moving, and the
-- census result means nothing until that is understood.

-- The derived band, from the map's own spawn separation. Asserted at CENSUS_TICK rather than
-- assumed -- see the band-extent guard.
local BAND_MIN_X = 31
local BAND_MAX_X = 33
local BAND_ROW = 16

-- DefconWallInfo.TerrainType. The wall writes this into Map.CustomTerrain for every band cell,
-- and Map.TerrainType reads back through CustomTerrain, so this is the wall's own record of what
-- it sealed rather than a second opinion about where it is.
local WALL_TERRAIN = "Wall"

-- Where everything is ordered for the pathability leg: clear ground six cells east of USA's
-- Supply Route, which occupies 5-7 x 15-17 (HomeLocation 6,16 plus StartingUnits' (-1,-1) offset
-- on a 3x3 footprint).
local RALLY = CPos.New(12, 16)

-- Budgets, in ticks. The wall raises on the world's FIRST tick (DefconWall applies from Tick, not
-- WorldLoaded), so anything past tick 1 can read it; 25 leaves room for the first-tick ordering to
-- settle without making the run long.
local CENSUS_TICK = 25
local ORDER_TICK = 40
local VERDICT_TICK = 440

local faults = {}

local function fault(text)
	faults[#faults + 1] = text
end

local function cellDistance(a, b)
	local dx = a.X - b.X
	local dy = a.Y - b.Y
	return math.floor(math.sqrt((dx * dx) + (dy * dy)))
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")

	-- Read by index rather than by name: the package is placed by the trait under test, so nothing
	-- here has a map.yaml name to reach for. ActorsInBox walks the ActorMap, which holds only
	-- actors that occupy space -- so the world and player actors are not in it and no trait-gated
	-- property is read off something that does not define it.
	local function usaUnits()
		local found = Map.ActorsInBox(
			Map.CenterOfCell(CPos.New(0, 0)),
			Map.CenterOfCell(CPos.New(65, 33)),
			function(a) return a.Owner == USA and a.Type ~= "supplyroute" end)
		return found
	end

	local units = {}
	local startDistance = {}
	local censusNote = ""
	local tick = 0

	-- ---- 1. GUARDS ---------------------------------------------------------------------------
	local function guards()
		local forward = Map.LobbyOption("forwarddeployment")
		if forward ~= "motorized" then
			fault(string.format("THE FORWARD DEPLOYMENT OPTION IS %q, NOT \"motorized\": rules.yaml sets"
				.. " SpawnStartingUnits.ForwardDeploymentClass and LobbyCommands.LoadMapSettings is"
				.. " supposed to seed the dropdown from it. With it at \"none\" no package is placed at"
				.. " all and every assertion below would pass over an empty map", tostring(forward)))
		end

		if not Test.DefconWallActive() then
			fault("THE DEFCON WALL IS NOT STANDING: with no border there is nothing for a package to"
				.. " land in, so the census below cannot fail and proves nothing. Check that both homes"
				.. " resolve to combatant sides (DefconWall derives nothing from one alliance group or"
				.. " from three) and that DefconEscalation is at level 3")
		end

		-- THE BAND WHERE THE ARITHMETIC SAYS IT IS. Columns 31-33 sealed, 30 and 34 open. If the
		-- derivation ever moves, this says so by name instead of letting the census quietly read the
		-- wrong columns.
		local sealed = {}
		for x = 29, 35 do
			local terrain = Map.TerrainType(CPos.New(x, BAND_ROW))
			local inBand = x >= BAND_MIN_X and x <= BAND_MAX_X
			sealed[#sealed + 1] = string.format("%d=%s", x, terrain)
			if inBand and terrain ~= WALL_TERRAIN then
				fault(string.format("THE BAND IS NOT WHERE IT SHOULD BE: column %d of row %d reads %q,"
					.. " expected %q. The border derives from the two homes (6,16 and 58,16), whose"
					.. " midpoint is column 32; a 1024 half-width makes that columns 31-33",
					x, BAND_ROW, terrain, WALL_TERRAIN))
			elseif not inBand and terrain == WALL_TERRAIN then
				fault(string.format("THE BAND IS WIDER THAN EXPECTED: column %d of row %d is sealed and"
					.. " should not be. HalfWidth has changed, or the derivation has moved", x, BAND_ROW))
			end
		end

		censusNote = "band " .. table.concat(sealed, " ")
	end

	-- ---- 2. THE BAND CENSUS ------------------------------------------------------------------
	local function census()
		units = usaUnits()

		-- The motorized package is twenty support actors (world.yaml StartingUnits@Motorized_america).
		-- A short count means the placement search gave up rather than stepped aside, which the
		-- last-resort tier in SpawnSupportActors is supposed to make impossible.
		if #units < 18 then
			fault(string.format("ONLY %d FORWARD UNITS WERE PLACED, expected the motorized package's 20."
				.. " The border filter must never DROP a unit -- its last resort is the unfiltered"
				.. " search -- so a short count means cells were rejected with no fallback taken", #units))
			return
		end

		local inBand, beyond = {}, {}
		for _, a in ipairs(units) do
			if not a.IsDead then
				local cell = a.Location
				if Map.TerrainType(cell) == WALL_TERRAIN then
					inBand[#inBand + 1] = string.format("%s at %d,%d", a.Type, cell.X, cell.Y)
				end

				if cell.X >= BAND_MIN_X then
					beyond[#beyond + 1] = string.format("%s at %d,%d", a.Type, cell.X, cell.Y)
				end
			end
		end

		if #inBand > 0 then
			fault(string.format("%d UNIT(S) WERE PLACED INSIDE THE DEFCON 3 BAND: %s. SpawnStartingUnits"
				.. " runs at IWorldLoaded and DefconWall writes its CustomTerrain from the first Tick,"
				.. " so CanEnterCell reads open grass there -- the placement filter has to ask"
				.. " DefconWall.ForbidsPlacement instead of testing the ground",
				#inBand, table.concat(inBand, ", ")))
		end

		if #beyond > 0 then
			fault(string.format("%d UNIT(S) WERE PLACED AT OR BEYOND THE BORDER (x >= %d): %s. The filter"
				.. " rejects IsBeyond and not merely the band, because half of an annulus centred at"
				.. " x 24 is legal ground on RUSSIA's side of a closed border",
				#beyond, BAND_MIN_X, table.concat(beyond, ", ")))
		end
	end

	-- ---- 3. PATHABILITY ----------------------------------------------------------------------
	local function order()
		if #units == 0 then
			return
		end

		for _, a in ipairs(units) do
			startDistance[a] = cellDistance(a.Location, RALLY)
		end

		Test.GroupMove(units, RALLY)
		Media.DisplayMessage("Forward package ordered home to 12,16.", "Test")
	end

	local function verdict()
		-- STRICTLY CLOSER, not arrived: twenty units converging on one cell jam, and a unit at the
		-- back of the queue that has moved a single cell has proved the only thing this leg is
		-- asking -- that it CAN move. A unit the border boxed in moves nowhere at all.
		local stuck = {}
		for _, a in ipairs(units) do
			if not a.IsDead then
				local now = cellDistance(a.Location, RALLY)
				local was = startDistance[a] or 0
				if now >= was and was > 2 then
					stuck[#stuck + 1] = string.format("%s at %d,%d (%d -> %d cells from the rally)",
						a.Type, a.Location.X, a.Location.Y, was, now)
				end
			end
		end

		if #stuck > 0 then
			fault(string.format("%d UNIT(S) COULD NOT PATH HOME: %s. A unit clear of the band can still"
				.. " be sealed into a pocket by it -- the escape-region search has to treat border"
				.. " cells as impassable, since they become impassable the moment the wall goes up",
				#stuck, table.concat(stuck, ", ")))
		end

		local summary = string.format("%d forward units | %s | defconWall=%s level=%d",
			#units, censusNote, tostring(Test.DefconWallActive()), Test.DefconLevel())

		if #faults > 0 then
			Test.Fail(table.concat(faults, " || ") .. " || " .. summary)
		else
			Test.Pass(summary)
		end
	end

	local step
	step = function()
		tick = tick + 1

		if tick == CENSUS_TICK then
			guards()
			census()

			if #units > 0 then
				TestHarness.FocusBetween(units[1], units[#units])
				TestHarness.Select(units[1])
			end
		end

		if tick == ORDER_TICK then
			order()
		end

		if tick >= VERDICT_TICK then
			verdict()
			return
		end

		Trigger.AfterDelay(1, step)
	end

	Trigger.AfterDelay(1, step)
end
