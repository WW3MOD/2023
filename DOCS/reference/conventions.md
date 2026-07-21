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

**Converting a bearing back to a cell step:** `WVec.FromSpeedAndAngle(speed, angle)` (`WVec.cs:94`) is the exact inverse of `WVec.Yaw` (`WVec.cs:66`). To turn a bearing `WAngle` (e.g. one built from a cell-space `WVec(dx,dy).Yaw`) back into a cell-space step, use `FromSpeedAndAngle` and take `Sign(X)/Sign(Y)` — it respects the "north = −Y" + counterclockwise convention automatically, avoiding hand-rolled `Cos()/Sin()` sign errors.

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

**A consumed-but-ungranted condition is a `make test` ERROR (not a warning).** `ConditionalTraitInfo.RequiresCondition` is `[ConsumedConditionReference]` (`ConditionalTrait.cs:21`), so a trait gated on `foo || bar` marks BOTH names *consumed on that actor*. `CheckConditions` (`Lint/CheckConditions.cs:73-75`) calls `emitError` — which fails `make test` — for any condition an actor consumes but nothing grants. (The reverse, granted-but-unconsumed, is only a `emitWarning`, `:70-71`.) To make a name grantable-but-not-yet-fired (e.g. a seam for later Lua/warhead activation) without a live grant, declare an `ExternalCondition` whose `Condition` field is `[GrantedConditionReference]` (`Conditions/ExternalCondition.cs`) — it satisfies the lint at zero runtime cost and a later `GrantCondition("name")` resolves to it.

**Grants scoped to the actor:** `GrantConditionOnBotOwner` grants only on the actor it sits on, checking `self.Owner.IsBot && Bots.Contains(self.Owner.BotType)` (`Conditions/GrantConditionOnBotOwner.cs:46`). A **unit** trait's `RequiresCondition` sees only conditions granted on that *unit*, so gating a unit trait to bot-only needs a per-unit `GrantConditionOnBotOwner` on the unit template — a Player-actor grant (e.g. the `enable-ai-experimental` grants in `ai.yaml`) does nothing for a unit trait.

### Faction-specific files

Each unit type has a base template file and two faction files:

- `aircraft.yaml` → `aircraft-america.yaml` + `aircraft-russia.yaml`
- `infantry.yaml` → `infantry-america.yaml` + `infantry-russia.yaml`
- `vehicles-america.yaml` + `vehicles-russia.yaml`

A unit type is defined in three tiers: an abstract template `^E6` and a bare concrete `E6:` (`Inherits: ^E6`) live in the base file (`infantry.yaml`); the buildable faction variants `E6.america:` / `E6.russia:` (each `Inherits: ^E6`, adding `Buildable`/`RenderSprites`) live in the faction file (`infantry-america.yaml:95`, etc.). Keys are declared uppercase but actor lookup is case-insensitive. **To annotate every variant at once, override the `^Template`** — a trait added to `^E6` reaches the bare concrete and both faction variants in one line; single-hull tweaks (`bradley`, `bmp2`) go on the concrete key.

This is also the reliable way to add a trait from **map/test `rules.yaml`**: override a `^Template` (e.g. `^Combatant`) or a bare hull key (`t90`, `bradley`), as `demo-wgm-suite/rules.yaml` does. Overriding a **faction-suffixed** concrete key (`ar.america`) from map rules has been observed to throw `LoadFromManifest<Rules>, duplicate values found for the following keys: ar.america: [ActorInfo,ActorInfo]` at load — prefer the template.

### Weapon `ValidTargets`: `Air` ≠ `Helicopter`

