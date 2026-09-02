# The rest of the `ChangeOwnerInPlaceSync` blast radius — census and triage

**Date:** 2026-09-02 · **Branch:** `wt/ownership-blast-radius` · **Base:** `main @ 26f9cec0`
**Scope:** research only. No engine changes, no game launched, no autotest run (launch embargo).
One `make all` and one `dotnet test` were spent on a throwaway reflection probe, since deleted.

## Executive summary

The predicate is now **mechanically closed**, not guessed at. `ChangeOwnerInPlaceSync` differs from
`ChangeOwnerSync` in exactly two ways, and both are enumerable:

1. It skips `INotifyAddedToWorld` / `INotifyRemovedFromWorld` on the actor's own traits.
2. It skips the `World.ActorAdded` / `World.ActorRemoved` **events**, which are global subscriptions.

Both paths *do* fire `INotifyOwnerChanged`, on the actor's traits and on the WorldActor's traits
(`Actor.cs:558-562` vs `:585-589`). So the bug class is precisely "owner-dependent state rebuilt on
one of those two skipped channels".

A reflection probe over the loaded assemblies (method in §5) returns **37 concrete types** on
channel 1, and grep returns **8 subscription sites** on channel 2. That is the whole universe. I
read every one that survives a YAML-presence filter.

**Headline result: of the recon's three named candidates, one is a confirmed reachable bug, and
two are confirmed mechanisms whose symptom is unreachable in the shipped ruleset.** The recon's
author was right to flag them as under-verified — the mechanisms are all real, but "real mechanism"
and "reachable symptom" came apart on two of three, and in a way the priority ordering depended on.

The most important finding is **not** in the recon's list. `UnitLifecycleLogger` is on channel 2
and stamps owner at spawn, so every ownership flip is invisible to the telemetry the project makes
its balance and AI decisions from. That is a measurement-apparatus bug, and it silently biases
analysis rather than annoying a player.

| Instance | Channel | Mechanism | Reachable today? | Verdict |
|---|---|---|---|---|
| `SupportPowerManager` | 2 | Confirmed | **Yes — `MSLO` on `nuclear-winter-ww3`** | **Real bug, fix it** |
| `UnitLifecycleLogger` | 2 | Confirmed | **Yes — every garrison flip** | **Real, corrupts measurement** |
| `TechTree` | 2 | Confirmed | No — masked by `SUPPLYROUTE` | Latent; comment, don't fix |
| `ActorIndex` | 2 | Confirmed | No — no building in any bot type list | Latent; comment, don't fix |
| `ProximityExternalCondition` | 1 | Confirmed | Yes, but self-heals on movement | Low; fix opportunistically |
| `BaseProvider` | 1 | Confirmed | Dev-cheat only | Negligible |
| `ProductionTabsLogic` | 2 | Confirmed | Yes, but self-heals next tick | Cosmetic |
| `UnitDefaultsManager` | 2 | Confirmed | Yes | **Arguably correct as-is** |
| `GpsDot` | 1 | **Refuted** | — | Reads owner live |
| `SupplyProvider` | 1 | **Refuted** | — | Re-evaluates every scan |
| `Building` | 1 | **Refuted** | — | Not owner-keyed |
| `FrozenUnderFog` | — | **Resolved** | — | Deliberate, documented |
| 25 further probe hits | 1 | **Refuted** | — | No `Owner` ref, or absent from YAML |

---

## Q1 — The three named candidates, read personally

### `TechTree` — mechanism CONFIRMED, symptom MASKED. Do not schedule a fix.

The mechanism is exactly as the recon guessed. `TechTree`'s constructor subscribes to both events
(`Traits/Player/TechTree.cs:32-33`) and `ActorChanged` gates on `a.Owner == Owner` (`:39`). An
in-place flip fires neither event, so neither the loser's nor the captor's watchers re-run.

