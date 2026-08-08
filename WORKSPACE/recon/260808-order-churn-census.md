# Order-churn census — why every unit wiggles every few seconds

**Researched against `main` @ `09877fd5`** (`git status -sb`: `main...origin/main [ahead 53]`, 0 behind
upstream; tree clean apart from four known untracked paths). Static analysis only — **no game run, no
autotest, no batch, no benchmark, no build.** No code or YAML modified. Every claim carries a `file:line`
that I opened and read.

**Builds on** `260807-order-source-census.md` (the order-source map: two order layers, per-call countdowns,
YAML-declaration-order conflict resolution) and `260807-supply-truck-oscillation.md` /
`260808-truck-post-fix-behaviour.md`. Facts established there are cited, not re-derived.

**Timestep correction, load-bearing for everything below.** `mods/ww3mod/mod.yaml:347` `DefaultSpeed:
default`, `:371` `Timestep: 60` ⇒ **16.667 ticks/s**, `seconds = ticks × 0.06`. The 0807 census has this
right. **`260808-truck-post-fix-behaviour.md` §1.2 uses "25 tps" and every wall-clock figure in that
document is therefore ~1.5× too fast** — its truck cycle is ~45 s, not ~30 s. Flagged, not fixed (read-only
task).

---

## 0. Verdict on the thesis

**The thesis is confirmed in outcome and REFUTED in mechanism — and the refutation is the useful part.**

The brief's thesis: *"each decision issues a fresh order that overrides the last… nothing commits."*

What is actually true:

1. **The override mechanism is confirmed, exactly as stated** (§1). A non-queued order hard-cancels the
   running activity and discards the whole queued chain.
2. **But "nothing commits" is wrong. Commitment is everywhere** — I counted 28 distinct damping mechanisms
   (§5). The problem is not absence of commitment. It is that **every one of them is private to the module
   that wrote it, and is deliberately garbage-collected the moment the unit leaves that module's
   eligibility set.**
3. **And eligibility is precisely what flickers.** Eligibility is computed from believed-danger fields, POI
   visibility, residue verdicts, ledger TTLs and `IsIdle` — all of which oscillate on their own faster
   clocks. So **the dedup memory is destroyed by the same event that triggers the re-issue.** Two modules
   are not needed. One module with a flickering eligibility predicate produces the full wiggle by itself.

That is the single mechanism. I will call it **eligibility-coupled amnesia**. It is a strictly stronger and
more specific claim than "no commitment", and it predicts something the brief's version does not: the
wiggle should survive any number of additional per-module dedups, because each new dedup inherits the same
purge-on-exit lifetime. Which is what has been observed.

**The second-order finding that matters for what to build:** because the churn is decision instability, not
duplicate orders, **a destination-equivalence gate at a shared choke point would NOT fix the top suspects.**
The destinations genuinely differ. See §7 — this is the part of the answer the brief will not like.

---

## 1. The override mechanism — verified

`Actor.QueueActivity(bool queued, Activity next)` (`engine/OpenRA.Game/Actor.cs:381-387`):

```csharp
public void QueueActivity(bool queued, Activity nextActivity)
{
    if (!queued)
        CancelActivity();
    QueueActivity(nextActivity);
}
```

`CancelActivity()` → `CurrentActivity?.Cancel(this)` (`Actor.cs:399-402`). Per the 0807 census (§3.1),
`Activity.Cancel` nulls `NextActivity`, so **the entire queued chain is discarded, not just the head.**
Movement survives only to the next cell boundary (`Mobile.cs:705` sets `IsInterruptible = false`
mid-traversal).

Bot orders reach this through the single funnel `IBot.QueueOrder` (`ModularBot.cs:91-98`) → private
`Queue<Order>` → drained at ⌈N/5⌉ per tick (`:127`) → `world.IssueOrder` (`:137`). The 0807 census
established (§3.2) that this is **not** a same-tick race: ≥2 world ticks of latency, and the winner is
whichever order arrives at `ResolveOrder` last, i.e. FIFO of module tick order, i.e. **YAML declaration
order in `ai.yaml`**.

**One module avoids it, and it is instructive that it had to be told to.** The drop-and-leave path
(`SupplyFollowerBotModule.cs:816-826`) returns `true` — taking the branch while issuing nothing — with the
comment:

> *"Already on our way to this exact cell: issue NOTHING, but still take the branch — returning false here
> would drop through to the follow path, whose non-queued Move would cancel the very errand this record
> exists to protect."*

That is the disease described in one sentence, by the author of a local patch for it.

---

## 2. Movement-order paths, ranked by expected visible churn

Cadences are `--countdown` per *call* (0807 census §6.3). Wall-clock at 16.667 tps. "Dedup" means: does it
check for an equivalent standing order before issuing?

### Tier 1 — fires on an ordinary unit in an ordinary match, every few seconds

