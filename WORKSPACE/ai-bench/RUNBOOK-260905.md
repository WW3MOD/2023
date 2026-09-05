# RUNBOOK — `@stable` benchmark re-baseline, unattended (2026-09-05)

**Read against `main @ 95bdffb2`** (`Merge wt/powers-economy: the powers economy proposal`).
Recon-only branch `wt/bench-recon`; nothing here was executed. Every path, flag and
line reference below was verified to exist at that SHA.

This is the **paste-in-order** procedure. The reasoning, the caveats and the things
that are still hypotheses live in the recon report that accompanies it — read that
before deciding *whether* to run. This file is only *how*.

---

## 0. What this run is

Four batches, **40 matches**, strictly sequential, hidden window, ~**65–80 min**
wall-clock at the shipped `SpeedMultiplier: 8`.

| Batch | Scenario | Matchup | Mirror? | Purpose |
|---|---|---|---|---|
| 1 | `tournament-s1-eco-cal-nn` | `@stable` v `@stable` | no | S1 spawn/side bias + noise band |
| 2 | `tournament-s2-combat-river-zeta-cal-nn` | `@stable` v `@stable` | no | S2 side bias + min-engagement validity |
| 3 | `tournament-s1-eco-river-zeta` | `@experimental` v `@stable` | **yes** | S1 economy baseline |
| 4 | `tournament-s2-combat-river-zeta` | `@experimental` v `@stable` | **yes** | S2 combat baseline |

Calibration batches run **first** and are not optional: they are the yardstick the
Exp-vs-Stable numbers are read against (LADDER §S1/§S2, SPEC §9.4). Cal scenarios take
**no `--mirror`** — both bots are identical, so a swap is a no-op; vary seed on one map.

Matchup verified by reading each scenario's `map.yaml` `Bot:` keys, not inferred from
the folder name:

| Scenario | USA-bot slot | Russia-bot slot |
|---|---|---|
| `tournament-s1-eco-river-zeta` | `experimental` (`map.yaml:62`) | `stable` (`:70`) |
| `tournament-s1-eco-river-zeta-mirror` | `stable` (`:61`) | `experimental` (`:69`) |
| `tournament-s2-combat-river-zeta` | `experimental` (`:62`) | `stable` (`:70`) |
| `tournament-s2-combat-river-zeta-mirror` | `stable` (`:60`) | `experimental` (`:68`) |
| `tournament-s1-eco-cal-nn` | `stable` (`:63`) | `stable` (`:71`) |
| `tournament-s2-combat-river-zeta-cal-nn` | `stable` (`:63`) | `stable` (`:71`) |

All six are `Faction: america` on both sides with `StartingUnitsClass: motorized` —
the 2026-07-21 regime. The mirrors are therefore **pure spawn swaps**.

---

## 1. Preconditions

- **A `6.0.4xx` .NET SDK is installed** (CLAUDE.md Build & Run). `global.json` pins
  `6.0.428` and cannot roll across a major version.
- **Nothing else is using the machine's single game slot.** There are 12 live
  worktrees; `run-tournament.sh` kills game processes by matching the result-file
  basename against each process command line (`run-tournament.sh:70-85`), so it will
  not kill a stranger's game — but a stranger's game **will** steal CPU and skew
  wall-clock, and the 2026-08-20 record says the original 150/400 s wall caps culled a
  match under shared-checkout load. Confirm the fleet is quiet before starting.
- **`python3` on PATH** — `aggregate-tournament.sh` and `tournament-report.sh` both
  hard-require it and exit 3 without it. Verified present (3.14.5).
- **The user's multi-test goahead is live for this turn.** CLAUDE.md forbids
  autonomous multi-test runs; the 2026-08-16 audit ruling additionally centralises
  simulation authority in the manager. This is 40 matches — it needs the grant.

## 2. Build first — `launch-game.sh` does NOT build

`run-tournament.sh:296` launches `./launch-game.sh`, which only *checks* for a built
tree and tells you to run `make` if it is missing (`launch-game.sh:42`). A stale
`engine/bin` therefore benchmarks **old code and stamps the new SHA** — the exact
failure mode SPEC §5.1 was rewritten to prevent.

```bash
cd /c/Users/fredr/Desktop/WW3MOD
git rev-parse --short HEAD          # record this; it goes in the result card
git status -sb                      # must be clean
./make.ps1 all                      # MUST succeed before any match runs
```

Run everything below from `C:/Users/fredr/Desktop/WW3MOD` — **the main checkout, not a
worktree.** `run-tournament.sh` derives `REPO_ROOT` from its own script path
(`run-tournament.sh:45-46`), so the batch measures whichever checkout invokes it.

