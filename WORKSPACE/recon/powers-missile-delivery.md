# Missile-strike support powers — delivery mechanism and warhead numbers

**Researched against `main @ 2c8488ef`** in worktree `wt/powers-missile` (branch `wt/powers-missile`, cut from that commit; `git rev-list --count HEAD..@{u}` cannot run — the branch has no upstream). **Static analysis only. No game runs, no autotests, no YAML lint, no build.** Every claim carries a `file:line` read at that SHA, or a commit SHA.

**Timestep:** `mods/ww3mod/mod.yaml:358` selects `DefaultSpeed: default`, whose block at `:380-383` gives `Timestep: 60` ms ⇒ **16.667 ticks/s**. `seconds = ticks × 0.06`. Every duration below shows its arithmetic. The 25 tps and 40 tps readings CLAUDE.md warns about are both live in files this document touches.

**Scope note.** Whether a missile can be *shot down* is another worker's question and is not answered here. This document is delivery and numbers only. (One fact leaks across and is worth flagging: the shipped in-flight missile is a full actor with `Health: HP: 100` and `Targetable: TargetTypes: ICBM` — `defaults.yaml:1074-1101` — so on that delivery shape interception is already half-built.)

---

## 0. Headline findings

1. **`NukePower` cannot deliver a missile from the map edge, and no YAML setting changes that.** The offset that separates the missile's spawn from its target is constructed `new WVec(WDist.Zero, WDist.Zero, velocity * (impactDelay - turn))` — **Z only** (`NukeLaunch.cs:73`), and the descent begins at `descendSource = targetPos + offset`, i.e. *directly above the target* (`:76`). `SkipAscent` sets `turn = 0` (`:62`) and starts the missile at that same point — it removes the ascent, it does not add a lateral leg. There is no spawn-offset field that reaches the descent. Answer to the brief's (a)/(b)/(c): **(c), a new trait** — but a small one. §1.2.

2. **The right delivery shape already ships, is hand-tuned, and is not a support power.** `BallisticMissile` (`engine/OpenRA.Mods.Common/Traits/BallisticMissile.cs`) + `BallisticMissileFly` (`engine/OpenRA.Mods.Common/Activities/BallisticMissileFly.cs`) fly a real actor along a parabola from wherever it spawned to a target position, with `Speed`, `Acceleration`, `TerminalSpeed`, `LaunchAngle`, exhaust trails and an ignition sound. It is live on `IskanderMissile` (`vehicles-russia.yaml:1116-1154`) and `HIMARSMissile` (`vehicles-america.yaml:1212+`). A Kinzhal is that actor spawned at the map edge instead of on a launcher. §1.3.

3. **The new power trait is ~80–120 lines and every part of it is copied from something shipped.** Edge cell: `map.ChooseClosestEdgeCell(self.Owner.HomeLocation)` (`AirstrikePower.cs:79`). Target assignment: `bm.Target = Target.FromPos(target.CenterPosition)` then add to world (`MissileSpawnerMaster.cs:112,116`) — `BallisticMissile.AddedToWorld` queues the flight itself (`BallisticMissile.cs:218`). Beacon/camera: `NukePower.cs:180-208`. **No new engine subsystem, no new projectile, no new warhead type.** §1.4.

4. **Closest existing trait, if you refuse to write code: `ParatroopersPower`.** It is the only power in either mod that computes a genuine map-edge entry *along a chosen bearing* — `startEdge = target − (DistanceToEdge(target, −delta) + Cordon) · delta / 1024` (`ParatroopersPower.cs:103`) — and the only one that reads the player's directional pick (`:83`). Point it at a missile-shaped aircraft and you get a cruise missile, not a Kinzhal. §1.5.

5. **Two `AirstrikePower` fields are dead, and both were killed by the same WW3MOD rewrite.** `AirstrikePowerInfo.QuantizedFacings` (`:35`) has no reader anywhere in the engine, and `AirstrikePower` never reads `order.ExtraData`, so `UseDirectionalTarget: true` draws eight direction arrows and then discards the player's choice — only `ParatroopersPower` consumes it. `git log -S` names `a20c8a82 "Rework airstrike: spawn from base edge, attack-move, selectable"` (2026-03-24) as the commit that replaced both with `ChooseClosestEdgeCell`. **That is also a design precedent worth honouring: in this mod a support power arrives from the edge nearest YOUR base.** §1.6.

6. **Perceived speed is `BallisticMissile.Speed`, in WDist/tick, and `cells/s = Speed × 0.016276`.** Shipped anchors: Iskander missile 600 (9.77 c/s), HIMARS rocket 500 (8.14 c/s), F-16 airframe 525 (8.54 c/s), A-10 390 (6.35 c/s). A Kinzhal at `Speed: 2000` is 32.6 c/s — **3.8× an F-16**, crossing a 50-cell run in 25 ticks = **1.5 s**. A Tomahawk/Kalibr at `Speed: 350` is 5.70 c/s, 146 ticks = **8.8 s** over the same run: slower than every aircraft in the game, which is the correct read. §1.7.

7. **Zero scatter is not merely expressible — it is the default on this path.** `BallisticMissileFly` ends `sbm.SetPosition(self, targetPos)` then kills the actor (`:208-210`); `Explodes` detonates at the corpse's position. `Inaccuracy` is a **projectile** field (`Bullet`/`Missile`), and there is no projectile here. `NukePower` is likewise exact — `descendTarget = targetPos` (`NukeLaunch.cs:77`). **Adding scatter to a cruise-missile tier is the thing that would need work, not removing it.** §1.8.

8. **The `Atomic` weapon, in one line: a 6.25-cell AIRBURST that instantly vaporises everything within ~8.4 ground cells, kills infantry to ~23 cells, and sets fire to structures out to 28 cells.** Peak single-application damage on an Abrams is ~266,000 against 28,000 HP — **9.5× overkill** — and that is measured from the burst point, not from an idealised ground zero. §2.1, §2.2.

9. **A conventional missile warhead has a hard anchor already: `IskanderExplosion` (`weapons-explosions.yaml:521-571`) does ~62,800 to an Abrams on a direct hit — 2.24× its HP — for a 6000-credit launcher carrying two.** A Kinzhal that is "one Iskander, but you do not have to drive there" is roughly the honest sizing; anything much above ~80,000 point damage stops being a missile and becomes a small nuke. §2.3.

10. **You cannot nuke the Supply Route, and the reason in the engine comment is wrong.** `TimeOrSrCaptureWinRule.cs:49` says *"SR is indestructible by design (Armor: Indestructable)"*. It is not the armour: no weapon in `mods/ww3mod/rules/weapons/` lists `Indestructable` in a `Versus` block, and `Indestructable` carries no `Thickness`, so the armour type contributes exactly nothing. The mechanism is `Targetable: TargetTypes: NoAutoTarget` alone (`structures.yaml:296-297`) — no weapon's `ValidTargets` contains `NoAutoTarget`, so `IsValidAgainst` rejects every warhead. Correct outcome, wrong stated cause. §2.5.

11. **Balance method used: arithmetic over shipped YAML, re-deriving the engine's damage pipeline from `DamageWarhead.cs`. The combat-sim was NOT used and is NOT usable for this question** — `tools/combat-sim/build/` does not exist (needs a TypeScript build), `dump-stats.sh` refuses without `engine/bin/OpenRA.Utility.dll` which is absent, and the committed `data/stats.json` is **stale in exactly the fields this question needs**. §3.

