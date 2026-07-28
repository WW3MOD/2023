# RUN PLAN — Stage-F re-baseline + item-8 gate (b) ambush pricing (2026-07-28)

**Status: PLAN ONLY, NOT RUN.** This is the executable procedure for the DECLARED
Stage-F re-baseline (`260724_stagef_repoint_rebaseline.md`) plus the item-8 gate (b)
ambush-default-on pricing. No matches were run for this card. Authored against
`main @ 0b0783be`.

---

## Two gating preconditions (BOTH must hold before ANY match below runs)

The ladder in this card is **blocked** until both fire. State of each is manager-owned;
do not start §3 until both are TRUE.

- **(a) Calibration runs finished in this checkout.** Another worker is running the
  **Stable-vs-Stable calibration** (`tournament-s1-eco-cal-nn`,
  `tournament-s2-combat-river-zeta-cal-nn`) on this shared checkout. Those runs
  produce the NEW calibration medians this re-baseline **consumes as the yardstick**
  (see "Why calibration MUST be re-run" below). Do not launch until they are done —
  concurrent games steal window focus and thrash the shared `debug.log`.
- **(b) Item 28 (path string-pulling) merged to `main`.** Item 28 changes **all
  vehicle trajectories** → another global break of benchmark byte-identity. Running
  the re-baseline before it merges would zero the instrument to a state that item 28
  immediately invalidates. Rebase this checkout onto post-merge `main` first, then run.

Also honor the CLAUDE.md hard rule: this is a multi-match batch → needs the user's
explicit goahead for the autoburn window (STANDING GRANT, PIPELINE item 25) to be
active. Do not autonomously start.

---

## Why this re-baseline is BROADER than the 260724 card says (drift found)

The `260724_stagef_repoint_rebaseline.md` card asserts: *"`@stable`, Normal, Rush,
Turtle are byte-identical … their numbers carry over unchanged and remain the fixed
yardstick."* **That claim is now STALE.** Since 2026-07-24 two items intentionally
broke benchmark byte-identity **globally (humans + all bots, including `@stable`)**:

- **Item 26 (merged `fc9fe396`)** — `DensityModifiesDamage` + superlinear forest
  ground-shadow. PIPELINE entry itself flags: *"Global by design (humans+bots) →
  item-25 re-baseline required before trusting any bot-improvement claim."* `@stable`
  takes forest cover-damage reduction and deeper concealment → its behavior moved.
- **Item 28 (pending merge, gate (b) above)** — path string-pulling, all vehicle
  trajectories change → `@stable` vehicle routes move.

**Consequence:** the `@stable` yardstick is no longer the same instrument it was at
`260724`. The re-baseline can NOT carry over old `@stable`/calibration numbers — it
must re-zero **both** the Stable-vs-Stable calibration (the yardstick) **and** the
Exp-vs-Stable baseline, on the post-26/28 instrument. This is precisely what gate (a)
provides: the other worker re-runs the calibration; this card consumes it.

## Executability verification vs `main @ 0b0783be` — GREEN, no script/scenario drift

| Referenced by the 260724 card | Exists now? |
|---|---|
| `tools/autotest/run-tournament.sh` (`--seeds`, `--mirror`, `--config`, `--max-wall-secs`) | ✅ all flags present |
| `tools/autotest/aggregate-tournament.sh` (auto-called on ≥1 verdict) | ✅ |
| `tournament-s1-eco-river-zeta` + `-mirror` | ✅ both, `tournament-eco-5min.yaml`, `TimeLimitSeconds 300` / `SpeedMultiplier 8` |
| `tournament-s2-combat-river-zeta` + `-mirror` | ✅ both, `tournament-combat-12min.yaml`, `TimeLimitSeconds 720` / `SpeedMultiplier 8` |
| `tournament-s1-eco-cal-nn`, `tournament-s2-combat-river-zeta-cal-nn` | ✅ both, Matchup `stable`-vs-`stable` |
| `StrategicRepointEnabled: true` + sub-multipliers | ✅ `ai.yaml:239` (boost 150 / damp 60 / danger 100·60·20) |
| `[exp-terr] repoint / axis-shift / reeval` markers | ✅ `PoiOffensiveBotModule.cs:633/645/647` |
| `LaneAmbushBotModule@experimental` (gate-b subject) | ✅ `ai.yaml:302`, `MaxAmbushes 2 × UnitsPerAmbush 2` |
| `[exp-ambush] reeval / lane / retire` markers | ✅ `LaneAmbushBotModule.cs:357/360/499` |

**Minor drift (non-blocking):** `tournament-s1-eco-cal-nn/description.txt` still reads
"experimental vs Normal" and the S2 cal `description.txt` reads "Normal vs Normal",
but both scenarios' `tournament-*.yaml` `Matchup:` are `stable`-vs-`stable` (the
config is authoritative — `BotVsBotMatchWatcher` reads `Test.TournamentConfig`). Stale
description text only; calibration IS Stable-vs-Stable. No action needed for the run.

---

## The measurement design