Helicopters are hit by the `Helicopter` target type, not `Air` — these are distinct. Ground autocannons and MGs list `Helicopter` and so *can* shoot helis without being air-defence: `^7.62mm` is `Infantry, Unarmored, Helicopter` (`weapons/weapons-ballistics.yaml:144`), `^12.7mm` adds `Light` (`:215`), and even Tunguska's dedicated `30mm.Tunguska.AA` autocannon is `ValidTargets: Helicopter` (`:455`). Only guided SAMs list `Air`: `MANPAD`/`Stinger`/`Stinger.quad`/`9M311` (`weapons/weapons-missiles.yaml:339,372`; `9M311 Inherits: Stinger`), which is how Tunguska gains true air-defence — via its `9M311` missile (`vehicles-russia.yaml:860`), not its gun. So any "is this an air-defence weapon?" test must key on the literal `Air` target type; keying on `Helicopter` sweeps in every MG-armed vehicle.

### Blank lines are significant

Templates and top-level entries must be separated by a blank line. The MiniYaml parser silently merges adjacent ones, producing confusing override behavior — not a parse error. If a template "isn't taking effect," check the blank lines first.

### Removing an inherited trait: `-Key` matches the FULL node key

`-TraitName:` removes an inherited node only by **exact key match, including any `@label`** — `ResolveInherits` does `resolved.RemoveAll(r => r.Key == removed)` and throws `There are no elements with key '<key>' to remove` if nothing matched (`MiniYaml.cs:482-483`). So `-SquadManagerBotModule:` does **not** remove `SquadManagerBotModule@experimental.america.fixedwing`; you must write `-SquadManagerBotModule@experimental.america.fixedwing:`. There is no "remove all instances of this trait type" wildcard form — each labeled instance must be listed. (A mismatch throws at load, surfacing as a load-error dialog.)

### WW3MOD's map grid is `Rectangular`, not `RectangularIsometric`

`mod.yaml:319` sets `MapGrid: Type: Rectangular`. So cell neighbours are the plain `(x±1, y±1)` grid, a Manhattan/Chebyshev disc maps directly to spatial adjacency, and `CellLayer.Contains(CPos)` has no `X<Y` isometric rejection. This is what lets the density-neighbour idiom (`CohesionMoveModifier.CoverScore`, and the Phase-1 map layers) step cells with raw `CVec` offsets.

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
- **Cell distance (`CVec.Length`, `CPos` subtraction) is Euclidean, not Chebyshev** (`CVec.cs:49-50`): `Length => Exts.ISqrt(X*X + Y*Y)` — rounded straight-line distance, so a diagonal reads ~1.4× farther than the grid distance a player/watcher sees on the minimap. For true chessboard "cells away" gates use `max(|dx|, |dy|)` computed by hand, not `.Length`.
- **A bot `Order` string must match a `ResolveOrder` case, not an activity class name.** `Cargo.ResolveOrder` handles only `"Unload"` and `"UnloadCargoPassenger"` (`Cargo.cs:248,255`); `"UnloadCargo"` is the *activity* class name (`Activities/UnloadCargo.cs`, queued internally at `Cargo.cs:519`) and matches no order — issuing `new Order("UnloadCargo", …)` is **silently dropped**. Also, even a correct `"Unload"` is dropped when `!CanUnload()` (no free adjacent cell on arrival), so a one-shot issue can permanently stall; re-issue when a cell frees. When wiring a bot to drive a unit trait, read the trait's `ResolveOrder` for the exact accepted strings.
- **`UnitDefaultsManager` overwrites human-owned units' stances in `Created`.** `AutoTarget.Created` applies per-type persisted defaults (fire/engagement/cohesion/resupply) **only for `self.Owner.Playable && !self.Owner.IsBot`** (`AutoTarget.cs:355-388`), read from `Platform.SupportDir/ww3mod/unit-defaults.yaml`. So a human-owned unit's stance is **per-machine state**, not the YAML default — a deterministic human-owned test must strip the bare-key `UnitDefaultsManager` World trait (`world.yaml`) or a locally-saved default silently changes behaviour. Bot-owned units skip this branch entirely (they read `Initial*StanceAI`).
