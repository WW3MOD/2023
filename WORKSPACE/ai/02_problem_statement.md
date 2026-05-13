# Problem statement

> Before the substrate doc (`03_substrate.md`) and the brain design (TBD), we need to be specific about three things: **what we want the AI to actually do**, **what we have today**, and **the gap between them**. This doc is deliberately scope-limited to that — no solutions, no architecture, no doctrine debates. Just: where are we, where do we want to be, what's blocking the path.
>
> Read this alongside `01_default_ai_explained.md`. That file explains the machinery; this one names the failure modes.

---

## 1. What we want — observable behaviors

These are the behaviors a player should be able to observe in a v2-vs-v2 or v2-vs-human match. None of these are aspirational "human-grade" play — they are the floor.

**A. Holds a coherent defensive line.**
When the bot is on defense (no opportunity to push), its units should distribute along the frontline (as defined by `InfluenceMap` / `FrontlineOverlay`), preferring contested cells where its own density is lowest. No thin spots, no piles. Reserves stay behind the line as a second echelon.

**B. Captures income structures, intelligently.**
TECN units should walk to the highest-income unowned structure first (`logisticscenter` > `bio` > `fcom` > `oilb` > `miss` > `hosp`), avoiding contested zones (≥ N enemies within 6 cells), with an escort when worth it. Once committed to a target, follow through — no thrashing between targets, no order-overwriting.

**C. Ferries infantry forward in carriers.**
Bradleys / BMP-2s / M113s should: pick up infantry from the SR rally area → drive to a frontline gap (thinnest friendly density on the contested band) → unload near cover → return to rally → repeat. Without this, infantry walks 30+ cells solo and arrives stale.

**D. Reacts to attacks by shifting units, not by panicking.**
When a sector is attacked and the bot's local density drops below threshold, reserves should walk toward that sector (not the whole pool — graded shift based on threat). When the attack subsides, units return to their original sector or new assignments. No total-army drain to one flashpoint.

**E. Does not auto-suicide.**
- TECN does not enter a contested oilb alone.
- Tanks do not push solo into known enemy infantry concentrations.
- Helicopters retreat to rearm when ammo low, not "fly straight into the AAA wall".
- Out-of-ammo units do not stand on the line; they retreat to supply.

**F. Uses cover.**
Infantry on the line ends up in `Tree` / `Rough` / `Field` terrain when cells within ~6 cells of their assigned slot allow it. Not standing in open at the slot center.

**G. Uses production budget coherently.**
Calls in unit composition to match what the front needs (more anti-tank if enemy vehicles, more anti-air if enemy helis, more infantry if losing infantry attrition). Does not flood-produce one unit type while the front collapses on a different vector.

**H. Coherent multi-axis play.**
If attacked on flank A, does not strip every unit from flank B. Each sector has a minimum garrison; over-flooding one sector requires deliberate decision, not implicit drainage.

That's the list. Eight observable behaviors. Each one fails today, on at least one map, in at least one repeatable way.

---

## 2. What we have today — the existing layers

Three concentric layers, in the order they were built:

**Layer 1 — Engine stock modules** (described in `01_default_ai_explained.md`).
`BaseBuilder`, `UnitBuilder`, `SquadManager`, `HelicopterSquad`, `CaptureManager`, `Scout`, `Garrison`, `BuildingRepair`, `SupportPower`, `Harvester` (dormant), `McvManager` (dormant), `Minelayer` (dormant). Independent `IBotTick` modules with their own scan countdowns. Coordination via push interfaces + partial `BotBlackboard` use. Each one tested-in-isolation works; the composite is brittle.

**Layer 2 — WW3MOD adaptations** (added over previous months).
`AdaptiveProductionBotModule` (counters enemy comp), `SupplyFollowerBotModule` (TRUKs follow the army), `GarrisonBotModule` and `ScoutBotModule` (WW3MOD variants of stock concepts). All `IBotTick`, all parallel. They opt into the blackboard for unit claims.

**Layer 3 — v2 doctrine attempt** (the current half-built rework, this session and last week).
- `InfluenceMap` world trait — friendly/enemy density grid. Works.
- `FrontlineOverlay` world trait — derived contested band. Works, ships an in-game overlay via `/frontline`.
- `LayeredDefenceBotModule` — assigns idle reserve units to contested cells, scored by friendly thinness. Works on paper; fights `SquadManagerBotModule` for the same idle units.
- `CaptureCoordinatorBotModule` — income-weighted capture target scoring + escort dispatch. Replaces the random-player flaw in legacy `CaptureManager`. Mostly works, but has the order-overwriting bug we've been hunting.
- `MountedTransportBotModule` — pairs IFVs with infantry to ferry them forward. Shipped and theoretically wired; **blocked by `IsIdle` carrier filtering** even after this session's fixes. Carriers either never qualify as candidates, or AutoTarget yanks them away during loading.

