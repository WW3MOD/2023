# Targeting / LOS / Blocking Rework — Brainstorm Handoff

**Status:** Brainstorm in progress, paused. Restart in a fresh session.

## To the next agent

You are picking up a brainstorming session about restructuring AutoTarget, line-of-sight, and projectile-blocking in WW3MOD. The previous session (Opus 4.7, 2026-05-05) reached a point where the user lost confidence in the direction. This document captures what was learned so you can restart cleanly without re-litigating settled questions.

**How to use this:**

1. Read it fully before asking the user anything.
2. Re-enter the `superpowers:brainstorming` skill.
3. Do **not** restart from the same starting frame as the previous session. The previous session's proposed solution is at the very end, marked clearly. Treat it as one possible direction, not as a foundation.
4. Engage the **Open Questions** section first — those are the conversational dead-ends that lost the user's trust. Especially Q1 (density semantics) and Q2 (spread weapons firing around obstacles).
5. The user is technically engaged and wants to think carefully. Don't over-summarize or rush to land an answer.

---

## 1. The user's observed problems

Concrete bugs and frustrations from playtesting. These are what we are solving.

### 1.1 Units sometimes refuse to fire when they should
- Specific case: AA infantry in range of an enemy helicopter, did not fire. Manual force-attack revealed the unit *had* a target solution and could fire from current position. So the LOS gate was too strict for the geometry.
- Intermittent — most units fire correctly most of the time. The issue feels nondeterministic to the player.

