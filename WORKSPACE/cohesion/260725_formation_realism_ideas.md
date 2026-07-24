# Formation realism ideas — make groups FEEL alive without degrading play

**Date:** 2026-07-25 · **Mode:** design/ideation, NO code changes.
**Grounded against:** `main @ e45fb307`. Feasibility claims cite `file:line` in
`engine/OpenRA.Mods.Common/Traits/CohesionMoveModifier.cs` (the formation
interpreter) and `CohesionSlotMemory.cs` (the per-unit leash), read for this doc.

## The question this answers

The user's exact signal: **perfect geometric formations look unrealistic even
when there's no cover to justify a shape** — a squad in the open lands on a
stamped lattice of evenly-spaced dots, all facing the same way, halting on one
crisp rank line. That reads as *manufactured*, not *manned*. The standing ask:
make formations feel more realistic **AND** non-disruptive — **nothing degraded
on purpose**; no unit is made to stand in worse ground or fire less to look
pretty. Favor cheap *perceptual* wins over new mechanics.

**Already in play — NOT re-pitched here:** cover-seeking / treeline positioning
(`EdgeLine` + `LayCoverAwareLine` :541-611), cover-beats-geometry line-bending
(`PickCoverSlotNear` :623-689), combat interval in Loose, dispersion in Spread,
halting before enemy vision (ambush arc). Also already proposed in
`260722_stance_proposals.html` and owned elsewhere: nearest-slot assignment
(shipped, `AssignAll` :961-1007), formation preview ghosts, formation-preserving
travel, stance-strength leash, Tight=column, AoE-derived Spread spacing. The
Defensive/Ambush cover-side and react-to-contact semantics are owned by
`260722_doctrine_realism_audit.md`.

## The determinism rule every idea below obeys

The whole modifier is a **pure function of sim state with zero RNG** — slots are
computed once per order and memoized on `(tick, click, mode, order, ID-set)`
(:159-171, :961), all positions are integer `CPos`/`WPos`. Any variation we add
**must not** read `SharedRandom`/`LocalRandom` (survey Q6: `LocalRandom` is not
in the sync hash — a divergent read desyncs silently). The safe primitive is a
**pure hash of `Actor.ActorID`** (already the sort key here, :800): identical on
every client and in replay, no RNG consumed. Notation below: `H(id)` = a fixed
integer hash of ActorID (e.g. Knuth `id*2654435761`), used only to derive small
*deterministic* per-unit offsets. `WAngle` is **counterclockwise** (CLAUDE.md
hard rule) — respect the sign when deriving facings.

---

# TOP 3 — recommended for the pipeline

## 1. Deterministic arrival jitter  ·  cost S  ·  the flagship

**Sees:** each unit settles ~⅓–¾ cell off its exact grid point instead of on a
surveyed lattice — the squad occupies the ground *loosely*, like men taking up
positions, not stamped dots. Directly targets the user's signal (ugly even in
the open).

**Reads realistic because:** no real section dresses on a perfect metre-grid in
the field; troops occupy available micro-terrain. Breaking the lattice is the
single biggest "manned vs manufactured" perceptual flip, and it applies to the
**Open box path** (`ComputeBoxSlots` :369-449) that is the common
open-terrain/AI case the user is complaining about.

**Disruption risk & mitigation:** a naive offset could shove a unit off cover,
onto impassable terrain, or into a worse cell — that *would* be "degrading on
purpose." Mitigation: (a) magnitude bounded to a fraction of a cell and always
**less than `MinSlotSpacing`** (:73) so slots never overlap; (b) after
offsetting, run the same `Mobile.CanStayInCell` guard the line path already uses
(:645) — if the jittered cell fails, fall back to the exact slot. Net effect is
cosmetic-only; nobody lands anywhere they couldn't already stand.

