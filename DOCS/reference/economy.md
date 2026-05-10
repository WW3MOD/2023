# WW3MOD Economy & Supply System

**Last revised:** 2026-05-10 (initial — formalizes the supply chain rules; some described behavior is the **target state**, not yet fully implemented in code. Items marked "Target" are pending engine changes.)

This doc is the source of truth for how money, ammo, and supply move through a match. It's written from a gameplay perspective with technical detail where it matters.

If anything here disagrees with code, the doc is right and the code needs to change — file a fix, don't quietly drift.

## Core principles

1. **Nothing is free.** Every unit, every magazine, every supply box has a cost. Cash spent buys ammo + body together. Selling/evacuating only refunds what's left.
2. **Supply is finite, not regenerating.** A Logistics Center spawns with a fixed pool. Trucks carry a fixed amount. Drained = drained. Players keep the supply chain alive by deploying more LCs and trucks (which themselves cost cash).
3. **Same trait for every supply source.** Trucks, the Logistics Center, and dropped supply caches all use one trait (`SupplyProvider`). Players see the same UI (range circle, supply bar) everywhere.
4. **One field controls rearm pacing.** Per-pool `ReloadCount` is the canonical batch size for self-reload, dock-rearm, and supply-economy rearm. The same number used by an Apache reloading at an HPAD also controls how trucks and the LC dispense rounds.

## What rearms what

| Unit class | Rearms at |
|---|---|
| Infantry | TRUK (supply truck), SUPPLYCACHE (dropped box), Logistics Center |
| Ground vehicles | Logistics Center only |
| Aircraft | HPAD (helicopter pad), AFLD (airfield) |
| Static defenses (CRAM, AGUN) | Self-reload via `ReloadAmmoPool` (no external supply consumed) |

**Trucks DO NOT rearm vehicles.** This is deliberate — vehicles are budgeted around dock-at-LC logistics. Adding `truk` to `Rearmable.RearmActors` on a vehicle is a balance change, not a default.

## Ammo pools, batches, and per-round cost

### Fields

```yaml
AmmoPool@1:
    Ammo: 900           # Maximum rounds in the pool.
    ReloadCount: 100    # Batch size — rounds delivered per rearm tick.
                        # Default 1 (per-round semantics, backward compat).
    ReloadDelay: 50     # Ticks between rearm batches when self-reloading.
    SupplyValue: 5      # Supply cost per BATCH (not per round).
    CreditValue: 5      # Cash deducted per missing BATCH on evac/sell.
                        # MUST equal SupplyValue.
```

Pool budget = `(Ammo / ReloadCount) × SupplyValue`. For Bradley 25mm above: `(900 / 100) × 5 = 45`.

### Why batches

Per-round costs hit an integer floor at 1. A unit with 900 rounds couldn't have ammo cheaper than `1 supply × 900 = 900` per pool — way too expensive for bulk MG/autocannon ammo on a 1500-cost IFV. Batches let us express fractional per-round cost cleanly: `ReloadCount: 100, SupplyValue: 5` ≈ 0.05 effective per round.

### SV/CV must stay synced

`SupplyValue` (rearm cost) and `CreditValue` (evac/sell deduction) **must be equal**. They represent the same real-world ammo cost from two angles. Drifting them apart creates exploits (cheap to refill but valuable to sell empty, or vice versa).

> **Editor's note:** earlier sweeps had SV ≠ CV (e.g., bulk pools at SV=1, CV=0). That model is **deprecated**. Bring all pools to SV = CV at the next pass.

### Tooltip format (Target)

Render: `Ammo: 900 (9 batches × 100 rounds × 5 supply = 45)`. Players see the batch math, not just an opaque per-round number.

## The supply chain

### Logistics Center (LC)

Cost ~3500. Spawns with `SupplyProvider.TotalSupply: 3000`. This pool **does not regenerate**. Drains as:
- Vehicles dock and rearm directly (`SV × batches given`).
- Trucks drive in to restock (truck pulls supply from LC; LC drops by the amount taken). **Target — not yet implemented**, see Bugs below.

When the LC's pool hits zero, it stops servicing rearm requests. Player must build another LC, or rely on trucks that haven't drained yet.

### Supply Truck (TRUK)

Cost 1000. Spawns with `SupplyProvider.TotalSupply: 750` (after the planned refactor — see "CargoSupply removal" below).

