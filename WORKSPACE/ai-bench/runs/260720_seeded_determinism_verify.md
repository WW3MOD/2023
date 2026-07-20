# Seeded determinism — VERIFY (result: FULL determinism PASS)

**Date:** 2026-07-20 · **Branch:** `main` @ `2d3c8fe0` (post-dispersion; NUnit 291/291)
· **Run mode:** B (hidden, `OPENRA_WINDOW_HIDDEN=1`, `SpeedMultiplier: 8`)
· **Scenario:** `tournament-s1-eco-river-zeta`, `tournament-eco-5min.yaml`
(300s / 7500t) · **Plan:** `WORKSPACE/plans/260720_seeded_determinism.md` (recon `aa0dc7e7`)

Raw dir: `tools/autotest/tournament-results/260720_seedverify/`. Three single hidden
matches (authorized shape: same seed ×2 + one different-seed negative control).

## The fix (commit 2d3c8fe0)

- **`World.cs:214`** — `LocalRandom` was constructed **unseeded** (`new MersenneTwister()`
  → `Environment.TickCount`), the sole simulation-affecting nondeterminism under a fixed
  `Test.RandomSeed` (~40 bot-decision sites read it). Now seeded from the lobby `RandomSeed`
  via a decorrelating PCG-style transform
  `(int)(RandomSeed*6364136223846793005 + 1442695040888963407)`, so its stream is a pure
  function of the seed yet independent of `SharedRandom`'s combat rolls. Guarded on
  `RandomSeed != 0` (the unset default — every real SP/MP/test seed is `Test.RandomSeed` or
  `DateTime.Now`-derived) so normal gameplay stays wall-clock-seeded across launches.
- **`BotVsBotMatchWatcher.cs`** — verdict now stamps the authoritative lobby `"seed"` and
  bumps `verdict_version` 4 → 5 (additive; `parse-s1-batch.py` and `aggregate-tournament.sh`
  both read keys via `.get()` — no parser change needed).

## Protocol & result

Same seed twice, then a different seed as negative control; generous 300s wall cap so every
match reached natural end (`win_reason: time_limit`, full 7500 ticks — no watchdog kill).

| run | seed | winner | reason | ticks |
|---|---|---|---|---|
| det_a | 1017 | Russia-bot | time_limit | 7500 |
| det_b | 1017 | Russia-bot | time_limit | 7500 |
| neg   | 9017 | USA-bot    | time_limit | 7500 |

- **Same-seed (1017 ×2): BYTE-IDENTICAL.** Deep JSON diff of the two verdict `notes` blobs =
  0 differing fields (duration_ticks, winner_client_index, win_reason, and every player's
  score_total / score_components / stats). Stronger: the watcher's tick-by-tick score log
  (60 logged intervals over the whole match) is identical line-for-line — determinism holds
  from tick 0 to end, not merely at the final verdict. **No residual nondeterminism**
  (async-pathfinding et al. from plan §5.3) surfaced.
- **Negative control (9017): 25 differing fields, different winner** (USA-bot vs Russia-bot)
  — the seed genuinely drives outcome; the fix did not make matches seed-invariant.

**Verdict: FULL replay determinism** for the S1 hidden Mode-B match. A fixed seed now
reproduces a whole match exactly.

## Side-observation — the variance was seed-driven (and is now reproducible)

Seed 1017 → **both** bots `capture_income_gross=0` (no derrick capture inside the 7500t
window); seed 9017 → experimental captures (`gross=10917`, score 23084 vs 3350). That
capture-vs-no-capture swing across seeds is exactly the 4/10-vs-9/10 in-window-capture
variance that motivated this work. It is now a *reproducible* per-seed fact, not run-to-run
noise — the precondition for a fixed small seed-set to yield a stable mean (parameter-search
on-ramp, plan §8). Keep N>1 for *evaluating a code change*; determinism only removes the
run-to-run wobble at a fixed seed.
