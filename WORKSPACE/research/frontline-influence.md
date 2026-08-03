# Frontline Influence — background research, gap analysis, phased design

**Mode:** RESEARCH (read-only; no engine/YAML edits, no autotests run). Design proposal only.
**Researched against:** `main @ 595b7002` (working tree, 2026-08-03). Every code claim cites the `file:line` it was read against; where a doc and code disagree, code wins.
**Author's one-line verdict:** The vision is *not* forgotten — its data substrate (the influence stack) and its coordinating layer (the Squad Brain) are already built or in-flight. What is genuinely missing is **terrain/reachability awareness** (the deferred "v2"), an **explicit whole-map frontline strength profile**, and a handful of **cheap behavior fixes** that would kill the three River-Zeta symptoms today. Recommendation: land the cheap symptom-killers first, then build the terrain-aware frontline layer as a new *sensor* consumed by the existing Brain — do **not** start a competing system.

---

## 0. The vision, restated (so the gap is measured against it)

The bot should: secure a front line spanning the **whole map**, spread out along it covering **all avenues of attack**, then move the front forward **where the enemy is weakest** (choosing the attack point from observed enemy forces there), and shift to a **defensive posture where resistance is too great**. Seeded from map geometry (each player's half = their influence). Scout with drones (force-attack to send them); sightings update the layer; standoff positioning (artillery a set distance behind the front but in reach of the enemy side) also driven by the layer.

This is recorded verbatim as the project **North Star**: `DOCS/design/ai-realism.md:62-125`, esp. the end-state at `:90-94` — *"forces end up spread along the entire line of combat so that every part of the front is defended… eventually at least some soldiers along the whole front — and the front steps forward wherever it is safe… Not a death-ball; a held, advancing line."*

---

## 1. Prior-art audit — what already exists or was already decided

The single most-documented strategic thread in the repo. The user's sense that it "keeps getting forgotten" is understandable but, in writing, **inverted**: the *substrate* shipped and the *coordinating design* is in-flight; what has not shipped is the *visible* "man the whole line + read terrain" behavior. Below is everything that touches the vision, with status.

### 1.1 The vision itself (curated, durable)
- **`DOCS/design/ai-realism.md:62-125`** — the North Star, user-authored 2026-07-20, sits *above* any benchmark cycle. §1 "Territorial-control map layer (the centerpiece)" (`:70-94`) is the vision almost word-for-word: own-half-safe prior, fog-respecting safe/grayzone/enemy, "always push where the enemy is comparatively weak," balance-of-power → reinforce weak spots, end-state "held, advancing line." **STATUS: standing mandate.**
- **`DOCS/reference/influence-stack.md`** — authoritative record of the *built* stack (Stages 0 + A–F), verified `main @ b93eda58`, final merge `36bd3b9e`. **STATUS: shipped, code-complete, curated.**

### 1.2 The data substrate — SHIPPED (the influence stack)
Every field the vision asks for as *data* exists and is fog-legal, zero-RNG, byte-identical when `@experimental` flags are off:
- **"Each half is mine" seed** → Stage-C control field is Voronoi-seeded from each player's public home beachhead (`ControlField.SeedVoronoi`, `influence-stack.md:54`). This *is* the user's "seeded from map geometry."
- **safe / grayzone / enemy classification, fog-respecting** → belief store + danger fields + control field (`influence-stack.md:20-59`; `BeliefStore.cs`, `DangerFieldLayer.cs`, `ControlField.cs`).
- **Frontline contour** → `ControlFieldMath.IsFrontlineEdge` (`ControlField.cs:168`; a half-plane enemy-vs-not split — render-only today, no sim consumer, `influence-stack.md:58`).
- **Distance-behind-the-front** → `ControlField.DistanceToEnemyFrontier` multi-source BFS + `FrontierStandoffMath.RearwardSteps` (`influence-stack.md:109`).
- **Weakest-point read (at POI granularity)** → Stage-F balance-of-power ring `PoiOffenseMath.NeighborhoodControlScore` / `BalanceOfPowerFactor` (`influence-stack.md:91`).
- **Artillery a set distance behind the front** → SHIPPED twice: echelon positioning (holds IndirectFire behind the MainBattle screen) and frontier-standoff (`MinFrontierDistanceCells`, `influence-stack.md:109`; `PoiOffensiveBotModule.cs:1028`). This vision element is **done**.

### 1.3 The coordinating layer — IN-FLIGHT (the Squad Brain)
- **`WORKSPACE/plans/260802_squad_brain_design.md`** (Revision 2) — a per-player `SquadBrainBotModule` that reads the influence stack and emits **posture** (Attack/Hold/Consolidate = the "shift to defense on resistance" the vision asks; also PIPELINE item 18), ranked **attack vectors**, ranked **defend POIs**, **force allocation** by role, and **opportunistic Advance missions** (§2.6: "when a sector is undefended and a free path exists, generally advance"). Aggressiveness is a first-class **slider** (§2.3/§2.7), not an archetype. **STATUS: designed; Phase 1 core (`auto/mission-commitment`, `1fec5070`) landed — LayeredDefence honors the ledger + held axes are not reshuffled. The Brain object + Mission object + line-manning allocation are NOT yet built.**
- **`WORKSPACE/plans/260722_bot_brain_architecture.md`** — ratified the verdict (extend the chassis, add a persistent operations object). Superseded by the 260802 doc.
- **`WORKSPACE/plans/260721_terr_offense_bias.md:351-354`** — the "spread-along-front / reinforce-weak" forward-compat note; its `exp-terr-bias` branch became Stage F.
- **`WORKSPACE/plans/260722_influence_stack_design.md:3`** — "ratified direction from the user"; the full 7-stage architecture.

### 1.4 PIPELINE state
- **Item 31** (`PIPELINE.md:35-37`) — "Aggressiveness slider + opportunistic advance," user-mandated 2026-08-02, folded into the Brain design; *"slider infrastructure is cheap and may land early to unlock testing."* Near the top of the live queue.
- **Item 18** (`PIPELINE.md:65-66`) — "Should I attack?" posture layer, now the Brain's top-level output.
- **Item 17** (`PIPELINE.md:61-63`) — SR-capture wiring, user-deferred until opening-economy is solid. Tangential.
- **Nothing about terrain / rivers / bridges / amphibious crossing is queued anywhere.**

### 1.5 Explicit decisions that constrain (not contradict) the vision
- **Byte-identity invariant** (`influence-stack.md:95-100`): zero `SharedRandom` draws in the always-on world layers; every consumer flag default-off; `@stable`/normal/human byte-identical. Any frontline behavior ships `@experimental`-gated. Shapes *how*, not *whether*.
- **Radial danger kernels are v1; terrain-aware flow is v2** (`DangerFieldLayer.cs:24-26`, verbatim: *"KERNELS ARE RADIAL v1. Terrain-aware flow (a river splitting the front) is a declared v2 upgrade."*; matching Stage-E line-walk terrain-blindness `GroundDangerNav.cs:87-90`). **This is the one decision that directly bounds the vision's terrain/amphibious dimension — it is deferred, not rejected.**
- **Stage-F re-baseline** (`influence-stack.md:115`, `ai-bench/runs/260728_rebaseline_result.md`): `@experimental` currently *underperforms* `@stable` (S2 net-swing median ≈ −$1,425). A measurement caution on the current tuning — no decision against the vision.

### 1.6 The terrain / amphibious / route-opening dimension — NOT STARTED
Only in archived idea backlogs, never in AI reasoning: `DOCS/archive/IDEAS.md:65-66` (pontoon bridges), `DOCS/archive/TODO.md:83,171` (engineer bridge repair, pontoon bridges). River Zeta appears in the repo almost exclusively as the **benchmark map**, not as an amphibious tactical study; `WORKSPACE/cases/` has only `case-01` (forest ambush) — **no frontline / flank / amphibious / bridge case exists.**

**Prior-art takeaway:** re-proposing the influence layer, weakest-point selection, or standoff would duplicate shipped work. The genuinely-open pieces are (a) the **terrain/reachability-aware front** (v2), (b) an **explicit whole-map frontline strength profile** + avenue enumeration, and (c) the **cheap behavior fixes** for the three symptoms. All three feed the in-flight Brain.

---

## 2. Root-cause analysis of the three symptoms (code-level)

### 2.1 Symptom 1 — center-only focus (two central bridges), flanks neglected
Three multiplicative causes compound; **none is explicit "seek the center" code.**

1. **PRIMARY — POI distance is Euclidean crow-flies from the bot's own SR.** `PoiMap.GetOffensiveTargets` computes `distCells = (actor.CenterPosition - ownSr.CenterPosition).Length / 1024` (`PoiMap.cs:352-354`) → `PoiScoring.DistanceFactor = hl*100/(hl+d)` (`:578-583`, half-life `DistanceHalfLifeCells=20`, `world.yaml`). On a symmetric river map the **central bridges are the shortest straight-line crossing to the far bank from both SRs**, so central POIs win the distance term. There is **no pathfinding** here — a river between SR and POI does not lengthen the distance.
2. **The total score is a multiplicative product**, so the distance advantage compounds with everything else: `Score = value × distFactor × threatFactor × ownershipMul` (`PoiScoring.Score`, `PoiMap.cs:662-663`) then `× bias/100` (`ApplyBias`, `:668-669`). A flank POI of equal value/threat always scores below a central one because `distFactor` is strictly lower.
3. **AMPLIFIER — top-k axes + score-proportional unit allocation funnel mass to the peak.** The offense module commits at most `MaxAxes=4` axes drawn from the single score-ranked list (`DesiredAxisCount`, `PoiOffensiveBotModule.cs:1779-1789`) and then splits leftover units **proportional to score** (`AllocateProportional`, `:1795-1846`, distribution `leftover*scores[i]/sum` at `:1829`). So the highest-scoring central cluster grabs the axis slots *and* the majority of the army. There is **no term that says "one axis north, one south"** — `EarlyGameSpread` only shrinks packet size, still drawn from the same score peak.

**Enabling condition — zero reachability awareness.** `PoiMap.Discover` (`PoiMap.cs:203-227`) filters candidates purely by actor type (income structure with a `CaptureManager`, or the SR type) — no path, no locomotor, no water check; grep for `reachab|PathFinder|IsReachable` in `PoiMap.cs` returns nothing. A far-bank derrick behind a **destroyed** bridge scores identically to a reachable near-bank one at the same crow-flies distance. Flank POIs are neither pruned nor rewarded as an alternate axis; they just lose the distance competition. (The omniscient `InfluenceMap.GetFrontline` is **not** read by offense scoring, so it is not the cause.)

### 2.2 Symptom 2 — flank neglect: amphibious + engineer + destroyed bridges invisible
The far-bank flank POIs are reachable in two ways the AI cannot see:
- **Amphibious crossing.** Four amphibious locomotors exist with nonzero water speed (`world.yaml:43` `foot-amphibious`, `:76` `foot-amphibious-mountainer`, `:138` `lighttracked-amphibious`, `:179` `tracked-amphibious`); amphibious IFVs/APCs bind to them (`vehicles-russia.yaml:61,174`; `vehicles-america.yaml:222`; `infantry.yaml:2020`). **No AI code reads this** — `[Aa]mphibious` matches nowhere in `engine/OpenRA.Mods.Common/Traits/`. The offense module issues one grouped, **untyped** `AttackMove` to the target cell (`PoiOffensiveBotModule.cs:1277`) with no locomotor/reachability discrimination — it never decides "amphibious → water target, land → bridge." The only reachability check in squad AI is naval-only (`NavyStates.cs:36`).
- **Engineer bridge repair.** Fully implemented as an *engine* mechanic — engineer `e6` carries `RepairsBridges` (`infantry.yaml:1903`), enters a `LegacyBridgeHut` (`civilian.yaml:856,867`) via the `"RepairBridge"` order (`RepairsBridges.cs:63,68`). **Zero AI callers** — `RepairBridge`/`BridgeHut` appear in no bot module; the only engine references are the trait/activity implementations. Bridges/huts are never even *discovered* as POIs (they have no income weight and no `CaptureManager`, so `PoiMap.Discover` skips them).
- **Terrain-blind strategic layer.** Confirmed by the actual v1/v2 comments: `DangerFieldLayer.cs:24-26` (radial v1) and `GroundDangerNav.cs:87-90` (line-walk left "blind to terrain… the declared v2 terrain-flow problem"). A river is not represented as a barrier anywhere in Stages A–F; all POI distance is Euclidean (`PoiMap.cs:353,420,488`). **No route-opening awareness exists — not even dead code.**

Net: the bot ignores the flanks because it has (a) no water/bridge terrain model, (b) no reachability test relating a target to the assigned units' locomotor, (c) no knowledge an engineer can open a crossing, and (d) crow-flies distance that makes an unreachable far-bank derrick look as attractive as a reachable near one.

### 2.3 Symptom 3 — units pool and shuffle near the spawn/SR
A two-part structural gap, amplified by one active lever:
1. **Uncommitted ground units get no order at all.** `BuildFreePool` (`PoiOffensiveBotModule.cs:1025-1035`) scans all `world.Actors` for eligible combat units not on an axis and not ledger-committed — but the `free` list is only ever `.Add`/`.Remove`/`.Count`-ed; **no `QueueOrder`/`AttackMove`/`Move` ever targets a free-pool unit.** A unit not funded onto an axis this evaluation keeps its (empty) activity and idles wherever it stands — the SR rally point where reinforcements muster. This is the DISCOVERIES "self-healing free-pool invariant" (`WORKSPACE/DISCOVERIES.md:57`) read correctly: re-collection makes a unit *pool-eligible*, not *moved*. Compounding: the SR rally point is **never advanced** as the front shifts (the intent at `supply-route.md:105` is unimplemented — the only `SetRallyPoint` caller is `BaseBuilderBotModule` near production buildings), and there is **no forward-staging/muster behavior for the ground body** (only helis via `HelicopterSquadBotModule.ForwardStaging` and mounted transport have staging). `LayeredDefenceBotModule` — the one module that could pull idle units forward — early-returns when there is no contested frontline (`if (contestedCells.Count == 0) return;`), and its own comment names the symptom: *"a death-ball reads as… centroid sitting near the SR pre-contact."*
2. **DOMINANT visible cause — the retreat FSM oscillates small axes back to the SR.** `@experimental` runs `RetreatWhenLosing: true` + `NoReinforceLostFights: true` (`ai.yaml`), and `EarlyGameSpread` makes 2–3-unit axes (`EarlyUnitsPerAxis:3`, `EarlyMinAxisSize:2`) that trivially read "losing" (enemy ≥ 2× own, `CombatRetreatMath.LosingBeyond`). A losing axis AttackMoves to the SR rally cell (`OrderRetreat`), reaches the `RetreatSafeDistanceCells=10` bubble, flips back to Engaged, advances, loses again, retreats again — a standing oscillation that parks and shuffles units in a ~10-cell bubble around the SR. `MissionCommitmentEnabled` (the anti-thrash mitigation) **deliberately exempts retreating axes**, so it does not damp this.
3. No module intentionally garrisons the *combat body* at home; the only thing repeatedly sending units to the SR is the retreat withdrawal.

Mechanism 2 makes it *visible and persistent*; mechanism 1 is why there is a standing reservoir of orderless units at the SR for it to act on.

---

## 3. Gap analysis — what the vision needs that today's stack lacks

| Vision requirement | Today | Gap |
|---|---|---|
| **Explicit front estimation across whole map width** | Contour (`IsFrontlineEdge`) + BFS distance-to-frontier; both render/standoff only | No **per-sector strength/thickness profile** of the front, and no **enumeration of avenues of attack** (crossings/chokepoints). Cannot answer "which sector of the enemy line is thin." |
| **Man the whole line — spread to cover all avenues** | Defend selection is POI-anchored (`GetDefendTargets` = own income + own SR); LayeredDefence positions on contested cells only *after* contact | No pre-contact enumeration of the frontier's sectors/crossings to garrison; free pool has no forward order (§2.3). The Brain's defend-POI list is not yet a whole-front spread. |
| **Weakest-point attack selection** | Stage-F balance-of-power ring, per POI (income structure / SR) | Reads the ring *around a POI*, not the **enemy frontline strength profile**. On River Zeta the only POIs are central-favoured; a thin *flank sector* with no structure is invisible. |
| **Posture switch (attack vs hold) on resistance** | Designed as Brain posture (Attack/Hold/Consolidate); Phase-1 commitment landed | Brain object + Mission object not built; posture is not yet emitted. |
| **Standoff placement for artillery** | SHIPPED (echelon + frontier-standoff) | None — this element is complete. |
| **Route-opening awareness (engineer + amphibious)** | Engine mechanics exist; AI uses neither; terrain-blind fields | Entirely missing: no crossing/reachability model, no amphibious-vs-land target typing, no "send an engineer to open a flank crossing" decision. |
| **Scout with drones (force-attack to send)** | Recon roles + littlebird recon exist (`0cb7c808`) | Out of scope for this doc, but the belief store already consumes whatever they see; a scouting-coverage driver over the frontier is a natural later add. |

**The through-line:** two missing *substrate* capabilities — a **terrain/reachability model** and an **explicit frontline strength profile** — plus a few **behavior wiring** gaps (free-pool forward order, amphibious typing, engineer route-opening). Everything else the vision asks for is either shipped or designed into the Brain, which is the natural consumer of both new substrates.

---

## 4. Design options

All options MUST respect the invariants (`influence-stack.md:95-100`): zero `SharedRandom`; fog-legal (belief data only, except **map-static facts** like terrain and structure locations, which are public — POI discovery already reads them under fog); every new lever a default-off flag on a per-profile trait so `@stable`/normal/human stay **byte-identical**. Any change to shared `PoiMap` scoring must be gated exactly like Stage F's `suppressOmniscientThreat` (default reproduces the frozen path).

### Option A — Extend the control field into an explicit frontline layer (+ a terrain/reachability map)
Add two new fog-legal reads on the **existing** substrate:
- A **`CrossingMap`** world layer computed at map load (public map fact, like POI discovery): per-locomotor **connected components** of the passable graph, the set of **crossing cells** (bridges — incl. destroyed/repairable) that join components, and the **water-crossable edges** the amphibious locomotor can use. This is the terrain/reachability model the "v2" comments defer.
- A **frontline strength profile** on `ControlField`: along the `IsFrontlineEdge` contour, per coarse sector, the believed own-vs-enemy value differential (from `BeliefStore` + presence), and each sector's associated **avenues** (crossings from `CrossingMap` mapped to that sector). This turns the existing contour into "where is the enemy line thin, and by which avenue."
The Squad Brain (in-flight) consumes both: defend allocation spreads across enumerated avenues; attack-vector selection reads the strength profile to pick the weakest enemy sector; reachability types each axis to land vs amphibious units.
- **Pros:** reuses the shared gate/stagger/seed machinery (one `Participates`, one `UpdateInterval`); stays byte-identical by construction; feeds the layer the Brain already reads; the reachability model is the enabling piece the deferred v2 always needed; each piece is independently inert until consumed.
- **Cons:** touches the hot control-field recompute (must stay integer/zero-RNG and cheap); the strength profile is new math to NUnit-pin; the biggest wins wait on the Brain.

### Option B — A standalone frontline system, separate from the control field
A new self-contained module owning its own seed, grid, stagger, and frontline math, parallel to the influence stack.
- **Pros:** isolation; can iterate without touching the control-field recompute.
- **Cons:** duplicates the `InfluenceStack.Participates` gate, the deterministic-stagger discipline, and the Voronoi seed — exactly the machinery the stack centralised to guarantee byte-identity; two systems that must agree on "where is the front" will drift; violates the "one gate, one stagger" invariant spirit. **Rejected.**

### Option C — Behavior-only, no new field
Directly patch the three symptoms without new substrate: add a **lateral-spread term** + a **reachability multiplier** to `PoiMap` scoring (gated), **type axis assignment** by locomotor (amphibious → water targets), give the **free pool a forward-staging order**, and **damp the retreat oscillation**.
- **Pros:** cheapest; front-loadable; each slice independently mergeable and byte-identical when gated off; directly kills the observed symptoms; needs no Brain.
- **Cons:** does not deliver the *full* vision — "read the enemy line's weakest sector" and "man every avenue" need the strength profile + crossing enumeration; a bare reachability multiplier still can't tell a *repairable* crossing from a permanent wall.

### Recommendation — **Option A as the architecture, sequenced with Option C's slices as the on-ramp**
Build the terrain-aware frontline layer (A) as the target, but **land C's cheap symptom-killers first**, because they (1) fix exactly what the user is watching fail on River Zeta, (2) are NUnit/single-autotest-gradeable so they don't need a multi-test grant, and (3) are prerequisites anyway (a reachability model and amphibious typing are shared by both C and A). Reject B outright — the control field is the correct substrate and duplicating it fights the byte-identity discipline the whole stack is built on. The frontline layer is a **new sensor**, and the **Squad Brain is its consumer** — this integrates with the in-flight design (`260802_squad_brain_design.md`), it does not compete with it.

---

## 5. Phased plan — small, independently-mergeable, acceptance-by-number

Front-loaded so Phases 0–3 need only build + NUnit or a **single** autotest (no multi-test grant); Phases 4–6 are where the benchmark ladder (user-gated) earns its keep. Every phase is `@experimental`-gated and byte-identical with the flag off.

**Phase 0 — Reachability/crossing model (no game run; build + NUnit).**
A `CrossingMap` computed at map load: per-locomotor connected components, crossing cells (bridges incl. destroyed→repairable, discovered via `LegacyBridgeHut`/`Bridge` actors), and amphibious-crossable edges. Pure math, NUnit-pinned on a River-Zeta fixture.
*Bar:* on the fixture, land-locomotor component count = 2 (split by the river); the 2 central bridges enumerated as crossings; the N flank destroyed-bridges flagged `repairable`; amphibious component = 1 (whole map). Zero RNG; `@stable` never builds it.

**Phase 1 — Reachability-gated + amphibious-typed targeting (single autotest).**
Gate a `PoiReachabilityFactor` into `PoiMap` scoring (default 1 = inert, active only `@experimental`, same shape as `StrategicRepointEnabled`): a far-bank POI reads **full value for the amphibious pool** and **down-weighted for land units** until a crossing to it exists. Split axis assignment so amphibious units are the ones sent to water-only targets.
*Bar:* single autotest on River Zeta — amphibious IFVs cross the river to reach ≥1 far-bank POI within T ticks; a land-only axis is never sent to an unreachable far-bank POI. Flag off ⇒ byte-identical.

**Phase 2 — Forward-staging order for the free pool + rally advance (single autotest).**
Give uncommitted free-pool units a move order to a forward staging point on the friendly side of `ControlField`'s frontier (reuse `FrontierStandoffMath` / `GroundDangerNav`), instead of idling at the SR; advance the muster point as the frontier moves.
*Bar:* single autotest — median distance of idle combat units from the SR rises from baseline X to ≥Y; the count of combat units within N cells of the SR ("road congestion") drops below threshold. Measurable directly with the new `UnitLifecycleLogger` JSONL analyzer (`25ab82d7`).

**Phase 3 — Retreat-oscillation damper (single autotest).**
Stop small early-spread axes oscillating into the SR bubble: raise the effective min-axis-strength before a retreat is triggered, and/or add enter/exit hysteresis + minimum dwell to the retreat FSM (mirroring the Brain's posture-hysteresis design).
*Bar:* single autotest — SR-bubble re-entries per axis over a match drop below threshold; no regression in overall retreat-when-genuinely-losing behavior (a truly outnumbered axis still withdraws once).

**Phase 4 — Explicit frontline strength profile (no game run; build + NUnit).**
Extend `ControlField` with a per-frontier-sector strength read (believed own vs enemy value along the `IsFrontlineEdge` contour) + avenue mapping (Phase-0 crossings → frontier sectors). Integer-only, NUnit-pinned; rides the existing `Participates`/`UpdateInterval` cadence (no new timer).
*Bar:* on the fixture, the thin enemy sector is identified as the min-strength sector, and its avenue (the flank crossing) is named. Zero RNG; `@stable` never builds the array.

**Phase 5 — Whole-front man-the-line allocation + weakest-point attack (benchmark ladder, user-gated).**
Consumed by the Squad Brain (or, pre-Brain, by `LayeredDefence`/`PoiOffensive` as gated readers): defend allocation **spreads across enumerated avenues** rather than only POIs; attack-vector selection reads the **frontline strength profile** to bias toward the weakest enemy sector; posture holds where the sector reads too strong.
*Bar:* benchmark ladder — frontier-coverage metric (fraction of frontier sectors with ≥1 own unit) rises; flank-engagement rate rises; win-rate vs `@stable` non-regressed (relative to the `260728_rebaseline` zero).

**Phase 6 — Engineer route-opening (single autotest → ladder).**
When the frontline strength profile says a flank sector is weak but its crossing is a repairable destroyed bridge, emit a route-open action: send an engineer (`e6`) to the `LegacyBridgeHut` (`RepairBridge` order) with a screen, opening a new land axis; alternatively commit the amphibious pool.
*Bar:* single autotest — the bot repairs a flank bridge and pushes a land axis across it within T ticks when that sector is the weakest; ladder confirms no economy regression from the engineer diversion.

**Sequencing rationale:** Phases 0, 4 are pure NUnit; 1, 2, 3, 6a are single-autotest; only 5 and 6b need the multi-test ladder. Phases 0→1→2→3 alone would visibly fix all three River-Zeta symptoms; Phases 4→5→6 deliver the full "read the enemy line, man every avenue, open routes" vision through the Brain.

---

## 6. Summary for the reader

- **The vision is largely already built or designed.** The influence stack (safe/enemy fields, Voronoi "each half is mine" seed, frontline contour, distance-to-front, weakest-point ring, artillery standoff) is **shipped**; the Squad Brain (posture / attack vectors / defend POIs / opportunistic advance / aggressiveness slider) is **designed and Phase-1-landed**. Do not re-propose these.
- **The three River-Zeta symptoms have precise code causes:** center-focus = Euclidean-from-SR distance × multiplicative score × score-proportional allocation, with zero reachability awareness (§2.1); flank neglect = no amphibious/engineer/terrain model anywhere in the AI (§2.2); spawn pooling = the free pool is never issued a move order + a retreat FSM that oscillates small axes into a 10-cell SR bubble (§2.3).
- **The real gaps** are a **terrain/reachability model**, an **explicit frontline strength profile**, and a few **behavior-wiring fixes** — all of which feed the in-flight Brain rather than competing with it.
- **Recommended:** Option A (extend the control field + add a crossing/reachability map) as the architecture, sequenced behind Option C's cheap symptom-killers as front-loaded phases. Reject a standalone system (Option B).
- **Phase list:** 0 reachability model (NUnit) → 1 reachability-gated + amphibious-typed targeting → 2 free-pool forward-staging → 3 retreat damper → 4 frontline strength profile (NUnit) → 5 man-the-line + weakest-point (ladder) → 6 engineer route-opening.

---

*Incidental insight for `WORKSPACE/DISCOVERIES.md` (logged separately, path-limited): the AI has **no representation of water, bridges, or per-locomotor reachability** — POI distance is Euclidean crow-flies from the SR (`PoiMap.cs:352-354`), amphibious locomotors are read nowhere in `Traits/`, and the engine's fully-working engineer bridge-repair (`RepairsBridges.cs`, `e6`, `LegacyBridgeHut`) has zero AI callers. This is the concrete content of the deferred "terrain-aware v2" (`DangerFieldLayer.cs:24-26`).*
