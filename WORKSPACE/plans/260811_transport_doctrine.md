# Transport doctrine — survivable lift, continuous employment, and a human pickup order

**Design + recon only. No behaviour changed by this document.**

Researched against `main` @ **`5eddff63`** (`git status -sb`: `main...origin/main [ahead 4]`; `git rev-list --count HEAD..@{u}` = 0 ⇒ not behind upstream; tree clean apart from untracked `.maestro/` scratch and one HTML status file). Static analysis only — **no game runs, no autotests** (the harness is held by another worker). Every claim about current behaviour carries a `file:line` read at that SHA. Where a fact cannot be established statically it is marked **NEEDS A LIVE LOOK** and is never used to justify a stage.

**Read first, not restated here:** [`WORKSPACE/recon/260808-transport-census.md`](../recon/260808-transport-census.md) (`f819d646`) — what carries whom, capacity facts, the two-layer order map, the poach hazard. This document sizes *doctrine* on top of that census and corrects it in three places (§0.3).

**Timestep** 60 ms ⇒ 16.667 ticks/s (`mods/ww3mod/mod.yaml:369-372`); `seconds = ticks × 0.06`.

---

## 0. Headline — read this before the stages

### 0.1 Three of the user's four requirements already have shipped machinery. We do not know if any of it fires.

| User requirement | Shipped machinery | Profile | Fires today? |
|---|---|---|---|
| Safe **landing site** | `PickRiskWeightedDropZone` (`HelicopterSquadBotModule.cs:1122-1179`) — scores candidate drop cells on believed air+ground danger | `@experimental` only (`RiskWeightedDropSite: true`, `ai.yaml:1791`) | yes, but it is a **weight, not a filter**, and every candidate is an enemy-side cell — §2.2 |
| Safe **route** | **nothing** | — | **no. This is the real gap** — §2.1 |
| **Evacuate** when not needed | `EvaluateIdleTransport` → `Evacuate` → `RotateToEdge` (`:1533-1583`, `:1739-1759`) | `@experimental` only (`EvacuateIdleTransports: true`, `ai.yaml:1816`) | structurally yes — §3.1 |
| **Ferry technicians** to derricks (item 35) | `CaptureCoordinator.TryFerryCapture` → `MountedTransport.TryReserveCaptureFerry` (`CaptureCoordinatorBotModule.cs:1317-1331`, `MountedTransportBotModule.cs:243-284`) | **BOTH** (`UseTransportForDistantCaptures: true`, `ai.yaml:186` and `:1893`) | unknown — §5 |

The user's report is *behavioural*: "most technicians are still just walking", "transports are basically doing nothing of use", "transports that are just sitting idle". Against the table above, at least two of those are reports that **existing enabled code is not producing its intended effect**. Writing new features on top of unfired old ones is how this subsystem has already burned three rounds (`supply-route.md:98`, `:143` records the same shape on the truck side: two scenarios green throughout while the behaviour never happened once in a 30-minute match).

**Consequence for staging: Stage 0 is diagnosis, and it is not optional.** It is also cheap — §1 shows the log lines mostly already exist.

### 0.2 The two requirements are in direct tension, and the plan must resolve it explicitly

> "they must be careful, these missions are easily ruined by a single AA missile" … "It would be nice if they work continuously to transport"

Continuous employment multiplies exposure. A transport that always has a mission flies more sorties through the same AA. If the only two states available are **flying a lift** and **evacuated**, then on a contested map "never idle" converts directly into "fleet deleted", and the user's first sentence loses to his second.

**The plan's answer: employment must include a third state that is neither flying a delivery nor being disposed of** — a *rear hold*: alive, retained, positioned to serve the next demand, deliberately not crossing the danger field. That state does not exist today (§3.2). Getting it is most of Stage 3's value, and it is why Stage 2 (route safety) is sequenced **before** Stage 3 (employment) rather than after: raising sortie rate before the route is survivable makes the user's stated primary complaint worse.

### 0.3 Corrections to the brief and to the census

Three load-bearing statements in the task brief and the prior census do not survive reading the code. Recorded here because each one would have mis-aimed a stage.

