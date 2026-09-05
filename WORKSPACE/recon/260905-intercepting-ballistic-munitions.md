# Intercepting ballistic munitions — costing

**Researched at `wt/counter-intercept` @ `95bdffb2`.** `git status -sb` → `## wt/counter-intercept`, clean.
The branch has no upstream, so `git rev-list --count HEAD..@{u}` cannot run; against `origin/main` the
worktree is **5 commits behind** (`origin/main` @ `e071a500`, `git rev-list --count HEAD..origin/main` → 5).

**Those 5 commits do not touch anything cited here.** `git diff --stat 95bdffb2..origin/main` is four files,
all under `WORKSPACE/`: `ai-bench/README.md`, `ai-bench/RUNBOOK-260905.md`, `ai-bench/SPEC.md`,
`pipeline/items/56-supply-truck-delivery.md`. No `rules/`, no `engine/`. Every `file:line` below therefore
reads identically at `e071a500`.

**Static analysis only.** No game launch, no autotest, no screenshot, no YAML lint, no build. Arithmetic is a
line-by-line port of the engine's own functions, not an approximation — see §9 for what that does and does
not entitle me to claim.

**Timestep 60 ms ⇒ 16.667 ticks/s** (`mods/ww3mod/mod.yaml:358` `DefaultSpeed: default`, `:382` `Timestep: 60`).
`seconds = ticks × 0.06`. Not 25 tps.

---

## 0. Headline

1. **The Iskander's and HIMARS's in-flight missiles are interceptable today — by construction. Nothing a
   player can build is allowed to shoot at them.** This is a pure availability gate, not missing machinery.
   Six weapons list `ICBM`; five actors mount one; **every one of those five carries
   `Buildable.Prerequisites: ~disabled`.** §1, §4.
2. **The brief's premise about the launcher is wrong.** The Iskander does not fire its warhead through
   `Explodes`. Its armament fires `IskanderTargeter`, a zero-damage `InstantHit` dummy
   (`weapons-missiles.yaml:380`), and `MissileSpawnerMaster` (`vehicles-russia.yaml:1101`) spawns a real
   world **actor**, `IskanderMissile`. The `Explodes` traits on the launcher are cook-off on its own death.
   HIMARS is identical (`vehicles-america.yaml:1187`). §1.1.
3. **No engine work is required, and the thing that genuinely cannot be shot down is not in the way.**
   `IProjectile : IEffect` (`WeaponInfo.cs:71`) — projectiles are effects, not actors, and `AutoTarget`
   never touches `World.Effects`. But WW3MOD's ballistic munitions are not projectiles. §1.4.
4. **Smallest honest change that makes them interceptable: one line of YAML.** Flip
   `structures-defenses.yaml:807` from `~disabled` to a real prerequisite and the `SAM` works, today,
   with no retune. §2.
5. **`20mm_CRAM` cannot reliably hit an Iskander, but not for the reason on file, and not by eleven
   cells.** Bullets in this engine **do** lead — `Armament.cs:636`, live since `9e7e3902` (2023-06-20).
   With the lead applied the crossing miss is **1.2–1.8 cells**, against a 0.42-cell hit radius. Still a
   miss; an order of magnitude closer than recorded. Two in-tree assertions to the contrary are stale. §6.
6. **All three support-power missiles are deliberately unacquirable and are documented as such** —
   `Hypersonic` on the Kinzhal and the tac nuke, `Penetrator` on the GBU-57, target types no weapon lists.
   §3.
7. **The user's Kinzhal target — "shootable at, almost never hit" — is one word of YAML and needs no
   retune.** Change `TargetTypes: Hypersonic` to `ICBM` (`vehicles-russia.yaml:1174`, `:1177`). At Speed
   2000→2400 the Kinzhal is **twice the CRAM's muzzle velocity** and 2.5–3× the SAM interceptor's top
   speed; and its whole time inside a SAM ring is 18–36 ticks against a ~24-tick acquire-aim-fire chain.
   It gets shot at rarely and hit almost never, for free. §3.3.
8. **Recommendation: make the `SAM` real, and only the `SAM`.** §5.
9. **Two defects would ship the moment a prerequisite is flipped**, both pre-existing and neither
   currently reachable: `CRAM`'s armament has no `PauseOnCondition: !ammo` (it fires with an empty
   magazine), and `PrismLaserMaxFirepower` is an ICBM-capable weapon with no host. §7.

---

## 1. Q1 — are the Iskander's and HIMARS's missiles interceptable today?

**Yes by construction. No in practice.** Every gate in the targeting stack passes except availability.

### 1.1 The launcher spawns an actor, not a projectile

`iskander` (`vehicles-russia.yaml:987`) is buildable — `Prerequisites: ~player.russia, ~vehicles.russia,
~techlevel.high` (`:995`), cost 6000. Its firing chain:

```yaml
# mods/ww3mod/rules/ingame/vehicles-russia.yaml:1055-1059
	Armament@1:
		Weapon: IskanderTargeter
		LocalOffset: -150,120,150, -150,-100,150
		PauseOnCondition: !ammo-primary || empdisable || unit.docked || heavy-damage-attained
		AimingDelay: 40
# :1101-1104
	MissileSpawnerMaster:
		Actors: IskanderMissile
		LoadedCondition: loaded
		LaunchingCondition: firing
```