The InfluenceMap + FrontlineOverlay halves of v2 are solid. The decision-making halves (LayeredDefence, CaptureCoordinator, MountedTransport) are not.

**The shared coordination point** — `BotBlackboard` — is half-used: unit claim mutex works for the four modules that opted in (Scout/Garrison/Heli/SupplyFollower); task API and intel API have zero readers; SquadManager and CaptureManager ignore the blackboard entirely.

---

## 3. The gap — five concrete root causes

Each one of these maps directly to one or more of the failures we've debugged.

### 3.1 No goal persistence

Every module decides on every scan. There is no data type that says "this unit is doing X until tick T". `Actor.CurrentActivity` is the closest thing, but it's noisy: it flickers to `null` between waypoints, after a Stop order, during turn-in-place, when AutoTarget acquires and releases. Every module that uses `IsIdle` to filter "available units" inherits this flicker.

Direct failures:
- **TECN order-overwriting**: TECN walks toward capture target → activity drops for any reason → next scan, TECN appears idle → CaptureCoordinator picks a new target (possibly the same one) → new `CaptureActor` order → walk re-starts. Looks like "orders gets overwritten" because it is.
- **MountedTransport carrier blocked**: empty Bradley sitting at SR rally → AutoTarget engages distant scout → IsIdle = false → fails candidate filter → never assigned to ferry duty.

### 3.2 No central decider

Each `IBotTick` module independently queries the world, picks units, queues orders. Two modules can want the same unit on the same tick. The reservation API only protects four modules (Scout / Garrison / Heli / SupplyFollower); SquadManager and CaptureManager will sweep claimed units anyway.

Direct failures:
- **SquadManager drains pool**: forms an attack squad with every idle unit, leaving LayeredDefence with no reserves to assign.
- **Bradley stolen by LayeredDefence**: pre-fix this session, LayeredDefence pushed empty Bradleys forward to the front before MountedTransport could see them as transport candidates. We patched this by adding carriers to LayeredDefence's exclusion set — exactly the kind of ad-hoc handshake that doesn't scale.

### 3.3 No shared world model below the InfluenceMap level

`InfluenceMap` and `FrontlineOverlay` give us shared perception of the contested band. But there's no shared:

- **Resource map** — which capturable structures exist, who owns them, what income they yield, who's near them. Each capture-related module re-queries the world fresh each scan.
- **Sector map** — there's no concept of "this part of the map is sector A". Everything is per-cell or per-actor, no hierarchical structure to budget against ("commit 8 units to sector A, 4 to sector B").
- **Terrain map** — cover cells (`Tree`/`Rough`/`Field`) are scanned ad-hoc per slot assignment by LayeredDefence. Chokepoints (narrow corridors) aren't identified at all.
- **Memory** — last-known enemy positions are not stored anywhere shared. Each module re-detects from `world.Actors` per scan, which gives only currently-visible enemies (subject to fog).

### 3.4 Push notifications are tick-stale by design

`IBotPositionsUpdated`, `IBotNotifyIdleBaseUnits`, `IBotRequestUnitProduction` — the engine's cross-module communication primitives — are last-tick caches. Module A pushes on tick N; module B reads what it cached on tick N+1. There's no way to have module B read fresh data on the same tick.

This is fine for low-frequency events (base center, idle count). It's hopeless for high-frequency coordination ("which unit should I take?" — every module has a slightly different answer because they're all reading slightly stale state).

### 3.5 The composition of layers fights itself

Most concrete: the v2 modules trust each other selectively. LayeredDefence checks MountedTransport's `IsPassengerReserved` for unit-A; CaptureCoordinator doesn't check anything from anyone; MountedTransport checks `carrierTasks.ContainsKey` for its own claims but doesn't ask LayeredDefence whether it's about to assign the same unit. Each handshake is hand-wired between two modules. Adding a new module means N new handshakes.

This is the structural ceiling. Even if we fix every individual bug in v2, the architecture has no answer for "module N+1 wants to coordinate".

---

## 4. Non-negotiables and non-goals

### Must keep working

