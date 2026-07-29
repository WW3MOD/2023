# case-01 forest-ambush — bar-ADJUST data mining

**Purpose:** sharpen the bar-ADJUST recommendation for case-01 (CALIBRATING) by extracting
everything the existing calibration-batch artifacts hold. Analysis only — no new runs.
**Scope of data:** the 6-seed batch (seeds 1001–6006) run 2026-07-28, scenario
`tools/autotest/scenarios/test-case01-forest-ambush/`, measured against `main` @ `57d88a74`.
**Mined against:** `main` @ `b3d5d7e1` (docs only; scenario Lua/harness unchanged since the batch).

---

## 1. Data richness found — THIN (verdict-level, and mostly already transcribed)

The harness does not archive per-seed data. `run-test.sh` writes a single
`~/.ww3mod-tests/result.json` and `rm -f`s it at the top of every run (line 293);
`run-batch.sh` calls `run-test.sh` sequentially, so each seed clobbers the previous
verdict. The per-seed metrics the Lua emits with `print()` go only to the engine
`debug.log` (`~/Library/Application Support/OpenRA/Logs/debug.log`), which is **already
overwritten** — a benchmark ladder has been writing it since (mtime 2026-07-29 03:40).

What actually survives on disk:

| Artifact | Content | Per-seed? |
|---|---|---|
| `~/.ww3mod-tests/result.json` | verdict note for seed **6006 only** (last run) | 1 of 6 |
| `~/.ww3mod-tests/screenshots/*test-case01*/manifest.json` (14 dirs) | pre-combat `settled-in-grove` screenshot at tick ~251 — **no verdict, no timing** | none |
| case file status-log table | def/att loss, att killed, resolved — **hand-transcribed** | 6 of 6 |
| `WORKSPACE/DISCOVERIES.md` (2026-07-28) | mechanism + COMPACT-variant comparison | narrative |

The surviving `result.json` (seed 6006) reads
`defLoss=0 attLoss=300 ratio=300.00 survDef=5/5 survAtt=2/5 sprang=true refined=5/5 resolved=false t=90.0s`
— it corroborates the transcribed 6006 row exactly (3 attackers dead, 0 defenders), which
gives confidence the hand-transcribed table is faithful. But the raw verdicts for the other
5 seeds are gone.

**Critically, the case-01 Lua never emitted per-unit events at all.** It polls `liveCount`
each tick and, at resolution/deadline, prints one aggregate `RESULT` line (final loss counts,
elapsed `t`, a `sprang` boolean, `refined` count). There are **no kill timestamps, no
time-to-first-shot, no shots-fired-at-defenders, no damage events** anywhere in the scenario —
so those metrics are *not lost, they were never measured*. Analysis points 1 (timing), 4
(kill-curve), and part of 3 (return-fire counts) are therefore bounded by what the aggregate
verdict can support, supplemented by the mechanism already established in DISCOVERIES.

---

## 2. Per-seed table (all that exists)

| seed | def loss (cr) | def killed | att loss (cr) | att killed | resolved | verdict-elapsed |
|------|------|------|------|------|------|------|
| 1001 | 0 | 0/5 | 400 | 4/5 | no  | 90.0s (deadline) |
| 2002 | 0 | 0/5 | 300 | 3/5 | no  | 90.0s |
| 3003 | 0 | 0/5 | 500 | 5/5 | **yes** | **83s** |
| 4004 | 0 | 0/5 | 400 | 4/5 | no  | 90.0s |
| 5005 | 0 | 0/5 | 200 | 2/5 | no  | 90.0s |
| 6006 | 0 | 0/5 | 300 | 3/5 | no  | 90.0s (result.json) |
| **agg** | **0** | **0/30** | **mean 350** | **3.5/5** | 1/6 | — |

Per-unit timing columns (ttf-shot, kill ticks) are intentionally blank: never emitted.

---

## 3. Variance analysis

**Defender loss — zero variance.** 0/30 defenders died across the batch. σ = 0. This is the
rock-solid axis.

