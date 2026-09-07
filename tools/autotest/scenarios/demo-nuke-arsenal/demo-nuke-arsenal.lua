-- DEMO -- the whole nuclear arsenal fired in ascending yield at one aim point. Nothing here is
-- asserted: no AssertWithin, no Test.Pass, no Test.Fail, no result.json. The viewer watches and
-- closes the window when done.
--
-- LAYOUT, so this file can be read without map.yaml: ground zero is cell 64,64, the exact centre of
-- a 128x128 playfield. 142 target sites sit in thirteen rings around it at 3, 7, 12, 18, 23, 28, 33,
-- 40, 48, 57, 65, 74 and 82 cells -- radii chosen to STRADDLE the six blast radii, so every shot
-- kills a band the one before it could not reach. Each site is the same mix (one structure, one
-- vehicle, two infantry, two trees), so distance from the burst is the only variable. USA's Supply
-- Route is on the south edge at 64,118; Russia's is in the far north-west at 10,10. Both survive
-- every shot: their only target type is NoAutoTarget and no arsenal warhead lists it.
--
-- THE SCHEDULE, in ticks from WorldLoaded. Timestep is 60 ms, so 16.67 ticks per second -- NOT the
-- 25 that TestHarness.TicksPerSecond carries (that is a deliberately-preserved harness convention
-- for AssertWithin budgets, documented in test-helpers.lua, and is not the tick rate). Every delay
-- below is therefore raw ticks through Trigger.AfterDelay, never a seconds conversion.
--
-- Each impact tick is order + MissileDelay(60, overridden in rules.yaml) + 64512/Speed, and that
-- flight term is EXACT rather than estimated because no arsenal missile sets TerminalAcceleration.
-- Expect +-5 ticks: SpawnActorEffect adds on the tick its counter goes negative and the effect is
-- installed by a frame-end task.
--
--   #  weapon              yield     order   impact   blast   wave ends   what it kills
--   1  B61-12 (0.3 kt)    0.3 kt        30      161     4.5c        192   ring 3 only
--   2  B61-12 (50 kt)      50 kt       220      351    24.6c        523   rings 7-23
--   3  W76-1              100 kt       560      666    31.0c        883   ring 28
--   4  RS-28 Sarmat    6x750 kt       920     1020    60.7c       1444   rings 33-57, as SIX
--                                                                         separate fireballs
--   5  B83-1              1.2 Mt      1480     1632    71.0c       2128   ring 65
--   6  Tsar Bomba          50 Mt      2170     2391   246.1c       4113   everything left
--
-- SO THE RUN IS ~4113 TICKS, about 4 minutes 7 seconds. Each shot's blast wave finishes before the
-- next is ordered, so nothing overlaps and each ring can be attributed to one weapon.
--
-- THE FRAMES WORTH CAPTURING, if you are screenshotting rather than watching:
--   t=360   shot 2 just landed -- the 0.3 kt crater from shot 1 is still visible inside it, which
--           is the clearest single image of dial-a-yield in the arsenal.
--   t=1100  shot 4's six Sarmat fireballs, before their waves merge.
--   t=2450  Tsar Bomba's fireball at full size: 15.9 cells of radius, burning for 417 ticks.
--   t=3000  Tsar Bomba's wavefront around 87 cells out, past everything the other five could reach.
--
-- THE CAMERA CANNOT BE ZOOMED FROM LUA -- CameraGlobal exposes Position and nothing else. This
-- centres on ground zero; zoom out manually. On a 128x128 map the default zoom shows about a third
-- of the playfield, and Tsar Bomba's blast is nearly twice the whole map, so the last shot cannot
-- be framed at any zoom. That is the weapon, not the staging.
--
-- THE SIX WARHEADS ARE BOUGHT, NOT CHARGED, and before 2026-09-07 that is why this demo fired
-- NOTHING. Every shipped support power carries `RequiresPurchase: True`, which replaces the timer
-- with a magazine: at zero banked shots the power is Disabled, therefore not Ready, and
-- Test.ActivateSupportPower returns 'not-ready:0'. The `ChargeInterval: 1` + `StartFullyCharged`
-- pairs this file's rules.yaml used to set on all six were read and discarded by
-- SupportPowerInstance's constructor (SupportPowerManager.cs:228-229) -- staging that looked
-- deliberate and did nothing. The chain is written out in the header of TestHarness.EnsurePower
-- (mods/ww3mod/scripts/test-helpers.lua).
--
-- SO THE SCRIPT NOW BUYS EACH WARHEAD FIVE TICKS AFTER THE PREVIOUS ONE IS FIRED, through the real
-- Powers queue at the Supply Route, exactly as a player does. rules.yaml supplies the 250000
-- credits, ticks the sandbox lobby option that provides the three EVENT-tier warheads' prerequisite
-- (no faction provides `powers.event`), and cuts each proxy's BuildDuration to 5 ticks.
--
-- THE ORDER SCHEDULE ABOVE DID NOT MOVE BY ONE TICK, and that is the point of buying ahead rather
-- than up front: a purchase completes about seven ticks after it is queued, so every magazine is
-- loaded with between 18 and 678 ticks to spare. Every impact, wave-end and capture tick in the
-- table above still holds. The Powers queue builds Queue[0] and only Queue[0]
-- (ProductionQueue.cs:338-345), which is why the buys are staggered instead of issued together.
--
-- AFTERWARDS EACH MAGAZINE IS EMPTY -- one purchase is one shot, and the cameo leaves the bin when
-- it is spent. 75000 credits are left over, so the viewer can re-buy the smaller warheads from the
-- Powers tab and fire them again wherever they like.

