-- CAPTURE INSTRUMENT: five enemy shades of red on one minimap, at real minimap scale.
--
-- =====================================================================================
-- THIS GRADES NOTHING. IT IS NOT A GUARD.
-- =====================================================================================
-- The terminal verdict is Test.Skip, for the same reason test-fog-darkness-ruler's and
-- test-screenshot-smoke's are: no clause here is a pass/fail statement about the mod. The
-- output is one PNG plus the setup readout below, and a human decides. A Test.Pass would
-- make this read as a regression guard it is not.
--
-- WHAT IT IS FOR. Player stance colours give each enemy a distinct lightness step of one
-- red (RelationshipShade.cs). The arithmetic is already pinned by
-- OpenRA.Test/RelationshipShadeTest.cs -- the step schedule, the light-to-dark ordering,
-- that no two shades collide as 8-bit colours, and that no shade leaves its hue band. None
-- of that needs a picture. The open question, never looked at by a human, is PERCEPTUAL:
-- at the two-and-a-bit pixels a unit occupies on a real minimap, does a reader see five
-- players or does the middle of the ramp mush into one red?
--
-- =====================================================================================
-- WHY THE MINIMAP SHOWS EVERY PLAYER HERE, AND WHY THAT IS HONEST
-- =====================================================================================
-- The obvious hazard with this capture is an image that silently shows FOUR dots instead
-- of six and is then read as "two shades blurred together". The minimap masks its actor
-- layer twice and BOTH masks are deliberately disarmed:
--
--   1. MiniMapWidget.cs:419 drops any actor with world.FogObscures(actor) true.
--      TestModeLogic.cs:30-31 nulls RenderPlayer for an autotest in a real player slot,
--      and World.cs:109 makes FogObscures unconditionally false when RenderPlayer is null.
--      This is why the scenario must NOT be run with Test.KeepRenderPlayer=true -- that
--      flag would restore the mask and fog out the very enemies the frame exists to show.
--
--   2. MiniMapWidget.cs:362-363 draws a shroud sprite OVER the actor layer, built from
--      `LocalPlayer ?? RenderPlayer` (:103) -- the local player, so nulling RenderPlayer
--      does not disarm it. rules.yaml turns fog off and explored on, which makes
--      MapLayers.GetVisibility return 10 for every cell (MapLayers.cs:662-691) and hence
--      alpha 200-20*10 = 0 (:257). The sprite is drawn and is entirely transparent.
--
-- Mask 2 is the one that is checked at runtime below rather than trusted, because it is
-- the one a lobby default could silently flip. Test.GetVisibility (TestGlobal.cs:1425)
-- calls player.MapLayers.GetVisibility -- the SAME function UpdateShroudCell calls at
-- :252 -- so the readout is not a re-derivation of the widget's input, it IS the widget's
-- input.
--
-- What this costs in realism, stated plainly: no single player in a real match ever sees
-- all five enemies at once, because fog would hide most of them. The frame is a
-- deliberate composite. It does not change how any one dot is drawn -- an enemy the viewer
-- CAN see in a real match is drawn in exactly the colour shown here -- so it is sound for
-- a question about colour, and it would be unsound for a question about fog.
--
-- =====================================================================================
-- WHAT THE FRAME SHOULD CONTAIN
-- =====================================================================================
-- The Enemies band holds five players, so the step is 0.11 lightness
-- (RelationshipShadeTest.AdjacentShadesKeepTheExpectedSeparation, count 5). Derived from
-- base FF0000 through RelationshipShade.Shade, index 0 lightest:
--
--   Enemy1  L 0.72   RGB 255,112,112
--   Enemy2  L 0.61   RGB 255, 56, 56
--   Enemy3  L 0.50   RGB 255,  0,  0     <- the tuned base colour
--   Enemy4  L 0.39   RGB 199,  0,  0
--   Enemy5  L 0.28   RGB 143,  0,  0
--
-- Note the ramp is NOT uniform in appearance even though it is uniform in lightness: the
-- light half separates by moving green and blue together (a wash toward pink), the dark
-- half by moving red alone. The Enemy3/Enemy4 and Enemy4/Enemy5 pairs are therefore the
-- ones to look at hardest -- both fully saturated, differing only in red channel by 56.
--
-- THE FRAME CARRIES ITS OWN TELL FOR THE SETTING BEING OFF. Every player's base Color in
-- map.yaml is a deliberately non-red rainbow (cyan, magenta, yellow, orange, purple).
-- AppearsOnMiniMap.cs:48 falls back to self.Owner.Color when UsePlayerStanceColors is
-- false. So a frame showing a rainbow means the launch arg did not take and the image
-- answers nothing; a frame showing a red ramp means it did. There is no Lua binding that
-- reads the setting, and this is why one is not needed.

local Enemies = { "Enemy1", "Enemy2", "Enemy3", "Enemy4", "Enemy5" }

-- Marker counts per owner, straight off the generated map.yaml. A miscount means a block
-- or a dot row is missing from the frame, which would read as a colour result.
local ExpectedMarkers = {
	Viewer = 25, Neutral = 25,
	Enemy1 = 28, Enemy2 = 29, Enemy3 = 29, Enemy4 = 29, Enemy5 = 28,
}

-- Cells sampled for the shroud-overlay check: the centre of every reference block, every
-- scatter row, the four corners and the middle. All must read visibility 10.
local VisibilitySamples = {
	{ 21, 10 }, { 30, 10 }, { 39, 10 }, { 48, 10 }, { 57, 10 }, { 66, 10 }, { 75, 10 },
	{ 44, 22 }, { 44, 28 }, { 46, 34 },
	{ 1, 1 }, { 96, 1 }, { 1, 80 }, { 96, 80 }, { 48, 40 },
}

