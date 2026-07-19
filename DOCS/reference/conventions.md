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

## PITFALL comments (full spec: [`pitfalls.md`](pitfalls.md))

Recurring traps get a one-line `// PITFALL:` (`# PITFALL:` in YAML) **at the temptation site** — the line a careless reader is looking at when about to fall in. List all: `git grep PITFALL`. Add them during bug fixes when the root cause would surprise a reader; prune when stale (a wrong PITFALL is worse than none). Not for one-shot fixes, generic best practice, or universal anti-patterns (those go in a hook or the engine code rules below).

## Engine code rules (enforced by `tools/git-hooks/pre-commit`)

- **No `Console.Write`/`WriteLine` in tick-path code** — use `Log.Write(channel, ...)`. Console output fires every tick and floods stdout. Allowlisted directories: `UtilityCommands/`, `UpdateRules/`, `/Lint/`, `OpenRA.Server/`, `OpenRA.Test/`, `OpenRA.Utility/`, `tools/`.

Hook install (once per clone): `ln -sf ../../tools/git-hooks/pre-commit .git/hooks/pre-commit`