| Rank | Path | Cadence | Dedup | Claims unit? | Why it churns |
|---|---|---|---|---|---|
| **1** | `MountedTransportBotModule` passenger `EnterTransport` — `:621` (and `:268`) | **50 t = 3.0 s** (`ai.yaml:1090`, `:1120`) | **none on the passenger** | ledger only when `CommitPassengers` set — **absent on `@poi`/`@stable`** (0807 §4.1) | `IsIdle` **deliberately not required** (`:531-534`). Targets infantry within 14 cells of the SR — i.e. every arriving reinforcement. |
| **2** | `LayeredDefenceBotModule` `AttackMove` — `:504`, `:639` | **75 t = 4.5 s** (`ai.yaml:1006`, `:1744`) | per-unit `AssignCooldownTicks: 250` | ledger writes gated on flags **absent on `@stable`** (0807 §4.1) | Named in MountedTransport's own comment as the module that "grabs fresh production and orders it forward". |
| **3** | `PoiOffensiveBotModule` axis `AttackMove` ↔ `StageFreePool` `AttackMove` (`:2292`) | **100 t = 6.0 s** (`ai.yaml:245`) | both dedup'd; **`stagedCells` purged on pool exit** (`:2214-2222`) | axis units are ledger-committed; staged units are **not** (`:2202`) | The `targets.Count == 0` reversal. §4.2. `@experimental` only (`ForwardStagingEnabled` at `ai.yaml:546`, no `@stable` twin). |
| **4** | `SupplyFollowerBotModule` follow `Move` — **`:681` and `:714`** | **150 t = 9.0 s** (`ai.yaml:778`) | **NONE — zero dedup, zero hysteresis** | blackboard `supply-follow` | Destination is a **moving cluster centroid** (`:539`). §3. |
| **5** | `DropsSupplyCache.ITick` → `RotateToEdge` (`:317`) | **every tick = 0.06 s** | once-per-frame latch only (`:295-299`) | none — activity layer, invisible to every order audit | Gated on `IsIdle && CountsAsEmpty`; `CountsAsEmpty` is re-decided **every 7 ticks**. §3. |

### Tier 2 — real but bounded, or gated behind a rarer condition

- `PoiGarrisonBotModule:479` — 100 t, own `HasOrdered`/`OrderedCell` dedup (`:475`), draws from the *same*
  free pool as offense (0807 §2.1).
- `LaneAmbushBotModule:439` — 100 t, own dedup (`:435`), **no `IsIdle` filter**, but capped at
  `MaxAmbushes × UnitsPerAmbush = 4` units.
- `CaptureCoordinatorBotModule` escorts `:1309` / defenders `:1416` — 75 t / 150 t, **any armed idle unit
  within 40 cells** of the capturer, no type whitelist.
- `CaptureCoordinatorBotModule:1133` capturer reserve muster — 75 t, dedup'd by `reserveCells` +
  `ReserveHysteresisCells` 3. TECN-only; its one real competitor (GarrisonBotModule) was gated this week.
- `EngineerRouteOpenBotModule:373` screen — 100 t, `IsIdle`-gated, capped at 3, `@experimental` only.
- `HelicopterSquadBotModule` lift passengers `:1075` — 100 t, **read-only ledger participant on `@stable`**
  (0807 §4.2), so its passengers are poachable by every unconditional writer.
- `HelicopterStates` / `AirStates` — squad FSM at **5 t = 0.3 s** for helis. Air units only; the entire
  ground squad layer is dead code (0807 §1.5).
- `ScoutBotModule:128` — 200 t, disjoint unit types, `IsIdle`-only. The cleanest design in the census.
- `GarrisonBotModule:319` `EnterTransport` / `:471` `Unload` — 200 t; the `Unload` re-issue is uncapped by
  design until the building is observed empty.

### Tier 3 — the activity layer, invisible to any order-level audit

Per 0807 §1.4. Two corrections to how these should be ranked:

- **`StancePositioningExecutor` is NOT an overwrite source, and should be demoted.** Its `Evaluate` runs
  from `INotifyIdle.TickIdle` (`:277`), not from `ITick` (`:233`, which only maintains leash/anchor state),
  and it issues `self.QueueActivity(new Move(self, dest))` (`:414`) — the **single-argument append form**,
  which cannot cancel. On an idle unit `CurrentActivity == null`, so it fills idle rather than overriding.
  Its dedup at `:405` is a 1-cell tolerance ball, and the comment there records that exact-equality was a
  live re-order loop (`3aca99a1`). Its residual harm is second-order: it makes units non-idle, defeating
  every other module's `IsIdle` filter, and it writes a `tacpos:` ledger claim it never reads (0807 §4.2).
- **`AutoSeekSupplies:112`** — 40 t = 2.4 s, `QueueActivity(false, …)` (cancelling), on **every soldier,
  human- and bot-owned alike**. This one *is* an overwrite source and it is not bot-gated.

---

## 3. Supply trucks, end to end — the user's worst case