Truck behavior:
- Drives near friendly **infantry** that need rearm. Delivers `ReloadCount` rounds per cycle, charges `SupplyValue` per batch from its own pool.
- Cannot deliver to vehicles (per-vehicle `Rearmable.RearmActors` excludes `truk`).
- When low (`currentSupply < RestockThreshold`), drives back to nearest LC and refills.
- Refill cost: drains LC's `currentSupply` by the amount taken (so a truck that needs 600 supply takes 600 from the LC, leaving the LC with 2400).
- Can drop its remaining supply as a SUPPLYCACHE box (deploy command) — see below.

### SUPPLYCACHE (dropped supply box)

Spawned when a truck unloads its supply on the ground. Functionally a stationary truck — same `SupplyProvider` trait, same UI:

- **Range circle** showing rearm reach (4 cells).
- **Selection bar** showing remaining supply.
- Sprite tier (Full/Mid/Low) reflects the supply remaining.
- Capturable by enemies (`ProximityCapturable`).
- Sellable for 50% of remaining supply value.

The cache supports the same delivery loop as a parked truck: nearby infantry get rearmed, drains over time, eventually empty and useless.

### Cash flow recap

| Action | Cash effect |
|---|---|
| Call in unit (any) | `−Cost` (cash drops by full unit cost; ammo is bundled in) |
| Unit destroyed in combat | Permanent loss of `Cost` |
| Unit rotated to map edge with full ammo | `+Cost` returned |
| Unit rotated to map edge with empty ammo | `+(Cost − sum_pools(missing_batches × CreditValue))` |
| Sell building (LC, defense) | `+RefundPercent% × Cost − missing_supply_credit` |
| Truck drops cache and dies before pickup | `0` — supply lost |
| Capture an enemy SUPPLYCACHE | Free supply (war booty) |

Sell formula (engine: `CustomSellValue.GetSellValue`):
```
refund = max(0, baseValue
              - sum_pools(floor(missing_rounds / ReloadCount) × CreditValue)
              - missing_supply_value)        // for buildings/trucks/caches
```

## Per-platform ammo budget targets

These are guideline ratios (`pool budget / unit Cost`). Specific values live in `DOCS/reference/ammo-values.md`.

| Class | Total pool budget | Reason |
|---|---|---|
| Bulk MG / autocannon / SMG / rifle (high Ammo, cheap rounds) | 0–5% | High Ammo + small ReloadCount × small SV; fits in one truck pass. |
| Tank main gun (40 shells) | ~10% | Per "5% of tank value" guidance; cheap relative to platform. |
| Infantry RPG / ATGM / MANPADS (1–3 missiles) | ~30–65% | Missile-tier ammo — significant deduction without zeroing the soldier's body. |
| IFV ATGM (Bradley TOW, BMP-2 WGM) | ~40% | Real-world ratio. |
| Helicopter / aircraft Hellfire | ~13–27% | Universal Hellfire rate (CV=200) regardless of platform. |
| Mobile artillery (155mm/152mm) | ~25% | Shell pool sized to artillery doctrine. |
| MLRS one-shot magazine | ~45–50% | Rocket pod *is* the platform's value. |
| Long-range missile platform (HIMARS, Iskander) | ~50% | Two missiles per launcher; the missiles are expensive and the launcher is mostly the missiles. |

### Munition consistency rule

The same munition costs the same supply across every platform:
- **Hellfire**: SV/CV per missile = 200 (Apache, MI-28, A-10, Stryker SHORAD, Littlebird).
- **ATGM** (TOW / Konkurs): per missile = 65–75 (Bradley, BMP-2, AT specialist).
- **MANPAD / short-range SAM**: per missile = 65 (Stryker Stinger, Tunguska 9M311, AA specialist).
- **Air-to-air missile**: per missile = 100 (F-16, MIG).

If a platform's missile rate changes, change every other platform that fires the same munition.

### Infantry empty-evac base

Most line-infantry classes train similarly. The cost above body+training baseline is the ammunition load. So when a soldier evacuates with all ammo expended, they should refund roughly the same baseline:

| Tier | Empty evac refund | Examples |
|---|---|---|
| Conscript | ~50 | E1 |
| Line infantry | ~100 | E3 (rifleman+RPG), AR (LMG), E2 (grenadier), MT (mortar), AT (ATGM), AA (MANPAD), E4 (flame), E6 (engineer), MEDI, DR (drone) |
| Squad role w/ extra training | ~150 | TL (team leader) |
| Premium specialist | ~200 | SN (sniper) |
| Elite | ~500 | SF (special forces), PILOT (and ranks) |

