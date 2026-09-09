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
	Camera.Position = WPos.New(GroundZero.X * 1024 + 512, GroundZero.Y * 1024 + 512, 0)
	Camera.Zoom = Camera.MinZoom

	-- CLOSE PASS for the scar-coverage frame, added 2026-09-09. MinZoom shows the whole 128x128 map,
	-- which is right for reading a 102-cell blast and useless for judging its EDGE -- the first
	-- capture of this demo came back too distant to tell a scorched tree cell from an unscorched
	-- one, which is the entire question the scar work turns on. So the last frame moves in.
	--
	-- Cell 48,52 rather than ground zero: a tree cluster with the river below it, so forest and
	-- shoreline are in one frame. Ground zero is bare and would answer neither.
	Trigger.AfterDelay(880, function()
		Camera.Position = WPos.New(48 * 1024 + 512, 52 * 1024 + 512, 0)
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
	-- 420 sits in the window this file already calls the most legible, with the inner rings gone, the
	-- middle band burning and the outer rings still intact; 900 is after the last suppression band
	-- (t=933 is close but the fires still run to 1743) and is the first frame where the scorched
	-- forest can be read on its own rather than through flame.
	TestHarness.ScreenshotAfter(200 / TestHarness.TicksPerSecond, "01-forest-before",
		"BEFORE. Living forest around ground zero -- full canopies, no char. This is the control " ..
		"frame the three after it are read against.")
	TestHarness.ScreenshotAfter(284 / TestHarness.TicksPerSecond, "02-fireball-max",
		"expects: the fireball at its second maximum. Not a burnt-tree frame -- it is here so the " ..
		"scale of the thing doing the burning is on the record beside its effect.")
	TestHarness.ScreenshotAfter(420 / TestHarness.TicksPerSecond, "03-three-states",
		"expects: three states at once -- inner rings gone, middle band burning, outer rings still " ..
		"intact and green. The boundary between burnt and unburnt forest is the thing to look at.")
	TestHarness.ScreenshotAfter(900 / TestHarness.TicksPerSecond, "04-scorched-forest",
		"THE BURNT-TREE FRAME. expects: trees inside the thermal radius still STANDING but leafless " ..
		"and charred, on scorched ground, with living forest further out. FAIL if the trees are " ..
		"gone (they must never be removed -- cover and line of fire depend on it), or if they are " ..
		"paler than the ground they stand on, which is the complaint this work was built to fix.")

	Trigger.AfterDelay(1, step)
end