1. **`ForwardStaging` is not the transport problem — it is attack-heli-only.** The brief calls its danger-blind `Move` "very likely the single biggest contributor to the 'transports fly into AA and die' problem". `StageIdleHelicopters` filters to `AttackHeavy`/`AttackLight` and `continue`s on everything else (`HelicopterSquadBotModule.cs:708-713`), so it never touches a transport. It *is* danger-blind — `ForwardStagingCell` (`:737-751`) consults no field, and the order is a bare `Move` (`:725`) — and it *is* on `@stable` (`ai.yaml:1685-1687`), so it is a real finding; it just belongs to the attack-heli file, not this one. **Recommendation: split it out** (§2.4).
2. **The actual danger-blind transport seam is `DispatchTransportDelivery`** (`:1264-1291`). The delivery is a three-leg chain of bare orders: `Move` to the drop zone (`:1273`), queued `Unload` (`:1276`), queued `Move` home (`:1280`). No field is read on any leg. An aircraft `Move` is a straight line, so this is literally "fly the shortest path into the drop zone and back out again", twice through whatever lies between.
3. **`@stable` heli lift is NOT starved to zero.** The census (§1.2, §0.3 headline 5) calls `TransportMissionSlots: 0` on `@stable` "a permanent starve". The gate is conditional: with the slot count at 0 the launcher falls through to `activeSquads.Count >= Info.MaxActiveSquads` (`:1025-1026`, `MaxActiveSquads: 3` at `ai.yaml:1674`). A transport mission never increments `activeSquads`, so lift is blocked **whenever three attack squads are live** — not always. Early game, and any time the attack loop is below three squads, `@stable` can and does launch lifts. **This inverts the `@stable` risk assessment**: heli-transport changes are *not* inert on the benchmark control. See §6.

---

## 1. Stage 0 — diagnosis. Make the four behaviours observable, change nothing.

**What the player sees change:** nothing. This stage emits log lines only.

**Why first:** §0.1. Three of four requirements have enabled code whose firing is unverified, and one of them (`ferried=`) is *already* logged unconditionally today.

### 1.1 What is already observable without any code change

`CaptureCoordinatorBotModule.cs:1308-1309` writes, unconditionally (plain `Log.Write("debug", …)`, not behind a `DebugLogging` flag), on every capture order:

```
[exp-capture] issue player=… actor=tecn.america@X,Y → oilb@X,Y score=… ferried=False tick=…
```

**`ferried=False` on a target ≥12 cells away is the entire item-35 diagnosis.** One live match answers whether the ferry is never attempted, attempted and refused, or attempted and succeeding but slower than walking. No instrumentation needed. **Do this before writing a line of item-35 code.**

The heli side is thinner but non-empty: `AIUtils.BotDebug` fires at lift dispatch (`:1289-1290`), load-abort (`:1223-1224`), forward-staging (`:730-731`) and evac (`:1757-1758`). `AIUtils.BotDebug` is gated on the engine's bot-debug flag rather than always-on.

### 1.2 What to add

Follow the precedent the supply subsystem was forced into (`supply-route.md:152`: "three rounds of work on this subsystem were tuned blind… 'never dropped' and 'never logged' were the same silence"). Make the *refusals* unconditional, not the successes:

| Line | Answers |
|---|---|
| `[lift] launch-declined reason=…` in `TryLaunchTransportMission` — one reason per early return: `SlotBusy` / `NoSquadMgr` / `NoTransport` / `NoCargo` / `BelowMinPax` / `NoDropZone` | why lift never launches. Six `return`s today (`:1020-1026`, `:1028-1029`, `:1041-1042`, `:1046-1047`, and the pax/dropzone gates) all exit **silently** |
| `[lift] employ actor=… ticks=… demand=… slotFree=… launchable=… → Employ\|Evacuate` in `EvaluateIdleTransport` (`:1580`) | whether a transport is being held, or retired, and on which term |
| `[ferry] refused reason=NoCarrier\|NoSR\|TooNear\|ModuleNull` in `TryReserveCaptureFerry` / `TryFerryCapture` | turns `ferried=False` from a symptom into a cause |

Log on **change of reason plus a periodic roll-up**, the shape `supply-route.md:158` settled on — a per-scan line at `ScanInterval: 50` would flood.

**Risk:** log volume. Mitigated by the change-plus-rollup shape.
**Verification:** one live match per profile; read the log. No autotest needed and none should be written for this stage.
**`@stable` impact:** none — logging only. Note the `[lift]` lines *will* appear for `@stable` (§0.3 correction 3), which is the point.

---

## 2. Stage 1 — survivable routing and a landing site that is actually safe

**What the player sees change:** transport helicopters stop taking the shortest line into a defended drop and dying to the first SAM. They arrive by a flanking leg, and they set down *short of* the objective in a cell outside believed AA, instead of on top of it. A lift with no survivable approach is not flown at all rather than flown and lost.

### 2.1 The route — there is a near-exact template in the same file

The primitives already exist, are pure, are NUnit-pinned, and are already bound to the right field **inside this very module**:

- `HeliDangerNav.PathMaxAirDanger(from, to, sampler)` (`HeliDangerNav.cs:47-64`) — worst believed air danger along the straight cell line. This *is* "how exposed is this flight path".
- `HeliDangerNav.DetourWaypoint(from, to, lateralCells, threshold, sampler)` (`:102-142`) — returns a lateral waypoint that lowers worst-case exposure of the two-leg route, or `null` for "fly direct".
- `HeliDangerNav.LeashedEngageCell(target, leash, threshold, sampler)` (`:71-95`) — expanding-ring search for the nearest cell to a target whose danger is at or below threshold. Named for engagement; **it is exactly the safe-LZ primitive** and needs no modification.
- `HeliDangerNav.SafestAirCellOnRing(origin, ring, sampler)` (`:148-173`) — the withdraw target.

All four are zero-RNG, integer-only, deterministic by fixed candidate order (`HeliDangerNav.cs:18-21` states the contract).

Consumers today are the attack squads (`HelicopterStates.cs:616`, `:619`, `:844`) and the careful-**scout** path (`HelicopterSquadBotModule.cs:967`). **Nothing in the transport path calls any of them** (verified by grep across `engine/**/*.cs`).

The sampler is already constructed in this module for the scout:

```csharp
scoutAirDangerAt = c => world.Map.Contains(c) ? dangerField.AirDanger(player, c) : HeliDangerNav.Impassable;
```
— `HelicopterSquadBotModule.cs:874`

**The template to copy is `CarefulScoutEmployment`, and it is a very close fit.** It exists because a `littlebird` is "a fragile troop-carrier/scout, not a strike aircraft" (`ai.yaml:1761-1766`) — which is the transport case, only more so. Its gate (`:962-971`) rejects a candidate when the *destination* is hot, the *path* is hot, or the leg is too deep, via `ReconSafetyMath.Acceptable(destAir, pathMax, threshold, distSq, capSq)` (`:1881-1894`). Three conditions, all must pass, and it is a **filter** — a rejected candidate is simply not chosen.

**Seam:** `DispatchTransportDelivery` (`:1264-1291`). Insert between the straggler stand-down (`:1268`) and the head `Move` (`:1273`): compute `DetourWaypoint(transport.Location, task.DropZone, …)`; when non-null, issue it as the head `Move` and make the drop-zone `Move` `queued: true`. The existing comment at `:1270-1272` — "THREE-LEG CHAIN, and the head is the only suppressible leg… All-or-nothing" — is a constraint the change must honour: a detour makes it a **four**-leg chain and the head moves to the waypoint, so the all-or-nothing bail on a dropped head order must still cover the whole chain.

### 2.2 The landing site — the current picker cannot produce a safe one, and the reason is the candidate set

`PickRiskWeightedDropZone` (`:1122-1179`) does read both danger channels (`:1144-1145`) and does score them (`TransportDropSiteMath.ScoreDrop`, `:1150`). Two structural limits mean it cannot satisfy "find safe landing sites":

1. **It is a weight, not a filter.** It returns the argmax over candidates (`:1153-1157`); there is no floor. If every candidate is lethal it returns the least-lethal lethal one. This is deliberate and *load-bearing elsewhere* — the comment at `:1571-1575` records that `EvaluateIdleTransport` omits the drop-zone precondition from its launchable proxy **only because** the picker is a weight and therefore effectively never returns null. **Turning it into a filter without touching that residual re-opens the "Employ shadows Evacuate" pin that adversarial review of `fd3bc036` found.** These two must change together or not at all.
2. **Every candidate is an enemy-side cell.** The set is `{FindWeakestEnemyCell}` ∪ `{top N believed offensive POIs}` (`:1160-1176`). There is no candidate that is a *safe cell near* an objective. So the picker can rank enemy positions by risk but can never propose an LZ short of one.

**Fix, and it is small:** wrap the chosen cell in `LeashedEngageCell(chosen, LiftLandingLeashCells, threshold, airSampler)` before it becomes `task.DropZone`. The existing ring search then walks the drop outward to the nearest cell at or under threshold, returning the target unchanged when it is already safe (`HeliDangerNav.cs:73-74`) and falling back to the target when no safe cell exists inside the leash (`:94`). That fallback is where the filter question from limit (1) has to be answered: **refuse the mission**, or **fly to the least-bad cell**. Recommendation: refuse, and make the refusal a `[lift] launch-declined reason=NoSafeLZ` line — but only in the same change that folds the precondition into `EvaluateIdleTransport`'s `launchable` (`:1578`), per limit (1).

Note the passengers walking the last stretch is *correct* and has precedent: `supply-route.md:169-176` establishes for the truck side that a short walk to a standoff drop is the doctrine working, and the hazard is only the *rearward* walk. Same logic applies here.

### 2.3 Threshold values — deliberately not specified

