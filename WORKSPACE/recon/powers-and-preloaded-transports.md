# Feasibility recon — a Powers menu, and transports bought pre-loaded

**Researched against `main @ d421e4ca`** in worktree `wt/powers-recon`. `git rev-list --count HEAD..@{u}` could not run (the branch has no upstream); the worktree was cut from `main @ d421e4ca`, which is `origin/main` at the time of writing. **Static analysis only — no game runs, no autotests, no YAML lint.** Every claim below carries a `file:line` or a commit SHA read at that SHA.

**Timestep:** 60 ms ⇒ **16.667 ticks/s** (`mods/ww3mod/mod.yaml:369-372`, restated at `recon/260808-transport-census.md:11`). `seconds = ticks × 0.06`. This matters twice below; the 25 tps assumption CLAUDE.md warns about is live in this feature.

**What this document is.** Two cost estimates so the user can decide what enters a locked v1. It advocates for nothing. Where a claim in the brief did not survive checking, the section says so first.

---

## 0. Headline findings

1. **The brief's premise "most of the Powers work is already written" is half true, and the false half is the expensive one.** The two v1-disabled airstrike blocks are genuinely complete and every reference resolves — **but the A-10 airstrike cannot fire a shot**, for a reason established on `main` two days ago and deliberately left unfixed (`38564a78`, 2026-09-02). Uncommenting the blocks today ships a NATO support power that flies over the target and does nothing. §1.3.
2. **The other thirteen commented power blocks are Red Alert corpses, not WW3MOD content.** They reference **six actors that do not exist anywhere in `mods/ww3mod/`** (`b52`, `u2`, `smig`, `badr.bomber`, `badr.cruiser`, `spmst.summon`) and nine of them consume a `disabled` condition nothing grants — which `CheckConditions.cs:75` emits as a **hard lint error**, not a warning. They are not a re-enable, they are a rewrite. §1.2.
3. **No paid-support-power idiom exists in this engine.** `grep -rn "PlayerResources\|TakeCash\|Cash"` across `engine/OpenRA.Mods.Common/Traits/SupportPowers/` **and** `engine/OpenRA.Mods.Cnc/Traits/SupportPowers/` returns **zero hits**. `SupportPowerInstance` holds a single scalar countdown (`SupportPowerManager.cs:154-161`) and has no concept of a stock of charges. That is the crux of Feature A. §1.4.
4. **But the buy-and-stock loop can be built almost entirely out of shipped code, and I did not expect that.** `Production.Produce` explicitly succeeds for a producee with no `IOccupySpace` (`Production.cs:123-127`), `AllowMultiple` keys each instance by ActorID (`SupportPowerManager.cs:48-51`) so N purchases become N separate top-left icons, and `OneShot` makes a spent icon disappear on its own (`RefreshIcons` filters `!p.Disabled`, `SupportPowersWidget.cs:135`). The "buy → stock → spend" model the user described is **one YAML queue plus a handful of proxy actor defs**, not new engine code. §1.5.
5. **The top-left home already exists and is already wired.** `Container@SUPPORT_POWERS` at X:10 Y:10 with `SupportPowerBinLogic` (`mods/ww3mod/chrome/ingame-player.yaml:16-38`). It is empty today only because no power is defined. It also already carries the hook for showing "×3" instead of a clock: `IconOverlayTextOverride()` (`SupportPowersWidget.cs:229`). §1.6.
6. **Nuclear strikes are far more built than the brief assumes.** `MSLO` ships live and uncommented with a complete `NukePower` (`structures-defenses.yaml:1107-1172`), a 359-line hand-authored `Atomic` weapon (`weapons-superweapons.yaml:28-386`), icon, beacon, explosion sequence and three notification lines. One is placed on a shipped map. **What is missing is not art or code — it is the design decision the user already wrote a doc about and did not resolve** (`archive/plans/260324-nukes.md`). §1.7.
7. **The lobby cooldown dropdown is dead config, and an upstream merge killed it.** `SupportPowerInfo.LobbyChargeIntervalId` (`SupportPower.cs:25`) has **zero readers repo-wide**; `PowersLobbyOptions.AirstrikeCooldown` (`:99`) is assigned and never read. `git log -S` names the culprit: `71687440 "Upstream merge: fix OpenRA.Game compilation + resolve duplicate types"` removed the parsing that `6f2191be` had added. §1.8.
8. **Feature B's prior discussion exists, and its "skip" reason is narrower than it looks.** `RELEASE_V1.md:138` carries it as an **open v1 item** ("Cargo Phase 3 — template sidebar for pre-loaded transport purchasing"), and PIPELINE item **R16** rules it *"too vague to dispatch … it needs a design pass to become an item, not a worker"* (`PIPELINE.md:226-230`). **It was never skipped on mechanism grounds — nobody ever established it was hard.** §2.1.
9. **The engine already spawns produced actors with their cargo loaded, and it survives the Supply Route call-in path intact.** `Cargo.InitialUnits` (`Cargo.cs:54`, consumed `:326-333`) and `CargoInit` (`:1290`) both exist; `ProductionFromMapEdge` ends in a plain `CreateActor` (`:181`) and only ever inspects the *transport's* `MobileInfo`/`AircraftInfo`. A pre-loaded Humvee spawns at the map edge and drives to the rally point with its squad aboard. Fixed presets are close to free. §2.2.
10. **…except that pricing one is a money pump, and that is the real cost of Feature B tier (a).** `GetSellValue` (`CustomSellValue.cs:28-54`) has **no passenger term**, and the evacuation refund reads it (`RotateToEdge.cs:457`). Price a preset at transport+infantry and the player unloads the squad, evacuates the empty hull, and banks the infantry's cost twice. Price it at the transport alone and the infantry are free. **Neither pricing is safe without a code change.** §2.3.

---

# Feature A — a Powers menu

## 1.1 What is live today (uncommented, reachable, shipping)

