# Supply-cost audit — every `AmmoPool` in the mod, priced

**Date:** 2026-08-30 · **Ref:** `wt/supply-economics` off `main` @ `b3a7564d` · **Method:** static YAML + engine read, no game launch, no build.

Written for the pending change that makes *all* ammunition cost supply. Today two refill paths
are free and one is paid; when the free ones start charging, every `SupplyValue` below becomes a
price the player pays. Nobody has audited these numbers under that assumption.

## How to read this — computed vs. inferred

Every figure in the table is **computed** from YAML by resolving each actor's full `Inherits` chain
(110 actors resolve to 47 distinct pool configurations; the resolver reproduces the 110/90/1 counts
already recorded in `economy.md:71`). "Full refill" is `Σ over pools of ceil(Ammo / ReloadCount) × SupplyValue`.
`ReloadDelay` shown as *(50)* is **inferred** — the pool omits it and takes the engine default
(`AmmoPool.cs:44`).

Three things are **engine behaviour, not YAML**, and every claim about pricing below rests on them:

1. **The dock-at-LC rearm charges nothing.** `Resupply.cs:301` calls `Rearmable.RearmTick`, and
   `Rearmable.cs:57-79` calls `GiveAmmo` with no reference to any `SupplyProvider` and no supply
   deduction. `Resupply.cs` contains no `RemoveSupply` and no read of `SupplyValue` at all. This is
   the free path used by **13 ground vehicle types** — every actor whose `Rearmable.RearmActors`
   names `logisticscenter` except `HIMARS` and `iskander`, which are the only two declaring
   `replenish-vehicles` (`vehicles-america.yaml:1138`, `vehicles-russia.yaml:1029`) and therefore
   the only two served by the paid `SupplyProvider` push.
2. **`ReloadAmmoPool` also charges nothing.** `ReloadAmmoPool.cs:91` calls `GiveAmmo` directly. Every
   infantry class carries one per pool gated `RequiresCondition: replenish-soldiers`, which the LC
   grants by proximity out to `4c0` (`structures.yaml:455-459`). That is the "soldier standing near a
   Logistics Centre rearms free" path.
3. **A partial batch is charged the FULL `SupplyValue`.** `SupplyProvider.cs:983-996` computes
   `roundsToGive = min(batchSize, missing)`, delivers **exactly one batch per `RearmDelay` cycle**, and
   deducts `currentSupply -= SupplyValue` unconditionally on success. `AmmoPool.GiveAmmo` clamps at
   `Info.Ammo` (`AmmoPool.cs:247`), so a short final batch is delivered short and billed whole.
   *(Note: the pseudocode at `economy.md:363-372` shows multi-batch-per-cycle math — `batchesToGive =
   min(needed, available)` — that the shipped code does not implement. The doc is stale here.)*

Tick rate is **16.67/s** (`mod.yaml:382`, default `Timestep: 60`), consistent with the
`SupplyRouteContestation` correction in `CLAUDE.md`. Durations below use it.

## Provider capacities (verified)

| Provider | `TotalSupply` | Cost | file:line |
|---|---:|---:|---|
| `LOGISTICSCENTER` | 2250 | 3000 | `structures.yaml:466`, `:422` |
| `LCCV` (undeployed, mobile) | 2250 | 3000 | `vehicles.yaml:681`, `:652` |
| `truk` | 750 | 1000 | `vehicles.yaml:569`, `:561` |
| `supplycache` | **750** | — (dropped) | `misc.yaml:468` |

LC:truck is exactly 3:1 on both supply and cash. The undeployed LCCV is a full-capacity mobile
infantry provider at the same price as the deployed building — deliberate, user-ruled 2026-08-22
(`vehicles.yaml:669-676`).

> The audit brief stated `supplycache` holds 2250. It holds **750** (`misc.yaml:468`), matching TRUK
> because a crate is the truck's own load set down (`misc.yaml:459-465`).

## The table

Every actor carrying an `AmmoPool`, collapsed to its 47 distinct configurations. `LC 2250` and
`TRUK 750` are full refills served from a fresh provider. Variant actors resolving to each row are
listed after the table.