## 3. The window is already hidden — do not add a flag

`run-tournament.sh:296` sets `OPENRA_WINDOW_HIDDEN=1` **unconditionally** on every
launch. The engine consumes it at `Sdl2PlatformWindow.cs:233` and adds
`SDL_WINDOW_HIDDEN`, so the window is never mapped and never takes focus.

**No wrapper change is needed, and `-v|--visible` must not be used.** That flag is
inert: it assigns `RUN_TEST_FLAGS` (`:107`), a variable the script never reads again —
`run-tournament.sh` does not call `run-test.sh` at all, it calls `launch-game.sh`
directly, and says so at `:268-270`. Passing `--visible` changes nothing: it does not
make the run visible, and hidden is unconditional either way.

## 4. `--config` is mandatory

None of the six ladder scenarios contains a `tournament.yaml`, which is the default
`run-tournament.sh` falls back to (`:143`). Only the 12 legacy `tournament-arena-*` /
`-parity-*` / `-capture-*` scenarios have one. Omit `--config` on a ladder scenario and
the script prints `Error: tournament config not found` and **exits 3 before launching
anything** — recorded as deviation #2 against the 2026-07-28 card, whose §3 command
block still omits it. Do not paste that card's commands.

## 5. The commands — paste serially, one batch at a time

Wall caps of **300 / 600** are the values that ran clean on 2026-08-20; they sit
deliberately far above the natural match length so the watchdog never culls a match
early (a culled match breaks the paired model).

```bash
cd /c/Users/fredr/Desktop/WW3MOD

# ---- Batch 1: S1 calibration (Stable-vs-Stable, no mirror) ----
./tools/autotest/run-tournament.sh tournament-s1-eco-cal-nn \
  --config tools/autotest/scenarios/tournament-s1-eco-cal-nn/tournament-eco-5min.yaml \
  --seeds 10 --max-wall-secs 300 \
  --result-dir tools/autotest/tournament-results/260905_rebaseline_s1_cal

# ---- Batch 2: S2 calibration (Stable-vs-Stable, no mirror) ----
./tools/autotest/run-tournament.sh tournament-s2-combat-river-zeta-cal-nn \
  --config tools/autotest/scenarios/tournament-s2-combat-river-zeta-cal-nn/tournament-combat-12min.yaml \
  --seeds 10 --max-wall-secs 600 \
  --result-dir tools/autotest/tournament-results/260905_rebaseline_s2_cal

# ---- Batch 3: S1 baseline (Exp-vs-Stable, mirrored) ----
./tools/autotest/run-tournament.sh tournament-s1-eco-river-zeta \
  --config tools/autotest/scenarios/tournament-s1-eco-river-zeta/tournament-eco-5min.yaml \
  --seeds 10 --mirror tournament-s1-eco-river-zeta-mirror --max-wall-secs 300 \
  --result-dir tools/autotest/tournament-results/260905_rebaseline_s1_exp

# ---- Batch 4: S2 baseline (Exp-vs-Stable, mirrored) ----
./tools/autotest/run-tournament.sh tournament-s2-combat-river-zeta \
  --config tools/autotest/scenarios/tournament-s2-combat-river-zeta/tournament-combat-12min.yaml \
  --seeds 10 --mirror tournament-s2-combat-river-zeta-mirror --max-wall-secs 600 \
  --result-dir tools/autotest/tournament-results/260905_rebaseline_s2_exp
```

`aggregate-tournament.sh` is invoked automatically at the end of each batch whenever at
least one verdict was written (`run-tournament.sh:371-374`), producing `summary.csv`,
`summary.json` and `batch.meta.json` (which stamps `git_sha` and `git_dirty`) in the
result dir. `--result-dir` is given explicitly above only so the four dirs form one
findable set; the default is `<YYMMDD_HHMM>_<scenario>`.

## 6. Read out

```bash
for d in tools/autotest/tournament-results/260905_rebaseline_*; do
  echo "== $d"
  ./tools/autotest/tournament-report.sh "$d"
done
```

Then check every batch is complete before believing any of it:

```bash
# 10 verdicts per batch, and git_dirty must be false in all four
for d in tools/autotest/tournament-results/260905_rebaseline_*; do
  printf "%s  verdicts=%s  " "$d" "$(ls "$d"/match_*.json 2>/dev/null | wc -l)"
  grep -o '"git_sha": "[^"]*"\|"git_dirty": [a-z]*' "$d/batch.meta.json" | tr '\n' ' '; echo
done
```

