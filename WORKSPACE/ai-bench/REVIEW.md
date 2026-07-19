# AI Benchmark — Review Board

**This is the one document to check.** Designed for a **one-minute review**:
skim *At a Glance*, drop any directives in *Your Inbox*, glance at what's new
above the *review cursor* in the *Activity Log*. Protocol defined in
[`SPEC.md`](SPEC.md) §7. The manager reads this file at the start of **every**
cycle and treats the Inbox as outranking its own backlog.

> **Terminology — v2 → Experimental (2026-07-20, `ai-bench` rename commit).** The dev
> bot formerly keyed `@v2` / "V2 AI (experimental)" is now `ModularBot@experimental` /
> "Experimental AI"; a frozen **`ModularBot@stable`** ("Stable AI") holds the last
> validated snapshot (promotion policy: SPEC §13). Old references below (`@v2`,
> `enable-ai-v2`, `[v2-*]` logs, `tournament-v2-vs-normal-*`) map to `@experimental` /
> `enable-ai-experimental` / `[exp-*]` / `tournament-experimental-vs-normal-*`.
> **Historical run records under `runs/` keep their original "v2" names** — history
> stays as written; only "v2 median" column labels and this board's live rows read old.

---

## 1. At a Glance

| | |
|---|---|
| **System** | AI Benchmark — autonomous improvement of the Experimental AI (`ModularBot@v2`) vs the Normal control |
| **Active rung** | River Zeta WW3 (Scenarios 1–3, [`LADDER.md`](LADDER.md)) — **not yet cleared** |
| **Run mode** | **Mode B (hidden / unsupervised) — ACTIVE.** `OPENRA_WINDOW_HIDDEN=1` verified (no window, no focus steal, verdict written, sim/render decoupled). Unlimited unattended runs. Mode A (windowed) is the fallback only if the decoupling regresses (SPEC §3.1) |
| **Last cycle** | 2026-07-20 · `cd06358` (branch) · **HARNESS ×2** — (A) repointed the scorer/win-rule economy term net→gross (verdict_version 3→4, LADDER 1a), (B) built the S1 mirror scenario (bots swapped). NUnit 282→**287**, build green, 2 hidden smokes boot full 7500t. NOT merged. |
| **Headline** | **Win-rule economy term now counts held-derrick income; S1 mirror built.** (A) `WeightedComponentMatchScorer.capture_income` — which feeds `TimeOrSrCaptureWinRule`, i.e. match *outcomes* — now reads the **gross** integral (`state.GrossCaptureIncomeFor`, same value as `capture_income_gross`) instead of net `PlayerResources.Earned`, which was structurally blind to a held derrick in the SR-budget economy. No JSON field added/removed (`capture_income_gross`, `resources_earned` both unchanged); **verdict_version 3→4** flags the changed *meaning* of the emitted `score_components.capture_income`. Safe now because S2/S3 have no recorded baselines to invalidate. Weighting math factored to pure `WeightedComponentScoring` + `WeightedComponentScoringTest` (5 cases); NUnit 282→**287**, build green. (B) Added **`tournament-s1-eco-river-zeta-mirror`** — byte-identical to the primary with the two bots' spawn assignments swapped (Experimental on Russia/80,35, Normal on USA/14,45); averaging primary+mirror cancels spawn-side derrick-distance luck (SPEC §9.4). Two hidden N=1 smokes (primary + mirror) both boot + run full 7500t at verdict_version 4 and confirm `capture_income == gross×2` in every player-case (gross 0 both draws — the known in-window capture-reliability variance, not a wiring issue; the nonzero path is pinned by the unit test). Committed on `ai-bench` (`cd06358a`), NOT merged. **Next:** S1 N=10 baseline + Normal-vs-Normal calibration across primary+mirror. |

---

## 2. Your Inbox  (user → manager)

> **How to use (protocol, SPEC §7.2):**
> - Add a directive as `- [ ] your thought here`.
> - The manager reads every cycle, acts, and appends its response after ` → `
>   on the same line. It will **not** check the box or delete your text.
> - When you've read the manager's response, **check the box** `- [x]`. That's
>   your "seen" signal — the manager then moves it to *Resolved directives* at
>   the bottom next cycle.
> - Unchecked = still live. Checked = you're done with it.

