# WW3MOD Economy & Supply System

This doc is the source of truth for how money, ammo, and supply move through a match. It's written from a gameplay perspective with technical detail where it matters.

If anything here disagrees with code, the doc is right and the code needs to change — file a fix, don't quietly drift.

> **Related:** [`supply-route.md`](supply-route.md) covers the Supply Route (the sector beachhead — fixed at spawn, not a factory). This doc is about the cash/ammo/supply pipeline; that doc is about the building those flow through.

## Where cash comes from

**Income is a per-tick allocation stream, not a starting balance** — `PassiveIncome` 100 every `PassiveIncomeInterval` 50 ticks after a `PassiveIncomeInitialDelay` of 50 (`PlayerResources.cs:63-69`), entirely independent of `DefaultCash` (`:32`, 20000; the mod leaves it at the engine default — `player.yaml:167` has it commented out). There is no harvesting and no resource patch: this stream and evacuation refunds are the whole of a player's income.

**One gate decides whether a player has an economy at all, in BOTH directions.** `PlayerResources.Tick` pays passive income, pays building income and charges upkeep on a single line (`:209`), behind `if (self.Owner.Playable || (self.Owner.IsBot && !self.Owner.NonCombatant))` (`:208`). A player failing it is not *poor* — it is disconnected from the economy entirely, earning nothing and paying no upkeep. **`Playable` means "occupies a lobby slot", not "is a real participant"**: `PlayerReference.Playable` defaults to `false` (`PlayerReference.cs:24`) and map players copy it verbatim, while a lobby-slot player keeps the `true` field initialiser. The `IsBot` disjunct exists because a **map-player bot is not `Playable`** — which is how every `tournament-*` scenario declares its bots. *(There is a PITFALL at the site. Consequence for anyone reading old numbers: before that disjunct was added, every bot-vs-bot tournament ran as a no-income match for **both** profiles — `earned=0` for the whole match while a do-nothing Observer accrued normally. **Any benchmark artefact predating the fix measured bots that could not buy anything**, and cannot be compared against a current run.)*

**Consequence for scenario authors, and it has burned tests: `DefaultCash: 0` does NOT freeze a force.** Several scenarios carry a comment claiming "no cash anywhere, so nothing is produced — the only actors are the pre-placed ones", and it is false: a bot observed at `cash=0` on tick 40 had 1437 by tick 750 and had bought two units in between. **Any scenario relying on "the placed force is the whole force" for attribution is relying on something untrue — count named actors instead**, since named actors keep their identity while the population around them grows.

## Core principles