**Two questions, one shared arm.** Arm A (repoint ON + ambush ON = `main` as-shipped)
serves BOTH the re-baseline *and* the "ambush ON" side of gate (b). Only the "ambush
OFF" arm (B) is extra work.

- **Arm A — `main` as-is:** `@experimental` (`StrategicRepointEnabled: true`,
  `LaneAmbushBotModule@experimental` active) vs `@stable`. This is the **new
  Exp-vs-Stable re-baseline** on the post-26/28 instrument.
- **Arm B — ambush OFF:** identical except `LaneAmbushBotModule@experimental` disabled.
  **Mechanism: mod-YAML edit only, NO rebuild** — flip `ai.yaml:303`
  `RequiresCondition: enable-ai-experimental` → `RequiresCondition: enable-ai-legacy-only`
  (a condition `@experimental` is never granted, so the module never instantiates).
  Mod rules are read at game load, so a relaunch suffices; `make all` is NOT needed.
  This edit is **uncommitted, local, reverted after arm B** — only this run plan is
  committed.

**Byte-identity of the yardstick across A/B:** `@stable` has NO ambush twin and never
instantiates `LaneAmbushBotModule` (ai.yaml comment, `:297-301`), so disabling the
module touches `@experimental` only. `@stable` is byte-identical between arm A and arm
B → both arms share the ONE calibration from gate (a). Arms A and B are intentionally
NOT byte-identical to each other (that delta IS the ambush signal); paired seeds
(same map/faction/spawn/opponent) give the clean A/B.

**Rungs.** The two live scenarios (S3 win-rate is TBD, not built): S1 eco + S2 combat,
each with its faction-swap mirror, N=10 (the `--mirror` flag alternates even=primary /
odd=mirror inside the N=10, so N=10 already spans both faction assignments).

---

## §3 Commands — exact order

Run from repo root. All hidden (default `--background`), all N=10. `--max-wall-secs`
set generously (≥ 4× the 300/720s clock at 8× ≈ enough that the watchdog never kills a
natural-length match — the paired model breaks if a match is culled early; S2 card R-2).

### Step 0 — preflight (after BOTH preconditions hold)
```
git rev-parse --short HEAD            # confirm post-item-28 main; record SHA in the result card
git status -sb                        # confirm clean except this run plan
# confirm the other worker's calibration result dirs exist + aggregated (gate a):
ls tools/autotest/tournament-results/ | grep -i cal-nn
```

### Step 1 — Arm A (re-baseline = repoint ON + ambush ON), `@experimental` vs `@stable`
```
# S1 eco (5-min clock, ~70s/match hidden)
./tools/autotest/run-tournament.sh tournament-s1-eco-river-zeta \
    --seeds 10 --mirror tournament-s1-eco-river-zeta-mirror \
    --max-wall-secs 150

# S2 combat (12-min clock, ~115s/match hidden) — generous wall cap per S2 card R-2
./tools/autotest/run-tournament.sh tournament-s2-combat-river-zeta \
    --seeds 10 --mirror tournament-s2-combat-river-zeta-mirror \
    --max-wall-secs 400
```
Record each `--result-dir` path printed. `aggregate-tournament.sh` runs automatically
→ `summary.json` (win split, per-side winrate, capture counts) + `summary.csv`.

### Step 2 — enter Arm B (ambush OFF), YAML edit only
```
# Edit mods/ww3mod/rules/ai/ai.yaml line ~303:
#   RequiresCondition: enable-ai-experimental   ->   RequiresCondition: enable-ai-legacy-only
# (uncommitted; reverts in Step 4)
```

### Step 3 — Arm B runs (same rungs, same seeds → paired)
```
./tools/autotest/run-tournament.sh tournament-s1-eco-river-zeta \
    --seeds 10 --mirror tournament-s1-eco-river-zeta-mirror \
    --max-wall-secs 150

./tools/autotest/run-tournament.sh tournament-s2-combat-river-zeta \
    --seeds 10 --mirror tournament-s2-combat-river-zeta-mirror \
    --max-wall-secs 400
```

### Step 4 — revert the arm-B edit immediately
```
git checkout -- mods/ww3mod/rules/ai/ai.yaml   # restore default-on; leaves only the run plan committed
git status -sb                                  # confirm clean
```

### Step 5 — firing proofs (per arm, from preserved per-match debug logs)
```
# repoint fired (arm A only; OFF for arm B is impossible — repoint stays on both arms):
grep -h "\[exp-terr\]" <result-dir>/match_*_debug.log | grep -E "repoint|axis-shift|reeval" | head
# ambush fired in arm A, ABSENT in arm B (the A/B proof):
grep -h "\[exp-ambush\]" <armA-result-dir>/match_*_debug.log | grep -E "reeval|lane|retire" | head
grep -c "\[exp-ambush\]" <armB-result-dir>/match_*_debug.log   # expect 0 in every arm-B match
```

---

## Expected wall-clock (hidden, 8×; no rebuild — arm B is YAML-only)

