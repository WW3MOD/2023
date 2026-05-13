# Problem statement

> Before designing what comes next, we need to be specific about three things: **what we want the cohesion system to actually do**, **what we have today**, and **the gap between them**. No solutions in this doc — just diagnosis. The gaps named here drive `03_design_directions.md`.
>
> Read this alongside `01_cohesion_as_built.md` (the machinery) and `archive/260512_intent_aware_movement.md` (the original intent). This file names the failure modes.

---

## 1. What we want — observable behaviors

These are behaviors a player should be able to observe in a normal river-zeta or woodland-warfare skirmish, without any explanation from the dev. None of them are aspirational; they are the floor.

**A. Click in a tree cluster → squad spreads through trunks.**
Each unit lands in a passable cell adjacent to at least one trunk. No unit ends up in open ground between trees when in-cover cells are available. Spacing follows the squad's `CohesionMode` (Tight/Loose/Spread).

**B. Click near a tree cluster → squad reaches the cluster.**
A click 2–4 cells off a cluster edge should land the squad **in the cluster**, not on the open-ground line at the click. The current `EdgeLine` anchor is the cover centroid; this is already implemented, but the *visual* of a perpendicular line still reads as a directional formation rather than "behind trees".

**C. Click far across the map into cover → squad marches there and forms there.**
For a long-distance Approach order, units don't pile up next to the squad's starting position. They actually travel to the destination region and arrange around the cover at the destination. (This was the 260513 Approach fix.)

**D. Click in open ground → squad takes a directional box.**
This is the legacy behavior. It should still work cleanly — most player orders are open-ground waypoints (move to a road, move to a base, move to a contested cell with no nearby trees). Open formation should look intentional, not like a fallback. Spacing follows `CohesionMode`.

**E. Click on a garrisonable building → squad enters as occupants.**
Today a click on a garrisonable building goes through whatever cover signal that building emits and the intent classifier picks an outcome. The desired behavior is `enter the building`, not "spread around it". Garrison logic exists but is not integrated with the order-level intent dispatch.

**F. Click at a forest edge with the squad on the open side → defensive line at the tree edge facing out.**
This is `EdgeLine`'s job. With the CoverScore-aware slot bidding it now does this *mostly* correctly — but the resulting line is still nearly straight (max 2 cells of deviation per slot). On a sparse tree line that has gaps, the formation looks like 3 units behind trunks and 1 unit standing in the gap. The user expects every unit to be behind a trunk if there are enough trunks, even if it bends the line significantly.

**G. Units in cover stay in cover after engaging an enemy.**
The leash (`CohesionSlotMemory`) walks idle units back to their slot. Today the leash only fires when the unit is `IsIdle` or notified-blocking. Units engaging an enemy from a slightly-displaced cell never return — they drift forward to the line of contact and stay there. The cover-aware formation degrades over a single engagement.

**H. Player sees what the cohesion system is doing.**
There is no visualization today. The player issues a grouped move, units walk to somewhere, and the player has to infer from final positions whether "intelligent cover" actually fired or whether it's just legacy box. Without feedback, the player cannot calibrate their expectations, learn the system, or trust it. **This is the single biggest item on this list.** It's why the 260513 playtest described the (already cover-aware!) behavior as "feels like the old broken cohesion" — visually the EdgeLine and box were indistinguishable.

**I. Bots inherit the same behavior for free.**
This already works — `CohesionMoveModifier` sits at the order layer, so any bot module that issues a grouped order gets cover-aware dispatch. Nothing to fix; called out so we don't accidentally break it.

That's the list. Nine observable behaviors. A through D are functional (post-260513-fix); E through I have visible gaps.

---

## 2. What we have today — the existing pieces

Five layers, in order from the cell signal up to the user:

**Layer 1 — `Map.DensityLayer` cover signal.** Per-cell `byte` populated from `Building.Density` (single trait implementor). Trees contribute 10 per trunk cell; rocks 50; sandbags/walls/buildings could contribute but mostly don't. Cached in each map's `shadows.bin`. Works correctly — verified by probe on river-zeta.

**Layer 2 — `IModifyGroupOrder` dispatch.** `UnitOrders.ProcessOrder` invokes registered modifiers per-subject for every grouped order. Single entry point, deterministic, sequential. Works correctly — verified by 12/12 probes reaching the modifier.