1. **Every unit, every magazine, every supply box has a cost.** Cash spent buys ammo + body together. Selling or evacuating refunds what's left.
2. **A unit of supply is worth a fixed amount of cash** wherever it sits — in an LC, a truck, a cache. When the player gets it back on evac, capture, or absorb, it returns at face value.
3. **Supply is finite.** A Logistics Center spawns with a fixed pool. Trucks carry a fixed amount. Drained pools stay drained until the player brings in more supply. **An LC is obtained by deploying an `LCCV`** (`vehicles.yaml`, Cost 1200, `Prerequisites: ~techlevel.low`, `Transforms: IntoActor: logisticscenter`), or by capturing one of the Neutral pre-placed LCs on the three maps that have any. **It is NOT obtained from the build sidebar** — `logisticscenter` carries `Buildable.Prerequisites: ~disabled` (`structures.yaml:367`), so its icon never appears. *(**Corrected 2026-08-17.** This principle previously read "LCs are **not** buildable … the only ones in a match are the Neutral pre-placed ones you can capture", inferring unobtainability from `~disabled`. That inference is wrong: `Transforms.CanDeploy` (`Transforms.cs:93-99`) tests only trait-disabled state and cell placement and **never consults `Buildable.Prerequisites`**, so the deploy route is wide open. The error funded a money pump — sell value 3500 for a 1200 LCCV — fixed in the same commit. Downstream reasoning that cited the old claim: `WORKSPACE/audit/260816-bug-reconciliation.md:259`, `WORKSPACE/DISCOVERIES.md` 2026-08-16. **General lesson: `~disabled` gates the sidebar icon, not the actor's existence** — enumerate every route to an actor, not just the build queue.)*
   **The mirror image: `~techlevel.*` is decorative, not a gate.** `MapOptions.TechLevel` defaults to `"unrestricted"` (`MapOptions.cs:52`), `world.yaml:435` sets `TechLevelDropdownVisible: false` so the lobby never offers a lower one, and no map or autotest scenario overrides it (checked repo-wide at `de78a1ed`). `ProvidesTechPrerequisite@unrestricted` (`player.yaml:222-225`) grants `techlevel.infonly`, `.low`, `.medium`, `.high` and `.unrestricted` in one block, and those five are the only tiers any **live** rule asks for. So an actor whose only prerequisite is `~techlevel.<tier>` is unconditionally available — the LCCV's `~techlevel.low` above restricts nothing. Two guards, because this is a dated observation and not an invariant: a map that set `TechLevel` would re-arm every one of them; and `~techlevel.futuristic` is *asked for* by three actors while **nothing provides it**, which would make it behave exactly like `~disabled` — harmless today only because all three of those blocks are commented out (`vehicles.yaml:722`, `vehicles-america.yaml:1155`, `vehicles-russia.yaml:1064`). Uncommenting one silently yields an actor with no route into play.
4. **Trucks, LCs, and dropped supply caches share one trait** (`SupplyProvider`). Players see the same UI (range circle, supply bar) everywhere.
5. **`ReloadCount` is the canonical batch size for rearm.** Whether a Bradley docks at the LC or a soldier waits next to a truck, the per-pool `ReloadCount` decides how many rounds arrive per cycle and `SupplyValue` decides what one cycle costs. (Aircraft reach a host only where an LC has been captured — see "What rearms what".)

## What rearms what

| Unit class | Rearms at |
|---|---|
| Infantry | TRUK (supply truck), SUPPLYCACHE (dropped box), Logistics Center |
| Ground vehicles | Logistics Center only — **except `m270`, `grad` and `tos`, which rearm nowhere.** Those three carry no `Rearmable` trait at all, so there is no host to pull from, and the LC push cannot reach them either (it is gated on the recipient declaring `replenish-vehicles`, which only `himars` and `iskander` do). Once spent they are finished as combatants; evacuating for a refund is the whole plan for them — see "Evacuate-when-dry is opt-in per actor" below. |
| Aircraft | Logistics Center only, and only a **captured** one — it is the sole host they can reach. See "Aircraft rearm…" below. |
| Static defenses (CRAM, AGUN) | Self-reload via `ReloadAmmoPool` (no external supply consumed) |

Vehicles are budgeted around dock-at-LC logistics. Adding `truk` to `Rearmable.RearmActors` on a vehicle is a balance change.

**Aircraft rearm ONLY at a captured Logistics Center, and on seven of the ten maps that means not at all.** The airframes previously named `hpad`/`afld`, which carry `Buildable.Prerequisites: ~disabled` with **nothing in the repo providing `disabled`** and are pre-placed on none of the ten maps — so aircraft ammunition and health were one-way everywhere. They now name `logisticscenter` (`aircraft.yaml:79-80`, `:162-163` for the two `Repairable` templates; the seven `Rearmable` sites in `aircraft-america.yaml`/`aircraft-russia.yaml`), which has `RepairsUnits`, `SupplyProvider`, and — added for this — `Reservable`. **An LC enters a match only as one of the Neutral pre-placed capturables on `polar-disorder`, `river-zeta` and `woodland-warfare`**, so on the other seven maps ammunition and health remain one-way, and even on those three they are one-way until the LC is taken.

**`Reservable` is the load-bearing part of that wiring, and the reason a bare `RearmActors` swap is wrong.** An aircraft's only route to any host is `ReturnToBase`, whose `ChooseResupplier` filters `ActorsHavingTrait<Reservable>()` (`ReturnToBase.cs:45-50`). The ground route — `AmmoPool.AutoRearm` → `Resupply` — is closed to aircraft at every entrance: `AutoRearmIfAllEmpty`, `AutoRearmIfAnyNotFull` and `PoiOffensiveBotModule.IsOutOfAmmoSweepCandidate` all refuse `AircraftInfo`, and `Resupply`'s approach block is guarded on `aircraft == null` so it would never fly one in. But `AmmoPool.ChooseResupplier` matches on `RearmActors` membership plus a `SupplyProvider`/`RearmsUnits` trait and **never asks whether the caller can reach the host**, so naming the LC without `Reservable` would report aircraft as hosted while leaving them unable to arrive — flipping their readiness gates to the strict restore-first bars with nothing able to satisfy them. `AirframeReadiness.CountsAsRearmHost` refuses the dock term for aircraft to hold that line from the other side.

A landed aircraft also trickle-refills independently of all this: `ReloadAmmoPool@1/@2` is gated `unit.docked && !airborne` (e.g. `aircraft-america.yaml:175`, `:205`) and a captured LC grants `unit.docked` within 2c0 (`structures.yaml:400-404`). Before the rearm wiring above, nothing ever sent an aircraft there; `ReturnToBase` now does.

Consequences worth knowing before touching aircraft logic: where no LC has been captured, `ReturnToBase` still resolves no resupplier and degrades to `FlyIdle`-then-finish (`ReturnToBase.cs:127-128`), and any gate of the form "wait until healthy / wait until full" is **unsatisfiable, not merely pessimistic**. The bot readiness gates therefore ask whether a host actually exists rather than whether one is named (`AirframeReadiness`), and `Aircraft` refuses a `ReturnToBase` order when none does, so a no-op return cannot cancel a live attack.

**A truck can never rearm a vehicle anywhere — the split is structural, not a tuning choice.** The provider PUSH is gated on the recipient declaring the provider's `RearmCondition` as an `ExternalCondition`; TRUK (`vehicles.yaml:546`) and SUPPLYCACHE (`misc.yaml:412`) both name `replenish-soldiers`, which is declared **only** on `^Soldier` (`infantry.yaml:214-215`). The only provider naming `replenish-vehicles` is the static `logisticscenter` (`structures.yaml:394`), additionally `DockedCondition: unit.docked` — so a vehicle can only ever be served standing at a fixed building. Two corollaries that are easy to get wrong: (1) even at the LC the *push* reaches only the two vehicles that declare the receiving condition (`himars`, `vehicles-america.yaml:1080`; `iskander`, `vehicles-russia.yaml:980`) — every other vehicle rearms through the `Rearmable`/`Resupply` **pull**, not `SupplyProvider`; (2) no `RearmActors` list anywhere names `supplycache`, so a cache can never be *walked to* — it is push-only, infantry-only. Any "supply truck follows the army" reasoning that counts vehicle ammo as demand is counting demand the truck cannot relieve (`SupplyFollowerBotModule`'s cluster `AmmoNeed` sums every `AmmoPool` in the cluster, vehicles included — `:523-531` — so a pure-armour cluster still attracts a truck that can do nothing for it).