**But the recon's phrasing — "neither player's prerequisite set updates" — is wrong, and the error
matters.** There is no cached prerequisite set. `GatherOwnedPrerequisites` (`:72-112`) walks
`ActorsWithTrait<ITechTreePrerequisite>` filtered by live owner on **every** call, and
`HasPrerequisites` (`:65-70`) calls it directly. Anything that asks "do I have this prerequisite
right now" gets a correct answer even with the events missed. The only stale state is
`Watcher.hasPrerequisites` / `hidden` (`:123`, `:125`), two booleans whose sole job is edge-detecting
so `PrerequisitesAvailable` / `Unavailable` fire once (`:188-192`). The staleness is therefore
confined to *UI notification callbacks*, not to buildability.

**Reachability, and why it is nil.** I traced every prerequisite that gates a real unit:

- `~player.*`, `~techlevel.*` — provided by the **player actor** (`player.yaml:208-224`, `:236-267`).
  Never changes hands.
- `infantry.*`, `vehicles.*`, `aircraft.*` — provided by **`SUPPLYROUTE`**
  (`structures.yaml:372-390`, inside the actor opening at `:222`). `SUPPLYROUTE` inherits only
  `^ExistsInWorld`, `^SpriteActor`, `^SelectableBuilding` (`:223-225`) — **not** `^BasicBuilding`,
  so it carries no `Capturable` and is not garrisonable. Its owner never changes.
- `HPAD` (`:629-639`) and `AFLD` (`:699-709`) are capturable (`Inherits: ^Building` → `:69`
  `^BasicBuilding` → `:10` `^NeutralOrOccupiedCapturable`), but they provide only `aircraft.*`,
  which `SUPPLYROUTE` already provides. Capturing or losing one changes no prerequisite the player
  did not already hold.
- `structures.russia` is **required** at `:640` and `:710` and **provided by nothing** — a dead arm.
- No live `BuildLimit > 0`: the only ones are in `old.yaml:68,:76` and commented out in `mcvs.yaml`.

So the second half of `ActorChanged`'s predicate (`bi.BuildLimit > 0`, `:39`) is unreachable too.

**What would make it reachable:** any prerequisite provided *only* by a capturable or garrisonable
building, or any `BuildLimit > 0` on a buildable. Both are one YAML line away.

**One genuine asymmetry worth recording even so.** `ProductionQueue` *does* implement
`INotifyOwnerChanged`, and its handler calls `techTree.Update()` for the **new** owner only, while
the old owner gets `Remove(this)` with no `Update()` (`ProductionQueue.cs:195-209`). So even on the
handled path the loser's watchers are left un-refreshed. Latent for the same reason as above.

### `SupportPowerManager` — CONFIRMED, reachable, and the sharpest symptom in the report

`Powers` (`Traits/SupportPowers/SupportPowerManager.cs:31`) is populated **only** from `ActorAdded`
(`:53-75`) and drained **only** from `ActorRemoved` (`:77-94`), and both early-return on
`a.Owner != Self.Owner` (`:55`, `:79`). An in-place flip fires neither. The captor's manager never
learns the building exists; the loser's manager never lets it go.

**Reachability — this one ships.** Exactly one `SupportPower` exists in the mod: `NukePower` on
`MSLO` (`structures-defenses.yaml:1134`). `MSLO` is `Inherits: ^Building` (`:1108`), so it is
capturable. It is unbuildable (`Buildable.Prerequisites: ~disabled`, `:1120`) — but it is
**pre-placed on a shipped map**: `nuclear-winter-ww3/map.yaml:1146-1148`, `Actor436: mslo`, owner
`Creeps`, location `50,35`. `Creeps` is a `Player` actor and so carries `SupportPowerManager`
(`player.yaml:105`).

**Player-visible symptom, captor side:** *you capture the nuclear missile silo and get nothing.* No
icon appears in the support power palette, no charge bar, the building is decoration. On a map whose
identity is the silo, and against a `Cost: 50000` / `HP: 135000` structure, that is the single most
disappointing possible outcome of a successful capture.

