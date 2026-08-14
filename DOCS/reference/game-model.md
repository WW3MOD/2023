# How WW3MOD Differs from Red Alert (game model)

WW3MOD is NOT Red Alert with new sprites. The entire gameplay model is different. **Do not assume any Red Alert mechanic still applies.** This doc is the full mental model; the hard rules are summarized in `CLAUDE.md`.

## Reinforcement Model (no factories)

There are **no Construction Yards, Barracks, War Factories, or Naval Yards**. Units are NOT "built" — they are **called in as reinforcements from off-map reserves** via the **Supply Route** building. Think of the Supply Route as a radio/logistics hub that requests reinforcements, not a factory that manufactures units.

- **The Supply Route is a fixed sector beachhead, NOT a buildable factory.** Every player **starts with exactly one** SR, spawned near their map-edge spawn point. You do not build it, you do not choose where it sits, and you cannot build a second one. The intended way to get a second SR is to **capture** a neutral one placed by the mapmaker — but note SR capture is **not wired in the code yet** (`SUPPLYROUTE` has no `Capturable`/`CaptureManager`; details in [`supply-route.md`](supply-route.md)). An SR is **indestructible** — but via `Targetable: TargetTypes: NoAutoTarget`, which makes it an invalid target for every warhead in the mod; the `Armor: Indestructable` line is inert and protects nothing (see [`supply-route.md`](supply-route.md)). Today an SR only leaves a player's hands by being **contested down** or when that player is **defeated** (`OwnerLostAction` flips it Neutral). **Read [`supply-route.md`](supply-route.md) before any AI/strategic-layer design touching SRs** — it's the canonical mental model and the trap that keeps recurring.
- **Supply Route is the single core production building.** It produces ALL unit types (infantry, vehicles, aircraft) via `ProductionFromMapEdge` — units spawn at the map edge nearest the SR and march/fly to the rally point.
- **Buildings and defenses** are the exception — they spawn locally at the Supply Route via a separate `Production@Local` queue.
- **HPAD (Helipad)** and **AFLD (Airfield)** are **rearm/repair support buildings**, not production prerequisites. Helicopters and planes CAN be produced without them. HPADs/AFLDs let aircraft rearm faster on-map instead of flying back to the map edge. Future plans include capturable HPADs on maps.
- **"Buying" a unit** = calling in a reinforcement from reserves. **"Rotating out" a unit** = sending it back to the map edge to recover its budget cost. This is the economy loop.
- **Unit costs represent budget allocation**, not manufacturing cost. A destroyed unit is a permanent loss of that budget.
- **The AI YAML lists `supplyroute` under `ConstructionYardTypes` / `VehiclesFactoryTypes` / `BarracksTypes`.** That's OpenRA-trait integration so the production queues wire up — it is **not** a statement that the SR is a factory in the strategic sense. Any strategic-planner code that reads "the AI has a ConstructionYard" must treat that as the player's sector beachhead, not as an expandable industrial base.

## No tech tree / building prerequisites

There is no "build barracks → build war factory → build radar → unlock X" progression. Tech levels exist (`~techlevel.low/medium/high`) but they are granted automatically based on game time or other conditions, not by constructing specific buildings. Any unit the player's tech level allows can be called in immediately.

## Map-edge spawning

Units don't appear at the production building — they enter from the map edge nearest to the Supply Route's SpawnArea hint, then walk/fly across the map to the rally point. This means:

- Production has inherent travel time (units have to traverse from edge to SR rally before being usable)
- Enemy can ambush reinforcements en route — and the same is true in reverse: your AI/units can ambush the enemy's reinforcement lane
- SR position is **fixed near the player's spawn edge** by the map — it isn't a player decision. The only positioning lever the player has on their own SR is the rally point, which determines where units muster *after* arriving

## Engine code still has old RA patterns

Many engine files still contain classic RA assumptions (e.g., `HasAdequateAirUnitReloadBuildings` checking for 1 airpad per aircraft). When you encounter these patterns, understand they may not apply. Always check how WW3MOD actually uses the system before assuming the old logic is correct. The `SkipRearmBuildingCheck` YAML property on `UnitBuilderBotModule` was added specifically to bypass one such legacy check.

## Capturing neutral buildings consumes the technician

Neutral income/tech buildings (derricks, etc.) are taken by technicians (TECN) via the `^CapturesNeutralBuildings` template (`infantry.yaml:941`), which sets **`ConsumedByCapture: true`** (`infantry.yaml:947`). A *successful* capture removes the TECN from the game — so the technician pool is a **consumable**, not a persistent squad: it shrinks by one on every capture success, on top of every combat loss. With a unit limit like `tecn: 3`, capturing two or three buildings can exhaust the live pool until production replaces it, at which point no further captures are possible. For any capture-focused AI, availability of technicians — not coordinator logic — is the binding constraint.