**The aura `Range` once gated SELECTION only, never DELIVERY.** All three `Info.Range` comparisons were dimensionally correct; the defect was a *missing* comparison — `ResupplyTarget()` called `GiveAmmo` with no range check, so a target that walked out of the aura during the `RearmDelay` wait (or one picked by the unbounded Hunt scan below) was served anyway, in one case clean across the map. Fixed by extracting `SupplyProvider.InAuraRange(WPos, WPos, WDist)` (`:927-930`) — a **squared** horizontal comparison matching `WorldUtils.FindActorsInCircle`'s own `HorizontalLengthSquared <= r.LengthSquared` (`WorldUtils.cs:84`) rather than the floor()'d `WVec.HorizontalLength` (`Exts.ISqrt` defaults to `ISqrtRoundMode.Floor`, `Exts.cs:306`), so selection and delivery agree exactly on the boundary — applied at `IsValidTarget` (`:470`), `SetTarget` (`:516`), `SyncTargetCondition` (`:552`) and the new delivery gate (`:664-675`). The gate **keeps** the target and re-arms `rearmTicks` rather than dropping it, so an approaching provider still serves on arrival instead of thrashing its target pick. Sheltered garrison passengers stay exempt (they are `!IsInWorld` with a stale `CenterPosition`, and their building was in range when picked). The general lesson: a proximity aura's range must be re-checked at the moment of effect, because the wait between selection and delivery is where the geometry changes.

**There is an UNBOUNDED whole-map provider hunt in the engine, one stance away from live.** `SupplyProvider.UpdateTarget` falls through to `FindNeedsResupplyTarget` when `AutoTarget.EngagementStanceValue >= EngagementStance.Hunt` (`:302-304`), and that helper scans `world.ActorsHavingTrait<AmmoPool>()` with **no range term and no leash** (`:356-364`); `SetTarget` then drives the provider to it (`:513-525`). It is dormant for TRUK by default — TRUK's `AutoTarget` block overrides only `InitialResupplyBehavior*` (`vehicles.yaml:514-516`), so it ships `Defensive`, the engine default for both the human and AI fields (`AutoTarget.cs:160/163`), and TRUK inherits `^AutoTarget` rather than the `^Combatant` chain that some shipped maps flip to Hunt. But Hunt is both player- and AI-settable, and `UnitDefaultsManager` persists a human's per-type stance across games — so "supply trucks don't wander" is a default, not an invariant. Know this before anyone "enables truck hunting" by flipping a stance.

**Provider rearm is a PUSH gated on the RECIPIENT's condition, not on `RearmActors`.** `SupplyProvider.Tick` scans `FindActorsInCircle`, picks the greatest-need friendly `Rearmable` in range, and calls `GiveAmmo` on it directly (`SupplyProvider.cs:225/:308/:546`) — it **never consults the recipient's `Rearmable.RearmActors`**. What gates the push is `IsValidTarget` (`:403-440`): the recipient must be a friendly `Rearmable` with a non-full pool AND (when the provider sets one) carry the provider's `RearmCondition` external condition — default `replenish-soldiers` (`:59`), which only infantry hold. That is why a truck/cache tops up nearby infantry but skips vehicles, with no driving involved. `RearmActors` gates the *other* path — the recipient-initiated drive-to-a-host PULL (`AmmoPool.ChooseResupplier`, `:340`) — so adding `truk` to a vehicle's `RearmActors` lets that vehicle dock at a truck but does not make the truck's push serve it.

