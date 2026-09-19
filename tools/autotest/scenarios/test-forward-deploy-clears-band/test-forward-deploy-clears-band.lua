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
--   2. THE GEOMETRY AND THE CENSUS. First the shape of the overlap the filter has to handle --
--      how many cells the package's annulus has, how many of those the border forbids and which
--      -- so that a change in the derivation or the package is named rather than silently
--      absorbed. Then every unit the trait placed, read two ways: the terrain under its cell (the
--      wall's own CustomTerrain write, which is what makes the ground impassable) and its column
--      against the band's known extent (which also catches a unit placed clean THROUGH the band,
--      on Russia's side of a closed border).
--
--   3. PATHABILITY. Everything is ordered home. A unit standing in the band would still move --
--      the crossing guard allows any step that reduces depth -- so this is not a second way of
--      asking (2); it is the pocket check, and it is what catches a unit whose escape route the
--      border closed even though the unit itself is clear of it.
--
-- WHAT WOULD MAKE THIS RED, STATED HONESTLY BECAUSE THE ANSWER IS "USUALLY, NOT ALWAYS".
-- At this separation the annulus and the band overlap in exactly ONE cell of 68 -- (31,16), the
-- only annulus cell at x >= 31, because the outer radius is 7 and the centre is at x 24. Twenty
-- units drawing from 68 cells miss it about three runs in four, so REVERTING THE FILTER AND
-- RERUNNING IS NOT A RELIABLE RED ARM and a green pre-fix run proves nothing. That is why the
-- geometry leg below asserts the SHAPE of the overlap -- 68 annulus cells, exactly one of them
-- forbidden, at exactly (31,16) -- rather than leaning on where the dice fell: those numbers are
-- deterministic, and any change to the advance percentage, the package radius, the half-width or
-- the derivation moves one of them and is named. The deterministic proof that the overlap exists
-- at all is OpenRA.Test/ForwardDeploymentBandOverlapTest, which needs no game.

-- The derived band, from the map's own spawn separation. Asserted at CENSUS_TICK rather than
-- assumed -- see the band-extent guard.
local BAND_MIN_X = 31
local BAND_MAX_X = 33
local BAND_ROW = 16

-- The forward package's centre and annulus, from the map's own numbers rather than read back off
-- the units: home x 6 + (52 * 35 / 100) = x 24, and FindTilesInAnnulus(centre, InnerSupportRadius
-- + 1, OuterSupportRadius) = (6, 7) for the motorized package. The engine buckets a cell by
-- ceil(sqrt(dx^2 + dy^2)) (MapGrid.CreateTilesByDistance), so buckets 6 and 7 are exactly
-- 25 < d^2 <= 49. All 68 of them are inside Bounds 1,1,64,32, so none is dropped.
local PACKAGE_X = 24
local PACKAGE_Y = 16
local ANNULUS_MIN = 6
local ANNULUS_MAX = 7
local EXPECT_ANNULUS_CELLS = 68
local EXPECT_FORBIDDEN_CELLS = 1

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
	local startCell = {}
	local censusNote = ""
	local geometryNote = ""
	local tick = 0

	-- ---- 1. GUARDS ---------------------------------------------------------------------------
	local function guards()
		-- Test.LobbyOption AND NOT Map.LobbyOption. The latter resolves ScriptLobbyDropdown traits
		-- only (MapGlobal.cs:112-120) and returns NIL for an ILobbyOptions id like this one, so a
		-- guard written against it faults on EVERY run -- which is how this scenario was written
		-- first, and it could never have passed. Map.LobbyOptionOrDefault(id, "motorized") is the
		-- other trap: the fallback is the expected value, so the guard can never fire at all.
		local forward = Test.LobbyOption("forwarddeployment")
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

	-- The package's annulus, enumerated the way Map.FindTilesInAnnulus does: a cell falls in bucket
	-- ceil(sqrt(dx^2 + dy^2)), and the trait asks for buckets InnerSupportRadius + 1 .. Outer.
	local function annulusCells()
		local cells = {}
		for dy = -ANNULUS_MAX, ANNULUS_MAX do
			for dx = -ANNULUS_MAX, ANNULUS_MAX do
				local d2 = (dx * dx) + (dy * dy)
				if d2 > 0 then
					local bucket = math.ceil(math.sqrt(d2))
					if bucket >= ANNULUS_MIN and bucket <= ANNULUS_MAX then
						cells[#cells + 1] = CPos.New(PACKAGE_X + dx, PACKAGE_Y + dy)
					end
				end
			end
		end
		return cells
	end

	-- ---- 2a. THE SHAPE OF THE OVERLAP --------------------------------------------------------
	-- DETERMINISTIC, UNLIKE THE CENSUS. Nothing here depends on the shuffle: it is the arithmetic
	-- the filter exists to handle, asserted so that a change to the advance percentage, the package
	-- radius, the band half-width or the derivation is NAMED instead of quietly making the census
	-- vacuous. A census over an annulus with nothing forbidden in it passes for free.
	local function geometry()
		local cells = annulusCells()
		local forbidden = {}

		for _, c in ipairs(cells) do
			if c.X >= BAND_MIN_X then
				forbidden[#forbidden + 1] = c
			end
		end

		if #cells ~= EXPECT_ANNULUS_CELLS then
			fault(string.format("THE PACKAGE ANNULUS HAS %d CELLS, EXPECTED %d: the annulus is"
				.. " InnerSupportRadius + 1 .. OuterSupportRadius = %d..%d around (%d,%d). Either the"
				.. " motorized package's radii moved or some of the ring left Bounds",
				#cells, EXPECT_ANNULUS_CELLS, ANNULUS_MIN, ANNULUS_MAX, PACKAGE_X, PACKAGE_Y))
		end

		if #forbidden ~= EXPECT_FORBIDDEN_CELLS then
			fault(string.format("THE BORDER FORBIDS %d OF THE ANNULUS'S CELLS, EXPECTED %d. The whole"
				.. " point of this scenario is that the package's outer ring and the band touch; with"
				.. " none forbidden the census below cannot fail and proves nothing, and with many"
				.. " more the advance or the half-width has changed",
				#forbidden, EXPECT_FORBIDDEN_CELLS))
		end

		-- The overlap cell is the band's own terrain, not merely a column the arithmetic picked.
		for _, c in ipairs(forbidden) do
			if c.X <= BAND_MAX_X and Map.TerrainType(c) ~= WALL_TERRAIN then
				fault(string.format("ANNULUS CELL %d,%d IS INSIDE THE BAND'S COLUMNS BUT READS %q,"
					.. " NOT %q -- the wall did not seal a cell the filter is being asked to reject",
					c.X, c.Y, Map.TerrainType(c), WALL_TERRAIN))
			end
		end

		-- The retreat ladder must NOT have engaged: 67 legal cells is far above
		-- ForwardDeploymentMinValidCells (12), so the package stays at its full advance and the
		-- overlap is real rather than something the search already stepped away from.
		local legal = #cells - #forbidden
		if legal < 12 then
			fault(string.format("ONLY %d LEGAL ANNULUS CELLS: below ForwardDeploymentMinValidCells the"
				.. " search steps back toward home and this scenario stops testing the overlap", legal))
		end

		geometryNote = string.format("annulus=%d forbidden=%d legal=%d", #cells, #forbidden, legal)
	end

	-- ---- 2b. THE BAND CENSUS -----------------------------------------------------------------
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

		local inBand, beyond, offAnnulus = {}, {}, {}
		for _, a in ipairs(units) do
			if not a.IsDead then
				local cell = a.Location

				-- EVERY UNIT MUST BE IN THE ANNULUS THE GEOMETRY LEG JUST MEASURED, or the two legs
				-- are talking about different ground and the overlap count above says nothing about
				-- where these units actually are.
				local d2 = ((cell.X - PACKAGE_X) ^ 2) + ((cell.Y - PACKAGE_Y) ^ 2)
				local bucket = math.ceil(math.sqrt(d2))
				if bucket < ANNULUS_MIN or bucket > ANNULUS_MAX then
					offAnnulus[#offAnnulus + 1] = string.format("%s at %d,%d", a.Type, cell.X, cell.Y)
				end
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

		if #offAnnulus > 0 then
			fault(string.format("%d UNIT(S) ARE OUTSIDE THE PACKAGE ANNULUS (%d..%d around %d,%d): %s."
				.. " The deployment centre moved, so the overlap this scenario measured is not the"
				.. " overlap these units were placed against",
				#offAnnulus, ANNULUS_MIN, ANNULUS_MAX, PACKAGE_X, PACKAGE_Y,
				table.concat(offAnnulus, ", ")))
		end
	end

	-- ---- 3. PATHABILITY ----------------------------------------------------------------------
	local function order()
		if #units == 0 then
			return
		end

		for _, a in ipairs(units) do
			startCell[a] = a.Location
		end

		Test.GroupMove(units, RALLY)
		Media.DisplayMessage("Forward package ordered home to 12,16.", "Test")
	end

	local function verdict()
		-- MOVED AT ALL, not moved CLOSER. Twenty units converging on one cell jam, and a unit shoved
		-- sideways by the one in front is briefly further from the rally than it started while being
		-- perfectly able to path -- so "closer than it started" is a plausible false red. What this
		-- leg actually asks is whether the unit can leave the cell it was put down on, and a unit the
		-- border sealed into a pocket leaves it never.
		local stuck = {}
		for _, a in ipairs(units) do
			if not a.IsDead then
				local was = startCell[a]
				if was ~= nil and a.Location.X == was.X and a.Location.Y == was.Y then
					stuck[#stuck + 1] = string.format("%s still on %d,%d", a.Type, was.X, was.Y)
				end
			end
		end

		if #stuck > 0 then
			fault(string.format("%d UNIT(S) COULD NOT PATH HOME: %s. A unit clear of the band can still"
				.. " be sealed into a pocket by it -- the escape-region search has to treat border"
				.. " cells as impassable, since they become impassable the moment the wall goes up",
				#stuck, table.concat(stuck, ", ")))
		end

		local summary = string.format("%d forward units | %s | %s | fwd=%q defconWall=%s level=%d",
			#units, geometryNote, censusNote, tostring(Test.LobbyOption("forwarddeployment")),
			tostring(Test.DefconWallActive()), Test.DefconLevel())

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
			geometry()
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
