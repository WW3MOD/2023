# WW3MOD — weapons × armour-class matrix

Ranked defect list: [`260816-weapons-armour-matrix-defects.md`](260816-weapons-armour-matrix-defects.md).

Generated read-only from the resolved rulesets at `main` @ `d919c81a` (worktree `wt/weapons`).
Weapon inheritance, `-Removals` and duplicate-key merges are resolved exactly as `MiniYaml.Merge`
does them, so these are the values the engine loads — not the values a file reads top-down.

**Population.** 164 weapon definitions (33 abstract `^` templates, 131 concrete). 9 armour classes,
the complete set: `Unarmored Kevlar None Light Medium Heavy Wood Concrete Indestructable`. No `Armor`
trait in the mod carries a `RequiresCondition`, and no actor carries two — so the two 'fails open'
shapes that `conventions.md` warns about (conditional armour, multiple armour) have **zero instances**.

**Reachability tier** (column `T`) — the axis is player visibility, so every row is graded:

| tier | meaning | count |
|---|---|--:|
| `F` | **fieldable** — reachable from an actor a player can build or that a shipped map places | 63 |
| `L` | live in the ruleset but only from `~disabled` content | 18 |
| `D` | dead — no consumer anywhere in rules, maps, Lua or engine | 50 |

---

## Matrix A — `Versus`: damage % per armour class

`DamageWarhead.DamageVersus` (`Warheads/DamageWarhead.cs:96-109`) starts at 100 and filters the victim's
armours to the classes the table *lists*. So:

- `·` — the warhead declares **no `Versus` table at all**. `Versus.Count == 0` early-returns 100 (`:101-102`).
  100 % against every class. This is 198 of the 205 damage warheads in the mod.
- `—` — the table exists and **omits** this class. `ContainsKey` misses, the modifier sequence is empty,
  and the victim takes the unmodified **100 %** (`:105-108`). **This is the defect direction.**
- a number — explicit percentage. **0** — explicit immunity.

One row per damage warhead. `pen` in _italics_ means the field was omitted and the engine default of 1 applies.