- [ ] (mgr-flagged 2026-07-19 `86aa2db`) S1 economy metric is `0/0` again after the map rescope — but now because **neither bot captures a derrick in 5 game-minutes**, not because the map lacks POIs (that wall is fixed). Normal earning $0 is correct (no capture logic). v2 earning $0 is the real gap: its `PoiMap → CaptureCoordinatorBotModule` layer didn't convert reachable POIs into a capture in-window. → (mgr) Recommend the **first AI cycle** target exactly this (compare v2 capture on `tournament-capture-arena-2p` vs here to localize the gap); I did **not** bump the 300s clock (nearest derrick is ~3 cells away, so the clock isn't the blocker). Flagging in case you'd rather I first bump S1 to 420–600s or add a capture-forcing setup before touching v2. Also pending: build the S1 mirror + a Normal-vs-Normal calibration batch before any v2 number is trusted (SPEC §9.4). → (mgr 2026-07-19 `0eb71d49`) **ROOT-CAUSED (static + debug log), AI-code-side.** The prior "capture layer didn't fire" framing was wrong: v2 *did* build a TECN and *did* issue a capture — but to a **Logistics Center @31,52 (score 104M), not the derrick 3 cells from its SR**. `PoiMap.IncomeWeights` (`world.yaml:303`) lists `logisticscenter: 200` — the **highest** weight — yet the LC has **no CashTrickler** (`structures.yaml:345-403`), so capturing it earns **$0**. The sole TECN is goal-guard-committed cross-map to the LC, dies ~t1737, and the 12 real OILB derricks are never touched → `resources_earned` 0 by *mis-scoring*, not map/reachability. **One-line fix (loop's first AI cycle): delist `logisticscenter` from `PoiMap.IncomeWeights` (`world.yaml:303`) + `CaptureCoordinatorBotModule@v2.tecn.IncomeWeights` (`ai.yaml:126`)** — a no-income depot must not be an income POI (PoiMap.cs:91-92 contract). I did NOT change AI code per the boundary. Full evidence: `runs/260719_s1_no_capture_diagnosis.md`. → (mgr 2026-07-19 `2d5433a`) **DONE — fix applied + verified; the capture gap is closed.** Delisted `logisticscenter` from both v2 income tables; hidden N=1 smoke confirms v2 now targets *and captures* the nearest OILB (still owns it at match end). **New blocker surfaced one layer down:** `resources_earned` (net `PlayerResources.Earned`) is structurally blind to a lone captured derrick's gross income in the SR-budget economy, so S1 still reads 0/0 — a **metric** problem, not a v2 problem. Next cycle is a harness fix (gross capture-income accumulator), not an AI change; I did **not** re-roll. Finding: `runs/260719_s1_earned_metric_finding.md`. Mirror + Normal-vs-Normal calibration still pending, now gated behind the metric fix. → (mgr 2026-07-19 `2bb65d6`) **METRIC BLOCKER RESOLVED.** Added `GrossIncomeIntegrator` (read-only, observer-only) + new stats field `capture_income_gross` (verdict_version 3, additive — no scorer/win-rule input touched). Hidden N=1 smoke: **v2 `capture_income_gross` 6093, control 0** (~$5,900 predicted from the held $50 derrick). `resources_earned` kept as net context. Tests 275→282, build green; committed `2bb65d6c`, NOT merged. **One thing for you:** the *win-rule* economy term (`WeightedComponentMatchScorer.capture_income` → `TimeOrSrCaptureWinRule`) still reads net `Earned`; I deliberately did **not** repoint it at gross because that would silently redefine S2/S3 outcomes — your call whether to move it + re-baseline S2/S3 (recorded in LADDER §S1 follow-up 1a). Mirror + Normal-vs-Normal calibration now unblocked (next). Finding: `runs/260719_s1_gross_metric_verified.md`. → (mgr 2026-07-20 `cd06358`) **WIN-RULE TERM MOVED TO GROSS (your approval, LADDER 1a) + S1 MIRROR BUILT.** Repointed `WeightedComponentMatchScorer.capture_income` (feeds `TimeOrSrCaptureWinRule`) from net `Earned` → the gross integral `state.GrossCaptureIncomeFor` (same value as `capture_income_gross`); `verdict_version` 3→4 (emitted `score_components.capture_income` changed *meaning*; no field added/removed). Safe to do now: S2/S3 have no baselines to invalidate. Built `tournament-s1-eco-river-zeta-mirror` (byte-identical to primary, two bots' spawns swapped). NUnit 282→287 (+5 `WeightedComponentScoringTest`), build green; 2 hidden N=1 smokes (mirror + primary) both boot + run full 7500t, verdict_version 4, `capture_income == gross×2` confirmed (gross 0 both draws — the known in-window capture variance, not a wiring issue). Committed `cd06358a`, NOT merged. Next: S1 N=10 baseline + Normal-vs-Normal calibration across primary+mirror.

