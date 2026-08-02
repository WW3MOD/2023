# Varied-map confirmation — Experimental vs Stable AI 0730, both delivery fixes (2026-08-02)

**What this measures:** whether the 10-0 Experimental sweep on
`tournament-s2-combat-river-zeta` (see `260802-exp-vs-stable0730-bothfixes.md`)
**generalizes to other maps**, or was a river-zeta-specific quirk. Same bots, same
both-delivery-fixes build, same 12-min config — only the **map** changes. Two new
rungs, N=10 mirrored each:

- `tournament-s2-combat-polar-disorder` (+ `-mirror`)
- `tournament-s2-combat-woodland-warfare` (+ `-mirror`)

Fresh seed base (`i*1000+31`, seeds 1031…10031) so **no seed is reused** from the
river-zeta batch (which used `i*1000+17`).

## Headline

| | River-zeta (prior) | **Polar-disorder** | **Woodland-warfare** | **Varied combined** |
|---|:---:|:---:|:---:|:---:|
| **Experimental W-L** | **10 – 0** | **10 – 0** | **10 – 0** | **20 – 0** |
| Win sequence (m1→m10) | `W×10` | `W×10` | `W×10` | — |
| TECN-fired (Exp tecn>0) | 10 / 10 | **10 / 10** | **10 / 10** | 20 / 20 |
| oilb held (Exp / Sta, total) | 41 / 14 | **44 / 9** | **31 / 0** | 75 / 9 |
| Exp `max_alive` / `pending` cap | 3–4 / 4≤5 | **3–4 / 4≤5** | **3–4 / 4≤5** | held all 20 |
| Army agg (Exp / Sta) | 25,250 / 28,250 | 20,850 / 22,650 | 22,600 / 20,100 | parity |
| Voids / crashes | 0 | **0** | **0** | 0 |
| Decision mode | 10× score@limit | 10× score@limit | 10× score@limit | all `time_limit` |

**Verdict: the sweep generalizes across maps. Experimental is now 30 – 0 across all
three benchmark maps** (river-zeta + polar + woodland), with the identical mechanical
signature every game: TECN completes (3–4 alive, `pending` capped at 4 ≤ floor 5, the
deadlock stays gone), the capture economy runs away (oilb 75 vs 9 across the 20 new
games), and army stays at rough parity. **Critical scope limit (below): all three
rungs share a byte-identical *capture-weighted* scorer, so this confirms
map-generalization, NOT scoring-generalization.** The river-zeta caveat "confirm on a
combat-weighted rung before generalizing" remains **unmet** — these are different
maps, same scoring weights.

## Instrument

| | |
|---|---|
| **SHA** | main `@ 8bd77ae9` (`batch.meta.json git_sha`, `git_dirty:false`). Rebuilt at HEAD before the run (`make.ps1 all`, 0 errors). |
| Bots | `experimental` = "Experimental AI" vs `stable` = "Stable AI 0730" (config `P1Bot:experimental P2Bot:stable`). |
| Scenarios | `tournament-s2-combat-polar-disorder`, `tournament-s2-combat-woodland-warfare` (S2 combat rungs, US-mirror — both factions `america`). |
| Config | `tournament-combat-12min.yaml` — **byte-identical across all three rungs** (verified `diff`): `TimeLimitSeconds:720`, `SpeedMultiplier:8`, `GameSpeed:fastest`, `WinRule:score_or_sr_capture`, `Scorer:weighted_components`, `ArmyValueWeight:1.0`, **`CaptureIncomeWeight:2.0`**, `KillsValueWeight:1.0`. |
| Sample | N=10 each, paired `--mirror <scenario>-mirror` (odd seeds = mirror/swapped slots; even = primary). |
| Seeds | deterministic `Test.RandomSeed = i*1000+31` (1031…10031) — **fresh base, disjoint from the river-zeta batch's `i*1000+17`.** |
| Profile | hidden (`OPENRA_WINDOW_HIDDEN=1`, baked into the runner), sequential, muted. `--max-wall-secs` auto = 360s/match (720×4/8). |
| Raw | `tools/autotest/tournament-results/260802_variedmaps_exp_vs_stable0730_polar/` and `…_woodland/` (git-ignored). |
| Runner | private seed-31 copy of `run-tournament.sh` (only line 283 changed, `+17`→`+31`; removed after the run). |
| Parsers | `parse-tecn-batch.py` (oilb/TECN/score) + `parse-floor-traj.py` (pending/alive). |

**Attribution** strictly by `notes.players[].bot_type` (never slot/faction; `--mirror`
swaps slots on odd seeds). Verified programmatically: all 20 winners resolve to
`bot_type == experimental`; 0 voids, 0 unattributed. `oilb` = `oilb#` entries in the
final `[exp-capture] ownership-snapshot` per owner. `TECN` = `max(total-tecns +
committed)` over `no-idle-capturers` samples.

