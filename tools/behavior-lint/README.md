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
`--idle-total N` / `--idle-span N` / `--r1-max-orders N`.

## First-slice rules

| Rule | Fires when |
|---|---|
| **R1** | A called-in unit received ≤ 1 order over its whole lifetime (`--r1-max-orders`). |
| **R2** | `total_idle > --idle-total` (default 1500t ≈ 60s) **or** `longest_span > --idle-span` (default 750t ≈ 30s). |
| **R6** | Unit died and never appeared as an order subject. |
| **R8** | Per-type end-of-game idle census (survivors, idle-at-end fraction, median total idle). Descriptive, no threshold. |

R3/R4/R5/R7 (territory-abandon, spawn→first-order latency, order churn,
transport-parked) need the `sample` position track and are part of the full
build (spec §2d).

## Fixture / self-test

`fixtures/synthetic.lifecycle.jsonl` is a hand-written log exercising every
first-slice rule. It is the analyzer's smoke test — no game run required:

```bash
python tools/behavior-lint/behavior_lint.py tools/behavior-lint/fixtures/synthetic.lifecycle.jsonl
```

Expect 5 WARN (R1×2, R2×2, R6×1), the R8 census, and exit code 1.
