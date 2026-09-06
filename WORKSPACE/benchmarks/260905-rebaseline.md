# `@stable` benchmark RE-BASELINE — the first valid corpus (2026-09-05/06)

**Pipeline item 43.** Four batches, 40 measured matches, run overnight from the main
checkout `C:/Users/fredr/Desktop/WW3MOD` per
[`../ai-bench/RUNBOOK-260905.md`](../ai-bench/RUNBOOK-260905.md). Recorded per SPEC §5.1
(`WORKSPACE/benchmarks/<YYMMDD>-<name>.md`). Runner log:
`C:/Users/fredr/worktrees/ww3mod/rebaseline-260905.log` (ends `ALL-DONE-B1`).

> **This is the FIRST VALID `@stable` baseline corpus.** Every benchmark number ever
> taken from the `tournament-*` suite before the economy gate fix is **void, not
> stale** — `PlayerResources.Tick` gated income *and* upkeep on `Playable`, which
> map-player bots are not, so every prior match measured a game in which neither side
> could earn or spend anything past its opening 7,500 allocation. The scoring math was
> never broken; the simulation being scored was not a game. History, mechanism and the
> retraction of the "exposure is forward-only" claim:
> [`../pipeline/items/43-benchmark-rebaseline.md`](../pipeline/items/43-benchmark-rebaseline.md).
> There is **no prior number to diff against.** These are the zero.

## Instrument

| | |
|---|---|
| **Stamped SHA** | `9cb423d4` — `batch.meta.json git_sha: 9cb423d457f8b6036073a7e1281030fbae955c62`, `git_dirty: false`, identical in all five `batch.meta.json`. |
| **Code SHA under test** | **`bb89f9fd`.** `git diff --stat bb89f9fd 9cb423d4` is `WORKSPACE/PIPELINE.md \| 2 ++` — one docs-only commit. The compiled tree is `bb89f9fd`; quote that when comparing engine behaviour, and `9cb423d4` when reconciling against `batch.meta.json`. The run **pre-dates `wt/item64-axis`.** |
| Bots | `ModularBot@experimental` vs `ModularBot@stable`; calibration batches are `@stable` v `@stable`. |
| Regime | 2026-07-21: `Faction: america` both sides, `StartingUnitsClass: motorized`, opponent = `@stable`. |
| Scenarios | S1 `tournament-s1-eco-river-zeta` (+`-mirror`), S1 cal `tournament-s1-eco-cal-nn`; S2 `tournament-s2-combat-river-zeta` (+`-mirror`), S2 cal `tournament-s2-combat-river-zeta-cal-nn`. |
| Configs | `tournament-eco-5min.yaml` (7,500 ticks) / `tournament-combat-12min.yaml` (18,000 ticks). |
| Sample | N=10 per batch. Exp batches paired `--mirror` (odd seeds = mirror/swapped slots). Cal batches unmirrored (both bots identical — a swap is a no-op). |
| Seeds | `Test.RandomSeed = i*1000+17`, i.e. 1017…10017, identical across all batches. |
| Profile | hidden (`OPENRA_WINDOW_HIDDEN=1`, unconditional at `run-tournament.sh:296`), sequential, `--max-wall-secs` 300 (S1) / 600 (S2). |
| Verdict version | `8` in all 40 matches. |
| Raw | `tools/autotest/tournament-results/260905_rebaseline_{s1_cal,s1_cal_b,s2_cal,s1_exp,s2_exp}/` (untracked; not in any worktree). |

**Attribution is strictly by `notes.players[].bot_type`**, never by slot or faction —
`--mirror` swaps slots on odd seeds, so slot-attribution silently inverts half the
sample. Ladder metrics per RUNBOOK §6: **S1 = `stats.capture_income_gross`**,
**S2 = `stats.kills_cost − stats.deaths_cost`**.

## Batches