**An `AmmoPool` never refills itself on the battlefield — passive trickle is opt-in via `ReloadAmmoPool`.** `AmmoPool` is not `ITick` (`AmmoPool.cs:111` implements only `INotifyCreated, INotifyAttack, INotifyBecomingIdle, IResolveOrder, ISync`), and its self-reload method `AmmoPool.Reload()` (`AmmoPool.cs:361`) has **zero callers** engine-wide — so the `RemainingTicks`/`FullReloadTicks`/`FullReloadSteps` countdown it decrements (`:366`) never advances. The `ReloadDelay` field only *seeds* that inert countdown (`:237`) and is re-seeded on a dock rearm (`Rearmable.cs:52,66`). Actual in-field trickle exists **only** on the separate `ReloadAmmoPool` trait (`ReloadAmmoPool.cs:46`, which *is* `ITick` and calls `ammoPool.GiveAmmo(self, Count)` every `Delay` ticks, `:91`). A unit that does not carry `ReloadAmmoPool` therefore cannot top up in place — it must retreat and rearm at a provider (LC / TRUK / cache via `Rearmable` + `Resupply`). `ReloadAmmoPool` appears in only 7 mod YAML files (mostly static defenses); most units, including tunguska AA, lack it. **Do not read `AmmoPool.ReloadDelay` as "seconds to self-reload in the field" — it drives nothing without `ReloadAmmoPool`.**

**"A resupplier exists" is the engine's whole reachability test.** `AmmoPool.ChooseResupplier` ends in `ClosestToIgnoringPath` (`AmmoPool.cs:343-344`) and filters only on ownership, `RearmActors` membership and `CurrentSupply > 0` (`:331-341`) — no path check, and no `IsInWorld` check either. So a depot across an unfordable river reads as reachable, and any consumer that wants real reachability must supply its own proxy (and should document it as a proxy). When `ChooseResupplier` returns null, `AutoRearm` just sets `NeedsResupply = true` on every pool and returns (`:313-320`, with an in-code note that "Evacuation only happens when `ResupplyBehavior` is explicitly set to `Evacuate`") — the flag has only two readers, the Hunt-stance provider scan (`SupplyProvider.cs:361`) and `UnitBuilderBotModule.AnyFieldedUnitNeedsResupply` (`:487`, a production gate), neither of which disposes of the unit. So nothing at the unit level *removes* a dry unit with no reachable source — that judgement belongs at the *sector* level, not in a unit trait. What such a unit no longer does is keep fighting: the attack activities test `AmmoPool.CannotFight` and end, so it drops the attack order and falls idle rather than standing in range aiming a weapon it cannot fire, and going idle re-enters the `INotifyBecomingIdle` dispatch above — so it retries resupply once per cycle instead of asking exactly once and never again. Note also that `AutoRearmIfAllEmpty` requires **every** pool empty (`ammoPools.All(a => !a.HasAmmo)`, `:170-173`) — a unit dry on its main gun but holding a loaded secondary never enters the path at all.

**Evacuate-when-dry is opt-in per actor, and it is not just trucks.** Mind the names: `ResupplyBehavior` is the **enum type** (`AutoTarget.cs:28`, values `Hold`/`Auto`/`Evacuate`) — there is no `ResupplyBehavior:` YAML key. The two settable fields are **`InitialResupplyBehavior`** (human-owned) and **`InitialResupplyBehaviorAI`** (bot- or non-playable-owned), chosen at `AutoTarget.cs:473`, and an actor that sets only one keeps the default on the other. The `^AutoTarget` default is `Auto` for both (`defaults.yaml:322-323`); exactly four actors override to `Evacuate`, all setting both keys — TRUK (`vehicles.yaml:529-530`) plus the rocket artillery **m270** (`vehicles-america.yaml:704-705`), **grad** (`vehicles-russia.yaml:529-530`) and **tos** (`vehicles-russia.yaml:654-655`). A spent Grad rotating to the map edge is designed behaviour, not a bug.

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

`Valued.Cost` 3500, but **fielded by deploying a 1200-cost `LCCV`** (see Core principle 3). Spawns with `SupplyProvider.TotalSupply: 3000`. The pool drains as:
- Vehicles dock and rearm directly (`SupplyValue × batches given`).
- Trucks drive in to restock (truck pulls supply from LC; LC drops by exactly the amount taken).

When the LC's pool hits zero it stops servicing rearm requests. The player deploys another LCCV, or relies on trucks that still have supply.

