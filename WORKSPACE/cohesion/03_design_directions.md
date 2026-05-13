# Design directions — proposed paths forward

> Speculative design doc. For each gap named in `02_problem_statement.md`, this doc proposes one or more concrete directions, names the trade-offs, and flags the open questions. Treat each section as a starting point for discussion — not a binding spec. Push back. The goal is to converge on a shape worth building before we start writing it.
>
> Read `01_cohesion_as_built.md` for the machinery and `02_problem_statement.md` for the failure modes this doc tries to dissolve.

---

## 1. The discipline this doc applies

Three rules so the design doesn't sprawl:

1. **Every proposal must trace to a specific gap in `02_problem_statement.md`.** If we can't point to a behavior that fails today, we shouldn't build the thing.
2. **Single-trait architecture is preserved.** Anything new either extends `CohesionMoveModifier`, attaches a sibling per-actor trait (like `CohesionSlotMemory`), or hooks into an existing engine system (e.g., `INotifyOrderIssued`). No parallel order pipelines.
3. **Bots inherit for free.** Anything we add must work the same way whether the order came from the player or a bot — except for the visualization layer (§3), which is rendered for the local player only.

One legitimate counter-argument we should keep in mind: **the single biggest improvement is visualization**, not new bidder logic. The 260513 playtest described the (working!) cover-aware behavior as broken because the player couldn't see it work. Section §3 is therefore deliberately listed first.

---

## 2. The layer cake — what exists, what changes

```
┌─────────────────────────────────────────────────────────────────┐
│  Layer 6 — Visualization (NEW)                                  │
│            Preview ghost, slot lines, voice cue, intent badge   │
├─────────────────────────────────────────────────────────────────┤
│  Layer 5 — Leash (CohesionSlotMemory — exists, needs depth)     │
│            Per-stance budget, engagement-aware return           │
├─────────────────────────────────────────────────────────────────┤
│  Layer 4 — Slot bidder (CohesionMoveModifier — exists)          │
│            Per-intent, CoverScore-aware. NEW: per-unit role     │
├─────────────────────────────────────────────────────────────────┤
│  Layer 3 — Intent classifier (CohesionMoveModifier — exists)    │
│            4 intents. NEW: garrison intent, attack-on-contact    │
├─────────────────────────────────────────────────────────────────┤
│  Layer 2 — Cover signal (Map.DensityLayer — exists)             │
│            byte per cell. NEW: modular ICoverSignal aggregation │
├─────────────────────────────────────────────────────────────────┤
│  Layer 1 — Order pipeline (UnitOrders.cs — exists, unchanged)   │
└─────────────────────────────────────────────────────────────────┘
```

Layers 1–4 exist; layers 5–6 are partial (leash exists but shallow) or absent (visualization). Below, each direction is mapped to the layer it lives in.

---

## 3. Visualization (Layer 6 — new)

> Target gaps: 3.6 (no learning loop), 3.1 (line shape reads as box). **This is the single highest-value direction in the doc.** Everything else makes the system *better*; this makes the existing system *visible*.

### 3.1 The minimum viable surface

Three things, smallest to largest:

**a. Voice cue on commit.** When the modifier resolves an intent, play a short voice clip on `subject[0]` keyed on the intent name. "Taking cover" for SpreadInside; "Forming line" for EdgeLine; "Moving up" for Approach; silence for Open (or a generic "moving"). Cooldown 1.5s per group so a flurry of orders doesn't stack clips.

Hook: `INotifyOrderIssued` on the actor or a new world-level callback from `CohesionMoveModifier.ModifyGroupOrder`. Voice infrastructure exists (`VoiceExts.cs:19` → `Actor.PlayVoice("phrase")`).

Cost: one new YAML voice-set entry per intent name; ~20 lines of wiring; some art asset.

**b. Per-unit slot lines.** A faint line from each unit's current cell to its assigned slot cell, fading out 1.5s after order issue. Same mechanism as the existing path-line rendering (`Effects/SpriteAnnotation.cs` or whatever the order-feedback path uses today).

Hook: `INotifyOrderIssued` triggers a one-shot effect spawn per actor. Lifetime managed by the effect itself.

