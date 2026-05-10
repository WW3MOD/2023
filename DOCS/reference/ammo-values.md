# Ammo Value Reference

**Last revised:** 2026-05-10 (sweep to fix evac/sell deduction overshoot)

This is the per-unit reference for `SupplyValue` and `CreditValue` on every `AmmoPool` in the mod, plus the rationale behind each one. Read alongside `mods/ww3mod/rules/ingame/{vehicles,aircraft,infantry,structures-defenses}*.yaml`.

## What the two fields do

In `engine/OpenRA.Mods.Common/Traits/AmmoPool.cs`:

| Field | Used by | Effect |
|---|---|---|
| `SupplyValue` | `SupplyProvider`, `CargoSupply` (rearm) | How much **supply budget** a Logistics Center / supply truck spends to refill **one** missing round in this pool. |
| `CreditValue` | `CustomSellValue.GetSellValue` | How much **cash** is deducted from the unit's evac/sell refund per **missing** round in this pool. |

Sell/evac math: `refund = max(0, Cost − sum_over_pools(missing × CreditValue))`. The `max(0, …)` clamps the floor at zero — the bug the user reported was *not* an over-refund (engine math caps at unit cost) but a **vastly disproportionate ammo-value display**: e.g. a Bradley shows "Ammo: 900 × 15 supply = 13500" in the tooltip while costing 1500 to call in. The sweep below pulls every ammo pool back into a sensible band.

## Target ratios (Ammo × CreditValue) / Cost

| Class | Target | Reason |
|---|---|---|
| Bulk MG / autocannon / SMG / rifle ammo | **0%** (`CreditValue: 0`) | Bullets are pennies in real life and deeply lopsided in-game (high Ammo, low Cost). Set CV=0 so the round count never deducts from sell value. SupplyValue stays at 1 so the rearm tooltip still renders. |
| Tank main gun | ~5–10% | Per user's Abrams example (5%); modern MBT main-gun rounds are cheap relative to the platform regardless of generation. |
| Infantry pistol / SMG / sniper | 0–25% | Match-grade rounds get a small per-shot value; bulk auto-fire stays at 0. |
| Infantry RPG / ATGM / MANPADS | ~25–30% | The missile *is* the unit's reason for existing; significant deduction without zeroing the refund. |
| IFV ATGM secondary (TOW / Konkurs / Kornet) | ~12% | Real-world Bradley ATGM load is ~$700K against a ~$1.8M Bradley (~40%); we run lighter so a fired-out IFV still recovers most of its budget. |
| Mobile artillery (155mm / 152mm / 60mm mortar) | ~25% | Already in line pre-sweep; comments preserved. |
| MLRS one-shot (M270 / TOS / Grad) | ~45–48% | Already in line. The whole platform is its rocket pod — high deduction matches doctrine. |
| Long-range missile platform (HIMARS / Iskander) | 50% | Already in line. Same logic as MLRS. |
| Helicopter cannon | 0% | Bulk autocannon — see above. |
| Helicopter / aircraft missiles (Hellfire / AAM) | ~10–20% | The ordnance is the strike asset; cannon rounds round out the magazine. |
| Strike-aircraft rocket pods (Su-25, A-10) | ~10–15% | Mid-cost unguided rockets. |
| Mobile / static AA + SAM | ~12–25% | Static defenses take a cap-volley deduction; mobile AA splits between cheap autocannon (0%) and missile (~14%). |
| Drone / disposable | ~17% | Quadcopter-class — cheaper than a guided munition. |

## Per-unit table

Columns: **Cost** = `Valued.Cost`. **Pool** = AmmoPool name & ammo count. **SV** = `SupplyValue`. **CV** = `CreditValue`. **Total** = `sum(Ammo × CreditValue)` across all pools — i.e. the maximum amount that could be deducted from the evac/sell refund. **% Cost** = Total ÷ Cost.

### Vehicles — America (`mods/ww3mod/rules/ingame/vehicles-america.yaml`)

