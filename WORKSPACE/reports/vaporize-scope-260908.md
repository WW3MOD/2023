# Vaporize scope: what it should destroy, and what it must not

**Date:** 2026-09-08 · **Base:** `wt/vaporize-scope`, forked from `main @ 87728dc7` · **Read-only research.**
Every claim below carries a `file:line` I read on this branch. Findings marked **[inferred]** are reasoning
from code I read, not behaviour I observed — nothing here was run, no game was launched.

---

## Recommendation

**Yes, make vaporize universal — but the gate is not "everything", it is *has a Health trait*, and there are
two explicit exemptions.** That is three edits, not a list that rots.

1. **Override `IsValidAgainst` in `VaporizeWarhead`** to skip the target-type test (keep `AffectsParent` and
   the relationship test) and require `HasTraitInfo<IHealthInfo>()`. There is an exact precedent for the
   shape at `engine/OpenRA.Mods.Common/Warheads/DamageWarhead.cs:57-64`, and an exact precedent for the
   semantics in `DoomsdayStrike.Annihilate()` (`engine/OpenRA.Mods.Common/Traits/World/DoomsdayStrike.cs:618-632`),
   which already kills everything on the map filtered on exactly `HasTraitInfo<HealthInfo>()`.
2. **`-Vaporizable:` on `SUPPLYROUTE`** (`mods/ww3mod/rules/ingame/structures.yaml:273`).
3. **`-Vaporizable:` on `^ShootableMissile`** (`mods/ww3mod/rules/defaults.yaml:1080`), which covers all 19
   in-flight missile bodies in one line.

The trait's own presence is already the opt-out mechanism — `VaporizeWarhead.cs:83` does
`victim.TraitOrDefault<Vaporizable>()?.Begin(...)`, a no-op when the trait is absent — so **no new engine
field is needed**. Your prior ("default-on with an explicit opt-out marker") is correct, and the codebase
makes it easy rather than awkward.

**Answering "how many actors would need the marker": two edits, covering 20 actors.** The large healthless
class (crop fields, rocks, waypoints, spawn areas, cameras, flags — thousands of actors on a dense map)
needs **zero** authoring, because the `Health` predicate removes all of it by construction.

### Before the list: the mechanism is not wired to any nuke

`VaporizeWarhead` is instantiated **zero times** in `mods/`. I checked both spellings —
`grep -rn ": Vaporize\b" mods/` and `": VaporizeWarhead"` each return 0. `mods/ww3mod/rules/defaults.yaml:4`
says so itself: *"INERT until a VaporizeWarhead calls into it — nothing in the shipped ruleset does yet."*
The only live instantiation in the repo is `tools/autotest/scenarios/demo-vaporize/weapons.yaml:24,55`.

**The `Warhead@Vaporize:` blocks throughout `weapons-nuclear-arsenal.yaml` are typed `SpreadDamage`** (e.g.
`:358`), not `Vaporize`. They are ordinary damage warheads that happen to carry the name. So "the nuke's
vaporize doesn't hurt trees" is not a fact about `VaporizeWarhead` at all — it is a fact about a `SpreadDamage`
warhead with `InvalidTargets: Trees` on it (`weapons-nuclear-arsenal.yaml:365`). **The work is therefore
"adopt the mechanism", not "widen it", and there is no shipped behaviour to regress.**

---

## The exemption list

### 1. `SUPPLYROUTE` — CONFIRMED, and disqualifying for a stronger reason than you gave

Your argument holds. The code makes it worse than "no counter": it is **immediate defeat**, not merely a
production lockout.

- `SUPPLYROUTE` carries `MustBeDestroyed: RequiredForShortGame: true` (`structures.yaml:326-327`).
- `PlayerExtensions.cs:22-23` defines a player with no such actor as having no required units.
- `ConquestVictoryConditions.cs:76-77` marks that player's objective failed.

So one vaporize on an enemy beachhead does not slow them down — it ends their game on the spot.

