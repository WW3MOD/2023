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

> _Pending — appended when batch + demo land._