| Unit | Cost | Pool | Ammo | Old SV/CV | New SV | New CV | Total | % Cost | Note |
|---|---|---|---|---|---|---|---|---|---|
| humvee | 450 | 7.62 MG | 300 | 3 / 3 | 1 | 0 | 0 | 0% | Bulk MG; CV=0 fixes the 67%-of-cost overshoot. |
| m113 | 700 | 12.7 HMG | 500 | 5 / 5 | 1 | 0 | 0 | 0% | Bulk HMG; CV=0 fixes the 357%-of-cost overshoot. |
| bradley | 1500 | 25mm autocannon | 900 | 15 / 15 | 1 | 0 | 0 | — | Autocannon → CV=0. |
| bradley | 1500 | TOW | 8 | 100 / 100 | 22 | 22 | 176 | **12%** | Real Bradley TOW is ~$700K/8 = $87K/missile. |
| abrams | 2500 | 120mm | 40 | 80 / 80 | 6 | 6 | 240 | **9.6%** | Per user's "5% of tank value" guidance. |
| m109 (Paladin) | 1800 | 155mm | 39 | 12 / 12 | 12 | 12 | 468 | **26%** | Unchanged — already at the artillery target band. |
| m270 MLRS | 1800 | 227mm rocket | 12 | 70 / 70 | 70 | 70 | 840 | **47%** | Unchanged — one-shot magazine doctrine. |
| strykershorad | 2500 | 25mm autocannon | 400 | 15 / 15 | 1 | 0 | 0 | — | Autocannon → CV=0. |
| strykershorad | 2500 | Stinger | 8 | 60 / 60 | 25 | 25 | 200 | — | Stinger ~$120K real. |
| strykershorad | 2500 | Hellfire | 4 | 200 / 200 | 80 | 80 | 320 | **21% (combined)** | Combined missile pools. |
| HIMARS | 6000 | GMLRS missile | 2 | 1500 / 1500 | 1500 | 1500 | 3000 | **50%** | Unchanged — long-range missile platform. |

### Vehicles — Russia (`mods/ww3mod/rules/ingame/vehicles-russia.yaml`)

| Unit | Cost | Pool | Ammo | Old SV/CV | New SV | New CV | Total | % Cost | Note |
|---|---|---|---|---|---|---|---|---|---|
| btr | 600 | 14.5 HMG | 500 | 5 / 5 | 1 | 0 | 0 | 0% | Bulk HMG. |
| bmp2 | 1300 | 30mm autocannon | 900 | 15 / 15 | 1 | 0 | 0 | — | Autocannon → CV=0. Was 13500-of-1300 (1037%). |
| bmp2 | 1300 | WGM (Konkurs) | 8 | 100 / 100 | 20 | 20 | 160 | **12%** | Mirrors Bradley TOW. |
| t90 | 2400 | 125mm | 40 | 80 / 80 | 6 | 6 | 240 | **10%** | Mirrors Abrams. |
| giatsint (2S5) | 1800 | 152mm | 39 | 12 / 12 | 12 | 12 | 468 | **26%** | Unchanged — mirrors Paladin. |
| grad (BM-21) | 1500 | 122mm rocket | 40 | 17 / 17 | 17 | 17 | 680 | **45%** | Unchanged. |
| tos (TOS-1) | 2000 | 220mm thermobaric | 24 | 40 / 40 | 40 | 40 | 960 | **48%** | Unchanged. |
| tunguska | 1700 | 30mm AA autocannon | 180 | 15 / 15 | 1 | 0 | 0 | — | Autocannon → CV=0. |
| tunguska | 1700 | 9M311 SAM | 8 | 60 / 60 | 30 | 30 | 240 | **14%** | Mirrors Stryker SHORAD's per-Stinger rate. |
| iskander | 6000 | 9M723 missile | 2 | 1500 / 1500 | 1500 | 1500 | 3000 | **50%** | Unchanged — mirrors HIMARS. |