**One correction to your premise, which strengthens rather than weakens it.** You wrote (and `CLAUDE.md`
agrees) that `SupplyRouteContestation` "slows and eventually freezes production without ever transferring
ownership". The ownership half is right. The rest under-sells it — the trait's own `[Desc]` at
`engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs:21-26` reads:

> *"When control bar is fully depleted, a defeat bar fills. At 100% defeat bar, the player becomes passive if
> a teammate still holds an active Supply Route (they can be relieved), and is defeated outright if nobody is
> left to relieve them."*

Contestation **is** the win condition already, implemented as a 30–90 s contested siege
(`BaseTicks: 1500`, `MinTicks: 500`, `structures.yaml:357-358`) that is relievable and reversible
(`BaseRecoveryTicks: 3000`, `FriendlyRecoveryMultiplier: 3`, `:363-364`). A vaporize would not make a
mechanic pointless — it would replace a designed, counterable siege with an instant purchase.

**How it survives today, which is not what the docs imply.** `CLAUDE.md` calls the SR "indestructible".
The code has no invulnerability on it at all: it has `Health: HP: 75000` (`structures.yaml:345-346`) and
`Armor: Type: Indestructable` (`:368-369`) — but that armour type appears in **zero** `Versus:` tables
anywhere in `mods/ww3mod/rules/weapons/`, and `DamageWarhead.DamageVersus` only applies a modifier when the
type is a key in the dict (`DamageWarhead.cs:105-109`). The armour type is inert naming.

What actually protects it is **`Targetable: TargetTypes: NoAutoTarget`** (`:347-348`), its only `Targetable`
trait — it inherits `^ExistsInWorld`, `^SpriteActor` and `^SelectableBuilding` at `:274-276`, none of which
declares one. `Warhead.IsValidAgainst` requires an overlap between the warhead's `ValidTargets` and
`Actor.GetEnabledTargetTypes()` (`Warhead.cs:74-76`, `Actor.cs:661-669`), and **no weapon in the mod lists
`NoAutoTarget` as a valid target** (0 hits across `rules/weapons/`). It is untargetable, not tough — the same
idiom `^TechBuilding` uses and documents at `structures.yaml:162-173`. `DoomsdayStrike.cs:151-152` records the
same fact independently.

**Consequence for the fix:** the exemption cannot be left implicit. Once `VaporizeWarhead` stops consulting
target types, `SUPPLYROUTE`'s only protection is gone — and it already carries `Vaporizable` via
`^ExistsInWorld`. It goes from *unkillable by anything* to *killable by the cheapest nuke* in one commit.

### 2. In-flight missile bodies — the class you did not name, and I would rank it second

19 actors inherit `^ShootableMissile` (`defaults.yaml:1080`), which inherits `^ExistsInWorld` at `:1081` —
so **every missile in the game already carries `Vaporizable`**. They are deliberately unhittable: 30 sites
declare `TargetTypes: Hypersonic`, and `Hypersonic` is listed by **zero** weapons. `doomsday.yaml:60-65`
states the intent verbatim — *"an actor with NO target types is invalid for every weapon, splash included —
it would fall through its own salvo untouched"*. (Contrast the template default `ICBM`, which SAMs *do* list,
`weapons-ballistics.yaml:547`.)

Three facts combine badly:

- `Warhead.AffectsParent` only protects `victim == firedBy` (`Warhead.cs:66-67`). **Sibling warheads in the
  same salvo are not the parent.**
- `VaporizeWarhead`'s radius test is **horizontal-only and ignores altitude entirely**, deliberately and with
  a comment saying so (`VaporizeWarhead.cs:75-77`). A missile at 38 cells' altitude passing over an impact
  point is inside the circle.
- The largest shipped fireball radius is `15c883` on `NukeTsarBomba`
  (`weapons-nuclear-arsenal.yaml:2897`) — a ~16-cell horizontal disc.

