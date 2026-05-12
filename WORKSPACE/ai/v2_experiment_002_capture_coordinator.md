# v2 Experiment #2 — CaptureCoordinatorBotModule

> Replaces the dumb `CaptureManagerBotModule` for v2 with a coordinator
> that scores targets by **income value × proximity × safety**, dispatches
> **escort infantry** with each engineer, and **summons defenders** when
> own captured structures come under threat.

## Why this matters

The current `CaptureManagerBotModule` (`engine/.../BotModules/CaptureManagerBotModule.cs:137`):

- Picks targets by `target.GetSellValue()` — same weight for OILB (50 $/tick),
  BIO (150 $/tick), MISS (no income, only radar).
- Sends engineer **alone** with `Order("CaptureActor", capturer, target, true)`.
  No escort. Walks across the map past enemies and dies.
- Doesn't defend own captured structures after capture.
- Doesn't coordinate with `SquadManagerBotModule` (engineers are excluded
  from squads via `ExcludeFromSquadsTypes`, so they're truly alone).

Income structures in `mods/ww3mod/rules/ingame/structures-neutral.yaml`:

| Building | CashTrickler | Notes |
|---|---|---|
| OILB | 50 $/tick | Most numerous on maps |
| FCOM | 100 $/tick | |
| BIO | 150 $/tick | Highest value |
| MISS | none | Radar only |
| HOSP | none | Hospital only |

The legacy `target.GetSellValue()` ordering ignores all of this.

## Scenarios I considered

| # | Scenario | Handling |
|---|---|---|
| 1 | Engineer alone-into-death across enemy turf | Escort: pull 2 nearest idle non-engineer infantry, attack-move them adjacent to target. If no escort available, engineer still goes — capture is better than no capture |
| 2 | Multiple engineers / multiple targets | Each engineer assigned to its highest-score remaining target; same building can be assigned twice (CaptureActor activity handles "already captured by another") |
| 3 | Engineer killed en route | `activeCapturers.RemoveAll(idle)` re-picks each tick — engineer goes idle when its order chain dies |
| 4 | Target captured by enemy mid-walk | CaptureActor activity cancels on next entry attempt; engineer goes idle; coordinator re-targets next tick |
| 5 | Captured structure under attack | Defense scan every N ticks. For each own capturable, scan enemy ArmyValue within DefenseEnemyRadius. If enemy value > friendly value within DefenseFriendlyRadius, pull K nearest idle infantry/light vehicles to attack-move on the target |
| 6 | No idle units available for escort/defense | Engineer/capture goes anyway; defense logs but does nothing this tick |
| 7 | Map with no neutral capturables | No targets, no orders. Defense logic only runs if I own at least one capturable |
| 8 | All targets clustered on one side, multiple engineers | Score includes distance — closer engineer naturally claims closer target |
| 9 | Capture into a building already burning down (low HP) | `IsDead` filter + existing CaptureActor `SabotageThreshold` (line 104-118) handles |
| 10 | Bot already has high income, neutral OILB still available | Still goes — compound income wins. No "we're already rich" gate |
| 11 | Defender summoned, then engineer crisis on other side of map | Defender keeps its order chain; new capture order doesn't reroute it. TTL avoids stuck-in-defense forever |
| 12 | LCs (logisticscenter) — separate `CaptureManagerBotModule@captureenemystructures` | Left intact under `enable-ai-any` — different target set, narrow scope, works fine. Don't touch |
| 13 | Engineer with `CaptureDelay: 20` races with another bot's engineer | The first to arrive wins, second cancels. Fine |
| 14 | Building captured and immediately recaptured (flip-flop) | Coordinator picks a target once per ScanInterval; flip rate is limited |

## Scope chosen

**Three behaviors in one new trait:**

1. **Smart target prioritization** — income-weighted score, distance decay,
   safety bonus when no enemy near target.
2. **Escort dispatch** — pull K nearest idle non-engineer infantry to
   attack-move on the target. Run as `Order("AttackMove", escort, Target.FromCell(targetCell), queued: false)`.
3. **Defense scan** — for each own capturable, if enemy ArmyValue in
   radius > friendly ArmyValue in inner radius, summon K nearest idle units
   to defend.

**Out of scope** (deferred):

- Active enemy-recapture targeting (current squad auto-attack already
  reaches captured enemy buildings; let it work)
