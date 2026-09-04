# Missile powers — can a missile be shot down, and by what?

**Researched against `main @ 2c8488ef`** in worktree `wt/powers-intercept` (`git rev-parse --short HEAD` → `2c8488ef`; `git status -sb` clean; the branch has no upstream, so `git rev-list --count HEAD..@{u}` cannot run — `2c8488ef` is the merge commit at the tip of `main`). **Static analysis only — no game runs, no autotests, no YAML lint, no build.** Every claim carries a `file:line` read at that SHA.

**Timestep:** 60 ms ⇒ **16.667 ticks/s** (`mods/ww3mod/mod.yaml`, restated `recon/powers-and-preloaded-transports.md:5`). `seconds = ticks × 0.06`. Every duration below uses that, not 25 tps.

---

## 0. Headline findings

1. **A projectile cannot be shot down. Not "is not"; cannot.** `IProjectile : IEffect` (`engine/OpenRA.Game/GameRules/WeaponInfo.cs:71`) and `IEffect` declares exactly two members, `Tick` and `Render` (`engine/OpenRA.Game/Effects/IEffect.cs:17-21`). Effects live in a `List<IEffect>` that is a different field from the actor dictionary (`World.cs:33` vs `:394-402`), `TargetType` has four values and none of them is a projectile (`Target.cs:18`), and `ITargetable` is a trait interface whose every method takes an `Actor` (`TraitsInterfaces.cs:601-607`). There is no seam to widen. §1.
2. **But the mod does not need one, because the shape that CAN be shot down is already shipping and has been for months.** `^ShootableMissile` (`mods/ww3mod/rules/defaults.yaml:1074-1101`) is a missile-as-actor template — `BallisticMissile` flight, `Armor: Light`, `HitShape`, `Detectable`, `RejectsOrders`, and `Targetable@Ground` + `Targetable@Airborne` both `TargetTypes: ICBM`. `IskanderMissile` (`vehicles-russia.yaml:1116`, `Health.HP: 100`) and `HIMARSMissile` (`vehicles-america.yaml:1212`, `HP: 50`) inherit it and are in the live game today. **This confirms the manager's read in full.** §2.
3. **"AA vehicles yes, MANPADs no" is expressible, and the MANPAD half already ships true.** `MANPAD` is `ValidTargets: Air` with no `ICBM` (`weapons-missiles.yaml:481-484`) and `^AA` auto-targets through `^AutoTargetAir`, whose priority table lists `Air` and never `ICBM` (`infantry.yaml:1823`, `defaults.yaml:739-745`). A MANPAD cannot engage an ICBM-typed actor today by two independent gates. §3.
4. **The AA-vehicle half does NOT ship — and neither does anything else.** `strykershorad` and `tunguska` both auto-target through `^AutoTargetAAIFV` (`defaults.yaml:460`), which lists Helicopter / Aircraft / Vehicle / Infantry and **no ICBM**; their missiles (`Stinger.quad`, `9M311`) are `ValidTargets: Air`. The only actors that list ICBM are `CRAM`, `AGUN`, `SAM`, `HSAM` — **all `Buildable.Prerequisites: ~disabled`** (`structures-defenses.yaml:643`, `:729`, `:814`, `:911`). **Nothing a player can currently build can intercept anything.** §3.2.
5. **⚠️ THE HIGHEST-RISK RESIDUE, AND IT INVERTS THE MANAGER'S DISCRIMINATOR: guns cannot hit a ballistic missile in this engine, at any range, ever.** `Bullet` fires at the target's position **at the moment of firing** (`Bullet.cs:201`, `target = args.PassiveTarget`) — no lead. `20mm_CRAM` (the literal Counter-Rocket-Artillery-Mortar gun) has `Speed: 1c0`; at its 22-cell range a round takes 22 ticks to arrive, during which an Iskander in terminal flight moves **≈11 cells**. The Tunguska's 30 mm (`Speed: 900`) is off by ≈10 cells at 18c0 and still ≈1.2 cells at point blank, against a 426-WDist hitshape. **Only homing `Missile` projectiles can intercept**, because only they lead (`Missile.cs:1148`, `WVec.CalculateLeadTarget`). §4.
6. **And the physics quietly makes "MANPADs no" true a third time.** `MANPAD` flies at `Speed: 450` (`weapons-missiles.yaml:509`); an Iskander's terminal speed is **516–600 WDist/tick**. A MANPAD physically cannot catch it even if every target-type gate were opened. `Stinger`/`9M311` at 600 tie and can only kill head-on or crossing. `SurfaceToAirMissile` at **800** is the only weapon in the mod that comfortably runs one down. §4.
7. **Adding `ICBM` to the AA vehicles' *missiles* is a 2-line YAML change with a blast radius of exactly two actors — and it is the wrong two.** `Stinger` is inherited by `Stinger.quad` (SHORAD) and `9M311` (Tunguska) and nothing else; `9M311` is used by `tunguska` alone. But because both are Speed 600, the honest fix is a **new dedicated interceptor weapon** or raising their speed. The gun keys are worse: `25mm.Bradley` is shared with `bradley` itself (`vehicles-america.yaml:375` and `:944`), so touching it arms every Bradley — and arms it with a weapon that cannot hit. §3.3.
8. **A missile killed in flight detonates its full warhead where it dies, and that mostly reads correctly.** `BallisticMissileFly` ends with `self.Kill(self)` (`BallisticMissileFly.cs:209`), so normal impact and interception are the *same* code path — `Explodes` on death. Airborne death fires `Explodes` (`IskanderExplosion`, full), ground-level death fires `SpawnedExplodes` (same weapon, XP to the launcher). Warhead falloff is measured in **3D** (`SpreadDamageWarhead.cs:97`), so a hit near apex (4–8 cells up) is harmless; a hit in the last second of the dive is not. §5.
9. **`IskanderExplosionAirborne` exists, is entirely commented out, and has zero references** (`weapons-explosions.yaml:619-629`). Someone started the "harmless airburst" weapon and stopped. As written it is a bare `Inherits: IskanderExplosion` — byte-identical to the full warhead. §5.
10. **The Kinzhal is free, and the cleanest form is `NukePower`'s shape, not an actor.** `NukePower.Activate` adds a `NukeLaunch` **effect** (`NukePower.cs:172`), never an actor — untargetable by construction, and already wired and art-complete on `MSLO`. If both tiers must share the actor shape, give the Kinzhal a target type nothing lists (e.g. `Hypersonic`); do not delete `Targetable`, which also makes it immune to splash. §6.
11. **An actor-missile does NOT inherit the A-10 strafe failure.** `Explodes` calls `weapon.Impact(...)` directly (`Explodes.cs:133`) — `Armament.CanFire` and its `IsValidAgainst` gate (`Armament.cs:402`, the thing that silences `A10.Airstrike`) are never on the path. This is the single strongest argument for the actor-missile over an aircraft power. §2.4.
12. **The one thing that is genuinely missing is delivery.** No shipped support power can put a `BallisticMissile` actor on the map. `AirstrikePower` hard-requires `AircraftInfo` (`AirstrikePower.cs:75`); `SpawnActorPower` spawns at the *target* cell and never sets `BallisticMissile.Target`, which `BallisticMissileFly` reads unconditionally — the documented `InvalidOperationException` at `MissileSpawnerMaster.cs:85-87`. This is ~80 lines of new C#, and it is the whole engine cost of the feature. §2.3.

