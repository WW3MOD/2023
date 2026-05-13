# Stage B.4 — Mounted infantry transport

> **Status: spec only.** Implementation deferred until B.2 (cover) lands
> and stabilises. This doc captures the design so it's ready to pick
> up.
>
> Addresses playtest finding O3 (vehicles outrun infantry; infantry
> arrives at the front piecemeal) and aligns with the doctrine's
> vehicle-as-mobile-fire-support role.

## What "done" looks like

In a v2 vs Normal match, you should see:

1. Early game: v2 produces a Bradley (`bradley`) and 4–6 infantry
   (`e3`/`ar`/`at`). Instead of all walking forward independently,
   the Bradley **picks up the infantry** at the Supply Route, drives
   forward to the screen position, **unloads** them at a treeline,
   then **returns** to the reserve area.
2. The Bradley is now free to go act as fire support wherever the
   line is under pressure. The infantry is in cover, holding.
3. The pattern repeats with M113s and other carriers. Infantry stays
   the baseline; vehicles are the delivery vector + fire support.

The visible difference: the front fills with infantry MUCH faster
than today, and vehicles don't pile up at the front waiting for
slow-moving infantry to catch up.

## Why this is doctrine-correct

From the user's playtest report (O3):

> "Vehicles are used to quickly provide fire superiority to where it
> is needed, but they don't sit on the front and hold it, they are
> used for their mobility more than their firepower. The firepower
> they provide is low compared to what they cost, in comparison to
> infantry. So the AI should prioritize infantry as the baseline and
> use vehicles to strengthen where it is needed at short notice."

Mounted transport is the mechanism that makes this work:

- **Infantry is the baseline** because they're cheap, numerous, and
  hold positions (with cover).
- **Vehicles are mobile fire support** that move *infantry* to the
  front rapidly, then continue moving along the line based on
  pressure.

Without transport, infantry vs vehicle speed mismatch means:

- Vehicles arrive at the screen position first.
- They sit there waiting for infantry to catch up.
- Infantry trickles in piecemeal, gets shot before forming up.
- The line forms slowly and unevenly.

## Engine surface area

WW3MOD has the relevant primitives:

- **`Cargo` trait** on transport vehicles (IFVs/APCs). Defines
  `MaxWeight`, `Types`, `PipCount`. Already on Bradley, BMP-2, M113.
- **`Passenger` trait** on infantry. Defines `CargoType` (matched
  against Cargo.Types) and `Weight`. Already on most infantry.
- **`EnterTransport` order** — issued to a passenger; the passenger
  walks to the transport and boards. Cargo activity handles the rest.
- **`UnloadCargo` order** — issued to the transport; vehicle disgorges
  its passengers at its current position.

So no new engine traits — purely a new BotModule that orchestrates the
existing primitives.

## Module design

New `MountedTransportBotModule` in `engine/.../BotModules/`. World
trait that ticks once per `ScanInterval` (e.g. 100 ticks):

### Phase 1 — pair carriers with passenger groups

For each idle carrier (Bradley/BMP/M113) with empty Cargo:

1. Find K nearest idle infantry that:
   - Are in `PassengerTypes` (e3, ar, at, sn, tl, medi — match what
     `Cargo.Types` accepts).
   - Sum of `Passenger.Weight` ≤ Cargo.MaxWeight (so they all fit).
   - Are in the RESERVE zone (far from contested cells, i.e. behind
     the LayeredDefence's OnLine radius).
2. If K passengers found, post a "PendingLoad" task to a blackboard or
   local dict: `{carrier, passengers}`.

### Phase 2 — issue load orders

For each pending load:

1. Issue `EnterTransport` (or equivalent) to each passenger targeting
   the carrier.
2. Mark carrier as "loading" so it isn't reassigned.
3. Wait for the passengers to arrive and board (poll
   `Cargo.PassengerCount` per tick).

### Phase 3 — drive to drop-off

Once Cargo is full (or after a timeout):

1. Compute drop-off cell: a SCREEN position from the LayeredDefence
   slot scoring, OR a cover-adjacent cell near the frontline.
2. Issue `Move` (NOT AttackMove — we want to deliver, not engage)
   to the drop-off cell.

### Phase 4 — unload + return

When the carrier arrives at the drop-off:

1. Issue `UnloadCargo` — passengers disembark.
2. Issue `Move` for the carrier back to the reserve zone (near own
   SR or a designated muster point).
3. Mark the carrier idle for the next loading cycle.

### State tracking

Per-carrier state machine:

```
IDLE → LOADING → READY → DELIVERING → UNLOADING → RETURNING → IDLE
```

Stored per carrier in a Dictionary<Actor, CarrierState>. Cleaned on
carrier death.

## Interaction with LayeredDefenceBotModule

Currently LayeredDefence treats all idle infantry as candidates for
forward dispatch. With transport in play:

- Infantry that's been *picked up by the transport module* should
  NOT also be assigned a screen slot directly. The transport will
  deliver them.
- When transport drops infantry at a screen cell, the infantry
  becomes idle there. LayeredDefence's "on-the-line" check sees
  them at the line and leaves them alone.

Simplest coordination: when MountedTransportBotModule assigns a unit
to load, it adds the unit to a shared exclusion set (or sets a
condition / claims via BotBlackboard) that LayeredDefence honours.

Alternative: just rely on the cooldown — if MountedTransport issues
an order to an infantry unit before LayeredDefence's next scan,
LayeredDefence will see it as "not idle" and skip. Likely
sufficient in practice.

## Edge cases / risks

- **Carrier gets shot en route.** Passengers may die inside (Cargo
  behaviour on death). Mitigation: keep carriers at standoff
  distance from contested cells until drop-off; route them through
  the rear zone where possible.
- **Drop-off cell occupied.** Infantry inside can't disembark.
  Mitigation: try a nearby cell, or wait a few ticks.
- **No passenger available.** If the bot has no idle infantry to
  load, the carrier sits empty. Fall back to LayeredDefence main-line
  behaviour for that carrier this tick (don't waste it).
- **Carrier in active combat.** If the carrier is engaging an enemy
  along its route, it'll deviate. Probably acceptable — fire support
  on the way. But the delivery timeline becomes uncertain.
- **Cargo full but no pressure.** If infantry is loaded but the line
  is stable everywhere, deliver to any thin sector — same scoring as
  LayeredDefence.

## YAML config sketch

```yaml
MountedTransportBotModule@v2:
    RequiresCondition: enable-ai-v2
    ScanInterval: 100
    CarrierTypes: bradley, bmp2, m113
    PassengerTypes: e3.america, e3.russia, ar.america, ar.russia,
                    at.america, at.russia, sn.america, sn.russia,
                    tl.america, tl.russia, medi.america, medi.russia
    MaxPassengersPerLoad: 5
    ReserveZoneRadiusCells: 12     # passenger must be this close to own SR
    DeliveryTimeoutTicks: 1500     # 60 sim-sec — give up loading if passengers can't reach
```

## TODOs (when implemented)

- [ ] B.4.1 — Module skeleton, state machine, dictionaries.
- [ ] B.4.2 — Carrier-passenger pairing.
- [ ] B.4.3 — Load order issuance + boarding poll.
- [ ] B.4.4 — Drop-off cell selection + Move.
- [ ] B.4.5 — Unload + return to reserve.
- [ ] B.4.6 — Coordination with LayeredDefenceBotModule (exclusion set).
- [ ] B.4.7 — Demo `demo-mounted-transport`.
- [ ] B.4.8 — Tournament batch to measure winrate effect.

## Adjacent open items

These came up in the same playtest report but live elsewhere:

- **O4: Active rearm/retreat for out-of-ammo units.** Skipped today
  (LayeredDefence just won't push them forward). Needs its own module
  later.
- **O5: Empty supply truck redirect.** Extension or replacement of
  `SupplyFollowerBotModule`. Not a transport feature per se — it's
  about how trucks SELF-manage, not how they move infantry.