Per the brief and `DISCOVERIES` `f2a31035`, the danger field's thresholds are being re-derived on a parallel branch. **This plan therefore specifies which signal is consulted and leaves every number to that work.** Concretely, the new fields (`LiftRouteAirDangerThreshold`, `LiftLandingLeashCells`, `LiftDetourLateralCells`) should ship with C# defaults that make the behaviour **inert** (threshold sentinel = "disabled", leash 0), so the merge order between this and item 40 does not matter. The one existing datum: `AirDangerSpikeUnits: 25` is set identically on both twins (`ai.yaml:1684`, `:1718`) and is the Stage-D withdraw level in normalised danger units — a reference point, not a value to copy.

### 2.4 Split out: `ForwardStaging`'s danger-blind `Move`

Stated plainly, as the brief asks. **Split it out.** It is attack-heli-only (`:712-713`), so it is not in this feature's blast radius; it is on `@stable` (`ai.yaml:1685-1687`), so touching it moves the benchmark control; and it is genuinely unobserved in a live game. Bundling an `@stable`-affecting attack-heli change into a transport-doctrine merge makes the benchmark delta unattributable. It deserves its own item, sized against the same `ReconSafetyMath` template.

**Risks in this stage**
- The detour is computed once at dispatch against a field that moves. A SAM revealed mid-flight is not re-routed around. Accepted for Stage 1; the re-route belongs with `FlightPathHysteresis` (`ai.yaml:1783`) and the Stage-D withdraw machinery, and adding it here would couple two tunings.
- `DetourWaypoint` picks perpendicular offsets of the **midpoint** only (`:114-127`). Against an AA belt that spans the whole approach it will find no improvement and return null, and the mission flies direct into it. The LZ leash and the refusal in §2.2 are what actually protect that case — not the detour.
- Off-map waypoints are already handled: the sampler maps out-of-bounds to `Impassable` (`:874`), so the search cannot steer off the playable area.

**Verification:** NUnit for any new pure math (the existing `HeliDangerNavTest.cs` / `ReconSafetyMathTest.cs` are the pattern; no new math class may be needed at all). Behaviourally, a scenario with a known AA emplacement between the SR and a drop POI, asserting the transport's path max air danger stays under threshold — but see §7 on autotest budget.

**`@stable` impact: REAL, per §0.3 correction 3.** `@stable` flies lifts whenever `activeSquads.Count < 3`. Route safety would change `@stable` lift behaviour. Per `CLAUDE.md`, that is *allowed* (improvement flows to `@stable`) but must be **announced in the commit message** so the next baseline is re-taken knowingly. It must not arrive silently.

---

## 3. Stage 2 — never idle. First find out what "idle" even means here.

**What the player sees change:** dedicated transports stop parking. Between lifts they sit in a rear hold rather than at the SR corner or wherever they last unloaded, and a transport with genuinely nothing left to do retires and refunds instead of decorating the map.

### 3.1 What is currently causing idleness — and the trap the brief names is real

The brief warns that a fallback firing on a broken idleness test fixes nothing. That trap is live and documented: `Actor.IsIdle` is `CurrentActivity == null` (`Actor.cs:75`) and `Actor.Tick` runs a newly-queued activity in the **same** tick (`Actor.cs:290-299`), so for an airframe carrying `FlyIdle` it is never observable. `AIUtils.IsUnoccupiedAirframe` (`AIUtils.cs:45-51`) is the replacement, and the transport paths already use it: `IsUnoccupied` (`:1587`) is called by `EvaluateIdleTransport` (`:1546`) and by `StageIdleHelicopters` (`:702`).

**So the idleness test itself is already fixed. Idleness is therefore not being caused by a broken test — it is being caused by employment never being offered.** The candidate causes, in the order Stage 0 will distinguish them:

| Candidate cause | Where | How Stage 0 tells |
|---|---|---|
| Lift never launches — slot gate | `:1020-1026`; `@stable` blocked while 3 attack squads live | `[lift] launch-declined reason=SlotBusy` |
| Lift never launches — no passengers pass `IsLiftCandidate` | `:1614-1642`; requires ≤14 cells from the SR **and** role `MainBattle` **and** uncommitted **and** unreserved | `reason=BelowMinPax` |
| The 14-cell bubble is one-shot | `:1641` + `LiftHomeCell` `:1650`; census §0.3.4 — a soldier past 14 cells from home can never be lifted again | `reason=BelowMinPax` with a live map showing infantry forward |
| No drop zone resolves | `:1063-1064` per census | `reason=NoDropZone` |
| Health bench | `IsReadyForMission`; `tran`/`halo` ship `ReEngageHealthPercent 90` with no repair host (`:1564-1567`) | `[lift] employ … launchable=False` |

