-- DEMO: every garrisonable building in the mod, on one map, labelled, photographed.
--
-- WHAT THIS IS FOR. Phase 1 of per-building garrison tuning (user ruling 2026-09-15):
-- "look at what they actually look like in game and decide from that instead of deciding
-- by some static data like footprint size etc." Nothing here asserts anything and nothing
-- here is tuned — this scenario exists to produce the pictures that the tuning is decided
-- from. No Test.Pass, no Test.Fail, no result.json.
--
-- THE CENSUS. 41 actors, re-derived from the YAML rather than copied from the audit:
-- 38 inherit ^CivBuilding (directly or through ^DesertCivBuilding), and GTWR / PBOX / HBOX
-- declare the garrison stack independently off ^Defense. V14–V18 and RICE inherit ^CivField
-- and are NOT in the set. V19.Husk is in the 38 by inheritance but is NOT garrisonable on
-- this branch — it strips Cargo, GarrisonManager, GarrisonProtection and Health — and is
-- placed anyway, last slot, so the lineup is provably complete; it has nothing to tune.
--
-- LAYOUT (the same table the map.yaml was generated from, so the two cannot drift):
--
--        col1*    col2   col3   col4      col5      col6   col7      col8
--   r1   GTWR     V02    V03    V04       V05       V06    V07       V08
--   r2   PBOX     V09    V10    V11       V12       V13    V20       V21
--   r3   HBOX     V22    V23    V24       V25       V26    V27       V28
--   r4   V01      V29    V30    V31       V32       V33    V34       V35
--   r5   V19      V36    V37    ASIANHUT  SNOWHUT   LHUS   WINDMILL  V19.Husk
--   r6   RUSHOUSE  —      —      —         —         —      —         —
--
--   * column 1 is the garrisoned example for its row. Column X = 7,16,25,34,43,52,61,70;
--     row Y = 6,14,22,30,38,46.
--
-- WHY THE ENEMY MARKERS ARE THERE. A port is only manned when GarrisonManager finds a
-- target inside that port's arc — ScanForTarget filters candidates through
-- IsTargetInPortArc, and Cone is a HALF-angle (GarrisonArcMath's header). So each garrisoned
-- building is ringed with HoldFire Russian riflemen, one per bearing we want lit:
--   * civilian buildings carry eight ports on four diagonals (NE1/NE2 yaw 896, SE 640,
--     SW 384, NW 128, all cone 140 = 49.2 deg either side) -> four diagonal markers,
--     which puts all eight men outside at once;
--   * GTWR has four ports on N/E/S/W -> four markers on the cross;
--   * PBOX and HBOX have exactly two, front (yaw 768, east) and back (yaw 256, west)
--     -> two markers.
-- The markers are given 2,000,000 HP in rules.yaml so the ports cannot kill their own
-- target and stand down mid-capture, and HoldFire on BOTH stance fields so nothing shoots
-- back. Port deployment does not read the soldier's stance; GarrisonManager scans itself.
--
-- WHAT THE PORT FRAMES ARE REALLY SHOWING. GarrisonManager.DeployToPort positions the man
-- at self.CenterPosition + port offset and then CLAMPS Z to terrain level
-- (GarrisonManager.cs:384-387, and again every tick at :728-733) — so the Z component of a
-- port Offset moves the muzzle, never the sprite. The shipped civilian offsets are 80/280
-- world units, i.e. under a third of a cell from the building's centre, so the men land in
-- a knot at the middle of the sprite rather than at its windows. That is the thing the port
-- close-ups exist to show.

local COLS = { 7, 16, 25, 34, 43, 52, 61, 70 }
local ROWS = { 6, 14, 22, 30, 38, 46 }

local DETAIL_ZOOM = 1.9   -- multiple of the default (fully zoomed-out) level
local PORT_ZOOM = 4.0

local LabelTick = 0

-- GENERATED, together with map.yaml, by generate.py in this directory. The two have to
-- agree cell for cell — edit the generator and re-run it, not this table.
--
-- Map-actor globals are readable at FILE SCOPE, not just inside WorldLoaded: ScriptContext
-- constructs every ScriptGlobal first (`ScriptContext.cs:217-232`), and MapGlobal's own
-- constructor is what calls RegisterMapActor for each map actor (`MapGlobal.cs:34-36`); the
-- script chunks are only executed afterwards, at `:242-244`. Sibling scenarios build their
-- squad tables inside WorldLoaded, which reads like a requirement and is not one.
local Grid = {
	{ actor = B_GTWR, name = "GTWR", x = 7, y = 6, row = 1, col = 1 },
	{ actor = B_V02, name = "V02", x = 16, y = 6, row = 1, col = 2 },
	{ actor = B_V03, name = "V03", x = 25, y = 6, row = 1, col = 3 },
	{ actor = B_V04, name = "V04", x = 34, y = 6, row = 1, col = 4 },
	{ actor = B_V05, name = "V05", x = 43, y = 6, row = 1, col = 5 },
	{ actor = B_V06, name = "V06", x = 52, y = 6, row = 1, col = 6 },
	{ actor = B_V07, name = "V07", x = 61, y = 6, row = 1, col = 7 },
	{ actor = B_V08, name = "V08", x = 70, y = 6, row = 1, col = 8 },
	{ actor = B_PBOX, name = "PBOX", x = 7, y = 14, row = 2, col = 1 },
	{ actor = B_V09, name = "V09", x = 16, y = 14, row = 2, col = 2 },
	{ actor = B_V10, name = "V10", x = 25, y = 14, row = 2, col = 3 },
	{ actor = B_V11, name = "V11", x = 34, y = 14, row = 2, col = 4 },
	{ actor = B_V12, name = "V12", x = 43, y = 14, row = 2, col = 5 },
	{ actor = B_V13, name = "V13", x = 52, y = 14, row = 2, col = 6 },
	{ actor = B_V20, name = "V20", x = 61, y = 14, row = 2, col = 7 },
	{ actor = B_V21, name = "V21", x = 70, y = 14, row = 2, col = 8 },
	{ actor = B_HBOX, name = "HBOX", x = 7, y = 22, row = 3, col = 1 },
	{ actor = B_V22, name = "V22", x = 16, y = 22, row = 3, col = 2 },
	{ actor = B_V23, name = "V23", x = 25, y = 22, row = 3, col = 3 },
	{ actor = B_V24, name = "V24", x = 34, y = 22, row = 3, col = 4 },
	{ actor = B_V25, name = "V25", x = 43, y = 22, row = 3, col = 5 },
	{ actor = B_V26, name = "V26", x = 52, y = 22, row = 3, col = 6 },
	{ actor = B_V27, name = "V27", x = 61, y = 22, row = 3, col = 7 },
	{ actor = B_V28, name = "V28", x = 70, y = 22, row = 3, col = 8 },
	{ actor = B_V01, name = "V01", x = 7, y = 30, row = 4, col = 1 },
	{ actor = B_V29, name = "V29", x = 16, y = 30, row = 4, col = 2 },
	{ actor = B_V30, name = "V30", x = 25, y = 30, row = 4, col = 3 },
	{ actor = B_V31, name = "V31", x = 34, y = 30, row = 4, col = 4 },
	{ actor = B_V32, name = "V32", x = 43, y = 30, row = 4, col = 5 },
	{ actor = B_V33, name = "V33", x = 52, y = 30, row = 4, col = 6 },
	{ actor = B_V34, name = "V34", x = 61, y = 30, row = 4, col = 7 },
	{ actor = B_V35, name = "V35", x = 70, y = 30, row = 4, col = 8 },
	{ actor = B_V19, name = "V19", x = 7, y = 38, row = 5, col = 1 },
	{ actor = B_V36, name = "V36", x = 16, y = 38, row = 5, col = 2 },
	{ actor = B_V37, name = "V37", x = 25, y = 38, row = 5, col = 3 },
	{ actor = B_ASIANHUT, name = "ASIANHUT", x = 34, y = 38, row = 5, col = 4 },
	{ actor = B_SNOWHUT, name = "SNOWHUT", x = 43, y = 38, row = 5, col = 5 },
	{ actor = B_LHUS, name = "LHUS", x = 52, y = 38, row = 5, col = 6 },
	{ actor = B_WINDMILL, name = "WINDMILL", x = 61, y = 38, row = 5, col = 7 },
	{ actor = B_V19HUSK, name = "V19.HUSK", x = 70, y = 38, row = 5, col = 8 },
	{ actor = B_RUSHOUSE, name = "RUSHOUSE", x = 7, y = 46, row = 6, col = 1 },
}

local Squads = {
	{ building = B_GTWR, name = "GTWR", men = { S1_1, S1_2, S1_3, S1_4, S1_5, S1_6 } },
	{ building = B_PBOX, name = "PBOX", men = { S2_1, S2_2, S2_3, S2_4 } },
	{ building = B_HBOX, name = "HBOX", men = { S3_1, S3_2, S3_3, S3_4 } },
	{ building = B_V01, name = "V01", men = { S4_1, S4_2, S4_3, S4_4, S4_5, S4_6, S4_7, S4_8, S4_9, S4_10 } },
	{ building = B_V19, name = "V19", men = { S5_1, S5_2, S5_3, S5_4, S5_5, S5_6, S5_7, S5_8, S5_9, S5_10 } },
	{ building = B_RUSHOUSE, name = "RUSHOUSE", men = { S6_1, S6_2, S6_3, S6_4, S6_5, S6_6, S6_7, S6_8, S6_9, S6_10 } },
}

local function CellPos(x, y)
	return WPos.New(x * 1024 + 512, y * 1024 + 512, 0)
end

-- Re-issued on a short cycle rather than posted once: FloatingText RISES 86 world units a
-- tick (FloatingText.cs:24, :50) and expires, so a single call is a drifting, vanishing
-- label. Duration 4 with a 4-tick period holds each name inside a quarter-cell band a cell
-- south of its building, clear of the sprite, for the whole run. TextAnnotationRenderable
-- draws in screen space, so the text stays the same size at every zoom.
local function DrawLabels()
	for _, g in ipairs(Grid) do
		Media.FloatingText(g.name .. "  r" .. g.row .. "c" .. g.col,
			CellPos(g.x, g.y + 1), 4)
	end
end

-- atPorts / inShelter / outside / dead, counted separately. DeployToPort does
-- SetPosition(soldier, self.Location), so a man AT a port stands on the building's own cell
-- while in-world; a man in shelter is out of the world entirely; a man still walking is
-- in-world somewhere else. Collapsing those into one number makes a quiet failure
-- ("0 here") unreadable afterwards.
local function Census(squad, building)
	local atPorts, inShelter, outside, dead = 0, 0, 0, 0
	for _, s in ipairs(squad) do
		if s.IsDead then
			dead = dead + 1
		elseif not s.IsInWorld then
			inShelter = inShelter + 1
		elseif s.Location.X == building.Location.X and s.Location.Y == building.Location.Y then
			atPorts = atPorts + 1
		else
			outside = outside + 1
		end
	end

	return atPorts, inShelter, outside, dead
end

local function CensusLine()
	local parts = {}
	for _, sq in ipairs(Squads) do
		local atPorts, inShelter, outside, dead = Census(sq.men, sq.building)
		parts[#parts + 1] = sq.name .. " " .. atPorts .. "p/" .. inShelter .. "s/" ..
			outside .. "o/" .. dead .. "d"
	end

	return table.concat(parts, "  ")
end

-- One capture beat: move the camera at `tick`, sample at `tick + settle`. The sample is
-- ONE FRAME LATE (Game.cs:926-930 reads the pixels at the end of the next RenderTick), so
-- the settle gap is not politeness — a capture armed in the same beat as the camera move
-- photographs the previous frame's viewport.
local function Beat(tick, settle, pos, zoom, label, note, before)
	Trigger.AfterDelay(tick, function()
		if before then
			before()
		end
		Camera.Position = pos
		Camera.Zoom = zoom
	end)

	Trigger.AfterDelay(tick + settle, function()
		TestHarness.Screenshot(label, note)
	end)
end

local function Sanitize(s)
	return string.lower((string.gsub(s, "[^%w]", "")))
end

WorldLoaded = function()
	UserInterface.SetMissionText("GARRISON LINEUP — 41 garrisonable actors; column 1 of each row is manned")

	Trigger.OnTick(function()
		LabelTick = LabelTick + 1
		if LabelTick % 4 == 0 then
			DrawLabels()
		end
	end)

	for _, sq in ipairs(Squads) do
		for _, s in ipairs(sq.men) do
			s.EnterTransport(sq.building)
		end
	end

	Media.DisplayMessage("41 garrisonable actors placed; six manned. Captures run to ~35s.",
		"LINEUP")

	-- --- 0. orientation ------------------------------------------------------------
	-- Camera.MinZoom is "as far out as this display goes" and frames the whole 100x60 map
	-- on any resolution. Buildings are unreadable at that scale by design; this frame is
	-- only here to show that all 41 spawned and the grid is laid out as the header says.
	Trigger.AfterDelay(400, function()
		Camera.Position = CellPos(49, 29)
		Camera.Zoom = Camera.MinZoom
		UserInterface.SetMissionText("00 OVERVIEW — whole grid, 8 cols x 6 rows; " .. CensusLine())
	end)

	Trigger.AfterDelay(410, function()
		TestHarness.Screenshot("00-overview",
			"expects: 41 buildings in an 8x6 grid, each with a white name label a cell below " ..
			"it; leftmost column manned with riflemen visible on the sprites. Orientation " ..
			"frame only — sprites are not meant to be legible here.")
	end)

	-- --- 1. the lineup: 12 detail frames, a 2x2 block of the grid each -----------------
	-- 2 columns x 2 rows per frame at zoom 1.9. At the default (zoom-1) level the viewport
	-- shows 600-900 world PIXELS of height depending on the viewer's viewport-distance
	-- setting (WorldViewportSizes), i.e. 25-37 cells at 24px/cell; 1.9 takes the worst case
	-- to 13 cells tall and 17 wide at 4:3. A 2x2 block spans 9 x 8 cells plus sprite
	-- overhang, so it fits on the narrowest plausible display rather than only on this one.
	local frameIndex = 0
	for rb = 1, 3 do
		for cb = 1, 4 do
			local c1, c2 = cb * 2 - 1, cb * 2
			local r1, r2 = rb * 2 - 1, rb * 2

			local names = {}
			for _, g in ipairs(Grid) do
				if (g.col == c1 or g.col == c2) and (g.row == r1 or g.row == r2) then
					names[#names + 1] = g.name
				end
			end

			if #names > 0 then
				frameIndex = frameIndex + 1
				local slug = {}
				for i, n in ipairs(names) do
					slug[i] = Sanitize(n)
				end

				local label = string.format("%02d-%s", frameIndex, table.concat(slug, "-"))
				local listed = table.concat(names, ", ")
				local tick = 430 + (frameIndex - 1) * 20

				Beat(tick, 10,
					CellPos(math.floor((COLS[c1] + COLS[c2]) / 2), math.floor((ROWS[r1] + ROWS[r2]) / 2)),
					DETAIL_ZOOM, label,
					"expects: " .. listed .. " reading left-to-right then top-to-bottom, each " ..
					"under its own white label. This is the frame the building's identity " ..
					"(wood / brick / concrete / industrial) is read off.",
					function()
						UserInterface.SetMissionText(label .. " — " .. listed)
					end)
			end
		end
	end

	-- --- 2. the ports: one close frame per manned building ----------------------------
	-- Zoom 4.0 and centred on the actor's own CenterPosition rather than on its Location
	-- cell, so a 2x2 (V01) and a 1x2 (RUSHOUSE) frame on their sprite and not on a corner
	-- of their footprint.
	for i, sq in ipairs(Squads) do
		local tick = 690 + (i - 1) * 25
		local label = string.format("p%d-%s-ports", i, Sanitize(sq.name))

		Trigger.AfterDelay(tick, function()
			if not sq.building.IsDead then
				Camera.Position = sq.building.CenterPosition
			end

			Camera.Zoom = PORT_ZOOM
			UserInterface.SetMissionText(label .. " — " .. CensusLine() ..
				"  (p=at port, s=in shelter, o=outside, d=dead)")
		end)

		Trigger.AfterDelay(tick + 12, function()
			TestHarness.Screenshot(label,
				"expects: " .. sq.name .. " close up with its manned firing ports. Read WHERE " ..
				"on the sprite each man stands relative to windows, doors and roofline — that " ..
				"is the port offset being photographed. The mission text along the top carries " ..
				"the per-building census; if this building reads 0p the ports never deployed " ..
				"and the frame shows nothing about port geometry.")
		end)
	end

	-- --- 3. hand the map back ----------------------------------------------------------
	Trigger.AfterDelay(860, function()
		Camera.Position = CellPos(49, 29)
		Camera.Zoom = Camera.MinZoom
		UserInterface.SetMissionText("Captures complete — " .. CensusLine() ..
			". Pan and zoom freely; close the window when done.")
		Media.DisplayMessage("Captures complete. " .. CensusLine(), "LINEUP")
	end)

	-- No Test.Pass. This is a demo: it holds the window open until the viewer closes it.
end
