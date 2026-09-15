# Per-building garrison tuning — current state, ready to fill in

**Date:** 2026-09-15 · **Branch:** `wt/garrison-tuning @ da1d519f` (base `wt/garrison-followups @ 51272f83`)
**Status:** DECIDED AND APPLIED. Written as prep before the captures existed; §4 is now filled in
from run `260915_210535_p58516_demo-garrison-lineup` (19 frames) and the values below are what
`civilian.yaml` now carries. §1–§3 are the pre-capture analysis and are unchanged — they are what
constrained the decisions.

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

## 4. What each building turned out to be, and what it now carries

Every row was decided by looking at the sprite in the numbered capture named in its rationale — not
from footprint size, which is the thing the ruling specifically rejected. `HIMARS` and `tank` are
**hits to rubble** at the per-hit damage derived in §1 (HIMARS 40250–45500 by armour, tank round
23000 flat now that civilian Thickness is 0).

| Actor | What it is (from the frames) | HP | Armor | Cap | Ports | HIMARS | tank | Rationale |
|---|---|---|---|---|---|---|---|---|
| `V01` | White clapboard church, steeple, shingle roof | 38000 | Light | 10 | 8 | 1 | 2 | Frame 006/017: timber-framed church, big open nave. One HIMARS rubbles it - the user's worked example. |
| `V02` | Dwelling joined to a corrugated warehouse shed | 30000 | Light | 8 | 6 | 1 | 2 | Frame 002: half house, half yellow sheet-metal shed on a dirt yard. Big volume, no structure. |
| `V03` | Two-storey rendered farmhouse + outbuilding + haystack | 60000 | Medium | 8 | 6 | 2 | 3 | Frame 003: the most substantial temperate dwelling - two full storeys, masonry walls. |
| `V04` | Village house, pitched slate roof | 45000 | Medium | 6 | 4 | 2 | 2 | Frame 003: single well-built house, smaller than V03 and with no outbuildings. |
| `V05` | Small red-roofed cottage + shed | 24000 | Light | 5 | 4 | 1 | 2 | Frame 004: one-storey cottage, timber, plus a lean-to. |
| `V06` | Large red timber barn + fence | 26000 | Light | 8 | 6 | 1 | 2 | Frame 004: classic plank barn. Huge internal volume, nothing structural - burns and folds. |
| `V07` | Farmhouse with yard gate | 34000 | Light | 6 | 4 | 1 | 2 | Frame 005: rendered farmhouse, one and a half storeys, timber roof. |
| `V08` | Flat-roofed storehouse | 18000 | Light | 4 | 2 | 1 | 1 | Frame 005: a plain box with one opening. A store, not a dwelling. |
| `V09` | Small yellow cottage | 16000 | Light | 4 | 2 | 1 | 1 | Frame 003: single-room dwelling, two windows. |
| `V10` | Two-storey masonry townhouse, red roof | 52000 | Medium | 6 | 4 | 2 | 3 | Frame 003: white masonry, two storeys on a 1x1 - tall and solid, the temperate set's brick house. |
| `V11` | Small outbuilding / shed | 14000 | Light | 3 | 2 | 1 | 1 | Frame 003: low red-roofed shed, barely a room. |
| `V12` | TWO HAYSTACKS | 4000 | None | 2 | 2 | 1 | 1 | Frame 004: not a building - two stacks of hay. Was 60000 HP of concrete holding ten men. |
| `V13` | ONE HAYSTACK | 3000 | None | 2 | 2 | 1 | 1 | Frame 004: a single hay stack. Same inherited-concrete defect as V12. |
| `V19` | Oil pump jack on a concrete pad | 20000 | Medium | 2 | 2 | 1 | 1 | Frame 018: machinery, not a room. Two men can shelter behind the pad; there is no interior. |
| `RUSHOUSE` | Multi-storey concrete apartment block | 120000 | Concrete | 12 | 8 | 3 | 6 | Frame 019: dark poured-concrete slab block, the tallest thing in the set. HP unchanged; armour Heavy->Concrete. |
| `ASIANHUT` | Small house, red tile roof | 18000 | Light | 4 | 2 | 1 | 1 | Frame 011: one-room rendered dwelling with a tiled roof. |
| `SNOWHUT` | Log cabin under snow | 20000 | Light | 4 | 2 | 1 | 1 | Frame 012: timber cabin, lit windows, one storey. |
| `LHUS` | Stone lighthouse | 70000 | Heavy | 3 | 2 | 2 | 4 | Frame 012: thick masonry tower. Very tough shell, almost no floor area - high HP, capacity 3. |
| `WINDMILL` | Timber windmill on a stone base | 16000 | Light | 3 | 2 | 1 | 1 | Frame 013: timber body and sails over a small stone plinth. |
| `V20` | Adobe compound, two thatched houses | 50000 | Light | 8 | 6 | 2 | 3 | Frame 005: two mud-brick houses sharing a sand yard. Thick walls, but they crumble. |
| `V21` | Adobe house with a well | 44000 | Light | 7 | 6 | 1 | 2 | Frame 005: single adobe dwelling plus the well its HitShape@WELL already models. |
| `V22` | Small adobe house, thatch roof | 20000 | Light | 5 | 4 | 1 | 1 | Frame 006: one-storey whitewashed mud-brick with a low boundary wall. |
| `V23` | A LOW WALL AND A WELL | 5000 | None | 2 | 2 | 1 | 1 | Frame 007: not a building - a wall stub and a well head. Was concrete at 60000 holding ten. |
| `V24` | Concrete block building + water tank | 100000 | Concrete | 10 | 8 | 3 | 5 | Frame 007: flat-roofed poured-concrete structure, the one genuinely concrete desert building. |
| `V25` | Desert church, whitewashed, steeple | 38000 | Light | 10 | 8 | 1 | 2 | Frame 008: the second church. Matched to V01 exactly so the two read the same. |
| `V26` | Two joined adobe huts | 24000 | Light | 6 | 4 | 1 | 2 | Frame 008: a pair of thatched mud-brick dwellings. |
| `V27` | Small concrete box building | 55000 | Concrete | 5 | 4 | 2 | 3 | Frame 009: grey flat-roofed concrete, one room but properly built. |
| `V28` | Small adobe hut, thatch roof | 15000 | Light | 4 | 2 | 1 | 1 | Frame 009: single-room mud-brick. |
| `V29` | Adobe hut with windows | 16000 | Light | 4 | 2 | 1 | 1 | Frame 006: slightly larger single-room adobe. |
| `V30` | Long flat-roofed adobe store | 26000 | Light | 6 | 4 | 1 | 2 | Frame 007: low mud-brick range, wide but single-storey. |
| `V31` | RUIN AND MARKET STALLS | 8000 | None | 3 | 2 | 1 | 1 | Frame 007: a collapsed structure and awning stalls. Cover, not a building. |
| `V32` | Low huts / tents | 9000 | None | 3 | 2 | 1 | 1 | Frame 008: sand-coloured shelters, barely knee-high in the sprite. |
| `V33` | Adobe hut with a market stall | 12000 | Light | 4 | 2 | 1 | 1 | Frame 008: one small mud-brick unit plus an awning. |
| `V34` | Tent / sand mound | 6000 | None | 2 | 2 | 1 | 1 | Frame 009: the smallest thing in the set - a mound with an opening. |
| `V35` | Small adobe ruin | 8000 | None | 2 | 2 | 1 | 1 | Frame 009: a broken shell, roof gone. |
| `V36` | Small adobe mound hut | 10000 | Light | 3 | 2 | 1 | 1 | Frame 010: tiny intact mud-brick dwelling. |
| `V37` | Large warehouse / hangar on a concrete apron | 95000 | Concrete | 12 | 8 | 3 | 5 | Frame 011: by far the biggest civilian structure - a long roofed hall on poured concrete. |

