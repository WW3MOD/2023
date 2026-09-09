-- DEMO -- the high-yield strategic nuclear strike, fired at the centre of the largest terrain in
-- the mod. Nothing here is asserted: no AssertWithin, no Test.Pass, no Test.Fail, no result.json.
-- The viewer watches, and closes the window when done.
--
-- LAYOUT, so this file can be read without map.yaml: ground zero is cell 64,64, the exact centre of
-- a 128x128 playfield. 81 target sites are arranged in nine rings around it at 6, 12, 19, 27, 36,
-- 46, 57, 69 and 82 cells, 4 to 16 bearings per ring. Every site is the same mix -- one structure,
-- one vehicle, two infantry, two trees -- so distance from the burst is the only variable and the
-- gradient reads the same in whichever direction the camera is pointed. USA's Supply Route is on
-- the south edge at 64,118; Russia's is in the far north-west corner at 10,10. Both survive: their
-- only target type is NoAutoTarget and no AtomicHighYield warhead lists it.
--
-- THE SCHEDULE, in ticks from WorldLoaded. Timestep is 60 ms, so 16.67 ticks per second -- NOT the
-- 25 that TestHarness.TicksPerSecond carries (that value is a deliberately-preserved harness
-- convention for AssertWithin budgets, documented in test-helpers.lua, and is not the tick rate).
-- Every delay below is therefore written in RAW TICKS through Trigger.AfterDelay rather than going
-- through a seconds conversion.
--
--     t=25    order issued at cell 64,64. Beacon, minimap ping and AbombLaunchDetected fire now.
--     t=125   missile enters the world at cell 64,127, the south edge (MissileDelay 100, overridden
--             from the shipped 500 in rules.yaml).
--     t=243   DETONATION. 118 ticks of flight over 63 cells at Speed 550, and that number is exact
--             rather than estimated: this missile has Acceleration 0 and no TerminalAcceleration,
--             so BallisticMissileFly does not accelerate it. Burst is 15c0 above the aim point.
--     t=243   THE FIREBALL LIGHTS. First maximum of the double flash, 21 cells of white.
--     t=244   flash (3 stacked palette warheads, white through ~t=313), mushroom cloud at 700%.
--     t=246   the DIP -- the shock front has gone opaque, so the light dims to 0.9 and turns orange
--             while its radius keeps growing. Blast wave is born here too, at the fireball surface
--             (6.8 cells) travelling at Mach 4.05, not at a point travelling at Mach 1.
--     t=284   *** SECOND MAXIMUM. Intensity 7.0 across 124 cells: the whole map is white. ***
--     t=404   fireball out on a dull red, 161 ticks after it lit. Fire ignition also finishes
--             staging outward here, to 124 cells -- past the blast wave's own 102.
--     t=657   thermal radiation pulse ends (413 ticks of it, 51 damage pulses).
--     t=840   blast wave reaches its full 102-cell radius. Everything on the map has now been hit.
--     t=933   last suppression band lands.
--     t=1743  innermost fires burn out (1500 ticks).
--     t=2043  the strike camera is removed (CameraRemoveDelay 1800).
--
-- (Every figure from t=243 onward was re-derived on 2026-09-06, when this warhead was relabelled
-- from ~800 kt to ~6 Mt and every radius and delay in it recomputed from that yield. The blast wave
-- is faster than it was -- 597 ticks to full radius against 714 -- because it now leaves the
-- fireball supersonic instead of crawling from a point at the speed of sound.)
--
-- SO: the interesting window is t=243 to t=840 -- 14.6 s to 50.4 s after the map loads. Screenshot
-- anywhere in it. The single best frame is t=284, the fireball's second maximum. After that the
-- most legible is around t=380 to t=450, when the wavefront is 35-48 cells out: the inner rings are
-- already gone, the middle band is burning, and the outer rings are still intact and untouched, so
-- all three states are on screen at once.
--
-- THE DEMO ZOOMS ITSELF. Camera.Zoom is a multiple of the default level, so Camera.MinZoom is as
-- far out as this display goes -- deliberately read rather than hardcoded, because the floor is
-- derived from the viewer's resolution and viewport-distance setting. At the default level a
-- 128x128 map shows perhaps a third of the playfield, which is not enough for a blast that reaches
-- 102 cells; the viewer used to be told to zoom out by hand.
--
-- AFTERWARDS THE MAGAZINE IS EMPTY, and that is the shipped economy rather than a limitation of
-- the demo: one purchase buys exactly one shot, so SupportPowerInstance.Activate consumes the
-- charge and the cameo leaves the support bin. The viewer has 80000 credits left and the Powers
-- tab is on the sidebar, so a second strike is two clicks and about five ticks of load away --
-- which is itself worth seeing, since it is the loop a real player uses.

