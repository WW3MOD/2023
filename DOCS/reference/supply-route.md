# Supply Route (SR)

> The Supply Route is the player's **sector beachhead**, not a factory. Read this before any design work that mentions SRs — the in-engine name and the OpenRA-style "production building" wiring are misleading if you treat it like a Red Alert Construction Yard.

## The mental model

A Supply Route is a **flag**. Think of it as the assembly area where a sector's units muster after being deployed in from off-map reserves. In wargame terms: the **beachhead**. In real life: the marker post where new arrivals report to before being sent into the line.

- **One per player, fixed at game start.** Every starting-units package (`StartingUnits@*` in `world.yaml`) ships with `BaseActor: supplyroute`. You don't build it, you don't choose its location — it spawns near your player's map-edge spawn point. The reason it's "near" the edge rather than *on* the edge is just to give it footprint clearance; spawn point and SR are essentially the same thing.
- **Units don't come out of the SR.** They enter from the map edge nearest to the SR and walk/fly to the SR's rally point. The SR is the destination, not the origin. (Engine: `ProductionFromMapEdge` on the SR's queues.)
- **Losing your SR = losing the sector.** When the SR changes hands (captured) or is contested to zero, your reinforcements from outside the map are cut off. That is the rationale for the whole building — it's the player's link to off-map reserves, and the battle for that link *is* the campaign for that sector.
- **The SR is indestructible by design.** `Armor: Indestructable` in the YAML — it cannot be killed by damage. **Contestation** is the only *implemented* pressure mechanic (graduated production slowdown via `SupplyRouteContestation` while enemies stand inside the 10-cell contestation circle). Capture is part of the intended design but **is not wired in the code today** — see the Capture section below for what actually happens.

## Why "Supply Route" is not "Construction Yard"

The engine wiring treats it like a Red Alert conyard because that's how OpenRA understands production buildings. But the gameplay model is different in every way:

| Red Alert Construction Yard | WW3MOD Supply Route |
|---|---|
| Built mid-game from MCV | Spawned with the player |
| Player picks where to place it | Fixed near spawn edge, no choice |
| Units come out of the building | Units come from the map edge, *to* the building |
| Destroyed → game-loss | Indestructible — only captured or contested |
| Multiple per player as you expand | One per player at start; more only by **capture**, never by build |
| Manufactures from raw materials | Calls in pre-existing reinforcements from off-map |

If you read AI or strategic-planner code that talks about "candidate SR locations" or "expanding by building a second SR" — **it's wrong**. That's vanilla OpenRA thinking applied to a system that doesn't work that way.

## Spatial layout per map

Every map's spawn locations are placed close to the map edges (corners, sides). Each player's SR ends up next to their spawn. **An SR will never be mid-map** under normal play — only via capture of a neutral SR that the mapmaker placed there. The implication for any spatial reasoning code: SRs are an edge phenomenon, not a placement choice.

```
┌─────────────────────────────────────────────────┐
│  P1 SR ⚑                                        │
│   ↑                                             │
│   │  (units spawn at map edge,                  │
│   │   walk to the SR rally point)               │
│  edge                                           │
│                                                 │
│                                                 │
│                                                 │
│                                                 │
│                                                 │
│                                          edge   │
│                                            │    │
│                                            ↓    │
│                                       ⚑ P2 SR   │
└─────────────────────────────────────────────────┘
```

## Neutral SRs on maps

Mapmakers can place additional `SUPPLYROUTE` actors as **neutral** (no starting owner). These sit on the map as objectives that any player can capture. A captured neutral SR gives that player an additional reinforcement entry point — units spawned through it walk from the *nearest map edge to that SR*, which may be a different edge than the player's home SR uses.

This is the only way to "get a second SR." There is no build queue for it.

Open design questions about neutral SRs (tracked in `RELEASE_V1.md` under Supply Route):
- Which map edge does a captured-neutral SR pull reinforcements from?
- Should multiple players be able to fight over the same neutral SR, or first-capture-wins?
- Are neutral SRs visible from game start, or fog-of-war until scouted?

## Capture and contestation mechanics

### Capture (binary) — DESIGN INTENT, not yet implemented

**Verified 2026-07-20: the SR cannot be captured today.** The intended model is that an engineer/technician takes an enemy SR and cuts off the previous owner's reinforcements. That wiring does not exist:

- `SUPPLYROUTE` (`structures.yaml:202`) has **no `Capturable` and no `CaptureManager`**, and inherits none — its bases are `^ExistsInWorld` / `^SpriteActor` / `^SelectableBuilding`, none of which pull in the capture chain (`^NeutralOrOccupiedCapturable`, `structures.yaml:149`). There is no capture-type for a TECN's `Captures{building-neutral/-occupied}` to intersect, so a technician has nothing to enter. The `CaptureNotification` at `structures.yaml:216` is commented out and wires nothing.
- **`OwnerLostAction: ChangeOwner → Neutral` (`structures.yaml:227`) does NOT fire on capture.** `OwnerLostAction` implements `INotifyOwnerLost` — "when the actor's owner is **defeated**" — and `OnOwnerLost` is called *only* from `ConquestVictoryConditions.cs:109` and `StrategicVictoryConditions.cs:152`, both iterating a just-defeated player's actors. So an SR goes Neutral **only when its owning player loses the match**, never via an engineer.

Two subtleties for whoever implements this to match the design: (1) neutral-SR capture needs `Types: building-neutral`, enemy-SR needs `building-occupied`; (2) vanilla `Captures`/`Capturable` transfers to the **capturer**, not to Neutral — so the "capturer can never keep it, it just neutralizes" behavior needs a custom on-capture hook, not the stock traits alone.

The rest of this doc describes the intended capture model; treat those passages as design, not current behavior, until the wiring lands.

### Contestation (graduated)

`SupplyRouteContestation` (10-cell range, on every SR) tracks enemy presence inside the contestation circle:
- `BaseTicks: 1500` — countdown when enemies stand inside
- `SlowdownThreshold: 50` — when contestation reaches this %, production slows
- `FriendlyRecoveryMultiplier: 3` — friendly units in the circle recover the meter at 3× the drain rate
- `ContestationNotification: BaseAttack` + `Supply Route contested!` text — both fire when contestation kicks in

So a single scout sitting in your SR circle won't immediately kill production, but it will slow you down — and a sustained presence will force you to react. **This is the dominant pressure mechanic** for siege play.

### Contestation to zero ends the match — the real defeat path

The match-ending win/loss runs on **`SupplyRouteContestation`, not stock `ConquestVictoryConditions` alone.** The SR is `Armor: Indestructable` (`structures.yaml:270-271`) — never destroyed — so "destroy the conyard = defeat" is replaced by "contest the SR to zero":

- When an SR's control bar empties, a defeat bar fills; at full it calls `OnDefeatBarFull` (`SupplyRouteContestation.cs:354`) → the owning player goes **passive** (production frozen). Elimination fires only if that team has **no other active SR** (`ResolveTeamElimination`, `:412`).
- **The win is awarded explicitly, per survivor, on ANY defeat path.** Older code only marked the *losing* team and relied on stock `ConquestVictoryConditions.Tick` to infer the win — which fails in a near-simultaneous mutual overrun (both resolve as `Lost` before the inference tick, so everyone shows "mission failed"). The award is now a path-independent `SupplyRouteContestation.AwardDecidedSurvivors(World)` (`SupplyRouteContestation.cs:466`) called synchronously from `ConquestVictoryConditions.OnPlayerLost` (`:119`) — so it fires the instant *any* player is defeated (SR contested to zero, loss of all required units via `MarkFailed`, surrender, or a mutual same-tick overrun), not only from the SR-contested `OnDefeatBarFull` path where the original award lived. It marks `Won` each survivor whose every non-allied combatant is now `Lost` (`ShouldAwardVictory`), reproducing CVC's per-survivor test so FFA / 2v2v2 don't instantly resolve when one party dies. Idempotent — it only touches `Undefined`/`Incomplete` objectives, so a second defeat in the same tick is a no-op (the anti-"everyone loses" invariant).
- **`MarkFailed` is asymmetric and unforgiving.** It overwrites a *Completed* objective back to `Failed` and re-fires `OnPlayerLost` (`MissionObjectives.cs:146-162`), whereas `MarkCompleted` no-ops on any non-`Incomplete` objective (`:129`). So once a survivor's win-inference is missed it can never be re-awarded by a later tick — which is why the award must fire eagerly on the defeat event (above) rather than being inferred afterwards.
- **Awarding via `MissionObjectives`:** `MarkCompleted` fires `OnPlayerWon` only when **all** required objectives are Completed (`MissionObjectives.cs:136-137`). `AwardVictory` (`:491`) therefore no-ops unless the player has a `ConquestVictoryConditions` trait and completes only its `Type == "Primary"` objectives — never blanket-completing (which would auto-win a scripted campaign mission running `-ConquestVictoryConditions` + `MissionObjectives.EarlyGameOver`).
- **`TestMode` symmetry:** `ResolveTeamElimination` early-returns on `TestMode.IsActive` (`:427`), matching `ConquestVictoryConditions.Tick` (`:63`) / `MissionObjectives.CheckIfGameIsOver` (`:171`), so an SR contest emits no stray victory lines mid-autotest. Consequence for test authors: the whole interactive win/loss *verdict* is suppressed under `TestMode`, so a regression test for win attribution cannot observe a game-over — it must read `Player.WinState` directly (Lua getter, `PlayerProperties.cs:72`). `AwardDecidedSurvivors` itself is NOT `TestMode`-gated (it fires from `OnPlayerLost` regardless), so the underlying Won/Lost state is set and observable even while the end-screen stays suppressed. The pure branches (`ResolveEliminationOutcome` / `ShouldAwardVictory`) are NUnit-pinned (`SupplyRouteEliminationTest.cs`); the full team-propagation ending is verified by unit test + reasoning (no single autotest map runs 2v2 team victory).

## Forward delivery — how supply reaches the front

**DELIVERY IS UNCONDITIONAL. Danger selects the MODE; it never grants permission to abandon a run.** This is the invariant everything below hangs off, and it is the one thing three separate rounds of work each got wrong: a danger reading was allowed to cancel a delivery, and the visible symptom every time was a truck shuttling back and forth while a platoon starved. If you are changing this subsystem and a danger term can make a delivery *not happen*, that is the bug.

### The two modes

| front reads | truck does | cargo |
|---|---|---|
| **dangerous** | drives in, stops `DropShortCells` short of the platoon, unloads its **whole** load as a SUPPLYCACHE, egresses | emptied — the drop is all-or-nothing (`DropsSupplyCache` calls `SetSupply(0)`) |
| **quiet** | closes to aura range and serves in place | **retained** for the next customer |

The anchor is relative to the **platoon that needs the supply**, not to the beachhead: `ClusterDropAnchor` places the crate `DropShortCells` back along the cluster→truck line. The older descent from the Supply Route survives only as a fallback for a truck with **no cluster selected** (`ResolveDropAnchor`).

**"No cluster" is not "no demand", and the difference decides whether the fallback is worth anything.** Demand is counted around the *anchor* (`CountStarvingNear`: individual starving men inside `DropDemandRadiusCells`, no grouping requirement). A *cluster* is a stricter object — `SmallSquadMinNearbyFriendlies` men within 10 cells of each other, `AmmoNeed > 0`, inside this truck's follow leash, and winning a `SectorSpread` assignment. So the fallback's live cases are a surplus truck the spread left unassigned, a lone starving man below the cluster floor, and a needy cluster outside the leash. What it is *not* for is a front too hot to approach: `SelectServableClusters`' relief valve always keeps the least-dangerous needy cluster selectable, so danger never empties the cluster list.

**The `starving=` figure on a `reason=NoAnchor` decline line is not a demand reading.** Every demand term in `StepDrop` is measured around the anchor, so with no anchor they are hard-coded placeholders. The line prints `<not-counted>` rather than `0` for exactly this reason — a `0` there once produced the reasonable-but-wrong conclusion that the fallback only ever fires when nobody needs anything.

**The standoff is a request, not a guarantee** (`DropClampStandoff`). `ForwardStagingMath.StagingCell` returns its start unchanged when `frontierAt(start) <= standoffCells`, and a returned start reads as "no anchor" — so a player whose front sits closer to his own beachhead than `DropStandoffCells` could not resolve a fallback anchor *at all*, in precisely the geometry where resupply matters most. Measured 2026-08-10 in one match: `sr=6,16 → <none> … frontier-at-sr=5` for one bot against `sr=58,16 → 23,17 standoff=8 frontier=8` for the other, same map, same scan. With the clamp on, the descent asks for `frontier-1` instead — the tightest ring strictly forward of the SR — takes one step and stops on the beachhead's doorstep. It degrades the *standoff*, never the delivery. Two properties keep it safe: it can only bite on the input that returns the start today (so no working descent moves), and a frontier of 1 or 0 yields a non-positive standoff that `StagingCell` treats as disabled — which is correct, since the only cell left would be inside the SR's 3×3 footprint where `CanDropCache` refuses the crate anyway.

### What picks the mode — and why neither limb alone works

`SupplyDropMath.DangerSelectsDrop` (`SupplyDropMath.cs:234`), evaluated only for a delivery that has not yet started:

1. **floor** — below it, always safe. Checked first, and may *only* ever declare safe.
2. **absolute** — at or above N danger units, dangerous regardless of the field's shape.
3. **relative** — at or above the player's own median stamped cell (`DangerFieldLayer.GroundDangerMedian`, `DangerFieldLayer.cs:925`).

**Both limbs are load-bearing, and this reasoning outlives the numbers:**

- **Relative alone fails on a saturated field.** *When everything is dangerous, nothing is relatively dangerous.* Measured: a cell holding ~135 reference contacts' worth of danger classified **safe**, because believed long-range artillery bathed the whole map and pulled the median up with the cluster. A ratio answers "is this unusual for us", never "is this lethal".
- **Absolute alone is what broke the original thresholds** — but only because their *values* were written for a scale that no longer existed. The danger unit is now normalised (100 units = one reference contact at point-blank), so a figure in these units survives a rebalance.
- **A ratio fails at both ends and the floor covers only one.** The empty end — no believed contact, so "above the median of nothing" admits anything — is the floor's job; the saturated end is the absolute limb's. Removing either re-opens one end.

Live values: `DropRequiresDanger`, `DropDangerFloorUnits`, `DropDangerMedianPercent`, `DropDangerAbsoluteUnits` (in `ai.yaml`, `SupplyFollowerBotModule@supply`; C# defaults on `SupplyFollowerBotModuleInfo`).

**The floor is what protects the quiet-front branch, not the absolute limb** — it is evaluated first and may only ever declare *safe*, so a map with no believed enemy serves in place regardless of where the absolute limb sits. Any retuning of the absolute limb is therefore a decision about *contested* fronts only.

### Commitment — and it starts at INTENT, not at dispatch

A truck is classified into one of three errand states every scan (`SupplyDropMath.ClassifyErrand`), and the state — not branch order — decides whether evac may run:

| state | meaning | evacuates? |
|---|---|---|
| `Delivering` | a drop errand is recorded and still running | no (`DropCommitmentOverridesEvac`) |
| `Intent` | holds cargo **and** has a customer cluster selected — on an errand from the moment it has a target | no (`DeliveryIntentOverridesEvac`) |
| `None` | empty, or no reachable customer | **yes** — this is evac's actual job |

**Shipping only the `Delivering` half left a total gap, not a partial one.** Commitment protects a drop *already in flight*; a truck that has not been dispatched yet was still fair game, so evac out-ranked **starting** a delivery while losing only to one under way — and a delivery that can never start never happens. Measured in a real 30-minute match, 2026-08-10, on the build that shipped commitment: `adopt truck=4802 supply=750`, `evac-enter @20,43 danger=17773 threshold=1706`, `evac-exit @13,46`, repeating for the whole game with no crate ever placed. Both supply scenarios were green throughout — there the truck committed early enough that the window never opened, so **the scenarios were right and insufficient**.

A truck can never reach a drop point lying *beyond* the cell where evac fires. That was stated for the in-flight case and is equally true one scan earlier, which is why the answer is a priority rule over the errand state rather than more damping. Both `Intent` terms are responsive — a drop empties the truck, and the customer is re-derived every scan — so the state ends when the situation ends; there is no timer and no bail-out.
- **The destination is frozen** (`ResolveDropAnchor`, `:1442`). The anchor derives from a cluster centroid, and a platoon that advances or scatters drags that centroid with it — so a re-derived anchor follows the platoon into the guns. Commitment is to a *place*, not merely to not evacuating.

Both read the same responsive `ErrandStillRunning` predicate, so a truck that goes idle still holding its load is no longer committed and re-derives normally. There is deliberately **no bail-out**: commitment costs trucks, and a lost truck releases its claim and dispatch record in the ordinary scan cleanup so another truck inherits the delivery.

### What a normal match logs — the subsystem must be diagnosable without a rebuild

Everything below is unconditional (no `DebugLogging`), because three rounds of work on this subsystem were tuned blind: the only line that carried the drop terms sat behind the flag, `[supply] drop` fires only on success, and so "never dropped" and "never logged" were the same silence. The evac lines were unconditional and were the sole reason the 2026-08-10 defect could be found at all.

| line | says |
|---|---|
| `[supply] errand … A→B` | the truck's errand state changed (`None` / `Intent` / `Delivering`) — logged on transition only |
| `[supply] holds-on` | evac was suppressed because a delivery outranks it, with the danger it is driving into |
| `[supply] drop-declined … reason=…` | which gate refused, from `SupplyDropMath.DropVeto` — `NoAnchor` / `LowLoad` / `NoDemand` / `Covered` / `SafeFront`. Logged whenever the reason CHANGES, plus a roll-up every `DropDeclineRollupScans` carrying the streak |
| `[supply] drop` | an errand was **issued** — not that a crate exists |
| `[supply] crate-placed` / `crate-merged` | a crate is **on the ground** (`DropsSupplyCache`) |
| `[supply] crate-refused … reason=never-arrived\|cell-blocked` | the errand completed and still put no crate down |

`drop` and `crate-placed` are separated by a drive, an arrival test and an occupancy test, each of which can refuse. Reading only `drop` will tell you a delivery happened when it did not.

### Starvation lifts the follow leash

A cluster holding `StarvingFollowMinUnits` starving men gets `StarvingMaxFollowDistance` instead of `MaxFollowDistance` (`ai.yaml:805/818-819`; `FollowLeashCellsFor`, `:1153`). Urgency-gated rather than a bigger single number on purpose: a dying platoon must not be abandoned for being two cells too far, while a topped-up one still gets the short leash or every truck chases every distant squad across the map.

### Infantry walking to a placed crate is CORRECT behaviour

This distinction cost several rounds of analysis, so state it plainly:

- Walking a short distance to a **placed crate** is the doctrine working. The crate is a real, collectable actor (SUPPLYCACHE, 4-cell aura — see [`economy.md`](economy.md)), and `AutoSeekSupplies` (`infantry.yaml:221-222`, 20-cell selection leash) is what walks them to it. The standoff exists precisely so they make that walk.
- Walking rearward to meet a **truck** is the front collapsing.

Same trait, similar distance, opposite meaning. A test or metric that measures only "did the platoon get fed" cannot tell them apart, because the men end up fed either way — judge position as well as ammo, and judge position against whether a crate exists.

## Strategic implications

For AI design and for any strategic-layer code:

### What an AI / strategic system should reason about

- **Defending the home SR** — existential. Treat it as the top defense priority, always. Loss = match loss (or close to it in modes where it isn't literal game-over).
- **Pressuring the enemy SR** — by far the most valuable spatial objective. A unit standing inside the enemy's contestation circle does more than damage — it slows their entire production.
- **Capturing neutral SRs** — if the map has them, this is the only "expansion" decision. Worth the same as a capturable income building only if the SR delivers a better reinforcement angle than your home SR (different map edge, closer to the front).
- **Rally point placement** — the only "positioning" decision a player makes about their own SR. The rally point determines where reinforcements muster after walking in from the edge. AI should move the rally point as the front shifts.
- **Reinforcement-lane awareness** — units walk a path from edge → SR rally. That path can be ambushed by the enemy. Smart play: ambush enemy reinforcement lanes; screen own.

### What an AI should never assume

- "I should build a second SR to expand my economy." → SRs are not buildable in normal play.
- "I should place my SR closer to the front line." → You don't place your SR; it spawns where the map says.
- "I can destroy the enemy SR with enough firepower." → Indestructible; only capture and contestation work.
- "The SR works like a Red Alert Construction Yard." → It doesn't. See the comparison table above.

## Engine integration points

- **Actor definition:** `mods/ww3mod/rules/ingame/structures.yaml:202` — `SUPPLYROUTE` block. The physical building is a **3×3 footprint** (`=+= +++ =+=`, `Dimensions: 3,3`, `structures.yaml:242-243`), which matters for placement math: a top-left-anchored 3×3 near a map corner can overflow the map bounds, so corner SRs sit a few cells inward. Note: **no `Capturable`/`CaptureManager`** here or in any inherited template — capture is unimplemented (see the Capture section).
- **Neutralize-on-defeat:** `OwnerLostAction` (`structures.yaml:227`) → fires only from `ConquestVictoryConditions.cs:109` / `StrategicVictoryConditions.cs:152` when the owner is defeated — **not** a capture path.
- **Spawn wiring:** `mods/ww3mod/rules/world.yaml:316–388` — every `StartingUnits@*` has `BaseActor: supplyroute`.
- **Contestation trait:** `SupplyRouteContestation` (engine side, paired with `WithRangeCircle@Contestation` for the visual).
- **AI YAML:** `mods/ww3mod/rules/ai/ai.yaml` treats SR as `ConstructionYardTypes` / `VehiclesFactoryTypes` / `BarracksTypes` simultaneously. This is the OpenRA-trait integration, not a strategic statement — the AI's strategic layer should *not* read these to mean "SR is a factory."
- **In-flight v1 work:** `RELEASE_V1.md` → "Supply Route contestation — graduated control bar, production slowdown, notifications" and "Captured SR handling — what spawns link, neutral SRs between players."

## Related docs

- [`economy.md`](economy.md) — supply, ammo, cash flow. The economic side of how reinforcements get paid for. (The SR triggers the spend; the economy doc covers what the spend actually buys.)
- [`architecture.md`](architecture.md) — engine layout, scenario system.
- `WORKSPACE/ai/foundation_260511.md` — AI overhaul foundation. Its "WW3MOD-specific" section should reference this doc rather than restating the model.

## When you update this doc

This is the **canonical SR mental model**. If anything here disagrees with the code, the doc is right and the code/YAML needs to change — or the doc needs to change, but never silently. Any change to:
- SR buildability (currently `Prerequisites: ~disabled` keeps it permanently un-buildable in normal play)
- Capture vs. neutral-on-capture behavior
- Contestation parameters
- Neutral-SR support on maps

...should be reflected here.
