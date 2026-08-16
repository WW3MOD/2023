# Bug-backlog reconciliation — recorded entries vs. the code as it actually is

> **Scope:** every open entry in `WORKSPACE/RELEASE_V1.md` (Phase A in-flight + Phase B), `WORKSPACE/bugs/discovered.md`, and the code-asserting items of `WORKSPACE/AWAITING-USER.md`, checked against the tree.
> **Reconciled against `main` @ `55459146`** ("merge wt/heli-gun: unzero the littlebird, then tune it down"), working tree clean, in sync with `origin/main`.
> **Method:** read-only. Verified against CODE, never against the documents. No game launched, no autotest run, no build. Line numbers in the source documents are months old, so symbols were located by grep and the CURRENT `file:line` is reported.
> **Nothing in this document was fixed.** It is a fact-finding pass.

---

## 0. Tally

| Status | Count |
|---|---|
| **STILL REAL** (defect present at HEAD) | **68** |
| **FIXED** (later commit closed it; never struck off) | **49** |
| **STALE PREMISE** (system no longer works that way) | **9** |
| **NOT A BUG** — unbuilt feature filed as a defect | **9** |
| **CANNOT DETERMINE STATICALLY** (needs live repro) | **9** |
| **DUPLICATE** (same defect filed twice) | **1** |
| Backlog entries reconciled | **~150** |
| Unfinished-work markers found NOT on any list | **12 substantive** (of 504 raw / 56 WW3MOD-authored) |

Of the STILL REAL 68, **8 are latent** (real defect on an unreachable path — see §4) and **8 are documentation-only** (a wrong comment, not a player-facing bug).

**Headline: roughly one in three open entries is not open.** 49 fixed + 9 stale + 9 not-a-bug + 1 duplicate = **68 of ~150 entries describe nothing actionable**, exactly matching the count that do.

---

## 1. Prioritised STILL REAL bugs

### The eight that should be decided first

---
- **[BLOCKER]** Creating any veteran actor hard-crashes the game
- **Perceived:** instant crash-to-desktop the moment a promoted or map-placed veteran unit enters the world.
- Evidence: `mods/ww3mod/rules/ingame/infantry-america.yaml:16,29,50` + `infantry-russia.yaml:16,29,50` + `infantry.yaml:1188,1308,1445,2469,2479,2489,2499` — all 13 `OverrideActor` values are **capitalised** (`E1.america`, `PILOT`). The lookup at `engine/OpenRA.Mods.Common/Traits/Player/PlayerStatistics.cs:74` is `Rules.Actors[name]`, and `engine/OpenRA.Game/Primitives/ActorInfoDictionary.cs:36` is a plain case-sensitive `Dictionary`. Reached from `PlayerStatistics.cs:302,346,380,406`.
- Confidence: **high** — I verified both halves personally (capitalised values, case-sensitive dict). Actor definitions are lowercase throughout.
- Rough fix size: one line (`StringComparer.OrdinalIgnoreCase` in `ActorInfoDictionary`) **or** 13 YAML values. No test creates a veteran, so add one.
- Note: this is the same defect family that already bit twice (`UnitBuilderBotModule`, `SquadManager.AirUnitsTypes`, `AirstrikePower`). The durable `ActorNameCase` fix shipped for the bot path but was never applied here.

---
- **[BLOCKER]** Fog of war is switched off for every building, by a shipped short-circuit
- **Perceived:** all enemy and neutral structures render through fog for the entire match. Fog is meaningless for buildings.
- Evidence: `engine/OpenRA.Mods.Common/Traits/Modifiers/FrozenUnderFog.cs:146` — a bare `return true;` under a `// QUICK FIX 260503` comment, with the real call left dead at `:148` (`// return IsVisibleInner(byPlayer);`).
- Confidence: **certain** — read directly.
- Rough fix size: needs design. Small in lines, but the comment records why the strict path was abandoned (`frozen.Visible` defaults true and `state.IsVisible` inverts it, leaving neutral/civilian buildings invisible at game start). Budget a day plus an autotest, not a one-liner.
- Note: this is **honestly filed** in `RELEASE_V1.md` and the code comment even points back at the tracker. It is the single most trustworthy entry in the whole backlog. It also blocks Supply Route contestation and the garrison playtest.