**Layer 3 — `CohesionMoveModifier` classifier + bidders.** Four intents (`Open`, `SpreadInside`, `EdgeLine`, `Approach`), each with its own slot bidder. CoverScore-aware bidding for SpreadInside / EdgeLine / Approach via a per-slot neighborhood search. YAML-tunable thresholds. Works as designed *for the four behaviors A–D*; does not address E–H.

**Layer 4 — `CohesionSlotMemory` leash.** Per-actor trait that remembers the assigned slot for 750 ticks (~30s) and walks the actor back when idle or notified-blocking. Works correctly for the narrow "actor got bumped while standing on its slot" case. Does not address the broader "actor drifted out during engagement" failure (G).

**Layer 5 — `CohesionMode` Tight/Loose/Spread toggle.** Hotkey-bound, set on `AutoTarget`, read by the modifier for per-mode spacing. Works locally; `SetCohesion` order has no `IResolveOrder` handler so it doesn't roundtrip the network — fine for single-player, broken for multiplayer.

What we *don't* have at all:

- **No visualization.** No preview, no slot lines, no voice cue, no overlay.
- **No per-stance leash behavior.** The leash is the same regardless of `EngagementStance`.
- **No per-unit-type role profile.** AT / MG / sniper / rifle all interchangeable.
- **No garrison-aware intent classification.**
- **No modular cover signal API.** `Map.DensityLayer` is the only signal; you can't add a wall-cover signal or a building-cover signal as separate weighted inputs.
- **No backward-rewriting waypoint chains.** Shift-click waypoints resolve naively.
- **No attack semantics integration.** Attack-move on contact does not snap to cover; attack-click on an enemy does not approach via cover.

---

## 3. The gap — concrete root causes

Each one of these traces to one or more of the failures in §1.

### 3.1 The line shape reads as a box even when it's cover-aware

This is the dominant complaint. `EdgeLine` and the perpendicular-at-boundary part of `Approach` produce a line of N slots. With the post-fix CoverScore-aware bidder, each slot can deviate up to 2 cells from the geometric line to find better cover — but the *overall shape* is still recognizably a line. To a player who doesn't know the intent classifier exists, this is indistinguishable from a directional box formation. The cover-aware claim is invisible.

Two contributing factors:

- **The line geometry is constrained to ±2 cells per slot.** `LineSlotSearchRadius = 2` keeps slots close to the ideal line. A larger radius would let units snap onto trunks even when those trunks are 3–4 cells off the geometric line — at the cost of "line" no longer reading as "line".
- **There's no signal to the player that this is the cover-aware formation.** No slot markers, no voice cue, no preview. The player infers intent from final positions, and final positions in a sparse forest can look identical to a box.

Direct failures: F (line at tree edge looks like a box when trunks are spaced wider than the search radius), H (no visualization).

### 3.2 The leash is gentle and unaware of engagement context

`CohesionSlotMemory.TryReturnToSlot` fires only on `INotifyIdle.TickIdle` or `INotifyBlockingMove`. A unit that has just engaged an enemy is **not idle** — its activity stack contains `AttackBase`'s aiming/firing activities, or a queued `Move` toward the target. The leash never fires until the engagement ends *and* the unit returns to idle, by which time the unit may be in open ground 4 cells forward of the cover slot.

Worse: there is no per-stance variation. The original plan's `Defensive / Ambush / Hold` budgets — "stance Defensive allows 1–2 cell forward slack, snap back; Hold zero slack; Ambush larger" — are not implemented. The leash is binary: either the unit is idle and we walk it back, or it isn't and we don't.

Direct failure: G (units drift out of cover after engagement).

### 3.3 The intent dispatcher knows nothing about garrisonable buildings

`ClassifyIntent` reads `DensityLayer` and decides among four geometric intents. None of them consider "is this click on a building the squad could enter?". The garrison interaction today is a separate UI path: right-click on an enemy-held garrisonable building issues a `EnterGarrison` order via the unit's own order generator. A grouped move targeted at a friendly garrisonable building falls through to whatever density that building contributes — usually nothing — and ends up as `Open` or `SpreadInside` depending on nearby trees.

