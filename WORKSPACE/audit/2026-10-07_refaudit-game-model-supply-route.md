# Reference-doc accuracy audit: `game-model.md` and `supply-route.md`

**Audited:** 2026-10-07, against `main @ c276679c` (branch `wt/refaudit-gm`). Static reading only: no build, no launch, no lint.
**Tick rate used for every duration:** default speed `Timestep: 60` ms (`mods/ww3mod/mod.yaml:429-432`, `DefaultSpeed: default` at `:407`), so **16.67 ticks/s**. Every tick→second figure below uses that.

Verdicts: **OK** = re-derived, holds. **Re-pointed** = the claim holds, the line cite had drifted. **Corrected** = the claim was wrong; fixed in the doc. **Unverifiable** = left as written, reason given.

## game-model.md

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 1 | SR has no `Capturable`/`CaptureManager`, inherits none | OK | `structures.yaml:295-298` (bases `^ExistsInWorld`/`^SpriteActor`/`^SelectableBuilding`); resolved inheritance has neither |
| 2 | SR protected by `Targetable: TargetTypes: NoAutoTarget` | OK | `structures.yaml:389-390`; no weapon lists `ValidTargets: … NoAutoTarget` (grep empty) |
| 3 | `Health: HP: 75000` | OK | `structures.yaml:387-388` |
| 4 | `Armor: Indestructable` inert | OK | `structures.yaml:416-417`; "Indestructable" appears in no `Versus:` key, only comments (`weapons-missiles.yaml:404,407`, `weapons-superweapons.yaml:1508`) |
| 5 | `-Vaporizable:` on `SUPPLYROUTE` | OK | `structures.yaml:318` |
| 6 | "SR leaves a player's hands by being contested down or on defeat" | **Corrected** | Ownership changes only via `OwnerLostAction` on defeat (`structures.yaml:363-365`, called from `ConquestVictoryConditions.cs:137`/`StrategicVictoryConditions.cs:165`). Contestation never transfers (`SupplyRouteContestation.cs:702-742` sets `isPassive` / eliminates; no owner change) |
| 7 | All unit types via `ProductionFromMapEdge`; buildings/defences via `Production@Local` | OK | `structures.yaml:461-474` (`Production@Local` also produces `Powers`) |
| 8 | AI YAML lists `supplyroute` under ConYard/Factory/Barracks types | Unverifiable (not re-read) | not opened this pass |
| 9 | `ProductionFromMapEdge.FindClosestSpawnArea` `:57`, fallback `?? self.Location` `:101`, `:119` | OK | `ProductionFromMapEdge.cs:57,101,119` |
| 10 | `ProductionFromMapEdge` declared at `structures.yaml:472` inside actor opening `:295` | OK | `structures.yaml:295,472` |
| 11 | `RotateToEdge` ground branch `FindClosestSpawnAreaForOwner(self) ?? FriendlyEvacuationOrigin(self)` at `:209`; origin chain `:158-170`; aircraft `?? self.Owner.HomeLocation` `:195` | OK | `Activities/RotateToEdge.cs:158-169,195,208-209` |
| 12 | Only `river-zeta-ww3` ships `spawnarea` actors (6, one per mpspawn); other nine zero | OK | `grep -c spawnarea` over the 10 `map.yaml`: river-zeta 6 (6 mpspawns), rest 0 |
| 13 | Most autotest scenarios have zero `spawnarea` | OK | 19 of 368 scenario `map.yaml` contain it |
| 14 | 70.4% opponent-wall evac figure (item 78) | Unverifiable | historical measurement |
| 15 | `SkipRearmBuildingCheck` on `UnitBuilderBotModule`; `HasAdequateAirUnitReloadBuildings` still present | OK | `UnitBuilderBotModule.cs:105,1838` |
| 16 | `^CapturesNeutralBuildings` `infantry.yaml:956`, `ConsumedByCapture: true` `:962` | OK | `infantry.yaml:956,962` |
| 17 | `^CapturesOccupiedBuildings` `:934`, `ValidRelationships: Enemy` `:953`, `CaptureToNeutral`+`EnterBehaviour: Exit` `:954-955` | OK | `infantry.yaml:934-955` |
| 18 | "all 23 capturable actors" + list | **Corrected** (wording) | The 23-name list matches exactly the actors resolving `Capturable@occupied` (`building-occupied`, `structures.yaml:242-248`) — re-derived with an inheritance resolver over `mod.yaml` Rules. But 86 actors carry *some* `Capturable` (vehicles, husks, LCCV, TRUK…). Reworded to "23 actors carrying the `building-occupied` type" |
| 19 | Both flags on `CapturesInfo`, default stock (consume, transfer) | OK | `Captures.cs:41,46,51` |
| 20 | `Captures.cs:41` defaults `ConsumedByCapture` to true | OK | `Captures.cs:41` |
| 21 | `Capturable` carries only `Types`, no relationship filter | OK | `Capturable.cs:22-23` |
| 22 | nuclear-winter `Creeps` is NonCombatant with `Enemies: Multi0, Multi1` (`map.yaml:25-29`) | OK | `nuclear-winter-ww3/map.yaml:25-29` |
| 23 | `CaptureDelay` 500 ticks = 30.0 s (`infantry.yaml:948`) | **Re-pointed** → `:947`; 500/16.67 = 30.0 s OK | `infantry.yaml:947` |
| 24 | `CaptureManager` gates entry on `currentTargetDelay`, incremented per tick | OK | `CaptureManager.cs:63,197-200` |
| 25 | `CaptureClearDurationTest` pins it | OK (exists) | `engine/OpenRA.Test/CaptureClearDurationTest.cs` |
| 26 | `CaptureCoordinatorBotModule` capped at three technicians, no reclaim logic | Unverifiable (not re-read) | — |
| 27 | `CrewFireDurationTicks` exists nowhere | OK | grep over engine+mods: no hits |
| 28 | `VehicleCrew.cs:359-362` grants `onfire` with no duration | **Re-pointed** → `:458-462` | `VehicleCrew.cs:458-462` (`crew.GrantCondition(fireCondition)` in a loop, no duration) |
| 29 | `VehicleCookoffTiny` `Duration: 25` `:46`, `VehicleCookoffLarge` `Duration: 150` `:68` | **Re-pointed** → `:47`, `:69` | `weapons-explosions.yaml:36,47,58,69` |
| 30 | `ChangesHealth@BurnDamage_3` `infantry.yaml:879-883`, −1% every 8 ticks | **Re-pointed** → `:981-985` | `infantry.yaml:981-985` |
| 31 | "every ejected crewman dies eventually"; mechanism is "always, eventually" vs ruling "sometimes" | **Corrected** | Stacks = vehicle fire stacks + `CrewFireStackOffset` (−3, `VehicleCrew.cs:106`, computed `:424-434`); fire ramp `StartFraction 50 / EndFraction 0 / MaxStacks 10` (`vehicles.yaml:217-221`); `CalculateStacks` (`GrantStackingConditionOnHealthFraction.cs:84-96`) gives ≥4 vehicle stacks only at ≤35% HP. Crew ejected above 35% get 0 stacks and do not burn |
| 32 | Vehicle 1%-per-5-tick bleed below Heavy | OK | `vehicles.yaml:184-187` (`ChangesHealth@CriticalDamage`, `StartIfBelow: 50`) |
| 33 | Skirmish is default and a strict no-op for Escalation | Unverifiable (not re-read) | — |
| 34 | `SizeFor` rounds half up then clamps, `FinalExchangePackage.cs:46-60` | OK | `FinalExchangePackage.cs:46-61` |
| 35 | `CellsPerImpact 2400`, `MinPackage 2`, `MaxPackage 6` at `world.yaml:769-775` | OK | `world.yaml:769,772,775` |
| 36 | N-per-map table | OK | Re-derived from each `map.yaml` `Bounds` w×h: arena 2048→2, shellmap 5400→2, nuclear-winter 7000→3, river-zeta 7680→3, siberian 6175→3, polar 9216→4, woodland 9216→4, seventh-woods 13552→6, twin-rivers 15876→6, x-lake 16384→6 |
| 37 | Final exchange window 250 ticks = 15.0 s (`world.yaml:840`) | OK | `world.yaml:840`; 250×60 ms = 15.0 s |
| 38 | America's ender `MissileStrikePower@TridentW88` at `player.yaml:242` | OK | `player.yaml:242-243` |
| 39 | `NuclearGameEnders.Is` via `RungForYield` (`:77-86`) | **Re-pointed** → `:76-85` | `NuclearGameEnders.cs:76,85` |
| 40 | Tsar Bomba excluded via `SandboxOnlyAboveTons` | OK (constant exists) | `NuclearReleaseLadder.cs:137` |
| 41 | `FinalExchangeMissileDelay: 100` (`world.yaml:793`) | OK | `world.yaml:793` |
| 42 | Powers' `MissileDelay` 500 = thirty seconds | OK | Sarmat/TridentW88 `MissileDelay: 500` (`nuclear-arsenal.yaml:276,367`); 500×60 ms = 30 s |
| 43 | `RungForYield` pure over four ceilings, `NuclearReleaseLadder.cs:140-158` | OK | `NuclearReleaseLadder.cs:118-121,140-158` |
| 44 | Tier lines in merged `Player:` at `player.yaml:211-243` | OK | `player.yaml:211-243` (B83 event entry continues to `:261`) |
| 45 | `Active` / `ReleasedRung` at `NuclearUnlockClock.cs:320`, `:328-330`; `IsBandPurchasable` true when `!Active` `:347-349`; `IntervalOptions` `:123` | OK | `NuclearUnlockClock.cs:123,320,328,346-349` |
| 46 | `world.yaml:943` registers `NuclearUnlockClock:` bare | OK | `world.yaml:943` |
| 47 | `PreCapturedStructures` border logic `:300-371`, `SideOfFootprint` `:400-409`, `MiddleBandPercent: 10` `:214` | OK | `PreCapturedStructures.cs:214,305,370,400` |
| 48 | 90 eligible structures owned, 0 neutral; ratio rule left 19 neutral | Unverifiable | runtime measurement |
| 49 | `BARL`/`BRL3` inherit `^TechBuilding` (`civilian.yaml:772, 793`) | **Re-pointed** → `:1820-1821, 1841-1842` | `civilian.yaml:1820-1842` |
| 50 | Trait excludes them via `-Selectable:`; owner filter `OwnsWorld` | OK | `PreCapturedStructures.cs:338-350` |
| 51 | nuclear-winter `MSLO` owned by `Creeps` (`map.yaml:1146-1148`), the only non-Neutral capturable on the ten maps | OK | `nuclear-winter-ww3/map.yaml:1146-1148`; owner scan of all ten maps for the 23 types finds only this one |

