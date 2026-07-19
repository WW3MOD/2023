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
| **Run mode** | **Mode A (windowed / supervised)** — batch runs only in user-declared windows. Mode B (hidden/unsupervised) locked until the switch criterion passes (SPEC §3.1) |
| **Last cycle** | — (none yet; system just bootstrapped) |
| **Headline** | System spec authored; loop not yet started. First action: confirm run window + create the worktree. |

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
2026-07-19 | 06afb643 | NOTE | System bootstrapped: SPEC/LADDER/REVIEW/README authored under WORKSPACE/ai-bench/. Loop not yet started; Mode A pending a user run window.
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

- **Run window (Mode A):** the system needs a user-declared window to run its
  first batches (windowed runs steal focus on Windows, SPEC §3). When can it run?
- **Hidden-window flag (Mode B gate):** `OPENRA_WINDOW_HIDDEN` is being built in
  parallel (separate work). The moment it lands, the manager's first job is the
  switch-criterion test (SPEC §3.1: same-seed hidden-vs-windowed verdict
  identity). Until it passes, all runs are Mode A.

---

## Resolved directives  (archive — manager moves checked Inbox items here)

_(empty)_
