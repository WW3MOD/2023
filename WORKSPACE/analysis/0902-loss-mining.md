# Loss mining — @experimental recurring mistakes from existing artifacts

_Worked at `main` @ `26f9cec0` (branch `wt/loss-mining`). 2026-09-02. **No games were run**; this is
archaeology on artifacts already in the tree._

Corpus examined: all 43 dirs under `tools/autotest/tournament-results/` (222 match JSONs, 115
preserved `match_*_debug.log`), `WORKSPACE/ai-bench/` (LADDER, REVIEW, SPEC, runs), all 5
`WORKSPACE/benchmarks/` reports, `DISCOVERIES.md`, `bugs/discovered.md`, `pipeline/`, `recon/`,
`proposals/`.

---

## 0. Read this first: 218 of 222 match records are void

Item 43 (`WORKSPACE/pipeline/items/43-benchmark-rebaseline.md:3`) declares the whole `tournament-*`
suite **invalid, not stale** — `PlayerResources.Tick` gated income *and* upkeep on `Playable`, which
map-player bots are not, so both bots played with no economy past their opening 7 500.

**I verified this independently rather than taking it on trust.** `resources_earned` is exactly `0`
for every player-record in every run except one:

| Run | player-records | `resources_earned > 0` | Status |
|---|---|---|---|
| `260902_0641_tournament-arena-composition-2p` | 4 | 4 | **VALID** |
| all 25 other runs | 218 | 0 | VOID (no economy) |

The fix is in `main` now (`PlayerResources.cs:208`, `IsBot && !NonCombatant`, with a PITFALL comment).
So the usable outcome corpus is **two matches, one map, `@experimental` vs `@experimental`**.

Consequences, and they are severe:

- **There is no valid `@experimental`-vs-`@stable` number anywhere in this repo.** Every winrate in
  `benchmarks/`, `ai-bench/runs/`, and `recon/2607*` is void. I am not quoting any of them as a
  result, only as a *mechanism*.
- I initially computed a headline "exp trades at 0.33, stable at 0.83 across 80 matches" from the
  `item24_*`/`rebase_*` runs. **That number is void** and is not a finding. I record it only because
  its uniformity (exp 0.26–0.41, stable 0.60–0.91, in all 8 arms) is a *hypothesis generator* for
  §1.1, not evidence for it.
- `260811_1839_tournament-s1-eco-river-zeta` is v7 with the rich `unit_types` block **but is still
  void** (`resources_earned=0`). Its per-unit numbers must not be pooled with the valid run. An
  earlier draft of this analysis pooled them and produced a false "$4 750 of technicians wasted"
  finding — on the eco map those technicians are `ConsumedByCapture` against 8 captures, i.e.
  correct behaviour. Do not repeat that.

**Evidence tiers used below.** Every finding is tagged:

- **[T-A] outcome** — depends on economy/score. Valid corpus only: n = 2 matches. Weak on its own.
- **[T-B] path-exercise** — "this code path never fires / always takes one branch", read across all
  115 debug logs. Void economy starves these logs of units, so a path *not* firing is partly
  explained by there being less to do; but a path that never fires in 115 matches across 4 months and
  5 scenarios is still a strong inertness signal, and I say where each sits.
- **[T-C] static** — read from source, no run behind it.

---

## 1. Ranked findings

Ranked by **value at risk × confidence**, not by raw effect size, because effect size is mostly
unmeasurable here. I give my honest confidence for each.

### 1.1 — The army parks outside the ring it was sent to contest [T-B, high confidence]

**Observed.** In the valid corpus (2 matches, 12 401 log lines), `action=Pressure` is the **only**
offensive action ever taken — 341 of 341 `[exp-offense] axis` lines. And `distToTarget` on the
committed orders **never goes below 24 cells** (min 24, then 25, 27, 28, 29…).

**The number that makes this matter:** `SupplyRouteContestation.Range` is `10c0` — ten cells
(`mods/ww3mod/rules/ingame/structures.yaml:303`, and the matching `WithRangeCircle@Contestation` at
`:299`). The bot's sole offensive action is to send force to *pressure* an SR, and it stops at more
than **twice the radius at which pressure has any effect**. The contestation bar cannot move.

Across the full 115-log corpus the same shape holds: 15 848 `action=Secure` + 3 555
`action=Pressure`, and the order-distance histogram peaks at 10–19 with a large 20–29 shoulder.