### Vehicles — Ukraine (`mods/ww3mod/rules/ingame/vehicles-ukraine.yaml`)

| Unit | Cost | Pool | Ammo | Old SV/CV | New SV | New CV | Total | % Cost | Note |
|---|---|---|---|---|---|---|---|---|---|
| t72 | 1700 | 125mm | 40 | 80 / 80 | 4 | 4 | 160 | **9.4%** | Mirrors T-90/Abrams; older-gen ammo a touch cheaper. |

### Aircraft — America (`mods/ww3mod/rules/ingame/aircraft-america.yaml`)

| Unit | Cost | Pool | Ammo | Old SV/CV | New SV | New CV | Total | % Cost | Note |
|---|---|---|---|---|---|---|---|---|---|
| TRAN (Chinook) | 2000 | — | — | — | — | — | — | — | Unarmed transport. |
| littlebird | 3000 | 7.62 minigun | 160 | 3 / 3 | 1 | 0 | 0 | — | Bulk minigun. |
| littlebird | 3000 | Hellfire | 2 | 200 / 200 | 200 | 200 | 400 | **13%** | Hellfires are the unit's reason for existing. |
| HELI (Apache) | 6000 | 30mm chain gun | 200 | 15 / 15 | 3 | 0 | 0 | — | Bulk chain gun. |
| HELI (Apache) | 6000 | Hellfire | 8 | 200 / 200 | 150 | 150 | 1200 | **20%** | Real Hellfire ~$150K. |
| A10 | 6000 | 30mm GAU-8 | 100 | 15 / 15 | 3 | 0 | 0 | — | Bulk GAU-8. |
| A10 | 6000 | Hellfire | 4 | 200 / 200 | 150 | 150 | 600 | **10%** | A-10's main weapon is the gun, missiles run lighter. |
| F16 | 6000 | AAM | 6 | 60 / 60 | 100 | 100 | 600 | — | Real AIM-9X ~$400K. |
| F16 | 6000 | 20mm M61 | 150 | 15 / 15 | 1 | 0 | 0 | **10% (combined)** | Cannon → CV=0. |
| TRAN.airdrop / A10.Airstrike etc. | inherits from above | | | | | | | | Disposable strike variants — no rearm pool, sell trait stripped. |

### Aircraft — Russia (`mods/ww3mod/rules/ingame/aircraft-russia.yaml`)

| Unit | Cost | Pool | Ammo | Old SV/CV | New SV | New CV | Total | % Cost | Note |
|---|---|---|---|---|---|---|---|---|---|
| HALO (Mi-26) | 2000 | — | — | — | — | — | — | — | Unarmed transport. |
| HIND (Mi-24) | 4000 | 12.7 chin gun | 150 | 5 / 5 | 1 | 0 | 0 | — | Bulk MG. |
| HIND (Mi-24) | 4000 | S-8 rocket pod | 80 | 25 / 25 | 8 | 8 | 640 | **16%** | Unguided 80mm rockets — cheap each, big payload. |
| MI28 | 6000 | 30mm chain gun | 200 | 15 / 15 | 3 | 0 | 0 | — | Mirrors Apache primary. |
| MI28 | 6000 | Hellfire-class | 8 | 200 / 200 | 150 | 150 | 1200 | **20%** | Mirrors Apache secondary. |
| FROG (Su-25) | 6000 | rocket pod | 60 | 25 / 25 | 15 | 15 | 900 | **15%** | Strike-aircraft rocket payload. |
| MIG (MiG-29) | 6000 | AAM | 6 | 60 / 60 | 100 | 100 | 600 | — | Mirrors F-16 AAM. |
| MIG (MiG-29) | 6000 | 20mm cannon | 150 | 15 / 15 | 1 | 0 | 0 | **10% (combined)** | Mirrors F-16 cannon. |

### Infantry templates (`mods/ww3mod/rules/ingame/infantry.yaml`)

