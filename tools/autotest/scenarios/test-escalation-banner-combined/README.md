# `test-escalation-banner-combined` and its discriminating twin

```bash
./tools/autotest/run-test.sh --hidden test-escalation-banner-combined   # expect PASS
./tools/autotest/run-test.sh --hidden test-escalation-banner-separate   # expect PASS
```

No `--speed` and no `--timeout`. The combined arm reaches its verdict in ~220 ticks and the
separate arm in ~470, i.e. well under half a minute of wall clock each and far inside the
watchdog's 300 s. **Both arms are required, and both are PASSES** — see "Why the pair is two
greens" below, because that is unusual here and is deliberate.

## The mechanism

DEFCON 2 is the phase the whole mode exists to dramatise, and it ends on the first casualty — so
in a real match it is **short**. Measured at **98 ticks (5.9 s)** in run `260920_010605_p1901`
and at **one tick** in run `260915_012829`, which went 3 → 2 at tick 5000 and 2 → 1 at 5001.

`DefconTransitionBannerWidget` held one `shownLevel` and the next edge **overwrote** it, while the
band itself is held for `DefconReadoutModel.BannerHoldTicks` = 66 ticks. So in the common case the
player saw `OPEN WAR` alone: the mode's signature phase was invisible, and the two `DefconAlert`
sounds landed on consecutive ticks and clipped each other. §B6 of the 2026-09-19 escalation review,
and its ruling is that this is the *whole* remedy — §B1's geometric fix was measured and **rejected**
(b1a made the phase *shorter*, 38 ticks against 98).

The fix draws **one** band naming both causes and how long the phase lasted, and raises an event-log
line naming who fired. Both halves are asserted here.

## Why the arms differ by a hit point total and nothing else

The sequence is identical in both directories: the 150-tick no-rush clock runs out, the abrams is
clicked onto the t90 **on that same tick**, and the t90 dies. What differs is how long dying takes,
and that is one number in `rules.yaml`.

| arm | `t90 Health.HP` | rounds needed | kill lands | gap vs the 66-tick hold |
|---|---|---|---|---|
| `-combined` | **8000** | 1 | ~20 ticks after the edge | **inside** |
| `-separate` | **40000** | 2–4 (`BurstWait` 130) | ≥130 ticks after the edge | **outside**, by 2× or more |

**Both numbers are chosen for a BAND, not for an exact round count**, and that is deliberate.
`TankRound.Abrams` is 20000 `TargetDamage` + 3000 `SpreadDamage` nominal, scaled by range falloff
(`DamageAtMaxRange: 50` over `Range: 25c0`, so ~92% at four cells) and then by whatever `Versus`
applies to the t90's armour — call it 15000–25000 per round. 8000 dies to any reading of that; 40000
survives the first round under every reading and dies inside the script's 600-tick budget under every
reading. Neither arm depends on predicting the number exactly.

No Lua constant, no clock and no geometry differs between the arms; `diff -r` them before believing
any result that rests on the comparison.

## Why the pair is two greens rather than a treatment and a red twin

The `-skirmish` twins of the hold-fire scenarios are *controls*: they exist to prove that something
could have made the treatment red. That shape does not fit here, because **both branches of this
rule are correct behaviour**. Combining edges that are 260 ticks apart would be a real defect — it
would put a sentence about a finished phase on screen while the war is already on, which is exactly
the "queueing" option §B6 rejects. So the separate arm is not a control that must fail; it is the
second half of the rule, and it is what stops the fix over-reaching.

The RED that makes the combined arm's green mean something is therefore a **sabotage**, not a
directory:

```
# in DefconReadoutModel.CombinesWithPrevious, replace the body with:
return false;
```

Expected failure text, from the combined arm:

```
fail: §B6 -- the two edges landed N ticks apart (DEFCON 2 at A, DEFCON 1 at B), INSIDE the 66-tick
hold window, so the player should have had ONE banner naming both -- but the engine says
combines=no. This is the shipped defect: the 2 -> 1 edge overwrote the DEFCON 2 banner and the
player saw only OPEN WAR.
```

The separate arm is **unaffected** by that sabotage and still passes, which is itself informative:
it says the sabotage removed the combining and nothing else.