---
- **[BLOCKER]** `make test` — the project's own YAML guard rail — has been RED on `main`, so it teaches everyone to ignore it
- **Perceived:** nothing in-game today. The cost is that the next real YAML break ships silently.
- Evidence: `mods/ww3mod/maps/siberian-pass-ww3/map.yaml:11,13` — `MapSize: 97,67` with `Bounds: 0,0,97,67` (full-size bounds, no border).
- Confidence: **high** — verified the map file personally.
- Rough fix size: a decision plus either a one-line waiver or ~9 map-bounds edits.
- Note: compounding this, `HOTBOARD.md:9` declares the lint "usable as a merge gate again" after `4d3c8f90` took it 583→100. Both can be true — the count fell, the gate is still red. **No file in the repo asserts the current count**, and the per-map re-run still multiplies each error, so the count is not a signal. Diff the error LIST, never the number.

---
- **[SHOULD-FIX]** A unit ordered somewhere it cannot path to is effectively deleted from the game
- **Perceived:** the unit stops, never arrives, never goes idle, never complains, and is never picked up again by resupply, evac or bot tasking. It just stands there for the rest of the match.
- Evidence: `engine/OpenRA.Mods.Common/Traits/Mobile.cs:265` declares `public MoveResult MoveResult { get; set; }`. **Three readers, zero writers, engine-wide** (`MoveAdjacentTo.cs:107`, `MoveCooldownHelper.cs:69,76`). So `CompleteDestinationBlocked` and `CompleteDestinationReached` are both unreachable.
- Confidence: **high** on the mechanism (I grepped the whole engine and confirmed zero assignments); **medium** on how often it bites in practice.
- Rough fix size: multi-file — every `Move` exit path has to set the result.
- Note: two traits already carry `[Desc]` strings *documenting* this defect as a known limitation (`AutoFollowAlly.cs:45,160`, `AutoSeekSupplies.cs:88`). It is load-bearing under several other entries in this report, including the resupply wedges.

---
- **[SHOULD-FIX]** The Mi-28 has no anti-air weapon at all, while advertising one
- **Perceived:** Russia's 6000-credit attack helicopter is helpless against any aircraft. The USA's Apache is not. The unit description promises AA.
- Evidence: `mods/ww3mod/rules/ingame/aircraft-russia.yaml:340,350,395` reference an armament named `secondary-air`. **`Name: secondary-air` appears zero times in the entire mod** — I grepped for it. The Mi-28's only armaments are `primary` and `secondary`, neither valid against Air.
- Confidence: **high** — verified personally.
- Rough fix size: small, but it is a balance call, not a mechanical fix — define the armament or remove the references and correct the description.
- Note: **this is balance proposal `003-mi28-secondary-air.md`, which has been awaiting per-proposal sign-off since 2026-08-02 and was re-verified unapplied on 2026-08-11.** It is now filed in two places with no cross-reference.

---
- **[SHOULD-FIX]** Iskander/HIMARS designation damages essentially the whole infantry roster, including your own
- **Perceived:** painting a target quietly deals 50 damage to any infantry in the blast, friendly included.
- Evidence: `mods/ww3mod/rules/weapons/weapons-missiles.yaml:337-344` — the `Versus` tables zero `Brick`, an armor class that does not exist in this ruleset, and omit `Kevlar`, `Unarmored` and `Indestructable`, which do. An unlisted armor class takes **100%**, so an omission is the opposite of a zero.
- Confidence: **high**.
- Rough fix size: four YAML lines.

---
- **[SHOULD-FIX]** `humvee` declares `RenderSprites` twice, so no map can override anything on it
- **Perceived:** any map carrying a `Rules:` section that touches `humvee` fails to load — which presents as a hang, not an error.
- Evidence: `mods/ww3mod/rules/ingame/vehicles-america.yaml:28` and `:156`, both inside the `humvee:` actor (which spans lines 2-160). Verified personally.
- Confidence: **high**.
- Rough fix size: one line, plus a visual check that the surviving block is the intended one.
- Note: **latent today** — no shipped map overrides humvee. It fires the first time anyone tries.

---
- **[SHOULD-FIX]** A supply cache below 50 supply serves nobody and never despawns
- **Perceived:** a supply crate sits on the map permanently, giving no ammunition to anyone who walks to it.
- Evidence: `engine/OpenRA.Mods.Common/Traits/SupplyProvider.cs:38` (`RestockThreshold` default 50) gates serving at `:358`; `mods/ww3mod/rules/misc.yaml:427-437` sets no `RestockThreshold` on `SUPPLYCACHE`, and a cache has no restock trip. Contradicts `DOCS/reference/economy.md`.
- Confidence: **high**.
- Rough fix size: one YAML line.