local GroundZero = { X = 64, Y = 64 }

-- order tick, OrderName, buy proxy, tick to start buying, label for the in-game chat line.
--
-- THE BUY TICK IS ALWAYS `previous shot + 5`, never earlier: the queue has to be empty for the
-- next order to be accepted, and the previous purchase has left it by then. The first is 5 rather
-- than 1 because a production property must not be touched before the queue's first Tick has found
-- a producer -- see TestHarness.ProductionWarmupTicks, where getting that wrong fails permanently
-- rather than transiently.
local SHOTS = {
	{ 30, "B61LowStrike", "power.b61low", 5, "1/6  B61-12 at 0.3 kt  -- 4.5 cell blast" },
	{ 220, "B61MaxStrike", "power.b61max", 35, "2/6  B61-12 at 50 kt   -- 24.6 cells: same bomb, dial turned up" },
	{ 560, "W76Strike", "power.w76", 225, "3/6  W76-1 at 100 kt   -- 31.0 cells" },
	{ 920, "SarmatStrike", "power.sarmat", 565, "4/6  RS-28 Sarmat      -- SIX 750 kt warheads across 21 cells" },
	{ 1480, "B83Strike", "power.b83", 925, "5/6  B83-1 at 1.2 Mt   -- 71.0 cells, largest in the US stockpile" },
	{ 2170, "TsarBombaStrike", "power.tsarbomba", 1485, "6/6  TSAR BOMBA 50 Mt  -- 246 cells, 25-second fireball" },
}

-- How long a shot keeps retrying after its scheduled tick before the demo gives up on it and moves
-- to the next. Without this a single unbuyable warhead would stall the whole arsenal behind it.
local ShotPatience = 200

local tick = 0
local USA
local next_shot = 1
local next_buy = 1
local buy_status = "not-started"

local function step()
	tick = tick + 1

	-- BUY AHEAD, one warhead at a time. EnsurePower is stateless and idempotent: it reads the
	-- power's own state, queues a purchase when the magazine is empty, and reports 'loading' while
	-- one is in flight. Nothing is printed here -- the shot line below carries the diagnosis if a
	-- warhead does not arrive, and this demo's output is a frame.
	local buying = SHOTS[next_buy]
	if buying ~= nil and tick >= buying[4] then
		local ready, status = TestHarness.EnsurePower(USA, buying[3], buying[2], tick)
		buy_status = status
		if ready then
			next_buy = next_buy + 1
			buy_status = "banked"
		end
	end

	local shot = SHOTS[next_shot]
	if shot ~= nil and tick >= shot[1] then
		-- Test.ActivateSupportPower is staging, not an assertion: it is the only way to issue a
		-- support-power order from script. Its result IS read, because with the powers bought
		-- rather than charged there are two separate ways for nothing to happen -- an empty
		-- magazine and a closed gate -- and the retry below needs to know which.
		local status = Test.ActivateSupportPower(USA, shot[2], CPos.New(GroundZero.X, GroundZero.Y))
		if status == "issued" then
			next_shot = next_shot + 1
			Media.DisplayMessage(shot[5], "ARSENAL")
		elseif tick >= shot[1] + ShotPatience then
			-- Give up on this warhead so the rest of the arsenal still runs on schedule. The buy
			-- status is printed alongside because it is what says WHY: 'refused' means the buy tab
			-- would not take the order (check PowersSandboxCheckboxEnabled and DefaultCash in
			-- rules.yaml), 'absent' means the OrderName is wrong, 'loading' means 200 ticks was not
			-- enough for a 5-tick build and something is pausing the Supply Route.
			next_shot = next_shot + 1
			Media.DisplayMessage(shot[5] .. "  [NOT FIRED: " .. status
				.. ", magazine " .. buy_status .. "]", "ARSENAL")
		end
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")

	-- Cell centre in world coordinates is cell * 1024 + 512.
	Camera.Position = WPos.New(GroundZero.X * 1024 + 512, GroundZero.Y * 1024 + 512, 0)

	-- Pre-selected so the support-power bin is on screen without the viewer clicking first.
	TestHarness.Select(OwnSR)

	Media.DisplayMessage("Nuclear arsenal: six warheads, ascending yield, one aim point. "
		.. "ZOOM OUT.", "ARSENAL")

	Trigger.AfterDelay(1, step)
end
