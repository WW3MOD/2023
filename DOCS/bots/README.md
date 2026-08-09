# The bots — how they work, from start to finish

**Verified against `main` @ `dcc2f7c5`** (`git status -sb`: `main...origin/main [ahead 71]`;
`git rev-list --count HEAD..@{u}` = **0**, so this checkout is not behind upstream). Static read only — no
build, no game run, no autotest. This is a synthesis of the technical documents in this folder, but it is
**not** a summary written on trust: every load-bearing claim below was re-opened in the code at this commit,
and §7 reports the places where those documents disagree with each other or with the code.

> **Note on `06`.** [`06-inherited-misfits.md`](06-inherited-misfits.md) was written in parallel with this file
> and landed while it was being drafted. It is the **ranked, prioritised** audit; this file is the **narrative
> and the map**. They are meant to be read together, and the division of labour is spelled out at the end
> (§"The set"). Where they disagree on a number, `06` re-derived the arithmetic and wins — §7.4 records the one
> place I correct my own earlier draft because of it.

**Who this is for.** The owner of the mod, who does not live in the bot code, and who needs to be able to spot
design problems and point future work in the right direction. It is written to be read end to end, in order.
Everything else in this folder exists to be *consulted*, not read (§6).

**What it is.** The whole system, in plain language: the shape of it (§1), the complete life of one unit from
the moment it is called in to the moment it fights (§2), an honest list of what does not run (§3), what we
inherited versus what we built (§4), and the failure patterns this codebase keeps producing so you can
recognise the next one yourself (§5).

---

## How to read this document

Two markers, matching [`02`](02-lifecycle-and-arbitration.md) and [`04`](04-perception-and-fields.md).

**Provenance** — where a component came from:

| Marker | Meaning |
|---|---|
| **[OpenRA]** | Inherited from OpenRA `release-20230225` essentially unchanged. Designed for a base-building RTS with factories, harvesters and a tech tree. |
| **[MODIFIED]** | OpenRA structure, but WW3MOD changed its behaviour or added fields to it. |
| **[WW3MOD]** | Written for this mod. No OpenRA ancestor. |

**Opinion** — every paragraph beginning **`OPINION:`** is judgement, not description. Argue with those freely.
Everything else is a fact you can check, and the technical documents carry the `file:line` for it.

**Jargon.** Every term is defined where it first appears. Three you will meet constantly:

- **Module** — a self-contained piece of bot brain (e.g. "the one that runs attacks"). The bot is a bag of
  modules, each ticking on its own timer. There are 24 module classes and 42 copies of them running.
- **Order** — the same message a human player's mouse click produces ("move here", "attack that"). Bots
  produce orders too, and mostly that is *all* they produce.
- **Claim / commitment** — a note saying "this unit is mine, don't take it". There are four separate,
  incompatible systems for this, which is §1.3 and the source of a great deal of confusion.

---

## 0. The five facts that explain most of the confusion

Read these before anything else. Each is expanded later.

1. **There are two generations of bot code living side by side, and they do not talk to each other.** An older
   support layer (early 2026) records unit claims in one place and sees the map through a grid that ignores
   fog of war. A newer attack layer (mid 2026) records claims somewhere else entirely and sees the map through
   fog-legal belief fields. Neither reads the other's claim registry. §1.1.

2. **A large amount of this code never executes.** Thirteen of the twenty squad states, seven module entries
   that look alive in the config file, an entire task-board API with zero callers, and a base-builder whose
   every construction knob is dead while still reading like a tuning surface. **Do not spend a week studying
   machinery that never runs.** §3.

3. **Not everything that moves a unit is an order.** A second, invisible layer moves units by pushing an
   activity onto them directly. It produces no order, appears in no log, and the new order gate cannot see it.
   Two of its members are switched on for *human*-owned units too. §1.2.

4. **The bot's sense of danger is on a scale nobody wrote down, and its core arithmetic is wrong twice.** The
   numbers span several orders of magnitude while every threshold configured against them is between 0 and 120
   — so most thresholds mean "is there any enemy at all", not "how dangerous is this". Underneath that, the
   calculation overflows for the heaviest units and silently collapses them to the minimum value. §5, Patterns
   1–3, and [`06` §1 rank 1](06-inherited-misfits.md).

5. **The recurring defect is not a bad inherited component — it is a good inherited component wired to the
   wrong thing.** Nearly every finding across this whole document set has this shape: the OpenRA piece is fine, the
   WW3MOD piece is fine, and nobody introduced them to each other. §4.3.

---

## 1. The shape of the system, before the parts

### 1.1 Two generations, side by side **[WW3MOD]**

This is the single most useful thing to understand, and it is not written down anywhere in the code because
nobody designed it — it accumulated.

WW3MOD's bot was built in two waves:

| | **Wave 1 — the support layer** | **Wave 2 — the POI/axis layer** |
|---|---|---|
| Written | ~2026-03 | ~2026-05 to 2026-08 |
| Modules | Scout, Garrison, SupplyFollower, AdaptiveProduction, HelicopterSquad, BotBlackboard, ThreatMapManager | PoiOffensive, PoiGarrison, LaneAmbush, LayeredDefence, MountedTransport, CaptureCoordinator, EngineerRouteOpen, PoiGoalGuard |
| Records unit claims in | **`BotBlackboard`** — a plain "who holds this unit" dictionary with no expiry | **`PoiGoalGuard`** — a ledger of "unit U is pursuing objective O until tick T", with expiry |
| Sees space through | **`ThreatMapManager`** — a coarse grid that scans *every* actor in the world with **no fog-of-war check** | the **belief/danger/control fields** — built only from what the player can legally see |
| Has an `@stable` twin protecting the benchmark? | mostly **no** — several run as one shared instance | **yes** — every one is twinned |

**The two claim registries are honoured by overlapping but different sets of modules, and neither one reads
the other.** I verified this directly: the entire POI stack contains **zero** references to `BotBlackboard`
(checked across `PoiOffensiveBotModule`, `PoiGarrisonBotModule`, `LaneAmbushBotModule`,
`LayeredDefenceBotModule`, `MountedTransportBotModule`, `CaptureCoordinatorBotModule`, `PoiGoalGuard`).

So when the garrison module claims a rifleman on the blackboard, the attack module cannot see that claim, and
vice versa. Two modules can each believe, correctly by their own bookkeeping, that they own the same soldier.

**The consequence in one sentence:** the older layer has the weaker intelligence model *and* the weaker
benchmark protection *and* is invisible to the newer layer's arbitration — so it is simultaneously the most
likely place for a bug and the hardest place to measure one.

> **OPINION:** this seam explains more reader confusion than any other single fact, and it is worth naming
> explicitly whenever it comes up. It is also not a disaster: the wave-2 layer is genuinely good work and is
> the right shape for this game. The honest framing is that wave 1 was never retired, only bypassed — and
> half-retired code is more dangerous than either live or deleted code, because it still looks authoritative.

The seam is not perfectly clean, and the exception matters: **`HelicopterSquadBotModule` (wave 1) reads the
wave-2 ledger on both profiles**, and `GarrisonBotModule` reads it when its opt-in flag is set. So a small
number of modules straddle both worlds. (One document in the set states the two sets are disjoint; that is
an overstatement — see §7.1.)

