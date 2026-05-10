# Ammo Value Reference

**Last revised:** 2026-05-10 (recalibrated against a consistent infantry empty-evac baseline + real-world ATGM/Hellfire ratios)

This is the per-unit reference for `SupplyValue` and `CreditValue` on every `AmmoPool` in the mod, plus the rationale behind each one. Read alongside `mods/ww3mod/rules/ingame/{vehicles,aircraft,infantry,structures-defenses}*.yaml`.

## What the two fields do

In `engine/OpenRA.Mods.Common/Traits/AmmoPool.cs`:

| Field | Used by | Effect |
|---|---|---|
| `SupplyValue` | `SupplyProvider`, `CargoSupply` (rearm) | How much **supply budget** a Logistics Center / supply truck spends to refill **one** missing round in this pool. |
| `CreditValue` | `CustomSellValue.GetSellValue` | How much **cash** is deducted from the unit's evac/sell refund per **missing** round in this pool. |

Sell/evac math: `refund = max(0, Cost − sum_over_pools(missing × CreditValue))`. The engine clamps the floor at zero.

The bug that triggered this sweep was a wildly disproportionate **displayed** ammo value — e.g. a Bradley showed "Ammo: 900 × 15 supply = 13500" while costing 1500 to call in. The values below pull every pool into a sensible band.

## Design principles

### Infantry: consistent **empty-evac base**

Most line-infantry classes train similarly (rifleman, LMG gunner, grenadier, AT specialist all need similar drilling). The cost above that body+training baseline is the ammunition load. So when a soldier evacuates with **all ammo expended**, they should refund roughly the same baseline — only the ammo budget is consumed.

| Tier | Empty evac refund | Examples |
|---|---|---|
| Conscript | ~50 | E1 |
| Line infantry / specialist | ~100 | E3 (rifleman+RPG), AR (LMG), E2 (grenadier), MT (mortar), AT (ATGM), AA (MANPAD), E4 (flame), E6 (engineer), MEDI, DR (drone), MT |
| Squad role with extra training | ~150 | TL (team leader) |
| Premium specialist | ~200 | SN (sniper) |
| Elite | ~500 | SF (special forces), PILOT (and ranks) |

Per-pool CreditValue is then `CreditValue = (Cost − base) / Ammo`.

### Vehicles & aircraft: anchor common munitions to a single rate

Hellfire is Hellfire whether it's on a Littlebird, Apache, A-10, MI-28, or Stryker. ATGMs are ATGMs whether on a Bradley, BMP-2, or AT specialist. Same for Stinger/MANPADS/9M311 — all short-range SAMs.

| Munition class | CreditValue | Rationale |
|---|---|---|
| **ATGM** (TOW, Konkurs, Javelin) | **65–75** | ~40% of an IFV's cost when fully loaded (8 missiles), matching real-world. Bradley uses 75; BMP-2 / infantry AT use 65. |
| **MANPAD / short-range SAM** (Stinger, 9M311) | **65** | Mirrors infantry MANPAD; same on Stryker SHORAD and Tunguska. |
| **Hellfire-class AGM** | **200** | Apache/MI-28/A-10/Littlebird/Stryker SHORAD all charge the same rate. Empty Apache evac ≈ 4400 (73%). |
| **Air-to-air missile** (AIM-9X, R-77) | **100** | F-16 and MiG match. |
| **Tank main gun** | **6** (Abrams/T-90) **/ 4** (T-72) | ~5–10% of cost — per the user's "5% for an Abrams" example. Tank ammo is cheap relative to a tank. |
| **Bulk autocannon / MG / SMG / rifle / flame fuel** | **0** | High-ammo-count weapons would dwarf unit cost at any non-zero CV. SupplyValue stays at 1 so the rearm tooltip still renders. |
| **Mortar / sniper / artillery shell** | per-shell, scaled to evac base | See infantry table. |
| **MLRS / HIMARS / Iskander rocket** | **17–1500** | Per-rocket value targets ~45–50% of platform cost (one-shot magazine doctrine — the rocket pod *is* the platform). |

### Why some pools have `CreditValue: 0`

Engine math: `missingAmmoValue += missing × CreditValue`. For a 900-round 25mm Bushmaster, even `CreditValue: 1` deducts 900 from a 1500 Bradley — wiping the IFV body's value because of the autocannon rounds it fired. Setting `CreditValue: 0` says "this ammo is fungible enough we won't deduct on missing rounds." The pool still has a `SupplyValue: 1` so the resupply tooltip renders and rearm has a token cost.

## Per-unit table