12. **Two defects found on the way, neither previously filed.** (a) `NukeLaunch.Explode` builds its own `WarheadArgs` and never sets `ImpactPosition` (`NukeLaunch.cs:143-150`), so every nuke splash computes armour-facing direction from the map origin — the same family as `missiles.md` §10 instance 1, at a site §10 does not name. (b) `BallisticMissileFly` reads `sbm.Info.Speed` raw (`:52, :220, :223`) and never calls `BallisticMissile.MovementSpeed` (`:230`), so `ISpeedModifier` is dead on the flight path. §4.

---

# Q1 — How a missile gets from the map edge to a target

## 1.1 `NukePower` end to end, and what the four fields actually do

`NukePower.Activate` (`NukePower.cs:166-209`) does five things and then is finished — it owns no per-tick logic:

```
skipAscent = info.SkipAscent || body == null                                                      (:169)
launchPos  = skipAscent ? WPos.Zero : self.CenterPosition + body.LocalToWorld(info.SpawnOffset)   (:170)
new NukeLaunch(... launchPos, targetPosition, DetonationAltitude, RemoveMissileOnDetonation,
               FlightVelocity, MissileDelay, FlightDelay, skipAscent, trail…)                     (:172-176)
RevealShroudEffect at FlightDelay - CameraSpawnAdvance, for CameraSpawnAdvance + CameraRemoveDelay (:185)
Beacon, removed at FlightDelay - BeaconRemoveAdvance                                              (:191-205)
```

**The projectile class is `NukeLaunch` (`engine/OpenRA.Mods.Common/Projectiles/NukeLaunch.cs`), and it is an `IProjectile` *effect*, not an actor and not a `Missile`.** It has no hitshape, no health, no collision, no guidance and no `Inaccuracy`. It cannot be intercepted, blocked or deflected because there is nothing there to interact with. It is a sprite on a lerp plus a `weapon.Impact` at the end.

| Field | Live value on `MSLO` | What it does, from the code |
|---|---|---|
| `MissileDelay` | `5` (`structures-defenses.yaml:1149`) | `launchDelay`; `Tick` early-returns while `launchDelay-- > 0` (`NukeLaunch.cs:89`). 5 ticks × 0.06 = **0.30 s** of nothing after the click. |
| `FlightDelay` | `70` (`:1161`) | `impactDelay`. Split as `turn = skipAscent ? 0 : impactDelay / 2` (`:62`) = 35. Total nominal flight 70 ticks × 0.06 = **4.20 s**. |
| `FlightVelocity` | `1024` (`:1162`) | Builds `offset = (0, 0, velocity × (impactDelay − turn))` = 1024 × 35 = 35,840 wdist = **35 cells** (`:73`). Ascent covers it in 35 ticks and descent covers it in 35 ticks, so it really is the speed of both legs: 1024 wdist/tick = 1 cell/tick = **16.67 cells/s**. |
| `DetonationAltitude` | `6c256` = 6400 wdist (`:1160`) | Detonates when `DistanceAboveTerrain(pos) <= detonationAltitude` while descending (`:129`). Altitude at tick *t* is `35840 × (70−t)/35`; ≤ 6400 when `70−t ≤ 6.25`, so **t = 64**. |
| `SkipAscent` | unset ⇒ `false` | Sets `turn = 0` and starts the missile at `descendSource` — see §1.2. **`MSLO` does have `BodyOrientation`** (via `^Building` → `^BasicBuilding` → `^SpriteActor`: `structures.yaml:69`, `:4`, `defaults.yaml:43-44`), so the `body == null` fallback does not fire and the full ascent runs. |

**Full timeline of one shipped nuke:** click → 5 ticks dead → 35 ticks rising vertically out of the silo → **a hard cut**: the sprite jumps from above the silo to 35 cells above the target → 29 ticks descending → detonation at tick 64 ≈ **3.84 s after the click**, at **6.25 cells above the terrain**. Camera reveals `20c0` from tick 45 for 1025 ticks = **61.5 s**. Beacon disappears at tick 45.

**`ChargeInterval: 10` (`:1139`) is 10 × 0.06 = 0.60 s.** The recon's figure is right; restated because it is the single most dangerous number in that file.

## 1.2 Why "from the map edge" is not reachable from `NukePower`'s YAML

`NukeLaunch`'s constructor is five lines and they are the whole answer:

```csharp
var offset    = new WVec(WDist.Zero, WDist.Zero, velocity * (impactDelay - turn));  // :73  ← X and Y are literally WDist.Zero
ascendSource  = launchPos;                                                          // :74
ascendTarget  = launchPos + offset;                                                 // :75
descendSource = targetPos + offset;                                                 // :76  ← directly above the target
descendTarget = targetPos;                                                          // :77
```

Then `Tick` lerps `ascendSource → ascendTarget` for `ticks < turn` and `descendSource → descendTarget` after (`:111-115`). The two legs are **not joined**: at `ticks == turn` the position jumps discontinuously from above the silo to above the target. `WPos.LerpQuadratic(..., WAngle.Zero, ...)` (`WPos.cs:63`) with a zero pitch is a plain linear interpolation, so both legs are straight vertical lines.

Consequences, each of which someone will otherwise try:

- **`SpawnOffset` cannot help.** It is applied to `launchPos` only (`NukePower.cs:170`), which feeds `ascendSource`/`ascendTarget`. The descent never reads it. A `SpawnOffset` of 40 cells moves the *ascent* 40 cells sideways and changes the impact by nothing.
- **`SkipAscent: true` does not give a lateral entry.** It sets `turn = 0`, so `offset` becomes `velocity × impactDelay` and `pos` starts at `descendSource` (`:84`) — still directly above the target. It is "drop from higher up", not "come in from somewhere".
- **`FlightVelocity` only trades altitude for time.** Raising it raises the start altitude proportionally, because the same number multiplies the offset and divides the descent.

So the honest classification is **(c), a new or derived power trait.** It is not, however, a large (c) — see §1.4.

## 1.3 The delivery shape that already ships: `BallisticMissile`

`IskanderMissile` (`vehicles-russia.yaml:1116-1154`) inherits `^ShootableMissile` (`defaults.yaml:1074-1101`) and is a real actor with a hitshape, `Health: HP: 100`, `WithShadow`, an exhaust trail, and a `BallisticMissile` trait. `BallisticMissileFly` gives it:

| Field | Iskander | HIMARS | What it means, from `BallisticMissileFly.cs` |
|---|---|---|---|
| `Speed` | 600 | 500 | WDist/tick horizontal. `horizontalProgress += currentSpeed / hDist` per tick (`:227`), so flight ticks ≈ `hDist / Speed`. |
| `Acceleration` | 3 | 4 | WDist/tick² added until `Speed` (`:223`). Flight time is then simulated at construction (`:64-79`), not divided. |
| `InitialSpeedPercent` | 0 | — | Starts from rest. |
| `LaunchAngle` | 110 | 80 | `arcPeakHeight = hDist × Tan() / (4 × 1024)` (`:91`). `WAngle.Tan()` returns tan × 1024 (`WAngle.cs:80-87`), so 110 units = 38.7°, tan ≈ 0.800, and over a 50-cell run the apex is **10 cells**. 30 units = 10.5° gives a 2.3-cell apex — a flat hypersonic profile. |
| `TerminalSpeed` / `TerminalAcceleration` | 600 / 10 | — | Past `horizontalProgress ≥ 0.5` the cap switches (`:214-221`). Set above `Speed` for "accelerates on the way down". |
| `LaunchRiseTicks` / `PostErectionWaitTicks` | 60 / 20 | 0 | Pre-launch erection, **80 ticks = 4.80 s** on the Iskander. `PreLaunchTicks` is 0 unless `LaunchRiseTicks > 0` (`BallisticMissile.cs:85`). For a power spawned already in the air, both should be 0. |
| `VisualPitchMultiplier` | 47 | — | Fakes 3D pitch on a 2D sprite (`:104-138`). |
| `IgnitionSound` | `vv3latta/b.aud` | — | Played once at motor light (`BallisticMissile.cs:186-187`); a lint pass, `CheckMissileLaunchReport.cs`, fails the build if a launch report is put on the weapon instead. |

