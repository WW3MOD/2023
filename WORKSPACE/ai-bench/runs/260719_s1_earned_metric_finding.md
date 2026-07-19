# S1 finding — the LC-delist AI fix WORKS (v2 now captures the nearest OILB), but `resources_earned` is still 0 for a NEW, metric-side reason

**Cycle:** `260719_1934__tournament-s1-eco-river-zeta__2d5433a` (N=1 hidden smoke, Mode B)
**Change under test:** commit `2d5433aa` — delist `$0` `logisticscenter` from v2's two
income-scoring tables (`PoiMap.IncomeWeights` in `world.yaml`; `CaptureCoordinatorBotModule@v2.tecn.IncomeWeights` in `ai.yaml`). The fix recommended by `260719_s1_no_capture_diagnosis.md`.
**Method:** one hidden match + static engine/YAML analysis of the surviving debug log. **No re-rolls.**
**Boundary verdict:** the AI fix is **confirmed correct**. The remaining 0 is a **METRIC/HARNESS-SIDE**
defect (`resources_earned` cannot see owned-derrick income in this economy), **not** a v2 behavior gap.

---

## TL;DR

1. **The fix did exactly what it was designed to do.** With `logisticscenter` delisted, PoiMap's
   top capture target flipped from the `$0` Logistics Center to the **nearest OILB derrick** (`oilb@17,44`,
   ~3 cells from v2's SR). v2 issued a capture order to it (tick 1351), walked the TECN adjacent, and
   **completed the capture (~tick 1550).** The exact failure the diagnosis root-caused is resolved.
2. **`resources_earned` is nonetheless still 0** — but for a *different, downstream* reason. v2 captured
   and **still owns** the derrick at match end, yet its `$50/interval` CashTrickler income never reaches
   `PlayerResources.Earned`, which is what the verdict reports as `resources_earned`. Both bots read
   `Earned = 0` for the **entire** match, before *and* after the capture. The metric is structurally
   insensitive to owned-derrick income in WW3MOD's SR-budget economy.
3. **Do not re-run to chase a non-zero.** The bar isn't met because the *yardstick's income accounting*
   can't register the win, not because v2 played badly. Next cycle is a harness/metric fix, not an AI change.

Result: **winner Russia-bot (normal) on time_limit @7500t; v2 `resources_earned` 0, normal 0; ~110s wall.**

---

## Evidence chain (file:line + debug log)

### A. The fix retargeted v2 to the real derrick (was: the `$0` LC)
Debug log (`%APPDATA%\OpenRA\Logs\debug.log`, the 19:34 smoke):
```
[v2-capture] poimap-scan player=USA-bot idleCapturers=1 targets=13 top=oilb@17,44 action=Capture score=45000000 tick=1351
[v2-capture] issue     player=USA-bot actor=tecn.america@14,45 → oilb@17,44 score=45000000 tick=1351
```
- **`top=oilb@17,44`, not `logisticscenter`.** `targets=13` (was `15` pre-fix — the two River Zeta LCs
  are gone from the income-POI set). The delist landed and PoiMap now ranks the nearest derrick #1.
- Contrast the pre-fix diagnosis log: `top=logisticscenter@31,52 score=104000000`. Fixed.

### B. The TECN reached the derrick and entered the capture
```
[v2-capture] pre-scan ... actor=tecn.america@16,44 idle=False activity=CaptureActor committed=True commitN=1 tick=1426
[v2-capture] pre-scan ... actor=tecn.america@16,44 idle=False activity=CaptureActor committed=True commitN=1 tick=1501
```
- TECN moved `14,45 → 16,44` (adjacent to `oilb@17,44`) and was in `CaptureActor` by tick 1426.
- After tick 1501 the TECN vanishes from all later scans — **consumed by a completed capture**, not
  killed (see C: nothing was destroyed).

### C. The capture COMPLETED and USA still OWNS the derrick at match end
Three independent signals, all consistent with a finished ownership change (not a death/destruction):
- **POI pool shrank on ownership, not destruction.** `[v2-offense] reeval ... targets=13` at t1507 →
  `targets=12` at t1607: one OILB left the *neutral income-POI* set. Verdict stats show
  **`buildings_killed:0` and `buildings_dead:0` for BOTH bots** — nothing was destroyed, so the derrick
  left the pool because it became **owned** (captured), the only other way out of the set.
- **v2's garrison layer began holding it.** `[v2-garrison] reeval ... held=0 ... tick=1516` →
  `held=1 ... tick=1616`. You can only garrison a structure you own.
- **Asset accounting shows an owned non-army building.** Verdict: USA `army_value 1250` vs
  `assets_value 1450` (**+200** = an owned building's value); Russia `army_value == assets_value` (3100,
  owns no building). USA holds the captured OILB through tick 7500.
- The offense correspondingly `retire ... target=oilb ... reason=dropped tick=1607` and re-tasked to the
  next derrick — i.e. it stopped "securing" `17,44` because it was **secured/owned**.

### D. …yet `resources_earned` (= `PlayerResources.Earned`) never moved
- Verdict: `resources_earned:0` for **both** players; `score_components.capture_income:0` throughout.
- `[Tournament] tick=X scores`: USA's weighted score only swings with army/kills (e.g. 4700→2400 when
  units die, +100 blips on kills) — **never the steady linear ramp** a held `$50/interval` derrick would
  add from ~t1600→t7500 (which would be on the order of **+$5,900** of `Earned`). It is flat at the
  income axis. Both bots sit at `Earned=0` before *and* after any capture.