`IskanderTargeter` (`weapons-missiles.yaml:380-401`) is `Projectile: InstantHit` with a `TargetDamage`
warhead of `Damage: 50` and a `Versus` block zeroing every armour class — a dummy trigger. The payload is
the actor. `MissileSpawnerMaster.Attacking` sets `bm.Target` and calls `SpawnIntoWorld` on the same tick
(`MissileSpawnerMaster.cs:111-118`), so the missile is a live world actor from the moment the trigger pulls.

`HIMARS` (`vehicles-america.yaml:1069`, `Prerequisites: ~player.america, ~vehicles.america,
~techlevel.high` at `:1077`) is the same shape: `Weapon: HIMARSTargeter` (`:1137`),
`MissileSpawnerMaster: Actors: HIMARSMissile` (`:1187`).

The `Explodes` / `Explodes@Loaded` pair on each launcher gates on `loaded` / `!loaded` and fires when the
**launcher** dies — cook-off with a missile still on the rail. It is not the delivery path.

### 1.2 The missile is targetable for the whole flight, at both altitude bands

```yaml
# mods/ww3mod/rules/defaults.yaml:1074-1088
^ShootableMissile:
	Inherits@ExistsInWorld: ^ExistsInWorld
	Inherits@SpriteActor: ^SpriteActor
	Armor:
		Type: Light
	BallisticMissile:
		LaunchAngle: 128
		Speed: 110
		AirborneCondition: airborne
	Targetable@Ground:
		TargetTypes: ICBM
		RequiresCondition: !airborne
	Targetable@Airborne:
		TargetTypes: ICBM
		RequiresCondition: airborne
```

**Both bands carry `ICBM`, so there is no window in which the missile drops out of the target set.** The
`!airborne` / `airborne` split is a seam for differentiating the two states, and it is currently unused —
both sides are the same value. `IskanderMissile` (`vehicles-russia.yaml:1116`, `HP: 100`) and
`HIMARSMissile` (`vehicles-america.yaml:1212`, `HP: 50`) inherit it unmodified.

Coverage is in fact slightly wider than "the flight": `IskanderMissile` sets `LaunchRiseTicks: 60` and
`PostErectionWaitTicks: 20`, so `BallisticMissile.PreLaunchTicks` is **80 ticks = 4.8 s**
(`BallisticMissile.cs:85`) during which the missile actor is already in the world, targetable, sitting
erect on the launcher. `HIMARSMissile` sets neither, so its `PreLaunchTicks` is 0 and it flies immediately.

`Detectable` on `^ShootableMissile` is `Vision: 1, Radar: 1, Position: Ground` — the **lowest** vision tier
and radar-detectable, with the detection point projected down to terrain level
(`Detectable.cs:140-155`, `:168-183`). `Actor.CanBeViewedByPlayer` routes to that
(`Actor.cs:642-650`), which `AutoTarget.ChooseTarget` requires (`AutoTarget.cs:1389`). A missile eight cells
up is therefore detected against the ground beneath it, and radar coverage alone suffices. It carries no
`RevealsShroud`, so a defender with neither vision nor radar there sees nothing — correct, and not a defect.

### 1.3 Every targeting gate passes

| Gate | Requirement | Status |
|---|---|---|
| Victim has a target type | `^ShootableMissile` → `ICBM`, both bands | ✅ `defaults.yaml:1084`, `:1087` |
| Auto-target priority lists it | `^AutoTargetAirICBM` → `ValidTargets: Air, AirSmall, ICBM` | ✅ `defaults.yaml:747-750` |
| Weapon `ValidTargets` lists it | 6 weapons do (§4.1) | ✅ |
| Warhead `ValidTargets` lists it | `20mm_CRAM` `Warhead@Target`, `AACannon` `Warhead@Spread`, `SurfaceToAirMissile` `Warhead@Spread` all list `ICBM` | ✅ |
| Victim visible to the shooter | `Detectable` Vision 1 / Radar 1 | ✅ |
| Range | `Target.IsInRange` is **2D** — "Target ranges are calculated in 2D, so ignore height differences" (`Target.cs:201-202`) — so a missile at apex is at its ground-track distance | ✅ |
| **A player can build the shooter** | — | ❌ **all five hosts are `~disabled`** |

### 1.4 There is no anti-projectile path in this engine, and it does not matter

`public interface IProjectile : IEffect { }` (`WeaponInfo.cs:71`). `Bullet` and `Missile` are
`IProjectile, ISync` (`Bullet.cs:152`, `Missile.cs:211`) — not actors. `AutoTarget.cs` contains no
reference to `World.Effects`; every scan goes through `World.FindActorsInCircle`
(`AutoTarget.cs:1331`, `:1358`), which returns `IEnumerable<Actor>`. **A projectile cannot be shot down and
there is no seam to widen.**

This is irrelevant to the question asked. WW3MOD's ballistic munitions are actors. Intercepting them is
expressible today with **zero engine work**.

### 1.5 Flight profiles — a line-by-line port of `BallisticMissileFly.EstimateArcTicks`

Ported from `engine/OpenRA.Mods.Common/Activities/BallisticMissileFly.cs:72-102` (the static estimator) and
`:223-241` (the live tick loop); the two agree by construction — the estimator is extracted from the
activity so a caller reads "the activity's own arithmetic instead of keeping a second copy of it in step
by hand" (`:65-71`). Apex from `:61-62`, `arcPeakHeight = hDist × LaunchAngle.Tan() / (4×1024)`.

**40-cell shot (`hDist` 40960):**