Trucks are **cleanly single-owner at the module layer**: `truk` is excluded by every other module and named
only by `SupplyFollowerBotModule` (`SupplyTruckTypes: truk`, `ai.yaml:777`), and `TRUK` does not inherit
`^Combatant` (0807 §2.2). **So the truck's confusion is not inter-module conflict.** It is five branches of
one module plus one per-tick trait, arbitrated by nothing.

### 3.1 The competing sources for one truck

| Source | `file:line` | Cadence | Non-queued? |
|---|---|---|---|
| follow `Move` (flag off) | `SupplyFollowerBotModule.cs:681` | 150 t | yes, **no dedup** |
| follow `Move` (no detour needed) | `:714` | 150 t | yes, **no dedup** |
| Stage-E detour pair | `:705`+`:706` | 150 t | first yes; `lastVia` deadband at `:703` |
| drop dispatch `DropSupplyCacheAt` | `:835` | 150 t | yes; `dropTarget` dedup at `:818` + `DropAnchorHysteresisCells: 3` (`ai.yaml:869`) |
| drop revoke `Stop` | `:793` | 150 t | yes |
| idle-truck hunt `Move` | `:1186` | 150 t | yes |
| evac retreat `Move` | `:1498` | 150 t | yes; `EvacDwellScans` |
| **`DropsSupplyCache.ITick` → `RotateToEdge`** | `DropsSupplyCache.cs:317` | **every tick** | yes — and it is an **activity**, not an order |

### 3.2 The fast oscillator that no order-layer audit can see

This is the chain I most want on the record, because it is the fastest re-decision loop touching any unit in
the codebase and it is invisible to every census that greps for `QueueOrder`:

1. **`SupplyProvider.ScanInterval = 7`** — a C# default, unset on TRUK (`vehicles.yaml:542-550` sets
   `TotalSupply`, `RearmDelay`, `RestockThreshold`, `EvacuateOnUnusableResidue`, **not** `ScanInterval`).
   So `UpdateTarget()` runs **every 7 ticks = 0.42 s** (`SupplyProvider.cs:256-259`), and is additionally
   forced to the *next* tick after every resupply pip (`:275` `scanTicks = 0`).
2. `UpdateTarget` re-decides the `residueUnusable` latch on every call (`:292-294`). `ResidueVerdict`
   (`:944-957`) returns `true`/`false`/`null` — **it flips both ways**; only "no demand at all" leaves it
   unchanged.
3. `CountsAsEmpty => currentSupply <= 0 || residueUnusable` (`:153`).
4. `IsLowOnSupply(a)` returns `sp.CurrentSupply < RestockThreshold || sp.CountsAsEmpty`
   (`SupplyFollowerBotModule.cs:1561-1568`).
5. On the next 150-tick scan the truck fails the eligibility filter (`:473-479`), so it is **dropped from
   the roster, its blackboard claim released** (`:427-435`) **and its `dropTarget` dedup record purged**
   (`:485-490`).
6. The truck is now owned by nobody and goes idle. `DropsSupplyCache.ITick` (`:281-291`) fires **within one
   tick** and, under TRUK's `InitialResupplyBehaviorAI: Evacuate` (`vehicles.yaml:516`), queues
   `RotateToEdge` — **drive to the map edge and sell**.
7. A soldier walking into range flips the verdict back to usable. On a later scan the truck is re-adopted
   and a fresh non-queued `Move`/`DropSupplyCacheAt` **cancels the `RotateToEdge`**. The truck turns around.

**Asymmetry is the whole problem:** departure is detected at **0.42 s** granularity and executed in **1
tick**; recovery is detected at **9 s** granularity. Anything that flickers the residue verdict produces an
immediate rearward lurch and a delayed, differently-aimed return.

**And note step 5 is deliberate.** The purge comment (`:481-484`) reads: *"a record that outlived its errand
would suppress the re-issue that should restart it."* Correct in isolation, and it is exactly why the dedup
cannot survive the flicker it most needs to survive.

### 3.3 The other truck source is the single most undamped site in the census

`SupplyFollowerBotModule.cs:681` and `:714` issue a **non-queued `Move` every 150 ticks with no dedup and no
hysteresis**, to `FindSafeFollowPosition(bestCluster)` — a cell derived from a **moving cluster centroid**
(`:539`). Every 9 s the truck is stopped dead, re-pathed, and sent to a different cell. The comment at
`:678-680` justifies this as *"unchanged base behaviour (byte-identical)"*.

**That justification is now against policy.** `CLAUDE.md` (settled 2026-08-07, commit `875c93c1`) states
`@stable` inherits improvements and byte-identity is not a reason to withhold a fix. The `else` branch got
`lastVia` and the drop path got `DropAnchorHysteresisCells: 3`, with a comment (`ai.yaml:866-868`) saying
*"a destination that moves is the entire defect this mode exists to remove"* — while the plain follow path
two branches above keeps the moving destination for byte-identity. **Same defect, same module, opposite
treatment.**