### E. Why `Earned` can't see the derrick income (the root cause)
- `resources_earned` is emitted as `playerResources.Earned` (`BotVsBotMatchWatcher.cs:308`).
- OILB income is a **CashTrickler** (`structures-neutral.yaml:19`, `Amount:50`). CashTrickler does **not**
  call `GiveCash`; it registers passive income via `resources.AddIncome(...)` (`CashTrickler.cs:127`),
  summed into `PlayerResources.TotalBuildingIncome` (`PlayerResources.cs:331`). CashTrickler correctly
  **re-registers under the new owner on capture** (`INotifyOwnerChanged`, `CashTrickler.cs:71-76`), so the
  income *is* attributed to USA after the capture.
- `TotalBuildingIncome` is only paid out — and `Earned` only incremented — through the periodic
  unified-economy tick: `if (self.Owner.Playable) ChangeCash(PassiveIncomeAmount + TotalBuildingIncome − Upkeep)`
  (`PlayerResources.cs:199-205`), and `ChangeCash` credits `Earned` **only when its argument is ≥ 0**
  (`ChangeCash:208-211 → GiveCash:249-280`, `Earned += num`). The harvester path that would credit `Earned`
  directly (`GiveResources:228-231`) is **never used in WW3MOD** (no harvesters — SR-budget economy).
- Bot players are `Playable` (map default; `map.yaml` sets no override, engine default
  `PlayerReference.Playable=true`), so the `Playable` gate is **not** the blocker.
- **Therefore:** for `Earned` to stay exactly 0 the whole match — including ~5,900 ticks of derrick
  ownership — the periodic `PassiveIncomeAmount + TotalBuildingIncome − Upkeep` must be **≤ 0 every
  interval** (so `ChangeCash` takes the `TakeCash` branch and never touches `Earned`). In this SR-budget
  benchmark passive income is effectively off and a single `$50` derrick does not produce a net-positive,
  `Earned`-crediting tick against standing costs. Net-vs-gross: **`Earned` measures net periodic cash and
  is blind to a lone captured derrick's gross income.** That is why the bar reads 0 despite a real capture.

> The exact offsetting term (upkeep amount vs the lobby `passiveincome` setting) is the one value not
> nailed from the log alone; it is a static config read + a one-line watcher probe next cycle, **not** a
> reason to re-roll. The *conclusion* — capture completed, derrick owned, `Earned` blind to it — is fully
> evidenced by C+D above regardless of the precise offset.

---

## Boundary: AI fix = CORRECT; remaining 0 = METRIC-SIDE

- **AI side (this cycle):** the delist is a strict improvement and is **verified working** — v2 stopped
  wasting its sole TECN on a `$0` depot and now captures the nearest income derrick in-window. This is
  exactly the S1 behavior the benchmark wants. Tests stayed **275/275**. Kept, committed on `ai-bench`.
- **Not the AI's fault that the number is still 0:** v2 did the right thing and owns the derrick. The
  yardstick's income metric (`PlayerResources.Earned`) cannot register that income in this economy — the
  same *class* of "the ruler can't see the thing S1 measures" problem the prior two cycles fixed at the
  **map** level (no POIs) and here surfaces one layer deeper at the **accounting** level.

## Recommended next-cycle fix (HARNESS/METRIC — no AI change, no re-roll)

Measure S1 economy success by a **gross** derrick-income signal instead of net `Earned`. Options, cheapest first:
1. **Add a cumulative capture-income accumulator to `BotVsBotMatchWatcher`** — sum each player's
   `TotalBuildingIncome` paid per interval (or `ownedCashTricklers × ratePerTick`), emit it as
   `capture_income_cumulative` / repoint `resources_earned` at it. This is the true S1 metric.
2. Or confirm the tournament lobby `passiveincome`/upkeep settings; if upkeep masks derrick income,
   the net metric is simply the wrong choice and #1 supersedes it.
3. Only after the metric can see income: re-establish the S1 baseline (N=10) + build the mirror twin +
   Normal-vs-Normal calibration (SPEC §9.4).

## What this does NOT change

Unit stats, the LC's own traits, control/legacy AI configs, the map, and the engine are all untouched.
The `logisticscenter`-as-*strategic-deny* idea remains a possible **separate** future objective type
(low-priority offense/deny target), never an *income* POI — recorded again here for the backlog.