| T | weapon | warhead | dmg | pen | Unarm | Kevlar | None | Light | Med | Heavy | Wood | Concr | Indest |
|:-:|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| F | `12.7mm.Hind` | Target | 600 | 15 | · | · | · | · | · | · | · | · | · |
| F | `12.7mm.Hind.AA` | Air | 300 | 5 | · | · | · | · | · | · | · | · | · |
| F | `12.7mm.MG` | Target | 600 | 15 | · | · | · | · | · | · | · | · | · |
| L | `20mm_CRAM` | Target | 600 | 40 | · | · | · | · | · | · | · | · | · |
| L | `20mm_CRAM` | Spread | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `25mm.Bradley` | Shrapnel | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `25mm.Bradley` | Target | 500 | 60 | · | · | · | · | · | · | · | · | · |
| F | `25mm.Bradley` | Spread | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `30mm.A10` | Shrapnel | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `30mm.A10` | Target | 1000 | 70 | · | · | · | · | · | · | · | · | · |
| L | `30mm.A10` | Spread | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `30mm.BMP2` | Shrapnel | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `30mm.BMP2` | Target | 500 | 60 | · | · | · | · | · | · | · | · | · |
| F | `30mm.BMP2` | Spread | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `30mm.Fighter` | Shrapnel | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `30mm.Fighter` | Target | 1000 | 70 | · | · | · | · | · | · | · | · | · |
| D | `30mm.Fighter` | Spread | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `30mm.Heli` | Shrapnel | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `30mm.Heli` | Target | 1000 | 70 | · | · | · | · | · | · | · | · | · |
| F | `30mm.Heli` | Spread | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `30mm.TimerWolf` | Shrapnel | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `30mm.TimerWolf` | Target | 1000 | 70 | · | · | · | · | · | · | · | · | · |
| D | `30mm.TimerWolf` | Spread | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `30mm.Tunguska.AA` | Shrapnel | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `30mm.Tunguska.AA` | Target | 1000 | 70 | · | · | · | · | · | · | · | · | · |
| F | `30mm.Tunguska.AA` | Spread | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `30mm.Tunguska.AG` | Shrapnel | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `30mm.Tunguska.AG` | Target | 1000 | 70 | · | · | · | · | · | · | · | · | · |
| F | `30mm.Tunguska.AG` | Spread | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `5.56mm.AR` | Target | 200 | 4 | · | · | · | · | · | · | · | · | · |
| F | `5.56mm.DMR` | Target | 200 | 4 | · | · | · | · | · | · | · | · | · |
| F | `5.56mm.DMR.silencer` | Target | 200 | 4 | · | · | · | · | · | · | · | · | · |
| F | `5.56mm.E3` | Target | 200 | 4 | · | · | · | · | · | · | · | · | · |
| F | `60mm_Mortar` | Shrapnel | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `60mm_Mortar` | Target | 3000 | 100 | · | · | · | · | · | · | · | · | · |
| F | `60mm_Mortar` | Spread | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `7.62mm.DMR` | Target | 250 | 5 | · | · | · | · | · | · | · | · | · |
| F | `7.62mm.MG` | Target | 250 | 5 | · | · | · | · | · | · | · | · | · |
| F | `7.62mm.Minigun` | Target | 250 | 5 | · | · | · | · | · | · | · | · | · |
| F | `7.62mm.Minigun` | Spread | 60 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `7.62mm.Minigun.AA` | Air | 150 | 4 | · | · | · | · | · | · | · | · | · |
| F | `7.62mm.Sniper` | Target | 350 | 5 | · | · | · | · | · | · | · | · | · |
| D | `73mm_BMP` | Shrapnel | 150 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `73mm_BMP` | Target | 5000 | 300 | · | · | · | · | · | · | · | · | · |
| D | `73mm_BMP` | Spread | 1000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `9M311` | Spread | 5000 | 20 | · | · | · | · | · | · | · | · | · |
| L | `AACannon` | Spread | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `ai.targeting.helper` | Spread | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `ai.targeting.helper.noattack` | Spread | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `AirToAirMissile` | Spread | 1000 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `AntFireball` | Spread | 50 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `AntFireball` | 1Dam | 4000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `ArtilleryExplode` | Spread | 150 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `ArtilleryRound.Giatsint` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `ArtilleryRound.Giatsint` | Target | 15000 | 1000 | · | · | · | · | · | · | · | · | · |
| F | `ArtilleryRound.Giatsint` | Spread | 3000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `ArtilleryRound.Paladin` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `ArtilleryRound.Paladin` | Target | 15000 | 1000 | · | · | · | · | · | · | · | · | · |
| F | `ArtilleryRound.Paladin` | Spread | 3000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `Ataka` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `Ataka` | Target | 10000 | 900 | · | · | · | · | · | · | · | · | · |
| F | `Ataka` | Spread | 2000 | 20 | · | · | · | · | · | · | · | · | · |
| F | `ATGM` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `ATGM` | Target | 10000 | 100 | · | · | · | · | · | · | · | · | · |
| F | `ATGM` | Spread | 2000 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `ATMine` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `ATMine` | Spread | 4000 | 500 | · | · | · | · | · | · | · | · | · |
| F | `Atomic` | ThermalVaporize | 200000 | 5000 | · | · | · | · | · | · | · | · | · |
| F | `Atomic` | TreeVaporize | 200000 | 5000 | · | · | · | · | · | · | · | · | · |
| F | `Atomic` | HeatRadiation2 | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `Atomic` | ThermalRadiation | 3000 | 300 | **—** | **—** | **—** | 120 | 60 | 30 | **—** | 20 | **—** |
| F | `Atomic` | BlastWave | 100000 | 5000 | **—** | **—** | **—** | 80 | 60 | 40 | **—** | 30 | **—** |
| D | `BuildingExplodeRef` | Spread | 1000 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `C4` | Spread | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `CivPanicExplosion` | Spread | 1 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `Claw` | 1Dam | 3000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `ClearMines` | Target | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `CrateNuke` | Spread_impact | 10000 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `CrateNuke` | 4Dam_areanuke1 | 500 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `CrateNuke` | TREEKILL | 120 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `Demolish` | Spread | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `DepthCharge` | Spread | 40 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `DepthChargeDual` | Spread | 40 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `DogJaw` | Target | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `DroneJammer` | Spread | 3 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `DroneTargeter` | Target | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `EmpBomb` | Spread | 36 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `FireballLauncher` | Spread | 50 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `FlakFX` | Spread | 40 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `Flamespray` | Spread | 10 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `Flamespray.heavy` | Spread | 10 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `FlamethrowerExplosion` | Spread | 50 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `GradRockets` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `GradRockets` | Target | 6000 | 250 | · | · | · | · | · | · | · | · | · |
| F | `GradRockets` | Spread | 1000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `GrenadeLauncher` | Shrapnel | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `GrenadeLauncher` | Target | 1000 | 60 | · | · | · | · | · | · | · | · | · |
| F | `GrenadeLauncher` | Spread | 150 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `GrenadeLauncher.5mag` | Shrapnel | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `GrenadeLauncher.5mag` | Target | 1000 | 60 | · | · | · | · | · | · | · | · | · |
| F | `GrenadeLauncher.5mag` | Spread | 150 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `HandGrenade` | Shrapnel | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `HandGrenade` | Spread | 1000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `Heal` | 1Dam | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `Hellfire` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `Hellfire` | Target | 10000 | 800 | · | · | · | · | · | · | · | · | · |
| F | `Hellfire` | Spread | 2000 | 20 | · | · | · | · | · | · | · | · | · |
| F | `Hellfire.Littlebird` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `Hellfire.Littlebird` | Target | 10000 | 800 | · | · | · | · | · | · | · | · | · |
| F | `Hellfire.Littlebird` | Spread | 2000 | 20 | · | · | · | · | · | · | · | · | · |
| F | `Hellfire.strykershorad` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `Hellfire.strykershorad` | Target | 10000 | 800 | · | · | · | · | · | · | · | · | · |
| F | `Hellfire.strykershorad` | Spread | 2000 | 20 | · | · | · | · | · | · | · | · | · |
| F | `HIMARSExplosion` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `HIMARSExplosion` | Target | 36000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `HIMARSExplosion` | Spread_impact | 2500 | 1800 | · | · | · | · | · | · | · | · | · |
| F | `HIMARSExplosion` | Shockwave | 7000 | 1500 | **—** | **—** | **—** | 80 | 60 | 40 | **—** | 25 | **—** |
| F | `HIMARSTargeter` | Target | 50 | _1_ | **—** | **—** | **0** | **0** | **0** | **0** | **0** | **0** | **—** |
| F | `IskanderExplosion` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `IskanderExplosion` | Target | 54000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `IskanderExplosion` | Spread_impact | 4000 | 2500 | · | · | · | · | · | · | · | · | · |
| F | `IskanderExplosion` | Shockwave | 12000 | 2000 | **—** | **—** | **—** | 80 | 60 | 40 | **—** | 25 | **—** |
| D | `IskanderExplosionAirborne` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `IskanderExplosionAirborne` | Target | 54000 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `IskanderExplosionAirborne` | Spread_impact | 4000 | 2500 | · | · | · | · | · | · | · | · | · |
| D | `IskanderExplosionAirborne` | Shockwave | 12000 | 2000 | **—** | **—** | **—** | 80 | 60 | 40 | **—** | 25 | **—** |
| F | `IskanderTargeter` | Target | 50 | _1_ | **—** | **—** | **0** | **0** | **0** | **0** | **0** | **0** | **—** |
| F | `M270Rockets` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `M270Rockets` | Target | 15000 | 500 | · | · | · | · | · | · | · | · | · |
| F | `M270Rockets` | Spread | 1500 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `Mandible` | 1Dam | 6000 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `MandibleHeavy` | 1Dam | 10000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `MANPAD` | Spread | 3000 | 15 | · | · | · | · | · | · | · | · | · |
| D | `MarineSapper` | Spread | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `MiniNuke` | Spread_impact | 300 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `MiniNuke` | 4Dam_areanuke1 | 60 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `MiniNuke` | 7Dam_areanuke2 | 60 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `MiniNuke` | 8Dam_areanuke2 | 120 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `MiniNuke` | 10Dam_areanuke3 | 60 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `MiniNuke` | 11Dam_areanuke3 | 180 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `MP5` | Target | 100 | 1 | · | · | · | · | · | · | · | · | · |
| D | `NapalmExplosion` | Spread | 500 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `NapalmFX` | Spread | 20 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `OreExplosion` | Spread | 10 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `Pistol` | Target | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `PlaceC4` | TargetValidator | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `PlaceC4Seal` | TargetValidator | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `PrisBurst` | Spread | 65 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `PrisCLaser` | Spread | 180 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `PrisLaser` | Spread | 3000 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `PrisLaserSupport` | 1Dum | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `PrismLaser` | Spread | 25 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `PrismLaserMaxFirepower` | Spread | 5000 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `PrisTBurst` | Spread | 40 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `PrisTLaser` | Spread | 150 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `Repair` | Spread | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `ReplenishSoldiersTargeter` | Spread | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `RocketPods` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `RocketPods` | Target | 5000 | 50 | · | · | · | · | · | · | · | · | · |
| F | `RocketPods` | Spread | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `RPG` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `RPG` | Target | 6000 | 500 | · | · | · | · | · | · | · | · | · |
| F | `RPG` | Spread | 800 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `SeaMineTargeting` | Target | 0 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `SilencedPPK` | Target | 100 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `Stinger` | Spread | 5000 | 20 | · | · | · | · | · | · | · | · | · |
| F | `Stinger.quad` | Spread | 5000 | 20 | · | · | · | · | · | · | · | · | · |
| L | `SurfaceToAirMissile` | Spread | 2000 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `SurfaceToAirMissile.double` | Spread | 2000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `TankRound.Abrams` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `TankRound.Abrams` | Target | 20000 | 800 | · | · | · | · | · | · | · | · | · |
| F | `TankRound.Abrams` | Spread | 3000 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `TankRound.T72` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `TankRound.T72` | Target | 20000 | 800 | · | · | · | · | · | · | · | · | · |
| L | `TankRound.T72` | Spread | 3000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `TankRound.T90` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `TankRound.T90` | Target | 20000 | 800 | · | · | · | · | · | · | · | · | · |
| F | `TankRound.T90` | Spread | 3000 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `TeslaBurst` | Spread | 500 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `TeslaZap` | Spread | 3000 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `TimerWolf_Barrage` | Spread | 1000 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `TimerWolf_Missiles` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `TimerWolf_Missiles` | Spread | 1500 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `TosRockets` | Target | 3000 | 100 | · | · | · | · | · | · | · | · | · |
| F | `TosRockets` | Spread | 1500 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `TTankZap` | Spread | 400 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `TTankZapMaxFirepower` | Spread | 3000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `UnitExplode` | Spread | 50 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `UnitExplodeHeli` | Spread | 50 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `UnitExplodeHeliEmpty` | Spread | 10 | _1_ | · | · | · | · | · | · | · | · | · |
| L | `UnitExplodePlane` | Spread | 50 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `UnitExplodePlaneEmpty` | Spread | 10 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `UnitExplodeShip` | Spread | 50 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `UnitExplodeSmall` | Spread | 40 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `UnitExplodeSmall.suicide` | Spread | 40 | _1_ | · | · | · | · | · | · | · | · | · |
| D | `UnitExplodeSubmarine` | Spread | 50 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `VehicleCookoff` | Damage | 8000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `VehicleCookoffLarge` | Damage | 14000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `VehicleCookoffTiny` | Damage | 1500 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `WGM` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `WGM` | Target | 10000 | 800 | · | · | · | · | · | · | · | · | · |
| F | `WGM` | Spread | 2000 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `WGM.bradley` | Shrapnel | 200 | _1_ | · | · | · | · | · | · | · | · | · |
| F | `WGM.bradley` | Target | 10000 | 800 | · | · | · | · | · | · | · | · | · |
| F | `WGM.bradley` | Spread | 2000 | _1_ | · | · | · | · | · | · | · | · | · |