The original plan listed this as a v1 intent. It was deferred to keep the first cut narrow.

Direct failure: E (garrison click doesn't enter as occupants).

### 3.4 The cover signal is structurally flat — no modularity, no weights

`Map.DensityLayer` is a single `byte` per cell summing all `Building.Density` contributions. There is no way for the modifier to say "this cell has 10 from a tree + 20 from a sandbag wall, weight the sandbag higher". The original plan's `CoverField(cell) = Σ wᵢ · Signalᵢ(cell)` modular formulation does not exist. Adding `Signal_Walls`, `Signal_Buildings`, `Signal_RidgeLOS` requires either extending the `IDensityInfo` interface to multiple trait types (commented-out `BlocksSight: IDensityInfo` is one indicator the engine team has thought about this) or building a parallel signal layer.

This is not currently *breaking* anything — the existing tree signal is enough to demonstrate the system. But it's a structural ceiling for adding cover types beyond trees. A wall isn't a tree-equivalent: it has *direction* (blocks LOS along one axis only), and the bidder ought to know which side of the wall is the cover-providing side.

Direct failure: scope ceiling for future signal work; no immediate player-visible issue.

### 3.5 Slot assignment by ActorID ignores starting position

`Array.Sort(validActors, (a, b) => a.ActorID.CompareTo(b.ActorID))` then `slot[idx]` for subject at `idx`. The unit closest to a given slot doesn't necessarily get assigned to it — the unit with the lowest ID in the leftmost slot position gets the leftmost slot, even if it spawned on the right side of the squad. Result: units cross each other on their way to slots, paths interleave, the formation looks chaotic during travel and only "clicks into place" at the end (sometimes).

This is more visible with larger squads (8+) and longer travel distances. For the 4-unit smoke tests it rarely manifests. In actual play on river-zeta with a squad of 6–10 infantry, the criss-crossing is noticeable.

Direct failure: visual chaos during traversal (not in the §1 list but felt during playtest).

### 3.6 No visualization means no learning loop

§1.H is large enough to deserve its own root cause. Without preview / slot lines / voice cues:

- The player cannot anticipate what a click will produce before clicking.
- The player cannot tell whether the cover-aware formation actually fired or whether it was Open box.
- The player cannot calibrate their click style ("oh, clicking 3 cells off the cluster gets me an EdgeLine — I want SpreadInside, so I'll click 1 cell deeper").
- Tuning and debugging require parsing `debug.log` — possible for the developer, opaque to the player.

This was the proximate cause of the "feels like old box" feedback. The system was actually working (post-fix); the player just couldn't see it work.

Direct failure: H (player sees nothing about intent).

### 3.7 The diagnostic log line is on in shipping code

Currently `CohesionMoveModifier.cs:614` does `Log.Write("debug", ...)` for every grouped order. This is fine during diagnosis but pollutes `debug.log` in normal play. It's a known TODO — strip when feel is dialed — but it's a release-blocker if left in.

Direct failure: not a player-facing issue; a release-readiness issue.

---

## 4. Non-negotiables and non-goals

### Must keep working

- **The single-trait architecture.** `CohesionMoveModifier` is one trait at one hook point. Anything we add should either extend that trait or attach to it; we should not fork into a parallel order pipeline.
- **Bot integration is automatic.** Bot modules that issue grouped orders inherit cover-aware behavior. Don't break this.
- **The single-unit short-circuit.** Single-actor orders bypass the modifier entirely (no `GroupedActors` array). Snipers and other "I want this unit on exactly this cell" cases work today because the engine's order path is unmodified.
- **Per-map cache invalidation.** When `shadows.bin` formula changes, every map regens cleanly via `--regen-shadows`. Don't break the cache mechanism.
- **Determinism.** Slot assignment, classifier, bidder must be functionally pure on `(map, click, group, n, mode, mobile)`. No `World.LocalRandom` in modifier code. (Currently clean — preserve.)
- **The autotest harness.** Scenarios under `tools/autotest/scenarios/test-cohesion-*/` must keep passing. New behaviors get new scenarios; we don't break the existing ones.
- **CohesionMode hotkeys.** Ctrl+Alt+1/2/3 → Tight/Loose/Spread. The toggle works locally today; multiplayer wiring is missing but the local UX must not regress.

### Out of scope

- **Pixel-perfect formation alignment.** "Every unit is exactly behind a trunk" is the goal *when achievable*; not "the formation is visually symmetrical to the pixel". The bidder is allowed to bend the line.
- **AI-specific cohesion tuning.** Bots use the same dispatcher as players. Knobs that affect both are fine; AI-only overrides are not in scope.
- **Custom-per-player cohesion knobs.** No "Player A likes tight, Player B likes spread" persistent profile beyond the per-session `CohesionMode`.
- **ML-based slot assignment.** Heuristic only.
- **Replay-determinism beyond the engine's existing guarantee.** Orders are deterministic; modifier output must be a function of order input + map state.
- **Multiplayer-grade sync for `SetCohesion`.** This is a Phase 1 wiring fix listed in the original plan; it can ship later — it doesn't block v1 single-player feel work.
- **Per-tier-2 formation when squad is over-supplied.** The original plan's "build a second tier behind first tier with staggered slots" is deferred unless playtest demands it.

---

## 5. Success criteria

The system ships when these are observably true. All checkable via the autotest harness, the diagnostic log line, or a short scripted playtest.

| ID | Test | How we know |
|----|------|-------------|
| **C-A** | Click in dense cluster → SpreadInside | Existing `test-cohesion-cover-bid` passes; `[Cohesion]` log shows `intent=SpreadInside` for the click; ≥3 of 4 units land chebyshev ≤ 1 from a trunk. |
| **C-B** | Click 2–4 cells off cluster edge → SpreadInside or EdgeLine, all units near trunks | Existing `test-cohesion-cover-redirect` passes (all 4 adjacent to a trunk); `[Cohesion]` log shows non-Open intent. |
| **C-C** | Click far across map → Approach, slots near destination | `test-cohesion-river-zeta-actual` probe 9/11/12 — slots within 5 cells of click, not within 5 cells of group. |
| **C-D** | Click in open ground → Open box, units form predictable directional formation | `[Cohesion]` log shows `intent=Open` and slot positions form a recognizable grid centered on click. Currently works. |
| **C-E** | Click on garrisonable building → squad enters as occupants | New autotest needed. Squad of 4 infantry, click on `barr` or equivalent, expect all 4 to garrison or queue-to-garrison. **Not yet implemented.** |
| **C-F** | Line at tree edge → each unit adjacent to a trunk (when trunks exist within search radius) | New autotest. Place 5 trunks in a row, click 3 cells west, expect 4 of 4 units chebyshev ≤ 1 from a trunk. **Currently fails when trunks are spaced > LineSlotSearchRadius.** |
| **C-G** | After engagement, units return to cover | New autotest. Squad in cluster, spawn enemy at edge, wait until enemy dies, assert squad cells unchanged from pre-engagement. **Currently fails — leash is idle-only.** |
| **C-H** | Player can see intent classification at click time | Visualization shipped — slot ghost markers under cursor, voice cue on commit, slot lines from unit to target. **Not implemented.** |
| **C-I** | Stripped diagnostic log line | `grep "Log.Write.*Cohesion" engine/` returns nothing in release build. Currently on. |

Nine criteria, mapped to the nine observable behaviors plus the release-readiness item. A–D pass today; E–I do not.

---

## 6. What this doc explicitly leaves open

- **Visualization style.** Hover ghost vs slot markers vs path lines vs voice — `03_design_directions.md` proposes; user picks.
- **Per-stance leash budget knobs.** What's the forward-step budget for Defensive vs Ambush vs Hold? Deferred to design.
- **Per-unit-type role profiles.** Which unit types get front-arc vs overwatch vs flank preference? Deferred.
- **Modular cover signal API design.** Whether to extend `IDensityInfo` or build a parallel layer. Deferred.
- **Slot-assignment policy when ActorID ordering produces criss-crossing.** Hungarian matching, nearest-slot greedy, or something else. Deferred.
- **Attack semantics integration.** Whether attack-move on contact should snap to cover; whether attack-click on enemy should approach via cover. Deferred to a later doc — these are user-experience decisions, not just code.

These are real questions. Holding them until `03` so this doc stays scope-limited to diagnosis.