The two properties that matter for the brief:

- **It flies from where it was created.** `spawnPos = self.CenterPosition` (`:44`), `targetPos = t.CenterPosition` (`:45`). Create the actor at a map-edge cell at altitude and it flies in from the map edge. Nothing in the trait cares where the launcher was, or whether there was one.
- **It arrives exactly.** `sbm.SetPosition(self, targetPos); Queue(new CallFunc(() => self.Kill(self)))` (`:208-210`). `Explodes`/`SpawnedExplodes` on the missile then detonate at the corpse position (`vehicles-russia.yaml:1148-1154`).

## 1.4 Sizing the new trait

Everything a `MissileStrikePower` needs is an existing call:

| Need | Shipped source |
|---|---|
| Pick the map-edge cell | `map.ChooseClosestEdgeCell(self.Owner.HomeLocation)` — `AirstrikePower.cs:79` |
| …or an edge along a bearing | `map.DistanceToEdge(target, -delta)` — `ParatroopersPower.cs:103` |
| Spawn the missile in the air | `w.CreateActor(false, type, { CenterPositionInit(edge + (0,0,alt)), OwnerInit, FacingInit })` — `AirstrikePower.cs:104-109` |
| Give it a target and launch | `bm.Target = Target.FromPos(pos); w.Add(actor);` — `MissileSpawnerMaster.cs:112,116`; the flight activity is queued by `BallisticMissile.AddedToWorld` (`:218`) |
| Beacon + reveal camera + minimap ping | `NukePower.cs:180-208`, unchanged |
| Target selection cursor + range circles | `SelectNukePowerTarget` / `SelectGenericPowerTarget` — `NukePower.cs:217-243` |

**Estimate: 80–120 lines of C#, all of it assembled from those five sites, plus one `^ShootableMissile`-derived actor per tier and one weapon per tier in YAML.** No new warhead class, no new projectile, no new effect. The only genuinely new decision is the `Target` handshake — `BallisticMissile.Target` is a public field set *before* `w.Add`, and getting that order wrong gives a missile that flies to `WPos.Zero` (the map corner) with no error.

**One trap to write into the trait from the start.** `BallisticMissileFly` interpolates `baseZ` linearly from `spawnPos.Z` to `targetPos.Z` and adds the parabola on top (`:242-245`). Spawn at the edge at high altitude and you get a descending glide with a bump — which is the desired Kinzhal look — but `LaunchAngle` still scales the bump by `hDist`, so a long shot arcs higher than a short one. For a fixed-looking terminal dive, drive the altitude from the spawn Z and keep `LaunchAngle` low (≤ 40 units ≈ 14°).

## 1.5 Every `SupportPower` subclass, and what it delivers

`engine/OpenRA.Mods.Common/Traits/SupportPowers/` and `engine/OpenRA.Mods.Cnc/Traits/SupportPowers/`, complete:

| Trait | Delivers | Map-edge entry? | Relevance to a missile |
|---|---|---|---|
| `SupportPower` (`SupportPower.cs:166`) | base class; charge, icon, beacon, notifications | — | the base every option inherits |
| `DirectionalSupportPower` (`DirectionalSupportPower.cs:32`) | base + an 8-arrow direction picker (`UseDirectionalTarget`) | — | how a player would choose the *bearing* a Kinzhal comes from |
| `AirstrikePower` (`AirstrikePower.cs:53`) | N aircraft, spawn at the edge nearest the owner's home, fly to target, fly back, exit | **Yes**, but hard-coded to the owner's own edge | closest *lateral entry* precedent; wrong payload |
| `ParatroopersPower` (`ParatroopersPower.cs:69`) | aircraft in from one edge, through the target, out the far edge, dropping cargo | **Yes, along a chosen bearing** | the only power with true bearing-driven edge maths (`:103-104`) |
| `NukePower` (`NukePower.cs:141`) | a `NukeLaunch` effect: straight up, cut, straight down | **No — structurally impossible** | correct warhead plumbing, wrong flight |
| `ProduceActorPower` (`ProduceActorPower.cs:51`) | queues an actor through a production queue | No | not a strike |
| `SpawnActorPower` (`SpawnActorPower.cs:54`) | one actor at the *target* cell, optional lifetime | No — spawns **on** the target | closest existing trait by shape, but it uses `LocationInit(cell)` (`:86`) and never sets `BallisticMissile.Target`, so a missile spawned this way flies to the map corner |
| `GrantExternalConditionPower` (`GrantExternalConditionPower.cs:61`) | a condition in an area | No | EMP-style effects |
| `AttackOrderPower` (Cnc, `AttackOrderPower.cs:40`) | orders the *host actor's own* `AttackBase` to fire at the target | No | **the "put a launcher on the map" route** — see below |
| `ChronoshiftPower` (Cnc, `:66`) | teleports units | No | — |
| `DropPodsPower` (Cnc, `DropPodsPower.cs:81`) | 5–8 `FallsToEarth` pods entering **at an angle** set by `PodFacing` (`:103-108`) | Angled from off-screen, not from a map edge | the only other angled-arrival power; gated by `CanActivate` on landable, empty terrain (`:118-123`), which disqualifies it for strikes into a base |
| `GpsPower` (Cnc, `:55`) | reveals the map | No | — |
| `GrantPrerequisiteChargeDrainPower` (Cnc, `:43`) | a prerequisite while draining | No | — |
| `IonCannonPower` (Cnc, `IonCannonPower.cs:65`) | a `WeaponDelay`-fused beam effect at the target | No | vertical-only, like `NukePower` |

**`AttackOrderPower` deserves its own line, because it is the one route that needs zero new C#.** It requires `AttackBaseInfo` on the host and calls `attack.AttackTarget(...)` (`:61`). Put a `BallisticMissile`-spawning armament (the Iskander's `MissileSpawnerMaster` pattern, `vehicles-russia.yaml:1101-1104`) on a hidden actor, and the power fires it. The catch is `SelectAttackPowerTarget.IsValidTarget` (`:102-108`): the target must be within `attack.GetMaximumRange()` **of the host actor**, and the annotation draws min/max range circles around it (`:130-153`). That is a launcher with a range, not an off-map strike — the right trait if the design wants a visible, killable launch site, and the wrong one for "arrives from the map edge".

## 1.6 Two dead fields in `AirstrikePower`, and the precedent they set

`AirstrikePowerInfo.QuantizedFacings = 32` (`:35`) is **read by nothing**: `grep -rn "QuantizedFacings" --include=*.cs engine/` returns 20 hits, all of them `BodyOrientation`/`Turreted`/render code plus `ParatroopersPower.cs:93` — never `AirstrikePower`.