| Missile | Speed / Accel / Terminal | Ticks | Seconds | Avg speed | Peak | Arc apex |
|---|---|---|---|---|---|---|
| `IskanderMissile` | 600 / 3 / 600 @10 | 156 | 9.36 | 4.27 c/s | 9.77 c/s | 8.0 cells |
| `HIMARSMissile` | 500 / 4 / 550 @7 | 138 | 8.28 | 4.83 c/s | 8.95 c/s | 5.3 cells |
| `GBU57Bomb` | 500 / 0 / 900 @40 | 81 | 4.86 | 8.23 c/s | 8.14 c/s | 1.9 cells |
| `TacNukeMissile` | 900 / 0 / 1100 @40 | 45 | 2.70 | 14.81 c/s | 14.65 c/s | 1.9 cells |
| `KinzhalMissile` | 2000 / 0 / 2400 @40 | 20 | 1.20 | 33.33 c/s | 32.55 c/s | 1.9 cells |

**The two shipped direct-fire munitions are the slowest things in the table and among the slowest movers in
the mod.** The Iskander launches from rest (`InitialSpeedPercent: 0`) and accelerates at 3 WDist/tick²; it
averages 4.3 cells/s over a 40-cell shot, which is slower than every aircraft in the game. It is an easy
target, not a hard one.

### 1.6 Engagement windows

Ticks the missile spends inside a defence ring **sited on the aim point** — the pessimistic siting, where
the defence only sees the inbound radius and the missile is at terminal speed throughout:

| Missile | CRAM 22c0 | AGUN 20c0 | SAM 35c0 |
|---|---|---|---|
| `IskanderMissile` | 46 t / 2.76 s | 40 t / 2.40 s | **98 t / 5.88 s** |
| `HIMARSMissile` | 45 t / 2.70 s | 40 t / 2.40 s | **89 t / 5.34 s** |
| `GBU57Bomb` | 46 t / 2.76 s | 41 t / 2.46 s | 72 t / 4.32 s |
| `TacNukeMissile` | 26 t / 1.56 s | 23 t / 1.38 s | 40 t / 2.40 s |
| `KinzhalMissile` | 12 t / 0.72 s | 11 t / 0.66 s | 18 t / 1.08 s |

**The acquire–aim–fire chain costs ~21–27 ticks (1.3–1.6 s) before the first projectile leaves:**

| Stage | Ticks | Citation |
|---|---|---|
| Auto-target rescan | 3–7 | `AutoTarget.cs:199` `MinimumScanTimeInterval = 3`, `:202` `MaximumScanTimeInterval = 8`, re-armed at `:1169` |
| `AimingDelay` | 15 (default) | `Armament.cs:101`; reset on every retarget, `:427-431` |
| Turret traverse | 0–13 | `CanFire` requires `turret.HasAchievedDesiredFacing` (`Armament.cs:400-401`); `TurnSpeed: 40` is a `WAngle` (`Turreted.cs:26`) = 14.06°/tick, so 180° = 12.8 ticks |
| `FireDelay` | 3 (CRAM/AGUN, default `Armament.cs:98`) or 5 (SAM,  `structures-defenses.yaml:824`) | `Armament.cs:579` |

`AimingDelay` and traverse run concurrently, so the total is `scan + max(15, traverse) + FireDelay`.

**Consequence:** against Iskander/HIMARS a SAM has ~65–75 ticks of live fire — ample. Against the Kinzhal
the chain is *longer than the entire window* at every ring, which is exactly the property the user wants
and §3.3 builds on.

---

## 2. Q2 — the smallest honest change

### Option A — YAML only, one line. **This is the recommendation.**

`mods/ww3mod/rules/ingame/structures-defenses.yaml:807`, inside `SAM:` (opens `:784`):

```yaml
	Buildable:
		BuildPaletteOrder: 8
		Prerequisites: ~disabled          # ← the only thing standing in the way
```

Replace with a real prerequisite in the house style (`~techlevel.*` plus a faction/queue token, matching
the launchers at `vehicles-russia.yaml:995` / `vehicles-america.yaml:1077`). Nothing else moves:

- The `Defense` queue already exists on the Player (`player.yaml:35`, `ClassicProductionQueue@Defense`),
  so the cameo appears without new production plumbing.
- `SAM` already inherits `^AutoTargetAirICBM` (`:786`), already mounts
  `SurfaceToAirMissile.double` (`:823`), already has `AttackTurreted`, a husk, a range circle and a cost
  (2000, `:810`).
- `SurfaceToAirMissile` is a homing `Missile` projectile, so **the lead question does not arise** — it
  re-solves the intercept every tick (`Missile.cs:1148`, `WVec.CalculateLeadTarget`) and steers.
- Kinematics work: interceptor top speed **800** against Iskander 600 and HIMARS 550. It overtakes from
  any aspect.
- Lethality is not marginal: `Warhead@Spread` is `Damage: 2000` + `RandomDamageAddition: 1000`
  (`weapons-missiles.yaml:435-437`) against `HP: 100` / `HP: 50`. One contact, one kill.

Porting `Missile`'s launch profile (`MaximumLaunchSpeed: 50`, `Acceleration: 35`, cap 800 —
`Missile.cs:540`, clamped to `maxSpeed`) and closing head-on against each missile already in its terminal
phase, with `Arm: 5` and `CloseEnough: 400` honoured:

| Target | Engaged at 10c | 20c | 30c | 35c |
|---|---|---|---|---|
| `IskanderMissile` | HIT, 12 t | HIT, 20 t | HIT, 28 t | HIT, 32 t |
| `HIMARSMissile` | HIT, 12 t | HIT, 21 t | HIT, 29 t | HIT, 33 t |
| `TacNukeMissile` | HIT, 9 t | HIT, 17 t | HIT, 23 t | HIT, 26 t |
| `KinzhalMissile` | passes through | HIT, 10 t | HIT, 14 t | HIT, 16 t |

The Kinzhal rows look alarming and are not — they assume a SAM that has *already launched*. §3.3 shows the
chain never gets that far in the window available.

**Cost: one line. No weapon retune. No engine work.** Plus the §7 hygiene items, which are optional here
because none of them is on the `SAM`.

### Option B — YAML plus weapon retune (the `CRAM`)

If the CRAM must work, the gun needs a real change, not a prerequisite flip. §6 shows the crossing miss is
1.2–1.8 cells against a 0.42-cell hit radius. Raising `20mm_CRAM`'s `Projectile: Bullet` `Speed` from
`1c0` toward `AACannon`'s `8c0` collapses the single-iteration lead error to ~0 (§6.2 table). Cost: one
line in `weapons-ballistics.yaml:549` plus the prerequisite, **but** `20mm_CRAM` is shared with the F-16
and the MiG (`aircraft-america.yaml:638`, `aircraft-russia.yaml:652`), so retuning it touches three actors,
and the two aircraft are themselves `~disabled`. Blast radius is small but not one. Also requires fixing
the missing ammo gate (§7.1) or the CRAM fires forever.

### Option C — engine work

**Not required, and I could not find a case for it.** The only thing engine work would buy is a converged
intercept solve for `Bullet` (iterate `CalculateLeadTarget` to a fixed point rather than once). That would
make the CRAM lethal and is ~10 lines in `Armament.cs`, but it changes every bullet weapon in the mod
against every moving target, which is a balance event, not a fix. Do not reach for it to solve this.

---

## 3. Q3 — the three support-power missiles

### 3.1 All three are unacquirable on purpose, and say so

Each overrides the inherited `ICBM` with a type no weapon lists:

| Power | Actor | Target type | Citation |
|---|---|---|---|
| `MissileStrikePower@Kinzhal` (`player.yaml:114`) | `kinzhalmissile` (`:124`) | `Hypersonic` | `vehicles-russia.yaml:1174`, `:1177` |
| `MissileStrikePower@GBU57` (`player.yaml:189`) | `gbu57bomb` (`:206`) | `Penetrator` | `vehicles-america.yaml:1266`, `:1269` |
| `MissileStrikePower@TacNuke` (`player.yaml:255`) | `tacnukemissile` (`:273`) | `Hypersonic` | `vehicles.yaml:1033`, `:1036` |

The in-tree reasoning is explicit and worth preserving — from `vehicles-russia.yaml:1166-1172`:

> UNINTERCEPTABLE, and this is the whole point of the Phase 1 tier. […] `Hypersonic` is a type no weapon
> in the mod lists, so nothing can acquire this actor.
> PITFALL: do NOT express that as `-Targetable@Ground:` / `-Targetable@Airborne:`. `WeaponInfo
> .IsValidAgainst` reads `victim.GetEnabledTargetTypes()` and gates on it (`WeaponInfo.cs:256-264`), so an
> actor with NO target types is invalid for every weapon — including splash warheads it happens to stand
> inside. Overriding the types keeps it damageable and merely unacquirable.

**That pitfall is the one thing not to break.** Any change here edits the *value* of `TargetTypes`; it must
never delete the traits.

### 3.2 `MissileDelay` buys zero engagement window

`MissileDelay` is the gap **before the actor enters the world**, not flight time:

```csharp
// engine/OpenRA.Mods.Common/Traits/SupportPowers/MissileStrikePower.cs:38-40
[Desc("Delay (in ticks) after the order until the missile is added to the world.",
    "The launch sounds play immediately; this is the gap before anything is visible.")]
public readonly int MissileDelay = 0;
```

```csharp
// :147-150
if (info.MissileDelay <= 0)
    world.AddFrameEndTask(w => w.Add(missile));
else
    world.AddFrameEndTask(w => w.Add(new SpawnActorEffect(missile, info.MissileDelay)));
```

During the delay the actor is constructed but `IsInWorld` is false — the trait's own beacon code says so at
`:174-178` ("for those ticks the actor exists but is not in the world"). `FindActorsInCircle` cannot return
it. **So the 150 / 300 / 500-tick delays (9 / 18 / 30 s) contribute nothing to interception.** The
engagement window is the flight, and only the flight — the §1.6 table.

This corrects the framing in the original dispatch: the arrival delays are not "the first time an
interceptor could physically engage something in flight." They are a warning siren with no target attached.

### 3.3 The Kinzhal against the user's stated acceptance criterion

**Target: shootable-at, almost never hit — a defence that visibly tries and mostly misses.**

**The change is one word.** `vehicles-russia.yaml:1174` and `:1177`, `TargetTypes: Hypersonic` →
`TargetTypes: ICBM`. Every other gate then passes exactly as it does for the Iskander (§1.3). No retune of
anything, on either side.

**Why "almost never hit" falls out of the shipped numbers rather than needing to be tuned in:**

*Closure.* The Kinzhal is `Speed: 2000`, `TerminalSpeed: 2400` (`vehicles-russia.yaml`, documented at
32.6 → 39.1 cells/s).

