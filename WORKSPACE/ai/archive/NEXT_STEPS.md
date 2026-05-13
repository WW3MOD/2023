# AI Project — Next Steps (handoff doc)

> If you're a fresh agent told "continue working on the bots", read this
> in full before doing anything else. Should take 5 minutes.

## State of the world

The **measurement infrastructure** (tournament harness) is **shipped**.
You can A/B-test any AI change in ~30 min wall-clock with statistical
confidence. The **AI brain overhaul itself has not started.**

What that means concretely:

- The bots that run today are the existing `Normal AI` / `Rush AI` /
  `Turtle AI` — same modules they had before 260511. No "v2 brain" code
  exists yet; `ModularBot@v2` in YAML currently mounts identical
  modules to `@normal`.
- The harness is ready. Drop in a new bot module behind
  `RequiresCondition: enable-ai-v2`, run a mirror-paired batch, see
  whether it helps.

## Read these (in order, 10 min total)

1. **`WORKSPACE/ai/WAKEUP_CHECKLIST_260512.md`** — quick orientation,
   step-by-step.
2. **`WORKSPACE/ai/sanity_findings_260512.md`** — the empirical baseline
   you'll be measuring AI changes against. Russia 60% / America 40% at
   n=20 (mild, borderline-significant edge).
3. **`WORKSPACE/ai/foundation_260511.md`** — the architecture. Three
   layers: Perception / Strategy / Tactics. Five phases. **You are
   sitting between Phase 1 (done) and Phase 2 (the first real AI
   work).**
4. **`WORKSPACE/ai/tournament_workflow.md`** — usage cookbook for the
   harness. Memorize the "mirror-paired benchmark" recipe.

If anything in these contradicts what you find in the code, the code
wins — but flag it to the user before changing the docs.

## What to do first

**Step 1 (5 min): resolve `foundation_260511.md` §7 open questions with
the user.** Six design decisions are still open and they affect the
direction of Phase 2 work:

1. Foundation reset aggressiveness — evolve in place vs greenfield brain
2. Difficulty levels — N tiers, naming, what each tier means
3. Honest fog vs omniscient AI
4. Waypoint/Planning mode coupling
5. Per-map opening books
6. Allied AI behavior in coop

Ask the user **before writing any AI code**. They can defer most of
these by saying "your call" — but the first one matters now.

**Step 2: pick a small, measurable AI change for the first v2 module.**
Something concrete from `foundation_260511.md` Phase 2 (Strategic
intent layer) — for example:

- **Cleanest first move:** implement the `MapAnalyzer` in
  `engine/OpenRA.Mods.Common/AI/Perception/MapAnalysis/`. Compute
  regions + chokepoints once at world load. Don't wire it to a bot
  module yet — just expose the data + a debug overlay. This validates
  the perception layer's architecture before the harder strategic
  planner.

- **Lazier first move:** tweak one knob in `ai.yaml` under
  `RequiresCondition: enable-ai-v2` (e.g. a different
  `AdaptiveProductionBotModule@v2` with `EvaluationInterval: 300`
  instead of `600`). Measure via mirror batch. This validates that the
  harness can detect a 5-point shift.

The lazy move is the better Phase 2 *entry point* — it proves the
"measure via harness, then iterate" loop works before any heavy
architectural code.

**Step 3: every change goes through this loop.**

```bash
# 1. Write the change. Either:
#    - YAML knob in ai.yaml under enable-ai-v2-only, OR
#    - New engine module in OpenRA.Mods.Common/AI/

# 2. Build the engine.
make all

# 3. Run mirror-paired batch (10-15 min wall-clock).
./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p \
    --seeds 20 \
    --config tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-quick.yaml \
    --mirror tournament-arena-mirror-2p

# 4. One-line summary for commit message.
./tools/autotest/tournament-report.sh tools/autotest/tournament-results/<latest>

# 5. Compare against the baseline.
./tools/autotest/compare-batches.sh \
    tools/autotest/tournament-results/260512_0849_tournament-arena-skirmish-2p \
    tools/autotest/tournament-results/<your-new-batch>

# 6. If the delta favors v2, commit. Otherwise, revert and try
#    something else — but commit a `:think:` markdown note in
#    WORKSPACE/ai/ documenting what didn't work and why.
```

## What's already done — don't redo

- **Don't rewrite the tournament harness.** It works. Use it.
- **Don't add per-module measurement infrastructure.** The IMatchScorer
  interface is the swap point.
- **Don't change the existing `@normal/@rush/@turtle` bot module
  configs unless explicitly told to.** Those are the legacy baseline.
  v2 changes go under new `enable-ai-v2` conditions.
- **Don't `make all` while a batch is running.** PITFALL §11 — mid-launch
  mmap race kills matches.

## Engine entry points you'll need

For new perception code:
- `engine/OpenRA.Mods.Common/AI/` — make this folder; subfolders for
  `Perception/`, `Strategy/`, `Tactics/` per `foundation_260511.md` §4.

For new bot modules (the executors that consume Strategy decisions):
- `engine/OpenRA.Mods.Common/Traits/BotModules/` — existing. Add new
  ones here following the existing pattern (see `ScoutBotModule.cs`).
  Gate on `RequiresCondition: enable-ai-v2`.

For tournament-harness-side additions (new scorers / win rules):
- `engine/OpenRA.Mods.Common/Tournament/Scorers/` and `WinRules/`.
  Read `WORKSPACE/ai/tournament_swap_guide.md` first.

## Critical references

- **`DOCS/reference/supply-route.md`** — read before *any* strategic-AI
  work. SR is a fixed sector beachhead, NOT a buildable factory. This
  misunderstanding has cost me hours.
- **`WORKSPACE/ai/PITFALLS.md`** — 18 traps already hit. Skim before
  touching the harness.
- **`CLAUDE.md` "How WW3MOD Differs from Red Alert"** — the canonical
  mental model. Re-read on first session if unsure.

## Decisions I made overnight that you can revisit

If any of these don't sit right, the user should be told.

- **Used `world.Timestep` override (Round 5) for speed multiplier**
  instead of real headless renderer (Phase 2 plan). Result: 3×
  practical speedup. Real headless is days of work; this was the 90%
  solution.
- **Built mirror-pairing into the harness as the standard pattern.**
  All real benchmarks should run with `--mirror`. Single-sided batches
  have wide sample-bias variance that mirror cancels out.
- **Kept the old `ModularBot@normal` baseline alive in YAML** — v2
  changes layer on top via `enable-ai-v2` conditions rather than
  replacing normal. Lets you A/B test cleanly.

## Things that DIDN'T survive

If you find references to these, they're stale:

- ~~84% USA / 16% Russia~~ — was specific-seed sample bias at n=19.
  The corrected mirror-paired finding is 40/60 america/russia at n=20,
  not statistically significant.
- ~~Wait for headless renderer~~ — Phase 2 of the harness plan; not
  pursued; framerate cap is the substitute.

## When you're stuck

- Run a 1-seed smoke against the current code:
  `./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p
   --seeds 1 --config <smoke.yaml>` — verifies harness still works.
- Read the most recent `match_*.watcher.log` in any batch dir for
  per-tick score progression. Useful for "is my new module even doing
  anything?"
- `git log --oneline --grep='ai:' -30` shows the chain of overnight
  work plus any work after; the commit messages explain each round.

## Last commit before this handoff

```
77c20cde ai: morning summary final — 22+ commits, harness ready for AI overhaul
```

That's the bookmark. Anything before it is the autonomous overnight
work; anything after is post-handoff.
