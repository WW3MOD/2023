# WW3MOD Economy & Supply System

This doc is the source of truth for how money, ammo, and supply move through a match. It's written from a gameplay perspective with technical detail where it matters.

If anything here disagrees with code, the doc is right and the code needs to change — file a fix, don't quietly drift.

> **Related:** [`supply-route.md`](supply-route.md) covers the Supply Route (the sector beachhead — fixed at spawn, not a factory). This doc is about the cash/ammo/supply pipeline; that doc is about the building those flow through.

## Core principles

1. **Every unit, every magazine, every supply box has a cost.** Cash spent buys ammo + body together. Selling or evacuating refunds what's left.
2. **A unit of supply is worth a fixed amount of cash** wherever it sits — in an LC, a truck, a cache. When the player gets it back on evac, capture, or absorb, it returns at face value.
3. **Supply is finite.** A Logistics Center spawns with a fixed pool. Trucks carry a fixed amount. Drained pools stay drained until the player calls in more trucks — LCs are **not** buildable (`Buildable.Prerequisites: ~disabled`, `structures.yaml:367`); the only ones in a match are the Neutral pre-placed ones you can capture, on the three maps that have any.
4. **Trucks, LCs, and dropped supply caches share one trait** (`SupplyProvider`). Players see the same UI (range circle, supply bar) everywhere.
5. **`ReloadCount` is the canonical batch size for rearm.** Whether a Bradley docks at the LC or a soldier waits next to a truck, the per-pool `ReloadCount` decides how many rounds arrive per cycle and `SupplyValue` decides what one cycle costs. (Aircraft have no reachable host at all — see "What rearms what".)

## What rearms what

| Unit class | Rearms at |
|---|---|
| Infantry | TRUK (supply truck), SUPPLYCACHE (dropped box), Logistics Center |
| Ground vehicles | Logistics Center only — **except `m270`, `grad` and `tos`, which rearm nowhere.** Those three carry no `Rearmable` trait at all, so there is no host to pull from, and the LC push cannot reach them either (it is gated on the recipient declaring `replenish-vehicles`, which only `himars` and `iskander` do). Once spent they are finished as combatants; `ResupplyBehavior: Evacuate` is the whole plan for them — see "`ResupplyBehavior: Evacuate` is opt-in per actor" below. |
| Aircraft | HPAD (helicopter pad), AFLD (airfield) — **specified, not yet true in play: neither host can exist. See "Aircraft ammunition…" below.** |
| Static defenses (CRAM, AGUN) | Self-reload via `ReloadAmmoPool` (no external supply consumed) |

Vehicles are budgeted around dock-at-LC logistics. Adding `truk` to `Rearmable.RearmActors` on a vehicle is a balance change.

**Aircraft ammunition and health are one-way today — an UNMET SPEC, not the intent.** The table row above says what aircraft resupply is supposed to do; this paragraph says what the code currently does, and per this file's header the row is the authority and the gap is the code's to close. **How it should close is an open design decision** (re-enable a host, place one, or wire aircraft to `logisticscenter`) and is deliberately not settled here. Every airframe declares `Rearmable.RearmActors: hpad`/`afld` and `Repairable.RepairActors: hpad`/`afld` (`aircraft.yaml:79-80`, `:162-163`; `aircraft-america.yaml:219,376,498`; `aircraft-russia.yaml:224,392,530,625`), but **both hosts carry `Buildable.Prerequisites: ~disabled`** (`structures.yaml:432`, `:500`) and **nothing in the repo provides `disabled`**, so neither can be built; and neither is pre-placed on any of the ten shipped maps. `logisticscenter` — which does have `RepairsUnits` (`structures.yaml:377`) and `SupplyProvider` (`:387`), is pre-placed as a Neutral capturable on `polar-disorder`, `river-zeta` and `woodland-warfare`, and is already named by every infantry and ground-vehicle `RearmActors`/`RepairActors` list — is **not** named by any aircraft. So no aircraft can rearm or repair anywhere today. There is one latent exception that no code drives: `ReloadAmmoPool@1/@2` on the airframes is gated `unit.docked && !airborne` (e.g. `aircraft-america.yaml:175`, `:205`), and a *captured* LC grants `unit.docked` within 2c0 (`structures.yaml:400-404`), so an aircraft landed beside a captured LC would trickle-refill. Nothing — human UI or bot — ever sends one there.