---

### The rest of the STILL REAL list, grouped

**Gameplay / player-visible**

| Severity | Bug | Evidence |
|---|---|---|
| SHOULD-FIX | Apache 30mm is valid against structures (`ValidTargets: Ground` overrides the `^30mm` default) — *this is Phase B entry "Apache shouldn't shoot guns at structures", and the cause is one line* | `weapons-ballistics.yaml:669` vs `:546` |
| SHOULD-FIX | Units walk to a vanished target's last position instead of firing at it | `Activities/Attack.cs:170-180` |
| SHOULD-FIX | Anti-drone weapon deletes drones instead of jamming them (~2s kill at 25 shots/s vs 50 HP); the `dronedisable` effect is never seen | `weapons-other.yaml:683,695-703` |
| SHOULD-FIX | Infantry route along the shore instead of across bridges (`foot` Beach 80 / Shallow 30 vs Bridge 100) | `world.yaml:27-41` |
| SHOULD-FIX | DR operator renders the idle body over the prepare pose (base `WithInfantryBody` has no `RequiresCondition`) | `infantry.yaml:2367-2385` |
| POLISH | Helicopter husks do not sink on water | `husks/husks-aircraft.yaml:343-349` |
| POLISH | Drones vanish on death — no husk, no fall, no impact | `ingame/aircraft.yaml:363-397` |
| POLISH | An evacuating ground unit stays selectable and keeps blocking its origin cell while driving off | `RotateToEdge.cs:269-283` |
| POLISH | A transport already burning when loaded evacuates its passengers but rides down with its crew | `VehicleCrew.cs:173` |
| POLISH | Burning woodland silently seals ground to infantry (husks lose `Passable: tree`; six tree types occupy MORE cells as husks) | `decoration.yaml:12-14` vs `husks.yaml` |
| POLISH | Bail-dropped men block the exits `Cargo.Killed` needs; the rest of the squad is `Dispose()`d with no corpse and no kill credit | `Cargo.cs:982` |
| POLISH | `^ArtilleryRound` damage radii are smaller than its own inaccuracy — a shell aimed at infantry may routinely do nothing | `weapons-ballistics.yaml:806-822` |
| COSMETIC | Parked helicopters spin their rotors forever — **all six helis, not just the littlebird** (see §4) | `HeliEmergencyLanding.cs:405-409` |
| COSMETIC | Infantry walk animation never scales to terrain speed — legs cycle at full rate at 30% movement | `Render/WithInfantryBody.cs:188` |
| COSMETIC | The "primary building" tag has never rendered — five `pips` sequences point at a `pips.shp` the mod does not ship | `sequences-misc.yaml:204,208,214,217,244` |
| COSMETIC | 7 of 10 lobby map previews are stale (twin-rivers preview misses 23% of the map) | `map.png` vs `MapSize:` |
| COSMETIC | No menu fade — upstream `IngameMenuLogic` only knows `MenuPostProcessEffect`; the mod uses `MenuPaletteEffect` | `IngameMenuLogic.cs:187` |
| COSMETIC | `InaccuracyPerProjectile` is dead config on six weapons (`lastPosIsSet` is `readonly … = false`, never assigned) | `Bullet.cs:170,213` |
| COSMETIC | ~38 orphan server/lobby strings in `en.ftl` — the live keys carry a `notification-` prefix and live in the engine's `common.ftl` | `mods/ww3mod/languages/en.ftl:84-129` |

**Bot behaviour** (affects match quality; the player perceives it as a weak or strange opponent)