---

## 1. Q1 — a projectile cannot be a target

### 1.1 What a projectile is

Every file in `engine/OpenRA.Mods.Common/Projectiles/` implements `IProjectile`, verified by reading the class declarations rather than assuming:

```
$ grep -rn "^	public class .* : I" engine/OpenRA.Mods.Common/Projectiles/
AreaBeam.cs:88:     public class AreaBeam : IProjectile, ISync
Bullet.cs:152:      public class Bullet : IProjectile, ISync
GravityBomb.cs:55:  public class GravityBomb : IProjectile, ISync
InstantHit.cs:46:   public class InstantHit : IProjectile
LaserZap.cs:112:    public class LaserZap : IProjectile, ISync
Missile.cs:211:     public class Missile : IProjectile, ISync
NukeLaunch.cs:21:   public class NukeLaunch : IProjectile
Railgun.cs:107:     public class Railgun : IProjectile, ISync
```

None derives from `Actor`. And `IProjectile` is a marker on `IEffect`:

```csharp
// engine/OpenRA.Game/GameRules/WeaponInfo.cs:71-72
public interface IProjectile : IEffect { }
public interface IProjectileInfo { IProjectile Create(ProjectileArgs args); }
```

```csharp
// engine/OpenRA.Game/Effects/IEffect.cs:17-21
public interface IEffect
{
    void Tick(World world);
    IEnumerable<IRenderable> Render(WorldRenderer r);
}
```

**Two members. No health, no owner, no traits, no identity.** A projectile has no `ActorID`, so nothing can even name it in an order.

### 1.2 The storage is a different container

```csharp
// engine/OpenRA.Game/World.cs
33:  readonly List<IEffect> effects = new();
394: public void Add(Actor a) { a.IsInWorld = true; actors.Add(a.ActorID, a); ActorAdded(a); ... }
414: public void Add(IEffect e) { effects.Add(e); if (e is not ISpatiallyPartitionable) unpartitionedEffects.Add(e); ... }
```

Actors go into a keyed dictionary and fire `ActorAdded`. Effects go into a plain list and fire nothing. `World.Tick` runs them separately (`:510`, `effects.DoTimed(e => e.Tick(this), "Effect")`).

### 1.3 The query path can only return actors

`World.FindActorsInCircle` — the function every scan in the codebase goes through — is typed `IEnumerable<Actor>` and reads the actor position index:

```csharp
// engine/OpenRA.Game/WorldUtils.cs:79-85
public static IEnumerable<Actor> FindActorsInCircle(this World world, WPos origin, WDist r)
{
    var vec = new WVec(r, r, WDist.Zero);
    return world.ActorMap.ActorsInBox(origin - vec, origin + vec).Where(
        a => (a.CenterPosition - origin).HorizontalLengthSquared <= r.LengthSquared);
}
```

Effects never enter `ActorMap`. `World.Add(IEffect)` (`:414-423`) touches `effects`, `unpartitionedEffects` and `syncedEffects` and nothing else — no `AddToMaps`, no `ActorMap.AddPosition`.

### 1.4 The target vocabulary has no room for one

```csharp
// engine/OpenRA.Game/Traits/Target.cs:18
public enum TargetType : byte { Invalid, Actor, Terrain, FrozenActor }
// :88
public static Target FromActor(Actor a) { return a != null ? new Target(a, a.Generation) : Invalid; }
```

Four cases. Adding a fifth is not a YAML change and not a small C# change: `TargetType` is switched on throughout order handling, warhead impact classification, and the render/annotation layer.

```csharp
// engine/OpenRA.Game/Traits/TraitsInterfaces.cs:601-607
public interface ITargetable
{
    BitSet<TargetableType> TargetTypes { get; }
    bool TargetableBy(Actor self, Actor byActor);
    bool RequiresForceFire { get; }
}
```

`ITargetable` is an actor trait. `Actor.GetEnabledTargetTypes()` (`Actor.cs:661-669`) walks `Targetables`, a trait collection that only actors have.

**Verdict: flat no.** Making a projectile targetable means giving effects an identity, a spatial index entry, a trait system and a fifth `TargetType`. That is not a feature; that is a rewrite of the actor/effect split.

### 1.5 The one thing in-tree that *does* kill a projectile — and why it is not this

Searching for pre-existing interception found exactly one mechanism, and it surfaced through a WW3MOD-authored diagnostic enum:

```csharp
// engine/OpenRA.Mods.Common/Projectiles/MissileTrace.cs:47
JammedAps,      // JamsMissiles with ActiveProtection shot it down
```

```csharp
// engine/OpenRA.Mods.Common/Projectiles/Missile.cs:921-931
var jammingActor = world.ActorsWithTrait<JamsMissiles>().FirstOrDefault(JammedBy);
var jammed = info.Jammable && jammingActor.Actor != null;
if (jammed)
{
    if (jammingActor.Trait.Info.ActiveProtection)
    {
        if (trace != null) trace.PendingReason = MissileEndReason.JammedAps;
        Explode(world);
    }
    else { /* random heading diversion */ }
}
```

`JamsMissiles` (`engine/OpenRA.Mods.Common/Traits/JamsMissiles.cs:17-31`) is an **aura**, not a targeting operation: `Range`, `DeflectionRelationships`, `Chance`, `ActiveProtection`. Any actor carrying it detonates enemy `Missile` projectiles that come within `Range`. It is not a shot, it has no lead problem, it cannot miss, and it cannot be aimed.

**It is commented out in the mod** — the sole occurrence is `mods/ww3mod/rules/ingame/vehicles-america.yaml:516`, `# JamsMissiles:`.

Also note its scope: `Missile` only. `Bullet`, `GravityBomb`, `Railgun`, `AreaBeam` have no equivalent — grep for `JamsMissiles` under `Projectiles/` returns hits only in `Missile.cs` and `MissileTrace.cs`.

