# AI Foundation — the basics, 2026-05-11

> Goal of this doc: lay out what's *possible* in RTS AI today, what fits **WW3MOD specifically**, and a phased plan that delivers visible wins each step. You read this, then we re-decide the foundation-reset question with full info.
>
> **Not a plan.** Plans go in `WORKSPACE/plans/`. This is the briefing.

---

## 1. Where we are today

### Existing scaffolding (good news)

`engine/OpenRA.Mods.Common/Traits/BotModules/` — ~6.1k LOC, all running:

| Module | What it does | Quality |
|---|---|---|
| `ModularBot` (player trait) | The bot itself — drives the `IBotTick` heartbeat | OK |
| `BaseBuilderBotModule` | Decides what buildings to place | OK |
| `UnitBuilderBotModule` | Picks units to queue from production buildings | OK |
| `SquadManagerBotModule` (508 LOC) | Forms squads, dispatches attacks, multi-axis split exists | Shallow — fixed thresholds, simple FSM |
| `Squad` + states (~1.5k LOC) | Per-squad FSM: Idle / Attack / Flee / Regroup | Hard-coded transitions, no real tactics |
| `ScoutBotModule` | Sends a humvee/BTR somewhere unseen | Picks random unseen cell, no terrain awareness |
| `CaptureManagerBotModule` | Sends engineers/technicians to capture | Doesn't weight capture value vs cost |
| `GarrisonBotModule` | Stuffs infantry into defense buildings | Works |
| `SupplyFollowerBotModule` | TRUKs trail the army | Works |
| `AdaptiveProductionBotModule` | Counter-builds vs scout sightings | Threshold-based, no real composition model |
| `HelicopterSquadBotModule` | Helicopter-specific squad logic | Inherited from OpenRA |
| `SupportPowerBotModule` | Fires support powers | Inherited |
| `BotBlackboard` | Cross-module task posting | Used lightly |
| `ThreatMapManager` (428 LOC) | **8×8 coarse influence grid**, military + economic layers | Too coarse, only used in a couple of spots |

### What's missing

1. **No real map analysis.** AI doesn't know where chokepoints are, what counts as "the frontline", or how long reinforcements take from a given Supply Route. The threat grid is the only spatial knowledge and it's a flat 8×8 cell-blob.
2. **No strategic intent layer.** Every decision is local: squad picks own target, builder picks own next unit, scout picks random cell. Nothing answers "what are we *trying to do this minute*?".
3. **Squad tactics are an FSM, not behaviors.** Squads attack-move and flee. They don't coordinate, time pushes, screen with infantry while vehicles flank, or retreat with covering fire.
4. **No model of the enemy.** AdaptiveProduction counter-builds against last few sightings; nothing tracks comp over time, infers tech tier reached, predicts the next push.
5. **No personality at the strategy level.** Rush/Normal/Turtle differ in YAML build orders but make the same tactical calls at runtime.
6. **WW3MOD economics are second-class.** No code currently reasons about Supply Route reinforcement distance, capturable income value (or its absence in zero-income-mod games), or the fact there's no tech tree to plan around.

---

## 2. What modern RTS AIs actually do

A quick tour, with which pieces fit us and which don't.

### Map analysis — *what to do here*

**BWEM (Brood War Easy Map)** — decomposes the walkable map into regions connected by chokepoints. Used by every competitive BWAI for ~15 years. Region = a polygon of passable terrain; chokepoint = the narrow corridor between two regions. Gives the AI a graph it can reason about: "go from my base region to the contested chokepoint between region B and C", "defend chokepoint X", "this region has 2 exits — I need to cover both".

→ **Strong fit for WW3MOD.** Maps are tile-based and have meaningful terrain. Static analysis = compute once, cache. **This is the single most valuable Phase 1 deliverable.**

### Influence / threat maps — *where is it safe / dangerous*

A grid (per-cell or per-region) of weighted unit values. Friendly units add positive influence; enemy units add negative. Gradient gives "walk toward safety" without needing pathfinding queries.

→ **Already exists** (`ThreatMapManager`) but at 8×8 cell granularity it's a blunt instrument. Phase 1 swap: per-region values from the map analysis, plus a finer dynamic 4×4 grid for tactical use.