**Salvage is capped at the LCCV's cost, and that cap is load-bearing.** `Sellable.RefundPercent: 34` on `logisticscenter` puts the sell refund at 1190 — just under the 1200 it costs to field one — and `SpawnActorsOnSell.ValuePercent: 0` stops the sale additionally emitting technicians. Without both, deploy-and-sell paid 3500 in cash plus up to five 250-credit technicians for a 1200 outlay, repeatable. `RefundPercent` rather than `CustomSellValue` deliberately: the LC is capturable (via `^BasicBuilding` → `^NeutralOrOccupiedCapturable`) and bots rank capture targets by `GetSellValue` (`CaptureManagerBotModule.cs:147`), so its strategic valuation must stay at 3500 while only its scrap value moves. **If either Cost changes, recompute: `RefundPercent ≤ 100 × LCCV.Cost / logisticscenter.Cost`.**

### Supply Truck (TRUK)

Cost 1000. Spawns with `SupplyProvider.TotalSupply: 750`.

Truck behavior:
- Drives near friendly **infantry** that need rearm. Delivers `ReloadCount` rounds per cycle, charges `SupplyValue` per batch from its own pool.
- Serves units whose `Rearmable.RearmActors` lists `truk` (infantry).
- **HALTS while it has a customer, and resumes the order it already had.** `SupplyProvider.ServingCondition` (`supply-serving`, set only on TRUK) is granted whenever the last aura scan found a unit that could be handed a batch right now, and TRUK's `Mobile.PauseOnCondition` names it — so a truck under a move order stops for the units it would otherwise drive past, and moves on the moment the last of them is topped up. Pausing `Mobile` rather than cancelling is what makes resuming free: `Move.Tick` returns false while paused (`Move.cs:168`) and the activity is left standing. **The escape hatch is the `HoldPosition` engagement stance**, which switches the halt off — that value was chosen because it is the only one on a live stance axis with no other truck-side reader, and because it already means "does what I told it" elsewhere (`ControlAllUnitsManager.cs:56-59`). `ResupplyBehavior` could not carry this: all three of its values are already live on TRUK. Two consequences worth knowing: a halted truck will not obey a fresh move order until its customers are full or you switch it to `HoldPosition`; and the halt terminates by construction, because serving is what empties the set that holds it.
- When low (`currentSupply < RestockThreshold`, 50) an **Auto**-stance truck drives back to nearest LC (`RestockActors: logisticscenter`) and refills. But TRUK's default resupply stance is **Evacuate** for both human and AI (`vehicles.yaml:514-515`), so a low truck normally rotates to the map edge to return its credit rather than shuttling — see the residue-evac rule below.
- Refill drains the LC's `currentSupply` by the amount taken. A truck that needs 600 supply takes 600 from the LC, leaving the LC with 2400. If the LC has less than the truck wants, the truck takes what's there and leaves partially full.
- Can drop its remaining supply as a SUPPLYCACHE box — by the player's deploy command, or, for a bot-owned truck, as the **dangerous-mode delivery**. The drop is all-or-nothing either way (`DropSupplyCacheHere` → `SetSupply(0)`): there is no partial unload.
- **A bot truck's delivery MODE is chosen by believed danger; danger never decides whether to go.** Quiet front → close to aura range, serve in place, **keep** the remainder for the next customer. Under fire → stop short of the platoon, unload everything, egress. The classifier, the commitment invariants, and why infantry walking to a placed crate is correct behaviour are in [`supply-route.md`](supply-route.md) §"Forward delivery" — not restated here.
- **A COMMITTED SUPPLY ERRAND is never interrupted, by the halt above or by the dry break-off below.** `SupplyProvider.OnSupplyErrand` walks the activity queue for the three named errand types — `RestockSupply` (drive to a host and refill), `PlaceSupplyCache` (drive to a cell and unload), `CollectSupplyCache` (drive to a crate and load it) — and both mechanisms stand down while one is running. Each errand is a *named type* precisely so this question can be asked at all: a bare `MoveTo` is indistinguishable from a player's move order, which is why the distinction did not exist before. The failure this prevents is not theoretical — a truck that halts to serve the platoon it was sent to unload *near* never reaches the drop cell, never places a crate, and lingers in the danger the errand was routing it out of, while the platoon still ends up resupplied so the outcome looks fine.
- **Runs dry under orders → breaks off.** `DropsSupplyCache` re-checks `CountsAsEmpty` every `DryMoveScanInterval` (25) ticks while the truck is *not* idle, and cancels the current activity so the shipped `INotifyBecomingIdle` return/evacuation can run. Needed because that return is idle-triggered and a truck driving somewhere is never idle, so an empty truck used to complete the whole drive first — the same blind spot `AutoSeekSupplies.ReturnWhenEmpty` closes for infantry. **The rule is "cancel a move that is invalidated by being empty; never cancel a move that exists to stop being empty."** Exempt: any committed supply errand (above), a truck already evacuating (`evacuating` condition — that *is* the disposition being aimed for), and `ResupplyBehavior: Hold`, where `EvacuateOrRestock` returns immediately so a cancel would destroy the standing order and put nothing in its place.

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
- Sits in place until drained, captured, or destroyed. The player recovers a cache's remaining supply by absorbing it into a friendly LC (the LC's `AbsorbsSupplyCache` trait pulls in any nearby cache), by **right-clicking it with a supply truck** (the `PickupSupply` order — the truck drives over and loads whatever fits inside its own headroom), or by spending it through infantry rearming off it. Both routes are available on every map: the truck needs no host, and the LC route needs an LC, which any player can field by deploying an `LCCV` (Core principle 3) as well as by capturing one of the three pre-placed Neutral ones. *(**Corrected 2026-08-17.** This bullet previously claimed the LC route was "mostly unavailable … on the other seven a truck is the *only* way supply put on the ground ever comes back", reasoning from the same `~disabled` mistake corrected at Core principle 3.)*
- **Truck collection is an ORDER, never an aura — and that asymmetry with the LC is deliberate.** `DropSupplyCacheHere` places the crate on the truck's *own* cell, so a proximity absorber on a truck would re-swallow the load it had just dropped; it would also eat forward dumps the player placed on purpose for infantry to walk to. Both failures are silent and both undo a deliberate act, so collection is asked for, exactly like the drop it mirrors. The transfer is capped at the truck's headroom: a crate holding more than the truck can take is partially emptied and stays put with the remainder.