| # | Batch | Matchup | Verdicts | Win split | Wall clock | Status |
|---|---|---|---:|---|---|---|
| 1 | `260905_rebaseline_s1_cal` | Stable v Stable | 10 files | — | 18 m 03 s | **VOID — aborted, dir shared with a second runner. Superseded by 1b.** |
| 1b | `260905_rebaseline_s1_cal_b` | Stable v Stable | 10 / 10 | USA 5 – Russia 5, 0 draws | 17 m 14 s (~1 m 43 s/match) | clean |
| 2 | `260905_rebaseline_s2_cal` | Stable v Stable | 10 / 10 | USA 5 – Russia 5, 0 draws | 58 m 02 s (~5 m 48 s/match) | clean, see caveat |
| 3 | `260905_rebaseline_s1_exp` | Exp v Stable, mirrored | 10 / 10 | **Exp 3 – Stable 7**, 0 draws | 18 m 08 s (~1 m 49 s/match) | clean |
| 4 | `260905_rebaseline_s2_exp` | Exp v Stable, mirrored | 10 / 10 | **Exp 2 – Stable 8**, 0 draws | 60 m 07 s (~6 m 01 s/match) | clean |

Wall clock measured from `batch.meta.json started_at` to the `summary.csv` mtime.
Batch order as executed: 1 (aborted) → 2 → 3 → 4 → 1b. Total wall 21:41:30 → 00:23:26
local = **2 h 41 m 56 s**; the four valid batches account for 2 h 33 m 31 s. RUNBOOK §8
predicted 65–80 min — the **S2 batches ran ~3× the predicted per-match cost** (~6 min
against ~2 min). The S1 batches matched prediction. Nothing was culled: the longest
match observed was 393 s against a 600 s cap.

All 40 measured matches ran the **full clock** (`duration_ticks` = 7,500 / 18,000,
`win_reason = time_limit` in every one). **0 no-verdicts, 0 crashes, 0 draws, 0 SR
captures.**

## Batch 1 aborted, and why 1b replaces it rather than repairing it

The batch-1 runner died at match 5 with

```
mv: cannot stat 'tools/autotest/tournament-results/260905_rebaseline_s1_cal/.settings.yaml.bak': No such file or directory
BATCH1-EXIT 1
```

An **orphaned earlier launcher was already running the same scenario into the same
result dir.** The settings backup is per-**dir**, not per-process: `run-tournament.sh:276`
sets `SETTINGS_BACKUP="${RESULT_DIR}/.settings.yaml.bak"` and `:277` copies into it;
`:347-348` guard on `[ -f "${SETTINGS_BACKUP}" ]` and then `mv` it back. Two runners
sharing a dir share that one path, so the orphan's `mv` at `:348` consumed the file
between our `[ -f ]` check and our `mv` — a TOCTOU race. `set -e` (`:44`) turned the
failed `mv` into an immediate batch exit.

**The dir is void because it cannot be attributed, not because the numbers are wrong.**
It ends up holding ten `match_*.json` and a `summary.csv` — it *looks* complete — but
our runner wrote only matches 1–5 (mtimes 21:42:11–21:49:40) and the orphan wrote 6–10
(21:51:35–21:59:28) and the aggregate (21:59:33), with overlapping match indices
throughout. Physical trace of the second process: **`match_3_debug.log` is 0 bytes** in
`s1_cal` and 1,153,677 bytes in `s1_cal_b` — the shared `%APPDATA%/OpenRA/Logs/debug.log`
was truncated by the other game at the instant `run-tournament.sh` copied it.

The rerun `s1_cal_b` is the batch of record. Its `summary.csv` is **byte-identical** to
`s1_cal`'s, all ten `match_*.json` are equal field-for-field once `timestamp` is
dropped, and nine of ten `match_*_debug.log` are byte-identical (the tenth is the
0-byte one). **The engine is deterministic per seed on this build**, so the
contamination cost attribution and wall-clock, not outcomes.

**Caveat inherited by batch 2 (`s2_cal`): its first two matches overlapped the orphan.**
`s2_cal` started 21:49:42 while the orphan ran until 21:59:33, so `s2_cal` matches 1
(ended 21:56:15) and 2 (ended 22:02:11) shared the CPU with a second game. Match 1 is
the longest in the batch at 6 m 33 s — consistent with contention — but match 9, which
was uncontended, is 6 m 17 s, so wall-clock alone does not isolate the effect. **The
verdicts are unaffected**: the sim is deterministic per seed, no wall cap was
approached, and all ten ran the full 18,000 ticks. Batches 3 and 4 ran alone.

## Calibration — side bias and noise band

