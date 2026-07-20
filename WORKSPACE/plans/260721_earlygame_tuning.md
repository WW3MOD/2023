# Early-game tuning — drivers + implement-ready tunables (a/b/c)

**Date:** 2026-07-21
**Researched against:** `main @ 2ed2c0ac`
**Mode:** read-only recon — no build/launch/test. All claims cited file:line.
**Scope:** behaviour tuning (not bugs). Bugs 1–3 in `260721_playtest_bugs_triage.md`.

## Standing constraints (repeat — every tunable below obeys these)
- New Info fields **default to frozen behaviour**; `@stable` + Normal/Rush/Turtle must stay **byte-identical** to `2ed2c0ac`. Opt-in only on `@experimental`.
- Never touch shared singletons: world `PoiMap`, `PoiGoalGuard@poi`, `MountedTransportBotModule@poi` config shared with the Stable control. Per-bot Info fields only.
- **Suggested cycle split: one behaviour change per verify cycle** (a, then b, then c) so causal credit is attributable — the S2 dispersion result (`runs/260720_s2_exp_vs_normal_n10.md`) shows why bundling behaviours hides the signal.

---

## 0. Motorized-start directive (regime the ladder should measure)

**Lobby option:** `startingunits` — a `LobbyOption` dropdown implemented by `SpawnStartingUnits` (world actor). Declared `SpawnStartingUnitsInfo` `engine/OpenRA.Mods.Common/Traits/World/SpawnStartingUnits.cs:23-53`; option key `"startingunits"` `:51`; resolved `:76-77` via `OptionOrDefault("startingunits", info.StartingUnitsClass)`. **Default value `"none"`** (`SpawnStartingUnits.cs:25`). Class list built from `StartingUnits`/`MapStartingUnits` traits.

**Values (per-faction pairs, `mods/ww3mod/rules/world.yaml`):** `none` "None" (`:364-368`, spawns only `supplyroute`), `squad` (`:370-385`), `platoon` (`:387-402`), `motorized` **"Motorized"** (`:404-419`), `air` "Air support" (`:421-436`).

**Motorized spawns (`world.yaml:404-419`):** BaseActor `supplyroute` +
- America (`:409`): `abrams, bradley, humvee` + 3× rifle fireteams + `MT, AT, DR, SN, MEDI`. **Included AA = the `humvee` only** (mobile MG/utility; no dedicated SAM). Dedicated AA (`strykershorad`/`aa`/`tunguska`) appears only in the **air** set (`:426/:434`), NOT Motorized.
- Russia (`:417`): `t90, bmp2, bmp2` + 3× fireteams + `MT, AT, DR, SN, MEDI`. **No dedicated AA vehicle** (relies on `bmp2` autocannon).

### Ladder-regime flag (IMPORTANT — measures the wrong regime)
Every tournament scenario runs at the **default `none`** — bots start with only their two hand-placed `supplyroute` beachheads and call in the entire force from budget. Evidence: `tournament-s1-eco-river-zeta`, `-mirror`, `tournament-s2-combat-river-zeta`, `-mirror`, `-cal-nn` all use the byte-identical River Zeta map with no `StartingUnitsClass` on the bot `PlayerReference`s and no `startingunits` override in rules.yaml (`WORKSPACE/ai-bench/LADDER.md:448-451`; map `OwnSR`/`OpponentSR: supplyroute` at map.yaml:14056-14061). The canonical `mods/ww3mod/maps/river-zeta-ww3/scenarios.yaml` Frontline/Shellmap variants even carry `-SpawnStartingUnits` (`scenarios.yaml:564`) — but those are campaign/shellmap, **not** the ladder.

**Consequence:** if we optimise for the Motorized regime (as directed), the ladder is grading a different world (`none`). Per doctrine a scenario change → **re-BASELINE** (S1/S2 bars must be re-measured on the Motorized map). **Recommended:** add `StartingUnitsClass: motorized` to the two bot `PlayerReference`s (or a `startingunits: motorized` lobby-option override in the tournament rules.yaml) on a **new** `-motorized` scenario variant, and re-run the S1/S2 advancement bars before trusting any Motorized tuning. Do this as its own scenario/BASELINE cycle *before* the item-b AA tuning, because b's safe floor depends on the Motorized starting force.

---

## a. Supply trucks bought from the start with no rearm demand

### Driver
`truk` is a static composition share in the **shared normal ground builder** the Experimental AI inherits (`enable-ai-player` → normal+experimental+stable, `ai.yaml:52-54`):
- `truk: 20` share, `UnitLimits truk: 4` — `mods/ww3mod/rules/ai/ai-america.yaml:33, 36`; Russia identical `ai-russia.yaml:33, 36`.
- The buy decision is `UnitBuilderBotModule.ChooseUnitToBuild` (`engine/OpenRA.Mods.Common/Traits/BotModules/UnitBuilderBotModule.cs:188-192`): picks `truk` whenever `ownedTruks*100 < 20*totalOwned` — a **pure army-ratio test with zero supply-demand awareness**. Trucks are bought from tick 0 at full ammo.
- `SupplyFollowerBotModule` only *moves* owned trucks; it never gates purchase, but it does already read real demand: `IsLowOnSupply` = `SupplyProvider.CurrentSupply < RestockThreshold` (`SupplyFollowerBotModule.cs:237-243`) and cluster ammo need via `AmmoPool.CurrentAmmoCount / Info.Ammo` (`:170-174`) — the exact queryable state a purchase gate would reuse.