### Frontier / tension map — *where is the war*

A derived layer: cells where both friendly and enemy influence are nonzero. That's where fighting happens. Useful for: choosing scout targets (right behind the frontier = high-value intel), siting Supply Routes (away from frontier = safer reinforcement), picking attack axes (around the frontier flanks, not into the middle).

→ **Easy add** once influence layers exist.

### Hierarchical Task Network (HTN) — *plan decomposition*

High-level goal → decomposed into ordered subgoals → each decomposed further. Like AI planning in F.E.A.R., Killzone 2, Total War.
- "Win the game" → "control more income than enemy" + "destroy enemy supply route"
- "control more income" → "capture neutral oilbs" + "defend my oilbs"
- "capture neutral oilbs" → "send engineer to closest" + "secure with squad"

→ **Good fit for the strategic layer.** Implementation is just a tree of methods + ordering + reconsider-every-N-ticks. We don't need a fancy planner library.

### Goal-Oriented Action Planning (GOAP) — *A\* over states*

Like HTN but more flexible: Goal + available actions (with preconditions/effects) + A* search over states. F.E.A.R., Tomb Raider Reboot. More expensive at runtime.

→ **Overkill for us.** HTN gives 80% of the benefit at 20% of the cost. Skip.

### Behavior trees — *tactical execution*

The standard for unit/squad AI in modern games. Nodes are conditions and actions; the tree is evaluated top-down each tick. "Sequence: Has target? → In range? → HP > 30%? → Attack". Easy to author, easy to extend, easy to debug visually.

→ **Strong fit for the tactical layer** — replaces the current handwritten FSM. Phase 3.

### Utility AI — *score options, pick best*

Each option scores itself based on world state (e.g. "Capture oilb-12" scores higher if our income is low, the oilb is close to our forces, and a capture-friendly unit is free). Top score wins. Smooth blending — no hard thresholds.

→ **Fits the strategic layer perfectly** for "what should we do next?". Use it inside the HTN at decision branches. Used heavily in Civ, Total War, Endless Legend.

### Replay-based opening books

Top BWAIs (LocutusAI, Iron, …) mine pro-game replays for the first 2–4 minutes of build order. Out of scope: we don't have a replay corpus and even if we did, WW3MOD doesn't have factory build-orders to mine.

→ **Skip.** We'll hand-author opening "scripts" per personality.

### Strategy classifier — *read the enemy*

Look at enemy comp, tempo, buildings → classify as rush / boom / tech / turtle / air → choose counter strategy. Used by AlphaStar, IRA bot, all serious BWAIs.

→ **Yes, in Phase 2.** A simple classifier (5–10 features → label) suffices. Better than today's "saw 3 tanks → build AT".

### What we're *not* doing

- **Machine learning** (reinforcement learning, neural nets). Top-tier work but requires self-play infrastructure we don't have. Out of scope.
- **Symbolic logic / Prolog-style planners.** Overengineered for action games.
- **Multi-agent negotiation between bot players.** Allied bots can share intel via blackboard but won't formally negotiate plans.

---

## 3. What's different about WW3MOD

These aren't optional — they're load-bearing. Any AI that ignores them will be obvious-bot.

### Supply Routes are the sector beachhead — not a buildable expansion

**Read [`DOCS/reference/supply-route.md`](../../DOCS/reference/supply-route.md) first.** The SR is a starting flag near each player's spawn edge, not a Red Alert-style Construction Yard. You don't build it, you don't place it, you can't build a second one — every player spawns with exactly one, fixed, near the map edge.

What the AI should reason about:
- **Defending the home SR** is existential. It's indestructible (no damage kills it) but can be captured by engineers/technicians, and is slowed by `SupplyRouteContestation` (graduated production slowdown when enemies stand inside the 10-cell circle).
- **Pressuring the enemy SR** is the highest-value spatial objective. A unit inside the enemy contestation circle slows their entire production.
- **Capturing neutral SRs**, when the map has them, is the only "expand" decision. Worth more than a capturable income building only if the new SR's reinforcement edge gives a better angle than the home SR.
- **Rally point management** is the only positioning decision an AI makes about its own SR. Move the rally as the front shifts.
- **Reinforcement-lane awareness** — units walk a path from map edge → SR. That path is ambushable in both directions.