**The 14-cell reserve bubble is the structural one and it is the census's central finding.** No amount of employment logic reaches a transport whose only legal passengers are within 14 cells of home. Widening it is *not* free — the comment at `:1602-1604` is explicit that the bubble is what stops the lift stripping the front line. Any widening needs a compensating claim rule, which is exactly the demand-publication layer item 34 asks for and which this plan does **not** attempt (§8).

### 3.2 What a transport with no work should do — and why "evacuate" is the wrong default

The user names evacuation. The machinery is real and reusable exactly as the brief hoped:

`Evacuate(h)` (`:1739-1759`) queues `new RotateToEdge(h, true, h.GetSellValue())` (`:1741`) and then removes the airframe from **every** management set — `evacuating`, `idleHelicopters`, all squads, `managedHelicopters`, `stagedTo`, `idleTicks`, blackboard (`:1746-1755`) — specifically so nothing re-tasks it and cancels the evac. `RotateToEdge` handles both airframes and ground units and refunds through `GetSellValue`. The decision is `TransportEmploymentMath.Decide(ticks, TransportIdleEvacuateTicks, hasDemand && launchable, slotFree)` (`:1580`), and employment already outranks retirement by construction (`:1531-1532`) — the same "commitment outranks evac" shape the truck side settled on (`supply-route.md:133-148`).

**Reuse it. Do not extend it. And do not make it more eager.** Two reasons:

1. **It is terminal.** `ai.yaml:1804` states it: "Terminal; no hold-and-recheck." An evacuated transport is gone. The trigger is `TransportIdleEvacuateTicks: 900` = **54 s** (`ai.yaml:1817`) of no demand. In a game where the front moves and the reserve bubble is 14 cells wide, "no lift demand for 54 seconds" is an extremely weak proxy for "never needed again". The user's complaint is *idle transports*; the remedy for idle is employment, not disposal.
2. **The money pump.** `ai.yaml:1810-1814` records that `TransportMissionSlots` and `EvacuateIdleTransports` are **coupled** and must ship together — with slots at 0, `Employ` is unreachable and every transport evacuates at its window. Item 33 closed the buy half at `UnitBuilderBotModule.cs` (`ShouldBuyTransport` counting via `IsUnoccupiedAirframe`). **Any change to the evac trigger re-opens this**, and the brief's own caution says so.

**Recommendation — the third state from §0.2.** Before `Evacuate`, a transport with no current lift should take a **rear hold**: a `Move` to a cell chosen by `SafestAirCellOnRing` around the SR (or around the last drop), which keeps it alive, out of the danger field, and available. Evac then becomes what it should be — the terminal branch after a *sustained* hold with no demand — rather than the first answer to a 54-second gap. This is one new state and one reuse of an existing primitive.

**Ground carriers are a separate question and mostly should not evacuate.** `MountedTransportBotModule` has **no evacuation path at all** (grep for `RotateToEdge`/`Evacuate` in that file returns nothing). That is arguably correct: `CarrierTypes: bradley, bmp2, m113` (`ai.yaml:1372`) are armed IFVs, not dedicated transports, and the user scoped his request to "at least the dedicated transports that are not good for anything else". `tran`/`halo` are the dedicated ones. **Recommendation: leave the ground side out of the evac stage entirely.**

**Risks:** the money pump above; the hold cell becoming a new congregation point that `LayeredDefence`/`GarrisonBotModule` see as claimable (census §5 — "a rendezvous manufactures exactly the state that is maximally poachable"), though this applies to the *airframe*, which those consumers do not recruit; and the terminal-evac decision being taken on a demand signal that the 14-cell bubble makes unrepresentative.

**Verification:** Stage 0's `[lift] employ` line, over a full match, on both profiles.

**`@stable` impact:** `EvacuateIdleTransports` is `@experimental`-only (`ai.yaml:1816`; absent from the `@stable` block which ends at `:1688`), so the evac branch is inert on `@stable` **provided the new rear-hold state is gated the same way**. A rear hold added ungated to `EvaluateIdleTransport` would still be inert, because that method early-returns on `!Info.EvacuateIdleTransports` (`:1535-1536`). Keep it inside that guard and `@stable` is untouched by this stage.

---

## 4. Stage 3 — the human pickup order

**What the player sees change:** you select infantry, right-click a transport, and the transport drives to them, stops a few cells short, waits while they board, moves to the next group if some are further off, and then carries on with whatever you had shift-queued for it. Today it does not move at all — the soldiers walk the whole way, and anything you had queued on the transport is silently discarded the moment the first soldier reserves a seat.

### 4.1 The seam, and it is not where you would guess