-- Camera parked in the empty lower band, well below all geometry (blocks at y8-12, dots at
-- y22-34). The minimap draws a white 1px viewport outline (MiniMapWidget.cs:373) whose
-- size depends on window resolution and zoom, neither of which this scenario controls; the
-- zoom is pushed in to shrink it and the camera aimed away so it cannot cross a block.
local CameraCellX = 48
local CameraCellY = 72
local TargetZoom = 2.0

local SettleTicks = 40
local ShotTicks = 75
local VerdictTicks = 130

local setupNotes = {}

local function CheckRoster()
	local players = { "Viewer", "Neutral" }
	for _, e in ipairs(Enemies) do players[#players + 1] = e end

	local resolved = {}
	for _, name in ipairs(players) do
		local p = Player.GetPlayer(name)
		if p == nil then
			setupNotes[#setupNotes + 1] = "player " .. name .. " did not resolve"
		else
			resolved[name] = p
		end
	end

	for name, expected in pairs(ExpectedMarkers) do
		local p = resolved[name]
		if p ~= nil then
			local actual = #p.GetActorsByType("shademarker")
			print(string.format("[minimap-shades] %s markers=%d (expected %d)", name, actual, expected))
			if actual ~= expected then
				setupNotes[#setupNotes + 1] = string.format(
					"%s has %d markers, expected %d", name, actual, expected)
			end
		end
	end

	return resolved["Viewer"]
end

-- The shroud sprite is composited from the LOCAL player's layers, so the viewer is the
-- player whose visibility decides whether the dots are covered.
local function CheckShroudIsTransparent(viewer)
	local worst = 10

	for _, s in ipairs(VisibilitySamples) do
		local vis = Test.GetVisibility(viewer, CPos.New(s[1], s[2]))
		if vis < worst then worst = vis end
		if vis ~= 10 then
			print(string.format("[minimap-shades] cell %d,%d visibility=%d  <== NOT 10", s[1], s[2], vis))
		end
	end

	-- UpdateShroudCell (MiniMapWidget.cs:249-259) branches: visibility 0 is FULLY OPAQUE
	-- black, not alpha 200. Everything above it is alpha 200-20*cv, reaching 0 at 10.
	local alpha = 255
	if worst > 0 then alpha = 200 - 20 * worst end
	print(string.format("[minimap-shades] lowest sampled visibility=%d -> shroud overlay alpha=%d/255",
		worst, alpha))

	if worst ~= 10 then
		setupNotes[#setupNotes + 1] = string.format(
			"the minimap shroud overlay is NOT transparent (lowest sampled visibility %d, alpha %d/255) -- "
			.. "the dots are being darkened by an amount that varies across the map, so NO colour "
			.. "comparison may be read off this image", worst, alpha)
	end
end

WorldLoaded = function()
	local viewer = CheckRoster()
	if viewer == nil then
		Test.Skip("SETUP FAULT: the Viewer player did not resolve, so nothing can be checked or shown")
		return
	end

	Camera.Position = WPos.New(CameraCellX * 1024, CameraCellY * 1024, 0)

	local appliedZoom = Test.SetZoom(TargetZoom)
	print(string.format("[minimap-shades] camera on cell %d,%d; zoom requested %.2f applied %.2f",
		CameraCellX, CameraCellY, TargetZoom, appliedZoom))
	if math.abs(appliedZoom - TargetZoom) > 0.001 then
		setupNotes[#setupNotes + 1] = string.format(
			"zoom clamped to %.2f (asked %.2f), so the white viewport outline on the minimap is "
			.. "larger than intended and may cross the geometry", appliedZoom, TargetZoom)
	end

	UserInterface.SetMissionText(
		"MINIMAP STANCE SHADES: read the minimap panel, not the world. Seven blocks, then three dot rows.")

	-- Visibility resolves on the MapLayers tick; reading in WorldLoaded reports pre-tick state.
	Trigger.AfterDelay(SettleTicks, function()
		CheckShroudIsTransparent(viewer)
	end)

	-- The shot gets its own delay and the verdict another one after it. Test.Screenshot only
	-- ARMS a capture -- pixels are sampled at the end of the NEXT RenderTick -- so anything
	-- touching the world in the same closure is photographed instead of the asserted state.
	-- Nothing here changes state, but the gap is kept: it also gives the minimap time to
	-- finish its open animation (MiniMapWidget.cs:449-455), before which hasMiniMap is false.
	Trigger.AfterDelay(ShotTicks, function()
		TestHarness.Screenshot("minimap-stance-shades",
			"READ THE MINIMAP PANEL, top-right of the sidebar, 220x220. expects: a row of seven "
			.. "square blocks across the upper part of the minimap -- leftmost BLUE (the viewer), "
			.. "then FIVE blocks of red running lightest to darkest left to right, then a TAN one. "
			.. "Below them three rows of small dots: five dots in ramp order, five in reversed "
			.. "order, then four touching pairs. Judge only whether adjacent reds are separable. "
			.. "IF THE BLOCKS ARE A RAINBOW (cyan/magenta/yellow/orange/purple) the stance-colour "
			.. "launch arg did not take and this image answers nothing. The world view behind is "
			.. "empty grass and is not the subject.")
	end)

	Trigger.AfterDelay(VerdictTicks, function()
		if #setupNotes > 0 then
			Test.Skip("capture taken, but SETUP IS SUSPECT: " .. table.concat(setupNotes, "; "))
		else
			Test.Skip("capture taken; all 193 markers present and the minimap shroud overlay is "
				.. "fully transparent (visibility 10 everywhere sampled)")
		end
	end)
end