What the AI must never assume (recurring trap):
- "Build a second SR to expand." → Not buildable.
- "Place the SR closer to the front." → No placement choice.
- "Destroy the enemy SR." → Indestructible; capture and contestation only.

The AI YAML currently lists `supplyroute` as `ConstructionYardTypes` / `VehiclesFactoryTypes` / `BarracksTypes` — that's an OpenRA-trait integration so production queues wire up, **not** a statement that the SR is a factory. The strategic layer must look past it.

### Capturables drive income — but only if the lobby says so

Oilbs, biolabs, missiles, hospitals etc. give passive income *if the lobby's "Capturable income" multiplier is nonzero*. The AI **must read the lobby setting** and adapt:
- 100% mod → capturables are the primary income → fight for them
- 0% mod → capturables are useless cosmetic actors → ignore entirely
- Sliding scale in between

Same for passive income: a high passive base income makes capturables relatively less important.

→ **Implementation:** strategic planner reads `World.WorldActor.Trait<MapOptions>()` or equivalent at game start, computes a `CapturableValuePerSecond` metric, feeds it into the utility scores.

### No tech tree → no prereq planning

Tech levels are time-gated (or event-gated), not building-gated. AI doesn't need to plan "barracks → war factory → radar dome → unlock helipad". It just builds what its current tech level allows.

→ **Simpler than vanilla OpenRA AI.** Most of `BaseBuilderBotModule`'s prereq logic is dead weight here.

### HPAD/AFLD are optional rearm buildings, not production prereqs

Aircraft can be produced without them; helipads make rearming faster. AI today doesn't know this — it inherits OpenRA assumptions.

→ **Phase 2:** strategic planner treats air builds as cheap *if* the AI also wants to ship a helipad; otherwise still possible just slower-rearm.

### Map-edge spawning = ambushable reinforcements

Units come from the map edge nearest the SR. Smart AI exploits this both ways: **ambush enemy reinforcement lanes** (huge edge in 1v1) + **shield own lanes** with screening units.

→ **Phase 3:** "reinforcement lane" is a first-class spatial concept the tactical layer reasons about.

### Garrisoned buildings are powerful

Already have a `GarrisonBotModule` but it doesn't know which buildings are *valuable to garrison* (close to chokepoint, overlooking a SR, on a capture target).

→ **Phase 3 polish.**

---

## 4. Proposed architecture — three-layer brain

```
┌─────────────────────────────────────────────────────────────────┐
│ PERCEPTION  (always-on, world-watching)                         │
│  ├─ Map analysis        (static, computed once, disk-cached)    │
│  │   regions, chokepoints, edge→region distance, defendability  │
│  ├─ Influence layers    (dynamic, ~30 ticks)                    │
│  │   military, economic, frontier, exploration age              │
│  └─ Enemy intel         (continuous, event-driven)              │
│      composition tracker, last-seen positions, strategy class   │
└──────────────────────────────────┬──────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────┐
│ STRATEGY  (slow loop, ~5s)                                      │
│  ├─ Strategic Planner   (HTN + utility scoring)                 │
│  │   reads perception + lobby settings + personality            │
│  │   emits a Plan: goals + priorities + budget allocation       │
│  └─ Plan                                                        │
│      attack-axes, capture-targets, build-comp goal,             │
│      scouting goals, defense priorities                         │
└──────────────────────────────────┬──────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────┐
│ TACTICS  (fast loop, ~30 ticks)                                 │
│  ├─ Squad orchestrator  (behavior trees, replaces FSM)          │
│  ├─ Build executor      (current UnitBuilder/BaseBuilder)       │
│  ├─ Scout executor      (current ScoutBotModule)                │
│  ├─ Capture executor    (current CaptureManagerBotModule)       │
│  ├─ Supply executor     (current SupplyFollowerBotModule)       │
│  └─ Garrison executor   (current GarrisonBotModule)             │
└─────────────────────────────────────────────────────────────────┘
```