Cost: ~50 lines of effect glue; needs a sprite for the line endpoint (the slot marker).

**c. Hover preview.** While the player has a selection and the cursor is over a map cell, run the classifier + bidder for a hypothetical click at the hovered cell and show a faint ghost of where each unit *would* go. Throttled to 4–8 Hz (the original plan suggests 8 Hz).

Hook: `UnitOrderGenerator` or a custom widget. Has to run a full `ClassifyIntent + LayCoverAwareLine` cycle every preview tick — cheap, but spammy at high frame rate without throttling.

Cost: bigger — ~200 lines, plus the preview rendering. Higher polish bar. Defer until (a) and (b) ship.

### 3.2 Order of work

1. Voice cue first. Cheapest, most legible improvement. Player immediately understands "the system thinks this is X".
2. Slot lines second. Tells the player *where each unit is going* — solves the "is this cover-aware or box?" ambiguity directly.
3. Hover preview last. High polish; deferred until 1+2 ship and we have feedback that (1+2) isn't enough.

### 3.3 What this doesn't address

Visualization makes the system *legible*, not *better*. If the bidder is making poor slot picks (§5 below), visualization just makes the bad picks visible. The bidder fixes have to happen too — visualization is necessary, not sufficient.

### 3.4 Open questions

- **Per-player vs world-level visualization?** Slot lines for "my units" only or for all units the player can see (allies, fog-permitting)? Probably my-units-only for slot lines; voice cues only on own orders.
- **Should bots also play the voice cue?** Probably not — would be noisy. Bots fire orders constantly.
- **Visualization for the leash?** When `CohesionSlotMemory` walks a unit back, should there be a faint "returning" indicator? Probably yes, faint, distinguishable from the order-commit slot line.

---

## 4. Engagement-aware leash (Layer 5 — exists, needs depth)

> Target gap: 3.2 (leash is gentle and unaware of engagement context).

### 4.1 The current shape

`CohesionSlotMemory.TryReturnToSlot` fires on `INotifyIdle.TickIdle` or `INotifyBlockingMove`. Idle = no pending activity. A unit in combat is not idle — it has aiming, firing, or pursuing activities queued. The leash never fires during engagement.

### 4.2 The proposed shape — per-stance forward-step budget

Each unit's `AutoTarget` carries an `EngagementStance` (`Defensive` / `Aggressive` / `Hunt` etc. — exact set is engine-defined). Cohesion adds a per-stance forward-step budget:

| Stance | Forward-step budget | Behavior |
|--------|--------------------|--------|
| `HoldFire` / `Hold` | 0 cells | Never leave the slot. Engage from slot only. |
| `Defensive` (default) | 1–2 cells | May pursue / fire from up to 2 cells off slot; snap back when idle. |
| `Aggressive` / `Hunt` | unlimited | Same as today — leash effectively off during engagement. |

Implementation sketch:

1. `CohesionSlotMemory` tracks `CPos slotCell` and `int stepsForward` (computed from `self.Location - slotCell`).
2. Hook `INotifyAttack` or `Mobile`'s move-completed callback (TBD): when a unit's location changes from the slot, increment `stepsForward`.
3. Check `stepsForward <= info.ForwardBudget[stance]`. If exceeded, queue an immediate `Move` back to slot — don't wait for idle.
4. When stance changes (`SetStance` order), re-evaluate budget and pull units back if newly out-of-budget.

### 4.3 What this doesn't address

A unit that's been issued an explicit `Move` by the player overrides the slot. We don't want the leash to fight the player. Solution: clearing the slot on a non-cohesion `Move` order. Hook: when `ModifyGroupOrder` is called we re-`Assign` anyway; when an individual `Move` happens, we just clear `hasSlot`.

Edge case: the player issues a grouped `Move` that the modifier rewrites into per-unit slots, then individually moves one unit elsewhere. The individual move clears that one unit's slot; the rest stay leashed. Correct behavior.

### 4.4 Open questions