**Attacker loss — WIDE, not tight.** Kills per seed = {4, 3, 5, 4, 2, 3}.

- mean 3.5 kills (350cr), median 3.5, **range 2–5 (spread of 3 kills / 300cr)**
- population σ ≈ 0.96 kills (~96cr); sample σ ≈ 1.05 kills (~105cr); CV ≈ 27–30 %
- distribution: 2×3-kills, 2×4-kills, 1×2-kills, 1×5-kills — dispersed, no tight mode
- standard error of the batch mean ≈ 105/√6 ≈ **±43cr (1 SE)**
- **observed per-seed floor = 2 kills (200cr)**

Implication for the bar: ±1 kill (±100cr) is ordinary seed-to-seed noise, ±1.5 at the tails.
Any attacker-side threshold must sit clear of that noise band. A per-seed "≥ 200cr always"
bar would rest exactly on the observed floor (zero headroom → brittle). The discriminating
teeth belong on the zero-variance **defender** axis.

---

## 4. Why defenders never die + kill-curve

**Why defenders never die — DETECTION ASYMMETRY, not a fair-fight cover/first-strike win.**
This is answerable from existing evidence despite the missing return-fire counts:

- The concealed defenders read attacker-visibility = 1 at their cell, below
  `Detectable.Vision 3`, so **the attackers cannot acquire them as targets** → ~0 return fire
  (case file status log; DISCOVERIES 2026-07-28).
- The discarded COMPACT-clearing variant that let attackers detect defenders at ~5c **flipped
  the result: defenders LOST on 2 of 3 seeds** (ratio 0.33 / 0.50). `DensityModifiesDamage`
  (≤20 % cut) + Ambush first-strike do **not** win a symmetric close brawl.

So "defenders never die" = "attackers never acquired a defender," **not** "the concealment gate
+ cover won a fair fight." The whole margin is non-detection. This is the single most important
input to the bar: the invariant under test is *the attacker's inability to see the defender*,
which is binary and near-noiseless — exactly what a discriminating bar should gate on.

**Kill-curve shape — not directly measurable; inferable as early-burst-then-stall.** No per-kill
ticks were logged. Two surviving signals constrain the shape:

- Only **1/6 seeds fully resolved** (all 5 attackers dead), and that one took **83s** of the
  90s window — i.e. even a full wipe is slow.
- DISCOVERIES records the mechanism: the lead attackers die crossing the exposed clearing
  (the ambush burst), then *surviving attackers halt at the wall's south face out of the
  defenders' effective range and the sprung defenders stop firing* — a crippled remnant that
  stalls rather than dies.

That is consistent with an **early burst (2–5 kills as the column crosses the clearing) then a
hard plateau** — attrition does not continue to a wipe, which is why the mean is 3.5/5 and the
resolve rate is only 17 %. First-kill / last-kill ticks remain unmeasured.

---

## 5. Proposed ratifiable bar(s)

The provisional "defender:attacker cost ratio ≥ 1:3" is ill-posed (defender loss = 0 → ÷0) and
has no discriminating power. Replace it with a two-clause decisive-win bar that puts the teeth on
the zero-variance defender axis and keeps the noisy attacker axis as a soft floor.

### Bar A — recommended (6-seed aggregate)

> **mean defender cost-loss ≤ 50cr (≤ 0.5 kills/5)** AND **mean attacker cost-loss ≥ 300cr
> (≥ 3.0 kills/5)**, over ≥ 6 seeds.
>
> This batch: def 0cr, att 350cr → **GREEN**.

- **Defender clause headroom (huge):** observed 0cr vs 50cr cap. Mean-over-6 tolerates ~3
  defender deaths across the *entire* batch (each death contributes 100/6 ≈ 17cr) before
  flipping — noise-tolerant, yet a real concealment break drives it far past 50 (COMPACT
  evidence: near-full-squad losses → 300–500cr mean).
