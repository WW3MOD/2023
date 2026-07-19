# DESIGN NOTE — Enemy SR Contestation, Cycle 1

> Behavior cycle: elevate enemy Supply Route contestation in Experimental-bot
> offensive scoring. Scope: `ModularBot@experimental` only. Stable/Normal/Rush/
> Turtle untouched. Author date: 2026-07-20. Mode: EXPERIMENTAL.

Read alongside:
- `DOCS/reference/supply-route.md` — SR mental model (canonical)
- `DOCS/reference/game-model.md` — no-factory model (the recurring trap)
- `WORKSPACE/plans/260719_ai_realism_research.md` §6 — motivation
- `WORKSPACE/plans/260719_experimental_ai_poi_strategy.md` Phase 2/3 findings

---

## 1. Where the enemy SR enters (or fails to enter) offensive scoring TODAY

### 1.1 Code path

`PoiMap.GetOffensiveTargets` (`engine/OpenRA.Mods.Common/Traits/World/PoiMap.cs:279`) is
the sole source of army objectives for `PoiOffensiveBotModule`. For each candidate POI it
applies:

```
score_biased = ApplyBias( Score(value, distFactor, threatFactor, ownershipMul), bias )
             = value × distFactor × threatFactor × ownershipMul × bias / 100
```

For an **enemy Supply Route** (PoiMap.cs:299–313):

| Parameter | Source | Current value |
|---|---|---|
| `value` | `PoiMapInfo.SupplyRouteDenyValue` (world.yaml:305) | **120** |
| `distFactor` | `PoiScoring.DistanceFactor(dist, halfLife=20)` = `20×100 / (20+dist)` | at 95 cells: **17** |
| `threatFactor` | `ThreatFactor(enemyInfluence, mildThreshold=20, safe=100, mild=40, hostile=10)` | usually **40 or 10** |
| `ownershipMul` | `OwnershipEnemySupplyRouteMultiplier` (world.yaml:313) | **100** |
| `bias` | `OffensiveEnemyAttackBias` (shared with enemy income) | **80** |

The enemy SR is discovered and assigned `PoiAction.Pressure` (PoiMap.cs:313), and
`OffensiveEnemyAttackBias = 80` is applied (PoiMap.cs:310 → 344-345). No separate
Pressure-specific bias exists; the SR shares the "below-100 to not outrank income" bias
that was designed for **enemy income buildings**, not for the strategically unique SR deny.

### 1.2 Concrete scores on River Zeta 2P geometry

River Zeta (`mods/ww3mod/maps/river-zeta-ww3/map.yaml`): 98×82, corner spawns at
(15,6) and (80,76). P1 SR ≈ (15,6). All `oilb` locations from map.yaml actor list;
enemy SR ≈ (80,76). Distances are Chebyshev approximated from the formula
`|Δx|+|Δy| * 0.7` (rough cell straight-line).

| Target | Dist (cells) | distFactor | threatFactor | ownershipMul | bias | **Biased score** |
|---|---|---|---|---|---|---|
| Nearest oilb (15,3), neutral, safe | 3 | 87 | 100 | 100 | 150 | **130,500,000** |
| oilb (25,22), neutral, safe | 19 | 51 | 100 | 100 | 150 | **76,500,000** |
| oilb (17,44), neutral, safe | 38 | 34 | 100 | 100 | 150 | **51,000,000** |
| oilb (25,22), now enemy-owned | 19 | 51 | 100 | 70 | 80 | **28,560,000** |
| oilb (56,26), neutral, safe | 46 | 30 | 100 | 100 | 150 | **45,000,000** |
| oilb (75,3), neutral, safe | 60 | 25 | 100 | 100 | 150 | **37,500,000** |
| oilb (76,35), neutral, safe | 67 | 22 | 100 | 100 | 150 | **33,000,000** |
| **Enemy SR (80,76), mild threat** | **95** | **17** | **40** | **100** | **80** | **4,624,000** |
| **Enemy SR (80,76), safe threat** | **95** | **17** | **100** | **100** | **80** | **13,600,000** |
| **Enemy SR (80,76), hostile** | **95** | **17** | **10** | **100** | **80** | **1,360,000** |

