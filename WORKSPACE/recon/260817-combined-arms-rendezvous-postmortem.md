# Combined arms — why the rendezvous measured harmful, and what is actually worth doing

**Date:** 2026-08-17 · **Branch:** `wt/combined-arms-research` · **Base:** `main @ f882681a`
**Status:** research only. No production code, no game launched, no autotest/batch/tournament run.
**PIPELINE:** item 64.

> **Bottom line.** The rendezvous did not fail because coupling is dangerous. It failed because it
> consumed an anchor that was **not an anchor at all** — a grid-quantisation artifact of the Supply
> Route cell, produced by a zero-step gradient descent, published because the guard written to reject
> exactly that case **cannot fire for 3 out of 4 Supply Route placements**. The missing lower bound
> in `AnchorAcceptable` is the proximate cause on record; it is one layer above the real one.
>
> **Recommendation: do not re-attempt the coupling in this release window.** Reasons in §3. The two
> things worth doing cost zero run budget between them.

---

## 0. Predictions registered before verification, and how they scored

Per the brief, registered before reading code. Two of three were wrong.

| # | Prediction | Verdict |
|---|---|---|
| **P1** | The brief's mechanism ("armour and mounted infantry compute destinations independently, with different arithmetic") is *wrong*; the real gap is timing, not destination. | **Half right, and the wrong half matters.** The arithmetic difference is literally true in code. But it is not the binding constraint, and it is not why the rendezvous was harmful. |
| **P2** | The rendezvous was harmful because it gated fast units on slow ones — added latency, paralysis, same shape as `bd3abacf`. | **WRONG, and importantly so.** There is no wait state anywhere in the rendezvous. `RendezvousMath.ResolveDropOff` changes a *destination* and nothing else. The harm was a degenerate destination causing a shuttle livelock, not a stall. I imported the `bd3abacf` frame from the brief and it does not apply. |
| **P3** | `CohesionMoveModifier`'s `Tight` branch is a red herring for this question. | **Right, for a reason I did not predict** — see §4. `Tight` is gated `isHuman` (`:1085`), so it never applies to a bot, and nothing in the mod sets `Tight` anyway (`defaults.yaml:321-322` sets `Loose` for both humans and AI, globally, with no per-unit-type override). The brief's hint is doubly inert. But the file is the right place to look, for a different reason.

---

## 1. Why did the rendezvous coupling measure harmful?

### 1.1 What was observed

From `4c4d8a49` / `97cb73c2` (run `260815_202509`, seed 1017): with
`RendezvousWithOffensiveStaging` enabled, the drop cell moved from `lerp=32,10` to `anchor=7,17`.
The Supply Route is at `6,16`. The carrier then **looped four times: load 5, drive one cell, unload,
reload.** A 26-cell forward delivery became a shuttle in place.

### 1.2 The cause on record — correct, but not the root

`mods/ww3mod/rules/ai/ai.yaml:1605-1609` states the cause as a missing lower bound:

```csharp
// RendezvousMath.cs:71-78
public static bool AnchorAcceptable(int srX, int srY, int anchorX, int anchorY, int fallbackX, int fallbackY, int marginCells)
{
    var margin = Math.Max(0, marginCells);
    var anchorReach   = CellDistance(srX, srY, anchorX, anchorY);
    var fallbackReach = CellDistance(srX, srY, fallbackX, fallbackY);
    return anchorReach <= fallbackReach + margin;   // bounded ABOVE only
}
```

Confirmed. With `sr=(6,16)`, `anchor=(7,17)`, `fallback=(32,10)`, `margin=6`
(`RendezvousMaxAdvanceCells`, default 6): `anchorReach=1`, `fallbackReach=26`, and `1 <= 32` passes
trivially. An anchor arbitrarily far *behind* the transport's own choice is accepted without limit.

**The guard was designed against the wrong failure direction, and its own header says so.**
`RendezvousMath.cs:26-30` reasons that "the staging anchor ADVANCES as the believed front moves, so
a transport that chased it unconditionally could be walked steadily deeper." That is the *late-game*
regime. The harmful regime is the *opening* — which is precisely and only when `DeliverBeforeContact`
runs. The safety term is anti-correlated with the phase it executes in.

### 1.3 The root cause — the anchor was never legitimate

The record stops at "the frontier descent has nothing to descend toward, so the anchor sits on the
SR." **That is not what the code does, and the difference is the whole finding.**

