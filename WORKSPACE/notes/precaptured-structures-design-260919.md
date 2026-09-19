# Pre-captured structures — design + calibration (2026-09-19, paused)

Worktree `wt/precaptured`, forked from `main @ 64185a89`. **No engine code written yet.** This note
is the whole of the research so the next session can go straight to implementation.

User's request, verbatim:

> I want an option after 'Forward deployment' that makes all capturable structures on our side
> already captured, to the extent it is possible. If something is genuinely in the middle it is
> neutral (think the nuclear reactor on Woodland Warfare), but everything else is captured by the
> closest player.

---

## 1. What is actually capturable — measured, not assumed

Resolved by walking the mod's own MiniYaml inheritance (`Inherits@` + `-Key:` removals) over the
rules files `mod.yaml` lists. Tool: `tools/precaptured-calibration/precaptured_calibration.py`.

`^BasicBuilding` (structures.yaml:10) pulls `^NeutralOrOccupiedCapturable` (structures.yaml:242),
so *everything* descending from it is capturable unless it strips the traits. Three families strip
them:

| Family | Result | Where |
|---|---|---|
| `^CivBuilding` (V01–V19, SNOWHUT, ASIANHUT, RUSHOUSE, HUTS…) | **not** capturable | `civilian.yaml:12-16` |
| GTWR, PBOX, HBOX | **not** capturable | `structures-defenses.yaml:80-84, 207-211, 330-334` |
| `^Wall` / `^Tree` / `^Rock` (FENC, BARB, BRIK, WOOD, T*, TC*, utilpol*, tanktrap2) | never had it — `^Wall` does not descend from `^BasicBuilding` | `structures-defenses.yaml:29` |

Husks (`OILB.Husk`, `t10.husk`, …) are not capturable. `SUPPLYROUTE` is not capturable — confirming
the CLAUDE.md rule; this feature does not touch it.

**Capturable and Neutral-owned on shipped maps:** `OILB`, `BIO`, `FCOM`, `MISS`, `HOSP`,
`LOGISTICSCENTER`, `GUN`. (`GUN` is the one defense that keeps its capture traits — PBOX/HBOX/GTWR
strip them, `GUN` does not. `ai.yaml:336` already records that asymmetry, so it is intentional.)

### The three filters the trait should use

1. **`Capturable`** — engine truth for "capturable".
2. **`Building`** — the user said *structures*. Excludes the one capturable vehicle
   (`vehicles.yaml:108`, `Capturable: Types: vehicle`). **Decision: vehicles are out of scope.**
3. **`Selectable`** — excludes `BARL` and `BRL3`. Explosive barrels inherit `^TechBuilding`
   (`civilian.yaml:772, 793`) and are therefore genuinely capturable, but they carry `-Selectable:`
   and `MapEditorData: Categories: Decoration`. Handing a player 9 barrels at match start is not
   what "pre-captured structures" means. If a player cannot select it, it is not a structure they
   can own in any meaningful sense. siberian-pass has 6 BARL + 9 BRL3 and seventh-woods 4 BRL3, so
   this filter is load-bearing, not hypothetical.

---

## 2. The anchor — Supply Route, and why the fallback is exact