| Severity | Bug | Evidence |
|---|---|---|
| SHOULD-FIX | The bot never captures a Logistics Centre — the whole `CaptureSupplyDepots` tier sits below an early return WW3MOD always takes, and `logisticscenter` is absent from the POI list | `world.yaml:314-319`; `CaptureCoordinatorBotModule.cs:793`; inert `ai.yaml:144` |
| SHOULD-FIX | The capture ferry's drop site bypasses the danger standoff entirely, and now carries four units into it | `MountedTransportBotModule.cs:570` |
| SHOULD-FIX | A dry soldier's resupply errand can wedge him permanently (`moveQueued` latches and never clears) | `SeekSupplyProvider.cs:210-218` + `AutoSeekSupplies.cs:243-245` |
| SHOULD-FIX | `SeekSuppliesAndReturn` has the same never-ticks defect that was fixed in its sibling — the fix was never applied here | `SeekSuppliesAndReturn.cs` (no `ChildHasPriority`) |
| SHOULD-FIX | Ejected crew mid-evac get re-tasked by six recruiters, none of which check `IsEvacuating` | `PoiOffensiveBotModule.cs:2570` is the only guard |
| SHOULD-FIX | Healthy targets read as saturated because an idle unit re-marks its target ~every tick, so other units skip them | `AutoTarget.cs:1073-1091` |
| POLISH | Transport helicopters depart half-empty (the ground path was fixed, the heli path was not) | `HelicopterSquadBotModule.cs:1862-1864` |
| POLISH | Nothing ever *requests* a combat engineer — `e6` is procured only by an argmax that measurably never reaches it | `EngineerRouteOpenBotModule.cs:160` |
| POLISH | Bot map-players get no starting units (`SpawnStartingUnits` gates on `Playable`), so 19 tournament files' `StartingUnitsClass` is dead config | `SpawnStartingUnits.cs:69-71` |
| POLISH | Ferry escorts and walking escorts are recruited independently — double spend on one objective | `CaptureCoordinatorBotModule.cs:1678,1688` |
| POLISH | `AdaptiveProductionBotModule`'s counter-buy lane is gated on stale, never-decaying intel while its own fog-legal scanner sits unread behind the gate | `AdaptiveProductionBotModule.cs:242,267,271` |
| POLISH | `GoalGuardLedger.Release` is keyed on the ACTOR, not the objective — an ambient claim silently deletes a capture-escort one. **Blast radius has grown since filing: 8 release sites, was 5** | `PoiGoalGuard.cs:100` |
| POLISH | Three `ForwardStagingMath.StagingCell` callers still pass no passability predicate — AttackMoves issued to water/cliff | `PoiOffensiveBotModule.cs:2123,2321`; `CaptureCoordinatorBotModule.cs:1615` |
| POLISH | `GarrisonBotModule.baseCenter` is sampled once and frozen for the match | `GarrisonBotModule.cs:221,239` |
| POLISH | Five `AIHelicopterRoleInfo` fields are set in mod YAML and read by no C# | each identifier has exactly one occurrence (its own declaration) |
| POLISH | A dry unit can loop forever seeking an existing-but-unreachable rearm host (no path probe, no retry counter) | `PoiOffensiveBotModule.cs:2631`; `AutoSeekSupplies.cs:281` |

**Developer-facing** (does not ship, but slows every fix)

| Bug | Evidence |
|---|---|
| Restart drops out of any harness scenario instead of restarting it, and the run ends | `engine/OpenRA.Game/Game.cs:237-256` |
| Every demo is killed at exactly 300s by a watchdog waiting for a verdict demos never write; **and `set -e` makes the success mapping unreachable** | `tools/autotest/run-demo.sh:17,51,52` |
| The autotest single-instance lock covers `run-test.sh` only — the tournament scripts ignore it | `tools/autotest/run-test.sh:435` |
| Autotest triage reads the wrong `debug.log` — the run's log is `debug.log.1` whenever another instance holds the lock | `run-test.sh:339-352` |
| The engine still falls back to the menu forever when a test map's rules fail to load (the harness watchdog half was fixed; this half was not) | `run-test.sh:150` timeout only |
| `test-stance-optout` is a FALSE GREEN — it silences its own units with the very stance whose opt-out it claims to test | scenario lua + `StancePositioningFireStanceTest.cs:81` |
| `test-offense-ammo-guard` is RED and its predicate contradicts the evac disposition it runs with | scenario untouched since `5a02f341` |
| `wip-transport-delivers` can go GREEN on a one-cell carry (its "moved ≥ 10 cells" clause is satisfiable by the passenger WALKING) | `test-transport-delivers.lua:203,223` |

---

## 2. Entries that are FIXED but still ticked open

**49 entries.** The ones most worth striking, because they are still steering work:

| Entry | Fixed at | Verified, or only claimed? |
|---|---|---|
| Helicopter→helicopter missiles silently vanish | `Missile.cs:1171,1179-1214`; `weapons-missiles.yaml:238` | **VERIFIED** — all three claimed changes present; autotest scenario in tree |
| Units out of ammo reject attack orders | `bd7b6bb2`; `AttackBase.cs:492` | **VERIFIED** — `test-attackfollow-dry-breaks-off` observed RED→GREEN |
| No-ammo units reject attack-move | `bd7b6bb2`; `AttackMove.cs:109` | **VERIFIED** — two scenarios |
| AI garrisons defense buildings | `GarrisonBotModule.cs:262`; `09877fd5` | **VERIFIED** |
| AI uses attack-move for aircraft | `6d7b921f`; `ai.yaml:1851,1885` | **VERIFIED** — `test-heli-standoff` |
| AI conscripts don't abandon capture for squad orders | `CaptureCoordinatorBotModule.cs:735-748` | CLAIMED |
| AI rearms | `175a4784`, `484eb913`, `1bbfdb7c` | **VERIFIED** — three autotests + NUnit |
| Vehicle off-map evac flight | `9ab1b2e2`; `RotateToEdge.cs` | **VERIFIED** — `EvacDriveOffMathTest.cs` |
| Unit sell value at all ammo levels | `CustomSellValue.cs:36-46` | **VERIFIED** — generic over every `AmmoPool`, not TRUK-special |
| Iskander/HIMARS shockwave radius | `9578557c` | CLAIMED only — commit itself says "verify in playtest" |
| AI attack helicopters benched with no HPAD `[high]` | `fba34159`/`1d2544cf` | **VERIFIED** — `AirframeReadinessTest.cs` |
| Called-in helis loiter at the SR | `HelicopterSquadBotModule.cs:684-731` | CLAIMED |
| Mounted transports never dismount | `ai.yaml:1521,1567` | **Gate is now ON for BOTH profiles** — the "experimental-gated" caveat is stale |
| Attack-heli / ballistic-missile tilt, heli landing refinement | `0a099143`, `1d61d605`, `02006314` | CLAIMED — visual, unmeasured |
| Bot map-players have no economy | `779d0b62`/`2c274589` | **VERIFIED** — `@stable` moved knowingly |
| Bot cannot reclaim its cleared base | `ef9de559` | Code-verified, never watched in play |
| Supply trucks never procured | `1bbfdb7c` | **VERIFIED**, measured |
| Case-sensitivity family (`UnitBuilderBotModule`, `SquadManager`) | `fe70b6c1` | **VERIFIED** — `ActorNameCaseTest.cs` |
| `SupplyProvider.restocking` latched forever | `SupplyProvider.cs:169-179` | field removed; test present |
| Danger-field scale + `WeaponThroughput` | `DangerFieldLayer.cs:304,308,975-1021` | Abrams weight 2,950 → 128; overflow closed |
| 402-of-496 lint multiplication | `2fedd71b` | **VERIFIED** — also closes the `make.ps1 test` RED entry |
| Littlebird zero damage | `55459146` | **VERIFIED**, measured before/after |

**Pattern worth naming:** of the fixes that carry an autotest, several were never *run against a RED control*. The repo already banked this as a rule in `AUTOTEST.md` after `f910ac7d` ("a green run is not evidence unless something could have made it RED"), and `68b627ce`'s preemption is the standing example — shipped on zero behavioural evidence. Treat "VERIFIED" in the table above as "a test exists and passed", not always as "the test could have failed".

---

## 3. STALE PREMISE and NOT-A-BUG

**STALE PREMISE (9)** — the bug as written cannot exist:

- **Heavy artillery deliberately ignores infantry.** `^AutoTargetArtillery` **does** list Infantry at Priority 2 and has since 2023 (`defaults.yaml:418-420`), predating the note. The likelier real cause of the observation is artillery `MinRange: 10c0-12c0`, honoured at `AutoTarget.cs:1348` — close infantry is genuinely unengageable. **The entry asks a design question that was already answered; the actual mechanism was never investigated.**
- **Eight locomotors declare `Crushes: fence` without `Passes:`, so 384 fence actors are solid walls.** **This entry is FALSE at the root.** `Locomotor.cs:110` is `PassableClasses = Passes.Union(Crushes)` — I verified this personally. Crushes IS unioned in; the fences are passable. **`tools/nav-guard/README.md:91` repeats the same false claim**, which puts nav-guard's vehicle-locomotor connectivity numbers, and its "diagonal-squeeze is connectivity-neutral" conclusion, in doubt.
- **`@experimental` bot runs at cash=0 all match.** Refuted by a later entry *in the same file* (L1101 vs L231) after `779d0b62`.
- **LC dock-and-rearm can never complete (`closeEnough = WDist.Zero`).** Premise refuted by the subcell entry's own measurement — the LC centre cell *is* stand-on-able.
- **River Zeta neutral SAM.** No `SAM`/`HSAM` is placed on **any** of the 10 maps and both are `Prerequisites: ~disabled`. The cloak-always defect is real but unreachable.
- **Edge spawn/leave for planes.** `ProductionFromMapEdge` already produces Aircraft; all four fixed-wing planes are `~disabled`.
- **AI builds Logistics Centers.** `LOGISTICSCENTER` is `Prerequisites: ~disabled` — *nobody* can build one. The intended route is capture. (But see §1: the bot cannot capture one either — that half is STILL REAL.)
- **Captured SR handling.** `SUPPLYROUTE` has no `Capturable` and no `CaptureManager` — CLAUDE.md's warning holds. This is a feature request, not a bug.
- **Helicopter force-land tuning.** `CanForceLand` defaults true, mechanism intact, no defect found.