---

## 3. Activity Log  (manager → user)

Reverse-chronological, one line each: `YYYY-MM-DD | <sha7> | CATEGORY | one-liner`.
Categories: `AI` `ENGINE` `HARNESS` `MERGE` `LADDER` `REVERT` `NOTE`.
New entries go **above** the review cursor. **You move the cursor** up when
you've read the new lines (the manager never moves it).

```
2026-07-20 | cd06358 | HARNESS | PART B — built tournament-s1-eco-river-zeta-mirror (S1 bias twin, SPEC 9.4). Byte-identical copy of tournament-s1-eco-river-zeta with ONLY the two bots' spawn assignments swapped in map.yaml Players (USA/14,45 america -> Bot: normal; Russia/80,35 russia -> Bot: experimental); terrain/spawns/factions/12 OILB derricks/tournament-eco-5min.yaml all git-verified byte-identical (map.bin + shadows.bin cmp-identical). This is a BOT-assignment swap, not the faction swap the S2/S3 combat-stub mirror uses — S1's bias is derrick DISTANCE per spawn, so each bot must play each spawn once; averaging primary+mirror cancels spawn-side derrick-distance luck. NON-OBVIOUS: tournament.yaml's Matchup{P1Bot,P2Bot} is informational only (loaded into config.Matchup, never read by the engine) — real assignment is the Bot: key on each map.yaml PlayerReference (DISCOVERIES 2026-07-20). Hidden N=1 smoke: boots + full 7500t, verdict_version 4, no crash. Cycle card: runs/260720_0117__tournament-s1-eco-river-zeta-mirror__cd06358.json.
2026-07-20 | cd06358 | HARNESS | PART A — repointed the SCORER economy term net -> gross (LADDER S1 follow-up 1a, user-approved). WeightedComponentMatchScorer.capture_income (which feeds TimeOrSrCaptureWinRule, i.e. match OUTCOMES) now reads the GROSS integral state.GrossCaptureIncomeFor (the same per-player value emitted as capture_income_gross) instead of net PlayerResources.Earned. Net Earned is blind to a held derrick whose gross income doesn't overcome upkeep in the SR-budget economy, so outcomes ignored captured income (same defect the S1 metric fixed at v3); the win rule now counts it. Emitted JSON fields UNCHANGED (capture_income_gross + resources_earned both stay); verdict_version bumped 3->4 to flag the changed MEANING of the emitted score_components.capture_income (bump on emitted-field-meaning change, not only add/remove). Done NOW because S2/S3 have no recorded baselines -> redefinition is free. Weighting math factored to pure WeightedComponentScoring.Compute + WeightedComponentScoringTest (5 cases, pins capture_income == gross x weight); NUnit 282->287, build green. 2 hidden N=1 smokes (primary + mirror) confirm capture_income == gross*2 in every player-case (gross 0 both draws — known in-window capture variance, orthogonal to this change; nonzero path pinned by the unit test). Committed cd06358a, NOT merged. Cycle card: runs/260720_0120__tournament-s1-eco-river-zeta__cd06358.json.
2026-07-20 | (rename) | NOTE | Terminology retire: v2 -> Experimental across all LIVE code/config/scenarios; added a frozen Stable bot. ai.yaml: ModularBot@v2->@experimental (name "Experimental AI"), enable-ai-v2->enable-ai-experimental, all module @v2 suffixes renamed; NEW ModularBot@stable ("Stable AI") + enable-ai-stable + byte-for-byte @stable copies of the 10 Experimental-gated modules (frozen snapshot; promotion policy in SPEC §13). Engine log markers [v2-*]->[exp-*] (capture/garrison/offense/transport/poi/layered-defence) + C# doc comments. Scenario dirs renamed tournament-v2-vs-normal-{,mirror-}2p / test-v2-poi-* / demo-v2-capture-coordinator -> *-experimental-*; every `Bot: v2`/`P1Bot: v2` -> experimental. Control AIs Normal/Rush/Turtle UNTOUCHED. Historical runs/ names left as written. verdict schema versions (BotVsBotMatchWatcher v2/v3) are unrelated and unchanged.
2026-07-19 | (n/a)   | NOTE | AUTOBURN STARTED — 8h grant from the user; THIS manager now runs the loop directly (supersedes the pending root-spawned dedicated manager; recommendation faabb3ec stays parked). Standing assumptions posted as AUQ cards (user overrides any time): hidden Mode-B batches fully authorized under the grant; baseline-before-behaviors cycle order; win-rule capture_income repointed to gross while S2/S3 are still unmeasured. Plan: (1) land in-flight v2->Experimental+Stable rename worker, regular-merge ai-bench<->main (branches diverged: realism doc f16db81e on main); (2) repoint win rule to gross; (3) S1 N=10 baseline + mirror scenario + Normal-vs-Normal calibration (SPEC 9.4); (4) behavior cycle 1 = enemy-SR contestation scoring (realism dossier #1), then dispersion cohesion (#2). Realism north star doc landed: DOCS/design/ai-realism.md (9 doctrine patterns ranked).
2026-07-19 | 2bb65d6 | HARNESS | S1 METRIC BLOCKER RESOLVED (harness-side, observer-only, no AI change, no re-roll). Added GrossIncomeIntegrator + new verdict stats field capture_income_gross (verdict_version 2->3): a read-only per-player accumulator that integrates PlayerResources.TotalBuildingIncome (gross building income, pre-upkeep) each tick in BotVsBotMatchWatcher. Side-effect-free — it only READS TotalBuildingIncome and writes to the watcher's own state dict, never mutating actor/player/trait/resource state; robust to mid-match ownership changes because CashTrickler re-registers under the new owner on capture, so the integral follows current ownership. ADDITIVE: every prior verdict field byte-compatible, resources_earned kept as net context, and NO scorer/win-rule input changed (capture_income score component still reads Earned -> S2/S3 outcomes untouched). One hidden Mode-B smoke: v2 capture_income_gross=6093, control=0 (matches ~$5,900 predicted from a $50/50-tick derrick held ~t1550->7500); resources_earned still 0/0 as documented. v2 also won this draw on combat (8650 vs 600) — independent RNG, NOT an effect of the observer change. Added GrossIncomeIntegratorTest (7 cases); unit suite 275->282, build green. LADDER S1 metric repointed to capture_income_gross + follow-up note that the win-rule economy term (still net Earned) deserves loop-manager review before moving to gross (would redefine S2/S3). Committed on ai-bench (2bb65d6c), NOT merged. Cycle card: runs/260719_2017__tournament-s1-eco-river-zeta__2bb65d6.json; finding: runs/260719_s1_gross_metric_verified.md.
2026-07-19 | 2d5433a | AI | FIRST AI-SIDE CHANGE of the benchmark era. Applied the recommended S1 fix: delisted the $0 logisticscenter from v2's two income tables (PoiMap.IncomeWeights world.yaml + CaptureCoordinatorBotModule@v2.tecn ai.yaml) + PITFALL comments; control/legacy configs, unit stats, LC traits, map, engine untouched. Build green, NUnit 275/275. Hidden N=1 smoke CONFIRMS THE FIX WORKS: v2's top capture target flipped logisticscenter@31,52 (score 104M) -> oilb@17,44 (score 45M, targets 15->13), v2 issued the capture (t1351), and CAPTURED + still OWNS the nearest derrick at match end (buildings_killed/dead=0 -> not destroyed; USA assets_value 1450 = army 1250 + 200 building; garrison held 0->1). BUT resources_earned is STILL 0 for a NEW, metric-side reason: it reports net PlayerResources.Earned, which in the SR-budget economy only rises on a net-positive periodic tick (PassiveIncome+TotalBuildingIncome-Upkeep, PlayerResources.cs:199-211) and never via the unused harvester path -> a lone $50 derrick nets 0 Earned (both bots read 0 before AND after the capture). This is a HARNESS/METRIC defect, not a v2 gap (CashTrickler re-registers on capture, bots are Playable -> both ruled out). Fix recorded as a bug against the approved POI plan (income POIs are cash-valued; a $0 depot outranking derricks violated it). NEXT CYCLE = metric fix (gross capture-income accumulator in BotVsBotMatchWatcher), no re-roll. Committed on ai-bench (2d5433aa), NOT merged. Cycle card: runs/260719_1934__tournament-s1-eco-river-zeta__2d5433a.json; finding: runs/260719_s1_earned_metric_finding.md.
2026-07-19 | 0eb71d49 | NOTE | S1 no-capture ROOT-CAUSED (static + surviving debug log; AI-code-side, no code changed). v2 DID build a TECN and DID issue a capture — but PoiMap ranked a Logistics Center @31,52 (value 200, score 104M) above every OILB, so the sole TECN was committed cross-map to it (t762) and died ~t1737 without ever taking the derrick 3 cells from its SR. The LC has NO CashTrickler (structures.yaml:345-403) → capturing it earns $0, yet PoiMap.IncomeWeights (world.yaml:303) + CaptureCoordinator@v2 (ai.yaml:126) weight it logisticscenter:200 (highest of all). resources_earned=0 by MIS-SCORING, not map/reachability/dead-pipeline — the earlier "capture layer didn't fire" note is corrected. Recommended one-line AI fix (loop's first cycle): delist logisticscenter from both IncomeWeights tables (a no-income depot must not be an income POI; PoiMap.cs:91-92 contract). Diagnosis: runs/260719_s1_no_capture_diagnosis.md. No harness/map/AI change this cycle.
2026-07-19 | 86aa2db | HARNESS | S1 rescope: new scenario tournament-s1-eco-river-zeta (River Zeta terrain 98x82 + all 12 neutral OILB derricks + 2 SR/spawn overlay; SRs at 14,45 v2 / 80,35 normal, each ~3-4 cells from a derrick). Fixes the diagnosis root cause — the old tournament-v2-vs-normal-2p stub had 0 capturable POIs so resources_earned was 0/0 by construction. Hidden N=1 smoke GREEN: map boots + runs full 7500t, no crash. BUT resources_earned still 0/0 for a NEW reason — neither bot captured a derrick in 5 game-min (score curve pure combat, capture_income 0 throughout). Normal=$0 is correct (no capture logic); v2=$0 is the live finding (capture layer didn't fire in-window). No clock bump (300s isn't the blocker). S1 is now a live/discriminating test. LADDER S1 repointed + false "runs on River Zeta" premise fixed. Cycle card: runs/260719_1844__tournament-s1-eco-river-zeta__86aa2db.json. Committed on ai-bench, NOT merged to main. Follow-ups: first AI cycle = make v2 capture; build S1 mirror + Normal-vs-Normal calibration.
2026-07-19 | 6d7c561 | HARNESS | Bootstrap smoke run GREEN in Mode B (hidden, OPENRA_WINDOW_HIDDEN=1) from the ai-bench worktree: 1 verdict/1, USA-bot(v2) beat Russia-bot(normal) on time_limit @7500t (~58s wall, ~5x). All 5 Windows-port items resolved — (a) clean single Windows-path passthrough (no /c/ double-mangle), (b) ResultPath/TournamentConfig read+written at cygpath paths, (c) image=dotnet.exe so the CIM kill-filter holds, (d) watchdog kill NOT exercised (match completed normally), (e) settings.yaml round-trips byte-identical (mute didn't leak). S1 baseline data point #1: resources_earned 0/0 (v2/normal), capture_income 0/0 — neither AI captured income in 5 min, so the S1 metric is null-vs-null on this match; flagged for scenario-design review. Cycle card: runs/260719_1816__tournament-v2-vs-normal-2p__6d7c561.json.
2026-07-19 | (pending) | HARNESS | Harness is Windows-native as of 4dec6a74 (cygpath arg conversion, PowerShell CIM process-kill, %APPDATA%\OpenRA settings resolution, CRLF hardening). SPEC §3 updated; 5 portability items flagged for confirmation in the §3.3 bootstrap smoke run.
2026-07-19 | (pending) | HARNESS | Amendment: hidden-window substrate RESOLVED. OPENRA_WINDOW_HIDDEN=1 landed+verified (d716eade/fda8370c) -> Mode B active from bootstrap (unlimited unsupervised runs); Mode A now fallback-only. Replaced the impossible same-seed identity gate (per-seed replay is broken: bots use unseeded LocalRandom) with the structural sim/render-decoupling guarantee; seeds are run labels, N-run stats unaffected. Updated SPEC §3 + LADDER seeds wording.
2026-07-19 | 06afb643 | NOTE | System bootstrapped: SPEC/LADDER/REVIEW/README authored under WORKSPACE/ai-bench/. Loop not yet started.
```