## supply-route.md

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 52 | Every `StartingUnits@*` has `BaseActor: supplyroute` | OK | 9 of 9 blocks, `world.yaml:598-669` |
| 53 | `Armor: Indestructable` at `structures.yaml:368-369` | **Re-pointed** → `:416-417` | |
| 54 | `DamageWarhead.DamageVersus` returns unmodified for unlisted type (`:96-108`) | **Re-pointed** → `:97-109` | `DamageWarhead.cs:97-109` |
| 55 | `ArmorInfo.Type` has no other effect (`Armor.cs:20-21`) | **Re-pointed** → `:26`, narrowed to "no other effect on damage" | also read by the tooltip (`Armor.cs:37-70`) and `AutoTarget.cs:1731` (damage estimate); neither changes a hit |
| 56 | `Targetable` at `structures.yaml:347-348` | **Re-pointed** → `:389-390` | |
| 57 | Warheads default `ValidTargets: Ground, Water`; `IsValidAgainst` rejects (`Warhead.cs:55-74`) | OK (re-pointed end → `:75`) | `Warhead.cs:30,55-75` |
| 58 | `Targetable.TargetTypes` has no initializer (`Targetable.cs:23`) | OK | `Targetable.cs:23` |
| 59 | `VaporizeWarhead.IsValidAgainst` ignores target types | OK | `VaporizeWarhead.cs:23,83` |
| 60 | `-Vaporizable:` deletion → "immediate defeat" by `RequiredForShortGame` | **Corrected** | `RequiredForShortGame: true` on SR (`structures.yaml:368-369`) and on `^Building` (`:149-150`), which `LOGISTICSCENTER`/`HPAD`/`AFLD` inherit (`:499`,`:779`,`:847`). `HasNoRequiredUnits` (`PlayerExtensions.cs:19-24`) only fails a player owning none. Short game on by default (`MapOptions.cs:32`; `world.yaml:586` hides checkbox only). Defences opt out (`structures-defenses.yaml:14-15`). So defeat is immediate only if no such building is held |
| 61 | `VaporizeScopeTest.TheSupplyRouteOptsOutOfVaporisation` | OK (exists) | `VaporizeScopeTest.cs:192` |
| 62 | 10-cell contestation circle | OK | `structures.yaml:392,397` |
| 63 | Comparison-table row "Indestructible — only captured or contested" | **Corrected** | contradicted the doc's own §UNTARGETABLE and §Capture; now "Untargetable … only contested; capture designed but not wired" |
| 64 | SR at `spawn + (-1,-1)`: `SpawnStartingUnits.cs:91`, `MapStartingUnits.cs:37` | **Re-pointed** → `SpawnStartingUnits.cs:191`; `:37` OK | |
| 65 | No `BaseActorOffset` override under `mods/`; no map places `supplyroute` | OK | grep: zero hits for both |
| 66 | Parity worked trap (siberian spawns 95,15 / 1,51; twin-rivers 112,92 & 112,28 only odd/odd SR pair; arena/shellmap all-even spawns) | OK | mpspawn `Location`s in each `map.yaml` |
| 67 | `SUPPLYROUTE` at `structures.yaml:222` (Capture §, Engine §) | **Re-pointed** → `:295` (3 sites) | |
| 68 | `^NeutralOrOccupiedCapturable` at `:169` | **Re-pointed** → `:242` | |
| 69 | Commented-out `CaptureNotification` at `:236` | **Re-pointed** → `:329` | |
| 70 | `OwnerLostAction` at `:270-272` | **Re-pointed** → `:363-365` (2 sites) | |
| 71 | `OnOwnerLost` only from `CVC.cs:109` / `SVC.cs:152` | **Re-pointed** → `:137` / `:165` (2 sites) | grep `OnOwnerLost(` call sites: exactly these two |
| 72 | `CaptureToNeutral` / `EnterBehaviour` on `CapturesInfo`, default off | OK | `Captures.cs:46,51` |
| 73 | `../gameplay/capturing.md` link | OK | file exists |
| 74 | `BaseTicks: 1500`, `SlowdownThreshold: 50`, `FriendlyRecoveryMultiplier: 3`, `BaseAttack` + text | OK | `structures.yaml:399,406-409` |
| 75 | `OnDefeatBarFull` `:702` called from `:572` | OK | `SupplyRouteContestation.cs:572,702` |
| 76 | `ResolveTeamElimination` `:810`, `HasActiveTeamSupplyRoute` `:745-748`, `OtherSupplyRoutes` `:752` | OK | |
| 77 | In 1v1 passive and Lost land the same tick | OK | `OtherSupplyRoutes` skips self (`:756`), `HasRescuer` needs a same-team other (`:771-795`) |
| 78 | `OnReinstated` `:992` called from `:602` | OK | |
| 79 | `HasRescuer` `:771`, pinned in `SupplyRouteEliminationTest.cs` | OK | test file references it (`:309`) |
| 80 | Wording "Production frozen.", not income | OK | `SupplyRouteContestation.cs:121,125,727` |
| 81 | Public `IsPassive` at `:262`, zero call sites | OK | grep `.IsPassive` repo-wide: only the tuple field inside the same file (`:790`) |
| 82 | `AwardDecidedSurvivors` `:475`, from `CVC.OnPlayerLost` `:119` | **Re-pointed** → `:862`; CVC `:134` (call `:146`) | |
| 83 | `MarkFailed` overwrites Completed, re-fires `OnPlayerLost` (`MissionObjectives.cs:146-162`); `MarkCompleted` no-ops non-Incomplete (`:129`) | OK | |
| 84 | `MarkCompleted` fires `OnPlayerWon` only when all required Completed (`:136-137`) | OK | |
| 85 | `AwardVictory` `:580` | **Re-pointed** → `:969` | |
| 86 | `ResolveTeamElimination` TestMode early-return `:814`; CVC.Tick `:63`; `CheckIfGameIsOver` `:171` | **Re-pointed** CVC → `:62`; others OK | |
| 87 | `Player.WinState` Lua getter `PlayerProperties.cs:72` | OK | |
| 88 | Shipped `IgnoreDangerForDelivery: true` at `ai.yaml:1689`; gate at `SupplyFollowerBotModule.cs:1662` | **Re-pointed** → `ai.yaml:1813`; gate `:1662` OK | |
| 89 | `AutoSeekSupplies.cs:197`, dispatch `:202`; `SupplyHuntLeashCells` 20; `ScanInterval: 40` | **Re-pointed** `:197` → `:185` (provider find; leash `:457`); rest OK | `AutoSeekSupplies.cs:44,48,185,202,457` |
| 90 | `SupplyProvider.OnSupplyErrand` reads only four activity types | OK | `SupplyProvider.cs` ~`:312` |
| 91 | `DangerSelectsDrop` at `SupplyDropMath.cs:234` | **Re-pointed** → `:388` | |
| 92 | `GroundDangerMedian` at `DangerFieldLayer.cs:925` | **Re-pointed** → `:1128` | |
| 93 | `DropAnchorAtCluster: true` `ai.yaml:1819`; `ClusterDropAnchor` `:1834` | **Re-pointed** ai.yaml → `:1960`; C# OK | |
| 94 | `SelectionMinStarvingUnits: 1` `:1688`, `DropMinStarvingUnits: 1` `:1914`; `CountStarvingNear` `:2101` over an 18-cell disc | **Re-pointed** → `:1829`, `:2055`; C# OK; 18 = `DropDemandRadiusCells 20 − margin 2` OK | `SupplyFollowerBotModule.cs:2101-2124`, `ai.yaml:2044` |
| 95 | `ResolveDropAnchor` `:1882` | OK | |
| 96 | `ClusterStickinessNeedMargin` `ai.yaml:1574`, comment `:1568-1573`, C# default 0 | **Re-pointed** → `:1708` (shipped 1000), comment `:1702-1707` | |
| 97 | `DropMinSupply: 100` `:1921`; `RestockThreshold: 50` | **Re-pointed** → `:2062`; `RestockThreshold` OK (`vehicles.yaml:626`) | |
| 98 | Box: "`ai.yaml:1657` and `:1679` still describe `DropMinStarvingUnits` as 3, not corrected in place" | **Corrected** | Both comments now read the shipped 1: `ai.yaml:1791-1795`, `:1816-1818`; the "3 -> 1" record is at `:2052` |
| 99 | Box: `IgnoreDangerForDelivery` checked before `FindSafeFollowPosition` at `:1000` | **Re-pointed** → `:2488-2489` (`:1000` is bypass site 1, cluster selection) | |
| 100 | Box: `[Desc]` site 7 text at `:141` | OK | |
| 101 | Commitment: frozen destination `ResolveDropAnchor` `:1442` | **Re-pointed** → `:1882` | |
| 102 | Commitment flags / `ErrandStillRunning` / `ClassifyErrand` exist | OK | `SupplyFollowerBotModule.cs:348,370`; `SupplyDropMath.cs:107,339` |
| 103 | Follow leash `ai.yaml:805/818-819`; `FollowLeashCellsFor` `:1153` | **Re-pointed** → `:1648/1661-1662`; `:1453` | |
| 104 | `AutoSeekSupplies` `infantry.yaml:221-222` | **Re-pointed** → `:252-254` | |
| 105 | Strategic-implications "Indestructible; only capture and contestation work" | **Corrected** | same contradiction as #63 |
| 106 | `AttackSupplyRoute` moves into range (`:84`), stands (`:100`); no `Armament`/`CheckFire`/`AttackBase`/`Ammo` | OK; stand re-pointed → `:93-100` | grep of the file: none of the four |
| 107 | `RecalculateForces` `:184-206` scores by `Valued.Cost` only | **Re-pointed** → `SupplyRouteContestation.cs:315-338` | |
| 108 | 3×3 footprint `structures.yaml:285-286` | **Re-pointed** → `:378-379` | |
| 109 | Spawn wiring `world.yaml:316–388` | **Re-pointed** → `:598–669` | |
| 110 | Contestation trait at `structures.yaml:303`, actor `:222` | **Re-pointed** → `:396`, `:295` | |
| 111 | Public accessors `SupplyRouteContestation.cs:220-226` | **Re-pointed** → `:256-262` | |
| 112 | `AcceptsDeliveredCash` `:370-371`; `DeliversCash@Rotation` at `vehicles.yaml:133`, `aircraft.yaml:170,220`, `infantry.yaml:156` | **Re-pointed** → `:477-478`; aircraft `:177,227`; infantry `:157`; vehicles OK | |
| 113 | Targeter priority 5 / cursor `enter` (`DeliversCash.cs:150`, `:38`) outranks `Mobile` 4 (`Mobile.cs:1258`) | OK | |
| 114 | Settled reasoning at `DeliversCash.cs:101-114` | **Re-pointed** → `:105-124` | |
| 115 | `Prerequisites: ~disabled` keeps SR un-buildable | OK | `structures.yaml:383` |
| 116 | Measured match figures (2026-08-10 evac loop, tick 250 riflemen, 0.069 cells/tick, 54.1% NoDemand, 85% follow) | Unverifiable | historical runtime measurements |