**Cost S:** one hash-derived `(dx,dy)` added to each slot inside
`ComputeBoxSlots` before `map.Clamp` (:444-445), plus a CanStayInCell fallback.
Isolated to the box path; line paths already wander via cover scoring so they
need none. (Fold in the *ragged trailing rank* note from idea #6 for free here.)

**Determinism:** offset = `f(H(actorID))`, pure integer, no RNG. Byte-identical.

## 2. Settle facing — orient the halted line to the front, with sector micro-variation  ·  cost M

**Sees:** when a group finishes a Move, the units **turn to face the front**
(the movement/threat azimuth) instead of freezing on whatever heading the
pathfinder happened to leave — and each unit's final facing varies by a few
degrees so the line looks like troops scanning slightly different sectors, not a
row of mannequins.

**Reads realistic because:** this is *arcs of observation / sectors of fire* —
the most-cited real infantry fieldcraft: a halted element orients on a common
front and fans overlapping arcs so no two men stare down the identical azimuth.
A firing line all facing one way already looks 10× more deliberate than
post-path random headings; the micro-fan removes the last "cloned" tell. Purely
about where barrels point — **it never moves a single unit**, so it is the most
non-disruptive high-feel idea on the board.

**Disruption risk & mitigation:** cosmetic only; zero positional change. One
seam: a unit turning to its scan facing must still snap to engage a target.
`Turn` (`Activities/Turn.cs:37` `IsCanceling → true`) is interruptible, and
AutoTarget's attack path cancels the current activity, so a pending settle-turn
never delays a shot. Keep the micro-fan tight (a small arc) so it never points a
unit *away* from the front.

**Cost M:** facing is **not** set by the modifier today — it only rewrites the
target cell (`WithTarget(cell)` :911). Cleanest home is to extend
`CohesionSlotMemory`: store a desired `WAngle` in `Assign` (:75-95) — front
azimuth from `orderPoint − groupCentroid` (both integer `CPos`), plus a
`H(id)`-derived micro-delta — and queue `new Turn(self, facing)` when the unit
reaches its slot in the existing `TickIdle` (:143-146, already queues a `Move`
at :173). Stays entirely in the cohesion family; no new trait wiring.

**Determinism:** front azimuth is integer geometry; micro-delta from `H(id)`.
`WAngle` is integer and synced — deterministic by construction, no RNG.

## 3. Rolling halt — stagger stop depth along the move axis  ·  cost S

**Sees:** instead of the whole squad braking onto one perfectly-aligned rank,
units come to rest at slightly different depths along the direction of travel —
the formation "flows" to a stop the way a moving body of troops actually does,
front-runners a touch ahead, others settling in behind.

**Reads realistic because:** a formation in motion never halts on a drawn line;
arrival is ragged along the axis of advance. This is the *temporal*-feeling
cousin of jitter but cheaper than true start-time staggering — a small
**along-axis** position offset reads as staggered *stopping* without any
movement-timing plumbing.

**Disruption risk & mitigation:** depth variance could pull a rear unit into the
rank behind it. Mitigation: cap the along-axis offset below `rowSpacing/2` and
below `MinSlotSpacing`, and (like idea #1) it composes with the same
CanStayInCell fallback. Only touches box/open-line paths; leave Tight column
crisp (a column *should* look tight — that's its identity).

**Cost S:** add a `H(id)`-derived signed offset along the `moveDir` unit vector
in `ComputeBoxSlots` (the move axis is already computed :386-402). A few lines;
naturally bundles with idea #1 (same hash, same clamp/fallback) into one small
change if pipelined together.

**Determinism:** along-axis offset = `f(H(id))` projected on integer `moveDir`.
No RNG.

---

# PARKED — good, lower priority or more ambitious

## 4. Fire-team clumping in Loose — buddy pairs, not even beads  ·  cost M  ·  (mildly ambitious)

**Sees:** in Loose, units settle in 2–3 small knots (buddy teams) with gaps
between the knots, instead of evenly-spaced beads on a line. **Reads realistic:**
infantry move and hold in fire teams / buddy pairs, not at uniform interval —
clumping *is* the doctrine, not a defect. **Disruption:** could read as
"bunching" if overdone, and tighter local spacing slightly raises AoE exposure —
so gate it to Loose only (Spread must stay dispersed for its anti-artillery
identity). **Cost M:** replace the uniform slot generator with a
deterministic-by-index clustering pass. **Determinism:** grouping is a pure
function of the ID-sorted index, no RNG. Higher feel-payoff than the top-3 but
more moving parts and a real (small) tactical side-effect, so it's parked behind
the zero-risk perceptual wins.

## 5. Terrain-conforming line — hug the ridge/road, not a geometric chord  ·  cost M

**Sees:** a line ordered across broken ground bends to follow the ridgeline /
road / contour rather than cutting a dead-straight chord across a slope.
**Reads realistic:** troops string *along* terrain features. The cover-aware line
already bends to trees; this generalizes it. **Disruption:** could over-bend and
lose the readable line shape — mitigate by weighting it under the existing
`LineSlotDistancePenalty` (:139) so terrain only wins when the deviation is
small. **Cost M:** add a terrain/elevation term (Ramp/road layers) to the
`PickCoverSlotNear` score (:648-650) — the scoring hook exists, the substrate
read is new. **Determinism:** reads static map layers, pure. Parked because the
cover-aware bend already delivers most of this feel where it matters (treelines).

## 6. Ragged trailing rank in the box  ·  cost S

**Sees:** the box's partial last row sits deliberately uneven/offset rather than
neatly centered under a full rank — the formation's tail looks unfinished the way
real ones are. **Reads realistic:** a section's rear element is never a tidy
half-rank. **Disruption:** none beyond idea #1's. **Cost S:** perturb only the
final-row slots in `ComputeBoxSlots` (:428-434). **Parked** because idea #1's
jitter already produces this as a side-effect — list it only if jitter is
descoped and a cheaper targeted fix is wanted.

## 7. Temporal staggered march-start  ·  cost M

**Sees:** rear ranks step off a few ticks after the front, so the squad "peels
out" instead of all lurching at once. **Reads realistic:** columns move off by
element, not simultaneously. **Disruption:** low, but it delays some units'
departure — must be tiny so responsiveness isn't hurt (a control-feel cost the
top-3 avoid). **Cost M:** the modifier returns an `Order`; it can't cheaply
prepend a per-unit `Wait`. Needs an initial-delay activity injected on the Move,
i.e. movement-plumbing work outside this file. **Determinism:** delay =
`f(H(id))` in integer ticks, no RNG. Parked: idea #3 buys most of the "staggered"
read *positionally* for a fraction of the cost.

## 8. Guide / base-unit dressing  ·  cost M

**Sees:** one unit (the one already nearest the click) acts as the base man and
the others visibly *dress off* it, rather than every unit independently seeking
an abstract slot. **Reads realistic:** formations form on a designated base man.
**Disruption:** low; mostly changes assignment feel. **Cost M:** re-anchor slot
generation on the chosen guide's cell instead of the raw click, then let
`AssignAll` match the rest. **Determinism:** guide pick is deterministic
(min distance, tie-break ActorID). Parked as a subtle polish with less payoff
than jitter/facing.

## 9. Contact drill — firm-and-face on taking fire mid-march  ·  cost L  ·  (ambitious)

**Sees:** when a moving formation takes effective fire, the nearest units go
firm and turn to face the threat (a visible "react to contact") while the rest of
the order continues — the squad *reacts* instead of strolling on unbothered.
**Reads realistic:** react-to-contact is the most fundamental battle drill; a
column that ignores an ambush is the exact RTS trope the project wants dead.
**Disruption:** real — this interrupts a player's move, so it must be a
*stance-gated, opt-in* behavior and register in a commitment ledger (survey Q5)
so squad orders don't stomp it and it doesn't fight the human's intent. **Cost
L:** a new positioning executor + threat read; overlaps the doctrine audit's
react-to-contact (§2-B) and the ratified Phase-2 executor — **should be built
there, not as a formation tweak.** Listed as the one genuinely ambitious item
because it's the highest realism ceiling, but it is a *mechanic*, not a
perceptual win, and belongs to the stance roadmap.

## 10. Idle scan micro-bob at the halt  ·  cost S–M  ·  (low priority)

**Sees:** halted units slowly pan their facing a few degrees back and forth,
"scanning," instead of standing frozen. **Reads realistic:** sentries scan;
frozen statues don't. **Disruption:** risks looking twitchy/jittery if the rate
or arc is wrong, and it's motion-for-motion's-sake — easy to overdo. **Cost
S–M:** a slow deterministic facing oscillation keyed on `WorldTick + H(id)`
(pure), gated to idle-at-slot. **Determinism:** `WorldTick`-driven, integer, no
RNG. Parked lowest: it's the only idea with a real "annoying if wrong" failure
mode, and idea #2's static sector-fan already sells "alert, not frozen" without
continuous motion.

---

# Summary ranking

| # | Idea | Cost | Disruption | Feel payoff |
|---|------|------|-----------|-------------|
| **1** | **Arrival jitter** | S | none (clamped, CanStayInCell fallback) | **high** — kills the stamped lattice in the open |
| **2** | **Settle facing + sector fan** | M | none (never moves a unit) | **high** — firing line, not mannequins |
| **3** | **Rolling halt (depth stagger)** | S | none (capped < spacing) | med-high — organic stop |
| 4 | Fire-team clumping (Loose) | M | small (AoE) | high |
| 5 | Terrain-conforming line | M | small (line shape) | med |
| 6 | Ragged trailing rank | S | none | low (subsumed by #1) |
| 7 | Temporal march-start stagger | M | low (responsiveness) | med |
| 8 | Guide/base-unit dressing | M | low | low-med |
| 9 | Contact drill (react-to-contact) | L | high (opt-in/ledger) | very high — but a mechanic, owned by stance roadmap |
| 10 | Idle scan micro-bob | S–M | med (twitch risk) | low |

**Why these three top:** #1 and #3 are near-free, live entirely inside
`ComputeBoxSlots`, share one hash + one clamp/fallback, and can ship as a single
small change that directly answers the user's complaint (open-terrain formations
look manufactured). #2 is the one facing win — zero positional disruption,
strongest doctrine grounding (sectors of fire), and extends the existing
`CohesionSlotMemory`/`TickIdle` seam rather than adding new wiring. All three are
pure functions of `ActorID`/tick with no sim RNG, so they're byte-identical
across clients and replays by construction.
