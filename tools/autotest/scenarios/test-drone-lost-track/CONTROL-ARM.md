# Control arm for `test-drone-lost-track`

The two arms must differ by **exactly one quantity**, because the thing being measured is a
*preference* rather than a binary. If they differ by anything else, a clustering difference has more
than one available explanation and the run stops being evidence.

## Selecting an arm — there is no edit any more

The two arms are two scenario directories, and you pick one by running it:

```bash
./tools/autotest/run-test.sh --hidden test-drone-lost-track           # treatment
./tools/autotest/run-test.sh --hidden test-drone-lost-track-control   # control (RED)
```

Nothing to revert, in either order, and **no rebuild** — it is mod YAML, read at map load. The
arms share one Lua body at `mods/ww3mod/scripts/drone-lost-track-lib.lua`; the control directory
differs from the treatment by exactly one stanza in its own `rules.yaml`:

```
Player:
	DroneOperatorBotModule@experimental:
		IntelSampleInterval: 999999
```

**Why this replaced the old procedure, which was to edit `mods/ww3mod/rules/ai/ai.yaml` and then
`git checkout` it back.** That edit was correct and it worked; what it could not be was *safe* on
this machine. It mutates a file every scenario in the repo reads, and it has to stay mutated across
a launch that is serialized against other people's launches — so any run that started while the
control was in flight silently got a bot with the drone feature switched off, and nothing in its
result would say so. A forgotten revert leaves the same landmine in the working tree indefinitely.
The per-scenario override carries the same single quantity with none of that reach.

**What the split does NOT change: the arms must still differ by exactly one quantity.** The
directories are byte-identical except for that stanza and the title/comment text — `diff -r` them
before believing any result that depends on the comparison. If someone edits one `rules.yaml` and
not the other, a clustering difference gets a second available explanation and the run stops being
evidence, which is the whole failure this file exists to prevent.

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

## 2026-09-15: the rig should now go GREEN, and the reason is reach rather than weight