**Design note worth putting in front of the user:** if the cruise missile were a plain `Missile` projectile, `JamsMissiles` + `ActiveProtection` is a shipped, zero-code way to give AA vehicles a "hard-kill APS bubble" that stops it — a **radius**, not a shot. It expresses "AA vehicles yes, MANPADs no" exactly as well as target types do (put the trait on the vehicles and not the infantry), it cannot miss, and it would read as an invisible dome rather than a visible intercept. I mention it because it is genuinely cheaper than everything in §2, not because I think it is what the user described.

---

## 2. Q2 — the shape that CAN be shot down is already in the game

### 2.1 `^ShootableMissile`

`mods/ww3mod/rules/defaults.yaml:1074-1101`, read in full:

```yaml
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
	Detectable:
		Vision: 1
		Radar: 1
		Position: Ground
	Tooltip:
		Name: Missile
		GenericName: Missile
		ShowOwnerRow: false
	HitShape:
	RejectsOrders:
	Interactable:
	WithFacingSpriteBody:
	WithShadow:
```

Concrete users, both live and buildable:

| Actor | Where | Health | Warhead | Launcher |
|---|---|---|---|---|
| `IskanderMissile` | `vehicles-russia.yaml:1116` | `HP: 100` | `IskanderExplosion` | `iskander` (`:987`, cost 6000, `~techlevel.high`) |
| `HIMARSMissile` | `vehicles-america.yaml:1212` | `HP: 50` | `HIMARSExplosion` | `HIMARS` (cost 6000, `~techlevel.high`) |

Both carry `MissileSpawnerSlave` and are launched by `MissileSpawnerMaster` on the launcher, which sets the flight target on the slave before adding it to the world:

```csharp
// engine/OpenRA.Mods.Common/Traits/MissileSpawnerMaster.cs:111-116
var bm = se.Actor.Trait<BallisticMissile>();
bm.Target = Target.FromPos(target.CenterPosition);
...
SpawnIntoWorld(self, se.Actor, self.CenterPosition + a.MuzzleOffset(self, barrel));
```

**Someone has already debugged intercepting these.** Two in-tree comments describe an intercept that produced no visual until `ICBM` was added to an effects warhead's `ValidTargets`:

```yaml
# mods/ww3mod/rules/weapons/weapons-effects.yaml:682-687
	Warhead@Effect: CreateEffect
		# PITFALL: an in-flight ballistic missile (^ShootableMissile, defaults.yaml) is
		# TargetTypes: ICBM ONLY — it is not Air. Omitting ICBM here makes the missile an
		# invalid actor at impact, which suppresses the whole effect (CreateEffectWarhead
		# .DoImpact early-returns on ImpactActorType.Invalid), so an intercept renders nothing.
		ValidTargets: Air, ICBM
# :704-706
		# ICBM: see ^MinimalExplosionEffectsAir — a SAM intercept of a ballistic missile
		# rendered nothing without it.
```

So interception of an actor-missile is not theoretical in this codebase — it has been observed, debugged and fixed at least once.

### 2.2 Does it register in the target query? (the step the brief asked me not to assume)

`BallisticMissile.OccupiedCells()` returns an empty array (`BallisticMissile.cs:224-227`), which raised the obvious worry that the actor is invisible to `ActorMap`. It is not:

```csharp
// engine/OpenRA.Mods.Common/Traits/BallisticMissile.cs:216-222
void INotifyAddedToWorld.AddedToWorld(Actor self)
{
    self.World.AddToMaps(self, this);
    self.QueueActivity(new BallisticMissileFly(self, Target, this));
    ...
}
```

```csharp
// engine/OpenRA.Mods.Common/Traits/World/ActorMap.cs:593-607
public void AddPosition(Actor a, IOccupySpace ios) { addActorPosition.Add(a); }
public void UpdatePosition(Actor a, IOccupySpace ios) { RemovePosition(a, ios); AddPosition(a, ios); }
```

`AddPosition` **ignores the `ios` argument entirely** and indexes by the actor's `CenterPosition`. `BallisticMissile.SetPosition` calls `World.UpdateMaps` every tick (`:266-269`). So the flying missile is in the position bins that `ActorsInBox`/`FindActorsInCircle` read, despite occupying no cells. ✅

### 2.3 What is missing: nothing can deliver one from a map edge

This is the real cost, and I did not find a way around it.

- **`AirstrikePower` is out.** `AirstrikePower.cs:75` does `actorInfo.TraitInfo<AircraftInfo>()` — an unconditional lookup that throws for an actor without `Aircraft`. `BallisticMissile` is a separate `IMove`/`IPositionable` implementation, not `Aircraft`. (`AirstrikePower.cs:24` also declares `[ActorReference(typeof(AircraftInfo))]`, so the lint would reject it before the game did.)
- **`ParatroopersPower` is out** for the same reason.
- **`SpawnActorPower` is out, twice.** It spawns at the *target* cell, not at a map edge (`SpawnActorPower.cs:83-87`, `new LocationInit(cell)`), and it never sets `BallisticMissile.Target`. `AddedToWorld` immediately queues `BallisticMissileFly(self, Target, this)` with a default-constructed `Target`, and `BallisticMissileFly` reads `t.CenterPosition` in its constructor (`:45`) — the exact `InvalidOperationException: Attempting to query the position of an invalid Target` transcribed into `MissileSpawnerMaster.cs:85-87`.
- **`NukePower` is out** for the actor tier — it adds an *effect*, not an actor (§6). It is exactly right for the Kinzhal.

**So the feature needs one new support power.** Shape, modelled directly on `AirstrikePower.Activate` (`:74-148`):

1. Take `order.Target.CenterPosition` as the aim point.
2. Pick a spawn `WPos` on the map edge (`AirstrikePower.cs:74-102` already computes an edge entry point and a facing from the target; that arithmetic is reusable verbatim).
3. `w.CreateActor(false, info.MissileType, { CenterPositionInit, FacingInit, OwnerInit })`.
4. `actor.Trait<BallisticMissile>().Target = Target.FromPos(aimPoint)` — **before** `w.Add(actor)`, because `AddedToWorld` consumes it.
5. `w.Add(actor)`.

Estimate: ~80 lines plus an Info class. No new interfaces, no engine-wide changes. The `MissileSpawnerMaster` precedent means step 4's ordering constraint is already documented in-tree.

**Alternative worth pricing before committing:** generalise `SpawnActorPower` with two fields — `SpawnFromMapEdge: true` and a target-setting hook — instead of a new trait. That is smaller, but it puts map-edge logic on a trait whose whole current identity is "spawn here". I lean to the new power; either is defensible.

### 2.4 Does the actor-missile inherit the A-10 strafe failure? **No. Cleanly no.**