- **Should the leash fire while the unit is mid-engagement?** Queueing a `Move` back to slot while the unit is in the middle of `AttackBase`'s firing activity is rude — it interrupts the shot. Probably the leash should only fire *after* the current activity completes naturally, by hooking the post-activity tick rather than `TickIdle`.
- **Stance-change retroactive pull?** If a unit was on `Defensive` and stepped 2 cells out (within budget), then user sets `Hold` (budget 0), do we yank the unit back immediately? Yes — that's the point of switching to Hold. But it might be jarring if the unit was mid-attack.
- **Per-actor budget overrides in YAML?** Sniper might want budget 0 even in Defensive. Probably yes — add to `CohesionSlotMemoryInfo`.

---

## 5. Better slot picking — per-unit role + tighter cover snap (Layer 4 — exists, refine)

> Target gaps: 3.1 (line shape reads as box), 3.5 (ActorID ordering criss-crosses).

### 5.1 Two sub-directions, both modest

**a. Larger search radius with directional bias.** Bump `LineSlotSearchRadius` from 2 to 3 (7×7 window), but penalize deviation **along the line** more than deviation **perpendicular to the line**. The line shape stays — the slots can dip closer to/farther from cover but don't migrate sideways into adjacent slot positions.

Implementation: `PickCoverSlotNear` already takes a `(backX, backY)` fallback direction. Add the perpendicular axis explicitly, and weight perpendicular deviation 2× the parallel deviation in the score. Cheap — same algorithm, different penalty.

**b. Slot-to-unit assignment by Hungarian-ish matching.** Replace `Array.Sort(validActors, ActorID)` + `slot[idx]` with: compute all slots first, then assign units to slots minimizing total travel distance. For N ≤ 12 a greedy nearest-slot-first approximation is enough — full Hungarian is overkill.

Hook: change `ModifyGroupOrder` to compute the slot array once (cached on the order? — no, recomputed per call but cheap), then assign slots based on subject's distance to each slot, picking the nearest available.

Cost: ~30 lines; deterministic if we tiebreak on ActorID.

### 5.2 Per-unit role profiles — defer

The original plan's "AT prefers front-arc with LOS to vehicle approach, sniper prefers overwatch/depth" requires:

- A trait declaring the unit's preferred profile.
- Slot tagging by profile (which slots are "front-arc", "overwatch", "flank").
- Profile-aware assignment.

This is a sizeable system. The current heuristic (sort by ActorID, take slot at index) is bad; the proposed greedy-nearest is better. **Role-aware assignment is a third tier of polish** — skip until §5.1b ships and we see whether the formation is "good enough" without it.

### 5.3 Open questions

- **Cost of running full slot-assignment per subject?** Today each per-subject `ModifyGroupOrder` call re-sorts validActors and indexes — O(N log N). With nearest-slot greedy, we'd compute N×N distance matrix per call (still per-subject, no cache). For N=12 that's 144 distance comps × 4 subjects × 1 grouped order = small. Cache or no cache?
- **What does `enemy is on this slot's far side, so don't pick that slot for this unit` look like?** Probably out of scope for v1 — that's role-aware bidding, deferred.

---

## 6. Garrison intent (Layer 3 — exists, extend)

> Target gap: 3.3 (intent dispatcher knows nothing about garrisonable buildings).

### 6.1 The shape

Add a fifth intent: `Garrison`. Fires when the click cell contains an actor with `Garrisonable` (or whatever the engine trait is called — likely `Cargo` or `Garrisoned`). Each per-subject suborder gets rewritten to an `EnterGarrison` order targeting that building.

Classification check: before the density-window scan, look at `world.ActorMap.GetActorsAt(clickCell)`. If any have a garrison trait and the subject is a valid garrison candidate (infantry, light vehicle depending on the building), return `Garrison` and bypass the bidder entirely.

### 6.2 Edge cases