| Actor | Cost | Pool (`Name`) | Ammo | ReloadCount | SupplyValue | ReloadDelay | Batches | Pool total | Full refill | % of Cost | LC 2250 | TRUK 750 | Rearms at | file:line |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| HIMARS | 6000 | `primary-ammo` | 2 | 1 | 1500 | *(50)* | 2 | 3000 | 3000 | 50.0% | 0.8 | 0.25 | logisticscenter | `ingame/vehicles-america.yaml:1103` |
| HELI | 6000 | `primary-ammo` | 200 | 40 | 5 | *(50)* | 5 | 25 | 1625 | 27.1% | 1.4 | 0.46 | hpad | `ingame/aircraft-america.yaml:362` |
|  |  | `secondary-ammo` | 8 | 1 | 200 | *(50)* | 8 | 1600 |  |  |  |  |  | `ingame/aircraft-america.yaml:391` |
| strykershorad | 2500 | `primary-ammo` | 400 | 50 | 1 | *(50)* | 8 | 8 | 1328 | 53.1% | 1.7 | 0.56 | logisticscenter | `ingame/vehicles-america.yaml:925` |
|  |  | `secondary-ammo` | 8 | 1 | 65 | *(50)* | 8 | 520 |  |  |  |  |  | `ingame/vehicles-america.yaml:953` |
|  |  | `tertiary-ammo` | 4 | 1 | 200 | *(50)* | 4 | 800 |  |  |  |  |  | `ingame/vehicles-america.yaml:988` |
| MI28 | 6000 | `primary-ammo` | 200 | 40 | 5 | *(50)* | 5 | 25 | 1225 | 20.4% | 1.8 | 0.61 | hpad | `ingame/aircraft-russia.yaml:362` |
|  |  | `secondary-ammo` | 8 | 1 | 150 | *(50)* | 8 | 1200 |  |  |  |  |  | `ingame/aircraft-russia.yaml:404` |
| tos | 2000 | `primary-ammo` | 24 | 3 | 120 | *(50)* | 8 | 960 | 960 | 48.0% | 2.3 | 0.78 | **none** | `ingame/vehicles-russia.yaml:740` |
| FROG | 6000 | `primary-ammo` | 60 | 5 | 75 | 25 | 12 | 900 | 900 | 15.0% | 2.5 | 0.83 | afld | `ingame/aircraft-russia.yaml:517` |
| m270 | 1800 | `primary-ammo` | 12 | 1 | 70 | *(50)* | 12 | 840 | 840 | 46.7% | 2.7 | 0.89 | **none** | `ingame/vehicles-america.yaml:806` |
| A10 | 6000 | `primary-ammo` | 100 | 25 | 5 | *(50)* | 4 | 20 | 820 | 13.7% | 2.7 | 0.91 | afld | `ingame/aircraft-america.yaml:491` |
|  |  | `secondary-ammo` | 4 | 1 | 200 | *(50)* | 4 | 800 |  |  |  |  |  | `ingame/aircraft-america.yaml:521` |
| grad | 1500 | `primary-ammo` | 40 | 5 | 85 | *(50)* | 8 | 680 | 680 | 45.3% | 3.3 | 1.10 | **none** | `ingame/vehicles-russia.yaml:613` |
| HIND | 4000 | `primary-ammo` | 150 | 30 | 1 | 6 | 5 | 5 | 645 | 16.1% | 3.5 | 1.16 | hpad | `ingame/aircraft-russia.yaml:183` |
|  |  | `secondary-ammo` | 80 | 10 | 80 | 16 | 8 | 640 |  |  |  |  |  | `ingame/aircraft-russia.yaml:214` |
| bradley | 1500 | `primary-ammo` | 900 | 100 | 5 | *(50)* | 9 | 45 | 645 | 43.0% | 3.5 | 1.16 | logisticscenter | `ingame/vehicles-america.yaml:376` |
|  |  | `secondary-ammo` | 8 | 1 | 75 | *(50)* | 8 | 600 |  |  |  |  |  | `ingame/vehicles-america.yaml:401` |
| F16 | 6000 | `primary-ammo` | 6 | 1 | 100 | 100 | 6 | 600 | 605 | 10.1% | 3.7 | 1.24 | **none** | `ingame/aircraft-america.yaml:620` |
|  |  | `secondary-ammo` | 150 | 30 | 1 | 100 | 5 | 5 |  |  |  |  |  | `ingame/aircraft-america.yaml:646` |
| MIG | 6000 | `primary-ammo` | 6 | 1 | 100 | 100 | 6 | 600 | 605 | 10.1% | 3.7 | 1.24 | afld | `ingame/aircraft-russia.yaml:640` |
|  |  | `secondary-ammo` | 150 | 30 | 1 | 100 | 5 | 5 |  |  |  |  |  | `ingame/aircraft-russia.yaml:666` |
| bmp2 | 1300 | `primary-ammo` | 900 | 100 | 5 | *(50)* | 9 | 45 | 565 | 43.5% | 4.0 | 1.33 | logisticscenter | `ingame/vehicles-russia.yaml:193` |
|  |  | `secondary-ammo` | 8 | 1 | 65 | *(50)* | 8 | 520 |  |  |  |  |  | `ingame/vehicles-russia.yaml:223` |
| tunguska | 1700 | `primary-ammo` | 180 | 30 | 1 | *(50)* | 6 | 6 | 526 | 30.9% | 4.3 | 1.43 | logisticscenter | `ingame/vehicles-russia.yaml:871` |
|  |  | `secondary-ammo` | 8 | 1 | 65 | *(50)* | 8 | 520 |  |  |  |  |  | `ingame/vehicles-russia.yaml:901` |
| m109 | 1800 | `primary-ammo` | 40 | 5 | 60 | *(50)* | 8 | 480 | 480 | 26.7% | 4.7 | 1.56 | logisticscenter | `ingame/vehicles-america.yaml:647` |
| FROG.Airstrike | 6000 | `primary-ammo` | 30 | 5 | 75 | 25 | 6 | 450 | 450 | 7.5% | 5.0 | 1.67 | **none** | `ingame/aircraft-russia.yaml:739` |
| A10.Airstrike | 6000 | `primary-ammo` | 40 | 25 | 5 | *(50)* | 2 *ceil* | 10 | 410 | 6.8% | 5.5 | 1.83 | **none** | `ingame/aircraft-america.yaml:711` |
|  |  | `secondary-ammo` | 2 | 1 | 200 | *(50)* | 2 | 400 |  |  |  |  |  | `ingame/aircraft-america.yaml:713` |
| littlebird | 3000 | `primary-ammo` | 160 | 40 | 1 | *(50)* | 4 | 4 | 404 | 13.5% | 5.6 | 1.86 | hpad | `ingame/aircraft-america.yaml:183` |
|  |  | `secondary-ammo` | 2 | 1 | 200 | *(50)* | 2 | 400 |  |  |  |  |  | `ingame/aircraft-america.yaml:218` |
| MNLY | 600 | `mines-ammo` | 10 | 1 | 25 | *(50)* | 10 | 250 | 250 | 41.7% | 9.0 | 3.00 | logisticscenter | `ingame/vehicles.yaml:484` |
| abrams | 2500 | `primary-ammo` | 40 | 5 | 30 | *(50)* | 8 | 240 | 240 | 9.6% | 9.4 | 3.12 | logisticscenter | `ingame/vehicles-america.yaml:530` |
| t90 | 2400 | `primary-ammo` | 40 | 5 | 30 | *(50)* | 8 | 240 | 240 | 10.0% | 9.4 | 3.12 | logisticscenter | `ingame/vehicles-russia.yaml:348` |
| MT.america | 300 | `primary-ammo` | 25 | 5 | 40 | *(50)* | 5 | 200 | 200 | 66.7% | 11.2 | 3.75 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:1568` |
| SN.america | 400 | `primary-ammo` | 50 | 5 | 20 | *(50)* | 10 | 200 | 200 | 50.0% | 11.2 | 3.75 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:1636` |
| AT.america | 300 | `primary-ammo` | 3 | 1 | 65 | *(50)* | 3 | 195 | 195 | 65.0% | 11.5 | 3.85 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:1711` |
| t72 | 1700 | `primary-ammo` | 40 | 5 | 20 | *(50)* | 8 | 160 | 160 | 9.4% | 14.1 | 4.69 | logisticscenter | `ingame/vehicles-ukraine.yaml:53` |
| E6 | 250 | `primary-ammo` **⚠ not in `Rearmable.AmmoPools`** | 100 | 20 | 1 | *(50)* | 5 | 5 | 155 | 62.0% | 14.5 | 4.84 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:1870` |
|  |  | `secondary-ammo` | 3 | 1 | 50 | *(50)* | 3 | 150 |  |  |  |  |  | `ingame/infantry.yaml:1898` |
| CRAM | 1000 | `AmmoPool` | 24 | 4 | 20 | 6 | 6 | 120 | 120 | 12.0% | 18.8 | 6.25 | **none** | `ingame/structures-defenses.yaml:643` |
| AGUN | 800 | `AmmoPool` | 24 | 4 | 20 | 6 | 6 | 120 | 120 | 15.0% | 18.8 | 6.25 | **none** | `ingame/structures-defenses.yaml:721` |
| SF.america | 600 | `primary-ammo` | 100 | 20 | 1 | *(50)* | 5 | 5 | 104 | 17.3% | 21.6 | 7.21 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:2102` |
|  |  | `secondary-ammo` | 3 | 1 | 33 | *(50)* | 3 | 99 |  |  |  |  |  | `ingame/infantry.yaml:2133` |
| E3.america | 100 | `primary-ammo` | 100 | 20 | 1 | *(50)* | 5 | 5 | 55 | 55.0% | 40.9 | 13.64 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:1216` |
|  |  | `secondary-ammo` | 1 | 1 | 50 | *(50)* | 1 | 50 |  |  |  |  |  | `ingame/infantry.yaml:1245` |
| TL.america | 200 | `primary-ammo` | 100 | 20 | 1 | *(50)* | 5 | 5 | 53 | 26.5% | 42.5 | 14.15 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:1478` |
|  |  | `secondary-ammo` | 6 | 1 | 8 | *(50)* | 6 | 48 |  |  |  |  |  | `ingame/infantry.yaml:1503` |
| E2.america | 100 | `primary-ammo` | 30 | 6 | 10 | *(50)* | 5 | 50 | 50 | 50.0% | 45.0 | 15.00 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:1400` |
| DR.america | 150 | `primary-ammo` | 1 | 1 | 25 | *(50)* | 1 | 25 | 25 | 16.7% | 90.0 | 30.00 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:2428` |
| AR.america | 100 | `primary-ammo` | 500 | 50 | 1 | *(50)* | 10 | 10 | 10 | 10.0% | 225.0 | 75.00 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:1333` |
| m113 | 700 | `primary-ammo` | 500 | 50 | 1 | *(50)* | 10 | 10 | 10 | 1.4% | 225.0 | 75.00 | logisticscenter | `ingame/vehicles-america.yaml:245` |
| btr | 600 | `primary-ammo` | 500 | 50 | 1 | *(50)* | 10 | 10 | 10 | 1.7% | 225.0 | 75.00 | logisticscenter | `ingame/vehicles-russia.yaml:70` |
| humvee | 500 | `primary-ammo` | 300 | 50 | 1 | *(50)* | 6 | 6 | 6 | 1.2% | 375.0 | 125.00 | logisticscenter | `ingame/vehicles-america.yaml:89` |
| E1.america | 50 | `primary-ammo` | 100 | 20 | 1 | *(50)* | 5 | 5 | 5 | 10.0% | 450.0 | 150.00 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:1134` |
| ^PILOT | 500 | `primary-ammo` | 100 | 20 | 1 | *(50)* | 5 | 5 | 5 | 1.0% | 450.0 | 150.00 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:2513` |
| PILOTR1 | 800 | `primary-ammo` | 100 | 20 | 1 | *(50)* | 5 | 5 | 5 | 0.6% | 450.0 | 150.00 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:2513` |
| PILOTR2 | 1200 | `primary-ammo` | 100 | 20 | 1 | *(50)* | 5 | 5 | 5 | 0.4% | 450.0 | 150.00 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:2513` |
| PILOTR3 | 2000 | `primary-ammo` | 100 | 20 | 1 | *(50)* | 5 | 5 | 5 | 0.2% | 450.0 | 150.00 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:2513` |
| PILOTR4 | 3000 | `primary-ammo` | 100 | 20 | 1 | *(50)* | 5 | 5 | 5 | 0.2% | 450.0 | 150.00 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:2513` |
| ^CrewMember | 100 | `primary-ammo` | 24 | 8 | 1 | *(50)* | 3 | 3 | 3 | 3.0% | 750.0 | 250.00 | **none** | `ingame/crew.yaml:28` |
| E4.america | 150 | `primary-ammo` | 90 | 30 | 1 | *(50)* | 3 | 3 | 3 | 2.0% | 750.0 | 250.00 | truk, supplycache, logisticscenter | `ingame/infantry.yaml:2024` |
| FTUR | 1000 | `AmmoPool` | 10 | 5 | 1 | 40 | 2 | 2 | 2 | 0.2% | 1125.0 | 375.00 | **none** | `ingame/structures-defenses.yaml:932` |
### Variants collapsed into the rows above

