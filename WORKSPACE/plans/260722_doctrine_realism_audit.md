# Doctrine-Realism Audit — bot behavior vs modern combined-arms doctrine

> **Purpose.** WW3MOD has a *primary* goal of AI realism: bots that read like a
> plausible modern battlefield force — doctrine-grounded, not RTS/movie tropes —
> so viewers and players are impressed by how *militarily believable* the bots
> look ([[ww3mod-ai-realism-goal]]). This doc audits current + planned bot
> behavior against real modern combined-arms doctrine (US Army FM 3-0 /
> ATP-level concepts; north-star reading of the Russo-Ukraine war via
> RUSI/ISW/CEPA/CSIS) and **ranks the gaps by viewer-visible realism impact per
> unit of effort.**
>
> **Author date:** 2026-07-22. **Mode:** EXPERIMENTAL (the audit targets the
> experimental bot brain; Normal/Rush/Turtle remain the frozen A/B control).
>
> **Read alongside (the material this audit synthesizes, not restates):**
> - `WORKSPACE/plans/260722_strategic_tactical_split_SPEC.md` — the **ratified**
>   L1/L2/L3 roadmap (the thing this audit critiques).
> - `WORKSPACE/plans/260722_stance_tactical_survey.md` — current substrate facts
>   (every `file:line` in the "today" column traces here or to a direct read).
> - `WORKSPACE/plans/260719_ai_realism_research.md` — the sourced doctrine→behavior
>   translation table (the "roadmap will add" column keys to its §-numbers).
> - `DOCS/reference/game-model.md` + `DOCS/reference/supply-route.md` — the gameplay
>   model (off-map reserves, budget allocation, indestructible SR beachhead).
>
> **What this doc adds over the two prior plans:** (1) it grounds *what bots
> actually do today* in `file:line`, phrased as doctrine; (2) it runs a **critical
> pass on the ratified 260722 SPEC**, flagging where its own design would produce
> doctrinally *wrong-looking* behavior; (3) it re-ranks everything by **viewer
> visibility**, not just win-rate.

---

## 0. The WW3MOD grain (two filters that reshape every doctrine concept)

1. **No manufacturing — a call-in economy.** Units are reinforcements deployed
   from off-map reserves; they walk/fly in from the map edge nearest the **Supply
   Route** (a fixed, indestructible, one-per-player *beachhead*, not a factory)
   to the rally, then to the front (`game-model.md`, `supply-route.md`). "Build" =
   spend budget + eat travel time; "rotate out" = send a unit to the edge to
   *recover* its budget; a dead unit is permanent budget loss. This maps unusually
   cleanly onto the real war's biggest lesson — **logistics is the center of
   gravity** — and it makes **force preservation** literally an economic act.
2. **The RTS format caps realism.** Casualties are HP, not KIA/WIA; "morale" is
   the suppression meter, not a rout model; there is no operational rear to strike.
   Where a doctrine concept can't survive these limits it is marked accordingly.

A structural point that recurs below: **today both AI spatial grids are
omniscient** — `InfluenceMap.Recompute` iterates `world.Actors` with no fog check
(`InfluenceMap.cs:92`), as does `ThreatMapManager` (`:89`). Almost every "recon"
doctrine concept is therefore *unearned* today: the bot already knows everything,
so it never *looks* like it is finding the enemy. Phase 4's fog migration is the
declared fix — and, as §3 argues, its own biggest risk.

---

## 1. Behavior-by-behavior audit table

Legend for **Gap**: ✅ present & credible · 🟡 partial / stubbed · ❌ absent.
"Today" cites current behavior; "Roadmap adds" cites the ratified 260722 SPEC
phase and/or the 260719 research §.

