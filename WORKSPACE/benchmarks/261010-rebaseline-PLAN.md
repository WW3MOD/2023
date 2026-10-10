# `@stable` benchmark RE-BASELINE #4 — PLAN (not yet run, 2026-10-10)

**Status: PLAN ONLY. Nothing here has been run.** This is the plan the manager carries out once
the user gives an explicit go-ahead. A batch run needs that go-ahead under CLAUDE.md §"Who runs
what" rule 2. Once the corpus exists it gets recorded as `WORKSPACE/benchmarks/261010-rebaseline.md`
(or `<YYMMDD>-rebaseline.md` for the actual run date), mirroring
[`260923-rebaseline.md`](260923-rebaseline.md), and this PLAN file is deleted or archived in the
same commit.

Researched against **`origin/main @ 45b2fca4`** (fetched 2026-10-10, 113 commits after the
260923 control's `55df64e2`). The worker didn't build, launch or lint anything.

## Why: `@stable` moved twice since the control was taken

The current control is [`260923-rebaseline.md`](260923-rebaseline.md): 40/40 full-clock, 0 culls,
taken at `main @ 55df64e2`. Two merges since then change `@stable` behaviour, and both say so in
their merge messages:

| Merge | What changes for `@stable` | Fires on this instrument? |
|---|---|---|
| `a4fd519e` (wt/rearm-desync) | A bot's out-of-ammo rearm is now a `RearmAtHost` **Order** instead of a host-only `QueueActivity` (`PoiOffensiveBotModule.cs`, `AmmoPool.cs`, commit `92ce936a`). A dry unit starts its rearm one order-latency later. | **Almost certainly.** Dry units and rearming are routine in a 5–12 min motorized match. |
| `1a610a8c` (wt/supply-guards) | Trucks refuse to restock from, or deliver to, a Logistics Centre that is no longer allied: `SupplyTransferMath.HostStillServes`, keyed on `DropsSupplyCache.ValidRelationships` (`SupplyTransferMath.cs:219-231`; callers `DropsSupplyCache.cs:298,323`, `RestockSupply.cs:95`, `DeliverSupply.cs:106`). It also makes `GroundDangerAt` null-safe and has idle-heli launchers skip dead actors (`da96e3c4`). | **Possibly.** The map carries two `logisticscenter` actors, both `Owner: Neutral` (`tournament-s2-combat-river-zeta/map.yaml`, `Actor2300`, `Actor5166`). See [Watch](#watch). |

Both changes reach `@experimental` too, because the modules are shared. **This run is a new
control, not an A/B of those two merges.** There is no arm with them switched off.

### Other changes since `55df64e2` that could touch the sim: all inert for this purpose

This sweep comes from `git diff --stat 55df64e2..45b2fca4 -- engine mods` rather than from merge
messages (item 43 warns that counts taken from messages undercount):

- `ai.yaml`: the 31 changed lines are **comments only**. They re-point line citations and correct
  the `DropMinStarvingUnits` prose. No key changed.
- `player.yaml` (`ee89be4e`): adds the `ConquestVictoryConditions.Objective` text and removes
  `ResourceStorageWarning` (a text notification). Neither is bot behaviour.
- `BallisticMissileFly.cs` / `TestMode.cs` (`93233408`): adds a static test counter inside the
  arrival `CallFunc`, ahead of the existing `self.Kill(self)`. The counter doesn't touch sim
  state. `himarsmissile`/`iskandermissile` do fire in these matches, so the path runs.
- `DefconWall.cs`, `SupportPowerManager.cs` (`003155f5`): comments only. `Directory.Build.props`:
  `Product` string only.
- `BotVsBotMatchWatcher.cs`: no commits in range. **The verdict is still version 8**
  (`BotVsBotMatchWatcher.cs:581`), so the new corpus is field-comparable with 260905, 260922 and
  260923.
- The six tournament scenario directories and `launch-game.sh` have no commits in range.

## Method: is anything in 260923's method stale at `45b2fca4`?

**The executable method is unchanged.** The only commit since `55df64e2` touching
`run-tournament.sh` is `c461d99f`, which per its message and the diff changes the header comment
only. Verified by reading:

| 260923 parameter | Today | Citation |
|---|---|---|
| `--seeds N`, seed = `i*1000+17` | unchanged | `run-tournament.sh:289` (`MATCH_SEED=$((i * 1000 + 17))`) |
| `--mirror <scen2>`: odd index runs the mirror | unchanged | `run-tournament.sh:259-262` |
| `--max-wall-secs` explicit 900 / 2200 | unchanged. Without the flag the auto value would be `TimeLimitSeconds*4/SpeedMultiplier` = 150 / 360 s, **which culls every match**, so the flag is mandatory. | `run-tournament.sh:163-173` |
| `--config` per-scenario yaml | unchanged. The mirror match uses the **primary's** config. The primary and mirror configs are byte-identical anyway (`diff`, both rungs). | `run-tournament.sh:144-152`, `:307` |
| Hidden, uncapped framerate | unchanged: always `OPENRA_WINDOW_HIDDEN=1` and `CapFramerate=false` | `run-tournament.sh:302-312` |
| `batch.meta.json` `git_sha`/`git_dirty` | unchanged. **`git_dirty` is `git diff --quiet HEAD`, so untracked files never count.** | `run-tournament.sh:182-183` |
| Configs | `TimeLimitSeconds: 300` / `720`, `SpeedMultiplier: 8`, `GameSpeed: fastest`, `WinRule: score_or_sr_capture` in all six scenarios | the scenario yaml files |

**What the 2026-10-07 header rewrite makes explicit, and the manager must account for:**

1. **The exit code says nothing.** `run-tournament.sh` exits `0` even when no match wrote a
   verdict (header `:40-42`, final `exit 0` at `:383`; `bugs/discovered.md` 2026-10-07). Judge
   each batch by `match_*.json` count and by `summary.json`.
2. **There is no single-instance lock.** The runner calls `launch-game.sh` directly, not through
   `run-test.sh` (header `:5-8`, launch at `:302`), so nothing stops a second game launching on
   top of it. Serialization is entirely the manager's job.
3. `-v|--visible` does nothing (`:28-29`, `:113` sets a variable nothing reads). Don't pass it.

**One deliberate change from 260923's execution:** run from a **dedicated worktree pinned at the
gated SHA**, not from the main checkout. 260923 ran from `/Users/fredrik/Desktop/WW3MOD` through
`/tmp/queue44.sh`, which no longer exists. Every match is a fresh process that reads `engine/bin/`
and `mods/` from disk. In the shared main checkout, a merge-gate build or a `git merge` landing
during the ~9 h run would change the game under a running batch without any flag catching it:
`git_dirty` is computed once, at batch start. `REPO_ROOT` comes from the script's own location
(`run-tournament.sh:52`), so a worktree changes only where results land. Host, flags, seeds and
scenarios stay identical. `settings.yaml` and `debug.log` are still the global
`~/Library/Application Support/OpenRA/` copies (`:219`, `:238`), so worktree isolation does
**not** make concurrent launches safe.

## Preconditions checklist (manager, before the first match)

- [ ] **SHA.** Pick the run SHA `$SHA` = current `origin/main`, then check that both merges are
      ancestors:
      `git merge-base --is-ancestor a4fd519e $SHA && git merge-base --is-ancestor 1a610a8c $SHA && echo OK`.
      Both pass at `45b2fca4`.
- [ ] **Re-sweep for further `@stable` moves** if `$SHA` ≠ `45b2fca4`:
      `git diff --stat 45b2fca4..$SHA -- engine mods`, then `git log --merges --oneline 45b2fca4..$SHA | grep -i stable`.
      Disclose anything new in the card.
- [ ] **Merge gate green at `$SHA`.** The run must not start until the gate (`all` → `check` →
      `dotnet test` → `test`, plus `smoke` because both merges touched `engine/`) has passed and
      released the CPU.
- [ ] **Worktree:**
      `git -C /Users/fredrik/Desktop/WW3MOD worktree add --detach /Users/fredrik/worktrees/ww3mod/rebaseline-261010 $SHA`
- [ ] **Build in that worktree:** `make all` (Release). `engine/bin` isn't in git. Then confirm
      `engine/bin/OpenRA.dll` exists and its mtime is newer than the worktree's creation. This also
      closes 260923's "no build recorded" Watch item for this corpus.
- [ ] **Clean tree:** `git -C <wt> status --porcelain --untracked-files=no` prints nothing. Run it
      after the build: tracked files must not change. Otherwise `git_dirty: true` gets stamped.
- [ ] **Result-directory hygiene:** none of the four target dirs exists in the worktree yet
      (`ls -d tools/autotest/tournament-results/261010_rebaseline_* 2>/dev/null` prints nothing).
      The runner `mkdir -p`s and **overwrites `match_N.json` in place** with no refusal (`:180`).
      A re-run into an existing dir mixes corpora silently. If the run date isn't 261010, change
      the prefix in all four commands below.
- [ ] **No other game running, and nothing else will launch for ~9.5 h.**
      `pgrep -fl 'OpenRA|launch-game'` prints nothing. Hold the launch slot for the whole run
      (rule 2: concurrent launches crash this machine, and the runner has no lock).
- [ ] **No stale settings backup:** `ls tools/autotest/tournament-results/*/.settings.yaml.bak`
      prints nothing. The per-dir backup at `:280-283` is restored after each match (`:353-355`). A leftover
      one means an earlier run was killed mid-match.
- [ ] **Previous control still on disk** for the per-seed diff:
      `/Users/fredrik/Desktop/WW3MOD/tools/autotest/tournament-results/260923_rebaseline_{s1_cal,s1_exp,s2_cal,s2_exp}/`
      (untracked; all four were present on 2026-10-10).

## Commands: same shape as 260923

Run **from the worktree root**, strictly sequentially, in this order (260923's order). Each
batch's `batch.meta.json` must then match 260923's on `scenario`, `config`, `seeds_requested` and
`max_wall_secs`. Those four fields are the only record of 260923's commands, because its runner
script is gone. The commands below are reconstructed from them.

```sh
cd /Users/fredrik/worktrees/ww3mod/rebaseline-261010
T=tools/autotest/scenarios
R=tools/autotest/tournament-results

# 1. S1 calibration — Stable v Stable, unmirrored              (~61 min)
./tools/autotest/run-tournament.sh tournament-s1-eco-cal-nn --seeds 10 \
  --config $T/tournament-s1-eco-cal-nn/tournament-eco-5min.yaml \
  --max-wall-secs 900 --result-dir $R/261010_rebaseline_s1_cal

# 2. S1 Exp v Stable, mirrored                                 (~66 min)
./tools/autotest/run-tournament.sh tournament-s1-eco-river-zeta --seeds 10 \
  --config $T/tournament-s1-eco-river-zeta/tournament-eco-5min.yaml \
  --mirror tournament-s1-eco-river-zeta-mirror \
  --max-wall-secs 900 --result-dir $R/261010_rebaseline_s1_exp

# 3. S2 calibration — Stable v Stable, unmirrored              (~3 h 27 min)
./tools/autotest/run-tournament.sh tournament-s2-combat-river-zeta-cal-nn --seeds 10 \
  --config $T/tournament-s2-combat-river-zeta-cal-nn/tournament-combat-12min.yaml \
  --max-wall-secs 2200 --result-dir $R/261010_rebaseline_s2_cal

# 4. S2 Exp v Stable, mirrored                                 (~3 h 30 min)
./tools/autotest/run-tournament.sh tournament-s2-combat-river-zeta --seeds 10 \
  --config $T/tournament-s2-combat-river-zeta/tournament-combat-12min.yaml \
  --mirror tournament-s2-combat-river-zeta-mirror \
  --max-wall-secs 2200 --result-dir $R/261010_rebaseline_s2_exp
```

After each batch, check before starting the next. Don't trust the exit code:
`ls $R/<batch>/match_*.json | wc -l` must print `10`, and `summary.json` must exist.
`aggregate-tournament.sh` runs automatically at `:379-381` when at least one verdict exists.
**Stop the queue on any no-verdict.** 260923 had zero, so any no-verdict is a new fact to explain
before spending more hours.

### Expected wall-clock on this Mac

260923 ran on the same host with the same flags in **9 h 03 m 53 s** total. Per batch: 1 h 00 m 53 s,
1 h 06 m 02 s, 3 h 26 m 52 s and 3 h 30 m 06 s, about 6 m 05 s to 6 m 36 s per S1 match and
20 m 41 s to 21 m 01 s per S2 match. 260922 on the same host gave the same throughput. **Budget
about 9 h 10 m for the matches, plus about 5–10 min for `make all`; plan on 9.5 h of exclusive
launch slot.** Nothing in the diff should change sim cost much. Worst case under the caps is
10×900 + 10×900 + 10×2200 + 10×2200 = 17.8 h. An S2 match over about 1,500 s (260923's longest
was 1,367 s) is the early sign that something got slower.

**Optional checkpoint after batch 1 (~1 h).** The sim is deterministic per seed (verdict v5+;
`run-tournament.sh:286-289`). So the S1 cal batch alone answers **"did `@stable` change at all on
this instrument in the first 7,500 ticks?"** using the per-seed diff below. If all 10 seeds come
back identical to 260923, the two merges never fired before t=7,500. **That does NOT license
skipping batches 3–4:** S2 runs the same games on to t=18,000, and the changes can first fire
after t=7,500. Report it to the user at the checkpoint and continue unless they say stop.

## Readout to compare

Write it in the 260923 card's shape (Instrument / Batches / Calibration / Baseline / Side by side
/ Core finding / Watch), with attribution **strictly by `notes.players[].bot_type`**, ladder
metrics per RUNBOOK §6 (S1 `stats.capture_income_gross`, S2 `stats.kills_cost − stats.deaths_cost`),
and a fourth column `261010 (<sha>)` added to every side-by-side table.

**1. The per-seed determinism diff. This is new, and it is the one reading that isn't noise.**
The seeds, scenarios, configs and host are the same as 260923, and the sim is deterministic. So a
seed whose `stats` block is identical in both corpora is a game the two merges didn't touch, and a
seed that differs is one they did. For each batch and each `i` in 1…10:

```sh
python3 - "$OLD" "$NEW" <<'PY'
import json,sys,os
old,new=sys.argv[1:3]
for i in range(1,11):
    a=json.loads(json.load(open(f"{old}/match_{i}.json"))["notes"])
    b=json.loads(json.load(open(f"{new}/match_{i}.json"))["notes"])
    key=lambda p:(p["bot_type"],p["faction"])
    sa={key(p):p["stats"] for p in a["players"]}; sb={key(p):p["stats"] for p in b["players"]}
    same = sa==sb and a["winner_name"]==b["winner_name"] and a["duration_ticks"]==b["duration_ticks"]
    print(i, a["seed"], "IDENTICAL" if same else "DIVERGED", a["winner_name"], "->", b["winner_name"])
PY
```

(`OLD=/Users/fredrik/Desktop/WW3MOD/tools/autotest/tournament-results/260923_rebaseline_s1_cal`,
`NEW=$R/261010_rebaseline_s1_cal`, and so on for the other batches. Both are 260923-era files, so
check `notes` is still a JSON string the first time the script runs.) Report the
`IDENTICAL`/`DIVERGED` count per batch. **The earliest tick at which a seed diverges can't be
read from these files.** `time_to_first_capture_tick` matching is a weak hint, nothing more.

**2. Per-seed winner table, S2 cal and S1 cal**, extended to four builds (260905 / 260922 / 260923 /
261010), exactly as in 260923 §"The cal split has gone…". The count to quote is how many seeds
flipped 260923 → 261010.

**3. Win splits and ladder metrics** for all four batches, quoted against 260923's calibration
bands:

| Rung | 260923 cal side split | 260923 cal swing noise band | 260923 Exp split | 260923 ladder |
|---|---|---|---|---|
| S1 | USA 7 – Rus 3 | −14,400 … +14,050 | Exp 6–4 | cap-gross median Exp 27,234 / Sta 27,662 |
| S2 | USA 8 – Rus 2 | −40,650 … +53,000 | Exp 4–6 | Swing Δ median +10,800, mean +4,560 (≈ item-87 bias) |

**The calibration caveat goes in the card's first paragraph, not only in Watch:** two byte-identical
`@stable` bots have produced an **8–2** side split on this instrument (S2 cal, 260923). **Any win
split up to and including 8–2, in either direction, carries no information at N=10**: under a fair
coin P(≥8 of 10, either side) = 0.109. The three splits so far (5–5, 6–4, 8–2) also came from the
same seeds and so aren't independent, and must not be pooled.

## What counts as "`@stable` moved materially"

**The run's purpose is to re-take the control at the current SHA, not to detect a move.**
Whatever it shows, the 261010 corpus becomes the control for everything measured after `$SHA`.
The "did it move" question is secondary, and the instrument mostly can't answer it. State the
verdict as one of three tiers, decided **by the cal batches only**. The exp batches compare two
bots that both moved.

| Tier | Criterion | What to write |
|---|---|---|
| **0: did not fire** | All 20 cal seeds (10 S1 + 10 S2) `IDENTICAL` to 260923. | "The two merges are inert on this instrument; the 260923 numbers stand unchanged as the control at `$SHA`." (Only valid because tier 0 is a byte-level statement.) |
| **1: moved, not detectably** | At least one cal seed `DIVERGED`, but tier 2 isn't met. **Expected outcome.** | "`@stable` changed behaviour in N of 20 cal games; no aggregate reading leaves 260923's noise band at N=10." |
| **2: moved materially** | Any one of these: (a) a cal side split of **9–1 or 10–0** on either rung, beyond anything identical bots have produced here (P ≈ 0.021 under a fair coin); (b) a cal-rung **per-side median capture-gross** shift against 260923 larger than the spread of that side's 260923 cal values (S1 USA 21,089–29,259, Russia 21,081–29,404; S2 USA 68,288–94,008, Russia 81,989–106,950); (c) any cal match with **`win_reason` other than `time_limit`**, any **SR defeat**, or a **no-contact void** (zero `kills_cost` or `deaths_cost` on a side). 260923 had none of these in 40 matches. | Name which criterion fired, with numbers. Open a follow-up to find which merge did it before anyone reads an exp result against this control. |

**Per-seed flip counts are NOT a materiality criterion.** Between 260922 and 260923, 4 of 10 S2
cal seeds flipped side after one merge. Flips under small code changes are the instrument's normal
behaviour (260923 §"The cal split…").

## The go-ahead question for the user

> **Run the four-batch `@stable` re-baseline (40 matches, same seeds, flags and scenarios as
> 260923) at main `$SHA`, which includes both `@stable`-moving merges, a4fd519e and 1a610a8c,
> from a fresh pinned worktree? Cost: ~9.5 h of exclusive launch slot (no other game launches),
> one `make all`, and an optional checkpoint report after the first ~1 h batch.**

## Watch

- **If the trucks used the two neutral Logistics Centres before `1a610a8c`, this map gets a much
  bigger `@stable` change than "captured mid-drive" implies.** `HostStillServes` accepts a host
  only if its relationship falls in `ValidRelationships`, which defaults to `Ally`
  (`SupplyTransferMath.cs:222`, `:230`). Both LCs on the tournament map are `Owner: Neutral`. I
  didn't check whether the order resolvers already excluded neutral hosts before the merge, or
  whether `^Building` LCs are capturable here. If they weren't excluded, every truck errand to
  those LCs now fails, and S2 cal should diverge on most seeds. **Quick disproof before the run:**
  `grep -c 'restock\|logisticscenter' 260923_rebaseline_s2_cal/match_*_debug.log`, or read
  `DropsSupplyCache.cs:290-330` at `55df64e2`.
- **Determinism across the two corpora is assumed, not shown.** The per-seed diff only means
  something if seed `i` replays byte-identically on an unchanged build. 260923's own Watch
  proposed the same check (re-run one seed, compare `match_1.json`) and nobody ran it. A tier-1
  "diverged" could therefore be build or host nondeterminism, not the merges. **The cheap control
  is to re-run one seed of 260923's S1 cal at `55df64e2`** (one extra ~6 min match plus a build of
  that SHA, which the go-ahead above doesn't cover). If that diverges too, the per-seed diff is
  worthless and tier 0 can't be claimed.
- **The worktree deviates from 260923's execution.** I argued it's neutral (`REPO_ROOT` is
  script-relative, flags identical) but haven't measured it. It does move results from the main
  checkout to the worktree. Copy the four dirs next to the 260923 ones afterwards if the next
  corpus should find them there.
- **The command lines are reconstructed, not copied.** `/tmp/queue44.sh` is gone, and the 260923
  card records parameters, not argv. The reconstruction matches all four `batch.meta.json` field
  values, but any flag that leaves no trace in `batch.meta.json` would be invisible to this check.
  Of the parser's flags (`:101-131`), only `-v` (inert) qualifies.
- **The static counter in `BallisticMissileFly` (`93233408`) is on a path these matches execute.**
  I read it as sim-neutral (static field, no RNG, no actor state). If tier 0 fails narrowly on
  missile-heavy seeds only, look here before blaming the two merges.