The order belongs to the **passenger**, not the transport. `Passenger.IIssueOrder.Orders` yields one `EnterAlliedActorTargeter<CargoInfo>` with `OrderID "EnterTransport"` (`Passenger.cs:85-93`); `IssueOrder` returns `new Order(order.OrderID, self, target, queued)` with `self` = the soldier (`:97-103`); `IResolveOrder.ResolveOrder` queues `new RideTransport(self, order.Target, …)` on the soldier (`:207`). The transport is only ever the *target*.

The transport's sole involvement is passive and hostile to this feature. `Passenger.Reserve` → `Cargo.ReserveSpace` (`Cargo.cs:371-396`) calls `LockForPickup` (`:393`), which:

- sets `state = State.Locked` (`Cargo.cs:417`),
- **calls `self.CancelActivity()` on the transport** (`:419`),
- lands it if airborne (`:421-426`),
- queues `new WaitFor(() => state != State.Locked, false)` (`:428`).

`ReleaseLock` (`:431-443`) fires only at `reservedWeight == 0` (`:405`), then queues `Wait(Info.AfterLoadDelay)` and a `TakeOff` if it had been flying (`:438-440`).

**Two consequences, both design constraints:**

1. **The transport is pinned in place by design.** Not a bug to route around — `LockForPickup` deliberately freezes it so passengers have a stationary entry frame. Any "drive to the pickup" behaviour must happen **before** the first reservation lands, or must change the lock's semantics. The former is far safer.
2. **The player's shift-queued transport orders are destroyed today.** `self.CancelActivity()` at `Cargo.cs:419` wipes the transport's activity queue when the *first* soldier reserves. So "when all are done, it continues with its queue of orders" is not a behaviour to preserve — **it is a behaviour to restore.** The user is describing a repair, not an addition.

This is the same class as the known prior defect the brief flags (a shift-queued order walking a unit out of an ambush hold): an unrelated mechanism silently discarding queued player intent. Here it is worse, because the discard is unconditional and happens on someone *else's* order.

**There is no precedent anywhere in the engine for a transport moving to meet passengers** — searched `Activities/` and `Traits/`; `Land`/`LandOnTarget` move an aircraft to a landing spot but are queued *after* `LockForPickup` has frozen it, and `UnloadCargo` is the reverse direction. Confirmed greenfield, and it matches the census's §6 finding ("no code anywhere moves a carrier to a computed assembly cell and holds it there").

### 4.2 Shape

Engine-side, and the ordering is what makes it safe:

1. On `EnterTransport` resolve, **before** `RideTransport` reserves, notify the transport of an inbound pickup (a small trait on the cargo side collecting pending passenger actors within the tick, plus the queued-ness of the originating order).
2. The transport computes a pickup anchor from the pending set, and issues itself a `Move` to `standoff` cells short of it — `queued: false` if the *player's* order was unqueued, `queued: true` if it was shift-queued. This is the seam where `Order.Queued` (`Actor.cs:381-387`: `queued == false` ⇒ `CancelActivity()` then queue; `true` ⇒ append) must be honoured, and it is the one the current code ignores.
3. Passengers hold rather than walking the full distance; they board when the transport arrives. Reservation — and therefore `LockForPickup`'s `CancelActivity` — happens only at that point.
4. "Wait until no more pickups are within those cells": a bounded dwell after the last boarding, then re-anchor on any remaining pending passenger, else release.
5. On release, **restore** what `LockForPickup` cancelled. This requires snapshotting the transport's activity queue before the cancel, which is the single most invasive part of the change.

**Risks — this is the highest-risk stage in the plan, by a wide margin.**
- `Cargo.cs` and `Passenger.cs` are **shared with every bot module and every profile**. Item 33's history is the calibration: waking a dormant path made the whole path new code, and `.Take(cargo.Info.MaxWeight)` — harmless while unreachable — ordered 36 soldiers aboard a mission dispatching at 4. Anything here lands on `MountedTransportBotModule`, `HelicopterSquadBotModule`, the capture ferry, garrison loading and human play simultaneously.
- Step 5 (queue restore) has no precedent and changes long-standing engine semantics.
- Multiplayer determinism: the pickup anchor must be computed from order-resolution state only, with zero RNG, or clients desync. `HeliDangerNav`'s determinism contract (`HeliDangerNav.cs:18-21`) is the standard to meet.
- A transport driving to a pickup is a transport *not* doing what its owner last told it. Getting the `Queued` semantics backwards here is a worse user experience than the status quo.

**Recommendation: this stage should be its own merge, reviewed adversarially, and it should be the LAST of the four to land** despite being the one the user asked about first. It is the only stage that can regress bot behaviour on both profiles and human play in one commit. Stages 0–2 are additive and profile-scoped; this is not.

