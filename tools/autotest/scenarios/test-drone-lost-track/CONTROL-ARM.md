# Control arm for `test-drone-lost-track`

The two arms must differ by **exactly one quantity**, because the thing being measured is a
*preference* rather than a binary. If they differ by anything else, a clustering difference has more
than one available explanation and the run stops being evidence.

## The edit

One value in `mods/ww3mod/rules/ai/ai.yaml`, under `DroneOperatorBotModule@experimental`:

```
IntelSampleInterval: 25        # treatment (shipped default)
IntelSampleInterval: 999999    # control
```

Run the control, then revert with `git checkout mods/ww3mod/rules/ai/ai.yaml`. **No rebuild** — it is
mod YAML, read at map load.

## Why this knob and not `LostTrackIntelSquares: 0`

`LostTrackIntelSquares: 0` looks like the obvious off-switch and is actively wrong twice over:

1. **It inverts the decay rather than disabling it.** The lost tier is
   `areaSquares + (lostSquares - areaSquares) * remaining / span`. With `lostSquares` below
   `areaSquares` that term goes negative, so the value *rises* from 0 at the moment of loss to 60 two
   minutes later — the control arm would actively prefer cold trails to warm ones.
2. **It leaves two of the three tiers running.** Statics still score `StaticIntelSquares` (20) and
   currently-visible mobiles still score `AreaIntelSquares` (60). The arm would still carry an intel
   preference, just a differently-shaped one.

`IntelSampleInterval` is the only *single* knob that zeroes the whole term. `intel` is written only by
`SampleIntel`, which runs only behind that countdown, so the table stays empty, `BestIntelAt` returns 0
for every candidate, and `ScoreCandidate` receives `intelSquares: 0` everywhere — tasking falls back to
pure revealed-area staleness.

**Precisely what that reproduces:** the pre-change behaviour *up to* the old `ContactBonus: 2000`, which
was worth two revealed squares against a term reaching ~841 and is the term this whole change exists
because it could never decide anything. It is not a bit-identical revert and should not be described as
one.

## Expected results

| Arm | `IntelSampleInterval` | Expected | Log signature |
|---|---|---|---|
| Control (RED) | 999999 | **FAIL** — drone prefers the dark region | `records=0`, `intel=0` on every launch |
| Treatment | 25 | **PASS** — majority of samples near the vanish cell | `records>0`, `intel>0`, `intelkey` naming the truk |

`records=0` in the control's `[drone]` lines is the arm's self-identifying marker: if a control run
shows `records>0`, the edit did not take and the run must be discarded rather than interpreted.

## First check the run actually ran — this is not optional

A Lua abort and a real failure both arrive as `status: "fail"`. The first attempt at this RED reported
one that looked exactly like the control result it was meant to produce; only the verdict *wording*
gave it away (`Fatal Lua Error: Actor 'player' does not define a property 'Location'` is the engine's
phrasing, not this scenario's).

The scenario now brackets itself with two markers recorded in `result.json`'s `screenshots[]` array,
which `TestMode` writes synchronously (`TestMode.cs:294-308`) even though the PNGs land async:

| `screenshots[]` contains | Meaning |
|---|---|
| neither marker | the script never loaded — do not interpret the status at all |
| `00-script-loaded` only | loaded, then either aborted mid-run **or** failed setup validation in `WorldLoaded`; tell them apart by whose wording the verdict carries |
| `00-script-loaded` **and** `99-verdict-reached` | reached a verdict through the sampling path under its own power — the status means what it says |

Both rules stay in force together: check the markers, and check that the verdict text is one this
scenario authored. The markers make it an artefact check rather than a discipline.

## The confound guard is armed only until the drone exists

Run 4 was voided at ~t1200 by a technician 27 cells from the vanish cell — genuinely inside
the radius at which a standard ground unit verifies that cell, so genuinely erasing the
contact. It was also **1000 ticks after the only decision the run measures.**

The launch decision resolves in a single tick. `ChooseTargetCell` reads the intel table and
the order goes out as one unqueued `ForceAttack`, which `CarrierMaster` turns into a one-shot
`MoveTo` (`CarrierMaster.cs:190`); the drone has no `Armament`, so the retarget loop at `:138`
is unreachable and `TaskOperator` early-returns while a slave is out. **An airborne drone's
destination is immutable**, so contamination after the launch cannot reach the verdict.

The guard therefore fails hard only while `firstDroneTick < 0`, and records after that:

| Log reads | Meaning |
|---|---|
| `lateintruder=none` | the guard looked after the window closed and saw nothing |
| `lateintruder=t<tick> <who> (ignored: …; N of M samples follow it)` | contamination arrived after the decision. **Ignored on purpose.** Not a void |
| verdict `SETUP INVALID: a friendly gained vision … BEFORE any drone launched` | a real void — the decision itself was contaminated |

`N of M samples follow it` is the number that would falsify that reasoning. Every sample should
belong to the sortie ordered *before* the contamination. Today that holds by roughly 50 ticks:
`ReturnAfter` (1000) + the return flight + `RearmTicks` (150) + the ~300-tick docked window put
the earliest possible SECOND launch at ~t1775, whose drone spawns after `DeadlineTicks`. **If
`N` is a large fraction of `M`, a second sortie targeted from erased belief is feeding the
statistic and the run is not evidence.** N of 0-2 is expected and harmless.

## How this could come back ambiguous — read before interpreting

**The control's exploration target could land near the vanish cell by chance.** The hover disc has
radius 22 around the operator; the vanish cell is 30 cells away; so cells lying within
`NearVanishCells` (18) of the vanish cell *and* inside the disc do exist, in the south-east of the
disc. The control's argmax is deterministic (fixed nested scan order, no RNG), so it will not vary
between runs — but if it happens to sit in that overlap the control will "pass" and there is no RED.

That is a result to **report and stop on**, not to re-run and not to fix by moving the threshold after
seeing the data. **The guard narrowing above does not change this and makes it more likely to be
seen**, not less: the control now runs its full sample window instead of being cut short at ~t1200,
so an argmax sitting in that overlap now gets the chance to show up as a passing control. The tell is the trace in the summary plus `intel=0` in the launch line: a control that
passes with `intel=0` has landed on the contact by exploration geometry alone, which means this map's
geometry cannot separate the hypotheses and the scenario needs new coordinates.

## What this scenario does NOT measure

`Bounds: 0,0,98,82` equals `MapSize`, so this map has **no non-playable border** and the `border=`
diagnostic will correctly read ~0 throughout. That is the diagnostic working, not failing — but it
means this run cannot size the border artefact. That needs a map where `Bounds` is strictly smaller
than `MapSize`, which is most real maps.
