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
| **Last cycle** | 2026-07-19 16:17Z · `6d7c561` · bootstrap smoke (Mode B) — GREEN |
| **Headline** | Substrate live-verified end-to-end on Windows: worktree built, hidden Mode B smoke match wrote a verdict, all 5 Windows-port items resolved. Open finding for the first hypothesis cycle: S1 `resources_earned` came back 0/0 (no income captured in 5 min) — the eco metric may need a longer clock / capture-forcing setup / fixed-target rescope before it discriminates. |

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

_(empty — no directives yet)_

---

## 3. Activity Log  (manager → user)

Reverse-chronological, one line each: `YYYY-MM-DD | <sha7> | CATEGORY | one-liner`.
Categories: `AI` `ENGINE` `HARNESS` `MERGE` `LADDER` `REVERT` `NOTE`.
New entries go **above** the review cursor. **You move the cursor** up when
you've read the new lines (the manager never moves it).

```
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
| S1 Economy Race (5 min) | `resources_earned` | — | — | ≥ control ×1.15 | not measured | — |
| S2 Force Efficiency (12 min) | `kills_cost − deaths_cost` | — | — | ≥ control + 1 unit-cost | not measured | — |
| S3 Win-rate (12 min) | v2 win fraction | — | — | ≥ 0.55 (map ≈0.50) | not measured | — |
| **Composite gate** | all three, one commit | — | — | all pass together | **not cleared** | — |

---

## 5. Open Questions / Blockers

- _(resolved)_ ~~Hidden-window flag / run-window~~ — `OPENRA_WINDOW_HIDDEN=1` is
  landed + verified; Mode B is active, so no user run window is required and there
  is no unsupervised-eligibility gate left to pass (SPEC §3).
- **Per-seed replay (backlogged, non-blocking):** bots draw from an unseeded
  `LocalRandom`, so seeds don't replay identical games (SPEC §3.2). This does
  **not** affect the ladder (N-run stats only need independent samples), but
  single-match outlier reproduction returns only once the seeding fix lands.

---

## Resolved directives  (archive — manager moves checked Inbox items here)

_(empty)_