Anchor per player = the `CenterPosition` of an owned actor carrying `BaseBuilding`
(`structures.yaml:370`; SUPPLYROUTE is the **only** actor in the shipped ruleset with it —
`mcvs.yaml:34` is commented out and `old.yaml` is not in `mod.yaml`'s Rules list). Fall back to
`Map.CenterOfCell(p.HomeLocation)`.

**The fallback is not an approximation.** `SpawnStartingUnits` places the SR at
`p.HomeLocation + BaseActorOffset` with `BaseActorOffset = (-1,-1)` (`MapStartingUnits.cs:37`,
not overridden anywhere in `world.yaml`), and a 3×3 building's `CenterOffset` is `(+1,+1)` cells
(`Building.cs:207-211`). The two cancel: the SR's CenterPosition **is** `CenterOfCell(HomeLocation)`.
So it does not matter whether this trait runs before or after `SpawnStartingUnits` in the
`IWorldLoaded` order (`World.cs:334`, actor trait-creation order) — the geometry is identical.
The fallback matters for autotest scenarios, which routinely carry `-SpawnStartingUnits:`.

---

## 3. "Genuinely in the middle" — a ratio, and the calibrated default

Rule: let `d1` be the distance to the nearest player and `d2` the distance to the nearest player
**not allied with that player**. The structure stays Neutral when `d2 / d1 - 1 <= X`.

**`MiddleBandPercent` default = 10.**

The alliance clause is a judgment call the manager's brief left open ("allies share nothing here —
nearest PLAYER wins; if two allied players tie, either is fine"). Comparing against the nearest
*enemy* rather than the nearest *other player* is what stops a 2v2 leaving a derrick sitting between
two teammates Neutral for no reason (x-lake `oilb (9,64)` sits at a 0.0% margin between spawns 1 and
2 — teammates in the obvious 2v2 — and is 110 cells from either enemy). In FFA and in every 1v1 the
clause is inert, so the table in §4 is unchanged by it. Ties between players resolve on index in
`world.Players`, which is identical on every client.

### Why 10

Sorted margins across all ten shipped maps, FFA, excluding the obvious (>30%):

```
0.0  0.0  0.0  0.0  0.0  0.1  0.4  1.0  2.1  3.8  3.9  4.1  4.5  4.5  7.1  7.2  8.0  8.4
                                                                                   ^^^^
                                                              <-- gap: 8.4% .. 15.2% -->
15.2  16.8  16.8  18.8  18.9  21.9  22.0  23.2  24.6  25.9  27.9  29.3 ...
```

There is a clean empty band between **8.4% and 15.2%**. Any threshold inside it produces
*identical* results on all ten maps, so 10 is the middle of a gap rather than a tuned edge.

**It is also the smallest defensible value.** The brief required the Woodland Warfare reactor to
stay Neutral; its margin is **2.1%**. But `oilb (64,31)` on the same map sits at **0.1%** — it is
*more* central than the reactor — and its mirror `oilb (31,64)` at **4.1%**. Any threshold that
keeps the reactor Neutral and both midfield derricks captured does not exist; and any threshold
below 8.0% splits the symmetric pair of LOGISTICSCENTERs (4.5% vs 8.0%), handing one to a player and
leaving its mirror Neutral. **10% is the smallest value that keeps every symmetric pair on every map
symmetric.** That is the property worth defending, and it should be the first thing re-checked if
anyone retunes X.

(Correction to the brief: Woodland Warfare has **8** `oilb`, not two, and the reactor is `BIO`,
tooltip "Nuclear Reactor", `structures-neutral.yaml:104`. There is no `npwr`/`apwr` in the mod.)

---

## 4. Per-map ownership table — what the user will judge

Computed at X = 10%, FFA, every spawn occupied. `margin` = how much further the second-nearest
spawn is than the nearest. `spawnN` = index into the map's `mpspawn` list, printed with the table.
Reproduce with `python tools/precaptured-calibration/precaptured_calibration.py 10`.

`arena-tank-duel` and `shellmap-open-field` have no spawn points and no neutral capturables — the
option is a no-op on both.

### nuclear-winter-ww3 — spawns (1,64) (100,7)
| actor | at | near | 2nd | margin | → |
|---|---|---|---|---|---|
| fcom | 5,4 | 59.7 | 94.5 | 58.4% | spawn0 |
| fcom | 93,64 | 57.9 | 92.5 | 59.9% | spawn1 |
| miss | 14,41 | 26.5 | 91.7 | 246.2% | spawn0 |
| miss | 84,28 | 26.2 | 91.2 | 247.9% | spawn1 |
| oilb | 18,15 | 51.6 | 81.9 | 58.9% | spawn0 |
| oilb | 28,18 | 53.2 | 72.4 | 36.2% | spawn0 |
| oilb | 40,62 | 39.5 | 81.4 | 105.8% | spawn0 |
| oilb | 60,8 | 39.5 | 81.4 | 105.8% | spawn1 |
| oilb | 71,52 | 53.7 | 71.4 | 33.0% | spawn1 |
| oilb | 81,55 | 51.9 | 80.9 | 55.9% | spawn1 |

Nothing Neutral. 5 each — perfectly symmetric.

### polar-disorder-ww3 — spawns (96,16) (1,81)
| actor | at | near | 2nd | margin | → |
|---|---|---|---|---|---|
| gun | 16,57 | 28.3 | 89.9 | 217.6% | spawn1 |
| gun | 81,40 | 28.3 | 89.9 | 217.6% | spawn0 |
| logisticscenter | 32,64 | 35.6 | 79.9 | 124.7% | spawn1 |
| logisticscenter | 64,31 | 35.1 | 80.5 | 129.3% | spawn0 |
| oilb | 1,87 | 6.5 | 118.5 | 1717.7% | spawn1 |
| oilb | 2,94 | 13.6 | 122.1 | 798.8% | spawn1 |
| oilb | 10,9 | 72.1 | 85.7 | 18.9% | spawn1 |
| oilb | 30,50 | 42.4 | 74.0 | 74.5% | spawn1 |
| oilb | 32,3 | 64.7 | 83.7 | 29.3% | spawn0 |
| oilb | 47,69 | 47.9 | 72.2 | 50.8% | spawn1 |
| oilb | 49,27 | 47.9 | 72.2 | 50.8% | spawn0 |
| oilb | 64,93 | 64.7 | 83.7 | 29.3% | spawn1 |
| oilb | 66,46 | 42.4 | 74.0 | 74.5% | spawn0 |
| oilb | 85,86 | 71.3 | 84.7 | 18.8% | spawn0 |
| oilb | 94,2 | 13.6 | 122.1 | 798.8% | spawn0 |
| oilb | 95,7 | 8.5 | 119.7 | 1306.0% | spawn0 |

Nothing Neutral. 8 each. Note the two corner derricks at 18.8/18.9% go to the *far-corner* player —
correct by the rule, but it is the one row on this map that may read oddly in play.

### river-zeta-ww3 — spawns (81,76) (74,5) (16,6) (23,75) (88,35) (9,45)
| actor | at | near | 2nd | margin | → |
|---|---|---|---|---|---|
| logisticscenter | 32,52 | 24.4 | 24.7 | 1.0% | **NEUTRAL** |
| logisticscenter | 63,26 | 23.9 | 25.9 | 8.4% | **NEUTRAL** |
| oilb | 16,3 | 2.5 | 42.2 | 1554.1% | spawn2 |
| oilb | 20,76 | 2.9 | 33.5 | 1050.2% | spawn3 |
| oilb | 22,44 | 13.5 | 30.5 | 125.8% | spawn5 |
| oilb | 26,22 | 19.6 | 28.5 | 45.7% | spawn2 |
| oilb | 39,53 | 27.1 | 31.7 | 16.8% | spawn3 |
| oilb | 45,1 | 28.7 | 29.8 | 3.9% | **NEUTRAL** |
| oilb | 52,79 | 28.7 | 29.8 | 3.9% | **NEUTRAL** |
| oilb | 57,26 | 27.1 | 31.7 | 16.8% | spawn1 |
| oilb | 74,35 | 13.5 | 30.5 | 125.8% | spawn4 |
| oilb | 75,57 | 19.3 | 25.7 | 33.4% | spawn0 |
| oilb | 76,3 | 2.9 | 33.5 | 1050.2% | spawn1 |
| oilb | 80,78 | 2.5 | 44.1 | 1631.4% | spawn0 |

Both LOGISTICSCENTERs and the symmetric derrick pair stay Neutral. **This is the row X=10 was chosen
for**: at X=8 the 8.4% LOGISTICSCENTER flips and its 1.0% mirror does not.

### seventh-woods-ww3 — spawns (92,112) (30,1) (1,33) (121,81)
| actor | at | near | 2nd | margin | → |
|---|---|---|---|---|---|
| hosp | 62,55 | 63.5 | 63.7 | 0.4% | **NEUTRAL** |
| oilb | 10,5 | 20.0 | 29.1 | 45.4% | spawn1 |
| oilb | 36,33 | 33.1 | 35.5 | 7.1% | **NEUTRAL** |
| oilb | 39,54 | 44.1 | 54.3 | 23.2% | spawn2 |
| oilb | 82,58 | 44.6 | 54.3 | 21.9% | spawn3 |
| oilb | 85,79 | 33.1 | 35.5 | 7.2% | **NEUTRAL** |
| oilb | 110,108 | 18.8 | 29.4 | 56.3% | spawn0 |

The central hospital stays Neutral, as does the symmetric 7.1/7.2% derrick pair.

### siberian-pass-ww3 — spawns (95,15) (1,51)
| actor | at | near | 2nd | margin | → |
|---|---|---|---|---|---|
| miss | 48,32 | 49.2 | 51.4 | 4.5% | **NEUTRAL** |
| oilb | 18,20 | 35.2 | 76.7 | 118.1% | spawn1 |
| oilb | 38,42 | 38.5 | 62.8 | 63.4% | spawn1 |
| oilb | 57,23 | 38.5 | 62.8 | 63.4% | spawn0 |
| oilb | 77,45 | 35.2 | 76.7 | 118.1% | spawn0 |

The mid-map Communications Center stays Neutral; 2 derricks each.

### twin-rivers-ww3 — spawns (1,22) (1,92) (112,92) (112,28)
| actor | at | near | 2nd | margin | → |
|---|---|---|---|---|---|
| oilb | 4,59 | 32.7 | 37.7 | 15.2% | spawn1 |
| oilb | 7,7 | 15.9 | 84.7 | 433.3% | spawn0 |
| oilb | 8,102 | 12.9 | 80.8 | 526.6% | spawn1 |
| oilb | 56,2 | 58.8 | 61.1 | 3.8% | **NEUTRAL** |
| oilb | 62,108 | 52.2 | 63.7 | 22.0% | spawn2 |
| oilb | 68,71 | 48.1 | 61.5 | 27.9% | spawn2 |
| oilb | 83,76 | 32.4 | 56.3 | 73.4% | spawn2 |
| oilb | 99,7 | 24.0 | 85.4 | 255.8% | spawn3 |
| oilb | 100,103 | 16.3 | 76.4 | 369.6% | spawn2 |
| oilb | 108,45 | 17.8 | 46.6 | 161.3% | spawn3 |

**The one row to look at twice.** `oilb (4,59)` is at 15.2%, just above the band, and its right-edge
counterpart `(108,45)` is at 161% — the map is not mirror-symmetric, so this is not a split pair.
spawn2 collects 4 derricks here against spawn0's 1; that is the map's own layout, not the rule.

### woodland-warfare-ww3 — spawns (1,4) (96,93) — **the calibration map**
| actor | at | near | 2nd | margin | → |
|---|---|---|---|---|---|
| **bio (Nuclear Reactor)** | 48,47 | 64.4 | 65.8 | **2.1%** | **NEUTRAL** ✔ required |
| logisticscenter | 20,73 | 72.2 | 78.0 | 8.0% | **NEUTRAL** |
| logisticscenter | 74,23 | 72.7 | 76.0 | 4.5% | **NEUTRAL** |
| oilb | 4,18 | 14.9 | 118.0 | 691.0% | spawn0 |
| oilb | 23,91 | 72.5 | 90.3 | 24.6% | spawn1 |
| oilb | 26,26 | 34.0 | 96.2 | 182.9% | spawn0 |
| oilb | 31,64 | 67.8 | 70.5 | 4.1% | **NEUTRAL** |
| oilb | 64,31 | 69.1 | 69.2 | 0.1% | **NEUTRAL** |
| oilb | 70,70 | 34.0 | 96.2 | 182.9% | spawn1 |
| oilb | 73,4 | 72.5 | 91.3 | 25.9% | spawn0 |
| oilb | 92,79 | 13.9 | 118.6 | 750.6% | spawn1 |

Reactor Neutral ✔. 3 derricks each ✔. The two midfield derricks and both LOGISTICSCENTERs sit on the
diagonal bisector and stay Neutral — still worth a fight, which is the point.

### x-lake-ww3 — spawns (128,21) (1,21) (1,108) (128,108)
| actor | at | near | 2nd | margin | → |
|---|---|---|---|---|---|
| bio | 64,64 | 77.0 | 77.0 | 0.0% | **NEUTRAL** |
| oilb | 9,64 / 118,64 / 64,9 / 64,117 | 44–65 | = | 0.0% | **NEUTRAL** (FFA) |
| oilb | 4,4 4,8 8,4 | 13–18 | ~100 | 472–667% | spawn1 |
| oilb | 4,120 4,124 8,124 | 13–18 | ~100 | 472–667% | spawn2 |
| oilb | 120,4 124,4 124,8 | 13–18 | ~100 | 472–667% | spawn0 |
| oilb | 120,124 124,120 124,124 | 13–18 | ~100 | 472–667% | spawn3 |

Perfectly symmetric: 3 corner derricks each, centre reactor and the 4 edge-midpoint derricks
Neutral. **In a 2v2 the alliance clause changes this**: the two edge-midpoint derricks that sit
between teammates get captured instead of staying Neutral. That is the clause doing its job.

---

## 5. Implementation plan (not yet written)

### `engine/OpenRA.Mods.Common/Traits/World/PreCapturedStructures.cs`
- `[TraitLocation(SystemActors.World)] PreCapturedStructuresInfo : TraitInfo, ILobbyOptions`.
- `public const string OptionId = "precapturedstructures";`
- Fields mirroring the `ForwardDeployment*` set on `SpawnStartingUnitsInfo`:
  `CheckboxLabel = "Pre-captured Structures"`,
  `CheckboxDescription = "Capturable structures start owned by the nearer player; ones in the middle stay neutral"`,
  `CheckboxEnabled = false` (**default OFF — Skirmish must not change silently**),
  `CheckboxVisible = true`, `CheckboxDisplayOrder = 8` in C# (overridden to **52** in `world.yaml`,
  immediately after `ForwardDeploymentDropdownDisplayOrder: 51`), `CheckboxLocked = false`.