## Why the observable is the MODEL and not the banner widget — this cost a run

The first version asserted the widget's own counter and **failed on its first run**
(`260921_165856`): `raised=0` on the very tick DEFCON reached 2. The cause is structural, not a
one-frame lag, and it rules widget counters out of autotests entirely:

> `Ui.Tick` **does** run under `--hidden` — `Game.cs:786-790` puts it in the *logic* tick, not the
> render path. But it runs on `Ui.Timestep` = **40 ms of wall clock** (`Widget.cs:30`), which is a
> different clock from the world's (`OrderManager.cs:187`). A `--hidden` autotest sets
> `Graphics.CapFramerate=false` and the sim free-runs, so the world advances many ticks between two
> UI ticks — and a DEFCON 2 that lasts 98 ticks, or one tick, can pass **entirely** between them.

So a widget-derived count is a fact about one client's sampling rather than about the match.
`Test.DefconEdgesCombine` is computed from `DefconEscalation.LevelReachedTick`, written in synced
code on the tick each level is first reached, and is what both arms assert.

**The same reading changed the shipped code, not just the test.** The widget now derives "do these
two edges combine" from the transition *record* rather than from the edges it happened to witness —
so a client that hitches across both edges gets the combined band too, instead of reproducing §B6's
exact defect from a second cause nobody had noticed.

## What the scenario refuses to assume

* **The model must be there.** `Test.DefconEdgesCombine()` returning `absent` is a `fail: SETUP`.
  The widget is deliberately **not** required — it is diagnostic, and a run that cannot see it still
  produces a real verdict.
* **Neither edge may already be recorded at settle.** Asserted as `pending`.
* **The engine must have recorded both edge ticks.** A missing one fails naming
  `DefconEscalation.RecordLevel`, not the banner.
* **The two arithmetics must agree.** The script computes `gap < 66` itself and cross-checks it
  against the engine's `DefconEdgesCombine`. They agree only if `BannerHoldTicks` really is 66 ticks
  at this game speed — i.e. only if the 1.5× tick-rate error is not live in it.
* **The ordered shot must be accepted.** `Test.ClickOrder` must return `Attack`. It is issued **four
  ticks after** the edge, never on it: `CeaseAutonomousFireEverywhere` runs on exactly that tick and
  the first run caught the abrams mid `AttackActivity(cancelling)` — the DEFCON 3 auto-acquisition
  being torn down, since `DefconHoldsFire` is false at 3 and only `Armament.CanFire` is gated there.
* **The arm must have taken the branch it was built for.** The lib asserts the *rule* against the
  measured gap in both arms, and then asserts the arm's own declaration (`arm.lua`) on top. Without
  the second check an arm whose staging drifted would quietly measure the other branch and still
  report PASS — two directories testing the same half, which looks exactly like a clean pair.

## An honest limit on what a green proves

A green proves the **rule** fired correctly on real match data: these two edges, this far apart, this
answer. It does **not** prove anything about the band on screen. The widget reads
`CombinesWithPrevious(LevelReachedTick(upper), LevelReachedTick(level), hold)` — the predicate is
pinned by NUnit (`DefconReadoutTest`, the §B6 block, 13 cases) but the *wiring* that feeds it
recorded ticks rather than witnessed ones is not reachable from `OpenRA.Test` and ships verified by
reading. Legibility, the combined line's fit at 1024 px, and whether the two `DefconAlert` sounds
still clip each other are all unanswered here — **this change does not touch the audio at all, and
the second alert still lands one tick after the first.** Those are capture questions
(`DOCS/recipes/SCREENSHOT.md`) and an audio question.

## What counts as the answer

* **`-combined` PASS** — `[banner-combined] PASS.` in `lua.log` with `banner raised=1|combined=True`
  and `casualty attacker=USA|weapon=abrams|victim=t90|owner=Russia` in the census.
* **`-separate` PASS** — `[banner-separate] PASS.` with `banner raised=2|combined=False`.
* **`fail: ARM DRIFT`** — the banner obeyed the rule but the staging moved, so the pair no longer
  covers both branches. Check the t90's `Health.HP`; it is the only quantity that differs.
* **`fail: SETUP`** in either arm — a staging fault in both directories, not a finding.