- **HIMARS** — identical: `iskander`
- **m109** — identical: `giatsint`
- **MT.america** — identical: `MT`, `MT.russia`, `^MT`
- **SN.america** — identical: `SN`, `SN.russia`, `^SN`
- **AT.america** — identical: `AA`, `AA.america`, `AA.russia`, `AT`, `AT.russia`, `^AA`, `^AT`
- **E6** — identical: `E6.america`, `E6.russia`, `^E6`
- **SF.america** — identical: `SF`, `SF.russia`, `^SF`
- **E3.america** — identical: `E3`, `E3.colorpicker`, `E3.russia`, `E3R1`, `E3R1.america`, `E3R1.russia`, `^E3`
- **TL.america** — identical: `TL`, `TL.russia`, `^TL`
- **E2.america** — identical: `E2`, `E2.russia`, `E2R1`, `E2R1.america`, `E2R1.russia`, `^E2`
- **DR.america** — identical: `DR`, `DR.russia`, `^DR`
- **AR.america** — identical: `AR`, `AR.russia`, `^AR`
- **E1.america** — identical: `E1`, `E1.russia`, `E1R1`, `E1R1.america`, `E1R1.russia`, `^E1`
- **^PILOT** — identical: `PILOT`
- **^CrewMember** — identical: `crew.commander.america`, `crew.commander.russia`, `crew.copilot.america`, `crew.copilot.russia`, `crew.driver.america`, `crew.driver.russia`, `crew.gunner.america`, `crew.gunner.russia`, `crew.pilot.america`, `crew.pilot.russia`
- **E4.america** — identical: `E4`, `E4.russia`, `^E4`
---