The failure documented at `WORKSPACE/recon/powers-and-preloaded-transports.md` §1.3 is: `StrafeAttackRun` aims at a `TargetType.Terrain` target, `WeaponInfo.IsValidAgainst` resolves that to the cell's `TargetTypes` (`Ground`), `30mm.A10` does not list `Ground`, and `Armament.CanFire` refuses (`Armament.cs:402`). I re-verified the weapon side at `2c8488ef`: `30mm.A10` (`weapons-ballistics.yaml:719`) inherits `^30mm`, whose `ValidTargets: Infantry, Vehicle, Defense` (`:579`) has no `Ground`. **The prior recon is correct and still current.**

An actor-missile never touches that path. Detonation goes through `Explodes`:

```csharp
// engine/OpenRA.Mods.Common/Traits/Explodes.cs:133
// Use .FromPos since this actor is killed. Cannot use Target.FromActor
weapon.Impact(Target.FromPos(self.CenterPosition + Info.Offset), source);
```

`weapon.Impact` is called directly. There is no `Armament`, no `CanFire`, no weapon-level `IsValidAgainst` gate on whether the detonation happens at all. The weapon's `ValidTargets` is applied **per victim, inside each warhead** (`Warhead.cs:55-57`, `:74`), which is a filter on who takes damage — not a veto on firing.

That is why `IskanderExplosion` can carry `ValidTargets: Ground, Trees, Water` (`weapons-explosions.yaml:523`) and still work perfectly when the missile lands on bare dirt, while `A10.Airstrike` with an analogous list fires nothing. **Different mechanism, opposite outcome.**

---

## 3. Q3 — "AA vehicles yes, MANPADs no"

### 3.1 Every gate that must line up (the manager's question 1)

An interception requires **six** independent conditions. I walked each in the engine rather than inferring them.