**Hypothesized cause (not established):** `SrPressureScoreMultiplier: 260` (`ai.yaml:534`) funnels
the pool onto a single SR-pressure axis, and the standoff anchor is set from weapon range rather than
from the contestation radius. `DISCOVERIES.md:10580` records the same mechanism being observed on
2026-08-03: *"92 of ~100-108 units (~85-92%) on the single held `supplyroute@79,34` Pressure axis"*,
distance *"never drops below ~26"*.

**HANDED OFF — do not implement here.** This is `wt/coord-assault` / `wt/flanking` territory
(standoff anchors, `SrPressureScoreMultiplier`, `UnitsPerAxis`). Flagging because it is, on this
evidence, the single largest behavioural defect in the bot, and because the *specific* framing
"standoff distance is 2.4× the contestation radius, so pressure is geometrically incapable of
working" may not be how those workers currently have it scoped.

### 1.2 — Over half of everything bought is destroyed, and ~60% of it is not credited to the enemy [T-A, medium]

**Observed**, valid corpus, 4 player-records:

- Total produced: **$90 000**. Total `deaths_cost`: **$49 150** — **55% of all spend destroyed** in
  9 000 ticks, with `captures_count = 0` on every side. Aggregate trade ratio **0.39**.
- The books do not balance. USA-bot lost $14 350 while Russia-bot was credited $4 500 of kills.
  Summed both ways, **~60% of losses are not attributed to any enemy kill.**

**Hypothesized causes (untested, mutually compatible):** crew ejection double-counting (a dying tank
spawns 3 crew that then die — see 1.4); self-inflicted//environmental damage; or `kills_cost` and
`deaths_cost` simply being accumulated over different actor sets in
`BotVsBotMatchWatcher`/`MatchTypes`. **Nobody has looked at this** — it is not mentioned in any doc I
read. Cheapest next step is a static reconciliation of the two accumulators, no game needed.

I did not fix this: identifying *which* accumulator is wrong requires either a run or a careful
read of both paths, and a wrong "fix" here silently corrupts every future benchmark.

### 1.3 — Aircraft are bought one at a time and die every single time [T-A, medium-low; n is small]

**Observed**, valid corpus: `littlebird` **2 produced / 2 lost ($6 000)**, `halo` **1/1 ($2 000)**.
Zero survivors. Together **8.9% of total spend** for no measurable return.

The same one-at-a-time-then-dead shape covers the heavy armour: `abrams` **2/2 ($5 000)**, `t90`
**2/2 ($4 800)**, `btr` **5/5 ($3 000)**. Never bought in pairs, never survives.

**Explicitly NOT established:** *why* they die. There is no killer/killed pairing, no engagement
record, and no terrain tagging anywhere in the artifacts (`260729-exp-deficit-attribution.md` §4
calls the analogous forest hypothesis *"untestable with surviving artifacts"*). Anyone "fixing" air
survivability from this data is guessing. n = 3 aircraft.

**HANDED OFF** — overlaps the composition worker's remit (`UnitBuilderBotModule` weights, buy
quantities) and PIPELINE 64 "first tank attacks alone".

### 1.4 — Ejected crew is 100% loss, every time [T-A, medium; small money]

**Observed**, valid corpus: `crew.driver.russia` **6/6 lost**, `crew.commander.america` 2/2,
`crew.driver.america` 2/2, `crew.gunner.america` 2/2. Every crew figure that ever existed died.
Direct cost is small ($1 200 in the valid corpus) but it inflates `units_dead` and is a candidate
explanation for the unattributed attrition in 1.2.

`[exp-crew] sweep … evac=1 banked=N` exists and fires, so there *is* an evacuation path; it recovered
1 unit per sweep in the logs where it appears. Whether 100% loss means the sweep is under-triggered
or simply that crews spawn inside a lost fight is **not determinable from these artifacts**.

Adjacent, already assigned: `wt/evac-refund`.

### 1.5 — Forward staging never advances [T-B, high confidence that it is inert; unknown cost]

**Observed:** `[exp-staging] advance=none advanced=0/0` on **58 of 58** samples in the valid corpus,
and on **93 of 93** across the entire 115-log corpus. The field has never held any other value in any
preserved artifact. Units accumulate at the staging anchor (`idle=7 staged=2` at the extreme) and the
advance branch is never taken.