| Interceptor | Speed | vs Kinzhal 2000 | vs terminal 2400 |
|---|---|---|---|
| `20mm_CRAM` bullet | 1024 | ratio 0.51 — **cannot overtake, head-on only** | 0.43 |
| `SurfaceToAirMissile` | 800 max | ratio 0.40 — **cannot overtake, head-on only** | 0.33 |
| `AACannon` bullet | 8192 | 4.10 — can overtake | 3.41 |

*Window vs reaction.* The chain to first projectile is ~21–27 ticks (§1.6). The Kinzhal's time inside a
ring sited on the aim point is 12 t (CRAM) / 18 t (SAM). Sited **off** the flight line the defence gets a
chord instead — at best `2R/v`, i.e. 22 t for the CRAM's 22c0 ring and 36 t for the SAM's 35c0 — so:

- **SAM:** ~12 ticks of fire in the best siting. `Burst: 2, BurstDelays: 20` (`weapons-missiles.yaml:447-448`)
  means the second missile of the pair is 20 ticks behind the first and usually falls outside the window.
  **One interceptor launches, sometimes; it is 2.5× too slow to overtake; it kills only on a head-on pass.**
- **CRAM:** the chain is 21–27 ticks against a 22-tick chord. It often gets **no shot at all**, which is
  worse than the brief wants — silence rather than a visible failed attempt.

**So: shootable-at, tried at occasionally, hit almost never. That is the criterion, delivered by a
one-word change.** The `SAM` is the right platform for it because it at least reliably *launches*.

**Knobs to turn after playtesting**, in the order I would reach for them:

| Symptom | Knob | Where | Direction |
|---|---|---|---|
| Nothing ever fires — no visible attempt | `AimingDelay` on the SAM armament | `structures-defenses.yaml:822` (add; default 15, `Armament.cs:101`) | ↓ to 5–8; buys 7–10 ticks of window, the cheapest lever |
| Still nothing fires | `Range` on `SurfaceToAirMissile` | `weapons-missiles.yaml:415` (`35c0`) | ↑; window scales linearly with ring radius |
| Fires but never even looks close | `Speed` / `Acceleration` on the `Missile` projectile | `weapons-missiles.yaml:424`, `:423` (800 / 35) | ↑ Acceleration first — the interceptor spends 22 ticks reaching top speed and covers only ~9.7 cells doing it |
| Hits too often | `Kinzhal` `Speed` / `TerminalSpeed` | `vehicles-russia.yaml` (2000 / 2400) | ↑; or ↓ interceptor `Speed` |
| Hits too often, and you want the *look* kept | `Inaccuracy` on `SurfaceToAirMissile` | `weapons-missiles.yaml:425` (`400`) | ↑; misses visibly rather than not engaging |

**One thing to change alongside it, or the game lies to the player:** the Kinzhal's `Description`
(`player.yaml:118-119`) ends `Cannot be intercepted.` Making it shootable-at makes that line false. The
honest replacement is something like `Too fast to reliably intercept.`

### 3.4 GBU-57 and the tac nuke

Both are one-word changes too (`Penetrator` → `ICBM`; `Hypersonic` → `ICBM`), and both would be *much*
more interceptable than the Kinzhal — the GBU-57 sits in a SAM ring for 72 ticks and the tac nuke for 40,
and the SAM overtakes the GBU-57 comfortably (800 vs 500). **Do not do these as part of the same change.**
The GBU-57's stated identity is "nothing in the mod can reach the B-2 that drops it"
(`vehicles-america.yaml:1256-1260`), and the tac nuke is lobby-gated off by default
(`player.yaml:251-254`). They are separate design decisions and the user has only ruled on the Kinzhal.

---

## 4. Q4 — what is gated, and behind what

Three distinct categories. They are not fixed the same way.

### 4.1 Gated by prerequisite — `Prerequisites: ~disabled`

`~disabled` is a **never-satisfiable** token: no `ProvidesPrerequisite` anywhere in `mods/ww3mod/rules/`
grants `disabled`. It is the mod's shelving idiom, used 52 times across 13 files. Flipping it is the whole
fix; nothing else holds these actors back.

Every actor in the mod that mounts an ICBM-capable weapon:

| Actor | Weapon | Weapon `file:line` | Actor | `~disabled` at |
|---|---|---|---|---|
| `CRAM` | `20mm_CRAM` | `weapons-ballistics.yaml:543` | `structures-defenses.yaml:622` | **:636** |
| `AGUN` | `AACannon` | `weapons-ballistics.yaml:559` | `structures-defenses.yaml:707` | **:722** |
| `SAM` | `SurfaceToAirMissile.double` | `weapons-missiles.yaml:445` | `structures-defenses.yaml:784` | **:807** |
| `HSAM` | inherits `SAM` | — | `structures-defenses.yaml:839` | **:849** |
| `F16` | `AirToAirMissile` + `20mm_CRAM` | `weapons-missiles.yaml:451` | `aircraft-america.yaml:612`, `:638` | `Prerequisites: ~disabled` |
| `MIG` | `AirToAirMissile` + `20mm_CRAM` | — | `aircraft-russia.yaml:626`, `:652` | `Prerequisites: ~disabled` |

All three ground defences already inherit `^AutoTargetAirICBM` (`structures-defenses.yaml:624`, `:709`,
`:786`), so **the auto-target half is already correct on all of them.**

### 4.2 Gated by a condition — and there is a name collision to be careful of

`CRAM`, `AGUN` and `SAM` each carry `AttackTurreted: PauseOnCondition: disabled`
(`structures-defenses.yaml:695`, `:772`, `:829`).