| Rung | Clock (sim) | ~per match | N | Subtotal |
|---|---|---|---|---|
| S1 eco (river-zeta + mirror) | 300s → 37.5s + cold-start | ~70s | 10 | ~12 min |
| S2 combat (river-zeta + mirror) | 720s → 90s + cold-start | ~115s | 10 | ~19 min |
| **Arm A subtotal** | | | 20 | **~31 min** |
| **Arm B subtotal** (same) | | | 20 | **~31 min** |
| edits + aggregation + log grep | | | | ~5 min |
| **TOTAL LADDER** | | | **40 matches** | **~65–70 min** |

Calibration (Stable-vs-Stable, S1+S2 N=10 each ≈ another ~31 min) is **NOT** in this
budget — it is gate (a), run by the other worker, consumed not re-run. If gate (a)
falls through and this card must run it too, add ~31 min → ~100 min total.

---

## Where numbers get recorded

1. **Raw** (git-ignored, harness-owned): `tools/autotest/tournament-results/<ts>_<scenario>/`
   — per-match `match_*.json`, `match_*_debug.log`, `summary.{json,csv}`, `batch.meta.json`.
2. **Cycle card** (committed, SPEC §8.3): `WORKSPACE/ai-bench/runs/260728_rebaseline_result.md`
   — distilled per-rung: git SHA of post-28 `main`; consumed calibration medians (S1 earned,
   S2 net-swing, from gate (a)); Arm A Exp-vs-Stable medians (S1 win split + capture X/10,
   S2 net-swing + sign X/10 + both-spawn + engaged X/10); Arm B same; the gate-(b) A−B delta
   and the verdict. Firing-proof grep counts inline.
3. **`LADDER.md`** — new "Current" standing line (post-26/28 instrument), superseding the
   `1eb644de`/`a88ef596` rows; note the instrument change so old rows aren't mis-compared.
4. **`REVIEW.md`** — one `LADDER` activity-log line (re-baseline done + gate-(b) verdict).
5. **`DOCS/reference/influence-stack.md` §Known gaps** — flip *"Stage-F benchmark
   re-baseline is DECLARED, NOT RUN"* → RUN, with the result-card SHA.

---

## Item-8 gate (b) — ambush default-on pricing design

**Question priced:** should `LaneAmbushBotModule@experimental` stay **default-on** for
the Experimental bot? It ships on today; gate (b) is the honest benchmark check owed
before that default is trusted.

**Arms / rungs:** Arm A (ambush ON) vs Arm B (ambush OFF), paired seeds, over BOTH
rungs (S1 eco + S2 combat, each + mirror, N=10). Ambush pulls ≤ `MaxAmbushes ×
UnitsPerAmbush` = 4 units off offense, so it is a **contact/combat lever** — S2 is the
signal rung, S1 is the regression guard (does peeling 4 units starve the economy race?).

**Metrics (unchanged from the ladder):**
- S2 = net combat swing `median(kills_cost − deaths_cost)` (Exp − opponent), sign-count
  X/10, both-spawn, validity `engaged ≥ 6/10`.
- S1 = win split + Exp capture X/10 + `resources_earned` median.

**Decision rule (tie goes to OFF — don't ship neutral complexity):**

- **KEEP default-ON** iff ambush is **net non-harmful on BOTH rungs**:
  - S2: `swing_A ≥ swing_B − noise` **AND** `engaged_A ≥ 6/10` (still a valid fight),
    ideally `swing_A > swing_B` (a measured ambush edge in $).
  - S1: no regression vs arm B beyond the floor — win-rate `≥ 0.40` and capture parity
    within `±2/10`, earned median not materially below arm B.
- **FLIP to default-OFF** (commit `ai.yaml` change gating the module off on
  `@experimental`) iff ambush **regresses either rung**: S2 swing measurably worse, OR
  S2 drops below the `6/10` engaged validity floor (ambush suppressing engagement), OR
  S1 falls through the floor.
- **Within-noise / neutral on both → default-OFF.** The module is already shipped-on,
  but its value is qualitative ("units feel alive"); the honest benchmark bar for
  *keeping the complexity on by default* is measured non-harm, and a pure wash does not
  clear it. (If the user prefers the qualitative value to win ties, that is a user call
  — record it, don't infer it.)

**What decides the gate:** the Arm A − Arm B delta on S2 (primary) with the S1
non-regression guard, both read from the §"Where numbers get recorded" cycle card.
The gate does NOT touch the promotion policy (SPEC §13) — this prices a default, it
does not promote `@experimental`→`@stable`.

**Carried OBS to watch in the logs** (from item-8 review, may confound a small delta):
FFA/2v2 anchor-set oscillation is N/A here (2p only); the ≤100-tick sprung-unit
re-task window (OBS-4) and detection-path spring no longer scenario-covered (OBS-D)
are bounded — flag if arm-A `[exp-ambush] retire … reason=` churn looks pathological.

---

## Guardrails

- **Shared checkout:** another worker is running game tests here. Do NOT touch
  `tools/autotest/scenarios/test-case01-forest-ambush/` or any running process; do not
  launch §3 while their calibration (gate a) is still running.
- **Never push.** Merges/commits local only. No attribution trailers.
- **Only artifact committed from this card is the result card + doc updates in
  §"Where numbers get recorded"** — the arm-B `ai.yaml` edit is reverted (Step 4),
  never committed.