**HANDED OFF** — `StagingStandoffCells: 6` (`ai.yaml:684`), forward-muster and staging anchors are
`wt/coord-assault`'s.

### 1.6 — Empty transports [T-B, medium]

**Observed**, corpus-wide: `[exp-transport] passengers-in-reserve=0` appears **16 339** times against
**3 059** for `=1`. Carrier lines read `bradley@… idle=True activity=<none> pax=0 task=False → OK`
thousands of times. In the valid corpus, `bradley` (2 produced, $3 000) and `bmp2` (3, $3 900) are
7.7% of spend and largely function as empty vehicles.

**Hypothesized cause:** the pickup window is a 14-cell bubble round the SR scanned every 50 ticks
(`ReserveZoneRadiusCells`, `MountedTransportBotModule.cs`), and infantry claimed by other modules on
spawn is never eligible — from `recon/260730-streak-levers.md` §Q1. Not verified by me.

**HANDED OFF** — composition worker owns APC buy quantities; transport radius is adjacent.

### 1.7 — Corrections to claims already in the tree

Two things I checked that turned out **not** to hold, recorded so nobody re-derives them:

- **"Zero retreats, ever."** False for the valid corpus. `[exp-retreat]` fires 5 times in 2 matches,
  including two real `fallback` orders (`own=116 enemy=350 safe=False` → `rally=6,16 units=3`). The
  retreat FSM works. What *is* true \[T-C\]: `RetreatDamperEnabled = false` by default
  (`PoiOffensiveBotModule.cs:714`), so the oscillation damper is inert — a different claim.
- **`retire reason=dropped` is the only retire reason in all 889 retires corpus-wide** \[T-B\]. The
  bot never breaks off an axis for danger or losses, only because the target left the list. This is
  real and I could not find it recorded anywhere. It sits inside `PruneAxes`, which the collision map
  assigns to `wt/coord-assault` — **coordinate before touching**.
- `postureEvals=0` on **109 of 109** retire lines that carry the field \[T-B\] — posture is never
  evaluated on the retire path.

---

## 2. Unit-composition waste — valid corpus only

2 matches, 4 player-records, exp-vs-exp, `tournament-arena-composition-2p`, $90 000 total spend.
**n is 2. Treat every row as directional.** This is nonetheless the *first* per-unit-type loss data
in the project's history — both `260729-exp-deficit-attribution.md` §1 and the ai-bench corpus state
flatly that *"loss-by-class is not recoverable"*. Verdict v7's `unit_types` block changed that, and
nothing had yet been read out of it.

| type | prod | $prod | lost | $lost | alive | loss% | % of spend |
|---|---:|---:|---:|---:|---:|---:|---:|
| `truk` | 10 | 10 000 | 7 | 7 000 | 3 | 70% | 11.1% |
| `littlebird` | 2 | 6 000 | 2 | 6 000 | 0 | **100%** | 6.7% |
| `abrams` | 2 | 5 000 | 2 | 5 000 | 0 | **100%** | 5.6% |
| `t90` | 2 | 4 800 | 2 | 4 800 | 0 | **100%** | 5.3% |
| `lccv` | 4 | 12 000 | 1 | 3 000 | 0 | 25% | 13.3% |
| `btr` | 5 | 3 000 | 5 | 3 000 | 0 | **100%** | 3.3% |
| `halo` | 1 | 2 000 | 1 | 2 000 | 0 | **100%** | 2.2% |
| `ar.america` | 23 | 2 300 | 17 | 1 700 | 6 | 74% | 2.6% |
| `at.america` | 5 | 1 500 | 4 | 1 200 | 1 | 80% | 1.7% |

Notes that stop this table being misread:

- **`lccv` is not waste.** 4 produced, 1 lost, 0 alive — the missing 3 became `logisticscenter`
  (3 produced, 0 lost, 3 alive, $9 000). It is a deploy transform, and the ledger is consistent.
- **`truk` at 70% loss is the largest single waste line** ($7 000, 11% of spend). A truck kills
  nothing, so this is close to pure loss. Corroborates `bugs/discovered.md:1464` (trucks desired
  while broke). **Composition worker's** — flagged, not touched.