### 1.2 Wire-guided missiles fire through trees and waste themselves
- Bradley TOWs and BMP AT-4s acquire a target through trees, fire, the missile detonates on a tree mid-flight.
- Most expensive failure mode (long reload, high credit cost, hard counter to the player's tactical intent).
- Long-standing issue. Multiple solution attempts in history have not satisfied the user. Previous attempts: density-summed shadow with per-weapon `ClearSightThreshold`, proximity bypass for close obstacles. Both partial fixes.

### 1.3 AutoTarget priority/scoring code is opaque
- Inline ~130 lines in `AutoTarget.ChooseTarget` mixing priority filtering, range, conditional priorities, overkill prevention, critical-damage bonus, armor prediction.
- Score direction is inverted (lower = better) which fights the variable name `priorityValue`.
- User wrote it in flight ("winged it") and finds it hard to tune.
- Wants this restructured into named, testable pieces. Independent of the LOS work.

### 1.4 Density-summed blocking is unpredictable to the player
- Current `FiringLOS.HasClearLOS` reads precomputed `ShadowLayer` of summed densities per cell-pair, compared to a per-weapon `ClearSightThreshold`.
- Two trees in a line might pass for one weapon and fail for another with no obvious cue.
- Player can't easily predict whether a shot will work.

### 1.5 Single trees in a cell unfairly block edge-of-cell shots
- Cell-pair density doesn't model whether the projectile geometrically intersected the trunk hitshape.
- A line through the corner of a cell with a tree in the middle gets the same shadow value as a line straight through the trunk.
- Sub-cell positioning is meaningless under the current model.

### 1.6 Targeting LOS and projectile-flight LOS use different geometries
- `FiringLOS.HasClearLOS` (targeting) reads precomputed cell-pair shadow density.
- `BlocksProjectiles.AnyBlockingActorsBetween` (in-flight) walks actors on the actual line geometry.
- They can disagree: targeting passes, projectile blocks. This is the deepest cause of "missile aims at target then hits tree."

### 1.7 Buildings persist in shadow after destruction
- `Map.UpdateShadowForCells` is currently disabled (it caused mid-game lag when many buildings changed at once — a previous attempt was reverted).
- Demolished buildings still cast targeting shadow until next match. Visual fog also affected.

### 1.8 Proximity bypass uses RNG
- `BlocksProjectiles` has a probabilistic bypass (4-7 cell falloff) so close obstacles can be skipped.
- Same shot may behave differently across runs.
- Originally added to mitigate ATGM-into-trees issue. Partial fix at best.

---

## 2. The user's gameplay aspirations

Things the user wants from the rework. Some are in tension with each other.

### 2.1 Determinism for the fire decision
- Same firer + same target + same world = same firing decision.
- No per-shot dice for "did the trees block."
- Per-projectile inaccuracy still varies, that's existing OpenRA behavior and fine.

### 2.2 Trees as physical cover
- A tree on the line should physically block projectiles.
- Front/back asymmetric: a unit behind a tree gets cover; in front does not.
- The user explicitly considered "trees give a defense bonus" and rejected it because of the front/back issue.
- BUT density matters — a tree occupies maybe 10-20% of its cell, so a projectile threading the cell edge can miss the trunk. **This was the user's correction in the final exchange of the previous session.**

### 2.3 Penetration as a physical model
- Weapons have `Penetration` (mm-equivalent), reuse existing `warhead.Penetration` field.
- Blockers have `Armor` (mm-equivalent) — new field on `BlocksProjectiles`.
- Punch-through weapons (kinetic): penetration math, blocker takes proportional damage, projectile continues with reduced energy.
- HE weapons: explode on first blocker. New `PunchThrough: bool` flag.
- Damage to blocker on punch-through hit ≈ `weaponDamage * blockerArmor / weaponPenetration` (proportional energy split). Edge case when armor ≥ pen needs careful definition.

### 2.4 Spread weapons should fire around obstacles
- An MG firing many bullets per burst has a wide spread cone.
- A single tree in the center of a cone doesn't stop the burst — most bullets still reach.
- A sniper or ATGM has a narrow cone — center-line block effectively means no shot.
- **This was the late-surfacing issue that broke the previous session's model.** The user rejected the previous spread-aware proposal as bolted-on rather than fundamental. Suggests the issue is conceptual: the targeting decision shouldn't be a binary line-block check at all.

### 2.5 Dodging into cover (in-flight)
- A guided missile chasing a target that ducks behind a tree should hit the tree.
- Per-tick projectile-flight check needed for guided munitions.
- For dumb fast projectiles (rifle, tank shell), in-flight check is largely irrelevant — path is set at fire time. The user said "let me know if you see some cases here for it or not" — the previous session's answer was "skip for dumb projectiles, but it's basically free to keep so probably keep uniformly."

### 2.6 Shared LOS vs direct LOS (post-v1, undecided)
- Current `CanBeViewedByPlayer` is binary at player-team level — anyone on the team sees, the whole team can target.
- User wants to model "I personally see the target" vs "an ally sees it for me, I can engage with reduced effectiveness."
- Direct sight = full firing.
- Shared sight = could fire with penalty (reduced accuracy, deprioritized in AutoTarget).
- Possible Phase 2 feature.

### 2.7 Team-leader designators (post-v1, undecided)
- Existing concept in `DOCS/IDEAS.md:15-16` and `DOCS/TODO.md:162-164` as binoculars/laser designator (originally active force-attack).
- User now considers: passive auto-mark by team leaders, with active force-mark as override.
- Each TL designates ~1-3 targets at a time.
- A designated target removes the shared-sight penalty for friendlies engaging it.
- Possible Phase 3 feature.

### 2.8 Vision system overhaul (future, separate work)
- Current vision is fixed-radius, all units same. User said it was "winged" long ago.
- Future direction (NOT this rework):
  - Per-tile vision accumulation: vision builds up over time on tiles being looked at.
  - Multiple viewers stack the rate.
  - Targeted "focus" vision: spotted enemies grant +N vision in a 3×3 around them, naturally implementing "many enemies see the spotted target clearly even though they couldn't detect it from scratch."
  - Cones of vision considered, rejected as too jumpy.
- This will eventually replace `CanBeViewedByPlayer` as the visibility input. Whatever LOS architecture lands here should have a clean seam for that swap.

---

## 3. Performance constraints

The user is tight on perf headroom:

- A separate parallel chat is currently working on per-tick allocation cleanup. AutoTarget allocation churn during scans is the next perf target after that.
- The user said: "there isnt much room right now to drain too many resources. So just keep it in mind and for the final spec/plan those considerations should be mentioned and handled and reported on risks and performance impact."
- Final spec needs an explicit performance section with estimates per component.

---

## 4. Open questions surfaced and not resolved

These are where the previous session glossed past, hand-waved, or hit user pushback. Engage these directly with the user when restarting.

### Q1. What does Density actually represent in the new model?

The previous session ended in unresolved tension here:

- Density was originally intended as "% of cell area occupied by blocker."
- The "geometric line vs hitshape" model the previous session proposed bypassed density entirely for targeting decisions.
- But density still exists for visual fog rendering and is meaningful gameplay-wise.
- Is density (a) a rendering-only relic to ignore for targeting, (b) a meaningful targeting input alongside hitshape, or (c) something to phase out / replace with hitshape-derived values?
- Related: what's the relationship between Density and Armor? Both encode "how much does this blocker resist?" but they're different axes (visual occupancy vs physical thickness). Are they independent fields, or one derived from the other?

### Q2. How should spread weapons fire around obstacles?

**The unresolved one.** Options surfaced:

- (a) Use weapon's existing `Inaccuracy` to compute a cone radius at the blocker's distance; if cone > obstacle radius, treat obstacle as not-on-line for targeting.
- (b) Per-weapon `RequiresClearLOS` flag, set per character (sniper=yes, MG=no).
- (c) Don't gate firing on obstacles at all for spread weapons; let per-projectile flight checks handle waste.
- (d) Model the targeting decision as "fraction of cone clear": fire if > some threshold.

The user dismissed the previous agent's spread-aware proposal (option a) as bolted-on. Suggests the conceptual frame is wrong, not just the parameters.

**Hypothesis worth investigating fresh:** maybe the targeting decision shouldn't be a binary block check at all. Maybe it should be an *expected damage to target* calculation: "given weapon spread, blocker geometry, density, and penetration, how much damage will this shot do to the intended target?" If expected damage < some threshold, don't fire. This composes naturally with PunchThrough, density, spread, and overkill prevention all under one roof — but it's a fundamentally different framing than "is the line clear?"

### Q3. Targeting LOS and projectile-flight LOS — same logic, or related-but-distinct?

- Previous session tried to unify them: "same geometry, same predicate." Then realized for spread weapons the in-flight per-bullet behavior naturally diverges from the once-per-fire targeting decision.
- True framing might be: same *facts* (line, blockers, hitshapes, penetration, density), different *queries* (targeting asks "is this shot worth firing?", in-flight asks "did this specific projectile hit this specific actor?").
- Resolving Q2 likely resolves this too.

### Q4. Building dynamic update strategy

Three options were tabled:

- (a) **Static-only.** Keep dynamic updates disabled. Accept staleness — destroyed buildings still cast LOS shadow until next match.
- (b) **Trees in ShadowLayer (static), buildings live-checked.** Avoids the dynamic update path. Visual fog from buildings becomes incorrect after destruction.
- (c) **Re-enable dynamic ShadowLayer with budget+coalesce.** Fix the lag this time around.

Previous session leaned (b). User did not push back but didn't deeply examine. Worth revisiting given the visual-fog regression that (b) implies.

### Q5. Vision LOS as a separate concept from Projectile LOS?

Previous session proposed splitting:

- Vision LOS = "any blocker on line" (no penetration math). Per-firer geometric check.
- Projectile LOS = penetration math vs blocker armor.

Two independent predicates on the same line. Then realized for hitshape geometry the two collapse: if the line geometrically clips a blocker, that's both a vision event and a projectile-LOS event. They can diverge by weapon (penetration math allows projectile to continue, vision still blocked).

Open: is the Vision/Projectile split conceptually useful, or should the architecture have just one predicate with a "what does a hit mean for this weapon?" delegate?

### Q6. Proximity bypass — keep, drop, or transform?

- Current: 4-cell hard exemption + 4-7 cell probabilistic falloff zone in `BlocksProjectiles`.
- Original purpose: prevent ATGMs hitting trees right next to launcher.
- Previous session leaned "drop entirely" because new penetration + PunchThrough model handles most cases.
- BUT: HE weapons (PunchThrough=false) near trees still hold fire under the no-bypass rule. Is "force the player to reposition" acceptable gameplay for HE near cover, or do we need an exemption?

### Q7. Trunk/canopy hitshape split

- Idea surfaced and tentatively endorsed: trees declare two hitshapes — trunk (low/narrow) and canopy (mid/wide).
- Air-to-ground / ground-to-air *steep* lines catch canopy at intermediate altitudes.
- Pure ground-to-ground horizontal lines hit trunk only.
- Cheap (just data on tree actors). No new geometry code.
- Status: probably good, not yet implemented.
- **Important clarification from user:** asymmetry is NOT direction-based (line is line). It's altitude-profile-based (steep lines pass through canopy heights at some points; flat lines never do).

### Q8. Scope phasing

- Phase 1 (target for v1): geometric LOS rework, AA fix, penetration model, scoring restructure, bug fixes only.
- Phase 2 (likely post-v1): shared-sight feature with penalty.
- Phase 3 (likely post-v1+): designators (passive auto + active force-mark).
- User wants worktree to keep v1 main branch unaffected during this rework.

### Q9. AutoTarget scoring restructure (D)

- Independent of LOS work. Could land first as a pure refactor.
- Pull `ChooseTarget` inline scoring into named methods on a new helper (suggested name: `TargetingScore`).
- Each axis (priority, range, overkill, conditional, critical-damage bonus) becomes its own method.
- Fix score direction (lower-is-better is confusing). Rename to `score`, flip math.
- Same numeric output as today; just legible and unit-testable.
- **Could ship this independently as a small PR before the LOS rework even starts.**

---

## 5. What's already shipped on main (do not redo)

- **AA airborne lookup symmetry fix.** When firer is ground and target is airborne, `FiringLOS.HasClearLOS` reads `ShadowLayer[target][firer].airborneShadow` instead of `[firer][target]`. `Target` was threaded through `HasClearLOS` so the target's airborne trait is reachable. WPos overload kept for compatibility. Build clean. Solves the AA-not-firing-at-helicopter bug as a tiny standalone PR.
- This is the only code change that landed during the brainstorm. Everything else is design discussion.

---

## 6. Code touchpoints

Don't waste time re-finding these.

### Targeting LOS path (4 callers, 1 helper)

| File | Role |
|---|---|
| `engine/OpenRA.Mods.Common/Traits/FiringLOS.cs` | Helper. `HasClearLOS(self, target, threshold)` reads `ShadowLayer`. `GetBestThreshold(self, target)` picks max-permissive threshold across active armaments. |
| `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs:748` | Fire-time check during target choice. |
| `engine/OpenRA.Mods.Common/Traits/Attack/AttackBase.cs:170` | `TargetInFiringArc`. |
| `engine/OpenRA.Mods.Common/Traits/Attack/AttackFollow.cs:360` | Per-tick check during follow attack. |
| `engine/OpenRA.Mods.Common/Activities/Attack.cs:237` | `TickInner` LOS gate. |

### Projectile in-flight path (5 projectile types, 1 helper)

| File | Role |
|---|---|
| `engine/OpenRA.Mods.Common/Traits/BlocksProjectiles.cs` | `AnyBlockingActorsBetween` is the live walker. Has proximity bypass logic (currently uniform across weapons). |
| `engine/OpenRA.Mods.Common/Projectiles/Bullet.cs` | Calls `AnyBlockingActorsBetween` per tick. |
| `engine/OpenRA.Mods.Common/Projectiles/Missile.cs` | Same. |
| `engine/OpenRA.Mods.Common/Projectiles/Railgun.cs` | Same. |
| `engine/OpenRA.Mods.Common/Projectiles/InstantHit.cs` | Same. |
| `engine/OpenRA.Mods.Common/Projectiles/AreaBeam.cs` | Same. |
| `engine/OpenRA.Mods.Common/Projectiles/LaserZap.cs` | Calls commented out, currently disabled. |

### ShadowLayer / DensityLayer

| File:Line | Role |
|---|---|
| `engine/OpenRA.Game/Map/Map.cs:1005` | `SetShadowLayer()` builds the cell-pair shadow grid at map load. |
| `engine/OpenRA.Game/Map/Map.cs:1020` | `UpdateShadowForCells()` for dynamic updates. **CURRENTLY UNUSED** per inline comment "260503". |
| `engine/OpenRA.Game/Map/Map.cs:1088` | `RecomputeShadowFrom()` recomputes a single from-cell. |
| `engine/OpenRA.Game/Map/Map.cs:1145` | `UpdateDensityForBuilding()` for placing/removing buildings. **CURRENTLY UNUSED.** |

ShadowLayer memory layout: `CellLayer<CellLayer<(byte GroundShadow, byte AirborneShadow)>>` — outer cell × inner cell, 2 bytes per pair. Allocated lazily per from-cell.

### AutoTarget scoring

- `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs:648-825` — `ChooseTarget`. Inline scoring including:
  - Priority filtering (relationship, target types)
  - Per-priority loop with `priorityValue` (lower = better, inverted from naming)
  - `+50000` bonus for `CriticalDamage` target type
  - Range bias (shorter range = lower value = higher priority)
  - `AverageDamagePercent` overkill skip + soft penalty
  - `priorityCondition` weighting via `ConditionalPriority` and `PriorityCondition`
- `engine/OpenRA.Mods.Common/Traits/AutoTargetPriority.cs` — per-trait priority info (`Priority`, `ConditionalPriority`, `PriorityCondition`, `ValidTargets`, `OnlyTargets`, `InvalidTargets`, `ValidRelationships`).

### Existing weapon fields

- `WeaponInfo.ClearSightThreshold` (byte 0-255) — current per-weapon LOS gate.
- `WeaponInfo.Inaccuracy`, `Range`, `MinRange` — usable for spread-aware logic.
- `DamageWarhead.Penetration` and `DamageWarhead.Damage` — for penetration math.

### Existing blocker fields

- `BlocksProjectilesInfo.Height`, `MinBypass`, `MaxBypass`, `BypassChance`, `ExplodesOn`, plus the static proximity bypass constants.
- `BlocksSightInfo.Density` (byte, 0-255).
- `HitShapeInfo` — geometric hitshape info, has `VerticalTopOffset`.
- `Armor.Thickness` — actor-level armor, used for vehicle damage calcs.

---

## 7. Tentatively decided (worth revisiting)

The previous session reached partial consensus on these. Each is worth a fresh look with the user.

| Item | Tentative decision | Confidence |
|---|---|---|
| AA airborne symmetry fix | Done, shipped | Locked |
| Trees indestructible in v1 | Yes — simplifies a lot | High |
| Determinism over RNG for fire decision | Yes | High |
| Penetration vs Armor as physical model | Yes — reuse `warhead.Penetration`, new `Armor` field | High |
| `PunchThrough: bool` flag on weapons | Yes — true=kinetic, false=HE | Medium |
| Damage to blocker = proportional formula | Tentative — formula edge cases need definition | Medium |
| Drop proximity bypass entirely | Tentative — model handles most cases except HE-near-trees | Medium |
| Trees in ShadowLayer (static), buildings live-checked | Tentative — sidesteps dynamic update problem | Medium |
| ShadowLayer kept structurally for rendering | Yes — don't touch byte layout, no memory regression | High |
| Live walk uses `FindBlockingActorsOnLine` + hitshape geometry | Tentative — same as projectile flight | Low (broken on spread weapons) |
| AutoTarget scoring restructure (D) | Yes — independent and worthwhile | High |
| Phase 1 / 2 / 3 split | Yes — clear phasing | High |
| Worktree for the work | Yes — keep v1 main free | High |

---

## 8. Previous session's proposed solution (one direction, NOT endorsed)

**Read with skepticism. The user explicitly said they don't fully believe in this and want to start over. Captured for reference only.**

### Architecture
- Single helper `LineOfSight` (replacing `FiringLOS`) with two predicates:
  - `HasVision(self, target)` — true if firer has direct vision LOS (no blocker on geometric line).
  - `HasProjectilePath(self, weapon, target)` — true if projectile can physically reach target.
- ShadowLayer kept structurally; serves rendering and as a fast-path "trigger live walk" filter.
- Trees stay in ShadowLayer (static, indestructible). Buildings live-checked via `FindBlockingActorsOnLine`.
- Live walk uses geometric line-vs-hitshape, same as projectile in-flight.
- Cache results in `LineOfSight` keyed by `(firerCell, targetCell, weaponClass)` with 5-tick TTL.
- `BlocksProjectiles.AnyBlockingActorsBetween` learns penetration math; in-flight check applies armor accumulation per blocker hit.

### Penetration model
- New `BlocksProjectilesInfo.Armor` field.
- New `WeaponInfo.PunchThrough` bool.
- Damage to blocker on punch-through hit = `weaponDamage * blockerArmor / weaponPenetration` (proportional energy split).
- Damage to target = `weaponDamage * remainingPen / weaponPenetration`.
- For PunchThrough=false: blocker takes full warhead damage, projectile detonates.

### Spread-aware targeting (the part the user pushed back on)
- For each blocker on the center line, compute `coneRadiusAtBlocker = weapon.Inaccuracy * blockerDist / range`.
- If `coneRadiusAtBlocker >= blocker.HitShapeRadius`: blocker fully blocks, apply penetration math.
- If `coneRadiusAtBlocker < blocker.HitShapeRadius`: blocker doesn't fully block, treat as not-on-line for targeting.
- The user found this unconvincing. The core complaint: it felt like a patch on a model that was fundamentally wrong about spread weapons, rather than a model that handles them naturally from the start.

### Phasing
- Phase 1: dual LOS, AA fix, penetration model, scoring restructure, no shared-sight.
- Phase 2: shared-sight (fire when vision blocked but projectile clear, with priority deboost + accuracy hit).
- Phase 3: designators.

### Perf accounting (incomplete)
- Per-AutoTarget-scan: same or cheaper.
- Per-tick `AttackBase` during attack: same on fast path; cache + bounded live walk on through-cover path.
- Memory: zero ShadowLayer change.
- New live walks: ~70-110 ops per query, cached for 5 ticks.

---

## 9. Suggested approach for restart

1. Read this document fully before talking to the user.
2. Re-enter `superpowers:brainstorming`.
3. Acknowledge to the user that you've read this handoff and confirm the **Open Questions** are the things to dig into.
4. Engage Q1 (density semantics) and Q2 (spread weapons) early. They're the conceptual cracks that broke the previous session.
5. **Consider a fundamentally different framing.** The previous session treated this as a geometric line problem with a binary "blocked or clear" answer. The user's pushback on spread weapons suggests the right primitive may be expected damage delivered to target rather than line-clarity. Worth exploring before committing to any architecture.
6. Don't skip the "what does this feel like in playtest?" lens. The user is grounded in concrete observed bugs (AA + heli, ATGM + tree). Tie every architectural choice back to which observed bug it fixes and which gameplay shape it produces.
7. The AutoTarget scoring restructure (D) can ship completely independently as a small refactor PR. Mention it but don't entangle it with the LOS work.
8. The AA fix is already shipped. Start from "AutoTarget bugs partially mitigated, the real work is structural improvement."
9. Don't over-summarize or rush to land an answer. The user wants to think.

---

## 10. Useful prior context

- `CLAUDE/HOTBOARD.md`, `CLAUDE/RELEASE_V1.md` for current v1 priorities.
- `DOCS/IDEAS.md:15-16` and `DOCS/TODO.md:162-164` for the original laser designator concept.
- A separate parallel chat is independently working on AutoTarget allocation churn during scans. Coordinate to avoid conflict.
- Existing tests in `engine/OpenRA.Test/` follow NUnit 3. Suppression and damage tests provide patterns to model from.
- The prior commit `52d726a8` ("ShadowLayer-based targeting LOS replaces BlocksProjectiles for targeting") is the one that introduced the current `FiringLOS` system — its commit message explains the perf-driven motivation for the precompute approach.
- The prior commit `d9e65666` ("Add proximity-based bypass for projectile blocking") is where the proximity bypass came from — its commit message explains the original intent.
