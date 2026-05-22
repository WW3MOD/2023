# Autoburn — Lua API survey (260520)

Survey of test scenario Lua scripts under `tools/autotest/scenarios/` to find
duplicated boilerplate that wants to live in `mods/ww3mod/scripts/test-helpers.lua`
(loaded first by every `rules.yaml` via `LuaScript.Scripts`).

## Summary

- **Scenarios scanned:** 57 `.lua` files (49 `test-*` + 8 `demo-*`).
- **Patterns found:** 7 (ranked by save-LOC potential below).
- **Helpers extracted this run:** 1 (`TestHarness.CellPos`).
- **Scenarios migrated as proof:** 1 (`test-balance-heli-1v1`).

The existing harness already has `TestHarness.*` (focus, select, assert) and
`BalanceHarness.*` (duel runner, force-engage). Both are loaded by 56/56
scenarios that have an active Lua script. Adding more is cheap.

## Top patterns

### A. `cellPos` cell→WPos helper — 6 files, verbatim
The exact same 3-line function is copy-pasted into:

- `test-mi28-fires-ataka.lua:29`
- `test-heli-vs-heli-missile.lua:44`
- `test-balance-heli-1v1.lua:4` *(migrated this run)*
- `test-evac-suite.lua:20`
- `test-burn-arena.lua:29`
- `test-burn-demo.lua:19`

```lua
local function cellPos(cx, cy, altitude)
    return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end
```

**Extracted:** `TestHarness.CellPos(cx, cy, altitude)` in `mods/ww3mod/scripts/test-helpers.lua`.
Migrate the remaining 5 call sites in a follow-up pass (~25 LOC removed).

### B. "Did the unit fire?" assert pattern — 11 files, ~5–7 lines each
The `Unit.Stance = "HoldFire"; Unit.Attack(target, ...); local startingAmmo = ...;
AssertWithin(N, () => ... AmmoCount < startingAmmo, "X did not fire within Ns")`
pattern is in:

`test-paladin-fires`, `test-attackmove-engages`, `test-attackmove-paladin`,
`test-arty-force-attack-during-setup`, `test-wgm-fires-clean`,
`test-wgm-fires-thru-1-tree`, `test-wgm-no-fall-short` (variant),
`test-sr-attackmove-rally-engages` (variant), `test-mi28-fires-ataka` (inside
state machine), `test-heli-vs-heli-missile` (inside state machine),
`test-wgm-target-dies-midflight` (inside state machine).

Suggested helper:

```lua
function TestHarness.AssertFires(unit, ammoPool, seconds, label)
    local startingAmmo = unit.AmmoCount(ammoPool)
    TestHarness.AssertWithin(seconds, function()
        if unit.IsDead then
            return "fail: " .. (label or "unit") .. " died before firing"
        end
        return unit.AmmoCount(ammoPool) < startingAmmo
    end, (label or "unit") .. " did not fire within " .. seconds .. "s")
end
```