Consequences worth knowing before touching aircraft logic: `ReturnToBase` resolves no resupplier and degrades to `FlyIdle`-then-finish (`ReturnToBase.cs:127-128`), and any gate of the form "wait until healthy / wait until full" is **unsatisfiable, not merely pessimistic**. The bot readiness gates therefore ask whether a host actually exists rather than whether one is named (`AirframeReadiness`), and `Aircraft` refuses a `ReturnToBase` order when none does, so a no-op return cannot cancel a live attack.

**A truck can never rearm a vehicle anywhere — the split is structural, not a tuning choice.** The provider PUSH is gated on the recipient declaring the provider's `RearmCondition` as an `ExternalCondition`; TRUK (`vehicles.yaml:546`) and SUPPLYCACHE (`misc.yaml:412`) both name `replenish-soldiers`, which is declared **only** on `^Soldier` (`infantry.yaml:214-215`). The only provider naming `replenish-vehicles` is the static `logisticscenter` (`structures.yaml:394`), additionally `DockedCondition: unit.docked` — so a vehicle can only ever be served standing at a fixed building. Two corollaries that are easy to get wrong: (1) even at the LC the *push* reaches only the two vehicles that declare the receiving condition (`himars`, `vehicles-america.yaml:1080`; `iskander`, `vehicles-russia.yaml:980`) — every other vehicle rearms through the `Rearmable`/`Resupply` **pull**, not `SupplyProvider`; (2) no `RearmActors` list anywhere names `supplycache`, so a cache can never be *walked to* — it is push-only, infantry-only. Any "supply truck follows the army" reasoning that counts vehicle ammo as demand is counting demand the truck cannot relieve (`SupplyFollowerBotModule`'s cluster `AmmoNeed` sums every `AmmoPool` in the cluster, vehicles included — `:523-531` — so a pure-armour cluster still attracts a truck that can do nothing for it).

**The aura `Range` once gated SELECTION only, never DELIVERY.** All three `Info.Range` comparisons were dimensionally correct; the defect was a *missing* comparison — `ResupplyTarget()` called `GiveAmmo` with no range check, so a target that walked out of the aura during the `RearmDelay` wait (or one picked by the unbounded Hunt scan below) was served anyway, in one case clean across the map. Fixed by extracting `SupplyProvider.InAuraRange(WPos, WPos, WDist)` (`:927-930`) — a **squared** horizontal comparison matching `WorldUtils.FindActorsInCircle`'s own `HorizontalLengthSquared <= r.LengthSquared` (`WorldUtils.cs:84`) rather than the floor()'d `WVec.HorizontalLength` (`Exts.ISqrt` defaults to `ISqrtRoundMode.Floor`, `Exts.cs:306`), so selection and delivery agree exactly on the boundary — applied at `IsValidTarget` (`:470`), `SetTarget` (`:516`), `SyncTargetCondition` (`:552`) and the new delivery gate (`:664-675`). The gate **keeps** the target and re-arms `rearmTicks` rather than dropping it, so an approaching provider still serves on arrival instead of thrashing its target pick. Sheltered garrison passengers stay exempt (they are `!IsInWorld` with a stale `CenterPosition`, and their building was in range when picked). The general lesson: a proximity aura's range must be re-checked at the moment of effect, because the wait between selection and delivery is where the geometry changes.

**There is an UNBOUNDED whole-map provider hunt in the engine, one stance away from live.** `SupplyProvider.UpdateTarget` falls through to `FindNeedsResupplyTarget` when `AutoTarget.EngagementStanceValue >= EngagementStance.Hunt` (`:302-304`), and that helper scans `world.ActorsHavingTrait<AmmoPool>()` with **no range term and no leash** (`:356-364`); `SetTarget` then drives the provider to it (`:513-525`). It is dormant for TRUK by default — TRUK's `AutoTarget` block overrides only `InitialResupplyBehavior*` (`vehicles.yaml:514-516`), so it ships `Defensive`, the engine default for both the human and AI fields (`AutoTarget.cs:160/163`), and TRUK inherits `^AutoTarget` rather than the `^Combatant` chain that some shipped maps flip to Hunt. But Hunt is both player- and AI-settable, and `UnitDefaultsManager` persists a human's per-type stance across games — so "supply trucks don't wander" is a default, not an invariant. Know this before anyone "enables truck hunting" by flipping a stance.