**[inferred]** So a MIRV (`NukeSarmatMIRV`, `:3143`) or a Dead Hand salvo — two waves, `WithinWaveTicks: 2`,
`OutlierToCityPauseTicks: 40` (`DoomsdayStrike.cs:169,175`) — would vaporize its own later warheads as they
descend through an earlier fireball's footprint. That reads to a player as a nuke that randomly did nothing,
and it would break `DoomsdayStrike`'s scheduled-arrival arithmetic, which computes launch times from
`EstimateArcTicks` on the assumption every RV arrives (`doomsday.yaml:42-46`).

One line on `^ShootableMissile` closes all 19.

### 3. Healthless actors — a hard engine constraint, and the one that fails SILENTLY

This is the most important finding in the report, and it is the "silent is worse" case the brief asked for.

`Vaporizable.Tick` ends with `self.Kill(attacker, damageTypes)` (`Vaporizable.cs:187`). **`Actor.Kill`
returns immediately when the actor has no `Health` trait** (`engine/OpenRA.Game/Actor.cs:634-640`:
`if (Disposed || health == null) return;`). Nothing else clears the trait's `active` flag — `TraitDisabled`
does (`:242-247`) but nothing disables it — and `Sample()` past `Delay + Duration` clamps `t` to 1 and returns
a silhouette at alpha `1 - 1×(2-1) = 0` (`Vaporizable.cs:146-164`).

**Net effect: a healthless actor given this trait fades to fully transparent and stays alive and functional
forever**, paying an `IRenderModifier` pass every frame for the rest of the match. Nothing logs. Nothing
fails. It just becomes invisible.

The population is large and load-bearing:

| Actor class | Site | Note |
|---|---|---|
| `^CivField` (6 inheritors) | `civilian.yaml:150`; `Health` commented out at `:189-190` | **3187 of river-zeta's 4544 actors** (`civilian.yaml:158`) |
| `^Rock` (9 inheritors) | `decoration.yaml:221-244` | blocking scenery, no `Health` |
| `spawnarea` | `misc.yaml:255-270` | carries the `SpawnArea` trait `ProductionFromMapEdge.cs:57-59,100,118` reads to pick the reinforcement edge |
| `waypoint` | `misc.yaml:272-284` | **62 scenario directories reference `waypoint`/`spawnarea`** |
| `mpspawn` | `misc.yaml:239-253` | |
| `CAMERA`, `camera.paradrop`, `camera.paradrop.detector`, `camera.spyplane` | `misc.yaml:150-175` | support-power vision proxies |
| `FLARE`, `RAILMINE`, `DRILLMINE` | `misc.yaml:177,197,215` | |
| `CTFLAG` | `misc.yaml:294-298` (`-Health:`) | |

None of these carries `Vaporizable` today (none reaches `^ExistsInWorld`), so **the bug is latent, not live**
— I checked all 19 missile bodies and every one declares `Health`, so nothing currently reachable hits it.
It becomes live the moment the trait is broadened. That is precisely why the fix should be a **`Health`
predicate on the warhead** rather than "put the trait on everything": the predicate exempts this whole class
with no per-actor authoring, and makes the zombie state unreachable by construction.

This is also the answer to the scenario question. Without the guard, a stray vaporize over a scenario's
waypoint cluster leaves the waypoints invisible but alive — Lua lookups keep succeeding, the test keeps
passing, and only the screenshot is wrong. With the guard, there is no hazard at all.

### 4. Trees — I would push back, but this one is your call, not mine

Trees are protected by **three independent layers**, which is why assuming one explanation covers both trees
and buildings is the error:

1. **No trait.** `^Tree` (`decoration.yaml:139-142,151`) inherits `^SpriteActor`, `^TreeCover`, `^1x1Shape`
   and `^BuildingAffectedByFire` — **never `^ExistsInWorld`** (0 occurrences of `ExistsInWorld` in that
   file). So trees have no `Vaporizable` at all and `VaporizeWarhead.cs:83` is a no-op against them.