---

## 4. Interference — which pairs actually beat, and at what period

### 4.1 The pair that matches the user's report, and is documented in code

**`MountedTransportBotModule` (50 t) × `LayeredDefenceBotModule` (75 t)** on infantry within 14 cells of
the Supply Route.

This is not inferred. `MountedTransportBotModule.cs:531-535`:

> *"We DELIBERATELY do not require IsIdle. **LayeredDefence often grabs fresh production and orders it
> forward before we get a tick**; if we waited for idle we'd never see them. EnterTransport with
> queued=false cancels the existing AttackMove, so **a passenger walking forward 2 cells turns around** to
> board the carrier — that's the desired flow."*

And `:613-616`, on why it needs a hand-rolled cross-module reservation against the heli twin:

> *"The commitment ledger does **NOT** cover this on `@stable`: neither module sets its commit flag there,
> so both leave goalGuard null and never touch it."*

**Beat.** Periods 50 and 75, independent random phases (`MountedTransportBotModule.cs:302`,
`LayeredDefenceBotModule.cs:208`), `lcm = 150 t = 9.0 s`. Per 9 s window: MountedTransport at 0/50/100,
LayeredDefence at 0/75. Inter-order gaps cycle **50, 25, 25, 50 ticks = 3.0 s, 1.5 s, 1.5 s, 3.0 s.**

That is **"every few seconds… forward, back, forward again to a different spot"**, with three distinct
destinations (forward line cell → carrier actor → a re-picked forward cell). It requires no rare condition:
only infantry near the SR, which — since the SR is where *all* reinforcements arrive (`game-model.md`) —
is unconditionally true for every unit's first ~10 seconds of life. **It is live on `@stable` and
`@experimental` alike.**

### 4.2 The other candidate, `@experimental` only

**`PoiOffensiveBotModule` axis ↔ `StageFreePool`**, both at `ReevaluateInterval: 100` — *the same eval*,
mediated by `targets.Count == 0` (`:1261`). Verified chain:

- `BuildFreePool` (`:1932-1943`) excludes `claimedByAxis` (`:1939`) and ledger-committed units.
- `StageFreePool` purges `stagedCells` for anything **no longer in that pool** (`:2214-2222`).
- On `targets.Count == 0`: `RetireAllAxes("no-targets")` (`:1263`), then — **newly reachable as of
  `09877fd5`** — `StageFreePool(bot, tick)` (`:1287`).
- The unit is back in the pool with **no `stagedCells` entry**, so the dedup at `:2289` cannot fire, and it
  gets an unconditional rearward `AttackMove` (`:2292`).
- Next eval, targets refill, the axis re-forms, and it is ordered forward again — to a *re-scored* target.

Period **100 t = 6.0 s**, amplitude = the full standoff (`StagingStandoffCells: 6` coarse cells,
`ai.yaml:547`). `4776e713`'s own commit message names this as candidate (C) for the live wiggle report and
states the posture budget is **inert** against it.

**I cannot rank 4.1 vs 4.2 statically**, because 4.2's frequency depends on how often the believed-POI set
empties — which is a fog/belief-decay question no static read answers. 4.1 needs no such condition, which is
why I rank it first. §8 says how to settle it in one run.

### 4.3 Pairs that look dangerous and are not

- **`StancePositioningExecutor` (30 t) × anything.** Demoted — append-only and idle-gated (§2 Tier 3).
- **Squad layer × ground modules.** Masked: squads are air-only, ground modules exclude `Aircraft`
  (0807 §2.4). Not solved — `StateBase.cs:170` filters on `tacpos:` prefixes only — but not live.
- **`PoiOffensive` × `PoiGarrison`** (both 100 t, same free pool). Both are unconditional ledger writers,
  so the first to tick claims and the other's `BuildFreePool` skips it. **The ledger genuinely works here.**
  This is the case worth holding up as the counterexample: where both parties read *and* write, churn stops.

---

## 5. What damps churn today, and the one thing every damper is missing

**28 mechanisms found. Full table with `file:line`, form and shipped value is the Appendix.**
Summarised by form:

| Form | Count | Examples |
|---|---|---|
| (a) destination-equivalence dedup | 7 | `stagedCells` (`PoiOffensive:2289`), `dropTarget` (`SupplyFollower:818`), `lastFiresAnchor` (`:3163`), `lastBombardAnchor` (`:3474`), `reserveCells` (`CaptureCoordinator:1122`), `CohesionSlotMemory:207`, `StancePositioningExecutor:405` |
| (b) spatial hysteresis | 8 | `RepathThresholdCells: 3` ×3 modules; `StagingHysteresisCells: 3` (`ai.yaml:550`); `DropAnchorHysteresisCells: 3` (`:869`); `ReserveHysteresisCells` 3; `lastVia` (`:703`); `HeliPathHysteresis` |
| (c) time dwell / cooldown | 7 | `MinGarrisonDwellTicks: 750` (`ai.yaml:767`); `AssignCooldownTicks: 250` (`:1006`); `EvacDwellScans`; `EvaluateCooldown` 30; `ReturnCooldownTicks` 25; `StickyTargetTicks` 50 |
| (d) claim/commitment ledger | 1 | `PoiGoalGuard.GoalGuardLedger` — **the only genuinely cross-module mechanism** |
| (e) eval-count budget | 3 | `SectorPostureHoldMaxEvals: 3` (`ai.yaml:619`); `RetreatReadvanceDwellEvals: 3`; `MaxAdvanceHoldEvals` |
| (f) `IsIdle` busy-check | ~57 sites | not a lock; modules re-fire on timers regardless (0807 §3.4) |
| (g) one-shot latch | 2 | `settleFacingDone`; `lastEvacuateTick` (`DropsSupplyCache:295-299`) |

### 5.1 Are these N patches on ONE disease? — Yes, and here is the shared mechanism they each approximate

**Every mechanism in classes (a), (b), (c), (e) and (g) is per-module and independently reimplemented.**

- **Three modules each define their own private `HasOrdered`/`OrderedCell` pair** with copy-pasted
  `LengthSquared >= RepathThresholdCells²` predicates, and each declares its own `RepathThresholdCells`
  Info field — all coincidentally 3: `PoiOffensiveBotModule.cs:3031`, `PoiGarrisonBotModule.cs:475`,
  `LaneAmbushBotModule.cs:435`.
- **Five more keep their own private `Dictionary<Actor, CPos>` last-destination maps** with independently
  written equality tests: `stagedCells`, `lastFiresAnchor`, `lastBombardAnchor`, `dropTarget`, `lastVia`.
- Two per-unit traits add two more variants (tolerance-ball at `StancePositioningExecutor:405`,
  exact-equality at `CohesionSlotMemory:207`).
- The only shared *math* is `ForwardStagingMath.AnchorShifted`, reused by four anchor resolvers
  (`PoiOffensive:1977`, `:2128`; `CaptureCoordinator:1200`; `SupplyFollower:967`). That is a shared
  **primitive**, not a shared decision — each caller supplies its own threshold field and its own
  `lastXAnchor` state, and it damps the module's aggregate *anchor*, never the per-unit order.

**The one cross-module mechanism, `GoalGuardLedger` (`PoiGoalGuard.cs:39-117`), is the wrong shape.** It
answers *"may I claim this unit?"* — TTL-bounded **ownership**. It carries **no destination**, so it cannot
answer *"is the order I am about to send different from the one already in flight?"* Its own header
(`PoiGoalGuard.cs:5-17`) says it exists to defeat `IsIdle` flicker, which is a different failure.

**So the shared mechanism they are each independently approximating is: a per-unit record of the unit's
currently-standing destination, with a lifetime that is independent of any module's eligibility set.**

Every existing approximation gets the first half and fails the second. And the failure is not an oversight
— it is *chosen*, twice, in comments, for a correct local reason:

- `SupplyFollowerBotModule.cs:481-484` — purge `dropTarget` on ineligibility, else a stale record suppresses
  a needed re-issue.
- `PoiOffensiveBotModule.cs:2213` — purge `stagedCells` on pool exit, *"so a re-recruited/dead unit drops
  out"*.

Both are right that a stale record must not suppress forever. Both reach for **eligibility** as the
lifetime, when the correct lifetime is **"is that order still running?"** — and eligibility is the one
signal guaranteed to be uncorrelated with it.

---

## 6. Did today's five merges make it worse? — One did, clearly

| Merge | Verdict |
|---|---|
| **`09877fd5` auto/unit-purpose** | **Made it worse.** §4.2: `StageFreePool` is newly reachable in the `targets.Count == 0` state (`PoiOffensiveBotModule.cs:1287`). Because `stagedCells` is purged for any unit that joined an axis (`:2214-2222`), every transition of the believed-POI set through empty now produces an unconditional rearward `AttackMove` on the whole uncommitted ground line, followed by a forward one next eval. Before this merge that branch issued nothing and units kept their in-flight orders. `@experimental` only (`ForwardStagingEnabled`, `ai.yaml:546`). **Compounded** by the garrison gate in the same merge (`RequireBelievedThreat`/`MinBelievedDanger`, `ai.yaml:757-767`): units that used to vanish into a house now stay idle and orderable, i.e. eligible for every free-pool consumer. That part also changes `@stable`. |
| `dd3430a8` auto/tank-trap | Neutral. Inside the movement activity only (`Move.cs:261`, `:296`; `Mobile.cs:433-437`), scoped to actors with `BlocksDiagonalSqueeze` = TANKTRAP/TANKTRAP2. Adds a path recompute to the *same* destination. No new order source. |
| `8d0ff18b` auto/supply-drop | **Reduced churn, on trucks.** The drop errand *displaces* the undamped per-scan follow orders, and is properly dedup'd (`:818`) and hysteresis-damped (`ai.yaml:869`). New `Stop` at `:793` is a fresh cancel source but bounded by the same record. Did not touch §3.2 or §3.3. |
| `9ab1b2e2` auto/evac-polish | Mixed, small. Reduced for ordinary units (`IsEvacuating` at `PoiOffensiveBotModule.cs:2533` is ungated and removes evacuating units from the offense pool). **Worse for ejected crew specifically:** five modules still recruit crew by role and none consult `IsEvacuating`, so they cancel the evac and `SweepEjectedCrew` re-issues `RotateToEdge` next eval — a genuine 100-tick ping-pong, already logged in `WORKSPACE/bugs/discovered.md` by that merge. |
| `bd3abacf` auto/posture-veto | Neutral to slightly better. No new order writer; `SectorPostureHoldMaxEvals: 3` bounds a previously non-terminating hold. Its own commit message (`4776e713`) states the budget is **inert** against the axis↔staging churn of §4.2. |

