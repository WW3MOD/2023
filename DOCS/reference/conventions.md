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
- **Actor types in map YAML must be the lowercased ruleset key, or the density pass throws.** `Map.SetDensityLayer` looks the actor up with the raw map string — `actorsRules[actorReference.Type]` (`Map.cs:989`) — and `Rules.Actors` is keyed lowercase, so an uppercased placement like `Truck0: TRUK` throws `KeyNotFoundException: 'TRUK'` at map load (the density pass runs for any map lacking a `shadows.bin`). Use `truk`. Normal actor spawning is case-insensitive and hides this, so the same map can spawn actors fine yet crash the density pass — always lowercase actor types in map placements.
- **`World.SharedRandom` is the synced gameplay RNG — never draw from it to stagger a net-new always-on trait.** `SharedRandom` (`World.cs:50`, seeded from `RandomSeed` at `:217`) is the stream folded into the sync hash (`World.cs:543` adds `SharedRandom.Last`), so every `.Next(...)` advances it for **all** clients/profiles. A trait that ticks or loads for everyone (not just one gated profile) and calls `world.SharedRandom.Next(...)` in `WorldLoaded`/`Tick` to pick a per-instance stagger therefore shifts the stream for control/benchmark games too — silently breaking replay byte-identity versus any earlier baseline that lacked the trait, even when the trait is otherwise behaviour-inert. Derive a per-instance offset **deterministically** instead (a fixed constant, or a hash of a stable synced identity). Drawing from `SharedRandom` at load is only safe for a trait that is itself part of the baseline being measured. (Contrast `LocalRandom`, which is *not* in the sync hash — see architecture.md "Bot decisions ARE seed-reproducible".)
- **`AutoTarget.ScanForTarget` returns `Target.Invalid` for TWO indistinguishable reasons — "found nothing" AND "scan interval not elapsed".** `ScanForTarget` (`AutoTarget.cs:928-951`) returns `Invalid` both when a scan ran and found no target, and when `nextScanTime > 0` so no scan ran at all. A scan that *does* run re-arms `nextScanTime = SharedRandom.Next(MinimumScanTimeInterval, MaximumScanTimeInterval)` (`:936-937`), decremented one per `ITick` (`:924-925`) — so a unit only actually scans on ~1 of every N idle ticks. Any caller that treats every `Invalid` as "target lost" therefore wipes its per-target state ~N−1 of every N ticks. To tell the cases apart, capture `scannedThisTick = nextScanTime <= 0` **before** the call and, on a gated off-scan `Invalid`, reuse the cached target (re-checking alive + `CanBeViewedByPlayer`) — exactly what `AmbushTickIdle` does (`:636/:644-648`). This conflation was the concrete bug that kept the Stage-3 ambush cadence counters from ever reaching their sample threshold (see architecture.md §Widened ambush).
- **The logging thread is the engine's only long-lived FOREGROUND thread — keep `IsBackground = true` on it or the process never exits.** Every other long-lived engine thread (Server/Connection/MapCache/graphics) sets `IsBackground = true`; `Log.DoWork` spins until its cancellation token fires, and the token is cancelled **only** in `Log.Dispose()` (cancel + `Thread.Join`, `Log.cs:187-192`), reached via the `finally { Log.Dispose(); }` in each entrypoint (`OpenRA.WindowsLauncher/Program.cs:81-83`, `OpenRA.Launcher/Program.cs`). A .NET foreground thread keeps the process alive after `Main` returns, so any exit that bypasses that finally (a signal kill mid-shutdown, or a main-thread hang between `Game.Exit` and the finally) leaves the thread spinning and **pins the process** — the mechanism behind lingering `dotnet.exe`/`OpenRA.exe` after the game closes. WW3MOD sets `IsBackground = true` on the Log thread (`Log.cs:59-68`, PITFALL comment inline) so the runtime reaps the process on every exit path; clean exits still `Log.Dispose()` (cancel + join + final flush), so no log lines are lost.
- **`world.Actors` includes positionless PlayerActors — enumerate-then-read-position NREs on them.** `world.Actors => actors.Values` (`World.cs:522`) yields every `PlayerActor` and the world actor, not just spatial units — each `Player` adds and `Initialize`s a `PlayerActor` into the same dict (`Player.cs`), and a `PlayerActor` has no `IOccupySpace`. `Actor.CenterPosition => OccupiesSpace.CenterPosition` (`Actor.cs:79`), so reading `.CenterPosition` on one throws `NullReferenceException`. A relationship filter does **not** exclude it: an enemy `PlayerActor`'s `Owner` is *itself*, so `RelationshipWith(Owner) == Enemy` passes it through — the bug that crashed a bot game to desktop on tick 0 when a HelicopterSquad's `FindClosestEnemy` fell through to the `world.Actors` scan before the ThreatMap had data (`HelicopterStates.cs`, guarded at `:226` with `a.OccupiesSpace != null`). Any bot code that enumerates `world.Actors` (rather than a spatial query) and later reads a position must filter `a.OccupiesSpace != null`. Spatial-partition queries (`World.FindActorsInCircle`) are safe by construction — they only ever hold actors that occupy space.
- **Reading any trait of a Disposed actor throws — so a bot squad module must prune dead members *before* it ticks squad states, not on a slower scan cadence.** `TraitDictionary` guards every lookup with `CheckDestroyed`, which throws `InvalidOperationException: Attempted to get trait from destroyed object` on a Disposed actor (`TraitDictionary.cs:81-84`) — `TraitOrDefault` included, so there is no "safe" trait read. A module whose state-update interval is shorter than its membership-prune interval will therefore hold dead actors in `Squad.Units` for the gap and CTD the first time a state touches a trait (the concrete crash: `HelicopterSquadBotModule` updated squads every 5 ticks but pruned only every 100, so a state tick's `GetRole` → `TraitOrDefault<AIHelicopterRole>` hit a destroyed hind). The engine-standard invariant is **prune-before-update**: `SquadManagerBotModule.CleanSquads()` runs on every `BotTick` before any `s.Update()` (`SquadManagerBotModule.cs:229-233`); `HelicopterSquadBotModule.PruneSquads()` (`:355`, `RemoveAll` on `IsDead`/`!IsInWorld`/foreign owner) mirrors it and is called at the top of `UpdateSquads` (`:249`). Enforce list hygiene upstream at the single choke point that runs before every update — don't scatter per-site trait-read guards through the squad states (they rot; the choke point can't be bypassed).
- **`Map.GetTerrainInfo(CPos)` / `GetTerrainIndex(CPos)` are UNGUARDED — an off-map cell throws `IndexOutOfRangeException`.** `GetTerrainInfo(CPos)` (`Map.cs:1649`) delegates to `GetTerrainIndex(cell)` (`:1627`), which does a raw `cachedTerrainIndexes[cell.ToMPos(this)]` array index (`:1637`) with no bounds check — so any code that can hand it a cell past the map edge crashes hard. The engine idiom everywhere else is to gate it behind `Map.Contains(cell)` (`:1363`) first (Aircraft land checks, Wanders, Missile trails, HeliEmergencyLanding, AIUtils all do); the `Rules.TerrainInfo.GetTerrainIndex(string/TerrainTile)` *overload* is safe (no per-cell array). The recurring source of off-map cells is death effects: a `FallToEarth`/`SpawnActorOnDeath` husk or a `LeavesTrailsCA` ballistic-missile trail whose `CenterPosition` drifts past the edge. When adding a per-cell terrain read, either `Contains`-guard it or `Map.Clamp` (`:1654`) the cell into bounds first. (Contrast the harmless `Rules.TerrainInfo` string overload.)
- **Bot-module actor-name config is a case-mismatch trap: `actor.Info.Name` is ALWAYS lowercase, so a case-sensitive config set silently no-matches.** `Ruleset.cs` builds every `ActorInfo` with `k.Key.ToLowerInvariant()` (`GameRules/Ruleset.cs:126`), so at runtime an actor's name is always lowercase. A bot-module Info field that stores YAML actor names verbatim in a default (ordinal, case-sensitive) `HashSet<string>`/`Dictionary<string,int>` and then does `.Contains(a.Info.Name)` / `.ContainsKey(name)` / `== key` **silently no-matches** the moment the YAML value carries an uppercase letter — no warning, no exception, the unit is just never built/classified/priced (this was the "US only ever buys the littlebird" bug: uppercase `A10/F16/…` in `UnitsToBuild`). The tell (grep signature): `= new HashSet<string>()` / `= null` dict with NO `StringComparer.OrdinalIgnoreCase`, consumed by `.Contains(<name>)`/`.ContainsKey(<name>)`. **Hardened at the engine level:** the shared helper `ActorNameCase` (`Traits/BotModules/ActorNameCase.cs`, `NormalizeInPlace(HashSet)` `:37` / `NormalizeKeysInPlace(Dict)` `:51`) lowercases the collection contents in place, wired into ~15 bot-module Info `RulesetLoaded` methods — so config case can no longer silently no-op. It lowercases *contents* rather than swapping to `OrdinalIgnoreCase` because `FieldLoader.ParseHashSetOrList`/`ParseDictionary` build each collection via `Activator.CreateInstance(fieldType, capacity)` (`FieldLoader.cs:506/:524`), a fresh default-comparer instance that discards any comparer set in the `readonly` field initializer. **Do NOT lowercase engine-token sets** that look like the family but compare against non-lowercased tokens — production-queue `Type` sets (`Vehicle`/`Infantry`/`Aircraft`), target-type `BitSet`s (`Submarine`), terrain sets (`Water`/`Tree`), cohesion enums — their capitalization is correct. (`PoiMap.IncomeWeights`, a World trait not a bot module, carries the same hazard but was out of the audit's scope — lowercase-safe today.)
- **An `EnterBehaviour.Dispose` unit is REMOVED at structure-entry, *before* the structure's effect completes — so "actor gone ⇒ mission failed" is a false read.** `RepairsBridges` uses `EnterBehaviour.Dispose` (`RepairsBridges.cs:31`): the engineer (`e6`) is disposed the instant it enters the `LegacyBridgeHut`/`BridgeHut`, but the bridge stays `BridgeDamageState.Dead` through the whole repair animation while `hut.Repairing` is true (`LegacyBridgeHut.cs:32`, `BridgeHut.cs:240` — both public). A bot mission that checks "bridge still Dead + engineer gone ⇒ failure" therefore records a FALSE failure (with cooldown + a wasted retry) on a mission that is actually succeeding. Gate `Repairing` (repair-in-progress ⇒ hold and wait for the bridge to flip to not-Dead ⇒ success) BEFORE the engineer-alive and timeout checks — the *ordering* is the whole safety property (`EngineerRouteOpenBotModule.TickActiveMission`). The `RepairBridge` order itself is `new Order("RepairBridge", engineer, Target.FromActor(hut), false)` (`Target.Type == Actor`); `bot.QueueOrder` resolves it on the engineer and queues the walk-to-hut + enter + repair activity (`RepairsBridges.cs:66-72/:92-118`) — no custom activity needed.
- **Ordering a bot passenger to board while queuing the carrier's Move in the SAME pass flies the transport EMPTY.** The `EnterTransport` orders land on the *infantry's* activity queues, but a queued `Move` on the *carrier's* own (empty, idle) queue starts IMMEDIATELY — so the carrier departs before any soldier boards. The engine gives no boarding-complete callback; boarding "completes" only when `Cargo.Load` fires, observable only by polling `Cargo.PassengerCount`. The correct shape (used by `MountedTransportBotModule` and now `HelicopterSquadBotModule.TryLaunchTransportMission`): stage a `Loading` task, dispatch the delivery only once `PassengerCount >= min` (or a partial on timeout), abort to the idle pool if nobody boarded — and queue the carrier's RETURN move right behind the `Unload` so it doesn't idle at the drop (a dedicated transport is free-pool-excluded, so nothing else re-collects it — see architecture.md §"The bot free pool self-heals").