| # | Doctrine concept | What bots do TODAY (file:line) | What the roadmap will ADD | Remaining gap |
|---|---|---|---|---|
| 1 | **Suppression before assault** (fix the enemy with fire, then close) | Suppression → prone is real, always-on, condition-driven: `suppressed>30` grants prone (`infantry.yaml:252`), `ProneSpeedModifier 60` + damage/vision bands (`InfantryStates.cs:182,195`, `^SuppressionEffects infantry.yaml:339+`). Units fire while moving (`SmartMove.cs:49,75`). But nothing **sequences** "suppress, THEN assault" — the offense module just attack-moves the axis in (`GroundStates.cs:67,174`). | 260719 §3: fires-first positioning + massed suppression opening the assault. | 🟡 The *mechanic* (suppression) is excellent and visible; the *tactic* (deliberately pin, then maneuver) is unmodelled. Not in the 260722 SPEC at all. |
| 2 | **Fire & maneuver / bounding overwatch** (one element fires while another moves) | None. Squad moves are grouped `AttackMove` blobs re-issued `queued:false` ~every 75 t (`GroundStates.cs:67,161,174`; survey Q5). No base-of-fire / bounding split. | 260722 **Phase 5**: "bounding movement between covers" (`SPEC §7`). L3 Hunt "may creep forward between covers within leash" (`SPEC §4`). | 🟡 Planned but last (Phase 5). Bounding is per-unit creep, not a *paired* base-of-fire/maneuver-element split — see critical pass §2-C. |
| 3 | **Recon-pull / sensor-to-shooter** (see before you shoot; act on observed intel) | ❌ effectively. `ScoutBotModule` is wired (`ai.yaml:246,254`) and heli scout squads exist, but the strategic grids are **omniscient** (`InfluenceMap.cs:92`, `ThreatMapManager.cs:89`) so scouting changes nothing the bot *acts on*. `PoiMap` scores off omniscient influence. | 260722 **Phase 4**: full fog migration — grids rebuilt per-player from `FrozenActorLayer`; opens a recon/scouting behavior cycle. 260719 §1: ThreatMap-freshness → PoiMap targeting + standing scout. | 🟡→❌ The *substrate* migration is ratified; the *behavior* (a standing recon task, sensor→shooter link) is deferred to an unspecified follow-on. Blind window risk — critical pass §3-F. |
| 4 | **Echeloned attack + reserves** (lead echelon, follow-on, uncommitted reserve) | 🟡 Multi-axis, no echelon. `PoiOffensiveBotModule` splits the army across score-floating axes (anti-death-ball; module header, `:179+`), each axis a single wave. No reserve concept — every unit is committed. | 260719 §4 reserve fraction (`PoiGoalGuard`-marked `reserve`, released on penetration). Not in the 260722 SPEC. | ❌ No reserve, no echelon depth. An army with nothing held back can't reinforce success or plug a breach. |
| 5 | **Defense in depth / engagement areas / kill zones** | 🟡 Single reactive line. `LayeredDefenceBotModule` fills the `InfluenceMap` frontline with reserves: SCREEN units to the contested edge, MAIN-LINE (tanks/arty/AA) to a standoff shifted toward own SR (`LayeredDefenceBotModule.cs:1-80`). `PoiGarrisonBotModule` places 1–3 garrisons per POI. No-ops with no frontline. | 260719 §4: echeloned garrisons (forward screen POI + depth POI via `FrontlineOverlay`), elastic counterattack. Not in the 260722 SPEC. | 🟡 "Layered" today is one band, not depth; no engagement-area/kill-zone design (no pre-planned fires tied to obstacle/canalization). |
| 6 | **Combined-arms pairing** (armor+infantry+indirect in mutual support) | 🟡 Mixed by roster, not paired. `LayeredDefence` splits SCREEN (light inf) vs MAIN-LINE (everything heavy) into two bands (`:50-64`) — a rough echelon, but no tank-infantry *teaming*, no infantry screen for advancing armor. Offense axes pull all types together but move them as one blob. | 260719 §7 mounted-vs-dismounted posture selector. L3 stances shared across types. Not explicitly in the 260722 SPEC. | ❌ No unit-pairing/task-org. Unsupported armor creeping forward (see critical pass §2-C) is the classic doctrinally-wrong look. |
| 7 | **Casualty response — break contact / consolidate** | 🟡 At squad level only. `AttackOrFleeFuzzy.CanAttack` decides attack-vs-flee from own/enemy health + relative power/speed (`AttackOrFleeFuzzy.cs:166-182`); losing squads enter `GroundUnitsFleeState` → `FindSafestRetreatCell` (`GroundStates.cs:270-275`) → `GroundUnitsRegroupState` (re-mass, re-engage, or dissolve, `:299-381`). **No per-unit HP-flee** — AutoTarget flags the gap itself: "maybe we should automatically run away?" (`AutoTarget.cs:472`). | 260722 **Phase 5**: HP/threat flee (the `:472` TODO), panic reactivation (`SPEC §7`). 260719 §5 culmination/force-preservation (retire failing axes, rotate damaged units out). | 🟡 Squad break-contact + regroup is genuinely good and visible. Per-unit self-preservation is Phase 5. Axis-level culmination not in the SPEC. |
| 8 | **Casualty care / medevac** | ✅ (partial). `HealerAutoTarget` auto-targets wounded, critical-first, capped by engagement stance (`DefensiveRange`), deconflicted 1:1 by `HealerClaimLayer` (survey Q3.4). Reads as medics tending casualties. | — (not a named roadmap item). | ✅ Present and credible. No casualty *collection point* / rearward evac of wounded, but the visible core (medic seeks wounded) exists. |
| 9 | **Logistics discipline — resupply timing / culmination** | 🟡 Reactive only. `AmmoPool.AutoRearmIfAllEmpty` + resupply stances (Auto→`SeekSupplyProvider`, Evacuate→`RotateToEdge`, survey Q3.5); out-of-ammo squad units get `ReturnToBase` (`GroundStates.cs:88,245`). Supply trucks / `SupplyFollowerBotModule` exist. No *anticipatory* resupply, no culmination detection. | 260719 §5: culmination detection + rotate-damaged-out; §6 supply-truck hunting. Not in the 260722 SPEC. | 🟡 Individual resupply works; operational logistics discipline (pull back before culminating, protect own lane, hunt enemy trucks) is unmodelled. |
| 10 | **Terrain use — cover, treelines, reverse slope** | ❌ for positioning. Cover cells are queryable (`Map.DensityLayer`, `CohesionMoveModifier.CoverScore cs:156`), LOS/shadow real (`ShadowLayer`), but nothing makes a unit *seek* cover or face known threat. `CohesionMoveModifier` uses cover cells only to interpret a *player's* grouped click. | 260722 **Phases 1–3** (critical path): sighting/threat + cover-edge affordance layers, a positioning executor, Engagement-stance mapping (Hunt=edge toward threat, Defensive=cover away, `SPEC §4`), shipped default-ON. | 🟡→❌ The headline feature. Substrate half-exists; the executor is the plan's core. **The stance→cover-side mapping is doctrinally inverted — critical pass §2-A, the single most important finding.** |
| 11 | **Artillery employment — pre-registered fires, counter-battery, shoot-and-scoot / displacement** | ❌ Artillery is treated as just another main-line gun: `LayeredDefence` lists it under `MainLineUnitTypes` and parks it at a standoff (`LayeredDefenceBotModule.cs:54-59`). **A repo-wide grep for artillery/indirect/counter-battery bot logic returns only that unit-type list — there is no fires module.** No displacement after firing, no counter-battery, no pre-planned/registered fires. | 260719 §3: fires-first, shoot-and-scoot displacement, counter-battery via threat-map arty classifier. **Not in the 260722 SPEC at all.** | ❌ The biggest content gap. Fires are *the* modern casualty producer; static arty that trundles up and sits is the loudest "not a real battlefield" tell — critical pass §3-G. |
| 12 | **Air-ground integration** (CAS synchronized with ground scheme of maneuver) | ❌ Parallel, uncoordinated. `HelicopterSquadBotModule` runs attack/scout/transport heli squads as an independent module (survey Q5); no synchronization with ground axes / no CAS-on-call for a stalled axis. | — (not addressed in the 260722 SPEC; not a 260719 headline). | ❌ Helis fight their own war. No air-ground task organization. Medium visibility (helis already *look* active), but the *coordination* is absent. |
| 13 | **Dispersion under observation → mass at the decisive point** (modern anti-fires survival) | 🟡 Built but gated OFF. `PoiOffensiveBotModule` has the dispersion doctrine (`ApproachCohesion=Spread` en route → `AssaultCohesion=Tight` inside `AssaultRadiusCells=15`, `:96-106`) but behind `CohesionSwitchEnabled=false` (`:94`) and **benchmark-negative** (~−$1,500, survey Q2). Meanwhile the *unconditional* `CohesionMoveModifier` over-spreads every group move (Phase-0 just bounded it). | 260722 **Phase 0** bounded the over-spread; 260719 §2 is the spread-to-move/mass-to-assault doctrine. | 🟡 Ironic state: the code exists, is the *most visible* realism change, and is switched off because it lost the benchmark. Needs re-pricing, not re-building. |
| 14 | **Mission command / decentralized execution** (intent down, execution local) | 🟡 Architecturally begun. Score-floating axes = central intent (`PoiMap`) + local execution; `PoiGoalGuard` ledger commits units so modules don't fight over them (module header). But squad FSMs still re-issue `queued:false` orders ~every 75 t (survey Q5) — central micro that thrashes local autonomy. | 260722 **§2 contract**: upper layers command *intent*, L3 owns *execution* within a leash, registered in the ledger; 260719 §8. | 🟡 The right architecture is chosen and half-wired; the L3 executor + ledger discipline is what makes it real (Phases 2–4). |
| 15 | **Logistics as objective — SR-deny / lane interdiction** | ✅ (partial, live). `PoiMap` discovers + scores enemy SRs and income; `PoiOffensiveBotModule` has a `Pressure` action that parks units in the enemy SR's 10-cell contestation circle (`SrPressureScoreMultiplier :108-115`) — throttles enemy production. | 260719 §6: elevate SR-deny score, reinforcement-lane ambush, supply-truck hunting. | 🟡 SR-deny is coded & credible; lane-ambush + truck-hunting (the dramatic interdiction) absent. WW3MOD models this doctrine *best* of all. |