local OrderTick = 25
local GroundZero = { X = 64, Y = 64 }

-- THE PURCHASE, added 2026-09-07, and without it this demo fires nothing at all.
--
-- Every shipped support power carries `RequiresPurchase: True`, which is a BANK and not a timer:
-- at zero banked shots SupportPowerInstance.Disabled is true, Active is false, Ready is false, and
-- Test.ActivateSupportPower returns 'not-ready:0' forever. The `ChargeInterval` and
-- `StartFullyCharged` this file's rules.yaml used to set were read and discarded
-- (SupportPowerManager.cs:228-229), so the demo looked staged and did nothing. The whole chain is
-- written out in the header of TestHarness.EnsurePower (mods/ww3mod/scripts/test-helpers.lua).
--
-- rules.yaml supplies the three things a purchase needs: USA's cash, the sandbox lobby option that
-- provides this power's `powers.event` tier (no faction provides it), and a BuildDuration of 5 on
-- the proxy. The magazine is therefore loaded around t=12 and NOT ONE TICK OF THE SCHEDULE ABOVE
-- MOVED -- the order still goes out at t=25 and every derived time below it still holds.
local Proxy = "power.highyieldnuke"
local PowerKey = "HighYieldNukeStrike"

local tick = 0
local USA
local fired = false
local warned = false

-- Quiet on success, loud on failure. This demo's entire output is a FRAME, so nothing is written
-- over it while the purchase is working; a chat line appears only if the shot did not go out on
-- time, which is the one case where the viewer needs to know rather than keep waiting.
local function complain(text)
	if not warned then
		warned = true
		Media.DisplayMessage(text, "DEMO")
	end
end

