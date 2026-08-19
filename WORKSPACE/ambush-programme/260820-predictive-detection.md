# Predictive detection — "stop before you are seen"

**Date:** 2026-08-20 · **Branch:** `wt/predictive-detection` · **Base:** `main @ 4bb3fae9`
**Status:** DESIGN ONLY. No behaviour changed, no code written, no YAML touched. **Game never launched;
no validator or test run.** Every `file:line` below was read from source in this worktree.

**Requirement (user's words):** *"A soldier that is running and is almost spotted by an enemy (that we
have detected/can see), then our soldier will stop running, dropping its visibility that way, and
remaining hidden."*

**Design brief (user's words):** *"I want some more automatic behaviour when it is helpful to the
player. It should not feel invasive, but if we can figure out small things that soldiers can do on
their own, that will feel like the soldier's 'instinct', then it is good."*

**Shipping scope (ruled 2026-08-20):** stop-and-resume ships first and is designed properly; reroute is
a later stage, sketched here only far enough to prove the seam (§8).

---

## 0. Verdict up front

The feature is **buildable, cheap, and deterministic** — and the mechanism it needs already exists in
three separate shipped places, so almost nothing new has to be invented:

1. Stopping really does conceal. Not a premise to be built — it is live today, worth **9 cells** for a
   rifleman (§1.3).
2. Stop-and-resume has a **shipped precedent with a written rationale**: pause `Mobile` via
   `PauseOnCondition`, do not cancel the order. `SupplyProvider.ServingCondition` does exactly this and
   documents why (§4.1). This is the single most useful finding in the document.
3. The "am I spotted by an enemy I know about" predicate is **already written**, in
   `WithSpottedDecoration.IsSpotted` — but it is render-only and reads `RenderPlayer`, so it must be
   re-derived rather than lifted (§3.2). That one line is the whole determinism trap.

**The risk is not engineering. It is that the behaviour may not be good.** Against an observer who is
walking toward you, stopping delays detection by a couple of seconds and throws away your head start,
leaving a stationary soldier to be seen anyway. See §9 — this is the thing most likely to sink the
feature, and it shapes the v1 trigger.

---

## 1. How being seen actually works here

### 1.1 The reveal rule is one comparison

`MapLayers.IsVisible` (`engine/OpenRA.Game/Traits/Player/MapLayers.cs:574-579`):

```csharp
public bool IsVisible(PPos puv, int visibility)
{
    if (!FogEnabled)
        return map.Contains(puv);

    return ResolvedVisibility.Contains(puv) && ResolvedVisibility[puv] > visibility;
}
```

`ResolvedVisibility` is a `ProjectedCellLayer<byte>` (`:131`) holding, per player per cell, the highest
vision strength any of that player's sources stamps there. So:

> **You are seen when some enemy source puts a strength on your cell strictly greater than your
> current visibility level.**

The level is `Detectable.CurrentVisibility` — `[Sync]`ed, recomputed every tick, clamped to `[1,10]`
(`Detectable.cs:70-71, 86-95`). Throughout this document I write **CV**.

**Sign trap, and it has already caused shipped copy errors:** a *higher* CV means *harder to see*
(`Detectable.cs:24` — *"What level of vision is required to detect this actor"*). A **positive**
`VisionModifier` conceals.

### 1.2 Strength is distance, in ten nested bands

`^StandardVision` (`defaults.yaml:47-84`) is ten concentric annuli, strength 10 at 4 cells down to
strength 1 at 32 cells. Reveal needs strength **CV+1**, so the range at which an observer first sees
you is the outer edge of the band carrying strength `CV+1`:

| CV | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| **Seen from** | 28c | 25c | 22c | 19c | 16c | 13c | 10c | 7c | 4c | **never** |

*(Derived from `MapLayers.cs:574-579` against the YAML ladder; carried from
`WORKSPACE/recon/260819-infantry-visibility-stances.md` §2.2, which marks it `[NEEDS RUN]`.)*

### 1.3 Stopping conceals — this is live today, and it is the whole feature

Verified against `mods/ww3mod/rules/ingame/infantry.yaml:703-732`:

| Modifier | Condition | Effect on CV | Site |
|---|---|---|---|
| Moving | `moving` | **−1** | `:730-732` |
| Prone | `prone` | **+1** | `:716-718` |
| Dug in | `dugin` | **+1** | `:719-721` |
| Firing | `firinganyweapon` | **−2** | `:727-729` |
| In cover ×1/2/3 | `object-proximity` | **+1/+2/+3** | `:707-715` |

`prone` is granted on `!moving` (`:294`) — **prone is simply what a stopped soldier is**, no command,
no delay. `dugin` arrives from `GrantConditionOnMovement` with `TimeToBeStill: 200` (`:139-142`).

So for a plain rifleman (`Vision: 3`):

| State | CV | Seen from |
|---|---|---|
| Running | 3 − 1 = **2** | **25c** |
| Stopped (prone, immediate) | 3 + 1 = **4** | **19c** |
| Stopped 200 ticks (prone + dug in) | 3 + 1 + 1 = **5** | **16c** |

**Stopping is worth 6 cells immediately and 9 cells after 200 ticks.** The user's premise is correct
and already implemented. Nothing in §1.3 needs building.

**Tick rate is not a constant.** `Timestep` is a player-selectable game speed, 120 ms down to 30 ms,
default 60 ms (`mods/ww3mod/mod.yaml:357-403`). 200 ticks is 12.0 s at default and 24 s at
"strategical". **Every horizon and dwell constant in this design is specified in ticks and must never be
converted to seconds in code.**

### 1.4 Nothing reads "am I about to be spotted"

Confirmed by the 2026-08-19 recon (§5, exhaustive) and re-checked here. Every existing reaction to being
seen is a decision to *shoot*: the Ambush spring (`AutoTarget.cs:749-762`) fires on being spotted, which
costs −2 CV and makes the unit *more* visible. **No code path anywhere breaks contact, goes prone, or
re-conceals in response to visibility.** This is new behaviour, not a tuning problem.

---

## 2. The predicate

### 2.1 Margin, not a boolean

Define, for our unit `u` and one observer `o` that we know about:

```
required(u)     = CV(u) + 1                    // strength needed to reveal us (strictly greater)
R(o, u)         = max Range over o's Vision bands whose Strength >= required(u)
                  ( = 0 if o carries no such band — o can never reveal u at any range)
margin(o, u)    = |pos(o) - pos(u)| - R(o, u)   // >0 hidden, <=0 revealed
```

`R` is a property of the observer's band ladder and our CV, so it is recomputed whenever either changes
— which is every tick in principle, but see §5.

**Off-by-one, and it is already shipped wrong.** `WithSpottedDecoration.VisionCovers`
(`WithSpottedDecoration.cs:136-157`) skips bands with `visionInfo.Strength < requiredStrength` where
`requiredStrength = detectable.CurrentVisibility` — i.e. it accepts `Strength == CV`, which does **not**
reveal (`IsVisible` is strictly `>`). It is one band — roughly 3 cells — optimistic. It gets away with
this because `IsSpotted` ends on an exact truth gate (`self.CanBeViewedByPlayer(owner)`, `:115`) that
corrects the optimism.

**A predictor has no truth gate available, because the future has no ground truth to check against.**
Any reuse of that helper must fix the comparison to `Strength < CV + 1`. The same off-by-one is recorded
against `^DetectableRangeCircles` in the recon (§7.1 G2), so this is a family of bugs, not an isolated
one.

### 2.2 Terrain shadow must be subtracted, and it can be

`MapLayers.AddSource` (`MapLayers.cs:357-375`) reduces a source's strength along the sightline before
stamping:

```csharp
var modifiedStrength = strength - shadowModify;
if (modifiedStrength < 1) modifiedStrength = 1;
```

`WithSpottedDecoration` explicitly gives up on reproducing this (`:131-135`: *"the per-source records
that would answer exactly are private with no accessor"*). **A predictor cannot afford to give up**,
because woodland is exactly where ambushes happen: ignoring shadow makes the unit systematically
pessimistic and it would halt constantly under trees, for threats that could never have seen it.

It is reachable. `Map.ShadowLayer` is a public property (`Map.cs:253`) of the public sealed
`MapShadowLayer`, which exposes `public (byte GroundShadow, byte AirborneShadow) this[MPos from, MPos to]`
(`MapShadowLayer.cs:105`). It is frozen at map load (`Building.cs:372-397`, disabled recalc) and is map
data, so it is byte-identical on every client. **The predicate must apply the same subtraction and the
same floor of 1**, or it does not model the shipped rule.

### 2.3 Projection and the horizon

Sample per observer: current position, our position, both velocities (`Mobile` exposes movement state;
a one-tick position delta is the cheap deterministic estimate), our CV, the observer's band ladder, and
the shadow term along the segment.

Rather than integrate a path, project linearly and ask a distance question:

```
closingSpeed = -( d/dt |pos(o) - pos(u)| )    // >0 when the gap is shrinking
TRIGGER when   margin(o, u) <= closingSpeed * H
```

**How far ahead is H?** It must cover the time between deciding to stop and actually being concealed:

- **The stop is not instant.** `Move.Tick` releases at a cell boundary — a unit finishes its current
  cell transition before it holds (`Move.cs:160-168`). Sub-cell stopping does not exist.
- **Condition propagation** — `moving` revoked → `prone` granted → `Detectable.ITick` recomputes CV.
  That is a small number of ticks, not zero.
- **The evaluation period** (§5) — if we only evaluate every `N` ticks we may already be up to `N` ticks
  stale.

So `H >= N + cellTransitionTicks + conditionLatency`, plus margin. **Recommend `H = 15` ticks**
(≈0.9 s at default speed) with `N = 5`. This is a starting point to be tuned against a run, not a
measured value.

**Should H depend on speed? It already does, and that is the elegant part.** H is a constant in ticks;
the *distance* threshold it implies is `closingSpeed * H`, which is automatically proportional to how
fast the threat is closing. A sprinting scout trips the trigger from further out than a crawling one,
with no special-casing. Do not add a second speed term.

### 2.4 Fog legality: only enemies we know about

The user's parenthesis — *"that we have detected/can see"* — is a hard constraint, and it is what forbids
the obvious cheap implementation. There is a ready-made, correct filter, used identically by two shipped
systems:

```csharp
if (!observer.CanBeViewedByPlayer(self.Owner))
    continue;   // an enemy we have not spotted does not get to influence our behaviour
```

`SightingThreatLayer.InjectSightings` uses precisely this (`:207`), as does `BeliefStore`, and
`WithSpottedDecoration` states the reasoning (`:20-22`): a badge driven by true visibility *"would be a
wallhack"*. The same argument applies with more force to behaviour than to a badge — a unit that dodges
threats it cannot see is a cheating unit.

### 2.5 Hysteresis — and the real source of oscillation

The brief asks for damping against boundary flicker. Sensor noise is **not** the main problem here. The
predicate is **self-referential**, and that alone makes a single-threshold design oscillate *by
construction*:

> Stop → `moving` revoked, `prone` granted → CV rises by 2 → R shrinks by ~6 cells → margin becomes
> comfortably positive → "I am safe" → resume → CV drops by 2 → R grows → "I am about to be seen" →
> stop.

A unit sitting still evaluated on its *stopped* CV will always conclude it is safe, because it is safe
*because it stopped*. No amount of threshold tuning fixes this; it is a modelling error.

**The fix is to evaluate the resume test with the CV the unit would have if it resumed** — the
moving-CV, i.e. current CV with the stop-derived modifiers (`prone`, `dugin`) removed and `moving`
re-applied. Ask *"if I started running again right now, would I be about to be seen?"* That question has
a stable answer and the loop disappears.

On top of that, three ordinary dampers:

| Damper | Value | Why |
|---|---|---|
| Asymmetric thresholds (Schmitt) | halt at `margin <= closing*H`; resume only at `margin > closing*H + G`, `G ≈ 2 cells` | separates the two edges so a stationary boundary cannot chatter |
| Minimum hold | ≈ 30 ticks | a halt shorter than this is never useful — it costs a cell of travel and buys no concealment the player can perceive |
| Re-halt cooldown per contact | ≈ 60 ticks | stops one persistent observer from ratcheting a unit to a standstill |

---

## 3. Determinism — the gate

### 3.1 Two independent gates, and an API can pass one while failing the other

This distinction is the most important thing in this document, and it is easy to collapse the two:

- **Sync-safety** — does every client compute the same answer? Violated by anything downstream of
  `RenderPlayer`, because that is whose screen it is.
- **Fog-legality** — is the answer one this player is *entitled* to? Violated by reading ground truth,
  even when it is perfectly deterministic.

`enemyPlayer.MapLayers.IsVisible(myCell, myCV)` is **perfectly sync-safe and completely illegal**: it is
a pure function of synced state, gives an identical answer on every client, and tells us what the enemy
can see using observers we have never detected. It would pass a desync test forever and it is a
wallhack. **Do not use the enemy's `MapLayers` for this feature at all.**

### 3.2 The trap this feature walks straight into

The predicate we need is already written — `WithSpottedDecoration.IsSpotted`
(`WithSpottedDecoration.cs:82-120`). It enumerates nearby enemies, applies the fog filter, checks the
band ladder. It is the right shape. And its very first line is:

```csharp
// WithSpottedDecoration.cs:86
var viewer = self.World.RenderPlayer ?? self.Owner;
```

**Lifting this function into a simulation-ticked trait is the desync.** Its own header says so
(`:24-28`): *"RENDER-ONLY. Evaluated from the render path, reads RenderPlayer, and writes nothing that
simulation can observe, so it cannot desync and it grants no condition."*

The fix is one line — take the viewer as `self.Owner`, never `RenderPlayer` — but the function must be
**re-derived into a shared sim-side helper, not called from the render trait**, and the render trait
should then be re-pointed at the helper so the two cannot drift. This project's standing rule is
explicit that duplicating subtle logic and relying on comments to keep copies in step does not work.

### 3.3 Named API table

| API | Site | Sync-safe? | Fog-legal? | Use it? |
|---|---|---|---|---|
| `player.MapLayers.IsVisible(cell, level)` | `MapLayers.cs:554-579` | **YES** — explicit `Player`, flat byte array read | only for **our own** player | **YES**, for our own vision |
| `player.MapLayers.IsExplored(cell)` | `MapLayers.cs:492-521` | **YES** | ditto | yes if needed |
| `actor.CanBeViewedByPlayer(simPlayer)` | `Actor.cs:591-599` | **YES** when passed a sim `Player` | **YES** — this is the fog filter | **YES — the load-bearing call** |
| `Detectable.CurrentVisibility` | `Detectable.cs:70-71` | **YES** — `[Sync]`ed | yes (our own) | **YES** |
| `Map.ShadowLayer[from, to]` | `MapShadowLayer.cs:105` | **YES** — map data, frozen at load | yes | **YES** (§2.2) |
| `world.FogObscures(Actor/CPos/WPos)` | `World.cs:109-111` | **NO — RenderPlayer** | n/a | **NEVER in sim** |
| `world.ShroudObscures(...)` (4 overloads) | `World.cs:112-115` | **NO — RenderPlayer** | n/a | **NEVER in sim** |
| `world.RenderPlayer` / `LocalPlayer` | `World.cs` | **NO** | n/a | **NEVER in sim** |
| `IResourceLayer.IsVisible(CPos)` | `ResourceLayer.cs:301` | **NO** | n/a | **NEVER — see below** |
| `enemyPlayer.MapLayers.IsVisible(...)` | `MapLayers.cs:554` | YES | **NO — omniscient** | **NEVER for this feature** |
| `WithSpottedDecoration.IsSpotted` | `:82-120` | **NO — RenderPlayer at `:86`** | yes | **re-derive, do not call** |

**The worst-named API in the codebase**, still present and unfixed at `main @ 4bb3fae9`:

```csharp
// engine/OpenRA.Mods.Common/Traits/World/ResourceLayer.cs:301
bool IResourceLayer.IsVisible(CPos cell) { return !world.FogObscures(cell); }
```

An interface method called `IsVisible(CPos)` whose name promises nothing about rendering, implemented as
a `RenderPlayer` read. Its two current callers are both renderers, so it is safe *today*. It is exactly
what sim code would reach for.

### 3.4 There is no guard. It is discipline.

I looked for one. There is **no `engine/OpenRA.Analyzers/` directory**, no Roslyn rule, no lint rule, and
no test in `engine/OpenRA.Test/` mentioning `RenderPlayer`. **The build will not stop this mistake.**
Protection is code review plus the audit in `DOCS/reference/architecture.md`.

Given that, this feature should carry its own guard: a unit test asserting the sim-side helper returns
identical results with `world.RenderPlayer` set to each player in turn and to `null`. That is cheap, it
pins the one property that matters, and it fails loudly if someone later "simplifies" the helper back
onto `FogObscures`.

### 3.5 Three further determinism rules for this specific build

1. **No `SharedRandom` for the per-unit stagger.** It is sync-safe, but every draw advances a shared
   stream, so a new consumer shifts the draw sequence for everything else — including `@stable` bot
   behaviour, which is the benchmark control. `StancePositioningExecutor` does draw from it
   (`:204`), but `BeliefStore` deliberately uses a fixed offset of 0 instead, and
   `DOCS/reference/influence-stack.md` records "zero RNG" as an invariant. **Use `self.ActorID % N`.**
2. **Never `[Sync]` a condition token.** `Detectable.cs:160-162` carries the PITFALL: *"never [Sync] a
   condition token — its value is an allocation handle counting how many conditions the actor has been
   granted, so a grant-count skew desyncs clients whose gameplay state agrees."* Two shipped desyncs
   have this shape. Sync the halt *state* (a bool or a tick stamp); never the token.
3. **No order is issued.** Because every input is synced sim state and the computation is
   RNG-free and RenderPlayer-free, all clients reach the same verdict independently. The halt is a
   trait-local condition grant, like `SupplyProvider.ServingCondition`. Round-tripping it through the
   order system would add latency and a second failure mode for no benefit.

---

## 4. Stop-and-resume — the shipping behaviour

### 4.1 The mechanism already exists, with its rationale written down

`Mobile` is a `PausableConditionalTrait<MobileInfo>` (`Mobile.cs:213`), and `Move.Tick`
(`Move.cs:167-168`):

```csharp
if (mobile.IsTraitDisabled || mobile.IsTraitPaused)
    return false;        // NOT complete — path and queued orders survive intact
```

`return false` means the activity is not finished. The path, the destination and everything queued
behind it are preserved. Revoke the condition and the unit walks on as if nothing happened.

**This is a shipped, documented pattern.** `SupplyProviderInfo.ServingCondition`
(`SupplyProvider.cs:104-114`) exists for exactly this and explains itself:

> *"Intended for a MOBILE provider, whose `Mobile.PauseOnCondition` should name it: the transport then
> HALTS for as long as there is anyone left to serve and resumes the order it was already carrying out
> the moment there is not, instead of driving past its customers. Pausing Mobile rather than cancelling
> the order is what makes 'and then continue moving' free — Move.Tick returns false while paused
> (Move.cs:168), leaving the activity intact rather than tearing it down."*

Same shape, same problem, already solved and in the game. It even carries a stance opt-out
(`ShouldHaltToServe`, disabled under `HoldPosition`) — a precedent worth copying verbatim.

**The consequence for concealment is that the feature is nearly free.** Pausing `Mobile` stops the unit;
stopping revokes `moving` (`GrantConditionOnMovement` tracks `IMove.CurrentMovementTypes`,
`GrantConditionOnMovement.cs:85-95`); `!moving` grants `prone` (`infantry.yaml:294`); `Detectable.ITick`
picks up both modifiers next tick and CV rises by 2. **The concealment is a consequence of halting, not
something the feature has to implement.** *(INFERRED: I did not verify in play that a paused `Mobile`
reports `CurrentMovementTypes == None`. It is the crux of the whole design and it is the first thing a
run must confirm — §10.1.)*

Contrast with cancelling the move: that destroys the path, loses everything queued behind it, and
forces a re-path on resume. It also cannot be a base for reroute (§8). **Do not cancel.**

### 4.2 What *clear* means — the resume rule

Five candidates were named. My rulings, with reasons:

| Candidate | Ruling |
|---|---|
| **Threat died** | **Yes, implicitly.** A dead actor leaves the enemy set; the predicate goes false. No special case. |
| **Threat moved away** | **Yes, implicitly** — this is just `margin` growing. The primary rule. |
| **Threat turned** | **Cannot be implemented, and must not be promised.** `Vision` is radial — `Range` and `MinRange` only (`Vision.cs`), stamped by `AddSource` over projected cells. **There are no vision cones in this engine.** A soldier cannot sneak past someone's back because backs do not exist. |
| **Threat lost sight of us** | **Explicitly NOT sufficient — this is the dangerous one.** |
| **Timeout** | **Yes, and mandatory.** |

**Primary rule:** resume when the predicate is false at the resume threshold, evaluated with the
**moving-CV** (§2.5), and the minimum hold has elapsed.

**Why "lost sight of us" must not count as clear.** The fog filter cuts both ways. A unit halts because
it sees a scout at 20 cells; the scout steps behind trees and we lose him; our known-enemy set empties;
the predicate goes false; we resume — straight into the scout, who never went anywhere. The feature
would reliably walk units into the exact threat it just avoided.

**Mitigation, and it is reuse rather than new machinery:** `BeliefStore` already keeps last-seen
contacts with confidence decay, is fog-legal by construction, and is computed for human players
(`InfluenceStack.Participates`). The halting contact should be remembered for a decay window after we
lose sight of it. A local per-unit memory of the halting contact (≈50 ticks) is the cheaper v1 if
plumbing `BeliefStore` into a per-unit trait proves awkward; the belief store is the right long-term
source and reaching for it first is correct.

**Why the timeout is mandatory.** Without it, one patient observer pins a unit forever and the player's
move order never completes. That is the precise failure that reads as *disobedience* rather than
instinct, and it is worse than never having built the feature. **Hard cap ≈ 300 ticks (≈18 s at default
speed), after which the unit resumes regardless and will not re-halt for that contact for a cooldown.**
A soldier who is late is fine; a soldier who never arrives is a bug report.

### 4.3 Instinct, not disobedience — the rules that make it feel right

Judged against the user's test — *would a player read this as the soldier being smart, or as the game
ignoring my order?*

1. **An explicit new order from the player always wins, immediately.** If the player issues a move while
   a unit is halted, the halt is dropped and suppressed for a cooldown. The player said go. This is the
   single most important rule and it is what keeps the behaviour on the "instinct" side.
2. **Bounded by construction** — minimum hold, hard timeout, per-contact cooldown (§2.5, §4.2).
3. **Gated by stance.** Ambush only. The player who chose Ambush asked for this; nobody else gets a
   surprise. This also repairs the backwards interaction the recon found (§7.1 G7): today Ambush is the
   one stance that *disables* the only automatic cover-seeking there is
   (`StancePositioningExecutor.FireStanceAllowsRepositioning`, `:587-590`). Ambush would finally do
   something protective.
4. **It must be visible.** A soldier stopping for no visible reason is indistinguishable from a bug.
   The recon's §8 proposed a white `!` mark and concluded *"the behaviour must exist before the
   indicator can"* — this is that behaviour, and `haltedForConcealment` is a clean, honest,
   already-latched state to render. **Ship the indicator with the behaviour, not after it.**

### 4.4 Other instincts that fall out of the same machinery

The user explicitly invited suggestions. Ordered by value-per-unit-of-work; all reuse the §2 predicate:

- **Don't fire if firing would reveal you.** Firing costs −2 CV for 12 ticks
  (`infantry.yaml:722-729`), which is ~6 cells. The same predicate, run with `CV − 2`, answers *"will
  shooting get me seen by someone who currently cannot see me?"* For an Ambush unit holding for a better
  shot this is directly in character, and it needs no new sensing at all. **Strongest suggestion here.**
- **Halt on the better cell.** When halting, prefer an adjacent cell with higher concealment.
  `ConcealmentScore` already exists (`CohesionMoveModifier.cs:330-346`) and is already used for
  Ambush-stance group moves. This is also the natural first step toward reroute (§8).
- **Stop *before* the line, not on it.** Already implicit: the trigger fires at
  `margin <= closing*H`, so the unit halts short of the reveal radius rather than on top of it. Worth
  stating because it is what makes the behaviour read as anticipation.

One I considered and reject for v1: auto-prone on nearby fire. Suppression already drives prone
(`infantry.yaml:294`), so it would duplicate an existing mechanism for no gain.

---

## 5. Cost

**Per evaluation, per unit:** `FindActorsInCircle(self.CenterPosition, 32c)` — the bound must be at
least the largest `Vision.Range` in the mod, as `WithSpottedDecoration` warns (`:35-39`) — then per
candidate: a relationship check, `CanBeViewedByPlayer` (a `ShouldHide` loop plus an O(1) flat-array read
per occupied cell, `Actor.cs:591-599` → `MapLayers.cs:574-579`), a band-ladder walk (≤10 iterations for
`^StandardVision`), and one `ShadowLayer` lookup. Call it **~20–40 simple operations per candidate
observer**, no allocation, no pathfinding, no RNG.

**Scaling.** With 50 ambush units and ~8 known enemies within 32 cells of each:
50 × 8 × ~30 ≈ **12,000 operations per evaluation round**. Staggered over `N = 5` ticks, ≈2,400
operations per tick. That is negligible next to an OpenRA tick.

The 2026-07-22 design doc reached the same conclusion independently and put it more bluntly:
*"per-unit strength scans are not meaningfully costly at ambush cadence — the fear that drove 'use
map-layers instead' is largely unfounded"* (`WORKSPACE/plans/260722_ambush_undetected_design.md` §0.2).

**Four cost controls, in order of value:**

1. **Shrink the active set.** Only *moving* units in Ambush stance evaluate the halt predicate. A halted
   unit runs the cheaper resume test. This is the big one — the working set is far smaller than 50.
2. **Stagger by `ActorID % N`** (not `SharedRandom` — §3.5). Spreads the round over `N` ticks.
3. **Couple `N` to `H`, and treat it as a correctness constraint, not a tuning knob.** `H >= N +
   cellTransition + conditionLatency` (§2.3). Raising `N` to save cost silently shortens the effective
   lookahead until the unit stops too late to matter. **This is why the evaluation rate cannot simply be
   turned down.**
4. **Coarse pre-filter via an existing field.** `SightingThreatLayer` already maintains a per-player,
   per-cell, fog-legal enemy-intensity field, refreshed every 25 ticks, queryable as
   `ThreatIntensity(player, cell)` (`SightingThreatLayer.cs:287`). If it reads zero at our cell there is
   no believed enemy nearby and the whole scan can be skipped.

### 5.1 What cannot be reused, and why that is the right call

I checked the influence stack for something closer, because reusing beats building here:

| Layer | Stores | Verdict |
|---|---|---|
| `BeliefStore` | remembered enemy contacts, fog-legal, confidence decay | **Reuse** — for contact memory (§4.2). Solves "enemies we know about" already. |
| `DangerFieldLayer` | per-cell threat from believed contacts | **No** — stamped from **weapon** range, not **vision** range. Different quantity. |
| `ControlField` | territory score, 2×2 coarse grid | **No** — wrong quantity, and the coarse grid cannot resolve a 3-cell band edge. |
| `SightingThreatLayer` | per-cell enemy sighting intensity | **Pre-filter only** (§5.4) — it is "where enemies threaten me from", not "who can see me", and at 25-tick refresh it is far too stale to be the predicate. |

**No reverse-vision field exists** — nothing anywhere answers "how much enemy vision strength points at
this cell". Building one is *not* the recommendation: a per-cell field would cost more than the direct
scan (§5 shows the scan is already cheap), it would be stale at the 25-tick cadence the other layers
use, and staleness is precisely what a *predictive* feature cannot tolerate. **Scan directly; reuse
`BeliefStore` for memory and `SightingThreatLayer` as a pre-filter.**

---

## 6. Where this lives

**`StancePositioningExecutor` is the wrong home**, for three independent reasons, any one of which is
disqualifying:

1. **It is idle-only by design** — rule S5, *"Evaluate only in TickIdle with CurrentActivity == null"*
   (`:20`, `:277`). Our unit is by definition *running*. It would never tick.
2. **It runs every 30 ticks** (`EvaluateCooldown = 30`, `:73`) — twice our whole horizon.
3. **It explicitly refuses to manage Ambush units** (`FireStanceAllowsRepositioning`, `:587-590`), and
   the comment above it says a silent widening *"would strand the trait off"*. Ambush is our only
   audience.

It is a low-frequency planner for idle units. This is a per-tick sensor for moving ones.

**Recommended home:** a new conditional trait on the unit — provisionally `ConcealmentHalt` — modelled
directly on `SupplyProvider`'s serving-halt: it evaluates the predicate, grants a condition named by
`Mobile.PauseOnCondition`, and exposes the halt state for the indicator. The predicate itself goes in a
**static, actor-free helper class** so it is unit-testable without a live world — this codebase already
does exactly that for `AmbushTactics.ShouldHaltBeforeContact` and
`StancePositioningExecutor.FireStanceAllowsRepositioning`, both extracted static and pinned by tests.

**Precedent worth knowing:** halt-before-contact already exists
(`Activities/Move/AttackMoveActivity.cs`, the `ShouldHaltBeforeContact` branch) — an Ambush unit that is
attack-moving ends its march and drops to idle rather than engaging. It is close in spirit but not
reusable here: it is gated behind `AmbushTacticsCondition`, granted only per-unit by
`LaneAmbushBotModule`, so **humans never receive it**; it triggers on *having a target*, not on
visibility; and its own comment records the ruling that *"a plain Move is always obeyed; only
attack-move / bot auto-move can halt."* Our feature deliberately reopens that question for plain moves,
which is why §4.3's player-override rule carries so much weight.

---

## 7. Build order

| Stage | Work | Verifies |
|---|---|---|
| 1 | Sim-safe predicate helper + unit tests, including the RenderPlayer-invariance test (§3.4) and the `CV + 1` band arithmetic (§2.1) | determinism, off-by-one |
| 2 | `ConcealmentHalt` trait: halt only, no resume rule beyond the timeout; wired to `Mobile.PauseOnCondition`; default OFF | that pausing Mobile conceals (§10.1) |
| 3 | Resume rule: moving-CV evaluation, hysteresis, contact memory | no oscillation (§2.5) |
| 4 | Indicator (`haltedForConcealment`) | legibility (§4.3.4) |
| 5 | Re-point `WithSpottedDecoration` at the shared helper | kills the duplicate |

Stages 1–2 are independently valuable and each is small.

---

## 8. The reroute seam

Reroute is the same *decision* with a different *execution*. The seam costs almost nothing if taken now
and is expensive to retrofit:

1. **The predicate returns a verdict, not a bool.** `enum ConcealmentVerdict { Continue, Halt, Divert }`
   from the outset, with v1 mapping `Divert → Halt`. If it ships as `bool ShouldHalt`, every caller
   encodes the two-state assumption and stage 2 becomes a rewrite.
2. **Pause, never cancel** (§4.1). A rerouting unit needs its original destination and its queued orders
   intact — reroute is "go around and carry on", which is only expressible if the order survived. **This
   single decision is what keeps reroute layerable**, and it is also the right call for v1 on its own
   merits. It is the seam.
3. **Keep the offending contact identified**, not just a boolean. Reroute needs to know *which* observer
   to go around and from which bearing; the memory in §4.2 already carries it.
4. **`ConcealmentScore` is already the cell scorer** (`CohesionMoveModifier.cs:330-346`). Stage 4.4's
   "halt on the better cell" is a one-cell reroute; extending to a detour is a change of search radius,
   not a new subsystem.

Nothing in stop-and-resume paints us into a corner provided (1) and (2) hold.

---

## 9. The single thing most likely to make this infeasible

Not determinism (solved, §3), not cost (negligible, §5), not the mechanism (already shipped, §4.1).

**It is that stopping is the wrong response to the situation that most often triggers it.**

The reveal radius shrinks from 25 to 19 cells when a rifleman stops (§1.3). That is a real 6 cells. But
consider the case the trigger actually fires in: an observer walking toward us at 22 cells. We halt. We
are now unseen — for as long as he stays outside 19 cells. **He is walking. He arrives at 19 cells a few
seconds later and sees us anyway** — and now we are a stationary soldier who has thrown away the head
start and the option of being somewhere else.

Stopping only wins against an observer who does **not** close: one transiting past, one scanning from a
fixed post, one who turns around for reasons of his own. Against a closer it buys seconds and costs
position. And "an enemy is about to see me" is a condition most often produced by exactly the observers
who *are* closing. **The feature fires most often in the case where it helps least**, and the player
watches his soldier stop for no visible benefit, get spotted anyway, and die stationary. That reads as
the game malfunctioning, which is the precise opposite of the instinct brief.

There is no engineering fix, because the mechanic is correct — 6 cells is 6 cells. There are two
honest responses, and they are not exclusive:

- **Narrow the v1 trigger to the case it actually solves.** Require the observer to be non-closing, or
  closing below a threshold — halt for a *passing* or *stationary* observer, never for a charging one.
  This makes v1 rarer and correct instead of frequent and wrong. It also degrades gracefully: against a
  closer the unit simply keeps its current behaviour, which is what it does today.
- **Accept that reroute is the real feature and stop-and-resume is stage 1 of it.** Against a closing
  observer the action that helps is moving *away from his approach*, not stopping. This is consistent
  with the staged plan; it just means stage 1 should not be oversold.

**Recommendation: build stop-and-resume as specified, but ship the trigger narrowed to non-closing
observers.** It will fire less often than the brief imagines, and every time it fires it will be right.
That is the version a player reads as instinct.

---

## 10. What I did not verify

- **The game was never launched. No test, no validator, no build.** Nothing here is observed behaviour.
- **§10.1 — the load-bearing unknown: that a paused `Mobile` reports `CurrentMovementTypes == None`,
  and therefore revokes `moving` and grants `prone`.** The entire "stopping conceals for free" claim
  (§4.1) rests on it. It is inferred from `GrantConditionOnMovement.cs:85-95` reading
  `movement.CurrentMovementTypes` plus `Move.Tick` returning early while paused — I did not confirm the
  transition actually fires. **Verify this before writing anything else.** If it does not hold, the
  trait must revoke `moving` itself and the design gets meaningfully worse.
- **Whether pausing mid-cell is clean.** `Move.Tick`'s paused check sits in the parent activity
  (`Move.cs:167`), so a `MoveFirstHalf`/`MoveSecondHalf` child already in flight presumably completes
  the cell first. I read this as "halts at the next cell boundary" but did not trace the child tick
  order to confirm, and it sets the feature's true reaction latency.
- **The §1.2 range table and the §1.3 CV table are arithmetic, not measurement.** They are carried from
  the 2026-08-19 recon, which marks them `[NEEDS RUN]`. The one in-game data point that exists
  (commit `6cb66e28`) is consistent with them.
- **The `VisionCovers` off-by-one (§2.1) is inferred** from comparing `:142` against
  `MapLayers.cs:579`. I did not observe a wrong badge.
- **I did not measure anything.** The operation counts in §5 are estimates from reading call chains,
  not profiles. No benchmark exists in `engine/OpenRA.Test/` for these paths.
- **I did not audit which units besides infantry would qualify.** Vehicles carry a bare `Detectable:`
  with no movement modifier (`vehicles.yaml:66`), so **stopping conceals a vehicle by nothing at all** —
  the feature is infantry-only in effect, and applying it to vehicles would halt them for zero benefit.
  That gate needs to be explicit in the YAML, and I have not checked every vehicle for a per-actor
  override.
- **Multiple simultaneous observers** are treated as independent (halt if any one trips). Overlapping
  vision from several observers does not stack strength in `AddSource` — it takes the max — so this is
  probably right, but I did not verify the multi-source resolution path (`MapLayers.cs:243-289`).