---

## 2. Critical pass — where the RATIFIED plan would look doctrinally WRONG

The 260722 SPEC is a strong architecture. But three of its concrete design
choices, if implemented literally, would ship *doctrinally inverted* behavior —
and because Phase 3 ships the executor **default-ON to every unit, human and
bot** (`SPEC §7 Phase 3`), these are not experimental curiosities; they become
the game's *default* look. Flagging concretely:

### §2-A — Defensive = "back side of the trees" is doctrinally inverted (TOP FINDING)

`SPEC §4` maps the Engagement stances onto cover as:

> **Defensive (default):** take cover *away* from ThreatDirection — back side of
> the trees, hull-down equivalent; hold and return fire.

Two things are conflated here, and the result is wrong:

- **"Back side of the trees" cannot "return fire."** If a unit sits on the *far*
  side of a treeline from the enemy, the treeline blocks its LOS (that is exactly
  what `ShadowLayer`/`BlocksSight` model). It cannot see or shoot the threat.
  Real defensive posture holds the **forward edge** of concealment — concealed
  *in* the treeline with fields of fire *into* the engagement area — or a
  deliberate **reverse-slope** defense, which is a *different, specific* choice
  (give up long-range observation to ambush at close range as the enemy crests),
  not the same thing as "hull-down."