**Net: one merge made the most visible defect measurably worse, and it is the newest one on `main`.**

---

## 7. The smallest global intervention — and the honest answer

### 7.1 A choke point exists

`IBot.QueueOrder` (`ModularBot.cs:91-98`) is a genuine single funnel: **every** `bot.QueueOrder` call in
`BotModules/` lands there, ~60 sites across 12 modules. It holds the `OrderString`, the `Subject` /
`GroupedActors`, and the `Target`, and it can query `actor.IsIdle` and `actor.CurrentActivity`. State placed
there is owned by the **player**, not by any module — which is exactly the lifetime property §5.1 says every
existing damper lacks.

### 7.2 But a destination-equivalence gate there would NOT fix the top suspects

This is the finding that matters, and it cuts against the brief's framing.

A shared *"this unit already has an equivalent order, don't re-issue"* gate suppresses orders whose
destination **matches** the standing one. Both top suspects are cases where the destination **genuinely
differs**:

- §4.1: forward line cell → carrier actor → different forward cell. All different. Gate passes all three.
- §4.2: axis target cell → rear staging cell → re-scored axis target. All different. Gate passes all three.
- §3.3: follow cell recomputed from a moving centroid. Different every scan by construction.

**The churn is decision instability, not duplicate orders.** Equivalence dedup is the wrong instrument, and
that is precisely why seven independent reimplementations of it have not fixed the symptom.

### 7.3 What would work at that choke point: a per-unit re-order dwell

Invert the predicate. Instead of *"drop if the destination is the SAME"*, apply *"drop if the destination is
**DIFFERENT** and the standing order is younger than N ticks"* — a per-unit minimum commitment window,
enforced centrally.

Sketch, all inside `ModularBot.cs`:

```
Dictionary<uint /*ActorID*/, (string OrderString, CPos Cell, int Tick)> standing;

in QueueOrder, for a movement order naming exactly one actor A:
    if (standing has A
        && world.WorldTick - standing[A].Tick < ReorderDwellTicks
        && standing[A].Cell != newCell
        && !A.IsIdle
        && !IsUrgent(order.OrderString))
            drop the order;            // A keeps walking where it was sent
    else
            record and pass through;
```

**Honest cost, itemised:**

- **Size:** ~60–80 lines in one file, plus a prune on actor death. No new trait, no YAML schema beyond one
  interval. Genuinely small.
- **Coverage:** the single-actor case covers `StageFreePool` (`:2292` — `groupedActors` of size 1),
  `LayeredDefence` (per-unit), `MountedTransport` (`Subject`), `SupplyFollower` (`Subject`),
  `EngineerRouteOpen` screen, `ScoutBotModule`, and all squad states. It misses the grouped multi-actor axis
  orders — which are the ones that already have a working dedup. Good split.
- **Design decision it forces, and this is the real cost:** `IsUrgent` requires classifying order strings by
  priority. Suppressing an evac `Move`, a `Stop`, or a retreat for up to N ticks is a genuine regression
  risk. That classification **is the first component of the scheduler.** There is no version of this that
  avoids authoring it.
- **Not byte-identical for `@stable`.** Dropping orders changes `orders.Count`, which changes
  `⌈N/5⌉` (`:127`), which changes *which* orders drain on *which* tick — for every module, including ones
  the gate never touches. Per `CLAUDE.md` this is permissible as deliberate visible improvement, but it
  **must** be called out in the commit message so the benchmark baseline is re-taken knowingly.