`AirstrikePower` inherits `DirectionalSupportPowerInfo`, so `UseDirectionalTarget: true` is accepted in YAML and puts `SelectDirectionalTarget` on screen, which encodes the player's pick as `ExtraData` (`SelectDirectionalTarget.cs:91-94`). **`AirstrikePower` never reads `order.ExtraData`.** The only reader in either mod is `ParatroopersPower.cs:83`. So the arrows draw and the choice is discarded.

`git log -S "ChooseClosestEdgeCell" -- .../AirstrikePower.cs` returns one commit — `a20c8a82 "Rework airstrike: spawn from base edge, attack-move, selectable"` (2026-03-24) — and `git log --all -S "order.ExtraData"` on the same file returns the same commit plus the initial import. **The two were removed together, deliberately, to make the airstrike always come from the edge nearest the player's own base.**

That is a shipped design precedent and the proposal should either follow it or break it on purpose. Following it makes the Kinzhal's approach bearing free (no extra UI); breaking it means reviving `UseDirectionalTarget`, which is ~3 lines in the new trait (`info.UseDirectionalTarget && order.ExtraData != uint.MaxValue ? WAngle.FromFacing((int)order.ExtraData) : null`, lifted verbatim from `ParatroopersPower.cs:83`).

## 1.7 Speed — the field, the units, and the arithmetic

For a `BallisticMissile` delivery the field is **`BallisticMissile.Speed`, WDist per tick** (`BallisticMissile.cs:23-24`). Conversion, done once:

> `cells/s = Speed ÷ 1024 × 16.667 = Speed × 0.016276`

| Thing | `Speed` | cells/s | Ticks to cross 50 cells (`51200 / Speed`) | Seconds (`× 0.06`) |
|---|---|---|---|---|
| Tomahawk/Kalibr tier (proposed) | 350 | 5.70 | 146 | **8.78 s** |
| A-10 (`Aircraft.Speed`, `aircraft-america.yaml:471`) | 390 | 6.35 | 131 | 7.87 s |
| `HIMARSMissile` (`vehicles-america.yaml:1224`) | 500 | 8.14 | 102 | 6.14 s |
| F-16 / MiG-29 (`aircraft-america.yaml:604`, `aircraft-russia.yaml:614`) | 525 | 8.54 | 97 | 5.85 s |
| `IskanderMissile` (`vehicles-russia.yaml:1128`) | 600 | 9.77 | 85 | 5.12 s |
| Kinzhal tier (proposed) | 1200 | 19.5 | 42 | 2.56 s |
| Kinzhal tier, upper (proposed) | 2000 | 32.6 | 25 | **1.54 s** |