**Soldiers clear; only technicians own.** The rule is uniform across every capturable building — there is no per-building exception. Line infantry use `^CapturesOccupiedBuildings` (`infantry.yaml:927`), a single `Captures@OCCUPIED` trait on the `building-occupied` type, restricted to enemy-owned targets (`ValidRelationships: Enemy`, `:938`) and carrying `CaptureToNeutral: true` + `EnterBehaviour: Exit` (`:939-940`). A soldier walking into **any** enemy-held building drops it to **Neutral** instead of claiming it, then walks back out alive.

That covers all 23 capturable actors, defences and production included: `AFLD`, `AGUN`, `AMMOBOX1-3`, `BARL`, `BIO`, `BRL3`, `CRAM`, `CTFLAG`, `FCOM`, `FTUR`, `GUN`, `HGATE`, `HOSP`, `HPAD`, `HSAM`, `LOGISTICSCENTER`, `MISS`, `MSLO`, `OILB`, `SAM`, `VGATE`.

Ownership by capture is therefore **exclusively** the technician's, and a neutralised building — however it got that way — then needs a TECN walked into it like any other neutral. Both flags live on `CapturesInfo` (`Captures.cs`) and default to stock capture-and-be-consumed, so `^CapturesNeutralBuildings` is untouched and the technician is still consumed on every success.

> **User ruling, 2026-08-14:** *"This should be same for all buildings, a technician is always needed to capture anything, and a soldier can 'clear' it (turn neutral)."* This replaced a narrower rule shipped a day earlier that applied clearing only to `^TechBuilding` via a separate `building-occupied-tech` capture type; that type and the second `Captures` trait are gone.

> Correcting an earlier claim: this section used to state that soldiers were "*not* consumed". That was never true — `Captures.cs:41` defaults `ConsumedByCapture` to `true` and no override existed, so a soldier **was** removed from the game on a successful capture until these changes.

**Clearing has no unit cost, and that is a known live risk.** The soldier survives, so denial is repeatable and limited only by the 1000-tick (~40 s) delay. One rifleman can walk an enemy base turning every AA gun, SAM, airfield and silo Neutral. A Neutral defence never fires but still holds its footprint, so the former owner can neither use nor rebuild there without spending a technician — and `CaptureCoordinatorBotModule` has **no logic to reclaim its own neutralised structures** and is capped at three technicians. Against a bot this is close to unanswerable; tracked in `WORKSPACE/bugs/discovered.md`.

## Ejected vehicle crew burn to death — intended, not a defect

**User ruling, 2026-08-10:** *"The crew is supposed to burn sometimes, when the vehicle is heavily damaged… sometimes it just looks cool (in a dark way) to see your enemies crawling out of the vehicle only to burn and die."* A fix was built and deliberately **reverted at `36ad9865`**, with the `CrewFireDurationTicks` knob removed rather than left dormant — the symbol exists nowhere in code or YAML today.

The mechanism, for anyone who rediscovers it and assumes it is a bug: `VehicleCrew.cs:359-362` grants `onfire` to each ejected crewman with **no duration**, unlike the `VehicleCookoff*` warheads (`weapons/weapons-explosions.yaml`), which set one — note these are *weapons*, not a trait, and the variants vehicles actually reference are `VehicleCookoffTiny` (`Duration: 25`, `:46`) and `VehicleCookoffLarge` (`Duration: 150`, `:68`). Against `ChangesHealth@BurnDamage_3` (`infantry.yaml:964-968`, −1% MaxHP every 8 ticks while `onfire == 3`), an undurated grant means **every ejected crewman dies eventually; the randomness is only in *when***. Noted without reopening: the ruling says "burn *sometimes*" while the mechanism is "always, eventually" — if that gap matters it will surface as a gameplay observation, not a test failure.

**Binding consequence for tests:** an evacuation phase must assert **who got out**, never **who is still alive**. Post-ejection survival is not a property the game guarantees, so a survivor-count assertion is a coin flip that no threshold can stabilise (the 12 → 8 → 6 walk of 2026-05-09 was three attempts at exactly that). `test-evac-suite` is built this way — it counts a **peak** crew delta (`out = peak - before`), so later burn deaths cannot move the number.

## Related reference

- [`supply-route.md`](supply-route.md) — canonical Supply Route mental model
- [`economy.md`](economy.md) — supply/ammo economy details
- [`architecture.md`](architecture.md) — engine layout, custom traits, modified systems