- **Building can hold fewer units than the grouped order.** Excess units stay outside — what intent for them? Probably fall back to the EdgeLine bidder around the building (units form a perimeter line at the building's edge).
- **Building is enemy-held.** Enter as captors (existing `EnterGarrison` already handles this for engineers, infantry).
- **Building is destroyed mid-resolve.** Order rejected; units fall through to standard `Move` (engine already handles this).

### 6.3 Cost

~50 lines plus a YAML knob for "garrison preference per unit type" (some units shouldn't auto-garrison even if they could).

### 6.4 Open questions

- **Does this also handle "enter transport"?** Click on a friendly Bradley with grouped infantry selected → enter as passengers? Probably yes, same mechanism — different target trait. Defer until the basic building-garrison ships.

---

## 7. Modular cover signals (Layer 2 — exists, extend)

> Target gap: 3.4 (cover signal is flat).

### 7.1 The original plan's API

`CoverField(cell) = Σ wᵢ · Signalᵢ(cell)` with `ICoverSignal { float Sample(cell, world); }`. Each signal weighted, summed per cell, cached.

### 7.2 What it'd take

Two paths, both viable:

**a. Extend `IDensityInfo` to multiple trait types.** Walls (`BlocksProjectiles`?), sandbags, regular buildings get density grids. `Map.SetDensityLayer` already iterates `ActorDefinitions` and sums; just need more traits implementing the interface. Today there's the commented-out `BlocksSight: IDensityInfo` — uncomment and supply density values for cells along the wall.

**b. Parallel signal layer.** Keep `Map.DensityLayer` (Building.Density only) and add `Map.CoverField` — a `float` per cell summing multiple signals with weights. Modifier reads `CoverField` instead of `DensityLayer`. Signals registered via a world-trait pattern.

Path (a) is cheaper and matches the engine's existing pattern. Path (b) is cleaner architecturally but requires more rewiring.

### 7.3 Why not now

Today's tree signal is sufficient to validate the bidder. Adding wall/sandbag/building signals matters when we have maps that ship those covers. River-zeta and woodland-warfare are tree-dominated. **Defer until a player-visible map needs the extra signal types** — adding signal infrastructure with no consumer is exactly the speculative work the discipline rule §1.1 forbids.

### 7.4 Open question

- **Directional cover.** A wall blocks LOS along one axis. The bidder should know which side of the wall is the cover-providing side. This needs more than a scalar `byte` per cell — it needs per-direction info or a separate "wall normal" map. Out of scope until walls are a real signal type.

---

## 8. Attack-semantics integration (Layer 3 — exists, extend)

> Target gap: not in §1 directly, but called out in the original plan; matters for "the system feels coherent across move and attack actions".

### 8.1 Two specific behaviors

**a. Attack-move on contact.** When an attack-move squad enters LOS of an enemy, units snap to the nearest cover patch and engage from there. Today they engage from wherever they are when contact happens.

Hook: not a `ModifyGroupOrder` thing — happens *after* the order is resolved, during the unit's `AttackMoveActivity` (or whatever the engine equivalent is). On entering combat, queue a one-cell-radius cover-snap move before firing.

This is a per-unit behavior, not a grouped-order rewrite. Lives outside `CohesionMoveModifier` — probably as a new activity or a hook in `AttackBase`.

**b. Attack-click on enemy.** Player right-clicks an enemy unit. Today this is a `Attack` order targeting the actor. Cohesion-aware version: route to the cover patch nearest the enemy that's within weapon range, then attack from cover.

Hook: this *is* a `ModifyGroupOrder` thing. Add a fifth-and-sixth intent? Or treat `Attack` as a special case that internally classifies based on the enemy's cell? Probably the latter — `Attack` orders fall through the classifier with the enemy cell as click cell, then resolve to a cover patch within range.

### 8.2 Cost and order

(a) is small but lives in unfamiliar territory (the attack activity stack). (b) is medium and lives in `CohesionMoveModifier`. Ship (b) first because it's local.

### 8.3 Open questions

- **What's "within weapon range" for a mixed squad?** Different unit types have different ranges. Use min-range across squad? Use per-unit decision (each unit picks its own cover slot within its own range)?
- **Charge behavior when no cover is in range.** Probably: fall through to the existing `Attack` path — march to the enemy.

---

## 9. Multiplayer wiring for `SetCohesion` (Layer 1 — exists, fix)

> Target gap: not in §1 — Phase 1 wiring item from the original plan that was skipped. Not a v1 blocker; called out so we don't lose it.

### 9.1 The shape

`SetCohesion` order needs an `IResolveOrder` handler. Right now setting the mode via hotkey only updates the local trait — doesn't go through `OrderManager`, doesn't sync, doesn't replay. Single-machine play works because there's only one client. Multiplayer / replay breaks.

### 9.2 Cost

~30 lines. `AutoTarget` already handles other `SetX` orders; pattern-match one of those.

### 9.3 Why not now

It's a wiring fix. Ship when we ship a multiplayer-relevant cohesion feature. Until then, the single-player feel work doesn't need it.

---

## 10. Strip the diagnostic log (release-readiness)

> Target gap: 3.7.

Trivial. `CohesionMoveModifier.cs:614` — remove the `if (idx == 0) Log.Write("debug", ...)` block. Do this *after* the playtest loop is closed and we trust the system.

Until then, leave it on. It's the cheapest way to read intent from a live game.

---

## 11. What we're explicitly not doing

Cross-referencing the original plan to be honest about what's deferred:

- **Hover preview (Layer 6).** Listed in §3 as the third visualization layer. Defer until voice + slot lines ship.
- **Per-unit-type role profiles (Layer 4).** Listed in §5.2. Defer until per-stance leash and slot-assignment improvements ship.
- **Modular cover signals (Layer 2).** Listed in §7. Defer until a map needs them.
- **Attack-semantics integration (Layer 3).** Listed in §8. Defer until move-semantics feel is solid.
- **Tier-2 oversupply formation.** Original plan's "second row behind the first, staggered slots when N > K". Defer until playtest complains.
- **Backward-rewriting waypoint chains.** Defer until shift-click chains feel broken in playtest.
- **Per-player cohesion knob persistence.** Out of scope (non-goal).
- **AI-specific overrides.** Out of scope (non-goal).

The discipline: visualization first, then leash depth, then bidder polish. Everything else only if playtest demands it.

---

## 12. Migration order — suggested

Ranked by impact-to-effort, assuming we want visible improvement quickly:

1. **Voice cue per intent.** ~half a day. Immediately tells the player what the system thinks.
2. **Slot lines on order commit.** ~1 day. Visualizes per-unit destination.
3. **Engagement-aware leash with per-stance budget.** ~1–2 days. Fixes G (units drift out of cover) — the largest functional gap.
4. **Larger slot search radius with axis-asymmetric penalty + nearest-slot assignment.** ~1 day. Polish on §5.
5. **Garrison intent.** ~1 day. Closes E.
6. **Strip diagnostic log + autotests for new behaviors (C-E, C-F, C-G).** ~half a day. Release-readiness.

That's the v1.x ship sequence. Total ~5–7 days of focused work, no architectural rework. Each step is independently shippable and provides visible value.

7. (Beyond v1.x) Hover preview, attack-semantics integration, modular cover signals, multiplayer `SetCohesion` wiring, per-unit-type role profiles, tier-2 oversupply — in roughly this order, governed by playtest signal.

---

## 13. Open questions worth user input before building

- **Voice clips: per-faction or universal?** Different lines for NATO vs Russia? Universal is cheaper; per-faction is more polished.
- **Slot line color/intensity:** muted ally-color, intent-tinted, or generic? Intent-tinted (e.g., green for SpreadInside, blue for EdgeLine, orange for Approach) doubles as a visualization channel.
- **Leash budget defaults:** is the proposed Hold=0 / Defensive=1–2 / Aggressive=∞ the right shape? Or do we want a 4th value (`Camp` = even tighter than Hold)?
- **Garrison auto-fire when buildings exist nearby:** click far from a building, but a building is between click and group — does the bidder consider entering the building en route? Probably not in v1; users would find it surprising. Defer to playtest.
- **Should the leash know about transports?** If a unit was assigned a cohesion slot, then later EnterTransport queued, the leash will pull the unit out of the transport when it deboards. We probably want to clear the slot on EnterTransport. Same mechanism as clearing on individual Move orders.

These are real decisions. Each one is small enough to resolve in a chat exchange before the next build session.