- **"Hull-down" is not "behind cover."** Hull-down = turret up / hull masked by a
  crest so the unit *presents minimal profile while still shooting*. Equating it
  with "back side of the trees" (no shot at all) blurs a firing posture into a
  hiding posture.

**Viewer consequence:** the headline feature — shipped ON by default — would show
defending units standing with their **backs to the fight, not returning fire**.
That is the *opposite* of impressive; it reads as the units being scared or
broken. The same error poisons the SPEC's aside that "Ambush + cover-back
composes naturally into an ambush posture" (`SPEC §4`): an ambush from behind
concealment *with no fields of fire into the kill zone* is not an ambush.

**Fix (cheap, design-only, do before the Phase-2 executor):** Defensive should
take the **near/forward edge of cover facing ThreatDirection** — concealed but
with LOS/fields of fire — using the `3b` cover-edge-orientation layer's
*threat-facing* edge, the same primitive Hunt uses, just without the forward
creep. Make **reverse-slope** a distinct, terrain-triggered behavior (or an
explicit stance option), not the meaning of Defensive. This inverts one lookup
(threat-facing vs threat-away edge) and is the difference between the treeline
scenario looking *impressive* vs *cowardly*.

### §2-B — "Push through contact without deviating" is the RTS-blob trope it aims to avoid