- **Legacy AI** under `enable-ai-legacy-only` — the bot people play against today must continue to work for at least the v1 release cycle. The new brain runs under `enable-ai-v3` (or whatever we settle on); legacy is the fallback.
- **Autotest harness** — `tools/autotest/` runs the deterministic scenarios. The new brain must be inspectable by autotests (Lua API to query unit state, etc.).
- **Demo scenarios** — `demo-frontline-overlay`, `demo-layered-defence`, etc. — these become regression checks for the new brain.
- **Build pipeline** — `make all`, `./make.ps1 all`, cross-platform (mac / Linux / Windows). No new dependencies that break Windows.
- **The condition-gating system** — `enable-ai-v2` / `enable-ai-legacy-only` is how we A/B. Extend it (`enable-ai-v3`), don't replace it.
- **The InfluenceMap + FrontlineOverlay world traits and the `/frontline` chat command** — keep verbatim, build on top.
- **The doctrine doc** (`archive/doctrine.md`) — intent doesn't change, only the implementation does.

### Out of scope

- **Human-grade play.** Floor is "competent and coherent" — the bot reliably executes the eight behaviors in §1. Not "Korean StarCraft pro".
- **Replacing the engine bot framework.** We extend `IBotTick` / `IBotRespondToAttack` / `BotBlackboard` / `ModularBot`; we don't fork OpenRA.
- **ML / neural / RL.** Out of scope. This is hand-tuned heuristics with a clean substrate.
- **Multi-team alliance reasoning.** v3 reasons about own-vs-enemy, plus optional ally awareness. Full multi-side diplomacy is not on the table.
- **Replay-determinism beyond what OpenRA already gives.** Orders are deterministic by engine guarantee; our brain's internal state must be sync-safe (no `World.LocalRandom` in sim-affecting code that runs on only some clients).
- **Resource economy / harvester logic.** WW3MOD doesn't use it; we ignore it.
- **MCV/build-base reasoning.** Same — no MCVs, no expandable base.
- **Supply-chain optimization.** TRUKs already have `SupplyFollowerBotModule`; we keep it.

---

## 5. Success criteria

The new brain ships when these tests pass. All testable via the autotest harness or a short scripted playtest.

| ID | Test | How we know |
|----|------|-------------|
| **S-A** | Frontline coherence | On a 2v2 map after 90 sim-seconds of contact, no contested cell has friendly density 0 while a neighbor has density 3+. Visual check via `/frontline` overlay. |
| **S-B** | Capture priority | v3-bot's TECNs capture `logisticscenter` and `bio` before `oilb` and `hosp` when both are equidistant + same safety. `[v3-capture]` log shows scoring picked the higher-income target. |
| **S-C** | Carrier ferry visible | A Bradley loads ≥2 infantry at SR rally, drives to frontline gap, unloads, returns. End-to-end in the autotest harness with deterministic timing. |
| **S-D** | Anti-suicide TECN | TECN does not enter a capturable structure when ≥3 enemies are within 6 cells. Skirmish + log check. |
| **S-E** | No order-overwriting | TECN issued capture order does not receive a second `CaptureActor` order within 200 ticks unless the original target became unreachable. `[v3-dispatch]` log shows order-emit cadence. |
| **S-F** | Reserve preservation | When SquadManager-equivalent forms an attack force, ≥ N units remain available to LayeredDefence-equivalent. Configurable N (defaults TBD). |
| **S-G** | Production responsive | When enemy helicopter sighted, v3 calls in anti-air within 2 production cycles. `[v3-plan]` log shows demand shift. |
| **S-H** | Cover seeking | Infantry on the line in a map with treelines ends up in `Tree`-typed cells > 60% of the time. Map-stat check via autotest. |

Eight tests, one per observable behavior in §1. Anything that doesn't pass these eight is not "done"; anything not in this list is bonus.

---

## 6. What this doc explicitly leaves open

- **Architecture choice** — single-brain `BotBrain` trait that owns the tick loop, or pipeline of `IBotTick` phases? Deferred to `03_substrate.md` / brain design.
- **Goal data model** — what fields does a `UnitGoal` carry? Deferred.
- **Sectorization** — Voronoi from spawn points, regular grid, hand-painted regions? Deferred.
- **Personality / doctrine knobs** — what's tunable per AI variant (Normal / Rush / Turtle)? Deferred to a later doc.
- **Migration order** — which behavior do we ship first? Deferred to a later doc.

These are real decisions, but making them here would derail the diagnosis. Hold them for the next docs.
