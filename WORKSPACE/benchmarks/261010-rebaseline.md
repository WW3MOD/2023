# `@stable` benchmark RE-BASELINE #4 — after `a4fd519e` + `1a610a8c` (2026-10-10/11)

**Read the calibration first.** Two byte-identical copies of `@stable` produced an **8–2** side
split on this instrument (S2 cal, 260923) — and do so again here. **Any win split up to and
including 8–2, in either direction, carries no information at N=10**: under a fair coin
P(≥8 of 10, either side) = 0.109. The cal splits across builds (5–5, 6–4, 8–2, and now 7–3 / 8–2
again) come from the **same ten seeds**, so they are not independent and must not be pooled.

**The new reading on this page is the per-seed determinism diff, and it is the one reading here
that isn't noise.** Same seeds, scenarios, configs and host as 260923, deterministic sim: a seed
whose verdict is byte-identical in both corpora is a game whose *outcome* the code changes never
touched. **Of 40 games, 30 have a verdict byte-identical to 260923** (whole `notes` block —
stats, `unit_types`, `income_samples`, `capture_events`): S1 cal 8/10, S1 exp 10/10, S2 cal 5/10,
S2 exp 7/10. **One winner changed in 40** (S2 exp seed 5017, Stable → Exp). Every logged first
divergence is at tick **≥ 5,700**.

**Verdict: Tier 1 — "`@stable` changed behaviour in 7 of 20 cal games; no aggregate reading
leaves 260923's noise band at N=10."** See [Did `@stable` move?](#did-stable-move-plan-what-counts-as-stable-moved-materially).
**This corpus is the control for everything measured after `69ca8a13`.**

Recorded per SPEC §5.1, mirroring [`260923-rebaseline.md`](260923-rebaseline.md),
[`260922-rebaseline.md`](260922-rebaseline.md) and [`260905-rebaseline.md`](260905-rebaseline.md).
It replaces `261010-rebaseline-PLAN.md`, deleted in the commit that finalised this card, as that
plan required (recoverable from git history).

> **Why this corpus exists.** Two merges since the 260923 control change `@stable` and say so:
> `a4fd519e` (wt/rearm-desync — a bot's out-of-ammo rearm is a `RearmAtHost` **Order**, so it
> starts one order-latency later) and `1a610a8c` (wt/supply-guards — trucks refuse a Logistics
> Centre that is no longer allied; null-safe `GroundDangerAt`; idle-heli launchers skip dead
> actors). Both modules are shared, so both reach `@experimental` too. **This is a new control,
> not an A/B of those merges** — there is no arm with them off.

## Instrument

