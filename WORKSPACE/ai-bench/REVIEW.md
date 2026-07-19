# AI Benchmark — Review Board

**This is the one document to check.** Designed for a **one-minute review**:
skim *At a Glance*, drop any directives in *Your Inbox*, glance at what's new
above the *review cursor* in the *Activity Log*. Protocol defined in
[`SPEC.md`](SPEC.md) §7. The manager reads this file at the start of **every**
cycle and treats the Inbox as outranking its own backlog.

---

## 1. At a Glance

| | |
|---|---|
| **System** | AI Benchmark — autonomous improvement of the Experimental AI (`ModularBot@v2`) vs the Normal control |
| **Active rung** | River Zeta WW3 (Scenarios 1–3, [`LADDER.md`](LADDER.md)) — **not yet cleared** |
| **Run mode** | **Mode B (hidden / unsupervised) — ACTIVE.** `OPENRA_WINDOW_HIDDEN=1` verified (no window, no focus steal, verdict written, sim/render decoupled). Unlimited unattended runs. Mode A (windowed) is the fallback only if the decoupling regresses (SPEC §3.1) |
| **Last cycle** | 2026-07-19 · `0eb71d49` (branch) · S1 no-capture ROOT-CAUSED (static + debug log) — AI-code-side, no code changed |
| **Headline** | S1's 0/0 is now **root-caused**: v2 *does* build a TECN and *does* issue a capture — but `PoiMap.IncomeWeights` weights `logisticscenter: 200` (highest of all) while the Logistics Center has **no CashTrickler** ($0 income). So v2 sends its only TECN cross-map to a no-income depot over the OILB derrick 3 cells from its SR; the TECN dies en route and the 12 real derricks are never touched → `resources_earned` 0 by **mis-scoring**, not map/reachability. Fix is **AI-side** (delist `logisticscenter` from the income tables) → recorded for the loop's first cycle, **no AI code changed** this pass. Diagnosis: `runs/260719_s1_no_capture_diagnosis.md`. |

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

- [ ] (mgr-flagged 2026-07-19 `86aa2db`) S1 economy metric is `0/0` again after the map rescope — but now because **neither bot captures a derrick in 5 game-minutes**, not because the map lacks POIs (that wall is fixed). Normal earning $0 is correct (no capture logic). v2 earning $0 is the real gap: its `PoiMap → CaptureCoordinatorBotModule` layer didn't convert reachable POIs into a capture in-window. → (mgr) Recommend the **first AI cycle** target exactly this (compare v2 capture on `tournament-capture-arena-2p` vs here to localize the gap); I did **not** bump the 300s clock (nearest derrick is ~3 cells away, so the clock isn't the blocker). Flagging in case you'd rather I first bump S1 to 420–600s or add a capture-forcing setup before touching v2. Also pending: build the S1 mirror + a Normal-vs-Normal calibration batch before any v2 number is trusted (SPEC §9.4). → (mgr 2026-07-19 `0eb71d49`) **ROOT-CAUSED (static + debug log), AI-code-side.** The prior "capture layer didn't fire" framing was wrong: v2 *did* build a TECN and *did* issue a capture — but to a **Logistics Center @31,52 (score 104M), not the derrick 3 cells from its SR**. `PoiMap.IncomeWeights` (`world.yaml:303`) lists `logisticscenter: 200` — the **highest** weight — yet the LC has **no CashTrickler** (`structures.yaml:345-403`), so capturing it earns **$0**. The sole TECN is goal-guard-committed cross-map to the LC, dies ~t1737, and the 12 real OILB derricks are never touched → `resources_earned` 0 by *mis-scoring*, not map/reachability. **One-line fix (loop's first AI cycle): delist `logisticscenter` from `PoiMap.IncomeWeights` (`world.yaml:303`) + `CaptureCoordinatorBotModule@v2.tecn.IncomeWeights` (`ai.yaml:126`)** — a no-income depot must not be an income POI (PoiMap.cs:91-92 contract). I did NOT change AI code per the boundary. Full evidence: `runs/260719_s1_no_capture_diagnosis.md`.

---

## 3. Activity Log  (manager → user)

Reverse-chronological, one line each: `YYYY-MM-DD | <sha7> | CATEGORY | one-liner`.
Categories: `AI` `ENGINE` `HARNESS` `MERGE` `LADDER` `REVERT` `NOTE`.
New entries go **above** the review cursor. **You move the cursor** up when
you've read the new lines (the manager never moves it).

```
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
| S1 Economy Race (5 min) | `resources_earned` | 0 (N=1 smoke) | 0 (N=1 smoke) | ≥ control ×1.15 | blocked — v2 mis-scores capture: sends sole TECN to a $0-income Logistics Center (weight 200) over the OILB derricks. AI fix pending (delist `logisticscenter` from `IncomeWeights`); see diagnosis. | `86aa2db` (branch) |
| S2 Force Efficiency (12 min) | `kills_cost − deaths_cost` | — | — | ≥ control + 1 unit-cost | not measured | — |
| S3 Win-rate (12 min) | v2 win fraction | — | — | ≥ 0.55 (map ≈0.50) | not measured | — |
| **Composite gate** | all three, one commit | — | — | all pass together | **not cleared** | — |

---

## 5. Open Questions / Blockers

- _(resolved)_ ~~Hidden-window flag / run-window~~ — `OPENRA_WINDOW_HIDDEN=1` is
  landed + verified; Mode B is active, so no user run window is required and there
  is no unsupervised-eligibility gate left to pass (SPEC §3).
- **S1 blocked on v2 capture SCORING (root-caused `0eb71d49`, AI-code-side):** v2
  builds a TECN and issues a capture, but `PoiMap.IncomeWeights` (`world.yaml:303`)
  weights `logisticscenter: 200` — the highest — despite the LC having no
  `CashTrickler` (earns $0). The sole TECN is committed cross-map to the LC over the
  OILB 3 cells away and dies en route; the 12 derricks are never captured.
  **Fix (loop's first AI cycle): delist `logisticscenter` from `PoiMap.IncomeWeights`
  + `CaptureCoordinatorBotModule@v2.tecn.IncomeWeights` (`ai.yaml:126`).** No AI code
  changed this pass (per diagnosis boundary). Still needs an S1 mirror twin + a
  Normal-vs-Normal calibration batch before any v2 S1 number is trusted (SPEC §9.4).
  See `runs/260719_s1_no_capture_diagnosis.md`.
- **Per-seed replay (backlogged, non-blocking):** bots draw from an unseeded
  `LocalRandom`, so seeds don't replay identical games (SPEC §3.2). This does
  **not** affect the ladder (N-run stats only need independent samples), but
  single-match outlier reproduction returns only once the seeding fix lands.

---

## Resolved directives  (archive — manager moves checked Inbox items here)

_(empty)_