**Totals:** 116 claims checked — **63 OK, 40 re-pointed, 7 corrected** (#6, #18, #31, #60, #63, #98, #105; #63 and #105 are the same contradiction at two sites), **6 unverifiable** (#8, #14, #26, #33, #48, #116). Rows marked "OK" with a re-pointed sub-cite are counted as re-pointed.

## Code / YAML comments that contradict the code — NOT edited (for the code lane)

| Where | Says | Actually |
|---|---|---|
| `mods/ww3mod/rules/world.yaml:705-706` | final-exchange window is "thirty seconds … raised from fifteen on 2026-09-16" | `FinalExchangeWindowTicks: 250` (`world.yaml:840`) = **15.0 s**; the raise to 500 was reversed on 2026-09-20 (`world.yaml:829` already says 15.0 s) |
| `mods/ww3mod/rules/ingame/structures.yaml:312-315` | a player with no SUPPLYROUTE "has no required units" → "IMMEDIATE DEFEAT"; cites `ConquestVictoryConditions.cs:76-77` | Only if they own no other `RequiredForShortGame` actor — `^Building` sets it true (`structures.yaml:149-150`), inherited by `LOGISTICSCENTER`/`HPAD`/`AFLD`. The CVC lines are now `:80-81` |
| `engine/OpenRA.Mods.Common/Traits/DeliversCash.cs:110` | `AcceptsDeliveredCash` at `structures.yaml:222` | `SUPPLYROUTE:` opens at `:295`; the trait is at `:477-478` |
| `engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs:42` | Timestep 60 ms at `mod.yaml:381` | `mod.yaml:431` (the 16.67 tps value itself is right) |
| `engine/OpenRA.Mods.Common/Traits/World/PreCapturedStructures.cs:292` | `DefconWall` declared at `world.yaml:971` | `world.yaml:1021` (`PreCapturedStructures` at `:688` is right; the ordering conclusion still holds) |

## Out of scope but stale — `CLAUDE.md` (not a target of this audit, not edited)

`CLAUDE.md`'s Supply Route hard rule cites `structures.yaml:347-348` (Targetable → now `:389-390`), `:368-369` (Armor → now `:416-417`; `:368-369` is now `MustBeDestroyed`), `:303` (SupplyRouteContestation → now `:396`) and `:222` (actor → now `:295`). Its claim that the 25-tps duration error "is still live at ten other sites" was not re-counted.
