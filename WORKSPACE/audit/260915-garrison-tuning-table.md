# Per-building garrison tuning — current state, ready to fill in

**Date:** 2026-09-15 · **Branch:** `wt/garrison-tuning @ da1d519f` (base `wt/garrison-followups @ 51272f83`)
**Status:** PHASE 2 PREP. Every number in the left half is resolved from the shipped YAML; the four
right-hand columns are deliberately blank and get filled once the `demo-garrison-lineup` frames
exist. Nothing here is a proposal yet — the user's ruling is that what each building IS gets decided
from the sprite, not from these figures.

Resolved with a MiniYaml resolver that expands `Inherits@` in order and applies `-Key:` removals, so
the values are what the engine sees, not what each actor block happens to say. Spot-checked against
hand-read values for V01, GTWR, V20 and RUSHOUSE.

---

## 1. The finding that changes what phase 2 can do

**Armour TYPE is very nearly inert against the weapons that matter.** Of the four the brief names,
**not one has a `Versus` table on its main damage warhead**:

| Weapon | Main warhead | Damage | Penetration | Versus |
|---|---|---|---|---|
| HIMARS (`HIMARSExplosion`) | `Warhead@Target` `TargetDamage` | 36000 | 2500 | **none** |
| | `Warhead@Spread_impact` `SpreadDamage` | 2500 @ 768 | 1800 | **none** |
| | `Warhead@Shockwave` `ShockwaveDamage` | 7000 @ 1c0 | 1500 | Light 80 · Medium 60 · Heavy 40 · Concrete 25 |
| Abrams 120 mm / T-90 125 mm | `Warhead@Target` `TargetDamage` | 20000 | 800 | **none** |
| | `Warhead@Spread` `SpreadDamage` | 3000 @ 64 | *(omitted → 1)* | **none** |
| 30 mm autocannon (`^30mm`) | `Warhead@Target` `TargetDamage` | 1000 | 70 | **none** |
| | `Warhead@Spread` `SpreadDamage` | 100 @ 64 | *(omitted → 1)* | **none** |
| 5.56 rifle / 7.62 MG | `Warhead@Target` `TargetDamage` | 200 / 250 | 4 / 5 | **none** |

Across the whole mod, the only warheads that discriminate on a building's armour type are the
**blast/shockwave** family: `HIMARSExplosion`, `IskanderExplosion`, `OreshnikRVExplosion`, the eleven
`Nuke*`, and the eight `VolatileLoad*`. Everything else is flat damage.

So on a HIMARS direct hit, the armour lever is worth **7000 of ~44000** — the `Warhead@Target`
36000 is untyped. The whole armour vocabulary spans **40250 (Concrete) to 45500 (Unarmored), a 13%
range**, and every building in the mod dies to 1–3 HIMARS. *"A HIMARS into a wooden church is a bad
day, a concrete block takes several" cannot be expressed through `Armor: Type` at these weights.* It
has to come from **HP**, or from putting a `Versus` table on `Warhead@Target` — which is a
weapons-file change affecting every actor, not a per-building one.

**Small arms cannot scratch any of these buildings at all.** `^5.56mm` is
`ValidTargets: Infantry, Vehicle, AirLight`; a civilian building's target types are
`Ground, C4, DetonateAttack, Structure, Defense`. No overlap, so `WeaponInfo.IsValidAgainst` fails
before damage is computed. Only the men at the ports are shootable. 30 mm reaches the buildings
solely through `Defense`, which is the type `^CivBuilding` keeps deliberately.

## 2. Two inversions in the current numbers

**(a) 21 of the 38 civilian actors have no `Health` and no `Armor` block at all** — V12, V13, V19 and
every desert building V20–V37 — and therefore inherit **`HP 60000` / `Armor: Concrete`** from
`^TechBuilding` (`structures.yaml:160`). A mud-brick desert hut and the oil pump are currently
concrete, at the same hit points as a reinforced bunker. This is not a tuning that was made badly;
it is a tuning that was never made, and it is the single largest thing the frames will correct.

**(b) `Penetration` defaults to 1, so declaring an armour Thickness makes a building *tougher against
splash* in a way nobody chose.** `ApplyPenetration` returns `damage * penetration / thickness`
whenever the warhead does not out-thickness the armour (`DamageWarhead.cs:128-134`), and the default
`Penetration = 1` (`:25`). A tank round's 3000 `Warhead@Spread` therefore lands as:

| target | thickness | splash taken |
|---|---|---|
| V20 and the other 20 inheritors | 0 | **3000** (full — thickness 0 short-circuits the check) |
| V01 church | 10 | 300 |
| RUSHOUSE | 50 | 60 |
| PBOX | 300 | **10** |

