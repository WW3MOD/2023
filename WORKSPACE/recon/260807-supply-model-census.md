# Supply model census — what exists today, ahead of a carried-supply / conscript / transfer-order rescope

**Read against `main @ f2a72e43`** (`git status -sb`: `main...origin/main [ahead 16]`, clean apart from untracked
`.claude/scheduled_tasks.lock`, `.maestro/managers/`, `WORKSPACE/open-decisions-260725.html`,
`tools/autotest/__pycache__/`). Read-only recon: no engine or YAML file was modified.

**Scope caveat.** A worker on branch `auto/supply-dwell` is editing
`SupplyFollowerBotModule.cs`, `SupplyLogisticsMath.cs` and `GarrisonBotModule.cs` in a separate worktree.
Every claim below about those three files is scoped to `f2a72e43` and may already be stale. Nothing in the
answers below *depends* on them — they are bot consumers, not part of the supply model itself.

This document maps the current system. It deliberately does **not** design the new one.

---

## 0. TL;DR

- **"Supply" is one fungible integer**, `SupplyProvider.currentSupply` (`SupplyProvider.cs:124`). It is not
  per-weapon and not an ammo pool. It is spent in whole **batches** priced by the *recipient pool's*
  `SupplyValue`. One number, three actors carry it: `logisticscenter`, `truk`, `supplycache`.
- **"Part of the cost is supplies" is ALREADY EXPRESSIBLE**, exactly, via
  `SupplyProviderInfo.SupplyCreditValue` (`SupplyProvider.cs:73`) → `MissingSupplyValue` (`:959`) →
  `CustomSellValue.GetSellValue` (`CustomSellValue.cs:48-51`). TRUK is literally `Cost: 1000` /
  `SupplyCreditValue: 750`. A conscript at `Cost: 100` / `SupplyCreditValue: 50` is a YAML edit.
- **`SupplyProvider` is a plain composable trait with no `Requires<>`** (`SupplyProvider.cs:23`). Nothing
  stops it sitting on a rifleman. This is the single most load-bearing finding in this document.
- **A crate actor already exists and is fully built**: `SUPPLYCACHE` (`misc.yaml:370-427`), a real world
  actor holding a real quantity, droppable by deploy, mergeable, capturable, absorbable. Deploy-to-drop is
  **done**. Right-click-to-reload-it-back-into-a-truck is **not** — it is the one missing leg.
- **Two providers CAN already serve one receiver simultaneously.** There is no reservation and no lock.
  What does *not* exist is cost-splitting: a batch is indivisible and must be affordable by **one**
  provider alone (`SupplyProvider.cs:682`, `:700`).
- **The conscript is fully authored and disabled by one word** (`Prerequisites: ~disabled`,
  `infantry.yaml:1108`) with the real prerequisite preserved in a trailing comment.

---

## 1. What IS "supply" in this codebase today?

**One fungible integer per provider actor. Not a boolean, not an ammo pool, not a cargo payload.**

The whole of supply is the private field `int currentSupply` on `SupplyProvider`
(`SupplyProvider.cs:124`), seeded at construction from `SupplyInit` or `Info.TotalSupply`
(`:159`) and exposed read-only as `CurrentSupply` (`:142`). There is exactly one mutator triple —
`DeductSupply(int)` (`:772`), `SetSupply(int)` (`:783`), `AddSupply(int)` (`:790`) — plus the in-trait
decrement inside `ResupplyTarget` (`:707`). No other quantity anywhere in the tree represents supply.

I checked for a second store: `CargoSupply`, which `economy.md:146` still names in the sell formula, **does
not exist**. It survives only in `DOCS/archive/` (`LOBBY_REDESIGN.md:93/95/295`,
`PERFORMANCE_FIXES.md:113-114`, `RELEASE_V1_TODO.md:29`,
`superpowers/plans/2026-05-04-supply-truck-resupply-and-rubble-evac.md:8-21`). The live sell path reads
`SupplyProvider` only (`CustomSellValue.cs:49`). *That line of `economy.md` is stale and should be corrected
by whoever next touches the doc — I have left it alone, being read-only here.*

### Is it fungible, or per-weapon?

**Fungible on the provider side; priced by the recipient's pool on the consumption side.** The provider
holds an undifferentiated number. What one batch *costs* comes from the receiving pool's own
`AmmoPoolInfo.SupplyValue`, and what one batch *delivers* comes from that pool's `ReloadCount`
(`SupplyProvider.cs:698-707`):

```csharp
var batchSize = Math.Max(1, bestPool.Info.ReloadCount);      // :698
var missing   = bestPool.Info.Ammo - bestPool.CurrentAmmoCount;
var canAfford = currentSupply >= bestPool.Info.SupplyValue;  // :700
if (canAfford && missing > 0) {
    var roundsToGive = Math.Min(batchSize, missing);
    if (bestPool.GiveAmmo(currentTarget, roundsToGive))      // :705
        currentSupply -= bestPool.Info.SupplyValue;          // :707
}
```

So a conscript's rifle batch costs 1 (`infantry.yaml:1121-1124`: `ReloadCount: 20`, `SupplyValue: 1`) while
an AT missile costs 65 — out of the *same* pool of numbers. Supply is a universal currency; the exchange
rate is per-pool. **One provider serves exactly one pool per `RearmDelay` cycle**, the greatest-need one
(`:678-691`), then drops the target and re-scans (`:716-719`).

### How a truck's stock is decremented and replenished