`SPEC §2` contract pt 2 (in-transit detour semantics):

> Examples: one stance pushes through contact without deviating; another stops
> and seeks nearby cover when under fire, then continues.

The "push through without deviating" mapping (intended for the aggressive stance)
is **react-to-contact done wrong**. Under effective fire, doctrine is *never*
"keep walking in the open on your original azimuth" — it is return fire + take a
covered position + maneuver. A column that strolls through an ambush unreacting
is precisely the "attack-move ignores the machine gun" RTS trope the whole
project is trying to kill. `SmartMove` (`cs:49,75`) at least lets it *shoot*
while walking, so it isn't fully passive — but not seeking cover under effective
fire still looks dumb, and on the aggressive stance especially it will read as
suicidal.

**Fix:** the aggressive (Hunt) detour should be *aggressive react-to-contact* —
return fire and **bound toward** the threat using cover, or fix-and-flank — not
"ignore it." Reserve a true "push through, don't stop" behavior for a deliberate
*road-march / movement-under-time-pressure* posture (e.g. behind own lines, or a
breakthrough exploitation), not for a combat stance in contact. Cross-reference
gap #2 (bounding) and #6 (combined arms): a lone Hunt tank creeping cover-to-cover
*ahead of its infantry* is the unsupported-armor look — the leash (`SPEC §2` pt 1)
must also keep Hunt units within mutual-support distance, not just within a radius
of the commanded point.

### §2-C — Bounding is per-unit creep, not a base-of-fire / maneuver split

`SPEC §7 Phase 5` and §4 describe bounding as a unit "creeping forward between
covers within leash." Real bounding overwatch is a **two-element** behavior: a
*base of fire* element is set, weapons oriented on the objective, **while** a
*maneuver* element moves, then they swap. A squad where every unit independently
creeps cover-to-cover looks like a nervous shuffle, not a bound. This is a
lower-priority note (Phase 5 is far out) but worth capturing now so the executor's
data model (which cell is "overwatch," which unit is "moving") isn't painted into
a per-unit-only corner in Phases 2–3.

### §3-F — Phase 4 creates a visible "blind and dumb" window

`SPEC §7 Phase 4` migrates the strategic grids to per-player fog *and* explicitly
accepts "an initial bot-strength dip" and defers the recon/scouting behavior to a
"follow-on behavior cycle." Doctrinally and *visibly*, this is backwards: the
moment you take away the bot's omniscience, if it has **no scouting behavior yet**,
it will march into things it cannot see and get ambushed — it will look *more*
stupid, not more real, for the entire gap between Phase 4 and the recon follow-on.
The realism payoff of fog (scout → find → commit) only materializes when the
scouting behavior ships *with* it. **Recommend folding a minimal standing-scout /
sensor-to-shooter link into Phase 4 itself** (260719 §1), not after — so
omniscience-loss and earned-intel land together.

### §3-G — The ratified roadmap has no fires/artillery phase at all