**`disabled` the CONDITION is not `~disabled` the PREREQUISITE, and they do completely different things.**
The condition is granted by `^BuildingAffectedByEMP` (`structures.yaml:218-220`):

```yaml
	GrantCondition@EMP:
		RequiresCondition: empdisable
		Condition: disabled
```

So `PauseOnCondition: disabled` means "stop shooting while EMP'd" — a live, intended, temporary gate. It is
**not** a shelving mechanism and must not be removed when the prerequisite is flipped. The two also carry
`RequiresCondition: !build-incomplete`, the ordinary under-construction gate.

*(The near-identical spelling is the trap. `~disabled` never resolves; `disabled` resolves under EMP.)*

### 4.3 Present but unreferenced

- **`PrismLaserMaxFirepower`** (`weapons-other.yaml:407`) lists `Vehicle, Structure, Defense, Water, Air,
  ICBM` and `Damage: 5000` at `Range: 25c0`. Its only other appearance in the tree is inside a commented-out
  block at `vehicles-america.yaml:1370`. **No actor mounts it.** Dead weight, not a candidate.
- **`PATRIOT`** (`structures-defenses.yaml:877-911`) is entirely commented out. Even in the comment it is
  `~disabled`, and its `Queue: Defence.USA, RADefence.USA` names queues that do not exist in this mod
  (`player.yaml` defines `Building`, `Defense`, `Vehicle`, `Infantry`). Dead code.

### 4.4 What is *not* gated — and cannot be made to work cheaply

`MANPAD` (`infantry.yaml:1841`), `Stinger`, `Stinger.quad` and `9M311` (Tunguska, `vehicles-russia.yaml:823`)
are all `ValidTargets: Air` with **no** `ICBM` (`weapons-missiles.yaml:531`, `:559`). The mobile SHORAD half
of the roster is doubly excluded: wrong target types *and* `Stinger` tops out at `Speed: 600`, which ties
the Iskander and cannot overtake it. Opening those is a separate, larger job. Leave them.

---

## 5. Q5 — recommendation: make the `SAM` real, and only the `SAM`

**Change `structures-defenses.yaml:807` and stop.**

Why the SAM and not the other two:

1. **It is the only one that works with no retune.** Homing projectile, so the whole lead question is moot;
   800 vs 600/550 so it overtakes from any aspect; 2000–3000 damage against 50–100 HP so contact is a kill.
2. **It has the largest window by a wide margin** — 89–98 ticks against the CRAM's 45–46 (§1.6). That
   margin is what absorbs the ~24-tick reaction chain and leaves a real engagement.
3. **It is the platform that already delivers the Kinzhal behaviour the user asked for**, unchanged (§3.3).
   The CRAM at its current range often gets no shot at all, which reads as a bug rather than a near miss.
4. **It carries no `AmmoPool`**, so it side-steps the §7.1 defect entirely — the CRAM does not.
5. **It is one building, not a system.** A CRAM *and* an AGUN *and* a SAM is three overlapping rings, three
   cost curves and three counter-play stories to balance. One is a feature; three is a rebalance.

Against it, honestly: the SAM costs 2000 (`:810`) and is the most expensive of the three, so it is the
least likely to be built speculatively; and `HP: 4500` (`:814`) is *lower* than the CRAM's and AGUN's 7500,
so the answer to a SAM is to shoot it, which may make it feel fragile before it feels useful. Both are
tuning, not blockers.

**Do not flip all three "while we are here."** The CRAM would ship a gun that visibly fires and never
connects (§6) *and* fires with an empty magazine (§7.1) — strictly worse than shipping nothing, which was
the original brief's concern and remains correct for the CRAM specifically.

---

## 6. The eleven-cell finding — arithmetic right, mechanism wrong

`WORKSPACE/recon/powers-interception.md` §4.2 states: *"Guns do not lead. At all."*, citing
`Bullet.cs:201` (`target = args.PassiveTarget`). An in-tree YAML comment says the same
(`weapons-ballistics.yaml:711-712`: *"Bullets do not lead (Bullet.cs:200 aims at the target's position at
fire time)"*).

**`Bullet.cs:201` is read correctly. It is the wrong file boundary.** The lead is applied *upstream*, in
`Armament`, before `PassiveTarget` is handed to the projectile:

```csharp
// engine/OpenRA.Mods.Common/Traits/Armament.cs:616-661 (inside the ScheduleDelayedAction lambda)
// Lead/aim in front of moving target
if (args.Weapon.Projectile != null)
{
    // If projectile is bullet (not missile), lead (aim in front of) target
    if (Weapon.Projectile is BulletInfo bullet && Target.Value.Type != TargetType.Invalid)
    {
        …
        var leadTarget = WVec.CalculateLeadTarget(self.CenterPosition, initialPosition, targetPosition,
            Info.FireDelay, bullet.Speed.First().Length);
        …
        args.PassiveTarget = aimCenter + args.TargetingVector;
```

### 6.1 Reachability, checked rather than assumed

- `Armament.Target` is assigned at the top of `FireBarrel` (`:501`), before the lambda is scheduled at
  `:579`. `Target.Value.Type` is therefore the live target, not `Invalid`.
- `AimInitialTargetPosition` is cleared on retarget (`:431`), appended in `FireBarrel` (`:518`), and
  popped FIFO inside the lambda (`:631`, `:639-640`) — so `initialPosition` is the target's position
  exactly `FireDelay` ticks earlier and `vectorDiffPerTick` is its true per-tick velocity.