### S1 cal (`s1_cal_b`), Stable v Stable, unmirrored

| # | Seed | Winner | USA cap-gross | RUS cap-gross | USA swing | RUS swing | Swing Δ (USA−RUS) |
|---|------|--------|--------------:|--------------:|----------:|----------:|------:|
| 1 | 1017 | Russia | 26,462 | 26,491 | −6,800 | −10,000 | +3,200 |
| 2 | 2017 | USA | 27,512 | 25,265 | −6,750 | −4,650 | −2,100 |
| 3 | 3017 | Russia | 26,500 | 25,600 | −7,550 | +950 | −8,500 |
| 4 | 4017 | USA | 28,810 | 26,162 | −1,500 | −10,000 | +8,500 |
| 5 | 5017 | Russia | 22,972 | 27,935 | −8,950 | −5,300 | −3,650 |
| 6 | 6017 | USA | 26,747 | 27,904 | −6,900 | −8,900 | +2,000 |
| 7 | 7017 | Russia | 25,542 | 28,208 | −8,600 | −7,300 | −1,300 |
| 8 | 8017 | Russia | 27,015 | 28,912 | −3,400 | −6,400 | +3,000 |
| 9 | 9017 | USA | 30,194 | 27,221 | +400 | −6,700 | +7,100 |
| 10 | 10017 | USA | 21,671 | 21,840 | −4,400 | −3,700 | −700 |

**Win 5–5. Capture-gross median USA 26,623 / Russia 26,856; USA leads in 4/10. Swing Δ
median +650, mean +755, 5/10 positive.** S1 is side-fair on this map to within the
sample: no bias worth correcting for, and the **noise band on the swing is roughly
±$8,500** (the observed extremes are −8,500 and +8,500).

### S2 cal (`s2_cal`), Stable v Stable, unmirrored

| # | Seed | Winner | USA cap-gross | RUS cap-gross | USA swing | RUS swing | Swing Δ (USA−RUS) |
|---|------|--------|--------------:|--------------:|----------:|----------:|------:|
| 1 | 1017 | USA | 89,462 | 89,491 | −6,450 | −52,750 | +46,300 |
| 2 | 2017 | Russia | 88,436 | 90,341 | −33,050 | −16,000 | −17,050 |
| 3 | 3017 | Russia | 81,446 | 96,654 | −18,200 | −25,350 | +7,150 |
| 4 | 4017 | USA | 89,162 | 91,810 | −18,400 | −36,600 | +18,200 |
| 5 | 5017 | Russia | 76,795 | 100,112 | −33,500 | −21,600 | −11,900 |
| 6 | 6017 | Russia | 85,869 | 94,782 | −25,500 | −27,100 | +1,600 |
| 7 | 7017 | Russia | 78,042 | 101,708 | −11,500 | −49,800 | +38,300 |
| 8 | 8017 | USA | 89,639 | 91,912 | −8,500 | −35,900 | +27,400 |
| 9 | 9017 | USA | 93,194 | 90,221 | −28,850 | −23,050 | −5,800 |
| 10 | 10017 | USA | 84,671 | 82,909 | −22,350 | −14,350 | −8,000 |

**Win 5–5. Capture-gross median USA 87,152 / Russia 91,861; USA leads in only 2/10.
Swing Δ median +4,375, mean +9,620, 6/10 positive.** Two things to carry forward:

1. **S2 carries a real spawn asymmetry in capture income** — the Russia spawn out-earns
   the USA spawn in 8 of 10 identical-bot games, median gap ≈ 4,700. The win split is
   nevertheless 5–5, so score is not decided by capture alone on this rung. **The
   mirror cancels this in batch 4; any unmirrored S2 comparison must not.**
2. **The S2 noise band on the swing is very wide** — ±$46,300 observed between two
   copies of the same bot. Any Exp-v-Stable swing edge below ~$40k on S2 is inside the
   noise floor of this instrument at N=10.

**Engagement validity: 10/10 on both rungs** — every match has non-zero `kills_cost`
and `deaths_cost` on both sides, so no match is a no-contact void.

## Baseline — Experimental v Stable

### S1 eco (`s1_exp`), mirrored

