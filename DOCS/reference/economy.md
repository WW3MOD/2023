# WW3MOD Economy & Supply System

This doc is the source of truth for how money, ammo, and supply move through a match. It's written from a gameplay perspective with technical detail where it matters.

If anything here disagrees with code, the doc is right and the code needs to change — file a fix, don't quietly drift.

## Core principles

1. **Every unit, every magazine, every supply box has a cost.** Cash spent buys ammo + body together. Selling or evacuating refunds what's left.
2. **A unit of supply is worth a fixed amount of cash** wherever it sits — in an LC, a truck, a cache. When the player gets it back on evac, capture, or absorb, it returns at face value.
3. **Supply is finite.** A Logistics Center spawns with a fixed pool. Trucks carry a fixed amount. Drained pools stay drained until the player builds more LCs or calls in more trucks.
4. **Trucks, LCs, and dropped supply caches share one trait** (`SupplyProvider`). Players see the same UI (range circle, supply bar) everywhere.
5. **`ReloadCount` is the canonical batch size for rearm.** Whether an Apache is topping up at an HPAD, a Bradley docks at the LC, or a soldier waits next to a truck, the per-pool `ReloadCount` decides how many rounds arrive per cycle and `SupplyValue` decides what one cycle costs.

## What rearms what

| Unit class | Rearms at |
|---|---|
| Infantry | TRUK (supply truck), SUPPLYCACHE (dropped box), Logistics Center |
| Ground vehicles | Logistics Center only |
| Aircraft | HPAD (helicopter pad), AFLD (airfield) |
| Static defenses (CRAM, AGUN) | Self-reload via `ReloadAmmoPool` (no external supply consumed) |

Vehicles are budgeted around dock-at-LC logistics. Adding `truk` to `Rearmable.RearmActors` on a vehicle is a balance change.

## Ammo pools, batches, and per-round cost

### Properties

```yaml
AmmoPool@1:
    Ammo: 900           # Maximum rounds in the pool.
    ReloadCount: 100    # Batch size — rounds delivered per rearm cycle.
                        # Default 1 (per-round semantics).
    ReloadDelay: 50     # Ticks between rearm batches when self-reloading.
    SupplyValue: 5      # Cost per BATCH (not per round). Used for both:
                        #   - rearm: supply spent per batch delivered
                        #   - evac/sell: cash deducted per missing batch
```

Pool budget = `(Ammo / ReloadCount) × SupplyValue`. For Bradley 25mm above: `(900 / 100) × 5 = 45`.

### Why batches

Batching keeps integer math honest while letting us express low per-round cost. `ReloadCount: 100, SupplyValue: 5` is ~0.05 effective per round — affordable for a 900-round bulk autocannon on a 1500-cost IFV, with whole-number bookkeeping.

### One property, two uses

`SupplyValue` is the single cost-per-batch property. It's charged when a supply provider hands over a batch (rearm) and deducted when a unit evacuates with that batch missing (evac/sell).

### Tooltip format

The pool tooltip renders the batch math directly:

```
Ammo: 900 (9 batches × 100 rounds × 5 supply = 45)
```

Players see what one cycle costs and how many cycles fill the pool, not an opaque per-round number.

## The supply chain

### Logistics Center (LC)

Cost ~3500. Spawns with `SupplyProvider.TotalSupply: 3000`. The pool drains as:
- Vehicles dock and rearm directly (`SupplyValue × batches given`).
- Trucks drive in to restock (truck pulls supply from LC; LC drops by exactly the amount taken).

When the LC's pool hits zero it stops servicing rearm requests. The player builds another LC, or relies on trucks that still have supply.

### Supply Truck (TRUK)

Cost 1000. Spawns with `SupplyProvider.TotalSupply: 750`.

Truck behavior:
- Drives near friendly **infantry** that need rearm. Delivers `ReloadCount` rounds per cycle, charges `SupplyValue` per batch from its own pool.
- Serves units whose `Rearmable.RearmActors` lists `truk` (infantry).
- When low (`currentSupply < RestockThreshold`), drives back to nearest LC and refills.
- Refill drains the LC's `currentSupply` by the amount taken. A truck that needs 600 supply takes 600 from the LC, leaving the LC with 2400. If the LC has less than the truck wants, the truck takes what's there and leaves partially full.
- Can drop its remaining supply as a SUPPLYCACHE box (deploy command) — see below.

### SUPPLYCACHE (dropped supply box)

Spawned when a truck unloads its supply on the ground. Functionally a stationary truck — same `SupplyProvider` trait, same UI:

- **Range circle** showing rearm reach (4 cells).
- **Selection bar** showing remaining supply.
- Sprite tier (Full/Mid/Low) reflects the supply remaining.
- Capturable by enemies (`ProximityCapturable`) — if the enemy reaches it first, the supply changes hands at full value.
- Sits in place until drained, captured, or destroyed. The player recovers a cache's remaining supply by absorbing it into a friendly LC (the LC's `AbsorbsSupplyCache` trait pulls in any nearby cache) or by spending it through infantry rearming off it.

### Cash flow recap

| Action | Cash effect |
|---|---|
| Call in unit (any) | `−Cost` (cash drops by full unit cost; ammo is bundled in) |
| Unit destroyed in combat | Permanent loss of `Cost` |
| Unit rotated to map edge with full ammo | `+Cost` returned |
| Unit rotated to map edge with empty ammo | `+(Cost − sum_pools(missing_batches × SupplyValue))` |
| Sell building with supply (LC) | `+max(0, Cost − missing_supply_value)` — supply refunds at constant rate, body refunds in full |
| Truck drops cache, drains in field | Spent supply is gone; remaining supply still recoverable via absorb/capture |
| Capture an enemy SUPPLYCACHE | Free supply at full value (war booty) |
| LC absorbs nearby friendly SUPPLYCACHE | Supply transfers from cache to LC at full value |