So *not* declaring armour is currently the more vulnerable setting, and any Thickness written on a
civilian building silently divides every under-penetrating warhead. Thickness is worth setting
deliberately or not at all; it should not be copied from a real-world figure without checking what it
does to the splash warheads.

**(c) Everything bottoms out at 1 HP, never 0.** `GarrisonManager.Indestructible` defaults to **true**
(`GarrisonManager.cs:85`) and no garrisonable actor overrides it, so the "hits" column below is hits
**to rubble**, not to destruction. Occupants then sit behind `RubbleProtection: 30`.

## 3. Port geometry — four layouts, and all of them are a knot at the sprite's centre

`Cone` is a **half-angle** each side of the port yaw. WAngle is counterclockwise: 0 N, 256 W, 512 S,
768 E.

| Layout | Used by | Ports | Offsets (X,Y,Z) | Yaw | Cone | Distance from centre |
|---|---|---|---|---|---|---|
| **CIV-8** | all 37 garrisonable civilian actors | 8 | `±80,±280,200` and `±280,±80,200` | 896 NE ×2, 640 SE ×2, 384 SW ×2, 128 NW ×2 | 140 (±49.2°) | **0.28 cells** |
| **GTWR-4** | GTWR | 4 | `0,∓600,384`, `±600,0,384` | 0 N, 768 E, 512 S, 256 W | 200 (±70.3°) | 0.59 cells |
| **BOX-2** | PBOX | 2 | `±300,0,256` | 768 E, 256 W | 300 (±105.5°) | 0.29 cells |
| **BOX-2** | HBOX | 2 | `±280,0,200` | 768 E, 256 W | 300 (±105.5°) | 0.27 cells |

Two consequences for the retune, both verified in the C# rather than guessed:

- **The Z is discarded for the man and kept for his muzzle flash.** Three sites clamp the soldier to
  terrain height (`GarrisonManager.cs:384-387`, `:728-733`, `AttackGarrisoned.cs:279-284`), while the
  flash animation uses the raw offset including Z (`AttackGarrisoned.cs:314`). So "port on the
  roofline" can only be spent in **X/Y**, and the shipped Z values currently float the gun-flash
  200–384 units above the man firing it.
- **0.28 cells is the middle of the sprite, not its windows.** The eight civilian ports span 560
  world units in X (−280 to +280). Against footprint width that is **55% on a 1×1, 27% on a 2×2 or
  2×1, and 11% on V37's 5×2** — so the wider the building, the more tightly the whole garrison
  bunches at its centre. GTWR is the outlier that got this right: its ±600 span is 117% of its 1×1
  footprint, i.e. its men stand at the tower's edges. Whatever the frames show, the civilian ring
  almost certainly wants to be spent wider and shaped per sprite.

## 4. The table

`HIMARS` / `tank` / `30 mm` are damage per direct hit with `(hits to rubble)` after them, computed
from the warhead numbers above. They assume the impact cell — the shockwave component is an upper
bound, since `ShockwaveDamage` is a travelling wave with its own `Falloff: 100, 70, 45, 25, 10` and
I have not traced its per-distance application. Port layouts are named from §3.