| Direction | Path | Line |
|---|---|---|
| **Out** — rearm push | `ResupplyTarget` → `currentSupply -= SupplyValue` | `SupplyProvider.cs:707` |
| **Out** — drop crate | `DropSupplyCacheHere` → `supply.SetSupply(0)` and spawn/merge | `DropsSupplyCache.cs:85-125` |
| **Out** — absorbed by an LC | `AbsorbsSupplyCache` calls `cacheProvider.DeductSupply(available)` | `AbsorbsSupplyCache.cs:90` |
| **Out** — passenger quick-rearm | `QuickRearm` → `supplyProvider.DeductSupply(cost)` | `QuickRearm.cs:51` |
| **In** — restock at LC (auto) | `TryRestock` → `hostProvider.DeductSupply(taken); AddSupply(taken)` | `SupplyProvider.cs:749-761` |
| **In** — restock at LC (ordered) | `QueueDriveAndRestock`, identical arithmetic | `DropsSupplyCache.cs:187-197` |
| **In** — LC absorbing a crate | `supplyProvider.AddSupply(available)` | `AbsorbsSupplyCache.cs:93` |
| **In** — merging a drop onto an existing crate | `existingProvider.AddSupply(amount)` | `DropsSupplyCache.cs:102` |

Restock is conservative in both directions: `taken = min(headroom, host.CurrentSupply)` — no free refills,
the host drops by exactly what moved.

### The free-ammo side channel you must know about before pricing anything

There is a **second, unpriced ammo stream** that costs no supply at all. `ReloadAmmoPool` is `ITick` and
calls `ammoPool.GiveAmmo(self, Count)` every `Delay` ticks with **no supply deduction whatsoever**
(`ReloadAmmoPool.cs:75-92`). Every soldier carries one, gated on the `replenish-soldiers` condition
(e.g. `infantry.yaml:1136-1138` for the conscript). That condition is granted from two places:

1. A provider holding you as its current target grants it (`SupplyProvider.cs:565-568`) — the trait's own
   comments (`:534-538`, `:857-871`) are explicit that the condition "enables the target's own
   `ReloadAmmoPool` trickle, which has no range check of its own", which is why the aura fix had to strip
   the condition as well as the delivery.
2. **The Logistics Center grants it by bare proximity, for free, to every ally within 4 cells** —
   `ProximityExternalCondition@ReplenishSoldiers` (`structures.yaml:382-386`), which is a *separate trait*
   from the LC's `SupplyProvider` (`:387-395`, `Range: 2c0`, `RearmCondition: replenish-vehicles`,
   `DockedCondition: unit.docked`). Nothing about that proximity grant consults `currentSupply`.

**So infantry standing near an LC rearm free, out of nothing, drawing on no pool.** I have not found a
consumer that compensates for this. Any design that prices carried supply needs to know this channel
exists, because it is the cheapest ammo in the game and it is uncapped. (`AffectsParent` defaults false,
`ProximityExternalCondition.cs:37`, so the LC does not grant to itself — irrelevant here since it holds no
`AmmoPool`.)

---

## 2. Who can hold supply today, and is that a trait or a unit type?

**It is a trait, and it is freely composable. A rifleman could hold supply today with zero new engine
machinery.**

`SupplyProviderInfo : PausableConditionalTraitInfo` (`SupplyProvider.cs:23`) declares **no `Requires<>`
clause at all**. Compare `DropsSupplyCacheInfo : TraitInfo, Requires<SupplyProviderInfo>`
(`DropsSupplyCache.cs:25`) and `AbsorbsSupplyCacheInfo : ConditionalTraitInfo, Requires<SupplyProviderInfo>`
(`AbsorbsSupplyCache.cs:19`) — the dependency runs *toward* `SupplyProvider`, never away from it. The trait
itself needs nothing: no `Mobile`, no `Building`, no `Cargo`. It reads `self.CenterPosition`, `self.Owner`,
and its own integer.

Today exactly three actors carry it, and all three configure the *same* trait differently:

| Actor | YAML | TotalSupply | Range | RearmCondition | Distinguishing config |
|---|---|---|---|---|---|
| `logisticscenter` | `structures.yaml:387-395` | 3000 | 2c0 | `replenish-vehicles` | `DockedCondition: unit.docked`; `AbsorbsSupplyCache` (`:396-398`) |
| `truk` | `vehicles.yaml:542-550` | 750 | 5c0 | `replenish-soldiers` | `RestockActors: logisticscenter`, `EvacuateOnUnusableResidue: true`, `DropsSupplyCache` (`:551-552`) |
| `supplycache` | `misc.yaml:408-421` | 750 | 4c0 | `replenish-soldiers` | `RemoveBelowSupply: 1`, sprite-tier conditions |

Note the **SUPPLYROUTE beachhead holds no supply** — it has no `SupplyProvider` at all (the only three
`SupplyProvider:` keys under `mods/ww3mod/` are the three above). Units arrive from off-map with ammo
already bundled into their cost; the beachhead is not a depot.

### What actually happens if you bolt `SupplyProvider` onto `^Soldier` or onto E1

Composable does not mean free. The concrete consequences, all readable off the trait:

- **The soldier becomes a provider that pushes to *other* infantry immediately.** Set
  `RearmCondition: replenish-soldiers` and it serves anyone in range carrying that condition — which is
  every soldier (`infantry.yaml:214-215`). No further wiring.
- **It cannot serve itself.** `IsValidTarget` rejects `a == self` outright (`SupplyProvider.cs:463`). The
  user's "it can resupply itself" is therefore *not* free. But there is a cheap idiom for it that already
  ships: add a `ProximityExternalCondition` with `AffectsParent: true`
  (`ProximityExternalCondition.cs:37`) granting `replenish-soldiers` to self, and the soldier's own
  `ReloadAmmoPool` trickles — **for free, deducting nothing**, which is exactly the unpriced channel from
  §1. A *priced* self-serve is new code (a small one: a self-branch in `ResupplyTarget`).
