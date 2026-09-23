# `@stable` benchmark RE-BASELINE #3 — after PIPELINE item 64 (2026-09-23)

**Pipeline item 64.** Four batches, 40 measured matches, run from the main checkout
`/Users/fredrik/Desktop/WW3MOD` by `/tmp/queue44.sh` (runner log `/tmp/queue44.log`,
ends `QUEUE44 COMPLETE`). Recorded per SPEC §5.1
(`WORKSPACE/benchmarks/<YYMMDD>-<name>.md`), mirroring
[`260922-rebaseline.md`](260922-rebaseline.md) and
[`260905-rebaseline.md`](260905-rebaseline.md).

> **Why this corpus exists: the control moved again.** Item 64 merged at `main @ 55df64e2`
> and **MOVES `@stable`** — `MountedTransportBotModule` is shared by both profiles, and both
> new levers are opted in on **both** twins: `EscortUnloadEnabled: true` (`ai.yaml:2333` and
> `:2478`, C# default `false` at `MountedTransportBotModule.cs:123`) and
> `EscortLoadGraceTicks: 100` (`ai.yaml:2362` and `:2507`, C# default `0` at `:150`). A
> carrier whose escort is already in contact now departs at the minimum load after the grace
> window, standing down reserved stragglers, and finishes its delivery within
> `EscortUnloadCells` (5) of the offensive axis lead instead of driving on to the lerp drop
> cell. `InfantryEscortHoldEnabled` shipped **OFF** on both twins (`ai.yaml:551`, `:3256`) and
> `RendezvousWithOffensiveStaging` is still **false** on both (`ai.yaml:2317`, `:2462`).
> **`@stable` is therefore not the same opponent it was on 2026-09-22, and the 260922 corpus
> is no longer the control.** This card replaces it. The 260922 and 260905 numbers remain
> valid *as measurements of the bots that existed then*, and the three-way side-by-side below
> is a comparison of three different opponents, not a before/after of one change in isolation
> — see [Watch](#watch--what-this-corpus-does-not-establish).

## Instrument

| | |
|---|---|
| **Stamped SHA** | `55df64e2` — `batch.meta.json git_sha: 55df64e207fe536c1bbab3af97d96d53ca4f0049`, `git_dirty: false`, **identical in all four `batch.meta.json` and verified field-by-field**. The runner's own `START` line also records `dirty=0`. |
| **Code SHA under test** | **`55df64e2`** — the item-64 merge itself. No docs-only offset: stamped SHA and code SHA are the same commit, as on 260922. |
| Bots | `ModularBot@experimental` vs `ModularBot@stable`; calibration batches are `@stable` v `@stable`. |
| Regime | Unchanged from 260905/260922: `Faction: america` both sides, `StartingUnitsClass: motorized`, opponent = `@stable`. |
| Scenarios | S1 `tournament-s1-eco-river-zeta` (+`-mirror`), S1 cal `tournament-s1-eco-cal-nn`; S2 `tournament-s2-combat-river-zeta` (+`-mirror`), S2 cal `tournament-s2-combat-river-zeta-cal-nn`. |
| Configs | `tournament-eco-5min.yaml` (7,500 ticks) / `tournament-combat-12min.yaml` (18,000 ticks). |
| Sample | N=10 per batch. Exp batches paired `--mirror` (odd match index = Exp in the Russia slot — verified directly, 5/5 odd and 5/5 even). Cal batches unmirrored (both bots identical — a swap is a no-op). |
| Seeds | `Test.RandomSeed = i*1000+17`, i.e. 1017…10017, identical across all four batches and **identical to both 260922 and 260905**. |
| Profile | hidden, sequential, **`--max-wall-secs` 900 (S1) / 2200 (S2)** — identical caps and identical commands to 260922. |
| Verdict version | `8` in all 40 matches — **same verdict schema as 260922 and 260905**, so all three corpora are field-comparable. |
| Raw | `tools/autotest/tournament-results/260923_rebaseline_{s1_cal,s1_exp,s2_cal,s2_exp}/` (untracked; not in any worktree). |

**Attribution is strictly by `notes.players[].bot_type`**, never by slot or faction —
`--mirror` swaps slots on odd match indices, so slot-attribution silently inverts half the
sample. In the **cal** batches both rows carry `bot_type: stable`, so those two tables are
attributed by player name / faction instead, exactly as the 260922 and 260905 cards do.
Ladder metrics per RUNBOOK §6: **S1 = `stats.capture_income_gross`**, **S2 = `stats.kills_cost
− stats.deaths_cost`**.

## Batches

| # | Batch | Matchup | Verdicts | Win split | Wall clock | Status |
|---|---|---|---:|---|---|---|
| 1 | `260923_rebaseline_s1_cal` | Stable v Stable | 10 / 10 | **USA 7 – Russia 3**, 0 draws | 1 h 00 m 53 s (~6 m 05 s/match) | clean |
| 2 | `260923_rebaseline_s1_exp` | Exp v Stable, mirrored | 10 / 10 | **Exp 6 – Stable 4**, 0 draws | 1 h 06 m 02 s (~6 m 36 s/match) | clean |
| 3 | `260923_rebaseline_s2_cal` | Stable v Stable | 10 / 10 | **USA 8 – Russia 2**, 0 draws | 3 h 26 m 52 s (~20 m 41 s/match) | clean |
| 4 | `260923_rebaseline_s2_exp` | Exp v Stable, mirrored | 10 / 10 | **Exp 4 – Stable 6**, 0 draws | 3 h 30 m 06 s (~21 m 01 s/match) | clean |

Batch order as executed: 1 → 2 → 3 → 4, strictly sequential, no overlap. Wall clock measured
from `batch.meta.json started_at` to the `summary.csv` mtime. Total 2026-09-22 20:53:27 →
2026-09-23 05:57:20 UTC = **9 h 03 m 53 s** (22:53:27 → 07:57:21 local, matching
`/tmp/queue44.log`).

All 40 measured matches ran the **full clock** (`duration_ticks` = 7,500 / 18,000,
`win_reason = time_limit` in every one). **0 no-verdicts, 0 culls, 0 crashes, 0 draws, 0 SR
captures.** Engagement validity **10/10 on every batch** — every match has non-zero
`kills_cost` *and* `deaths_cost` on both sides, so no match is a no-contact void.

**Nothing came near a cap.** Longest match 433 s (S1, cap 900) and 1,367 s (S2, cap 2,200) —
per-match wall including launcher overhead, so an upper bound on sim time. Shortest 337 s /
1,147 s.

### Instrument caveat: same host as 260922, still not the host of 260905

The 260905 corpus ran on the Windows checkout `C:/Users/fredr/Desktop/WW3MOD`. **This corpus
ran on macOS from `/Users/fredrik/Desktop/WW3MOD`, the same host as 260922**, with the same
`--max-wall-secs` 900/2200 and the same runner shape. Observed throughput **≈19–21 effective
ticks/s on S1 and ≈14–15 on S2** (wall per match including launcher overhead, so a lower bound
on sim speed) — indistinguishable from 260922's 18–21 / 13–15. **At the 260905 caps of 300 s /
600 s, every one of these 40 matches would still have been culled** (S1 min 337 s, S2 min
1,147 s).

**The 260922 → 260923 step is therefore the cleanest comparison in the set**: host, caps,
runner script shape, scenario files, configs and seeds are all unchanged, and **only the code
moved.** The 260905 → 260922 step conflates a host change with 16 days of merges, and nothing
here resolves it. The cheap disproof for the host question is unchanged and still unrun:
re-run one seed on the Windows host at a shared SHA and compare `match_N.json` field-for-field.

### Instrument caveat: S1 and S2 are the SAME GAME, read at two different ticks

**Confirmed again, and it is a property of the instrument, not of item 64.** All six scenarios
(the four batches plus the two mirror twins) ship the **same `map.bin`** (md5
`ea8c479df8dbdfe7c5bd9c2bc8d85711`) and a **byte-identical `rules.yaml`** (md5
`faa53b9342fc9e780babd514f4a4438f`, all six), and the only functional difference between the
S1 and S2 configs is `TimeLimitSeconds: 300` vs `720`. Empirically:

- **All 40 `time_to_first_capture_tick` values match pairwise** between each S1 batch and its
  S2 partner, per player, per match — 20/20 rows in each pair.
- **Zero monotonicity violations** across nine cumulative stats (`capture_income_gross`,
  `captures_count`, `steals_count`, `losses_count`, `kills_cost`, `deaths_cost`,
  `units_killed`, `units_dead`, `resources_earned`): the S2 value is ≥ the S1 value in all
  360 comparisons.
- `capture_income_gross == hold_ticks == poi_income_gross` in **80/80** player rows, so the S1
  ladder metric is a POI·tick count.

**Consequence for reading this card, unchanged: the corpus contains 20 distinct games, not 40.**
S1 reads them at t=7,500 and S2 reads the same games at t=18,000 under a different metric.
**The two rungs are not independent samples.**

**Two things the 260922 card asserts that this corpus contradicts. Neither is a defect in that
card's data; both are coincidences of that batch read as structure.**

1. **The S1 and S2 winner sequences are NOT the same.** 260922 recorded that `s1_cal` and
   `s2_cal` "produce the identical winner-by-faction sequence `U R U U U U U R R R`." Here they
   differ in **3 of 10** (`s1_cal` `U R U U U U U R U R` vs `s2_cal` `U R R U U U U U U U`,
   diverging at m3, m8, m10), and the exp pair differs in **4 of 10** by bot (`s1_exp`
   `W W L W W L W L L W` vs `s2_exp` `W L W L L L W L L W`). The same-game property still holds
   — 20/20 and 180/180 on both pairs above — so **the games are identical and the *verdicts*
   genuinely diverge between t=7,500 and t=18,000.** That is the correct reading: leading at
   five minutes does not mean leading at twelve. Do not expect the sequences to match.
2. **There is no 63,000 ceiling.** 260922 states the S1→S2 capture increment "is capped at
   exactly 63,000 = 6 POI × 10,500 ticks." **This corpus exceeds it**: the largest increments
   are **77,546** (cal pair m2, Russia — 7.39 POI·ticks per tick) and **73,874** (exp pair m4,
   Russia). The map carries **twelve** neutral OILB derricks, not six — `map.yaml`'s own header
   says *"all 12 neutral OILB oil-derrick income POIs kept intact"* — and the highest
   `captures_count` anywhere in this corpus is **7**. The real ceiling is 126,000. Five rows
   here land on exactly 63,000 (1 cal, 4 exp), which is presumably what made it look like a cap.

## Calibration — side bias and noise band

### S1 cal (`s1_cal`), Stable v Stable, unmirrored

| # | Seed | Winner | USA cap-gross | RUS cap-gross | USA swing | RUS swing | Swing Δ (USA−RUS) |
|---|------|--------|--------------:|--------------:|----------:|----------:|------:|
| 1 | 1017 | USA | 25,444 | 23,786 | −6,950 | −7,650 | +700 |
| 2 | 2017 | Russia | 21,089 | 29,404 | −14,250 | +150 | −14,400 |
| 3 | 3017 | USA | 27,747 | 21,081 | −9,650 | −2,150 | −7,500 |
| 4 | 4017 | USA | 29,259 | 26,998 | −7,375 | −6,275 | −1,100 |
| 5 | 5017 | USA | 27,093 | 25,598 | −675 | −7,875 | +7,200 |
| 6 | 6017 | USA | 27,863 | 25,928 | −2,800 | −5,750 | +2,950 |
| 7 | 7017 | USA | 28,094 | 26,757 | −1,425 | −7,425 | +6,000 |
| 8 | 8017 | Russia | 26,813 | 28,285 | −7,150 | −9,300 | +2,150 |
| 9 | 9017 | USA | 28,347 | 27,256 | +1,750 | −12,300 | +14,050 |
| 10 | 10017 | Russia | 25,859 | 25,380 | −6,175 | −4,175 | −2,000 |

**Win USA 7 – Russia 3. Capture-gross median USA 27,420 / Russia 26,342 (mean 26,761 /
26,047); USA leads capture in 8/10, Cap Δ median +1,416. Swing Δ median +1,425, mean +805,
6/10 positive.** **The noise band on the swing is −14,400 … +14,050** between two copies of the
same bot — wider than 260922's −2,500 … +10,900 and than 260905's ±8,500, but the same order.

### S2 cal (`s2_cal`), Stable v Stable, unmirrored

| # | Seed | Winner | USA cap-gross | RUS cap-gross | USA swing | RUS swing | Swing Δ (USA−RUS) |
|---|------|--------|--------------:|--------------:|----------:|----------:|------:|
| 1 | 1017 | USA | 87,716 | 87,514 | −19,025 | −36,625 | +17,600 |
| 2 | 2017 | Russia | 68,288 | 106,950 | −54,200 | −13,550 | −40,650 |
| 3 | 3017 | Russia | 92,839 | 81,989 | −45,075 | −8,075 | −37,000 |
| 4 | 4017 | USA | 93,419 | 88,838 | −20,400 | −39,850 | +19,450 |
| 5 | 5017 | USA | 92,374 | 86,317 | −27,525 | −31,975 | +4,450 |
| 6 | 6017 | USA | 90,863 | 86,653 | −17,050 | −36,100 | +19,050 |
| 7 | 7017 | USA | 85,119 | 93,234 | +2,825 | −50,175 | +53,000 |
| 8 | 8017 | USA | 92,873 | 88,225 | −8,025 | −50,025 | +42,000 |
| 9 | 9017 | USA | 94,008 | 87,595 | −14,575 | −37,925 | +23,350 |
| 10 | 10017 | USA | 91,996 | 85,243 | −17,825 | −34,725 | +16,900 |

**Win USA 8 – Russia 2. Capture-gross median USA 92,185 / Russia 87,554 (mean 88,950 /
89,256); USA leads capture in 8/10, Cap Δ median +4,614. Swing Δ median +18,325, mean +11,815,
8/10 positive.**

Three things to carry forward:

1. **Note the mean/median split on capture.** USA leads capture in 8 of 10 games and its
   *median* is 4,631 higher, yet Russia's *mean* is 306 higher — one game (m2, Russia 106,950
   against USA 68,288) carries the whole difference. **Quote the median and the win count on
   this rung; the mean is one game's hostage.**
2. **The S2 noise band on the swing is the widest yet measured: −40,650 … +53,000** between two
   copies of the same bot. **Any Exp-v-Stable swing edge below ~$53,000 on S2 is inside the
   noise floor of this instrument at N=10.** (260922 measured ±$36,550; 260905 ±$46,300. The
   band is not converging.)
3. **The side split is the headline number on this page and it is a warning, not a finding** —
   see below.

### The cal split has gone 5–5 → 6–4 → 8–2 with identical bots. What that does and does not mean

The seeds are **identical across all three corpora** (1017…10017), so these are not three
independent draws from a side-bias distribution: they are **the same ten seeds replayed against
three different builds of `@stable`**. That rules out a seed effect in the ordinary sense —
nothing about the seeds changed — and leaves the only other available explanation: **the
per-seed side outcome on this map is sensitive to small code changes in the bot, and ten seeds
is nowhere near enough to average that out.**

Per-seed winner, S2 cal, across the three builds:

| Seed | 260905 (`bb89f9fd`) | 260922 (`ef7362a7`) | 260923 (`55df64e2`) |
|---|:--:|:--:|:--:|
| 1017 | U | U | U |
| 2017 | R | R | R |
| 3017 | R | U | **R** |
| 4017 | U | U | U |
| 5017 | R | **U** | U |
| 6017 | R | **U** | U |
| 7017 | R | **U** | U |
| 8017 | U | **R** | **U** |
| 9017 | U | **R** | **U** |
| 10017 | U | **R** | **U** |
| **split** | USA 5 | USA 6 | **USA 8** |

**Only three seeds of ten — 1017, 2017, 4017 — keep the same winner across all three builds.**
Between 260922 and 260923, where nothing but the code changed, **four seeds flipped** (m3
U→R; m8, m9, m10 R→U) for a net of +2 to USA. On the S1 cal rung the same step flipped
**exactly one** seed (m9 R→U), moving 6–4 → 7–3.

**What this bounds — and it is the most useful sentence on this page.** On this instrument, two
**byte-identical** bots have now produced an **8–2** side split. Under a fair coin
P(≥8 of 10, either side) = 0.109 — unusual, not rare, and we drew it on the third attempt.
Therefore: **any Exp-v-Stable win split up to and including 8–2, in either direction, is
indistinguishable from two copies of the same bot on this rung.** Today's exp splits are 6–4
(S1) and 4–6 (S2). Both are far inside that, and **neither is evidence of anything.**

**Do not over-read the drift itself either.** `5–5 → 6–4 → 8–2` looks like a trend and is not
one. It is three points; the middle one differs from the first by a host change as well as a
code change; and a 5–5 is not "more side-fair" than an 8–2 — under a fair coin 5–5 has
probability 0.246 and is simply the modal outcome, while 6–4 has 0.205 per side. **The three
splits are also correlated, not independent** (same seeds, overlapping code), so they cannot be
pooled into an N=30 reading. The right summary is: *this rung's side outcome is unstable under
code change at N=10, and the instrument cannot currently separate side bias from bot strength.*

## Baseline — Experimental v Stable

### S1 eco (`s1_exp`), mirrored — ladder metric `capture_income_gross`

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

**Exp win rate 6/10.** Win sequence (m1→m10): `W W L W W L W L L W`. **Ladder metric:
capture-gross median Exp 27,234 / Stable 27,662 (mean 27,153 / 27,818); Exp leads capture in
4/10, Cap Δ median −812, mean −665.** Swing Δ median +800, mean +180, 5/10 positive, range
−14,450 … +9,450.

**Exp's six wins are split 3 USA-slot / 3 Russia-slot** — no slot concentration, as on 260922
and unlike 260905. **The ladder metric has not moved in two corpora**: Exp is still behind
Stable on median capture-gross and still leads capture in 4/10. **The win rate moved and the
ladder metric did not** — which is the shape you expect when the win rate is noise.

### S2 combat (`s2_exp`), mirrored — ladder metric `kills_cost − deaths_cost`

| # | Seed | Exp slot | Winner (bot) | Exp cap-gross | Sta cap-gross | Cap Δ | Exp swing | Sta swing | Swing Δ |
|---|------|----------|--------------|--------------:|--------------:|------:|----------:|----------:|--------:|
| 1 | 1017 | Russia | **Exp** | 91,148 | 88,285 | +2,863 | −23,125 | −33,875 | +10,750 |
| 2 | 2017 | USA | Stable | 81,856 | 100,002 | −18,146 | −4,625 | −44,125 | +39,500 |
| 3 | 3017 | Russia | **Exp** | 100,361 | 83,804 | +16,557 | −8,575 | −36,625 | +28,050 |
| 4 | 4017 | USA | Stable | 81,969 | 101,673 | −19,704 | −38,575 | −17,625 | −20,950 |
| 5 | 5017 | Russia | Stable | 89,724 | 89,588 | +136 | −46,075 | −7,875 | −38,200 |
| 6 | 6017 | USA | Stable | 91,527 | 90,525 | +1,002 | −37,750 | −14,400 | −23,350 |
| 7 | 7017 | Russia | **Exp** | 100,675 | 80,543 | +20,132 | −30,900 | −41,750 | +10,850 |
| 8 | 8017 | USA | Stable | 79,218 | 97,073 | −17,855 | −17,200 | −35,500 | +18,300 |
| 9 | 9017 | Russia | Stable | 67,677 | 100,929 | −33,252 | −25,050 | −31,850 | +6,800 |
| 10 | 10017 | USA | **Exp** | 95,921 | 85,844 | +10,077 | −24,950 | −38,800 | +13,850 |

**Exp win rate 4/10.** Win sequence: `W L W L L L W L L W`, split **3 Russia-slot / 1
USA-slot**. **Ladder metric: mean swing Exp −25,682.5 / Stable −30,242.5 → mean Swing Δ
+4,560; median swing Exp −25,000 / Stable −34,687.5, and the median of the per-match Swing Δ is +10,800 — **not** the difference of those two medians, which is not an additive statistic; 7/10 positive,
range −38,200 … +39,500.** Capture-gross median Exp 90,436 / Stable 90,056 (mean 88,008 /
91,827); Exp leads capture 6/10, Cap Δ median +569, mean −3,819.

**The win rate and the ladder metric point opposite ways here, and that is worth stating
plainly: Exp lost this batch 4–6 while leading the ladder metric on mean (+4,560), median
(+10,800) and count (7/10).** Both readings are inside the cal band; neither wins the argument.
Note also the exp batch's *faction* sequence is `R R R R U R R R U U` — Russia won 7 of 10
regardless of which bot occupied the slot — on the same map and seeds where the cal batch gave
USA 8–2. That is the same instability the calibration section documents, seen from the other
side.

## Side by side — all three corpora

**Read this as three different opponents, not as one change measured three times.** `@stable`
moved at each step (item 86 into 260922, item 64 into 260923, plus anything else that landed
in the gaps), and the host changed at the first step.

| | 260905 (`bb89f9fd`) | 260922 (`ef7362a7`) | 260923 (`55df64e2`) | |
|---|---|---|---|---|
| Host | Windows | macOS | macOS | 22→23 is host-clean |
| Caps (S1/S2) | 300 / 600 s | 900 / 2200 s | 900 / 2200 s | identical 22→23 |
| **S1 cal win split** | USA 5 – Rus 5 | USA 6 – Rus 4 | **USA 7 – Rus 3** | 1 seed flipped 22→23 |
| **S2 cal win split** | USA 5 – Rus 5 | USA 6 – Rus 4 | **USA 8 – Rus 2** | 4 seeds flipped 22→23 |
| S2 cal capture bias | **Russia leads 8/10**, median ≈ +4,700 | USA leads 5/10, median +3,533 | **USA leads 8/10**, median +4,614 | three different answers |
| S1 cal swing noise band | −8,500 … +8,500 | −2,500 … +10,900 | **−14,400 … +14,050** | widening |
| S2 cal swing noise band | −17,050 … +46,300 | −28,550 … +36,550 | **−40,650 … +53,000** | widest yet |
| **S1 Exp win rate** | 3/10 | 5/10 | **6/10** | |
| S1 cap median Exp / Sta | 26,689 / 27,482 | 27,207 / 27,721 | **27,234 / 27,662** | Exp behind in all three |
| S1 Exp leads capture | 3/10 | 4/10 | **4/10** | flat |
| S1 Swing Δ median / mean | +600 / −2,850 | +150 / +790 | **+800 / +180** | flat |
| **S2 Exp win rate** | 2/10 | 6/10 | **4/10** | non-monotone |
| S2 cap median Exp / Sta | 83,833 / 94,024 | 91,801 / 88,260 | **90,436 / 90,056** | deficit stays closed |
| S2 Exp leads capture | 2/10 | 6/10 | **6/10** | holds |
| S2 mean swing Exp / Sta | −26,790 / −29,810 | −21,752.5 / −32,617.5 | **−25,682.5 / −30,242.5** | |
| S2 Swing Δ median / mean | +7,300 / +3,020 | +17,825 / +10,865 | **+10,800 / +4,560** | non-monotone |

### What changed

**Almost nothing that is legible at this sample size.** The honest list:

- **The S2 capture deficit that decided the 260905 corpus remains closed.** 260905's core
  finding was that `@experimental` loses S2 because it trails `@stable`'s gross capture income
  in 8 of 10 games by a median ≈ 10,200. Across two corpora since, Exp leads capture 6/10 with
  a near-zero median gap (+3,633, then +569). **This is the one cross-corpus reading that has
  held for two builds in a row** — and it is a statement about the *metric*, not about who won.
- **S1 is flat and has been flat for three corpora.** Exp's win rate reads 3 → 5 → 6, but the
  ladder metric is motionless: Exp is behind Stable on median capture-gross in all three, and
  leads capture in 3, 4, 4 of 10. **Treat S1 as unchanged, again.**
- **The S2 win rate is non-monotone: 2 → 6 → 4.** A quantity that goes up then down by four
  then two, on a rung where identical bots split 8–2, is not measuring anything this corpus can
  name. **Do not draw a line through those three points.**
- **The calibration noise bands widened on both rungs** and have now widened at every step. The
  instrument is not becoming more precise as corpora accumulate.

### What cannot be concluded from this

**Not that item 64 caused any of it.** Item 64 is the *reason to re-measure*, not a variable
this corpus isolated. There is no arm in which item 64 is off — both bots carry both levers.
**This card is a new control, not an A/B.** The one A/B that exists for item 64 is the verdict
scenario `test-combined-arms-rendezvous`, run `260922_211126`, which is a single-seed
mechanism test and says nothing about match outcomes.

**Not that `@experimental` is behind `@stable` on S2 (4–6), and not that it is ahead on S1
(6–4).** Both sit inside a band in which two identical copies of `@stable` produced an 8–2.

**Not that the 260922 "parity" reading is confirmed, and not that it is refuted.** It is
re-measured against a different opponent and comes out mixed: better on S1's win rate, worse on
S2's, with both ladder metrics roughly where they were. **Parity remains the only statement the
instrument supports.**

**Not two independent confirmations.** Per the instrument caveat, S1 and S2 are the same 20
games read at two ticks — and this corpus shows their *verdicts* diverge in 3–4 of 10, so they
are neither independent nor redundant. They are one sample, scored twice, disagreeing.

**And the S2 swing metric is still biased.** PIPELINE item 87 stands: the doom model charges
the finishing blow to the victim and credits it to nobody, biasing the S2 swing ≈ $4,490/match
against whichever bot dies more. **The S2 mean Swing Δ here is +4,560 — the same magnitude as
the known bias, uncorrected.** That number should not be quoted as an Exp edge.

## Core finding

**On the new control, `@experimental` is at parity with `@stable` — 6/10 on S1 eco, 4/10 on S2
combat — and nothing in this corpus separates the two bots.** The S2 capture deficit that
decided 260905 is still closed (Exp leads capture 6/10, Cap Δ median +569) and S1's ladder
metric is unmoved for the third corpus running.

**The dominant fact on this page is the calibration, not the comparison. Two byte-identical
copies of `@stable` split 8–2 by side on S2 and 7–3 on S1, on the same ten seeds that split
5–5/5–5 and 6–4/6–4 against the two previous builds — with seven of ten seeds changing which
side wins across the three.** That bounds every Exp-v-Stable statement this instrument can make
at N=10: **a split of 8–2 or better, in either direction, carries no information.** Both of
today's exp splits are inside that, as was 260922's 6–4.

**This corpus is the control for everything measured after `55df64e2`.**

## Watch — what this corpus does not establish

- **`kills_cost − deaths_cost` is not zero-sum here, same as both prior corpora.** Both players
  are net-negative in **35 of 40** matches (75 of 80 player rows). The five positive rows are
  `s1_cal` m2 Russia (+150) and m9 USA (+1,750), `s1_exp` m6 Russia/stable (+750) and m7
  Russia/exp (+650), `s2_cal` m7 USA (+2,825). Cause and consequences are settled:
  `WORKSPACE/audits/260906-baseline-deaths-audit.md`; the open decision is PIPELINE item 87.
  **Absolute swing levels are not a combat scoreboard.**
- **No build is recorded for `55df64e2` before the batches.** `/tmp/queue44.sh` *pre-flights*
  `engine/bin/OpenRA.dll` and refuses with exit 3 if absent, and it additionally waits for
  `/tmp/merge-gate20.log` to report `PUSH rc=0` before starting — so the merge gate (which does
  build) completed first. That is circumstantial, not a freshness check. **HYPOTHESIS: the tree
  was built at `55df64e2`.** Disproof: rebuild and re-run one seed; deterministic sim,
  byte-identical `match_1.json` settles it.
- **One map for the whole corpus.** All six scenarios share one `map.bin` and one `rules.yaml`.
  Nothing here generalises across maps.
- **N=10, and effectively 20 games rather than 40.** Every per-game headline above is inside the
  calibration bands.
- **The side-split instability is unexplained.** Seven of ten S2 cal seeds changed which side
  wins across three builds of an identical-bot matchup. This card documents it and does not
  diagnose it. **It is the single most damaging fact for anything that wants to read a win
  split off this instrument**, and the obvious next step — more seeds on the cal rung, not more
  exp batches — is unrun. At ~21 min/match on S2 that is ~3.5 h per additional 10 seeds.
- **The host question from 260922 is still open**, though it no longer affects the 22→23
  comparison, which is host-clean.
- **`@stable` moved and nobody re-took a `@stable`-vs-old-`@stable` reading.** There is no
  measurement of how much item 64 changed `@stable` itself. Note the cal rung would be the
  instrument for exactly that, and the seed instability above says it would need far more than
  10 seeds to resolve.
- **The 260922 card's "S1 and S2 winner sequences are identical" and "63,000 POI ceiling"
  claims are contradicted here** (see the instrument caveat). Both were true of that batch's
  data and false as general statements; **neither affects that card's ladder numbers.** The
  260905 S2 spawn-asymmetry warning now has three mutually inconsistent readings across three
  corpora (Russia 8/10, USA 5/10, USA 8/10) and should be treated as unsettled, not as a fact
  about the map.

**Ref stamp:** stamped and code `55df64e2` (`git_dirty: false`, verified in all four
`batch.meta.json`), run 2026-09-22 20:53:27 → 2026-09-23 05:57:20 UTC (22:53 → 07:57 local).
Recorded from `/Users/fredrik/worktrees/ww3mod/rebaseline-260923` on branch
`wt/rebaseline-260923`, branched from `main @ 55df64e2`.
