# demo-tactical-positioning

Playable demo of the **StancePositioningExecutor**. Everything is on HoldFire, so nothing shoots and
the **only** thing that moves an idle unit is the executor.

> **This demo shows a behaviour human players no longer get.** Until 2026-08-30 the mod granted
> `enable-tactical-positioning` to every human-owned combatant, so the executor ran on your units in
> a normal game. That grant was removed — the cover shuffle read as units wandering off on their own
> — and the executor now ships for **experimental bots only**. This demo re-adds both halves of the
> enablement in its own `rules.yaml` so the layer stays demonstrable.

Launch:

```
./tools/autotest/run-demo.sh demo-tactical-positioning
```

Press **End** to restart; close the window when done. Use pause / speed to read the small steps.

## Three things to look for

1. **Cover-seek (LEFT, top — Zone A).** Three Defensive ARs start a few cells south of a treeline
   with an enemy tank sighted further south. At the start they walk **north** into the south
   (threat-facing) cover edge and hold hull-down. This is the small "shuffle into the trees" step.

2. **Threat response (RIGHT — Zone B).** Three Defensive ARs sit idle in the open with **no enemy in
   range** — they do not move. A scripted enemy tank drives up from the south; once it is close
   enough to register on the per-player threat field, the ARs take the treeline edge facing it. The
   probe then withdraws and the ARs are pushed back to the open, so the approach → reposition cycle
   repeats. On-screen `DEMO:` messages call out each phase.

3. **Opt-out freeze (LEFT, bottom — Zone C).** The same treeline + sighted-enemy geometry as Zone A,
   but this group is opted out: three ARs are on **HoldPosition** and one Defensive AR carries the
   **`deployed`** condition. None of them ever reposition — the freeze holds even with the enemy
   sighted right in front of them.

Contrast Zone A vs Zone C directly: identical setup, but A seeks cover and C stays put.