- `ArmamentInfo.FireDelay` defaults to **3**, not 0 (`:98`), so `ScheduleDelayedAction` really defers
  (`:387-393`) and the sample window is real. The field's own `[Desc]` is explicit: *"Cannot be 0 for
  Bullet Projectiles as it is used to calculate how much to lead target by checking position change
  (speed) between this many ticks."*
- `git log -L 616,662:…/Armament.cs` dates the helper call to `9e7e3902` (**2023-06-20**, "Missile lead
  target"), whose diff to `Armament.cs` is `1 insertion, 6 deletions` — it *replaced* a pre-existing inline
  lead computation. **Bullets have led for longer than that commit, and both in-tree assertions post-date it.**

### 6.2 What the lead actually buys

Ported exactly, including both integer truncations (`WVec / int` per component, and
`ticksToReachTarget = distanceToTarget / projectileSpeed`) and the fact that `distanceToTarget` is
`HorizontalLength` — 2D — while `Bullet` then computes its flight time over the 3D distance
(`Bullet.cs:228`). Target at 4 cells altitude. **Hit requires the impact within 427 WDist** = `CircleShape`
default `Radius` 426 (`HitShapes/Circle.cs:28`; `^ShootableMissile` declares a bare `HitShape:`) plus
`TargetDamage`'s default `Spread` of 1 (`TargetDamageWarhead.cs:24`, tested at `:84-85`). 427 WDist = **0.42 cells**.

`20mm_CRAM`, Bullet `Speed: 1c0`:

| Target | Range | Crossing miss | Closing miss | No-lead miss (the old model) |
|---|---|---|---|---|
| Iskander @600 | 10c | 1200 (1.17 c) | 3000 (2.93 c) | 6000 (5.86 c) |
| Iskander @600 | 22c | 1800 (1.76 c) | 7800 (7.62 c) | **13200 (12.89 c)** |
| HIMARS @500 | 22c | 1000 (0.98 c) | 5500 (5.37 c) | **11000 (10.74 c)** |
| Iskander @303 (mid-flight) | 22c | 303 (0.30 c) | 2121 (2.07 c) | 6666 (6.51 c) |

**The eleven cells reproduce exactly under the no-lead model** — 10.74 cells for HIMARS at the CRAM's max
range, 11.72 for the Iskander at 20c. The number was arithmetically sound; it was computed for a mechanism
the engine does not use.

**The conclusion survives; the magnitude does not.** With lead, the CRAM's crossing miss is 1.2–1.8 cells
against a 0.42-cell hit radius — a miss by ~3–4×, not ~26×. Two further notes worth having:

- **The closing case is worse than the crossing case**, which is counter-intuitive and is a property of the
  single-iteration solve: at 22c against a closing Iskander the lead pulls the aim 13200 WDist *nearer*,
  the bullet then only needs 9 ticks instead of 22, and the missile has moved 5400 — a 7800 over-lead. **A
  CRAM sited on the point being attacked is in the worst geometry available to it.**
- **Below one tick of bullet travel the lead is exactly zero**, because `ticksToReachTarget` is integer
  division. For `AACannon` at `Speed: 8c0` that is every engagement inside 8 cells.

`AACannon` (Bullet `Speed: 8c0`) converges: the same computation gives a miss of 0 at 10c and 20c against
every missile in the table, including the Kinzhal. **Kinematically the AGUN is the best gun in the mod
against a ballistic missile.** It is not the recommendation anyway, because its `Inaccuracy: 2c0`
(`weapons-ballistics.yaml:568`) scales to a full 2048-WDist scatter at max range
(`InaccuracyType.Maximum`, `Util.cs:406-408`) against that 0.42-cell hit radius — it would connect by
volume (`Burst: 10`) rather than by aim, which is a different feature from the one being costed.

---

## 7. Defects found in passing

Neither is currently reachable — both hosts are `~disabled` — so neither is a live bug today. Both become
live the moment a prerequisite is flipped, which is why they belong in a costing.

### 7.1 `CRAM` fires with an empty magazine

```yaml
# mods/ww3mod/rules/ingame/structures-defenses.yaml:655-658
	Armament@1:
		Weapon: 20mm_CRAM
		LocalOffset: 520,0,450
		MuzzleSequence: muzzle
```

versus `AGUN` at `:739-744`, which has `PauseOnCondition: !ammo`.

`Armament.CanFire` (`:395-410`) never consults `AmmoPool`; the ammo gate in this engine is *by convention*
the `PauseOnCondition: !ammo` line, via `IsTraitPaused`. `AmmoPool.TakeAmmo` returns `false` when empty
(`AmmoPool.cs:441-444`) but its caller, `INotifyAttack.Attacking` (`:997-1006`), discards the return.

So the CRAM has a complete ammo economy — `AmmoPool` 24 rounds (`:667-669`),
`WithAmmoPipsDecoration` 6 pips (`:677-681`), `ReloadAmmoPool` 24 every 42 ticks (`:682-685`), a reload
decoration (`:686-691`) — **and an armament that never stops.** The pips would drain to empty, the reload timer
would run, and the gun would keep firing through all of it. One line to fix, alongside any CRAM work.

### 7.2 `PrismLaserMaxFirepower` — see §4.3. An ICBM-capable 5000-damage weapon with no host.

---

## 8. Corrections to the dispatch's framing

Recorded plainly because each changes what is being chosen between:

1. **"The Iskander launcher fires its warhead through `Explodes` with no support-power order."** It does
   not — `MissileSpawnerMaster` spawns an actor (§1.1). The `Explodes` traits are launcher cook-off.
2. **"Three missile support powers … which is the first time in this mod's history that an interceptor
   could physically engage something in flight."** The Iskander and HIMARS have shipped interceptable
   missile actors for months, and their flight times (8.3–9.4 s) are longer than any of the three powers'
   (1.2–4.9 s). The powers are the *hardest* targets in the mod, not the first ones.
3. **"Arrival delays of 150/300/500 ticks … buys engagement window."** `MissileDelay` is pre-spawn; the
   actor is not in the world and cannot be found by any scan (§3.2). The delays buy warning, not window.
4. **"`20mm_CRAM` has the lowest muzzle velocity in the mod."** False as stated. Of 45 `Bullet` weapons
   carrying a `Speed`, `20mm_CRAM` at 1024 ranks **39th slowest** — it ties nine other weapons (`^9mm`,
   `^5.56mm`, `^7.62mm`, `^12.7mm`, `MP5`, the miniguns) and is faster than `^30mm` (900), `^ArtilleryRound`
   (500) and `GradRockets` (300). It is the slowest of the *anti-air* weapons, which is the true and
   relevant claim.
5. **"Misses by roughly eleven cells."** Correct arithmetic, wrong mechanism; the real figure is 1.2–1.8
   cells crossing (§6). The verdict "the CRAM cannot hit it as shipped" stands.

---

## 9. What I could not verify, and the exact runs that would settle it

I did not launch anything. Everything above is a port of engine source, so it is only as good as the claim
that the code I read is the code that runs. **The single load-bearing claim I would most like falsified is
§6: that `Armament`'s Bullet lead is live.** Two in-tree sources assert the opposite, and one of them is a
tuning comment written by someone who had presumably watched a Littlebird not get hit. My reachability
proof (§6.1) is a static one.

### Run 1 — settle the lead question (highest value; settles §6, §2 Option B, and §8.4)

- **Setup:** any scenario. One `CRAM` (scenario-local `rules.yaml` override of `Buildable.Prerequisites`,
  or preplaced), one `iskander` firing across the CRAM's front at ~15 cells so the geometry is *crossing*,
  not closing.
- **Instrument:** run with `WW3_GUNTRACE=1` (`engine/OpenRA.Mods.Common/GunTrace.cs:23-24`). `Bullet.cs:402`
  writes `[GUNTRACE] explode impact=… aimedAt=… src=… ticks=… length=…` to `debug.log`.
- **What counts as the answer:** compare `aimedAt` against the `IskanderMissile`'s position at fire time.
  - **Lead is live (my reading):** `aimedAt` is displaced from the missile's fire-time position along its
    velocity by roughly `range/1024 × 600` WDist — order 10000 at 15 cells.
  - **Lead is dead (the recon's reading, and I am wrong):** `aimedAt` equals the fire-time position to
    within `Inaccuracy` (≤256).
- Nothing needs to be *hit* for this to answer. One salvo is enough.

### Run 2 — does the one-line SAM change actually work (settles §2 Option A)

- **Setup:** `SAM` with its prerequisite flipped, sited on the aim point. One `iskander` firing at it from
  ~35 cells.
- **What counts as the answer:** the `IskanderMissile` dies **airborne** — `IskanderExplosion` at altitude,
  visibly short of the SAM, in ≥8 of 10 shots. Anything under ~5 of 10 means the reaction chain is eating
  more of the window than §1.6 predicts and `AimingDelay` is the first knob (§3.3 table).

### Run 3 — the Kinzhal criterion (settles §3.3; run only after 2 passes)

- **Setup:** `SAM` as above plus `KinzhalMissile.TargetTypes` set to `ICBM`. Fire the Kinzhal power at the
  SAM from the opposite map edge, ten times.
- **What counts as the answer, and it is a band, not a threshold:** the SAM **launches at least one
  interceptor in most runs** (the "visibly tries" half) **and the Kinzhal reaches its aim point in at least
  8 of 10** (the "mostly misses" half). Zero launches fails just as surely as ten kills — silence is the
  failure mode the CRAM would have given us.

### Not verified, out of scope, flagged rather than guessed

- `SurfaceToAirMissile` sets `HorizontalRateOfTurn: 35` but leaves `VerticalRateOfTurn` at its default
  `WAngle(24)` (`Missile.cs:102`) and `CruiseAltitude` at `512` (`:128`) — half a cell. Vertical homing
  against a target at a 5–8 cell apex is therefore on default guidance. I did not model it. The empirical
  argument that it is fine is that the same weapon already engages the mod's aircraft at
  `CruiseAltitude: 2560`; Run 2 would settle it directly.
- Whether the `AutoTargetPriority` band ordering makes a defence *prefer* an inbound missile over a nearby
  aircraft. `^AutoTargetAirICBM` lists `Air, AirSmall, ICBM` in one band at one priority
  (`defaults.yaml:748-750`), so they are peers and the tie breaks on the range/cluster/overkill terms
  (`AutoTarget.cs:1420+`). If a SAM ignores a nuke to keep shooting a helicopter, that is where to look —
  splitting `ICBM` into its own higher-priority `AutoTargetPriority@` is the fix, and it is YAML.
- Hit *probability* per round for the `AGUN`, which needs `SpreadDamage` falloff against `Inaccuracy`
  sampled over `Burst: 10`. I gave the deterministic lead error only. If the AGUN ever becomes the
  candidate, that distribution is the missing number.