| Thing | Where | State |
|---|---|---|
| `SupportPowerManager` on the player actor | `mods/ww3mod/rules/player.yaml:110` | **Live.** Not commented. |
| Top-left support power bin | `mods/ww3mod/chrome/ingame-player.yaml:16-38` | **Live.** `Container@SUPPORT_POWERS`, X:10 Y:10, `Logic: SupportPowerBinLogic`, child `SupportPowers@SUPPORT_PALETTE` (IconSize 62×46, `ReadyText: READY`, `HotkeyPrefix: SupportPower`, `HotkeyCount: 6`) plus a `PALETTE_FOREGROUND` overlay frame. |
| Countdown readout beside it | `ingame-player.yaml:39-42` | **Live.** `SupportPowerTimer@SUPPORT_POWER_TIMER`, X:80 Y:10, `Order: Descending`. |
| `MSLO` nuclear silo + `NukePower` | `mods/ww3mod/rules/ingame/structures-defenses.yaml:1107-1172` | **Live and uncommented.** Gated out of the sidebar only by `Buildable.Prerequisites: ~disabled` (`:1119`). |
| `Atomic` weapon | `mods/ww3mod/rules/weapons/weapons-superweapons.yaml:28-386` | **Live.** Hand-authored multi-phase warhead stack. |
| Beacon art (`arrow` / `clock` / `circles` / `atomicon` / `pinficon` …) | `mods/ww3mod/sequences/sequences-misc.yaml:50-110` | **Live.** All present. |
| `ability` cursor, `nuke` cursor | `mods/ww3mod/cursors.yaml:140`, `:179` | **Live.** |

**The top-left surface is not work.** It renders today; it is empty because `SupportPowerManager.Powers` is empty.

## 1.2 The sixteen commented blocks are two different things

`player.yaml` is 610 lines and carries **16** commented power blocks. They split cleanly.

### Group 1 — the real, deliberately-deferred WW3MOD content (3 blocks, `player.yaml:115-160`)

`GrantConditionOnLobbyOption@airstrikes` (`:115`), `AirstrikePower@America` (`:119`), `AirstrikePower@Russia` (`:140`). Written by `6f2191be` (2026-03-24), disabled by `c2ef5d1f` (2026-05-08).

**Every reference resolves.** Checked individually:

| Reference | Verdict |
|---|---|
| `UnitType: a10.airstrike` | `A10.Airstrike:` exists — `aircraft-america.yaml:687-712`, `Inherits: A10`, `AttackType: Strafe`, `AmmoPool@1 Ammo: 40` |
| `UnitType: frog.airstrike` | `FROG.Airstrike:` exists — `aircraft-russia.yaml:706-730`, `Inherits: FROG`, `AttackType: Strafe` |
| `CameraActor: camera.paradrop` | exists — `misc.yaml:166` |
| `IconImage: a10` / `Icon: icon` | `a10:` sequence has `icon: a10icon` (`sequences-aircraft.yaml:104,112`); sprite present at `mods/ww3mod/bits/misc/icons/a10icon.shp` |
| `IconImage: frog` / `Icon: icon` | `frog:` sequence has `icon: frogicon` (`sequences-aircraft.yaml:84,91`); `frogicon.shp` present |
| `ArrowSequence` / `ClockSequence` / `CircleSequence` | all under `beacon:` — `sequences-misc.yaml:50,53,56,107` |
| `Cursor: ability` | `cursors.yaml:140` |
| `PauseOnCondition: airstrikes-disabled` | granted by the `GrantConditionOnLobbyOption@airstrikes` block in the same comment (`:115-118`) — **valid only if both are uncommented together**, which is exactly what the in-file note says |

**Verified: these three blocks are re-enablable as written.** The brief's lead holds for them.

### Group 2 — Red Alert legacy, never adapted (13 blocks, `player.yaml:296-610`)

`ParatroopersPower@RUSSIAParatroopers` (`:296`), `@AmericaParatroopers` (`:319`), `@RussiaFastParatroopers` (`:343`), `@GrenParadrop` (`:365`), `@ParaMines` (`:388`), `@ParaJeeps` (`:411`), `@AmericaParaScouts` (`:435`); `AirstrikePower@Spyplane` (`:459`), `@SpyplaneExtra` (`:479`); `ParatroopersPower@ListeningPost` (`:500`); `AirstrikePower@PrecisionStrike` (`:521`), `@Parabombs` (`:543`), `@EMPBomb` (`:567`), `@V2Strike` (`:589`).

**Six referenced actors do not exist.** Checked with `grep -rniE "^<name>:" mods/ww3mod/` over the whole mod tree, not just `rules/`:

| Actor | Referenced by | Status |
|---|---|---|
| `b52` | `@AmericaParatroopers:340`, `@ParaJeeps:424`, `@AmericaParaScouts:448` | **MISSING** — no definition anywhere in `mods/ww3mod/` |
| `u2` | `@Spyplane:471`, `@SpyplaneExtra:491`, `@PrecisionStrike:534` | **MISSING** — a `u2:` *sequence* exists (`sequences-aircraft.yaml:230`) but no actor |
| `smig` | `@EMPBomb:580` | **MISSING** — sequence only (`sequences-aircraft.yaml:99,241`) |
| `badr.bomber` | `@Parabombs:555` | **MISSING** — commented out at `aircraft.yaml:497` |
| `badr.cruiser` | `@V2Strike:601` | **MISSING** |
| `spmst.summon` | `@ListeningPost:509` | **MISSING** |
| `1TNK.R1`, `2TNK.R1` | `@AmericaParaScouts:441` | **MISSING** |

**And nine of them consume a condition nothing grants.** `PauseOnCondition: disabled` appears at `:366, :389, :412, :436, :501, :522, :544, :568, :590`. `disabled` is not granted on the Player actor — `player.yaml` contains no `GrantCondition…: Condition: disabled`. The engine treats this as an **error, not a warning**: `CheckConditions.cs:75` emits *"Actor type `X` consumes conditions that are not granted"*. So uncommenting Group 2 does not produce nine silently-inert powers — it produces a lint failure, and if forced past it, nine powers that are **active** rather than paused (a `PauseOnCondition` naming an ungranted condition never evaluates true).