A missing `match_<i>.json` is a **no-verdict**, and SPEC §9.1 requires triage by
reading `match_<i>.log` — a watchdog kill and an engine crash both present as "no
verdict" and mean opposite things. Do not median over a batch with an untriaged hole
in it.

**Ladder metrics, read post-hoc from each `match_<i>.json` → `notes` blob:**

- **S1** = `stats.capture_income_gross` (cumulative gross building income). *Not*
  `resources_earned`, which is context only, and never `PlayerStatistics.Income`.
- **S2** = `stats.kills_cost − stats.deaths_cost`, the net combat swing.
- Attribute strictly by `notes.players[].bot_type` — **never by slot or faction.**
  `--mirror` swaps the slots on odd seeds, so slot-attribution silently inverts half
  the sample.

## 7. Where the numbers get recorded

**Recording moved.** SPEC §5.1 states `WORKSPACE/ai-bench/runs/` stops at 2026-07-29
and results since 2026-07-31 are written to `WORKSPACE/benchmarks/<YYMMDD>-<name>.md`.
SPEC §8.3 still prescribes the older `runs/<ts>__<scenario>__<sha7>.json` cycle-card
form and has not been reconciled with §5.1; §5.1 is the later correction and is the one
the last five recorded batches actually followed.

1. **Raw** (git-ignored): the four `tools/autotest/tournament-results/260905_rebaseline_*` dirs.
2. **Result card** (committed): `WORKSPACE/benchmarks/260905-stable-rebaseline.md`.
   Follow the shape of `WORKSPACE/benchmarks/260802-exp-vs-stable0730-bothfixes.md` —
   an **Instrument** table (SHA + `git_dirty`, bots, scenario, config, sample, seeds,
   profile, raw dir), a per-game table, then the findings. Stamp the SHA
   `git rev-parse --short HEAD` printed in §2 and the `batch.meta.json` `git_sha`
   beside it; they must match.
3. **`LADDER.md`** — a new `> # ✅ CURRENT STANDING` blockquote at the top, in the
   format of the existing `POST-26/28 RE-BASELINE — 2026-07-29 (main @ e5b7bbcc,
   N=10/rung)` block: a `| Rung | Calibration (Stable-v-Stable) | Baseline
   (Exp-v-Stable) | Verdict |` table with S1 and S2 rows, plus a "Core finding"
   paragraph. It must say **explicitly** that this is the first corpus taken on a live
   economy and that every earlier number in the file is void, not merely superseded.
4. **`WORKSPACE/ai-bench/REVIEW.md`** — one `LADDER` activity-log line.
5. **`WORKSPACE/pipeline/items/43-benchmark-rebaseline.md`** — close it against the
   result card, or record what remains.

## 8. Expected wall-clock

`GameSpeed: fastest` is Timestep **40 ms** in WW3MOD (`mods/ww3mod/mod.yaml:392-395`);
`SpeedMultiplier: 8` divides it to `max(1, 40/8)` = **5 ms/tick**
(`BotVsBotMatchWatcher.cs:178-184`). Match length is `TimeLimitSeconds × 25` ticks
(`TournamentConfig.cs:103`).

| Batch | Ticks | Sim wall | + load | Per match | ×10 |
|---|---|---|---|---|---|
| S1 cal | 7,500 | 37.5 s | ~30 s | ~70 s | ~12 min |
| S2 cal | 18,000 | 90 s | ~30 s | ~120 s | ~20 min |
| S1 exp | 7,500 | 37.5 s | ~30 s | ~70 s | ~12 min |
| S2 exp | 18,000 | 90 s | ~30 s | ~120 s | ~20 min |
| | | | | **40 matches** | **~64 min + readout ≈ 65–80 min** |

At `SpeedMultiplier: 1` the same 40 matches cost **~6 hours**. Do not run them that
way. `SpeedMultiplier` only divides `world.Timestep`, which is wall-clock pacing and is
read by nothing the scorer uses, so the statistics are unchanged (recon report §3 for
the verification). 8× is the shipped, recorded configuration; changing it would itself
break comparability with this baseline.

## 9. Guardrails

- **Never push.** Commit locally; the manager pushes `main` only after a merge is
  verified.
- **Do not edit any `ai.yaml`, scenario or scorer during the run.** A mid-batch edit
  splits the corpus into two instruments and neither half is a baseline.
- **`git_dirty: true` in any `batch.meta.json` voids that batch.** Check it in §6.
- **Take all four batches on one build.** Rebuilding between them re-zeroes the
  instrument mid-measurement.
