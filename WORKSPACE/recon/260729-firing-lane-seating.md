# Recon — firing-lane-aware cover-seat picking (2026-07-29, main @ e5b7bbcc)

Read-only design recon. Inputs: item-21 "stance-aware cover positioning" (merged @ `5c6cc1f0`), case-01 calibration (`8a6e998e`, `e5b7bbcc`; `WORKSPACE/cases/case-01-forest-ambush.md`). All file:line refs code-verified as of main @ `e5b7bbcc`. **No code changed. Deliverable is design input to a later decision — nothing here is a mandate to implement.**

## The problem in one sentence

The item-21 seat picker maximises **omnidirectional** window-density (how deep in shadow a cell sits from *every* approach), but a unit's ability to *fire* depends on the shadow along **one directional sightline** to its enemy — so the picker can bury an ambusher in a spot from which its own DMR fire is blocked (`ClearSightThreshold 4`, `weapons-ballistics.yaml:3`). Case-01's current geometry masks this (attackers are also blind), but it is a latent correctness gap flagged for refinement.

## 1. How the current seat picker works

Entry: `CohesionMoveModifier` (`engine/OpenRA.Mods.Common/Traits/CohesionMoveModifier.cs`), an `IModifyGroupOrder`. `ModifyGroupOrder` (`:1074`) classifies the click, computes formation slots per intent, then runs the ambush refinement as a **post-pass** over already-placed slots.

**Gate** (`:1144-1145`): `applyAmbushConcealment = isHuman && stance == UnitStance.Ambush && mode != CohesionMode.Tight`. Bots default `FireAtWill`, so the branch is inert for them → frozen AI benchmark byte-identical; Tight is the vanilla opt-out. Read once, cached with `cacheAmbush` (`:246`, `:1159`) so every subject in the same dispatch sees one consistent layout.

**Invocation** (`:1248-1249`): `if (applyAmbushConcealment) slots = RefineSlotsForConcealment(map, slots, subjectMobile);` — runs AFTER the intent branch (`SpreadInside`/`EdgeLine`/`Approach`/box) has seated the squad near cover.

**Scoring core — `ConcealmentScore`** (`:338-346`): sums `DensityLayer` over a `(2*windowRadius+1)²` window (default `ConcealmentWindowRadius = 2` → 5×5, `:209`) and feeds the sum through `Map.ForestGroundShadow` (`Map.cs:1102`, the same superlinear curve that bakes `shadows.bin`). **Viewer-independent by construction** — the header comment (`:334-337`) is explicit: at order time there is no enemy position, so it scores "how deep in shadow this cell sits" from *every* direction, not shadow along a sightline.

**Per-slot chooser — `PickConcealedCellNear`** (`:420-465`): over a `AmbushConcealmentSearchRadius = 3` (`:203`) window around the assigned slot, collects passable (`Mobile.CanStayInCell`, `:453`), not-yet-`taken` candidates, scores each via `ConcealmentScore`, defers ranking to the pure core.

**Ranking core — `PickBestConcealmentOffset`** (`:374-413`, pure, unit-tested): a candidate qualifies only if strictly more concealed than the assigned cell by `AmbushConcealmentBendMargin = 1` (`:220`, never trade concealment away) AND wins after a keep-home distance cost `effective = concealment − cheb*AmbushConcealmentDistancePenalty` (penalty `2`, `:214`). Winner maximises `effective` with a deterministic total-order tie-break (effective desc, raw concealment desc, chebyshev asc, then Dy,Dx asc). No qualifier → `(0,0)` identity.

**Conflict resolution — `ResolveConcealmentSlots`** (`:487-501`, pure): seeds `taken` with every original slot, frees the current slot's own cell before it chooses, re-occupies the pick — so no two units land on one cell and open-ground squads keep their exact formation.

**Determinism**: pure integer, zero RNG, no world reads beyond `DensityLayer`/`Mobile` — consistent with the influence-stack invariants (`DOCS/reference/influence-stack.md:92-95`). NUnit baseline 499 at merge.

## 2. How firing-LOS is computed, and what a per-seat lane query costs