| # | Seed | Exp slot | Winner (bot) | Exp cap-gross | Sta cap-gross | Cap Δ | Exp swing | Sta swing | Swing Δ |
|---|------|----------|--------------|--------------:|--------------:|------:|----------:|----------:|--------:|
| 1 | 1017 | Russia | **Exp** | 27,033 | 26,301 | +732 | −1,950 | −6,400 | +4,450 |
| 2 | 2017 | USA | Stable | 24,256 | 27,813 | −3,557 | −2,850 | −6,650 | +3,800 |
| 3 | 3017 | Russia | Stable | 26,681 | 27,485 | −804 | −750 | −2,800 | +2,050 |
| 4 | 4017 | USA | Stable | 23,654 | 29,031 | −5,377 | −15,650 | +2,200 | −17,850 |
| 5 | 5017 | Russia | **Exp** | 26,481 | 27,479 | −998 | −2,450 | −5,450 | +3,000 |
| 6 | 6017 | USA | Stable | 25,795 | 28,982 | −3,187 | −5,300 | −4,450 | −850 |
| 7 | 7017 | Russia | Stable | 27,448 | 25,977 | +1,471 | −13,600 | +450 | −14,050 |
| 8 | 8017 | USA | Stable | 27,615 | 30,211 | −2,596 | −3,950 | −8,750 | +4,800 |
| 9 | 9017 | Russia | **Exp** | 29,429 | 25,127 | +4,302 | −8,600 | +4,050 | −12,650 |
| 10 | 10017 | USA | Stable | 26,698 | 26,715 | −17 | −6,350 | −5,150 | −1,200 |

**Exp win rate 3/10.** Capture-gross median Exp 26,689 / Stable 27,482; **Exp leads
capture in only 3/10**. Swing Δ median +600, mean −2,850, 5/10 positive.

Win sequence (m1→m10): `W L L L W L L L W L`. Exp's three wins are all in the **Russia
slot** (seeds 1017, 5017, 9017 — odd = mirror), and Exp lost every USA-slot game. Note
the calibration says S1 is side-fair 5–5 for identical bots, so **this is not spawn
bias; it is Exp underperforming specifically from the USA spawn** — or, equally
consistent with the data, Exp winning only the three seeds it happens to win. At N=10
with 5 games per slot the two readings are not separable.

### S2 combat (`s2_exp`), mirrored

| # | Seed | Exp slot | Winner (bot) | Exp cap-gross | Sta cap-gross | Cap Δ | Exp swing | Sta swing | Swing Δ |
|---|------|----------|--------------|--------------:|--------------:|------:|----------:|----------:|--------:|
| 1 | 1017 | Russia | **Exp** | 93,820 | 85,514 | +8,306 | −29,850 | −24,100 | −5,750 |
| 2 | 2017 | USA | Stable | 76,756 | 90,813 | −14,057 | −19,800 | −40,850 | +21,050 |
| 3 | 3017 | Russia | Stable | 81,034 | 98,822 | −17,788 | −14,500 | −31,850 | +17,350 |
| 4 | 4017 | USA | Stable | 73,539 | 102,378 | −28,839 | −20,550 | −46,100 | +25,550 |
| 5 | 5017 | Russia | Stable | 89,481 | 90,479 | −998 | −44,350 | −20,150 | −24,200 |
| 6 | 6017 | USA | Stable | 75,208 | 105,569 | −30,361 | −11,050 | −45,550 | +34,500 |
| 7 | 7017 | Russia | Stable | 86,632 | 91,344 | −4,712 | −53,800 | −9,450 | −44,350 |
| 8 | 8017 | USA | Stable | 87,121 | 96,705 | −9,584 | −13,700 | −33,150 | +19,450 |
| 9 | 9017 | Russia | **Exp** | 102,929 | 77,627 | +25,302 | −28,100 | −25,350 | −2,750 |
| 10 | 10017 | USA | Stable | 80,158 | 99,255 | −19,097 | −32,200 | −21,550 | −10,650 |

**Exp win rate 2/10.** Capture-gross median Exp 83,833 / Stable 94,024; **Exp leads
capture in only 2/10 — and those are exactly the two games it won.** Swing Δ median
+7,300, mean +3,020, 5/10 positive.