### Cash flow recap

| Action | Cash effect |
|---|---|
| Call in unit (any) | `−Cost` (cash drops by full unit cost; ammo is bundled in) |
| Unit destroyed in combat | Permanent loss of `Cost` |
| Unit rotated to map edge with full ammo | `+Cost × HP/MaxHP` |
| Unit rotated to map edge with empty ammo | `+(Cost − sum_pools(missing_batches × SupplyValue)) × HP/MaxHP` |
| Sell building with supply (LC) | `+max(0, Cost − missing_supply_value) × RefundPercent/100 × HP/MaxHP` — supply refunds at constant rate; the LC sets `RefundPercent: 34` (see "Logistics Center") |
| Truck drops cache, drains in field | Spent supply is gone; remaining supply still recoverable via absorb/capture |
| Capture an enemy SUPPLYCACHE | Free supply at full value (war booty) |
| LC absorbs nearby friendly SUPPLYCACHE | Supply transfers from cache to LC at full value |

Sell formula. `GetSellValue` is the single path for the **salvage value**; the **cash paid** then scales it by health and, on the evacuation paths, by the owner's handicap:

```
sellValue = max(0, Cost
                 − sum_pools(floor(missing_rounds / ReloadCount) × SupplyValue)
                 − missing_supply_value)     // for actors with a SupplyProvider
                                             // (CustomSellValue.cs:36-51)

refund    = sellValue × RefundPercent/100 × HP/MaxHP        // Sell.cs:41, RotateToEdge.cs:381
evacRefund = handicapAdjust(sellValue) × HP/MaxHP           // RotateToEdge.cs:377
             where handicapAdjust(v) = v × 100/(100−handicap)
```

**The `HP/MaxHP` term is not optional and it is not decoration** — it is what stops a wrecked unit being worth a fresh one, and every code path applies it (`RotateToEdge.DoSell`, `Sell.cs`, and the `Sellable` tooltip). *(**Added 2026-08-17.** The formula in this file previously omitted it entirely, while three bot modules cited this file as the source for `GetSellValue × HP/MaxHP`. The code was right and the doc was incomplete; the doc is what changed.)*

`handicapAdjust` mirrors `HandicapProductionMultiplier`, which inflates what a handicapped player *pays* by the identical factor — so the two must move together. All three evacuation paths (`DeliversCash` rotation, `AmmoPool`'s evacuate-when-dry, `DropsSupplyCache`'s empty-truck return) go through `CustomSellValueExts.GetEvacuationRefund`; **do not compute an evacuation refund any other way.**

## Per-platform ammo budget targets

These are guideline ratios (`pool budget / unit Cost`). Specific per-pool values live in the YAML; the vehicle roster is tabulated below.

**Audit against these ratios and nothing else — two rival cost models are still lying around.** A 2026-08-16 sweep of ~63 `AmmoPool`s found three models in the repo and only one of them shipped:
1. **This section's budget ratios** — pick `SupplyValue` so `(Ammo/ReloadCount) × SupplyValue` lands at a target fraction of `Valued.Cost`. This is what the YAML implements, and the per-pool comments are written in it (e.g. `vehicles-america.yaml:519`, "8 batches × 5 shells × 30 supply = 240 (9.6% of cost 2500)").
2. **A per-shot tier table** in `WORKSPACE/archive/plans/260506_supply_ammo_economy.md:306-320` (T0=1 … T9=1500). **Stale — do not audit against it.** It survives only at the top end (T8 Hellfire 200, T9 HIMARS/Iskander 1500); T0–T5 are off by 8×–150× because the ratio model drove bulk per-shot costs toward zero. It also disagrees with this file on two munitions, and the YAML follows this file in both.
3. **A `SupplyValue == CreditValue` convention** from the same plan. **`CreditValue` does not exist on `AmmoPoolInfo` and no YAML sets it** — the only near-name is `SupplyProviderInfo.SupplyCreditValue` (`SupplyProvider.cs:86`), a different field on a different trait, used for the *supply pool's* refund. The convention was superseded, never implemented.

