# Coding Conventions — read before editing YAML or engine C#

## WDist notation

OpenRA uses `WDist` (World Distance) units throughout. Notation `NcXXX`:

- `1c0` = 1 cell = 1024 WDist units
- `1c512` = 1.5 cells (1024 + 512)
- `3c768` = 3.75 cells
- Plain `512` = 512 WDist units (half a cell)

## WAngle facing — counterclockwise (opposite of typical conventions!)

`WAngle` facings rotate **counterclockwise**, 0–1024 range. Easy to get wrong.

| WAngle | Direction | Screen (top-down) |
|--------|-----------|-------------------|
| 0      | North     | Up                |
| 256    | **West**  | **Left**          |
| 512    | South     | Down              |
| 768    | **East**  | **Right**         |

Map placement: units on the LEFT facing right → `Facing: 768` (East); on the RIGHT facing left → `Facing: 256` (West). Conversion: `WAngle.FromFacing(old)` where old RA facing × 4 = WAngle.

## YAML

### Templates (prefixed with ^)

```yaml
^Aircraft:          # Base template for fixed-wing planes
^Helicopter:        # Base template for helicopters
^Drone:             # Base template for drones
^Airborne:          # Common airborne traits
^NeutralAirborne:   # Airborne without faction-specific traits
^AirRadar:          # Radar trait for aircraft (range 24c0)
```

### Conditions system

Traits grant and consume named conditions:

```yaml
GrantConditionOnDamageState:
    Condition: heavy-damage-attained    # Granted at heavy damage
SpeedMultiplier@HeavyDamage:
    Modifier: 90
    RequiresCondition: heavy-damage-attained
```

Common conditions: `airborne`, `cruising`, `moving`, `empdisable`, `dronedisable`, `heavy-damage-attained`, `critical-damage`, `rank-veteran`, `suppression-*`, `unit.docked`

### Faction-specific files

Each unit type has a base template file and two faction files:

- `aircraft.yaml` → `aircraft-america.yaml` + `aircraft-russia.yaml`
- `infantry.yaml` → `infantry-america.yaml` + `infantry-russia.yaml`
- `vehicles-america.yaml` + `vehicles-russia.yaml`

### Blank lines are significant

Templates and top-level entries must be separated by a blank line. The MiniYaml parser silently merges adjacent ones, producing confusing override behavior — not a parse error. If a template "isn't taking effect," check the blank lines first.

### Disabling a string field: bare colon, not `""`

To clear a widget/chrome string field (`Background`, `Decorations`, `Separators`, `TooltipText`, …), use a **bare trailing colon**, never empty quotes. `FieldLoader.ParseString` returns the value verbatim (`FieldLoader.cs:161`), so `Separators: ""` parses as the literal two-character string `""`. That passes the `!string.IsNullOrEmpty` guards in the widgets, and the code then tries to load a chrome collection literally named `""` — e.g. `Sprite ""/separator was not found`. `Separators:` (bare colon) parses as null, `IsNullOrEmpty` fires, and the feature is skipped as intended.

### Maps must declare `Rules: rules.yaml`

A map's `rules.yaml` is loaded **only** when `map.yaml` names it under the top-level `Rules:` key. The engine maps that key to `Map.RuleDefinitions` (`Map.cs:176`, `required: false`); if it's absent, `RuleDefinitions` stays an empty MiniYaml (`Map.cs:364`) and the map loads only mod defaults. The map still renders and actors still spawn, so the omission is **silent** — LuaScript references, AutoTarget overrides, and every rule tweak are simply ignored. The same applies to `Weapons:`, `Sequences:`, etc.

## PITFALL comments (full spec: [`pitfalls.md`](pitfalls.md))

Recurring traps get a one-line `// PITFALL:` (`# PITFALL:` in YAML) **at the temptation site** — the line a careless reader is looking at when about to fall in. List all: `git grep PITFALL`. Add them during bug fixes when the root cause would surprise a reader; prune when stale (a wrong PITFALL is worse than none). Not for one-shot fixes, generic best practice, or universal anti-patterns (those go in a hook or the engine code rules below).

## Engine code rules (enforced by `tools/git-hooks/pre-commit`)

- **No `Console.Write`/`WriteLine` in tick-path code** — use `Log.Write(channel, ...)`. Console output fires every tick and floods stdout. Allowlisted directories: `UtilityCommands/`, `UpdateRules/`, `/Lint/`, `OpenRA.Server/`, `OpenRA.Test/`, `OpenRA.Utility/`, `tools/`.

Hook install (once per clone): `ln -sf ../../tools/git-hooks/pre-commit .git/hooks/pre-commit`

## Engine behaviors that surprise (debugging gotchas)

- **`AttackTurreted.CanAttack` short-circuits before `base.CanAttack`** (`AttackTurreted.cs:47`): it returns `turretReady && base.CanAttack(...)`, and `turretReady` is false while the turret is still rotating onto the target. A trace or breakpoint in `AttackBase.CanAttack` therefore never fires until the turret has finished aiming. When debugging "why won't this unit fire?", check the turret is pointed at the target first — the answer is often just "it hasn't finished turning."
- **`Activity.IsCanceling` is always false inside `OnLastRun`** (`Activity.cs:132-135`): `TickOuter` sets `State = Done` *before* calling `OnLastRun`, and `IsCanceling` is `State == Canceling` (`Activity.cs:84`). So `OnLastRun` cannot distinguish "ended naturally" from "was cancelled" — the flag is already cleared. Use a different signal: a queued `NextActivity` implies you were replaced, or compare `attack.RequestedTarget` to your own target field.