local function step()
	tick = tick + 1

	if not fired then
		-- Called every tick and stateless: it reads the power's own state, buys when the magazine
		-- is empty, and says nothing while a purchase is in flight. One purchase is one shot.
		local ready, status = TestHarness.EnsurePower(USA, Proxy, PowerKey, tick)

		if not ready and tick > OrderTick then
			complain("no shot in the magazine at t=" .. tick .. " (" .. status .. "). The strike"
				.. " will be late or will not come. 'refused' means the buy tab would not take the"
				.. " order -- check PowersSandboxCheckboxEnabled and DefaultCash in rules.yaml;"
				.. " 'absent' means the OrderName is wrong.")
		end

		if ready and tick >= OrderTick then
			fired = true
			-- Test.ActivateSupportPower is staging, not an assertion: it is the only way to
			-- issue a support-power order from script. Its result IS checked now, because with the
			-- power bought rather than charged there are two separate ways for nothing to happen
			-- -- an empty magazine and a closed gate -- and a silent demo cannot tell them apart.
			-- 'not-ready:0' here would mean the purchase banked and something un-banked it;
			-- EnsurePower above reports the ordinary empty-magazine case on its own.
			--
			-- The power's OWN lobby gate is untouched by this scenario: HighYieldNukeCheckboxEnabled
			-- keeps its shipped default of ON, so a closed gate still means the shipped default
			-- moved rather than that an override failed.
			local result = Test.ActivateSupportPower(USA, PowerKey,
				CPos.New(GroundZero.X, GroundZero.Y))
			if result ~= "issued" then
				Media.DisplayMessage("strike REFUSED at t=" .. tick .. ": " .. result, "DEMO")
			end

			return
		end
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")

	-- Cell centre in world coordinates is cell * 1024 + 512.
	--
	-- OPENS CLOSE, on the same cell and zoom as the 04 frame, then pulls out to the whole playfield
	-- at t=210 -- still 33 ticks before detonation. The first version of this demo opened wide and
	-- took its before-frame there, which made 01 and 04 a different zoom apart: a control framed
	-- differently from the frame it is read against cannot answer a per-cell question, and the
	-- capture came back unreadable for exactly that reason.
	Camera.Position = WPos.New(48 * 1024 + 512, 52 * 1024 + 512, 0)
	Camera.Zoom = math.min(3, Camera.MaxZoom)

	Trigger.AfterDelay(210, function()
		Camera.Position = WPos.New(GroundZero.X * 1024 + 512, GroundZero.Y * 1024 + 512, 0)
		Camera.Zoom = Camera.MinZoom
	end)

	-- CLOSE PASSES for the two scar frames. MinZoom shows the whole 128x128 map, which is right for
	-- reading a 102-cell blast and useless for judging its EDGE -- the first capture came back too
	-- distant to tell a scorched tree cell from an unscorched one, which is the entire question the
	-- scar work turns on. So the demo moves in twice.
	--
	-- Cell 48,52 is a tree cluster 20 cells from ground zero -- forest, well inside the burn.
	Trigger.AfterDelay(880, function()
		Camera.Position = WPos.New(48 * 1024 + 512, 52 * 1024 + 512, 0)
		Camera.Zoom = math.min(3, Camera.MaxZoom)
	end)

	-- Cell 72,51 is the WATERLINE frame, and it has to be its own position rather than a corner of
	-- the forest one: at zoom 3 the viewport is roughly 24 cells across and 48,52 cannot contain any
	-- water at all, which is why the first attempt at reading the shore fade showed no shore.
	--
	-- 72,51 is a BEACH FORD -- cells 71-74 x 50-52 are the only Beach on this map within reach of
	-- the burst, 14 cells from ground zero, with Water immediately north (y 47-49) and south
	-- (y 53-57). The terrain type is the whole point: `Beach` is what the scar work taught to accept
	-- the five Scar* smudges (`tilesets/temperat.yaml`), so this ford is the only place on this map
	-- where the shore fade can be seen at all.
	--
	-- An earlier version of this frame pointed at 78,64, the river bank due east of ground zero.
	-- That was a wasted capture and the reason is worth keeping: those banks are `Rock` and `Cliffs`,
	-- which accept NO smudge type whatsoever -- not Crater, not Scorch, not the Scar types, and not
	-- from any weapon in the mod, which has always been true and is not something the scar work
	-- changed. The frame came back showing a bright unscarred band running the length of the river
	-- and reading exactly like the failure this demo is meant to detect, while testing nothing.
	Trigger.AfterDelay(940, function()
		Camera.Position = WPos.New(72 * 1024 + 512, 51 * 1024 + 512, 0)
		Camera.Zoom = math.min(3, Camera.MaxZoom)
	end)

	-- THE ACTOR-FOOTPRINT FRAME, added 2026-09-09 with the reversal. Cell 45,64 is a ring-3 site
	-- 19 cells from ground zero, and it was picked on three properties rather than by eye:
	--
	--   * TERRAIN. A 15x15 window on it is 94% Clear, 3% Road, 2% Rock -- read statically out of
	--     map.bin with tools/nav-guard/modload.py, not guessed. That matters more than it sounds:
	--     Rock and Cliffs accept NO smudge type from any weapon in the mod and never have, so a
	--     frame pointed at them comes back looking exactly like this feature failing while in fact
	--     testing nothing. That has already cost one capture on this very demo -- see the note on
	--     the 72,51 waterline camera above about the 78,64 river bank.
	--   * IT IS OUTSIDE THE VAPORIZE RADIUS. Warhead@VaporizeRemoval has Radius 6c820, so the
	--     ring-1 sites at 6 cells are REMOVED outright at tick 1 and leave no actor behind to have
	--     blocked anything. A frame there would show continuous scar both before and after this
	--     change and would prove nothing. At 19 cells the actors die normally and leave husks.
	--   * THE FOOTPRINT IS BIG. afld at 45,64 is ^3x2Shape -- a six-cell rectangle, the largest
	--     single hole the old behaviour could punch. Its husk (afld.husk, ^BuildingHusk: 200 HP at
	--     ChangesHealth -10 per 8 ticks) burns out around 160 ticks after it spawns, so by the
	--     capture the footprint is bare ground and whatever is drawn there is the ground itself.
	Trigger.AfterDelay(1000, function()
		Camera.Position = WPos.New(45 * 1024 + 512, 64 * 1024 + 512, 0)
		Camera.Zoom = math.min(3, Camera.MaxZoom)
	end)

	-- Pre-selected so the support-power bin is on screen without the viewer clicking first.
	TestHarness.Select(OwnSR)

	-- CAPTURE, added 2026-09-09. This demo has 167 tree actors on it, which makes it the only
	-- shipped rig where the burn-in-place work merged at 74b559ed can actually be SEEN -- until now
	-- every image of a scorched forest has been a Python composite of the shipped art rather than an
	-- engine frame, and nobody has watched a nuke burn a wood.
	--
	-- Offsets come from the timing table at the head of this file, NOT from taste. t=243 is the
	-- detonation, so 200 is the last clean before-frame; 284 is the fireball's stated second maximum;
	-- 900 is after the last suppression band (t=933 is close but the fires still run to 1743) and is
	-- the first frame where the scorched forest can be read on its own rather than through flame.
	--
	-- t=860 for the wide boundary frame is a CORRECTION, 2026-09-09. That frame used to sit at 420,
	-- inside the window the timing table above calls the most legible. The capture taken there came
	-- back with a ~40-cell pure-white core and the ENTIRE remaining playfield washed pale grey, so
	-- the burnt/unburnt boundary it exists to show was not visible at all -- against the table's own
	-- claim that the flash is white only through ~t=313. That contradiction is unexplained and is
	-- worth chasing separately; moving the frame to 860 sidesteps it rather than resolving it. 860
	-- is past the fireball going out (404) and past the blast wave reaching full radius (840).
	TestHarness.ScreenshotAfter(200 / TestHarness.TicksPerSecond, "01-forest-before",
		"BEFORE. Living forest at cell 48,52, framed at the SAME camera and zoom as 04 -- full " ..
		"canopies, unscorched ground. Read 04 against this frame and nothing else; the wide frames " ..
		"are at a different zoom and cannot answer a per-cell question.")
	TestHarness.ScreenshotAfter(284 / TestHarness.TicksPerSecond, "02-fireball-max",
		"expects: the fireball at its second maximum, whole playfield. Not a burnt-tree frame -- it " ..
		"is here so the scale of the thing doing the burning is on the record beside its effect.")
	TestHarness.ScreenshotAfter(860 / TestHarness.TicksPerSecond, "03-scar-boundary",
		"expects: the whole scar at once -- scorched interior, living forest outside it, and the " ..
		"boundary between them. WIDE, and the flash is long gone by here.")
	TestHarness.ScreenshotAfter(900 / TestHarness.TicksPerSecond, "04-scorched-forest",
		"THE BURNT-TREE FRAME. expects: trees inside the thermal radius still STANDING but leafless " ..
		"and charred, on scorched ground, with living forest further out. FAIL if the trees are " ..
		"gone (they must never be removed -- cover and line of fire depend on it), or if they are " ..
		"paler than the ground they stand on, which is the complaint this work was built to fix.")
	TestHarness.ScreenshotAfter(960 / TestHarness.TicksPerSecond, "05-scar-waterline",
		"THE SHORELINE FRAME. Camera on the beach ford at 72,51 -- Beach cells 71-74 x 50-52 with " ..
		"water immediately above and below. expects: the scar darkens the SAND and THINS over the " ..
		"last two cells into the water. FAIL if a bright unscorched sand band sits between the " ..
		"black scar and the shoreline, or if a full-strength black cell butts straight onto water. " ..
		"The Rock and Cliffs either side of the ford staying bright is NOT a failure -- those two " ..
		"types accept no smudge from any weapon in the mod and never have. REVERSED 2026-09-09: this " ..
		"caption used to say buildings, vehicles and walls must still sit on CLEAN unscorched ground " ..
		"and that losing those holes meant the InvalidTargets line was damaged. The user ruled the " ..
		"other way -- a destroyed building must not mean the ground under it was undisturbed -- so " ..
		"the holes are now the failure, not the pass. Frame 06 is where that is read.")
	TestHarness.ScreenshotAfter(1020 / TestHarness.TicksPerSecond, "06-scar-under-actors",
		"THE REVERSAL FRAME. Camera on the ring-3 site at cell 45,64, 19 cells from ground zero and " ..
		"inside the ScarChar band (17-27 cells). The airfield that stood here is a 3x2 footprint and " ..
		"its husk has long burnt away, so the six cells it occupied are bare ground. expects: those " ..
		"cells are scarred exactly like the ground around them -- no rectangle, no seam, nothing that " ..
		"reads as a footprint. FAIL if a clean unscorched rectangle sits where the airfield was, or " ..
		"where any vehicle or wall stood: that is the pre-2026-09-09 behaviour and it means either " ..
		"IgnoreActors was lost from the Scar warheads or LeaveSmudgeWarhead stopped honouring it. " ..
		"NOT a failure: ground still hidden under a STANDING building or an intact vehicle husk. " ..
		"Smudges are drawn in the terrain pass, before any actor sprite, so a scar under something " ..
		"still standing is marked but covered -- that is draw order and is unchanged by this work.")

	Trigger.AfterDelay(1, step)
end