# Findings, ranked by whether the player feels it

## 1. The Logistics Centre was sized against a number that is wrong by 11×, and the wrong number is still in the tree

`mods/ww3mod/rules/ingame/structures.yaml:486-487`, in the comment block that justifies the LC's
`AuraRearmCondition` infantry arm:

> *"An E3 rifleman is Ammo 100 / ReloadCount 20 / SupplyValue 1 (infantry.yaml), i.e. 5 supply for a
> full refill — so 2250 buys ~450 rifleman refills against the truck's ~150."*

It counts only the DMR pool. The E3 also carries `AmmoPool@2 secondary-ammo`, one RPG round at
`SupplyValue: 50` (`infantry.yaml:1245-1251`), and that pool **is** in his
`Rearmable.AmmoPools: primary-ammo, secondary-ammo` (`infantry.yaml:1282`), so a host refills it.

| | claimed | actual |
|---|---:|---:|
| E3 full refill | 5 | **55** |
| refills per LC (2250) | ~450 | **~41** |
| refills per truck (750) | ~150 | **~13** |

This outranks everything else because it is not a comment error — it is the sentence the depot
economy was sized against. At the shipped `AuraRearmDelay: 6` (`structures.yaml:490`) an E3 takes 6
delivery cycles (5 rifle batches + 1 RPG batch) = 36 ticks ≈ **2.2 s** and 55 supply, so a full
Logistics Centre is roughly **90 seconds of continuous infantry service**. Whoever chose 2250
believed it was closer to fifteen minutes.

