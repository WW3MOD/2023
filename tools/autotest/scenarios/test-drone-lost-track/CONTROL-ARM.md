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

## How this could come back ambiguous — read before interpreting

**The control's exploration target could land near the vanish cell by chance.** The hover disc has
radius 22 around the operator; the vanish cell is 30 cells away; so cells lying within
`NearVanishCells` (18) of the vanish cell *and* inside the disc do exist, in the south-east of the
disc. The control's argmax is deterministic (fixed nested scan order, no RNG), so it will not vary
between runs — but if it happens to sit in that overlap the control will "pass" and there is no RED.

That is a result to **report and stop on**, not to re-run and not to fix by moving the threshold after
seeing the data. The tell is the trace in the summary plus `intel=0` in the launch line: a control that
passes with `intel=0` has landed on the contact by exploration geometry alone, which means this map's
geometry cannot separate the hypotheses and the scenario needs new coordinates.

## What this scenario does NOT measure

`Bounds: 0,0,98,82` equals `MapSize`, so this map has **no non-playable border** and the `border=`
diagnostic will correctly read ~0 throughout. That is the diagnostic working, not failing — but it
means this run cannot size the border artefact. That needs a map where `Bounds` is strictly smaller
than `MapSize`, which is most real maps.