| Actor | What it is | HP | Armor | Cap | Ports | Rationale |
|---|---|---|---|---|---|---|
| `GTWR` | Frame 014: timber tower with a conical roof. | 15000 | Unarmored 25mm | 6 | 4 | Frame 014: timber tower with a conical roof. HP/armour/capacity/ports UNCHANGED - its four ports at +/-600 on a 1x1 are the shipped example the civilian ring was rebuilt to match. Gains only the empty-vision gate. |
| `PBOX` | Frame 015: hexagonal concrete bunker. | 60000 | Concrete 300mm | 4 | 2 | Frame 015: hexagonal concrete bunker. UNCHANGED - a balance-tuned player structure, and its 4-man capacity is already scaled to an emplacement. Gains the empty-vision gate (the one-line fix). |
| `HBOX` | Frame 016: very low camouflaged pillbox. | 22500 | Light 150mm | 4 | 2 | Frame 016: very low camouflaged pillbox. UNCHANGED, same reasoning. |

### Armour type cannot carry the church-vs-block difference, so HP does

This is the ruling's central request and it needs stating plainly: **`Armor: Type` alone cannot
express "a HIMARS rubbles a wooden church but a concrete block holds".** Per §1, HIMARS' 36000
`Warhead@Target` and both tank rounds carry no `Versus` table at all; only the 7000 `Warhead@Shockwave`
discriminates, so the entire armour vocabulary spans 40250–45500 per hit — 13%. A church and a
bunker with the same HP would die to the same number of rockets whatever their armour said.