- **Attacker clause headroom (thin, by design):** 350cr vs 300cr = 0.5 kill ≈ 1.2 SE. This is
  the soft side; it should not carry the discriminating weight.

### Bar B — optional hard guard (per-seed), stackable on Bar A

> **every seed: defender cost-loss = 0** (no defender may die on any seed).
>
> This batch: 6/6 seeds def = 0 → **GREEN**.

- Zero headroom on the defender-death axis — deliberately. Concealment is binary (the attacker
  can acquire the defender or it cannot); a single defender death under intact concealment is
  itself the regression signal. Robust here because def loss was 0 on *all* 6 seeds, not merely
  in mean. e3's `5.56mm.DMR` is single-target direct fire (no splash), so a stray death while
  the gate holds is near-impossible. If a rare splash/edge death is later observed under an
  intact gate, soften to "≤ 1 defender death across the batch."

**Recommendation:** ratify **Bar A** as the primary gate; adopt **Bar B**'s per-seed `def = 0`
clause as an optional hard guard for the concealment invariant.

### Regression-mode coverage

| Regression mode | Bar A | Bar B | Notes |
|---|---|---|---|
| **Concealment gate breaks** (item-21 seating regresses; `Detectable`/ground-shadow retune makes defenders acquirable) | **CAUGHT** — defenders start dying, mean cost-loss ≫ 50 (COMPACT: 300–500cr) | **CAUGHT** on first death | The core invariant. Large margin over noise. |
| **Ambush stops springing / fire-lane blocked** (item-21 buries defenders where DMR is blocked; Stage-3 machine regresses) | **CAUGHT (soft)** — attacker mean collapses below 300 | not caught | Noisy axis; also covered more precisely by **case01b-detect** (`defFired`, `ttfShot`, `defShots`). |
| **Subtle balance shave (~1 kill, 350→250 mean)** | flips attacker clause | not caught | **False-positive risk**: may be seed noise, not regression — reason the attacker clause is soft, not per-seed. |
| **Cost/force-parity drift** (e3 `Valued.Cost` change) | rescales both clauses | rescales | Out of scope; parity is structural (both inherit `^E3`). |

**What both bars MISS:** whether the defenders actually *fired*. On this map only defenders can
kill attackers, so `att ≥ 300cr` implies they fired — but the bar cannot *prove* acquire→fire,
and item-21's max-density seating can bury a defender in a fire-blocked seat. That fire-lane axis
is exactly what **`test-case01b-detect`** measures (defender `Detectable.Vision 3→1` so the fight
resolves; per-side `defFired=k/5`, `ttfShot`, `defShots`/`attShots`). Clean split: **case-01's bar
gates the concealment/survival axis; case01b-detect gates the fire-lane axis.** Do not overload
case-01's bar with fire-lane teeth it cannot measure.

---

## 6. Recommended logging for a future calibration rerun (do NOT implement now)

If case-01 is re-run for ratification, three cheap instrumentation additions would make the next
batch self-documenting instead of leaning on hand-transcription:

1. **Persist per-seed verdicts.** `run-test.sh` clobbers a single `result.json`; have it (or
   `run-batch.sh`) copy each verdict into the per-run screenshot dir, or append each `RESULT`
   note to a batch log. Currently 5 of 6 verdicts evaporate the moment the next seed starts.
2. **Emit per-kill ticks in the Lua** (log the tick each attacker/defender dies) → yields the
   kill-curve shape (first-kill/last-kill, burst-vs-attrition) that is currently unmeasurable.
3. **Emit return-fire on defenders** (shots-fired-at / damage-events-on defenders) → turns the
   "blind slaughter vs cover won" inference into a measured fact. `test-case01b-detect` already
   carries the `defShots`/`attShots`/`ttfShot` instrumentation to borrow.

None of these are needed to ratify Bar A/B against the *current* data — the survival axis is
already decisive — but they would harden a re-measurement and remove the reliance on a
now-overwritten `debug.log`.