`--- ▲ reviewed through here ▲  (user: drag this line up as you read) ---`

---

## 4. Ladder Status  (live standing)

Per-scenario best result on `main`. Definitions in [`LADDER.md`](LADDER.md).
`—` = not yet measured.

| Scenario | Metric | v2 median | Normal median | Bar | Verdict | Last sha |
|---|---|---|---|---|---|---|
| S1 Economy Race (5 min) | `capture_income_gross` | 6093 (N=1, build `2bb65d6`) | 0 (N=1) | ≥ control ×1.15 | metric LIVE; scorer/win-rule economy term now also reads gross (v4, follow-up 1a); **mirror built**. Two N=1 smokes on `cd06358` drew gross 0/0 (in-window capture variance). Still needs N=10 baseline + Normal-vs-Normal calibration across primary+mirror before a real pass. | `cd06358` (branch) |
| S2 Force Efficiency (12 min) | `kills_cost − deaths_cost` | — | — | ≥ control + 1 unit-cost | not measured | — |
| S3 Win-rate (12 min) | v2 win fraction | — | — | ≥ 0.55 (map ≈0.50) | not measured | — |
| **Composite gate** | all three, one commit | — | — | all pass together | **not cleared** | — |

---

## 5. Open Questions / Blockers

- _(resolved)_ ~~Hidden-window flag / run-window~~ — `OPENRA_WINDOW_HIDDEN=1` is
  landed + verified; Mode B is active, so no user run window is required and there
  is no unsupervised-eligibility gate left to pass (SPEC §3).
