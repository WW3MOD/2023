# 05 — Squads and combat states: what still runs, and what is inherited scaffolding

**Researched against `main` @ `910507c1`** (`git status -sb`: `main...origin/main [ahead 67]`, working tree
clean apart from four known untracked scratch paths). Static read only — no build, no game run, no autotest.
Every factual claim below carries a `file:line` that I opened and read at that commit.

> **Reconciled 2026-08-09 against `main @ 25a8aebd`.** A cross-document pass re-derived every headline
> claim, summary count and computed figure in this six-document set from the code, and corrected the
> loser of every contradiction in place. Corrections made here are marked at the point they occur.
> **Danger-field magnitudes are the one excluded class** — they are pending re-derivation on
> `auto/danger-scale` and are flagged wherever they appear; see
> [`04` §3.2](04-perception-and-fields.md).

**What this document is.** The squad layer: what a squad is, how a unit gets into one, the state machines that
command squads once formed, and — the part that matters most — **which of those state machines actually
execute in a shipped match**. This is the most inherited region of the bot. Most of it is stock OpenRA
`SquadManagerBotModule` machinery written for a base-building RTS with production queues, harvesters and a
tech tree.

**It does not cover** the tick path, module cadences, or the order gate
([`02-lifecycle-and-arbitration.md`](02-lifecycle-and-arbitration.md)); the module catalogue; the
influence-stack fields (belief store, danger/control fields —
[`influence-stack.md`](../reference/influence-stack.md)); or `PoiOffensiveBotModule`, which is where ground
behaviour actually lives. Those are other documents. I link rather than restate.

**Framing you must not lose.** WW3MOD is a total conversion. There are no factories and no tech tree; units
are called in as reinforcements from off-map reserves through the **Supply Route**, a fixed, indestructible,
non-buildable beachhead, one per player ([`game-model.md`](../reference/game-model.md),
[`supply-route.md`](../reference/supply-route.md)). Every inherited assumption of *production*, *base
expansion*, *rearm buildings* or *repair* is therefore a misfit by definition, and §7 hunts them
specifically.

---

## How to read this document

Two markers, and they mean different things.

**Provenance** — where a component came from (established by `git log --follow` per file; the OpenRA import
is `7362fbc6 "Starting point (#2)"`, later refreshed by the `c5bb5ece` upstream merge to `release-20250330`):

| Marker | Meaning |
|---|---|
| **[OpenRA]** | Inherited from upstream OpenRA essentially unchanged. Designed for a base-building RTS. |
| **[MODIFIED]** | OpenRA structure, but WW3MOD changed its behaviour or added fields. |
| **[WW3MOD]** | Written for this mod. No OpenRA ancestor. |

**Opinion** — every paragraph beginning **`OPINION:`** is my assessment, not a description of the code.
Disagree with those freely. The `file:line` claims are the part that should be checkable and correct.

---

## 0. The answer first

If you read one section, read this one.

**There are 20 squad states in `Squads/States/`. Seven of them run on shipped content. Thirteen are
unreachable on both shipped bot profiles.**

Everything the mod calls "the squad system" for ground combat — attack squads, rush squads, base-protection
squads, naval squads, the fuzzy attack-or-flee evaluator, the regroup logic — **cannot execute**. Ground
combat is commanded by `PoiOffensiveBotModule` instead. What survives in the squad layer is exactly two
things: **one fixed-wing air squad per player** (3 states, all reachable) and **helicopter squads** (5 states,
**4** of which are reachable), the latter written for this mod.

> **On the count, because an earlier draft of this document got it wrong and it has been quoted.** The 13th
> dead state is `HelicopterAttackRunState`. It is entered from exactly one place, inside `if (!standoff)`
> (`HelicopterStates.cs:565-573`), and `StandoffEngagement: true` ships on **both** profiles
> (`ai.yaml:1419`, `:1446`) — so it never runs, and neither does the hit-and-run mechanic it carries (§3.2,
> §7.4). §3.2 and the §2.7 table have said so all along; the headline said "eight run, twelve are
> unreachable" and contradicted its own body. The honest shipped figure is **7 / 13**.