**The S2 result is the interesting one, and it does not say what the win rate alone
would suggest.** Exp's *combat* swing is at parity or slightly ahead — median +7,300,
5/10 positive, well inside the ±$46,300 calibration noise band and therefore **a wash,
not a deficit**. What Exp loses is the **capture economy**: it trails Stable's gross
capture income in 8 of 10 games by a median ≈ 10,200, and the two games where it leads
capture are the two it wins. On a `WinRule: score_or_sr_capture` rung where
`capture_income` is the dominant score component, **capture decided every game in this
batch.** Combat is not where the 2/10 comes from.

## Core finding

**On the first valid instrument, `@experimental` is BELOW `@stable` on both rungs —
3/10 on S1 eco and 2/10 on S2 combat — and the mechanism is capture income, not
combat.** Exp trails Stable's gross capture income in 7/10 (S1) and 8/10 (S2) games,
while its combat swing sits inside the calibration noise band on both rungs. Both
calibration batches are side-fair on wins (5–5, 5–5), so the deficit is real and not a
spawn artefact — though **S2 carries a large Russia-spawn capture advantage** (Russia
leads capture 8/10 between identical bots) that only the mirror cancels.

This is directionally the same verdict as the void 2026-07-29 corpus ("Exp BELOW Stable
on both rungs"), but **that agreement is not corroboration** — the old corpus measured
a game with no economy at all, so its agreeing is coincidence until shown otherwise.
Treat this card as the zero and the earlier numbers as absent.

## Watch — what this corpus does not establish

- **`kills_cost − deaths_cost` is not zero-sum here.** Both players are net-negative in
  **35 of 40** matches (75 of 80 player rows; the "38 of 40" first written here was a
  miscount — exceptions are `s1_cal_b` m3/m9 and `s1_exp` m4/m7/m9). **RESOLVED
  2026-09-06** by [`../audits/260906-baseline-deaths-audit.md`](../audits/260906-baseline-deaths-audit.md)
  (main @ f01e00d2): the doom model delivers the killing blow as *self-inflicted*
  damage (`ChangesHealth.cs:86`, and `AutoTarget.cs:244` stops shooting a
  `critical-damage` unit), and `UpdatesPlayerStatistics` charges `DeathsCost` above the
  `Attacker == null || Attacker == self` gate (`PlayerStatistics.cs:335` vs `:341`) but
  credits `KillsCost` below it — so 3,068 of 7,616 deaths (40.3 %, 49.5 % of value)
  are charged to the victim and credited to nobody. The earlier supply-starvation
  hypothesis is refuted (no supply trait damages or kills). **Reading consequences:**
  win rates stand (uncredited share ≈ 41.6 % vs 43.0 % of each bot's own deaths on
  S2), but the S2 *swing* metric is biased ≈ $4,490/match against `@experimental` —
  the same magnitude and direction as the +7,300 median it reported. Absolute levels
  remain not a combat scoreboard.
- **Whether `make.ps1 all` was run at `9cb423d4` before the batches is not recorded.**
  The runner log opens with `START … main=9cb423d4` and no build line. RUNBOOK §2 makes
  the build mandatory precisely because `launch-game.sh` does not build. **HYPOTHESIS:
  the build was taken.** The disproof, if anyone wants it, is to rebuild at `9cb423d4`
  and re-run one seed — the sim is deterministic, so a byte-identical `match_1.json`
  settles it.
- **One map pair per rung.** river-zeta primary + mirror only. Nothing here generalises
  across maps.
- **N=10.** The S2 swing noise band is ±$46,300 between identical bots; most of the
  S2 per-game numbers above are inside it.
- **The 2026-08-14 economy fix is assumed present and was not re-verified in this
  worktree.** The corpus is called valid on the strength of item 43's record, not on a
  fresh reading of `PlayerResources.Tick` at `bb89f9fd`.
- **RUNBOOK §7 names this card `260905-stable-rebaseline.md`.** It was filed as
  `260905-rebaseline.md` on the manager's instruction; there is no second card.

**Ref stamp:** stamped `9cb423d4` (`git_dirty: false`), code `bb89f9fd`, run
2026-09-05 21:41 → 2026-09-06 00:23 local. Recorded from
`C:/Users/fredr/worktrees/ww3mod/bench-record` at `main @ 9cb423d4`.
