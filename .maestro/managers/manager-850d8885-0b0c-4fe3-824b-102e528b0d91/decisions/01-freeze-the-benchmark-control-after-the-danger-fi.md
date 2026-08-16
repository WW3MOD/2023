# Freeze the benchmark control AFTER the danger fix lands, not before

_Recorded 2026-08-11T15:58:26.761Z by 17dc66e4_

## Context

User directive (2026-08-11): "we can reset stable to todays version now, before anything else, and start working on improving the experimental against it, setting a new baseline now."

The intent is clear and correct: the yardstick has been moving (`@stable` was silently promoted to full `@experimental` parity on 2026-08-02 at `b8d2e601`, so every number in `WORKSPACE/benchmarks/` was measured against a bot that no longer exists). The user wants a fixed opponent and a measurable improvement curve.

## The obstruction

`DangerFieldLayer` is a **world trait** — a single instance at `mods/ww3mod/rules/world.yaml:368`. Its `DurabilityBase` / `HealthDivisor` / `CostDivisor` are global Info fields. There is no per-profile instance and therefore **no way to gate a danger-field correction to `@experimental` only**. Any fix to the danger scale moves the field for BOTH bots simultaneously.

Meanwhile the durability weight is confirmed RA-scaled (worker `79cbaf5a`, reproduced `ReferenceIntensity` offline to an exact match against the live log). Fixing it moves the reference only −19.5%, but drops armour-type contribution by ×0.05–0.12, so armour-dominated field cells fall up to ~19×.

## The options

- **(a) Baseline now, then fix.** Honours the instruction literally. Produces a control that is invalidated within hours by a change already in flight, and burns ~60 min of matches measuring a field known to be wrong. This is precisely what decision C3 of the 08-10 plan warned against.
- **(b) Fix the danger scale first, then freeze and baseline.** The control is frozen on a correct field and genuinely stops moving, because every subsequent `@experimental` change IS gateable.

## Decision: (b), with a cheap concession to (a)

Full frozen-control baseline waits until the danger scale is corrected and re-derived. But a **short single-rung run (~10 matches, ~20 min) is taken immediately** as the BEFORE half of an A/B on the danger fix itself — the largest behavioural change in flight and currently entirely unmeasured. That captures the measurement value the user is asking for without spending an hour on a yardstick that breaks the same day.

This deviates from the literal instruction, so it is recorded here rather than done silently. The user explicitly delegated: "do what must be done… you are the one to decide."

## Consequence for the gating policy

Commit `875c93c1` settled "`@stable` inherits improvements, never gate off on purpose." Freezing a control opponent is in tension with that. Resolution: the frozen control is a **named snapshot profile used only as a benchmark opponent**, not `@stable` itself. `@stable` keeps inheriting improvements as policy requires; the control is a separate pinned thing. Mechanism to be scoped before the full baseline run.