Shipped map sizes, so "50 cells" is grounded: `arena-tank-duel` 66×34, `nuclear-winter-ww3` 102×72, `polar-disorder-ww3` 98×98, `river-zeta-ww3` 98×82, `seventh-woods-ww3` 123×114, `shellmap-open-field` 92×62, `siberian-pass-ww3` 97×67, `twin-rivers-ww3` 128×128, `woodland-warfare-ww3` 98×98, `x-lake-ww3` 130×130 (each map's `MapSize:`). Edge-to-centre is 33–65 cells; 50 is the median case.

**Recommendation for the visible contrast the user asked for: Kinzhal `Speed: 2000` with `TerminalSpeed: 2400`, cruise tier `Speed: 350` with no terminal phase.** That is 5.7× apart, and the cruise tier is slower than every aircraft in the game while the Kinzhal is nearly four times the fastest — legible without a HUD.

Two cautions on this field:

- **`Acceleration > 0` changes the arithmetic.** With acceleration, flight ticks come from a simulation loop at construction (`BallisticMissileFly.cs:64-79`), not `hDist / Speed`. For a missile that is already at cruise when it enters the map, set `Acceleration: 0` and `InitialSpeedPercent: 100` so the table above holds exactly.
- **Speed modifiers do not apply.** See §4.2.

If instead the delivery is `NukePower`-shaped, the perceived-speed field is `FlightVelocity` (WDist/tick, same conversion) — but it also sets the start altitude, so it cannot be tuned independently of the drop height.

## 1.8 Precision

**On the `BallisticMissile` path, a zero-scatter direct hit is the default and requires no field.** `Inaccuracy` lives on projectile infos (`Bullet`, `Missile`, …) — e.g. `TankRound` `Inaccuracy: 0c512` (`weapons-ballistics.yaml:847`), `ArtilleryRound` `Inaccuracy: 2c0` (`:882`), `GradRockets` `Inaccuracy: 4c0` (`:976`). A `BallisticMissile` actor has no projectile; `BallisticMissileFly` sets `pos = targetPos` exactly and the warhead fires there.

**`NukePower` is likewise exact** — `descendTarget = targetPos` (`NukeLaunch.cs:77`), no scatter term anywhere in the class.

So "strikes with precision at a fixed target" is free on both paths. **The inverse is the work item:** a cruise-missile tier that should be *less* accurate has nowhere to express that today. Three options, cheapest first: (a) offset the `Target` position by a seeded random `WVec` in the power trait before `bm.Target = …` — ~4 lines, and it is where an `Inaccuracy` field would naturally live; (b) accept perfect accuracy on both tiers and differentiate on speed, blast radius and cost alone; (c) deliver the cruise tier as a `Missile`-projectile weapon fired from a hidden actor, which brings `Inaccuracy` for free and drags in the whole guidance stack documented in `DOCS/reference/missiles.md` §7 — not recommended for a support power.

---

# Q2 — Warheads, survivability, cost

## 2.0 How damage is actually computed — the pipeline everything below uses

Re-derived from `DamageWarhead.InflictDamage` (`DamageWarhead.cs:230-302`), in order:

1. `damage = Damage` (plus the random terms, unused by the weapons here).
2. `effectiveThickness = Armor.Thickness × ArmorDirectionPercent / 100` (`:242-249`). For `TopAttack: true` this is `Distribution[3]`; otherwise it is computed from the impact orientation.
3. `damage = ApplyPenetration(damage, Penetration, effectiveThickness)` — **unchanged if `Penetration ≥ effectiveThickness` or thickness is 0, otherwise `damage × Penetration / thickness`** (`:128-134`). `Penetration` defaults to **1**, so a warhead that omits it delivers `damage / thickness` — 0.4% against a 280 mm hull.
4. `modifiedDamage = ApplyPercentageModifiers(damage, [falloff%, …, Versus%])` (`:299`).

`Falloff` is **piecewise linear, not stepped**: the table is tabulated at `i × Spread` and `int2.Lerp`-interpolated between adjacent entries (`SpreadDamageWarhead.cs:52`, `:144-157`). Total reach is `(Falloff.Length − 1) × Spread`, and the distance is measured **from the victim's hitshape edge**, in 3D. Values above 100 amplify. (`missiles.md` §6 states the same and is correct.)

For `ShockwaveDamageWarhead` the reach is `min(MaxRadius, (Falloff.Length − 1) × Spread)` (its own `[Desc]`, `ShockwaveDamageWarhead.cs:80-86`), and the wave takes `StartDelay + WaveSpeed × r` ticks to reach radius *r*.

## 2.1 What `Atomic` is, structurally

`mods/ww3mod/rules/weapons/weapons-superweapons.yaml:28-385` — **358 lines**, 29 warheads, hand-authored with phase headers. (The recon says "359 lines" and `28-386`; `EmpBomb:` opens at `:387`, so the block is `:28-385`. Immaterial, corrected for citation hygiene.) `ValidTargets: Ground, Trees, Water, Underwater, Air` (`:29`).

| Phase | Warheads | Line |
|---|---|---|
| 0 — flash | `FlashPaletteEffect` Duration 30 (**1.80 s**), `FlashType: Nuke` | `:33-35` |
| 0 — shake | four `ShakeScreen`: Intensity 80/40/15/5 at Delay 0/25/75/150, Duration 25/50/75/60. Coverage 0→210 ticks = **12.6 s** | `:39-53` |
| 0 — fireball | `CreateEffect` `nuke_large` at `ScalePercent: 300`, `ZOffset: 4096`, `kaboom1.aud`, `VisibleThroughFog: true` | `:56-65` |
| 1 — vaporise | `SpreadDamage` **200000**, Pen 5000, Spread `3c0`, Falloff `100,100,100,50` ⇒ reach **9 cells**, `DamageTypes: ElectricityDeath` | `:76-85` |
| 1 — trees | `SpreadDamage` 200000, Spread `1c0`, `ValidTargets: Trees`, `FireDeath` | `:88-96` |
| 1 — heat pulse | `SpreadDamage` 100, **Pen 1**, Spread 512, Falloff starts at **10000** (100×), 20 entries ⇒ reach **9.5 cells**, `Duration: 5 Modulus: 2` | `:99-107` |
| 1 — thermal | `ThermalRadiation` 3000, Pen 300, Spread `1c0`, 15-entry falloff ⇒ reach **14 cells**, `RadiationDuration: 50` / `DamageInterval: 3` ⇒ **17 pulses over 3.0 s**, Versus L120/M60/H30/C20 | `:116-132` |
| fire | 10 × `GrantExternalCondition onfire`, Range `10c0`→**`28c0`**, Delay 1→50, Duration 750→300, on `Structure, Infantry` | `:145-236` |
| fire (trees) | 3 more, Range `6c0`/`10c0`/`18c0` | `:249-275` |
| 2 — EMP | `GrantExternalCondition empdisable`, Range `15c0`, Duration 375 = **22.5 s**, `InvalidTargets: Infantry, Trees` | `:279-287` |
| 3 — blast | `ShockwaveDamage` **100000**, Pen 5000, Spread `1c0`, 30-entry falloff peaking at **500** (5×), `MaxRadius: 30c0` ⇒ reach **29 cells**, `WaveSpeed: 7` ⇒ the front reaches 29 cells at tick `3 + 7×29 = 206` = **12.4 s**, Versus L80/M60/H40/C30 | `:293-311` |
| 4 — suppression | 5 × `GrantExternalCondition suppression-1`, Range `8c0`→**`32c0`**, Amount 10→1, Delay 38→213 | `:316-364` |
| 5 — scarring | `LeaveSmudge` Crater size 3, Scorch size 5, Scorch size 7 at 60% chance | `:367-385` |

**Every damaging warhead carries `AirThreshold: 10c0`, and that is load-bearing.** The default is `WDist(128)` = 0.125 cells (`Warhead.cs:45`). Because the nuke detonates 6.25 cells up (§1.1), a warhead left at the default would resolve the impact cell's target types as *Air* rather than *Ground* and do nothing to the ground. **Anyone copying `NukePower` with a non-zero `DetonationAltitude` must raise `AirThreshold` on every warhead of the new weapon, or the weapon silently does nothing.** This is the single most expensive thing to get wrong when cloning `Atomic`.

## 2.2 `Atomic` in numbers

Computed by applying §2.0's pipeline to the four damaging warheads (vaporise + blast + 17 thermal pulses + 3 heat pulses). **Distances are slant range from the burst point, and the burst is 6.25 cells above the terrain, so the leftmost reachable column for a ground target is 6.25c — the "0c" figures are never experienced.**

| Target (HP · armour/thickness) | 6.25c | 8c | 10c | 12c | 15c | 20c | 25c |
|---|---|---|---|---|---|---|---|
| Abrams M1A2 (28,000 · Heavy/700) | 266,255 | 184,119 | 32,051 | 18,000 | 7,200 | 800 | 0 |
| T-90 (24,000 · Heavy/280) | 266,612 | 184,306 | 32,153 | 18,000 | 7,200 | 800 | 0 |
| Iskander TEL (10,000 · Light/15) | 344,448 | 237,224 | 64,612 | 36,000 | 14,400 | 1,600 | 0 |
| Rifleman (200 · Kevlar/0) | 382,061 | 263,032 | 80,510 | 45,000 | 18,000 | 2,000 | 0 |
| Logistics Center (60,000 · Concrete/0) | 247,429 | 171,216 | 24,102 | 13,500 | 5,400 | 600 | 0 |
| MSLO silo (135,000 · Concrete/2000) | 247,051 | 171,017 | 24,000 | 13,500 | 5,400 | 600 | 0 |

**Kill radii**, as slant range and as the ground radius a player sees (`√(R² − 6.25²)`):

| Target | slant | **ground** |
|---|---|---|
| Abrams M1A2 | 10.50c | **8.44c** |
| T-90 | 11.00c | **9.05c** |
| Iskander TEL | 15.83c | **14.54c** |
| Rifleman | 24.00c | **23.17c** |
| Logistics Center | 8.99c | **6.46c** |
| MSLO silo | 8.86c | **6.28c** |

*(The ground figure is a lower bound: a tall hitshape shortens the vertical leg. `LOGISTICSCENTER`'s hitshape has `VerticalTopOffset: 3072` — `structures.yaml:405` — so its true vertical separation from the burst is ~3.25c, not 6.25c, and its real ground radius is closer to 8.4c. Vehicles and infantry are short enough that the table stands.)*

**Plus fire, which is the largest single contributor against structures and is not in the table above.** `ChangesHealth@BurnDamage` on buildings is `PercentageStep: -1, Step: -100, Delay: 10` (`structures.yaml:180-185`): every 10 ticks a building loses `100 + 1% of MaxHP` (`ChangesHealth.cs:81-87`). On a 60,000 HP building that is 700 per 10 ticks = **70/tick**. `ExternalCondition@BurnDamage: TotalCap: 1` (`:186-188`) means only one `onfire` stack is ever held, so the duration is whichever Fire warhead reaches that building first: `Warhead@Fire1` (Range `10c0`, Duration 750) inside 10 cells, down to `Warhead@Fire10` (Range `28c0`, Duration 300) at the rim. So a building at 10 cells burns for 750 ticks = **45 s and takes 52,500** — nearly its whole health bar — and one at 28 cells burns 300 ticks for **21,000**.

**What one nuke does to a typical base, in one sentence:** everything inside ~8 ground cells ceases to exist in the first second; every vehicle out to ~9 cells and every infantryman out to ~23 cells dies; every structure and soldier out to **28 cells** catches fire and loses 21,000–52,500 HP over the following 18–45 s; everything within 15 cells is EMP'd for 22.5 s; and the screen shakes for 12.6 s. On a 98×98 map that is a **56-cell-diameter fire zone**, or over half the map's width.

## 2.3 Where a conventional missile warhead should sit

Direct-hit (r = 0) totals, same pipeline. These are the warhead's own numbers with no falloff and, for `TargetDamage`, 100% centre proximity.

| Weapon (`file:line`) | Abrams 28,000 | T-90 24,000 | Rifleman 200 | Logistics Ctr 60,000 | MSLO 135,000 | Splash reach |
|---|---|---|---|---|---|---|
| **`Atomic`** at 6.25c (`weapons-superweapons.yaml:28`) | 266,255 | 266,612 | 382,061 | 247,429 | 247,051 | 29c blast, 28c fire |
| **`IskanderExplosion`** (`weapons-explosions.yaml:521`) | **62,800** | 62,800 | 70,000 | 61,000 | 61,000 | 4c spread, 6c shock |
| **`HIMARSExplosion`** (`:573`) | 41,300 | 41,300 | 45,500 | 40,250 | 39,562 | 3c spread, 2.5c shock |
| `TankRound` (Abrams) (`weapons-ballistics.yaml:838`) | 20,004 | 20,010 | 23,000 | 23,000 | 8,001 | 0.25c |
| `ArtilleryRound` (`:871`, `TopAttack`) | 15,004 | 15,010 | 18,000 | 18,000 | 7,501 | 0.25c |
| `M270Rockets` (`:1086`) | 15,000 | 15,000 | — | — | — | — |
| `ATGM` / `WGM` / `Ataka` / `Hellfire` (`weapons-missiles.yaml:26/99/155/273`) | 10,057 | 10,142 | 12,000 | 12,000 | 4,020 | 0.17c |
| `GradRockets`, per rocket × 40 (`weapons-ballistics.yaml:962`) | 2,143 | 5,360 | 7,000 | 7,000 | 750 | 0.4c |
| `RPG` (`:516`) | 6,000 | 6,000 | — | — | — | — |
| `RocketPods` (`:910`, Pen 50) | 357 | 892 | — | — | — | 0.25c |

`ValidTargets` for the two ballistic explosions is `Ground, Trees, Water` with `InvalidTargets: Air` on the point warhead (`weapons-explosions.yaml:523`, `:542`). `Atomic` adds `Air` and `Underwater` (`:29`).

**Design anchor.** `IskanderExplosion` is the biggest conventional warhead in the mod and does **2.24× an Abrams' health** on a direct hit, in a 6-cell blast, delivered by a 6000-credit launcher that carries two rounds and cannot rearm (`vehicles-russia.yaml:996`, `:1064`, `:1077-1080`). A Kinzhal that fires from off-map, cannot be reached, and cannot be killed on the ground should not be *stronger* than that per shot. Concretely: **`TargetDamage` 50,000–60,000, `Penetration` 2500, `Spread` 512, plus a 5–7-cell `ShockwaveDamage` around 12,000** puts it exactly at Iskander parity, and the cruise tier at roughly `HIMARSExplosion` (36,000) with a wider, slower shock. Above ~80,000 point damage the weapon stops reading as a missile: `Atomic`'s own vaporise warhead is 200,000, and the gap between 80k and 200k is where "tactical nuke" lives — a category the user's own design note (`WORKSPACE/archive/plans/260324-nukes.md`) has not settled.

## 2.4 Survivability anchors

| Actor | `file:line` | HP | Armour / Thickness | Distribution | `TargetTypes` |
|---|---|---|---|---|---|
| `abrams` — Abrams M1A2 | `vehicles-america.yaml:464` | 28,000 | Heavy / **700** | 100,40,15,10,10 | Ground, Vehicle, Heavy |
| `t90` — T-90 | `vehicles-russia.yaml:289` | 24,000 | Heavy / **280** | 100,60,40,15,15 | Ground, Vehicle, Heavy |
| `iskander` — TEL | `vehicles-russia.yaml:987` | 10,000 | Light / 15 | 100,80,80,80,60 | Ground, Vehicle, Light |
| `^Infantry` → `^Soldier` (every rifleman) | `infantry.yaml:34-35`, `:175-176` | **200** | **Kevlar / none (0)** | — | Ground, Infantry, Disguise |
| `LOGISTICSCENTER` | `structures.yaml:392` | 60,000 | Concrete / none | — | Ground, C4, DetonateAttack, Structure (`^Building`) |
| `^TechBuilding` (civilian) | `structures.yaml:140` | 60,000 | Concrete / none | — | **NoAutoTarget**, C4, DetonateAttack |
| `AFLD` — Airfield | `structures.yaml:648` | 30,000 | Concrete / none | — | Structure |
| `HPAD` — Helipad | `structures.yaml:580` | 22,500 | Concrete / none | — | Structure |
| `PBOX` — Bunker | `structures-defenses.yaml:176` | 60,000 | Concrete / **300** | — | — |
| `MSLO` — Nuclear Missile Silo | `structures-defenses.yaml:1107` | **135,000** | Concrete / **2000** | — | Structure, Concrete, C4 |
| **`SUPPLYROUTE`** | `structures.yaml:222` | 75,000 | **Indestructable / none** | — | **NoAutoTarget** |

**Infantry carry no armour thickness at all**, so `Penetration` never bites on them and a warhead's raw `Damage` lands in full. That is why every figure in the Rifleman column of §2.3 is the largest in its row.

**A Kinzhal at Iskander parity (≈62,800 point damage) kills:** an Abrams in one (2.24×), a T-90 in one (2.62×), an Iskander TEL in one (6.3×), any infantryman it lands near, a Logistics Center in one (1.02× — *marginal*, and it would survive at low HP against any modifier that reduces it), an Airfield or Helipad in one, and an `MSLO` in **three**.

## 2.5 The Supply Route cannot be hit, by anything

`SUPPLYROUTE` carries `Targetable: TargetTypes: NoAutoTarget` and nothing else (`structures.yaml:296-297`). `Warhead.IsValidTarget` requires `ValidTargets.Overlaps(targetTypes)` (`Warhead.cs:55-58`), and:

```
grep -rn "ValidTargets:.*NoAutoTarget" mods/ww3mod/rules/weapons/   → no matches
grep -rn "Indestructable"            mods/ww3mod/rules/weapons/     → no matches
```

So no warhead in the mod is valid against it, and the `Indestructable` armour type contributes nothing (it has no `Thickness`, and appearing in no `Versus` block means `DamageVersus` returns the damage unmodified — `DamageWarhead.cs:101-109`). `DangerFieldKernelTest.cs:490` independently records `Indestructable` as one of the armour types that is "unlisted" by every weapon.

**Correction to file:** `engine/OpenRA.Mods.Common/Tournament/WinRules/TimeOrSrCaptureWinRule.cs:49` says *"SR is indestructible by design (Armor: Indestructable)"*. The conclusion is right and the reason is wrong — delete the `Armor:` clause and someone will eventually "simplify" `Indestructable` away and make the SR killable. It should read `TargetTypes: NoAutoTarget`.

**Consequence for the proposal:** "can a missile strike take out the enemy Supply Route" is answerable now, statically, and the answer is **no, and not by a bigger warhead either.** A missile power that is meant to threaten the SR must go through `SupplyRouteContestation` (`structures.yaml:303-316`) or change the SR's `TargetTypes` — a decision far outside a support-power proposal.

## 2.6 Cost anchors

Every `Valued: Cost:` in `mods/ww3mod/rules/`, top and bottom:

| Cost | Actor | Buildable | `file:line` |
|---|---|---|---|
| **50,000** | `MSLO` Nuclear Missile Silo | yes (gated `~disabled`) | `structures-defenses.yaml:1107` |
| 6,000 | `iskander`, `HIMARS`, `MIG`, `MI28`, `HELI`, `FROG`, `F16`, `A10` | yes | `vehicles-russia.yaml:987` etc. |
| 4,000 | `HIND` Mi-24 | yes | `aircraft-russia.yaml:90` |
| 3,000 | `littlebird`, `LOGISTICSCENTER`, `LCCV`, `HSAM` | yes | — |
| 2,500 | `abrams`, `strykershorad` | yes | `vehicles-america.yaml:464` |
| 2,400 | `t90` | yes | `vehicles-russia.yaml:289` |
| 100 | `^E3` Rifleman, `^AR`, `^E2` | yes | `infantry.yaml:1224` |
| **50** | `^E1` Conscript — cheapest infantry | yes | `infantry.yaml:1147` |
| 50 | `IskanderMissile` (a round, not a unit) | no | `vehicles-russia.yaml:1116` |
| 30 | `HIMARSMissile` | no | `vehicles-america.yaml:1212` |

**`MSLO Cost: 50000` verified** — the recon is right. Nothing else in the mod comes within 8×.

Price conversions a proposal can quote directly:

- **1 Abrams = 2,500.** So `MSLO` is **20 Abrams**, or 500 conscripts, or 8.3 Iskander launchers.
- **A single Iskander round costs the player 3,000** (6,000 launcher ÷ 2 rounds, non-rearmable — `vehicles-russia.yaml:1064`, `:1077-1080`), i.e. **1.2 Abrams per missile**, before the launcher's own survival risk.
- That gives the cleanest anchor for a strike power: **an off-map Kinzhal that cannot be intercepted on the ground should cost more than 3,000 per shot**, because it is strictly better than the Iskander round it matches. 4,000–6,000 per shot is "about two tanks"; anything under 3,000 is a strict upgrade at a discount.

**The `MSLO` price tells you which model the mod already assumes.** 50,000 is a once-a-game purchase in an economy where the most expensive unit is 6,000; that is a *building that then fires free* (`ChargeInterval`), not a *per-shot* price. A per-shot Powers-menu missile is a different economic object and cannot inherit `MSLO`'s number.

---

# Q3 — Which balance method was used, and its limits

**Method used: arithmetic over shipped YAML, with the damage pipeline re-derived by reading `DamageWarhead.cs`, `SpreadDamageWarhead.cs` and `ShockwaveDamageWarhead.cs` rather than assumed.** No sim output appears in this document.

**The combat-sim was not used, and it is not usable for this question without a build.** Three independent blockers, each verified:

1. **The dashboard is not built.** `tools/combat-sim/` contains `src/` and `package.json` but **no `build/`**. `BALANCE.md:34-40` invokes `node build/index.js`. That needs `npm install` + `tsc`.
2. **`dump-stats.sh` refuses without a compiled engine.** `tools/combat-sim/scripts/dump-stats.sh:21-24` exits 1 with *"OpenRA.Utility.dll not found — run 'make' first"*. `ls engine/bin/OpenRA.Utility.dll` → **no such file**. I am not permitted to build.
3. **The committed `data/stats.json` is stale in exactly the fields this question needs, and using it would have reproduced a bug the tree has already fixed.** Its `_meta.generated_at` is **`2026-08-22T01:37:18Z`**. It reports `iskanderexplosion` → `TargetDamageWarhead` with **`penetration: 1`** and **`spread: null`**; the live YAML at `weapons-explosions.yaml:526-537` sets `Penetration: 2500` and `Spread: 512`, and its own comment dates that fix **260827** — five days after the dump. Had I taken the sim's numbers, an Iskander direct hit on an Abrams would have computed as `54000 × 1 / 700 = 77` damage against 28,000 HP instead of 62,800 — a factor of **815**, and precisely the *"the Iskander hit a tank directly and it didn't get destroyed"* symptom `missiles.md` §10 records as fixed.

**A fourth limit, which persists even after a rebuild.** `DumpBalanceJsonCommand.cs:130-131` emits `spread` and `falloff` **only for `SpreadDamageWarhead`**:

```csharp
spread  = wh is SpreadDamageWarhead sd  ? (int?)sd.Spread.Length : null,
falloff = wh is SpreadDamageWarhead sd2 ? sd2.Falloff : null,
```

`ShockwaveDamageWarhead` and `ThermalRadiationWarhead` are siblings of `SpreadDamageWarhead` under `DamageWarhead`, not subclasses, so their `Spread` and `Falloff` are structurally absent from the dump. Confirmed in the committed file: `atomic`'s shockwave shows `spread: null, falloff: []` while the YAML declares `Spread: 1c0` and a 30-entry table (`weapons-superweapons.yaml:299-301`). **Every blast-radius question about the nuke, the Iskander or the HIMARS is therefore unanswerable from the sim even when it is current** — the sim would report a 29-cell nuclear shockwave as having no radius at all. That is worth fixing regardless of this proposal; it is a two-line change at `:130-131`.

### Limits of the method I did use

- **Single-application damage only.** The tables are one warhead application per victim. They exclude the fire DoT (quantified separately in §2.2) and any suppression, EMP or crew-casualty second-order effect.
- **`ArmorDirectionPercent` assumed frontal (100%).** For a splash impact the engine computes facing from `args.ImpactPosition`, which on the nuke path is broken (§4.1). Against an Abrams with `Distribution: 100,40,15,10,10` a rear or top hit would take effective thickness to 70–105, which changes nothing here because every warhead in question already has `Penetration` ≥ 1500.
- **`TargetDamage` proximity assumed 100%.** `CenterProximityPercent` falls linearly to 0 at the hitshape's corner distance (`missiles.md` §6), so a hit on the nose of a long hull can be ~10% of the figures in §2.3. The direct-hit column is a ceiling, not an average.
- **Falloff distance is edge-to-impact, not centre-to-impact.** Large hitshapes are effectively closer to the burst than their centres suggest.
- **Nothing here is verified in the engine.** No autotest ran. Every number is a prediction from the YAML and the C# that reads it. §5 lists the runs that would confirm the load-bearing ones.

---

# 3. Corrections and additions to `WORKSPACE/recon/powers-and-preloaded-transports.md`

Read at `main @ 2c8488ef`. §1.7 is broadly accurate — `MSLO` values, art and audio resolution, the `ChargeInterval: 10` = 0.6 s reading, and the `Creeps`-owned silo on `nuclear-winter-ww3` (verified: `map.yaml:1146-1148`, `Actor436: mslo`, `Owner: Creeps`, `Location: 50,35` — the only `mslo` on any shipped map) all check out. Four things to fix or add:

1. **§1.7's citation `NukePower (:1134-1167)` over-reaches.** The `NukePower:` block is `:1134-1163`; `:1164-1167` are `SupportPowerChargeBar`, `MustBeDestroyed` and `WithSupportPowerActivationAnimation`. Cosmetic, but the whole point of the citation discipline is that a reader can open the range and see only what was claimed.

2. **§1.7 does not mention `PauseOnCondition: disabled` (`:1136`), and that omission is correct — but for a reason worth writing down, because §1.2 of the same document makes `disabled` sound like a defect.** On the **Player** actor `disabled` is genuinely ungranted, so §1.2's lint-error claim stands there. On `MSLO` it *is* granted: `^BuildingAffectedByEMP` (`structures.yaml:207`) carries `GrantCondition@EMP: RequiresCondition: empdisable → Condition: disabled` (`:218-220`), and `^Building` inherits it (`:70`). **MSLO's `PauseOnCondition: disabled` is a live, correct EMP gate — the nuclear silo cannot launch while EMP'd.** A reader who applies §1.2's reasoning uniformly would delete a working mechanic. Ten defence armaments in `structures-defenses.yaml` share the same live pattern.

3. **§1.7's framing "much further along than the brief assumes" is right about the nuke and misleading about missiles generally.** It concludes that what is missing is "the design conversation", which is true for `Atomic` — but it does not mention that `NukePower`'s delivery is structurally vertical-only (§1.2 above), nor that the mod already ships a complete actor-based ballistic-missile stack (`BallisticMissile` + `BallisticMissileFly` + `MissileSpawnerMaster` + two tuned missiles) that is the actual prior art for a missile *strike power*. Anyone costing a Kinzhal from §1.7 alone would start from `NukePower` and hit the wall in §1.2.

4. **Two dead-config findings to add alongside §1.8's `LobbyChargeIntervalId`.** `AirstrikePowerInfo.QuantizedFacings` and `AirstrikePower`'s non-reading of `order.ExtraData` are the same *class* of finding — config that survived a rewrite of its only consumer — and they were both killed by `a20c8a82`, not by the upstream merge §1.8 blames for the cooldown. §1.6 above.

---

# 4. Incidental defects found, neither previously filed

## 4.1 The nuke computes armour facing from the map origin — VERIFIED

**Symptom (predicted, not observed — no game was run):** directional armour is applied incorrectly to every actor splashed by a nuclear detonation. **Cause: verified by reading the code.**

`NukeLaunch.Explode` constructs its own `WarheadArgs` and sets four fields (`NukeLaunch.cs:143-150`):

```csharp
var warheadArgs = new WarheadArgs { Weapon = weapon, Source = target.CenterPosition,
                                    SourceActor = firedBy.PlayerActor, WeaponTarget = target };
weapon.Impact(target, warheadArgs);   // :152 — the 2-arg overload, NOT ImmediateImpactArgs
```

`ImpactPosition` is not among them, so it stays `WPos.Zero`. The projectile-less helper that exists precisely to fix this — `WeaponInfo.ImmediateImpactArgs`, which sets `ImpactPosition = target.CenterPosition` at `WeaponInfo.cs:307` with a comment naming the bug — is **not on this path**. `SpreadDamageWarhead.DoImpact` then computes, for every victim at non-zero falloff distance (`:117-122`):

```csharp
var towardsTargetYaw = (victim.CenterPosition - args.ImpactPosition).Yaw;
var impactAngle      = Util.GetVerticalAngle(args.ImpactPosition, victim.CenterPosition);
impactOrientation    = new WRot(WAngle.Zero, impactAngle, towardsTargetYaw);
```

— i.e. the bearing from the **map corner**, which feeds `ArmorDirectionPercent` (`DamageWarhead.cs:142-215`) and therefore `effectiveThickness`.

**Scope is narrower than `missiles.md` §10 instance 1.** The damage *falloff* is unaffected: `DamageWarhead.DoImpact(in Target, args)` passes `target.CenterPosition` as the separate `pos` argument (`:94`), and that is what distances are measured from. `Atomic` has no `TargetDamage` warhead, so the negative-damage-heals failure mode cannot occur here. What is wrong is only the hit *direction* — which on `Atomic`'s warheads, all of which have `Penetration` 5000 against a maximum thickness of 2000, changes nothing today. **It is latent, not live.** It becomes live the moment anyone gives a `NukePower` weapon a `TargetDamage` warhead or a `Penetration` below the thicknesses on the map — which is exactly what a conventional missile power built on `NukePower` would do.

**One-line fix:** replace the hand-built args at `NukeLaunch.cs:143-150` with `WeaponInfo.ImmediateImpactArgs(weapon, target, firedBy.PlayerActor)`, then re-set `Source`.

## 4.2 `ISpeedModifier` is dead on the ballistic-missile flight path — VERIFIED

`BallisticMissile.MovementSpeed` applies `speedModifiers` (`BallisticMissile.cs:230-233`, collected at `:212`), but `BallisticMissileFly` never calls it: it reads `sbm.Info.Speed` raw at `:52`, `:220` and `:223`. So a speed-modifying condition on a missile actor is silently ignored, and `MovementSpeed`/`speedModifiers` exist only for `EstimatedMoveDuration` (`:351-355`). Not a bug today — nothing puts an `ISpeedModifier` on a missile — but it is a trap for exactly the kind of tuning a two-tier missile power would attempt ("grant a slow condition to the cruise tier").

---

# 5. YAML files touched, and runs requested

## Files touched

**None.** This is a read-only research pass. The only file added is this document, `WORKSPACE/recon/powers-missile-delivery.md`. No `mods/ww3mod/**` file, no engine source, no scenario was modified. No YAML lint was run (per standing constraint); nothing in this change can affect it.

## Runs I want the manager to perform

Ordered by value. Each states the exact command and the exact result that would count as the answer.

**R1 — Confirm the nuke's real detonation altitude and timing.** This is the one prediction that would falsify the most of §2.2 if wrong: the whole "the 0c column is never experienced" argument rests on the burst being 6.25 cells up at tick ~64.

> No scenario exists. It would need writing: an `MSLO` with `Buildable.Prerequisites` cleared, `ChargeInterval` left at 10, and a ring of `abrams` at 6, 8, 10, 12, 15 and 20 cells from the aim point, then one `NukeMissile` order.
>
> **Answer = the surviving/dead pattern.** §2.2 predicts: dead at 6, 8 and 10 cells; alive at 12 cells with **10,000/28,000 HP** (18,000 damage); alive at 15 with 20,800; untouched at 20. If instead the 12-cell Abrams dies, the burst is lower than 6.25 cells and every ground radius in §2.2 is ~2 cells too small.

**R2 — Confirm `IskanderExplosion` one-shots an Abrams.** This is the anchor the whole warhead recommendation hangs on, and it is cheap.

> `./tools/autotest/run-test.sh test-balance-tank-1v1` will not answer it. What is needed is one force-fire of an `iskander` at a stationary `abrams` at, say, 20 cells.
>
> **Answer = whether the Abrams dies to one round.** §2.3 predicts 62,800 damage against 28,000 HP — a comfortable one-shot. If it survives, `Penetration: 2500` is not reaching (check `ArmorDirectionPercent` on the actual impact facing) and the §2.3 table is wrong by up to 4×.

**R3 — Rebuild the combat-sim, then re-dump.** Not to answer this question — §3 explains why it cannot — but so the *next* balance question is not blocked and so the staleness in `data/stats.json` stops being a trap.

> ```
> ./make.ps1 all
> cd tools/combat-sim && npm install && npx tsc
> ./tools/combat-sim/scripts/dump-stats.sh
> ```
>
> **Answer = `_meta.generated_at` moves to today's date, and `weapons.iskanderexplosion.warheads[1].penetration` reads `2500` rather than `1`.** If it still reads 1 after a fresh dump, the fix at `weapons-explosions.yaml:533` is not being loaded, and that is a much larger problem than a stale file.

**R4 — Nothing else.** In particular, do **not** run a batch or a tournament for this. Nothing in this document is a balance verdict; it is an inventory of what the shipped numbers are.
