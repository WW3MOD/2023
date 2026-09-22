-- OFFLINE DRIVER for tools/autotest/scenarios/demo-nuke-perf-populated.
--
-- Runs that scenario's Lua to completion against STUBBED engine bindings, on the host's own
-- interpreter. NO BUILD, NO GAME, NO LAUNCH SLOT. It exercises everything in the file that is not
-- an engine call: the window bounds, the histogram and percentile maths, the firing state machine,
-- the salvo cross-check and the report.
--
-- IT HAS ALREADY EARNED ITS KEEP. Two bugs that would each have cost a launch slot were found here
-- and nowhere else -- a combat baseline sampled 58 ticks before the order existed (fatal,
-- `arithmetic on a nil value` at the closing sample) and a cross-check so tight that a
-- fully-landed salvo read as a miss. See WORKSPACE/DISCOVERIES.md, 2026-09-22.
--
--   lua tools/nuke-perf/drive-populated.lua                        # green arm
--   SALVO_LANDS=0 lua tools/nuke-perf/drive-populated.lua          # nothing detonates
--   ARMY=8        lua tools/nuke-perf/drive-populated.lua          # under ArmyFloor
--   FIRE_OK=0     lua tools/nuke-perf/drive-populated.lua          # the power never banks
--
-- Read the `NUKEPOP valid` / `NUKEPOP invalid` lines: the last three arms must each produce the
-- invalidation they are named for, and the first must produce none.
--
-- WHAT IT CANNOT TELL YOU, and the trap is specific. The host interpreter here is Lua 5.5; Eluant
-- binds an older Lua. 5.5 ACCEPTS constructs the engine rejects -- floor division `//` above all,
-- which test-escalation-full-match.lua:52 records as the one such occurrence ever committed. So a
-- green parse here is evidence about LOGIC and never about DIALECT. It also stubs every binding,
-- so it says nothing about whether a real Test.* call behaves as assumed.
--
-- Run it from the repository root.

-- Offline driver: exercises demo-nuke-perf-populated.lua's pure-Lua logic against stubbed
-- bindings. NOT a game launch. Validates window bounds, percentiles, the firing state machine
-- and the report, which is everything in the file that is not an engine call.

local ARMY = tonumber(os.getenv("ARMY") or "40")
local FIRE_OK = (os.getenv("FIRE_OK") or "1") == "1"

TestHarness = {
  TicksPerSecond = 1000/60,
  ProductionWarmupTicks = 5,
  ApproachMarginCells = 16,
}
function TestHarness.ApproachStandoffCells(x, y)
  return math.sqrt(x*x + y*y) + TestHarness.ApproachMarginCells
end
local bankTick = nil
function TestHarness.EnsurePower(player, proxy, key, tick)
  if not FIRE_OK then return false, "refused" end
  if bankTick == nil then bankTick = tick + 6; return false, "buying" end
  if tick >= bankTick then bankTick = nil; return true, "ready" end
  return false, "loading"
end

local curTick = 0
local impactCount = 0
local windowsHot = {}   -- ticks where we pretend a detonation is live

Test = {}
function Test.GetTickTimeMs()
  local base = 18 + (curTick % 7)          -- 18..24 ms steady state
  if windowsHot[curTick] then return base + 55 + (curTick % 11) end
  return base
end
function Test.GetImpactEffectCount() return impactCount end
function Test.ActivateSupportPower(p, key, cell) return "issued" end
local skipped = nil
function Test.Skip(reason) skipped = reason end

local function mkActor(x, y) return { Location = { X = x, Y = y } } end
local function mkPlayer(name)
  local p = { Name = name }
  function p.GetGroundAttackers()
    local t = {}
    for i = 1, ARMY do t[i] = mkActor(40 + (i % 12), 40 + math.floor(i / 12)) end
    return t
  end
  return p
end
Player = {}
local players = { ["USA-bot"] = mkPlayer("USA-bot"), ["Russia-bot"] = mkPlayer("Russia-bot") }
function Player.GetPlayer(n) return players[n] end

CPos = {}; function CPos.New(x, y) return { X = x, Y = y } end
WPos = {}; function WPos.New(x, y, z) return { X = x, Y = y, Z = z } end
Camera = { MinZoom = 0.2, Position = nil, Zoom = nil }
UserInterface = { SetMissionText = function() end }

local tickFn = nil
Trigger = { OnTick = function(f) tickFn = f end }

dofile("tools/autotest/scenarios/demo-nuke-perf-populated/demo-nuke-perf-populated.lua")
WorldLoaded()

-- Detonations are "hot" for 400 ticks from the derived impact ticks, so the synthetic tick_time
-- rises exactly where the scenario should be pointing its windows.
for t = 8158, 8558 do windowsHot[t] = true end
for t = 9758, 10188 do windowsHot[t] = true end

local SALVO = {}   -- tick -> extra gated impacts from a warhead
if os.getenv("SALVO_LANDS") ~= "0" then
  for _, base in ipairs({8158, 9758, 9765}) do
    for i = 0, 5 do SALVO[base + i * 12] = (SALVO[base + i * 12] or 0) + 1 end
  end
end

for t = 1, 11000 do
  curTick = t
  if t % 9 == 0 then impactCount = impactCount + 1 end        -- ordinary combat, every 9 ticks
  impactCount = impactCount + (SALVO[t] or 0)                  -- warheads
  if skipped == nil then tickFn() end
end

print("DRIVER skip=" .. tostring(skipped))
