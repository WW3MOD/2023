# BALANCE — stat dashboard + AUTOTEST-driven tuning

**Trigger:** `BALANCE <unitA> <unitB>` for a duel comparison; `BALANCE <topic>` for broader tuning (e.g. `BALANCE artillery range falloff`, `BALANCE ammo costs T5`).

**Gives you:** data-driven tuning instead of vibes-based YAML edits. Two tools, clean separation:

| Tool | What it answers | Source of truth |
|---|---|---|
| `tools/combat-sim` (dashboard) | "What does this unit/weapon look like? How does it compare?" | live YAML via `--dump-balance-json` |
| `tools/test/run-test.sh test-balance-*` (AUTOTEST) | "Who wins this fight? At what HP? How fast?" | the engine itself (in-game scenario) |

Stat drift between dashboard and game = structurally impossible (the dashboard reads the engine's resolved Ruleset). Combat-outcome drift between dashboard and game = N/A, because the dashboard never simulates combat.

**When *not* to use it:** balance changes you've already decided on with high confidence and just want to apply. Just edit the YAML.

---

## What I do

### 1. Refresh stats (one-time per session, after any YAML edit)

```bash
./tools/combat-sim/scripts/dump-stats.sh
# → tools/combat-sim/data/stats.json regenerated from live YAML
```

The script warns if you haven't run it after recent YAML changes. It also gates on JSON validity so a busted dump can't silently replace a good one.

### 2. Inspect stats with the dashboard

```bash
cd tools/combat-sim
node build/index.js units                  # list combatant actors
node build/index.js compare abrams t90     # side-by-side
node build/index.js actor abrams           # full stat dump
node build/index.js weapon tankround.abrams
node build/index.js dps abrams             # sustained DPS, dmg/credit
node build/index.js tier-cost              # cost-vs-power table
```

Use these to:
- Find stat outliers (cost too high, DPS too low for the cost class)
- Confirm symmetry between faction counterparts
- Compute derived metrics without re-deriving by hand

### 3. Verify with AUTOTEST

For "who wins?" / "how fast?" / "what HP%?" use the in-game test harness. It runs the actual engine, so it catches everything the dashboard's static math can't (positioning, autotarget jitter, projectile travel, suppression, AI behavior).

```bash
./tools/test/run-test.sh test-balance-tank-1v1
./tools/test/run-test.sh test-balance-arty-1v1
./tools/test/run-batch.sh test-balance-tank-1v1 test-balance-ifv-1v1 ...
```

Each test reports `WINNER=X | ttk=Ys | survivors=N/M | hp=H/MAX (P%)`. Verdicts are deterministic per-seed so re-runs are identical — for variance work, parameterise the scenario or add tests at multiple ranges.

### 4. Recommend tuning, then re-test

Write the proposed YAML edit, apply, **re-run dump-stats.sh** (the dashboard would otherwise lie), then re-run the relevant `test-balance-*` to confirm the change lands where intended.

---

## When data conflicts with feel

Dashboard says symmetric, AUTOTEST says symmetric, but the user's playtest says lopsided? **Trust the playtest** — the harness is missing context (positioning, fog, AI quirks, multi-unit dynamics). File as a TRIAGE item and dig in.

If dashboard says X, AUTOTEST says Y: that's an interesting finding — the engine's combat math (damage formula, AutoTarget priority, hit calc) is doing something the static stats don't predict. Worth investigating; usually points at a non-obvious engine path.

---

## Two-layer drift, both fixed

Pre-260511 the combat-sim was a TypeScript port of the engine's combat math + a hardcoded copy of unit/weapon stats. Both halves drifted: stats by 5-15× from real YAML, combat math by enough that sim verdicts didn't match in-game. Refactor scrapped both:

- **Stats**: dump from engine via `--dump-balance-json`, sim reads JSON. No re-implementation.
- **Combat outcomes**: AUTOTEST runs the engine. No re-implementation.

The dashboard's only computed numbers (DPS, dmg/credit) are derived directly from the dumped stats using simple cycle math (`burst × damage / cycle_ticks`). No engine fidelity required for that — it's a presentation layer.