- `MiddleBandPercent = 10` with a `[Desc]` carrying the §3 gap reasoning.
- `ILobbyOptions.LobbyOptions` yields one `LobbyBooleanOption(OptionId, …)` — same shape as
  `DoomsdayStrike.cs:311`.
- `PreCapturedStructures : IWorldLoaded`. In `WorldLoaded`:
  - `if (!world.LobbyInfo.GlobalSettings.OptionOrDefault(OptionId, info.CheckboxEnabled)) return;`
    — return **before** touching the actor list, the rules or `SharedRandom`, so the default-off
    match is observably identical (the invariant `SpawnForwardDeployment` documents at
    `SpawnStartingUnits.cs:~205`).
  - Build anchors: `world.Players.Where(p => p.Playable && !p.NonCombatant)`, anchor =
    nearest owned `BaseBuilding` CenterPosition else `CenterOfCell(p.HomeLocation)`.
  - Candidates: `world.Actors` where `Owner.NonCombatant` (Neutral) and the three filters of §1,
    tested on `Info.HasTraitInfo<CapturableInfo>() / BuildingInfo / SelectableInfo`.
  - Decide via the pure static helper, then `actor.ChangeOwner(winner)` — the queued FrameEndTask
    form. **Not** `ChangeOwnerSync`, which is documented as callable only from inside an existing
    FrameEndTask (`Actor.cs:565-568`); `WorldLoaded` is not one. Consequence for the scenario: the
    transfer lands at the end of tick 1, so assertions must allow a few ticks.