- **It gains a supply selection bar and it is `DisplayWhenEmpty` (`:846`)**, i.e. every rifleman would
  permanently show an amber bar. Cosmetic, but it will look wrong on a 40-man push.
- **`ICargoCanLoadFilter.CanLoadPassenger` returns `currentSupply > 0` (`:852-855`).** This is queried on
  the **cargo host** (`Cargo.cs:189` — `self.TraitsImplementing<ICargoCanLoadFilter>()`), so it is inert on
  a soldier with no `Cargo`. Harmless *here*, but it is a live trap if anyone later gives a supply-carrying
  actor a `Cargo` trait: an emptied transport silently refuses to load.
- **`AutoTarget`-driven paths change meaning.** `ShouldSelfRestock` (`:332-339`) reads the actor's
  `ResupplyBehaviorValue`; a soldier with `RestockActors` set would start walking to the LC on its own.
  Leaving `RestockActors` empty (the default, `:56`) disables that entirely (`:334`).
- **`CustomSellValue` immediately starts charging for missing supply** (`CustomSellValue.cs:49-51`), which
  is the *desired* behaviour for the "50 of the cost is supplies" ask — see §8.

**Verdict for (2): composable trait, not a unit type. No new machinery is required for a rifleman to hold
and give supply. Self-serve is the one sub-ask that is not already covered.**

---

## 3. Does a droppable supply cache/crate already exist?

**Yes, and it is more complete than the brief assumes.** Two traits are involved, one of which is dead.

### `SUPPLYCACHE` — a real actor holding a real quantity

`misc.yaml:370-427`. It is a `Building` (`:377-380`, 1×1, `Footprint: x`) with `Health: 5000` (`:382-383`),
`Armor: Light`, `Targetable: Ground, Structure` (`:386-387` — no `NoAutoTarget`, so enemies shoot it
unaided), `ProximityCapturable` at `1c512` with `CaptorTypes: Player, Vehicle, Tank, Infantry`
(`:388-391`), three sprite tiers driven by the provider's supply-band conditions (`:396-407`, `:419-421`), a
`SupplyProvider` with `TotalSupply: 750`, `Range: 4c0`, `SupplyCreditValue: 750`, `RemoveBelowSupply: 1`
(`:408-418`), and a rendered range circle (`:422-427`).

The quantity is genuine and per-instance: the spawn passes `new SupplyInit(cacheInfo, amount)`
(`DropsSupplyCache.cs:118-123`), and `SupplyProvider`'s constructor reads it
(`init.GetValue<SupplyInit, int>(info, info.TotalSupply)`, `SupplyProvider.cs:159`). A crate dropped from a
truck holding 137 is a crate holding 137, not a crate holding 750.

### `DropsSupplyCache` — the live trait (TRUK only)

`DropsSupplyCache.cs`, on TRUK at `vehicles.yaml:551-552`. It issues **three** orders (`:274-283`):

| Order | Targeter | Priority | What it does |
|---|---|---|---|
| `DropSupplyCache` | `DeployOrderTargeter` (`:280`) + `IIssueDeployOrder` (`:296-304`) | 5 | Drop everything here as a crate |
| `Restock` | `RestockOrderTargeter` (`:313-344`) | 7 | Right-click a friendly **LC**: drive there, `Wait(25)`, pull supply *from* it |
| `DeliverSupply` | `DeliverSupplyOrderTargeter` (`:346-372`) | 6 | **Ctrl+click** a friendly LC: drive there, drop the crate on our cell, let `AbsorbsSupplyCache` pull it in |

`DropSupplyCacheHere` (`:85-125`) **merges into an existing crate on the same cell** if one is there
(`:94-106`), otherwise spawns a fresh one at frame end (`:109-124`). `CanDropCache` (`:75-83`) requires
`CurrentSupply > 0` and a cell occupied by nothing but self or another cache. There is also a UI button:
`DROP_SUPPLY` in the cargo panel issues the same order (`CargoPanelLogic.cs:192-203`), and the header/label
show `"{n} supply"` (`:56-64`, `:178-190`).

### `DropsCrate` — a near-duplicate that is DEAD

`DropsCrate.cs` is a second trait doing the same thing (`CrateActor = "supplycache"`, `:25`; same
`DropCrate` deploy order, `:65-90`; same `SupplyInit` spawn, `:103-113`). **It appears in no mod YAML
whatsoever** — `grep -rn "DropsCrate" mods/` returns nothing. It is simpler than `DropsSupplyCache` (no
merge, requires a completely empty cell, `:62`) and it is the likely ancestor. Anyone extending the drop
path should delete it or extend `DropsSupplyCache`, not both.

### Can anything pick a crate back up?

**Only the Logistics Center, and only by standing next to it.** `AbsorbsSupplyCache`
(`AbsorbsSupplyCache.cs`) is `ITick`, finds one cache within `Range` (LC: `2c512`, `structures.yaml:397`),
and moves `min(TransferRate=50, headroom, cache.CurrentSupply)` per `TransferInterval` (5 ticks) into
itself, disposing the cache at zero (`:75-96`). It is on the LC and nothing else (`structures.yaml:396-398`).

So the round trip today is: **truck → crate (deploy) → LC (absorb)**. The leg the user wants —
**crate → truck** — does not exist. Nothing gives a `SupplyProvider` an order to *take* from another
`SupplyProvider` except `Restock`, and `Restock` is hard-gated to docking-aware hosts:
`RestockOrderTargeter.CanTargetActor` returns false unless the target's
`SupplyProvider.Info.DockedCondition` is non-empty (`DropsSupplyCache.cs:325-326`), and the crate leaves
`DockedCondition` empty. `TryQueueRestockAtNearestHost` applies the identical filter (`:263`) with the
in-code reason: *"Only target docking-aware hosts (LCs), so an empty truck doesn't try to 'dock' at a ground
SUPPLYCACHE."*