**NOT A BUG — unbuilt feature filed as a defect (9):** supply-truck→building transfer; cache death explosion scaling; truck→cache replenish; flametrooper vs unarmored (`^E4` is `~disabled`, and `Flamespray` has **no `Versus:` table at all**); ballistics hit-chance deprioritisation (**no hit-chance term exists anywhere in targeting — zero scaffolding**); primary-SR selection UI (`^PrimaryBuilding` exists but SUPPLYROUTE does not inherit it); suppression tuning (machinery complete, numbers only); the fog/visibility design questions; heli force-land tuning.

---

## 4. Latent — real defects that cannot currently fire

Per the repo's own recorded trap, **a bug that cannot fire looks exactly like a bug that is not there.** These are NOT fixed:

- **`CreatesShroud` → `NotImplementedException`.** `AffectsMapLayer.Type` is `virtual … => throw new NotImplementedException()` (`AffectsMapLayer.cs:201`). Three of four subclasses override it; **`CreatesShroud` does not** — I verified there is no `Type` member in `CreatesShroud.cs`. `MapLayers.AddSource` reads it on the first `AddCellsToPlayerMapLayer` call. Any actor with `CreatesShroud` hard-crashes on entering the world. The trait appears in **no** mod YAML today, so it fires the instant anyone adds a jammer, smoke unit or stealth structure — or a map author adds it via map rules. **One-line fix, worth taking now.**
- **`humvee` duplicate `RenderSprites`** — latent until any map overrides it (listed in §1 because the trigger is cheap and likely).
- **`rotor-stopped` — the grantor exists but is unreachable.** `HeliEmergencyLanding.cs:405-409` is the only grantor and its only caller is `HeliAutorotate.cs:65`, i.e. emergency autorotation. Ordinary `Land` has no equivalent, so `!airborne && !rotor-stopped` stays true forever across **all six helicopters** (14 consumer sites). **This refutes `bugs/discovered.md:381`**, which claims `rotor-stopped` "genuinely has no grantor anywhere in the mod" — a YAML-only sweep could not see the C# grantor. It also means the tracker's "Littlebird rotor still spins" is not a littlebird bug.
- **`RendezvousMath.AnchorAcceptable` has no lower bound** — real backwards bias, but `RendezvousWithOffensiveStaging: false` on both twins (`ai.yaml:1611`) *and* its only caller sits on the pre-contact branch.
- **`AIHelicopterRole.HitAndRunCooldown` counts squad updates, not ticks** — and its consuming state sits under `if (!standoff)` while both profiles set `StandoffEngagement: true`.
- **`UnitDefaultsManager` writes per-machine state into synced sim fields** (`UnitDefaultsManager.cs:40` → `AutoTarget.cs:492-497`). Latent for single-player; a multiplayer/replay divergence source. **Two more `[Sync]` condition tokens of the same class remain at `Detectable.cs:165,196`** — the first leak of exactly this shape was the confirmed cause of the bot desync fixed at `91056894`.
- **`CheckOwnershipAfterExit` has no "was this originally neutral" guard** — correct today only because all 10 map-placed garrisonable buildings happen to be Neutral.
- **An unsatisfiable `Resupply` arrival test never times out** — it spins on no-op approaches forever; only an even-footprint rearm host reaches it, and none exists.
- **`AirstrikePower.cs:104`** still passes `info.UnitType` un-lowercased into `CreateActor` — the 2026-03-24 "FIXED" was incomplete; all airstrike powers are commented out in `player.yaml`, which is the only reason it is quiet.

---

## 5. CANNOT DETERMINE STATICALLY — repro instructions

Each of these is a valid answer, and each is one playtest or one scenario away from becoming a fact:

| Entry | Repro |
|---|---|
| Some enemy soldiers untargetable (mutual) | Needs the original conditions: unit type, stance, whether near a garrison port. No candidate defect found in the targeting/stance code. |
| Allied shared vision blinks ~3-4 Hz for ~2s | Wait for recurrence; note attacker, healer presence, HP%, motion. Static analysis already ruled out condition-gated Vision, `VisionModifiers`, `EjectOnHusk` and owner flicker. |
| ATGM units can't unload while shooting | Order an ATGM squad to unload from a transport while they have a live target. (`LockAimPerBurst` ruled out — only artillery/rockets use it.) |
| Mobile sensor (CounterBatteryRadar) doesn't work | Deploy an MSAR, put an enemy Paladin inside 42c0, have it fire. Wiring chain is fully intact at HEAD — the likely answer is that the 4-second reveal has no audio or icon cue, so the player never notices it worked. |
| Crew-ejected vehicle crippled after repair | Damage a Bradley past `EjectionDamageState`, repair it to full, order it to move. Inferred from code, never tested. |
| Truck mode selector fires DANGEROUS-front doctrine on a quiet front | `test-supply-safe-front-keeps-cargo`, seed 5002, tracing **both** `DangerFieldLayer` and `ThreatMapManager` — the two disagree and only one is config-gated. |
| Crew bloat | Run an `@experimental` USA bot 2000+ ticks and count live `crew.*` actors. Note the sweep path is itself a recorded desync site. |
| Saved-game restore desync | `run-test.sh --speed 8 --timeout 420 test-savegame-resume-riverzeta`. Partly fixed (`91056894`); a second independent leak at net frame 711 is located but not fixed. |
| 6-player skirmish slow on MacBook | Profile. Read the prior perf work (shadow-cache freeze, density layer, AI tick budgets) first. |

---

## 6. Unfinished work on NO list

**How the line was drawn.** Grepped `engine/` + `mods/ww3mod/` for `TODO|HACK|FIXME|XXX|NotImplementedException|for now|temporar|workaround` → **504 marker lines in 285 files**. Then blamed **every one of those lines individually** and set-subtracted the introducing SHA against `git rev-list 7362fbc6` (the engine vendoring commit, "Starting point (#2)", 2023-03-20, and its 187 ancestors). This blames the *marker line*, not the file — necessary, because WW3MOD has touched ~1,840 engine `.cs` files, so file-level filtering is useless here.

- 339 excluded as **vanilla upstream**.
- 165 survived; **109 were false survivors** — either re-touched by the upstream re-merge `c5bb5ece` ("apply release-20250330") without changing the text, or in content WW3MOD does not ship (`engine/mods/{ra,cnc,d2k,ts,common}`).
- **56 genuinely WW3MOD-authored markers**; ~42 triaged away as not player-reachable (utility-command boilerplate, commented-out dead code, doc-string TODOs, notes recording a *settled* decision).
- **12 substantive.**

**Caveat on the method:** the `FrozenUnderFog` short-circuit — the single most important finding in this whole audit — **was missed by that regex** and only found by a supplementary grep for `QUICK ?FIX|short-circuit|stubbed|placeholder|Unimplemented`. Any future sweep must include those terms. Conversely, **the Supply Route, garrison, vehicle-crew, suppression and stance code are marker-clean**, and every missile/projectile marker is vanilla.

Beyond `FrozenUnderFog` and `CreatesShroud` (both above), the substantive ones:

| Title | file:line | Reachable? | Severity |
|---|---|---|---|
| Bot has **no way to create a Logistics Centre** — `ai.yaml:845` `# TODO: LCCV deployment needs custom strategic logic`, while `ai.yaml:145` builds the whole logistics layer around `SupplyDepotActorTypes: logisticscenter` | `mods/ww3mod/rules/ai/ai.yaml:845` | Reachable | SHOULD-FIX |
| Resupply command-bar icon reuses the Deploy icon rect — two adjacent buttons with the same glyph | `mods/ww3mod/chrome.yaml:259-260` | Reachable | SHOULD-FIX |
| `mod.yaml` still ships the OpenRA homepage URL and the stock Red Alert icon (`TODO(release)`) | `mods/ww3mod/mod.yaml:4,6` | Reachable | SHOULD-FIX |
| Force-move tooltip advertises "Chrono Tanks will teleport" | `chrome/ingame-player.yaml:118` | Reachable | POLISH |
| `Armament.AllowIndirectFire` gates fire and is documented, but `IndirectFire` semantics are unimplemented and no YAML sets it | `Armament.cs:79`; `AttackBase.cs:412` | Always-true | POLISH |
| `HitShape: # TODO` on MISS and two civilian buildings | `structures-neutral.yaml:104`; `civilian.yaml:592,617` | Reachable | POLISH |
| `Map.cs` silently exempts `ShadowLayer` from the required-field check | `engine/OpenRA.Game/Map/Map.cs:103` | Latent | POLISH |
| `MapLayers.Normalize` "TODO add not explored and all visible"; empty `/* Tick function. TODO */` | `MapLayers.cs:591,253` | Reachable | POLISH |
| LayeredDefence skips out-of-ammo units rather than routing them to supply | `LayeredDefenceBotModule.cs:101,404` | Reachable | POLISH |
| `DroneTargeter` ValidTargets unresolved for AutoTarget | `weapons-other.yaml:673` | Reachable | POLISH |