| Actor | Tooltip | Dims | HP | Armor | Th mm | Cap | Ports | HIMARS | tank | 30 mm | What it is | New HP | New armor | New cap | New ports |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `GTWR` | Guard Tower | 1,1 | 15000 | Unarmored | 25 | 6 | GTWR-4 | 45500 (1) | 20120 (1) | 1004 (15) |  |  |  |  |
| `PBOX` | Bunker | 1,1 | 60000 | Concrete | 300 | 4 | BOX-2 | 40250 (2) | 20010 (3) | 233 (258) |  |  |  |  |
| `HBOX` | Camo Pillbox | 1,1 | 22500 | Light | 150 | 4 | BOX-2 | 44100 (1) | 20020 (2) | 466 (49) |  |  |  |  |
| `V01` | Church | 2,2 | 75000 | Light | 10 | 10 | CIV-8 | 44100 (2) | 20300 (4) | 1010 (75) |  |  |  |  |
| `V02` | Civilian Building | 2,2 | 75000 | Light | 10 | 10 | CIV-8 | 44100 (2) | 20300 (4) | 1010 (75) |  |  |  |  |
| `V03` | Civilian Building | 2,2 | 90000 | Light | 10 | 10 | CIV-8 | 44100 (3) | 20300 (5) | 1010 (90) |  |  |  |  |
| `V04` | Civilian Building | 2,2 | 65000 | Light | 10 | 10 | CIV-8 | 44100 (2) | 20300 (4) | 1010 (65) |  |  |  |  |
| `V05` | Civilian Building | 2,1 | 50000 | Light | 10 | 10 | CIV-8 | 44100 (2) | 20300 (3) | 1010 (50) |  |  |  |  |
| `V06` | Civilian Building | 2,1 | 65000 | Light | 10 | 10 | CIV-8 | 44100 (2) | 20300 (4) | 1010 (65) |  |  |  |  |
| `V07` | Civilian Building | 2,1 | 50000 | Light | 10 | 10 | CIV-8 | 44100 (2) | 20300 (3) | 1010 (50) |  |  |  |  |
| `V08` | Civilian Building | 1,1 | 40000 | Light | 10 | 10 | CIV-8 | 44100 (1) | 20300 (2) | 1010 (40) |  |  |  |  |
| `V09` | Civilian Building | 1,1 | 25000 | Light | 10 | 10 | CIV-8 | 44100 (1) | 20300 (2) | 1010 (25) |  |  |  |  |
| `V10` | Civilian Building | 1,1 | 50000 | Medium | 30 | 10 | CIV-8 | 42700 (2) | 20100 (3) | 1003 (50) |  |  |  |  |
| `V11` | Civilian Building | 1,1 | 25000 | Medium | 10 | 10 | CIV-8 | 42700 (1) | 20300 (2) | 1010 (25) |  |  |  |  |
| `V12` | Civilian Building | 1,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V13` | Civilian Building | 1,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V19` | Oil Pump | 1,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V19.Husk` | Husk (Oil Pump) | 1,1 | — | — | — | — | — | — | — | — | husk, garrison stack stripped | n/a | n/a | n/a | n/a |
| `V20` | Civilian Building | 2,2 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V21` | Civilian Building | 2,2 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V22` | Civilian Building | 2,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V23` | Civilian Building | 1,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V24` | Civilian Building | 2,2 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V25` | Church | 2,2 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V26` | Civilian Building | 2,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V27` | Civilian Building | 1,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V28` | Civilian Building | 1,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V29` | Civilian Building | 1,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V30` | Civilian Building | 2,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V31` | Civilian Building | 2,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V32` | Civilian Building | 2,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V33` | Civilian Building | 2,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V34` | Civilian Building | 1,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V35` | Civilian Building | 1,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V36` | Civilian Building | 1,1 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `V37` | Civilian Building | 5,2 | 60000 | Concrete | 0 | 10 | CIV-8 | 40250 (2) | 23000 (3) | 1100 (55) |  |  |  |  |
| `RUSHOUSE` | Civilian Building | 1,2 | 120000 | Heavy | 50 | 10 | CIV-8 | 41300 (3) | 20060 (6) | 1002 (120) |  |  |  |  |
| `ASIANHUT` | Civilian Building | 1,1 | 25000 | Light | 10 | 10 | CIV-8 | 44100 (1) | 20300 (2) | 1010 (25) |  |  |  |  |
| `SNOWHUT` | Civilian Building | 1,2 | 25000 | Light | 10 | 10 | CIV-8 | 44100 (1) | 20300 (2) | 1010 (25) |  |  |  |  |
| `LHUS` | Lighthouse | 1,1 | 50000 | Medium | 30 | 10 | CIV-8 | 42700 (2) | 20100 (3) | 1003 (50) |  |  |  |  |
| `WINDMILL` | Windmill | 1,1 | 25000 | Light | 10 | 10 | CIV-8 | 44100 (1) | 20300 (2) | 1010 (25) |  |  |  |  |

**Capacity is uniform 10 on all 37 civilian actors** (from `^CivBuilding`), 6 on GTWR, 4 on PBOX and
HBOX. The user has ruled this should scale with the building. `Passenger.Weight` is the engine
default 1 for every infantry type, so `MaxWeight` reads directly as a man count.

**Protection curves** are uniform too: 95/35/30/15 on all 37 civilians, 97/50/30/15 on GTWR and PBOX,
96/48/30/15 on HBOX (Base/Critical/Rubble/MinPassThrough). `CriticalProtection` is the curve's
intercept at **zero** HP, not its value at `DamageState.Critical` — the interpolation is
`Critical + (Base − Critical) × hpPct`.

## 5. What is still unknown until the frames land

Every "what it is" judgement. That is the whole point of the demo and nothing in this document
substitutes for it: the four blank columns stay blank. Specifically unresolved:

- which of V02–V13 and V20–V37 are wood, brick, masonry, concrete or industrial;
- whether `Wood` is usable as an armour type. It exists and CYCL/WOOD (fences) use it, but the only
  `Versus` table naming it is the `IskanderTargeter`/`HIMARSTargeter` dummy trigger, which zeroes
  every type. Adopting `Wood` for buildings means adding rows to ~20 blast warheads, not one actor
  edit;
- where each sprite's real openings are, which is what the six port close-ups exist to show.