- **Blind spot, and it is the user's worst case.** The gate sits at the **order** layer. It cannot see the
  activity layer: `DropsSupplyCache.cs:317` `RotateToEdge` (§3.2 — the truck's map-edge lurch),
  `AutoSeekSupplies:112`, `CohesionSlotMemory:227`, `HelicopterSquadBotModule:1722`. **So one gate does not
  fix supply trucks.**

### 7.4 Trucks need a separate, smaller, and lower-risk change

Because the truck's fast oscillator is an activity, not an order, and lives behind one latch:

**Add hysteresis to the `residueUnusable` latch** at `SupplyProvider.cs:292-294` — require N consecutive
`true` verdicts before latching, rather than flipping on a single 7-tick sample. ~5 lines, one file, one
decision point, and it is the *only* thing gating a per-tick `RotateToEdge`. It does not require touching
`SupplyFollowerBotModule` at all.

**Separately**, §3.3: give the plain follow `Move` (`:681`, `:714`) the same `DropAnchorHysteresisCells`-style
deadband the drop path already has. The only argument against it is a byte-identity commitment that
`875c93c1` retired.

Neither is a per-module patch in the sense the brief rejects — they are two shared choke points (one latch
that gates a per-tick evac; one destination that moves by construction), not N band-aids.

---

## 8. What needs a live run — and the instrumentation already exists

**The ranking of §4.1 vs §4.2 can be settled empirically without writing a line of code.**
`ModularBot.QueueOrder` already calls `lifecycleLogger?.LogOrder(player, currentModuleTag, order)`
(`ModularBot.cs:96`), and `currentModuleTag` is set to the ticking module's type name (`:114`).
`UnitLifecycleLogger.LogOrder` (`World/UnitLifecycleLogger.cs:343`) self-gates on
`TestMode.IsActive && TestMode.UnitLifecycleLogPath` (`:144-148`), i.e. `Test.Mode=true
Test.UnitLifecycleLog=<path>`.

**One ordinary autotest run with that flag produces a per-unit, per-module, per-tick order log.** Group by
ActorID and the answer to "which module is actually re-ordering this unit, how often, and to where" falls
out directly. That is the cheapest possible next step and it is a single run — no code change, no batch.

Two caveats on that log: it records **issuance, never overwrite** (0807 §3.1), so the *loser* of each
conflict must be inferred from adjacency; and it is blind to the entire activity layer, so the truck chain
of §3.2 will not appear in it at all. The supply path remains instrumentation-free
(`260808-truck-post-fix-behaviour.md` §4).

**Also not established here:**

- **How often `targets.Count == 0` actually fires** on `@experimental` in a real match. This is the single
  number that decides whether §4.2 is the top suspect or an occasional one.
- **Whether `residueUnusable` actually flickers in play**, or settles. The verdict is bidirectional and
  re-sampled at 0.42 s, so flicker is *possible*; frequency depends on aura membership, which is unmeasured.
- **Whether the user's trucks are on the §3.2 residue chain or the §3.3 moving-follow-cell path.** The
  0807/0808 recon's by-eye test still separates them: §3.2 heads for the **map edge** with a **red** supply
  bar (`SupplyProvider.cs:850`); §3.3 heads for the **army**, amber bar.
- **The effective `NetFrameInterval`**, which scales the ≥2-tick resolution floor (0807 §7, still open).
- **Whether dropping orders at the funnel perturbs the `⌈N/5⌉` drain enough to change outcomes** beyond the
  intended suppression. Reasoned, not measured.

---

## Appendix — the 28 damping mechanisms

"Protects vs." is the key column: **every row except the last two says "same module only."**

| # | Guard site (`file:line`) | Owner | Form | Protects vs. | Shipped value |
|---|---|---|---|---|---|
| 1 | `PoiOffensiveBotModule.cs:3031` (predicate `:3027-3030`) | PoiOffensive axis | (b)+(a) `HasOrdered`/`OrderedCell` | same module | `ai.yaml:260` `RepathThresholdCells: 3` (stable twin `:1638`) |
| 2 | `PoiOffensiveBotModule.cs:3995` | PoiOffensive screen hold | (b)+(a) same fields | same module | same |
| 3 | `PoiOffensiveBotModule.cs:4019` | PoiOffensive `OrderRetreat` | (b)+(a) | same module | same |
| 4 | `PoiOffensiveBotModule.cs:2289` (`stagedCells`) | PoiOffensive `StageFreePool` | (a) exact-cell dedup | same module | none (structural) — **purged `:2214-2222`** |
| 5 | `PoiOffensiveBotModule.cs:1977` (`ForwardStagingMath.AnchorShifted`) | PoiOffensive staging anchor | (b) Chebyshev hysteresis | same module | `ai.yaml:550` `StagingHysteresisCells: 3` |
| 6 | `PoiOffensiveBotModule.cs:2128` (`OpportunisticAdvanceMath`) | PoiOffensive advance anchor | (b) one-way hysteresis | same module | `AdvanceHysteresisCells: 0` — **inert as shipped** |
| 7 | `PoiOffensiveBotModule.cs:3163` (`lastFiresAnchor`) | PoiOffensive fires standoff | (a) **or** (b) drift | same module | `RepathThresholdCells: 3` |
| 8 | `PoiOffensiveBotModule.cs:3474` (`lastBombardAnchor`) | PoiOffensive bombard | (a)+(b), mirrors #7 | same module | same |
| 9 | `PoiOffensiveBotModule.cs:2838` (`FrontlineAllocationMath.PostureBudgetExhausted`) | PoiOffensive posture | (e) eval budget | same module | `ai.yaml:619` `SectorPostureHoldMaxEvals: 3` |
| 10 | `RetreatDamperMath.StepReadvanceHold:64` | PoiOffensive retreat | (c) dwell in evals | same module | `ai.yaml:564` `RetreatReadvanceDwellEvals: 3` |
| 11 | `PoiOffensiveBotModule.cs:1897` `SelectStickyTargets` | PoiOffensive selection | (h) score-margin hysteresis on the *objective*, not the order | same module | `AxisCommitmentTicks: 250` (`ai.yaml:258`) |
| 12 | `PoiGarrisonBotModule.cs:475` (predicate `:473-474`) | PoiGarrison | (b)+(a), own copy of the fields | same module | `ai.yaml:660` `RepathThresholdCells: 3` |
| 13 | `LaneAmbushBotModule.cs:435` (predicate `:433-434`) | LaneAmbush | (b)+(a), own copy again | same module | `ai.yaml:703` `RepathThresholdCells: 3` |
| 14 | `SupplyFollowerBotModule.cs:818` (`SupplyDropMath.ShouldIssueDrop:156-159`) | SupplyFollower drop | (a) dedup on `dropTarget` | same module | none — **purged `:485-490`** |
| 15 | `SupplyFollowerBotModule.cs:967` (`AnchorShifted`) | SupplyFollower drop anchor | (b) hysteresis | same module | `ai.yaml:869` `DropAnchorHysteresisCells: 3` |
| 16 | `SupplyFollowerBotModule.cs:703` (`lastVia`) | SupplyFollower Stage-E | (b) waypoint deadband | same module | `RepathThresholdCells` (`:61`) |
| 17 | `SupplyLogisticsMath.StepEvacDwell` @ `SupplyFollowerBotModule.cs:1474` | SupplyFollower evac | (c) dwell + (b) deadband | same module | `ai.yaml:819` `EvacDwellScans: 1`, `:820` `EvacReleaseHysteresis: 15` |
| 18 | `GarrisonBotModule.cs:468` | GarrisonBotModule release | (c) time dwell, asymmetric | same module | `ai.yaml:767` `MinGarrisonDwellTicks: 750` |
| 19 | `CaptureCoordinatorBotModule.cs:1200` (`AnchorShifted`) | CaptureCoordinator reserve | (b) hysteresis | same module | `ReserveHysteresisCells = 3` (`:338`, C# default) |
| 20 | `LayeredDefenceBotModule.cs:342` | LayeredDefence | (c) per-unit assign cooldown | same module | `ai.yaml:1006`/`:1744` `AssignCooldownTicks: 250` |
| 21 | `HelicopterStates.cs:610-613`, `:831-836` (`HeliPathHysteresis:923`) | Heli squad states | (b) committed-leg hysteresis — a **second** implementation of #5's idea | same module | `ai.yaml:1484-1485` `FlightPathHysteresis: true`, `…Cells: 3` |
| 22 | `Traits/Garrison/GarrisonManager.cs:833-838` | GarrisonManager | (c) sticky target | same trait | `StickyTargetTicks = 50` (`:81`) |
| 23 | `StancePositioningExecutor.cs:336` | StancePositioning | (c) per-unit eval cooldown | same trait | `EvaluateCooldown = 30` (`:73`) |
| 24 | `StancePositioningExecutor.cs:405` `WithinOneCell` | StancePositioning | (a) dedup, 1-cell tolerance ball | same trait, **and** suppresses CohesionSlotMemory re-dispatch (`:392-394`) | none |
| 25 | `CohesionSlotMemory.cs:220` | CohesionSlotMemory | (c) cooldown; (a) exact-equality at `:207` | same trait | `ReturnCooldownTicks = 25` (`:32`) |
| 26 | `CohesionSlotMemory.cs:195` `settleFacingDone` | CohesionSlotMemory | (g) one-shot latch (facing only) | same trait | — |
| 27 | `DropsSupplyCache.cs:295-299` `lastEvacuateTick` | DropsSupplyCache | (g) once-per-frame latch | same trait | — |
| 28 | `PoiGoalGuard.cs:81-82` `GoalGuardLedger.IsCommitted` | **shared** — 11 reader modules | (d) ownership claim, TTL | **cross-module** | `ai.yaml:89` `DefaultCommitmentTicks: 600`; per-module TTLs 250 |

Plus (f), the `IsIdle` busy-check, at ~57 sites — not a lock, since modules re-fire on timers regardless
(0807 §3.4). Note the undamped sites that have *no* row here at all:
`SupplyFollowerBotModule.cs:681`/`:714` (§3.3), `MountedTransportBotModule.cs:621` (§4.1), and
`DropsSupplyCache.cs:317` (§3.2).