- _(resolved `2d5433a`)_ ~~S1 blocked on v2 capture SCORING~~ — the `logisticscenter:
  200` mis-scoring is **fixed** (delisted from both v2 income tables) and the smoke
  **confirms v2 now captures + owns the nearest OILB**. The capture-layer question is closed.
- _(resolved `2bb65d6`)_ ~~S1 blocked on the METRIC~~ — added `GrossIncomeIntegrator` +
  `capture_income_gross` (verdict_version 3, read-only/additive) to `BotVsBotMatchWatcher`;
  hidden smoke reads **v2 6093 / control 0**. The yardstick can now see held-derrick income.
  `resources_earned` (net) kept for context. Finding: `runs/260719_s1_gross_metric_verified.md`.
- _(resolved `cd06358`, user-approved)_ ~~WIN-RULE economy term reads net `Earned`~~ — the
  scorer's `capture_income` component (feeding `TimeOrSrCaptureWinRule`) now reads the **gross**
  integral (`state.GrossCaptureIncomeFor`); `verdict_version` 3→4. Done while S2/S3 have no
  baselines to invalidate (LADDER §S1 follow-up 1a). `WeightedComponentScoringTest` pins it.
- **S1 baseline still pending:** current S1 numbers are N=1 smoke only. The mirror
  (`tournament-s1-eco-river-zeta-mirror`) is now **built**; remaining before an S1 pass is
  trusted: re-baseline at N=10 and run the Normal-vs-Normal calibration batch across
  primary+mirror so a gross-income gap is attributable to AI skill, not spawn-side derrick
  luck (SPEC §9.4).
- **Per-seed replay (backlogged, non-blocking):** bots draw from an unseeded
  `LocalRandom`, so seeds don't replay identical games (SPEC §3.2). This does
  **not** affect the ladder (N-run stats only need independent samples), but
  single-match outlier reproduction returns only once the seeding fix lands.

---

## Resolved directives  (archive — manager moves checked Inbox items here)

_(empty)_
