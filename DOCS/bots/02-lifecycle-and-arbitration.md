# 02 — Lifecycle and arbitration: from a world tick to a unit having an order

**Researched against `main` @ `910507c1`** (`git status -sb`: `main...origin/main [ahead 67]`, tree clean apart
from four known untracked scratch paths). Static read only — no build, no game run, no autotest. Every factual
claim below carries a `file:line` that I opened and read at that commit.

> **Reconciled 2026-08-09 against `main @ 25a8aebd`.** A cross-document pass re-derived every headline
> claim, summary count and computed figure in this six-document set from the code, and corrected the
> loser of every contradiction in place. Corrections made here are marked at the point they occur.
> **Danger-field magnitudes are the one excluded class** — they are pending re-derivation on
> `auto/danger-scale` and are flagged wherever they appear; see
> [`04` §3.2](04-perception-and-fields.md).

**What this document is.** The plumbing: how a bot goes from the world advancing one tick to a specific unit
being told to walk somewhere. It covers the tick path, module cadences, the two order layers, the four
overlapping unit-ownership mechanisms, and the order gate merged 2026-08-08. It deliberately does **not**
re-describe the module catalogue, the influence-stack fields, or the squad state machines — those are other
documents in this folder.

**Framing you must not lose.** WW3MOD is a **total conversion**, not a Red Alert mod. There are no factories
and no tech tree: units are called in as reinforcements from off-map reserves through the **Supply Route**, a
fixed, indestructible, non-buildable beachhead, one per player ([`game-model.md`](../reference/game-model.md),
[`supply-route.md`](../reference/supply-route.md)). Almost every arbitration problem in this document is
sharper here than it would be in RA for one structural reason: **every unit in the game is born at the same
few cells**, so every module that recruits "nearby idle units" is recruiting from the same puddle at the same
moment.

---

## How to read this document

Two markers are used throughout and they mean different things.

**Provenance** — where a component came from:

| Marker | Meaning |
|---|---|
| **[OpenRA]** | Inherited from OpenRA `release-20230225` essentially unchanged. Designed for a base-building RTS with harvesters, a tech tree and ~8 bot modules. |
| **[MODIFIED]** | OpenRA structure, but WW3MOD changed its behaviour or added fields to it. |
| **[WW3MOD]** | Written for this mod. No OpenRA ancestor. |

**Opinion** — every paragraph beginning **`OPINION:`** is my assessment, not a description of the code. You can
disagree with those freely; the `file:line` claims are the part that should be checkable and correct.

---

## 1. The tick path

### 1.1 The chain, end to end

```
 Game.InnerLogicTick                         Game.cs:771
   ├─ orderManager.TickImmediate              :795
   ├─ orderManager.TryTick()                  :804   ──► SendOrders / ProcessOrders
   │                                                     (OrderManager.cs:312 / :320)
   │                                                     └─ UnitOrders.ProcessOrder → Actor.ResolveOrder
   └─ world.Tick()                            :808
        ├─ WorldTick++                        World.cs:496
        ├─ foreach actor: a.Tick()            World.cs:499-500   ← ACTIVITIES run here
        │     └─ Actor.Tick                   Actor.cs:285-302
        │          ├─ RunActivity(CurrentActivity)          :288
        │          └─ if idle → INotifyIdle.TickIdle        :300-302
        └─ ApplyToActorsWithTraitTimed<ITick> World.cs:502  ← ModularBot runs here
             └─ ModularBot.Tick               ModularBot.cs:204
                  ├─ foreach module: BotTick   :225-230
                  │     └─ module calls bot.QueueOrder(...)  → gate → private Queue<Order>
                  ├─ gate.Prune                :243
                  └─ drain ⌈N/5⌉ → world.IssueOrder  :253-263
```

Two orderings inside `World.Tick` are worth internalising, because a lot of behaviour falls out of them:

1. **Activities run before traits.** `actors.Values → a.Tick()` (`World.cs:499-500`) advances every unit's
   current activity, and dispatches `INotifyIdle.TickIdle` to any unit that has none (`Actor.cs:300-302`).
   Only *then* does the `ITick` pass reach `ModularBot` (`World.cs:502`). So when a bot module asks
   "is this unit idle?", it is reading a flag that was settled earlier in the same tick by the activity
   layer — see §3.
2. **`IsIdle` is not a status field, it is one pointer test.** `Actor.cs:75`:
   `public bool IsIdle => CurrentActivity == null;`. Nothing more. It is true for a unit that finished its
   errand, a unit that was interrupted, and a unit that has never been given anything — all
   indistinguishable.

### 1.2 Bot activation is gated on the host **[OpenRA]**

`Player.cs:210` sets `IsBot = BotType != null`, then `:224-231`:

```csharp
// Enable the bot logic on the host
if (IsBot && Game.IsHost)
{
    var logic = PlayerActor.TraitsImplementing<IBot>().FirstOrDefault(b => b.Info.Type == BotType);
    ...
    logic.Activate(this);
}
```

`ModularBot.Tick` early-returns unless `IsEnabled` (`ModularBot.cs:206`), and `IsEnabled` is set only inside
`Activate` (`:110`). **Consequence: in a multiplayer game the bot's brain runs on exactly one machine — the
host — and every other client sees only the resulting orders arriving over the wire.** `Activate` also
refuses to run in a replay (`:107-108`), because bot logic is unsynced and replays must reproduce from the
recorded order stream alone.

`Activate` does three things that matter later:

- **`:112` snapshots the module list once**: `tickModules = p.PlayerActor.TraitsImplementing<IBotTick>().ToArray()`.
  That array is fixed for the match, and its **order** is the arbitration mechanism discussed in §4.5.
- **`:115`** resolves the shared commitment ledger (`PoiGoalGuard`), or leaves it null.
- **`:116-117`** constructs the order gate **only if at least one of its two levers is set**, so a mod that
  sets neither gets the pre-2026-08-08 pass-through with no allocation at all.

### 1.3 The module loop **[MODIFIED]**

`ModularBot.cs:225-230`:

```csharp
foreach (var t in tickModules)
    if (t.IsTraitEnabled())
    {
        currentModuleTag = t.GetType().Name;
        t.BotTick(this);
    }
```

Straight OpenRA, plus one WW3MOD addition: `currentModuleTag`. Every order a module queues while it is ticking
is attributed to that module's type name, which is what makes both the lifecycle log (`:152`) and the order
gate's ownership predicate (§5.2) possible without touching 60 call sites. It is cleared in a `finally`
(`:236`) so a module that throws cannot mis-attribute the next module's orders.

Note `if (t.IsTraitEnabled())` — a module whose `RequiresCondition` is unsatisfied is **skipped, not removed**.
That is how the `@experimental` / `@stable` split is implemented: both profiles' modules exist on the player
actor; conditions decide which tick.

### 1.4 The order queue and its drain **[OpenRA]**

`bot.QueueOrder(...)` does **not** issue an order. It appends to a private `Queue<Order>`
(`ModularBot.cs:71`, enqueued at `:153`). The queue is drained at the *end* of `ModularBot.Tick`, at
`:253`:

```csharp
var ordersToIssueThisTick = Math.Min(
    (orders.Count + info.MinOrderQuotientPerTick - 1) / info.MinOrderQuotientPerTick, orders.Count);
```

`MinOrderQuotientPerTick = 5` (`ModularBot.cs:34`, not overridden in `ai.yaml`), so this is **⌈N/5⌉ per tick,
FIFO**. Only then does each survivor reach `world.IssueOrder` (`:263`) — and even that is not execution: it
enters the order manager, is projected through `SendOrders`/`ProcessOrders` (`OrderManager.cs:312`, `:320`)
and only then reaches `Actor.ResolveOrder`.

**Latency, and why it matters.** At `Timestep: 60` (`mod.yaml:369-372`, the `default`/"normal" speed) the game
runs at **16.667 ticks/s**, so one tick is 60 ms. A bot order therefore takes:

| Stage | Cost |
|---|---|
| Sits in `ModularBot`'s queue | ≥ 1 tick, more under a burst (⌈N/5⌉) |
| `IssueOrder` → `ProcessOrders` → `ResolveOrder` | ≥ 1 further tick; `TryTick` only processes on a net frame (`OrderManager.cs:316-321`, `IsNetFrame` `:331`) |
| **Total floor** | **≥ 2 world ticks ≈ 120 ms**, and materially more for the tail of a burst |

The burst case is the interesting one. If a module queues 40 orders in one tick (a large recruitment sweep),
the drain is **8, 7, 5, 4, 4, 3, 2, 2, 1, 1, 1, 1, 1** — **13 passes**, so the last order leaves the queue
**12 ticks (~0.72 s)** after the sweep was decided. (Re-derived at `25a8aebd` against
`ordersToIssueThisTick = min(⌈N/5⌉, N)` at `ModularBot.cs:253`; earlier drafts of this paragraph and of
[`README` step 6](README.md) wrote the sequence as "8, 7, 6, 5, …" and gave ~11 ticks — the ⌈32/5⌉ term is 7
and the ⌈25/5⌉ term is 5, not 6.)

**OPINION.** The ⌈N/5⌉ throttle is an [OpenRA] artefact that solved an OpenRA problem: a base-building bot that
dumps a hundred production and rally orders on one tick, where a fifth of a second of smoothing is free. In
WW3MOD it is doing something subtly different and worse. Our bursts are not production orders — they are
*recruitment sweeps over the Supply Route reserve*, i.e. exactly the contested pool, at exactly the moment two
modules are competing for it. Spreading those over 11 ticks means the arbitration outcome depends on a drain
schedule that no module can see and nothing documents. It is not a bug and I would not rush to change it, but
it is a place where an inherited constant is silently shaping a decision the mod cares about and OpenRA did
not. **If you ever change `MinOrderQuotientPerTick`, expect bot behaviour to move even where you changed
nothing else** — and note the order gate has already perturbed this (§5.6).

---

## 2. Cadences: countdowns, not clocks

### 2.1 What the code actually does **[OpenRA pattern, inherited by every WW3MOD module]**

No bot module in this codebase asks "what tick is it?". They all decrement a counter once per call. The
cleanest example is `BotBlackboard.cs:100-108`:

```csharp
void IBotTick.BotTick(IBot bot)
{
    if (--cleanupCountdown <= 0)
    {
        cleanupCountdown = Info.CleanupInterval;   // 300
        CleanupStaleTasks();
        CleanupDeadUnitClaims();
    }
}
```

Every scheduled module in the tree follows this shape. The 2026-08-07 order-source census counted **24 such
sites** and found only **three** pieces of module state that are genuinely tick-stamped and therefore
skip-safe (`260807-order-source-census.md` §6.3).

### 2.2 Why that distinction is load-bearing

A `--countdown` and a `WorldTick % N` behave identically **only while the module is called every tick**. The
moment anything withholds a `BotTick`, they diverge:

| | Called every tick | Called every 3rd tick |
|---|---|---|
| `if (--countdown <= 0)` with interval 100 | fires every 100 world ticks | fires every **300** world ticks |
| `if (WorldTick % 100 == 0)` | fires every 100 world ticks | fires every 100 world ticks (or is missed) |

So a module's "interval" is measured in **calls**, and its wall-clock period stretches by whatever factor it is
withheld. This is not a hypothetical — a **single-attention scheduler** (the bot only "pays attention" to one
thing at a time, like a human player) is the direction this project is heading, and withholding ticks is
precisely what such a scheduler does.

### 2.3 What breaks when a module is withheld

This is the part worth understanding because it constrains all future work. `ModularBot.cs:215-224` carries
the warning in code:

> **DO NOT GATE THIS LOOP.** Every module's cadence is a per-call `--countdown` decrement (24 sites) and every
> `Ledger.Commit` refresh in every bot module sits behind its module's own countdown, so withholding a
> `BotTick` stretches that module's interval by the withhold factor and — at TTL/interval = 250/100 = 2.5×
> headroom on the POI modules — **silently drops its units out of the ledger while it still lists them in
> `axis.Units`.** An attention scheduler must refuse the RE-DECISION inside the module's own eval (where the
> claim refresh already lives), never the tick.