### 1.2 The two ways a unit gets told to move

**Layer 1 — the order layer.** A module produces an `Order` — literally the same message type a human's mouse
click produces. Every bot order in the codebase funnels through one method, which is what made it possible to
add a single arbitration gate in one file rather than at sixty call sites. Full detail:
[`02` §3.1](02-lifecycle-and-arbitration.md), [`02` §5](02-lifecycle-and-arbitration.md).

**Layer 2 — the activity layer.** A trait attached to the *unit itself* pushes an activity ("walk to this
cell") straight onto the unit, producing **no order at all**. Five traits do this. Two are switched on for
human-owned units as well as bot-owned ones. Nothing can see them: no log, no gate, no future scheduler
placed at the order layer. Full detail: [`02` §3.2–3.3](02-lifecycle-and-arbitration.md).

**Why it matters, concretely.** A unit that layer 2 nudged two cells is momentarily "busy", so it becomes
invisible to the roughly 57 places in layer 1 that recruit "idle" units — for that one scan. On the next scan
it is idle again and gets grabbed. That flicker is manufactured by our own traits, and no instrument in the
bot can observe it.

> **OPINION:** if I could spend one architectural change here, I would spend it on giving layer 2 a shared,
> inspectable seam rather than on making layer 2 smaller. Each of those five traits is defensible on its own —
> a soldier who walks to resupply himself is *good* behaviour. The problem is that there is now no single place
> that can answer "what is this unit doing, and who decided that", because half the deciders leave no trace.

### 1.3 Four different answers to "who owns this unit"

There are four mechanisms that answer some version of "is this unit taken", and none replaces the others.
[`02` §4](02-lifecycle-and-arbitration.md) is the full treatment; the shape is:

| Mechanism | Lifetime | Can another module destroy it? |
|---|---|---|
| **`BotBlackboard` claim** (wave 1) | forever, until explicitly released or the unit dies | only by an explicit release |
| **`PoiGoalGuard` commitment** (wave 2) | expires on a timer, refreshed only inside the owning module's own periodic check | **yes** — silently, in two different ways |
| **A module's own "units available to me" list** | one scan; rebuilt from scratch every time | not applicable — it is rebuilt from flickering inputs |
| **The order gate's standing record** (new, 2026-08-08) | a fixed dwell window after an order is admitted | **no** — owned by the player, unreachable from any module |

The fourth is the newest and the only one with a lifetime that no module can reach. That single property is
why it works where seven previous attempts at the same problem failed (§5, Pattern 4).

### 1.4 The whole thing on one page

```
    THE WORLD ADVANCES ONE TICK  (60 ms; ~16.7 ticks per second)
              │
              ├─ 1. every unit advances whatever it is already doing
              │        └─ if it has nothing to do, it is flagged idle  ← layer 2 traits fire here
              │
              └─ 2. the bot brain runs                          ModularBot.Tick
                       │
                       ├─ for each of ~42 module instances, in a FIXED order:
                       │     "is my personal countdown up? if so, think, and queue orders"
                       │
                       │     ...each module reads some of:
                       │        · the belief store   "where do I think the enemy is"      ┐
                       │        · the danger fields  "how badly can I be hurt here"        ├ doc 04
                       │        · the control field  "whose ground is this"                │
                       │        · frontier distance  "how far behind the front am I"      ┘
                       │        · a claim registry   "is this unit already taken"   (one of two — §1.1)
                       │
                       ├─ 3. THE ORDER GATE inspects each queued order            (doc 02 §5)
                       │        · does another module already own this unit, at a higher rank?
                       │        · did this unit just get a different order moments ago?
                       │        └─ if either, and the order is marked droppable → drop it
                       │
                       └─ 4. release only ⌈N/5⌉ of the surviving orders this tick
                                 └─ each becomes a real order → the unit obeys it (≥2 ticks later)

    MEANWHILE, invisible to all of the above: layer 2 traits queue movement directly onto units.
```

Aircraft are the exception to step 2: they are commanded through a **squad state machine** rather than
directly, which is [`05`](05-squads-and-combat-states.md), and mostly a different world.

---

## 2. The life of one unit, start to finish

This is the spine. Follow one rifleman from the decision to call him in, to his first fight.

### Step 0 — someone decides to spend the budget **[OpenRA, heavily modified]**

There are no factories in WW3MOD. Units are **called in as reinforcements from off-map reserves** through the
**Supply Route** — a fixed, indestructible, non-buildable beachhead, one per player
([`game-model.md`](../reference/game-model.md)). "Buying" a unit is budget allocation, not manufacturing.

The module that decides *what* to call in is `UnitBuilderBotModule`, and there are **ten copies** of it, split
by profile, faction and airframe type. It runs every 30 ticks (1.8 s) on a hard-coded constant that is not
configurable from YAML.

There is a trap in its configuration that is easy to misread and is documented in the mod's own YAML: a
`UnitsToBuild` weight is a **share ceiling, not a priority**. Any weight of 100 or more never binds, so it
only marks a type "always eligible"; the real cap is `UnitLimits`. The `@experimental` profile replaces this
inherited lottery with a genuinely modern scheme — census the army you own, buy the class furthest below its
target share, bias by *believed* enemy composition. Detail: [`03` §3-B4](03-module-catalogue.md).

### Step 1 — he appears at the map edge, not at the base **[WW3MOD]**

The Supply Route uses `ProductionFromMapEdge`: the unit spawns at the **map edge nearest the Supply Route**
and must walk across open ground to a **rally point** near the beachhead
([`game-model.md`](../reference/game-model.md)). This is why reinforcement lanes can be ambushed, and it is
why every unit in the game spends its first several seconds in the same small area.

**Who chooses that rally point is worth knowing, because it is the mod's most quietly load-bearing piece of
inherited code.** It is `BaseBuilderBotModule` — the inherited base-building module whose entire construction
half is dead (§3.2). Its one surviving live behaviour walks every owned building that has a rally point and,
if the current one is invalid, picks a replacement with
`possibleRallyPoints.Random(world.LocalRandom)` — **a random buildable cell within 10 cells of the Supply
Route** (`BaseBuilderBotModule.cs:225-238`, radius from `ai.yaml:1198`).

So: the single spatial parameter that determines where every reinforcement in the game musters is chosen at
random, by a module that is otherwise entirely inert, for reasons that made sense in Red Alert. Nothing is
broken. Nothing is considered, either.

### Step 2 — he arrives, finishes walking, and becomes "idle"

"Idle" is not a status the bot maintains. It is one pointer test: *does this unit currently have anything
queued?* (`Actor.cs:75`). It is therefore true for a unit that finished its errand, a unit that was
interrupted, and a unit that has never been given anything — all indistinguishable.

Roughly 57 recruitment filters in the bot key off this flag. Because it is settled earlier in the same tick by
the activity layer (§1.2), and because layer 2 traits keep nudging units, the flag flickers for reasons no
module can see.

At this point our rifleman is standing near the beachhead, idle, and **contested**. This is the structural
fact that makes WW3MOD's arbitration problem sharper than Red Alert's: *every unit in the game is born at the
same few cells*, so every module that recruits "nearby idle units" is drawing from the same puddle at the same
moment. An idle rifleman near the Supply Route simultaneously satisfies the defence line pool, both
transports' pickup bubbles, the capture-escort radius, the engineer screen, the ambush pool, and the garrison
sweep. See [`02` §4.3](02-lifecycle-and-arbitration.md).

### Step 3 — the bot forms a picture of the world **[WW3MOD]**

Before any module decides anything, four always-on world layers have already been rebuilt (every 25 ticks,
one player at a time). This is [`04`](04-perception-and-fields.md) and it is the most important document in
the set for judging design quality.

| Layer | The question it answers | Scale |
|---|---|---|
| **Belief store** | "Where do I *think* the enemy is?" | confidence 15–100 per remembered contact |
| **Danger fields** (ground + air) | "How badly can I be hurt standing here?" | 0 … tens of millions |
| **Control field** | "Whose ground is this?" | −1000 … +1000 |
| **Frontier distance** | "How far behind the front line am I?" | 0 … 64 |

The belief store is the good news, and it is a real achievement: **a human and a bot with identical vision get
identical beliefs**, by construction. There is no code path in that file that reads an enemy the player cannot
legally see. Structures the bot has seen once are remembered and never decay; mobile units it has lost sight
of fade out over about ten seconds.

The danger fields are the bad news, and they are the subject of §5, Patterns 1–3. They are built by stamping a
"how much can this hurt me" number around every believed enemy contact. Because WW3MOD's weapon damage numbers
are two to three orders of magnitude larger than Red Alert's, and because the durability weighting was tuned
for units with 200 hit points and is being fed units with 28,000, the resulting field spans **several orders of
magnitude** — while every threshold configured against it sits between 0 and 120.

Worse, the multiplication that produces those numbers **overflows a 32-bit integer** for a high-damage,
high-health contact: it wraps negative and is then clamped to the minimum, so *a believed main battle tank
paints exactly one cell at value 1* while a light infantry fighting vehicle paints thousands. So the field does
not merely mis-scale threat, it **inverts the ranking for the heaviest units**. This was found while this
overview was being written and is [`06` §1 rank 1](06-inherited-misfits.md) and [`06` §5.1](06-inherited-misfits.md);
one consequence is that the magnitude table in [`04` §3.2](04-perception-and-fields.md) is exact arithmetic and
its heaviest row is not what the code actually produces. Treat `04` §3.2's *heavy-vehicle* figures as the
formula's intent, and `06` §5.1 as its behaviour.

**Status, so you do not chase a fixed bug:** the overflow half is being fixed on a branch (`auto/danger-scale`,
under review at the time of writing) with wider arithmetic and saturation. The *cadence-field* half — the wrong
input in Pattern 2 — is **not** fixed there and no unit change can fix it, because it is an ordering error
rather than a scale error. Check
[`WORKSPACE/bugs/discovered.md`](../../WORKSPACE/bugs/discovered.md) for current state before acting.

The control field, by contrast, is on a **designed** scale: somebody chose the clamp, the seed strength and the
contested band *together*, so a threshold of 300 against it is a statement you can reason about. That contrast
is the proof that the danger field's problem is not "thresholds are hard" — it is that the danger field has no
designed scale to threshold against.

### Step 4 — a module decides he should go somewhere **[WW3MOD]**

For a ground unit, this is almost always `PoiOffensiveBotModule` — 4,354 lines, the largest and most-worked
module in the repo, and the piece to build *toward*. It runs every 100 ticks (6 s).

Its model is genuinely modern and genuinely suited to this game: instead of forming one death-ball, it scores
enemy objectives ("POIs" — points of interest: income structures, the enemy beachhead, the enemy base) and
splits the army across up to four **axes** of advance, each aimed at one objective. Crucially there is **no
privileged beeline to the enemy base** — the base competes on the same score as everything else. It claims each
unit it assigns in the wave-2 ledger for 250 ticks.

Other modules may want him instead: the defence-line filler, either troop transport, the capture escort, the
ambush layer, the garrison sweep. What they emphatically do *not* do is negotiate.

### Step 5 — the contest is resolved

**Until 2026-08-08, when two modules wanted the same unit, the winner was decided by which module happens to
be declared later in the YAML config file.** Not by priority, not by urgency — by text position. That was
documented nowhere, and the loser was told nothing: the queueing call returned no value, so a dropped order
was indistinguishable from a delivered one, and modules re-issue on their own countdowns rather than in
response to loss. That is the thrash loop the user has been watching for weeks.

The **order gate** (merged 2026-08-08, live on both profiles — verified at `ai.yaml:47-48` and `:52-53`) is the
first thing in the bot's history that makes the winner a *stated rule*. It applies two tests:

- **Incumbency with rank.** The module that already holds the unit's commitment keeps it, unless the challenger
  outranks it. Three ranks: ambient positioning loses to ordinary combat tasking, which loses to scarce
  mission work like captures. Ties go to the incumbent — deliberately, because the old tie-break was YAML line
  order.
- **A re-order dwell.** Suppress a redirect of a unit whose current order is young, still running, and aimed
  somewhere else. Same destination is admitted; an idle unit is admitted.

Two design choices in it are worth internalising because they are the right instincts:

- **Everything fails open.** An unknown objective, an unknown order type, an unrecognised module, a missing
  ledger — all admit the order. A future module that nobody remembered to add to the rank table can still give
  orders; it just gets less damping. *Table rot degrades to "no suppression", never to "this module silently
  cannot command anything."*
- **Suppressible is opt-in, not opt-out.** The first attempt made every movement order droppable unless marked
  as an emergency, and two review rounds found six places where nobody had marked one — a flee, a withdrawal,
  a disengage, a capture extraction. Forgetting an annotation cost **safety**. Inverted, forgetting one costs
  only **damping**. As shipped, exactly four call sites are droppable.

Be precise about the gate's reach, because it is easy to over-credit: it damps churn, it does not schedule
attention; it cannot see layer 2 at all; and only four call sites can actually be dropped.
[`02` §5.7](02-lifecycle-and-arbitration.md) lists what it does not fix.

### Step 6 — the order waits its turn **[OpenRA]**

Queueing an order does not issue it. Orders accumulate in a private queue and are released at
**⌈N/5⌉ per tick**, oldest first. So a single order takes at least two world ticks (~120 ms) to reach the
unit, and if a module queued 40 orders in one sweep the last one lands about eleven ticks (~0.7 s) after it was
decided.

> **OPINION:** this smoothing constant is inherited and it solved an inherited problem — a base-building bot
> dumping a hundred production orders at once, where a fifth of a second of smoothing is free. In WW3MOD the
> bursts are *recruitment sweeps over the contested beachhead reserve*, at exactly the moment two modules are
> competing for it. So the arbitration outcome depends in part on a release schedule that no module can see and
> nothing documents. It is not a bug and I would not rush to change it — but if you ever do change it, expect
> behaviour to move in places you did not touch.

### Step 7 — he walks, and may be turned around

The order finally reaches the unit and it starts moving. From here, three things can interrupt it:

1. **Another module's order.** Damped now, per step 5, but only for four annotated sites.
2. **A layer 2 trait** (§1.2). Undamped, invisible, and two of the five use the *cancelling* form that
   destroys in-flight work rather than appending to it.
3. **Its own module re-deciding** on its next scan, 3–6 seconds later.

### Step 8 — if he is an aircraft, none of the above applies **[OpenRA / WW3MOD split]**

Aircraft are commanded by **squad state machines** instead: a squad is formed, and a small state machine
(idle → attack → flee, or the richer helicopter version) drives it. This is [`05`](05-squads-and-combat-states.md).

The essential facts: exactly **one fixed-wing squad per player for the whole match**, which every aircraft ever
called in joins and never leaves while alive; and helicopter squads, which do rotate properly. The fixed-wing
state machine is essentially untouched OpenRA — it picks targets by sweeping the whole map with no fog check,
flees when "enemy anti-air count × 3 > our aircraft count", and retreats to a random one of your own buildings.
The helicopter machine is wholly WW3MOD, is the most sophisticated bot code in the repo, and is the only state
machine that consumes the belief and danger fields.

**Ground units never enter this system at all** — the squad manager explicitly declines to recruit them
(verified: `IgnoreGroundUnits: true` on all four instances, `ai.yaml:1250, :1341, :1800, :1813`), which is
precisely what leaves the ground pool free for step 4. So this is not two systems fighting; it is one system
that has been hollowed out to aircraft-only, plus a replacement that took over ground.

### Step 9 — he runs out of ammunition or health, and there is no way back

WW3MOD builds no rearm or repair hosts: the helipad and airfield both carry a prerequisite that nothing in the
game ever grants. But the aircraft still *declare* rearm hosts, and the inherited state machines still contain
"go home and rearm" logic. The result is a "return to base" order that is accepted, finds no resupplier, and
quietly turns into "hover here" — never rearming, never repairing.

Three consequences follow, and the third is the one with teeth: several launch gates refuse to commit an
aircraft below a health percentage, and **with no repair anywhere, health only ever decreases**. On the
`@stable` profile, a helicopter that takes one chip of damage parks for the rest of the match.

> **OPINION:** this is the clearest "outdated module" in the whole bot, and it is not fixable with more flags.
> The mod has no repair and no rearm, so health and ammunition are **one-way resources** and the correct
> doctrine is *use it or bank it* — which is exactly what the evacuation work reinvented from scratch. The
> inherited triad of rearm/repair gates should be deleted from the launch path and replaced by one predicate:
> "is this airframe still worth committing?" Leaving three unsatisfiable gates in place and bypassing each with
> its own flag is why that module has more than fifty configuration fields.

### 2.10 The spine as a lookup table

| Step | What happens | Read | Main file |
|---|---|---|---|
| 0 | decide what to call in | [`03` §3-B4](03-module-catalogue.md) | `UnitBuilderBotModule.cs` |
| 1 | spawn at map edge, walk to rally point | [`game-model.md`](../reference/game-model.md) | `BaseBuilderBotModule.cs:208-238` |
| 2 | becomes idle, becomes contested | [`02` §1.1, §4.3](02-lifecycle-and-arbitration.md) | `Actor.cs:75` |
| 3 | the bot forms its picture of the world | [`04`](04-perception-and-fields.md) | `BeliefStore.cs`, `DangerFieldLayer.cs`, `ControlField.cs` |
| 4 | a module picks a destination for him | [`03` §3-D1](03-module-catalogue.md) | `PoiOffensiveBotModule.cs` |
| 5 | the contest between modules is resolved | [`02` §5](02-lifecycle-and-arbitration.md) | `OrderArbitrationMath.cs` |
| 6 | the order queues and drains | [`02` §1.4](02-lifecycle-and-arbitration.md) | `ModularBot.cs:253-263` |
| 7 | he moves, and may be interrupted invisibly | [`02` §3](02-lifecycle-and-arbitration.md) | `StancePositioningExecutor`, `AutoSeekSupplies` |
| 8 | if he flies, a state machine commands him instead | [`05`](05-squads-and-combat-states.md) | `HelicopterStates.cs`, `AirStates.cs` |
| 9 | ammo and health are one-way | [`05` §6.3](05-squads-and-combat-states.md) | `AirStates.cs`, `HelicopterStates.cs` |

---

## 3. What does not run

**Read this before studying any component.** The single most expensive mistake available in this codebase is
spending a week understanding machinery that cannot execute.

### 3.1 Thirteen of the twenty squad states never execute

[`05` §2](05-squads-and-combat-states.md) walks the verification chain for each. Summary:

| Group | States | Status | Why |
|---|---|---|---|
| Fixed-wing air | 3 | **run** | aircraft are bought on both profiles |
| Helicopter | 5 | 4 run, 1 does not | the close-range attack run is bypassed on both shipped profiles |
| Ground | 5 | **dead** | the squad manager refuses to recruit ground units |
| Naval | 4 | **dead** | the naval unit-type list is set nowhere |
| Base protection | 3 | **dead** | the protection type list is set nowhere |

I re-verified the three "set nowhere" claims by grepping the entire `mods/` tree: `ProtectionTypes` and
`NavalUnitsTypes` appear **nowhere at all**, and `ConstructionYardTypes` appears only once, on the base builder.

Dead by consequence: the entire 275-line fuzzy attack-or-flee evaluator, and a guard that correctly protects
committed units from being yanked by a squad — which currently guards only unreachable call sites.

**Everything the mod calls "the squad system" for ground combat cannot execute.** Ground combat is commanded
by `PoiOffensiveBotModule`. Roughly 1,050 lines of state machine plus ~150 lines of manager never run.

### 3.2 Seven entries in the live config file do nothing

These are the most misleading category, because appearing in `ai.yaml` makes a module look alive.
[`03` §2.1](03-module-catalogue.md) traces each to its terminating condition. The headline case:

**`BaseBuilderBotModule`'s entire construction half is inert.** Its `BuildingFractions` names eight structure
types, and I verified that **all eight carry a prerequisite that nothing in the mod ever grants**. Every
fraction is skipped, every cycle, forever. So `BuildingFractions`, `BuildingLimits`, `MinBaseRadius`,
`MaxBaseRadius`, `NewProductionCashThreshold: 5000`, `PlaceDefenseTowardsEnemyChance: 80` and the rest are
about thirty lines that read exactly like a tuning surface and cannot move anything. Its one live behaviour is
setting rally points (step 1 above), which has nothing to do with base building.

The other six: an engineer bridge-repair module that is fully built and correct but targets an actor type with
**zero instances on any of the ten shipped maps**; the helicopter lift lane on `@stable`; the squad manager's
ground and naval branches; and four levers inside a live attack module with about sixty lines of tested config
below them that never execute.

### 3.3 Dead interfaces and unread configuration

- **An entire task-board API with zero callers.** `PostTask`, `ClaimTask`, `GetOpenTasks`, `UpdateTaskStatus`,
  `HasTaskNear` — I grepped the whole engine and confirmed **no caller anywhere outside the file that defines
  them.** It is a half-built second coordination system sitting next to a live one, and its presence invites a
  future author to build on it.
- **Five module classes never instantiated at all** — the inherited capture director, harvester manager, MCV
  manager, support-power timer and minelayer. Verified: only one mention across all of `mods/`, and it is
  inside a comment.
- **Five helicopter role fields are configured per airframe and read by no C# code anywhere.** So tuning "how
  close does the Apache engage" or "does the Hind avoid anti-air" in the mod YAML changes nothing.
- **Four squad-manager knobs are set on all four live instances and read only by unreachable code** — including
  a squad size and an attack scan radius.

> **OPINION:** the cost of all this is not CPU. It is that the config file **lies about what is tunable**,
> which is precisely the mechanism that produces the worst bug class in §5. Someone will tune those knobs, run
> a benchmark, see no change, and conclude something false about the game. If one cleanup were on offer I would
> spend it here rather than on deleting the dead C#.

### 3.4 The honest counts

| | Count |
|---|---|
| Module classes in the engine | **24** (verified by enumeration) |
| Module instances running | **42** (41 in the AI config files + 1 world trait) |
| Classes never instantiated | **5** |
| Instantiated entries that do nothing | **7** |
| Squad states in the tree | **20** |
| Squad states that execute on shipped profiles | **7** (see §7.2 — the set's own headline says 8) |

---

## 4. Provenance: what we inherited, what we built, where the seams are

The user's concern, in their own words: *"we are starting from the OpenRA baseline, which had really bad
bots… there is a real risk here that we are using outdated modules that are not really the best way to achieve
the more advanced goals."* This section answers that as directly as the evidence allows.

Provenance throughout the set is established from **git history**, not from copyright headers — several
WW3MOD-original files carry the inherited OpenRA notice at line 3, so the headers are worthless as evidence.

### 4.1 What is inherited **[OpenRA]**

| Component | State |
|---|---|
| The tick loop, order queue and ⌈N/5⌉ drain | unchanged, and fine — but see step 6 |
| The `--countdown` cadence pattern used by all 24 scheduled modules | unchanged; this is the biggest structural debt in the bot (§4.3) |
| `BaseBuilderBotModule` | ~unmodified, construction half dead |
| `BuildingRepairBotModule` | ~unmodified, scope now tiny |
| `SquadManagerBotModule` | heavily modified, hollowed out to aircraft only |
| `UnitBuilderBotModule` | heavily modified; `@experimental` half is good WW3MOD work on an OpenRA substrate |
| The ground / naval / protection state machines and the fuzzy evaluator | unchanged and unreachable |
| The fixed-wing air state machine | essentially unchanged, and on the live path |
| Five module classes never wired up | unchanged, unused |

### 4.2 What was built here **[WW3MOD]**

The whole strategic layer: the belief store, the danger and control fields, frontier distance, the POI/axis
attack model, the commitment ledger, the capture coordinator, both transport ferries, the defence-line filler,
the ambush layer, the supply logistics module, the helicopter module and its state machine, the order gate,
and the blackboard.

One structural habit in this work deserves calling out as the healthiest thing in the codebase: the decision
mathematics is factored out into **28 engine-free static classes** with unit tests, so it can be reasoned
about and ported without the game. Note the contrast — **not one inherited module has such a partner.**

### 4.3 The seams — where the real problems live

The recurring finding across the whole set is not "inherited component X is bad". It is that **inherited
components are individually reasonable and misfit at the seams**, because OpenRA's bot never had twelve modules
competing for one puddle of units at a fixed beachhead, and never had a second invisible decision layer running
underneath. The seam list:

| Seam | What is on each side | Why it misfits |
|---|---|---|
| **Cadence ↔ scheduling** | inherited per-call countdowns ↔ the single-attention scheduler this project is heading toward | A module's "interval" is measured in *calls*, not ticks. Withhold a module's turn and its interval stretches by that factor — and because the *only* place a claim is refreshed is inside the module's own periodic check, withholding it past the expiry **silently drops its units out of the ledger while it still believes it owns them.** This is the one structural blocker on the attention model, and the fix is mechanical: convert 24 countdowns to tick stamps. [`02` §2.3](02-lifecycle-and-arbitration.md). |
| **Order smoothing ↔ recruitment** | inherited ⌈N/5⌉ drain ↔ WW3MOD recruitment sweeps over the contested beachhead | An inherited smoothing constant silently shapes an arbitration outcome. Step 6. |
| **Danger field ↔ its consumers** | WW3MOD-scale weapon damage ↔ RA-era threshold constants | §5, Pattern 1. Fourteen thresholds that cannot be justified. |
| **Rearm/repair gates ↔ a mod with neither** | inherited "go home and rearm" ↔ no rearm hosts exist | Step 9. Three unsatisfiable gates, each bypassed by its own flag. |
| **Two claim registries** | wave 1 blackboard ↔ wave 2 ledger | §1.1. Neither reads the other. |
| **The activity layer ↔ everything that inspects orders** | five WW3MOD unit traits ↔ the order gate, the logs, any future scheduler | §1.2. Entirely of our own making, not inherited. |
| **Config surface ↔ what is reachable** | inherited base-building and squad knobs ↔ prerequisites and flags that make them inert | §3.2, §3.3. |

> **OPINION — the direct answer to the user's question.** The risk you named is real but it is not evenly
> spread, and it is not mostly about *module quality*. Ranked by how much I would worry:
> 1. **The inherited cadence pattern**, because it blocks the architecture you want next. Highest value, boring
>    to fix, unblocks rather than being the feature.
> 2. **The RA-era constants against rescaled fields**, because they make the strategic layer's decisions
>    partly arbitrary while every knob looks tuned.
> 3. **The inherited air squad layer**, because it is a 2007-era design sitting next to your own far better
>    helicopter module, solving the same problem twice at wildly different maturity.
> 4. **The dead-but-tunable config**, because it manufactures bug class 1 continuously.
>
> Notably *not* on that list: the POI/axis attack layer, the belief store, the control field, the commitment
> ledger and the order gate. Those are your own work, they are the right shape for this game, and the correct
> instinct is to build toward them.

---

## 5. How to spot problems yourself

This is the section to internalise. The codebase produces the same handful of bug shapes over and over. Each
pattern below has a **name**, a **worked example that is verified**, and a **question to ask** that would have
caught it.

### Pattern 1 — a constant that was never rescaled when the game was

**Shape.** Someone chooses a threshold against a quantity. Later, somebody rescales the quantity by two or
three orders of magnitude for unrelated reasons. Nothing connects the two, so nothing complains. The threshold
now means something entirely different from what it says.

**Worked example.** `EvacDangerThreshold = 60` decides when a supply truck should flee danger. It is compared
against a danger field whose **measured median at the moment trucks actually flee is 66,834** — from the user's
own play log. Three separate trucks fled at a reading of 68 while sitting within four cells of their own
beachhead. The threshold sits *inside the ambient noise* of the field, so the "danger response" is permanently
on. And notice the shape: **nothing is wrong in either file.** The constant is plausible, the field is correct,
and the two were simply never introduced to each other after the field was rescaled.

**How widespread.** [`04` §5](04-perception-and-fields.md) audits every configured threshold against the range
of the field it reads: **8 justified, 4 structurally harmless, 14 that cannot be justified.** Every single
justified one on the danger field is a `0` or a `1` — a threshold used as a *yes/no* question, which is
scale-independent and therefore survived the rescaling. Every unjustifiable one is a mid-range number chosen
to mean "a moderate amount of danger". **The field cannot express "a moderate amount."**

**Questions to ask.**
- What is the actual range of the thing this number is compared against? Not the intended range — the range it
  reaches with this mod's data.
- Is this number `0`, `1`, or a ratio? Then it is probably safe. Is it a mid-range constant? Then compute the
  field's smallest meaningful step before trusting it.
- Two thresholds "3× apart" — do they actually select different things, or the same contour?

### Pattern 2 — a formula still reading the input the old game used

**Shape.** The mod makes a new input mandatory and demotes an old one. A consumer written against the old model
keeps reading the old field, which is now usually absent — and silently substitutes a default.

**Worked example, verified directly in the code.** The danger field converts a weapon into a "how much damage
per unit time" number by dividing by `ReloadDelay`, and **never reads `BurstWait` at all** — I confirmed the
file contains zero occurrences of `BurstWait`. But WW3MOD changed the firing model: `BurstWait` is
**mandatory** (the engine throws on a weapon that omits it) and is the real cadence; `ReloadDelay` is now only
an extra pause and is usually absent. I counted the weapon files: **14 `ReloadDelay` declarations against 90
`BurstWait` declarations.** So most weapons take the "absent → substitute 1" path and are modelled as firing
their entire burst damage *every single tick*.

**Why this one is worse than a tuning error.** It is a **ranking inversion**, not a scale offset. The field
believes an anti-tank specialist is roughly 900× more dangerous than a light machine gunner, when in sustained
output they are within a factor of one. So every module that sorts, buckets or compares threat is making
decisions dominated by *which cadence field a weapon's YAML happens to declare*. **The strategic layer is not
reading a threat map; it is reading a map of YAML style.** Filed `[high]`.

**A second, independent defect sits on top of it, and the two do not cancel.** The inflated throughput is then
multiplied by a durability weight that is itself mis-scaled (tuned for 200 hit points, fed 28,000), and for a
main battle tank the product **overflows a 32-bit integer** — wrapping negative and being clamped to the
minimum of 1. So the tank the formula over-ranks by 130× is, as executed, ranked *below a rifleman*. Two
compounding errors in the same expression, pointing in opposite directions, in the number the whole strategic
layer reasons about. [`06` §1 rank 1 and §5.1](06-inherited-misfits.md).

**Questions to ask.** When a formula reads a ruleset field, is that field still the one the mod actually uses,
and what happens when it is absent? A silent default substitution is where this hides. And: does the
intermediate product of this expression fit in its type at *this mod's* magnitudes?

### Pattern 3 — tests that cannot fail

**Shape.** A component is pinned by unit tests, so it reads as verified. But the tests feed it toy-scale inputs
and assert only *orderings*, never magnitudes — so they stay green no matter how far the real scale drifts. The
tests are not wrong. They are simply incapable of detecting this class of defect.

**Worked example — this one is new, found while writing this overview, and it is the mechanism by which
Pattern 1 and Pattern 2 both survived.** The danger kernel has a dedicated test file. Its "representative"
inputs are:

| Test fixture | throughput | health | cost |
|---|---|---|---|
| `Tank` | 400 | 1,000 | 1,500 |
| `Humvee` | 300 | 300 | 600 |
| Real `abrams` in the mod | **2,300,000** | **28,000** | 2,500 |

The test's tank is about **5,750× less lethal** and **28× less durable** than the mod's actual tank — these are
Red Alert magnitudes. And **every assertion on intensity is ordinal or relative**: "the humvee's core is denser
than the sniper's", "the tank outweighs the humvee", "half confidence gives half intensity". Not one test
asserts an absolute value. So the suite cannot notice that the field's real range is orders of magnitude away
from where its consumers threshold.

**And this is not a hypothetical cost — it is exactly how the integer overflow in Pattern 2 survived.** The
expression that overflows is `throughput × durabilityWeight`. Run the test's tank through it and you get
**92,000**. Run the mod's real tank through it and you get **6,785,000,000**, against a 32-bit signed maximum of
**2,147,483,647** — so the real value is 3.2× *over* the limit while the test fixture sits about 23,000× *under*
it. **No test can reach the regime where the function breaks.** The tests are green, the code is wrong, and
neither fact informs the other.

Note also which assertion the wrap would have broken: `tank >= humvee`. The suite does test exactly the
property the defect violates — it simply tests it with numbers at which the defect does not occur.

Worse still: the function that translates the ruleset into the field's units — the one carrying the Pattern 2
cadence bug — is **never called by any test at all.** The tests inject throughput as a hand-written integer,
bypassing it entirely. I verified this: it has exactly two call sites, both inside the layer itself. So the bug
lives in the untested seam between the ruleset and the well-tested pure mathematics.

There is a second, independent instance of this pattern already on the record: a difficulty slider that was
"live, documented, correctly clamped and unit-tested at its endpoints" and still produced **identical values at
three of the five points where it was actually going to be measured**, because the tests pinned 0 and 100 while
the benchmark swept the middle. The rule that came out of it: *pin the value at every planned measurement
point, not at the extremes.*

**Questions to ask.**
- Do this component's tests use *real ruleset magnitudes*, or invented small ones?
- Do they assert any absolute value, or only orderings and ratios?
- Is the function that converts game data into this component's units tested at all?

### Pattern 4 — memory purged by the same event that triggers the thing it prevents

**Shape.** A module remembers "I already sent this unit somewhere" so it will not spam. It correctly clears
that memory when the unit leaves its area of responsibility — otherwise a stale record would suppress a
re-issue that *should* happen. But leaving the area of responsibility is exactly what flickers. **So the
memory that prevents re-issuing is destroyed by the same event that triggers the re-issue.**

**Worked example.** A census counted **28 anti-churn dampers already in the codebase** before the order gate.
The problem was never that nothing commits — it is that 27 of the 28 are private to one module and are
deliberately purged the moment the unit drops out of that module's eligibility list, and *eligibility is what
flickers*. You do not even need two modules fighting: one module with a flickering predicate produces the whole
wobble by itself. **Seven independent reimplementations of the same fix had already failed for this reason. An
eighth would have too.**

This is the diagnosis worth learning as a concept, because it *predicts which fixes will fail*. The order gate
works not because of its dwell window but because **its record is owned by the player and cannot be reached by
any module.** That lifetime property is the whole fix.

**Questions to ask.** Who can delete this memory, and does the thing that deletes it correlate with the thing
it is supposed to suppress? Is the anti-flicker record owned by the flickering party?

### Pattern 5 — configuration that looks tunable and is wired to nothing

**Shape.** A knob exists, has a sensible name, has documentation, is set in the shipped config — and is read by
nothing, or is read only by code that cannot execute. It invites tuning that cannot work, and a benchmark run
against it produces a false conclusion.

**Worked examples**, all verified: thirty lines of base-building configuration behind prerequisites nothing
grants; five helicopter fields configured per airframe and read by no code anywhere; four squad knobs read only
by unreachable states; a helicopter doctrine knob that is the trait's most characteristic setting and whose
consuming state is bypassed on both shipped profiles; and a percentage lever documented as inert at its own
shipped value.

**Question to ask.** Before tuning anything: grep for the field name in the engine. Then check whether its
consumer can actually run. Both steps — a live consumer inside a dead branch is the common case.

### Pattern 6 — counting the wrong clock

**Shape.** A counter is incremented once per *scan* but named, documented and reasoned about as though it
counted *world ticks*. The error factor is the scan interval, which can be 5, 75 or more.

**Worked examples.** A regroup timeout documented in its own comment as "~12.5 seconds" is really **~56
minutes**. A helicopter cooldown documented as "ticks of engagement" is five times its stated duration. And the
whole cadence seam in §4.3 is this pattern at architectural scale.

**Question to ask.** What increments this, and how often is *that* called? A field named `...Ticks` is a claim
that wants checking.

### Pattern 7 — the gate that is false exactly when it matters

**Shape.** An availability check is written as "has the data arrived yet?" rather than "is this player supposed
to have data?". The first is false during warm-up — which is exactly when opening-play bugs live — so the gate
is silently disabled over the window you most care about.

**The rule this yields**, and it generalises well beyond this codebase: *for an availability check, prefer the
predicate that is true from tick 0 over the one that becomes true when the data arrives.*

### 5.8 The general rule underneath all seven

Almost every pattern above is one failure: **a number or a predicate whose meaning is defined somewhere else,
and which nobody re-derives when that somewhere else changes.**

Two operational habits follow, and they are the most transferable things in the whole set:

> **On a believed field with no designed scale, relative comparisons are meaningful and absolute thresholds
> mostly are not.** Prefer (a) comparing two readings of the same field, (b) a ratio against the same cell's
> earlier reading, or (c) a strict yes/no at zero. Reach for a mid-range constant only after computing the
> field's smallest real step in the regime you care about.

> **When a comment asserts an invariant, re-derive it rather than trusting the sentence.** A gate whose meaning
> is defined elsewhere can be widened out from under every comment that describes it — and this has already
> happened here: three flags written before a policy change now reach the benchmark control while their
> in-file comments still claim byte-identity.

---

## 6. Reading guide

**The technical documents are for consulting, not for reading front to back.** Each is 430–790 lines of
dense, cited reference organised for lookup. Reading all four in sequence is not the intended use and would
mostly cost you time. Read this overview end to end; then go to a document with a question.

### If you have one hour

This document, plus [`game-model.md`](../reference/game-model.md) (41 lines) if you have not read it recently.
That is enough to follow any bot conversation and to smell most of the problems.

### If you have a day, in this order

1. **This document**, end to end.
2. **[`04` §0, §3.2, §5, §7](04-perception-and-fields.md)** — the perception scales and the threshold audit.
   This is where the highest density of real, actionable defects sits, and §3.2 is the worked arithmetic behind
   Pattern 1. Skip §8 unless you are about to touch the fields themselves.
3. **[`02` §3, §4, §5.1](02-lifecycle-and-arbitration.md)** — the two order layers, the four ownership
   mechanisms, and the eligibility-coupled-amnesia diagnosis. §5.1 is the single best piece of reasoning in the
   set: it explains a whole family of bugs and predicts which fixes will fail.
4. **[`05` §0, §2.7, §6](05-squads-and-combat-states.md)** — what is dead, and where the inherited design
   fights the mod. §6.3 (rearm and repair) is the one with live behavioural consequences.
5. **[`03` §2, §4](03-module-catalogue.md)** — the inert-but-looks-alive list and the three fitness concerns.
   Treat the rest of `03` as a dictionary: look modules up, do not read it through.
6. **[`06` §1, §2, §3](06-inherited-misfits.md)** — and if you only have the appetite for two documents rather
   than six, make it this one and this overview. §1 is the ranked misfit table, §2 gives each failure pattern a
   **tell** you can apply yourself, §3 argues a fix-first order. §4 is what is genuinely good, which is worth
   reading precisely because the rest of the set is a problem inventory and will otherwise leave you with an
   unfairly bleak picture.

### By question

| Your question | Go to |
|---|---|
| "Why did that unit walk backwards?" | [`02` §5.1](02-lifecycle-and-arbitration.md), then §3 (the invisible layer) |
| "What does this module actually do, and what is its shipped config?" | [`03` §3](03-module-catalogue.md) — per-module entries |
| "Is this number reasonable?" | [`04` §5](04-perception-and-fields.md) — the threshold-versus-scale table |
| "Is this code even reachable?" | [`05` §2.7](05-squads-and-combat-states.md) for states, [`03` §2.1](03-module-catalogue.md) for modules |
| "Can the bot legally know that?" | [`04` §2](04-perception-and-fields.md) for the belief store; [`05` §6.4](05-squads-and-combat-states.md) for where aircraft still cheat |
| "Who decided this unit's destination?" | [`02` §4](02-lifecycle-and-arbitration.md) — four ownership mechanisms |
| "Why do the two bot profiles differ?" | [`03` §2.2, §2.3](03-module-catalogue.md) — the twin diff and the tick-order asymmetry |
| "What should we fix first?" | [`06` §1, §3](06-inherited-misfits.md) — the ranked table and the argued order. Start here. |
| "What should we build next?" | [`06` §3–4](06-inherited-misfits.md), then [`05` §7](05-squads-and-combat-states.md) (keep/replace/delete) and §4.3 above |
| "What is actually good in here?" | [`06` §4](06-inherited-misfits.md) — read it after the problem inventory, not before |
| "Why is the AI allowed to cheat here?" | [`04` §8](04-perception-and-fields.md) — the invariants and why each exists |
| "What is the game model again?" | [`game-model.md`](../reference/game-model.md), [`supply-route.md`](../reference/supply-route.md) |

### Where the raw evidence lives

These documents synthesise primary research that is kept separately, and which is more detailed but less
organised: the order-source census, the order-churn census, the transport census, the unit-purpose census and
the truck-loop live-log diagnosis, all under
[`WORKSPACE/recon/`](../../WORKSPACE/recon/). Defects found and not fixed are in
[`WORKSPACE/bugs/discovered.md`](../../WORKSPACE/bugs/discovered.md). Curated, trusted claims live in
[`DOCS/reference/`](../reference/) — in particular [`influence-stack.md`](../reference/influence-stack.md),
which is the implementation-level companion to `04`.

### A standing caution about line numbers

Every document in this set cites `file:line`, and **this repo drifts fast** — one of them notes that
`ai.yaml` and six modules moved by 3,548 lines between two censuses written days apart. The *findings* survive
that drift; the coordinates do not. Re-grep before acting on any citation more than a few weeks old.

---

## 7. Contradictions and gaps found while writing this

Reporting these rather than smoothing them over, since an overview that quietly averages two disagreeing
sources is worse than either. All three were checked against the code at `dcc2f7c5`.

### 7.1 The two claim registries are *not* honoured by disjoint sets of modules

[`03` §E2](03-module-catalogue.md) states that the blackboard and the ledger are "honoured by disjoint sets of
modules — the modern POI stack uses the ledger; the 2026-03 support modules use the blackboard. Neither reads
the other." [`02` §4.2](02-lifecycle-and-arbitration.md) says something different and more precise: that
`HelicopterSquadBotModule` and `CaptureCoordinatorBotModule` resolve the ledger **unconditionally**, so they
read it on both profiles.

**`02` is right.** `HelicopterSquadBotModule.cs:496` resolves the ledger with no flag and no profile condition,
and its own comment states it is used as an availability gate "for every profile"; `GarrisonBotModule.cs:220`
resolves it behind an opt-in flag. Both are wave-1 modules. `UnitBuilderBotModule.cs:276` resolves it too.

So the sets **overlap in at least two modules**, and the correct statement is narrower: *the POI stack never
reads the blackboard* (verified — zero references across all seven POI-stack files), and *nothing reads both
registries as a single source of truth*. The load-bearing conclusion survives; `03`'s wording overstates it.
I have kept the narrower version in §1.1.

### 7.2 The squad-state count: 8 running, or 7?

[`05` §0](05-squads-and-combat-states.md) headlines "Eight of them run. Twelve are unreachable." But
[`05` §3.2](05-squads-and-combat-states.md), thirty pages later, shows that the helicopter close-range attack
state is entered from exactly one place, inside a branch that only executes when a flag is *off* — and that
flag is **on for both shipped profiles**. Its own table marks that state `LIVE*` with the asterisk explaining
this, and §7.4 lists making it reachable as the first thing to fix.

**Verified:** `StandoffEngagement: true` at `ai.yaml:1419` and `:1446` (both profiles), and the only transition
into that state sits inside `if (!standoff)` at `HelicopterStates.cs:565-573`.

So on shipped content the honest count is **7 of 20 states execute, 13 do not.** `05` is internally
inconsistent — its headline counts a state its own body proves cannot run. The distinction matters because that
state carries the helicopter hit-and-run mechanic, which is the trait's most doctrine-flavoured behaviour. I
have used 7/13 above and noted the discrepancy in §3.4.

### 7.3 The fixed-wing squad cadence: 5 ticks or 75?

[`03` §3-B3](03-module-catalogue.md) lists the squad manager's cadence as "Squad FSM update 5 t".
[`05` §3](05-squads-and-combat-states.md) says air squads tick every 75 ticks (4.5 s).

**`05` is right, and `03` is wrong by 15×.** The squad state machines are updated inside
`if (--attackForceTicks <= 0)` at `SquadManagerBotModule.cs:274-279`, reset from `AttackForceInterval`, whose
default is **75** (`:72`) and which is **not overridden anywhere in `ai.yaml`** — I grepped. The 5-tick figure
belongs to `HelicopterSquadBotModule`'s own `SquadUpdateInterval`, a different module. Anyone reading `03`
would believe the fixed-wing air squad reacts fifteen times faster than it does.

### 7.4 A new defect found while writing this — and how it connects to the overflow

The Pattern 3 example in §5 — the danger kernel's tests pinning at Red Alert magnitudes with only ordinal
assertions, and the ruleset-to-field conversion function having no test coverage at all — is not in any of the
documents that preceded it. It is logged in
[`WORKSPACE/bugs/discovered.md`](../../WORKSPACE/bugs/discovered.md) (2026-08-09, `[med]`) and **not fixed**,
per this task's terms.

It is worth reading together with [`06` §5.1](06-inherited-misfits.md), which landed while this overview was
being written and which found the **integer overflow** in the same expression. The two findings are one story:
`06` establishes that the arithmetic breaks at this mod's magnitudes, and this one establishes *why nobody
noticed* — the pins sit some 23,000× below the value at which it breaks, and they assert only orderings.
Neither document is complete without the other, and together they are the cleanest example in the repo of
Pattern 3.

**A note on my own process, since it cuts both ways.** An earlier draft of this section stated that I had
searched for the overflow the task brief mentioned and could not verify it, and had therefore left it out. That
was wrong — the defect is real, at `DangerFieldLayer.cs:170`, and I have since confirmed it independently:
`throughput × durabilityWeight` for the mod's main battle tank is about 6.8 billion against a signed 32-bit
maximum of about 2.1 billion, it wraps negative, and the `if (intensity < 1)` guard then clamps it to 1. My
search had looked for the word "overflow" in the research log rather than computing the expression, which is
exactly the "trusted the prose instead of re-deriving" failure this document warns about in §5.8. Recorded
rather than quietly deleted, because a reader deciding how much to trust this file should be able to see where
it was wrong.

### 7.5 What I did not verify

- **I did not run the game, build, or run any test.** Documentation task; no simulations by instruction.
- The source documents were each written against a slightly earlier commit (`910507c1` for `02`, `04`,
  `05`; `4d583f2e` for `03`) and this one against `dcc2f7c5`. I re-verified the claims I lean on, **not every
  claim they make**. Where I did not re-open something, I have linked rather than restated it.
- `04` records that a worker was concurrently retuning danger thresholds on a branch called
  `auto/danger-scale`. If a number quoted here does not match the code you are reading, check whether that
  branch merged — but re-derive it either way, because the point of Pattern 1 is that these numbers were never
  derived in the first place.
- The three worst-case claims I am *relying on but did not independently re-measure* are the live-log danger
  median of 66,834, the sustained-output ratios behind Pattern 2 (which depend on a model of the firing cycle),
  and the "28 anti-churn dampers" count. Each is cited in its source document with its own honesty note.

---

## 8. What this document does not cover

By design. It is the map, not the territory.

- **How to change any of this.** No fix recipes here. [`05` §7](05-squads-and-combat-states.md) has a
  keep/replace/delete assessment; [`03` §4](03-module-catalogue.md) has three fitness concerns.
- **The influence stack's implementation.** [`influence-stack.md`](../reference/influence-stack.md), organised
  by build stage. [`04`](04-perception-and-fields.md) is the reader's view of the same material and corrects it
  in one place.
- **Per-module configuration reference.** [`03` §3](03-module-catalogue.md).
- **How to test bot behaviour.** [`AUTOTEST.md`](../recipes/AUTOTEST.md) — and note the project rule that
  multi-test runs need explicit permission each time.
- **Balance and unit tuning.** [`BALANCE.md`](../recipes/BALANCE.md).
- **Why the game works the way it does.** [`game-model.md`](../reference/game-model.md),
  [`supply-route.md`](../reference/supply-route.md). If you read nothing else in `DOCS/reference/`, read those
  two — every misfit in this folder is measured against them.

---

## The set

| Document | Lines | What it is for |
|---|---|---|
| **`README.md`** (this file) | — | The overview and the index. Read end to end, once. |
| [`02-lifecycle-and-arbitration.md`](02-lifecycle-and-arbitration.md) | 724 | The plumbing: world tick → module → order gate → unit. The two order layers, the four ownership mechanisms, the order gate in full. |
| [`03-module-catalogue.md`](03-module-catalogue.md) | 428 | The inventory: 24 module classes, 42 instances, 7 inert, provenance from git. A dictionary — look things up. |
| [`04-perception-and-fields.md`](04-perception-and-fields.md) | 791 | What the bot believes: the belief/danger/control fields, their real scales, and a threshold-by-threshold audit. |
| [`05-squads-and-combat-states.md`](05-squads-and-combat-states.md) | 791 | The inherited squad layer, and the verification that most of it cannot execute. |
| [`06-inherited-misfits.md`](06-inherited-misfits.md) | 644 | The consolidated, **ranked** audit across all of the above plus the bug log, with a fix-first order and a "what is genuinely good" section. **This is the one to read when you want to decide what to do.** |

There is no `01`. This file is it.

**How this file and `06` divide the work.** They were written in parallel and overlap deliberately. Read *this*
one to understand how the bot works — the narrative, the architecture, the vocabulary. Read *`06`* to decide
what to change — it ranks 22 misfits by impact on your goals, estimates each one's cost, and says which are
already in flight. Where they differ on a number, `06` is later and was written against this same commit with
the arithmetic re-derived; §7.4 above is the one place I add to it rather than defer.