Also on the list, in the other direction: `DropItems: E1R1, E2R1, E3R1` do exist (`infantry.yaml:1213, 1502, 1342`), `MNLY` exists (`vehicles.yaml:504`) and `humvee` exists — so the *paratrooper* blocks are closer to salvageable than the airstrike ones, but each still needs a delivery airframe that does not.

> **Group 2 is not "commented-out work". It is a graveyard.** Any estimate that counts sixteen blocks as re-enablable content is wrong by thirteen.

## 1.3 ⚠️ The A-10 airstrike cannot fire — this is the finding that changes the tier-(a) estimate

`38564a78` (2026-09-02, on `main`) fixed the universal strafe blocker. Its own commit message and the write-up at `DISCOVERIES.md:2449-2487` state the residue plainly:

- **Gate 1 — the `AimingDelay` reset.** Universal; blocked every strafe airframe regardless of weapon. **Fixed** by `38564a78`.
- **Gate 2 — the weapon cannot hit terrain.** The strafe aim point is a `TargetType.Terrain` target; `WeaponInfo.IsValidAgainst` resolves it to the *cell's* `TargetTypes` (`Ground` on every WW3MOD tileset, `WeaponInfo.cs:241-247`); `Armament.CanFire` refuses on `!IsValidAgainst` (`Armament.cs:402`). **Not fixed.**

**Re-verified at `d421e4ca` by reading the weapons themselves, not the write-up:**

- `30mm.A10` (`weapons-ballistics.yaml:719-727`) inherits `^30mm` and does **not** override `ValidTargets`. `^30mm: ValidTargets: Infantry, Vehicle, Defense` (`:579`). **No `Ground`.**
- `Hellfire` (`weapons-missiles.yaml:243`): `ValidTargets: Vehicle, Air, Defense`. **No `Ground`.**
- `RocketPods` (`weapons-ballistics.yaml:912`): `ValidTargets: Ground`. **Has it.**

So **`FROG.Airstrike` (Su-25, Russia) fires; `A10.Airstrike` (A-10, America) fires nothing, ever.** The A-10 is the NATO power — the one a player is most likely to see first.

**Filed and known:** `WORKSPACE/bugs/discovered.md:127` — *"`A10.Airstrike` weapons not valid against `Ground`."*

**The remedies are a design choice, and none is measured** (`DISCOVERIES.md:2481-2487`):

1. Add `Ground` to `30mm.A10` / `Hellfire` — but `^30mm` is widely inherited, so this also lets every other user of those weapons shoot at dirt.
2. Give `A10.Airstrike` its own ground-capable armament (narrowest blast radius; probably the right one).
3. Make `StrafeAttackRun` aim at the actor rather than the ground when the armament cannot hit terrain (engine change, widest blast radius).

**And there is no scenario that can host the test.** `test-strafe-engage` contains no A-10 lane, and its map's 40-cell lane pitch leaves no room for a fourth (`DISCOVERIES.md:2485-2487`). Verifying an A-10 fix needs a **new autotest scenario**, not a lane bolted onto an existing one.

## 1.4 The charge model, and what "bought for money" actually differs by

**Stock model, read end to end in `SupportPowerManager.cs`:**

- `SupportPowerInstance` holds one scalar, `remainingSubTicks` (`:154`), initialised to `TotalTicks * 100` (`:171`) where `TotalTicks = info.ChargeInterval` (`:170`).
- `Tick()` decrements it by 100 per tick (`:200`).
- `Ready => Active && RemainingTicks == 0` (`:161`).
- `Activate()` resets `remainingSubTicks = TotalTicks * 100` (`:250`).

**There is no count.** Readiness is a boolean derived from a countdown. You cannot own two airstrikes; you can only be ready or not.

**And there is no cost.** `SupportPowerInfo` (`SupportPower.cs:18-165`) has forty-odd fields and none of them is a price.

**Verified negative — no paid-power idiom exists in this engine:**

```
grep -rn "PlayerResources\|TakeCash\|Cash" \
  engine/OpenRA.Mods.Common/Traits/SupportPowers/ \
  engine/OpenRA.Mods.Cnc/Traits/SupportPowers/
→ (no output)
```

Every shipped power charges on wall-clock time. `GrantPrerequisiteChargeDrainPower` (`Cnc/…/GrantPrerequisiteChargeDrainPower.cs:70,102`) is the one power that subclasses `SupportPowerInstance` — it is a *drain*, not a purchase, but it proves the subclassing extension point works and is used.

## 1.5 The route that is cheaper than it looks: proxy + `OneShot` + `AllowMultiple`

This is the part I did not expect to find, and it is the single biggest lever on the Feature A estimate.

**Four shipped mechanisms compose into exactly the model the user described:**

1. **A support power can live on an actor with no body and no map position.** `Production.Produce` (`Production.cs:120-131`):

   ```
   var exit = SelectExit(self, producee, productionType);
   if (exit != null || self.OccupiesSpace == null || !producee.HasTraitInfo<IOccupySpaceInfo>())
   {
       DoProduction(self, producee, exit?.Info, productionType, inits);
       return true;
   }
   ```

   A producee with **no `IOccupySpace`** produces successfully with `exitinfo == null`, and `DoProduction` then skips every location init and calls `CreateActor(producee.Name, td)` (`:96`). `td` always carries `OwnerInit(self.Owner)` (`ProductionQueue.cs:728-732`).

2. **`SupportPowerManager` picks it up automatically.** `ActorAdded` (`:52-73`) registers every `SupportPower` on any actor whose owner matches.

3. **`AllowMultiple` turns purchases into a stock.** `MakeKey` (`:48-51`) returns `OrderName + "_" + ActorID` when `AllowMultiple` is set — so **each proxy gets its own dictionary entry and its own top-left icon.** Buy three, see three.

4. **`OneShot` makes a spent charge vanish.** `Activate` sets `oneShotFired = true` (`:255`), which makes `Disabled` true (`:159`), and `RefreshIcons` filters on `!p.Disabled` (`SupportPowersWidget.cs:135`).