**The bridge between Strategy and Tactics is the Plan object** + the existing Blackboard (evolved to carry richer claims).

### Folder layout

```
engine/OpenRA.Mods.Common/
├── AI/                              ← NEW
│   ├── Perception/
│   │   ├── MapAnalysis/             ← Phase 1
│   │   │   ├── Region.cs
│   │   │   ├── Chokepoint.cs
│   │   │   ├── MapAnalyzer.cs       (world trait)
│   │   │   └── MapAnalysisCache.cs  (on-disk: <map>/aianalysis.bin)
│   │   ├── InfluenceLayers/         ← Phase 1
│   │   │   ├── InfluenceMap.cs      (replaces ThreatMapManager)
│   │   │   └── FrontierMap.cs
│   │   └── EnemyIntel/              ← Phase 2
│   │       ├── EnemyCompositionTracker.cs
│   │       └── StrategyClassifier.cs
│   ├── Strategy/                    ← Phase 2
│   │   ├── StrategicPlanner.cs      (player trait, the brain)
│   │   ├── Plan.cs                  (current intent object)
│   │   ├── Goals/                   (HTN goal tree)
│   │   │   ├── WinGameGoal.cs
│   │   │   ├── ControlIncomeGoal.cs
│   │   │   └── …
│   │   └── Utility/
│   │       └── ActionScorer.cs
│   ├── Tactics/                     ← Phase 3
│   │   ├── BehaviorTrees/
│   │   │   ├── Node.cs (base)
│   │   │   ├── SquadAssaultTree.cs
│   │   │   └── …
│   │   └── SquadOrchestrator.cs     (replaces SquadManagerBotModule)
│   └── Debug/                       ← Phase 1+
│       └── AiDebugOverlay.cs        (toggleable in-game visualizer)
└── Traits/
    └── BotModules/                  ← EXISTING — executors stay here, retire one at a time
```

---

## 5. Phasing — every phase a real win

The phases below are sized to **ship a noticeable AI improvement per phase**, even if we stop after any of them. After each phase, if the user wants to revert or rip out, the rest of the bot keeps running on its previous implementation.

### Phase 1 — Perception foundation + visualizer

**Goal:** the AI *sees* the map. The user can see what the AI sees.

- `MapAnalyzer`: regions + chokepoints (BWEM-lite algorithm), with on-disk cache (`aianalysis.bin` next to `shadows.bin`, regen via `utility.sh`).
- New `InfluenceMap` replaces `ThreatMapManager`: per-region influence + a finer 4×4 dynamic grid; query API for "is cell X friendly/enemy/contested?".
- `FrontierMap`: derived layer of cells where both sides have influence.
- **`AiDebugOverlay`** — toggleable in-game (lobby option or dev hotkey) that draws regions, chokepoints, influence heatmap, current AI plan on top of the game. **This is critical** — without it we can't tune the AI.
- One small executor rewired to consume the new perception: `ScoutBotModule` switches from "random unseen cell" to "region the AI hasn't visited that's adjacent to a contested chokepoint". Visible behavior change for the user.

**Ship criterion:** start a game vs Normal AI, toggle the overlay, see regions and chokepoints. The bot's scout moves with apparent purpose.

**Effort:** 3–5 focused sessions. The map decomposition is the big algorithmic piece; the overlay is the visible deliverable.

### Phase 2 — Strategic intent layer

**Goal:** the AI has a **plan** that you can read on the overlay.

- `EnemyCompositionTracker`: rolling estimate of enemy comp from visibility + intel events.
- `StrategyClassifier`: 5–10 features → label (rush / boom / tech-air / turtle / mass-armor).
- `StrategicPlanner` trait per AI player. Every 5s:
  - Read perception + lobby settings (`CapturableIncomeMultiplier`, `PassiveIncomePerSec`, game length, etc.).
  - Score available high-level actions via utility.
  - Emit a `Plan` (attack-axis, capture-target list, comp-goal, defense priorities, scouting goals).
- Existing modules become Plan consumers: `UnitBuilderBotModule` queues units toward `Plan.ArmyCompGoal`; `CaptureManagerBotModule` targets `Plan.CaptureTargets` in priority order; `SquadManagerBotModule` attacks along `Plan.AttackAxes`.
- Debug overlay extended to display the current Plan.