2. **Wrong target type.** `Targetable: TargetTypes: Trees` (`decoration.yaml:185-186`), and the nuke's
   `Warhead@Vaporize` declares `ValidTargets: Ground, Water, Underwater, Air` plus an explicit
   `InvalidTargets: Trees` (`weapons-nuclear-arsenal.yaml:364-365`).
3. **Damage immunity.** `^TreeIndestructible` is `DamageMultiplier: Modifier: 0`
   (`decoration.yaml:135-137`), on all 22 `T##`/`TC0#` actors.

Layer 3 is *already* bypassed by the mechanism — `Health.Kill` passes `ignoreModifiers: true`
(`Health.cs:243-246`), and `Vaporizable.cs:184-186` says so explicitly. So making trees vaporizable is layers
1 and 2 only: two lines.

**The reason to hesitate is the ghost forest, and I verified it.** Of the three reasons trees were made
indestructible (`decoration.yaml:109-123`), two are about **husks** and therefore evaporate under vaporize,
which leaves none: the cover-bonus collapse (#2) and the passability flip (#3). Only #1 survives, and it gets
worse rather than better:

> `Map.DensityLayer` is stamped once, at map load. `SetDensityLayer()` is called from exactly one place
> (`engine/OpenRA.Game/Map/Map.cs:520`, defined `:1027-1053`). Both recompute paths are dead code, marked
> `CURRENTLY UNUSED (260503)`: `UpdateShadowForCells` (`:1073-1079`), the shadow recalc (`:1094`, `:1105`)
> and the density mutator (`:1260-1276`). The layer is summed along the sightline at `:1203-1238` and is read
> by `Detectable`, `FiringLOS`, `Armament` and `CohesionMoveModifier`.

So a vaporized forest keeps concealing units and keeps blocking line of fire, on ground where visibly nothing
stands. A burnt tree that still gives cover is odd; **an empty crater that still gives cover is a bug report.**
And the owner has already deferred the fix — `decoration.yaml:132-134`: *"DEFERRED, DO NOT BUILD: recomputing
forest shadow when trees die (owner: 'it gets complicated and it is not the right time now to invest time
into')."*

**My recommendation: leave trees out of the first pass, ship the rest, and revisit trees together with shadow
recompute.** If you would rather have them now, it is two lines (`Vaporizable:` on `^Tree`, and stop excluding
`Trees`) — but take the ghost forest knowingly.

**A trap if you do enable them:** the existing guards will not notice. `test-tree-indestructible` shoots trees
with an Abrams (`tools/autotest/scenarios/test-tree-indestructible/test-tree-indestructible.lua:146-245`) and
`engine/OpenRA.Test/TreeIndestructibleScopeTest.cs` is a static YAML-scope fixture. Both stay green while
trees become vaporizable, because *vaporizable* and *destructible* are different properties. Someone has to
extend them deliberately.

---

## Things that would newly die, and should

Not exemptions — listed so the blast radius is on the record.

**Neutral tech buildings — 5 live types** (`OILB`, `HOSP`, `FCOM`, `MISS`, `BIO`, plus their `.Husk`s and
`CTFLAG`), via `^TechBuilding` (`structures.yaml:160`). Same untargetable idiom as the SR but far less
severe: `TargetTypes: NoAutoTarget, C4, DetonateAttack, TechStructure` (`:193-194`), so they *are* killable
today by engineer C4 and by the `TechStructure` weapons in `weapons-heavy-ordnance.yaml`. Vaporizing them is
the point of the feature. Two notes: they are permanent (no restorable husk — that is the stated reason
`Structure` was withheld, `:189-192`), and `DoomsdayStrike` already aims at `oilb` on purpose
(`DoomsdayStrike.cs:129`), having reverted a B61 for being unable to kill one (`doomsday.yaml:92-98`).

**`^CivBuilding`, walls, tank traps, husks, barrels** — already declare `Ground`/`Structure`
(`civilian.yaml:17-22`, `structures-defenses.yaml:58`, `civilian.yaml:690`) and already die to nukes.
No change. Note `^CivBuilding` re-adds `Ground` and `Structure` on top of `^TechBuilding`, so ordinary
civilian buildings are **not** in the protected class despite the shared ancestor.

**`^SummonerDummy` / `^SummonBase`** (`defaults.yaml:1135,1175`) — both carry `^ExistsInWorld` and both have
`Health: HP: 1` (`:1159-1160`, `:1186-1187`), so they pass the guard and die. Harmless; they are transient
paradrop carriers.

## nav-guard: checked, and not a hazard in the failing direction

**[inferred]**, from `tools/nav-guard/README.md` plus the passability facts, not from running the tool.
The gate baselines `mods/ww3mod/maps` only and fails on *shrinkage* of the largest connected component. It
models two world states, live and `--state dead` (every destructible actor replaced by its husk). Vaporizing
scenery produces a **third** state that is strictly more passable than either: a living tree blocks vehicles
(`Building: Footprint: x`, `decoration.yaml:173-175`) and a husk blocks infantry as well
(`decoration.yaml:118-123`), while removal blocks neither. So vaporize can only *open* paths, and cannot trip
the gate.

The real consequence is a design one worth stating out loud: **nukes would carve permanent vehicle lanes
through forests**, on maps whose chokepoints were authored assuming woods are permanent. That is a balance
question for you, not a build failure. (`make nav-guard` is scenario-blind regardless — README, "the gate is
`mods/ww3mod/maps` only".)

---

## Contradictions found between the docs and the code

Per the brief, the code wins and the disagreement is itself reportable.

1. **`CLAUDE.md` calls `SUPPLYROUTE` "indestructible".** It is **untargetable** — full `Health: 75000`, never
   consulted. The distinction is load-bearing for exactly this task: an invulnerability would have survived a
   targeting bypass, and untargetability does not. `structures.yaml:162-173` already warns about this misread
   for the tech buildings (*"Do not 'fix' a weapon that cannot hurt one by raising its damage; check its
   ValidTargets"*); the same warning is owed at the SR.
2. **`CLAUDE.md` and this brief both describe contestation as slowing and freezing production.** It also
   defeats the player outright (`SupplyRouteContestation.cs:24-26`). Not wrong, but incomplete in the
   direction that matters here.
3. **`Armor: Type: Indestructable` on the SR (`structures.yaml:369`) is dead weight** — zero `Versus:` tables
   reference it, so `DamageWarhead.cs:105-109` never reads it. It reads as protection and provides none.
   Worth deleting or commenting; I changed nothing.
4. **Already-recorded, still live:** `weapons-superweapons.yaml` asserts in live comments that trees are
   "vaporized… instant husk" (`:291-292`, `:137`, `:781-782`). `DOCS/reference/conventions.md:376` and
   `WORKSPACE/DISCOVERIES.md:459-463` both flag this as an owed correction. Still owed.

---

## Handing up: what I could not answer without running

I did not launch the game, per the brief. Two things want a runtime check before this ships.

1. **Own-salvo interception.** `./tools/autotest/run-test.sh demo-doomsday-deadhand`, after wiring a
   `VaporizeWarhead` into `Atomic`. **Pass:** the impact count in the log equals
   `outliers + cities × WarheadsPerCity`. **Fail:** fewer impacts than warheads launched — a missile was
   vaporized in flight. I would run this one first; it is the failure I am least able to rule out by reading.
2. **Performance on a dense map.** `river-zeta`, `NukeTsarBomba` (`Radius 15c883`). `FindActorsInCircle` over
   a ~16-cell disc against 4544 actors, once per detonation. `doomsday.yaml:100-106` records that this exact
   dimension has caused a near-crash before (`NukeTsarBomba` at 6,786,649 actor-touches) and says the
   arithmetic must be re-run before raising either weapon. **Pass:** no frame-time spike at detonation. The
   `Health` predicate helps a lot here — it drops the 3187 crop fields before any per-frame work starts — but
   it does not remove the circle query itself.