**The proxy actor shape already exists in this mod, commented, as RA left it** — `powerproxy.parabombs` / `.sonarpulse` / `.paratroopers` at `misc.yaml:318-367`, and the first of them already carries `OneShot: true` + `AllowMultiple: true` (`:325-326`). The runtime-grant precedent is two lines: `w.CreateActor(info.Proxy, new TypeDictionary { new OwnerInit(collector.Owner) })` (`SupportPowerCrateAction.cs:40-43`; the same shape at `InfiltrateForSupportPower.cs:74`).

**One trap, and it will bite whoever builds this.** The new produce type must go on **`Production@Local`** (`structures.yaml:361-363`), **not** `ProductionFromMapEdge` (`:364-366`). `ProductionFromMapEdge.Produce` overrides the base and resolves a spawn cell only for a producee with `MobileInfo` or `AircraftInfo` (`ProductionFromMapEdge.cs:85-86, 96-155`); a bodiless proxy has neither, so `location` stays null and the method returns `false` at `:158-159` — the item would sit in the queue at 100% forever, with no error anywhere.

**Second trap:** `RankAccumulation` (`player.yaml:22`, `Types: Infantry, Vehicle, Plane, Ship`) hands out a `VeterancyLevelInit` built from `unit.TraitInfo<GainsExperienceInfo>()` **unguarded** when rank > 0 (`ProductionQueue.cs:734-736`). A power proxy has no `GainsExperience`, so putting power items on an *existing* queue type would throw. A new queue type keeps them out of `Types` and this stays safe.

**Residue, honestly:** the spent proxy actor is never removed from the world and its `Powers` entry is never removed from the dictionary (`ActorRemoved` at `:75-90` only fires if the actor is removed). Buying and spending twenty charges over a match leaves twenty dead actors and twenty dead dictionary entries. Neither is visible or costly, but it is untidy; a ~10-line "dispose self on activate" closes it.

## 1.6 Where bought powers live before use — already solved

The top-left bin (§1.1) renders any registered power. Two specifics for the "owned charges, not a recharging timer" question:

- **The overlay text hook exists.** `SupportPowersWidget.Draw` calls `p.Power.IconOverlayTextOverride()` and, when non-null, draws that string centred over the icon **instead of** READY / ON HOLD / the countdown (`:229-247`). `SupportPowerInstance.IconOverlayTextOverride()` is `virtual` and returns null (`SupportPowerManager.cs:291-294`). Showing `×3` is an override, not a widget rewrite.
- **With the proxy model you may not even need it.** N charges = N icons; the count is the icon count.
- **The clock ring draws unconditionally** (`:216-221`), but with `ChargeInterval: 0` → `TotalTicks == 0` → the fetch index short-circuits to `clock.CurrentSequence.Length - 1`, i.e. the full-charge frame. A bought power reads as permanently ready, which is correct.
- **Hotkeys cap at six** (`ingame-player.yaml:29`, `HotkeyCount: 6`). More than six simultaneous power icons renders fine; the seventh onward just has no hotkey.

## 1.7 Nuclear strikes — much further along than the brief assumes

**Present and live, uncommented:**

- `MSLO` (`structures-defenses.yaml:1107-1172`): `Valued: Cost: 50000`, `Health.HP: 135000`, `Armor: Concrete / Thickness 2000`, `Building 2×1`, `SpawnActorOnDeath: mslo.husk` (the husk exists — `husks-defenses.yaml:78`), `WithSupportPowerActivationAnimation`, `SupportPowerChargeBar`, `GpsDot: Nuke`.
- Its `NukePower` (`:1134-1167`): `MissileWeapon: atomic`, `MissileImage: atomic`, `DetonationAltitude: 6c256`, `FlightDelay: 70`, `FlightVelocity: 1024`, `CameraRange: 20c0`, `DisplayTimerRelationships: Ally, Neutral, Enemy`, beacon + minimap ping.
- `Atomic` weapon (`weapons-superweapons.yaml:28-386`) — **359 lines of hand-authored WW3MOD content**, with commented phase headers (`PHASE 0: THE FLASH`, thermal radiation, blast wave), four decaying `ShakeScreen` warheads, and a 300%-scale `nuke_large` fireball.
- **All art resolves:** `Icon: abomb` → `icon: … abomb: atomicon` (`sequences-misc.yaml:17`); `BeaconPoster: atomicon` → `beacon: atomicon` (`:58`); `Explosions: nuke_large` → `explosion: nuke_large` (`sequences-ingame.yaml:234`); `MissileImage: atomic` → `atomic:` (`sequences-ingame.yaml:412`); `Cursor: nuke` (`cursors.yaml:179`).
- **All audio resolves:** `AbombLaunchDetected: alaunch1`, `AbombPrepping: aprep1`, `AbombReady: aready1` (`rules/sound/notifications.yaml:4-6`), `InsufficientPower: nopowr1` (`:44`).

**So the answer to "does it drag in art, sound, and a balance conversation the airstrikes do not?" is: no art, no sound — but yes, a much larger design conversation, and the user has already written half of it.**

**Three concrete things stand between MSLO and a player:**

1. `Buildable.Prerequisites: ~disabled` (`:1119`) — the same one-line gate as the Supply Route. Removing it is trivial.
2. **`ChargeInterval: 10`** (`:1139`). At 16.667 tps that is **0.6 seconds**. This is a debug placeholder, not a balance number, and it is exactly the sort of value that reads as harmless in a diff.
3. **The design question is unresolved and the user owns it.** `WORKSPACE/archive/plans/260324-nukes.md` is nine lines of the user's own prose and it does not land: it proposes a DEFCON ladder, a "the player who orders the all-out attack is declared the loser" win-condition inversion, and a tactical-nuke-for-the-losing-player mechanic — and it ends mid-sentence with *"I lost my focus here, i am not sure if it makes sense."* **That is not a task. Nukes-as-a-buyable-power also contradicts the doc's own framing** (*"Nukes are not really meant to be used in the game … part of a gimmicky 'Doomsday' … theme"*), so shipping them as a purchasable Powers entry is a decision the user has to make, not one a worker can infer.