| | |
|---|---|
| **Stamped SHA** | **`69ca8a13`** — `git_sha: 69ca8a1370c77058003e15eaca7d48190545c52a`, `git_dirty: false` in all six `batch.meta.json` used (and in the aborted ones). |
| **Old control** | **`55df64e2`** (260923). 260923 → 261010 spans the PLAN's 113 commits to `45b2fca4` plus 20 more to `69ca8a13`. |
| `@stable`-moving merges in range | `a4fd519e`, `1a610a8c` — both verified ancestors of `69ca8a13` (`git merge-base --is-ancestor`). |
| Re-sweep `45b2fca4..69ca8a13` (`engine mods`) | 10 files. Sim-reachable candidates: **`Cargo.cs` + `GarrisonOwnershipMath.cs`** (`69ca8a13`, wt/cogarrison — on a garrison's owner change, only the outgoing owner's passengers convert) and **`SupplyRouteContestation.cs`** (`9b3bfbaf`, wt/stranded-passive — full-bar defeat decision). The rest is TestMode-only (`Game.cs`, `TestMode.cs`, `TestGlobal.cs`, `GameSaveRoundTripProbe.cs`) or tests. The SR change can only act at a full defeat bar; no match here has an SR defeat. The Cargo change acts when a garrison building changes owner with soldiers inside, and garrisons are used heavily on this map. See [Watch](#watch--what-this-corpus-does-not-establish). |
| Bots | `ModularBot@experimental` vs `ModularBot@stable`; cal batches `@stable` v `@stable`. |
| Regime | Unchanged: `Faction: america` both sides, `StartingUnitsClass: motorized`, opponent = `@stable`. |
| Scenarios / configs | Unchanged from 260923 (S1 `tournament-s1-eco-*`, S2 `tournament-s2-combat-river-zeta*`; 7,500 / 18,000 ticks). `scenario`, `config` and `seeds_requested` match 260923 in every primary `batch.meta.json`; **`max_wall_secs` matches on three of four** — see the cap raise below. |
| Sample | N=10 per batch. Exp batches `--mirror` (odd index = Exp in the Russia slot — verified 5/5 odd, 5/5 even on both exp batches). |
| Seeds | `i*1000+17`, 1017…10017 — identical to 260923, 260922, 260905. The spliced seeds carry `seed` 7017/8017 (S1) and 9017/10017 (S2) in their verdicts. |
| Profile | hidden, sequential. `--max-wall-secs` 900 (S1) / 2200 (S2 cal m1–m8) / **4000 (S2 cal m9–m10, all of S2 exp)**. **Run from a dedicated worktree** `/Users/fredrik/worktrees/ww3mod/rebaseline-261010` pinned at `69ca8a13` (the PLAN's one planned deviation from 260923, which ran from the main checkout). |
| Verdict version | `8` in every match — field-comparable with all three prior corpora. |
| Committed record | [`261010-rebaseline/`](261010-rebaseline/): `summary.json`, `summary.csv`, `batch.meta.json` for all six batch directories used, plus `run-tournament-seedlist.sh`. |
| Raw per-match files | **Mac-only, untracked:** `/Users/fredrik/worktrees/ww3mod/rebaseline-261010/tools/autotest/tournament-results/261010_rebaseline_{s1_cal,s1_cal_redo8,s1_exp,s2_cal,s2_cal_redo910b,s2_exp}/` (`match_N.json`, `match_N_debug.log`, `.log`, `.watcher.log`). Ignore the `*_ABORTED` dirs there. |

**Attribution strictly by `notes.players[].bot_type`** in exp batches; cal batches (both rows
`bot_type: stable`, both `faction: america`) are attributed by **player name** (`USA-bot` /
`Russia-bot`). Ladder metrics per RUNBOOK §6: **S1 = `stats.capture_income_gross`**, **S2 =
`stats.kills_cost − stats.deaths_cost`**.

## Batches

| # | Batch | Matchup | Verdicts | Win split | Wall clock | Status |
|---|---|---|---:|---|---|---|
| 1 | `261010_rebaseline_s1_cal` (+ `_redo8` m7, m8) | Stable v Stable | 10 / 10 (spliced) | **USA 7 – Russia 3**, 0 draws | first pass 1 h 46 m 15 s (8 verdicts); redo 1 h 23 m 48 s (8 matches) | **spliced** |
| 2 | `261010_rebaseline_s1_exp` | Exp v Stable, mirrored | 10 / 10 | **Exp 6 – Stable 4**, 0 draws | 1 h 34 m 52 s (~9 m 29 s/match) | clean |
| 3 | `261010_rebaseline_s2_cal` (+ `_redo910b` m9, m10) | Stable v Stable | 10 / 10 (spliced) | **USA 8 – Russia 2**, 0 draws | first pass 5 h 20 m 06 s (8 verdicts + 2 cap kills); redo 1 h 37 m 46 s (2 matches) | **spliced, cap 4000 on m9–m10** |
| 4 | `261010_rebaseline_s2_exp` | Exp v Stable, mirrored | 10 / 10 | **Exp 5 – Stable 5**, 0 draws | 5 h 05 m 02 s (~30 m 30 s/match) | **cap 4000** |

Wall clock from `batch.meta.json started_at` to the `summary.csv` mtime. All 40 verdicts ran the
**full clock** (`win_reason = time_limit`, `duration_ticks` 7,500 / 18,000). **0 draws, 0 SR
defeats, 0 no-contact voids** (every side has non-zero `kills_cost` and `deaths_cost`).

### Two splices — disclosed, and why each is safe

**S1 cal, seeds 7–8 from `261010_rebaseline_s1_cal_redo8`.** The first pass wrote no verdict for
7017 and 8017: both were killed at the 900 s wall cap under build load. `_redo8`
(`seeds_requested: 8`, same SHA, `git_dirty: false`, cap 900) replayed seeds 1–8. **Seeds 1–6 in
`_redo8` are byte-identical to the first pass over the entire `notes` block (6/6)**, and 7017 /
8017 are themselves byte-identical to 260923. `261010_rebaseline_s1_cal_redo7_ABORTED` is ignored.

**S2 cal, seeds 9–10 from `261010_rebaseline_s2_cal_redo910b`.** The first pass wrote verdicts for
seeds 1–8; 9017 and 10017 were killed at the 2,200 s cap under other-project host load (summary
written 12:14 CEST). They were re-run into `_redo910b` with `SEED_LIST="9 10"` through an
out-of-tree copy of the runner, recorded as
[`261010-rebaseline/run-tournament-seedlist.sh`](261010-rebaseline/run-tournament-seedlist.sh) —
**it differs from `run-tournament.sh` at `69ca8a13` by one line** (`for i in
${SEED_LIST:-$(seq 1 ${SEEDS})}`), so seed numbering, the `i*1000+17` seed and the match
filename are unchanged. `261010_rebaseline_s2_cal_redo910_ABORTED` is ignored. There is no
overlapping seed to byte-compare inside the batch, so the check is different and, I'd argue,
stronger: **S1 and S2 are the same game read at two ticks** (260923 instrument caveat), and the
first 7,500 ticks of `_redo910b` m9 and m10 (`income_samples` ≤ 7,500 and `capture_events` ≤
7,500) are **identical to S1 cal m9 and m10 of this corpus**, which are in turn byte-identical to
260923. (That same prefix check passes **10/10 for S1 cal ↔ S2 cal and 10/10 for S1 exp ↔ S2
exp**.) Both spliced seeds diverge from 260923 later, at ticks 10,900 and 13,516 in the logs —
in line with the other S2 divergences, not with a broken replay.

**Follow-up worth doing:** a `--seed-list` flag on `run-tournament.sh` itself, so a splice no
longer needs an out-of-tree runner copy.

### Cap raised to 4000 s for `s2_cal_redo910b` and all of `s2_exp` — a deviation from 260923's 2200 s

**Seeds are deterministic, so the wall cap only decides whether a slow match gets killed. It never
decides the verdict.** The cap is wall time; the verdict is a function of seed and code. The
evidence is in this corpus: **7 of the 10 `s2_exp` seeds, all run at cap 4000, are byte-identical
to their 260923 cap-2200 counterparts**, debug log included.

Observed S2 wall per match (verdict mtime minus previous; includes launcher overhead):
`s2_cal` m1–m8 1,569–2,036 s; `_redo910b` m9 **3,587 s**, m10 **2,279 s**; `s2_exp` m1–m8
1,822–2,283 s, m9–m10 854 / 865 s once the load lifted. **Five matches ran past 2,200 s** (redo m9,
m10; exp m1 2,283, m2 2,218, m4 2,223) and would have been killed under 260923's cap. 260923's
S2 range was 1,147–1,367 s. The host was loaded by other projects through most of the S2 run.

### Instrument caveat: verdict-identical is not decision-identical

A byte-identical **verdict** does not prove the bots made identical decisions. Diffing the
bracket-tagged lines of each pair of `*_debug.log` files finds the logs differing on **two seeds
whose verdicts are identical**:

| Seed | First logged difference | Verdict |
|---|---|---|
| S1 exp 5017 | tick 7,249: `[exp-offense] order … target=supplyroute@80,35 action=Pressure … via=54,27` (old) vs `via=54,26` (new) | identical through 7,500 (the same game diverges in S2 exp, and flips its winner) |
| S2 cal 4017 | tick 17,905 | identical through 18,000 |

So an `IDENTICAL` row here means "the outcome at the read tick did not change", which is what
the PLAN's test asks. At log level the count is **28/40** instead of 30/40 (S1 cal 8, S1 exp 9,
S2 cal 4, S2 exp 7).

## The per-seed determinism diff vs 260923

`IDENTICAL` = stats, winner and `duration_ticks` equal (the PLAN's test); every `IDENTICAL` here is
also identical over the **whole `notes` block**.

| Batch | IDENTICAL | DIVERGED | Winner changed | Diverged seeds |
|---|---:|---:|---:|---|
| S1 cal | **8** | 2 | 0 | 1017, 3017 |
| S1 exp | **10** | 0 | 0 | — |
| S2 cal | **5** | 5 | 0 | 1017, 3017, 7017, 9017, 10017 |
| S2 exp | **7** | 3 | **1** | 1017, 5017 (**Stable → Exp**), 9017 |
| **All** | **30 / 40** | 10 | **1** | |

S2 diverges more than S1, as predicted: it is the same games run 10,500 ticks further, and S2 cal
7017 / 9017 / 10017 and S2 exp 1017 / 9017 surface only after t=7,500.

### Where the diverged seeds first diverge

`income_samples` (every 25 ticks) and `capture_events` give the outcome-visible divergence; the
per-match `*_debug.log`, filtered to bracket-tagged bot lines excluding `[TestMode]` (no tag exists
only in one corpus), gives an earlier **first logged** divergence. **All are upper bounds** — the
sim diverged at or before the first logged difference.

| Seed | First logged divergence (debug log) | First differing `capture_event` | First differing income sample |
|---|---|---|---|
| S1/S2 cal 1017 | **5,700–5,704**: `[supply] cluster cell=34,55` (old) vs `34,54` (new) | 6,387 | 6,400 |
| S1/S2 cal 3017 | **6,441**: `[exp-ammo] release module=offense … at.america#4699 cell=40,47` vs `38,47` | 7,238 | 7,250 |
| S2 cal 7017 | **13,834**: `[exp-ammo] release module=garrison` — a different unit released (`ar.america#4762` vs `e3.america#4870`) | 14,030 | 14,050 |
| S2 cal 9017 | **10,900**: `[exp-ammo] release module=ambush` — different unit (`e3#4943` vs `ar#4983`) | 14,783 | 14,800 |
| S2 cal 10017 | **13,516**: old logs an `[exp-garrison] order`, new an `[exp-ammo] release module=garrison … at.america#4698` | 17,332 | 17,350 |
| S2 exp 1017 | **17,347**: `[exp-ammo] release module=offense … e3.america#5121 cell=41,48` vs `40,49` | none (captures/income identical; kills/deaths/army differ) | none |
| S2 exp 5017 | **7,249**: the offense `via=54,27` vs `54,26` line above | 10,895 | 10,900 |
| S2 exp 9017 | **11,785**: `[exp-ammo] release module=offense … aa.america#4745 cell=40,53` vs `41,51` | 17,992 | 18,000 |

**Read:** **six of eight first logged divergences are an `[exp-ammo] release` of a dry unit**, the
path `a4fd519e` changes. That is a strong hint and still not an attribution: the logged line is
where the difference became visible, and in four of the six it shows a unit already standing
elsewhere or a different unit released. The other two surface on a supply-cluster centre and an
offense waypoint. **No log names a `logisticscenter` as an errand host** (checked in the S1 cal
m1 and S2 cal m1 logs of both corpora), so the PLAN's worst case for `1a610a8c` — "most S2 seeds
diverge because truck errands to the neutral LCs fail" — did not materialise: half the S2 cal
seeds are untouched.

**This settles 260923's and the PLAN's open determinism question:** 30 of 40 games replay
byte-identically across a rebuild, a different checkout path, 133 commits and a raised wall cap.
It also retires 260923's "no build recorded for `55df64e2`" hypothesis.

## Calibration — side bias and noise band

### S1 cal (`s1_cal`, spliced), Stable v Stable, unmirrored

| # | Seed | Winner | USA cap-gross | RUS cap-gross | USA swing | RUS swing | Swing Δ (USA−RUS) | vs 260923 |
|---|------|--------|--------------:|--------------:|----------:|----------:|------:|---|
| 1 | 1017 | USA | 26,558 | 22,672 | −5,575 | −7,625 | +2,050 | **DIVERGED** |
| 2 | 2017 | Russia | 21,089 | 29,404 | −14,250 | +150 | −14,400 | same |
| 3 | 3017 | USA | 27,757 | 21,071 | −11,125 | +1,325 | −12,450 | **DIVERGED** |
| 4 | 4017 | USA | 29,259 | 26,998 | −7,375 | −6,275 | −1,100 | same |
| 5 | 5017 | USA | 27,093 | 25,598 | −675 | −7,875 | +7,200 | same |
| 6 | 6017 | USA | 27,863 | 25,928 | −2,800 | −5,750 | +2,950 | same |
| 7 | 7017 | USA | 28,094 | 26,757 | −1,425 | −7,425 | +6,000 | same (redo8) |
| 8 | 8017 | Russia | 26,813 | 28,285 | −7,150 | −9,300 | +2,150 | same (redo8) |
| 9 | 9017 | USA | 28,347 | 27,256 | +1,750 | −12,300 | +14,050 | same |
| 10 | 10017 | Russia | 25,859 | 25,380 | −6,175 | −4,175 | −2,000 | same |

**Win USA 7 – Russia 3** (`U R U U U U U R U R`, the 260923 sequence exactly). **Capture-gross
median USA 27,425 / Russia 26,342.5 (mean 26,873 / 25,935); USA leads capture 8/10, Cap Δ median
+1,416. Swing Δ median +2,100, mean +445, 6/10 positive. Noise band −14,400 … +14,050** —
unchanged, because both extremes are on seeds that replayed identically (2017, 9017).

### S2 cal (`s2_cal`, spliced), Stable v Stable, unmirrored

| # | Seed | Winner | USA cap-gross | RUS cap-gross | USA swing | RUS swing | Swing Δ (USA−RUS) | vs 260923 |
|---|------|--------|--------------:|--------------:|----------:|----------:|------:|---|
| 1 | 1017 | USA | 90,625 | 84,605 | −925 | −54,525 | +53,600 | **DIVERGED** |
| 2 | 2017 | Russia | 68,288 | 106,950 | −54,200 | −13,550 | −40,650 | same |
| 3 | 3017 | Russia | 83,584 | 91,244 | −46,175 | −4,825 | −41,350 | **DIVERGED** |
| 4 | 4017 | USA | 93,419 | 88,838 | −20,400 | −39,850 | +19,450 | same |
| 5 | 5017 | USA | 92,374 | 86,317 | −27,525 | −31,975 | +4,450 | same |
| 6 | 6017 | USA | 90,863 | 86,653 | −17,050 | −36,100 | +19,050 | same |
| 7 | 7017 | USA | 84,253 | 93,454 | −725 | −43,725 | +43,000 | **DIVERGED** |
| 8 | 8017 | USA | 92,873 | 88,225 | −8,025 | −50,025 | +42,000 | same |
| 9 | 9017 | USA | 90,790 | 90,813 | −19,075 | −35,025 | +15,950 | **DIVERGED** (redo910b) |
| 10 | 10017 | USA | 91,327 | 85,912 | −26,775 | −34,075 | +7,300 | **DIVERGED** (redo910b) |

**Win USA 8 – Russia 2** (`U R R U U U U U U U`, the 260923 sequence exactly — five games changed
and none changed its winner). **Capture-gross median USA 90,826.5 / Russia 88,531.5 (mean 87,840 /
90,301); USA leads capture 6/10 (260923: 8/10), Cap Δ median +4,395.5. Swing Δ median +17,500,
mean +12,280, 8/10 positive.**

1. **The mean/median split on capture persists.** USA leads the median, Russia leads the mean —
   seed 2017 (68,288 vs 106,950, identical to 260923) again carries the mean. **Quote the median
   and the win count; the mean is one game's hostage.**
2. **The S2 noise band widened again: −41,350 … +53,600** between two copies of the same bot
   (260923: −40,650 … +53,000). Both new extremes are diverged seeds (3017, 1017). **Any
   Exp-v-Stable S2 swing edge below ~$53,600 is inside the noise floor at N=10.** The band has
   widened at every step for four corpora.

### Per-seed winner across four builds

**S1 cal:**

| Seed | 260905 (`bb89f9fd`) | 260922 (`ef7362a7`) | 260923 (`55df64e2`) | 261010 (`69ca8a13`) |
|---|:--:|:--:|:--:|:--:|
| 1017 | R | U | U | U *(diverged)* |
| 2017 | U | R | R | R |
| 3017 | R | U | U | U *(diverged)* |
| 4017 | U | U | U | U |
| 5017 | R | U | U | U |
| 6017 | U | U | U | U |
| 7017 | R | U | U | U |
| 8017 | R | R | R | R |
| 9017 | U | R | U | U |
| 10017 | U | R | R | R |
| **split** | USA 5 | USA 6 | USA 7 | **USA 7** |

**S2 cal:**

| Seed | 260905 (`bb89f9fd`) | 260922 (`ef7362a7`) | 260923 (`55df64e2`) | 261010 (`69ca8a13`) |
|---|:--:|:--:|:--:|:--:|
| 1017 | U | U | U | U *(diverged)* |
| 2017 | R | R | R | R |
| 3017 | R | U | R | R *(diverged)* |
| 4017 | U | U | U | U |
| 5017 | R | U | U | U |
| 6017 | R | U | U | U |
| 7017 | R | U | U | U *(diverged)* |
| 8017 | U | R | U | U |
| 9017 | U | R | U | U *(diverged)* |
| 10017 | U | R | U | U *(diverged)* |
| **split** | USA 5 | USA 6 | USA 8 | **USA 8** |

**Seeds flipped 260923 → 261010: 0 of 10 on S1 cal, 0 of 10 on S2 cal** (260922 → 260923 flipped
1 and 4). Per the PLAN, flip counts are **not** a materiality criterion either way: zero flips
after two merges is as compatible with the instrument's normal behaviour as four flips after one.

## Baseline — Experimental v Stable

### S1 eco (`s1_exp`), mirrored — ladder metric `capture_income_gross`

**Byte-identical to 260923 on all 10 seeds**, so this table is 260923's, re-derived from the new
files:

| # | Seed | Exp slot | Winner (bot) | Exp cap-gross | Sta cap-gross | Cap Δ | Exp swing | Sta swing | Swing Δ |
|---|------|----------|--------------|--------------:|--------------:|------:|----------:|----------:|--------:|
| 1 | 1017 | Russia | **Exp** | 26,262 | 27,171 | −909 | −1,600 | −9,750 | +8,150 |
| 2 | 2017 | USA | **Exp** | 27,261 | 28,597 | −1,336 | −4,225 | −8,625 | +4,400 |
| 3 | 3017 | Russia | Stable | 28,672 | 29,493 | −821 | −8,175 | −625 | −7,550 |
| 4 | 4017 | USA | **Exp** | 29,843 | 27,799 | +2,044 | −6,975 | −1,725 | −5,250 |
| 5 | 5017 | Russia | **Exp** | 26,724 | 26,588 | +136 | −2,275 | −4,925 | +2,650 |
| 6 | 6017 | USA | Stable | 28,527 | 27,525 | +1,002 | −13,700 | +750 | −14,450 |
| 7 | 7017 | Russia | **Exp** | 27,208 | 28,010 | −802 | +650 | −8,800 | +9,450 |
| 8 | 8017 | USA | Stable | 23,930 | 27,231 | −3,301 | −8,525 | −6,325 | −2,200 |
| 9 | 9017 | Russia | Stable | 24,371 | 28,735 | −4,364 | −1,150 | −8,800 | +7,650 |
| 10 | 10017 | USA | **Exp** | 28,735 | 27,030 | +1,705 | −6,225 | −5,175 | −1,050 |

**Exp 6 – Stable 4** (`W W L W W L W L L W`), wins split 3 USA-slot / 3 Russia-slot.
**Capture-gross median Exp 27,234.5 / Stable 27,662 (mean 27,153 / 27,818); Exp leads capture
4/10, Cap Δ median −811.5, mean −664.6.** Swing Δ median +800, mean +180, 5/10 positive, range
−14,450 … +9,450.

### S2 combat (`s2_exp`), mirrored — ladder metric `kills_cost − deaths_cost`

| # | Seed | Exp slot | Winner (bot) | Exp cap-gross | Sta cap-gross | Cap Δ | Exp swing | Sta swing | Swing Δ | vs 260923 |
|---|------|----------|--------------|--------------:|--------------:|------:|----------:|----------:|--------:|---|
| 1 | 1017 | Russia | **Exp** | 91,148 | 88,285 | +2,863 | −24,200 | −34,400 | +10,200 | **DIVERGED** |
| 2 | 2017 | USA | Stable | 81,856 | 100,002 | −18,146 | −4,625 | −44,125 | +39,500 | same |
| 3 | 3017 | Russia | **Exp** | 100,361 | 83,804 | +16,557 | −8,575 | −36,625 | +28,050 | same |
| 4 | 4017 | USA | Stable | 81,969 | 101,673 | −19,704 | −38,575 | −17,625 | −20,950 | same |
| 5 | 5017 | Russia | **Exp** | 95,888 | 83,424 | +12,464 | −19,850 | −19,400 | −450 | **DIVERGED, winner flipped** (260923: Stable, Δ −38,200) |
| 6 | 6017 | USA | Stable | 91,527 | 90,525 | +1,002 | −37,750 | −14,400 | −23,350 | same |
| 7 | 7017 | Russia | **Exp** | 100,675 | 80,543 | +20,132 | −30,900 | −41,750 | +10,850 | same |
| 8 | 8017 | USA | Stable | 79,218 | 97,073 | −17,855 | −17,200 | −35,500 | +18,300 | same |
| 9 | 9017 | Russia | Stable | 67,686 | 100,920 | −33,234 | −12,600 | −34,900 | +22,300 | **DIVERGED** |
| 10 | 10017 | USA | **Exp** | 95,921 | 85,844 | +10,077 | −24,950 | −38,800 | +13,850 | same |

**Exp 5 – Stable 5** (`W L W L W L W L L W`), Exp wins split **4 Russia-slot / 1 USA-slot**; by
faction Russia won **8 of 10** regardless of bot (`summary.json` side win-rate Russia 80 %) —
on the seeds where the cal batch gives USA 8–2. **Ladder metric: mean swing Exp −21,922.5 / Stable
−31,752.5 → mean Swing Δ +9,830; median swing Exp −22,025 / Stable −35,200; median of the
per-match Swing Δ +12,350** (not the difference of the medians); **7/10 positive, range −23,350 …
+39,500.** Capture-gross median Exp 91,337.5 / Stable 89,405 (mean 88,625 / 91,209); Exp leads
capture 6/10, Cap Δ median +1,932.5, mean −2,584.4.

The one winner change (5017) moved the split 4–6 → 5–5. Its swing Δ moved +37,750 on one seed —
the largest single-seed shift in the corpus, and still a single game.

## Side by side — all four corpora

**Four different opponents, not one change measured four times.** 260923 → 261010 is host-clean
and seed-clean, like 260922 → 260923; it differs in the pinned worktree, the S2 cap raise, and
host load (wall time only — see above).

| | 260905 (`bb89f9fd`) | 260922 (`ef7362a7`) | 260923 (`55df64e2`) | **261010 (`69ca8a13`)** | |
|---|---|---|---|---|---|
| Host | Windows | macOS | macOS | macOS | 23→10 host-clean |
| Caps (S1/S2) | 300 / 600 s | 900 / 2200 s | 900 / 2200 s | 900 / 2200 s; **4000 s** for S2 cal m9–10 and S2 exp | wall-only |
| **Per-seed verdict IDENTICAL vs prior** | — | — | — | **S1 cal 8, S1 exp 10, S2 cal 5, S2 exp 7 (30/40)** | first such measurement |
| **S1 cal win split** | USA 5 – Rus 5 | USA 6 – Rus 4 | USA 7 – Rus 3 | **USA 7 – Rus 3** | 0 seeds flipped |
| **S2 cal win split** | USA 5 – Rus 5 | USA 6 – Rus 4 | USA 8 – Rus 2 | **USA 8 – Rus 2** | 0 seeds flipped |
| S1 cal cap median USA / Rus | — | — | 27,420 / 26,342 | **27,425 / 26,342.5** | |
| S2 cal cap median USA / Rus | — | — | 92,185 / 87,554.5 | **90,826.5 / 88,531.5** | |
| S2 cal capture bias | Russia leads 8/10 | USA leads 5/10 | USA leads 8/10 | **USA leads 6/10**, median +4,395.5 | four answers |
| S1 cal swing noise band | −8,500 … +8,500 | −2,500 … +10,900 | −14,400 … +14,050 | **−14,400 … +14,050** | unchanged |
| S2 cal swing noise band | −17,050 … +46,300 | −28,550 … +36,550 | −40,650 … +53,000 | **−41,350 … +53,600** | widest yet |
| **S1 Exp win rate** | 3/10 | 5/10 | 6/10 | **6/10** | byte-identical |
| S1 cap median Exp / Sta | 26,689 / 27,482 | 27,207 / 27,721 | 27,234 / 27,662 | **27,234.5 / 27,662** | Exp behind in all four |
| S1 Exp leads capture | 3/10 | 4/10 | 4/10 | **4/10** | |
| S1 Swing Δ median / mean | +600 / −2,850 | +150 / +790 | +800 / +180 | **+800 / +180** | |
| **S2 Exp win rate** | 2/10 | 6/10 | 4/10 | **5/10** | 1 seed flipped |
| S2 cap median Exp / Sta | 83,833 / 94,024 | 91,801 / 88,260 | 90,436 / 90,056 | **91,337.5 / 89,405** | deficit stays closed |
| S2 Exp leads capture | 2/10 | 6/10 | 6/10 | **6/10** | holds |
| S2 mean swing Exp / Sta | −26,790 / −29,810 | −21,752.5 / −32,617.5 | −25,682.5 / −30,242.5 | **−21,922.5 / −31,752.5** | |
| S2 Swing Δ median / mean | +7,300 / +3,020 | +17,825 / +10,865 | +10,800 / +4,560 | **+12,350 / +9,830** | |

### What changed

- **Little, and almost all of it on S2.** 30 of 40 verdicts are byte-identical to 260923; both
  cal win sequences are 260923's exactly; S1 exp is 260923's batch to the byte.
- **One winner in 40 changed** (S2 exp 5017), moving S2 Exp 4–6 → 5–5.
- **The S2 capture deficit that decided 260905 stays closed for a third corpus** (Exp leads 6/10,
  median gap +1,932.5). S1 is flat for a fourth.
- **The S2 cal noise band widened again**, by 700 at each end, on diverged seeds.

### What cannot be concluded from this

**Not which merge caused the divergences.** Six of eight first logged divergences sit on the
dry-unit release path `a4fd519e` changed, which is suggestive and nothing more: there is no arm
with either merge off, and a third candidate (`Cargo.cs`, see Watch) is unexcluded. **Not that
`@experimental` improved on S2**: 5–5 is far inside the 8–2 band, the change is one seed, and the
mean Swing Δ +9,830 is ~2× the uncorrected item-87 bias (≈ $4,490/match) — inside the cal band
either way. **Not two independent confirmations**: S1 and S2 are the same 20 games read twice.

## Did `@stable` move? (PLAN §"What counts as `@stable` moved materially")

Decided by the cal batches only, against [`260923-rebaseline.md`](260923-rebaseline.md).

- **Tier 0 (did not fire): ruled out.** It requires all 20 cal seeds `IDENTICAL`; **7 of 20
  diverged** (S1 1017, 3017; S2 1017, 3017, 7017, 9017, 10017).
- **Tier 2 (moved materially): not met.** None of the three criteria fires:
  - **(a)** cal side split 9–1 or 10–0: S1 cal **7–3**, S2 cal **8–2**. Not met.
  - **(b)** per-side median capture-gross shift larger than that side's 260923 cal spread:
    S1 USA **+5** vs spread 8,170 (21,089–29,259); S1 Russia **+0.5** vs 8,323 (21,081–29,404);
    S2 USA **−1,358.5** vs 25,720 (68,288–94,008); S2 Russia **+977** vs 24,961
    (81,989–106,950). The largest shift is ~5 % of the threshold. Not met.
  - **(c)** a cal match with `win_reason` ≠ `time_limit`, an SR defeat, or a no-contact void:
    **none in 20 cal matches.** Not met.
- **FINAL — Tier 1: "`@stable` changed behaviour in 7 of 20 cal games; no aggregate reading
  leaves 260923's noise band at N=10."**

Per the PLAN, Tier 1 needs no follow-up before exp results are read against this control.

## Core finding

**The two `@stable`-moving merges are real but small on this instrument.** 30 of 40 games replay
byte-identically to 260923; the 10 that diverged did so after tick 5,700; one winner in 40
changed; both cal win sequences are unchanged; every cal aggregate sits within ~5 % of the
materiality threshold. **Tier 1.**

**On the new control, `@experimental` is at parity with `@stable` — 6/10 on S1 eco, 5/10 on S2
combat.** S1 is 260923's batch to the byte. S2 Exp leads the ladder metric (median per-match Swing
Δ +12,350, 7/10 positive) and capture (6/10), both inside the cal band.

**The calibration still dominates any comparison.** Identical bots split 8–2 again on S2.
**No split up to 8–2 carries information at N=10.**

**This corpus is the control for everything measured after `69ca8a13`.**

## Watch — what this corpus does not establish

- **`Cargo.cs` (`69ca8a13`, wt/cogarrison) is a third candidate cause I did not rule out.** It
  changes which passengers convert when a garrison building changes owner, and **the path is
  live here**: ~40 `[garrison] enter` lines per S1 match in both corpora, and two of the eight
  first logged divergences are `module=garrison` ammo releases (S2 cal 7017, 10017). In a 1v1
  every passenger belongs to the outgoing or the incoming owner, so the new rule differs only if
  the old one's "convert" of a passenger *already owned by the incoming owner* was not a no-op. I
  did not read `Actor.ChangeOwner` to check. The PLAN's sweep stopped at `45b2fca4`, before this
  merge.
- **First-divergence ticks are upper bounds from logs, not from the sim.** I diffed bracket-tagged
  lines only (excluding `[TestMode]`), after confirming no tag exists in only one corpus. A log
  line whose text changed between `55df64e2` and `69ca8a13` would show as a false early
  divergence; none did. An untagged sim-relevant difference would be missed.
- **Verdict-identical ≠ decision-identical** (see the caveat above): S1 exp 5017's log already
  differs at 7,249 though its 7,500-tick verdict is byte-identical. Tier 0 could never have been
  claimed on verdicts alone; luckily it didn't need to be.
- **The S2 cal splice has no in-batch overlap check.** Its soundness rests on the S1↔S2 prefix
  identity (first 7,500 ticks of redo m9/m10 equal S1 cal m9/m10) and on the one-line runner
  diff. Ticks 7,500–18,000 of those two games have no second copy.
- **The cap raise is argued from determinism and evidenced by 7 byte-identical cap-4000 games,
  not by a same-seed A/B of the cap itself.** A cap can only kill a match, and a killed match has
  no verdict at all, so I see no path by which it changes one — but that is reasoning.
- **Host load was heavy and uneven.** It cost wall time only (every replay that should match does
  match), but anything that reads wall time — throughput comparisons with 260923 — is void here.
- **`kills_cost − deaths_cost` is still not zero-sum**, and the S2 swing is still biased ≈
  $4,490/match (PIPELINE item 87). Absolute swing levels are not a combat scoreboard.
- **One map, N=10, effectively 20 games, not 40.**

**Ref stamp:** corpus at `69ca8a13` (`git_dirty: false` in all six `batch.meta.json`), old
control at `55df64e2`. Run 2026-10-10 23:59:57 UTC → 2026-10-11 17:31:10 UTC (S1 cal 23:59 →
03:19 incl. redo; S1 exp 03:19 → 04:54; S2 cal 04:54 → 10:14 plus redo 10:48 → 12:26; S2 exp
12:26 → 17:31). Written from branch `wt/rebaseline-writeup` at `origin/main @ 69ca8a13`; raw data
read in place, unmodified.