- **Pure decision function**, separate and NUnit-visible (this is what the fixture tests):
  ```
  public static int Resolve(IReadOnlyList<(long DistSq, int Team)> candidates, int middleBandPercent)
  ```
  returns the winning index or -1 for Neutral. Keep it integer-only (compare
  `d2 * 100 <= d1 * (100 + X)` on **distances**, so take square roots once or compare squared
  distances against a squared ratio) — no floats in a synced path.

### YAML — `mods/ww3mod/rules/world.yaml`
Add immediately after the `SpawnStartingUnits:` block (~line 640):
```
	PreCapturedStructures:
		CheckboxDisplayOrder: 52
```
Lint expectation: an unknown trait name fails `--check-yaml` with
`Trait name PreCapturedStructures does not exist`; an unknown field fails with
`Trait ... does not have a property CheckboxDisplayOrder`. Neither is in `lint-baseline.txt`, so
either would be a hard red.

### NUnit — `engine/OpenRA.Test/…`
Fixtures from §4: reactor 64.4/65.8 → Neutral; `oilb (26,26)` 34.0/96.2 → spawn0; the
river-zeta 8.4% LOGISTICSCENTER → Neutral; the twin-rivers 15.2% derrick → captured (the pair that
pins the band edges). Plus one allied-pair case: d1 = d2 with the same team → captured, not Neutral.

### Scenario — `tools/autotest/scenarios/test-precaptured-structures/`
Small rig, two `supplyroute` actors, three neutral `oilb`: one near USA, one near Russia, one on the
bisector. `map.yaml` must declare `Rules: rules.yaml`. `rules.yaml` sets
`World: PreCapturedStructures: CheckboxEnabled: true` and `-SpawnStartingUnits:`. Sibling arm
`test-precaptured-structures-off` leaves the default and asserts all three stay Neutral.
Run `./make.ps1 lua-gate` after adding it. Both players need `Playable: True`
(15 existing scenarios already do this, e.g. `test-crate-rearm`).

---

## 6. Gates not yet run

Nothing to gate — no C# and no YAML changed. `./make.ps1 all`, `./make.ps1 check`, `dotnet test`
and `./make.ps1 lua-gate` are all still outstanding, and this worktree has **never been built**, so
the first `make.ps1 all` here will be slow (and is a prerequisite for any autotest run —
`AUTOTEST.md` §"NO-RESULT also covers the game never launched").
