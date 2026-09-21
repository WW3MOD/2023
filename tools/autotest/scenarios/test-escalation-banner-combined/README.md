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
fail: §B6 -- the two edges landed N ticks apart, INSIDE the 66-tick hold window, so the player
should have had ONE banner naming both. Got raised=2 combined=False. This is the shipped defect:
the 2 -> 1 edge overwrote the DEFCON 2 banner and the player saw only OPEN WAR.
```

The separate arm is **unaffected** by that sabotage and still passes, which is itself informative:
it says the sabotage removed the combining and nothing else.

## What the scenario refuses to assume

* **The widget must be in the UI tree.** `Test.DefconBannerState()` returning `absent` is a
  `fail: SETUP`, not a verdict — it means the ingame player chrome did not load and the run can say
  nothing about banners either way.
* **The first banner must have been raised at all.** The 3 → 2 edge is asserted to produce
  `raised=1 combined=false` before anything else is read. A wrong reading there is not a §B6
  failure; it is the banner not working.
* **The opening `NoLevel → 3` edge must announce nothing.** Asserted at settle.
* **The ordered shot must be accepted.** `Test.ClickOrder` must return `Attack` on the tick the
  level reaches 2. DEFCON 3 refuses the click outright, so the returned order string doubles as
  proof the level really moved.
* **The arm must have taken the branch it was built for.** The lib asserts the *rule* against the
  measured gap in both arms, and then asserts the arm's own declaration (`arm.lua`) on top. Without
  the second check an arm whose staging drifted would quietly measure the other branch and still
  report PASS — two directories testing the same half, which looks exactly like a clean pair.

## An honest limit on what a green proves

`Test.DefconBannerState` reads **the widget's own state**, not pixels. It proves the player is
handed one band rather than two and that the band knows it is combined; it does not prove the band
is legible, that the combined cause line fits at 1024 px, or that the two `DefconAlert` sounds no
longer clip each other — **this change does not touch the audio at all, and the second alert still
lands one tick after the first.** Those are capture questions (`DOCS/recipes/SCREENSHOT.md`) and an
audio question, and neither is answered here.

## What counts as the answer

* **`-combined` PASS** — `[banner-combined] PASS.` in `lua.log` with `banner raised=1|combined=True`
  and `casualty attacker=USA|weapon=abrams|victim=t90|owner=Russia` in the census.
* **`-separate` PASS** — `[banner-separate] PASS.` with `banner raised=2|combined=False`.
* **`fail: ARM DRIFT`** — the banner obeyed the rule but the staging moved, so the pair no longer
  covers both branches. Check the t90's `Health.HP`; it is the only quantity that differs.
* **`fail: SETUP`** in either arm — a staging fault in both directories, not a finding.