**One live oddity, filed here rather than acted on:** `nuclear-winter-ww3/map.yaml:1146-1148` places `Actor436: mslo` owned by **`Creeps`** at 50,35. It is the only `mslo` on any of the ten shipped maps. No bot module fires support powers in this mod (§1.9), so it almost certainly never launches — but a Creeps-owned silo with a 0.6-second recharge is worth one deliberate look before anyone un-gates nukes.

## 1.8 The lobby option — what it gates, and what it does not

`PowersLobbyOptions` (commented at `world.yaml:568-571`; trait at `engine/OpenRA.Mods.Common/Traits/World/PowersLobbyOptions.cs`) publishes **two** lobby entries under a `"Powers"` group (`:58-90`):

1. **`airstrikes` checkbox** — default enabled (`:29`). **This one works**, but not through the trait: the trait's own `AirstrikesEnabled` property (`:98`) is assigned at `:108` and **read by nothing repo-wide**. The working path is the generic `GrantConditionOnLobbyOption@airstrikes` block in `player.yaml:115-118`, which reads the raw option id and grants `airstrikes-disabled`, which the two `AirstrikePower` blocks consume via `PauseOnCondition`.
2. **`airstrike-cooldown` dropdown** — 2 / 3 / 4 / 5 / 8 minutes, default `"4min"` (`:47`, `:70-88`). **This one is entirely dead.**

**Proof the cooldown is dead:**

```
grep -rn "LobbyChargeIntervalId" --include=*.cs --include=*.yaml .
→ engine/.../SupportPowers/SupportPower.cs:25    (the field declaration)
→ mods/ww3mod/rules/player.yaml:126,147          (two commented usages)
```

Zero readers. `PowersLobbyOptions.AirstrikeCooldown` (`:99`) likewise has no reader outside its own assignment.

**And it used to work.** `6f2191be` (2026-03-24) added *"SupportPowerManager parses 'Xmin' lobby values to ticks (25 ticks/sec)"*. `git log -S "LobbyChargeIntervalId" -- …/SupportPowerManager.cs` returns exactly two commits: `6f2191be` (added) and **`71687440 "Upstream merge: fix OpenRA.Game compilation + resolve duplicate types"` (removed)**. The parsing was collateral damage in an upstream merge; the field, its `[Desc]`, and the lobby dropdown all survived it.

**Note the tick-rate trap while you are here.** The removed parser used 25 tps and the surviving `[Desc]` still asserts it (`SupportPower.cs:25-26`). The real rate is 16.667. And the shipped `ChargeInterval: 6000` (`player.yaml:127, 148`) is **360 s = 6 minutes**, not the 4 minutes the dead dropdown's default implies — a 1.5× error, exactly the class CLAUDE.md warns about.

## 1.9 Two things nobody has mentioned, and both affect the balance conversation

1. **Bots cannot use support powers in this mod.** `grep -rn "SupportPowerBotModule" mods/ww3mod/` returns **nothing**. The module exists in-engine (`BotModules/SupportPowerBotModule.cs:20`, `Requires<SupportPowerManagerInfo>`) and is simply not configured. Ship powers today and they become a **human-only lever** against the AI — a real balance change, and one that cuts across the project's stated goal that bots play a realistic battlefield.
2. **There is no test coverage for any of it.** No file under `engine/OpenRA.Test/` mentions `SupportPower`; `tools/autotest/scenarios/` contains no power / airstrike / nuke scenario. Every power estimate below should be read as including "and a scenario has to be written from scratch".

## 1.10 Cost estimate — Feature A

Sizing convention: **session** ≈ one focused working block, the unit `WORKSPACE` estimates already use.

### Tier (a) — uncomment and re-enable, stock recharge model

| | |
|---|---|
| **Pure uncomment** | **~30 minutes.** 46 lines in `player.yaml:115-160`, 4 lines in `world.yaml:568-571`. Group 2 stays commented. |
| **What that actually ships** | A working Su-25 strike for Russia and **an A-10 that flies over the target and does nothing** for America. |
| **Honest tier (a)** | **1–1.5 sessions.** Uncomment + pick one of the three A-10 remedies + write the new autotest scenario the fix needs (`test-strafe-engage` has no room for an A-10 lane) + delete or restore the two dead lobby-cooldown fields. |

**Risks.**

- **Ships a broken faction power if the A-10 is skipped.** This is not a balance nit; the power visibly does nothing, on the faction a first-time player is likeliest to pick.
- **Remedy 1 (add `Ground` to `^30mm`) has a blast radius** — `^30mm` is widely inherited, and every inheritor gains the ability to shoot dirt. Remedy 2 (a bespoke armament on `A10.Airstrike` alone) is the contained one.
- **Human-only advantage** until a `SupportPowerBotModule` is configured (§1.9). Against the AI this is a straight power-up for the player, and it will move benchmark numbers — which under CLAUDE.md's `@stable` rule must be said in the commit message so the next baseline is re-taken knowingly.
- **The balance pass is real and cannot be automated cheaply.** Six-minute free airstrikes in a mod whose whole economy is budget allocation is a genuine question, and answering it needs playtests.
- **Shipping only the Russian power is not a fallback.** Asymmetric factional powers is a design decision, not a degraded mode.

### Tier (b) — that, plus the buy-for-money conversion

Two shapes, and they are not close in cost.

**B1 — the proxy route (the cheaper one). ~1 session on top of tier (a).**

- New `ClassicProductionQueue@Support` in `player.yaml` — the commented `@Fakestructure` block at `:94-104` is the template.
- One word added to `Production@Local: Produces:` at `structures.yaml:362`. **Not `ProductionFromMapEdge`** (§1.5 trap 1).
- N `powerproxy.*` actor defs: `AlwaysVisible`, no `IOccupySpace`, a `Valued: Cost:`, a `Buildable: Queue: Support`, and the power block with `OneShot: true` + `AllowMultiple: true` + `ChargeInterval: 0`.
- Optional ~10 lines to dispose the spent proxy (§1.5 residue).
- **Zero new engine traits.** The entire buy → stock → spend loop is shipped code.
- **Depends on the buy-menu worker delivering a tab.** Not my call and not costed here.

