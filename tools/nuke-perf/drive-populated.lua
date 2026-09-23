-- OFFLINE DRIVER for tools/autotest/scenarios/demo-nuke-perf-populated.
--
-- Runs that scenario's Lua to completion against STUBBED engine bindings, on the host's own
-- interpreter. NO BUILD, NO GAME, NO LAUNCH SLOT. It exercises everything in the file that is not
-- an engine call: the window bounds, the histogram and percentile maths, the firing state machine,
-- the pair's rebuild trigger, the arrival bookkeeping and the report.
--
-- IT HAS ALREADY EARNED ITS KEEP TWICE OVER. Two bugs found here and nowhere else before the first
-- launch -- a combat baseline sampled 58 ticks before the order existed (fatal, `arithmetic on a
-- nil value` at the closing sample) and a cross-check so tight that a fully-landed salvo read as a
-- miss. What it did NOT catch is the one that cost run 260923_084012: the scenario's DERIVED impact
-- offset was 60 ticks late, and this driver was synthesising its fake detonations at the same
-- derived tick the scenario was looking at. A stub that agrees with the code's assumption cannot
-- test the assumption. SALVO_AT below exists to break that agreement, and the mis-window arm
-- reproduces the real run.
--
--   lua tools/nuke-perf/drive-populated.lua                        # green arm
--   SALVO_LANDS=0 lua tools/nuke-perf/drive-populated.lua          # nothing ever arrives
--   ARMY=8        lua tools/nuke-perf/drive-populated.lua          # under ArmyFloor
--   FIRE_OK=0     lua tools/nuke-perf/drive-populated.lua          # the power never banks
--   SALVO_AT=98   lua tools/nuke-perf/drive-populated.lua          # the 260923 geometry: the
--                                                                  # warheads land 60 ticks before
--                                                                  # the old derivation said
--   SALVO_AT=158  lua tools/nuke-perf/drive-populated.lua          # as if the sandbox did NOT drop
--                                                                  # the launch delay
--   REBUILD=0     lua tools/nuke-perf/drive-populated.lua          # the army never comes back, so
--                                                                  # the pair fires on its deadline
--
-- Read the `NUKEPOP valid` / `NUKEPOP invalid` / `NUKEPOP note` lines. The green arm must produce
-- no invalidation and must report deton1 opening on the SAME tick the driver detonated at; the
-- SALVO_LANDS=0 arm must produce the never-arrived invalidation; REBUILD=0 must produce the
-- deadline note.
--
-- WHAT IT CANNOT TELL YOU, and the trap is specific. The host interpreter here is Lua 5.5; Eluant
-- binds an older Lua. 5.5 ACCEPTS constructs the engine rejects -- floor division `//` above all,
-- which test-escalation-full-match.lua:52 records as the one such occurrence ever committed. So a
-- green parse here is evidence about LOGIC and never about DIALECT. It also stubs every binding,
-- so it says nothing about whether a real Test.* call behaves as assumed -- which is exactly the
-- hole SALVO_AT exists to keep visible.
--
-- Run it from the repository root.

local ARMY      = tonumber(os.getenv("ARMY") or "40")
local FIRE_OK   = (os.getenv("FIRE_OK") or "1") == "1"
-- Ticks from the order to the FIRST warhead arrival. 98 is what the shipped geometry actually
-- produces under the powers sandbox (standoff / Speed, with MissileDelay dropped).
local SALVO_AT  = tonumber(os.getenv("SALVO_AT") or "98")
-- Whether the target army grows back after a salvo deletes it, and how fast (attackers per 1000
-- ticks). Run 260923_084012 measured ~14.
local REBUILD   = tonumber(os.getenv("REBUILD") or "14")

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
local arrivalCount = 0
local lastArrivalTick = -1
local windowsHot = {}   -- ticks where we pretend a detonation is live

Test = {}
function Test.GetTickTimeMs()
  local base = 18 + (curTick % 7)          -- 18..24 ms steady state
  if windowsHot[curTick] then return base + 55 + (curTick % 11) end
  return base
