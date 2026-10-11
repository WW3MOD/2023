# `@stable` benchmark RE-BASELINE #4 — after `a4fd519e` + `1a610a8c` (2026-10-10/11)

> **DRAFT — S1 COMPLETE, S2 PARTIAL.** Written while batches 3 and 4 were still running. S1 cal
> (10/10, one splice, see [Batches](#batches)) and S1 exp (10/10) are final. **S2 cal is PARTIAL
> at 1/10 verdicts and S2 exp is PARTIAL at 0/10** (as of 2026-10-11 07:29 CEST; ETA ~10:30 and
> ~14:00). Every S2 number, the four-build S2 table and the "did `@stable` move" tier are
> **provisional** until those land. The PLAN file
> [`261010-rebaseline-PLAN.md`](261010-rebaseline-PLAN.md) stays until this card is final.

**Read the calibration first.** Two byte-identical copies of `@stable` produced an **8–2** side
split on this instrument (S2 cal, 260923). **Any win split up to and including 8–2, in either
direction, carries no information at N=10** — under a fair coin P(≥8 of 10, either side) = 0.109.
The cal splits so far (5–5, 6–4, 8–2, and now 7–3 again on S1) come from the **same ten seeds**,
so they are not independent and must not be pooled.

**The new reading on this page is the per-seed determinism diff, and it is the one reading here
that isn't noise.** Same seeds, scenarios, configs and host as 260923, deterministic sim: a seed
whose verdict is byte-identical in both corpora is a game the code changes never touched.
**On S1, 8 of 10 cal games and 10 of 10 exp games are byte-identical to 260923** (whole `notes`
block — stats, `unit_types`, `income_samples`, `capture_events`). The two that diverged first
show it at tick **5,700–5,704** and **6,441** of 7,500.

Recorded per SPEC §5.1, mirroring [`260923-rebaseline.md`](260923-rebaseline.md),
[`260922-rebaseline.md`](260922-rebaseline.md) and [`260905-rebaseline.md`](260905-rebaseline.md).

> **Why this corpus exists.** Two merges since the 260923 control change `@stable` and say so:
> `a4fd519e` (wt/rearm-desync — a bot's out-of-ammo rearm is a `RearmAtHost` **Order**, so it
> starts one order-latency later) and `1a610a8c` (wt/supply-guards — trucks refuse a Logistics
> Centre that is no longer allied; null-safe `GroundDangerAt`; idle-heli launchers skip dead
> actors). Both modules are shared, so both reach `@experimental` too. **This is a new control,
> not an A/B of those merges** — there is no arm with them off.

## Instrument

| | |
|---|---|
| **Stamped SHA** | **`69ca8a13`** — `git_sha: 69ca8a1370c77058003e15eaca7d48190545c52a`, `git_dirty: false` in every `batch.meta.json` written so far (`s1_cal`, `s1_cal_redo8`, `s1_exp`, `s2_cal`; the aborted `redo7` too). |
| **Old control** | **`55df64e2`** (260923). 260923 → 261010 spans the PLAN's 113 commits to `45b2fca4` plus 20 more to `69ca8a13`. |
| `@stable`-moving merges in range | `a4fd519e`, `1a610a8c` — both verified ancestors of `69ca8a13` (`git merge-base --is-ancestor`). |
| Re-sweep `45b2fca4..69ca8a13` (`engine mods`) | 10 files. Sim-reachable candidates: **`Cargo.cs` + `GarrisonOwnershipMath.cs`** (`69ca8a13`, wt/cogarrison — on a garrison's owner change, only the outgoing owner's passengers convert) and **`SupplyRouteContestation.cs`** (`9b3bfbaf`, wt/stranded-passive — full-bar defeat decision). The rest is TestMode-only (`Game.cs`, `TestMode.cs`, `TestGlobal.cs`, `GameSaveRoundTripProbe.cs`) or tests. The SR change can only act at a full defeat bar; none of these matches has an SR defeat. The Cargo change acts when a garrison building changes owner with soldiers inside, and garrisons are used heavily on this map. See [Watch](#watch--what-this-corpus-does-not-establish). |
| Bots | `ModularBot@experimental` vs `ModularBot@stable`; cal batches `@stable` v `@stable`. |
| Regime | Unchanged: `Faction: america` both sides, `StartingUnitsClass: motorized`, opponent = `@stable`. |
| Scenarios / configs | Unchanged from 260923 (S1 `tournament-s1-eco-*`, S2 `tournament-s2-combat-river-zeta*`; 7,500 / 18,000 ticks). `batch.meta.json` `scenario`, `config`, `seeds_requested`, `max_wall_secs` match 260923 on every complete batch. |
| Sample | N=10 per batch. Exp batches `--mirror` (odd index = Exp in the Russia slot — verified 5/5 odd, 5/5 even on `s1_exp`). |
| Seeds | `i*1000+17`, 1017…10017 — identical to 260923, 260922, 260905. |
| Profile | hidden, sequential, `--max-wall-secs` 900 / 2200. **Run from a dedicated worktree** `/Users/fredrik/worktrees/ww3mod/rebaseline-261010` pinned at `69ca8a13` (the PLAN's one deliberate deviation from 260923, which ran from the main checkout). |
| Verdict version | `8` in every match — field-comparable with all three prior corpora. |
| Raw | `/Users/fredrik/worktrees/ww3mod/rebaseline-261010/tools/autotest/tournament-results/261010_rebaseline_{s1_cal,s1_cal_redo8,s1_exp,s2_cal,s2_exp}/` (untracked). |

**Attribution strictly by `notes.players[].bot_type`** in exp batches; cal batches (both rows
`bot_type: stable`, both `faction: america`) are attributed by **player name** (`USA-bot` /
`Russia-bot`). Ladder metrics per RUNBOOK §6: **S1 = `stats.capture_income_gross`**, **S2 =
`stats.kills_cost − stats.deaths_cost`**.

## Batches

| # | Batch | Matchup | Verdicts | Win split | Wall clock | Status |
|---|---|---|---:|---|---|---|
| 1 | `261010_rebaseline_s1_cal` (+ `_redo8` m7, m8) | Stable v Stable | 10 / 10 (spliced) | **USA 7 – Russia 3**, 0 draws | first pass 1 h 46 m 15 s (8 verdicts); redo 1 h 23 m 48 s (8 matches) | **spliced** — see below |
| 2 | `261010_rebaseline_s1_exp` | Exp v Stable, mirrored | 10 / 10 | **Exp 6 – Stable 4**, 0 draws | 1 h 34 m 52 s (~9 m 29 s/match) | clean |
| 3 | `261010_rebaseline_s2_cal` | Stable v Stable | **1 / 10 — PARTIAL** | USA 1 – Russia 0 so far | m1 30 m 24 s | running |
| 4 | `261010_rebaseline_s2_exp` | Exp v Stable, mirrored | **0 / 10 — PARTIAL** | — | — | not started |

Wall clock from `batch.meta.json started_at` to the `summary.csv` mtime (local CEST in the file
mtimes; `started_at` is UTC).

### The S1 cal splice — disclosed, and why it is safe

The first pass of `s1_cal` wrote **no verdict for seeds 7 and 8** (7017, 8017): both were killed
at the 900 s wall cap while the host was under build load (`match_7.json` / `match_8.json`
absent; `.log`/`.watcher.log` present). A re-run into `261010_rebaseline_s1_cal_redo8`
(`seeds_requested: 8`, same SHA, `git_dirty: false`) replayed seeds 1–8. **This card takes m7 and
m8 from `_redo8` and m1–m6, m9, m10 from the first pass.**

**Why that is not mixing corpora:** seeds 1–6 in `_redo8` are **byte-identical to the first pass
over the entire `notes` block** — re-checked here, 6/6, beyond the manager's own check (which
excluded only timing fields; `notes` carries none). A replay that reproduces six games exactly is
the same instrument. `261010_rebaseline_s1_cal_redo7_ABORTED` (one partial match, no verdict) is
**ignored**.

**The wall cap fired, which never happened on 260923.** Throughput here is ~45 % slower than
260923 on S1 (9 m 29 s vs 6 m 36 s per exp match) and ~47 % slower on S2 m1 (30 m 24 s vs
~20 m 41 s), consistent with concurrent build load on the host. **The sim is unaffected —
deterministic per seed, as the byte-identical replays show — but the cap is wall time**, so an S2
match is now ~1,824 s against a 2,200 s cap. If S2 batches see no-verdicts, it is the same cause;
the remedy is a re-run of that seed, not a cap change (changing the cap breaks comparability).

All 21 verdicts so far ran the **full clock** (`win_reason = time_limit`, `duration_ticks` 7,500 /
18,000). **0 draws, 0 SR defeats, 0 no-contact voids** (every side has non-zero `kills_cost` and
`deaths_cost`).

## The per-seed determinism diff vs 260923

Same seed → same game, unless code changed what the bots did. `IDENTICAL` = stats, winner and
`duration_ticks` equal (the PLAN's test); every `IDENTICAL` here is also **identical over the whole
`notes` block**.

| Batch | Verdicts | IDENTICAL | DIVERGED | Winner changed | Diverged seeds |
|---|---:|---:|---:|---:|---|
| S1 cal | 10 | **8** | **2** | **0** | 1017, 3017 |
| S1 exp | 10 | **10** | 0 | 0 | — |
| S2 cal | 1 (PARTIAL) | 0 | 1 | 0 | 1017 |
| S2 exp | 0 (PARTIAL) | — | — | — | — |

**S1 exp is 260923's S1 exp, to the byte.** Every number in [S1 eco](#s1-eco-s1_exp-mirrored--ladder-metric-capture_income_gross)
below therefore equals 260923's exactly.

### Where the diverged seeds first diverge

`income_samples` (every 25 ticks) and `capture_events` give the outcome-visible divergence; the
per-match `*_debug.log`, filtered to bracket-tagged bot lines (no tag exists only in one corpus),
gives an earlier **first logged** divergence. **All three are upper bounds** — the sim diverged at
or before the first logged difference, possibly silently earlier.

| Seed | First logged divergence (debug log) | First differing `capture_event` | First differing income sample |
|---|---|---|---|
| S1 cal 1017 | **tick 5,700–5,704**: `[supply] cluster cell=34,55` (old) vs `34,54` (new) — USA-bot's supply cluster centre is one cell off, i.e. some unit already stood elsewhere | 6,387: old has a Russia **steal** of POI 1259 (USA→Russia); new has no such steal and instead a neutral capture of POI 3038 at 6,430 | 6,400 |
| S1 cal 3017 | **tick 6,441**: `[exp-ammo] release module=offense … at.america#4699 cell=40,47` (old) vs `38,47` (new) — an out-of-ammo unit's release cell | 7,238 vs 7,248: the same USA steal of POI 3030, ten ticks earlier | 7,250 |
| S2 cal 1017 | identical to S1 cal 1017 (same game through t=7,500, per the S1/S2 instrument caveat) | 6,387 / 6,430, as above | 6,400 |

**Read:** both S1 divergences surface late (76 % and 86 % of the S1 clock). The 3017 one is on an
`[exp-ammo]` release of a dry unit — the path `a4fd519e` changes — **which is a hint, not an
attribution**: the logged line is where the difference became visible, not where it started, and
the 1017 one surfaces on a supply-cluster line instead. Neither log ever names a
`logisticscenter` as an errand host (0 matches in `260923_rebaseline_s2_cal/match_1_debug.log` and
`261010_rebaseline_s1_cal/match_1_debug.log`), so the PLAN's worst case for `1a610a8c` — "every
truck errand to the neutral LCs now fails, most S2 seeds diverge" — has no support so far.

**What this settles from 260923's Watch and the PLAN's Watch:** cross-build determinism is now
shown, not assumed. 18 of 20 S1 games replay byte-identically across a rebuild, a different
checkout path and 133 commits. Any build- or host-nondeterminism would have had to spare all 18.
It also retires 260923's "no build recorded for `55df64e2`" hypothesis — that tree's output is
reproduced exactly by a fresh build at a descendant.

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

The two diverged seeds kept their winner. 1017 moved USA cap-gross +1,114 and Russia −1,114
(the missing steal); 3017 moved Russia's swing from −2,150 to +1,325 and USA's from −9,650 to
−11,125 — the swing Δ shifted −4,950 on one seed, inside the band.

### S2 cal (`s2_cal`), Stable v Stable — **PARTIAL, 1/10**

| # | Seed | Winner | USA cap-gross | RUS cap-gross | USA swing | RUS swing | Swing Δ (USA−RUS) | vs 260923 |
|---|------|--------|--------------:|--------------:|----------:|----------:|------:|---|
| 1 | 1017 | USA | 90,625 | 84,605 | −925 | −54,525 | +53,600 | **DIVERGED** (260923: USA, Δ +17,600) |
| 2–10 | | *pending* | | | | | | |

One game is not a statistic. Note only that **m1's swing Δ +53,600 already sits at the top of
260923's S2 band (−40,650 … +53,000)** — a single diverged seed, from two identical bots, landing
on the band's edge. That is the noise band doing what 260923 said it does, not a signal.

### Per-seed winner across four builds

**S1 cal** (complete):

| Seed | 260905 (`bb89f9fd`) | 260922 (`ef7362a7`) | 260923 (`55df64e2`) | 261010 (`69ca8a13`) |
|---|:--:|:--:|:--:|:--:|
| 1017 | R | U | U | U *(diverged, same winner)* |
| 2017 | U | R | R | R |
| 3017 | R | U | U | U *(diverged, same winner)* |
| 4017 | U | U | U | U |
| 5017 | R | U | U | U |
| 6017 | U | U | U | U |
| 7017 | R | U | U | U |
| 8017 | R | R | R | R |
| 9017 | U | R | U | U |
| 10017 | U | R | R | R |
| **split** | USA 5 | USA 6 | USA 7 | **USA 7** |

**Seeds flipped 260923 → 261010: 0 of 10.** (260922 → 260923 flipped 1.)

**S2 cal** (PARTIAL):

| Seed | 260905 | 260922 | 260923 | 261010 |
|---|:--:|:--:|:--:|:--:|
| 1017 | U | U | U | U *(diverged, same winner)* |
| 2017 | R | R | R | *pending* |
| 3017 | R | U | R | *pending* |
| 4017 | U | U | U | *pending* |
| 5017 | R | U | U | *pending* |
| 6017 | R | U | U | *pending* |
| 7017 | R | U | U | *pending* |
| 8017 | U | R | U | *pending* |
| 9017 | U | R | U | *pending* |
| 10017 | U | R | U | *pending* |
| **split** | USA 5 | USA 6 | USA 8 | *1–0 so far* |

**Seeds flipped 260923 → 261010 so far: 0 of 1.** Per the PLAN, flip counts are **not** a
materiality criterion — 260922 → 260923 flipped 4 of 10 S2 seeds after one merge. Expect S2 to
diverge on more seeds than S1 did: it is the same games run 10,500 ticks further, and both S1
divergences surfaced after t=5,700.

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
−14,450 … +9,450. (260923 printed the medians as 27,234 and −812; same values, rounded.)

**Why S1 exp didn't move while S1 cal did:** not established. The likeliest reading is that the
changed paths (a dry unit's rearm, a truck's host check) simply didn't arise before t=7,500 in
these ten exp games in a way that altered anything, while in cal 1017 and 3017 they did — the cal
games are different games (both slots `@stable`, different opponents' choices). **Do not read
"S1 exp identical" as "`@experimental` is unaffected"** — S2 runs the same games 10,500 ticks
further.

### S2 combat (`s2_exp`), mirrored — ladder metric `kills_cost − deaths_cost` — **PARTIAL, 0/10**

*Pending — batch not started at time of writing (ETA ~14:00 CEST).* Fill: per-match table in the
260923 shape plus a `vs 260923` column; Exp win split and slot split; mean/median swing per bot,
median of per-match Swing Δ (not the difference of medians), count positive, range; capture-gross
medians and Exp-leads-capture count; IDENTICAL/DIVERGED count with first-divergence tick per
diverged seed (the drafting scripts were `/tmp/rb261010/{an,dl2,agg}.py` — ephemeral; the method is fully described above).

## Side by side — all four corpora

**Four different opponents, not one change measured four times.** 260923 → 261010 is host-clean
and seed-clean, like 260922 → 260923; it differs in being run from a pinned worktree and under
host load (wall time only).

| | 260905 (`bb89f9fd`) | 260922 (`ef7362a7`) | 260923 (`55df64e2`) | **261010 (`69ca8a13`)** | |
|---|---|---|---|---|---|
| Host | Windows | macOS | macOS | macOS | 23→10 host-clean |
| Caps (S1/S2) | 300 / 600 s | 900 / 2200 s | 900 / 2200 s | 900 / 2200 s | S1 cap fired twice (load) |
| **Per-seed IDENTICAL vs prior** | — | — | — | **S1 cal 8/10, S1 exp 10/10**, S2 *pending* | first such measurement |
| **S1 cal win split** | USA 5 – Rus 5 | USA 6 – Rus 4 | USA 7 – Rus 3 | **USA 7 – Rus 3** | 0 seeds flipped |
| **S2 cal win split** | USA 5 – Rus 5 | USA 6 – Rus 4 | USA 8 – Rus 2 | *PARTIAL 1–0* | |
| S1 cal cap median USA / Rus | — | — | 27,420 / 26,342 | **27,425 / 26,342.5** | |
| S2 cal capture bias | Russia leads 8/10 | USA leads 5/10 | USA leads 8/10 | *PARTIAL* | |
| S1 cal swing noise band | −8,500 … +8,500 | −2,500 … +10,900 | −14,400 … +14,050 | **−14,400 … +14,050** | unchanged |
| S2 cal swing noise band | −17,050 … +46,300 | −28,550 … +36,550 | −40,650 … +53,000 | *PARTIAL (m1 +53,600)* | |
| **S1 Exp win rate** | 3/10 | 5/10 | 6/10 | **6/10** | byte-identical |
| S1 cap median Exp / Sta | 26,689 / 27,482 | 27,207 / 27,721 | 27,234 / 27,662 | **27,234.5 / 27,662** | Exp behind in all four |
| S1 Exp leads capture | 3/10 | 4/10 | 4/10 | **4/10** | |
| S1 Swing Δ median / mean | +600 / −2,850 | +150 / +790 | +800 / +180 | **+800 / +180** | |
| **S2 Exp win rate** | 2/10 | 6/10 | 4/10 | *PARTIAL* | |
| S2 cap median Exp / Sta | 83,833 / 94,024 | 91,801 / 88,260 | 90,436 / 90,056 | *PARTIAL* | |
| S2 Exp leads capture | 2/10 | 6/10 | 6/10 | *PARTIAL* | |
| S2 Swing Δ median / mean | +7,300 / +3,020 | +17,825 / +10,865 | +10,800 / +4,560 | *PARTIAL* | |

### What changed (S1, final)

- **On S1, almost nothing: two of twenty games diverged, late, and neither changed its winner.**
  Every S1 aggregate on this page equals 260923's or moves by a rounding-sized amount (S1 cal
  USA cap median +5).
- **The first per-seed determinism reading on this instrument says the code change is real but
  small through t=7,500**: it touched 2 of 10 cal games and 0 of 10 exp games, and surfaced at
  ticks 5,700 and 6,441.

### What cannot be concluded from this

**Not which merge caused the two divergences.** There is no arm with either merge off, and the
first *logged* difference is not the first *simulated* one. **Not that S2 will look like S1** —
S2 is where changes that fire late get their 10,500 extra ticks. **Not anything from the exp
splits**: S1 exp is 260923's 6–4 to the byte, and 6–4 is far inside the 8–2 band.

## Did `@stable` move? (PLAN §"What counts as `@stable` moved materially") — **PROVISIONAL**

Decided by the cal batches only.

- **Tier 0 (did not fire): RULED OUT.** It needed all 20 cal seeds IDENTICAL; 3 cal seeds have
  already diverged (S1 1017, S1 3017, S2 1017).
- **Tier 2 (moved materially): not met on the data so far.**
  - (a) cal side split 9–1 or 10–0: S1 cal is **7–3**; S2 cal is 1–0 of 1 — undecided.
  - (b) per-side median cap-gross shift larger than that side's 260923 cal spread: S1 USA shift
    **+5** against a spread of 8,170 (21,089–29,259); S1 Russia **+0.5** against 8,323
    (21,081–29,404). Not met. S2 undecided.
  - (c) a cal match with `win_reason` ≠ `time_limit`, an SR defeat, or a no-contact void: **none
    in 11 cal matches so far.**
- **Provisional verdict — Tier 1:** *"`@stable` changed behaviour in at least 3 of 20 cal games
  (2/10 S1, 1/1 S2 so far); no aggregate reading leaves 260923's noise band at N=10."* **Final
  once S2 cal reaches 10/10:** restate N of 20, re-check (a)–(c) on S2 (S2 bands: USA cap-gross
  68,288–94,008, Russia 81,989–106,950), and promote to Tier 2 if any fires.

## Core finding (S1 final, S2 pending)

**The two `@stable`-moving merges are real but small on this instrument through t=7,500: 18 of 20
S1 games are byte-identical to 260923, the two that diverged did so after tick 5,700 and kept
their winners, and every S1 aggregate is unchanged.** S1 exp is 260923's batch to the byte —
Exp 6–4, Exp still behind on median capture-gross (27,234.5 / 27,662), leading capture in 4/10.

**The calibration still dominates any comparison.** Identical bots have split 8–2 here; S1 cal
reads 7–3 again. **No split up to 8–2 carries information at N=10.**

**This corpus becomes the control for everything measured after `69ca8a13` once S2 completes.**
Until then, the S1 rung of this card and the S1 rung of 260923 are interchangeable to within two
games.

## Watch — what this corpus does not establish

- **The S2 half is the half that matters for "did `@stable` move", and it isn't in.** Both S1
  divergences surfaced after 76 % of the S1 clock; S2 runs the same games to t=18,000. The
  provisional Tier 1 can still become Tier 2 on S2 cal alone.
- **The first-divergence ticks are upper bounds from logs, not from the sim.** The debug log
  records bot decisions; a unit can move differently many ticks before any logged line changes.
  I diffed only bracket-tagged lines (excluding `[TestMode]`), after confirming no tag exists in
  only one corpus — a log line whose *text* changed between `55df64e2` and `69ca8a13` would
  surface as a false early divergence, and none did, but an untagged sim-relevant line would be
  missed.
- **The `a4fd519e` hint on seed 3017 is a hint.** The line that first differs is an `[exp-ammo]`
  release of a dry unit, which is the rearm path — but its *cell* differs, meaning that unit was
  already elsewhere. Attribution needs an arm with the merge reverted; none exists.
- **`Cargo.cs` (`69ca8a13`, wt/cogarrison) is a third candidate I did not rule out.** It changes
  which passengers convert when a garrison building changes owner, and **the path is live here**:
  ~40 `[garrison] enter` lines per S1 match in both corpora (S1 cal m1: 42 old / 40 new). In a
  1v1 every passenger belongs to either the outgoing or the incoming owner, so the new rule
  differs from the old only if the old one's "convert" of a passenger *already owned by the
  incoming owner* (e.g. the first claimant of a Neutral building) was not a no-op. I did not
  read `Actor.ChangeOwner` to check. The PLAN's sweep stopped at `45b2fca4` and predates this
  merge.
- **The wall cap is now within ~17 % of S2 matches** (1,824 s observed vs 2,200 s cap) because of
  host load. A no-verdict in S2 is a wall-time artefact, not a sim result; re-run the seed into a
  fresh directory and splice as S1 did, with the same byte-identity check on overlapping seeds.
- **The splice rests on six overlapping seeds.** Seeds 7 and 8 themselves have no second
  first-pass copy to compare against; their identity to 260923 (both `IDENTICAL`, whole `notes`)
  is independent evidence they are sound.
- **`kills_cost − deaths_cost` is still not zero-sum** and the S2 swing is still biased ≈
  $4,490/match by PIPELINE item 87; carry 260923's caveats unchanged.
- **One map, N=10, effectively 20 games not 40** (S1 and S2 are the same games read at two
  ticks — the S2 cal m1 debug log matches S1 cal m1's divergence exactly, as expected).

**Ref stamp:** corpus at `69ca8a13` (`git_dirty: false` in every `batch.meta.json`), old control
at `55df64e2`. S1 cal run 2026-10-10 23:59:57 UTC → 2026-10-11 03:19:57 UTC (with redo); S1 exp
03:19:58 → 04:54:50 UTC; S2 cal started 04:54:51 UTC. Written 2026-10-11 ~07:30 CEST from
branch `wt/rebaseline-writeup` at `origin/main @ 69ca8a13`; raw data read in place, unmodified.