**Firing gate**: `FiringLOS.HasClearLOS(self, target, threshold)` (`FiringLOS.cs:46-116`). After endpoint/range guards it is an **O(1) array read**: `map.ShadowLayer[fromMPos][toMPos]` → `(groundShadow, airborneShadow)`, return `groundShadow <= threshold`. `ShadowLayer` is `CellLayer<CellLayer<(byte,byte)>>` (`Map.cs:253`), indexed `[from][to]`. Range window is 2–32 cells (`distSq < 4` → clear, `> 1024` → falls back to `BlocksProjectiles.AnyBlockingActorsBetween`). Per-weapon enforcement in `Armament.cs:364`; unit-level in `AttackBase.cs:250`, `AutoTarget.cs:1113-1116` (most-permissive threshold across armaments).

**What the shadow byte is**: `groundShadow = ForestGroundShadow(Σ density of cells strictly BETWEEN from and to)` (`Map.cs:1136-1176`, endpoints excluded at `:1154`). Superlinear above knee 20 (`:1113-1119`).

**The load-bearing symmetry** — the crux of case-01 interaction #1:
- **Detection**: viewer sees a cell at `modifiedStrength = strength − ShadowLayer[viewer][cell].groundShadow` (`MapLayers.cs:362-368`, floored at 1). Attacker→defender density attenuates attacker vision.
- **Firing**: defender fires iff `ShadowLayer[defender][attacker].groundShadow <= ClearSightThreshold` (`FiringLOS.cs:113`).
- Both read `ShadowLayer[A][B].GroundShadow`, and the between-set of cells is **identical regardless of direction** (line symmetric, endpoints excluded both ways) ⇒ **ground shadow is direction-symmetric**: `[att][def] == [def][att]`. So one scalar — interposed density — is compared against two thresholds: the ~3 detection margin for a `Detectable.Vision 3` infantryman and the DMR firing threshold 4. **You cannot be concealed-by-interposition against an enemy AND fire at that same enemy through the same interposition.**

**Cost of a "can this seat fire toward point/direction X" query per candidate seat**: two `CPos.ToMPos` conversions + two `CellLayer` index reads + a byte compare = **O(1), ~identical to one `ConcealmentScore` window sum** (which already touches 25 cells). No live actor needed — the layer is addressable by cell, so it is queryable at order time against a hypothetical seat. Cost is negligible; the design question is purely *what point X is* at order time (see options), not query expense.

**Key distinction the refinement can exploit**: `ConcealmentScore` (5×5 window sum) is an *omnidirectional proxy*; real detection and real firing both key off the *directional* sightline byte. A cell at the rear/flank edge of a dense patch can have a high window score (concealed from most bearings) yet a **thin** shadow byte along one specific lane. The window proxy and the directional truth are not the same number — that gap is the whole opportunity.

## 3. Design options

At order time there is no live enemy, so every option must synthesise a **threat bearing** to define "toward X". Cheapest source: the group-move vector already computed — `frontAzimuth = (targetPos − groupCentroidPos).Yaw` (`:1205`), i.e. the squad's advance/facing direction. In case-01 the defenders face north (toward the clearing the attacker crosses), so the fire lane = the seat's forward bearing. Note WW3MOD `WAngle`/`WVec.Yaw` is **counterclockwise** (CLAUDE.md hard rule) — any bearing math must respect that.

### Option A — Threat-direction-aware seat scoring (bias the score, one pass)

Replace/augment the omnidirectional `ConcealmentScore` with an **anisotropic** score for ambush seats: reward density in the rear/flank hemisphere (concealment from the approach) and penalise density lying on the seat→threat lane (blocks fire). Concretely, split the density window by whether each cell's offset projects onto `+frontAzimuth` (in front / on-lane → penalise or exclude) vs. behind/beside (→ reward), all integer dot-products.