**Verdict for (3): deploy-to-drop is 100% built, including merge, capture, absorb, sprite tiers and a UI
button. Right-click-crate-to-reload is the single missing piece, and the shape of the fix is small and
obvious: relax that one `DockedCondition` filter and give the crate a compatible arrival gate.**

---

## 4. Resupply delivery mechanics

### It is a PUSH aura, rated over time, and it is one *pool* per cycle

`SupplyProvider.ITick.Tick` (`:176-278`) walks an early-return ladder (out-of-world `:186`,
paused/disabled `:192`, restocking `:199`, self-remove `:212`, drained `:226`, below-threshold `:244`), then
every `ScanInterval` (7 ticks, `:68`/`:256-260`) re-picks the greatest-need target, and every `RearmDelay`
ticks (TRUK: 6; LC/cache: 25) calls `ResupplyTarget` (`:269-277`). One call delivers **one batch of one
pool** and then drops the target to force a re-scan (`:716-719`).

Selection is `FindGreatestNeedTarget` (`:366-436`): a `FindActorsInCircle` sweep at `Info.Range`, filtered
by `IsValidTarget` (`:461-500`), scored by `CalculateNeed` = supply-weighted missing fraction (`:438-459`),
and skipped below `MinNeedThreshold` (5%, `:32`). It additionally reaches **inside garrison buildings** for
sheltered passengers, who are out of the world with stale positions (`:399-432`).

`IsValidTarget` is where the structural rules live:
- alive, in world, not self, allied (`:463-467`)
- **in aura range** (`:470`)
- if `DockedCondition` is set, the target must already hold it (`:476-482`) — the LC's `unit.docked`
- `Rearmable` with a non-full pool (`:485-486`)
- and the target must declare the provider's `RearmCondition` as an `ExternalCondition` (`:488-494`)

That last gate is why a truck can never serve a vehicle: TRUK/cache name `replenish-soldiers`
(`vehicles.yaml:546`, `misc.yaml:412`), which only `^Soldier` declares (`infantry.yaml:214-215`).

### The aura-range finding: still holds, and the fix is in place

`DISCOVERIES.md:186-195` recorded that `Range` gated selection only, never delivery. **Verified fixed at
`f2a72e43`.** `SupplyProvider.InAuraRange(WPos, WPos, WDist)` (`:927-930`) is a squared horizontal
comparison matching `WorldUtils.FindActorsInCircle`, and it is now applied at four sites: `IsValidTarget`
(`:470`), `SetTarget` (`:516`), `SyncTargetCondition` (`:552`) and — the one that was missing — a delivery
gate inside `ResupplyTarget` (`:664-675`). The gate routes through the pure `DecideServe(inWorld, inAura)`
(`:873-882`) so delivery and condition-holding can never disagree, and it **keeps** the target while
re-arming `rearmTicks` rather than dropping it. Sheltered garrison passengers stay exempt via the
`!targetInWorld` branch (`:875-876`).

The **unbounded Hunt-stance scan is also still present**: `UpdateTarget` falls through to
`FindNeedsResupplyTarget` when `EngagementStanceValue >= Hunt` (`:302-304`), and that helper scans
`world.ActorsHavingTrait<AmmoPool>()` with no range term and no leash (`:356-364`). Dormant by default,
one stance flip from live. Unchanged since the 2026-08-04 entry.

### Can two providers serve one receiver? **YES — today, with no code change.**

Nothing enforces one-to-one *on the receiver side*. Concretely:

- **The one-to-one that does exist is per-provider, not per-receiver.** `SupplyProvider` holds a single
  `Actor currentTarget` field (`:128`). One provider serves one unit at a time. That is a provider-side
  fan-out limit, not a receiver-side lock.
- **There is no reservation, no claim registry, and no "already being served" test anywhere.** I looked for
  one: `IsValidTarget` (`:461-500`) checks relationship, range, dock condition, `Rearmable`, non-full pool,
  and rearm condition — and nothing else. Two trucks scanning the same tick will both pick the same
  greatest-need soldier and both serve it.
- **The condition grant supports multiple simultaneous sources.** `ExternalCondition.permanentTokens` is a
  `Dictionary<object, HashSet<int>>` keyed by *granting source* (`ExternalCondition.cs:63`), and
  `CanGrantCondition` only refuses on `SourceCap`/`TotalCap` (`:88-104`), both of which default to 0
  (= unlimited, `ExternalConditionInfo:32/35`) and are **not set** on infantry's
  `ExternalCondition@AmmoReplenish` (`infantry.yaml:214-215`). So provider A and provider B can each hold
  their own live grant on the same soldier.
- **Delivery just clamps.** `AmmoPool.GiveAmmo` clamps to `Info.Ammo` and returns false when already full
  (`AmmoPool.cs:141-152`). Two providers over-delivering is a wasted batch on the loser, not a corruption.

**What does NOT exist is cost-splitting.** A batch is atomic and must be affordable by a single provider:
the pool-selection loop skips any pool where `currentSupply < pool.Info.SupplyValue` (`:682`), and the
delivery re-tests `canAfford = currentSupply >= bestPool.Info.SupplyValue` (`:700`). Two conscripts holding
40 supply each cannot combine to deliver one 65-cost ATGM batch — each independently reads "cannot afford",
`hasUnaffordableTargets` goes true (`:382`), and (on a residue-evacuating provider) the residue latches
unusable (`:290-295`, `:944-956`).

