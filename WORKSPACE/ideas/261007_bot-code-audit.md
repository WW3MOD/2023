# Bot CODE audit — modules as implemented vs the rebuilt game model (scout pass 2026-10-07)

_Read at **`main @ 754999d5`** (worktree `wt/scout-bots-code`, branched from `origin/main`; `git rev-list
--count HEAD..@{u}` = 0 at branch time). **Read-only scout: no build, no launch, no autotest, no lint, no
`utility.sh`.** Paths are repo-relative; `ai.yaml` = `mods/ww3mod/rules/ai/ai.yaml` (line numbers drift weekly
— grep the key)._

**Provenance labels.** **[read]** = I opened the lines myself at this SHA. **[sub-read]** = read at this SHA by
one of three sub-agents I dispatched and cross-checked only at the decision line. **[hyp]** = inferred, not
observed in a match. No claim here comes from a watched game.

**Angle and overlap.** The design lane's scout `2026-10-07_opportunities-bots.md` (read at `c276679c`) covers
*visible stupidity* — including **own-SR non-defence (its #4)** and **no conventional strikes (its #11)**. This
document is the *code-level* read: which modules still encode Red Alert, which never engage, what the bots know
about the win condition, and how they spend. Where an item is already queued or proposed, it is marked
**KNOWN** with the pointer; I only add what changes the brief. The prior deep audit is
[`DOCS/bots/06-inherited-misfits.md`](../../DOCS/bots/06-inherited-misfits.md) (researched at `dcc2f7c5` /
`25a8aebd` / `af36e686`); §1 re-verifies every row at this SHA.

---

## 0. Headline — ten lines

1. **The win condition is invisible to the bots' offence.** No module reads the *enemy* SR's contestation bar,
   `Range`, or `NetEnemySurplus`. The only bot read of any bar is `NuclearBotModule` (own SR, nukes only).
2. **The human-only order that wins the game exists and bots never issue it.** Every armed unit carries
   `AttacksSupplyRoutes` (`defaults.yaml:442`) → `AttackSupplyRoute` activity: *walk into the ring and stay until
   resolved* (`Activities/AttackSupplyRoute.cs:20-25, 72-102`). Zero references under `BotModules/` **[read]**.
   Bots send a plain `AttackMove` to the SR cell (`PoiOffensiveBotModule.cs:4289`) and idle wherever it ends.
3. **Measured consequence:** 1.3% of bot force ever inside the 10-cell ring (`ai.yaml:953`); **40/40** benchmark
   matches end `time_limit`, **0** SR overruns (`260923-rebaseline.md:63-65`, same in 260922/260905).
4. **The benchmark cannot see the win condition either:** ladder metrics are POI·ticks and kill-cost
   (`260923-rebaseline.md:46-48`); the verdict has `win_reason` but no ring-time or min-bar field (`BotVsBotMatchWatcher.cs`).
5. **A dormant RA base-builder woke up and spends money:** SAM became buildable (`880b4eb5`,
   `structures-defenses.yaml:921`), so `BaseBuilderBotModule@normal`'s `sam: 2` is live on both profiles — and
   OpenRA's `failCount += failCount` (`BaseBuilderQueueManager.cs:161`) means failed placement never backs off.
   `DOCS/bots/03:86` still says the whole construction half is inert.
6. **Spending is cash-snapshot, one-item-per-empty-queue, no float, no income-rate reasoning** — and four lanes
   (FIFO drain, legacy heli pick, SR-defence, scouted counters) start items with **no affordability check** at all
   in a pay-as-you-go queue.
7. **Contestation slowdown is not reacted to** by purchasing; own-SR defence is a *purchase* (one cheapest counter
   per 300 ticks), never a recall (KNOWN — design #4).
8. **Never engage, but tick on both profiles:** `BuildingRepair`, `EngineerRouteOpen` ×2, fixed-wing
   `UnitBuilder` ×2 + `SquadManager` ×4, `Nuclear` ×2 outside Escalation; 4 classes never instantiated.
9. Of 06's rows 2–21: **12 still true as written, 8 changed, none fully fixed**; row 20 (support powers) went from
   latent to **live**.
10. Ranked briefs in §5. The top one is ~one function: give the SR-pressure axis's final leg the human's order.

---

## 1. Module-by-module: RA assumption still encoded, and whether it engages

Instance list re-derived at this SHA **[sub-read]**; profile = which `ModularBot` gets it via `ai.yaml:99-112`
conditions. "Engages?" answers *can this code ever issue an order on a shipped map in the default mode*.

| Module (instance, `ai.yaml`) | RA assumption still encoded | Engages? | Evidence |
|---|---|---|---|
| **BaseBuilderBotModule@normal** `:2524` (both) | Construction yard + base build-out + silo-cash threshold. `ConstructionYardTypes: supplyroute`. | **PARTLY — SAM only (new since 06)**. 7 of 8 `BuildingFractions` still `~disabled`; `sam` buildable since `880b4eb5`. `NewProductionCashThreshold` reads silo `Resources`, always 0 → no gate. `failCount += failCount` (0+0) **[read]** never reaches `MaximumFailedPlacementAttempts`, so no back-off. | `structures-defenses.yaml:921`; `structures.yaml:470` (SR produces `Defense`); `player.yaml:35` (Defense queue); `BaseBuilderQueueManager.cs:114,157-172,219` |
| **BuildingRepairBotModule@aiplayer** `:1489` (both) | Repairable base buildings. | **NO** — no `RepairableBuilding` in the mod. | sub-read; KNOWN in `03` |
| **UnitBuilderBotModule** fixed-wing ×2 `:2562/:2703` | RA aircraft production off airfields (`SkipRearmBuildingCheck` bypass). | **NO as a buyer** — a10/f16/mig/frog `~disabled` (`aircraft-america.yaml:455,584`, `aircraft-russia.yaml:471,593`). Survives only as a FIFO drain for other modules' requests. | sub-read |
| **SquadManagerBotModule** ×4 `:2591/:2722/:3480/:3494` | RA squad state machine: rush the construction yard (`TryToRushAttack`), protect base, flee to a random own building. | **NO** — fixed-wing airframes unbuyable; ground/naval/protection branches off (`IgnoreGroundUnits: true`, `ProtectionTypes`/`NavalUnitsTypes` unset, `ConstructionYardTypes` unset). [hyp] exception: a support power that spawns player-owned airframes. | 06 row 12 still true |
| **HelicopterSquadBotModule** `:2823/:2873` | `StateBase.ShouldFlee` vetoes retreat near any own building (= the SR) and flees to `RandomBuildingLocation`. Omniscient `FindClosestEnemy`. | YES. Retreat doctrine still RA (06 row 15); target pick omniscient but now capped at 40 cells (06 row 5). | `StateBase.cs:29-38,90-110`; `HelicopterStates.cs:388-401,535-541` |
| **EngineerRouteOpenBotModule** `:2261/:3580` | RA bridge-hut repair. | **NO** — 0 bridge/bridgehut actors on all 10 maps; now twinned on `@stable` too, so the "declared ONLY here" comment `:2259-2260` is stale. | 06 row 16, scope grew |
| **NuclearBotModule** `:3059/:3604` | (WW3MOD) | **Only in Escalation** (`NuclearBotModule.cs:278`). | sub-read |
| **GarrisonBotModule@defenses** `:1611` (both) | RA "defenses" naming; garrisons any `PassengerInfo` host. | YES — competes for idle infantry; 06 row 13 unchanged. | `GarrisonBotModule.cs:19,495-503` |
| **AdaptiveProductionBotModule** ×4 | RA "scout then counter-build" gated on `MinEnemySightings` (overwritten, never-decaying blackboard counters). | YES. Gate still there, but since `3318d5c7` both profiles run SR-defence + CompositionNeed, which bypass it. Comment `:256-260` ("@stable omits the flag") now false. | 06 row 8 changed |
| **UnitBuilderBotModule** ground ×4 (`ai-america/russia.yaml`) | Prerequisite-gated `BuildableItems()` (tech gating survives as the mechanism, not as a tree). No factory/harvester/power logic left. | YES — the call-in economy (§4). `@*.normal` is the **stable** twin; the name misleads. | sub-read |
| PoiOffensive, PoiGarrison, LaneAmbush, LayeredDefence, MountedTransport, CaptureCoordinator, SupplyFollower, LogisticsCenter, Scout, Drone, Minelayer, EngineerOperator, CounterBatteryRadar (twinned) | WW3MOD-built; RA residue is *doctrine*, not mechanics: retreat to SR (`PoiOffensiveBotModule.cs:5050-5054`), supply-shaped transport (06 row 4). | YES. CounterBatteryRadar's output read by nothing (KNOWN — design #8). | sub-read |
| Never instantiated | `CaptureManager`, `Harvester`, `McvManager`, `SupportPower` | — | grep of `mods/` **[sub-read]** |

### 1.1 06's ranked table, rows 2–21, re-verified at `754999d5` [sub-read]

| Row | Verdict now | What moved |
|---|---|---|
| 2 heli hosts disabled | CHANGED, mitigated | `fba34159`: readiness gates ask whether a host exists, so damaged helis are no longer benched; spent helis evacuate. HPAD/AFLD still `~disabled`. |
| 3 `--countdown` cadences | TRUE, **bigger** | **35** sites now; `ModularBot.cs:251` still says "24". |
| 4 supply-shaped transport | TRUE | 6-cell pickup corridor is the only mitigation. |
| 5 omniscient air targeting | TRUE, bounded | 40-cell caps. |
| 6 inert BaseBuilder config | **CHANGED — partly live** | SAM. See §5 brief B. |
| 7 tick order differs by profile | TRUE | order gate damps only. |
| 8 AdaptiveProduction gate | CHANGED, mostly bypassed | `3318d5c7`. |
| 9 `Release` keyed on actor | TRUE | `PoiGoalGuard.cs:100`. |
| 10 two coordination generations | TRUE | |
| 11 activity-layer movers | CHANGED | human `tacpos` grant removed `6705bd20`; `AutoSeekSupplies` still on humans. |
| 12 dead squad states | TRUE | |
| 13 Garrison@defenses | TRUE | |
| 14 hit-and-run unreachable | TRUE | |
| 15 RA retreat doctrine | TRUE | lines moved (`HelicopterStates.cs:962`). |
| 16 EngineerRouteOpen inert | TRUE, scope grew | `@stable` twin added. |
| 17 blackboard task API | TRUE | 0 callers. |
| 18 `MinOrderQuotientPerTick` | TRUE | |
| 19 rank table hand-kept | TRUE | |
| 20 SupportPower absent | **CHANGED — now LIVE** | powers reachable by faction prerequisite (`player.yaml:157-162`, e.g. `RuIskander`/`GBU57`); no bot buys one. |
| 21 stale cross-file comments | TRUE, more instances | new: `ai.yaml:1158,1171,1351,1429` ("NO @stable TWIN" — twins exist at `:3516-3560`), `:2259-2260`, `AdaptiveProductionBotModule.cs:256-260`, `SupplyPrecedenceMath.cs:56`, `ModularBot.cs:251`. |

---

## 2. Engagement — what is configured to never fire

Beyond the "NO" rows above:

- **Four `PoiOffensive@experimental` levers ship `false`** (`ai.yaml:474,553,914,973`), including
  `CoordinatedAssaultEnabled` and `CloseInRatchetEnabled` — the two that touch the contestation ring. The ratchet
  was measured and lost (`ai.yaml:944-962`); `AssaultRingCells: 10` is **telemetry only, drives no decision**
  (`PoiOffensiveBotModule.cs:892-903`) and, with the flag off, the census that emits `insideRing` does not run
  (`ai.yaml:940-947`). **So the only bot code that knows the ring radius is switched off.** [read]
- `InfantryEscortHoldEnabled` OFF and `RendezvousWithOffensiveStaging` false on both twins
  (`260923-rebaseline.md:18-19`).
- `IBotRequestPauseUnitProduction` has no implementer (`UnitBuilderBotModule.cs:621`) [sub-read].
- `BotBlackboard` task API: 0 callers (06 row 17).

---

## 3. Contestation awareness

### 3.1 Reads of the win-condition state

| Reader | Reads | Used for | Evidence |
|---|---|---|---|
| `NuclearBotModule` | own SR `ControlBarFraction` (lowest of owned) | "losing now" trigger + SR aim bonus, Escalation only | `NuclearBotModule.cs:308-311,346,425,623-641`; `LosingContestationPercent: 40` both profiles [sub-read] |
| **anything else** | — | — | grep `ControlBar\|NetEnemySurplus\|SupplyRouteContestation` over `BotModules/` and `PoiMap.cs` [sub-read; I re-grepped `AttackSupplyRoute` myself] |

### 3.2 Own SR (defence) — KNOWN (design #4), with one addition

- Only response: `AdaptiveProductionBotModule.TrySupplyRouteDefense` (`:373-454`) queues one cheapest counter per
  300-tick cycle when believed enemy value near the SR passes a threshold (armour 1200, air/inf 1000), both
  profiles. **Its zone is a Chebyshev square from `sr.Location`** (`:459`) while contestation is a Euclidean
  circle `Range: 10c0` from `CenterPosition` (`structures.yaml:386`, `SupplyRouteContestation.cs:268`) — the
  square reaches ~14 cells on the diagonals [sub-read; geometry is arithmetic].
- `PoiMap.GetDefendTargets` skips the SR: *"SR defence is out of scope (decision #2)"* (`PoiMap.cs:420-421`), and
  `TryScore` drops it as *"handled elsewhere"* (`:465-471`). Nothing else handles it.
- **Addition to design #4's brief:** the recall it proposes does not need a new movement primitive.
  `AttackSupplyRoute` on an **allied** SR already means *go into the ring and stay until
  `NetEnemySurplus <= 0 && ControlBarFraction >= 95`* (`AttackSupplyRoute.cs:113-119`) [read] — the exact
  release condition that brief has to invent a hysteresis band for. Same order, both directions (§5 A + C).

### 3.3 Enemy SR (offence) — the gap this audit adds

- **The enemy SR is an objective.** `PoiMap.GetOffensiveTargets` adds it as `PoiAction.Pressure`
  (`PoiMap.cs:298-387`), value `SupplyRouteDenyValue: 120` (`world.yaml:368`), bias 80 vs 150 for neutral income;
  rescaled by `SrPressureScoreMultiplier: 260` and capped at `SrPoolSharePct: 40` of the army; identical on both
  profiles [sub-read]. Fog-legal *for SRs only* because SR is `AlwaysVisible` to enemies
  (`structures.yaml:356-357`, user ruling 2026-08-27).
- **The order is wrong for the objective.** A Pressure axis emits a grouped `AttackMove` to `t.Location`
  (`PoiOffensiveBotModule.cs:1886`, `:4289`) [read]. The SR is `NoAutoTarget`, so the AttackMove engages what it
  meets and then **ends** at the cell; there is no "stay in ring", no re-issue unless the target moves ≥3 cells or
  the unit set changes (`:4250-4255`), and cohesion switches at `AssaultRadiusCells: 15` — outside the ring.
- **The measured result** is the 1.3% `inside10` figure and the 100%-`reason=dropped` axis churn
  (`ai.yaml:953-962`). Churn is the other half: an axis that does reach the ring is dissolved when its target
  falls out of the sticky top-k (KNOWN — design #1, sticky-target defect).
- **No "destroy everything" logic remains live.** `SquadManager.TryToRushAttack` targets `ConstructionYardTypes`,
  empty on all four instances [sub-read].
- **Parked overlap:** PIPELINE item 18 "(Future) Should I attack?" is the *decision* layer (when to go for the
  kill). Brief A below is not that — it is making the existing Pressure axis use the order that actually counts.
- **[hyp, unverified]** Fixed-wing `FindClosestEnemy` does not exclude `NoAutoTarget`, so an air squad could lock
  onto the SR and sit on an unattackable target. Moot while fixed-wing is unbuyable.

---

## 4. Reinforcement economics — bot vs human [sub-read unless marked]

**Income** (engine defaults, `player.yaml:1048-1051` commented out): passive 100 per 50 ticks
(`PlayerResources.cs:63-69`), start 20000 (tournament scenarios override to 7500, `rules.yaml` [read]);
`CashTrickler` POIs OILB +50 / FCOM +100 / BIO +150 per interval; upkeep `InfersUpkeep PermilleCost: 5` on
vehicles and infantry. [hyp] An army worth ~20k eats the whole passive stream.

| | Bot | Human |
|---|---|---|
| Cadence | `FeedbackTime = 30` ticks: 1 priority request, 1 FIFO request, then one `BuildUnit` per queue (`UnitBuilderBotModule.cs:801-837`) | any time |
| Parallelism | **one item per queue, only when that queue is empty** (`:1003`, `:1816`; quantity 1 at `:1063`, `:1823`) | stack many; infantry queue is parallel (`player.yaml:57`) |
| What | ground: truck shortfall → `UnitFloors` → group completion → furthest-below-target value share; skips the cycle rather than random-buy (`:1890-1975`). heli: weighted shuffle. AdaptiveProduction adds ≤2 requests / 300 ticks | intent |
| Float | **none general**. Composition lanes need 100% of cost on hand; AdaptiveProduction need-lane 200%; truck-bank holds ≤4 stalled cycles; LogisticsCenter needs 3000. **FIFO drain, legacy heli pick, SR-defence and scouted counters: no affordability check.** Code admits "a bot spends to zero routinely" (`:1967`) | saves for a target |
| Income vs spend | **never reasoned about** — `PassiveIncomeAmount`/`Upkeep`/`NetChange` reach only a log line (`:927-931`); decisions read `AvailableBudget()` snapshot (`:2414-2418`) | watches the ticker |
| Contested SR (slowdown) | no reaction; [hyp] fewer empty-queue cycles → cash accrues idle | buys defenders, pulls army home |
| Caps | no pop cap; `UnitLimits` count world actors only (overshoot while pending); FIFO ignores limits unless `EnforceExternalRequestUnitLimits` | — |
| Support powers | **never** — no bot lists the Powers queue; only `StartProduction` calls are `UnitBuilderBotModule.cs:1063,1823` and `BaseBuilderQueueManager.cs:120` | 6 `RequiresPurchase` powers |
| RA residue | SAM via BaseBuilder (§5 B) | — |

**[hyp] Starvation interaction.** `ProductionQueue` is pay-as-you-go (`ProductionQueue.cs:879-884`). A
no-affordability lane can start a 6000-credit heli (or a 2000 SAM) at low cash; that item then drains every income
tick, pinning cash near 0, so the ground lane's 100%-on-hand test fails until it completes. Plausible, not
measured — the benchmark corpus does not record per-queue stall ticks.

Profiles: ground UnitBuilder twins identical on every shared key; `@experimental` adds `FirstTruckNeedThreshold`,
three `CounterMatrixPct` rows, trade-feedback knobs, `MinGroupSizes abrams/t90: 2`. Heli, fixed-wing and
AdaptiveProduction twins identical.

---

## 5. Ranked code changes — dispatch-ready briefs

Ranked by **(effect on the win condition or on money actually spent) × (confidence) ÷ (cost)**. Every brief:
zero RNG, new Info fields default to baseline in C# (CLAUDE.md shared-trait rule), opted in per profile in YAML;
`@stable` may inherit deliberately, stated in the commit.

### A. Pressure axes hold the ring with the order humans use — **NEW** (S, high confidence in mechanism)
- **File / function:** `PoiOffensiveBotModule.cs`, the axis order site that issues the final `AttackMove`
  (`~:4280-4292`), for axes whose target is `PoiAction.Pressure`.
- **Change:** when the axis lead is within `SrHoldEngageCells` (new field; suggest 14 ≥ `Range` + slack) of the
  enemy SR, issue `new Order("AttackSupplyRoute", null, Target.FromActor(sr), false, groupedActors: units)` instead
  of the `AttackMove`. Default `false` in C#; on for `@experimental`. Keep `axis.OrderedCell` stamping so re-issue
  logic does not thrash.
- **Expected delta:** units that reach the ring stay and count toward `NetEnemySurplus` instead of idling at the
  end cell; `inside10` should rise above 1.3%; first-ever non-`time_limit` verdicts in bot-vs-bot become possible.
- **Risks [hyp]:** (1) `AttackSupplyRoute.Tick` holds with no child activity — whether units *return fire* while
  holding depends on AutoTarget/opportunity-fire behaviour under a non-idle activity; read `AutoTarget` scan
  conditions first, and if non-turreted units go passive, queue `AttackMove` to the ring centre first and the hold
  after it. (2) Axis churn (design #1) will still dissolve holding axes — fix the sticky-target defect first or the
  delta is masked. (3) A held axis no longer retreats via the move path; check `RetreatWhenLosing` still wins.
- **Show it:** one scenario `test-bot-holds-enemy-sr-ring`: bot with 6 units on a Pressure axis vs a passive enemy
  SR; **pass** = ≥3 bot units inside `Range` for ≥500 consecutive ticks and enemy bar < 100%; fail text
  `"ring occupancy peaked at N units for M ticks"`. RED on HEAD (expect ≈0), then GREEN.
- **Read first:** `DOCS/reference/supply-route.md` whole; `Activities/AttackSupplyRoute.cs`; ai.yaml `:930-973`
  (why the ratchet lost — this is not a ratchet: it changes the order, not the hold logic).

### B. Stop the RA base-builder spending 2000 on a SAM it may not be able to place — **NEW** (XS–S)
- **File / function:** `BotModuleLogic/BaseBuilderQueueManager.cs:161` (`failCount += failCount` → `failCount++`
  — inherited OpenRA bug, counter is stuck at 0); and a ruling on `ai.yaml:2552` `sam: 2`.
- **Change:** fix the increment unconditionally (it only makes back-off reachable). Then **either** remove `sam`
  from `BuildingFractions` (bot never buys static AA) **or** keep it deliberately and add an affordability floor.
  That second half is a design call for the user: the SAM is *"the only structure that can shoot down incoming
  ballistic missiles"* (`structures-defenses.yaml:922`), so a bot owning one is arguably correct.
- **Expected delta [hyp]:** if placement fails (SR gives no buildable area; FCOM/LC/HPAD/AFLD do), today it can
  loop produce → cancel/refund with no back-off, intermittently locking up to 2000 credits and the Defense queue.
- **Show it — verify the premise first, cheaply:** one bot-vs-bot match with BotDebug on; grep the log for
  `has nowhere to place sam` and count SAM actors owned by bots at end. If neither appears, downgrade B to the
  one-line fix plus correcting `DOCS/bots/03:86`.

### C. Recall to own SR via `AttackSupplyRoute` — **KNOWN (design #4)**, brief amended
Use §3.2: the activity already has the release condition (`NetEnemySurplus <= 0 && bar >= 95`). Brief shrinks to
*trigger + choose which units*, which removes the hysteresis-band invention from that brief. Shares A's
return-fire risk; do A's AutoTarget read once for both.

### D. Contestation telemetry in the verdict — **NEW** (S, enabling)
- **File:** `engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs` (verdict v8 → v9, additive).
- **Change:** per player, record `min_control_bar_pct`, `ticks_bar_below_100`, and `ticks_with_enemy_in_ring`
  (sample `SupplyRouteContestation` per SR at the watcher's existing cadence).
- **Expected delta:** none in play. Benchmarks become able to see A and C — today the ladder metrics are
  POI·ticks and kill-cost and every match ends `time_limit`, so a contestation improvement would read as noise.
- **Show it:** one tournament match; fields present and non-trivial in `match_N.json`. Note: changing the schema
  invalidates field-comparability with 260923 only for the new fields; existing fields are untouched.

### E. Affordability gate on the unchecked lanes — **NEW** (S, medium confidence the problem bites)
- **File / function:** `UnitBuilderBotModule.cs` FIFO drain (`:1803-1829`) and legacy pick (`:1015-1063`).
- **Change:** don't `StartProduction` when `cost > AvailableBudget() + FifoAffordHorizonTicks × netIncomePerTick`
  (new field, default = unbounded = today). This is the first place income rate would enter a decision.
- **Expected delta [hyp]:** fewer windows where one expensive pending item starves the ground composition lane.
- **Show it — premise first:** add a log counter of ticks where ground `BuildUnit` skipped for affordability while
  another queue held an unpaid item; one benchmark-config match. If it is rare, drop E.

### F. Conventional support powers — **KNOWN (design #11, 06 row 20 now LIVE)**
Confirmed reachable outside Escalation: `powers.america`/`powers.russia` are granted by faction
(`player.yaml:157-162`) and gate `RuIskander`, `RuKalibr`, `GBU57`, etc. (`:271-278`, `:606`). Brief as in design
#11; the purchase is a Powers-queue `StartProduction`, which no bot code issues today.

### G. Comment and doc rot, one commit — **NEW instances of 06 row 21** (XS)
`ModularBot.cs:251` "24 sites" → 35; `AdaptiveProductionBotModule.cs:256-260`; `SupplyPrecedenceMath.cs:56`;
`ai.yaml:1158,1171,1351,1429` ("NO @stable TWIN ON PURPOSE" — twins exist); `ai.yaml:2259-2260`;
`DOCS/bots/03-module-catalogue.md:86` (SAM is live). No behaviour change; `make test` is the gate.

### H. Remove or label the never-engaging instances — 06 rows 6/12/16 (XS–S)
`BuildingRepair@aiplayer`, `EngineerRouteOpen` ×2, fixed-wing `UnitBuilder` ×2 and `SquadManager` ×4 tick on
every bot for no order. Either delete the instances or put `# INERT on shipped maps:` headers on them. Lowest
rank: costs attention and a little tick time, not behaviour. **Check first** that no autotest scenario
references these instance names.

---

## 6. What this pass did not verify

- **No match was run or watched.** Everything in §5 marked [hyp] is code-shaped inference; A's and C's whole value
  depends on the return-fire question (A risk 1), which I did not resolve.
- I re-read myself: `AttackSupplyRoute.cs` whole, the bot order site `:4280-4292`, `AttacksSupplyRoutes` on
  `defaults.yaml:442`, `failCount` lines, the SAM prerequisite, SR `Produces`, the Defense queue, the ratchet
  record `ai.yaml:940-962`, the benchmark headers and power prerequisites. The module instance table, economics
  lane table and row 2–21 verdicts are **sub-agent reads** spot-checked only at decision lines.
- I did not check whether any bot actually owns a SAM in the 260923 corpus (raw results are untracked and were
  not read).
- `git log -S` across the repo timed out for one lookup (`TransportMissionSlots: 1`), so that row's fixing commit
  is unattributed.
- "Contestation radius 10 cells" assumes the shipped `Range: 10c0`; scenario overrides were not surveyed.