- **Complexity**: moderate. New scoring function alongside `ConcealmentScore`; the ranking/conflict cores (`PickBestConcealmentOffset`, `ResolveConcealmentSlots`) are unchanged and stay pure/testable. Must thread `frontAzimuth` into `RefineSlotsForConcealment` (currently takes only map/slots/mobile).
- **Determinism**: preserved. Integer dot-products, zero RNG, same total-order tie-break. Gate unchanged ⇒ bots/Tight/open-terrain byte-identical (influence-stack `:94-95` satisfied). No `shadows.bin` touch (reads existing `DensityLayer`).
- **Interaction with the concealment gate**: this is the *most aligned* with the case-01 premise — it seeks the geometrically reconciling seat (cover on the flanks, thin lane forward) rather than max omnidirectional burial. In **oblique** geometry it genuinely finds "hidden AND can shoot." In **head-on** geometry (case-01's cover patch directly between defender and attacker) the anti-correlation is physical and unavoidable — the score will still prefer the least-bad seat but cannot conjure a clear lane through the only cover. Risk: over-penalising on-lane density could pull ambushers *out* of concealment toward the enemy, weakening the detection edge that case-01's win depends on. Needs a weight that favours concealment when the two conflict.

### Option B — Post-seat lane check with bounded reseat (two pass)

Keep item-21's omnidirectional pass as-is; add a **second** pass that, for each refined seat, queries `ShadowLayer[seat][projectedThreatCell].groundShadow` (a cell some fixed distance along `frontAzimuth`) and, if it exceeds the unit's firing threshold, searches the same radius-3 window for the nearest cell that both (a) stays acceptably concealed and (b) has a clear-enough lane — reseating only if one exists.

- **Complexity**: higher. A second candidate sweep with a two-criterion accept (concealment floor AND lane byte ≤ threshold), plus conflict resolution re-run. Needs the per-unit firing threshold — reachable via `FiringLOS.GetBestThreshold`-style armament walk, but that wants a live actor; at order time would need a threshold read off the subject's `Armament` infos.
- **Determinism**: preserved if the projected-threat cell and threshold are derived by pure integer geometry (no RNG). Same byte-identity gate. **Depends on a valid `shadows.bin`** for the map — the lane byte is only meaningful in the 2–32 cell band and only if the cache is current (`Map.cs:1172-1174` pitfall); at seat distances < 2 cells the query returns "clear" and the check no-ops.
- **Interaction with the concealment gate**: explicitly two-objective, so it can *refuse* to trade away concealment (keep the item-21 seat when no lane-clear cell also clears the concealment floor). Cleaner separation than A, but the projected-threat-cell is a guess (the real attacker path is unknown at order time), so a wrong bearing reseats toward nothing. In head-on geometry, same physical wall as A — no window cell will pass both criteria, and it correctly falls back to the concealed-but-blocked seat.

### Option C — Accept and document (no engine change)

Leave the picker viewer-independent; record the max-density-can-block-own-fire behaviour as a known, bounded limitation. Rationale: (1) the head-on anti-correlation is *physical* — the same interposed density that hides you blocks your DMR, so in case-01's head-on geometry no seat picker can give both; the ambusher's edge is the **first-strike / detection asymmetry** (attacker acquires late, per case-01's own finding), not sustained fire through cover. (2) Ambush units spring — they may reposition or the enemy closes to < 2 cells where `FiringLOS` returns clear anyway. (3) Zero determinism/regression risk.

- **Complexity**: none.
- **Determinism**: trivially preserved.
- **Interaction with the concealment gate**: the concealment gate is exactly what case-01 relies on; C keeps it untouched. Cost: oblique-geometry cases that *could* have both hidden and firing seats keep sitting in fire-blocked cells unnecessarily — the picker leaves free value on the table wherever geometry would reconcile.

## Recommendation (input to a later decision — do NOT implement from this doc)

**Prefer Option A (anisotropic seat scoring), gated exactly as item-21, with the concealment-favouring weight when the two objectives conflict** — pending a case-01 re-calibration that actually *measures* return fire (the current batch can't distinguish "defenders fire freely" from "defenders blocked but attackers blind"). A is the smallest change that closes the real gap (it makes the score directional, matching the directional mechanic that governs both detection and firing), keeps all pure cores and the byte-identity gate intact, needs no `shadows.bin` regen, and degrades gracefully to item-21's behaviour in head-on geometry where no picker can win. Option B is the fallback if A's single anisotropic score proves too blunt to hold concealment and lane clearance simultaneously — its explicit two-criterion accept is more surgical but costs a second pass and an order-time threshold read. Option C is the correct call *only if* the later re-calibration shows the fire-blocked seat never actually costs the defender a decisive win (plausible given the detection-asymmetry finding) — in which case the complexity of A/B buys nothing for the shipped case.

Whichever is chosen, the deciding evidence is a **case-01 variant that lets attackers detect defenders** (the discarded COMPACT-clearing variant already showed defenders *lose* a symmetric brawl) so that defender return-fire — and therefore fire-lane quality — becomes measurable rather than masked.