### Proposed tunable (per-profile, default-frozen)
The ratio table is **shared** with Normal, so it can't be edited in place without changing Normal. Options:
- **(A, cleanest for isolation)** Add a per-instance `readonly bool SuppressTruckWhenAmmoFull = false` + `readonly HashSet<string> SupplyProviderTypes = {}` to `UnitBuilderBotModuleInfo`; in `ChooseUnitToBuild` (`:188-192`) skip a candidate whose name ∈ `SupplyProviderTypes` when **no** owned actor has an `AmmoPool` below full (iterate player actors, pattern from `SupplyFollowerBotModule.cs:170-174`). Default false = frozen. But the ground builder is the *shared* `@america.normal` instance — to set the flag only for Experimental you must first **split** a `UnitBuilderBotModule@experimental.america/russia` gated `enable-ai-experimental` (copy the table, add the flag) and remove the ground `UnitQueues` from the shared builder for experimental. That split is the real cost.
- **(B, no engine change)** Simply drop the `truk` **share** and rely on `SupplyFollowerBotModule`/demand — but there is no demand-driven truck *purchase* path today, so trucks would never be bought. Not viable without (A)'s ammo-gate.

**Recommend (A).** New state consulted: per-owned-unit `AmmoPool.CurrentAmmoCount < Info.Ammo`. Touches: `UnitBuilderBotModule.cs:188-192` + a new `@experimental` builder split. Risk: **med** (builder split is the invasive part; the ammo gate itself is small and default-off).

---

## b. AA overbuild at the start (multiple SHORAD/Tunguska)

### Driver
Two sources; the **static composition** is the early culprit:
- Normal builder (shared): `aa.america: 30` + `strykershorad: 10` (limit `strykershorad: 2`) — `ai-america.yaml:20, 30, 40`; Russia `aa.russia: 30` + `tunguska: 10` (limit `tunguska: 2`) — `ai-russia.yaml:20, 29, 41`. Bought from tick 0 by the same ratio test (`UnitBuilderBotModule.cs:190`) with **no air-threat gating**. `aa.america/aa.russia` have **no UnitLimit** → up to ~30% army share of AA infantry early.
- `AdaptiveProductionBotModule` `AntiAirUnits` (`ai.yaml:283/291/301/309`, stable `:705/713`) **does** gate — `totalSightings >= MinEnemySightings:3` (`AdaptiveProductionBotModule.cs:98`) AND `enemyAir > 0` from a live visible-actor scan (`:125-131, :187-188`). So Adaptive is **not** the overbuild; the static `aa: 30` share is.

### Motorized interaction (why the floor matters)
A Motorized start ships **no dedicated SAM** (§0) — only the `humvee`/`bmp2` autocannon as incidental AA. So the AI does need *some* early AA against helicopter cheese, but far less than a 30% infantry-AA share. The directive's target: **scale AA share to observed enemy air, with a floor of ~2 AA infantry**. The floor exists precisely because Motorized's built-in AA is weak.

### Proposed tunable (per-profile, default-frozen)
Same shared-builder problem as (a) — the `aa`/`strykershorad`/`tunguska` shares live in the Normal table. Approach:
- Add to the (split) `UnitBuilderBotModule@experimental.*`: `readonly HashSet<string> AntiAirTypes = {}`, `readonly int AntiAirFloor = 0`, `readonly int AntiAirMaxShareWithoutEnemyAir = <current>` (or a multiplier). In `ChooseUnitToBuild`, cap AA-type selection to `AntiAirFloor` units until `enemyAir > 0` is observed, then relax toward the full share. Reuse `AdaptiveProductionBotModule.ScanEnemyComposition`'s visible-`AircraftInfo` count (`:187-188`) as the enemy-air signal (or the `ThreatMapManager` used by SupplyFollower `:70`). Note there is **no persisted `enemy-air-sighted` blackboard counter** — air is only a per-scan live count, so the gate must read it live each cycle.
- Defaults reproduce current shares (frozen). Enable the threat-conditional cap on `@experimental` only.

Touches: the split `@experimental` builder + `ChooseUnitToBuild` (`UnitBuilderBotModule.cs:177-195`). Risk: **med** — depends on the builder split; the cap logic is contained. **Sequence after §0** (the Motorized BASELINE) so the floor is tuned against the real Motorized AA baseline, not the `none` regime.

---

## c. Massing at the SR (~90s: 5 vehicles + many infantry waiting)