`PoiOffensiveBotModule.ResolveStagingAnchor()` (`:2114-2147`) already contains a guard for a
degenerate descent, and it returns `null` — not the SR cell:

```csharp
var (sgx, sgy) = controlField.MapCellToGridCell(rallyCell.Value);   // MAP -> GRID
var (agx, agy) = ForwardStagingMath.StagingCell(sgx, sgy, ...);     // descent, in GRID space
var candidate  = controlField.GridCellToMapCell(agx, agy);          // GRID -> MAP

// Descent stayed at the SR => no forward gradient ... no staging this eval.
if (candidate == rallyCell.Value)      // <-- compared in MAP space
{
    lastStagingAnchor = null;
    return null;
}
```

If that guard fired, `hasAnchor` would be false and `ResolveDropOff` would return the fallback
unchanged. The rendezvous would have been a **no-op** before contact, not harmful.

**It does not fire, because the round-trip is lossy.** From `InfluenceMap.cs:126-137`:

```csharp
public (int X, int Y) MapCellToGridCell(CPos c) => (c.X / Info.CellSize, c.Y / Info.CellSize);   // floor
public CPos GridCellToMapCell(int gx, int gy)
    => new CPos(gx * Info.CellSize + Info.CellSize / 2, gy * Info.CellSize + Info.CellSize / 2);  // CENTRE
```

`GridCellToMapCell(MapCellToGridCell(c))` returns the **centre of c's grid cell**, which equals `c`
only by coincidence. With `CellSize: 2` (`world.yaml:290-291`) the round trip is
`X -> 2*(X/2)+1`, so it is the identity **only when the coordinate is odd**. Both coordinates must be
odd for the guard to fire: **it is reachable for 1 Supply Route placement in 4, by parity.**

**This reproduces the logged run to the digit.** SR `(6,16)`, both even:

```
MapCellToGridCell(6,16) = (6/2, 16/2)        = (3, 8)
zero-step descent                             -> (3, 8)
GridCellToMapCell(3,8)  = (3*2+1, 8*2+1)     = (7, 17)   <-- the logged anchor, exactly
guard: (7,17) == (6,16)?  NO -> anchor published
```

The anchor observed in the harmful run carried **zero information**. It is the SR, re-projected
through a lossy quantisation. `ForwardStagingMath.StagingCell` (`:108-113, 150-151`) returns its
seed unchanged in three cases, including the pre-contact one — no neighbour is strictly closer to a
front that is not yet believed, so the loop breaks on step 0 and returns `(startX, startY)`.

### 1.4 So the honest causal chain

1. Pre-contact, the frontier field is flat → the descent moves zero grid steps and returns its seed.
2. The seed is converted back to map space through a **lossy** grid round-trip, producing a cell ~1
   away from the SR instead of the SR itself.
3. The `candidate == rallyCell` guard is written in map space against a quantised value, so it
   **cannot fire** unless both SR coordinates are odd. A false anchor is published.
4. `AnchorAcceptable` has no lower bound, so the false anchor — 1 cell from the SR — is accepted over
   a 26-cell forward delivery.
5. The transport delivers one cell from its own Supply Route, the passengers are immediately eligible
   again, and it reloads. Four times.

**Fixing only step 4 (adding a lower bound) masks steps 1-3 and leaves the false anchor in
circulation.** That matters because the rendezvous is not its only consumer — see §5.

---

## 2. What is the actual failure a player sees?

Stated as a spectator would describe it, not as internal state.

**A single tank drives out of the base and into the enemy on its own.** Behind it, a scattered line of
infantry walks the same route on foot, strung out over twenty to forty cells, arriving in ones and
twos over the following half-minute. They are shot piecemeal as they trickle in. The transport that
should have carried them either sits at the base or leaves with one passenger aboard and four empty
seats.

Three distinct mechanisms produce that one picture, and they are worth separating because they have
different fixes and different states of repair:

