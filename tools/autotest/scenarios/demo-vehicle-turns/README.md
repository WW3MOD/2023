# demo-vehicle-turns

Stages the PIPELINE item 27 turn-feel tuning (YAML-only) so a human can *feel* the difference.

## Run

```sh
./tools/autotest/run-demo.sh demo-vehicle-turns
```

A game window opens (foreground, audio on). Close it when done — the demo writes no result.

## What it stages

Four USA lanes, each looping a horizontal serpentine of 90° corners. All units are HoldFire with no
enemies, so the only motion on screen is the turn behaviour under test.

| Lane | Unit | Locomotor | Why it's here |
|---|---|---|---|
| 1 (top) | `abrams` ×3 | heavytracked | MBT column — clearest read of "columns look less robotic" |
| 2 | `bradley` | lighttracked | IFV |
| 3 | `humvee` | lightwheeled | fast wheeled scout |
| 4 (bottom) | `TRUK` | wheeled | unarmed supply truck on the `^Vehicle` template default turn |

## What to watch

The **corners**. This tuning is two knobs:

- `^Vehicle` **TurnSpeedLoss 1 → 0**: vehicles stop bleeding speed *inside* a turning arc, so they carry
  pace through the sweep instead of stuttering to a crawl and re-accelerating on the exit.
- **raised Mobile TurnSpeed** (per-unit + templates): the hull swings onto the new heading sooner, so
  the pivot at each jog is shorter.

## Before/after

The course, `Speed` and `Acceleration` are untouched — only the turn knobs changed. To compare, run
this demo on `auto/vehicle-turn-feel`, then `git stash` the tuning commit (or check out `main`) and run
it again: any difference at the corners is exactly this change.
