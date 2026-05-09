# WW3MOD AI Strategy — From "Send Troops One Way" to Competent Commander

> Research-backed roadmap for building intelligent AI opponents, ordered by impact-to-effort ratio.

---

## Table of Contents

1. [Current System Assessment](#1-current-system-assessment)
2. [How Top RTS Games Do It](#2-how-top-rts-games-do-it)
3. [Architecture Patterns](#3-architecture-patterns)
4. [Prioritized Implementation Roadmap](#4-prioritized-implementation-roadmap)
5. [Implementation Details Per Feature](#5-implementation-details-per-feature)
6. [Not Recommended](#6-not-recommended)
7. [References](#7-references)

---

## 1. Current System Assessment

### What Exists

WW3MOD uses OpenRA's **ModularBot** framework — a modular, event-driven AI system:

| Module | What It Does | Status |
|--------|-------------|--------|
| `ModularBot.cs` | Orchestrator — manages lifecycle, order queue, calls `IBotTick` modules | Working |
| `UnitBuilderBotModule` | Decides what to produce based on ratio-matching priorities | Working, but static |
| `SquadManagerBotModule` | Forms squads, runs attack state machine (Idle→AttackMove→Attack→Flee) | Working, but simple |
| `CaptureManagerBotModule` | Sends engineers to capture buildings | Working |
| `BuildingRepairBotModule` | Repairs damaged buildings | Working |
| `HarvesterBotModule` | Sends harvesters to resources | N/A (no harvesters in WW3MOD) |
| `SupportPowerBotModule` | Uses superweapons | Minimal |
| `McvManagerBotModule` | Deploys MCVs | N/A |
| `BaseBuilderBotModule` | Places buildings | Not configured |

### How Attacks Work Today

1. `UnitBuilderBotModule` builds units based on fixed priority ratios (e.g., E3:120, AR:100, ABRAMS:80)
2. Units idle at base until `SquadManagerBotModule` has enough (6 + random 0-14 = 6-20 units)
3. Squad forms → marches toward closest enemy → fights or flees
4. Attack/flee decision uses **Mamdani fuzzy logic** (inputs: own HP, enemy HP, relative DPS, relative speed)
5. If outnumbered, squad flees back to base. If winning, keeps attacking
6. No coordination between squads. No flanking. No retreat-and-reinforce

### Critical Gaps

| Gap | Impact | Why It Matters |
|-----|--------|---------------|
| **No strategic layer** | AI has no concept of "map control," "push timing," or "economy first" | Feels like mindless aggression |
| **No threat awareness** | Squads walk into defenses, anti-air, kill zones | Wastes units, looks stupid |
| **No logistics** | AI doesn't build supply trucks, units run out of ammo and become useless | Entire army becomes dead weight |
| **No scouting** | AI doesn't explore; attacks blind | Can't adapt to what player builds |
| **No retreat/preservation** | Units fight to the death, never retreat suppressed/damaged units | Wastes veterancy, violates WW3MOD's design philosophy |
| **No garrison use** | AI never puts infantry in buildings | Misses a core defensive advantage |
| **No stance usage** | AI never uses Ambush, HoldPosition, or any tactical stances | Fights worse than a player using defaults |
| **Static build orders** | Always builds the same ratio of units regardless of what opponent does | Easily exploited, no adaptation |
| **Module conflicts** | Multiple modules can fight over control of the same unit | Engineers get pulled into attack squads |
| **No multi-direction attacks** | All units blob toward one target | Easily countered by player with one defensive line |

---

## 2. How Top RTS Games Do It

### Supreme Commander / FAF

**Key innovation: Platoon + Intel Map (IMAP) + Builder System**

- **Platoons**: Named squad templates with a mission (e.g., "RaidEnemyMexes", "DefendBase", "MainAssault"). Units assigned to platoons based on composition needs. Platoons can merge at rally points before attacking
- **IMAP (Intel Map)**: Coarse grid-based threat map. Each cell stores threat by type (air, land, naval, economic). AI queries "where's the weakest point?" or "where's the most valuable undefended target?"
- **Builders**: Priority-scored production — "build power plant if energy deficit > 100" with utility scoring. Highest-scoring valid option wins

**Top custom AIs** (M27AI, AI-Uveso, RNGAI) all extend this with: custom platoon behaviors, automatic pathfinding markers, performance-conscious threat detection.

**Relevance to WW3MOD**: HIGH. OpenRA's Squad system ≈ FAF's Platoons. Adding IMAP/threat maps is the single biggest value-add.

### StarCraft 2 Competition Bots

**Key innovation: Modular architecture + Combat simulation + Opponent modeling**

- **UAlbertaBot** (AIIDE 2013 winner): StrategyManager → ProductionManager → CombatCommander → ScoutManager → WorkerManager. Uses **SparCraft** combat simulator to predict fight outcomes before committing
- **PurpleWave** (#1 ladder): Multiple strategic plans with conditional switching based on scouting intel
- **Common pattern**: Blackboard for inter-module communication. Opponent modeling (track enemy builds via scouting → switch strategy)

**Relevance to WW3MOD**: Combat prediction is interesting — WW3MOD already has the `combat-sim` tool. Opponent modeling via scouting is very achievable.

### Company of Heroes

**Key innovation: Territory control + Unit preservation + Cover/suppression AI**

- AI evaluates which **sectors to contest, hold, or abandon** based on strategic value
- **Retreats damaged units** to preserve veterancy (retreat is a core mechanic, not just AI behavior)
- AI evaluates **cover positions** when placing infantry, seeks flanking angles
- Difficulty scaling adjusts attack/defense priority ratios, not just cheating resources

**Relevance to WW3MOD**: VERY HIGH. WW3MOD shares suppression, garrison, veterancy, and supply route contestation — all concepts that CoH AI handles well. Retreat logic is especially critical.

### Age of Empires 2/4

**Key innovation: Build order scripting + Personality system + Scouting**

- Rule-trigger scripted AI: `if (food < 100 and idle_farms < 3) then build farm`
- Multiple AI **personalities** with different aggression timing, unit preferences, expansion patterns
- Multi-pronged scouting with dedicated scout units on separate paths

**Relevance to WW3MOD**: Personality system maps directly to "aggressive Russia rush" vs "defensive NATO turtle" archetypes. Scouting behavior is a gap.

---

## 3. Architecture Patterns

### 3a. Influence/Threat Maps ⭐ HIGHEST VALUE

A coarse grid overlay on the map where each cell stores threat/value scores.

**How it works:**
1. Divide map into cells (e.g., 8×8 or 16×16 tile cells)
2. Each unit adds influence to its cell, spreading to neighbors with distance falloff
3. Friendly = positive, enemy = negative
4. Maintain separate layers: military threat, economic value, exploration age, visibility

**What it enables:**
- Smart attack routing (path through low-threat corridors)
- Intelligent expansion (build where safe)
- Defense placement (cover high-threat approaches)
- Front line detection (cells where influence is balanced)
- Retreat path selection (flee toward friendly influence)
- Supply Route defense prioritization

**Complexity: LOW** — a 2D array, iterate units, add values with falloff. Can be a single C# world trait.

### 3b. Blackboard Architecture ⭐ HIGH VALUE

A shared data structure where AI modules post and claim tasks without coupling.

**How it works:**
1. Strategic layer posts tasks: "Defend East", "Scout North", "Attack South"
2. Squads score tasks based on composition, location, current state
3. A squad claims a task — other squads see it's taken and pick alternatives
4. Prevents the documented OpenRA problem of modules fighting over unit control

**Complexity: LOW-MEDIUM** — simple data structure, discipline is the hard part.

### 3c. Utility-Based Scoring ⭐ HIGH VALUE

Replace hard-coded priorities with dynamic scoring curves.

**How it works:**
```
BuildTank_utility =
    curve_resources(current_cash) * 0.3 +
    curve_enemy_vehicles(scouted_tanks) * 0.4 +
    curve_army_ratio(my_army / enemy_army) * 0.3
```

Smooth transitions between strategies. No hard-coded if/else chains. Easy to tune.

**Complexity: MEDIUM** — framework is simple, tuning curves is iterative.

### 3d. Three-Layer Architecture ⭐ STRUCTURAL

| Layer | Timescale | Decisions | Update Rate |
|-------|-----------|-----------|-------------|
| **Strategic** | Minutes | Build order, tech path, army composition, expansion timing | Every 5-10s |
| **Tactical** | Seconds | Where to attack, squad routing, retreat, flanking, garrison | Every 1-2s |
| **Micro** | Ticks | Focus fire, suppression avoidance, cover seeking, kiting | Every tick |

Current AI is strategic-only. Adding tactical + micro layers would be transformative. WW3MOD's systems (suppression, stances, garrison) specifically need tactical-layer AI.

### 3e. Map Analysis (Static, One-Time)

At game start (or cached per map):
- **Choke point detection**: Find narrow passages between regions (Voronoi or heuristic)
- **Region decomposition**: Divide map into meaningful areas (bases, corridors, open ground)
- **Strategic locations**: Height advantages, garrison-able buildings, defensive positions
- **Attack corridors**: Multiple routes from base A to base B with cost estimates

**Complexity: MEDIUM-HIGH** for automated analysis, **LOW** for hand-annotated maps (only 13 maps).

---

## 4. Prioritized Implementation Roadmap

Ordered by **impact ÷ complexity**. Each tier builds on the previous.

### Tier 0: Quick Wins (1-2 sessions each, no new C# systems)

These are YAML/config changes and minor code tweaks that immediately improve AI behavior.

| # | Feature | What | Impact |
|---|---------|------|--------|
| 0.1 | **Build logistics centers + supply trucks** | Add LOGISTICSCENTER and SUPPLYTRUCK to UnitBuilderBotModule priorities | CRITICAL — without ammo, AI army is useless |
| 0.2 | **Fix engineer decoupling** | Prevent CaptureManager units from being recruited into attack squads | HIGH — engineers stop being cannon fodder |
| 0.3 | **Don't attack buildings being captured** | Filter capture targets from attack targeting | MEDIUM — stops wasting damage |
| 0.4 | **Set default stances** | AI units spawn with FireAtWill + Defensive (already default), but configure Hunt for assault squads | MEDIUM — attack squads actually chase |
| 0.5 | **Ammo awareness** | Check AmmoPool.HasAmmo before issuing attack orders; return to supply when empty | HIGH — stops useless empty-gun aggression |
| 0.6 | **AI personality variants** | Create 2-3 YAML personality profiles: Aggressive (early rush), Balanced, Defensive (turtle + tech) | MEDIUM — variety, replayability |

### Tier 1: Foundation Systems (3-5 sessions each)

New C# world traits that all future AI improvements build upon.

| # | Feature | What | Complexity | Impact |
|---|---------|------|-----------|--------|
| 1.1 | **ThreatMapManager** | World trait maintaining coarse influence grid (friendly/enemy military, economic value, exploration age). Updated every 60-120 ticks. All AI modules can query it | MEDIUM | EXTREMELY HIGH — enables everything below |
| 1.2 | **BotBlackboard** | World trait for inter-module coordination. Tasks posted, claimed, completed. Prevents module conflicts | LOW-MEDIUM | HIGH — fixes architectural flaw |
| 1.3 | **ScoutingBotModule** | New BotModule that sends fast units to unexplored/stale areas. Uses ThreatMap exploration-age layer to prioritize. Posts scouted intel to Blackboard | MEDIUM | HIGH — AI can finally adapt |

### Tier 2: Tactical Layer (3-5 sessions each)

The layer between "what to build" and "attack-move toward enemy base."

| # | Feature | What | Complexity | Impact |
|---|---------|------|-----------|--------|
| 2.1 | **Retreat logic** | Squads retreat when: suppression high, HP below threshold, outnumbered (threat map query). Retreat toward friendly influence. Regroup and reinforce before re-engaging | MEDIUM | VERY HIGH — stops suicidal charges |
| 2.2 | **Multi-axis attacks** | Split army into 2-3 squads attacking from different directions. Use threat map to find multiple approach routes. Pin + flank pattern | MEDIUM-HIGH | HIGH — breaks single-blob behavior |
| 2.3 | **Garrison usage** | AI garrisons infantry in buildings near the front line (threat map balanced zones). Evacuates when threatened by overwhelming force or demo charges | MEDIUM | HIGH — uses core WW3MOD mechanic |
| 2.4 | **Defense positioning** | Place defensive units at choke points / high-threat approach cells. Reposition as front line shifts | MEDIUM | HIGH — AI actually defends intelligently |
| 2.5 | **Supply chain awareness** | AI protects its own supply routes. AI targets enemy supply routes for contestation. Supply trucks escorted or routed through safe areas | MEDIUM | HIGH — exploits WW3MOD's unique mechanics |

### Tier 3: Smart Production (2-4 sessions each)

Make the AI build the right things at the right time.

| # | Feature | What | Complexity | Impact |
|---|---------|------|-----------|--------|
| 3.1 | **Utility-based production** | Replace fixed ratio priorities with dynamic scoring. Inputs: scouted enemy composition, current army balance, resource levels, map control %, game phase | MEDIUM | HIGH — AI adapts to player strategy |
| 3.2 | **Counter-composition** | If scouting detects heavy armor → build more AT. Heavy infantry → build suppression weapons. Air → build AA. Simple counter-table lookup | LOW-MEDIUM | HIGH — AI doesn't get hard-countered |
| 3.3 | **Game phase awareness** | Early game: economy + scouting. Mid game: army + expansion. Late game: tech + combined arms. Transition triggers based on time + income + army value | MEDIUM | MEDIUM — natural-feeling AI progression |
| 3.4 | **Base building** | AI places buildings intelligently (production near rally points, defenses at approaches, supply buildings behind front). Uses threat map for placement scoring | MEDIUM-HIGH | MEDIUM — currently buildings just appear |

### Tier 4: Micro Layer (2-4 sessions each)

Individual unit intelligence — makes combat feel competent.

| # | Feature | What | Complexity | Impact |
|---|---------|------|-----------|--------|
| 4.1 | **Focus fire** | Squad members focus on the same target (highest value or lowest HP). Prevents overkill — switch targets when current target has enough incoming damage | MEDIUM | HIGH — massively increases combat efficiency |
| 4.2 | **Suppression-aware movement** | Units avoid moving through suppressed zones. Suppressed infantry go prone (already automatic) but squad leader pulls them back or flanks | MEDIUM | MEDIUM — looks competent |
| 4.3 | **Vehicle crew preservation** | AI retreats vehicles at critical damage (before crew eject). If crew eject, orders them to seek safety. Re-crew repaired vehicles | LOW-MEDIUM | MEDIUM — uses crew system |
| 4.4 | **Kiting / range exploitation** | Long-range units (snipers, artillery) maintain maximum range. Pull back if enemies close distance. Use terrain for LOS advantage | MEDIUM-HIGH | MEDIUM — rewards player for flanking |
| 4.5 | **Ability usage** | AI uses active abilities: deploy smoke, call artillery, use special weapons at appropriate times | MEDIUM | MEDIUM — uses the full unit toolkit |

### Tier 5: Advanced Strategy (5-8 sessions each)

Polish and depth. Only pursue after Tiers 0-3 are solid.

| # | Feature | What | Complexity | Impact |
|---|---------|------|-----------|--------|
| 5.1 | **Map analysis at load** | Automated choke point detection, region decomposition, waypoint graph generation. Cached per map | HIGH | MEDIUM — makes AI work on any map |
| 5.2 | **Opponent modeling** | Track player's unit composition, attack patterns, economy via scouting. Adjust strategy accordingly. "Player always rushes at minute 5" → prepare defenses at 4:30 | HIGH | MEDIUM — makes AI feel intelligent |
| 5.3 | **Combat prediction** | Before committing a squad, simulate the fight (using combat-sim logic). Only attack if predicted to win. Reinforces retreat decisions | HIGH | MEDIUM — reduces wasted attacks |
| 5.4 | **Multi-AI coordination** | In team games, AI allies coordinate attacks, share intel, avoid duplicating targets | HIGH | LOW — only matters in team games |
| 5.5 | **Difficulty scaling** | Easy: slower decisions, no micro, worse production. Normal: full AI. Hard: faster reactions, optimal countering. Brutal: slight resource bonus | MEDIUM | HIGH — but only matters for release polish |

---

## 5. Implementation Details Per Feature

### 5.1 ThreatMapManager (Tier 1.1)

**New file:** `engine/OpenRA.Mods.Common/Traits/BotModules/ThreatMapManager.cs`

**Architecture:**
```csharp
public class ThreatMapManager : IWorldLoaded, ITick
{
    // Coarse grid (map divided into cells of CellSize tiles)
    int cellSize = 8; // 8x8 tile cells
    float[,] friendlyMilitary;
    float[,] enemyMilitary;
    float[,] economicValue;
    int[,] lastExplored; // tick when cell was last scouted

    // Updated every UpdateInterval ticks (60-120)
    void UpdateThreatMap() {
        // Clear and recalculate from all visible actors
        // Each unit contributes its combat value to its cell
        // Value spreads to adjacent cells with distance falloff
    }

    // Query API for other modules
    public float GetThreat(CPos cell, Player perspective);
    public float GetFriendlyInfluence(CPos cell, Player player);
    public CPos FindWeakestEnemyCell(Player player);
    public CPos FindSafestRetreatCell(CPos from, Player player);
    public List<CPos> FindAttackCorridor(CPos from, CPos to, Player player);
}
```

**Value calculation:** Unit value = `Valued.Cost` (YAML Cost field). Military threat = cost × (currentHP / maxHP). Economic value = production buildings + supply routes.

**Update frequency:** Every 90 ticks (~1.5 seconds at 60 TPS). Full recalculation, not incremental (simpler, fast enough for coarse grid).

### 5.2 BotBlackboard (Tier 1.2)

**New file:** `engine/OpenRA.Mods.Common/Traits/BotModules/BotBlackboard.cs`

**Architecture:**
```csharp
public class BotBlackboard : IWorldLoaded
{
    // Task board — strategic layer posts, squads claim
    Dictionary<string, BotTask> tasks; // "defend-east", "attack-north", etc.

    // Intel board — scouting module posts, production module reads
    Dictionary<string, object> intel; // "enemy-has-tanks": true, "enemy-base-pos": CPos

    // Unit claims — which module controls which unit
    Dictionary<Actor, string> unitClaims; // actor → "squad-3", "capture-team", "scout"

    public BotTask ClaimTask(string taskId, string claimant);
    public void PostIntel(string key, object value);
    public bool IsUnitClaimed(Actor actor);
    public void ClaimUnit(Actor actor, string claimant);
    public void ReleaseUnit(Actor actor);
}
```

**Key rule:** Modules MUST check `IsUnitClaimed` before issuing orders. Capture module claims engineers. Scout module claims scouts. Squad module claims assault units. No more conflicts.

### 5.3 Retreat Logic (Tier 2.1)

**Modify:** `engine/OpenRA.Mods.Common/Traits/BotModules/SquadManagerBotModule.cs`

**New squad states:**
```
Current:  Idle → AttackMove → Attack → Flee (back to base)
Proposed: Idle → AttackMove → Attack → Retreat → Regroup → (rejoin or AttackMove)
                                  ↓
                              Suppressed → Retreat
```

**Retreat triggers:**
- Squad average HP < 40%
- Squad suppression average > tier 5 (50%)
- Threat map shows 3:1 enemy advantage in current cell
- Key unit lost (only vehicle in mixed squad destroyed)

**Retreat behavior:**
- Query ThreatMap for safest retreat direction (highest friendly influence)
- Move to nearest friendly-controlled cell, NOT necessarily back to base
- In Regroup state: wait for reinforcements (check blackboard for nearby idle units)
- If reinforced to 70%+ of original strength, resume attack on original objective
- If not reinforced within 30 seconds, dissolve squad and return units to pool

### 5.4 Multi-Axis Attacks (Tier 2.2)

**Concept:** Instead of one blob, create 2-3 squads with coordinated objectives.

**Implementation:**
1. Strategic layer identifies target (e.g., enemy base)
2. Query ThreatMap for 2-3 approach routes with different threat profiles
3. Assign squads: **main force** (largest, takes safest route), **flanking force** (smaller, takes alternate route), **fire support** (artillery/snipers, positions at range)
4. Main force engages first (draws attention), flanking force hits 10-15 seconds later
5. If either squad triggers retreat, the other covers withdrawal

**Coordination via Blackboard:**
- Post task: "main-assault-north" (claimed by squad A)
- Post task: "flank-east" (claimed by squad B)
- Post task: "fire-support-hill" (claimed by squad C)
- Squads check each other's status before advancing

### 5.5 Utility-Based Production (Tier 3.1)

**Replace** the current ratio-matching in `UnitBuilderBotModule` with utility scoring.

**Inputs (from Blackboard + ThreatMap):**
```
enemy_armor_count    → scouting intel, or 0 if not scouted
enemy_infantry_count → scouting intel
own_army_value       → sum of own unit costs
enemy_army_value     → estimated from threat map
income_rate          → current production speed
game_time            → ticks since match start
map_control_pct      → % of threat map cells with friendly > enemy influence
supply_status        → trucks active, supply remaining
```

**Scoring example for "build anti-tank":**
```
score = base_priority * 0.2
      + curve_enemy_armor(enemy_armor_count) * 0.35  // steep rise 0→5 tanks
      + curve_own_at_deficit(own_at / own_army) * 0.25 // low if we already have AT
      + curve_resources(cash) * 0.1                     // can we afford it
      + curve_game_phase(game_time) * 0.1               // AT more important mid-late
```

**Curves:** Simple piecewise linear or sigmoid. Start with linear, tune from playtesting.

---

## 6. Not Recommended

| Approach | Why Not |
|----------|---------|
| **Machine Learning / AlphaStar** | Requires TPU clusters, millions of training games, not practical for a mod |
| **Full GOAP (Goal-Oriented Action Planning)** | Too expensive (A* per agent per replan), unpredictable behavior, hard to tune |
| **Full HTN Planner** | Overkill — utility + FSM covers same ground more simply |
| **Automated Voronoi choke detection** | Only 13 maps; hand-annotation is faster and more reliable. Consider automating only if community maps become a thing |
| **Neural network opponent modeling** | Insufficient training data, overfitting risk. Simple stat tracking (counter-table) is more robust |

---

## 7. References

### Game-Specific
- **FAF AI Dev Guide & M27AI Devlog** — forum.faforever.com/topic/2373
- **FAF AI Modding Wiki** — wiki.faforever.com/en/Development/AI/AI-Modding
- **AI-Uveso** (custom platoon behaviors, auto path markers) — github.com/Uveso/AI-Uveso
- **UAlbertaBot** (SC2, modular architecture + SparCraft combat sim) — github.com/davechurchill/ualbertabot/wiki
- **PurpleWave** (SC2, multi-strategy with conditional switching) — github.com/dgant/PurpleWave
- **AlphaStar** (DeepMind, for reference only) — nature.com/articles/s41586-019-1724-z
- **FransBots** (improved OpenRA AI) — moddb.com/mods/fransbots

### Architecture & Techniques
- **Influence Mapping Core Mechanics** — gamedev.net/tutorials/.../the-core-mechanics-of-influence-mapping-r2799
- **Infinite-Resolution Influence Mapping** — Game AI Pro 2, Chapter 29
- **Spatial Reasoning for Strategic Decisions** — Game AI Pro 2, Chapter 31
- **HTN Planners Through Example** — Game AI Pro, Chapter 12
- **AI Blackboard Architecture** — tonogameconsultants.com/ai-blackboard
- **Terrain Analysis: Choke Points & Regions** — Perkins et al., ResearchGate
- **Utility AI Introduction** — shaggydev.com/2023/04/19/utility-ai
- **MicroRTS** (lightweight RTS AI testbed) — github.com/Farama-Foundation/MicroRTS

### OpenRA-Specific
- **OpenRA AI Forum Thread** — forum.openra.net/viewtopic.php?t=21241
- **OpenRA Issue #16126** (difficulty levels proposal) — github.com/OpenRA/OpenRA/issues/16126
- **OpenRA Architecture Overview** — delftswa.github.io/chapters/openra

---

## Summary: The 80/20 Path

If you do nothing else, do these three things:

1. **Tier 0 quick wins** (1-2 sessions) — Fix logistics, ammo awareness, engineer decoupling. The AI immediately stops being brain-dead.
2. **ThreatMapManager** (3-4 sessions) — One world trait that enables every other improvement. Without spatial awareness, the AI is blind.
3. **Retreat logic** (2-3 sessions) — Squads that retreat and regroup instead of dying feel 10× smarter to the player, even if nothing else changes.

These three alone transform the AI from "sends troops one way" to "has a basic sense of self-preservation and map awareness." Everything after that is incremental improvement.

---

*Document created: 2026-03-21*
*Based on: analysis of current WW3MOD AI code + research into FAF, SC2, CoH, AoE AI systems + RTS AI academic literature*