| # | Mechanism | State |
|---|---|---|
| **(a)** | **The infantry never boarded.** `StageFreePool` records a standing `AttackMove` at tick 7; the transport's `EnterTransport` is single-target `Recurring` so it goes through `BotOrderGate`'s dwell rule; a walking infantryman reports busy at age 58 < `ReorderDwellTicks` **120** (`ai.yaml:48`) and is suppressed. The offensive module's `ReevaluateInterval` is **100** (`ai.yaml:319`) — *shorter than the dwell* — so it refreshes its own standing record inside its own suppression window forever. **A damping window shorter than the period of the module refreshing it is a permanent lock, not a delay.** Measured: seats 1 of 5, first departure tick 1015. | **FIXED and measured.** `TransportStandoffEnabled: true`, confirmed at `ai.yaml:463` inside `PoiOffensiveBotModule@experimental` (block opens `:317`). Seats 1→5, departure 1015→**365** — earlier, not later. |
| **(b)** | **They are sent to the right place anyway, so the destination was never the problem.** `314f0ed3`'s durable finding: `StageFreePool` recruits armed infantry and walks them to the *same* anchor as the armour from tick 3. The tank drives and they walk. **The lever is the speed differential.** | **Open. Untouched by anything shipped.** |
| **(c)** | **They do not move as a body even when going to the same cell.** See §4. | **Open, newly identified.** |

**(b) is why I judge a fourth rendezvous attempt low-value.** A tank that outruns its infantry is
indistinguishable, from the spectator's seat, from one sent where they were not. The rendezvous
addresses a destination divergence that is real in code but is not what the user is watching.

---

## 3. What would I try next, and what measurement settles it?

### 3.1 First: pay the verification debt. Zero new code.

`4c4d8a49` states plainly: *"NOT independently verified: standoff-on/rendezvous-off is the shipped
configuration and was never run as such — the two were measured together and the rendezvous then
switched off. The attribution is mechanical reasoning, not measurement."*

**The currently-shipped `@experimental` configuration has never been run.** That is the cheapest and
highest-value measurement outstanding, and it is a regression check on code already merged.

- **Run:** one `run-test.sh wip-transport-delivers`, seed 1017, current `main`. *(Manager holds the grant — I have not run it.)*
- **Acceptance, as numbers:**
  - **First departure carries ≥ 4 of 5 seats.** (Baseline 1; standoff-measured 5.) Below 4 → the shipped config does not reproduce the measured gain and the attribution in `4c4d8a49` is wrong.
  - **First departure at tick ≤ 500.** (Baseline 1015; standoff-measured 365.) Above 500 → the standoff is extending the wait, violating the standing never-delay-a-departure constraint.
  - **Drop cell Chebyshev distance from own SR ≥ 10.** Guards against any recurrence of the shuttle without needing the rendezvous enabled.

### 3.2 Second: repair the guard, provable with no game run at all.

The `ResolveStagingAnchor` defect (§1.3) is a correctness bug **independent of the rendezvous** and
live right now with the flag off (§5). The repair is to compare in the space the descent happened in:

> compare `(agx, agy)` against `(sgx, sgy)` in **grid** coordinates, instead of comparing
> `GridCellToMapCell(agx, agy)` against `rallyCell` in **map** coordinates.

- **Measurement: an NUnit case, no game session.** `ForwardStagingMath` is already engine-free and
  NUnit-pinned, as are `RendezvousMath`, `CohesionLayoutMath` and `CohesionIntentMath`.
- **Acceptance, as a number:** a flat frontier field with the SR at **all four coordinate parities**
  — `(6,16) (7,16) (6,17) (7,17)` — yields **4 of 4 null anchors**. Today it yields **1 of 4**.
  That single number is the whole finding, and it falsifies cleanly.

### 3.3 What I would NOT do in this window

**Do not re-enable the rendezvous, with or without a lower bound.** Even fully repaired it addresses
mechanism (b)'s destination, and the durable finding is that the destination was already shared. It
is necessary-but-not-sufficient by construction and its user-visible payoff is unevidenced. The
user's bar is between "credible" and "not embarrassing"; a correct-but-invisible destination fix does
not move that bar, and this feature has now consumed three attempts.

**Do not build an armour hold that waits for followers.** Deferred by the `2026-08-15` scope ruling,
and `bd3abacf` is the standing prior art. Note that P2 was wrong — the *rendezvous* never had a wait
state — but that cuts the other way: the paralysis risk of an actual hold has still never been tested
here, so `bd3abacf` remains unretired as a warning about the thing nobody has yet built.

---

## 4. New finding: the bot's own order shape defeats formation logic

Not previously on record, and the closest thing to a real combined-arms lever I found.