Scoring formula from `PoiScoring.Score` (PoiMap.cs:581):
`score = value × distFactor × threatFactor × ownershipMul`, then
`ApplyBias(score, bias)` at PoiMap.cs:587: `score × bias / 100`.

**Verdict: the enemy SR scores 4.6–13.6M with realistic threat, versus 33–130M for
neutral oilbs on the same map.** It ranks LAST among all offensive targets and
NEVER enters an axis within MaxAxes=4.

### 1.3 Why three factors conspire against the SR

1. **Distance penalty is maximal.** The SR is always near the enemy's spawn edge —
   the farthest possible point from our SR. On River Zeta: 95 cells → distFactor = 17
   (i.e., 17% of what a POI at distance 0 would score). The half-life of 20 cells was
   designed for income structures where closeness means less travel time; for the SR
   the position is structurally fixed and the distance decay is unavoidable and extreme.

2. **ThreatFactor is inverted for Pressure semantics.** The threat gate on income POIs
   is correct: a heavily-guarded derrick deters a lone TECN. But for SR Pressure, enemy
   presence at their SR means their garrison is there — and contesting it is the whole
   point. The hostile multiplier (10) and mild multiplier (40) that deter TECN captures
   also deter army pressure, halving or quartering the score at exactly the moments
   when a real player would push hardest. (The insight is real but the fix below is
   deliberately conservative — see §3.)

3. **OffensiveEnemyAttackBias = 80 is designed for enemy income, not the SR.**
   The "below 100 so early pushes don't outrank securing income" intent
   (PoiMap.cs:158-159) is appropriate for enemy derricks but wrong for the SR. The SR
   is the highest-value spatial objective in the game model; it should rank above enemy
   income once enough army exists to contest it.

### 1.4 Axis-count gate (quantified)

`PoiOffenseMath.DesiredAxisCount` (`PoiOffensiveBotModule.cs:428`):

```
k = min(pool/UnitsPerAxis, MaxAxes, poiCount, pool/MinAxisSize)
```

With UnitsPerAxis=8, MinAxisSize=3, MaxAxes=4:

- 20-unit army: k = min(2, 4, N, 6) = 2. Only top-2 targets get axes.
- 24-unit army: k = min(3, 4, N, 8) = 3. Top-3 targets.
- 32-unit army: k = min(4, 4, N, 10) = 4. Top-4 targets.

The enemy SR at 4.6M would need to be in the top-4 to get any axis at all. With a
32-unit army on River Zeta (before any oilb is captured): the top-4 are the four
nearest neutral oilbs (scores 130M, 76M, 51M, 45M). The enemy SR at 4.6M is 10th.

After all 10 neutral oilbs are captured (they become Defend targets, dropped from
GetOffensiveTargets), the remaining offensive targets are: enemy-owned oilbs and the
enemy SR. Now the SR competes only against enemy-owned oilbs. But by this point the
game is almost certainly decided.

**In practice, the enemy SR never receives an offensive axis under the current config.**

---

## 2. Minimal change: two YAML fields + a 6-line C# addition

### 2.1 Root-cause decomposition

The three factors above have different fix costs:

| Factor | Fix | Cost |
|---|---|---|
| Distance penalty maximal at SR | Raise `SupplyRouteDenyValue` | YAML only |
| ThreatFactor inverted for Pressure | New `SrPressure*ThreatMultiplier` fields, invert logic | C# change |
| Bias too low for Pressure | New `OffensiveSrPressureBias` field | C# + YAML |