**Loser side, and this is worse than a leak.** `SupportPowerInstance.Disabled` (`:151-156`) tests
the manager's owner `WinState`, `prereqsAvailable`, `instancesEnabled` and `oneShotFired` — and
**never whether the instance's actor still belongs to the manager's player**. `Tick` (`:191-219`)
keeps charging it, and `ResolveOrder` (`:102-107`) resolves by `OrderString` against the same dict.
So the previous owner retains a firable nuclear power sourced from a building they no longer own.
With `Creeps` as the previous owner this is inert in practice (a non-combatant issues no orders) —
but it is inert by accident of who happens to own it on this map, not by design. Place the same
actor owned by a real player, or give any capturable building a support power, and it is live.

A confirming tell that nobody intended this: `Target()` plays its selection notification to
`power.Self.Owner` (`:227-229`) — the **new** owner — while the manager firing it belongs to the old.

**The capture is reachable, and WW3MOD's two-step capture model makes it worse, not better.** I
initially filed this as my least-certain link and then checked it. `Creeps` on `nuclear-winter-ww3`
is `NonCombatant: True` **and `Enemies: Multi0, Multi1`** (`map.yaml:26-29`) — so the silo presents
as an *enemy* building, not a neutral one. WW3MOD splits capture in two
(`infantry.yaml:916-939`, and the comment at `:922-926` states the rule explicitly — "Soldiers
CLEAR, they never own"):

1. Any soldier (`^CapturesOccupiedBuildings`, `CaptureTypes: building-occupied`,
   `ValidRelationships: Enemy`, `CaptureToNeutral: true`, `CaptureDelay: 1000`) drops it
   **Creeps → Neutral**.
2. A Technician (`^CapturesNeutralBuildings`, `CaptureTypes: building-neutral`, `CaptureDelay: 20`,
   `ConsumedByCapture: true`) then installs ownership, **Neutral → player**.

Both steps run through the same in-place site (`CaptureActor.cs:140-141`), so **the in-place path
fires twice per capture**, and the player must spend a 1000-tick soldier clear plus a consumed
Technician to reach a building that then does nothing. `Capturable` itself carries no relationship
filter — `Capturable.cs:23` declares only `Types` — so all the gating lives on the `Captures` side,
consistent with that model.

### `ActorIndex` — mechanism CONFIRMED, symptom UNREACHABLE, and the recon's claim is too strong

The asymmetry is real and is the cleanest example of the class in the codebase. `AddActor` gates on
`ShouldIndexActor` (`ActorIndex.cs:40-44`), which for every subclass tests `actor.Owner == owner`
(`:92-95`, `:147-150`). `RemoveActor` (`:46-49`) removes **unconditionally**. Miss both events and
the victim's index keeps the actor while the captor's never gains it — until the actor genuinely
leaves the world, i.e. dies.

The staleness is genuinely consumed: `AIUtils.CountActorByCommonName` (`AIUtils.cs:67-71`) reads
`actorIndex.Actors.Count(a => !a.IsDead)` with **no owner re-filter**, so a ghost is counted.

**But the recon's "a captured building permanently ghosts in the victim bot's index" does not hold
for the shipped configuration**, because the in-place path is buildings-only and no bot type list
names a flippable building. The complete ww3mod configuration is three lines:

- `CapturingActorTypes: tecn,tecn.russia,tecn.america` (`ai/ai.yaml:117`, `:2462`) — TECN is
  **infantry**, so it flips via `ChangeOwnerSync`, which fires both events. Not affected.
- `ConstructionYardTypes: supplyroute` (`ai/ai.yaml:1965`) — a building, so in-place-eligible in
  principle, but `SUPPLYROUTE`'s owner never changes (see above). Not affected.
- No `RefineryTypes`, `HarvesterTypes`, `McvTypes` or `McvFactoryTypes` anywhere — those indexes are
  constructed empty and stay empty.

**What would make it reachable:** adding any capturable or garrisonable building to
`ConstructionYardTypes`, `McvFactoryTypes` or `RefineryTypes`. `LOGISTICSCENTER` is the obvious
candidate if anyone ever teaches the bot to value it.

---

## Q2 — The rest of the class

### Channel 2 is complete: eight subscription sites

`grep -E 'Actor(Added|Removed)\s*\+='` over `engine/` returns exactly eight, and I read all eight.
Three are covered above. Of the remaining five:

**`UnitLifecycleLogger` — confirmed, reachable, and I would rank it second overall.**
`UnitTrack.Owner = a.Owner.ClientIndex` (`Traits/World/UnitLifecycleLogger.cs:307`) is a **snapshot
taken once at `Track` time and never updated**. There is no owner-change hook anywhere in the file.
Worse, `IsInteresting` rejects `a.Owner.NonCombatant` (`:280-281`), so:

- A building garrisoned Neutral → player is **never tracked at all** — it failed `IsInteresting` at
  spawn and no later event re-offers it.
- A building captured player → player stays attributed to the **losing** player for the rest of the
  run.
- The `MSLO` case is the worst of both: spawned under `Creeps` (`NonCombatant: True`,
  `map.yaml:26-27`) it fails `IsInteresting` at spawn, then passes through Neutral and into a real
  player's hands without a single event firing. **A captured nuclear silo is invisible to the
  telemetry for the entire match**, on both the mechanic that took it and the player who now owns it.

This is not player-visible; it is worse than that. It is the instrument the project reads to decide
balance and AI questions, and it under-reports exactly the mechanic — garrison — that the recon
identified as the highest-traffic user of the in-place path. Any analysis that counts structures by
owner, or that reasons about who held what when, is reading a record that never saw a capture.

**`UnitDefaultsManager` (`:51`, `ApplyDefaultsToLocalActor` `:60-75`) — confirmed, and arguably
correct.** A captured actor does not get the new owner's stance defaults applied. But the trait's
documented semantics (`:54-59`) are "defaults at spawn, equivalent to the player clicking the stance
button", and re-applying a preference to a unit the player just took — possibly overriding a stance
the previous owner deliberately set and the captor may want to inspect — is a design question, not a
bug. **I would leave it and record the reasoning, not silently "fix" it.**

**`ProductionTabsLogic` (`:53-54`) — cosmetic and self-healing.** The production tab strip does not
refresh on an in-place flip. `ProductionQueue` itself implements `INotifyOwnerChanged` so the queue
transfers correctly; only the widget refresh is missed, and any other actor entering or leaving the
world — constant in a live game — refreshes it. Not worth a handler.

**`TSResourceLayer` (`:47-48`) and `TSVeinsRenderer` (`:192-193`) — unreachable.** `OpenRA.Mods.Cnc`
*is* loaded (`mod.yaml:170`), so these compile and ship, but neither trait appears anywhere in
`mods/ww3mod/` — grep returns zero. No instance is ever constructed.

### Channel 1: 37 probe hits, filtered

The probe (§5) lists every concrete type implementing `INotifyAddedToWorld` or
`INotifyRemovedFromWorld` **without** `INotifyOwnerChanged`. Twelve of the 37 have no presence in
`mods/ww3mod/rules/` at all (`EnergyWall`, `TDGunboat`, `WithBuildingBib`, `ChangesTerrain`,
`BridgePlaceholder`, `GroundLevelBridge`, `AttackBomber`, `KillsSelf`, `TerrainLightSource`, and —
worth noting — **`ProximityContestable`, which the recon listed as a confirmed instance**). Of the
rest:

**Refuted — these cache nothing owner-dependent:**

- **`GpsDot`** (`Mods.Cnc/Traits/GpsDot.cs:48-56`). Its `AddedToWorld` only adds the effect to the
  world; the effect resolves ownership live at render time —
  `GpsDotEffect.cs:111-114` reads `actor.EffectiveOwner ?? actor.Owner` every frame. **Fine.** This
  matters because `GpsDot` is on 7 files including `MSLO` (`structures-defenses.yaml:1171`) and
  `FCOM` (`structures-neutral.yaml:62`), so it looked like the widest-reaching hit in the probe.
- **`SupplyProvider`** (`Traits/SupplyProvider.cs:224-226`). Implements only
  `INotifyRemovedFromWorld`, and solely to release a held target (`:1013`). Every relationship test
  is live at scan time (`:621`, `:659`, `:856`, `:1115`). **Fine.**
- **`Building`** (`Traits/Buildings/Building.cs:359-370`). `AddToMaps` + `influence.AddInfluence` —
  cell occupancy, not owner-keyed. **Fine.**
- **`SpawnArea`, `AmbientSound`, `Immobile`** — no reference to `Owner` anywhere in the file.
- The `Husk` / `Crate` / `Gate` / bridge / `With*SpriteBody` / `Contrail` / `LeavesTrails` /
  `Passenger` / `ParaDrop` / `Mobile` / `Aircraft` cluster is positional or cosmetic; none registers
  owner-keyed state. `Mobile`, `Aircraft` and `Passenger` are also mobile-only, so the in-place path
  never touches them.

**Confirmed, low severity:**

- **`ProximityExternalCondition`** (`Traits/Conditions/ProximityExternalCondition.cs:51-52`). This
  one is subtle and worth reading carefully, because it *looks* handled. It implements
  `INotifyProximityOwnerChanged` (`:174-202`), which `ExternalCondition.OnOwnerChanged` fans out to
  every such trait in the world (`ExternalCondition.cs:277-282`) — and since `ExternalCondition`
  implements `INotifyOwnerChanged`, that fan-out **does** fire on the in-place path. But it answers
  the wrong question: it re-evaluates whether *the actor that changed owner* should hold conditions
  from other triggers. It does **not** re-evaluate the actors sitting inside *this* trait's own
  trigger when *`self.Owner`* changes. `ActorEntered` snapshots the relationship at entry
  (`:125-126`).
  Reachable via `LOGISTICSCENTER`'s `ProximityExternalCondition@UNITDOCKED`
  (`structures.yaml:564-568`, `ValidRelationships: Ally`, `Range: 2c0`): capture an LC with vehicles
  docked and those vehicles keep `unit.docked`, which pauses their weapons
  (`vehicles-america.yaml:152` and ~8 siblings). **Self-heals the moment the unit moves out of
  range**, so it is a transient disarm, not a permanent one.
- **`BaseProvider`** (`Traits/Buildings/BaseProvider.cs:59`) — recon's claim confirmed verbatim:
  `devMode` is resolved from `self.Owner.PlayerActor` in the constructor. But it is read at exactly
  two places (`:82`, `:126`) and only for `DeveloperMode.FastBuild`, a cheat. Build *placement* is
  unaffected: `IsCloseEnoughToBase` re-queries live (`Building.cs:257-265`). On `FCOM`
  (`structures-neutral.yaml:57`), which is capturable. **Negligible.**
- **`RenderDebugState`** — debug overlay only, as the recon said.
- **`SupplyRouteContestation`** — appears in the probe, so it is *not* an `AffectsMapLayer` subclass
  and did not inherit the merged fix. Unreachable for the same reason as `TechTree`'s SR arm:
  `SUPPLYROUTE` never changes owner. Worth a comment at the trait, not a fix.

### Resolved, not a bug: `FrozenUnderFog`

The recon flagged `FrozenUnderFog.OnOwnerChanged` as "implements the interface but refreshes only
the old owner — unresolved". **It is resolved: the asymmetry is deliberate and documented in place**
(`Traits/Modifiers/FrozenUnderFog.cs:221-235`). The comment explains that `TooltipOwner` is
withheld on purpose so a fogged ghost does not name the captor and hand out free information on FFA
maps. Nothing to do here, and nothing that should confound a capture autotest's fog assertions.

---

## Q3 — Symptoms, ranked

1. **`SupportPowerManager` — "I captured the nuclear silo and it does nothing."** Reachable today on
   `nuclear-winter-ww3`. Also leaves a firable power attached to the previous owner, currently inert
   only because that owner happens to be `Creeps`.
2. **`UnitLifecycleLogger` — "the telemetry never saw the capture."** Reachable on every garrison
   flip, which is the core loop. Garrisoned buildings are absent from the record entirely; captured
   ones are attributed to the loser. Corrupts the evidence base rather than the game.
3. **`ProximityExternalCondition` — "the vehicles I just captured at the logistics centre won't
   shoot until I move them."** Reachable, transient, self-healing.
4. **`BaseProvider`, `ProductionTabsLogic`, `RenderDebugState`** — dev-cheat, self-healing UI, and
   debug overlay respectively. No player-visible symptom.
5. **`TechTree`, `ActorIndex`, `SupplyRouteContestation`** — no symptom today. Each is one YAML line
   from having one; the trigger conditions are named in §Q1.

**On "unreachable".** The vision bug was unreachable through engineer capture until an unrelated
change removed the reason, so I have tried to say for each latent instance *what specifically*
would make it reachable rather than just filing it as safe. For `TechTree` it is a prerequisite
provided only by a flippable building, or any `BuildLimit`. For `ActorIndex` it is a flippable
building added to a bot type list. For `SupplyRouteContestation` it is wiring SR capture — which
CLAUDE.md says is designed but not wired, so this is the instance most likely to detonate later,
since the person wiring capture will be thinking about ownership transfer and not about a
contestation cache.

---

## Q4 — What the merged `AffectsMapLayer` fix already covers

**Only `AffectsMapLayer` subclasses, and the probe proves the boundary exactly.** Before `0d862eeb`,
`Vision`, `Radar`, `CounterBatteryRadar` and `CreatesShroud` would all have appeared in the probe's
37; after it, none does — the base class now carries `INotifyOwnerChanged`
(`AffectsMapLayer.cs:43`, handler `:174-180`) and every subclass inherits it. The recon's claim that
the base-class fix covers `Radar`/`CounterBatteryRadar`/`CreatesShroud` for free is **confirmed**.

**Nothing else is covered.** Every instance in this report needs its own handler, and none of them
can copy `AffectsMapLayer`'s. Two reasons, and the second is the trap:

- The channel-2 instances (`SupportPowerManager`, `TechTree`, `ActorIndex`, `UnitLifecycleLogger`)
  are not traits on the flipping actor at all. They are player- or world-level subscribers. They
  cannot implement `INotifyOwnerChanged` and receive the flip *for another actor* — except that they
  **can**, because both change-owner paths fan out to `World.WorldActor.TraitsImplementing<INotifyOwnerChanged>()`
  (`Actor.cs:561-562`, `:588-589`), passing the changed actor as `self`. That is the existing,
  already-load-bearing channel `ExternalCondition` uses (`ExternalCondition.cs:277-282`). **This is
  the mechanism a fix should use**, and it is worth stating because it is not obvious that a
  player-level trait can be notified about a building at all.
- The `IsInWorld` guard that is load-bearing for `AffectsMapLayer` is load-bearing there for a
  reason that does **not** generalise: it exists because that class also has world hooks that would
  double-add (`AffectsMapLayer.cs:165-180` PITFALL comment). A channel-2 subscriber has no such
  hooks. Copying the guard reflexively into a `SupportPowerManager` handler would be wrong in the
  opposite direction — `ChangeOwnerSync` calls the notification while the actor is out of the world,
  and a `SupportPowerManager` handler **must** run then too, or mobile support-power carriers stop
  transferring. Any fix here needs its own reasoning about the out-of-world window, not a paste.

---

## §5 — Method: the reflection probe

The brief was right that the `[Obsolete("ZZCENSUS")]` / `CS0618` trick cannot answer an
implements-A-but-not-B question. A reflection pass can, exactly, in about 40 lines and ~110 ms.

Added as `engine/OpenRA.Test/ZZOwnershipProbe.cs`, run with
`dotnet test engine/OpenRA.Test/OpenRA.Test.csproj -c Release --filter FullyQualifiedName~ZZOwnershipProbe`,
**deleted before committing** (this branch contains no probe).

```csharp
var assemblies = new[] {
    typeof(OpenRA.Mods.Common.Traits.AffectsMapLayer).Assembly,
    typeof(OpenRA.Mods.Cnc.Traits.ChronoshiftPaletteEffect).Assembly,
    typeof(Actor).Assembly };
foreach (var t in asm.GetTypes().Where(t => t.IsClass && !t.IsAbstract)) {
    var i = t.GetInterfaces();
    if ((i.Contains(typeof(INotifyAddedToWorld)) || i.Contains(typeof(INotifyRemovedFromWorld)))
        && !i.Contains(typeof(INotifyOwnerChanged)))
        rows.Add(t.FullName);
}
```

Four practical notes for whoever reuses this:

- **`TestContext.Progress.WriteLine` output does not survive `dotnet test`'s default console
  logger.** I lost one run to this. Write to a file from inside the test; it is the only reliable
  channel without fighting `--logger` verbosity.
- Pick a **public** type to anchor each assembly. `typeof(TSResourceLayer)` fails to compile —
  `CS0122`, the Cnc trait classes are internal. `ChronoshiftPaletteEffect` works.
- `GetInterfaces()` returns the transitive closure, so a subclass inheriting the interface from an
  abstract base is correctly excluded. That is what makes the probe a valid *regression* check on
  the merged fix rather than just a census: `Vision` disappearing from the output is direct evidence
  the base-class handler reaches every subclass.
- **It only answers the interface question.** Owner-dependence has to be read. Of 37 hits, 12 were
  eliminated by a YAML grep and the remaining 25 by reading. The probe narrows the reading list from
  "the whole engine" to 37 files; it does not do the reading.

**This is worth keeping as a technique.** The same shape answers "which traits implement X but not
Y" for any interface pair, and interface-pair invariants are exactly what greps are worst at.

---

## Trust ledger

### Verified by reading the code myself

- The exact two-channel delta between the paths — `Actor.cs:545-563` vs `:569-593`, `World.cs:394-412`.
- Both paths fan out to WorldActor traits — `Actor.cs:561-562`, `:588-589`.
- `TechTree` subscription and owner gate — `TechTree.cs:32-33`, `:39`; fresh recomputation at
  `:65-70`, `:72-112`; stale booleans at `:123`, `:125`, `:188-192`.
- Every prerequisite provider in the mod, and that `SUPPLYROUTE` carries no `Capturable` —
  `structures.yaml:222-225`, `:372-390`, `:629-639`, `:699-709`; `player.yaml:208-224`, `:236-267`.
  `structures.russia` required at `:640`/`:710`, provided nowhere. `BuildLimit` only in `old.yaml`.
- `^Building` → `^BasicBuilding` → `^NeutralOrOccupiedCapturable` — `structures.yaml:69`, `:10`, `:169-177`.
- `SupportPowerManager` owner gates and the `Disabled` predicate's omission —
  `SupportPowerManager.cs:31`, `:53-56`, `:77-80`, `:102-107`, `:151-156`, `:191-219`, `:227-229`.
- `MSLO` is `^Building`, carries `NukePower`, and ships on `nuclear-winter-ww3` owned by `Creeps` —
  `structures-defenses.yaml:1107-1134`, `nuclear-winter-ww3/map.yaml:1146-1148`.
- `Creeps` is `NonCombatant` **and** an enemy of both playable slots — `map.yaml:26-29`.
- The two-step capture model and both `Captures` blocks — `infantry.yaml:916-939`, with the
  design rule stated in-comment at `:922-926`. `Capturable` has no relationship filter,
  only `Types` — `Capturable.cs:23`.
- `ActorIndex`'s add/remove asymmetry and the un-refiltered consumer —
  `ActorIndex.cs:40-49`, `:92-95`, `:147-150`; `AIUtils.cs:67-71`.
- The complete ww3mod bot type-list configuration — `ai/ai.yaml:117`, `:1965`, `:2462`.
- `UnitLifecycleLogger`'s owner snapshot and NonCombatant filter — `:280-281`, `:290-309`, `:321-332`.
- `ProximityExternalCondition`'s fan-out and what it does not cover — `:51-52`, `:125-126`,
  `:174-202`; `ExternalCondition.cs:277-282`.
- `GpsDot` / `GpsDotEffect` live resolution — `GpsDot.cs:48-56`, `GpsDotEffect.cs:111-114`.
- `SupplyProvider` live relationship tests — `:224-226`, `:621`, `:659`, `:856`, `:1013`, `:1115`.
- `BaseProvider`'s cached `devMode` and its two uses — `:59`, `:82`, `:126`; live placement query at
  `Building.cs:257-265`.
- `FrozenUnderFog`'s deliberate asymmetry — `:221-235`.
- The merged fix and its PITFALL comment — `AffectsMapLayer.cs:43`, `:162-180`.

### Established mechanically rather than by reading

- The 37-type channel-1 census. The probe is exhaustive over the three loaded assemblies by
  construction; I read the ones that survived the YAML filter and did **not** read the twelve with
  zero YAML presence beyond confirming the grep returned nothing.

### Taken on trust

- That `grep -E 'Actor(Added|Removed)\s*\+='` catches every channel-2 subscription. It would miss a
  subscription made through a variable alias or reflection. I saw none, but I did not prove none
  exists.
- That a `NonCombatant` player issues **no support-power orders**. I read that `Creeps` is
  `NonCombatant` on this map (`map.yaml:26-27`) but inferred the no-orders consequence from the
  absence of an order source rather than reading one. This is the load-bearing assumption behind
  calling the loser-side leak inert — see "the single thing I would most expect to be wrong".

### Not established

- **Any runtime behaviour.** No game launched, no autotest run — launch embargo. Every claim is
  static reading, one YAML grep, and one reflection pass. In particular I have **not observed** a
  capture of `MSLO`.

### The single thing I would most expect to be wrong

**That the `SupportPowerManager` leak is inert on the loser's side.** I claim the previous owner
retains a charging, firable nuke but that it does not matter on `nuclear-winter-ww3` because that
owner is `Creeps`, a non-combatant that issues no orders. Two things could break that.

First, the two-step capture means the *intermediate* owner is **Neutral**, not `Creeps` — after a
soldier clears the silo, whichever manager holds the instance is whatever `ChangeOwnerInPlaceSync`
left it with, and I reasoned about that transition statically rather than watching it. Second, and
more likely to be the real hole: I asserted that a non-combatant issues no support-power orders from
the *absence* of an order source, not from reading one. `SupportPowerInstance.Tick` charges
regardless of owner (`:191-219`), and `NukePower`'s `DisplayTimerRelationships: Ally, Neutral,
Enemy` plus `DisplayBeacon: True` (`structures-defenses.yaml:1151-1152`) mean a charging instance is
capable of putting a **visible countdown and beacon on every player's screen**. If a stale instance
keeps charging under `Creeps`, players may see a nuke timer for a silo nobody is going to fire —
which would be a live, confusing, reachable symptom that I have classified as inert.

That would not change the fix, but it would raise the priority and change the bug report's headline
from "capture gives nothing" to "capture gives nothing *and* leaves a phantom countdown running".

**The run that would settle it** (I am not asking; embargo):

> A scenario placing a TECN, a rifleman and a `Creeps`-owned `MSLO`; clear then capture; assert on
> the capturing player's `SupportPowerManager.Powers` containing a `NukeMissile` key, and separately
> observe whether any support-power timer/beacon is rendered afterwards. **The answer** is the key's
> presence for the main bug, and the beacon's presence for this uncertainty. Absent key on today's
> `main` is the RED; present after a fix is the GREEN. Sabotage form for the RED-before-green rule:
> force that capture site to `ChangeOwnerSync` and confirm the key **appears** — proving the
> scenario can observe the difference at all before anyone trusts its failure.

The claim I had expected to be my weakest — that the capture is reachable at all — I checked instead
of shipping, and it came back stronger than drafted (§Q1, two-step model). That is worth recording
as a process note: the reachability question was answerable by two greps and would have been the
single wrong sentence in this report had I left it in the ledger.