Columns: **Cost** = `Valued.Cost`. **Pool** = AmmoPool name & ammo count. **SV** = `SupplyValue`. **CV** = `CreditValue`. **Total** = `sum(Ammo × CreditValue)` across all pools — i.e. the maximum amount that could be deducted from the evac/sell refund. **% Cost** = Total ÷ Cost. **Empty refund** = Cost − Total.

### Vehicles — America (`mods/ww3mod/rules/ingame/vehicles-america.yaml`)

| Unit | Cost | Pool | Ammo | SV | CV | Total | % Cost | Empty refund | Note |
|---|---|---|---|---|---|---|---|---|---|
| humvee | 450 | 7.62 MG | 300 | 1 | 0 | 0 | 0% | 450 | Bulk MG → CV=0. |
| m113 | 700 | 12.7 HMG | 500 | 1 | 0 | 0 | 0% | 700 | Bulk HMG → CV=0. |
| bradley | 1500 | 25mm autocannon | 900 | 1 | 0 | 0 | — | — | Autocannon → CV=0. |
| bradley | 1500 | TOW | 8 | 75 | 75 | 600 | **40%** | 900 | Real-world Bradley ATGM ratio. |
| abrams | 2500 | 120mm | 40 | 6 | 6 | 240 | 9.6% | 2260 | Per "5% of tank value" guidance. |
| m109 (Paladin) | 1800 | 155mm | 39 | 12 | 12 | 468 | 26% | 1332 | Unchanged. |
| m270 MLRS | 1800 | 227mm rocket | 12 | 70 | 70 | 840 | 47% | 960 | Unchanged — one-shot magazine. |
| strykershorad | 2500 | 25mm autocannon | 400 | 1 | 0 | 0 | — | — | Autocannon → CV=0. |
| strykershorad | 2500 | Stinger | 8 | 65 | 65 | 520 | — | — | Mirrors MANPAD rate. |
| strykershorad | 2500 | Hellfire | 4 | 200 | 200 | 800 | **53% (combined)** | 1180 | Mirrors universal Hellfire rate. |
| HIMARS | 6000 | GMLRS missile | 2 | 1500 | 1500 | 3000 | 50% | 3000 | Unchanged. |

### Vehicles — Russia (`mods/ww3mod/rules/ingame/vehicles-russia.yaml`)

| Unit | Cost | Pool | Ammo | SV | CV | Total | % Cost | Empty refund | Note |
|---|---|---|---|---|---|---|---|---|---|
| btr | 600 | 14.5 HMG | 500 | 1 | 0 | 0 | 0% | 600 | Bulk HMG. |
| bmp2 | 1300 | 30mm autocannon | 900 | 1 | 0 | 0 | — | — | Autocannon → CV=0. |
| bmp2 | 1300 | WGM (Konkurs) | 8 | 65 | 65 | 520 | **40%** | 780 | Mirrors Bradley TOW ratio. |
| t90 | 2400 | 125mm | 40 | 6 | 6 | 240 | 10% | 2160 | Mirrors Abrams. |
| giatsint (2S5) | 1800 | 152mm | 39 | 12 | 12 | 468 | 26% | 1332 | Unchanged. |
| grad (BM-21) | 1500 | 122mm rocket | 40 | 17 | 17 | 680 | 45% | 820 | Unchanged. |
| tos (TOS-1) | 2000 | 220mm thermobaric | 24 | 40 | 40 | 960 | 48% | 1040 | Unchanged. |
| tunguska | 1700 | 30mm AA autocannon | 180 | 1 | 0 | 0 | — | — | Autocannon → CV=0. |
| tunguska | 1700 | 9M311 SAM | 8 | 65 | 65 | 520 | **31%** | 1180 | Mirrors short-range SAM rate. |
| iskander | 6000 | 9M723 missile | 2 | 1500 | 1500 | 3000 | 50% | 3000 | Unchanged. |

### Vehicles — Ukraine (`mods/ww3mod/rules/ingame/vehicles-ukraine.yaml`)

| Unit | Cost | Pool | Ammo | SV | CV | Total | % Cost | Empty refund | Note |
|---|---|---|---|---|---|---|---|---|---|
| t72 | 1700 | 125mm | 40 | 4 | 4 | 160 | 9.4% | 1540 | Mirrors T-90/Abrams; older-gen ammo a touch cheaper. |

### Aircraft — America (`mods/ww3mod/rules/ingame/aircraft-america.yaml`)