**Fix:** correct the comment, then decide whether 2250 is still the number you want now that you can
see what it buys.

## 2. The rifleman is 5.5× more expensive to sustain than the automatic rifleman, at the same purchase price

| | Cost | full refill | % of cost | refills/LC | file:line |
|---|---:|---:|---:|---:|---|
| `^E3` rifleman | 100 | **55** | 55.0% | 41 | `infantry.yaml:1216`, `:1245` |
| `^AR` automatic rifleman | 100 | **10** | 10.0% | 225 | `infantry.yaml:1333` |

Identical cash. The entire difference is E3's single RPG round at `SupplyValue: 50`. AR also carries
**5× the magazine** (500 rounds vs 100) at half the per-round price (0.02 vs 0.05 supply/round).

Under the incoming change a player who fields ARs instead of E3s gets an identical-cash infantry
force that sustains 5.5× longer per depot, and gives up one rocket per man. On the mod's most-bought
unit that is a dominant-strategy risk, and it is the finding most likely to change how the game is
actually played.

Sharpening it against armour: one E3 RPG round costs 50 supply; an Abrams shell costs 6
(`240 / 40 rounds`, `vehicles-america.yaml:530-544`). **One RPG round prices at 8.3 tank shells.** An
infantry anti-tank screen costs materially more supply per shot than the armour it exists to kill.

## 3. The mortar team is the most expensive thing in the game relative to its price, and it exceeds the doc's own ceiling

`^MT` — Cost 300, full refill **200 = 66.7%** (`infantry.yaml:1568-1576`).

`economy.md:261` sets the band for missile-tier infantry at **30–65%**. MT is above it, and MT is not
a missile unit — it is 25 tube-fired bombs at `ReloadCount: 5`, `SupplyValue: 40`.

An LC serves **11** mortar refills. A single 300-credit mortar team that fires two full loads has
consumed **400 supply — 18% of an entire Logistics Centre.**

The rounding rule (engine fact 3 above) bites hardest exactly here in *practice*, because
`60mm_Mortar` is `Burst: 1` (`weapons-ballistics.yaml:784`) against `ReloadCount: 5`: **a mortar team
that fired one bomb and walked back to the truck is charged the same 40 supply as one that fired
five.** Four out of five return trips overpay.

## 4. Supply cost tracks munition type, not purchase price — a 55× spread across buyable ground units

This is the direct answer to *"are all other units well balanced in terms of supply cost compared to
their total price?"* **No — and price is not the axis they are balanced on.**

Supply consumed per 1000 credits of army value, buyable ground units only:

| supply / 1000 cash | units |
|---:|---|
| 12–17 | `humvee` 12, `m113` 14, `btr` 17 |
| 20 | `^E4` flamer 20 |
| 94–100 | `t72` 94, `abrams` 96, `t90` 100, `^AR` 100 |
| 167–173 | `^DR` 167, `^SF` 173 |
| 265–309 | `^TL` 265, `m109`/`giatsint` 267, `tunguska` 309 |
| 417–500 | `MNLY` 417, `grad` 453, `m270` 467, `tos` 480, `^E2` 500, `^SN` 500, `HIMARS`/`iskander` 500 |
| 531–667 | `strykershorad` 531, `^E3` 550, `^E6` 620, `^AT`/`^AA` 650, `^MT` **667** |

One Logistics Centre fully refills **22,500 credits of Abrams** or **3,300 credits of mortar teams** —
a 6.8× difference in army value sustained per depot, decided entirely by what the unit shoots.

Whether that is a defect is a design call, not an audit finding. What *is* an audit finding: it means
an armour-heavy force is close to logistics-free while an infantry/missile force lives on the supply
line, and no `TotalSupply` number can be right for both. If depots are sized for infantry they are
irrelevant to tanks; if sized for tanks they starve infantry.