These are `^X` templates — the per-faction units (`X.america`, `X.russia`) inherit them.

| Unit | Cost | Pool | Ammo | Old SV/CV | New SV | New CV | Total | % Cost | Note |
|---|---|---|---|---|---|---|---|---|---|
| ^E1 (conscript) | 50 | 5.56 rifle | 100 | 2 / 2 | 1 | 0 | 0 | 0% | Bulk rifle. |
| ^E3 (rifleman) | 100 | 5.56 DMR | 100 | 2 / 2 | 1 | 0 | 0 | — | |
| ^E3 (rifleman) | 100 | RPG | 1 | 25 / 25 | 25 | 25 | 25 | **25%** | One-shot disposable AT. |
| ^AR (autorifle) | 100 | 5.56 LMG | 500 | 3 / 3 | 1 | 0 | 0 | 0% | Bulk LMG. Was 1500% of cost. |
| ^E2 (grenadier) | 100 | 40mm grenade | 30 | 3 / 3 | 3 | 1 | 30 | **30%** | The grenadier's whole reason for existing. |
| ^TL (team leader) | 200 | 7.62 DMR | 100 | 3 / 3 | 1 | 0 | 0 | — | |
| ^TL (team leader) | 200 | 40mm grenade | 6 | 10 / 10 | 8 | 8 | 48 | **24%** | Boutique pool, fewer rounds. |
| ^MT (mortar) | 300 | 60mm mortar | 25 | 5 / 5 | 5 | 3 | 75 | **25%** | Indirect-fire infantry band. |
| ^SN (sniper) | 400 | 7.62 sniper | 50 | 2 / 2 | 4 | 2 | 100 | **25%** | Match-grade rounds. |
| ^AT (anti-tank) | 300 | ATGM | 3 | 100 / 100 | 30 | 30 | 90 | **30%** | Javelin-class top-attack. |
| ^AA (anti-air) | 300 | MANPAD | 3 | 60 / 60 | 30 | 30 | 90 | **30%** | Stinger-class. |
| ^E6 (engineer) | 250 | 9mm SMG | 100 | 2 / 2 | 1 | 0 | 0 | — | |
| ^E6 (engineer) | 250 | C4 / mine | 3 | 25 / 25 | 20 | 20 | 60 | **24%** | Demolition charge / AT mine. |
| ^E4 (flamethrower) | 100 | flame fuel | 90 | 2 / 2 | 1 | 0 | 0 | 0% | Bulk fuel. |
| ^SF (special forces) | 600 | 5.56 suppressed | 100 | 2 / 2 | 1 | 0 | 0 | — | |
| ^SF (special forces) | 600 | C4 | 3 | 25 / 25 | 30 | 30 | 90 | **15%** | Operator's training is the unit's value, not the explosives. |
| ^MEDI | 100 | (Heal weapon) | — | — | — | — | — | — | No magazine. |
| ^TECN | 250 | — | — | — | — | — | — | — | Unarmed. |
| ^DR (drone op) | 150 | recon drone | 1 | 50 / 50 | 25 | 25 | 25 | **17%** | Disposable quadcopter. |
| ^PILOT (and rank variants) | 500 | 9mm SMG | 100 | 2 / 2 | 1 | 0 | 0 | 0% | Pilots aren't usually sold; bulk SMG. |

### Defenses (`mods/ww3mod/rules/ingame/structures-defenses.yaml`)

