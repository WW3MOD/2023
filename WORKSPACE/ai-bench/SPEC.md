# AI Benchmark — System Specification (the constitution)

**Status:** ACTIVE. This is the source of truth for the AI Benchmark side project.
**Bootstrap contract:** a fresh autonomous manager (Maestro on autoburn) is
started *from this file*. Read it top to bottom, then read [`LADDER.md`](LADDER.md)
and [`REVIEW.md`](REVIEW.md), and you have everything needed to run the loop with
no other context. If something here is stale, fix it here — this doc is mutable
and self-maintaining.

**Grounded against:** `main` @ `06afb643` + the substrate findings
([`../plans/260719_ai_benchmark_substrate_findings.md`](../plans/260719_ai_benchmark_substrate_findings.md)).
Every claim about the harness cites a script or engine file; if a cite no longer
matches the code, trust the code and update the cite.

> **Terminology — v2 → Experimental (2026-07-20, `ai-bench` rename commit).** The dev
> bot is now `ModularBot@experimental` ("Experimental AI"), not `@v2` / "V2 AI
> (experimental)". A frozen **`ModularBot@stable`** ("Stable AI") was added alongside it
> — the promotion policy is **§13**. Throughout the body below, read the historical
> identifiers `ModularBot@v2` → `@experimental`, `enable-ai-v2` → `enable-ai-experimental`,
> `[v2-*]` logs → `[exp-*]`, and scenario `tournament-v2-vs-normal-*` →
> `tournament-experimental-vs-normal-*`. The body is intentionally **not** rewritten
> (its history stays legible); this note is the mapping. (The verdict-schema
> `verdict_version: 2`/`3` in §8 is unrelated to the bot name and unchanged.)

---

## 1. What this system is

An **autonomous, self-improving benchmark loop**. A manager repeatedly:

> **pick a hypothesis → implement it in a worktree → run a benchmark batch →
> score it against a fixed control → log the outcome → merge-or-revert.**

The thing being improved is the **Experimental AI** (`ModularBot@v2`, lobby name
"V2 AI (experimental)"). The yardstick is the **Normal AI** control, which is
never touched. Progress is measured on a **ladder of standardized scenarios**
(see [`LADDER.md`](LADDER.md)) run on the **River Zeta WW3** map. The system runs
for the long haul across many manager sessions; continuity lives in
[`REVIEW.md`](REVIEW.md), not in any one session's memory.

### 1.1 Why it can exist today

The substrate is ~90% built (findings doc, headline). A mature bot-vs-bot
tournament harness already produces seeded, JSON-verdict matches (seeds are run
labels, not replay guarantees — §3.2):

- `tools/autotest/run-tournament.sh` — N seeded matches → per-match verdict JSON
  + log → `summary.csv`/`summary.json`, git-SHA stamped.
- `tools/autotest/aggregate-tournament.sh` — batch rollups.
- `tools/autotest/loop-tournament.sh` — autonomous milestone loop (batch →
  aggregate → stop-condition), `loop_progress.csv`, terminal bell.
- `tools/autotest/compare-batches.sh`, `tournament-report.sh` — cross-batch diffing.
- Engine trait `BotVsBotMatchWatcher` writes a rich per-player verdict at match
  end (`engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs`).
- Scenario `tournament-v2-vs-normal-2p` already pits Experimental (P1) vs Normal
  (P2) on the River Zeta layout.

The **one** thing standing between this and unlimited unsupervised operation on
Windows is headless/hidden-window support — see §3.

### 1.2 What the manager consumes vs. produces

- **Consumes:** this SPEC, the LADDER, the REVIEW inbox, `git log`, the harness
  scripts, and prior cycle cards under `WORKSPACE/ai-bench/runs/`.
- **Produces:** AI/engine/harness code changes (in the worktree), merges to
  `main`, cycle cards, and REVIEW.md activity-log + ladder-status updates.

---

## 2. The loop, step by step

Each **cycle** is one hypothesis tested to a verdict. This is the manager's
inner loop; run it continuously while budget allows (§8).

1. **Read the board.** Open [`REVIEW.md`](REVIEW.md). Process the **Inbox**
   first (§7) — user directives outrank the manager's own backlog. Then read the
   ladder status and the last few cycle cards to know where you left off.