**Why I did NOT extract this run:** sites have bespoke timeout messages
("Paladin did not engage t90 on attack-move", "force-attack order was likely
eaten", "Bradley did not fire WGM"). A migration that drops those loses
diagnostic signal at fail time. Extract only if a future migrator commits to
preserving messages via a parameter or rewording them generically.

**Potential savings:** ~50 LOC across 7 clean sites (the 4 state-machine sites
are too intertwined with their state to fold cleanly).

### C. `Player.GetPlayer + nil check + Test.Fail` boilerplate — 7 files
```lua
local USA = Player.GetPlayer("USA")
local RUSSIA = Player.GetPlayer("Russia")
if USA == nil or RUSSIA == nil then
    Test.Fail("required players not found")
    return
end
```

In: `test-mi28-fires-ataka`, `test-heli-vs-heli-missile`, `test-balance-heli-1v1`,
`test-burn-demo`, `test-burn-arena`, `test-evac-suite`, `test-parallel-queue-pause`
(single player only). Mixed casing — some use `RUSSIA`, some `Russia`.

Suggested helper:

```lua
function TestHarness.RequirePlayers(...)
    local out = {}
    for _, name in ipairs({...}) do
        local p = Player.GetPlayer(name)
        if p == nil then
            Test.Fail("required player '" .. name .. "' not found")
            return nil
        end
        table.insert(out, p)
    end
    return table.unpack(out)
end

-- Usage: local USA, RUSSIA = TestHarness.RequirePlayers("USA", "Russia")
-- Returns nil on failure; caller must `if USA == nil then return end`.
```

**Savings:** ~5 LOC × 7 files = ~35 LOC. Smaller blast radius than B but
helper is slightly more complex (multi-return, early-fail semantics).

### D. `TICKS_PER_SEC / sec(s)` second→tick converter — 3 files
```lua
local TICKS_PER_SEC = TestHarness.TicksPerSecond
local function sec(s) return math.floor(s * TICKS_PER_SEC) end
```

In: `test-burn-arena`, `test-burn-demo`, `test-evac-suite`.

Suggested helper:

```lua
function TestHarness.Sec(s)
    return math.floor(s * TestHarness.TicksPerSecond)
end
```

**Savings:** ~3 LOC × 3 files = ~9 LOC. Low value (only 3 sites) but trivial
extraction and the conversion logic is repeated *inside* AssertWithin /
AssertAfter / ScreenshotAfter already.

### E. Demo respawn-loop machinery — 2 files, ~25 lines each
`demo-ifv-brawl.lua:31–56` and `demo-wgm-suite.lua:31–52` both define
near-identical `spawnReplacement` + `respawnLoop` pairs. The signatures
differ by one optional `onSpawn` callback.

Suggested helper(s):

```lua
function TestHarness.SpawnReplacement(actorType, owner, location, facing, delaySec, onSpawn)
    local delayTicks = math.floor((delaySec or 5) * TestHarness.TicksPerSecond)
    Trigger.AfterDelay(delayTicks, function()
        local fresh = Actor.Create(actorType, true, {
            Owner = owner,
            Location = location,
            Facing = Angle.New(facing or 0),
        })
        if onSpawn then onSpawn(fresh) end
    end)
end

function TestHarness.RespawnLoop(actor, actorType, owner, location, facing, delaySec, onSpawn)
    if onSpawn then onSpawn(actor) end
    Trigger.OnKilled(actor, function()
        TestHarness.SpawnReplacement(actorType, owner, location, facing, delaySec, function(fresh)
            TestHarness.RespawnLoop(fresh, actorType, owner, location, facing, delaySec, onSpawn)
        end)
    end)
end
```

**Savings:** ~50 LOC across 2 files. Only 2 call sites today — borderline by
the task's bar — but very likely to be reused in future demo scenarios (this
is a useful primitive for "long-running show-me" demos).

### F. Missile state-machine pattern — 3 files, ~30 lines each
`test-mi28-fires-ataka.lua`, `test-heli-vs-heli-missile.lua`,
`test-wgm-target-dies-midflight.lua` all implement the same tick-driven state
machine:

```
wait_fire    → AmmoCount drops & ActiveMissileCount > 0     → wait_detonate
wait_detonate → ActiveMissileCount == 0                       → assert damage
```

Suggested helper:

```lua
function TestHarness.WaitForMissileImpact(shooter, target, ammoPool, deadlineSeconds, onImpact, onFireSeen)
    -- onImpact(damage, ticksSinceFire) → "fail: reason" | nil to pass
    -- onFireSeen(elapsed) optional callback when first missile leaves
end
```

**Savings:** would be ~50 LOC but the predicate logic varies (kill target on
fire vs measure damage on impact vs check fuel-out). High value if the next
missile bug surfaces — but designing the right callback shape needs at least
one more sample site. Defer until a 4th caller appears.

### G. `TrunkCells / chebyshev / adjacentToTrunk` cluster geometry — 2 files
`test-cohesion-cover-bid.lua:19–44` and `test-cohesion-cover-redirect.lua:15–40`
have identical 25-line blocks for the 6-tree cluster used by the cohesion
cover tests.

**Recommendation:** leave these in scenario files. Each cluster geometry is
unique to its map; abstracting prematurely would need a parameter for the
trunk list at every call site. Two duplicates is below the "extract" bar.