**B2 — spend cash from the top-left icon. ~1.5–2 sessions on top of tier (a).**

- A cost field on `SupportPowerInfo`; a `Charges` int on a `SupportPowerInstance` subclass; a new buy `Order` plus an `IResolveOrder` case; a `PlayerResources` deduction; a widget click branch; `IconOverlayTextOverride` returning `×N`. Roughly 250–350 lines plus NUnit.
- Buys one thing B1 does not: the purchase and the use are in the same place, so **no sidebar tab is needed at all** — which may matter if the buy-menu worker reports a seventh tab is expensive.

**Risks (both shapes).**

- **B1's failure mode is silent.** Wire the produce type to the wrong `Production` trait and the item builds to 100% and never completes, with no error anywhere. Whoever builds this must be told §1.5 trap 1 explicitly.
- **Pricing a power is a fresh balance axis**, not a tuning of an existing one. There is no precedent in the mod for what an airstrike is worth in a budget-allocation economy.
- **B1 leaks dead actors and dictionary entries** unless the disposal is written.
- **B2 touches `SupportPowersWidget`**, which the buy-menu worker may also be reasoning about. Coordinate before either starts.

### Tier (c) — that, plus nukes and a new menu tab

**3+ sessions, and the gating item is not code.**

- Un-gating MSLO and setting a real `ChargeInterval` is minutes (§1.7).
- **But the design question is open in the user's own words**, and the doc that raises it ends mid-sentence. Nukes-as-a-purchasable-power directly contradicts that doc's framing. **This needs a decision from the user before a single line is written.**
- Then: a nuke touches the win-condition surface (`MustBeDestroyed`), interacts with the `Doomsday Clock` / `TimeLimitManager` block (`world.yaml:548-562`), and reaches an AI that cannot use it.
- The new tab is the buy-menu worker's cost, not mine.

**Risk: this is the quagmire the user named.** Not because the code is hard — it is because the design is unresolved, and unresolved design plus a locked scope is how a v1 slips.

---

# Feature B — transports bought pre-loaded with infantry

## 2.1 The prior discussion — found, and its verdict is more useful than expected

The user is right that it is documented. Four places, in dependency order:

1. **`WORKSPACE/RELEASE_V1.md:138`** — `- [ ] **Cargo Phase 3** — template sidebar for pre-loaded transport purchasing`, under *"Open development threads"*. **It is an open v1 item today.** It is not out of scope; it is in scope and unstarted.
2. **`WORKSPACE/audit/260816-systems-completeness.md:53`** — *"Phase 3 (template sidebar for pre-loaded transport purchasing) was never started — zero hits repo-wide."*
3. **`WORKSPACE/PIPELINE.md:226-230`, item R16** — ❌ *"DO NOT DISPATCH. Neither half is a workable brief. … Phase 3 is one line at `RELEASE_V1.md:138` with zero code hits — **it needs a design pass to become an item, not a worker.**"* It also warns: do not conflate it with `260722_phase3_redteam.md`, which is the AI tactical-positioning phase and unrelated.
4. **`WORKSPACE/pipeline/items/83-veteran-reserve.md:37-42`** and **`WORKSPACE/proposals/260902-safe-wins-and-swings.md:744-748`** — both cite it as the blocking precedent for a *different* feature: *"⚠️ Sidebar scope — this is the one that could kill it. … the same class of work is already an open, unstarted thread … If that is hard, this is hard for the same reason, and for the same missing design."*

`git log --all -i --grep=` on preset / preload / pre-loaded / fireteam / transport returns **nothing** on this subject — every preset-related commit in history is the *lobby* preset system (§2.4).

> **Was it skipped for a reason that still holds?** Partly, and the distinction is the most decision-relevant thing in this section.
>
> It was **never** skipped because someone established the mechanism was hard. Three separate documents reached the same verdict — *too vague* — and **none of them looked at whether the engine supports it.** That reason still holds for the **user-authored-preset half** (nobody has decided what a preset editor is). It is **dischargeable for the fixed-preset half**, because §2.2 establishes the mechanism exists, ships, and is exercised upstream.
>
> Second-order finding worth carrying: **PIPELINE item 83 (veteran reserve) is currently blocked on this item.** If fixed presets ship and prove the "buy a composed thing from the sidebar" pattern, item 83's stated killer risk goes away with it.

## 2.2 What the engine already supports — verified, and it is more than expected

**Two inits, both shipped:**

- **`Cargo.InitialUnits`** (`Cargo.cs:54`): `[Desc("A list of actor types that are initially spawned into this actor.")]`, consumed at `:326-333`:

  ```
  foreach (var u in info.InitialUnits)
  {
      var unit = self.World.CreateActor(false, u.ToLowerInvariant(),
          new TypeDictionary { new OwnerInit(self.Owner) });
      cargo.Add(unit);
  }
  totalWeight = cargo.Sum(c => GetWeight(c));
  ```

- **`CargoInit`** (`Cargo.cs:1290`, a `ValueActorInit<string[]>`), consumed at `:305-320`, which takes precedence over `InitialUnits`. This is the per-instance variant — the one a *player-authored* preset would need.

**Neither is used anywhere in `mods/ww3mod/`** (`grep -rn "InitialUnits" mods/ww3mod/` → nothing). Upstream RA uses it in map rules (`engine/mods/ra/maps/ant-03/rules.yaml:35,46`; `desert-shellmap/rules.yaml:62`), so the code path is exercised in the wild.

**It survives the Supply Route call-in path intact.** This was the brief's open question and the answer is clean:

- `ProductionFromMapEdge.Produce` inspects `producee.TraitInfoOrDefault<AircraftInfo>()` and `…<MobileInfo>()` (`:85-86`) — **the transport's traits, not its cargo's** — picks an edge cell round-robin (`:96-155`), and ends in a plain `self.World.CreateActor(producee.Name, td)` (`:181`), then queues the rally-point waypoint activities (`:184-187`).
- `Cargo`'s constructor runs inside that `CreateActor`, so the squad is aboard before the unit takes its first step.
- Nothing on the path unloads. `EjectOnDeath: True` is set on all the ground transports, so one killed en route dumps its squad — which is the desired behaviour.