## 5. Three rocket-artillery pieces carry the most expensive pools in the ground roster and can never spend a single point of supply

| | Cost | full refill | % | `Rearmable` |
|---|---:|---:|---:|---|
| `tos` | 2000 | 960 | 48.0% | **none** — `vehicles-russia.yaml:740` |
| `m270` | 1800 | 840 | 46.7% | **none** — `vehicles-america.yaml:806` |
| `grad` | 1500 | 680 | 45.3% | **none** — `vehicles-russia.yaml:613` |

They sit at #3, #5 and #8 in the "most expensive refill" ordering and a reader will assume they drain
depots. **They cannot.** No `Rearmable` trait means no host, by either path (documented as design at
`economy.md:31`, `:117`). Their `SupplyValue` exists solely as a `CustomSellValue` evac deduction —
a spent `m270` rotates off the map for `1800 − 840 = 960` × HP/MaxHP.

Stated here because the incoming change does not touch them, and any "these are our biggest supply
sinks" reading of the table is wrong on three of its top eight rows.

## 6. `^E6`'s rifle is the one place the incoming change could create a new dead-unit state

`^E6` engineer carries `AmmoPool@1 primary-ammo` (100-round MP5, `infantry.yaml:1870-1877`) that is
**absent from his `Rearmable.AmmoPools: secondary-ammo`** (`infantry.yaml:1954`). This is the single
divergence in the corpus and is already documented (`economy.md:71`). What the audit adds is the
consequence *under the new pricing*:

E6's rifle has exactly one refill route today — the free `ReloadAmmoPool@1` at `infantry.yaml:1878`.
No host will ever touch it, because `Rearmable.RearmTick`, the `SupplyProvider` aura and `QuickRearm`
all iterate the filtered `RearmableAmmoPools` (`Rearmable.cs:44`).

**If the incoming change removes or gates `ReloadAmmoPool`, the E6 loses his rifle permanently after
one magazine.** He keeps his C4/mines (the listed pool), so he is not dry and not dispatched — he
just stops shooting, forever, with no indication why. Worth checking before merge: either add
`primary-ammo` to his `Rearmable.AmmoPools`, or confirm the change leaves `ReloadAmmoPool` free.

## 7. Two expensive pools name armaments that do not exist, so their dry-dispatch trigger is idle-only

| pool | declares | actual armaments on the actor | pool value |
|---|---|---|---:|
| `^E6.secondary-ammo` (`infantry.yaml:1898-1901`) | `Armaments: secondary` | `primary`, `repair`, `clearmines` | **150 of 155** |
| `^SF.secondary-ammo` (`infantry.yaml:2133-2136`) | `Armaments: c4` | `primary` | **99 of 104** |

Both are consumed by traits (`Demolition.UseAmmo`, `Minelayer.AmmoPoolName`) rather than by an
`Armament`. Per `economy.md:53`, `AmmoPool`'s `INotifyAttack.Attacking` dispatch is keyed on
`Info.Armaments.Contains(a.Info.Name)` (`AmmoPool.cs:658`), so it can never fire for these pools —
only the `INotifyBecomingIdle` path reaches them. Pre-existing, not caused by the change, but these
are the *expensive* halves of both units (97% and 95% of their refill bill), so it is where a missed
resupply trigger costs most.

## 8. Flamer vs grenadier: two comparable close-assault classes, 17× apart on sustain

| | Cost | full refill | % | empty-evac refund |
|---|---:|---:|---:|---:|
| `^E4` flamethrower | 150 | **3** | 2.0% | 147 |
| `^E2` grenadier | 100 | **50** | 50.0% | 50 |

`infantry.yaml:2024-2033` vs `:1400-1409`. E4 is the cheapest-to-sustain armed unit in the infantry
roster by an order of magnitude; his 90 flame ticks cost 3 supply total. Nothing about the two roles
justifies 17×, and E4 also breaks the tier table below.

## 9. The empty-evac tier table in `economy.md` no longer describes the YAML

`economy.md:341` asserts ~100 refund for the whole "line infantry" tier. Computed
(`Cost − full refill`):

| unit | tier target | actual | Δ |
|---|---:|---:|---:|
| `^E3` | ~100 | **45** | −55% |
| `^E2` | ~100 | **50** | −50% |
| `^E4` | ~100 | **147** | +47% |
| `^DR` | ~100 | **125** | +25% |
| `^AR` | ~100 | 90 | −10% |
| `^E6` | ~100 | 95 | ✓ |
| `^MT` / `^AT` / `^AA` | ~100 | 100 / 105 / 105 | ✓ |
| `^TL` | ~150 | 147 | ✓ |
| `^SN` | ~200 | 200 | ✓ |
| `^SF` / `^PILOT` | ~500 | 496 / 495 | ✓ |
| `^E1` | ~50 | 45 | ✓ |

Five of thirteen hold exactly; four miss by ≥25%. The specialist tiers are clean and the line tier is
not — which is the same four units (E3, E2, E4, DR) that findings 2 and 8 flag on other grounds.