### What Matrix A shows

**`Versus` is very nearly unused in this mod.** 7 warheads out of 205 carry a table; 6 of those are
fieldable. Every other weapon in the game is a flat 100 % against all nine classes, and the whole column
structure is inert for them. Any balance intuition imported from Red Alert — where `Versus` *is* the
armour model — is wrong here.

The seven, in full:

| weapon / warhead | listed | omitted (⇒ 100 %) |
|---|---|---|
| `Atomic` / ThermalRadiation | Light:120, Medium:60, Heavy:30, Concrete:20 | **Unarmored, Kevlar, None, Wood, Indestructable** |
| `Atomic` / BlastWave | Light:80, Medium:60, Heavy:40, Concrete:30 | **Unarmored, Kevlar, None, Wood, Indestructable** |
| `HIMARSExplosion` / Shockwave | Light:80, Medium:60, Heavy:40, Concrete:25 | **Unarmored, Kevlar, None, Wood, Indestructable** |
| `HIMARSTargeter` / Target | None:0, Wood:0, Concrete:0, Light:0, Medium:0, Heavy:0, Brick:0 | **Unarmored, Kevlar, Indestructable** |
| `IskanderExplosion` / Shockwave | Light:80, Medium:60, Heavy:40, Concrete:25 | **Unarmored, Kevlar, None, Wood, Indestructable** |
| `IskanderExplosionAirborne` / Shockwave | Light:80, Medium:60, Heavy:40, Concrete:25 | **Unarmored, Kevlar, None, Wood, Indestructable** |
| `IskanderTargeter` / Target | None:0, Wood:0, Concrete:0, Light:0, Medium:0, Heavy:0, Brick:0 | **Unarmored, Kevlar, Indestructable** |