Fires are the doctrinal center of gravity of modern land war and *the* main
casualty producer (260719 §3). Yet the 260722 SPEC — the *implementation* roadmap
— contains **zero** artillery-employment behavior across all six phases. Today
artillery is only a `MainLineUnitTypes` string that gets parked at a standoff and
left static (`LayeredDefenceBotModule.cs:54-59`); grep confirms no fires module
exists (gap #11). Static guns that never displace, never counter-battery, and
fire nothing pre-planned are the loudest single "this is not a modern battlefield"
tell a viewer will notice. This omission is a scoping choice (the SPEC is about
the stance/positioning split), but it should be an *explicit* acknowledged gap
with a home, not a silent absence — see recommendation #2.

---

## 3. Ranked gap list — by viewer-visible realism impact per effort

Ranked by: **how dumb its absence looks / how impressive its presence looks**
(Vis), against rough **implementation surface** (layer + systems it rides), with
a suggested **phase**. Highest impact-per-effort first.

| Rank | Gap | Viewer visibility | Impl surface (layer / systems) | Suggested phase |
|---|---|---|---|---|
| **1** | **Fix Defensive/Ambush cover-side mapping** (§2-A) — face the threat from the forward edge, not hide behind it | **Very high** — it flips the *default* posture of every unit from "backs to the fight" to "holding the line." Absence looks cowardly; presence looks disciplined. | **L3 / design.** Near-zero code: invert one edge lookup against the `3b` cover-edge-orientation layer. Do it *before* the Phase-2 executor is written. | **Phase 2 (design gate)** |
| **2** | **Fires employment** (#11, §3-G) — fires-first positioning + shoot-and-scoot displacement + (later) counter-battery | **Very high** — gun-lines pinning infantry then displacing vs guns trundling up and dying. The central casualty exchange. | **L2/L3.** Fires-first = offense-module positioning weight; shoot-and-scoot = per-unit fire-then-move (Patrol/move plumbing) riding existing suppression; counter-battery needs a threat-map arty classifier (heavier). | **New phase (post-Phase-3), classifier deferred** |
| **3** | **Recon-strike link folded INTO Phase 4** (#3, §3-F) — standing scout + ThreatMap-freshness→PoiMap targeting, shipped *with* the fog migration | **High** — "scout, find, commit" is the visible payoff of fog; without it Phase 4 just looks blind. | **L1.** `PoiMap` freshness term + standing recon assignment in the offense module; both substrates (`ThreatMapManager`, scout modules) exist. | **Phase 4 (bundle, don't defer)** |
| **4** | **Aggressive react-to-contact** (§2-B) — return fire + bound/flank, not "walk through the ambush" | **High** — the difference between the anti-blob promise and the blob itself. | **L3.** Corrects the in-transit detour semantics for the Hunt stance; rides the same executor + `SmartMove`. | **Phase 2–3 (executor design)** |
| **5** | **Dispersion doctrine re-priced** (#13) — spread-to-move/mass-to-assault, already coded, currently OFF & benchmark-negative | **Very high** — dispersed columns converging on a point is the iconic modern-advance look; but it's *built*, so this is a benchmark/tuning task, not new code. | **L2.** `PoiOffensiveBotModule` `CohesionSwitchEnabled` + the now-bounded cohesion; needs a re-verify run to fix the −$1,500 regression (likely the *unbounded* footprint that Phase 0 just fixed was the cause). | **Re-verify next benchmark cycle** |
| **6** | **Reserves + echelon depth** (#4, #5) — hold an uncommitted fraction; release on penetration; counterattack to retake | **High** — layered lines and counterattacks that *retake* ground look far more real than a single blob; the reserve plugging a breach is dramatic. | **L1/L2.** Reserve ledger = small `PoiGoalGuard` extension; elastic counterattack extends `LayeredDefenceBotModule`. | **Post-split (260719 §4)** |
| **7** | **Reinforcement-lane ambush + supply-truck hunting** (#15) — interdict units before they reach the front | **High** — sieging the SR and ambushing a reinforcement column *reads as* modern interdiction; WW3MOD models this best. | **L1.** Derive the enemy lane from their SR edge; seed an `Ambush`-stance force held via `PoiGoalGuard`. | **Post-recon (260719 §6)** |
| **8** | **Combined-arms pairing** (#6) — infantry screen for armor; keep Hunt units in mutual support | **Medium-high** — unsupported armor creeping alone is a specific "wrong" look; paired advance looks trained. | **L2/L3.** Task-org in the offense/transport modules + a mutual-support term in the L3 leash. | **Post-executor** |
| **9** | **Suppress-then-assault sequencing** (#1) | **Medium-high** — visible as fire pinning defenders prone *before* the assault element moves. Mechanic already there; needs sequencing. | **L2.** Offense-module micro: hold assault until suppression is on the objective. | **Post-fires (with #2)** |
| **10** | **Per-unit self-preservation / culmination** (#7, #9) — HP-flee, retire failing axes, rotate damaged units out to bank budget | **Medium** — you notice the AI *not* suiciding; subtle but it stops the worst "dumb" moments. | **L3 + L1.** The `AutoTarget.cs:472` TODO (Phase 5) + per-axis combat-power tracking (260719 §5). | **Phase 5 / offense module** |
| **11** | **Air-ground integration** (#12) — CAS on-call for a stalled axis | **Medium** — helis already look busy; the *coordination* is the subtle upgrade. | **L1/L2.** Link `HelicopterSquadBotModule` to axis state. | **Later** |
| **12** | **Bounding as base-of-fire/maneuver split** (#2, §2-C) | **Medium** — a real bound vs a nervous shuffle; only matters once cover-seeking is solid. | **L3.** Executor data model (overwatch vs moving element). | **Phase 5** |
| **13** | **Feints** (260719 §9) | **Low-medium** — satisfying when it lands, subtle otherwise; needs reserves first. | **L1.** Cheap feint axis in the offense module. | **Last** |

Note on #8/#12 pairing with #1/#4: the four L3 items (cover-side, react-to-contact,
combined-arms leash, bounding) all ride the *same* Phase-2 positioning executor.
Getting the **semantics right in that one trait's design** (§2-A/B/C) is worth
more than any single downstream feature, because it is shipped default-ON to
everyone and sets the game's baseline "look."

---

## 4. Top-3 recommendations

### 1. Correct the Defensive/Ambush cover-side mapping *before* writing the Phase-2 executor.

This is the highest impact-per-effort item in the entire audit, and it costs a
paragraph of spec, not a sprint. As written (`SPEC §4`), the *default* Engagement
stance would place every defending unit — human and bot — on the far side of cover
from the enemy, unable to see or return fire, backs to the fight. Shipped ON by
default in Phase 3, that becomes the game's baseline defensive look, and it reads
as cowardice, the exact opposite of the "militarily believable" north star.
Redefine Defensive as *forward-edge-of-cover facing the threat* (concealed, with
fields of fire — the same threat-facing `3b` edge lookup Hunt uses, minus the
creep), and make reverse-slope a distinct terrain-triggered option. Same for the
Ambush composition: an ambush is fought from the near edge into a kill zone, never
from behind concealment. One inverted lookup separates the headline feature
looking *impressive* from looking *broken*.

### 2. Give fires/artillery an explicit home on the roadmap — at minimum fires-first positioning + shoot-and-scoot displacement.

Fires are the doctrinal center of gravity of modern land war and the main
casualty producer, yet the ratified 260722 SPEC contains no artillery behavior in
any of its six phases, and today artillery is just a `MainLineUnitTypes` string
parked at a standoff (`LayeredDefenceBotModule.cs:54-59`) — grep confirms no fires
module exists. Static guns that never displace and fire nothing pre-planned are
the single loudest "not a real battlefield" tell. The cheap, high-visibility core
rides systems that already exist: bias the offense module to stand indirect-fire
units *behind* the lead element, and add a per-unit "fire a burst, displace a few
cells" rule on the existing move plumbing — the suppression system already makes
that fire *pin* defenders prone even when it doesn't kill, which visibly opens an
assault. Counter-battery (needs an enemy-arty classifier on the threat map) can
defer. This doesn't have to enter the L1/L2/L3 split; it needs to stop being a
silent omission and become a scheduled phase.

### 3. Fold a minimal recon behavior into Phase 4's fog migration instead of deferring it.

Phase 4 deliberately strips the bot's omniscience (`InfluenceMap.cs:92` /
`ThreatMapManager.cs:89` become per-player) and *accepts* an initial strength dip
while deferring scouting to a "follow-on cycle." Visibly, that ordering is
backwards: a bot that just lost its all-seeing eye and has no scouting behavior
will march blind into ambushes and look *dumber* than the omniscient version it
replaced, for the entire gap until the follow-on lands. The realism dividend of
fog — units that scout, find, then commit, the master pattern of the Ukraine war
(260719 §1) — only appears when the scouting behavior ships *with* the migration.
A standing scout/heli task ahead of each axis plus a `PoiMap`-freshness term that
raises the score of freshly-observed clusters is modest work on substrates that
already exist, and it converts Phase 4 from a "declared strength dip" into a
visible capability upgrade.

---

## 5. Honest limits (carried from 260719 §11, still valid)

Physical decoy replicas, deep operational interdiction (no operational rear on one
map), minefield-centric depth (content/pathing cost), and true morale/rout
modeling are all judged low-value-per-effort in this format. Suppression + ordered
withdrawal (recommendation-adjacent) is the right stand-in for morale; the
behavioral feint axis captures the useful 80% of deception. These are not gaps to
close — they are correctly out of scope.
