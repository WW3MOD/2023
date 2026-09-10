-- DEMO -- the high-yield strategic nuclear strike, fired on the SHIPPED River Zeta map.
-- Nothing here is asserted: no AssertWithin, no Test.Pass, no Test.Fail, no result.json.
--
-- WHY THIS EXISTS, 2026-09-10. The user rejected the evidence for the shore-fade fix and was right
-- to: every frame and every offline render of that work came from `demo-highyield-nuke`, a
-- purpose-built 128x128 rig. Their objection, verbatim: water on real maps is never just placed
-- like that without neighbouring shoreline/beach tiles, so a screenshot of that map says nothing
-- about how it will look in a real game.
--
-- The terrain census backs them, though not for the reason first assumed -- the demo rig is NOT
-- beachless. Counted out of map.bin with tools/nav-guard/modload.py:
--
--     demo-highyield-nuke   Clear 12294  Cliffs 1237  Rock 1148  Road 526  Water 315
--                           Rough 225  Beach 84  River 47
--     river-zeta-ww3        Clear 5948  Road 706  Water 579  Beach 293  Rock 87
--                           Rough 26  Bridge 24  River 17
--
-- 84 Beach cells against 315 Water on the rig, and they sit at ONE ford; 293 against 579 here, and
-- they follow the water everywhere it goes. That ratio is the whole difference, and it is why this
-- map can answer the question and the rig cannot.
--
-- THE OFFLINE RENDERER CANNOT SUBSTITUTE FOR THIS FRAME, which was worth learning the hard way.
-- tools/impact-scar/shore_fade_preview.py reproduces the engine's per-cell DECISIONS exactly, and
-- it draws Beach, Rock and Cliffs with the CLEAR ground art because the extracted tile cache holds
-- no sand or cliff templates. Pointed at this map it produced two panels that were pixel-identical
-- and contained no shoreline at all. It models the mechanism, not the tileset; a shoreline IS the
-- tileset. Only an engine frame settles this.
--
-- ---- LAYOUT ------------------------------------------------------------------------------------
-- The map is the shipped 98x82 River Zeta, unmodified: same map.bin, same 4544 neutral actors,
-- same terrain. Only the header and the player list differ from mods/ww3mod/maps/river-zeta-ww3 --
-- every actor on it is Owner: Neutral, so replacing the six Multi slots with USA and Russia
-- orphans nothing. USA's home is the north-west mpspawn at 16,6, Russia's the south-east at 81,76.
--
-- GROUND ZERO IS CELL 38,20, and it was picked on the census rather than by eye. A 27x27 window on
-- it holds 203 Water and 102 Beach cells against 424 land -- the densest shoreline on the map.
-- The aim cell ITSELF is Beach (a 15x15 window on it reads 47% Water, 43% Clear, 10% Beach), which
-- is deliberate and is also fine: Beach accepts all five Scar smudge types. The map's geometric
-- centre (49,41) is WATER and is not used. Camera cell 38,14 is Clear; 45,40 is Beach.
--
-- THE RIVER BANK TRAP, inherited from the rig's own hard-won note and repeated here because it
-- would cost this capture the same way: Rock and Cliffs accept NO smudge type from any weapon in
-- the mod and never have. A frame pointed at them comes back showing a bright unscarred band along
-- the water that reads exactly like the bug this demo exists to look for, while testing nothing.
-- Both close cameras below are on Beach/Clear, verified from map.bin, not chosen by looking.
--
-- ---- THE SCHEDULE ------------------------------------------------------------------------------
-- Timestep is 60 ms, so 16.67 ticks per second -- NOT the 25 that TestHarness.TicksPerSecond
-- carries (that is a preserved harness convention for AssertWithin budgets, documented in
-- test-helpers.lua, and is not the tick rate).
--
-- THE DETONATION TICK BELOW IS DERIVED, NOT MEASURED, and this map is smaller than the rig so it
-- does not inherit the rig's numbers. The missile is born off-map at
--     standoff = mapDiagonal + ApproachMargin
--     mapDiagonal = 1024 * sqrt(98^2 + 82^2) ~= 130867 wdist
--     standoff ~= 130867 + 16384 = 147251 wdist
-- and HighYieldNukeMissile has Acceleration 0 and no TerminalAcceleration, so at Speed 550 the
-- flight is 147251 / 550 ~= 268 ticks and that division is exact rather than an estimate.
--
--     t=25    order issued at 38,20. Beacon and launch notification fire now.
--     t=125   missile enters the world (MissileDelay 100, overridden from the shipped 500).
--     t~=393  DETONATION.  25 + 100 + 268.
--     t~=434  fireball second maximum, ~41 ticks after the burst on the rig's measured profile.
--     t~=797  fireball out (+404 on the rig's profile, which is a property of the warhead).
--     t~=1233 blast wave at full radius (+840).
--
-- EVERY FRAME BELOW IS OFFSET FROM t=393 USING THE RIG'S MEASURED PROFILE. If the strike lands at
-- a different tick the frames slide with it and the captions stop matching; the demo says so in
-- chat rather than failing, because nothing here is asserted. Re-derive before trusting a tick.

local OrderTick = 25
local GroundZero = { X = 38, Y = 20 }
local Detonation = 393

local Proxy = "power.highyieldnuke"
local PowerKey = "HighYieldNukeStrike"

local tick = 0
local USA
local fired = false
local warned = false

local function complain(text)
	if not warned then
		warned = true
		Media.DisplayMessage(text, "DEMO")
	end
end

local function step()
	tick = tick + 1

	if not fired then
		local ready, status = TestHarness.EnsurePower(USA, Proxy, PowerKey, tick)

		if not ready and tick > OrderTick then
			complain("no shot in the magazine at t=" .. tick .. " (" .. status .. "). 'refused'"
				.. " means the buy tab would not take the order -- check PowersSandboxCheckboxEnabled"
				.. " and DefaultCash in rules.yaml; 'absent' means the OrderName is wrong.")
		end

		if ready and tick >= OrderTick then
			fired = true
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

	-- OPENS ON THE SHORELINE, at the same cell and zoom as the after-frame. A control framed
	-- differently from the frame it is read against cannot answer a per-cell question -- that
	-- mistake cost the rig a whole capture, and the fix is simply to open where frame 04 will sit.
	Camera.Position = WPos.New(38 * 1024 + 512, 14 * 1024 + 512, 0)
	Camera.Zoom = math.min(3, Camera.MaxZoom)

	-- Wide for the burst itself.
	Trigger.AfterDelay(Detonation - 40, function()
		Camera.Position = WPos.New(GroundZero.X * 1024 + 512, GroundZero.Y * 1024 + 512, 0)
		Camera.Zoom = Camera.MinZoom
	end)

	-- CLOSE PASS 1 -- the northern shoreline at 38,14, six cells from ground zero and deep inside
	-- the core. Water immediately north, Beach between. This is the frame the user asked for.
	Trigger.AfterDelay(Detonation + 660, function()
		Camera.Position = WPos.New(38 * 1024 + 512, 14 * 1024 + 512, 0)
		Camera.Zoom = math.min(3, Camera.MaxZoom)
	end)

	-- CLOSE PASS 2 -- the beach at 45,40, 21 cells out and therefore in a WEAKER band than the
	-- first. The fade and the band coverage are different mechanisms and this is where they meet:
	-- a shoreline drawn at ScarChar strength rather than ScarCore strength.
	Trigger.AfterDelay(Detonation + 780, function()
		Camera.Position = WPos.New(45 * 1024 + 512, 40 * 1024 + 512, 0)
		Camera.Zoom = math.min(3, Camera.MaxZoom)
	end)

	TestHarness.ScreenshotAfter((Detonation - 50) / TestHarness.TicksPerSecond, "01-shore-before",
		"BEFORE. The northern shoreline at cell 38,14, framed at the SAME camera and zoom as 04. " ..
		"Unscorched ground, sand, water. Read 04 against this frame and nothing else.")

	TestHarness.ScreenshotAfter((Detonation + 41) / TestHarness.TicksPerSecond, "02-fireball-max",
		"expects: the fireball at its second maximum over a real map. Here for scale, not for " ..
		"reading the scar.")

	TestHarness.ScreenshotAfter((Detonation + 620) / TestHarness.TicksPerSecond, "03-scar-wide",
		"expects: the whole scar over River Zeta at once -- the burn, the river running through " ..
		"it, and the unburnt ground outside. WIDE; the flash is long gone by here.")

	TestHarness.ScreenshotAfter((Detonation + 700) / TestHarness.TicksPerSecond, "04-shore-core",
		"THE FRAME THIS DEMO EXISTS FOR. Camera on the northern shoreline at 38,14, six cells from " ..
		"ground zero, on real authored terrain with real beach tiles between land and water. " ..
		"expects: the scar darkens the sand and thins over the last cells into the water, with no " ..
		"straight edges that do not follow the shoreline. FAIL if a bright unscorched sand band " ..
		"sits between the black scar and the water, or if the boundary is made of axis-aligned " ..
		"rectangles rather than following the coast. NOT a failure: Rock and Cliffs staying bright " ..
		"-- those accept no smudge from any weapon in the mod and never have.")

	TestHarness.ScreenshotAfter((Detonation + 820) / TestHarness.TicksPerSecond, "05-shore-outer",
		"THE WEAKER-BAND SHORELINE. Camera on the beach at 45,40, 21 cells out, where the scar is " ..
		"drawn at a lower band coverage than in 04. expects: the same shape of transition, fainter. " ..
		"FAIL if the fade reads fine at full strength and blocky here -- that would mean the two " ..
		"mechanisms interact, which nothing in the code predicts.")

	-- START THE PURCHASE LOOP. Omitting this is how the first two runs of this demo fired
	-- nothing at all: step() was defined, never called, and the only symptom was cash sitting
	-- untouched at its starting value while every camera and every screenshot worked. The
	-- string `Trigger.AfterDelay(1, step)` also appears INSIDE step(), which is how a guard
	-- grepping for it concluded the loop was already started and skipped the fix.
	Trigger.AfterDelay(1, step)
end