**Net: a pre-loaded Humvee bought from the Vehicle queue spawns at the map edge and drives to the rally point with its fireteam inside.** That is arguably closer to the mod's own reinforcement fiction than anything currently shipping.

**The transports that could carry a preset** (`Cargo:` with `Types: Infantry`, ground vehicles):

| Actor | `Cargo:` at | `MaxWeight` |
|---|---|---|
| `humvee` | `vehicles-america.yaml:157` | 8 |
| `m113` | `vehicles-america.yaml:283` | 12 |
| `bradley` | `vehicles-america.yaml:440` | 6 |
| `strykershorad` | `vehicles-america.yaml:1055` | 9 |
| `btr` | `vehicles-russia.yaml:111` | (not read) |
| `bmp2` | `vehicles-russia.yaml:275` | (not read) |

Aircraft carry cargo too (`TRAN`, `littlebird`, `HALO`, `HIND`, `BADR`), but a preloaded helicopter is a different balance question and is not costed here.

`PassengerInfo.Weight` defaults to `1` (`Passenger.cs:29`) and `^Infantry` sets no override (`infantry.yaml:87-94`), so `MaxWeight` is a headcount throughout — which `CargoInfo`'s own tooltip comment already asserts (`Cargo.cs:35-37`).

## 2.3 ⚠️ Three engine hazards and one exploit — this is the real cost of tier (a)

**1. `InitialUnits` is not lint-checked.** It carries **no `[ActorReference]` attribute** (`Cargo.cs:53-54`) — contrast `ProduceActorPowerInfo.Actors` (`ProduceActorPower.cs:19-22`) and `SupportPowerCrateActionInfo.Proxy` (`:19-21`), both of which do. `CheckActorReferences.cs` therefore cannot see a preset's contents. **A typo'd actor name is not a lint error; it is a `CreateActor` throw at runtime**, inside a frame-end task, at the moment the unit is delivered.

**2. A non-`Passenger` entry throws too.** `static int GetWeight(Actor a) { return a.Info.TraitInfo<PassengerInfo>().Weight; }` (`Cargo.cs:381`) — `TraitInfo<>`, not `TraitInfoOrDefault<>`. Put a vehicle in an infantry preset and it throws on the weight sum at `:333`.

**3. The `InitialUnits` path bypasses every capacity and type check.** `:326-333` calls `cargo.Add(unit)` directly. It does not consult `HasSpace`, `MaxWeight`, `Types`, or `loadFilters`. **An over-capacity or wrong-`CargoType` preset loads silently** and only misbehaves later.

> Hazards 1–3 together mean **fixed presets need an NUnit test that walks every preset actor's `InitialUnits` and asserts each entry exists, carries `Passenger`, has a matching `CargoType`, and fits `MaxWeight`.** That test is small and it is not optional — it is the lint the engine does not provide.

**4. ⚠️ The refund is a money pump, and this is what stops tier (a) being free.**

`GetSellValue` (`CustomSellValue.cs:28-54`) computes `CustomSellValueInfo.Value ?? ValuedInfo.Cost`, then deducts missing ammo and missing supply. **There is no passenger term.** The evacuation refund reads it directly: `RotateToEdge.cs:457` — `var sellValue = self.GetSellValue();` (also `Sell.cs:37`, `DeliversCash.cs:96`, `GivesBounty.cs:60`).

So a preset actor has two pricing options and **both are broken:**

- **Priced at transport + infantry** (the honest price): the player buys the preset, unloads the squad, evacuates the now-empty hull, and is refunded the *combined* price. The infantry are free and have been laundered into cash. Repeatable.
- **Priced at the transport alone:** the infantry are free at the point of purchase. Straightforwardly better than buying them separately, for everyone, always.

**The fix is small and it belongs inside tier (a), not after it:** add a passenger term to `GetSellValue` — sum `GetSellValue()` over the `Cargo` trait's passengers. ~10 lines plus a test. It is also correct for **every** transport, not just presets: today, evacuating a loaded Bradley refunds the Bradley and silently deletes the squad's value.

**5. Minor — veterancy accrual splits.** `RankAccumulation` (`player.yaml:22`, `Types: Infantry, Vehicle, Plane, Ship`) keys free-rank stock by actor name. A `humvee.fireteam` preset is a **separate stock line** from `humvee`, so a player alternating between them accrues rank on each about half as fast. Not a bug; worth one sentence in the design so it is a choice rather than a surprise.

**6. Minor — the buy tooltip will under-describe.** `CargoInfo` implements `IProvideTooltipDescription` and returns exactly one line, `Stat("Carries", "{MaxWeight} infantry")` (`Cargo.cs:40-48`). A preset's tooltip would say "Carries 8 infantry" and **not list what is actually inside**. Adding the manifest is ~15 lines in that same method, gated on `InitialUnits.Length > 0`.

## 2.4 Cost estimate — Feature B

### Tier (a) — a handful of fixed presets

**1 session**, and here is where it goes:

| Work | Size |
|---|---|
| ~4–8 preset actor defs (`humvee.fireteam:` = `Inherits: humvee` + `Cargo: InitialUnits:` + `Valued: Cost:` + `Tooltip: Name:` + `Buildable:`) | pure YAML, ~1 hour |
| **The `GetSellValue` passenger term** (§2.3 hazard 4) | ~10 lines C# + NUnit — **mandatory** |
| Preset-validity NUnit test (§2.3 hazards 1–3) | ~60 lines — **mandatory** |
| Tooltip manifest line (§2.3 item 6) | ~15 lines, optional but cheap |
| Sidebar entry class / where they go | **the buy-menu worker's cost, not costed here** |

**Risks.**