2. **Pick a hypothesis.** One concrete, falsifiable change to the Experimental AI
   expected to move a specific ladder metric (e.g. "raising the offensive axis
   count from 3 to 4 should raise force-efficiency on Scenario 2 without hurting
   the eco race"). Prefer the smallest change that tests one idea. Draw from: an
   inbox directive, the current AI plan
   ([`../plans/260719_experimental_ai_poi_strategy.md`](../plans/260719_experimental_ai_poi_strategy.md)),
   or a weakness a prior cycle card flagged.
3. **Implement in the worktree.** All work happens in
   `C:\Users\fredr\worktrees\ww3mod\ai-bench` on its own branch, own build (§5).
   Respect the mutable-scope + anti-cheat rules (§4) — no exceptions.
4. **Build.** In the worktree: `./make.ps1 all`. On Windows a build can fail on
   locked DLLs if the game is running — wait or move on, don't alarm the user.
5. **Run the benchmark batch.** For each ladder scenario in scope, run the
   configured N matches in the current **run mode** (§3). The manager NEVER
   fires batch runs outside the sanctioned mode — Mode A requires a user window;
   Mode B is unlimited.
6. **Score against control.** Extract the scenario's metric from the per-match
   verdict JSONs, compute the median over N, and compare to the Normal control
   per the scenario's advancement criterion (§6, LADDER).
7. **Log a cycle card.** Write `WORKSPACE/ai-bench/runs/<YYMMDD_HHMM>__<scenario>__<sha7>.json`
   (schema §9) and append a one-line entry to the REVIEW.md activity log.
8. **Decide — merge or revert (§5.3):**
   - **Improvement** (a few positive runs, no crash, soft bar met) → **merge to
     `main`** so the user can play the progress. Log a `MERGE` entry.
   - **Neutral / uncertain** → keep iterating on the worktree; do not merge.
   - **Regression or crash** → **revert**; never merge. Log a `REVERT` entry.
9. **Loop.** Return to step 1. Pace against the budget (§8); finalize cleanly
   before context/budget exhaustion.

The loop is quiet by default. It pings the user only at genuine milestones
(ladder rung cleared, regression that reached `main`, a blocker it can't resolve,
budget exhausted) — reuse `loop-tournament.sh`'s bell + a REVIEW entry.

---

## 3. Run-policy modes (the hidden-window switch)

Per the binding user decision, the system is **hybrid**. There are exactly two
modes; the loop is written so the mode is a **single switch** (which env/flag set
`run-tournament.sh` launches with) and nothing else differs.

> **STATUS (2026-07-19): Mode B is ACTIVE from bootstrap.** The parallel
> substrate worker landed and *verified* `OPENRA_WINDOW_HIDDEN=1`
> (`Sdl2PlatformWindow.cs`, commit `d716eade`; verification + finding commit
> `fda8370c`). Hidden runs create no window, steal no focus, and write a
> `verdict_version: 2` verdict; the simulation is decoupled from SDL/render (the
> lockstep sim cannot touch the renderer — static root-cause analysis), so a
> hidden run **provably cannot alter a match outcome**. The system therefore runs
> **unlimited, unsupervised, Mode B by default**. Mode A remains documented below
> as the **fallback** to fall back to if a future engine regression ever re-opens
> the hidden-window question (§3.1).

### Mode A — Windowed / supervised (the FALLBACK — used only per §3.1)

- Every match opens a real SDL window that **grabs OS focus on Windows** — there
  is no focus mitigation on Windows (findings Q1; the existing `osascript` fix is
  macOS-only, `run-test.sh:215-282`). So batch runs **steal the user's focus**
  and cannot run while the user is working.
- **Runs happen ONLY in user-declared windows** (typically overnight). The user
  says "go" and defines the window; the manager runs batches until it closes,
  then stops firing new batches.
- Launch profile: `SpeedMultiplier: 6`–`8` in the scenario `tournament.yaml`
  (`BotVsBotMatchWatcher.cs:121-127` applies it at `WorldLoaded`) + framerate cap
  (`Graphics.CapFramerate=true Graphics.MaxFramerate=5`, already wired
  `run-tournament.sh:224-226`) → **4–6× practical** wall-clock; a 12-min match
  ≈ ~2 wall-clock minutes windowed (findings Q2).
- Windows portability: **the harness is Windows-native as of commit `4dec6a74`**
  (run under **Git-Bash / MSYS**). That commit added additive Windows branches to
  `run-tournament.sh`, `run-test.sh`, and `loop-tournament.sh` — macOS/Linux paths
  are byte-for-byte unchanged; `aggregate-tournament.sh` needed no change. What it
  covers: **cygpath POSIX→Windows conversion** for engine-bound args
  (`Test.ResultPath` / `Test.TournamentConfig`), so the .NET process gets `C:\…`
  not `/c/…`; a **PowerShell CIM process-kill** (`Get-CimInstance Win32_Process |
  Stop-Process`, filtered to the `dotnet.exe`/`OpenRA*` image and the result-file
  basename) replacing `pkill` on Windows; **`%APPDATA%\OpenRA` settings.yaml**
  resolution (then `engine/Support`, then `Documents\OpenRA`) for the mute
  backup/restore; and **CRLF hardening** of the awk config extractions.
- **These 5 items are coded but await live confirmation** — the manager verifies
  them during the bootstrap smoke run (§3.3) before trusting an unattended batch:
  (1) MSYS passes the converted args through without re-mangling; (2) the engine
  reads/writes `ResultPath` + `TournamentConfig` at the converted Windows paths;
  (3) the kill-filter's `dotnet.exe` image-name assumption matches the actual game
  process; (4) the watchdog actually terminates a *live* match (not just a
  finished one); (5) the settings backup/restore round-trips (mute doesn't leak
  into the user's config). None block Mode B (which doesn't need the window), but
  all matter whenever Mode A is used as the fallback.

### Mode B — Hidden / unsupervised (ACTIVE — the default from bootstrap)

- Enabled by `OPENRA_WINDOW_HIDDEN=1` (`Sdl2PlatformWindow.cs`, commit
  `d716eade`). It adds `SDL_WINDOW_HIDDEN` at window creation, so SDL never maps
  or focuses the window → no window, no focus theft, GL context still exists.
- **Verified live** (commit `fda8370c`): a hidden run created no visible window,
  stole no focus, ticked the sim to completion, and wrote a `verdict_version: 2`
  verdict. The sim/render decoupling means the flag **cannot** change match
  outcomes (§3.1).
- Launch profile: `SpeedMultiplier: 8`, drop the framerate cap, expect **8–12×**
  (findings Q2). Runs are **unlimited and unattended, any time of day** — no user
  window required.

### 3.1 Why Mode B is safe, and the fallback trigger

**The original "same-seed verdict-identity" gate was impossible to satisfy and
has been replaced.** Per-seed reproducibility does **not** hold in this codebase:
bots draw decisions from an **unseeded** `world.LocalRandom`
(`World.cs:214`; e.g. `UnitBuilderBotModule.cs:173`), so *any* two runs of the
same seed diverge within ~125 ticks — **two windowed runs would diverge exactly
as much as windowed-vs-hidden**. Comparing verdicts across a mode boundary can
therefore never prove identity (commit `fda8370c`,
`WORKSPACE/DISCOVERIES.md` 2026-07-19 LocalRandom entry). Determinism was **not**
the right test.

What actually established Mode B's safety (all confirmed, `fda8370c`):

1. **No window / no focus steal** — the hidden flag creates no mapped surface.
2. **A verdict is written** — the sim runs to a natural match end and emits
   `verdict_version: 2`.
3. **Sim/render decoupling** — static analysis confirms the lockstep simulation
   has no path to the renderer, so hiding the window **cannot** perturb the
   outcome. This is a *structural* guarantee, stronger than any single-run
   comparison could give.

**Fallback trigger (Mode A):** revert to windowed, user-window-only runs **only
if** a future engine change breaks the sim/render decoupling — i.e. if a hidden
run ever fails to write a verdict, or the substrate worker reports the decoupling
invariant broken. If that happens: log a `NOTE`/`ENGINE` entry, flip the mode
switch to A, and treat restoring hidden-mode as a blocker for the user. Absent
that, **stay in Mode B**.

### 3.2 Known substrate caveats

- **Per-seed reproducibility is broken** until the `LocalRandom` seeding fix
  lands (backlogged — e.g. seed `LocalRandom` from `RandomSeed ^ const`, or route
  bot decisions through the seeded server RNG; see the DISCOVERIES entry). A given
  seed is **not replayable**: re-running seed *N* yields a *fresh independent*
  game, not the same game.
- **This does not affect the benchmark.** All ladder criteria are **N-run
  statistics** (medians / win-rates over a batch, §6), which only need
  *independent samples* — and independent is exactly what unseeded runs give.
  **Treat seeds purely as run labels**, not reproducibility guarantees. The one
  thing lost is single-match *debugging* replay (reproduce a specific outlier by
  its seed); that returns once the seeding fix lands. Nothing in the loop depends
  on it.

### 3.3 Bootstrap smoke run (first thing on a fresh system)

Before trusting any full batch, the first-ever manager runs a **1–2 match smoke
run** to prove the whole pipeline end-to-end on this machine: build → launch →
verdict written → aggregate → cycle card. It doubles as the live confirmation of
the 5 Windows-portability items listed in Mode A above (converted-path arg
passthrough, `ResultPath`/`TournamentConfig` read-write at Windows paths, the
`dotnet.exe` kill-filter, watchdog kill of a *live* match, settings
backup/restore round-trip). Run it in Mode B (hidden). Record the outcome as a
`HARNESS` activity-log entry. If any of the 5 items fails, fix the harness (a
`HARNESS`-category change, allowed §4.1) before starting the hypothesis loop —
this is the only pre-loop gate that remains.

---

## 4. Mutable scope + anti-cheat (the integrity rules)

The optimizer may change **anything except unit stats / balance numbers**, with
one overriding rule: **never move the goalposts.** The benchmark measures the AI
against *fixed game rules*; changing the rules to score higher is cheating and
invalidates the whole ladder.

### 4.1 Allowed

- **Experimental AI behavior** — `ModularBot@v2` modules and any new bot modules,
  gated `enable-ai-v2` (`PoiMap`, `PoiOffensiveBotModule`, `PoiGoalGuard`,
  `CaptureCoordinatorBotModule`, etc.).
- **AI YAML wiring** under `enable-ai-v2` (`mods/ww3mod/rules/ai/*.yaml`).
- **The harness** — scenarios, scorers, win rules, runner scripts, verdict
  schema, new metrics extraction.
- **Engine fixes that *unblock* AI improvement** — e.g. a pathfinding bug that
  strands capture TECNs, or a unit-claim gap that lets one module steal another's
  units. These are real fixes, not benchmark-gaming.
- **Benchmark docs** (this SPEC, LADDER, REVIEW, cycle cards).

### 4.2 Forbidden (hard rules)

- **Unit stats / balance numbers.** Damage, armor, cost, range, health, and
  income amounts (e.g. Oil Derrick `$50`, `structures-neutral.yaml`) are
  **immutable**. Raising derrick income to win the eco race is the canonical
  forbidden move.
- **Any engine/game-rule change made *to fit the benchmark*.** If a change makes
  the yardstick shorter rather than the AI smarter, it is forbidden even if it
  isn't a "stat."
- **The control AIs.** `ModularBot@normal / @rush / @turtle` must stay
  **behaviorally byte-identical**. They are the measuring stick; if they drift,
  every historical result is invalidated. The established pattern (Phase 2/3 of
  the POI plan) is: new behavior gated `enable-ai-v2`, controls untouched — keep
  it.

### 4.3 The litmus test

For any change, ask: **"Does this make the AI *decide* better, or does it make
the *yardstick* shorter?"** The former is the job; the latter is forbidden.
Non-blocking issues (bugs that don't gate AI improvement) need not be fixed —
stay focused on the AI.

---

## 5. Worktree + merge protocol

### 5.1 Isolation

- **All loop work happens in `C:\Users\fredr\worktrees\ww3mod\ai-bench`** on its
  own branch (`wt/ai-bench` or similar), with its **own build** (separate
  `engine/bin`). This keeps the user's `main` checkout playable and unbuilt-upon
  while the loop churns. (Per the global worktree convention in
  `~/.claude/CLAUDE.md`.)
- The manager does **not** create the worktree in this spec's bootstrap — the
  first cycle creates it if absent (`git worktree add C:\Users\fredr\worktrees\ww3mod\ai-bench wt/ai-bench`).
- **NEVER push to remote** (CLAUDE.md hard rule). The user pushes/pulls manually.
  "Merge to main" below means a **local** merge only.

### 5.2 Merge early and often

Per the binding user decision: **anything believed to be an improvement merges to
`main` promptly** so the user can play from `main` and *see progress*. The bar to
merge is deliberately **soft** — a few positive runs and a soft rule is enough.
Do not hoard improvements on the worktree waiting for statistical certainty; the
ladder's rigorous advancement criteria (§6) gate *ladder advancement*, not
*merging*. Merge is cheap and reversible; a merged improvement that later proves
neutral can be revisited.

Mechanism (local, no push):
```
# in the worktree, after a positive cycle
git commit -m "ai-bench: <hypothesis one-liner> (+<metric delta> on <scenario>)"
# merge into main locally
git -C C:\Users\fredr\Desktop\WW3MOD merge wt/ai-bench   # fast-forward or merge commit
```
Then log a `MERGE` entry in REVIEW.md (dated + sha + one-liner).

### 5.3 The one unacceptable merge: crashes

**A build that crashes must never reach `main`.** A crash is the only
categorically unacceptable merge. Before any merge, the candidate build must have
completed its benchmark batch with **verdicts written and no engine
exceptions/stacktraces** in the match logs. Triage (§10.1) distinguishes an
acceptable stalemate-timeout from a crash — only the latter blocks the merge and,
if already on `main`, forces an immediate revert.

### 5.4 Reverting

- **On the worktree:** a neutral/regressive experiment is simply not merged;
  reset or abandon the branch commit.
- **On `main` (a regression slipped through):** create a **new revert commit**
  (`git revert <sha>`), never a history rewrite (CLAUDE.md: create new commits,
  don't rewrite published history). Log a `REVERT` entry with the reason.

---

## 6. Advancement criteria (default + fluid)

Advancement is **statistical vs. control, with no-regression** — but the manager
may **adjust the criterion per scenario** as long as it stays honest and
discriminating. Fixed targets, beat-Normal-within-X-minutes, and handicap
variants are all valid tools.

### 6.1 Default rule

A scenario is **passed** on a candidate build when, over **N runs**:

> **median(Experimental metric) ≥ median(Normal control metric) × (1 + margin)**

with the per-scenario `N`, `margin`, and metric defined in [`LADDER.md`](LADDER.md).
Median (not mean) to resist seeded-variance outliers.

### 6.2 No-regression (ladder-level)

Before a scenario is declared passed **for advancement**, **re-verify every
previously-passed scenario on the same candidate build** — all must still pass.
This prevents winning one facet by regressing another. (This is stricter than the
merge bar in §5.2 — merging is soft; *advancing the ladder* is rigorous.)

### 6.3 Fluid per-scenario tools (explicitly sanctioned)

The manager may pick, per scenario, whichever keeps the test **discriminating**:

- **Fixed target** — an absolute number (e.g. "≥ $X earned in 5 min"), when the
  control is uninformative for that facet.
- **Beat-Normal-within-X-minutes** — a time-to-outcome bar rather than a
  score-margin bar.
- **Handicap variants** — as the Experimental AI outclasses Normal, keep the test
  sharp by handicapping (Experimental starts with fewer reserves, or Normal with
  more). A handicap that Experimental still beats is stronger evidence than an
  ever-widening margin on an even start.

Whenever the manager changes a criterion, it records the change and rationale in
the scenario's LADDER entry and a REVIEW `LADDER` log line, so the history stays
interpretable.

### 6.4 The composite gate (clearing a ladder rung)

A **rung** (one map, e.g. River Zeta) is **cleared** only when a **single
commit** passes **all three scenarios together, re-verified in one sitting** —
not three different commits that each win one facet. Passing the composite gate
is a `LADDER` milestone: it merges (if not already), pings the user, and advances
the ladder (new map / tighter margins / a new scenario — §11).

---

## 7. Review-board protocol (core deliverable)

[`REVIEW.md`](REVIEW.md) is the single overview document. Design goal (user
quote): **review "should ideally take a minute only."** It is **bidirectional** —
the manager writes a categorized activity log; the user writes free-form
directives back.

### 7.1 File layout (top-down, so the minute-review reads newest-first)

```
1. AT A GLANCE      — current rung, last cycle time+sha, one-line headline.
2. YOUR INBOX       — user → manager. Free-form directives. (protocol below)
3. ACTIVITY LOG     — manager → user. Reverse-chron, categorized, one line each.
                       A REVIEW CURSOR line marks how far the user has read.
4. LADDER STATUS    — living table: per scenario, best median vs control, verdict.
5. OPEN QUESTIONS / BLOCKERS — what the manager needs from the user.
```

### 7.2 Inbox protocol (the read/clear contract)

The Inbox is the only place the **user writes**. The rule set is small and
strict so neither side ever destroys the other's text:

- **User adds** a directive as an unchecked item: `- [ ] <free-form directive>`.
- **Manager reads the Inbox every cycle** (loop step 1), before its own backlog.
  For each **unchecked** item it acts on it and **annotates in place**, appending
  its response after a ` → ` on the same item, e.g.
  `- [ ] tighten scenario 2 margin  → (mgr 2026-07-20 a1b2c3d) raised margin 10%→15%, re-baselined; see cycle card.`
  The manager **never checks the box** and **never deletes** a user item — the
  checkbox belongs to the user.
- **User marks an item seen** by checking it: `- [x] ...`. That is the "I've read
  your response" signal.
- **Manager clears checked items:** on its next cycle, any `- [x]` item is
  **moved out of the Inbox** into the `Resolved directives` archive at the bottom
  of REVIEW.md (verbatim, with its date). The Inbox thus only ever contains live
  (unchecked) items — the user's "what still needs me" list stays short.

Net effect: **unchecked = live** (manager keeps acting + annotating);
**checked = acknowledged** → manager archives it. The manager only ever *clears
items the user has marked seen*, exactly per the binding decision.

### 7.3 Activity-log protocol (the categorized record)

Every action the loop takes lands as one reverse-chronological line
(newest at top):

```
YYYY-MM-DD | <sha7> | CATEGORY | one-line description
```

`CATEGORY` ∈ **`AI`** (Experimental AI change), **`ENGINE`** (engine fix that
unblocked AI work), **`HARNESS`** (scenario/scorer/runner/metric change),
**`MERGE`** (merged to main), **`LADDER`** (scenario passed / rung cleared /
criterion changed), **`REVERT`** (backed out), **`NOTE`** (finding, blocker,
handoff). Each line is dated, commit-stamped where a commit exists, and one
sentence — so the whole history skims in a minute.

**The REVIEW CURSOR:** a single line `--- ▲ reviewed through here ▲ (user moves this) ---`
sits in the activity log. The manager **adds new entries above it** and **never
moves it**. The user drags it up to the top when they've read the new entries —
the log's mirror of the inbox checkbox. "New since your last review" = everything
above the cursor.

---

## 8. Data recording (where results live, naming)

Two layers — raw (harness-owned, verbose) and distilled (benchmark-owned, skimmable).

### 8.1 Raw (unchanged, harness-owned)

Per-match verdict JSON, per-match log, `summary.csv`/`summary.json`,
`batch.meta.json` (git SHA, scenario, config, seeds) land in the existing
timestamped dir (`run-tournament.sh:131-133`):

```
tools/autotest/tournament-results/<YYMMDD_HHMM>_<scenario>/
    match_<i>.json      # verdict — schema below
    match_<i>.log       # stdout/stderr (crash triage reads this)
    summary.csv / .json # aggregate
    batch.meta.json     # sha, scenario, config, seeds_requested, git_dirty
```

These are **git-ignored working data**, not committed (they're bulky, and a fresh
batch can always be regenerated as a new independent sample — though not as a
bit-identical replay, §3.2). The manager keeps them until it has written the
distilled cycle card, then they may be pruned.

### 8.2 The verdict JSON (what a match emits)

`BotVsBotMatchWatcher.SerializeVerdict` (`BotVsBotMatchWatcher.cs:250-316`),
`verdict_version: 2`. Per-player fields include `score_total`,
`score_components{army_value, capture_income, kills_value}`, and a full `stats`
block: `units_killed`, `units_dead`, `buildings_killed`, `buildings_dead`,
`kills_cost`, `deaths_cost`, `army_value`, `assets_value`, `order_count`,
`experience`, and **`resources_earned`** (`:308`).

> **PITFALL (load-bearing for Scenario 1):** the cumulative-cash metric is
> **`resources_earned`** ← `PlayerResources.Earned`. **Do NOT** use
> `PlayerStatistics.Income` — that's a rolling/sampled 60s figure, not lifetime
> earnings (`BotVsBotMatchWatcher.cs:292-294`, PITFALLS §14).

### 8.3 Distilled (benchmark-owned, committed)

One **cycle card** per hypothesis-batch, committed so the history survives even
after raw data is pruned:

```
WORKSPACE/ai-bench/runs/<YYMMDD_HHMM>__<scenario>__<sha7>.json
```

Schema:
```json
{
  "cycle_ts": "2026-07-20T02:14:00Z",
  "scenario": "tournament-v2-vs-normal-2p",
  "rung": "river-zeta",
  "sha": "a1b2c3d",
  "hypothesis": "raise offensive axis count 3->4",
  "run_mode": "windowed",            // or "hidden"
  "n_runs": 10,
  "seeds": [1017, 2017, "..."],
  "metric": "resources_earned",
  "experimental_median": 41250,
  "control_median": 33800,
  "margin_required": 0.15,
  "margin_observed": 0.22,
  "verdict": "pass",                 // pass | hold | regression | crash
  "no_regression_checked": ["scenario-1"],
  "decision": "merge",               // merge | keep_iterating | revert
  "raw_result_dir": "tools/autotest/tournament-results/260720_0214_tournament-v2-vs-normal-2p",
  "notes": "one line"
}
```

Cycle cards are the atoms the REVIEW activity log links to. Naming is
sort-friendly (timestamp-first) and self-describing (scenario + sha).

---

## 9. Failure handling

### 9.1 Crash mid-batch

`run-tournament.sh` already has a per-match wall-clock **watchdog** that kills a
hung match and records "no verdict file" (`run-tournament.sh:231-276`). Two very
different causes produce "no verdict"; the manager **must** triage by reading
`match_<i>.log`:

- **Watchdog timeout** (log shows the game still ticking, killed at
  `MAX_WALL_SECS`): a stalemate that never resolved. **Acceptable but noteworthy**
  — treat as a `time_limit`-like non-result; if it recurs, the scenario's time
  limit or win rule needs tuning, not the AI. Does **not** block a merge on its
  own, but a batch full of timeouts is not a valid sample → re-run or fix the
  scenario.
- **Engine exception / stacktrace** in the log: a **crash**. This **blocks the
  merge** (§5.3). If the crash is on the candidate build → do not merge, root-cause
  it (a crash *is* an AI/engine bug worth a cycle). If it somehow reached `main`
  → revert on `main` immediately (§5.4).

A batch is **valid** only if a strong majority of matches wrote verdicts. Define
a floor (e.g. ≥ 80% of N) below which the batch is discarded and re-run rather
than scored.

### 9.2 Regression detected

Candidate build's median on a previously-passed scenario drops below its bar:

- **On the worktree** → don't merge; either iterate to recover or abandon the
  hypothesis. Log a `NOTE`.
- **On `main`** (a soft-merge that later proved regressive) → `git revert` the
  offending commit on `main`, log a `REVERT`, and open a `NOTE`/inbox flag if the
  cause is unclear. Because merges are soft and frequent (§5.2), catching
  regressions here is expected and cheap — the no-regression re-verification
  (§6.2) is the safety net before *advancement*, and periodic re-baselining of
  `main` against control catches soft-merge drift.

### 9.3 Budget / context exhaustion

The manager runs on autoburn, pacing against the Claude usage budget. It must
**never stop mid-merge or mid-batch in an inconsistent state.** When budget is
low or context nears compaction:

1. Finish the current atomic step (let the running match/batch complete or kill
   it cleanly; never leave `main` half-merged).
2. Commit all docs (cycle card, REVIEW updates).
3. Write a `NOTE` handoff line in the activity log: *"cycle interrupted at
   <step>; worktree at <sha>; resume by <next action>."*
4. Stop. The next manager bootstraps from this SPEC + REVIEW and resumes from the
   handoff line. No state lives in session memory — everything needed is on disk.

### 9.4 Map/positional bias

A single map favors one spawn. Guard against reading spawn luck as AI skill:
alternate P1/P2 assignment across seeds using the **mirror** scenarios
(`tournament-v2-vs-normal-mirror-2p`, faction/side swapped) via
`run-tournament.sh --mirror` — even seeds primary, odd seeds mirror
(`run-tournament.sh:169-177`). If Normal-vs-Normal on a scenario isn't ~50/50,
the map has bias that must be understood before trusting Experimental results
there.

---

## 10. How the system scales

- **New scenarios:** add a rung/scenario to [`LADDER.md`](LADDER.md) and a
  scenario folder under `tools/autotest/scenarios/` (copy an existing
  `tournament-*` folder; only `tournament.yaml` + `map.yaml` slots change). A new
  ladder metric usually needs **no engine change** — the verdict JSON already
  carries the full stats menu (§8.2); the metric is extracted post-hoc by the
  manager, not computed by a new engine scorer. Add an engine scorer only if a
  metric needs *in-match* win-rule behavior.
- **New maps (anti-overfitting):** the biggest long-term risk is tuning the AI to
  River Zeta's quirks. Once a rung is cleared, **add a second map** (new
  `tournament-<map>-2p` scenario) and re-run the cleared rung's scenarios on it.
  An AI that only wins on River Zeta hasn't generalized. Rotating seeds within a
  scenario guards against seed-overfit; rotating maps guards against map-overfit.
- **Parallelism (Mode B is active, so this is available now):** run **N hidden
  instances** concurrently with isolated support dirs (the harness's
  unbuilt Phase 3, findings Q2/Q5). Because the ~30s init cost dominates short
  matches, parallelism is the bigger throughput lever than raw speed — N
  instances ≈ 1/N wall-clock. Not needed to start; the loop is fully functional
  sequential.
- **Personality / difficulty reuse:** the same harness measures Rush-vs-Turtle or
  difficulty tiers by changing only the `matchup`/scenario — out of scope for
  this project but the substrate carries it for free.

---

## 11. Glossary + invariants (quick reference)

- **Experimental AI** = `ModularBot@v2`, the *only* thing being improved.
- **Control** = `ModularBot@normal` (Rush/Turtle also frozen) — never touched.
- **Rung** = one map's set of scenarios; cleared by the composite gate (§6.4).
- **Cycle** = one hypothesis tested to a verdict + a cycle card.
- **Mode A / B** = windowed-supervised / hidden-unsupervised (§3).
- **Invariants that must always hold:** no push to remote; controls
  byte-identical; no stat/balance edits; no crash on `main`; every batch
  git-SHA-stamped; every cycle produces a card; REVIEW.md is the single durable
  state across manager sessions.

---

## 12. Related docs

- [`LADDER.md`](LADDER.md) — the scenario ladder + the three River Zeta scenarios.
- [`REVIEW.md`](REVIEW.md) — the live review board (read every cycle).
- [`README.md`](README.md) — one-page orientation tying these together.
- [`../plans/260719_ai_benchmark_substrate_findings.md`](../plans/260719_ai_benchmark_substrate_findings.md)
  — the feasibility findings this spec is built on.
- [`../plans/260719_experimental_ai_poi_strategy.md`](../plans/260719_experimental_ai_poi_strategy.md)
  — the current Experimental-AI work (PoiMap, goal-guard, score-floating axes).
- [`../plans/260511_ai_tournament_harness.md`](../plans/260511_ai_tournament_harness.md)
  — the harness design this consumes.
- [`../../DOCS/reference/game-model.md`](../../DOCS/reference/game-model.md) — no
  factories, SR reinforcement model, cost = budget.

---

## 13. The Experimental / Stable bot pair (promotion policy)

There is no "V1", so the old "V2" name is retired. The lobby now offers five
bots; the loop touches exactly one of them.

- **Experimental AI** (`ModularBot@experimental`, `enable-ai-experimental`) — the
  optimization loop's **working bot**. Every hypothesis/cycle edits *these*
  modules. This is the bot under active development; it may be ahead of, behind,
  or sideways of Stable at any moment.
- **Stable AI** (`ModularBot@stable`, `enable-ai-stable`) — a **frozen snapshot**
  of the last *validated* Experimental config. Its modules under the
  `enable-ai-stable` gate in `mods/ww3mod/rules/ai/ai.yaml` are a byte-for-byte
  copy of the Experimental modules at the moment of the last promotion.
  **Two exceptions** are *shared*, not twinned: `PoiGoalGuard` and
  `MountedTransportBotModule` are fetched by consumers via a single-instance
  `player.TraitOrDefault<T>()` lookup, so a second trait instance on one player
  throws at runtime (`Actor player has multiple traits of type …`). They are
  therefore defined once, gated `enable-ai-experimental || enable-ai-stable`, and
  shared by both bots — i.e. **not** independently frozen. Every other strategic
  module (capture scoring, offense axes, garrison, adaptive production, layered
  defence, fixed-wing squad managers) *is* an independent `@stable` copy, so the
  bulk of the tunable surface is snapshotted. Making the two shared modules
  independently freezable would need an engine change (switch those lookups to an
  enabled-aware `TraitsImplementing<T>().FirstOrDefault(...)`); backlogged, not
  required for the pair to work.
- **Normal / Rush / Turtle** (`@normal` / `@rush` / `@turtle`) — the **frozen
  control AIs / measuring sticks** (§4.2, §11). The loop **never** updates them;
  if they drift, every historical result is invalidated.

**Promotion (the only time Stable changes):** when an Experimental change is
validated — it clears its benchmark bar **and** the user accepts it — copy the
Experimental module settings down into the matching `@stable` modules in
`ai.yaml`, keeping the two blocks' values identical at that instant. That copy is
a `NOTE`/`MERGE` activity-log event. Between promotions, Stable stays put while
Experimental keeps moving, so a player always has a known-good bot to select
while the loop churns on the experimental one. Promotion edits only the
`enable-ai-stable` block; it never touches Normal/Rush/Turtle.