So the ladder is carried by **HP**, and armour type is set for coherence and because it *does* bite
hard on the blast family (Iskander, Oreshnik and every nuke run the same table at up to 400000
damage, where Concrete's 25% is worth 300000). The resulting bands:

| Band | HP | Reads as | Members |
|---|---|---|---|
| Not a building | 3000–9000 | one tank round | haystacks V12/V13, the well V23, the ruin V31, tents V32/V34/V35 |
| Light timber / single-room adobe | 10000–26000 | 1 HIMARS, 1–2 tank | V05, V06, V08, V09, V11, V19, V22, V26, V28, V29, V30, V33, V36, ASIANHUT, SNOWHUT, WINDMILL |
| Substantial dwelling | 30000–45000 | 1 HIMARS, 2 tank | V01, V02, V04, V07, V21, V25 |
| Masonry / compound | 50000–60000 | 2 HIMARS, 3 tank | V03, V10, V20, V27 |
| Stone tower | 70000 | 2 HIMARS | LHUS |
| Concrete | 95000–120000 | 3 HIMARS, 5 tank | V24, V37, RUSHOUSE |

**No weapon `Versus` line was touched.** Introducing a `Wood` armour type was considered and
rejected: `Wood` already belongs to CYCL and WOOD (the fences), the only table naming it is the
`IskanderTargeter`/`HIMARSTargeter` dummy trigger that zeroes every type, and a type absent from a
table takes 100% — so adopting it for buildings would have meant editing ~20 blast warheads *and*
changing what fences do. The existing Light/Medium/Heavy/Concrete ladder already reads as a softness
scale and needed no new vocabulary.

### `Thickness: 0` on every civilian building, deliberately

§2(b) recorded that `Penetration` defaults to 1, so any declared Thickness divides every warhead
that omits one — a tank round's 3000 splash landed as 300 on the church and 10 on PBOX. Sixteen
civilians carried a Thickness (10/30/50) that nobody chose for that effect. All 37 are now
`Thickness: 0`, stated explicitly rather than left to inherit: a house wall is not armour, and the
toughness belongs in HP where it can be read. **This makes those sixteen take full splash where they
used to take a divided share** — a real behavioural change, in the direction of the ruling. The three
fortifications keep their thicknesses (GTWR 25, HBOX 150, PBOX 300), which are genuine armour and
already tuned.

### Ports

`^CivBuilding`'s shared eight-port ring is deleted and all 37 actors declare their own. Counts are
2 / 4 / 6 / 8 by how many openings the sprite has: a shed or haystack gets 2, a cottage 4, a
substantial dwelling 6, and the five largest and most fenestrated — V01 and V25 (the churches, long
naves with a window row down each side), V24, V37 and RUSHOUSE — keep 8. Cones widen as the count
drops (300 / 170 / 120 / 100 half-angle) so the ring always covers 360° with overlap.

Keeping eight on V01 was not only a realism call: `test-visual-command-bar` garrisons v01
specifically to populate all eight `EJECT_PORT_0-7` chrome rows in one frame, and a blanket cut to
six would have left the last two rows permanently empty and quietly defeated that test. Its comment
now records that only five actors can serve that purpose.

Offsets sit on a ring that **circumscribes the footprint** along each port's own bearing, plus a
margin — 90 world units, and 270 on northern ports. The margin is asymmetric because **Z is
discarded for the soldier**: `GarrisonManager` clamps him to terrain level at three separate sites,
so a port cannot be lifted onto a roofline and a north-side man can only be kept out from under the
sprite by pushing him further out in Y. GTWR's shipped ±600 on a 1×1 is the calibration point; the
formula reproduces it.

`CivBuildingPortCoverageTest` pins all of this: that the template declares no ring (a restored one
would MERGE, not replace), that every garrisonable actor declares ≥2 ports of its own, and that no
port sits inside its own footprint. Both failure modes were confirmed red before the fixture was
trusted.

### Not changed, and why

- **GTWR / PBOX / HBOX HP, armour, capacity and ports.** These are player-built, balance-tuned
  structures, and their capacities (6/4/4) are already scaled to small emplacements rather than
  uniform. The ruling's uniformity complaint was the civilian 10. They get only the vision fix.
- **V12, V13, V23, V31 remain garrisonable.** They are a haystack pair, a haystack, a well and a
  ruin — arguably they belong on `^CivField` with V14–V18, which is where the other non-buildings
  already live. That is a structural reclassification nobody asked for, so they are instead tuned to
  what they are: 2–3 men, 3000–8000 HP, no armour class. Flagged rather than done.

## 5. Vision when empty

PBOX was the only garrison emplacement that revealed shroud while unmanned, and the whole difference
was one missing `Inherits@DetectionWhenLoaded: ^StandardVisionWhenLoaded` that GTWR (`:79`) and HBOX
(`:329`) already carried. Added. All three now gate all ten `Vision@N` bands on `loaded`, verified by
re-resolving the merged rules. The gating works by key collision rather than removal — that template
re-states the same ten keys carrying only `RequiresCondition: loaded` — so there was never a
`-Vision@N` to look for, and every one of the three carries identical ranges, which is why a check
comparing trait *values* across the defences would have found nothing.
