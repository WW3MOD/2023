# Transport demand is the second half of the purpose layer, not a parallel build

_Recorded 2026-08-08T07:05:31.908Z by bdedd544_

Two recons landed together and reframed the user's request.

**What the user asked for:** a transport system that tracks where soldiers want to go and groups them into transports, "so the bots don't just transport one technician at a time."

**What is actually true** (`WORKSPACE/recon/260808-transport-census.md`, `f819d646`): both transport modules ALREADY pool. `MountedTransportBotModule` loads 2–5 and explicitly refuses a single-passenger run (`:609-610`), holding the carrier until the minimum is aboard or 90 s elapse. `HelicopterSquadBotModule` loads 4–8 and won't dispatch until `Cargo.PassengerCount` confirms embarkation. **Batching is not the missing piece.**

**Options considered for the transport work:**

- **(A) Build the demand layer as its own 2–4 session project, in parallel with the purpose fix.** Rejected.
- **(B) Sequence it as the second half of the purpose layer.** TAKEN.
- **(C) YAML-only capacity/lift win and call it done.** Rejected as a complete answer — it increases ride volume without touching destination correctness — but kept as a separate cheap item, posted to the user because it touches `@stable` scope.

**Why (B).** The transport census's key finding is that destinations are PRIVATE: the shared ledger stores `{Objective:string, ExpiresAtTick, CommitCount}` with **no position** (`PoiGoalGuard.cs:41-51`), destination-bearing state lives in private nested `Axis`/`Garrison` classes with no accessors, and the only public per-unit destination anywhere is `CohesionSlotMemory.AssignedSlot` — a formation slot, not a mission.

The purpose census (`260808-unit-purpose-census.md`, `5be3b98b`) independently found that in the no-contact state **nothing assigns a destination at all**: `LayeredDefence` ⇒ `SquadManager` ⇒ `PoiOffensive` each defer, ending in `∅`.

Put together: **you cannot publish "where this soldier wants to go" until something decides where it should go.** A demand layer built first would have nothing to read in exactly the state the user observed. So purpose-assignment is a PREREQUISITE, not a sibling — and once units carry assigned destinations, transport reading them is a much smaller change than the standalone 2–4 session estimate implies.

**Corroborating alignment nobody designed on purpose:** transport pickup is gated to 14 cells around the own Supply Route, so transport only ever serves the first leg out of the beachhead. That is precisely where the unowned free pool stands, and precisely what the unreachable `StageFreePool` (`PoiOffensiveBotModule.cs:1467`, written verbatim to stop units "idle at the SR clogging the road to the front") was meant to move. The three pieces are already pointed at the same cell of the map.

**Consequence to watch:** if the purpose fix lands and units still walk everywhere, the 14-cell radius is the next suspect, not the pooling logic.