I verified this independently rather than taking it from a doc; the chain is in §2. The same conclusion is
recorded in [`WORKSPACE/recon/260807-order-source-census.md`](../../WORKSPACE/recon/260807-order-source-census.md)
§1.5 and in [`architecture.md`](../reference/architecture.md) (§AI, "Ground units are commanded by
`PoiOffensiveBotModule` on BOTH profiles").

> **Note on the census's line numbers.** That recon cites the four manager instances as `ai.yaml:1075, :1166,
> :1617, :1635`. Those were correct at its commit; at `910507c1` the same four instances are at
> `mods/ww3mod/rules/ai/ai.yaml:1239, :1330, :1787, :1805`. The finding holds; the coordinates moved.

---

## 1. The squad model

### 1.1 What a `Squad` is

**[OpenRA]** with **[WW3MOD]** additions. `Squads/Squad.cs` — 9 commits, first is the OpenRA import.

A squad is a plain object, not an actor and not a trait. It holds a unit list, a type, a target, and a state
machine:

| Field | `Squad.cs` | Meaning |
|---|---|---|
| `Units` | `:23` | `List<Actor>`. Membership is this list and nothing else — no trait, no condition on the unit. |
| `Type` | `:24` | One of six `SquadType` values (`:19`). |
| `SquadManager` | `:28` | Back-pointer to a `SquadManagerBotModule`, used for `Info` tunables and helpers. |
| `Target` | `:31` | The tactical target. `TargetActor` is a thin accessor (`:96-100`). |
| `FuzzyStateMachine` | `:32` | The state machine (`StateMachine.cs`). |
| `ApproachWaypoint` | `:36` | **[WW3MOD]** flanking waypoint for the multi-axis split (`b989e83f`). |
| `StrategicTarget` | `:48` | **[WW3MOD]** durable strategic pin, kept separate from the tactical target (`4aca4aa0`). |
| `StrategicCommitTick` | `:52` | **[WW3MOD]** tick the pin was committed; feeds the bounded commit window. |

`IsValid` is simply `Units.Count > 0` (`:94`). A squad with no units is dissolved by whichever manager owns
it. There is no other lifecycle.

The state machine is trivially small (`StateMachine.cs:14-32`): one `IState currentState`, `Update` calls
`currentState?.Tick(squad)`, `ChangeState` calls `Deactivate` on the old and `Activate` on the new. There is
no state stack, no history, no transition table — a state changes state by constructing the next one inline.

The constructor picks the initial state by type (`Squad.cs:67-85`):

| `SquadType` | Initial state | Created by |
|---|---|---|
| `Assault` | `GroundUnitsIdleState` | `SquadManagerBotModule.CreateAttackForce` |
| `Rush` | `GroundUnitsIdleState` | `SquadManagerBotModule.TryToRushAttack` |
| `Air` | `AirIdleState` | `SquadManagerBotModule.FindNewUnits` |
| `Protection` | `UnitsForProtectionIdleState` | `SquadManagerBotModule.ProtectOwn` |
| `Naval` | `NavyUnitsIdleState` | `SquadManagerBotModule.FindNewUnits` |
| `Helicopter` | `HelicopterIdleState` | `HelicopterSquadBotModule.TryLaunchAttackMission` |

### 1.2 The two managers

There are **two** modules that build squads, and they are not peers.

**`SquadManagerBotModule`** **[MODIFIED]** — `SquadManagerBotModule.cs`, 565 lines, 16 commits from the OpenRA
import. Four instances are attached in ww3mod, one per (profile × faction):

| Instance | `ai.yaml` | Condition |
|---|---|---|
| `@experimental.russia.fixedwing` | `:1239` | `enable-ai-experimental && player.brics` |
| `@experimental.america.fixedwing` | `:1330` | `enable-ai-experimental && player.nato` |
| `@stable.russia.fixedwing` | `:1787` | `enable-ai-stable && player.brics` |
| `@stable.america.fixedwing` | `:1805` | `enable-ai-stable && player.nato` |

Exactly one is enabled per player: the profile conditions are mutually exclusive
(`GrantConditionOnBotOwner@experimental` / `@stable`, `ai.yaml:61-66`) and the faction conditions partition the
rest.

**`HelicopterSquadBotModule`** **[WW3MOD]** — `HelicopterSquadBotModule.cs`, 2002 lines, 24 commits, first is
`9dfd8620 "Helicopter AI: role-based squad module with hit-and-run behavior"`. No OpenRA ancestor. Two
instances, `@stable` (`ai.yaml:1409`) and `@experimental` (`:1429`), not split by faction.

**The coupling that surprises people.** The helicopter module cannot construct a `Squad` on its own — the
`Squad` constructor requires a `SquadManagerBotModule` (`Squad.cs:57`). So `Initialize()` reaches across and
grabs the enabled fixed-wing manager (`HelicopterSquadBotModule.cs:481-482`), and `TryLaunchAttackMission`
**returns without launching if it finds none** (`:777-778`). Consequences:

- Helicopter squads read the **fixed-wing** manager's `Info` for anything routed through `StateBase` — e.g.
  `RandomBuildingLocation` → `SquadManager.GetRandomBaseCenter()` (`StateBase.cs:31`), used as the heli
  withdraw fallback at `HelicopterStates.cs:822`.
- Editing the fixed-wing manager's YAML silently changes helicopter behaviour.
- Disabling the fixed-wing manager would disable helicopter attack missions entirely.

> **OPINION:** this is an accidental dependency, not a design. `Squad` needs a `SquadManagerBotModule` only
> because upstream had exactly one squad manager and used it as a convenient bag of tunables. It should be an
> interface (or the tunables should be passed in), so the two modules are independent. Low risk, real
> clarity win.

### 1.3 How a unit enters and leaves a squad

**Fixed-wing path** — `SquadManagerBotModule.FindNewUnits` (`:294-346`), driven every `AssignRolesInterval`
(50 ticks, `:281-285`). It scans **every `IPositionable` actor the player owns** that is not in
`ExcludeFromSquadsTypes` and not already claimed (`:303-309`), then sorts each one:

```
                 ┌──────────────────────────────────────────────┐
  new unit ───►  │ IsAirSquadUnit(a)?          :313             │──yes──► the ONE Air squad  :315-319
                 ├──────────────────────────────────────────────┤
                 │ NavalUnitsTypes.Contains?   :321             │──yes──► the ONE Naval squad :323-327
                 ├──────────────────────────────────────────────┤
                 │ IgnoreGroundUnits?          :329             │──yes──► `continue` — NOT CLAIMED :335
                 ├──────────────────────────────────────────────┤
                 │ else                        :337             │───────► unitsHangingAroundTheBase :338
                 └──────────────────────────────────────────────┘
```

`GetSquadOfType` (`:247-250`) returns the *first* squad of a type, so there is **exactly one air squad per
player for the whole match**, and every aircraft ever called in joins it. It is never dissolved while it has
a member — `CleanSquads` only removes squads whose unit list has emptied (`:241`).

`IsAirSquadUnit` (`:354-362`) **[MODIFIED]** has two modes. With `UseUnitRoles: true` (set on all four
instances) and a `UnitRoleResolver` present, membership is "Buildable `AttackAir` airframe that is not an
`AIHelicopterRole`". Otherwise it falls back to the `AirUnitsTypes` name list. Both are configured, so the
fallback is harmless.

Units leave a squad only by dying or changing owner: `CleanSquads` prunes with `unitCannotBeOrdered`
(`:239-244`, predicate at `:165`). There is no voluntary release, no rotation, no "this unit is better used
elsewhere". A living aircraft is in the air squad forever.

**Helicopter path** — `FindNewHelicopters` (`HelicopterSquadBotModule.cs:553-575`) adopts every actor with an
`AIHelicopterRole` trait into `managedHelicopters` + `idleHelicopters`, and claims it on the blackboard
(`:568-569`). `TryLaunchAttackMission` (`:772-827`) then forms a squad from idle, mission-ready
`AttackHeavy`/`AttackLight` helis. Helis **do** return to the idle pool when their squad dissolves
(`CleanUpHelicopters:628-653`), so the heli pool genuinely rotates. That is the better of the two designs.

### 1.4 Two parallel systems? Yes — but only one commands ground

This is the question the squad layer most often gets wrong, so state it plainly.

| Layer | Owns | Command primitive |
|---|---|---|
| Squad layer (this doc) | fixed-wing aircraft, helicopters | a `Squad` with a per-squad FSM |
| Axis/POI layer (`PoiOffensiveBotModule`) | **all ground units** | a scored *axis* toward a POI, ordered as a grouped `AttackMove` |

They are not competing for the same units, and there is no arbitration between them, because the fixed-wing
manager **declines to recruit ground units at all**: `IgnoreGroundUnits: true` on all four instances, and the
`continue` at `SquadManagerBotModule.cs:335` deliberately does *not* add the unit to `activeUnits`, so the
ground pool stays visible to the POI module as free units. The `[Desc]` at `:36-39` states this intent
explicitly.

So: **not two systems fighting, but one system (squads) that has been hollowed out to air-only, plus a
replacement (axes/POIs) that took over ground.** The authoritative layer for ground is the POI module,
unconditionally, on both profiles.

There is one genuine cross-layer interaction left, and it points the other way: `StateBase`
`ExcludeTacticallyCommitted` (`:155-171`) **[WW3MOD]** filters units holding a `tacpos:` claim in the POI
module's `PoiGoalGuard` ledger out of grouped squad orders — so the squad FSM cannot yank a unit the POI
positioning executor is adjusting. It is called from three sites, all in `GroundStates.cs` (`:67`, `:160`,
`:174`) — **all unreachable**. The guard is real, and it currently guards nothing.

---

## 2. What is REACHABLE — the verification

The claim is strong, so here is the chain I actually walked, not a citation of someone else's conclusion.

### 2.1 `SquadType.Assault` — unreachable

`CreateAttackForce` (`:364-423`) is the only producer, and it opens with

```csharp
if (unitsHangingAroundTheBase.Count < randomizedSquadSize)   // :370-371
    return;
```

`unitsHangingAroundTheBase` is written in exactly one place: `FindNewUnits:338`, in the `else` branch that
`IgnoreGroundUnits` short-circuits at `:329-336`. All four instances set `IgnoreGroundUnits: true`
(`ai.yaml:1250, :1341, :1800, :1813`). Air and naval units never reach that `else` either — they are handled
by the earlier branches. **The list is therefore always empty, and `CreateAttackForce` always early-returns.**

### 2.2 `SquadType.Rush` — unreachable, twice over

`TryToRushAttack` (`:425-455`) needs two things:

1. `allEnemyBaseBuilder.Count != 0` (`:433`), from
   `AIUtils.FindEnemiesByCommonName(Info.ConstructionYardTypes, Player)` (`:427`). **`ConstructionYardTypes`
   is set on no `SquadManagerBotModule` instance** — the only occurrence in `mods/ww3mod/` is
   `BaseBuilderBotModule@normal` at `ai.yaml:1183`. The set is empty, so the list is empty, so it returns.
2. `ownUnits.Count >= Info.SquadSize` (`:433`), where `ownUnits` is drawn from `activeUnits` filtered to
   non-air, non-naval (`:429-431`). Ground units never enter `activeUnits` (the `continue` at `:335` skips
   `activeUnits.Add` at `:340`), and the aircraft that *are* in `activeUnits` are filtered out by name via
   `AirUnitsTypes`, which **is** correctly set on all four instances (`ai.yaml:1245, :1336, :1798, :1811`).
   So `ownUnits` is empty.

Either condition alone kills it.

### 2.3 `SquadType.Protection` — unreachable

`ProtectOwn` (`:457-474`) is called from exactly one place, `RespondToAttack` (`:483-495`), behind

```csharp
if (Info.ProtectionTypes.Contains(self.Info.Name))   // :488
```

**`ProtectionTypes` is set nowhere in `mods/ww3mod/`** (verified by grep over the whole mod directory). The
set is empty, so the test is always false, so `ProtectOwn` is never called.

### 2.4 `SquadType.Naval` — unreachable

The only producer is `FindNewUnits:321-327`, gated on `Info.NavalUnitsTypes.Contains(a.Info.Name)`.
**`NavalUnitsTypes` is set nowhere in `mods/ww3mod/`.** Empty set, no match, no naval squad — regardless of
whether the mod has naval units.

### 2.5 `SquadType.Air` — REACHABLE

`FindNewUnits:313-319` forms it from `IsAirSquadUnit`. Both profiles buy fixed-wing aircraft:
`UnitBuilderBotModule@russia.fixedwing` (`ai.yaml:1219`, `mig`/`frog`) and `@america.fixedwing` (`:1315`,
`a10`/`f16`), both under `RequiresCondition: enable-ai-any`, which is granted to both bots (`:69-71`). Those
actor names are exactly `AirUnitsTypes`, and they satisfy the role gate too. Air squads form.

### 2.6 `SquadType.Helicopter` — REACHABLE

`TryLaunchAttackMission` (`:772-827`) needs an enabled `SquadManagerBotModule` (`:777`, satisfied — §1.2) and
enough mission-ready attack helis. Both profiles buy them (`UnitBuilderBotModule@russia.heli` `ai.yaml:1263`
and the `@experimental` twin `:1290`; America `:1356` and its twin), and both set `SkipRearmReadyCheck: true`
(`:1418`, `:1445`), which is what makes `IsReadyForMission` passable at all (§7.3). Heli squads form.

### 2.7 The reachability table

| State | File | Reachable? | Why |
|---|---|---|---|
| `AirIdleState` | `AirStates.cs:120` | **LIVE** | |
| `AirAttackState` | `AirStates.cs:146` | **LIVE** | |
| `AirFleeState` | `AirStates.cs:200` | **LIVE** | |
| `HelicopterIdleState` | `HelicopterStates.cs:359` | **LIVE** | |
| `HelicopterApproachState` | `HelicopterStates.cs:455` | **LIVE** | |
| `HelicopterAttackRunState` | `HelicopterStates.cs:671` | **dead on shipped content** | entered only inside `if (!standoff)` (`:565-573`) and `StandoffEngagement: true` on both profiles (`ai.yaml:1419`, `:1446`). Reachable only on a profile that turns standoff off, and none ships. §3.2 |
| `HelicopterWithdrawState` | `HelicopterStates.cs:762` | **LIVE** | |
| `HelicopterReturnState` | `HelicopterStates.cs:900` | **LIVE** | |
| `GroundUnitsIdleState` | `GroundStates.cs:31` | dead | no `Assault`/`Rush` squad (§2.1, §2.2) |
| `GroundUnitsAttackMoveState` | `GroundStates.cs:101` | dead | " |
| `GroundUnitsAttackState` | `GroundStates.cs:184` | dead | " |
| `GroundUnitsFleeState` | `GroundStates.cs:260` | dead | " |
| `GroundUnitsRegroupState` | `GroundStates.cs:299` | dead | " |
| `NavyUnitsIdleState` | `NavyStates.cs:55` | dead | `NavalUnitsTypes` empty (§2.4) |
| `NavyUnitsAttackMoveState` | `NavyStates.cs:95` | dead | " |
| `NavyUnitsAttackState` | `NavyStates.cs:178` | dead | " |
| `NavyUnitsFleeState` | `NavyStates.cs:236` | dead | " |
| `UnitsForProtectionIdleState` | `ProtectionStates.cs:16` | dead | `ProtectionTypes` empty (§2.3) |
| `UnitsForProtectionAttackState` | `ProtectionStates.cs:23` | dead | " |
| `UnitsForProtectionFleeState` | `ProtectionStates.cs:64` | dead | " |

**Also dead by consequence:** `AttackOrFleeFuzzy` (`Squads/AttackOrFleeFuzzy.cs`, 275 lines, the whole fuzzy
attack-or-flee evaluator) — its only callers are `GroundStateBase.ShouldFlee` (`GroundStates.cs:22`),
`GroundUnitsIdleState` (`:57`), `GroundUnitsRegroupState` (`:343`), `NavyStateBase` (`NavyStates.cs:22`),
`NavyUnitsIdleState` (`:81`) and `TryToRushAttack` (`SquadManagerBotModule.cs:442`). Every one is
unreachable. `StateBase.ExcludeTacticallyCommitted` (`:155-171`) likewise (§1.4).

**Line count:** of the 5,464 lines in the squad layer (all file lengths re-counted at `25a8aebd`), **987**
never execute as whole files — `GroundStates` 382 + `NavyStates` 251 + `ProtectionStates` 79 +
`AttackOrFleeFuzzy` 275. Add the dead members of `SquadManagerBotModule` (§7.1 itemises them at ~130 lines:
`TryToRushAttack` ~30, `CreateAttackForce` + the multi-axis split ~60, `ProtectOwn`/`RespondToAttack` ~40) and
the total is **≈1,120**. Earlier drafts quoted "~1,050" for the state files alone and then added the manager
lines again on top; 987 + ~130 is the arithmetic that actually reconciles.

### 2.8 Dead configuration in live YAML

A second-order consequence worth its own callout, because it silently wastes tuning effort. These fields are
set in all four live `SquadManagerBotModule` blocks and are read **only** by unreachable code:

| Field | Set at | Only consumer | Status |
|---|---|---|---|
| `AttackScanRadius: 48` | `ai.yaml:1244, :1335, :1792, :1810` | `GroundStates.cs:165`, `NavyStates.cs:159` | **inert** |
| `SquadSize: 2` | `:1241, :1332, :1789, :1807` | `CreateAttackForce:368,386`, `TryToRushAttack:433` | **inert** |
| `SquadSizeRandomBonus: 1` | `:1242, :1333, :1790, :1808` | `CreateAttackForce:368` | **inert** |
| `RushInterval: 600` | `:1243, :1334, :1791, :1809` | the countdown to `TryToRushAttack` (`:209-210, :270`) | **inert**† |

† Not *entirely* inert: `TraitEnabled:210` draws from `World.LocalRandom` using `RushInterval`, so changing it
shifts the shared RNG stream and breaks byte-identity against a recorded benchmark baseline. Treat it as
frozen, not as a knob.

Live fields on the same trait, for contrast: `AirUnitsTypes` (`:361`), `ExcludeFromSquadsTypes` (`:308`),
`UseUnitRoles` (`:356`), `IgnoreGroundUnits` (`:329`), `IgnoredEnemyTargetTypes` (`:175`), `DangerScanRadius`
(`AirStates.cs:64,95`; `StateBase.cs:88`), `AssignRolesInterval`/`AttackForceInterval` (`:283, :276`).

`IncludeInSquadTypes` (`SquadManagerBotModuleInfo:28`) deserves a special mention: its only use site is
**commented out** at `:306-307` (`// FF TODO: This could be useful`). It is a documented, lint-visible field
that has never done anything, in either OpenRA or WW3MOD.

### 2.9 A navigation trap

`Squads/States/GroundDangerNav.cs` **[WW3MOD]** (143 lines, influence-stack Stage E) sits in the state
directory but is **not a state and is not used by any state**. Its consumers are
`PoiOffensiveBotModule.cs:3005, :3628` and `SupplyFollowerBotModule.cs:724, :1420`. It is live, important
code filed inside a folder of dead code. `HeliDangerNav.cs` in the same folder *is* used by the heli states
(`HelicopterStates.cs:584, :587, :812`) and is correctly placed.

---

## 3. The state machines that run

Cadence matters for reading these, and the two differ by 15×.

| Driver | Interval | Real time @ `Timestep: 60` (`mod.yaml:369-371`, `DefaultSpeed: default` at `:347`) |
|---|---|---|
| Air squads — `SquadManagerBotModule.cs:274-279` | `AttackForceInterval` = 75 ticks (C# default `:72`, not overridden) | **4.5 s** per state tick |
| Heli squads — `HelicopterSquadBotModule.cs:508-512` | `SquadUpdateInterval` = 5 ticks (`:146`) | **0.3 s** per state tick |

`Squad.Update()` → `FuzzyStateMachine.Update` → `currentState.Tick(squad)` (`Squad.cs:88-92`,
`StateMachine.cs:18-21`).

### 3.1 Fixed-wing air squad — 3 states **[OpenRA]**

`AirStates.cs`: 7 commits, first is the OpenRA import; WW3MOD's changes are a LINQ-to-loop perf pass
(`dc3cc20e`) and two order-gate comment/annotation commits. **The logic is upstream's.**

```
                    ┌─────────────────────────────────────────────┐
                    │              AirIdleState  :120             │
                    │  ShouldFlee? ──────────────────────► Flee   │  :129-133
                    │  FindDefenselessTarget == null? → stay      │  :135-137
                    │  else set target, →                         │  :139-140
                    └──────────────┬──────────────────────────────┘
                                   ▼
                    ┌─────────────────────────────────────────────┐
                    │             AirAttackState  :146            │
                    │  target invalid → FindClosestEnemy          │  :155-160
                    │      none found ────────────────────► Flee  │  :163
                    │  !NearToPosSafely(target) ──────────► Flee  │  :168-171
                    │  per unit: Attack order                     │  :192-193
                    │  (never leaves except via Flee)             │
                    └──────────────┬──────────────────────────────┘
                                   ▼
                    ┌─────────────────────────────────────────────┐
                    │              AirFleeState  :200             │
                    │  per unit: Move to random own building      │  :224
                    │  UNCONDITIONALLY → AirIdleState             │  :227   ⚠ one-shot
                    └─────────────────────────────────────────────┘
```

| State | Entry | Exit | Orders emitted |
|---|---|---|---|
| `AirIdleState` | squad creation; from `AirFleeState` | flee check `:129`; target acquired `:140` | none |
| `AirAttackState` | from Idle `:140` | no target `:163`; target position unsafe `:170` | `Attack` per unit `:193`; `ReturnToBase` `:187` (dead — §7.3) |
| `AirFleeState` | from Idle `:131`, from Attack `:163/:170` | always, same tick `:227` | `Move` per unit `:224`; `ReturnToBase` `:217` (dead) |

**Threat model.** `ShouldFlee` (`AirStates.cs:114-117`) flees when
`CountAntiAirUnits(enemies) * 3 > owner.Units.Count` — i.e. one AA unit chases away up to three aircraft, and
the base `ShouldFlee` (`StateBase.cs:83-104`) first cancels the whole check if **any own building** is within
`DangerScanRadius` (`:93-95`). Target selection is `FindSafePlace` (`AirStates.cs:61-85`), a shuffled sweep
of the whole map on a `DangerScanRadius` grid, returning the first cell that is "safe" and contains an enemy.

### 3.2 Helicopter squad — 5 states **[WW3MOD]**

`HelicopterStates.cs`: 1002 lines, 13 commits, first is `9dfd8620`. No OpenRA ancestor. This is the most
developed state machine in the repo, and the only one that consumes the influence stack.

```
   ┌──────────────────────────────────────────────────────────────────────┐
   │                      HelicopterIdleState  :359                       │
   │  any unit rearming → hold                                    :369-371│
   │  squad HP < 80 → hold ("wait for repair")                    :374-375│  ⚠ §7.3
   │  no ammo && !SkipRearmReadyCheck → hold                      :381-382│
   │  strategic pin holds → resume pinned objective ──────────────► Approach :395
   │  ThreatMap weak-cell target, else closest non-too-hot enemy  :402-436│
   │  no target → hold                                            :438-439│
   └───────────────────────────────┬──────────────────────────────────────┘
                                   ▼
   ┌──────────────────────────────────────────────────────────────────────┐
   │                    HelicopterApproachState  :455                     │
   │  ShouldFlee (squad HP < role FleeHealthPercent) ─────────► Return    │  :478
   │  target invalid, pin lapsed ─────────────────────────────► Idle      │  :502
   │  target too hot, no soft swap ───────────────────────────► Withdraw  │  :533
   │  AA danger spike over own position (Stage D) ────────────► Withdraw  │  :554
   │  !StandoffEngagement && dist < 8 cells ──────────────────► AttackRun │  :571
   │  orders: AttackMove to leashed/detoured cell :639  OR  Attack :642   │
   │  stuckTicks > 200 (≈60 s) ───────────────────────────────► Idle      │  :651
   └───────────────────────────────┬──────────────────────────────────────┘
              (standoff off only — NEITHER shipped profile takes this edge)
                                   ▼
   ┌──────────────────────────────────────────────────────────────────────┐
   │                   HelicopterAttackRunState  :671                     │
   │  SendDamagedUnitsHome                                          :688  │
   │  ShouldFlee ─────────────────────────────────────────────► Withdraw  │  :693
   │  attackTicks >= role HitAndRunCooldown ──────────────────► Withdraw  │  :711  ⚠ §7.5
   │  target dead, no replacement within 12 cells ────────────► Withdraw  │  :731
   │  orders: Attack per unit                                       :753  │
   └───────────────────────────────┬──────────────────────────────────────┘
                                   ▼
   ┌──────────────────────────────────────────────────────────────────────┐
   │                    HelicopterWithdrawState  :762                     │
   │  squad HP < 50 or out of ammo ───────────────────────────► Return    │  :792
   │  withdrawTicks < 75 (≈22 s): Move to safest air/threat cell    :853  │
   │  after that, HP >= 70 and has ammo:                                  │
   │     pin holds and not too hot ───────────────────────────► Approach  │  :876
   │     else closest non-too-hot enemy ──────────────────────► Approach  │  :887
   │  otherwise ──────────────────────────────────────────────► Return    │  :894
   └───────────────────────────────┬──────────────────────────────────────┘
                                   ▼
   ┌──────────────────────────────────────────────────────────────────────┐
   │                     HelicopterReturnState  :900                      │
   │  per unit: ReturnToBase                                        :914  │  ⚠ §7.3 no-op
   │  UNCONDITIONALLY → HelicopterIdleState                         :918  │  ⚠ one-shot
   └──────────────────────────────────────────────────────────────────────┘
```

**`HelicopterAttackRunState` is conditionally dead.** It is entered only from `HelicopterApproachState:571`,
inside `if (!standoff)` (`:565`). `StandoffEngagement: true` is set on **both** shipped profiles
(`ai.yaml:1419` `@stable`, `:1446` `@experimental`). So on either shipped profile the close-range attack run
— and with it the entire hit-and-run cooldown mechanic — **never executes**. It survives as the legacy path
for a profile that turns standoff off, and none ships.

> **OPINION:** this is the single most misleading thing in the live heli FSM. `HitAndRunCooldown` is
> configured per airframe in `aircraft-america.yaml:114, :277` and `aircraft-russia.yaml:106, :278`, it is
> the trait's most doctrine-flavoured knob, and it is unreachable on both shipping profiles. (Note the
> `:114` value belongs to the littlebird's **Scout** role, which never forms a squad at all —
> `TryLaunchAttackMission` selects only `AttackHeavy`/`AttackLight`, `HelicopterSquadBotModule.cs:788-789` —
> so that one is inert twice over.) Either delete
> `HelicopterAttackRunState` and the field, or rebuild hit-and-run inside the standoff path where it can
> actually fire.

**Experimental gating.** Almost every behaviour in this FSM is behind a default-off `Info` flag resolved via
`GetHeliModuleInfo` (`:160-169`). Both shipped profiles enable `SkipRearmReadyCheck`, `StandoffEngagement`,
`DangerFieldAvoidance`, `ForwardStaging`, `MinFrontierDistanceCells: 4`; only `@experimental` adds
`AllowSoloAttackHeli`, plus the evacuation / pinning / hysteresis family (`ai.yaml:1409-1462+`). `@stable` is
therefore **not** at engine defaults here — it inherited most of the heli improvements, per the
project's standing "`@stable` inherits improvements" policy.

### 3.3 Dead state machines — read this instead of reading them

For completeness, and so nobody spends an afternoon in `GroundStates.cs`:

- **Ground (5 states, `GroundStates.cs`)** **[MODIFIED]** — Idle → AttackMove → Attack, with Flee → Regroup.
  `GroundUnitsRegroupState` (`:299-381`) and the `ThreatMapManager`-based retreat in
  `GroundUnitsFleeState.Activate` (`:264-281`) are WW3MOD additions (`4548fe6c`), as is the Hunt-stance
  set (`10014709`) and the multi-axis `ApproachWaypoint` consumption at `:63-65` (`b989e83f`). All of it
  unreachable. **Ground behaviour lives in `PoiOffensiveBotModule`.**
- **Navy (4 states, `NavyStates.cs`)** **[OpenRA]** — a near-copy of the ground machine plus a naval-production
  pathing heuristic (`:34-49`) that hunts for enemy shipyards. Unreachable, and doubly meaningless: the
  heuristic keys off `NavalProductionTypes`, another never-set field, in a mod with no production buildings.
- **Protection (3 states, `ProtectionStates.cs`)** **[OpenRA]** — base-defence reaction. Unreachable.

---

## 4. States that issue an order once and then transition

The task brief asks for these specifically, because that shape can never re-offer an order that gets dropped,
and it has already caused a real defect. Here is the full set, with the current status of the hazard.

**The hazard is real and is explicitly named in the engine.** `BotOrderDamping`
(`OpenRA.Game/Traits/TraitsInterfaces.cs:437-450`) documents that a `Recurring` (droppable) order asserts the
issuing module re-offers on its own cadence, and then says outright:

> *"A site that transitions state once and never revisits (the squad-state FSMs) can NEVER satisfy (1)."*
> — `TraitsInterfaces.cs:447-448`

**The hazard is currently defused, by an inverted default rather than by care.** `Protected = 0` is the
default (`:440`), and `IBot.QueueOrder(Order)` without a damping argument is `Protected`, so the gate never
drops it (`ModularBot.cs:137-145`). I verified that **none of the 29 `QueueOrder` call sites under
`Squads/` passes a `BotOrderDamping` argument** — every squad-state order is Protected. The comment at
`AirStates.cs:221-223` records this deliberately, and `HelicopterWithdrawState:846-851` reasons about it in
detail for its pre-loop cell stamp.

| State | Order site | Transition | Live? |
|---|---|---|---|
| `AirFleeState` | `Move` per unit `AirStates.cs:224` | → `AirIdleState`, same tick `:227` | **LIVE** |
| `HelicopterReturnState` | `ReturnToBase` per unit `HelicopterStates.cs:914` | → `HelicopterIdleState`, same tick `:918` | **LIVE** |
| `GroundUnitsIdleState` | `AttackMove` grouped `GroundStates.cs:67` | → `GroundUnitsAttackMoveState` `:70` | dead |
| `GroundUnitsFleeState` | `Move` per unit `GroundStates.cs:290` | → `GroundUnitsRegroupState` `:293` | dead |
| `NavyUnitsIdleState` | `AttackMove` grouped `NavyStates.cs:83` | → `NavyUnitsAttackMoveState` `:86` | dead |
| `NavyUnitsFleeState` | `GoToRandomOwnBuilding` `NavyStates.cs:245` | → `NavyUnitsIdleState` `:246` | dead |
| `UnitsForProtectionFleeState` | `GoToRandomOwnBuilding` `ProtectionStates.cs:73` | → `UnitsForProtectionIdleState` `:74` | dead |

Two near-misses worth knowing:

- `HelicopterWithdrawState:841-857` issues `Move` and then `return`s **without** transitioning while
  `withdrawTicks < 75`, so it *does* re-offer — except when hysteresis is on, where `committedRetreatCell`
  is stamped **before** the loop (`:836`). The in-code comment (`:846-851`) argues this is sound precisely
  *because* the order is unmarked/Protected. It is a correct argument that depends entirely on nobody ever
  marking that site `Recurring`.
- `HelicopterSquadBotModule.StageIdleHelicopters:718-721` does it right — it checks `QueueOrder`'s return
  value before advancing `stagedTo`, with a comment explaining why (`:716-717`). That is the pattern the FSM
  sites cannot use.

> **OPINION:** the current safety is a property of a default, and defaults get changed. The two live sites
> above should carry an explicit comment naming the invariant ("this order MUST stay Protected because this
> state transitions once"), as `HelicopterWithdrawState` already does. Better still, the `Recurring` marker
> should be statically forbidden inside `Squads/States/` — a lint rule, not a convention.

---

## 5. Provenance, per component

Established by `git log --follow` on each file. The OpenRA import is `7362fbc6`; `1f6ea0d4 "2022
integration"`, `4eed77af "Progressive fog (#5)"`, `c5bb5ece`/`71687440`/`76e484e1`/`7e2321aa`/`6f9dd239`
(the `release-20250330` merge chain) and `c7fa10d8` are all upstream-tracking, not WW3MOD design.

| Component | Lines | Provenance | WW3MOD's changes |
|---|---|---|---|
| `Squads/StateMachine.cs` | 40 | **[OpenRA]** | none |
| `Squads/AttackOrFleeFuzzy.cs` | 275 | **[OpenRA]** | none — and now unreachable |
| `Squads/Squad.cs` | 145 | **[MODIFIED]** | `ApproachWaypoint` (`b989e83f`); `StrategicTarget`/`StrategicCommitTick` + `Helicopter` squad type (`4aca4aa0`, `9dfd8620`) |
| `Squads/States/StateBase.cs` | 173 | **[MODIFIED]** | `SetSquadEngagementStance` (`10014709`); `ExcludeTacticallyCommitted` (`51024b70`) |
| `Squads/States/AirStates.cs` | 232 | **[OpenRA]** | perf pass only (`dc3cc20e`) + order-gate comments |
| `Squads/States/GroundStates.cs` | 382 | **[MODIFIED]** | Hunt stance + ammo awareness (`10014709`); ThreatMap retreat + `GroundUnitsRegroupState` (`4548fe6c`); multi-axis waypoint (`b989e83f`); `tacpos` exclusion (`51024b70`) |
| `Squads/States/NavyStates.cs` | 251 | **[OpenRA]** | perf pass only |
| `Squads/States/ProtectionStates.cs` | 79 | **[OpenRA]** | none |
| `Squads/States/HelicopterStates.cs` | 1002 | **[WW3MOD]** | entire file (`9dfd8620` →) |
| `Squads/States/HeliDangerNav.cs` | 175 | **[WW3MOD]** | influence stack Stage D (`36921468`) |
| `Squads/States/GroundDangerNav.cs` | 143 | **[WW3MOD]** | influence stack Stage E (`ab7bd283`) — misfiled, §2.9 |
| `SquadManagerBotModule.cs` | 565 | **[MODIFIED]** | multi-axis split `:373-412` (`b989e83f`, `4d8112b2`); `IgnoreGroundUnits` `:40,:329-336` (`e7921ef1`); `UseUnitRoles`/`IsAirSquadUnit` `:48,:354-362` (`232947ce`); `ActorNameCase` hardening `:106-114` (`fe70b6c1`) |
| `HelicopterSquadBotModule.cs` | 2002 | **[WW3MOD]** | entire file (`9dfd8620` →) |

**Read that table as a shape:** WW3MOD's real investment in this layer went into the **helicopter** module
(3,000+ lines, wholly new, influence-stack aware) and into **hollowing out** the inherited manager
(`IgnoreGroundUnits`). The inherited ground/naval/protection machinery was never modernised — it was bypassed
and left in place.

---

## 6. Where the inherited design fights WW3MOD

This is the section the brief asks for by name. Each item is a place where stock OpenRA assumes something
that WW3MOD does not have.

### 6.1 "The AI has a Construction Yard"

`GetRandomBaseCenter` (`SquadManagerBotModule.cs:125-132`) looks for an owned actor in
`ConstructionYardTypes` and falls back to `initialBaseCenter`, which is only ever written by
`IBotPositionsUpdated.UpdatedBaseCenter` (`:476-479`). On the four live instances `ConstructionYardTypes` is
**empty** (§2.2), so this always returns `initialBaseCenter` — whatever another module last broadcast, or
`default(CPos)` = `(0,0)` if nothing ever did.

That matters because it is reachable: `RandomBuildingLocation` (`StateBase.cs:29-38`) calls it as the
fallback when the player owns no `Building`-trait actors, and `RandomBuildingLocation` is on the **live**
path at `AirStates.cs:224` and `HelicopterStates.cs:822`.

In practice the player does own a Building — the Supply Route (`ConstructionYardTypes: supplyroute` is how
`BaseBuilderBotModule@normal` wires it, `ai.yaml:1183`) — so `buildings.Count > 0` at `StateBase.cs:34` and
the fallback is not taken. **The dangerous branch is shadowed, not removed.**

> **OPINION:** "flee to a random one of your buildings" is an RA idea. In WW3MOD a player's buildings are the
> SR plus whatever local defences it produced, which means "flee home to the beachhead" — often straight
> across the map. For aircraft that is nearly always wrong: the correct heli/plane retreat is *away from the
> AA envelope*, which the heli module already computes (`HeliDangerNav.SafestAirCellOnRing`,
> `HelicopterStates.cs:812`). `AirFleeState` should use the same thing rather than `RandomBuildingLocation`.

### 6.2 "There are production buildings to rush, and shipyards to path to"

`TryToRushAttack` (`:425-455`) is built entirely around locating the enemy Construction Yard and rushing it
before defences exist. `NavyStateBase.FindClosestEnemy` (`NavyStates.cs:25-52`) navigates by finding enemy
naval *production* buildings, on the explicit reasoning that they are reliably pathable
(`:29-31`). Both are dead, and both would be conceptually meaningless if revived: there is one indestructible
SR per player, fixed at spawn, and no production buildings to find.

### 6.3 "Aircraft rearm and repair at a building" — the biggest live misfit

This one is on the **live** path and shapes real match behaviour.

**Fact:** WW3MOD builds no rearm hosts. `HPAD` has `Prerequisites: ~disabled, ~techlevel.medium`
(`mods/ww3mod/rules/ingame/structures.yaml:432`) and `AFLD` likewise (`:500`). Neither can be produced,
despite `BaseBuilderBotModule@normal` listing both in `BuildingLimits`/`BuildingFractions`
(`ai.yaml:1199-1204`) — that configuration is inert against the prerequisite.

**Fact:** the aircraft still declare rearm hosts. `Rearmable: RearmActors: hpad` on the attack helis
(`aircraft-america.yaml:218-219, :375-376`), `RearmActors: afld, AmmoPools: primary-ammo, secondary-ammo` on
the A-10 (`:497-499`).

**Fact:** a `ReturnToBase` order in that situation is a no-op with a delay. `Aircraft.ResolveOrder` accepts it
(`Aircraft.cs:1316-1327`) because `RearmActors.Count != 0`; the activity then finds no `Reservable`
resupplier (`ReturnToBase.cs:39-51`), falls to `QueueChild(new FlyIdle(...)); return true`
(`:106-108`), and the aircraft idles where it stands. It never rearms and never repairs.

Three consequences follow, and they are of different kinds:

1. **The inherited ammo branches in `AirStates` are dead code inside a live state.**
   `ReloadsAutomatically(pools, rearmable)` (`StateBase.cs:129-139`) returns **true** when every pool is
   covered by a `Rearmable`. The A-10's `Rearmable` names both of its pools, so it returns true, so the
   guarded blocks at `AirStates.cs:180-190` and `:212-219` — the only `ReturnToBase` sites in the air FSM —
   never run. Fixed-wing aircraft simply fight until they die.
2. **The same predicate nearly bricked helicopter squads, and is patched rather than fixed.**
   `SquadHasAmmo` (`HelicopterStates.cs:120-133`) *skips* every unit for which `ReloadsAutomatically` is
   true, then returns false if none remain — so an all-attack-heli squad reports "no ammo" **at full ammo**.
   The comment at `:135-139` states this exactly. `SkipRearmReadyCheck` (`:186`, on for both profiles,
   `ai.yaml:1418, :1445`) bypasses it. The predicate itself is still wrong; it is simply routed around.
3. **The "wait for repair" gates are unsatisfiable.** `HelicopterIdleState:374-375` refuses to launch below
   80% squad health; `IsReadyForMission:1387-1394` refuses any heli below its role's `ReEngageHealthPercent`
   — which is **90** on both transports (`aircraft-america.yaml:9`, `aircraft-russia.yaml:9`) and 75–80 on
   the attack helis. With no repair host, health only ever decreases. **A helicopter that takes one chip of
   damage becomes permanently unusable.** The only thing that reclaims it is `EvacuateWhenIdle` /
   `EvacuateIdleTransports` — `@experimental` only, default off in C# (`:257`, `:130`). On `@stable`, a
   chipped heli parks for the rest of the match.

> **OPINION:** this is the clearest "outdated module" in the whole layer, and it is not fixed by more flags.
> The mod has no repair and no rearm; therefore health and ammo are **one-way resources** and the correct
> doctrine is *use it or bank it*, which is exactly what the evacuation work reinvented. The inherited
> `ReloadsAutomatically`/`HasFullAmmo`/`ReEngageHealthPercent` triad should be deleted from the launch path
> and replaced with a single "is this airframe still worth committing?" predicate. Leaving three
> unsatisfiable gates in place and bypassing each with its own flag is why the heli module has 50+ `Info`
> fields.

### 6.4 "The bot can see everything"

Every target-selection read in the live squad states goes to ground truth, not to the belief store:

| Read | Site | Fog-legal? |
|---|---|---|
| `FindSafePlace` / `NearToPosSafely` | `AirStates.cs:61-111` | no — `World.FindActorsInCircle` + `IsPreferredEnemyUnit`, no visibility test |
| `SquadManagerBotModule.FindClosestEnemy(pos)` | `:228-232` | partially — prefers `IsNotHiddenUnit` but **falls back to the omniscient set** if none qualifies (`:231`) |
| `HelicopterStateBase.FindClosestEnemy` | `HelicopterStates.cs:323-336` | no — walks `World.Actors` directly |
| `IsTargetTooHot` / `CountAntiAirNearTarget` | `HelicopterStates.cs:338-351` | no |
| soft-target swap | `HelicopterStates.cs:512-517` | no |
| `HelicopterIdleState` weak-cell pick | `HelicopterStates.cs:403-427` (`ThreatMapManager`) | no |

Contrast the **fog-legal** additions layered on top: the Stage-D AA routing reads `DangerFieldLayer`
(`:584-589`), the frontier standoff reads `ControlField` (`:598-601`), and the evacuation/drop-site logic
reads the belief store. So the heli FSM currently **picks its target omnisciently and then routes to it
fog-legally.** That is a coherent half-conversion, but it is a half.

> **OPINION:** the target-selection reads are the highest-value fog conversion left in this layer, and they
> are also the most visible to a human opponent — a bot that flies straight at a unit it has never seen reads
> as cheating in a way that a slightly suboptimal route does not. `IsTargetTooHot` is the worst of them: the
> bot declines to attack because of AA it cannot legally know about, which makes it *look* cowardly for
> reasons the player cannot observe. See [`influence-stack.md`](../reference/influence-stack.md) for the
> belief-side reads that already exist.

### 6.5 "Squad size means readiness"

`SquadSize` / `SquadSizeRandomBonus` (`:60-63`) encode "wait until you have 8 + up to 30 units, then attack" —
a production-economy idea, where the queue keeps producing and the question is when to spend the stock. In
WW3MOD, calling a unit in *is* the spend. Both fields are inert here (§2.8), but the same assumption survives
live in the heli module as `AttackSquadSize`/`AttackSquadSizeBonus` (`:27-30`), and `@experimental` had to add
`AllowSoloAttackHeli` + a `PairUpIncomeThreshold` (`:40-50`) to escape it — the `[Desc]` at `:35-39` says so
in as many words.

### 6.6 Counters measured in the wrong unit

Two counters in this layer are incremented **once per squad update**, not once per world tick, while their
documentation says "ticks". Same family as the `EvacDangerThreshold` class of defect.

| Counter | Declared as | Actually | Real duration @ `Timestep: 60` |
|---|---|---|---|
| `GroundUnitsRegroupState.MaxRegroupTicks = 750` (`GroundStates.cs:302`) | comment: *"~12.5 seconds"* | 750 × `AttackForceInterval` (75) world ticks | **≈ 56 minutes** (dead code) |
| `AIHelicopterRoleInfo.HitAndRunCooldown` (`AIHelicopterRole.cs:33-34`) | `[Desc]`: *"Ticks of engagement"* | N × `SquadUpdateInterval` (5) world ticks | Apache's 200 (`aircraft-america.yaml:277`) → **1000 ticks ≈ 60 s**, not 200 ticks ≈ 12 s |

The heli one is doubly hidden because its consuming state is itself unreachable on both profiles (§3.2).
`HelicopterApproachState.stuckTicks > 200` (`:651`) and `HelicopterWithdrawState.withdrawTicks < 75` (`:797`)
are in the same units — those two are **live**, and work out to ≈60 s and ≈22 s respectively, which look
deliberate. Logged in [`WORKSPACE/bugs/discovered.md`](../../WORKSPACE/bugs/discovered.md).

### 6.7 Config fields that are never read at all

`AIHelicopterRoleInfo` declares nine fields. **Five are read by no C# code anywhere in the repo** —
`EngagementRange` (`:25`), `PreferSoftTargets` (`:37`), `AvoidAntiAirRange` (`:40`), `AIBuildPriority`
(`:43`) and `AIBuildLimit` (`:46`) — re-verified by grep across `engine/`, which returns zero hits outside
`AIHelicopterRole.cs` for each. The first three are nonetheless configured per airframe in the mod
(`aircraft-america.yaml:10, :111, :115-116, :274, :278-279`; `aircraft-russia.yaml:10, :103, :107` and
neighbours); the two `AIBuild*` fields are dead because unit call-in weight is driven by
`UnitBuilderBotModule`'s own `UnitsToBuild`/`UnitLimits` instead. The four that *are* read are `Role`,
`FleeHealthPercent`, `ReEngageHealthPercent` and `HitAndRunCooldown` — and the last of those is read only from
`HelicopterStates.cs:704`, inside the state §3.2 shows is unreachable. (An earlier draft of this paragraph
opened "Three are read by no C# code" and then named five in the same breath; §7.1 and §8 have always said
five.)

So: a maintainer tuning "how close does the Apache engage" or "does the Hind avoid AA" by editing
`aircraft-america.yaml` changes **nothing**. Logged in `discovered.md`.

---

## 7. Fitness assessment — keep, replace, delete

**Everything in this section is OPINION.** The facts it rests on are cited above.

### 7.1 Delete

| Component | Lines | Why |
|---|---|---|
| `Squads/States/GroundStates.cs` | 382 | Unreachable on both profiles. Ground doctrine lives in `PoiOffensiveBotModule` and is being actively developed there. Keeping a second, older, unreachable ground FSM guarantees someone will one day fix a ground bug in it. The `4548fe6c` regroup work is the only part worth anything, and its ideas belong in the POI module. |
| `Squads/States/NavyStates.cs` | 251 | Unreachable, and its central heuristic (navigate by enemy naval production) has no referent in a mod with no production buildings. If naval combat is ever wanted, it should be an axis type, not a resurrected RA squad. |
| `Squads/States/ProtectionStates.cs` | 79 | Unreachable. Base defence is `LayeredDefenceBotModule`'s job and it does it better. |
| `Squads/AttackOrFleeFuzzy.cs` | 275 | Unreachable once the above go. A fuzzy-logic engagement evaluator is a fine idea, but this one is upstream's, tuned for RA unit values, and nothing in WW3MOD calls it. |
| `SquadManagerBotModule.TryToRushAttack` + `SquadType.Rush` | ~30 | Dead twice over (§2.2) and conceptually void — there is no CY to rush. |
| `SquadManagerBotModule.CreateAttackForce` + the multi-axis split | ~60 | Dead (§2.1). The multi-axis *idea* survives and is better realised as POI axes; this implementation is a coin-flip 60% split with `ThreatMapManager` waypoints. |
| `SquadManagerBotModule.ProtectOwn` / `RespondToAttack` | ~40 | Dead (§2.3). |
| `IncludeInSquadTypes` | field | Its use site has been commented out since upstream (`:306-307`). |
| `AIHelicopterRoleInfo.EngagementRange`, `PreferSoftTargets`, `AvoidAntiAirRange`, `AIBuildPriority`, `AIBuildLimit` | 5 fields | Read by nothing (§6.7). Delete the fields *and* their YAML, or implement them. Configured-but-inert is worse than absent because it invites tuning that cannot work. |
| The four inert YAML tunables (§2.8) | — | With one caveat: removing `RushInterval` changes the `LocalRandom` draw at `:210` and breaks benchmark byte-identity, so it must be a deliberate, separately-measured removal. |

That is **987 lines of dead state machine plus ~130 lines of dead manager (≈1,120 total, §2.7)**, removable
with no behavioural change — provable, because none of it is reachable.

> The one honest argument against deleting: it is upstream code, and keeping it makes the next engine merge
> mechanical. I think that trade is already lost — `SquadManagerBotModule` has diverged (`IgnoreGroundUnits`,
> `UseUnitRoles`, the axis split, case hardening) and the merge is manual regardless. If merge cost is the
> real concern, move the dead files to `engine/…/Squads/States/_unused/` with a README, rather than leaving
> them where they read as live.

### 7.2 Replace

| Component | Replace with | Why |
|---|---|---|
| `AirStates.cs` (all 3 states) | a heli-style FSM, or fold fixed-wing into `HelicopterSquadBotModule` | The air FSM is essentially untouched OpenRA: omniscient grid-sweep targeting (`:61-85`), a flee rule of "3 aircraft per AA unit" (`:114-117`), retreat to a random own building (`:224`), and dead ammo branches (§6.3). Meanwhile the heli module next door has fog-legal AA routing, frontier standoff, strategic pinning, hysteresis and evacuation. **Two aircraft FSMs of wildly different maturity, in the same codebase, for the same problem.** Fixed-wing should inherit the heli module's machinery, not keep its own 2007-era one. |
| `StateBase.ShouldFlee` (`:83-104`) | a danger-field read | It cancels the entire flee decision if any own building is within `DangerScanRadius` (`:93-95`) — an RA "you're at base, stand and fight" rule. In WW3MOD "own building" mostly means the SR, so it encodes "never retreat near the beachhead." |
| `Squad`'s dependency on `SquadManagerBotModule` | an interface / injected tunables | §1.2. Cheap, and removes a hidden coupling that makes heli behaviour depend on fixed-wing YAML. |
| The three unsatisfiable rearm/repair gates (§6.3) | one "still worth committing?" predicate | The current design is three broken gates plus three bypass flags. |

### 7.3 Keep

| Component | Why |
|---|---|
| `HelicopterSquadBotModule` + `HelicopterStates.cs` | This is the mod's own work and it is the most sophisticated bot code in the repo. It has real problems (§7.4) but they are *its* problems, not inherited ones. |
| `HeliDangerNav.cs`, `GroundDangerNav.cs`, `HeliPathHysteresis`, `HeliMissionPinMath`, `HeliPackageMath` | Pure, world-free, NUnit-pinned decision math split out of the FSM. This is the right pattern and should be the template for anything extracted from the states next. (Move `GroundDangerNav.cs` out of `Squads/States/` — §2.9.) |
| `Squad` / `StateMachine` as containers | Small, unopinionated, fine. |
| `SquadManagerBotModule.FindNewUnits` + `IsAirSquadUnit` + `IgnoreGroundUnits` | The recruitment/hand-off mechanism works and is the thing holding the two layers apart. |
| `ExcludeTacticallyCommitted` (`StateBase.cs:155-171`) | Currently guards only dead call sites, but the *pattern* — squad orders must not override a ledger commitment — is exactly right and will be needed the moment air/heli orders start contending with POI claims. Keep it and wire it into the live sites. |

### 7.4 The three things I would fix first, in order

1. **Make `HitAndRunCooldown` reachable, or delete it** (§3.2). Both shipped profiles run
   `StandoffEngagement: true`, which routes around `HelicopterAttackRunState` entirely, so the trait's
   signature doctrine knob does nothing. This is a small change with a visible behavioural payoff, and it is
   the kind of "configured but unreachable" trap the user is specifically looking for.
2. **Convert heli target selection to the belief store** (§6.4). The routing is already fog-legal; the target
   pick is not. This is the largest remaining realism gap in live squad code, and it is the one a human
   opponent can actually perceive.
3. **Delete the dead state machines** (§7.1). Not because they cost cycles — they cost *attention*, and this
   layer's whole problem is that it looks four times bigger and four times more capable than it is.

---

## 8. Defects logged, not fixed

Per the brief, nothing here was fixed. Entries added to
[`WORKSPACE/bugs/discovered.md`](../../WORKSPACE/bugs/discovered.md) on 2026-08-09:

- `AIHelicopterRoleInfo.HitAndRunCooldown` counts squad updates, not ticks, and its consuming state is
  unreachable on both shipped profiles.
- `GroundUnitsRegroupState.MaxRegroupTicks` comment claims ~12.5 s; the real figure is ~56 minutes (dead
  code, recorded for the class).
- Five `AIHelicopterRoleInfo` fields are configured in mod YAML and read by no C# code.
- `Squads/States/GroundDangerNav.cs` is live code filed among dead state machines.

---

## 9. Cross-references

- Tick path, module cadences, order gate, unit ownership — [`02-lifecycle-and-arbitration.md`](02-lifecycle-and-arbitration.md)
- Belief store, danger/control fields, Stage A–F — [`influence-stack.md`](../reference/influence-stack.md)
- Why there are no factories — [`game-model.md`](../reference/game-model.md), [`supply-route.md`](../reference/supply-route.md)
- Engine-wide AI notes, `PoiOffensiveBotModule` fires doctrine — [`architecture.md`](../reference/architecture.md)
- Order-source census (independent confirmation of §2) — [`WORKSPACE/recon/260807-order-source-census.md`](../../WORKSPACE/recon/260807-order-source-census.md)
- How a unit fails to get owned at all — [`WORKSPACE/recon/260808-unit-purpose-census.md`](../../WORKSPACE/recon/260808-unit-purpose-census.md)
