-- DEMO -- does the nuclear glow still draw a one-cell band down the top map edge?
--
-- Nothing here is asserted: no AssertWithin, no Test.Pass, no Test.Fail, no result.json. The viewer
-- watches, and closes the window when done.
--
-- THE ARRANGEMENT lives in map.yaml and rules.yaml and is described in full there. In one line:
-- the map is pre-explored and fog is on with NO camera actor, so the whole top edge sits at
-- visibility 1, and ground zero is eighty cells away so the edge falls in the light's falloff
-- rather than under its saturated core.
--
-- THE SCHEDULE, in ticks from WorldLoaded. Timestep is 60 ms, so 16.67 ticks per second -- NOT the
-- 25 that TestHarness.TicksPerSecond carries (that is a harness convention for AssertWithin budgets,
-- documented in test-helpers.lua, and is not the tick rate). Every delay below is therefore raw
-- ticks through Trigger.AfterDelay rather than a seconds conversion.
--
--     t=25    order issued at cell 64,80.
--     t=125   missile enters the world at the south edge (MissileDelay 100, overridden from 500).
--     t=213   DETONATION. 88 ticks of flight over 47 cells at Speed 550, exact rather than
--             estimated: the entry edge is derived from USA's HomeLocation 64,118, so the missile
--             enters at row 127 and flies 127 - 80 = 47 cells; 47 * 1024 / 550 = 87.5, and this
--             missile has Acceleration 0 and no TerminalAcceleration, so BallisticMissileFly does
--             not accelerate it.
--     t=214   fireball.
--     t=283   the three stacked flash warheads finish washing the screen white.
--     t=374   the light envelope ends (161 ticks from detonation).
--
-- SO: SCREENSHOT BETWEEN t=290 AND t=360. Earlier than t=283 and FlashPaletteEffect has the whole
-- screen white, which hides the very thing being looked at; later than t=374 and the LIGHT is over,
-- so the edge is no longer lit by anything and the band cannot be present or absent.
--
-- THE CAMERA IS NOT ON GROUND ZERO, and that is deliberate. This demo is about one cell row, not
-- about the fireball, so the view is parked on the TOP EDGE and zoomed in far enough that a single
-- cell is tens of pixels across. Ground zero is eighty cells south and off screen; its light is
-- what reaches the edge, and the light is the whole subject.
--
-- AFTERWARDS THE MAGAZINE IS EMPTY, and that is the shipped economy rather than a limitation of
-- this demo: rules.yaml leaves USA 120000 cash, so the viewer can buy and place more strikes by
-- hand from the Powers tab.

local OrderTick = 25

-- Eighty cells from the ring row at V = 0. NOT next to the edge: the high-yield light is saturated
-- white for roughly its first forty-five cells, and a saturated row either side of the boundary
-- shows nothing whatever the renderer is doing. See map.yaml.
local GroundZero = { X = 64, Y = 80 }

-- Where the answer is. Row 0 is the unplayable ring; rows 1 and up are playable. Centring here puts
-- a few rows of beyond-grid black at the top of the screen and about twenty playable rows below it.
local EdgeView = { X = 64, Y = 8 }

-- THE PURCHASE, added 2026-09-07, and without it this demo fires nothing at all.
--
-- Every shipped support power carries `RequiresPurchase: True`, which is a BANK and not a timer:
-- at zero banked shots SupportPowerInstance.Disabled is true, Active is false, Ready is false, and
-- Test.ActivateSupportPower returns 'not-ready:0' forever. The `ChargeInterval` and
-- `StartFullyCharged` that this file's rules.yaml used to set were read and discarded
-- (SupportPowerManager.cs:228-229). The whole chain is written out in the header of
-- TestHarness.EnsurePower (mods/ww3mod/scripts/test-helpers.lua).
--
-- rules.yaml supplies the three things a purchase needs: USA's cash, the sandbox lobby option that
-- provides this power's `powers.event` tier (no faction provides it), and a BuildDuration of 5 on
-- the proxy. The magazine is therefore loaded around t=12, thirteen ticks before the t=25 order.
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
			-- issue a support-power order from script. Its result IS checked, because with the
			-- power bought rather than charged there are two separate ways for nothing to happen
			-- -- an empty magazine and a closed gate -- and a silent demo cannot tell them apart.
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
	Camera.Position = WPos.New(EdgeView.X * 1024 + 512, EdgeView.Y * 1024 + 512, 0)

	-- Camera.Zoom is a MULTIPLE OF THE FULLY-OUT level and is clamped to Camera.MinZoom..MaxZoom by
	-- the engine (CameraGlobal.cs:42), so asking for 3 is safe on any display: a viewer whose
	-- window cannot reach it gets the closest it can. Zoomed IN rather than out, unlike the sibling
	-- demo-nuke-fog-seam -- the subject here is ONE CELL ROW, and at the fully-out level used there
	-- a single row is a couple of pixels tall and the band is invisible whether or not it is drawn.
	Camera.Zoom = 3

	-- Pre-selected so the support-power bin is on screen without the viewer clicking first.
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