## Per-game — Polar-disorder (Exp perspective)

| # | Map | Seed | Exp slot | Winner | Exp oilb | Sta oilb | Exp TECN | Sta TECN | Exp score | Sta score | Decided | Result |
|---|-----|------|----------|--------|---------:|---------:|---------:|---------:|----------:|----------:|:-------:|:---:|
| 1 | mirror  | 1031  | Russia | Experimental | 4 | 0 | 6  | 0 | 128,412 | 5,100  | time_limit | **W** |
| 2 | primary | 2031  | USA    | Experimental | 6 | 1 | 12 | 2 | 188,452 | 36,532 | time_limit | **W** |
| 3 | mirror  | 3031  | Russia | Experimental | 4 | 0 | 9  | 0 | 131,216 | 1,700  | time_limit | **W** |
| 4 | primary | 4031  | USA    | Experimental | 5 | 2 | 10 | 4 | 160,220 | 67,832 | time_limit | **W** |
| 5 | mirror  | 5031  | Russia | Experimental | 4 | 1 | 8  | 2 | 132,982 | 33,542 | time_limit | **W** |
| 6 | primary | 6031  | USA    | Experimental | 3 | 1 | 9  | 2 | 96,072  | 40,436 | time_limit | **W** |
| 7 | mirror  | 7031  | Russia | Experimental | 5 | 2 | 10 | 4 | 159,364 | 70,800 | time_limit | **W** |
| 8 | primary | 8031  | USA    | Experimental | 4 | 0 | 7  | 0 | 130,268 | 2,800  | time_limit | **W** |
| 9 | mirror  | 9031  | Russia | Experimental | 5 | 2 | 7  | 4 | 155,566 | 76,048 | time_limit | **W** |
| 10| primary | 10031 | USA    | Experimental | 4 | 0 | 8  | 0 | 129,614 | 7,050  | time_limit | **W** |

Totals: oilb **44 / 9**, TECN-fired **10 / 10**, army **20,850 / 22,650** (Exp below in
4/10, parity). Score ratio 2.4× (M6, closest) to 77× (M3). All 18,000-tick, 0 voids.

## Per-game — Woodland-warfare (Exp perspective)

| # | Map | Seed | Exp slot | Winner | Exp oilb | Sta oilb | Exp TECN | Sta TECN | Exp score | Sta score | Decided | Result |
|---|-----|------|----------|--------|---------:|---------:|---------:|---------:|----------:|----------:|:-------:|:---:|
| 1 | mirror  | 1031  | Russia | Experimental | 3 | 0 | 8  | 0 | 180,376 | 1,800 | time_limit | **W** |
| 2 | primary | 2031  | USA    | Experimental | 4 | 0 | 10 | 2 | 215,014 | 2,700 | time_limit | **W** |
| 3 | mirror  | 3031  | Russia | Experimental | 3 | 0 | 6  | 0 | 182,734 | 2,550 | time_limit | **W** |
| 4 | primary | 4031  | USA    | Experimental | 2 | 0 | 6  | 2 | 151,032 | 2,550 | time_limit | **W** |
| 5 | mirror  | 5031  | Russia | Experimental | 2 | 0 | 6  | 2 | 153,228 | 2,500 | time_limit | **W** |
| 6 | primary | 6031  | USA    | Experimental | 3 | 0 | 7  | 2 | 184,324 | 2,800 | time_limit | **W** |
| 7 | mirror  | 7031  | Russia | Experimental | 4 | 0 | 10 | 4 | 124,604 | 4,550 | time_limit | **W** |
| 8 | primary | 8031  | USA    | Experimental | 3 | 0 | 8  | 0 | 91,768  | 5,600 | time_limit | **W** |
| 9 | mirror  | 9031  | Russia | Experimental | 3 | 0 | 9  | 4 | 191,422 | 3,700 | time_limit | **W** |
| 10| primary | 10031 | USA    | Experimental | 4 | 0 | 9  | 0 | 221,108 | 4,350 | time_limit | **W** |

Totals: oilb **31 / 0**, TECN-fired **10 / 10**, army **22,600 / 20,100** (Exp below in
2/10, parity). **Stable held zero oilb in every game** — the most lopsided capture
race of the three maps. Score ratio 16× (M8, closest) to 100× (M1). All 18,000-tick, 0
voids.

## TECN delivery — the deadlock stays gone on both maps

The both-fixes signature reproduces exactly on the two new maps (identical to
river-zeta): every game, both bots.