So the user's ask decomposes cleanly:
- *"two or more providers serve one demand"* — **already works**, emergently.
- *"sharing the cost so that they both have the same amount left afterwards"* — **new machinery.** It
  requires a co-operative transaction: a way for provider A to find provider B, agree a split, and deduct
  proportionally before a single `GiveAmmo`. Nothing in the trait is shaped for that; every path is a
  single-provider decision made in that provider's own `Tick`.

### The other delivery paths (for completeness)

| Path | Mechanism | Rated? | Costs supply? |
|---|---|---|---|
| Provider push aura | `SupplyProvider.ResupplyTarget` | 1 batch / `RearmDelay` | **Yes** |
| Recipient pull to a host | `AmmoPool.AutoRearm` → `SeekSupplyProvider` (`AmmoPool.cs:286-293`) or `Resupply` (`:311`) | walks, then the push takes over | Yes (via the push) |
| Dock rearm | `Rearmable.RearmTick` (`Rearmable.cs:57-78`) | 1 `ReloadCount` / `ReloadDelay` | **No** — no provider involved |
| Free trickle | `ReloadAmmoPool.Reload` (`ReloadAmmoPool.cs:83-93`) | `Count` / `Delay` | **No** |
| Passenger quick-rearm | `QuickRearm.OnPassengerEntered` (`QuickRearm.cs:39-58`) | **instant, to full** | Yes |

`QuickRearm` is worth flagging: it is a *board-to-refill-instantly* mechanic that already exists, deducts
supply correctly, and auto-ejects after `EjectDelay` (`:24`, `:64-127`). **It is used in no mod YAML** —
`grep -rn "QuickRearm" mods/` is empty, and `Requires<CargoInfo>` (`:21`) means TRUK would need a `Cargo`
trait to use it. If "load up from a source" ever wants an instant variant, this is a built, unwired answer.

---

## 5. `AutoSeekSupplies` — the pull side

**It is ON.** `AutoSeekSuppliesInfo.Enabled` defaults false in code (`AutoSeekSupplies.cs:34`) but
`^Soldier` sets `Enabled: true` (`infantry.yaml:221-222`, with a comment recording it as deliberate and
owner-agnostic). `DISCOVERIES.md:61` records it shipped in isolated commit `f15cfbde` and that
`AutoSeekSuppliesInfo` derives from plain `TraitInfo`, so it **cannot be condition-disabled per owner
class** without a mechanical `TraitInfo` → `ConditionalTraitInfo` conversion.

### How it picks a provider

`INotifyIdle.TickIdle` (`:91-114`): bail without `Enabled`/`IMove`/`Rearmable`; throttle to
`ScanInterval` 40 ticks with a deterministic per-actor phase from `ActorID` (`:78` — explicitly *not* from
`SharedRandom`, to preserve control-game byte-identity); check `StancesPermit` (`:116-125`, delegating to
`SupplyHuntMath.StancesPermitHunt`) and `NeedsSupplies` (`:127-134`, any pool under 250‰); then
`FindNearestUsableProvider` (`:140-161`) and queue `SeekSuppliesAndReturn` (`:112`).

Selection is **nearest-by-straight-line inside a 20-cell leash** (`:38-44`, `:151-155`) among providers
passing `CanServe` (`:177-217`), which mirrors the provider's own gates from the other side:

- not dead/out-of-world/self (`:179`)
- `!provider.CountsAsEmpty && provider.CanServeNow` (`:192`) — `CanServeNow` (`SupplyProvider.cs:890-918`)
  is the provider's own Tick ladder, *asked rather than reproduced*, because it reads private restock state
- relationship (`:195`)
- **skips docking-gated providers entirely** (`:201-202`) — walking into an LC's aura achieves nothing;
  that is the `Rearmable`/`Resupply` dock path's job
- the recipient-side condition gate (`:207-208`)
- affordability: at least one non-full pool the provider can afford a batch of (`:212-214`)

There is **no path check** — `SupplyHuntMath.WithinLeash` is straight-line, and the trait's own doc comment
says so (`:42-43`): *"a source 20 cells away across a river passes the leash and is only rejected later,
when the approach fails to reach it."*

### What happens if the provider moves, dies, or empties mid-errand