Corollary that cost a real bug: **`Buildable.Description` strings are a consumer of the cost model.** Ten infantry blurbs quoted per-shot numbers from model (2) and nine were wrong against shipped YAML; they were re-synced in `46e6950a`. When a rate changes, grep `Description:` for numerals before calling the re-tune done.

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

### The vehicle ammo roster — every armed vehicle carries a live pool

**There is no armed vehicle anywhere that shoots without drawing from a pool**, and the capacities are deliberately small. Audited across all four vehicle files (as of 2026-08):

| File | Actor | Live pool capacities |
|---|---|---|
| `vehicles-america.yaml` | `humvee` | 300 |
| | `m113` | 500 |
| | `bradley` | 900 + 8 |
| | `abrams` | **40** |
| | `m109` | **39** |
| | `m270` | **12** |
| | `strykershorad` | 400 + 8 + 4 |
| | `HIMARS` | **2** |
| `vehicles-russia.yaml` | `btr` | 500 |
| | `bmp2` | 900 + 8 |
| | `t90` | **40** |
| | `giatsint` | **39** |
| | `grad` | 40 |
| | `tos` | **24** |
| | `tunguska` | 180 + 8 |
| | `iskander` | **2** |
| `vehicles-ukraine.yaml` | `t72` | **40** |
| `vehicles.yaml` | `MNLY` | 10 (`mines-ammo`, feeds `Minelayer`, not an armament) |

The only pool-less vehicles are the ones with **no gun**: `MSAR`, `TRUK`, `LCCV` (unarmed support), the shared `^Vehicle`/`^WheeledVehicle`/`^TrackedVehicle`/`^Walker` templates, and the two projectile-carrier actors `HIMARSMissile`/`IskanderMissile`. At 40 rounds for a main battle tank and 2 for `iskander`/`HIMARS`, **a vehicle running dry mid-battle is the normal course of a fight, not an edge case.**

**`vehicles.yaml` is the least representative member of its own category — do not read it as "the vehicles".** It holds the shared templates plus a handful of unarmed support units; every actual combat vehicle lives in `vehicles-america.yaml`, `vehicles-russia.yaml` or `vehicles-ukraine.yaml`. Grepping `vehicles.yaml` alone for `AmmoPool` returns one live block and several commented-out ones, which reads as "vehicles used to have ammo and it was disabled" — the exact opposite of the truth. The commented blocks are not disabled pools on live units; they are fragments of two **entirely commented-out actors** (`SandBagLayer` ~`:650`, `timberwolf` ~`:709`, disabled in `296a529c`, 2023-06-20), and a commented `AmmoPool@1:` line looks identical either way. **General rule: before concluding anything about "all vehicles", grep every file under `mods/ww3mod/rules/` rather than the file whose name matches the concept.** The per-faction split means the category-named file is frequently the least representative one.

**Two structural splits inside that roster**, both easy to get wrong by generalising from the tanks:

- **The attack trait is not uniformly `AttackTurreted`.** `giatsint` (`vehicles-russia.yaml:489`) and `iskander` (`:985`) are `AttackFrontal`; the other fifteen are `AttackTurreted`. `AttackFrontal.GetAttackActivity` returns `Activities.Attack` while `AttackTurreted` inherits `AttackFollow.AttackActivity` — so **the vehicle roster is split across both attack activities**, and `Activities/Attack.cs` is not the infantry-only path it looks like.
- **No vehicle has `AutoSeekSupplies` at all.** The trait appears on exactly two templates in the whole mod, both infantry: `^Soldier` (`infantry.yaml:228`, `ReturnWhenEmpty: true`) and `^E6` (`:1949`, `ReturnWhenEmpty: false`). So none of that trait's machinery — neither the idle seek nor `ReturnWhenEmpty` — applies to a single vehicle, whatever its `Rearmable` status.

**`AutoSeekSupplies.ReturnWhenEmpty` bounds what it will interrupt an order FOR — it is not a guarantee that a dry unit stops attacking.** Its `ITick` returns immediately when the actor has no `Rearmable` (`AutoSeekSupplies.cs:226-227`), and when `ChooseResupplier` returns null, the host is out of the world, or the host fails `WithinReturnLeash` (`ReturnWhenEmptyLeashCells`, default 30, `:84`), it flags `NeedsResupply` and **deliberately leaves the unit's current order alone** (`:281-288`) — "an unreachable errand is worse than none". That decision is correct on its own terms. It does mean that anything which must release a dry unit from an undischargeable order has to live in the attack activities, not in the seek trait.