Unpack that, because it is the single most important scheduling fact in the bot:

1. A module claims a unit by writing a **TTL-bounded** commitment to the shared ledger (§4.2).
2. The **only** place that claim is refreshed is inside the module's own periodic evaluation.
3. Therefore: withhold the module past its TTL, and the claim expires **while the module still believes it
   owns the unit**. The unit becomes free to every other module simultaneously, and the withheld module has no
   idea.

Concretely, on the POI modules: `ReevaluateInterval` is 100 ticks and `AxisCommitmentTicks` is 250, so there is
2.5× headroom. Withhold that module more than 60% of the time and its units start silently leaking. The module
does not fail loudly; it just stops owning things.

Other cadence-coupled state found by the 0807 census (§6.3) and worth knowing about: `HelicopterSquadBotModule`
prunes disposed squad members on its **5-tick** branch and a squad state tick that reaches a disposed member
**throws**; `MountedTransportBotModule` runs a 4-state per-carrier FSM whose arrival and unload-completion are
detected *only on a scan*, so a skipped scan means a carrier that reached its drop-off never unloads;
`PoiOffensiveBotModule` counts its force-preservation budgets in **evals**, not ticks, so variable attention
silently retunes every damper constant it has.

**OPINION — this is the highest-value structural debt in the bot, and it is inherited.** The `--countdown`
pattern is OpenRA's, from a design where nothing ever withheld a module tick, so the distinction could not
matter. It matters now. The fix is mechanical and boring — convert 24 countdowns to tick stamps — and doing it
would turn "you cannot schedule this bot" into "you can schedule this bot". I would put it ahead of most
behavioural work, precisely because it unblocks the attention model rather than being it. The counter-argument
in the code comment is sound as far as it goes ("gating re-opens a 25-site conversion whose coverage is not
statically verifiable"), but note it argues against *gating before converting* — not against converting.

---

## 3. The two order layers

This is the thing that surprises everyone, so state it plainly:

> **Not everything that moves a unit is an `Order`.** A second, entirely separate layer moves units by queueing
> an `Activity` on them directly. It produces no `Order`, appears in no order log, and is invisible to the
> order gate. **Two of its members are default-ON for human-owned units.**

### 3.1 Layer 1 — the order layer **[OpenRA structure, MODIFIED plumbing]**

Every `bot.QueueOrder(...)` in `BotModules/` lands on one method: `ModularBot.cs:127-155`, reached through the
two interface-explicit entry points at `:123-125`. That single funnel is what made the order gate a one-file
change rather than a sixty-site one.

`ModularBot.cs:129-136` states the boundary exactly:

> **HUMANS CANNOT REACH THIS.** `IBot.QueueOrder` is only ever called by bot modules holding an `IBot`; a
> human's orders come from the UI straight to `World.IssueOrder` and never enter this queue. […] What it also
> means: the gate is blind to the SECOND order layer.

### 3.2 Layer 2 — the activity layer **[WW3MOD, almost entirely]**

| Trait | Attached to | Owner scope | Cadence | What it does |
|---|---|---|---|---|
| **`StancePositioningExecutor`** **[WW3MOD]** | `^Combatant` (`defaults.yaml:27`) | `@experimental` bots via `GrantConditionOnBotOwner@tacpos` (`defaults.yaml:37-39`) **and every human-owned combatant** via `GrantConditionOnHumanOwner@tacpos` (`defaults.yaml:44-45`) | `EvaluateCooldown: 30` = 1.8 s | `self.QueueActivity(new Move(self, dest))` (`:414`) — the **single-argument append** form, which cannot cancel. Also writes a `tacpos:` ledger claim (`:643`) it never reads back. |
| **`CohesionSlotMemory`** **[WW3MOD]** | `^Combatant` (`defaults.yaml:20`, deliberately declared *before* the executor) | all | `INotifyIdle.TickIdle` | Returns a unit to its formation slot. |
| **`AutoSeekSupplies`** **[WW3MOD]** | `^Soldier` (`infantry.yaml:167`, trait at `:221-222`, `Enabled: true`) | **every soldier, human- and bot-owned alike** | `ScanInterval: 40` = 2.4 s | Sends an idle low-ammo soldier to a supply truck. Uses the **cancelling** two-argument form. |
| **`DropsSupplyCache`** **[WW3MOD]** | supply trucks | all | `ITick`, **every tick** | Queues `RotateToEdge` — drive to the map edge and sell — when the truck reads empty. |
| **`GarrisonManager`** **[WW3MOD]** | garrisonable structures | all | `ITick` FSM | Deploys/recalls garrisoned infantry by direct trait manipulation; issues nothing at all. |

The YAML comment on `AutoSeekSupplies` says the quiet part out loud (`infantry.yaml:217-218`):

> *"This is a TRAIT, not a bot module, so the one switch covers every soldier — human- and bot-owned alike;
> there is no owner-side split."*

### 3.3 The one distinction that decides whether layer 2 can hurt you

`Actor.cs:381-387` — the two-argument form:

```csharp
public void QueueActivity(bool queued, Activity nextActivity)
{
    if (!queued)
        CancelActivity();
    QueueActivity(nextActivity);
}
```

`CancelActivity()` nulls the whole queued chain, not just the head. So:

- **Single-argument `QueueActivity(activity)`** = append. Cannot cancel anything. On an idle unit it fills the
  idle. `StancePositioningExecutor:414` is this form — which is why the 0808 churn census *demoted* it from
  the suspect list.
- **Two-argument `QueueActivity(false, activity)`** = cancel-and-replace. `AutoSeekSupplies:112` and
  `DropsSupplyCache:317` are this form. These genuinely destroy in-flight work.

**OPINION — layer 2 is the most under-appreciated hazard in the bot, and it is entirely of our own making.**
[OpenRA] had a couple of autonomous unit traits (`Wanders`, `ScaredyCat`) and they were confined to civilians.
WW3MOD added five, put three of them on every combat unit or every soldier, and made two default-ON for
humans. Each one is defensible in isolation — a soldier who walks to resupply is *good*. The problem is
architectural: **there is now no single place that can answer "what is this unit doing and who decided that",
because half the deciders never produce an artefact.** The order gate shipped yesterday cannot see any of them.
Neither can the lifecycle log. Neither can any future attention scheduler placed at the order layer. Two
second-order effects follow directly:

1. **Layer 2 defeats every `IsIdle` filter in layer 1.** A unit that `StancePositioningExecutor` just nudged
   two cells is non-idle, so it is invisible to the ~57 `IsIdle`-gated recruitment filters — for that scan
   only. Next scan it is idle and gets grabbed. That is a flicker source manufactured by our own trait.
2. **The truck loop the user has been chasing for weeks lives here.** `DropsSupplyCache` re-checks its
   condition **every tick** and queues a cancelling `RotateToEdge` within one tick of it reading true, while
   re-adoption by `SupplyFollowerBotModule` is on a 150-tick scan. Departure at 0.42 s granularity against
   recovery at 9 s. No order-layer instrument can see either end of it.

If one architectural change were on offer, I would spend it on giving layer 2 a shared, inspectable seam —
not on making it smaller.

---

## 4. Who owns a unit

There are now **four** overlapping mechanisms that answer some version of "is this unit taken?". None of them
subsumes the others; they have different lifetimes and different honour lists.

### 4.1 `BotBlackboard.ClaimUnit` — a plain mutex **[OpenRA-shaped, WW3MOD file]**

`BotBlackboard.cs:196-211`. A `Dictionary<uint, string>` from ActorID to claimant string (`:84`). `ClaimUnit`
returns false if someone else holds it; `ReleaseUnit` (`:214-218`) removes it.

- **Lifetime: unbounded.** There is no TTL. A claim is released explicitly, or when the unit dies and
  `CleanupDeadUnitClaims` (`:122-132`) sweeps it on the 300-tick `CleanupInterval` countdown (`:70`, `:100-108`).
- **Honoured by:** `GarrisonBotModule`, `ScoutBotModule`, `SupplyFollowerBotModule` (readers and writers), and
  `HelicopterSquadBotModule` — **which writes but never reads** (0807 census §4.1), so it will happily take a
  unit another blackboard module already claimed.
- **Not honoured by:** the entire POI stack. Zero references.

The same file also carries a **task board** API — `PostTask` (`:137`), `ClaimTask` (`:145`), `GetOpenTasks`
(`:170`), `UpdateTaskStatus` (`:160`), `HasTaskNear` (`:184`). I grepped the whole engine: **zero callers
outside `BotBlackboard.cs` itself.** It is dead code and has been for as long as it has existed.

### 4.2 `GoalGuardLedger` commitments — the real cross-module registry **[WW3MOD]**

`PoiGoalGuard.cs:39-117`, a generic, engine-free ledger instantiated as `GoalGuardLedger<Actor>` (`:321`),
one per player, gated `enable-ai-experimental || enable-ai-stable` (`ai.yaml:100-101`), TTL
`DefaultCommitmentTicks: 600` (`ai.yaml:106`; C# default 300 at `:314`).

It stores, per unit: an **objective string** and an **expiry tick** (`:41-51`). Objectives are namespaced
prefixes so claims stay attributable — `offense:`, `bombard:`, `defend:`, `defend-line:<x>,<y>`, `garrison:`,
`ambush:`, `transport:`, `capture:`, `capture-escort:`, `capture-defend:`, `bridge-repair:`, `bridge-screen:`,
`tacpos:`.

Four properties you need to hold in your head:

1. **`IsCommitted` checks expiry directly** (`:81-82`), so a stale claim can never lock a unit even if nothing
   pruned it. `Prune` (`:104-116`) is hygiene, not correctness.
2. **`TryGetObjective` does *not* check expiry** (`:84-94`). Two callers rely on that deliberately; mixing the
   two up is an easy bug.
3. **`Commit` with a *different* objective silently destroys the previous claim** (`:68-76`) — new entry,
   `CommitCount` reset to 1. There is **no priority model in the ledger itself**. Last writer wins.
4. **`Release` is keyed on the actor, not on the objective** (`:100`). So a trait that releases "its" claim
   actually deletes whichever claim the actor happens to be holding at that moment.

**The asymmetry rule, which is the trap.** *A shared ledger only arbitrates between modules that **both write
it**.* Reading it is not participation. Several modules resolve their `goalGuard` field **only when an opt-in
flag is set** — `LayeredDefenceBotModule.cs:215`, `MountedTransportBotModule.cs:322`, `GarrisonBotModule.cs:220`
— and those flags are absent from the `@stable` twins. On `@stable` those modules hold a null ledger and their
`IsCommitted` reads are inert. Meanwhile `HelicopterSquadBotModule.cs:496` and
`CaptureCoordinatorBotModule.cs:518` resolve it **unconditionally**, so they read on both profiles.
`StancePositioningExecutor` writes (`:643`) and never reads.

**OPINION.** The ledger is the right idea and the correct shape for *ownership*. Its two design faults are that
it has no priority (fault 3) and that `Release` is under-keyed (fault 4) — and the second one is nastier than
it looks, because it means a per-unit trait with a short-TTL ambient claim can delete a mission-critical claim
made by a module, with no diagnostic. The `@stable`/`@experimental` flag split around ledger resolution is, I
think, a mistake that predates the current CLAUDE.md policy: it produces a benchmark control whose *arbitration
semantics* differ from the experiment's, which makes A/B results harder to attribute, not easier. That policy
has since been retired for exactly this class of gate; these flags are leftovers.

### 4.3 Per-module eligibility sets — the implicit ownership

Every module rebuilds, on its own cadence, a set of units it considers "available to me". These are not
registries and nothing publishes them, but they behave as ownership because a unit outside a module's
eligibility set will never be ordered by it.

The important structural fact is that **eligibility is computed from things that oscillate**: believed-danger
field reads, POI visibility under fog, residue verdicts, ledger TTLs, and `IsIdle`. Each of those has its own,
faster clock. This is the mechanism behind the whole churn family and it has a name (§5.1).

The 0807 census maps the actual overlaps (§2.1); the headline is that **infantry standing near the Supply Route
is the single most contested class in the game**, satisfying the line pool, both transports' reserve bubbles,
the capture escort radius, the engineer screen, the ambush pool, and — because `GarrisonActorTypes` is unset —
an unbounded `PassengerInfo` sweep. And since the SR is where *all* reinforcements arrive, every unit is in
that state for its first several seconds of life.

### 4.4 The order gate's standing records — new, and deliberately different **[WW3MOD]**

`BotOrderGate` (`OrderArbitrationMath.cs:374-667`) keeps `Dictionary<uint, Standing>` (`:397`) mapping ActorID
to `{ DestinationKey, Tick }`. Full detail in §5; the point *here* is the lifetime, stated at `:368-372`:

> a record is born when an order is admitted and can only die from (i) the dwell elapsing, (ii) the unit no
> longer executing anything, (iii) an explicit `Stop`, or (iv) an age-based prune. **Nothing a module does — no
> eligibility set, no roster rebuild, no TTL expiry, no pool exit — can reach it.**

That is the one property none of §4.1–§4.3 has.

### 4.5 The four side by side

| Mechanism | Stores | Lifetime | Who honours it | Can another module destroy it? |
|---|---|---|---|---|
| `BotBlackboard.ClaimUnit` | actor → claimant string | **unbounded**; explicit release or death sweep (300 t) | 4 modules, one of them write-only | Only by explicit `ReleaseUnit` |
| `GoalGuardLedger` | actor → objective + expiry tick | **TTL**, 250–600 t, refreshed only inside the owner's own eval | 11 modules, several read-only or write-only | **Yes** — any `Commit` with a different objective (`:68-76`); any `Release` (`:100`) |
| Per-module eligibility | nothing (recomputed) | **one scan** | the module itself | n/a — it is rebuilt from flickering inputs every scan |
| Order-gate standing record | actor → destination + tick | dwell window (120 t), or until idle / `Stop` / prune | the funnel, for all modules | **No.** Player-owned, module-unreachable |

### 4.6 The thing nobody wrote down

**Until 2026-08-08, when two modules wanted the same unit, the winner was decided by which module is declared
later in `ai.yaml`.** Not by priority, not by urgency, not by anything semantic. And that was documented
nowhere in code.

The chain, which I verified at both critical links:

1. `ModularBot.Activate:112` snapshots `TraitsImplementing<IBotTick>().ToArray()` once, and `:225` iterates
   that array in order every tick.
2. `TraitDictionary.TraitContainer<T>.Add` (`TraitDictionary.cs:150-155`) inserts each trait at
   `BinarySearchMany(actor.ActorID + 1)` — the end of that actor's run — so a given actor's traits are stored
   in **construction order**.
3. Construction order comes from `ActorInfo.TraitsInConstructOrder()` (`ActorInfo.cs:104-142`), a topological
   sort seeded from the `TypeDictionary` in **YAML declaration order**. Bot modules declare no `Requires<>` on
   each other, so they all resolve in the first pass (`:117`) — i.e. purely in source order.
4. Orders drain FIFO (`ModularBot.cs:255-263`), so the later-ticking module's order arrives at
   `Actor.ResolveOrder` last, and a non-queued order cancels whatever was running.

And the loser is told nothing. `IBot.QueueOrder` returned `void` before the gate; a dropped order was
indistinguishable from a delivered one; and modules re-issue on their own countdowns rather than in response
to loss. That is the thrash loop.

**OPINION.** This is the clearest example in the codebase of the exact failure mode the brief describes:
[OpenRA]'s bot had a handful of modules with near-disjoint jobs (build, harvest, squad, support power) and
never needed arbitration, so the engine never grew any. WW3MOD has twelve order-issuing modules with heavily
overlapping unit pools and inherited the "no arbitration" default without anyone deciding to. The result was
not a wrong policy — it was *no policy*, with a YAML text file accidentally serving as the priority list. The
order gate is the first thing in the bot's history that makes the winner a stated rule rather than an emergent
one, and that is why it matters more than the churn it damps.

---

## 5. The order gate (merged 2026-08-08)

Files: `engine/OpenRA.Mods.Common/Traits/BotModules/OrderArbitrationMath.cs` (pure predicates +
`BotOrderGate`), the `BotOrderDamping` enum and the `IBot` contract in
`engine/OpenRA.Game/Traits/TraitsInterfaces.cs:437-464`, and the wiring in `ModularBot.cs:116-202`. Tests:
`OpenRA.Test/OpenRA.Mods.Common/OrderArbitrationMathTest.cs` and `BotOrderGateCallerTest.cs`. **[WW3MOD]**

### 5.1 Why it exists — "eligibility-coupled amnesia"

This diagnosis is worth learning as a concept, because it explains a whole family of bugs and it predicts which
fixes will fail.

The 0808 churn census counted **28 distinct anti-churn dampers** already in the codebase. The problem was never
that nothing commits. It is that:

1. Every damper is **private to the module that wrote it** (27 of the 28; the ledger is the sole exception).
2. Every damper is **deliberately purged the moment the unit leaves that module's eligibility set** — and the
   comments doing the purging give a *correct* local reason: a record that outlived its errand would suppress
   the re-issue that should restart it.
3. But **eligibility is exactly what flickers** (§4.3).

So: *the dedup memory is destroyed by the same event that triggers the re-issue.* You do not need two modules
fighting. One module with a flickering eligibility predicate produces the full wiggle by itself.

The prediction that follows — and this is why it is a stronger claim than "nothing commits" — is that **the
churn survives any number of additional per-module dedups**, because each new one inherits the same
purge-on-exit lifetime. Seven independent reimplementations of destination-dedup had already failed for this
reason; an eighth would have too.

The census also ruled out the intuitive fix. A shared *"don't re-issue if the destination is the same"* gate
would not have touched the top suspects, because **in every one of them the destinations genuinely differ**
(forward line cell → carrier actor → a different forward cell). The churn is **decision instability, not
duplicate orders**. So the gate inverts the predicate.

### 5.2 Predicate (a) — ownership / incumbency

`OrderArbitrationMath.OwnershipBlocks` (`:313-331`). Rule: **the module that already holds the unit's
commitment keeps it, unless the challenger outranks it.** Incumbent wins ties — deliberately, because the
status quo tie-break was `ai.yaml` line order.

Three ranks (`:169-181`):

| Rank | Value | Meaning | Prefixes |
|---|---|---|---|
| `RankAmbient` | 0 | Idle-filling / cosmetic positioning. Loses to everything real. | `tacpos:` |
| `RankTasking` | 1 | Ordinary combat tasking. | `offense:`, `bombard:`, `defend:`, `defend-line:`, `garrison:`, `ambush:`, `transport:` |
| `RankMission` | 2 | Scarce units, expensive to restart. | `capture:`, `capture-escort:`, `capture-defend:`, `bridge-repair:`, `bridge-screen:` |

**The rank ladder is a correctness requirement, not decoration** (`:86-92`). `StancePositioningExecutor` stamps
a `tacpos:` claim on every `@experimental` bot-owned combatant it positions and never reads the ledger back.
Without a rank, that claim would be "foreign" to every bot module, and a naive incumbent-wins rule would
suppress **every order to every positioned unit** — the bot would stop playing. Ambient claims must lose to
real tasking.

`RankMission` above `RankTasking` encodes the doctrine point from
[`game-model.md`](../reference/game-model.md): capture **consumes** the technician, so technician availability
is the binding constraint on the whole capture game. A capture may recruit an offense unit; nothing may poach a
capture escort.

**Everything fails open** (`:94-99`): an unknown objective prefix, an unattributed order, an unknown order
string, a missing ledger, *and an unrecognised issuing module* all admit. The last one is aimed at the future:
a module added later that nobody remembered to add to the table would otherwise be unable to task any committed
unit at all, with no signal to its author. **Table rot must degrade to "no suppression", never to "this module
silently cannot give orders."**

**Honest reach.** The file header (`:36-53`) audits this itself and corrects an earlier overstatement: predicate
(a) only adds anything where the challenger does not *already* consult the ledger when building its pool. That
is three modules (LayeredDefence, Garrison, MountedTransport), and **only on `@stable`**, since on
`@experimental` every flag is on. Two others (HelicopterSquad, CaptureCoordinator) already skipped committed
units on both profiles. **Predicate (a) is not what damps the user's churn.**

### 5.3 Predicate (b) — the re-order dwell

`OrderArbitrationMath.DwellBlocks` (`:340-347`). Rule: **suppress a redirect of a unit whose standing order is
young AND still running AND aimed somewhere else.** Every clause is load-bearing:

- **same destination admits** — this is not an equivalence dedup;
- **an idle unit admits** — a finished or interrupted errand must never hold a unit;
- **`dwellTicks <= 0` admits** — the inert default.

Applied at `Admit:561-572`, **single-target only**. Grouped orders are excluded deliberately: every module that
issues one already carries a working same-destination dedup on its aggregate anchor, and the grabs that
actually turn a unit around mid-walk name one actor. (Note `PoiOffensiveBotModule:2299-2301` passes
`groupedActors: new[] { u }` — a group of one — so it *does* reach the dwell.)

One subtlety worth knowing: `ActivationGraceTicks = 5` (`:414`, applied at `:564`, `:579-580`). Because a queued
order waits at least one tick and longer under a burst, a unit reads **idle** for the first few ticks after its
standing record was written — which would hand a competing grab a free pass in exactly the window the fastest
churn sources live in. A just-ordered unit is therefore treated as busy for 5 ticks.

**Destination comparison** is `DestinationKey` (`:352-361`): actor targets and cell targets are placed in
disjoint numeric ranges, so "board carrier 7" never compares equal to "walk to (7,0)".

### 5.4 What is suppressible — opt-in, and why that direction

`BotOrderDamping` (`TraitsInterfaces.cs:437-450`) has two values. `Protected = 0` is the default; the gate never
drops it. `Recurring = 1` is opt-in and asserts two things that must both be true:

> 1. the issuing module re-offers this order on its own cadence, so a drop costs a delay and never the errand,
>    and
> 2. the call site checks `QueueOrder`'s return value before advancing any memory, booking, ledger claim or
>    state transition.

The comment records why the polarity was inverted (`:431-436`): the first cut made every movement order
suppressible unless marked as an emergency, and **two review rounds found six places where nobody had** — a
flee, a withdrawal, a predictive disengage, a capture-party extraction. Forgetting an annotation cost
**safety**. Inverted, forgetting one costs only **damping**.

`IBot.QueueOrder` now returns `bool` (`TraitsInterfaces.cs:460-461`) so a caller can tell. `BotOrderGateCallerTest`
enforces assertion (2) over the real sources.

There is also a **whitelist** on order strings, `Classify` (`:248-262`): only `Move`, `AttackMove`,
`EnterTransport` and `DropSupplyCacheAt` are `Tasking`; `Stop` is `Cancel` (always admitted, and it *clears* the
standing record); **everything else is `Passthrough`** and is untouchable by the gate. A new order type therefore
defaults to un-suppressible.

**As shipped, exactly four call sites are `Recurring`** — verified by grep at `910507c1`:

| Site | Cadence | Why it is here |
|---|---|---|
| `MountedTransportBotModule.cs:641` `EnterTransport` | 50 t (3.0 s) | Census §2 rank 1: no dedup on the passenger, and `IsIdle` **deliberately not required**, so it turns a unit LayeredDefence just sent forward straight back around. |
| `LayeredDefenceBotModule.cs:511` `AttackMove` | 75 t (4.5 s) | The other half of the §4.1 beat. |
| `LayeredDefenceBotModule.cs:650` `AttackMove` | 75 t | Same beat, man-the-line path. |
| `PoiOffensiveBotModule.cs:2299-2301` `AttackMove` | 100 t (6.0 s) | Census §4.2: the axis ↔ staging rearward-lurch cycle. |

The first three are the pair the churn census matched to the user's report **verbatim** — periods 50 and 75 with
independent phases give inter-order gaps cycling 3.0 s, 1.5 s, 1.5 s, 3.0 s: *"forward, back, forward again to a
different spot."* It needs no rare condition, only infantry near the Supply Route — which is every reinforcement
for its first ten seconds — and it was live on **both** profiles.

**Recording is not opt-in.** Every non-queued tasking order establishes a standing record (`Admit:527-532`), even
a `Protected` one. So an unmarked flee still **protects** its unit from the next `Recurring` challenger. The
inversion narrows what can be *dropped* without narrowing what can be *defended*.

Note also what is **deliberately excluded**: `SupplyFollowerBotModule`'s two follow `Move`s, which the census
called the most undamped site it found. `SupplyFollowerBotModule.cs:699-707` argues, correctly, that the gate
*provably cannot* suppress a truck order — trucks are single-owner and never ledger-committed so (a) has no
incumbent, and the module's 150-tick scan strictly exceeds the 120-tick dwell so (b) can never fire. That
oscillation is damped in the module by a distance deadband instead.

### 5.5 The sequence binding

`Admit:493-502`. A queued order *appends* and cancels nothing, so it is not itself a churn source — but it is
not independent of what came before it. A two-leg maneuver issues the danger-avoiding waypoint non-queued and
then chains the direct leg queued. **Admitting the tail after dropping the head leaves the direct leg to execute
alone — driving exactly the straight line the detour existed to avoid.** So: same tick, same actor, head
suppressed ⇒ drop the tail.

Two details that took iterations to get right and are worth respecting:

- **Classify runs first** (`:453-455`). An earlier cut tested `queued` before the whitelist, so a queued
  *passthrough* order could be dropped by this branch — and it really happened: CaptureCoordinator's on-foot
  fallback `CaptureActor` (queued, deliberately outside the whitelist) was dropped because the ferry attempt's
  `EnterTransport` for the same capturer had been suppressed the same tick. **An order the gate does not own
  must be unreachable from every path in it, not merely from the suppression predicates.**
- **Same-tick scope is inferred, not declared** (`:485-488`). An explicit atomicity marker would have to be
  remembered by every future author of a multi-leg pair — the same failure mode that produced the defect. A tick
  boundary cannot be forgotten and needs no lifetime management.

The guarantee, stated exactly (`:62-67`): **a non-queued `Protected` order is never dropped.** A *queued*
`Tasking` order marked `Protected` **is** dropped when the head it continues was suppressed in the same tick —
and that is the one case where dropping a `Protected` order is the safe choice.

The header at `:466-483` also records a **real, unenforced dependency**: two queued tasking sites
(`McvManagerBotModule:163`, `MountedTransportBotModule:428`) are not continuations of anything the gate saw, and
are safe only because their actors are disjoint from every `Recurring` site's actor set. Nothing enforces that
disjointness. If a future `Recurring` mark ever reaches an MCV or a troop carrier, re-check them.

### 5.6 What is shipped

`ai.yaml:44-53` — **both profiles**:

```
ModularBot@experimental:      ModularBot@stable:
    RespectCommitmentsOnIssue: true       (predicate a)
    ReorderDwellTicks: 120                (predicate b, = 7.2 s)
```

`ReorderDwellTicks: 120` is chosen (comment at `ai.yaml:38-43`) to **strictly exceed** the longest re-decision
period the census measured (100 t), so every identified beat is damped rather than half-damped, while staying
**well under** the shortest commitment TTL (250 t) so a dwell can never outlive the commitment it protects.

Turning it on for `@stable` is deliberate under settled CLAUDE.md policy, and the consequence is called out
honestly in two places (`ai.yaml:33-36`, `ModularBot.cs:247-252`): **suppressed orders change `orders.Count`,
which changes the ⌈N/5⌉ drain schedule, for every module including ones the gate never inspects.** FIFO order
among survivors is preserved and nothing is lost, so the effect is bounded latency rather than reordering — but
the ai-bench baseline must be re-taken knowingly. This is not byte-identical and was never claimed to be.

Instrumentation: `OrderGateLogIntervalTicks: 500` (`ModularBot.cs:56`) emits one `ordgate` line per
(issuing module, reason) per window into the unit-lifecycle log (`ReportGateSuppressions`, `:270-282`;
`UnitLifecycleLogger.LogOrderGate:396`). It costs nothing unless `Test.Mode=true Test.UnitLifecycleLog=<path>`
is set.

### 5.7 What the gate does not fix

Be precise about this, because it is easy to over-credit:

1. **It cannot see layer 2** (§3). `ModularBot.cs:132-136` says so explicitly. The supply-truck map-edge lurch,
   `AutoSeekSupplies`, `CohesionSlotMemory` — all still ungoverned in either direction.
2. **Predicate (a) inherits the ledger's amnesia** (`:21-27`). It reads `GoalGuardLedger`, which is still
   eligibility-coupled: `Commit` with a different objective silently overwrites the incumbent, and `Release` is
   keyed on the actor rather than the objective. It fails open, so this costs damping and not correctness — but
   **the cure is (b), not (a)**, and the file says so itself.
3. **It damps churn; it does not schedule attention.** A unit still gets a fresh order every few seconds. The
   human-attention model under consideration would leave a group untouched for tens of seconds. That gap is
   about an order of magnitude and the gate does not close it.
4. **Only four call sites are suppressible.** Everything else is recorded-and-admitted.

**OPINION — the gate is the right first move and the right size, and its most valuable property is the one
that is easiest to miss.** It is not the dwell window; it is that **the standing record is owned by the player
and cannot be reached by any module.** That single lifetime property is what breaks eligibility-coupled amnesia,
and it is why an eighth per-module dedup would have failed where this succeeds. The rank table is where I would
expect trouble to appear first: it is a hand-maintained mapping from objective prefixes to module names that is
**not** re-read from the modules that emit those prefixes, so it can silently drift. The fail-open design means
drift costs damping rather than correctness, which is the right call — but it also means drift is **silent**,
and nothing today would tell you it had happened. A lint check that reconciles the table against the modules'
own `*ObjectiveKey` helpers would close that, and it is cheap.

---

## 6. Misfits flagged while writing this

The brief asked for `EvacDangerThreshold`-class problems: constants or designs that survived the conversion
from Red Alert into a game whose numbers or goals are different, where nothing flags the mismatch.

### 6.1 The template case, still live

`EvacDangerThreshold = 60` (`SupplyFollowerBotModule.cs:91`, shipped `ai.yaml:830`) is compared against a
believed-danger field that reads a **median of 66,834** at the moment trucks enter evac
(`WORKSPACE/recon/260809-truck-loop-from-live-log.md` §6). The field's magnitude comes from WW3MOD weapon damage
values of 10³–10⁵ against RA's ~50. A threshold of 60 sits *inside the ambient
flicker* of that field at a player's own beachhead, so trucks evacuate from home on roughly every other scan.
**Still open at `25a8aebd`.** Already recorded; not re-filed.

> ⚠️ **The 66,834 is a *measured* log value and stands; the "rescale of roughly 200×" was an *estimate* and is
> superseded.** Every derived danger-field magnitude in this document set is **pending re-derivation** on
> `auto/danger-scale`, which fixes `WeaponThroughput`'s arithmetic — see the standing warning at
> [`04` §3.2](04-perception-and-fields.md). The *shape* of the finding (a mid-range constant against a field
> orders of magnitude larger) does not depend on the exact factor.

### 6.2 New observations from this pass

None of these are being fixed here — this was a documentation task.

| # | Observation | Provenance | Why I think it is a misfit |
|---|---|---|---|
| 1 | `MinOrderQuotientPerTick = 5` spreads a 40-order recruitment sweep over 13 drain passes (~12 ticks) | **[OpenRA]** | Sized for production/rally bursts in a base-building game. In WW3MOD the bursts are recruitment sweeps over the contested SR reserve, so an inherited smoothing constant is shaping arbitration outcomes. §1.4. |
| 2 | 24 module cadences are per-call `--countdown` decrements, not tick stamps | **[OpenRA pattern]** | Correct when nothing ever withholds a tick, which was true in OpenRA. It is the single structural blocker on the attention model this project is heading toward, and withholding silently leaks ledger claims. §2.3. |
| 3 | `BotBlackboard`'s entire task-board API is dead code | **[WW3MOD]** | `PostTask` / `ClaimTask` / `GetOpenTasks` / `UpdateTaskStatus` / `HasTaskNear` have **zero callers** anywhere in `engine/` outside `BotBlackboard.cs`. It is a half-built second coordination system sitting next to a live one, and its presence invites a future author to build on it. |
| 4 | `GoalGuardLedger.Release` is keyed on the actor, not on the objective (`PoiGoalGuard.cs:100`) | **[WW3MOD]** | A per-unit trait holding a short-TTL ambient `tacpos:` claim can delete a `capture-escort:` claim by releasing "its own". Silent, and rank cannot help because rank is checked at the gate, not at `Release`. |
| 5 | Ledger *participation* is gated by `@experimental`-only flags on three modules | **[WW3MOD]** | `LayeredDefenceBotModule.cs:215`, `MountedTransportBotModule.cs:322`, `GarrisonBotModule.cs:220` resolve `goalGuard` only under flags absent from their `@stable` twins, so the benchmark control has **different arbitration semantics** from the experiment. That makes A/B attribution harder, and the byte-identity policy those flags were built to serve has since been retired. |
| 6 | Two activity-layer traits are default-ON for **humans** and no bot-side mechanism can see or damp them | **[WW3MOD]** | `StancePositioningExecutor` via `GrantConditionOnHumanOwner@tacpos` (`defaults.yaml:44-45`) and `AutoSeekSupplies` on `^Soldier` (`infantry.yaml:221-222`). Not wrong as features; the misfit is that they create a second decision layer with no shared seam. §3.3. |
| 7 | The gate's rank table is hand-maintained and can drift silently from the modules that emit its prefixes | **[WW3MOD]** | `OrderArbitrationMath.cs:206-226`, acknowledged in its own comment (`:199-205`). Fails open, so drift costs damping not correctness — but nothing reports it. A `make test` lint would close it. |

**Provenance summary for this area.** The tick loop, the order queue and its drain, the `--countdown` cadence
pattern, and `Player`'s host-gated activation are all **[OpenRA]**, essentially unchanged. `currentModuleTag`,
the lifecycle logging, the commitment ledger, the blackboard, the activity-layer traits and the entire order
gate are **[WW3MOD]**. The pattern in the table above is consistent: *the inherited pieces are fine in
isolation and misfit at the seams*, because OpenRA's bot never had twelve modules competing for one puddle of
units at a fixed beachhead, and never had a second, invisible decision layer running underneath.

---

## What to read next

- **The module catalogue** — what each of the twelve order-issuing modules actually decides, and its shipped
  configuration. This document covers only *how* their orders reach a unit.
- **The influence stack** — [`DOCS/reference/influence-stack.md`](../reference/influence-stack.md). The belief,
  danger and territory fields that most eligibility predicates read, and the invariants (zero RNG,
  byte-identity) any scheduler must preserve.
- **The squad state machines** — the air and helicopter FSMs, which tick at 5–75 ticks and issue orders through
  the same funnel but are *not* in the `Recurring` set.
- **The primary sources behind this document**, if you want the raw inventories rather than the synthesis:
  [`WORKSPACE/recon/260807-order-source-census.md`](../../WORKSPACE/recon/260807-order-source-census.md) (every
  order source, every cadence, every claim registry) and
  [`WORKSPACE/recon/260808-order-churn-census.md`](../../WORKSPACE/recon/260808-order-churn-census.md) (the
  eligibility-coupled-amnesia diagnosis and the full 28-damper appendix).
- **The non-negotiable framing**, if you have not read it recently:
  [`DOCS/reference/game-model.md`](../reference/game-model.md) and
  [`DOCS/reference/supply-route.md`](../reference/supply-route.md).