The threat-inversion fix (factor 2) is the most principled but also the riskiest: inverting
threat for Pressure means the AI would *prefer* to push a heavily-garrisoned SR. That would
cause exactly the suicide behavior we want to avoid (§3). Therefore we intentionally KEEP
the existing ThreatFactor behavior for Pressure as a guard: high enemy influence at the SR
(hostile garrison) keeps its score low → AI does not push a defended SR. This preserves the
"if strong enough" gate.

Only factors 1 and 3 are fixed.

### 2.2 Proposed changes

**C# change — `PoiMapInfo` (PoiMap.cs, ~6 lines)**

Add a dedicated `OffensiveSrPressureBias` field to `PoiMapInfo` (alongside the existing
`OffensiveEnemyAttackBias` at line 157) so the Pressure action has its own tunable bias
separate from enemy-income Attack:

```csharp
// In PoiMapInfo (after OffensiveEnemyAttackBias, PoiMap.cs ~line 159):
[Desc("OFFENSIVE ranking bias (x100) for a PRESSURE axis — specifically the enemy Supply",
    "Route. Higher than OffensiveEnemyAttackBias: the SR is the highest-value spatial",
    "objective (throttles all enemy production); it should rank above enemy income once",
    "the army is large enough to contest it. The existing ThreatFactor hostile=10",
    "serves as the 'strong garrison' gate; this bias does NOT override that.")]
public readonly int OffensiveSrPressureBias = 100;
```

Wire it in `GetOffensiveTargets` at PoiMap.cs:310 (2 lines — change the Pressure case):

```csharp
// Before (PoiMap.cs:308-312):
if (isSupplyRoute)
{
    action = PoiAction.Pressure;
    bias = Info.OffensiveEnemyAttackBias;   // ← was shared, now split
}

// After:
if (isSupplyRoute)
{
    action = PoiAction.Pressure;
    bias = Info.OffensiveSrPressureBias;    // ← dedicated SR bias
}
```

**YAML change — `world.yaml`**

```yaml
# world.yaml ~line 305 (current PoiMap block):
SupplyRouteDenyValue: 120       # ← change to 250
OffensiveSrPressureBias: 100    # ← add (new field, replaces sharing OffensiveEnemyAttackBias=80)
```

`OffensiveEnemyAttackBias` stays at 80 — enemy income attack behavior is unchanged.

### 2.3 Arithmetic justification

Proposed scores on River Zeta with `SupplyRouteDenyValue=250`, `OffensiveSrPressureBias=100`:

| Scenario | threatFactor | distFactor | score |
|---|---|---|---|
| Safe (enemy army pulled forward, SR exposed) | 100 | 17 | 250×17×100×100×100/100 = **42,500,000** |
| Mild (small garrison, contestable) | 40 | 17 | 250×17×40×100×100/100 = **17,000,000** |
| Hostile (strong garrison, don't push) | 10 | 17 | 250×17×10×100×100/100 = **4,250,000** |

Comparison with remaining offensive targets mid-game (nearest 3 oilbs captured, next 3 are
neutral at dist 46, 60, 67):

| Target | Score |
|---|---|
| Neutral oilb at 46 cells (Secure) | 50×30×100×100×150/100 = **22,500,000** |
| Neutral oilb at 60 cells (Secure) | 50×25×100×100×150/100 = **18,750,000** |
| Neutral oilb at 67 cells (Secure) | 50×22×100×100×150/100 = **16,500,000** |
| **Enemy SR, mild threat (proposed)** | — | — | **17,000,000** |
| **Enemy SR, safe threat (proposed)** | — | — | **42,500,000** |

With proposed values, at mild threat the enemy SR ranks **3rd or 4th** among offensive
targets mid-game (after nearest neutral oilbs). With MaxAxes=4 and a 32-unit army, it
reliably receives an axis. With safe threat (enemy army pushed forward), it jumps to 1st,
which is correct behavior — an exposed SR should be the top priority.

The hostile-threat gate (4.25M → 15th–16th place) prevents suicide pushes at a well-
garrisoned SR without any new code. This is the existing `ThreatFactor` working correctly.

**Why SupplyRouteDenyValue=250 specifically:**
The goal is SR mild-threat score ≈ neutral oilb at ~65 cells. At distFactor=17 and
threatFactor=40, `value × 17 × 40 × 100 × 100/100 = 17M` requires value = 250. This puts
the SR where it belongs: competitive in a mid-game MaxAxes=4 army, but below nearby
uncaptured income in the opening (IncomeSecureBias=150 still dominates).

**Why OffensiveSrPressureBias=100 (not higher):**
Setting PressureBias above 100 risks pushing the SR above ALL neutral income in the opening
(before any oilb is captured). At safe threat with value=250 and PressureBias=100: 42.5M —
already above the 2nd oilb (76M is 1st). Raising bias to 150 would give 63M, beating the
2nd oilb and risking a game-start SR push before income is secured. The existing
IncomeSecureBias=150 on Secure oilbs at close distances still dominates the very early game,
and that's appropriate.

### 2.4 Files changed

| File | Change | Lines |
|---|---|---|
| `engine/OpenRA.Mods.Common/Traits/World/PoiMap.cs` | Add `OffensiveSrPressureBias` field to `PoiMapInfo`; wire in `GetOffensiveTargets` for Pressure | ~6 |
| `mods/ww3mod/rules/world.yaml` | Raise `SupplyRouteDenyValue: 120 → 250`; add `OffensiveSrPressureBias: 100` | 2 |

Stable mirrors (ai.yaml has `PoiOffensiveBotModule@stable` which reads PoiMap — no YAML
duplication needed; world.yaml is global). The @stable module is untouched (it reads the
same world-level PoiMap trait that the @experimental module does).

### 2.5 Deny-only invariant preserved

This change does NOT alter the capture semantics:

- `PoiAction.Pressure` (set at PoiMap.cs:313) → `PoiOffensiveBotModule` issues
  `AttackMove` to the SR cell (PoiOffensiveBotModule.cs:386). This is a move+fire order,
  NOT a `CaptureActor` order. Units walk into the 10-cell contestation circle and fight
  any defenders — that is the contestation mechanic.
- `SUPPLYROUTE` has no `Capturable` or `CaptureManager` trait (verified: structures.yaml
  + DISCOVERIES.md entry 2026-07-19). Even if a unit reaches the SR, there is nothing to
  capture. The SR cannot change ownership via this path.
- `GetCaptureTargets` (PoiMap.cs:257-260) only returns `Capture` and `DenyCapture` actions.
  The Pressure action is invisible to `CaptureCoordinatorBotModule`.

Deny-only is structurally enforced. No YAML guard needed.

### 2.6 Goal-guard: why SR Pressure doesn't interfere with capture/garrison commits

`PoiOffensiveBotModule.CommitAndOrder` (PoiOffensiveBotModule.cs:368-394) commits every
unit on a Pressure axis to the shared `PoiGoalGuard.Ledger` with key `"offense:<targetId>"`.
Units already committed (by capture → `"capture:..."` or garrison → `"defend:..."`) are
excluded from `BuildFreePool` (PoiOffensiveBotModule.cs:326-329). The ledger is the single
arbitration point; the SR push axis competes on the free pool, not on committed units.

If the army has committed most of its free combat units to capturing income and garrisoning
derricks, the SR axis may end up below MinAxisSize=3 and be retired silently
(PoiOffensiveBotModule.cs:264-270). This is correct: don't push the enemy SR if you don't
have the army for it.

---

## 3. Expected observable effects by scenario

### S1 — 5-minute economy race

**Expected effect: minimal.** In the first 5 minutes:
- Army is small: 8–16 units → k = 1–2 axes. Enemy SR at 17M (mild) or 42.5M (safe) only
  enters the top-2 after the 3 nearest oilbs (65–76M) have been secured or the SR happens
  to have safe threat. Typical: k=2, income POIs still dominate.
- S1's metric is economy (income rate, army_value). An SR push requires 3+ minutes of
  travel time before contestation begins. No meaningful S1 score delta expected.

The AI's S1 behavior should look roughly identical to the Stable baseline.

### S3 — 12-minute win-rate (primary target)

**Expected effect: visible improvement mid-game, measurable win-rate uplift.** Around
minutes 7–10:

1. Army has grown to 24–32 units (k = 3–4 axes).
2. Some neutral income (nearest 3–4 oilbs) is captured or contested.
3. The enemy SR at mild threat (17M) enters the top-4 scoring pool.
4. PoiOffensiveBotModule opens a Pressure axis of 5–8 units toward the enemy SR.
5. Units enter the 10-cell contestation circle → `SupplyRouteContestation` drains enemy
   production (`BaseTicks: 1500`, `SlowdownThreshold: 50`) → enemy reinforcement rate
   drops.

The win-rate uplift comes from slowing enemy production: fewer enemy reinforcements per
minute → our army's combat-power advantage grows. Per `supply-route.md`: "A unit standing
inside the enemy's contestation circle does more than damage — it slows their entire
production." This is the highest-value spatial action in the game model.

The 3× `FriendlyRecoveryMultiplier` means the enemy can push back units into their own
contestation circle to recover. This creates a real positional fight for the SR zone —
which is the intended gameplay.

### What to watch in logs

```
[exp-offense] axis ... target=supplyroute@{cell} action=Pressure score={N} units={N}
```

This line (PoiOffensiveBotModule.cs:278) confirms an SR Pressure axis is live. It should
appear in S3 runs after tick ~2000 (minutes 5–7 at normal speed). The number of units
committed shows pool competition.

---

## 4. What could go wrong

### 4.1 Suicide pushes at full-strength SR garrisons

**Risk:** Army commits to the SR Pressure axis, walks into a 5+ unit garrison, and is
destroyed. Budget permanently lost.

**Gate:** The `ThreatFactor` hostile branch (enemyInfluence > 20 → threatFactor = 10)
reduces the score from 17M (mild) to 4.25M (hostile). At 4.25M the SR does not enter
the top-4 on River Zeta — it falls well below neutral oilbs at 60+ cells (16–22M). The
axis never opens.

**Residual risk:** InfluenceMap granularity may underestimate enemy presence at the SR if
the grid cells are coarse. The hostile gate is only as good as the threat sample.
`PoiMap.SampleThreat` (PoiMap.cs:481) reads one grid cell via `influenceMap.GetEnemyInfluence`.
If enemy units are spread beyond one grid cell, mildThreshold=20 may not fire.

**Mitigation within scope:** None needed for Cycle 1 — the threat gate is accepted as-is.
If suicide pushes appear empirically in S3, raise `ThreatMildThreshold` from 20 to 10
(tightening to hostile sooner) or raise `OffensiveSrPressureBias` more conservatively.

### 4.2 Score jitter causing axis thrash

**Risk:** The SR's score fluctuates as InfluenceMap updates, causing the Pressure axis to
open and retire every 100 ticks (ReeevaluateInterval), re-issuing AttackMove to the same
cell constantly.

**Gate:** `SelectStickyTargets` (PoiOffensiveBotModule.cs:285-317) with
`ReassignScoreThresholdPct=30` keeps an existing axis unless a competitor outscores it by
30%. An SR that was mild (17M) and briefly spikes to hostile (4.25M) would need to be
beaten by a 30% margin before being dropped — and the SR's own score drop to 4.25M would
cause it to be retired. Retirement releases units back to the pool; the re-eval loop
handles the re-assignment cleanly.

### 4.3 Goal-guard contention with capture and garrison

**Risk:** The Pressure axis recruits units that the garrison or capture escort needs,
leaving a held oilb undefended or a TECN without its escort.

**Gate:** `BuildFreePool` in both `PoiOffensiveBotModule` and `PoiGarrisonBotModule`
checks `goalGuard.Ledger.IsCommitted(a, tick)` (PoiOffensiveBotModule.cs:328). Only
uncommitted units enter the free pool. The three consumers (capture/garrison/offense)
compete emergently through the ledger. With the proposed SR axis pulling 5–8 units and
garrison pulling 1–3 units per POI (max 4 POIs = 12 units), the free pool needs ≥20
units before the SR push becomes affordable. A smaller army gracefully leaves the SR
axis below MinAxisSize=3 and retires it.

### 4.4 AttackMove to SR cell causes unintended behavior

**Risk:** `AttackMove` to the enemy SR cell sends units to attack the SR building
directly. Since the SR is `Armor: Indestructable`, all fire is absorbed and units
waste ammo.

**Clarification:** `AttackMove` moves the unit toward the target cell while auto-targeting
any enemies encountered en route (via `AutoTarget`). It does NOT lock onto the SR actor
as the primary target — `AutoTarget` fires at the nearest enemy unit, not the building.
The SR is indestructible but not necessarily targetable. Units entering the contestation
circle fight the garrison, which is the intended mechanic. If no garrison is present,
units idle near the SR cell, which IS contestation.

**Residual risk:** The SR cell may be inside a wall or structure boundary that prevents
unit movement to the exact cell. `AttackMove` handles this via pathfinding — units will
path to the closest reachable cell. Whether that cell is within the 10-cell contestation
radius (`SupplyRouteContestation BaseTicks: 1500`) depends on the map. On River Zeta the
SR spawns near the edge with open terrain — this should be fine.

### 4.5 S1 regression

**Risk:** Opening behavior changes because the SR's safe-threat score (42.5M) now
beats the 2nd neutral oilb (76.5M is 1st, SR 42.5M is 2nd with safe threat), so the
opening axis goes at the enemy SR instead of the 2nd nearest oilb.

**Gate:** Early game the enemy SR will NOT have safe threat — the enemy's starting army
deploys near their SR. Safe threat only occurs when the enemy army has fully committed
forward. Practically, the opening SR score should be mild (17M) or hostile (4.25M), well
below the nearest neutral oilbs (130M, 76M, 51M). Only with k≥4 axes AND safe SR threat
would the SR appear in the opening, and k≥4 requires 32+ units — not achievable in the
first 5 minutes.

Watch S3 logs for the first SR Pressure axis tick to confirm it's appearing mid-game
(tick ~2000–4000), not opening-game (tick < 500).

---

## 5. Explicit non-goals

This cycle does NOT change:
- Unit stats or weapon values
- Normal, Rush, or Turtle AI behavior
- `PoiGarrisonBotModule` (does not garrison the enemy SR — that requires SR CaptureManager
  support, which is absent and out of scope per DISCOVERIES.md 2026-07-19)
- Reinforcement-lane ambush (§9 in the research note — this is a separate, longer-horizon
  behavior requiring new logic)
- Engine-fit-to-benchmark: no changes to how scenarios, benchmarks, or the harness run
- `WORKSPACE/ai-bench/**` (benchmark worker owns that directory)
- `.maestro/` (out of scope)

---

## 6. Implementation checklist (when ready to code)

1. `PoiMap.cs`: Add `OffensiveSrPressureBias` field to `PoiMapInfo` (after
   `OffensiveEnemyAttackBias`, with the Desc shown in §2.2). Update `GetOffensiveTargets`
   at the Pressure branch (line ~310): `bias = Info.OffensiveSrPressureBias`.
2. `world.yaml`: Change `SupplyRouteDenyValue: 120 → 250`. Add `OffensiveSrPressureBias: 100`.
3. Run `make test` (YAML validation). No new autotest needed for this cycle — the
   PoiOffenseMath / PoiScoring unit tests already cover the scoring formula, and the new
   field is purely a tuning constant. A live check via one S3 run and watching for
   `[exp-offense] axis ... action=Pressure` in debug.log is the appropriate verification.
4. Commit with message describing the scoring change; no attribution trailers.
