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
-- The powers stay charged (rules.yaml sets ChargeInterval 1), so after the script finishes the
-- viewer can fire any of the six again, anywhere, as often as they like.

local GroundZero = { X = 64, Y = 64 }

-- order tick, OrderName, label for the in-game chat line
local SHOTS = {
	{ 30, "B61LowStrike", "1/6  B61-12 at 0.3 kt  -- 4.5 cell blast" },
	{ 220, "B61MaxStrike", "2/6  B61-12 at 50 kt   -- 24.6 cells: same bomb, dial turned up" },
	{ 560, "W76Strike", "3/6  W76-1 at 100 kt   -- 31.0 cells" },
	{ 920, "SarmatStrike", "4/6  RS-28 Sarmat      -- SIX 750 kt warheads across 21 cells" },
	{ 1480, "B83Strike", "5/6  B83-1 at 1.2 Mt   -- 71.0 cells, largest in the US stockpile" },
	{ 2170, "TsarBombaStrike", "6/6  TSAR BOMBA 50 Mt  -- 246 cells, 25-second fireball" },
}

local tick = 0
local USA
local next_shot = 1

local function step()
	tick = tick + 1

	local shot = SHOTS[next_shot]
	if shot ~= nil and tick >= shot[1] then
		next_shot = next_shot + 1
		-- Test.ActivateSupportPower is staging, not an assertion: it is the only way to issue a
		-- support-power order from script. Its result is announced rather than checked, so a power
		-- that is missing from the bin says so on screen instead of failing silently -- which is
		-- what a wrong lobby default or a broken RequiresCondition chain would look like.
		local status = Test.ActivateSupportPower(USA, shot[2], CPos.New(GroundZero.X, GroundZero.Y))
		if status == "issued" then
			Media.DisplayMessage(shot[3], "ARSENAL")
		else
			Media.DisplayMessage(shot[3] .. "  [NOT FIRED: " .. status .. "]", "ARSENAL")
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