**Provider rearm is a PUSH gated on the RECIPIENT's condition, not on `RearmActors`.** `SupplyProvider.Tick` scans `FindActorsInCircle`, picks the greatest-need friendly `Rearmable` in range, and calls `GiveAmmo` on it directly (`SupplyProvider.cs:225/:308/:546`) — it **never consults the recipient's `Rearmable.RearmActors`**. What gates the push is `IsValidTarget` (`:403-440`): the recipient must be a friendly `Rearmable` with a non-full pool AND (when the provider sets one) carry the provider's `RearmCondition` external condition — default `replenish-soldiers` (`:59`), which only infantry hold. That is why a truck/cache tops up nearby infantry but skips vehicles, with no driving involved. `RearmActors` gates the *other* path — the recipient-initiated drive-to-a-host PULL (`AmmoPool.ChooseResupplier`, `:340`) — so adding `truk` to a vehicle's `RearmActors` lets that vehicle dock at a truck but does not make the truck's push serve it.

**An `AmmoPool` never refills itself on the battlefield — passive trickle is opt-in via `ReloadAmmoPool`.** `AmmoPool` is not `ITick` (`AmmoPool.cs:111` implements only `INotifyCreated, INotifyAttack, INotifyBecomingIdle, IResolveOrder, ISync`), and its self-reload method `AmmoPool.Reload()` (`AmmoPool.cs:361`) has **zero callers** engine-wide — so the `RemainingTicks`/`FullReloadTicks`/`FullReloadSteps` countdown it decrements (`:366`) never advances. The `ReloadDelay` field only *seeds* that inert countdown (`:237`) and is re-seeded on a dock rearm (`Rearmable.cs:52,66`). Actual in-field trickle exists **only** on the separate `ReloadAmmoPool` trait (`ReloadAmmoPool.cs:46`, which *is* `ITick` and calls `ammoPool.GiveAmmo(self, Count)` every `Delay` ticks, `:91`). A unit that does not carry `ReloadAmmoPool` therefore cannot top up in place — it must retreat and rearm at a provider (LC / TRUK / cache via `Rearmable` + `Resupply`). `ReloadAmmoPool` appears in only 7 mod YAML files (mostly static defenses); most units, including tunguska AA, lack it. **Do not read `AmmoPool.ReloadDelay` as "seconds to self-reload in the field" — it drives nothing without `ReloadAmmoPool`.**

**"A resupplier exists" is the engine's whole reachability test.** `AmmoPool.ChooseResupplier` ends in `ClosestToIgnoringPath` (`AmmoPool.cs:343-344`) and filters only on ownership, `RearmActors` membership and `CurrentSupply > 0` (`:331-341`) — no path check, and no `IsInWorld` check either. So a depot across an unfordable river reads as reachable, and any consumer that wants real reachability must supply its own proxy (and should document it as a proxy). When `ChooseResupplier` returns null, `AutoRearm` just sets `NeedsResupply = true` on every pool and returns (`:313-320`, with an in-code note that "Evacuation only happens when `ResupplyBehavior` is explicitly set to `Evacuate`") — the flag has only two readers, the Hunt-stance provider scan (`SupplyProvider.cs:361`) and `UnitBuilderBotModule.AnyFieldedUnitNeedsResupply` (`:487`, a production gate), neither of which disposes of the unit. So nothing at the unit level *removes* a dry unit with no reachable source — that judgement belongs at the *sector* level, not in a unit trait. What such a unit no longer does is keep fighting: the attack activities test `AmmoPool.CannotFight` and end, so it drops the attack order and falls idle rather than standing in range aiming a weapon it cannot fire, and going idle re-enters the `INotifyBecomingIdle` dispatch above — so it retries resupply once per cycle instead of asking exactly once and never again. Note also that `AutoRearmIfAllEmpty` requires **every** pool empty (`ammoPools.All(a => !a.HasAmmo)`, `:170-173`) — a unit dry on its main gun but holding a loaded secondary never enters the path at all.