## 10. Where whole-batch rounding costs most, in absolute supply

Engine fact 3: a top-up of a single missing round is billed one whole `SupplyValue`. Supply paid for
one round, worst first:

| unit | pool | `ReloadCount` | `SupplyValue` | per-round price | paid for 1 round | overcharge |
|---|---|---:|---:|---:|---:|---:|
| `tos` | primary | 3 | 120 | 40 | 120 | 3× |
| `grad` | primary | 5 | 85 | 17 | 85 | 5× |
| `HIND` | secondary | 10 | 80 | 8 | 80 | 10× |
| `m109` / `giatsint` | primary | 5 | 60 | 12 | 60 | 5× |
| `^MT` | primary | 5 | 40 | 8 | 40 | 5× |
| `abrams` / `t90` | primary | 5 | 30 | 6 | 30 | 5× |
| `t72` | primary | 5 | 20 | 4 | 20 | 5× |
| `^E2` | primary | 6 | 10 | 1.67 | 10 | 6× |

Only `^MT`, `abrams`, `t90`, `t72`, `m109` and `giatsint` are reachable in practice — `tos`/`grad`
have no host at all (finding 5) and `HIND` has no host on any shipped map (finding 12). Of those,
**`^MT` is the one that actually hurts**, per finding 3.

`m109`/`giatsint` are the mild case worth naming: `ArtilleryRound.Paladin` is `Burst: 3` and
`ArtilleryRound.Giatsint` is `Burst: 1` against `ReloadCount: 5`, so a Paladin's shell count is a
multiple of the batch size only every fifth trip; mean overpay ≈ 2 shells ≈ 24 supply, about 5% of a
full load.

## 11. One pool is not an exact multiple of its batch size

`A10.Airstrike primary-ammo`: `Ammo: 40`, `ReloadCount: 25` (`aircraft-america.yaml:711-712`) → 2
batches charged, the second holding 15. This is the exact pattern the Paladin comment
(`vehicles-america.yaml:650-655`) was written to prevent: `CustomSellValue` **floors** missing batches
while the tooltip **ceilings** them, so the remainder is advertised and then refunded free.

Impact is nil in play — `A10.Airstrike` is a support-power actor, not buildable, and has no
`Rearmable` — but it is the one live instance of the pattern, and worth closing so the invariant the
two artillery comments assert is actually true corpus-wide.

## 12. Aircraft supply values are unreachable on every shipped map

Eight airframes carry 605–1625 supply of pools each (`HELI` 1625, `MI28` 1225, `FROG` 900, `A10` 820,
`HIND` 645, `F16`/`MIG` 605, `littlebird` 404). Every one names `hpad` or `afld` as its only
`RearmActors`, both are `~disabled` and neither is pre-placed on any of the ten shipped maps
(`economy.md:85`). `F16` additionally removes `Rearmable` outright (`-Rearmable:`,
`aircraft-america.yaml:692`).

So none of those numbers can ever charge a depot. They matter only through `CustomSellValue` on
evacuation. Not a defect — it is the documented 2026-08-19 ruling — but it means **the incoming
change has no effect on ~30% of the pool-carrying roster**, and any post-merge measurement of depot
drain should exclude aircraft rather than wonder why they contribute nothing.

## 13. The drone operator is charged per launch, not per loss

`^DR` — Cost 150, `primary-ammo` `Ammo: 1`, `SupplyValue: 25` (`infantry.yaml:2428-2437`). The pool is
consumed by `CarrierMaster` at `CarrierMaster.cs:235` (`ammoPool.TakeAmmo(self, 1)`), and
`CarrierMaster` contains **no** `GiveAmmo` call anywhere. The armament sets `AmmoUsage: 0`
(`infantry.yaml:2424`) so aiming does not spend it.

Consequence: a drone that flies a sortie and returns intact still leaves its operator dry, and the
next launch costs a full 25-supply reload from a host. Whether that is intended is a design call —
the in-file comment does say "disposable quadcopter" — but it is worth stating explicitly while the
drone scoring work is in flight, because it makes every recon sortie a supply transaction rather than
a free repeat.

*(Contrast `EnterCarrierMaster.cs:49-53`, which refills every pool on the returning **slave** bypassing
`Rearmable`. That is the drone's own ammunition, not the operator's launch count.)*

## 14. Doc drift in `economy.md` — reported only, not edited

`DOCS/reference/` is curated and promotion is a separate pass, so nothing below was touched.

| `economy.md` | claims | actual | source |
|---|---|---|---|
| `:171`, `:356` | LC `TotalSupply` 3000 | **2250** | `structures.yaml:466` |
| `:21`, `:177` | LCCV Cost 1200 | **3000** | `vehicles.yaml:652` |
| `:177`, `:224` | LC Cost 3500 | **3000** | `structures.yaml:422` |
| `:278`, `:286` | `m109` / `giatsint` `Ammo: 39` | **40** | `vehicles-america.yaml:656`, `vehicles-russia.yaml:470` |
| `:363-372` | rearm gives `min(needed, available)` batches per cycle | **exactly one batch per cycle** | `SupplyProvider.cs:983-996` |
| `:341` | line-infantry empty evac ~100 | four of nine miss by ≥25% | finding 9 |