`test-drone-lost-track`'s `expected-status` declaration is **deleted** as of this change. It
declared a by-merit `fail` and its own closing line said to remove it in the same commit as
whatever made the operator prefer the contact. (`pass` is not a declaration you can write:
`expected-status.sh:115` rejects it with *"'pass' is the default and declares nothing; delete
the file"*, so the evidence lives here instead.)

**What changed is that a lost contact became reachable at all.** The hover ceiling was 22 cells
against the operator's own 28-cell verifying radius, so the drone could never come within 6
cells of a contact that had genuinely gone dark. Three constants moved together:

| | was | now |
|---|---|---|
| `quadcopterdrone` `CarrierSlave.MaxDistance` (enforced leash) | 25 | 33 |
| `DroneTargeter` `Range` (hard cap on tasking) | 25c0 | 33c0 |
| `LeashCells`, both profiles (the bot's model) | 25 | 33 |
| **`MaxHoverDistanceCells` = min(33, 33−3)** | **22** | **30** |

**`LostTrackIntelSquares` is unchanged at 250.** The measured 1.315× shortfall sits inside the
pre-registered ≤1.5× band and was never the lever. If this rig goes green, it is because the
drone can reach the contact — do not read it as evidence that the term was mis-sized.

**Predicted score at the first launch**, from the constants and the numbers already logged:

```
V is 30 cells from the operator, so the ceiling of 30 now admits V itself.
  IntelFalloff(247, 0, 28) = 247 * 29/29 = 247      candidate exactly on V
  IntelFalloff(247, 1, 28) = 247 * 28/29 = 238      nearest grid candidate, if 2-cell spacing misses V
  worth_hunt  = reveal_hunt (~14, measured at 39,51) + 238..247  =  252..261
  worth_expl  = 248 + 0                                          =  248
```

So the hunt cell wins by **4 to 13 squares**, and `score = worth*1000 − poiDistanceCells` puts
the POI tie-break (hunt ~9 cells from the derrick, the old winner ~29) on the same side. **This
is a narrow margin and it is honest about being narrow**: if the grid lands 2 cells off V rather
than 1, the term loses again.

**Two boundary conditions that could still refuse the cell, neither of which is the term.**
`cellDist(18,45 → 47,54)` is `floor(sqrt(922))` = **30**, and the candidate test is
`(cell - opCell).Length > maxHover`, so V is admitted with **exactly zero cells of margin** — any
future change to the leash, the margin or the weapon puts it back outside. And V must still
clear `MaxPoiDistanceCells` (40): it is ~9 cells from the derrick at 38,53, so that one is safe.

**The control's expected cell is NOT necessarily 29,25 any more, and this is the part to read
before calling a control run wrong.** `ChooseTargetCell` scans a `(2·maxHover+1)²` box, which
grew from 45² = 2025 to 61² = 3721 candidates. The control's argmax is the best *reveal* in that
larger set, and ground 30 cells out was not previously a candidate at all. **The RED criterion is
therefore `records=0 intel=0`, verdict `fail`, and the drone not going to V — not the literal
cell `29,25`.** A control that fails at some other exploration cell is a clean RED.

| Arm (scenario to run) | `IntelSampleInterval` | Expected today | Log signature |
|---|---|---|---|
| Control (RED) — `test-drone-lost-track-control` | 999999 | **FAIL** — drone prefers the dark region (cell may differ from 29,25; the box grew) | `records=0`, `intel=0` on every launch |
| Treatment — `test-drone-lost-track` | 25 | **PASS expected** since the 2026-09-15 leash fix — V is reachable, predicted worth 252-261 vs 248 | `records>0`, `intel>0`, `intelkey` naming the truk |

**Both arms FAILED until 2026-09-15, and that was the measured state, not a broken scenario.** The
majority-of-samples bar is the design intent and has not been lowered.

**The batch is kept green by the declaration, not by the verdict.** This scenario ships an
`expected-status` file declaring `fail`, so FAIL grades GREEN and the verdict stays honest —
see `tools/autotest/expected-status.sh`. An earlier revision of this branch instead softened
the verdict to `Test.Skip`; that is now wrong twice over, because under a `fail` declaration
SKIP grades **RED** ("declared fail, skips instead"). If the operator ever does prefer the
contact, delete the declaration file in the same commit — a PASS against a live `fail`
declaration is loudly RED on purpose, meaning the premise moved.

Discrimination survives where it matters: **`PASS` still means only one thing.** If anyone
raises `LostTrackIntelSquares`, the treatment goes green while the control stays FAIL. And
a control that ever comes back `PASS` is still the ambiguity tell described below.
The arms remain separable in the numbers even while their verdicts match — the treatment
reaches `mindist=25` against the control's `27`, which is also the only tell that separates
this recorded outcome from a *new* targeting regression.

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
always at the **closest candidate to the vanish cell**, which the leash fixes at **8** cells
(V sits 30 cells from the operator against a 22-cell leash; run 8 measured
`bestintelcell=39,51`, exactly 8 out):

> `247·(28−8+1)/29 − 247·(28−28+1)/29 = 178 − 8 = **170 squares**`

For a hunt cell to win it needs `reveal_hunt + 178 > 307 + 8`, i.e. `reveal_hunt ≳ 137`. It
did not win, so **`reveal_hunt < 137`** — and the multiplier the constant would need is
`(307 − reveal_hunt)/170`. Since `reveal_hunt ≥ 0`:

> **the shortfall is strictly between 1.006× and 1.806×.** Reading `intel=34` against
> `reveal=307` understates the term ~5×, because 34 is its value at the cell that won
> (25 cells out), not the 178 it reaches at the hunt cell it was competing for.

**Two corrections are folded into the block above, and both matter.** It used to read
`~11 cells`, `249` and a displacement of `146`, giving a bound of 1.0×–2.10×. The distance
was the falloff evaluated at the wrong range; `IntelSquares` resolves to **247**, not 249
(see the run-8 section below). The corrected upper bound is **1.806×**, which — unlike 2.10×
— sits *just past* the pre-registered ≥1.8× "real shortfall" line rather than far beyond it.
Do not quietly round that back under the threshold.

**This bound is still box-era in its numerator.** `reveal=307` predates `1e0226b9`; the
displacement `170` is intel-space and survives, but `307` does not. Treat 1.006×–1.806× as
the shape of the answer, not the answer.

The exact multiplier needs one number nothing logged: the revealed area *at the best-intel
cell*. The launch line now carries `bestintel=`, `bestintelcell=` and `bestintelreveal=`
(diagnostic only, no decision reads them), so:

> `multiplier = (reveal − bestintelreveal) / (bestintel − intel_at_reveal_argmax)`

**Verdict thresholds, recorded before run 8 and not moved after it:** ≤1.5× means the term
is roughly right and this scenario is simply a hard case — change nothing. ≥1.8× is a real
shortfall. In between is a judgement to argue in the commit, not silently round up. Setting
the constant to exactly the value that flips this scenario is tuning-to-pass and is the same
move as lowering `FreshSightingTicks` would have been.

### Run 8 — the derivation stands, the NUMBER is withdrawn (stale as of `1e0226b9`)

**Read this heading before the table below it.** The method here is sound and worth keeping;
its output is not usable, and the band was never actually entered.

`1e0226b9` (2026-09-02, after this run) replaced the drone's rectangular revealed-area query
with the vision **disc**, removing up to 228 squares of corner credit per candidate — about
19× `MinRevealedSquares`. The multiplier's numerator is `reveal − bestintelreveal`, and
**both** of those were computed by the box. Both lose corners, so the direction of the change
is not predictable from the old numbers: the multiplier below could move either way and must
be re-measured before any claim about sizing is made. "≤1.5×, change nothing" was therefore
never earned.

What survives is intel-space, which the disc change did not touch: `bestintel=178`,
`intel=34`, the `IntelSquares = 247` resolution, and the geometry.

```
launch cell=35,31 reveal=307 border=0 intel=34 intelkey=32
       bestintel=178 bestintelcell=39,51 bestintelreveal=95 records=1 nearby=1 tick=200
```

`border=0` confirms the exploration term is not inflated by non-playable border squares — but
that is a *different* artefact from the box corners, which `border=` never counted and which
`1e0226b9` removed separately. `border=0` does **not** make `307` disc-comparable.

**The multiplier does not depend on locating the reveal argmax** — this is the part of the
method worth reusing on the re-measured run. The winner `35,31` beat the argmax `A`, so
`reveal(W) + 34 ≥ 307 + intel(A)`, and `reveal(W) ≤ 307` forces `intel(A) ≤ 34`. Sweeping the
whole admissible range gave:

| `intel(A)` | multiplier (box-era, **withdrawn**) |
|---|---|
| 0 | 212/178 = 1.191 |
| 8 (a 28-cell argmax, the observed case) | 212/170 = 1.247 |
| 34 (upper bound) | 212/144 = 1.472 |

Every admissible value was ≤1.5× **under the box**. Redo this sweep with disc-era `reveal`
and `bestintelreveal` before concluding anything; the argmax-independence argument carries
over unchanged, the arithmetic does not.

The only bound derivable **without** a new run is the weak one derived above — strictly
between 1.006× and 1.806× — and even that rests on a box-era `reveal=307`. It straddles the
≤1.5× line and reaches just past the ≥1.8× one. **The sizing question is open.**

`IntelSquares` is **247**, not the 249 previously assumed — it is the only value satisfying
both `floor(sq·4/29)=34` and `floor(sq·21/29)=178`, and it corresponds to age 71–80, i.e.
`LastSeenTick` 120–129. The last belief pass before the t140 kill was ~t125, so the contact
is *older* than the kill tick. That is the one-sided error predicted when the timing was
chosen, resolving in the safe direction. `floor(247/29) = floor(249/29) = 8`, so the
denominator was unaffected.

**Scope, and it is narrower than any number here looks.** This is one point, not a
calibration. `reveal=307` is this map's strongest exploration alternative; `bestintelreveal=95`
is the terrain around one hunt cell; and the 8-cell closest approach follows from V sitting 30
cells out against a 22-cell leash. That geometry is close to **worst case for the term**: a
contact nearer the operator would be reachable at lower falloff — at 0 cells the value is the
full 247 rather than 178 — so the term would win comfortably. So whatever the re-measured
shortfall turns out to be, it is an **upper bound on the general case**, and must never be
read as *"the term is N% too small in general"* nor as *"the term is correctly sized"*.

**What the re-measurement run must capture, because run 8 did not.** The multiplier needs
`bestintelreveal=` off the `[drone] ... launch` line (`DroneOperatorBotModule.cs:734`). That
line is engine-side and the Lua verdict string cannot carry it. Run 8's directory held only
PNGs and `result.json`, and the global `debug.log` was truncated by a later launch before
anyone read it — so a run that reached a real verdict still could not answer the question it
was spent on. `run-test.sh` now archives `debug.log` into the run dir for every outcome:
read `RUN_DIR/debug.log`, never the global file.

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