| # | Gate | Where | On a missile today |
|---|---|---|---|
| 1 | The missile is in the actor position index | `BallisticMissile.cs:218` → `ActorMap.cs:593` | ✅ (§2.2) |
| 2 | Owner relationship — `AppearsHostileTo` | `AutoTarget.cs:1383` | ✅ (slave inherits the launcher's owner) |
| 3 | Visibility — `CanBeViewedByPlayer` | `AutoTarget.cs:1388`; `Actor.cs:642-650` | ✅ via `Detectable` (`Vision: 1`, `Radar: 1`), the `IDefaultVisibilityInfo` implementor (`Traits/Modifiers/Detectable.cs:22`) |
| 4 | **`Targetable.TargetTypes`** on the missile | `defaults.yaml:1083-1089`; union at `Actor.cs:661-669` | ✅ `ICBM` |
| 5 | **`AutoTargetPriority.ValidTargets`** must overlap those types | `AutoTarget.cs:1275` — `if (!ati.ValidTargets.Overlaps(targetTypes) \|\| ati.InvalidTargets.Overlaps(targetTypes))` | ❌ for AA vehicles and MANPADs; ✅ only for CRAM/AGUN/SAM |
| 6 | **`WeaponInfo.ValidTargets`** must admit them, at `Armament.CanFire` | `WeaponInfo.cs:261-263`; `Armament.cs:402` | ❌ for `Stinger`/`9M311`/`MANPAD`; ✅ for `20mm_CRAM`/`AACannon`/`SurfaceToAirMissile` |

**Gates 5 and 6 are genuinely separate, and this is the load-bearing detail for the whole feature.** Gate 5 decides whether a unit *goes looking*; gate 6 decides whether it *may pull the trigger*. Opening only 6 gives a weapon that works on a manual force-fire order and never fires by itself. Opening only 5 gives a unit that drives toward the missile and then refuses to shoot. **Both must be opened, in both files, for every actor you want to intercept.**

There is no seventh accidental gate: `AutoTargetPriorityInfo.ValidTargets` defaults to `new("Ground", "Water", "Air")` (`AutoTargetPriority.cs:21`), which does **not** include `ICBM`. So bare priority entries written without a `ValidTargets` line — e.g. `^AutoTarget`'s `AutoTargetPriority@FireAtWill: Priority: 1` (`defaults.yaml:413-414`) — cannot leak ICBM engagement to anything. The isolation is airtight by default.

Two further notes:

- **Scanning is fast enough.** `AutoTarget` rescans every 3–8 ticks when idle (`AutoTarget.cs:199-202`) and every 25 ticks while already engaged (`PreemptScanInterval: 25`, `defaults.yaml:412`). A missile spends 40–70 ticks inside an 18-cell ring (§4.4). Detection latency is not the problem.
- **The scan radius is the armament's own maximum range** (`AutoTarget.cs:1177`, `ab.GetMaximumRange()` when `ScanRadius <= 0`), and `FindActorsInCircle` is explicitly **2D** — `new WVec(r, r, WDist.Zero)` and `HorizontalLengthSquared` (`WorldUtils.cs:81-84`). **Altitude does not shrink the engagement ring.** A missile passing 8 cells overhead is "in range" of an 18-cell gun the moment it is within 18 cells horizontally. That is good news and it is not obvious.

### 3.2 What actually does anti-air today

**Infantry MANPADs — both disabled, and both correctly excluded from ICBM:**

| Actor | Where | Weapon | AutoTarget chain |
|---|---|---|---|
| `^AA` (template) | `infantry.yaml:1822` | `MANPAD` (`:1841`) | `^AutoTargetAir` (`:1823`) |
| `AA` | `infantry.yaml:1888` | ″ | ″ |
| `AA.america` | `infantry-america.yaml:74` | ″ | ″ |
| `AA.russia` | `infantry-russia.yaml:74` | ″ | ″ |

`MANPAD` weapon: `ValidTargets: Air`, no `ICBM` (`weapons-missiles.yaml:481-484`). `^AutoTargetAir` (`defaults.yaml:739-745`) inherits `^AutoTarget` and adds `AutoTargetPriority@Air: ValidTargets: Air, Priority: 2` (`:587-591` — the file declares `^AutoTargetAir` twice and the two entries merge, which is commented in-tree at `:741-743`). **No entry in that chain lists `ICBM`.**

Note `^AA` also carries `Buildable.Prerequisites: ~disabled` (`infantry.yaml:1829`), so MANPAD infantry is not currently buildable either.

**AA vehicles — two, both excluded from ICBM:**

| Actor | Where | Weapons | AutoTarget |
|---|---|---|---|
| `strykershorad` | `vehicles-america.yaml:874` | `25mm.Bradley` (`:944`), `Stinger.quad` (`:976`) | `^AutoTargetAAIFV` (`:879`) |
| `tunguska` | `vehicles-russia.yaml:823` | `30mm.Tunguska.AG` (`:892`), `30mm.Tunguska.AA` (`:900`), `9M311` (`:938`) | `^AutoTargetAAIFV` (`:828`) |

`^AutoTargetAAIFV` (`defaults.yaml:460-474`) inherits `^AutoTargetGroundAntiTank` and defines priorities for `Helicopter`, `Aircraft`, `Vehicle`, `Infantry`. Its inherited `@Default` is `ValidTargets: Vehicle, Defense, Water, Underwater` (`:693-697`). **`ICBM` appears nowhere.** And `Stinger` (`weapons-missiles.yaml:529-531`) is `ValidTargets: Air`, inherited unchanged by `Stinger.quad` (`:565`) and `9M311` (`:599`); `30mm.Tunguska.AA` is `ValidTargets: Helicopter` (`weapons-ballistics.yaml:701`).

**The only ICBM-capable actors — all four gated off:**

| Actor | Where | Weapon | Cost | Buildable |
|---|---|---|---|---|
| `CRAM` | `structures-defenses.yaml:622` | `20mm_CRAM` (`:656`) | 1000 | `Prerequisites: ~disabled` (`:643`) |
| `AGUN` | `:707` | `AACannon` (`:741`) | 800 | `~disabled` (`:729`) |
| `SAM` | `:784` | `SurfaceToAirMissile.double` (`:823`) | 2000 | `~disabled` (`:814`) |
| `HSAM` | `:839` | `SurfaceToAirMissile` (`:902`, commented) | 3000 | `~disabled` (`:911`) |

All three live ones inherit `^AutoTargetAirICBM` (`:628`, `:713`, `:790`), whose `AutoTargetPriority@Default: ValidTargets: Air, AirSmall, ICBM` (`defaults.yaml:747-750`) is the only place in the mod that opens gate 5 for `ICBM`.

**Confirmed: the manager's read is right, and the consequence is worth stating plainly — `iskander` and `HIMARS` are buildable at `~techlevel.high` for 6000 each, and nothing a player can build can stop their missiles.** The interception system is fully authored and 100% switched off.

Aircraft also carry `Air, ICBM` weapons (`AirToAirMissile` at `weapons-missiles.yaml:451`, `20mm_CRAM` on the F-16 at `aircraft-america.yaml:638` and the Russian twin at `aircraft-russia.yaml:652`), but they auto-target through their own chains — I did not audit whether any fighter lists `ICBM` at gate 5, and it is orthogonal to the user's question. Flagged, not resolved.

### 3.3 Blast radius of the cheap fix (the manager's question 3)

**`25mm.Bradley`** (`weapons-ballistics.yaml:605`) — live users:

```
mods/ww3mod/rules/ingame/vehicles-america.yaml:375   bradley      (actor at :298)
mods/ww3mod/rules/ingame/vehicles-america.yaml:944   strykershorad
mods/ww3mod/rules/ingame/naval.yaml:258,328,398,468,535,547   ALL COMMENTED OUT
```

So adding `ICBM` to it arms **exactly two** actors: the SHORAD and the Bradley. Two, not "every Bradley variant" — but the Bradley is an IFV that has no business intercepting missiles, and the `# 30mm.Stryker` comment the manager spotted at `vehicles-america.yaml:944` confirms a dedicated Stryker weapon was always intended. **A per-actor variant is the right shape** and the file is already asking for it.

**`30mm.Tunguska.AA`** (`weapons-ballistics.yaml:698`) — used by `tunguska` alone (`vehicles-russia.yaml:900`). **Zero blast radius**; the AG/AA split already did this work.

**`Stinger` family:** `Stinger` (`:529`) is inherited by `Stinger.quad` (`:565`, SHORAD only) and `9M311` (`:599`, Tunguska only), and used directly only in commented naval blocks (`naval.yaml:340, 410, 480`). Adding `ICBM` to `Stinger` itself arms both AA vehicles' missiles and nothing else. Adding it to the two children separately is equally cheap and more legible.

**But §4 says none of this actually works.** The gun keys cannot hit; the Stinger children are too slow to catch.

### 3.4 Would `ValidTargets: Air` break, or accidentally open, anything?

- **Adding `ICBM` to a weapon cannot make it stop hitting aircraft.** `IsValidTarget` is `ValidTargets.Overlaps(targetTypes) && !InvalidTargets.Overlaps(targetTypes)` (`Warhead.cs:55-57`; same shape for `WeaponInfo` at `:261-263`). Union semantics — adding a type only widens. ✅ No regression risk.
- **A MANPAD cannot accidentally gain ICBM.** Its weapon lists `Air` only, and `^AutoTargetAir` lists `Air` only. Both would have to be edited. ✅
- **The one real interaction is the reverse of the worry: `ICBM` is already treated as an air-domain type by the AI danger layer**, and adding it to a *ground* weapon would corrupt that:

```csharp
// engine/OpenRA.Mods.Common/Traits/World/DangerFieldLayer.cs:123-131
//   - ICBM: the interceptor/anti-missile marker. Pure anti-air weapons carry
//     "Air, ICBM" (20mm_CRAM, AACannon, SurfaceToAirMissile, AirToAirMissile). It is an
//     air-domain type, NOT a ground target — excluding it stops every SAM/CRAM/interceptor
//     stamping a spurious anti-ground aura at full AA range.
static readonly string[] AirDomainTypes = { AirType, HelicopterType, IcbmType };
// :142-149  WeaponThreatensGround(): true if ANY ValidTargets entry is not air-domain
```

`WeaponThreatensGround` returns true if **any** valid target is outside the air domain. `25mm.Bradley` already lists `Infantry, Vehicle, Defense` (via `^30mm`, `weapons-ballistics.yaml:579`), so it already threatens ground and adding `ICBM` changes nothing there. ✅ But if a future dedicated interceptor is written air-only, it must be `Air, ICBM` and nothing else, or the AI will stamp an anti-ground danger aura at full interceptor range. Worth a comment on the new weapon.

---

## 4. ⚠️ Would it actually connect? (the manager's question 2 — and the answer inverts the premise)

### 4.1 The target's speed

`IskanderMissile` (`vehicles-russia.yaml:1125-1141`): `Speed: 600`, `Acceleration: 3`, `InitialSpeedPercent: 0`, `TerminalSpeed: 600`, `TerminalAcceleration: 10`, `LaunchAngle: 110`.

`BallisticMissileFly` accelerates at `Acceleration` until `progress >= 0.5`, then at `TerminalAcceleration` capped at `TerminalSpeed` (`BallisticMissileFly.cs:213-224`). Integrating that:

| Shot distance | Flight time | Speed at apex | Speed at impact | Arc apex altitude |
|---|---|---|---|---|
| 20 cells (20480) | ≈109 ticks (6.5 s) | ≈248 WDist/tick | ≈516 WDist/tick | 4096 WDist (4 cells) |
| 40 cells (40960) | ≈156 ticks (9.4 s) | ≈350 | 600 (capped) | 8192 (8 cells) |

Apex from `BallisticMissileFly.cs:89-91`: `arcPeakHeight = hDist × LaunchAngle.Tan() / 4096`; `WAngle(110)` = 38.7°, `Tan()` returns tan×1024 = 819, so apex ≈ 0.20 × hDist.

**Terminal speed 516–600 WDist/tick = 0.50–0.59 cells/tick = 8.4–9.8 cells/s.** For scale, a Littlebird cruises at 265 (`weapons-ballistics.yaml:715`), and the mod already documents that the Tunguska's gun cannot hit *that*.

`HIMARSMissile` (`vehicles-america.yaml:1221-1231`): `Speed: 500`, `Acceleration: 4`, `InitialSpeedPercent: 3`, `TerminalSpeed: 550`, `TerminalAcceleration: 7`, `LaunchAngle: 80` (apex ≈ 0.134 × hDist). Slightly slower and much flatter.

### 4.2 Guns do not lead. At all.

```csharp
// engine/OpenRA.Mods.Common/Projectiles/Bullet.cs:201
target = args.PassiveTarget;
```

`PassiveTarget` is the target's centre at the instant of firing (`Armament.cs:572`, `:627`, `:659`). A `Bullet` flies to a fixed point. The mod already knows this and says so in a tuning comment:

```yaml
# mods/ww3mod/rules/weapons/weapons-ballistics.yaml:713-716
# COUNTER-INTUITIVE ... Bullets do not lead (Bullet.cs:200 aims at the target's
# position at fire time) and do not collide en route, so the aim point is displaced by
# speed x flight-time. Wide scatter accidentally bridges that displacement; tight
# scatter cannot. A Littlebird at its 265 u/tick cruise is unhittable by this gun at
# ANY Inaccuracy
```

Lead error = `(range / bullet speed) × missile speed`. Against an Iskander at 516 WDist/tick, with the missile's `HitShape` default radius **426 WDist** (`HitShapes/Circle.cs:28`):

| Weapon | Bullet speed | Range | Error at max range | Error at 5 cells | Error at 2 cells | Scatter |
|---|---|---|---|---|---|---|
| `20mm_CRAM` (CRAM) | 1c0 = 1024 | 22c0 | **11.4 cells** | 2.6 cells | 1.0 cell | 256 |
| `30mm.Tunguska.AA` | 900 | 18c0 | **10.6 cells** | 2.9 cells | 1.2 cells | 448 |
| `25mm.Bradley` (SHORAD) | 900 | 20c0 | **11.7 cells** | 2.9 cells | 1.2 cells | 312 |
| `AACannon` (AGUN) | 8c0 = 8192 | 20c0 | **1.3 cells** | 0.32 cells | 0.13 cells | 2048 |

**Three of the four guns miss by an order of magnitude more than the target's size, everywhere in their envelope.** The most striking case is `20mm_CRAM`: the weapon named for counter-rocket defence, mounted on the actor named `CRAM`, is the *worst* of the four because its muzzle velocity is the lowest in the mod. Only `AACannon`'s 8c0 muzzle velocity brings the lead error inside its own 2c0 scatter — it would land hits by accident, at a rate its `Burst: 10` might make tolerable, but I would not build a feature on it.

> **This inverts the manager's proposed discriminator.** "Cruise-missile defence is a gun problem, which is why CRAM is on the ICBM list" is doctrinally right and mechanically backwards in *this* engine, because `Bullet` has no lead solution. The `ICBM` entries on the two gun weapons are, on this reading, aspirational — they open gate 6 for a shot that cannot connect. **Hypothesis, not verified:** that they were added for `AutoTargetPriority` symmetry, and the intercept has only ever actually been scored by `SurfaceToAirMissile` on the `SAM` — which is precisely the one the in-tree comment at `weapons-effects.yaml:704-706` names ("a SAM intercept of a ballistic missile rendered nothing"). Confirming check: §8 run 1.

### 4.3 Homing missiles do lead, and their speed is the whole story

```csharp
// engine/OpenRA.Mods.Common/Projectiles/Missile.cs:1148-1149
var leadTarget = WVec.CalculateLeadTarget(pos, lastTargetPosition, targetPosition, 1, speed);
var tarDistVec = targetPosition + leadTarget + offset - pos;
```

`Missile` computes an intercept point every tick and steers to it (`HorizontalRateOfTurn`, `VerticalRateOfTurn` defaults 20 and 24 — `Missile.cs:99`, `:102`). So the question is purely kinematic: can it close?

| Interceptor | Speed | Accel | Launch spd | Range | vs Iskander 516–600 |
|---|---|---|---|---|---|
| `SurfaceToAirMissile[.double]` (SAM) | **800** | 35 | 50 | 35c0 | ✅ closes from any aspect |
| `Stinger.quad` (SHORAD) | 600 | 35 | 50 | 28c0, `RangeLimit: 30c0` | ⚠️ head-on / crossing only |
| `9M311` (Tunguska) | 600 | 35 | 50 | 28c0, `RangeLimit: 30c0` | ⚠️ same |
| `MANPAD` (infantry) | **450** | 25 | 20 | 23c0, `RangeLimit: 24c0` | ❌ cannot catch it |
| `AirToAirMissile` (fighters) | 800 | 35 | 400 | 30c0 | ✅ |

The 600-vs-600 case is not a rounding concern — the mod has already derived the consequence for these exact weapons:

```yaml
# mods/ww3mod/rules/weapons/weapons-missiles.yaml:588-593  (Stinger.quad), mirrored at :599-632 (9M311)
# 58 is the missile's MAXIMUM POSSIBLE LIFETIME ... Missile.cs:1159,1164 detonates
# the tick distanceCovered passes RangeLimit (30c0 = 30720), which is tick 58
```

A Stinger is culled at tick 58 whatever it is doing. In a stern chase against a 600-speed target the closing rate is ~0, so it fuel-outs behind the missile every time. Head-on and beam engagements work, and a static AA vehicle defending a point is usually in one of those geometries — but "usually" is not a countermeasure the user can rely on.

**`MANPAD` at 450 is the finding worth telling the user.** The "MANPADs cannot stop cruise missiles" rule is true *three* times over — target types (gates 4/5), weapon `ValidTargets` (gate 6), and raw kinematics. Whatever is done to the YAML, a MANPAD stays incapable unless someone deliberately raises its speed above 600. That is a design guarantee that will not silently rot.

### 4.4 Does the missile spend long enough inside the ring?

2D engagement rings (§3.1) and a terminal speed of ~0.5 cells/tick: a missile passing straight over an 18-cell AA site is inside for ≈70 ticks (4.2 s); one clipping the edge, ≈20 ticks. `SurfaceToAirMissile` needs ~21 ticks to reach full speed (`(800-50)/35`) plus flight time. **A 35-cell SAM has ample time; an 18-cell Tunguska is marginal on a passing shot and fine on an overhead one.** This is arithmetic, not observation — §8 run 1 is what would settle it.

### 4.5 So what should the countermeasure be?

Ranked, from the numbers above:

1. **A dedicated interceptor missile weapon (`Speed: 900–1000`, `ValidTargets: Air, ICBM`), on a new armament on the two AA vehicles.** Contained: a new weapon key touches nothing else, and it sidesteps both the gun-lead problem and the 600-vs-600 tie. This is what I would build.
2. Un-gate `SAM`/`CRAM`/`AGUN` and let static defences be the answer. Zero new content — four `Prerequisites` lines — but it is not what the user asked for, and `CRAM`/`AGUN` would be decorative (§4.2).
3. Add `ICBM` to `Stinger` and accept aspect-dependent interception. Cheapest; produces a countermeasure that works maybe half the time for reasons no player will ever deduce. I would not ship this.
4. Add `ICBM` to the gun keys. **Do not.** It cannot hit.

---

## 5. Q4 / manager question 4 — what happens when a missile dies in flight

### 5.1 Normal impact and interception are the same code path

```csharp
// engine/OpenRA.Mods.Common/Activities/BallisticMissileFly.cs:205-210
// Phase 2: Parabolic arc flight — one smooth trajectory from spawn to target
...
    sbm.SetPosition(self, targetPos);
    Queue(new CallFunc(() => self.Kill(self)));
```

The missile detonates by **killing itself**. So `Explodes`/`SpawnedExplodes` — the same traits an interceptor triggers — are what produce the normal impact too. The airborne/ground split is what distinguishes them.

### 5.2 The split, and what each actually fires

`IskanderMissile` (`vehicles-russia.yaml:1145-1154`):

```yaml
	SpawnedExplodes:
		Weapon: IskanderExplosion
		EmptyWeapon: VisualExplodeHusk
		RequiresCondition: !airborne
	Explodes:
		Weapon: IskanderExplosion
		RequiresCondition: airborne
```

`airborne` is granted by `BallisticMissile` above `MinAirborneAltitude` (default 5 WDist — `BallisticMissile.cs:97`; `AirborneCondition: airborne` at `defaults.yaml:1081`). `SetPosition(self, targetPos)` at impact drops the missile to ground level and revokes the condition before the queued `Kill` runs on the following tick.

So:

- **Normal impact →** `!airborne` → `SpawnedExplodes` → `IskanderExplosion` at ground level, with kill XP credited to the launcher (`SpawnedExplodes.cs:60`, `SourceActor = spawner`).
- **Intercepted in flight →** `airborne` → `Explodes` → `IskanderExplosion` at the intercept point, credited normally.

**Both fire the full warhead.** `EmptyWeapon: VisualExplodeHusk` never gets chosen: `SpawnedExplodes.ChooseWeaponForExplosion` (`:94-103`) returns `Info.WeaponInfo` immediately when the actor has no `Armament`, and a missile actor has none.

### 5.3 Does an intercept hurt the ground underneath?

**Warhead falloff is measured in 3D**, verified rather than assumed:

```csharp
// engine/OpenRA.Mods.Common/Warheads/SpreadDamageWarhead.cs:74, 94, 97
var distance = h.DistanceFromEdge(victim, pos).Length;      // not HorizontalLength
falloffDistance = victim.GetTargetablePositions().Min(x => (x - pos).Length);
falloffDistance = (victim.CenterPosition - pos).Length;
```

Same for `ShockwaveDamageWarhead.cs:188, 207, 210`. Altitude counts against the blast.

`IskanderExplosion` (`weapons-explosions.yaml:521-561`) reach:

| Warhead | Spread | Falloff steps | Max reach |
|---|---|---|---|
| `Warhead@Target` TargetDamage | 512 | — | direct hit only; `InvalidTargets: Air` |
| `Warhead@Spread_impact` SpreadDamage | 1024 | default `{100,37,14,5,0}` → 5 | `4 × 1024 = 4096` |
| `Warhead@Shockwave` ShockwaveDamage | 1c0, `MaxRadius: 4c0` | 7 | `min(4096, 6144) = 4096` |

Against §4.1's apex altitudes:

- **20-cell shot, apex 4096 up:** blast reach 4096. **Exactly borderline** — a unit directly beneath at apex sits at the outermost falloff step, which for `Spread_impact` is 0% (`Falloff[4] = 0`) and for the shockwave 5%. Essentially harmless, by a margin of nothing.
- **40-cell shot, apex 8192 up:** comfortably harmless.
- **Intercepted in the last ~2 s of the dive, below ~4 cells:** the warhead lands on the ground, roughly where it was headed. **Intercepting late buys the defender little.**

**Player-facing reading, which I think is good:** kill it early or high and it flashes harmlessly overhead; kill it in the terminal dive and you eat most of it anyway. That is a real skill gradient and it needs no code. But it is *accidental* — nobody tuned for it, the margin at 20 cells is zero, and a designer nudging `LaunchAngle` down would silently turn every intercept into a ground burst.

**`IskanderExplosionAirborne` is the fix someone started and abandoned** (`weapons-explosions.yaml:619-629`): a bare `Inherits: IskanderExplosion` with every warhead commented out, and **zero references anywhere in `mods/` or `engine/`**. Its commented body proposes `Range: 0, 4c0, 5c0 / Falloff: 100, 100, 0 / Damage: 6750` and `ValidTargets: ... ICBM` — a *wider*, weaker burst, i.e. an airburst that scatters. Pointing `Explodes` (the airborne branch) at a real version of this weapon is the one-line change that makes the intercept payoff deliberate instead of emergent. Same for `HIMARSExplosion`, which has no `Airborne` twin at all.

### 5.4 Nothing falls short

There is no debris, no ballistic continuation, no husk. `SpawnActorOnDeath` is on the *launchers* (`iskander.husk`, `HIMARS.husk`) and not on the missiles. The missile is removed at death and the warhead resolves at the death position. If "falls short and hits the ground" is wanted, it needs new work.

---

## 6. Q4 / manager question 5 — the Kinzhal

### 6.1 If the Kinzhal is a projectile, untargetable is free — and there is a shipped power for it

Per §1, a projectile cannot be targeted by anything, cannot be found by any scan, and cannot be hit by splash. "No countermeasure exists" is the default and requires no code.

**`NukePower` is exactly this shape and it already ships wired.** `NukePower.Activate` (`NukePower.cs:166-185`) constructs a `NukeLaunch` — `public class NukeLaunch : IProjectile` (`Projectiles/NukeLaunch.cs:21`) — and adds it as an **effect**. Never an actor. Per the prior recon (§1.7, re-verified: `structures-defenses.yaml:1107-1172`, `weapons-superweapons.yaml:28-386`) the whole stack is live and art-complete on `MSLO` — beacon, camera, `FlightDelay`, `FlightVelocity`, `DetonationAltitude`, notifications.

**A Kinzhal power is `NukePower` with a different weapon, a different image and a shorter `FlightDelay`.** It is by some distance the cheapest thing in this document. Its one gap versus the brief is "comes in from the map edge": `NukePower` ascends from the launcher and descends on the target, so a bodiless proxy owner would make it descend out of the sky rather than streak in laterally. Whether that reads as hypersonic is a visual call for the user, not a mechanism problem.

### 6.2 If both tiers must share the actor shape

Three ways to make an actor-missile untargetable, in order of preference:

1. **Give it a target type nothing lists** — `Targetable@Ground/@Airborne: TargetTypes: Hypersonic`. Gate 4 passes but gates 5 and 6 fail for every weapon in the mod, so nothing engages it and nothing can force-fire at it either. **Cleanest**: the actor keeps a place in the target-type vocabulary, `AutoTargetPriority`/weapon lints keep working, and the day someone wants a Kinzhal counter it is a one-line opt-in rather than an archaeology exercise. Costs nothing; add it to `DangerKernelMath.AirDomainTypes` (`DangerFieldLayer.cs:129`) at the same time so the AI does not misread a future interceptor.
2. **Remove `Targetable` entirely.** `GetEnabledTargetTypes()` returns an empty `BitSet` (`Actor.cs:661-669`); `ValidTargets.Overlaps(empty)` is false everywhere, so nothing can target it — **and nothing can splash it either**, since `Warhead.IsValidTarget` uses the same union (`Warhead.cs:55-57, 74`). Total immunity, which is arguably what "no countermeasure exists" means. **What it does not break:** `Detectable` is `IDefaultVisibilityInfo` and independent of targeting (`Traits/Modifiers/Detectable.cs:22`), so radar and fog behave; `HitShape` only `Requires<BodyOrientationInfo>` (`HitShape.cs:23`), not `Targetable`; `Health`, `Armor` and `Explodes` are independent (`Explodes` only `Requires<IHealthInfo>`, `Explodes.cs:24`). So it is safe — I just prefer (1) for legibility.
3. **Sheer speed.** Do not rely on this. `FindActorsInCircle` is 2D and rings are large; a fast missile is still inside a 35-cell SAM ring for ~35 ticks. Speed makes interception *hard*, never *impossible*, and "cannot be targeted" was stated as an absolute.

**Recommendation:** Kinzhal as a `NukePower`-shaped projectile (6.1). If the user wants both tiers to look and behave alike enough that they must share the actor shape, use (1).

---

## 7. Where the prior recon needs correcting

`WORKSPACE/recon/powers-and-preloaded-transports.md` was researched at `main @ d421e4ca`. Re-checked at `2c8488ef`:

- **§1.3 (A-10 strafe failure) — correct and still current.** `30mm.A10` (`weapons-ballistics.yaml:719`) inherits `^30mm`, `ValidTargets: Infantry, Vehicle, Defense` (`:579`), no `Ground`. `Armament.cs:402` still refuses. Unchanged.
- **§1.3's scope should be narrowed, though.** It reads as a general warning about spawned-unit powers. It is not: it is specific to `Armament`-mediated attacks. Anything detonating through `Explodes` bypasses it entirely (§2.4). A reader planning missile powers off that section alone would over-price them.
- **No correction needed elsewhere.** I did not re-verify §§1.4–2.3, which are orthogonal to this question; the `SupportPowerManager` facts in §1.4/§1.5 are the ones a Powers-menu implementation would lean on and nothing here affects them.
- **§0 item 6 ("nuclear strikes are far more built than the brief assumes") is doubly true for this feature** — it is also the ready-made Kinzhal (§6.1), which that document had no reason to notice.

---

## 8. Files touched, and runs I would want

**YAML files touched: none.** This branch adds one file, `WORKSPACE/recon/powers-interception.md`. No rules, weapons, scenarios or engine code were modified, so the YAML gate's verdict on this branch is unchanged from `main @ 2c8488ef`.

Everything above is static analysis. Three things I could not settle without running the game, in priority order.

### Run 1 — the one that matters: can anything actually intercept an Iskander? ⚠️

**Why:** §4 says guns cannot and Stingers can only sometimes. That is arithmetic over `Bullet.cs:201`, `Missile.cs:1148` and the tuning tables. It has never been observed. **If run 1 says a Tunguska routinely kills an inbound Iskander, §4 is wrong and the whole cheap-fix path opens up.** This is the highest-value hour on the feature.

A new scenario is needed — `tools/autotest/scenarios/` has 289 entries and none mentions ICBM interception (`grep -rn "ICBM" tools/autotest/scenarios/` → no hits). Good templates exist: `test-shorad-single-missile` and `test-tunguska-single-missile` already stand one AA vehicle against one air target and count missiles.

Proposed `test-icbm-intercept`: one `iskander` at one end, a target marker ~30 cells away, and one interceptor under the flight path, with five arms — `SAM` (ICBM-listed today), `AGUN`, `CRAM`, `tunguska` and `strykershorad` (the last two with `ICBM` added to `9M311` / `Stinger.quad` in the scenario's local `weapons.yaml`). Fire 10 missiles per arm.

```
./run-test.sh test-icbm-intercept
```

**What counts as the answer:** intercepts per 10 launches, per arm. My prediction, on the record so it can be falsified: **SAM ≥ 7/10; AGUN 1–3/10; CRAM 0/10; Tunguska and SHORAD ≤ 2/10 and only when the missile passes overhead rather than across.** Anything above 5/10 for a gun arm refutes §4.2, and I would want to know which premise broke — most likely candidate is that `Burst` + `Inaccuracy` bridges the lead the way the Tunguska tuning comment (`weapons-ballistics.yaml:713-716`) says wide scatter accidentally does.

### Run 2 — does an intercepted missile hurt the ground beneath it?

**Why:** §5.3 computes the blast reach as 4096 and the 20-cell apex as 4096. That is a zero margin, derived from `Falloff` defaults and a `Tan()` scaling. It decides whether interception feels like a save or a shrug.

Extend run 1's scenario: park three infantry directly under the arc apex and three under the two-thirds point, and log their HP after each intercept.

**What counts as the answer:** HP loss at apex should be **0**, and at two-thirds materially non-zero. If apex units die, `IskanderExplosionAirborne` stops being a nicety and becomes required before the feature ships.

### Run 3 — the YAML gate, at merge, on whoever implements this

Not for me — this branch changes no YAML. Noting it because §3.3's fix touches `defaults.yaml` and a weapons file, and `^AutoTargetAir` is already a documented duplicate-key merge (`defaults.yaml:587` and `:739`). Any new `^AutoTarget*` entry must be checked against both.

```
./utility.sh --check-yaml
```