**"Can this unit shoot?" has THREE different answers, and picking the wrong grain is a recurring bug.**

| Grain | Predicate | Right for | Wrong for |
|---|---|---|---|
| Per-**actor** | `AmmoPool.CannotFight(self)` — `AllPoolsEmpty && !HasTraitInfo<AircraftInfo>()` (`AmmoPool.cs:216`) | "nothing left to shoot with, anywhere": resupply dispatch, cheap early-outs | **any question about a specific target** — a unit can be unable to engage what is in front of it while a pool it cannot use here is still loaded |
| Per-**armament, per-target** | `ChooseArmamentsForTarget(t, force)` **plus** `!a.IsTraitPaused` | "is there a weapon both legal against THIS target and able to fire?" — e.g. should I stop moving to aim | — (`ChooseArmamentsForTarget` supplies only the legality half; it filters `IsTraitDisabled`, and an empty armament is *paused*, not disabled) |
| Per-**armament firing gate** | `Armament.CanFire` (`Armament.cs:327`) | the shot itself | **any movement or engagement decision** — it also tests `IsReloading`/`IsWaitingBurst`/`IsAiming`, transient per-shot states, so reading it would make a unit decline to engage merely because it is between bursts |

Aircraft are carved out of the per-actor grain deliberately: a dry airframe recovers through its own idle `ReturnToBase` flow, and tearing its activity down from outside fights that flow (see "What rearms what" — and that flow is the ONLY one open to them: every `AutoRearm` entry point refuses `AircraftInfo`, so the carve-out is what routes them to `ReturnToBase` rather than a dock they could never reach).

**The concrete case, and it is not exotic.** `^E3`, the standard rifleman, carries a DMR (`primary`, 100 rounds) and an RPG (`secondary`, 1 round) whose weapon declares `InvalidTargets: Infantry` (`weapons-ballistics.yaml:302`). After any infantry-vs-infantry firefight the DMR is spent and the RPG is still loaded — there was never a legal target for it. So against infantry his only offered armament is one he cannot fire, while `CannotFight` reads **false** because a pool is loaded. Both his `ReloadAmmoPool`s are gated `RequiresCondition: replenish-soldiers`, so it does not heal on its own. **Generalisation: a multi-pool unit whose weapons have disjoint `ValidTargets` will routinely sit at "empty for the enemy in front of me, loaded overall", and any per-actor ammo predicate is blind to exactly that state.**

Two alternatives that are simply wrong at every grain: `Rearmable.RearmableAmmoPools` (answers a different question) and the `^AmmoDecoration` red-pip condition (implied by emptiness, not equal to it).

**`Rearmable.AmmoPools` and "every `AmmoPool` on the actor" are DIFFERENT SETS — and on 13 of 14 infantry classes they coincide, which is exactly what makes the wrong one look right.** `Rearmable` filters to its declared list (`Rearmable.cs:44`), so it answers "which pools does a *host* refill?", not "which pools does this unit have?". Of the 14 `infantry.yaml` classes carrying both traits (`^E1 ^E3 ^AR ^E2 ^TL ^MT ^SN ^AT ^AA ^E6 ^E4 ^SF ^DR ^PILOT`), the three two-pool combat classes (`^E3`, `^TL`, `^SF`) all list both pools — so a test on any of them passes either way. **`^E6` is the single divergence:** it lists only `secondary-ammo` (`infantry.yaml:1941-1943`) while also carrying a 100-round `primary-ammo` (`:1859-1863`). Scope the count to `infantry.yaml` — `^CrewMember` (`crew.yaml:5`) is a 15th armed man-class with a single, listed pool, so counting it gives 14 of 15 instead.

**The caveat on `IsTraitPaused` attaches to the QUESTION, not to the predicate.** `AmmoPool.cs:210-214` warns against "every armament paused" as a `CannotFight` substitute, and that warning is right *for resupply dispatch* — but pause-for-any-reason is exactly correct for "should I stop walking to aim at this?", where it also picks up `suppressed >= 10`, `empdisable`, `heavy-damage-attained` and `inwater`, each of which wedges a move by the identical route. A warning recorded against one call site is not a global prohibition. *(That comment's stated example is itself inaccurate — see the note at the site; the caveat stands on the other terms.)*

**Do NOT fix this inside `ChooseArmamentsForTarget`.** Its `// FF TODO Check ammo?` reads like an invitation, but `AttackBase.AbandonWhenArmamentsPaused` was added *specifically* because "every armament paused" must not end an attack by default — holding aim through a brief pause is the wanted behaviour. Filtering paused armaments in the shared method would silently flip that opt-in on for every unit in the game and strip the attack cursor off any momentarily-paused one. **A standing TODO is not evidence that the change is safe where it sits: check whether a later opt-in was built precisely to avoid making it there.**

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