**`ResupplyBehavior: Evacuate` is opt-in per actor, and it is not just trucks.** The `^AutoTarget` default is `Auto` (`defaults.yaml:318-319`); exactly four actors override to `Evacuate` — TRUK (`vehicles.yaml:515`) plus the rocket artillery **m270** (`vehicles-america.yaml:704`), **grad** (`vehicles-russia.yaml:529`) and **tos** (`vehicles-russia.yaml:652`). A spent Grad rotating to the map edge is designed behaviour, not a bug.

**`MobileInfo.LocomotorInfo.SharesCell` is the name-free infantry/vehicle discriminator.** `MobileInfo.LocomotorInfo` is resolved in `RulesetLoaded` (`Mobile.cs:143-154`), so it is readable straight off an `ActorInfo` with no trait instance and no `TraitDictionary` lookup. In WW3MOD `SharesCell: true` appears on exactly the four `foot*` locomotors (`world.yaml:32/47/64/80`) and no vehicle locomotor — which cleanly separates "topped up in place by a truck/cache push" (infantry) from "must drive to a depot" (vehicles) without an actor-name list and its case-mismatch hazard. Caveat: the `Walker` locomotor (`world.yaml:193`) does not share cells, so an infantry-like actor using it would misclassify.

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

### Artillery salvo economics

A single artillery *volley* is priced as a batch, not per round: `SalvoCost(burst, reloadCount, supplyValue) = ceil(Burst / ReloadCount) × SupplyValue` (`FiresEconMath.cs:90`), the same whole-batch rounding the rearm/evac math uses. This is why rocket launchers and tube guns sit in different economic weight classes:

- **Rocket launchers fire a large `Burst`** (Grad 40, TOS 24, M270 12 — `weapons-ballistics.yaml`), so a volley repays hundreds of supply (e.g. Grad `8×85≈680`, M270 `12×70=840`, TOS `8×120=960`).
- **Tube guns fire `Burst` 1–3** (Paladin 3, Giatsint 1), so a shell repays ~60 supply.

So a lone $100 infantryman is worth a tube shell but not a Grad volley — the arithmetic reason a fires AI should gate rocket fire on target value.

**The AoE that catches a formation comes from the salvo spread, not the warhead.** The lethal footprint is the `Burst` rounds scattered across the projectile's `Inaccuracy` (the beaten zone), NOT the per-round `SpreadDamageWarhead.Spread` — which is sub-cell on every piece (64–196, i.e. < 0.2 cell; 1 cell = 1024). A cluster/AoE radius derived from warhead `Spread` alone would catch almost nothing.

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
- When low (`currentSupply < RestockThreshold`, 50) an **Auto**-stance truck drives back to nearest LC (`RestockActors: logisticscenter`) and refills. But TRUK's default resupply stance is **Evacuate** for both human and AI (`vehicles.yaml:514-515`), so a low truck normally rotates to the map edge to return its credit rather than shuttling — see the residue-evac rule below.
- Refill drains the LC's `currentSupply` by the amount taken. A truck that needs 600 supply takes 600 from the LC, leaving the LC with 2400. If the LC has less than the truck wants, the truck takes what's there and leaves partially full.
- Can drop its remaining supply as a SUPPLYCACHE box — by the player's deploy command, or, for a bot-owned truck, as the **dangerous-mode delivery**. The drop is all-or-nothing either way (`DropSupplyCacheHere` → `SetSupply(0)`): there is no partial unload.
- **A bot truck's delivery MODE is chosen by believed danger; danger never decides whether to go.** Quiet front → close to aura range, serve in place, **keep** the remainder for the next customer. Under fire → stop short of the platoon, unload everything, egress. The classifier, the commitment invariants, and why infantry walking to a placed crate is correct behaviour are in [`supply-route.md`](supply-route.md) §"Forward delivery" — not restated here.