The `RefundPercent` safety invariant at `economy.md:177` — `RefundPercent ≤ 100 × LCCV.Cost /
logisticscenter.Cost` — still **holds** at the current numbers: 34 ≤ 100 × 3000/3000. The doc's
worked example is stale; the property it protects is intact.

Also stale, in engine code rather than the doc: `AmmoPool.cs:52-57` describes `Essential` as shipping
"inert until someone does it". **35 pools now author `Essential: true`** across `vehicles-*.yaml`,
`infantry.yaml` and `crew.yaml`.

---

# What was checked and is fine

Stated explicitly because "the rest are coherent" is a result, and I would rather you know it was
looked at than assume it was skipped.

**Both factions are coherent on ammunition.** This was the finding I most expected to find and did
not. Every America/Russia pair either matches exactly or differs for a stated, documented reason:

| pair | America | Russia | verdict |
|---|---:|---:|---|
| Long-range missile | `HIMARS` 3000 (50%) | `iskander` 3000 (50%) | **identical** |
| Tube artillery | `m109` 480 (26.7%) | `giatsint` 480 (26.7%) | **identical** |
| Fighter | `F16` 605 (10.1%) | `MIG` 605 (10.1%) | **identical** |
| MBT | `abrams` 240 (9.6%) | `t90` 240 (10.0%) | within 0.4 pp |
| IFV | `bradley` 645 (43.0%) | `bmp2` 565 (43.5%) | within 0.5 pp |
| APC | `m113` 10 (1.4%) | `btr` 10 (1.7%) | identical pools |
| Rocket artillery | `m270` 840 (46.7%) | `grad` 680 (45.3%), `tos` 960 (48.0%) | 45–48% band |
| SHORAD | `strykershorad` 520 SAM | `tunguska` 520 SAM | **identical on the SAM** |

`t72` (Ukraine, 160 = 9.4% of 1700) lands inside the MBT band despite a different `SupplyValue`
(20 vs 30) — the ratio is what is held constant, and it is held.

**`strykershorad` vs `tunguska` is role, not price.** The raw totals look like a 2.5× faction
asymmetry (1328 vs 526) and they are not: both carry `Ammo: 8` short-range SAMs at `SupplyValue: 65`,
identical. The Stryker's extra 800 is a 4-round Hellfire pod (`vehicles-america.yaml:988-996`) that
`tunguska` simply does not have — a ground-attack capability, priced at the universal Hellfire rate.

**The munition consistency rule holds everywhere it is asserted.** Checked all four families from
`economy.md:327-331`: Hellfire 200 on `HELI`, `A10`, `littlebird`, `strykershorad`; MANPAD/short-SAM
65 on `strykershorad`, `tunguska`, `^AT`, `^AA`; air-to-air 100 on `F16` and `MIG`. Mi-28 at 150 is
**not** a violation — it fires Ataka, not Hellfire, and the divergence is deliberate and reasoned in
file (`aircraft-russia.yaml:409-413`).

**Provider capacities are internally consistent.** LC 2250 / cost 3000 against truck 750 / cost 1000
is exactly 3:1 on both axes. Undeployed LCCV matches the deployed LC on capacity, per the
2026-08-22 ruling. `supplycache` matches TRUK exactly on `Range`, `RearmDelay` and `TotalSupply`.
`SupplyCreditValue` tracks `TotalSupply` rather than `Cost` at all three sites.

**`Rearmable.AmmoPools` cross-check is clean apart from the known `^E6` case.** Every one of the 110
resolved actors was checked in both directions: no actor lists a pool that does not exist (0 ghost
entries), and `^E6.primary-ammo` is the **only** pool that exists but is not listed. The
load-time guard at `AmmoPool.cs:154-169` that would catch an `Essential` pool missing from the list
is satisfied by all 35 `Essential` sites.

**Rounding is a near-non-issue at the YAML level.** Exactly one pool of 63 has `Ammo` not an exact
multiple of `ReloadCount` (finding 11). The real rounding exposure is the engine's whole-batch
billing (finding 10), not the authored values.

**`HIMARS` / `iskander` at 3000 against a 2250 depot** — confirmed identical, confirmed `Cost: 6000`
both, left alone per the standing user ruling. Recomputed as the benchmark for "how extreme is too
extreme": 50% of purchase price, 1.33 depots per full load.

**Reference points from the brief, all recomputed independently and all correct:** `abrams` 240
(9.4 per LC), `bradley` 645 (3.5), `^AR` 10 (225), `^E3` 55 (40.9), `HIMARS`/`iskander` 3000
(0.75 — cannot be served by one depot).