## Inconsistencies noted

1. **Orphan Lua script.** `demo-v2-capture-coordinator/demo-v2-capture-coordinator.lua`
   exists but its `rules.yaml` has no `LuaScript.Scripts` entry, and neither
   does `map.yaml`. The script (which calls `TestHarness.FocusBetween(...)`)
   never runs — the demo's camera-center setup is silently lost. Fix: add
   `LuaScript: Scripts: test-helpers.lua, demo-v2-capture-coordinator.lua`
   to its `rules.yaml`.

2. **Naming convention drift.** `test-burn-arena.lua` and `test-burn-demo.lua`
   are named `test-*` but contain no `Test.Pass`/`Test.Fail` assertions other
   than a 32-second-deadline auto-pass — semantically they're demos. Per
   `CLAUDE.md`: *"Never put a Test.Pass/Fail call in a demo — if it has a
   verdict, it's a test; move it to test-* and use AUTOTEST."* Either rename
   to `demo-burn-*` (and strip the auto-pass), or strengthen the verdict so
   they earn the `test-` prefix.

3. **Mixed `Player.GetPlayer` capitalization.** Same variable, different cases
   across files: `USA / usa`, `RUSSIA / Russia / russia`. The argument string
   (`"USA"`, `"Russia"`) is canonical — only local-var capitalization drifts.
   A `TestHarness.RequirePlayers` helper would standardize this.

4. **`TestHarness.Screenshot` adoption.** Only `test-screenshot-smoke.lua`
   uses it. Per `CLAUDE.md` the recipe is "apply automatically for visual
   work" but no visual scenarios (`demo-ifv-brawl`, `demo-frontline-overlay`,
   `demo-layered-defence`, etc.) call it. Not a duplication issue, just an
   adoption gap — flagging because the doc explicitly directs otherwise.

5. **`Test.Mode` referenced only in tournament configs.** No `.lua` gates
   behavior on `TestMode.IsActive`; tests rely entirely on the LuaScript
   loader. Means leftover diagnostic prints (e.g. `print("[probe] ...")` in
   `test-cohesion-real-cluster.lua`) fire even in non-test launches if those
   scripts ever get referenced from a regular map. Low risk today — no map
   loads test-* scripts — but worth tightening the in-script `Test.*` guards
   if any of these helpers leak into shipped maps.

6. **Tournament scenarios have no `.lua`.** 7 `tournament-*` folders. Their
   rules.yaml correctly omits LuaScript — tournament configuration is
   handled by `tournament.yaml` instead. Not a bug, just clarifying for the
   count above.

## Recommended extraction order (next pass)

| Rank | Helper | Sites | Saved LOC | Risk |
|---|---|---:|---:|---|
| 1 | `TestHarness.CellPos` *(done this run)* | 6 | ~18 | none — purely mechanical |
| 2 | `TestHarness.RequirePlayers` | 7 | ~35 | low — uniform semantics |
| 3 | `TestHarness.AssertFires` | 7+ | ~50 | medium — bespoke timeout messages lost unless preserved |
| 4 | `TestHarness.SpawnReplacement` + `RespawnLoop` | 2 | ~50 | low — already abstract, just relocate |
| 5 | `TestHarness.Sec(s)` | 3 | ~9 | none — trivial |
| 6 | `TestHarness.WaitForMissileImpact` | 3 | ~50 | high — design needs 4th sample |
| 7 | (skip) cohesion `TrunkCells/chebyshev` | 2 | — | premature |

Cumulative potential after all six landings: ~210 LOC removed, ~60 LOC added
in helpers → net ~150 LOC across 25+ scenarios, plus the cross-cutting
benefit of standardized error messages.

## Files touched this run

- `mods/ww3mod/scripts/test-helpers.lua` — added `TestHarness.CellPos` (8 lines)
- `tools/autotest/scenarios/test-balance-heli-1v1/test-balance-heli-1v1.lua` —
  removed local `cellPos`, switched two call sites to `TestHarness.CellPos`
- `WORKSPACE/autoburn/lua-api-survey.md` — this report
