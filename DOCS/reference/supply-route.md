# Supply Route (SR)

> The Supply Route is the player's **sector beachhead**, not a factory. Read this before any design work that mentions SRs — the in-engine name and the OpenRA-style "production building" wiring are misleading if you treat it like a Red Alert Construction Yard.

## The mental model

A Supply Route is a **flag**. Think of it as the assembly area where a sector's units muster after being deployed in from off-map reserves. In wargame terms: the **beachhead**. In real life: the marker post where new arrivals report to before being sent into the line.

- **One per player, fixed at game start.** Every starting-units package (`StartingUnits@*` in `world.yaml`) ships with `BaseActor: supplyroute`. You don't build it, you don't choose its location — it spawns near your player's map-edge spawn point. The reason it's "near" the edge rather than *on* the edge is just to give it footprint clearance; spawn point and SR are essentially the same thing.
- **Units don't come out of the SR.** They enter from the map edge nearest to the SR and walk/fly to the SR's rally point. The SR is the destination, not the origin. (Engine: `ProductionFromMapEdge` on the SR's queues.)
- **Losing your SR = losing the sector.** When the SR changes hands (captured) or is contested to zero, your reinforcements from outside the map are cut off. That is the rationale for the whole building — it's the player's link to off-map reserves, and the battle for that link *is* the campaign for that sector.
- **The SR is indestructible by design.** `Armor: Indestructable` in the YAML — it cannot be killed by damage. The only way to take it from a player is to **capture it** (then `OwnerLostAction: ChangeOwner → Neutral` neutralizes it) or to **contest** it (graduated production slowdown via `SupplyRouteContestation` while enemies stand inside the 10-cell contestation circle).

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

### Capture (binary)

The SR can be captured by an engineer/technician (any `CapturableActorTypes` chain that targets `supplyroute`). On capture:
- `OwnerLostAction: ChangeOwner → Neutral` — the SR doesn't transfer directly to the capturing player; it goes neutral first. (Check intent: should it go to the capturer? Engine currently flips to Neutral. Possibly a v1 polish item.)
- The previous owner loses the ability to reinforce through that SR.
- If it was the player's only SR — they're cut off entirely.

### Contestation (graduated)

`SupplyRouteContestation` (10-cell range, on every SR) tracks enemy presence inside the contestation circle:
- `BaseTicks: 1500` — countdown when enemies stand inside
- `SlowdownThreshold: 50` — when contestation reaches this %, production slows
- `FriendlyRecoveryMultiplier: 3` — friendly units in the circle recover the meter at 3× the drain rate
- `ContestationNotification: BaseAttack` + `Supply Route contested!` text — both fire when contestation kicks in

So a single scout sitting in your SR circle won't immediately kill production, but it will slow you down — and a sustained presence will force you to react. **This is the dominant pressure mechanic** for siege play.

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

- **Actor definition:** `mods/ww3mod/rules/ingame/structures.yaml:202` — `SUPPLYROUTE` block.
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
