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

## Runs 5 and 6 both FAILED identically, and the diagnosis is a tier, not a threshold

Both arms reached a real verdict with both markers and identical traces: `samples=85 near=0
mindist=27 firstdrone=t285`, launching at 33,29 with `reveal=307`, control `intel=0`,
treatment `intel=2`.

**`intel=2` was the wrong TIER being read, not the term being too weak.** `IntelSquares`
(`DroneTaskingMath.cs:186`) returns `areaSquares` (60) — the *currently-observed* tier — for
any contact aged `<= FreshSightingTicks` (50, `ai.yaml:955`). The scout died at t175 and the
only evaluation that matters is at t200, so the contact was 25 ticks old: still "under
observation". 33,29 sits 28 cells from the vanish cell, the exact rim of `DroneVisionCells`,
where `IntelFalloff` gives `60 * (28-28+1) / (28+1) = 2`. Both observed values reproduce to
the digit and neither is consistent with the lost tier:

| Launch | distance to V | fresh tier (60) | lost tier | observed |
|---|---|---|---|---|
| t200 `cell=33,29` | 28 | **2** | 8 | `intel=2` |
| t1600 `cell=39,53` | 8 | **43** | 84 | `intel=43` |

t1600 reads *fresh* as well because the wandering `e3.america` at 21:46 is 27 cells from the
vanish cell — inside the strength-2 band — so from ~t1250 the truk is **re-observed**, not
merely erased. The lost-track ramp this scenario exists to test was never read at either
launch. `SpawnTick`/`KillScoutTick` moved to 25/140 so the contact is 60 ticks old at t200;
the derivation and the two constraints that nearly collide are in the Lua.

**The t1600 launch is not the mechanism working.** The control, with `records=0 intel=0`,
chose the same cell with the same `reveal=252` — that target is the revealed-area argmax.
No drone flew it either: the first sortie docked just after t1545 and `RearmTicks` left ~100
ticks to run when the armament fired at ~t1650, so the shot was wasted. See the
`DeadlineTicks` comment for why raising it would manufacture a double-PASS.

**The falsifier fired and resolved clean.** `20 of 85 samples follow it` decomposes exactly:
samples run t285–t1545 contiguously (14 outbound, 57 hovering at 33,29, 14 returning), and
the 20 after t1251 are the last 6 hover samples plus all 14 of the return leg. Every one
belongs to the sortie ordered at t200. Zero came from a second sortie.

## Run 7: the lost tier is being read, and `reveal=` is NOT the winner's reveal

```
CONTROL   cell=33,29 reveal=307 intel=0  records=0 tick=200
TREATMENT cell=35,31 reveal=307 intel=34 records=1 tick=200
```

`intel=34` is `249 · (28−25+1)/29` — the lost tier at 25 cells. The fresh tier would give 8.
The timing fix landed and the arms diverged for the first time, so the term demonstrably
moves the argmax.

**Do not read the two `307`s as a tie between the two winning cells.** `reveal=` on the
launch line prints `bestReveal` (`DroneOperatorBotModule.cs:726`), which is the maximum
revealed area over *every* candidate scanned — there is no `chosenReveal`. So both arms
reporting 307 says only that the best exploration on offer was 307 in both, which is
expected because the worlds are identical up to t200.

What *is* derivable: the control's score is `reveal·1000 − poiDistance` with intel 0
everywhere, so its winner **is** the reveal argmax — `33,29` has reveal exactly 307. In the
treatment that same cell carries `intel = 249·(28−28+1)/29 = 8`, so `worth = 315`. The
winner `35,31` carries intel 34, so its own reveal `r` satisfies `r + 34 ≥ 315`:

> **281 ≤ r ≤ 307** — intel overcame a reveal deficit of somewhere between **0 and 26** squares.

## Sizing `LostTrackIntelSquares` — the bound you can already state, and the one you cannot

The term's *displacement power* is its value at the best hunt cell minus its value at the
reveal argmax. `IntelFalloff` is monotonically decreasing in distance, so `bestIntel` is
always at the **closest candidate to the vanish cell**, which the leash fixes at ~11 cells:

> `249·(28−11+1)/29 − 249·(28−28+1)/29 = 154 − 8 = **146 squares**`

For a hunt cell to win it needs `reveal_hunt + 154 > 307 + 8`, i.e. `reveal_hunt ≥ 161`. It
did not win, so **`reveal_hunt < 161`** — and the multiplier the constant would need is
`(307 − reveal_hunt)/146`. Since `reveal_hunt ≥ 0`:

> **the shortfall is strictly between 1.0× and 2.10×. It is NOT an order of magnitude,**
> and that bound needs no further run. Reading `intel=34` against `reveal=307` understates
> the term ~4×, because 34 is its value at the cell that won (25 cells out), not the 154 it
> reaches at the hunt cell it was competing for.

The exact multiplier needs one number nothing logged: the revealed area *at the best-intel
cell*. The launch line now carries `bestintel=`, `bestintelcell=` and `bestintelreveal=`
(diagnostic only, no decision reads them), so:

> `multiplier = (reveal − bestintelreveal) / (bestintel − intel_at_reveal_argmax)`

**Verdict thresholds, recorded before run 8 and not moved after it:** ≤1.5× means the term
is roughly right and this scenario is simply a hard case — change nothing. ≥1.8× is a real
shortfall. In between is a judgement to argue in the commit, not silently round up. Setting
the constant to exactly the value that flips this scenario is tuning-to-pass and is the same
move as lowering `FreshSightingTicks` would have been.

### Run 8 — measured: 1.19×–1.47×, inside the "roughly right" band

```
launch cell=35,31 reveal=307 border=0 intel=34 intelkey=32
       bestintel=178 bestintelcell=39,51 bestintelreveal=95 records=1 nearby=1 tick=200
```

`border=0` confirms the exploration term is not inflated, so 307 is real ground.

**The multiplier does not depend on locating the reveal argmax.** The winner `35,31` beat
the argmax `A`, so `reveal(W) + 34 ≥ 307 + intel(A)`, and `reveal(W) ≤ 307` forces
`intel(A) ≤ 34`. Sweeping the whole admissible range:

| `intel(A)` | multiplier |
|---|---|
| 0 | 212/178 = 1.191 |
| 8 (a 28-cell argmax, the observed case) | 212/170 = **1.247** |
| 34 (upper bound) | 212/144 = 1.472 |

Every admissible value is ≤1.5×. **No constant moves.**

`IntelSquares` is **247**, not the 249 previously assumed — it is the only value satisfying
both `floor(sq·4/29)=34` and `floor(sq·21/29)=178`, and it corresponds to age 71–80, i.e.
`LastSeenTick` 120–129. The last belief pass before the t140 kill was ~t125, so the contact
is *older* than the kill tick. That is the one-sided error predicted when the timing was
chosen, resolving in the safe direction. `floor(247/29) = floor(249/29) = 8`, so the
denominator was unaffected.

**Scope, and it is narrower than the number looks.** This is one point, not a calibration.
`reveal=307` is this map's strongest exploration alternative; `bestintelreveal=95` is the
terrain around one hunt cell; and the 8-cell closest approach follows from V sitting 30
cells out against a 22-cell leash. That geometry is close to **worst case for the term**: a
contact nearer the operator would be reachable at lower falloff — at 0 cells the value is
the full 247 rather than 178 — so the term would win comfortably. The honest claim is *"in a
deliberately hard geometry it falls about 25% short of overriding the strongest available
alternative"*, not *"the term is correctly sized"*.

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