**Ship criterion:** flip lobby income-mod to 0 → AI stops trying to capture oilbs and pours into army. Set it to 200% → AI fights desperately for capturables. Both visible on the overlay.

**Effort:** 4–6 sessions. The HTN goal tree is straightforward; tuning the utility weights is most of the time.

### Phase 3 — Coordinated tactics

**Goal:** AI plays *human-like* — flanks, screens, retreats with cover, times pushes.

- Replace Squad FSM with behavior trees. Trees per squad type (Assault / Air / Helicopter / Protection / Capture-escort).
- **Multi-axis synchronization**: an attack with 2+ squads waits for all to be in position before pushing. The Plan owns the "push when ready" gate.
- **Reinforcement-lane awareness**: tactical layer reasons about enemy spawn edges → ambush squads, scout posts.
- **Screen + flank**: in a mixed squad, infantry advance first to absorb fire, vehicles flank around terrain (uses map analysis to find the flanking route).
- **Retreat-with-cohesion**: squad pulls back together, slowest unit's pace, lays down fire at pursuers.

**Ship criterion:** AI pushes feel *coordinated*. Watching a replay, you can identify the maneuver — "they flanked left while their infantry held the center". This is the most expensive phase and the most visible to playtesters.

**Effort:** 5–8 sessions. Behavior trees are easy; tuning the timed-push and flanking is where the time goes.

### Phase 4 — Personality differentiation

**Goal:** Rush / Normal / Turtle play *fundamentally* differently, not just with different build orders.

- Each personality has its own utility weight set in the strategic planner (Rush weights early-attack high, Turtle weights defense + capturables high, Normal balanced).
- Personality-specific HTN method preferences (Rush will skip the "secure capturables" subgoal in early game; Turtle does it first).
- Personality-specific behavior tree variants (Rush squads engage at lower force-ratio thresholds; Turtle squads retreat at higher HP thresholds).
- Lobby gets a difficulty slider that's *separate* from personality — Easy/Normal/Hard scales economy and reaction speed without changing playstyle.

**Ship criterion:** Three games on the same map vs different personalities feel like three different opponents.

**Effort:** 2–3 sessions.

### Phase 5 — Tournament mode, replay analysis, polish

**Goal:** tooling for sustained tuning.

- AI vs AI tournament harness (extends `tools/autotest/`): batch run many games, log winners, surface stats.
- Replay scrubber for AI Plans (record plan transitions over time, replay them on the debug overlay).
- Performance pass — make sure the brain doesn't tank tickrate.
- Lobby polish — difficulty slider, personality selector, optional "honest fog" toggle (AI plays without omniscient info).

**Effort:** 2–4 sessions, but largely orthogonal to gameplay — can interleave with other v1 work.

### Stretch: Phase 6 — Adversarial learning loop

If everything above lands and we want more: run thousands of headless AI-vs-AI games (offline tool), record outcomes, hand-tune utility weights based on win rates by personality and map. **Not committed.** Mentioned only because the tournament harness from Phase 5 makes it cheap if we ever want it.

---

## 6. Map analysis storage — pick per layer

The user wanted "all of them" — different layers, different mechanisms. Concretely:

| Layer | When | Where | Why |
|---|---|---|---|
| Region decomposition | Once per map version | Disk (`aianalysis.bin`) | Expensive (~tens of ms for a 256×256 map), totally static |
| Chokepoints | Once per map version | Disk (same file) | Derived from regions, same boat |
| Edge→region reinforcement-time table | Once per map version | Disk (same file) | Computed once, used every Plan tick |
| Defendability score per region | Once per map version | Disk (same file) | Same |
| Influence/threat grid | Every 30 ticks | RAM | Cheap, dynamic, no point persisting |
| Frontier map | Every 30 ticks | RAM | Derived from influence |
| Exploration age per cell | Every tick (per scout sighting) | RAM | Already tracked, keep it |
| Enemy composition tracker | Event-driven (intel events) | RAM | Continuous learning |
| Enemy strategy classification | Every 5s | RAM | Cheap, derived |