**`@stable` impact: unavoidable and total.** There is no gating seam — `Cargo`/`Passenger` are engine traits on every actor. A behavioural change here moves `@stable`, humans, and every bot module at once. Per `CLAUDE.md` that is permitted but must be called out in the commit message; per the benchmark freeze, **this stage should not land inside the freeze window at all.**

---

## 5. Stage 4 — derrick-rush ferrying (item 35)

**What the player sees change:** in the opening minutes, technicians ride to distant money structures instead of walking, and the transports that are "doing nothing of use" are the ones carrying them.

### 5.1 This is a diagnosis, not a feature — and it is not blocked by item 34

The ferry is **built and enabled on both profiles**. `CaptureCoordinatorBotModule.IssueCaptureOrder` computes `var ferried = Info.UseTransportForDistantCaptures && TryFerryCapture(bot, capturer, target)` (`:1296`) and issues the on-foot `CaptureActor` order only when the ferry refused (`:1297`). `UseTransportForDistantCaptures: true` with `TransportCaptureMinDistanceCells: 12` appears on `@experimental.tecn` (`ai.yaml:186-187`) **and** `@stable.tecn` (`ai.yaml:1893-1894`).

**PIPELINE's stated hard dependency on item 34 does not hold as written.** The recorded reason is that "'ferry the technician to the derrick' *is* a demand statement — a named passenger with a named destination — so it has nowhere to be expressed until item 34's demand-publication layer exists." That channel already exists and already carries exactly that: `TryReserveCaptureFerry(bot, capturer, target)` (`MountedTransportBotModule.cs:249`) takes a named passenger and a named destination, and sets the carrier task's destination to `target.Location` — which the census itself identifies as "the **only** transport path in the codebase whose destination is a real objective handed in from outside" (census §1.3). The dependency was written believing the channel had to be invented; it was invented already, in the same commit series the census documents. **Recommendation: reopen the 34-before-35 ordering.** Stage 4 as scoped here is diagnosis plus tuning of a live path and does not need the demand layer.

### 5.2 Why it may not be firing — ranked, all statically visible, none yet confirmed

`TryReserveCaptureFerry` (`MountedTransportBotModule.cs:249-284`) refuses when any of these holds, and **every refusal is silent**:

1. **No free carrier.** It wants an owned, alive, in-world actor whose name is in `CarrierTypes: bradley, bmp2, m113` (`ai.yaml:1372`), **not already in `carrierTasks`**, with a `Cargo` that `IsEmpty()` (`:262-271`). In the opening minutes the bot may own none of these at all, and any it does own are competing with the frontline shuttle for the same `carrierTasks` slot. **This is the most likely cause and it directly matches the user's words** — "some transports are basically doing nothing of use" describes a carrier the shuttle has claimed but not usefully employed.
2. **No SR resolved** — `FindOwnSupplyRoute()` returning null bails (`:257-259`).
3. **Distance under 12 cells** — walks by design (`CaptureCoordinatorBotModule.cs:1320-1321`).
4. **The one-shot module latch.** `transportModuleResolved` (`:1323-1328`) resolves `MountedTransportBotModule` once and caches the result — **including `null`** — for the rest of the match. If the first capture order is issued on a tick where the transport module is trait-disabled, the ferry is dead permanently. The enable conditions are paired (`CaptureCoordinator@experimental.tecn` and `MountedTransport@experimental` both `enable-ai-experimental`; `@stable.tecn` and `@poi` both `enable-ai-stable` — `ai.yaml:112`, `:1403`, `:1858`, `:1366`), so they *should* enable together. **NEEDS A LIVE LOOK** — I could not statically establish the relative tick of first-capture versus module enable, and this is stated as a hazard to check, not as a defect found.

Note the carrier is chosen as nearest to the **capturer** (`:279-283`), with no SR-bubble gate — deliberately unlike every other transport path (census §1.3). So the geometry is not the problem here.

### 5.3 The TECN claim-registry overlap the brief asks about

The brief's hazard is **already answered and should not be re-litigated.** PIPELINE item 35 records that `09877fd5` (2026-08-08) went after the same overlap from the other end and found it worse than the census described: `GarrisonBotModule`'s gate contained no enemy, danger, belief or POI term at all, so it grabbed an arbitrary rear civilian house on the bot's first tick — and no bot module can unload a garrison, making the technician unrecoverable for the match. That is fixed, along with `CaptureCoordinator` discarding its undispatched remainder with no order and no claim — which was manufacturing exactly the idle unclaimed unit garrison then recruited.

**What remains true and must be respected:** a successful capture **consumes** the technician (`ConsumedByCapture: true`, `infantry.yaml:903`; `game-model.md:33-35`). The technician pool is a consumable, not a squad, and with a limit like `tecn: 3` it is availability — not coordinator logic — that binds. **Any ferry tuning that increases capture *throughput* burns the pool faster.** A stage that successfully ferries technicians to three derricks in the opening minutes and then has no technicians is a legitimate outcome, not a bug, but it must be an intended one.