Sell formula (engine, single path through `CustomSellValue.GetSellValue`):
```
refund = max(0, Cost
              − sum_pools(floor(missing_rounds / ReloadCount) × SupplyValue)
              − missing_supply_value)        // for actors with SupplyProvider/CargoSupply
```

## Per-platform ammo budget targets

These are guideline ratios (`pool budget / unit Cost`). Specific values live in `DOCS/reference/ammo-values.md`.

| Class | Total pool budget | Reason |
|---|---|---|
| Bulk MG / autocannon / SMG / rifle (high Ammo, cheap rounds) | ~3–10% | Bullets cost something — even cheap rounds drain truck supply, so a unit can't sustain indefinitely. Batch-cost lets us keep individual rounds nearly free while the pool total still bites. |
| Tank main gun (40 shells) | ~10% | Ammo is cheap relative to a tank. Empty tank evac refunds ~90% of cost. |
| Infantry RPG / ATGM / MANPADS (1–3 missiles) | ~30–65% | Missile-tier ammo — significant deduction, but the soldier's body still has value. |
| IFV ATGM (Bradley TOW, BMP-2 WGM) | ~40% | Real-world ratio. The missile load is the IFV's main combat value above the autocannon. |
| Helicopter / aircraft Hellfire | ~13–27% | Universal Hellfire rate per missile regardless of platform. |
| Mobile artillery (155mm / 152mm) | ~25% | Shell pool sized to artillery doctrine. |
| MLRS one-shot magazine | ~45–50% | The rocket pod *is* the platform's value. |
| Long-range missile platform (HIMARS, Iskander) | ~50% | Two missiles per launcher; the launcher is mostly the missiles. |

### Munition consistency rule

The same munition costs the same supply across every platform:
- **Hellfire**: per-missile SupplyValue 200 (Apache, MI-28, A-10, Stryker SHORAD, Littlebird).
- **ATGM** (TOW / Konkurs): per-missile 65–75 (Bradley 75, BMP-2 65, AT specialist 65).
- **MANPAD / short-range SAM**: per-missile 65 (Stryker Stinger, Tunguska 9M311, AA specialist).
- **Air-to-air missile**: per-missile 100 (F-16, MIG).

If a platform's missile rate changes, change every other platform that fires the same munition.

### Infantry empty-evac base

Most line-infantry classes train similarly. The cost above body+training baseline is the ammunition load. So when a soldier evacuates with all ammo expended, they refund roughly the same baseline:

| Tier | Empty evac refund | Examples |
|---|---|---|
| Conscript | ~50 | E1 |
| Line infantry | ~100 | E3 (rifleman+RPG), AR (LMG), E2 (grenadier), MT (mortar), AT (ATGM), AA (MANPAD), E4 (flame), E6 (engineer), MEDI, DR (drone) |
| Squad role w/ extra training | ~150 | TL (team leader) |
| Premium specialist | ~200 | SN (sniper) |
| Elite | ~500 | SF (special forces), PILOT (and ranks) |

Per pool: `SupplyValue = (Cost − base) / batches`, where `batches = Ammo / ReloadCount`.

## Engine architecture

### Single trait: `SupplyProvider`

Trucks, LCs, and SUPPLYCACHEs all use `SupplyProvider`. They differ only in YAML config:

| Source | TotalSupply | RestockActors | Notes |
|---|---|---|---|
| `logisticscenter` | 3000 | (none) | Mounts at base; drains until empty. `AbsorbsSupplyCache` recovers dropped boxes. |
| `truk` | 750 | `[logisticscenter]` | Mobile; drives to LC when low; can drop a SUPPLYCACHE. |
| `supplycache` | 500 | (none) | Stationary; drained to zero, then despawns or is captured. |

### Rearm cost math

In the `SupplyProvider` rearm path (LC, truck, or cache):
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
    if (pool.Info.SupplyValue > 0)
    {
        var missingBatches = (pool.Info.Ammo - pool.CurrentAmmoCount) / pool.Info.ReloadCount;
        missingAmmoValue += missingBatches * pool.Info.SupplyValue;
    }
}
```

### LC restock drain

In `SupplyProvider.TryRestock` (called on the truck), when the truck arrives at the LC:
```csharp
var taken = Math.Min(Info.TotalSupply - currentSupply, lcSupplyProvider.CurrentSupply);
lcSupplyProvider.RemoveSupply(taken);
currentSupply += taken;
```

The LC pool drops by exactly what the truck took. Truck might leave partially full if the LC didn't have enough.

## When tuning further

- **Munition consistency**: a Hellfire is a Hellfire. Changing Apache's per-batch SupplyValue means changing every Hellfire-firing platform to match.
- **Per-tier infantry baseline**: when raising a soldier's `Cost`, the extra goes into the ammo budget so the empty-evac refund stays at the tier baseline.
- **Bulk-ammo cap**: a pool's full budget fits inside one truck-load (~750). Above that, combat economics break down.
- **Pool budget ceiling**: a pool's `(Ammo / ReloadCount) × SupplyValue` is at most `Cost − minimum-empty-refund`, so an empty unit always retains some salvage value.
