-- DEMO -- does the brightened fireball still stop dead at the edge of what has ever been seen?
--
-- Nothing here is asserted: no AssertWithin, no Test.Pass, no Test.Fail, no result.json. The viewer
-- watches, and closes the window when done.
--
-- THE ARRANGEMENT lives in map.yaml and rules.yaml and is described in full there. In one line:
-- the map is NOT pre-explored and fog is on, and one indestructible CAMERA twenty-five cells
-- west of ground zero puts the eastern edge of the explored disc just seven cells east of
-- the burst.
--
-- THE SCHEDULE, in ticks from WorldLoaded. Timestep is 60 ms, so 16.67 ticks per second -- NOT the
-- 25 that TestHarness.TicksPerSecond carries (that is a harness convention for AssertWithin budgets,
-- documented in test-helpers.lua, and is not the tick rate). Every delay below is therefore raw
-- ticks through Trigger.AfterDelay rather than a seconds conversion.
--
--     t=25    order issued at cell 64,64.
--     t=125   missile enters the world at the south edge (MissileDelay 100, overridden from 500).
--     t=243   DETONATION, 15c0 above the aim point. 118 ticks of flight over 63 cells at Speed 550,
--             exact rather than estimated: this missile has Acceleration 0 and no
--             TerminalAcceleration, so BallisticMissileFly does not accelerate it.
--     t=244   fireball at 700% scale. THIS IS THE FRAME THIS DEMO EXISTS FOR.
--     t=313   the three stacked flash warheads finish washing the screen white.
--
-- SO: SCREENSHOT BETWEEN t=320 AND t=500 -- 19 s to 30 s after the map loads. Earlier than t=313
-- and FlashPaletteEffect has the whole screen white, which hides the very thing being looked at;
-- the cloud is still full-height and legible well past t=500.
--
-- The camera is placed and zoomed by this script.
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

	-- Fully out. Camera.Zoom is a MULTIPLE OF THE DEFAULT level, so Camera.MinZoom is as far out as
	-- this display goes -- read rather than hardcoded, because the floor is derived from the
	-- viewer's resolution and viewport-distance setting. A 700%-scale mushroom cloud is wider than
	-- the default framing on a 128x128 map, and a demo about a boundary running THROUGH the cloud
	-- is worth nothing if either side of that boundary is off screen.
	Camera.Zoom = Camera.MinZoom

	-- Pre-selected so the support-power bin is on screen without the viewer clicking first.
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