**Risks:** carrier contention with the frontline shuttle (cause 1) is a real allocation decision, not a bug — ferrying a technician and shuttling infantry both want the same three hulls, and picking the ferry first is a doctrine choice the user has effectively already made ("capturing the derricks is priority number one"). Making it explicit is the actual content of this stage.

**Verification:** the existing `[exp-capture] issue … ferried=` line plus Stage 0's `[ferry] refused reason=`. Existing autotest `test-tecn-ride` covers the happy path end-to-end (census §7) and must stay green.

**`@stable` impact: REAL.** The ferry is enabled on `@stable.tecn` (`ai.yaml:1893`). Any change to ferry firing rate moves the benchmark control and must be announced.

---

## 6. `@stable` exposure summary — for the benchmark freeze

Flagged as the brief requires. The `@stable` heli block is short: `ai.yaml:1666-1688`. Everything from `:1693` to `:1817` is `@experimental`.

| Stage | Moves `@stable`? | Why |
|---|---|---|
| 0 — diagnosis | **No** | logging only |
| 1 — route + LZ | **Yes** | `@stable` flies lifts whenever `activeSquads.Count < 3` (§0.3 correction 3). Not inert |
| 1b — `ForwardStaging` fix (**split out**) | **Yes** | `ForwardStaging: true` at `ai.yaml:1685-1687` |
| 2 — never idle / rear hold | **No**, if kept inside the `EvacuateIdleTransports` guard (`:1535-1536`), which `@stable` does not set | gated |
| 3 — human pickup order | **Yes, totally** | `Cargo`/`Passenger` are engine traits on every actor; no gating seam exists |
| 4 — derrick ferry | **Yes** | `UseTransportForDistantCaptures: true` on `@stable.tecn` (`ai.yaml:1893`) |

**Recommendation given a freeze about to be taken:** land Stage 0 now (free), hold Stages 1, 1b, 4 until the baseline is taken or accept and announce the drift, and keep Stage 3 out of the freeze window entirely.

Per `CLAUDE.md`, none of these should be *gated off* to withhold the improvement from `@stable` — the rule is against silent drift, not against improvement. Each must simply say so in its commit message.

---

## 7. Autotest budget

The harness is held; nothing in this plan was run. Existing coverage that any of this must keep green (census §7): `test-tecn-ride` (the only bot-driven ferry test; asserts a four-stage latch), `test-spread-cargo-no-enter` (a **negative** test — three infantry must NOT end up in a BMP after a scatter; Stage 3 is the one most likely to break it), and `test-pips-zoom`.

New scenarios are warranted for Stage 1 (a transport routing around a known AA emplacement) and Stage 3 (a human-issued pickup with shift-queued follow-ups surviving). Stages 0 and 4 need **log reading, not tests** — their whole point is that the code already exists and its firing is unobserved.

---

## 8. What this plan deliberately does NOT do

- **It does not build the demand-publication layer.** Census §8.2 sizes that at 2–4 sessions across 4–6 files including `PoiOffensiveBotModule`, the largest and most regression-prone file in the AI. Nothing in the user's four requirements needs it: route safety, landing safety, employment and the human order are all expressible against destinations that already exist. Item 34's *pooling* ambition still needs it; item 34's *pickup* ambition (Stage 3) does not.
- **It does not widen the 14-cell reserve bubble** (§3.1). That is the demand layer's problem and widening it without a compensating claim rule strips the front line (`:1602-1604`).
- **It does not re-tune any danger threshold** (§2.3).
- **It does not touch the ground carriers' employment** (§3.2) — they are armed IFVs, outside the user's "not good for anything else" scope.

---

## 9. Open questions needing a live look

1. Does `@stable` ever actually launch a lift, and how often? Depends on `activeSquads.Count` over a match. Determines whether Stage 1 is a benchmark-moving change or nearly inert.
2. Is `ferried=False` on distant derricks caused by no free carrier, or by the `transportModuleResolved` latch (§5.2 cause 4)? One log read settles it.
3. Does any lift launch at all on either profile in a real match, or does `IsLiftCandidate`'s 14-cell bubble empty the pool? The census could not establish this either (census §9).
4. Does the mounted shuttle fire in a live match? Still open from census §9; the `[exp-transport]` lines at `MountedTransportBotModule.cs:561-564` would settle it.
5. Is the `TransportMissionSlots`/`EvacuateIdleTransports` money pump live now that lift launches? Open since item 33; the buy half is fixed, the slot arithmetic is unchanged.