---

## Matrix B — the model that actually grades damage: Penetration vs Thickness

`DamageWarhead.InflictDamage` (`:216-231`):

```
thickness = victim.Trait<Armor>().Info.Thickness * ArmorDirectionPercent / 100
if (thickness != 0 && Penetration < thickness)
    damage = damage * Penetration / thickness
```

`Thickness` is a field on the **actor's** `Armor` trait, not on the armour class — so the columns below are
representative victims, not classes. Two consequences worth holding onto:

- **Thickness 0 disables the whole reduction.** Every `Kevlar` actor (all 69 — the entire infantry roster),
  every `Wood` actor (73), 38 of 52 `Concrete`, all 42 `None`, and the 99 vehicle husks are Thickness 0.
  Against all of them Penetration is irrelevant and raw `Damage` lands in full.
- **Penetration defaults to 1.** Against `abrams` (700) a warhead that omits the field deals 1/700 of its
  listed damage. This is the same failure shape as an omitted `Versus` row, and it is the one that bites,
  because it is the mechanism this mod actually uses.

| T | weapon | warhead | dmg | pen | rifleman<br>`Kevlar/0` | TRUK<br>`Unarm/0` | bldg/husk<br>`Wood/Concr/0` | littlebird<br>`Light/5` | m113<br>`Light/15` | bmp2<br>`Med/15` | tunguska<br>`Med/19` | HIND<br>`Heavy/10` | MI28/HELI<br>`Heavy/20` | t90<br>`Heavy/280` | abrams<br>`Heavy/700` |
|:-:|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| F | `12.7mm.Hind` | Target | 600 | 15 | 100 | 100 | 100 | 100 | 100 | 100 | 78 | 100 | 75 | **5** | **2** |
| F | `12.7mm.Hind.AA` | Air | 300 | 5 | 100 | 100 | 100 | 100 | 33 | 33 | 26 | 50 | 25 | **1** | **0** |
| F | `12.7mm.MG` | Target | 600 | 15 | 100 | 100 | 100 | 100 | 100 | 100 | 78 | 100 | 75 | **5** | **2** |
| L | `20mm_CRAM` | Target | 600 | 40 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 14 | **5** |
| F | `25mm.Bradley` | Shrapnel | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `25mm.Bradley` | Target | 500 | 60 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 21 | **8** |
| F | `25mm.Bradley` | Spread | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `30mm.A10` | Shrapnel | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `30mm.A10` | Target | 1000 | 70 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 25 | 10 |
| L | `30mm.A10` | Spread | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `30mm.BMP2` | Shrapnel | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `30mm.BMP2` | Target | 500 | 60 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 21 | **8** |
| F | `30mm.BMP2` | Spread | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `30mm.Fighter` | Shrapnel | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `30mm.Fighter` | Target | 1000 | 70 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 25 | 10 |
| D | `30mm.Fighter` | Spread | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `30mm.Heli` | Shrapnel | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `30mm.Heli` | Target | 1000 | 70 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 25 | 10 |
| F | `30mm.Heli` | Spread | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `30mm.TimerWolf` | Shrapnel | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `30mm.TimerWolf` | Target | 1000 | 70 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 25 | 10 |
| D | `30mm.TimerWolf` | Spread | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `30mm.Tunguska.AA` | Shrapnel | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `30mm.Tunguska.AA` | Target | 1000 | 70 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 25 | 10 |
| F | `30mm.Tunguska.AA` | Spread | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `30mm.Tunguska.AG` | Shrapnel | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `30mm.Tunguska.AG` | Target | 1000 | 70 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 25 | 10 |
| F | `30mm.Tunguska.AG` | Spread | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `5.56mm.AR` | Target | 200 | 4 | 100 | 100 | 100 | 80 | 26 | 26 | 21 | 40 | 20 | **1** | **0** |
| F | `5.56mm.DMR` | Target | 200 | 4 | 100 | 100 | 100 | 80 | 26 | 26 | 21 | 40 | 20 | **1** | **0** |
| F | `5.56mm.DMR.silencer` | Target | 200 | 4 | 100 | 100 | 100 | 80 | 26 | 26 | 21 | 40 | 20 | **1** | **0** |
| F | `5.56mm.E3` | Target | 200 | 4 | 100 | 100 | 100 | 80 | 26 | 26 | 21 | 40 | 20 | **1** | **0** |
| F | `60mm_Mortar` | Shrapnel | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `60mm_Mortar` | Target | 3000 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 35 | 14 |
| F | `60mm_Mortar` | Spread | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `7.62mm.DMR` | Target | 250 | 5 | 100 | 100 | 100 | 100 | 33 | 33 | 26 | 50 | 25 | **1** | **0** |
| F | `7.62mm.MG` | Target | 250 | 5 | 100 | 100 | 100 | 100 | 33 | 33 | 26 | 50 | 25 | **1** | **0** |
| F | `7.62mm.Minigun` | Target | 250 | 5 | 100 | 100 | 100 | 100 | 33 | 33 | 26 | 50 | 25 | **1** | **0** |
| F | `7.62mm.Minigun` | Spread | 60 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `7.62mm.Minigun.AA` | Air | 150 | 4 | 100 | 100 | 100 | 80 | 26 | 26 | 21 | 40 | 20 | **1** | **0** |
| F | `7.62mm.Sniper` | Target | 350 | 5 | 100 | 100 | 100 | 100 | 33 | 33 | 26 | 50 | 25 | **1** | **0** |
| D | `73mm_BMP` | Shrapnel | 150 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `73mm_BMP` | Target | 5000 | 300 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 42 |
| D | `73mm_BMP` | Spread | 1000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `9M311` | Spread | 5000 | 20 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | **7** | **2** |
| L | `AACannon` | Spread | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `AirToAirMissile` | Spread | 1000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `AntFireball` | Spread | 50 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `AntFireball` | 1Dam | 4000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `ArtilleryExplode` | Spread | 150 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `ArtilleryRound.Giatsint` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `ArtilleryRound.Giatsint` | Target | 15000 | 1000 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `ArtilleryRound.Giatsint` | Spread | 3000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `ArtilleryRound.Paladin` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `ArtilleryRound.Paladin` | Target | 15000 | 1000 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `ArtilleryRound.Paladin` | Spread | 3000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `Ataka` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `Ataka` | Target | 10000 | 900 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `Ataka` | Spread | 2000 | 20 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | **7** | **2** |
| F | `ATGM` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `ATGM` | Target | 10000 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 35 | 14 |
| F | `ATGM` | Spread | 2000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `ATMine` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `ATMine` | Spread | 4000 | 500 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 71 |
| F | `Atomic` | ThermalVaporize | 200000 | 5000 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `Atomic` | TreeVaporize | 200000 | 5000 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `Atomic` | HeatRadiation2 | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `Atomic` | ThermalRadiation | 3000 | 300 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 42 |
| F | `Atomic` | BlastWave | 100000 | 5000 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| D | `BuildingExplodeRef` | Spread | 1000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `C4` | Spread | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `CivPanicExplosion` | Spread | 1 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `Claw` | 1Dam | 3000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `CrateNuke` | Spread_impact | 10000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `CrateNuke` | 4Dam_areanuke1 | 500 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `CrateNuke` | TREEKILL | 120 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `DepthCharge` | Spread | 40 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `DepthChargeDual` | Spread | 40 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `DogJaw` | Target | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `DroneJammer` | Spread | 3 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `EmpBomb` | Spread | 36 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `FireballLauncher` | Spread | 50 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `FlakFX` | Spread | 40 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `Flamespray` | Spread | 10 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `Flamespray.heavy` | Spread | 10 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `FlamethrowerExplosion` | Spread | 50 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `GradRockets` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `GradRockets` | Target | 6000 | 250 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 89 | 35 |
| F | `GradRockets` | Spread | 1000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `GrenadeLauncher` | Shrapnel | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `GrenadeLauncher` | Target | 1000 | 60 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 21 | **8** |
| F | `GrenadeLauncher` | Spread | 150 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `GrenadeLauncher.5mag` | Shrapnel | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `GrenadeLauncher.5mag` | Target | 1000 | 60 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 21 | **8** |
| F | `GrenadeLauncher.5mag` | Spread | 150 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `HandGrenade` | Shrapnel | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `HandGrenade` | Spread | 1000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `Hellfire` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `Hellfire` | Target | 10000 | 800 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `Hellfire` | Spread | 2000 | 20 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | **7** | **2** |
| F | `Hellfire.Littlebird` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `Hellfire.Littlebird` | Target | 10000 | 800 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `Hellfire.Littlebird` | Spread | 2000 | 20 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | **7** | **2** |
| F | `Hellfire.strykershorad` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `Hellfire.strykershorad` | Target | 10000 | 800 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `Hellfire.strykershorad` | Spread | 2000 | 20 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | **7** | **2** |
| F | `HIMARSExplosion` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `HIMARSExplosion` | Target | 36000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `HIMARSExplosion` | Spread_impact | 2500 | 1800 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `HIMARSExplosion` | Shockwave | 7000 | 1500 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `HIMARSTargeter` | Target | 50 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `IskanderExplosion` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `IskanderExplosion` | Target | 54000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `IskanderExplosion` | Spread_impact | 4000 | 2500 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `IskanderExplosion` | Shockwave | 12000 | 2000 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| D | `IskanderExplosionAirborne` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `IskanderExplosionAirborne` | Target | 54000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `IskanderExplosionAirborne` | Spread_impact | 4000 | 2500 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| D | `IskanderExplosionAirborne` | Shockwave | 12000 | 2000 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `IskanderTargeter` | Target | 50 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `M270Rockets` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `M270Rockets` | Target | 15000 | 500 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 71 |
| F | `M270Rockets` | Spread | 1500 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `Mandible` | 1Dam | 6000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `MandibleHeavy` | 1Dam | 10000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `MANPAD` | Spread | 3000 | 15 | 100 | 100 | 100 | 100 | 100 | 100 | 78 | 100 | 75 | **5** | **2** |
| D | `MarineSapper` | Spread | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `MiniNuke` | Spread_impact | 300 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `MiniNuke` | 4Dam_areanuke1 | 60 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `MiniNuke` | 7Dam_areanuke2 | 60 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `MiniNuke` | 8Dam_areanuke2 | 120 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `MiniNuke` | 10Dam_areanuke3 | 60 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `MiniNuke` | 11Dam_areanuke3 | 180 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `MP5` | Target | 100 | 1 | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `NapalmExplosion` | Spread | 500 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `NapalmFX` | Spread | 20 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `OreExplosion` | Spread | 10 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `Pistol` | Target | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `PrisBurst` | Spread | 65 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `PrisCLaser` | Spread | 180 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `PrisLaser` | Spread | 3000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `PrismLaser` | Spread | 25 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `PrismLaserMaxFirepower` | Spread | 5000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `PrisTBurst` | Spread | 40 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `PrisTLaser` | Spread | 150 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `RocketPods` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `RocketPods` | Target | 5000 | 50 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 17 | **7** |
| F | `RocketPods` | Spread | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `RPG` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `RPG` | Target | 6000 | 500 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 71 |
| F | `RPG` | Spread | 800 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `SilencedPPK` | Target | 100 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `Stinger` | Spread | 5000 | 20 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | **7** | **2** |
| F | `Stinger.quad` | Spread | 5000 | 20 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | **7** | **2** |
| L | `SurfaceToAirMissile` | Spread | 2000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `SurfaceToAirMissile.double` | Spread | 2000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `TankRound.Abrams` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `TankRound.Abrams` | Target | 20000 | 800 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `TankRound.Abrams` | Spread | 3000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `TankRound.T72` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `TankRound.T72` | Target | 20000 | 800 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| L | `TankRound.T72` | Spread | 3000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `TankRound.T90` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `TankRound.T90` | Target | 20000 | 800 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `TankRound.T90` | Spread | 3000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `TeslaBurst` | Spread | 500 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `TeslaZap` | Spread | 3000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `TimerWolf_Barrage` | Spread | 1000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `TimerWolf_Missiles` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `TimerWolf_Missiles` | Spread | 1500 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `TosRockets` | Target | 3000 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 35 | 14 |
| F | `TosRockets` | Spread | 1500 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `TTankZap` | Spread | 400 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `TTankZapMaxFirepower` | Spread | 3000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `UnitExplode` | Spread | 50 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `UnitExplodeHeli` | Spread | 50 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `UnitExplodeHeliEmpty` | Spread | 10 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| L | `UnitExplodePlane` | Spread | 50 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `UnitExplodePlaneEmpty` | Spread | 10 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `UnitExplodeShip` | Spread | 50 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `UnitExplodeSmall` | Spread | 40 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `UnitExplodeSmall.suicide` | Spread | 40 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| D | `UnitExplodeSubmarine` | Spread | 50 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `VehicleCookoff` | Damage | 8000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `VehicleCookoffLarge` | Damage | 14000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `VehicleCookoffTiny` | Damage | 1500 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `WGM` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `WGM` | Target | 10000 | 800 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `WGM` | Spread | 2000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `WGM.bradley` | Shrapnel | 200 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |
| F | `WGM.bradley` | Target | 10000 | 800 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| F | `WGM.bradley` | Spread | 2000 | _1_ | 100 | 100 | 100 | 20 | **6** | **6** | **5** | 10 | **5** | **0** | **0** |

Cells are the percentage of listed `Damage` that survives the thickness reduction. **Bold** = under 10 %.

### The `Spread`/`Target` idiom — read before calling a `_1_` a bug

The dominant WW3MOD weapon shape is a pair: `Warhead@Target` (aimed hit, real `Penetration`) plus
`Warhead@Spread` (splash, `Penetration` omitted). The omission is **deliberate** — it is how splash is made
near-harmless to armour while still shredding Thickness-0 infantry. 23 of the 30 fieldable default-penetration
warheads have a penetrating `Warhead@Target` sibling and are correct by design.

The seven fieldable default-penetration warheads with **no** penetrating sibling are
`IskanderTargeter`, `HIMARSTargeter`, `ArtilleryExplode`, `UnitExplode`, `CivPanicExplosion`,
`Flamespray` and `FlamethrowerExplosion` — all either designators or small explosion effects, and all
intentional except the two targeters (defect **D2**).