`CanServe` is deliberately the **one** eligibility test, re-asked every tick for the whole trip by
`SeekSuppliesAndReturn` (`:163-176` doc comment: *"a provider we would not walk to must also be one we stop
walking to, and two separate copies of this rule drifted apart the moment one of them gained a clause"*).
So a provider that dies, empties, latches an unusable residue, starts restocking, or drops below the
serving threshold fails `CanServe` and the errand ends. A provider that *moves* is simply chased —
`CanServe` has no distance term at all (the leash is applied only at selection, `:152`), so a truck driving
away is followed indefinitely until the stall guard in the activity fires.

The separate, older `SeekSupplyProvider` activity (`SeekSupplyProvider.cs`) — queued from
`AmmoPool.AutoRearm` (`AmmoPool.cs:290`), not from this trait — handles it differently: it re-picks every
25 ticks (`:36`, `:79-94`), validates on `CurrentSupply > 0` only (`:45-52`), and sets `NeedsResupply = true`
on every pool when nothing is left (`:96-102`).

### Would it cope with many small providers instead of a few big ones?

**Mechanically yes; economically it degrades, and there is one hard cliff.**

- The scan is `ActorsHavingTrait<SupplyProvider>()` (`:145`) with per-candidate trait resolution and
  `CanServe` — currently ~1-3 actors per player. With every conscript a provider this becomes O(infantry
  count) per scanning soldier per 40 ticks, i.e. **O(n²) across the army**. It will not be correct-wrong,
  but it is a real cost that nothing bounds today. The leash (`:152`) prunes only *after* the
  `HorizontalLengthSquared` is computed, and `CanServe` runs before it (`:148`).
- **The hard cliff is affordability.** `CanServe`'s last gate (`:212-214`) requires the provider to afford
  a full batch alone. A conscript carrying ~50 supply passes for rifle pools (`SupplyValue: 1`) and fails
  for an ATGM (65). A dry AT specialist would therefore walk past every conscript on the map and find no
  usable provider — `FindNearestUsableProvider` returns null (`:160`), `TickIdle` returns (`:109-110`), and
  the soldier stands there. **This is the same cliff as §4's no-cost-splitting finding, seen from the pull
  side.** Any multi-provider design has to fix both gates or the pull side silently ignores the feature.
- Nearest-first with many tiny providers means a soldier drains the closest conscript to residue, then
  re-scans and walks to the next — a chain of short errands, each of which makes it combat-inert
  (`AutoSeekSupplies.cs:25-28`: on an activity ⇒ not idle ⇒ `AutoTarget`'s idle scan and retaliation do not
  fire). With one big truck that is one trip; with twenty conscripts it could be many.

---

## 6. The conscript

**Fully authored. Disabled by one word. Assets intact.**

### The actor

`^E1` at `infantry.yaml:1097-1150`, with concrete actors `E1` (`:1151-1152`), `E1R1` (veteran,
`:1153-1161`), `E1.america` / `E1R1.america` (`infantry-america.yaml:1-16`) and `E1.russia` /
`E1R1.russia` (`infantry-russia.yaml:1-16`).

| Property | Value | Line |
|---|---|---|
| Inherits | `^CamoSoldier`, `^AutoTargetInfantry`, `^CapturesOccupiedBuildings` | `:1099-1101` |
| Name | "Conscript" | `:1103` |
| Cost | **50** | `:1110-1111` |
| Armament | `5.56mm.E3` (the same rifle the rifleman carries), `primary-turret` | `:1112-1116` |
| AmmoPool | `Ammo: 100`, `ReloadCount: 20`, `SupplyValue: 1` → 5 batches, budget 5 (~10% of cost) | `:1117-1124` |
| Rearmable | `RearmActors: truk, logisticscenter`; `AmmoPools: primary-ammo` | `:1133-1135` |
| ReloadAmmoPool | on `primary-ammo`, gated `replenish-soldiers` | `:1136-1138` |
| Sprites | `Scale: 0.6`, class pip `e1_class` | `:1145-1150` |
| Description | "Basic militia infantry with minimal training. — 5.56mm assault rifle — No armor" | `:1109` |

**It is already rifle-only.** The user's "armed only with a rifle" is the existing definition.

### What disabled it

`Buildable: Prerequisites: ~disabled` (`infantry.yaml:1108`). The faction actors do the same, and
**the original prerequisite is preserved verbatim in a trailing comment**:

```yaml
E1.america:                                         # infantry-america.yaml:2-5
	Inherits: ^E1
	Buildable:
		Prerequisites: ~disabled # ~player.america, ~techlevel.infonly
```

```yaml
E1.russia:                                          # infantry-russia.yaml:2-5
	Inherits@BaseUnit: ^E1
	Buildable:
		Prerequisites: ~disabled # ~player.russia, ~techlevel.infonly
```

Re-enabling is literally uncommenting, matching the shape every other line-infantry actor uses
(e.g. `E3.america`, `infantry-america.yaml:19-21`). I could not establish *why* it was disabled — there is
no comment giving a reason, and I did not trace the commit that did it.

The AI already expects it: `ai-america.yaml:11` / `:67` and `ai-russia.yaml:10` / `:64` both carry a
`# Conscripts -- low priority, mainly for capturing` comment against their composition blocks.

### Assets

**Intact, and shared the same way every other infantry class shares them.**

- Sprite: `mods/ww3mod/bits/units/infantry/e1.shp`. There is **no** `e1.america.shp` / `e1.russia.shp`, and
  there does not need to be — `sequences-infantry.yaml` defines `^e1` (`:132-209`) with every sequence
  pointing at the `e1` image, then `e1` (`:210`), `e1.america` (`:213`) and `e1.russia` (`:216`) each
  `Inherits: ^e1` and differ only in `icon:`. This is the same pattern as `e3` (one `e3.shp` for both
  factions).
- Full animation set: `stand`, `stand2`, `run`, `shoot`, `prone-stand`, `prone-stand2`, `prone-run`,
  `liedown`, `standup`, `prone-shoot`, `parachute`, `idle1`, `idle2`, `die1`–`die7`, `garrison-muzzle`
  (`sequences-infantry.yaml:132-209`).
- Icons: `bits/misc/icons/e1americaicon.shp`, `e1russiaicon.shp`. Class pip: `bits/units/classes/e1_class.shp`.
- It is the *reference* infantry sprite for the rest of the mod: the rot/corpse sequences use e1 frames
  (`sequences-infantry.yaml:18-46`) and `crew.yaml:18` borrows "the e1 (conscript) firing sequence".
- **Voices: I could not establish.** `grep -rn "e1" mods/ww3mod/audio/*.yaml` returned nothing, but I did
  not audit how `^Soldier` assigns `Voice`/`VoiceSet`, so this may simply be inherited. Treat as unverified,
  not as "missing".

---

## 7. The order layer

### Every order string in `OpenRA.Mods.Common/Traits/`

`ActivateCondition`, `AfterDeployTransform`, `Attack`, `AttackMove`, `AttackSupplyRoute`, `BeginMinefield`,
`CaptureActor`, `Deliver`, `DeliverSupply`, `DeliverUnit`, `DeployTransform`, `DevLevelUp`, `Dock`,
`DropCrate`, `DropSupplyCache`, `EngineerRepair`, `Enter`, `EnterTransport`, `EnterTunnel`, `Evacuate`,
`ForceDock`, `ForceMove`, `GrantConditionOnDeploy`, `Guard`, `Harvest`, `InstantRepair`, `Move`,
`PickupUnit`, `PlaceMine`, `PlaceMinefield`, `PowerOutage`, `Repair`, `RepairBridge`, `RepairBuilding`,
`RepairNear`, `Restock`, `Resupply`, `ReturnToBase`, `Rotation`, `Scatter`, `Sell`, `SetCohesion`,
`SetEngagementStance`, `SetRallyPoint`, `SetResupplyBehavior`, `SetUnitStance`, `Stop`, `Surrender`,
`Unload`, `UnloadCargoPassenger`.

### Is there a player-issued transfer-between-two-actors order?

**Yes — two of them, both on TRUK, both one-directional and both hard-gated to the LC.**

1. **`Restock`** (`DropsSupplyCache.cs:135-151`, targeter `:313-344`, priority 7). Right-click a friendly
   LC. `CanTargetActor` requires: allied (`:320`), target has `SupplyProvider` **with a non-empty
   `DockedCondition`** (`:324-326` — the LC-only filter), self has `SupplyProvider` (`:328-330`), and self
   is either not full or damaged (`:332-336`). Resolution drives to the host, `Wait(25)`, then pulls
   `min(headroom, host.CurrentSupply)` via `DeductSupply`/`AddSupply` (`:178-198`).

2. **`DeliverSupply`** (`:153-175`, targeter `:346-372`, priority 6). **Ctrl+click** (`ForceMove`, `:355`)
   a friendly actor with `AbsorbsSupplyCache` (`:361`) while holding supply (`:364-365`). Drives adjacent,
   drops the crate on its own cell, and lets the LC's absorber pull it in (`:170-174`).

**This is the closest existing analogue to "right-click target to give", and it is genuinely close.** The
priority ladder (`Restock` 7 > `DeliverSupply` 6 > `DropSupplyCache` 5) already demonstrates the exact
idiom a supply-transfer order needs: several supply orders on one actor, disambiguated by priority and by a
modifier key, each with its own `UnitOrderTargeter` predicate.

**What is missing is only the target filter.** Both targeters reject a non-LC provider by design:
`Restock` on the `DockedCondition` test (`:325-326`), `DeliverSupply` on the `AbsorbsSupplyCache` test
(`:361`). Truck→truck and crate→truck are excluded by those two lines, not by anything structural. There is
no reservation system, no transfer activity abstraction, and no notion of a transfer *rate* between
providers — `Restock` is `Wait(25)` then an instantaneous whole-amount move.

### The `Passenger` / `Cargo` pattern, for reference

`Passenger` yields exactly one order (`Passenger.cs:83-95`): an `EnterAlliedActorTargeter<CargoInfo>` named
`EnterTransport` at priority 5, with a cursor pair and two predicates — `IsCorrectCargoType` (`:105-121`,
checks `LoadingBlocked` and `CargoInfo.Types` against the passenger's `CargoType`) and `CanEnter`
(`:123-131`, checks `LoadingBlocked` and `HasSpace(Weight)`). `IssueOrder` (`:97-103`) is a bare
`new Order(order.OrderID, self, target, queued)`. `requireForceMove` (`:107`) gates it behind Ctrl.

`EnterAlliedActorTargeter<T>` is the reusable base for "right-click a friendly actor that has trait T" —
that is the class a supply-transfer order would most naturally use, parameterised on `SupplyProviderInfo`.
Note the asymmetry that matters for the design: `Cargo`'s host-side veto runs through
`ICargoCanLoadFilter` (`Cargo.cs:116/189`), an **interface the host implements** — and `SupplyProvider`
already implements it (`SupplyProvider.cs:852-855`). There is no equivalent "can this provider accept a
donation" interface today.

---

## 8. Feasibility verdicts

| # | The ask | Verdict | Why |
|---|---|---|---|
| 1 | **Units carry supplies, more than they do now** | **Already expressible — YAML only** | `SupplyProvider` has no `Requires<>` (`SupplyProvider.cs:23`); add it to `^Soldier`/E1 with `TotalSupply`, `SupplyCreditValue`, `RearmCondition: replenish-soldiers` and leave `RestockActors` empty. Cost accounting is free: `SupplyCreditValue` → `MissingSupplyValue` (`:959-969`) → `CustomSellValue` (`CustomSellValue.cs:48-51`) already deducts unspent supply on evac/sell, exactly like TRUK's 1000/750. **Watch:** the permanent amber selection bar (`:846`), the `ICargoCanLoadFilter` trap if the actor ever gains `Cargo` (`:852`), and the O(n²) provider scan in §5. |
| 2a | **Conscript reintroduced at ~100** | **Small extension** | Uncomment the prerequisite in three files (`infantry.yaml:1108`, `infantry-america.yaml:5`, `infantry-russia.yaml:5`); the original value is preserved in the comment. Raise `Cost: 50` → 100 (`:1111`). Art, sequences, icons, class pip, veteran variants, AI composition comments all intact (§6). Balance re-derivation per `economy.md`'s tier table is the only real work. |
| 2b | **…of which ~50 is supplies** | **Already expressible** | This is precisely what `SupplyCreditValue` means. `Cost: 100` + `SupplyProvider: TotalSupply: 50, SupplyCreditValue: 50` gives: full conscript refunds 100, drained conscript refunds 50. One YAML block, zero engine change. |
| 2c | **…can resupply nearby units** | **Already exists** (as a consequence of 1) | A soldier carrying `SupplyProvider` with `RearmCondition: replenish-soldiers` pushes to every soldier in its aura via the same `Tick`→`FindGreatestNeedTarget`→`ResupplyTarget` path a truck uses. `RearmActors` is irrelevant to the push (`DISCOVERIES.md:624`); no unit needs to list the conscript. |
| 2d | **…can resupply itself** | **New machinery — but small** | `IsValidTarget` rejects `a == self` (`:463`). Two routes: (i) *free* — a self-granting `ProximityExternalCondition` with `AffectsParent: true` feeding the existing `ReloadAmmoPool`, which is a YAML-only change but **deducts no supply**, joining the unpriced channel of §1; (ii) *priced* — a self-serve branch in `ResupplyTarget` that runs the same batch arithmetic against own pools. (ii) is maybe 30 lines and is the honest one. |
| 3a | **Multiple providers serve one demand** | **Already works** | No reservation, no lock, no already-served test anywhere in `IsValidTarget` (`:461-500`). Providers each hold one `currentTarget` (`:128`) but nothing stops two of them holding the *same* one; `ExternalCondition` grants are keyed by source with uncapped `SourceCap`/`TotalCap` (`ExternalCondition.cs:63`, `:88-104`); `GiveAmmo` clamps harmlessly (`AmmoPool.cs:141-152`). |
| 3b | **…combining/splitting cost so both end equal** | **New machinery — the most expensive item here** | A batch is atomic and single-provider-affordable by construction: `:682` skips unaffordable pools, `:700` re-tests before delivery, and `AutoSeekSupplies.CanServe:212-214` applies the *same* gate on the pull side. Needs a genuine multi-party transaction: discovery of co-providers, an agreed split, proportional `DeductSupply` on each, then one `GiveAmmo`. Nothing in the trait is shaped for it — every decision is made inside one provider's own `Tick` with no cross-provider visibility. **Both gates must change together**, or a dry AT specialist keeps walking past every conscript on the map and finding no usable provider. Expect a new pure math helper + NUnit pin, in the house style of `ResidueVerdict` (`:944`) / `SupplyLogisticsMath`. |
| 4a | **Deploy a truck to drop an ammo crate** | **Already exists, complete** | `DropSupplyCache` deploy order + `IIssueDeployOrder` (`DropsSupplyCache.cs:280`, `:296-304`), merge-into-existing (`:94-106`), real actor with a real per-instance quantity (`:118-123`, `SupplyProvider.cs:159`), sprite tiers, capture, LC absorb, and a `DROP_SUPPLY` UI button (`CargoPanelLogic.cs:192-203`). Nothing to build. |
| 4b | **Right-click a crate to load it back up** | **Small extension** | The `Restock` order already does drive→wait→`min(headroom, host)` transfer (`:178-198`). It is excluded from crates by **one predicate line** — `string.IsNullOrEmpty(hostProvider.Info.DockedCondition)` at `:325-326`, mirrored at `:263`. Relaxing it (plus deciding the arrival gate, since a crate has no `unit.docked` proximity trigger) is the bulk of the work. Note `DropsCrate.cs` is a dead near-duplicate with no YAML reference — delete rather than extend. |
| 4c | **One truck shares with another** | **Small extension**, same lever as 4b | Identical shape: an `EnterAlliedActorTargeter<SupplyProviderInfo>`-style targeter (the `Passenger` pattern, `Passenger.cs:83-95`) reusing `QueueDriveAndRestock`'s arithmetic. The priority ladder for stacking supply orders on one actor already exists (`Restock` 7 / `DeliverSupply` 6 / `DropSupplyCache` 5, `:274-283`), including a Ctrl-modifier variant (`:355`). Main open question is direction semantics: `Restock` is *pull-from-target*, `DeliverSupply` is *push-to-target*; a truck-to-truck order must pick one and say so in the cursor. |
| 4d | **Right-click supply transfer on *any* actor that has supply** (the general form) | **Small extension if 4b/4c land** | Once the `DockedCondition` filter is replaced by "target has `SupplyProvider` and headroom", the order generalises for free — including conscript→conscript, once ask 1 lands. There is no per-actor whitelist anywhere in the transfer path. The only genuinely absent piece is a host-side veto interface analogous to `ICargoCanLoadFilter` ("may this provider accept a donation"); today nothing asks. |

### The one thing to settle before designing

**The unpriced free-trickle channel (§1).** `ReloadAmmoPool` hands out ammo at zero supply cost whenever
`replenish-soldiers` is held (`ReloadAmmoPool.cs:91`), and the LC grants that condition to every ally
within 4 cells by bare proximity, with no reference to its own pool
(`structures.yaml:382-386`). A design that makes carried supply a meaningful fraction of unit cost is
competing against a free source. I have not established whether this is intentional (an
"in-base-you-rearm-free" affordance) or drift — but it should be a deliberate decision, not a discovery
made after the numbers are tuned.

### Things I could not establish

- **Why E1 was disabled.** No comment states a reason and I did not trace the commit.
- **Whether the conscript has voice lines.** `mods/ww3mod/audio/*.yaml` has no `e1` match, but I did not
  audit `^Soldier`'s voice inheritance, so this is unverified rather than negative.
- **Whether the free LC proximity trickle is intentional.** Stated as a mechanism above; the intent is not
  recorded anywhere I found.
- **Anything about the in-flight `auto/supply-dwell` work.** All bot-module claims are scoped to `f2a72e43`.