`CohesionMoveModifier` is what makes a group move as a body. It handles both `Move` and `AttackMove`
(`:1013-1015`), and it is **not** gated off for bots. But it opens with a hard minimum group size:

```csharp
// CohesionMoveModifier.cs:1017-1026
var n = 0;
for (var i = 0; i < allGroupedActors.Length; i++)
{
    var a = allGroupedActors[i];
    if (a != null && !a.IsDead && a.IsInWorld)
        n++;
}

if (n <= 1)
    return individualOrder;   // <-- unmodified; no layout computed
```

`StageFreePool` issues **one order per unit**, each carrying a group of exactly one
(`PoiOffensiveBotModule.cs:2471-2472`):

```csharp
new Order("AttackMove", null, Target.FromCell(world, target), false, groupedActors: new[] { u })
```

So every staging order hits `n <= 1` and returns unmodified. **The bot's entire forward staging is
invisible to the formation system** — N groups of one, never one group of N. Each unit paths
individually to the same cell and arrives smeared in time, which is mechanism (c) in §2 and
contributes directly to the strung-out arrival the user is describing.

**This is a finding, not a proposal.** Changing the order shape is a behavioural change with a real
blast radius (it would alter every staged unit's path simultaneously, and `stagedCells` dedup at
`:2461` assumes per-unit targets). It needs its own brief and its own measurement — an
arrival-spread number, e.g. the tick gap between first and last member reaching the anchor. I am
flagging it, not recommending it for this window.

---

## 5. The false anchor is not confined to the rendezvous

With `RendezvousWithOffensiveStaging: false`, the false anchor of §1.3 is still being published and
still has consumers:

- `PoiOffensiveBotModule.cs:2397` — `if (!stagingAnchor.HasValue || idle.Count == 0) return;`
  A false anchor makes this `HasValue`, so **`StageFreePool` runs pre-contact when its author's
  comment says it should not.** It then `AttackMove`s the free pool to a cell ~1 away from the SR.
- `:2970` and `:3051` — `OrderRetreat(bot, axis, ResolveMusterAnchor(axis) ?? stagingAnchor ?? rallyCell.Value, tick)`.
  A false anchor pre-empts the `rallyCell` fallback in the retreat path.

Blast radius today is small — the bogus cell is ~1 from the SR, so the resulting orders are close to
no-ops. **The significance is that a deliberate "do not stage before contact" rule silently does not
work, and any future consumer that trusts `ForwardStagingAnchor` inherits the same trap the
rendezvous did.** That is the argument for §3.2 being worth doing even though the rendezvous stays
off.

---

## 6. What I could not verify

- **Nothing was run.** No game, no autotest, no batch, no tournament — per the brief's hard rule. Every
  runtime claim here is either quoted from a prior commit's logged measurement or derived from code.
- **The `(7,17)` reproduction is arithmetic, not observation.** It matches the logged anchor exactly
  and I consider it strong, but I did not observe `ResolveStagingAnchor` return that value. The chain
  assumes the SR/`rallyCell` in run `260815_202509` was `(6,16)` — which is what `ai.yaml:1601-1602`
  records, so I am trusting that comment for the input while using it to explain the output.
- **`rallyCell == the Supply Route cell`** is assumed from naming and from the ai.yaml comment, not
  traced to its assignment.
- **The 1-in-4 parity figure assumes `CellSize: 2`** (`world.yaml:291`) and no per-map override. I
  grepped `mods/` and found only that one `InfluenceMap` value (the other, `CellSize: 8`, is
  `ThreatMapManager`, a different trait), but I did not check map-level YAML overrides.
- **§4's conclusion is code-derived, not observed.** I have not seen an arrival-spread measurement,
  so "contributes directly to the strung-out arrival" is mechanism, not evidence. The `n <= 1` early
  return itself is certain.
- **I did not audit `MountedTransportBotModule.PreContactStagingCell`'s lerp myself** — it is reported
  as a 50% lerp at `PreContactStagingPct: 50` by a subagent read and is consistent with the logged
  `lerp=32,10`, but that specific arithmetic is second-hand.
- **The `CaptureCoordinatorBotModule` `transportModuleResolved` one-shot latch** flagged in PIPELINE
  item 64 (`:1323-1328`) was **not checked**. It remains an open diagnostic that costs zero code —
  `:1308-1309` already logs `ferried=True|False`.