| Unit | Cost | Pool | Ammo | Old SV/CV | New SV | New CV | Total | % Cost | Note |
|---|---|---|---|---|---|---|---|---|---|
| CRAM | 1000 | 20mm CRAM | 24 | 15 / 15 | 5 | 5 | 120 | **12%** | Per-volley deduction; pool reloads on cooldown. |
| AGUN | 800 | dual cannon | 24 | 15 / 15 | 5 | 5 | 120 | **15%** | Mirrors CRAM. |
| FTUR (flame turret) | 1000 | flame fuel | 10 | 2 / 2 | 2 | 0 | 0 | 0% | Bulk fuel. The structure's value isn't its tank. |
| SAM | 2000 | (no AmmoPool) | ∞ | — | — | — | — | — | Infinite ammo SAM. |
| HSAM | 3000 | (no AmmoPool) | ∞ | — | — | — | — | — | Infinite ammo cloaked SAM. |
| MSLO | 50000 | (no AmmoPool) | ∞ | — | — | — | — | — | Superweapon silo, no per-shot pool. |
| GUN | 1000 | (uses parent's pool) | — | — | — | — | — | — | |

### Other (`mods/ww3mod/rules/ingame/vehicles.yaml`, `crew.yaml`)

| Unit | Cost | Pool | Ammo | Old SV/CV | New SV | New CV | Total | % Cost | Note |
|---|---|---|---|---|---|---|---|---|---|
| MNLY (mine layer) | 600 | mines | 10 | 25 / 25 | 25 | 25 | 250 | **42%** | Unchanged — mines are the unit's whole payload. Sits at the same band as MLRS. |
| TRUK (supply truck) | 1000 | (CargoSupply) MaxSupply 15 | — | CreditValuePerUnit 50 | (kept) | (kept) | 750 | **75%** | Unchanged — `CargoSupply` field, not `AmmoPool`. Already capped: drained truck refunds 1000 − 750 = 250. |
| ^CrewMember | 100 | 9mm pistol | 24 | 1 / 1 | 1 | 1 | 24 | **24%** | Unchanged — already in line. Crew aren't usually evac/sold. |

### Buildings — `SupplyProvider` (rearm hosts, not `AmmoPool`)

These use `SupplyCreditValue` + `TotalSupply` (not `CreditValue` per round). `SupplyCreditValue` is the *full* credit value; the engine prorates it against the missing supply fraction.

| Building | Cost | TotalSupply | SupplyCreditValue | Drained refund | % | Note |
|---|---|---|---|---|---|---|
| logisticscenter | 3500 | 3000 | 3000 | 500 | 86% drain | Already capped (refund stays ≥ 500). |
| SUPPLYCACHE | 0 (capturable) | 500 | 400 | 0 (no Cost) | — | Captured drop; sell trait pays 50% RefundPercent. |

## What changed in the engine

Nothing — this sweep is a pure YAML pass. The relevant engine code already caps refunds at unit cost (`engine/OpenRA.Mods.Common/Traits/CustomSellValue.cs:56`):

```csharp
return System.Math.Max(0, baseValue - missingAmmoValue);
```

The previous values were technically safe (refund could never exceed `Cost`), but the *displayed* tooltip values like "Ammo: 900 × 15 supply = 13500" were wildly disproportionate to the unit price. Now every pool's `Ammo × CreditValue` sits inside a sensible band relative to the unit it belongs to.

## Tooltip behaviour

`AmmoPool.cs` only renders the rearm-cost tooltip when `Ammo > 0 && SupplyValue > 0` (line 72). All bulk-MG pools were set to `SupplyValue: 1` (not 0) so the tooltip still appears with a token cost — useful for player feedback. `CreditValue: 0` only zeroes the *evac/sell* deduction; it has no other effect.

## When tuning further

- Move both fields together unless you have a specific reason to split them. They represent the same real-world ammo cost from two angles (rearm vs evac).
- Don't push any single pool's `Ammo × CreditValue` past **~50%** of the unit's `Valued.Cost` — that range is reserved for missile-only platforms (MLRS, HIMARS, Iskander) where the magazine *is* the unit's value.
- For pools with `Ammo ≥ ~100`, prefer `CreditValue: 0` and tune `SupplyValue` for the rearm cost. Multiplying any non-trivial CV by 100+ rounds quickly dwarfs the unit's price.
- For pools with `Ammo ≤ ~10` (missiles, rockets, demo charges), `SupplyValue == CreditValue` is fine.