`CreditValue` (and `SupplyValue` synced) per pool = `(Cost − base) / (Ammo / ReloadCount)`.

## Engine architecture (Target state)

### Single trait: `SupplyProvider`

Trucks, LCs, and SUPPLYCACHEs all use `SupplyProvider`. Differs only in YAML config (`TotalSupply`, `RestockActors`, sprite tiers).

| Source | TotalSupply | RestockActors | Notes |
|---|---|---|---|
| `logisticscenter` | 3000 | (none — doesn't restock anywhere) | Mounts at base; drains until empty. |
| `truk` | 750 | `[logisticscenter]` | Mobile; drives to LC when low; can drop a SUPPLYCACHE. |
| `supplycache` | 500 | (none) | Stationary; drained to zero, then despawn or capture. |

### Removed: `CargoSupply` trait class

The old `MaxSupply × SupplyPerUnit` model and the dropped "any-cargo-vehicle-can-hold-supply" feature are gone. `CargoSupply.cs` is deleted; trucks use `SupplyProvider` directly. (~700 LOC removed.)

### Rearm cost math (target)

In `SupplyProvider` rearm path:
```csharp
var roundsPerBatch = bestPool.Info.ReloadCount;       // canonical batch size
var batchesAvailable = currentSupply / bestPool.Info.SupplyValue;
var batchesNeeded = (missing + roundsPerBatch - 1) / roundsPerBatch;
var batchesToGive = Math.Min(batchesNeeded, batchesAvailable);
var roundsToGive = batchesToGive * roundsPerBatch;

bestPool.GiveAmmo(target, roundsToGive);
currentSupply -= batchesToGive * bestPool.Info.SupplyValue;
```

In `CustomSellValue`:
```csharp
foreach (var pool in a.TraitsImplementing<AmmoPool>())
{
    if (pool.Info.CreditValue > 0)
    {
        var missingBatches = (pool.Info.Ammo - pool.CurrentAmmoCount) / pool.Info.ReloadCount;
        missingAmmoValue += missingBatches * pool.Info.CreditValue;
    }
}
```

### LC restock drain (target)

In `SupplyProvider.TryRestock` (called on the truck), when the truck arrives at the LC:
```csharp
var taken = Math.Min(Info.TotalSupply - currentSupply, lcSupplyProvider.CurrentSupply);
lcSupplyProvider.RemoveSupply(taken);
currentSupply += taken;
```
No more "currentSupply = TotalSupply" without deduction. The LC pool drops, the truck takes only what's available, the truck might leave partially full.

## Known bugs / target-state items (not yet implemented)

1. **Free LC restock for trucks.** `SupplyProvider.cs:507` sets the truck's pool to `TotalSupply` without deducting from the LC. Fix per the math above.
2. **`CargoSupply` trait still in use on TRUKs.** Refactor target: remove the trait, port TRUK YAML to `SupplyProvider`. Audit `Cargo.cs` for the supply-weight reservation hook (lines 388, 394) and remove if unused after the port.
3. **Tooltip per-round, not per-batch.** Update `AmmoPool.ProvideTooltipDescription` to render the batch math.
4. **SV / CV drift.** Some pools have `CV: 0` (rifle, MG, autocannon, etc.). Bring every pool to `SV == CV` on the YAML pass after the engine batch refactor.
5. **SUPPLYCACHE UI verification.** `RenderRangeCircle@Supply` is in the YAML, `SupplyProvider` implements `ISelectionBar`. Both *should* render. Verify in-game on next launch; if missing, find the gap (selection-only display? wrong child actor? trait disabled?).

## When tuning further

- **Don't drift SV from CV.** They're synced — change both together.
- **Munition consistency**: a Hellfire is a Hellfire. If you change Apache's per-batch CV, change every Hellfire-firing platform.
- **Per-tier infantry baseline**: protect the empty-evac refund. If you raise a soldier's `Cost`, raise the ammo budget rather than the empty-evac refund — otherwise tier identity drifts.
- **Bulk-ammo cap rule of thumb**: a single pool's full budget shouldn't exceed roughly one truck-load (~750). If it does, the pool's combat economics are wrong.
- **Pool budget ceiling**: any single pool's `(Ammo / ReloadCount) × CreditValue` must not exceed `Cost − minimum-empty-refund`. Otherwise an empty unit refunds 0 and players treat it as a write-off rather than salvage.