**Unusable-residue trucks count-as-empty and evacuate** (gated by `SupplyProvider.EvacuateOnUnusableResidue`, true only on TRUK — `vehicles.yaml:549`). A near-empty truck holding a residue too small to give any nearby soldier a batch would otherwise park at the front forever:

- `SupplyProvider.CountsAsEmpty` = `currentSupply <= 0 || residueUnusable` (`SupplyProvider.cs:120`). The `residueUnusable` latch is set from a `MinNeedThreshold`-aware `ResidueVerdict` (`:693`, NUnit-pinned) — true (evac) when no reachable unit can be served a batch, false (keep serving) when one can, null (leave the latch) when there is no demand. Every refill path clears the latch, so a full truck never shows a phantom red bar.
- An Evacuate truck **keeps serving below `RestockThreshold`** (`KeepServingBelowThreshold`, `:272-274`) instead of reserving the last bit for a drive home it will never make — it serves down to the last usable batch, *then* the residue goes unusable and `CountsAsEmpty` carries it to evac. Self-restock is stance-aware (`ShouldSelfRestock`, `:257`): an Evacuate truck does not auto-shuttle to an LC (switching it to Auto restores restock-when-low).
- The supply **selection bar turns red** (`ISelectionBar.GetColor`, `:674`) while `residueUnusable`; a truly drained truck (`currentSupply == 0`) stays amber. The empty-evac path itself is `DropsSupplyCache.OnBecomingIdle` plus an `ITick` re-check (a truck that goes idle then has its residue become unusable never gets a second `INotifyBecomingIdle`), both gated on `CountsAsEmpty`. `SupplyFollowerBotModule.IsLowOnSupply` also returns true on `CountsAsEmpty` so the bot stops re-tasking an evacuating truck forward.

### SUPPLYCACHE (dropped supply box)

Spawned when a truck unloads its supply on the ground. Functionally a stationary truck — same `SupplyProvider` trait, same UI:

- **Range circle** showing rearm reach (4 cells). *(Pending visual verification in-game.)*
- **Selection bar** showing remaining supply. *(Pending visual verification in-game.)*
- Sprite tier (Full/Mid/Low) reflects the supply remaining.
- Capturable by enemies (`ProximityCapturable`) — if the enemy reaches it first, the supply changes hands at full value.
- **Auto-targetable like the truck — carries no `NoAutoTarget`.** Its `Targetable: TargetTypes: Ground, Structure` (`misc.yaml:387`) matches the base `AutoTargetPriority@FireAtWill`, so nearby enemies engage and destroy it unaided (HP 5000, Light armor). An earlier `NoAutoTarget` that made it inert to enemy fire was removed.
- **Serves down to empty — `RemoveBelowSupply: 1` (`misc.yaml:418`).** `SupplyProvider.Tick` despawns a provider once `currentSupply < RemoveBelowSupply` (`SupplyProvider.cs:166`). A stationary cache has no drive-home trip to reserve supply for (unlike TRUK's `RestockThreshold`), so the threshold is 1 — a freshly dropped low-supply crate no longer self-vanishes on its first tick.
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
| `supplycache` | 750 | (none) | Stationary; serves down to supply 1 (`RemoveBelowSupply: 1`), then despawns or is captured. |

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

## Where the live values live

This doc describes the rules. The current per-pool numbers are in YAML and shift more often than this doc updates. To list every pool's batch math:

```
git grep -nE 'AmmoPool|ReloadCount|SupplyValue' mods/ww3mod/rules/
```

## When tuning further

- **Munition consistency**: a Hellfire is a Hellfire. Changing Apache's per-batch SupplyValue means changing every Hellfire-firing platform to match.
- **Per-tier infantry baseline**: when raising a soldier's `Cost`, the extra goes into the ammo budget so the empty-evac refund stays at the tier baseline.
- **Bulk-ammo cap**: a pool's full budget fits inside one truck-load (~750). Above that, combat economics break down.
- **Pool budget ceiling**: a pool's `(Ammo / ReloadCount) × SupplyValue` is at most `Cost − minimum-empty-refund`, so an empty unit always retains some salvage value.
