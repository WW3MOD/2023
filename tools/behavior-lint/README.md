# behavior-lint

Reads a per-unit `*.lifecycle.jsonl` event stream and flags AI unit
anti-patterns — idle units, call-in-and-forget, units that die untasked —
so "strange unit behavior" is detectable from logs instead of a human
watching a match.

- **Producer:** the `UnitLifecycleLogger` world trait
  (`engine/OpenRA.Mods.Common/Traits/World/UnitLifecycleLogger.cs`), off by
  default, enabled per-run via the `Test.UnitLifecycleLog` launch arg.
- **Schema & rule catalogue:** `WORKSPACE/behavior-lint-spec.md`.
- **Advisory only:** exits non-zero if any WARN fired (so a batch/CI can gate),
  but never speaks to a match's pass/fail verdict.

## Run

```bash
# Produce a log during an autotest match:
./tools/autotest/run-test.sh --lifecycle <test-folder>

# Analyze it (run-test.sh does this for you, but you can re-run by hand):
./tools/behavior-lint/behavior_lint.py ~/.ww3mod-tests/result.lifecycle.jsonl

# Drill into one unit's full timeline (spawn, every order, idle spans, death):
./tools/behavior-lint/behavior_lint.py <file> --actor 413
```

Flags: `--warn-only`, `--json`, `--csv`, and threshold overrides
`--idle-total N` / `--idle-span N` / `--r1-max-orders N` / `--min-life N`.

## First-slice rules

| Rule | Fires when |
|---|---|
| **R1** | A mover received ≤ 1 order over its whole lifetime (`--r1-max-orders`). |
| **R2** | `total_idle > --idle-total` (default 1500t ≈ 60s) **or** `longest_span > --idle-span` (default 750t ≈ 30s). |
| **R6** | A mover died and never appeared as an order subject. |
| **R8** | Per-type end-of-game idle census (survivors, idle-at-end fraction, median total idle). Descriptive, no threshold. |

All four rules apply to combat **movers** only — units with the spawn `mobile`
flag (Mobile/Aircraft). Structures are tracked (for order-target resolution) but
never flagged: a Supply Route being idle and untasked is expected, not a
pathology. R1/R6 also skip units that lived `< --min-life` ticks (default 250 ≈
10s), since a late call-in hasn't had time to be "forgotten".

R3/R4/R5/R7 (territory-abandon, spawn→first-order latency, order churn,
transport-parked) need the `sample` position track and are part of the full
build (spec §2d).

## Fixture / self-test

`fixtures/synthetic.lifecycle.jsonl` is a hand-written log exercising every
first-slice rule — plus an idle-all-game structure (must stay unflagged) and a
late call-in (must not trip R1). It is the analyzer's smoke test — no game run
required:

```bash
python tools/behavior-lint/behavior_lint.py tools/behavior-lint/fixtures/synthetic.lifecycle.jsonl
```

Expected output:

```
== behavior-lint: synthetic-demo seed=42  (players: 0=usa_modular 1=russia_modular) ==
WARN R1  aid=413 type=abrams owner=0  orders=1 (<= 1) over lifetime
WARN R1  aid=502 type=bradley owner=1  orders=0 (<= 1) over lifetime
WARN R2  aid=413 type=abrams owner=0  idle_total=1600t longest=1600t terr=own
WARN R2  aid=610 type=abrams owner=0  idle_total=1100t longest=1000t terr=contested
WARN R6  aid=502 type=bradley owner=1  died t=9001 orders=0
R8 end-of-game idle census (owner 0):
  abrams       alive=4   idle=1 (25%)  median_total_idle=560t
R8 end-of-game idle census (owner 1):
  bradley      alive=1   idle=0 (0%)  median_total_idle=40t
Summary: 5 WARN across 3 rules; 1 units flagged R6.
  drill:  ./tools/behavior-lint/behavior_lint.py <file> --actor 413
```

5 WARN (R1×2, R2×2, R6×1), exit code 1. Note the structure `supplyroute` (aid
900, idle the whole match, 0 orders) and the late call-in (aid 800) produce no
WARN and no census row.