| Unit | Cost | Pool | Ammo | SV | CV | Total | % Cost | Empty refund | Note |
|---|---|---|---|---|---|---|---|---|---|
| TRAN (Chinook) | 2000 | — | — | — | — | — | — | 2000 | Unarmed transport. |
| littlebird | 3000 | 7.62 minigun | 160 | 1 | 0 | 0 | — | — | Bulk minigun. |
| littlebird | 3000 | Hellfire | 2 | 200 | 200 | 400 | **13%** | 2600 | Universal Hellfire rate. |
| HELI (Apache) | 6000 | 30mm chain gun | 200 | 3 | 0 | 0 | — | — | Bulk chain gun. |
| HELI (Apache) | 6000 | Hellfire | 8 | 200 | 200 | 1600 | **27%** | 4400 | Universal Hellfire rate. |
| A10 | 6000 | 30mm GAU-8 | 100 | 3 | 0 | 0 | — | — | Bulk GAU-8. |
| A10 | 6000 | Hellfire | 4 | 200 | 200 | 800 | **13%** | 5200 | Universal Hellfire rate. |
| F16 | 6000 | AAM | 6 | 100 | 100 | 600 | — | — | AIM-class. |
| F16 | 6000 | 20mm M61 | 150 | 1 | 0 | 0 | **10% (combined)** | 5400 | Cannon → CV=0. |

### Aircraft — Russia (`mods/ww3mod/rules/ingame/aircraft-russia.yaml`)

| Unit | Cost | Pool | Ammo | SV | CV | Total | % Cost | Empty refund | Note |
|---|---|---|---|---|---|---|---|---|---|
| HALO (Mi-26) | 2000 | — | — | — | — | — | — | 2000 | Unarmed transport. |
| HIND (Mi-24) | 4000 | 12.7 chin gun | 150 | 1 | 0 | 0 | — | — | Bulk MG. |
| HIND (Mi-24) | 4000 | S-8 rocket pod | 80 | 8 | 8 | 640 | **16%** | 3360 | Unguided rockets. |
| MI28 | 6000 | 30mm chain gun | 200 | 3 | 0 | 0 | — | — | Mirrors Apache primary. |
| MI28 | 6000 | Hellfire-class | 8 | 200 | 200 | 1600 | **27%** | 4400 | Universal Hellfire rate. |
| FROG (Su-25) | 6000 | rocket pod | 60 | 15 | 15 | 900 | **15%** | 5100 | Strike-aircraft rocket payload. |
| MIG (MiG-29) | 6000 | AAM | 6 | 100 | 100 | 600 | — | — | Mirrors F-16 AAM. |
| MIG (MiG-29) | 6000 | 20mm cannon | 150 | 1 | 0 | 0 | **10% (combined)** | 5400 | Mirrors F-16 cannon. |

### Infantry templates (`mods/ww3mod/rules/ingame/infantry.yaml`)

These are `^X` templates — the per-faction units (`X.america`, `X.russia`) inherit them. **Empty refund** = the body+training value, anchored to a consistent baseline per tier.

| Unit | Cost | Pool | Ammo | SV | CV | Total | % Cost | Empty refund | Tier |
|---|---|---|---|---|---|---|---|---|---|
| ^E1 (conscript) | 50 | 5.56 rifle | 100 | 1 | 0 | 0 | 0% | 50 | Conscript |
| ^E3 (rifleman) | 100 | 5.56 DMR | 100 | 1 | 0 | 0 | — | — | Line |
| ^E3 (rifleman) | 100 | RPG | 1 | 50 | 50 | 50 | **50%** | 50 | (RPG load) |
| ^AR (autorifle) | 100 | 5.56 LMG | 500 | 1 | 0 | 0 | 0% | 100 | Line (no expensive ammo) |
| ^E2 (grenadier) | 100 | 40mm grenade | 30 | 2 | 2 | 60 | **60%** | 40 | Line |
| ^TL (team leader) | 200 | 7.62 DMR | 100 | 1 | 0 | 0 | — | — | Squad role |
| ^TL (team leader) | 200 | 40mm grenade | 6 | 8 | 8 | 48 | **24%** | 152 | (extra training premium) |
| ^MT (mortar) | 300 | 60mm mortar | 25 | 8 | 8 | 200 | **67%** | 100 | Line |
| ^SN (sniper) | 400 | 7.62 sniper | 50 | 4 | 4 | 200 | **50%** | 200 | Premium |
| ^AT (anti-tank) | 300 | ATGM | 3 | 65 | 65 | 195 | **65%** | 105 | Line |
| ^AA (anti-air) | 300 | MANPAD | 3 | 65 | 65 | 195 | **65%** | 105 | Line |
| ^E6 (engineer) | 250 | 9mm SMG | 100 | 1 | 0 | 0 | — | — | Line |
| ^E6 (engineer) | 250 | C4 / mine | 3 | 50 | 50 | 150 | **60%** | 100 | (engineer skills) |
| ^E4 (flamethrower) | 100 | flame fuel | 90 | 1 | 0 | 0 | 0% | 100 | Line |
| ^SF (special forces) | 600 | 5.56 suppressed | 100 | 1 | 0 | 0 | — | — | Elite |
| ^SF (special forces) | 600 | C4 | 3 | 33 | 33 | 99 | **17%** | 501 | (operator training is the bulk) |
| ^MEDI | 100 | (Heal weapon) | — | — | — | 0 | 0% | 100 | Line |
| ^TECN | 250 | — | — | — | — | 0 | 0% | 250 | Line+ |
| ^DR (drone op) | 150 | recon drone | 1 | 25 | 25 | 25 | **17%** | 125 | Line |
| ^PILOT (and rank variants) | 500/800/1200/2000/3000 | 9mm SMG | 100 | 1 | 0 | 0 | 0% | full Cost | Elite |