`aianalysis.bin` regeneration follows the `shadows.bin` pattern — invalidated by map edits, refresh via `./utility.sh --regen-aianalysis <map>`.

---

## 7. Risks and open questions

### Risks

1. **Performance.** A strategic planner running utility scoring + HTN every 5s for each AI player can add up. **Mitigation:** stagger planner ticks across players, budget the planner to N ms per call, profile in Phase 2.
2. **Debugger is mandatory.** Without the overlay, tuning is guesswork. **Mitigation:** the overlay ships in Phase 1, not Phase 5.
3. **Regression vs current AI.** The current AI is mediocre but works. If Phase 2 ships a Strategic Planner that makes worse decisions than today's per-module logic, the AI temporarily gets *worse*. **Mitigation:** keep the existing modules running on the old logic, only wire one consumer at a time to the Plan, with a feature-flag to fall back.
4. **YAML config sprawl.** Each new system adds YAML knobs. **Mitigation:** keep personality differentiation in a single `ai-<name>.yaml` per personality, not scattered.

### Open questions for you

1. **Foundation reset aggressiveness** (re-ask from earlier) — does the three-layer architecture above feel right? If yes, "evolve in place" is the right answer to that question (we're keeping the executors and adding layers above). If you want a cleaner break, we go option 2 (greenfield brain).
2. **Difficulty levels for the lobby.** How many tiers? Names? Suggested: Easy / Normal / Hard / Brutal, where Easy throttles reaction speed + economy, Normal is honest, Hard gets ~10% income bonus, Brutal gets omniscient vision. This is partly a balance / playtesting question.
3. **"Honest fog" by default** — should the AI play with the same vision as the player by default (fair), with omniscience as an opt-in cheat for higher difficulties? Today the AI is omniscient; switching to honest fog is a balance change that'll make the AI weaker at first.
4. **Waypoint / Planning mode for the player** — you mentioned this is starting in a separate chat. The architecture above naturally supports it: a player-side waypoint queue is just a Plan that the player edits. **Question:** do you want the AI infrastructure built so it can share data structures (Plan, attack-axis, capture-target) with the eventual player-side feature, or are they parallel systems? Sharing means a tighter design dependency; not sharing means duplicate concepts.
5. **Per-map opening books.** Some maps have obvious openings ("expand toward the central oilb"). Do we want to encode any map-specific opening hints, or insist the AI figures it out every game? Lean toward: figure it out, with a clean fallback. Map hints can be a Phase 5 polish.
6. **Allied AI behavior in coop.** When the human and an AI ally share a team — should the AI defer to the player (defensive, scout-heavy) or play independently? Suggest: defer slightly by default, with a stance setting like "follow me / play independently".

---

## 8. What I recommend doing first

If you agree with the architecture, the cheapest first move is **Phase 1, scoped tight**:

1. New folder `engine/OpenRA.Mods.Common/AI/` with subfolders.
2. `MapAnalyzer` — region + chokepoint decomposition, in-memory only first (no disk cache yet — that's a polish iteration once the algorithm is right).
3. `AiDebugOverlay` — toggle hotkey (probably Shift+F12 or similar), draws regions/chokepoints/influence on screen.
4. **Stop.** Demo it. Decide whether the algorithm is good enough before building anything on top.

That's 1–2 sessions and gives us a real artifact: a game window with the AI's mental map drawn over it. Everything else hangs off that.

---

## Appendix — what to read

If you want to go deeper:

- **BWEM library** (Brood War Easy Map): https://github.com/Vibhu-vbh/BWEM-community — region decomposition for SC: BW. The algorithm we're cloning.
- **AIIDE Brood War tournament writeups** — every year's winning bot publishes a postmortem. Search "AIIDE bot postmortem".
- **F.E.A.R. GOAP paper** — Jeff Orkin, MIT. The seminal GOAP paper.
- **Killzone 2 HTN talk** — Game AI Pro 1, "Adversarial Heuristic Search". Free PDF online.
- **Total War Utility AI talk** — Game AI Pro 3, "Utility-Based Decision Making for AI in Total War".

You don't need to read these — I will reference them as needed when we get into each layer.