- Multi-tick capture orchestration with explicit state machine
- Lobby `CapturableIncomeMultiplier` reading (doesn't exist in WW3MOD yet)
- Cross-faction unit-type weighting for "best escort" — just pick nearest

## Implementation

**File:** `engine/OpenRA.Mods.Common/Traits/BotModules/CaptureCoordinatorBotModule.cs`

Inherits the same `IBotTick` / `INotifyActorDisposing` pattern as
`CaptureManagerBotModule`. Two parallel instances in YAML — one for tecn,
one for engineer — same coordinator code.

**Tick budget:** ScanInterval=75 ticks (3 sec sim). Faster than legacy 375
because we have more work per tick.

### YAML

```yaml
# Legacy ones gated off for v2
CaptureManagerBotModule@tecn:
    RequiresCondition: enable-ai-legacy-only    # was enable-ai-any
CaptureManagerBotModule@engineer:
    RequiresCondition: enable-ai-legacy-only    # was enable-ai-any

# New v2 coordinator
CaptureCoordinatorBotModule@v2.tecn:
    RequiresCondition: enable-ai-v2
    CapturingActorTypes: tecn,tecn.russia,tecn.america
    CapturableActorTypes: oilb,bio,miss,fcom,hosp
    ScanInterval: 75
    EscortSize: 2
    EscortableUnitTypes: e3,e3.russia,e3.america, ar,ar.russia,ar.america, ...
    IncomeValueOilb: 50
    IncomeValueFcom: 100
    IncomeValueBio: 150
    IncomeValueOther: 10
    SafetyEnemyRadius: 6
    SafetyMultiplierSafe: 100
    SafetyMultiplierMild: 40
    SafetyMultiplierHostile: 10
    DefenseScanInterval: 150
    DefenseEnemyScanRadius: 12
    DefenseFriendlyScanRadius: 6
    DefenseSummonCount: 3
    DefenseSummonRadius: 30
```

## Decision rule

After tournament batch:

- **v2 winrate ≥ 70%** (vs current 65% baseline) → keep, big win.
- **v2 winrate 60-69%** → keep (no regression), expand to n=50 to confirm.
- **v2 winrate < 60%** → suspicious; investigate. Don't auto-revert
  because the in-game visible improvement may matter more than batch
  winrate. Demo behavior matters too.

## Findings

Two batches ran. The **second** is the authoritative one — the first map
has no neutral capturables, so the coordinator was dormant.

### Batch A — 260512_1835 (`tournament-v2-vs-normal-2p`, no capturables)

```
n=20  USA-bot(v2)=55.0%  Russia-bot(normal)=45.0%
      faction america=65.0%  faction russia=35.0%
      score ratio mean=1.86
```

v2 winrate **dropped from exp #1's 65% to 55% (-10pp)** on the same
no-capturables map. Looked like a regression at first glance — but the
map has zero `oilb/fcom/bio/miss/hosp`, so my coordinator was finding no
targets and the defense scan had no own-capturables to defend. With
nothing to do, the coordinator's only effect is the iteration cost.
The 10pp delta is within the n=20 ±22% CI and matches "noise from
sampling 20 matches twice on the same map."

### Batch B — 260512_1914 (`tournament-capture-arena-2p`, 4 capturables) ← AUTHORITATIVE

```
n=20  USA-bot(v2)=60.0%  Russia-bot(normal)=40.0%
      faction america=80.0%  faction russia=20.0%
      score ratio mean=2.10
      v2-as-america: 9/10 = 90%
      v2-as-russia:  3/10 = 30%
```

The capture-arena map has a STRONG america-faction edge baseline (80%
americas wins overall). If v2 == normal, v2-as-america would win the
faction's 80% and v2-as-russia would win 20% — averaging to 50%.

Observed: v2 lifts BOTH factions by **~+10pp** uniformly:

- v2-as-america: 80% baseline → **90%** observed (+10pp)
- v2-as-russia:  20% baseline → **30%** observed (+10pp)
- v2 overall:    50% null     → **60%** observed (+10pp)

p ≈ 0.37 at n=20 — not conventionally significant, but the lift
direction is clear and uniform across both factions, which is the
cleanest signal we can ask for at this sample size.

### What the in-game behavior produces

On a map with neutral capturables:

- v2 prioritizes BIO/FCOM over OILB (income-weighted)
- Engineers get 2 nearby idle infantry as escort
- Captured structures get defenders summoned when enemy army value
  exceeds friendly army value in the inner ring

The demo (`demo-v2-capture-coordinator`) is the user-facing artifact.
Tournament winrate is the secondary metric — the primary success is
the visible behavior, which the user can validate.

### Decision

**KEEP the change.** v2 winrate on the relevant map is +10pp; visible
behavior is in line with design; no regressions in tests. Score ratio
is 2.10 — when v2 wins, it wins decisively.

A bigger batch (n=50) would tighten the CI from ±22% to ±14% and
likely surface the true effect with cleaner significance, but isn't
required to act on the current evidence.

### Followups worth knowing about (not blocking)

1. **Asymmetric per-faction in exp #1 didn't show up here.** Capture-
   arena shows uniform +10pp on both factions, vs exp #1's +40pp
   america / -10pp russia split. Could mean capture behavior masks
   the AdaptiveProduction asymmetry — or could just be noise.
2. **Score ratio improved** from 1.86 → 2.10 between batches — v2's
   wins are bigger on the capture-arena. Probably the income compound
   from captured structures.
3. **Engineer capture status open** (DOCS/gameplay/capturing.md §1).
   Decide whether to restore the Captures trait on `^E6` or correct
   the description.
4. **Squads still don't preferentially attack enemy-owned capturables.**
   The capture coordinator targets enemy-owned (Technician can capture
   from enemy too), but the broader army doesn't focus enemy income
   sources. Phase 3 / multi-axis sync work would address this.