- **AA against nearly no air:** Russia bought `tunguska` ×2 + `aa.russia` = $3 700 (17.6% of that
  bot's spend) against one enemy `littlebird`. 0 lost — they were never threatened. n = 1 bot.
- Infantry loss rates cluster at 41–80% and are *not* obviously anomalous for infantry.

---

## 3. What I changed

Both changes are **telemetry only**: no bot behaviour, no scorer, no win rule, no RNG, no sim state.
Nothing here needs an `enable-ai-experimental` gate because nothing here can change a game — and
correspondingly, neither profile moves.

I chose these over a behavioural tweak deliberately. The uncontested behavioural candidates
(`AbandonWhenArmamentsPaused`, `EngageAtLongestArmamentRange`) are **[T-C] code-read hypotheses** —
the proposal doc that raised them says outright they *"must be measured, not reasoned"*, and I cannot
run a game. Shipping an unmeasured behavioural change that also moves `@stable` is exactly the
failure this project keeps recording.

### 3.1 `client_index` is not a player identity — verdict v8

`engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs`

A map-player bot has no client, so `Player.cs:191` hands it **the host's** index — `0` — for every
such player (upstream's own `// TODO: fix this`). Both bots in every tournament match therefore
serialize `"client_index":0`, and `winner_client_index` matches **every** player and none of them.

This is not theoretical: joining on it is the first thing an analyst does, and doing so **reported a
100% win rate for both bots simultaneously** in this very investigation. It is the same shape as the
`Playable` pitfall already documented at `PlayerResources.cs:201-208` — a lobby-slot field read as
participant identity.

- Added `winner_player_index` and per-player `player_index` (positions within `players`, which is the
  identity that actually exists). Bumped `verdict_version` 7 → 8.
- Kept `client_index` / `winner_client_index` for schema stability, now documented at both the header
  and the emit site as must-not-join-on.
- Nothing in the tree gates on `verdict_version` (only a docstring in `parse-s2-batch.py` mentions
  it), so the bump is safe.

### 3.2 Removed the same trap from the aggregator

`tools/autotest/aggregate-tournament.sh` read `winner_client_index` into a `winner_idx` variable in
two places and then — correctly — joined on `winner_name` instead, leaving the bad read sitting there
as a loaded gun for the next editor. Both removed, replaced with a one-line note on why the join is
by name.

**The aggregator's existing output was never wrong**; this removes a trap, it does not fix a
corruption. Said plainly so nobody re-baselines thinking numbers changed.

---

## 4. Handed off to the manager, not implemented

| Finding | Why not me | Owner |
|---|---|---|
| 1.1 standoff ≫ contestation radius | Needs anchor/axis redesign | `wt/coord-assault`, `wt/flanking` |
| 1.3 air + heavy armour bought singly | Buy quantities | `wt/composition` |
| 1.5 staging never advances | `StagingStandoffCells`, muster | `wt/coord-assault` |
| 1.6 empty transports | Radius + buy quantity | `wt/composition` |
| 1.7 `retire reason=dropped` only | Sits in `PruneAxes` | `wt/coord-assault` — coordinate |
| 2. `truk` 70% loss | Economy/buy logic | `wt/composition` |
| 1.2 unattributed attrition | Needs accumulator audit | unowned — **recommend picking up** |

---

## 5. What I could not verify

- **Everything in §1 outcome-tagged [T-A] rests on 2 matches on one map, exp-vs-exp.** There is no
  valid exp-vs-stable data in existence. Nothing here should move a roadmap on its own.
- **No artifact records *why* any unit died** — no killer/killed pairing, no terrain or cover
  tagging, no engagement log. Every causal claim in §1 is labelled hypothesized for this reason.
- **[T-B] path-exercise findings are drawn mostly from void-economy logs.** A branch not firing is
  partly explained by a starved bot having less to do. The two findings I lean on hardest (1.1
  distance floor, 1.5 staging) both also hold *within the valid corpus*, which is why they are ranked
  where they are; 1.6 and the 889-retire count do not have that support and are ranked lower.
- **I did not read the 5 `WORKSPACE/benchmarks/` reports or `ai-bench/LADDER.md` myself** — those
  came via subagents. I verified their two load-bearing claims (item 43's void, the economy fix in
  `main`) directly against source and data, but their finer citations are second-hand.
- **My own build is the only check on §3.** No test exercises `SerializeVerdict`'s new fields, and I
  ran no game, so the v8 output shape is verified by compilation and by reading, not by an artifact.