| Metric | River-zeta | Polar | Woodland |
|---|:---:|:---:|:---:|
| Exp TECN-fired (`alive`/`tecn`>0) | 10/10 | **10/10** | **10/10** |
| Exp `max_alive` | 3–4 | **3–4** | **3–4** |
| Exp `max_pending` | 4 | **4** | **4** |
| Cap held (`pending ≤ floor=5`)? | yes | **yes** | **yes** |
| Stable lever (floor / priority / pending) | 1 / off / 0 | 1 / off / 0 | 1 / off / 0 |

`peek-don't-pop` drain + `[0, floor]` cap behave identically regardless of map: the
queue sits at a steady `pending=4` (one below floor 5), never the prior `pending=82`
deadlock. Stable is a clean negative control on all three maps (never touches the
lever).

## Does the capture-economy dominance generalize? — honest read

**Yes, across maps — within the capture-weighted scoring family.**

- The win is **not** a river-zeta map artifact. Two structurally different maps (polar
  ice, woodland) both reproduce a clean 10-0 with the same mechanism: TECN → oilb →
  score. The oilb race predicted the winner in all 20 new games (Exp led or tied oilb
  in 20/20; strictly led in 18/20 — the two ties are polar M-nothing… in fact Exp
  strictly led oilb in every game except where Stable managed 1–2, and Stable managed
  **0** oilb in *all 10* woodland games).
- If anything the gap **widens** on maps where Stable captures less: woodland (Stable
  oilb=0) is the most lopsided (score ratios 16×–100×), polar sits between woodland and
  river-zeta. This is consistent with — and strengthens — the river-zeta thesis that
  the edge is the capture economy compounding, not a map-specific fluke.
- **No regression on either map.** Army at rough parity (Exp within ~10% of Stable,
  slightly below on polar, slightly above on woodland), no army collapse, no runaway
  pending, 0 crashes, 0 voids across all 20 games.

**What this does NOT establish — the load-bearing caveat:**

- **All three rungs use a byte-identical scorer** (`diff` confirmed): `CaptureIncomeWeight:2.0`,
  double the weight of army or kills. These are **capture-weighted** rungs. So this
  batch varies the **map** but holds **scoring** fixed. The river-zeta benchmark's
  explicit caveat — *"the capture economy compounds… confirm on a combat-weighted rung
  before generalizing"* — is **still unmet.** Three capture-weighted maps agreeing tells
  us the effect is map-robust; it tells us nothing about whether Experimental still
  wins when combat is weighted over territory. That remains the open question.
- **Counter-composition (`cb93015c`) is still unmeasured** — the engine emits no
  per-unit-type/counter-buy telemetry marker, so its independent combat contribution
  cannot be isolated on these maps any more than on river-zeta. Army-at-parity again
  argues *against* a large independent combat effect: if counter-buys were dominating
  the fight, Exp army would lead, and it does not (it trails on polar).
- oilb/TECN telemetry is Experimental-emitted but snapshots the whole map, so Stable's
  holdings are observed, not inferred (same as river-zeta).

## Bottom line

- **30 – 0 across three benchmark maps** (river-zeta 10-0, polar 10-0, woodland 10-0).
  The both-fixes sweep is **map-robust**, not a river-zeta quirk. First-order goal of
  this confirmatory batch: achieved.
- **Same mechanism every map:** TECN completes (alive 3–4, pending capped at 4), oilb
  economy runs away (Exp 75 vs Stable 9 across the 20 new games), army at parity. The
  fix engages identically regardless of terrain.
- **The win is capture-economy-driven and the scoring caveat is unchanged.** All three
  rungs are capture-weighted (`CaptureIncomeWeight:2.0`); a **combat-weighted** rung is
  still required before claiming a general ladder verdict. This batch confirms *map*
  generalization, explicitly **not** *scoring* generalization.
- **No regression** on either new map.

## Caveats / scope

- Map generalization confirmed (3 maps); scoring generalization **not** — all rungs
  share the capture-weighted scorer. The single biggest open question (combat-weighted
  behavior) is untouched by this batch by construction.
- N=10 per map. A 10-0 per map is a strong signal but the capture-economy compounding
  amplifies a real edge into a blowout specifically on capture-weighted rungs.
- Counter-composition delivery remains unmeasurable (no telemetry marker); its
  contribution to the sweep is unproven, and the honest read attributes the win to the
  TECN/capture fix.

**Ref stamp:** batches ran at main `@ 8bd77ae9` (`git_dirty:false`), 2026-08-02, seeds
1031…10031. Prior single-map sweep: `260802-exp-vs-stable0730-bothfixes.md`
(`@ cb93015c`, river-zeta, seeds …17).