### Drivers (interacting)
1. **Idle carriers (largest visible clump):** `bradley`/`m113`/`bmp2` are produced (`ai-america.yaml:27-28`) but **excluded from both PoiOffensive** (`ai.yaml:187`) **and LayeredDefence** (`ai.yaml:341`) — they belong to MountedTransport, which is **dormant until frontline contact** (Bug 3.1: `PickDropOffCell` null pre-contact, `MountedTransportBotModule.cs:313-314`; `InfluenceMap.cs:248-256`). So the IFVs/APCs sit at the SR the whole early game. **This is the "5 vehicles waiting" the user saw** (plus `truk` which follows only when `MinNearbyFriendlies: 4` is met — `ai.yaml:268`).
2. **Offense form-up granularity:** `PoiOffensiveBotModule@experimental` uses `UnitsPerAxis: 8`, `MinAxisSize: 3`, `MaxAxes: 4` (`ai.yaml:178-180`). `DesiredAxisCount` opens ~one axis per 8 units and `AllocateProportional` funds each at ≥3 (`PoiOffenseMath.cs:550-560, 566-617`). Early, with a small pool, this yields **one big axis** (or none until ≥3 offensive units exist) — the army moves as a single clump rather than several small probes, and units beyond the funded axis sizes sit in the free pool at the SR.
3. **Cadence/hysteresis:** offense re-evaluates every `ReevaluateInterval: 100` and holds axes for `AxisCommitmentTicks: 250` (`ai.yaml:176, 181`) — units aren't re-tasked between evals, so a unit that misses an axis waits up to ~100t.
4. **LayeredDefence needs contact too:** it only re-tasks reserves toward *contested* cells (needs a frontline); pre-contact it also leaves units in reserve. So early, PoiOffensive is effectively the only mover, and (2)+(3) throttle it.
5. **Dispersion interplay:** `CohesionSwitchEnabled: true` issues `ApproachCohesion: Spread` en route (`ai.yaml:192-194`) — but its S2 causal credit on combat efficiency is **negative** (`runs/260720_s2_exp_vs_normal_n10.md:79-85`). Spreading a single early clump is not the same as launching multiple small packets; more axes (below) is the lever for "smaller packets", and it may let dispersion be reconsidered.

### Proposed tunables (per-instance on `@experimental` — all safe, all default-frozen via the existing `@stable` twin)
"Smaller early packets, spread and capture fast" maps to offense-side fields (no shared-singleton risk — these are per-`PoiOffensiveBotModule` Info fields; `@stable` keeps the frozen values at `ai.yaml:668-684`):
- **Lower `UnitsPerAxis`** (8 → e.g. 4–5): opens a second/third axis with fewer units → army splits into smaller probes sooner.
- **Lower `MinAxisSize`** (3 → 2): allows 2-unit early packets instead of holding units until 3 accumulate.
- **Raise `MaxAxes`** (4 → 5–6) *only if* pool supports it, so early spread isn't capped.
- Optionally **shorten `AxisCommitmentTicks`** for faster early re-tasking (trade: more order churn) and/or a lower `ReevaluateInterval` early.

These are the **cleanest** early-urgency levers because they're per-instance and the frozen `@stable` control is provably untouched. **Do NOT** attempt to fix (1) here — the idle carriers are a *transport dormancy* problem (Bug 3.1); it should be fixed in the Bug-3 cycle, and this tuning cycle should measure offense granularity in isolation.

**Interaction to watch (from S2):** because dispersion is S2-negative on efficiency, run the packet-size change **with dispersion held at its current setting**, then a follow-up A/B of dispersion under the new granularity — don't change both at once (per the one-behaviour-per-cycle rule).

Touches: `ai.yaml:178-181` (`@experimental` only). Risk: **low** (pure per-instance YAML constants; `@stable` byte-identical). Note the pure math is unit-tested (`PoiOffenseTest`) so the allocation behaviour under new constants is verifiable without a game.

---

## Suggested cycle split (one behaviour per verify cycle)
1. **§0 Motorized BASELINE** — add `-motorized` scenario variant(s), re-run S1/S2 advancement bars. Prerequisite for b (and arguably the honest regime for everything). Scenario change ⇒ re-BASELINE per doctrine.
2. **Cycle (c)** — offense packet granularity (`UnitsPerAxis`/`MinAxisSize`/`MaxAxes` on `@experimental`). Lowest risk, per-instance, biggest visible early-urgency win; measure on the Motorized rung.
3. **Cycle (a)** — truck-purchase ammo gate (needs the `@experimental` ground-builder split). Med risk.
4. **Cycle (b)** — threat-conditional AA cap + floor (reuses the split from a; tune floor against Motorized's built-in AA). Med risk.

Each cycle: implement-ready above; verify = S1 non-regression + the relevant S2 bar on the (re-BASELINEd) Motorized rung, `@experimental`-vs-`@stable` or code-on-vs-code-out where a shared instance is involved.