- **Without the refund fix this is a money pump from day one**, and it will not surface in playtesting — it looks exactly like a normal unload.
- **Preset content errors are runtime crashes, not lint errors.** The test is the whole mitigation.
- **The AI will buy them.** `UnitBuilderBotModule` builds from the queue, so preset entries become bot-purchasable the moment they carry a `Buildable`. That is probably *good* — a bot arriving with mounted infantry is more realistic than the current pattern — but it is a behaviour change reaching both bot profiles and must be stated in the commit message per CLAUDE.md's `@stable` rule.
- **Roster coupling.** Every preset hardcodes infantry actor names. Rename or remove a soldier and every preset naming it crashes, silently, at delivery.
- **Combinatorial creep is the design risk, not the code risk.** Six transports × N compositions is how "a handful of presets" becomes twenty sidebar entries. The discipline has to come from the design, not the implementation.

### Tier (b) — player-authored presets, persisted between games

**3–5 sessions, and I would not put it in v1.**

**What already exists and helps.** The persistence pattern is in-tree and WW3MOD-authored: `LobbyPresetLogic.cs` reads and writes `<Platform.SupportDir>/lobby-presets.yaml` (`archive/plans/260511_lobby-redesign.md:47,55,148-165`; Phase 7 at `:249-260`). So *"serialise a named preset to a support-dir YAML"* is a solved problem in this repo. **That is the cheapest part of tier (b), and it is the part that looks expensive.**

**What does not transfer, and is the real cost:**

1. **A user preset cannot be a queue item, because OpenRA production is ruleset-driven.** `ProductionQueue` builds from `ActorInfo`s carrying a `Buildable` whose `Queue` contains the category (`ProductionQueue.cs:243-244`), and the ruleset is fixed at map load. **There is no way to add an actor type at runtime.** So a user preset needs a *different order shape* entirely — buy the empty transport, then automatically queue and load the named passengers — which is a new order, a new activity, and a new sync surface. **Tier (a) does not scale up into tier (b); they are different features that happen to look alike.**
2. **Multiplayer.** A preset is client-local data that would drive a simulation purchase. **That is the exact shape of a defect that shipped here and had to be fixed** — the eject-rally desync, where `EjectRallyOrderGenerator.cs:62` wrote to a non-`[Sync]` dictionary the simulation then read (`audit/260816-systems-completeness.md:30-42`; fixed `409b0fd2`, closed as PIPELINE R10). The manifest must travel inside the `Order` and be validated on receipt, never read from local settings mid-simulation.
3. **Validation against a changing roster.** A saved preset outlives the ruleset it was written against. Every load needs: does each actor still exist, is it still buildable by this faction, does it still fit, is it still a valid `CargoType` — and then a *repair* story. Silently drop the missing entry, refuse the preset, or show it greyed? Nobody has made that decision.
4. **The editor UI itself** — pick a transport, pick slots, see the weight budget, name it, save it, delete it, reorder it. This is the "template sidebar" R16 called too vague, and **it is still too vague**.

**Risk: this is the second quagmire.** Persistence across games looks like a serialisation problem and is actually a validation-and-versioning problem, and the multiplayer half has a shipped precedent in this repo for getting it wrong.

## 2.5 Supply Route interaction

**Verified benign for tier (a)** (§2.2). Three specifics worth recording:

- The preset is delivered by `ProductionFromMapEdge` (`structures.yaml:364-366`, `Produces: Infantry, Soldier, Vehicle, Aircraft, Helicopter`), so it spawns at a round-robin map-edge cell and replays the rally-point waypoint plan (`ProductionFromMapEdge.cs:170-187`). Passengers are inert during that drive.
- **Contestation applies normally.** `SupplyRouteContestation`'s `IProductionSpeedModifier` (`SupplyRouteContestation.cs:860`) throttles the queue, not the delivery, so a preset is slowed exactly like any other vehicle.
- **NOT verified:** whether a preset transport killed *between the map edge and the rally point* drops its squad somewhere reachable. `EjectOnDeath: True` is set and the eject path is independent of the queued move activities; I read both code paths but ran nothing. One autotest scenario settles it if it matters.

---

# 3. Recommendation

**Split the two features. They are not equally ready and they should not be decided together.**

### Feature B tier (a) — fixed presets: **yes, v1** *(contingent on the buy-menu worker)*

It is the only item here already in v1 scope (`RELEASE_V1.md:138`), the mechanism is shipped engine code that upstream exercises, and its one serious hazard — the refund pump — is a ~10-line fix that **repairs an existing bug**: loaded transports already evacuate for the wrong amount today, presets or no presets. If the buy-menu worker reports a seventh entry class is cheap, this is a good session's work with a clear finish line. If they report it is expensive, defer it — the value is in the sidebar, not the YAML.

### Feature A — **no, v1.1** — and tier (a) is not as cheap as the deferral note claims

`RELEASE_V1.md:178` says re-enabling *"needs balance pass"*. That understates it. **Uncommenting the blocks today ships a NATO airstrike that cannot fire**, and the fix is an unmeasured three-way design choice that also needs an autotest scenario built from scratch. The `[v1.1]` classification is right; the note under it is not.

If the user wants powers in v1 anyway, **the cheapest honest package is tier (a) + B1** — 2–2.5 sessions total — and B1 is worth having *because* the proxy route (§1.5) means the buy-for-money model the user actually asked for costs almost nothing on top of the re-enable. The thing to refuse is tier (c).

### Nukes, and user-authored presets — **neither, and not v1.1 either until a design decision lands**

Both are blocked on the same thing: a decision the user has not made. For nukes it is written down and unresolved (`archive/plans/260324-nukes.md`, which ends *"I lost my focus here"*). For user presets, three separate documents have already ruled it *too vague to dispatch* and nothing has changed that. **Sending a worker at either today produces a design document, not a feature** — which is fine if that is what is wanted, but it should be commissioned as a design pass and not costed as implementation.

### One correction to the tracker, for whoever picks this up

`RELEASE_V1.md:178` — *"Re-enable by uncommenting; needs balance pass"* — is wrong in the expensive direction, in exactly the way CLAUDE.md's routing table warns about. It should read: re-enabling gives a working Su-25 and a non-functional A-10 (`bugs/discovered.md:127`, `DISCOVERIES.md:2449-2487`), plus a lobby cooldown dropdown that has done nothing since `71687440`. **Not edited by this branch** — `RELEASE_V1.md` is a hot shared file and this recon is read-only; the correction is one line and is the manager's to apply.