### Defenses (`mods/ww3mod/rules/ingame/structures-defenses.yaml`)

| Unit | Cost | Pool | Ammo | SV | CV | Total | % Cost | Empty refund | Note |
|---|---|---|---|---|---|---|---|---|---|
| CRAM | 1000 | 20mm CRAM | 24 | 5 | 5 | 120 | 12% | 880 | Per-volley deduction; pool reloads on cooldown. |
| AGUN | 800 | dual cannon | 24 | 5 | 5 | 120 | 15% | 680 | Mirrors CRAM. |
| FTUR (flame turret) | 1000 | flame fuel | 10 | 2 | 0 | 0 | 0% | 1000 | Bulk fuel. |
| SAM | 2000 | (no AmmoPool) | ∞ | — | — | — | — | 2000 | Infinite ammo SAM. |
| HSAM | 3000 | (no AmmoPool) | ∞ | — | — | — | — | 3000 | Infinite ammo cloaked SAM. |
| MSLO | 50000 | (no AmmoPool) | ∞ | — | — | — | — | 50000 | Superweapon silo. |

### Other (`mods/ww3mod/rules/ingame/vehicles.yaml`, `crew.yaml`)

| Unit | Cost | Pool | Ammo | SV | CV | Total | % Cost | Empty refund | Note |
|---|---|---|---|---|---|---|---|---|---|
| MNLY (mine layer) | 600 | mines | 10 | 25 | 25 | 250 | 42% | 350 | Unchanged — mines are the unit's whole payload. |
| TRUK (supply truck) | 1000 | (CargoSupply) MaxSupply 15 | — | CreditValuePerUnit 50 | (kept) | 750 | 75% | 250 | Unchanged — `CargoSupply` field, not `AmmoPool`. |
| ^CrewMember | 100 | 9mm pistol | 24 | 1 | 1 | 24 | 24% | 76 | Unchanged. Crew aren't usually evac/sold. |

### Buildings — `SupplyProvider` (rearm hosts, not `AmmoPool`)

| Building | Cost | TotalSupply | SupplyCreditValue | Drained refund | Note |
|---|---|---|---|---|---|
| logisticscenter | 3500 | 3000 | 3000 | 500 | Already capped (refund stays ≥ 500). |
| SUPPLYCACHE | 0 (capturable) | 500 | 400 | 0 | Captured drop; sell trait pays 50% RefundPercent. |

## What changed in the engine

Nothing. This is a pure YAML pass. The relevant cap already exists at `engine/OpenRA.Mods.Common/Traits/CustomSellValue.cs:56`:

```csharp
return System.Math.Max(0, baseValue - missingAmmoValue);
```

## When tuning further

- **Munition consistency**: a Hellfire is a Hellfire is a Hellfire. If you change the Apache Hellfire rate, change Stryker / A-10 / MI-28 / Littlebird to match. Same for ATGMs (Bradley + BMP-2 + AT specialist) and MANPADS (Stryker Stinger + Tunguska 9M311 + AA specialist).
- **Infantry empty-evac**: protect the per-tier baseline. If you raise a soldier's `Cost`, raise the ammo budget rather than the empty-evac refund — otherwise tier identity drifts.
- **Bulk-ammo cap**: any pool with `Ammo ≥ ~50` of cheap rounds (rifle / MG / autocannon / 20mm aircraft cannon / flame fuel) keeps `CreditValue: 0`. Going non-zero on those pools is what caused the original disproportion bug.
- **Per-pool CV ceiling**: a single pool's `Ammo × CreditValue` shouldn't exceed `Cost − minimum-empty-refund`. Otherwise an empty unit refunds 0 and players treat it as a write-off rather than a salvageable asset.