end
function Test.GetImpactEffectCount() return impactCount end
function Test.GetBallisticMissileImpactCount(actorType)
  -- The scenario must pass the type; a driver that ignored it would hide a missing argument.
  assert(actorType == "sarmatmissile", "expected the scenario to key arrivals on sarmatmissile")
  return arrivalCount
end
function Test.GetLastBallisticMissileImpactTick(actorType)
  assert(actorType == "sarmatmissile", "expected the scenario to key arrivals on sarmatmissile")
  return lastArrivalTick
end
function Test.ActivateSupportPower(p, key, cell) return "issued" end
local skipped = nil
function Test.Skip(reason) skipped = reason end

-- The target army: ARMY until the first salvo deletes it, then a linear rebuild at REBUILD per
-- 1000 ticks. This is what the pair's trigger is being tested against.
local wipedAt = nil
local function targetArmySize()
  if wipedAt == nil then return ARMY end
  if REBUILD <= 0 then return 1 end
  return 1 + math.floor((curTick - wipedAt) * REBUILD / 1000)
end

local function mkActor(x, y) return { Location = { X = x, Y = y } } end
local function mkPlayer(name, sizeFn)
  local p = { Name = name }
  function p.GetGroundAttackers()
    local t = {}
    local n = sizeFn()
    for i = 1, n do t[i] = mkActor(40 + (i % 12), 40 + math.floor(i / 12)) end
    return t
  end
  return p
end
Player = {}
local players = {
  ["USA-bot"]    = mkPlayer("USA-bot", targetArmySize),
  ["Russia-bot"] = mkPlayer("Russia-bot", function() return ARMY end),
}
function Player.GetPlayer(n) return players[n] end

CPos = {}; function CPos.New(x, y) return { X = x, Y = y } end
WPos = {}; function WPos.New(x, y, z) return { X = x, Y = y, Z = z } end
Camera = { MinZoom = 0.2, Position = nil, Zoom = nil }
UserInterface = { SetMissionText = function() end }

local tickFn = nil
Trigger = { OnTick = function(f) tickFn = f end }

-- The scenario's own constants, mirrored so the driver can schedule arrivals off an ORDER rather
-- than off a tick it shares with the code under test.
local WARHEADS = 4          -- round(96*96 / 2400) clamped to [2,6]
local AIM_INTERVAL = 12

-- Orders are observed rather than predicted: the pair's fire tick is decided at run time now.
local pendingSalvos = {}
local seenOrders = {}

local realPrint = print
_G.print = function(msg)
  realPrint(msg)
  local slot, orderTick = string.match(tostring(msg), "^NUKEPOP order shot=(%d+) tick=(%d+)")
  if slot ~= nil and not seenOrders[slot .. ":" .. orderTick] then
    seenOrders[slot .. ":" .. orderTick] = true
    local base = tonumber(orderTick) + SALVO_AT
    for i = 0, WARHEADS - 1 do
      local t = base + i * AIM_INTERVAL
      pendingSalvos[t] = (pendingSalvos[t] or 0) + 1
    end
    -- The detonation is expensive for 400 ticks from the FIRST warhead, which is what the scenario
    -- should be pointing `deton1` at.
    for t = base, base + 400 do windowsHot[t] = true end
  end
end

dofile("tools/autotest/scenarios/demo-nuke-perf-populated/demo-nuke-perf-populated.lua")
WorldLoaded()

local SALVO_LANDS = os.getenv("SALVO_LANDS") ~= "0"

for t = 1, 13100 do
  curTick = t
  if t % 9 == 0 then impactCount = impactCount + 1 end        -- ordinary combat, every 9 ticks

  local due = pendingSalvos[t]
  if due ~= nil and SALVO_LANDS then
    arrivalCount = arrivalCount + due
    lastArrivalTick = t
    impactCount = impactCount + due * 20                       -- ~20 gated impacts per 750 kt RV
    if wipedAt == nil then wipedAt = t end                     -- the first salvo deletes the army
  end

  if skipped == nil then tickFn() end
end

realPrint("DRIVER skip=" .. tostring(skipped))