The LCCV one deserves emphasis: the bot's entire forward-resupply doctrine is built around an actor it has **no route to except capturing a pre-placed neutral** — and per §1 it cannot capture one either. On any map without a neutral Logistics Centre, the AI has no forward resupply node at all, so its units run dry at the front and the ammo economy, which is the core of the game model, plays one-sided against a human. Also `MustBeDestroyed: RequiredForShortGame: true` on an undeployed LCCV means a stray one can prolong a short game.

**Dead-config sweep** was attempted and found nothing beyond the two already-known instances (`SafeFollowDistance`, five `AIHelicopterRoleInfo` fields). The heuristic is weak — treat as "not investigated", not "clean".

---

## 7. How far the existing lists can be trusted

**Verdict: trust the diagnoses, do not trust the statuses.**

Where an entry names a concrete mechanism and a `file:line`, it is usually **right about the mechanism** even when the line number has moved. That is the backlog's real value and it is considerable — these are careful, specific, falsifiable notes, and several correctly flag their own uncertainty ("Inferred from code, NOT tested", "UNVERIFIED", "CANDIDATE"). That discipline is why this reconciliation was possible at all.

What cannot be trusted is any claim about **state**:

1. **~1 in 3 open entries is not open.** 49 fixed, 9 stale, 9 not-a-bug, 1 duplicate.
2. **`discovered.md` has no merge-back pass.** At least five entries say "not fixed" or "addressed on unmerged branch" for branches that have since merged (`wt/bot-reclaim` `ef9de559`, `wt/transport-loading` `f78fb365`, `wt/truck-precedence` `1bbfdb7c`, `wt/econ-gate` `779d0b62`, `wt/heli-gun` `55459146`). A worktree branch merging does not write back to the entry that recorded the bug.
3. **The file contradicts itself.** The 08-14 cash=0 entry (L231) is refuted by an 08-15 entry (L1101) in the same file. Both are present, neither is struck.
4. **At least one filed bug is affirmatively FALSE** (`Crushes`/`Passes`), and the false claim has propagated into `tools/nav-guard/README.md:91`, where it taints a tool's conclusions.
5. **Line-number drift is near-universal** — roughly 30 instances across the six verification passes. Every citation should be treated as a hint, not an address.
6. **Cross-document duplication with no cross-reference:** the Mi-28 AA gap is both a `discovered.md` bug and balance proposal 003 in `AWAITING-USER.md`. The `@stable` benchmark re-baseline is raised by four separate close-outs whose counts disagree.
7. **`AWAITING-USER.md` is the most reliable of the three** — its 2026-08-11 reconciliation explicitly refused to move anything to RESOLVED without proof, and the two items I spot-checked (balance proposals unapplied, item-24 gates still ON) were correctly stated. Its discipline is the model the other two should follow.
8. **The severity labels are not calibrated against each other.** A tracker `!` URGENT (dropped supply cache) turned out to be **stale on its stated premise** — the cache has had `SupplyProvider`, an HP pool and a health bar for some time — while the two genuine BLOCKERs found here (the veteran crash, the fog short-circuit) sit as ordinary unmarked lines.

**Recommended process change, cheapest first:** (a) strike the 68 non-actionable entries — that alone makes the list readable; (b) add a merge-back step so closing a worktree branch updates the entry that recorded the bug; (c) stop citing line numbers, cite symbols; (d) re-audit `tools/nav-guard/README.md` given finding 4.

---

*Read-only audit. Nothing was fixed, edited or committed. `main` @ `55459146`.*
