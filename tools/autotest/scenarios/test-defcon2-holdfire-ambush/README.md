# `test-defcon2-holdfire-ambush` and its discriminating twin

```bash
./tools/autotest/run-test.sh --hidden test-defcon2-holdfire-ambush            # expect PASS
./tools/autotest/run-test.sh --hidden test-defcon2-holdfire-ambush-skirmish   # expect OK(fail)
```

No `--speed` and no `--timeout`. ~450 ticks, i.e. ~27 s of wall clock at `Timestep: 60`, well inside
the watchdog's 300 s (wall clock, **not** scaled by `--speed`). **Both arms are required.**

## The mechanism

Read site 6 is `AutoTarget.TriggerNearbyAmbushAllies` (`AutoTarget.cs:1018`). It springs *other*
units by writing their `ambushTriggered` latch directly — never consulting their stance, never
touching their fire path — so one ambusher that is shot or spotted can light off a whole lane. This
scenario pulls it through the **damage** trigger: `INotifyDamage.Damaged` sets the victim's latch and
calls the proxy when the victim is in `Ambush` (`:709-713`), and the whole handler returns at its
**first line** while the hold is on.

The trigger is delivered by an **order** — the abrams is clicked onto the bait — because a shot
somebody ordered is exempt from the DEFCON 2 hold by construction. That is what lets both arms run
one script and differ by one quantity.

## Why the pair discriminates — and why the geometry is the interesting part

An ally fires only from `AmbushTickIdle`'s ungated stock branch:

```csharp
if (isSpotted || ambushTriggered)
```

The proxy contributes **only the second term**. So an ally the enemy can *see* would spring on its
own, and a control arm built that way would demonstrate nothing whatever about read site 6 — the
twin would go red for the wrong reason, which is the same failure as a twin that goes green.

The fix is the vision arithmetic, and it is the whole design:

| actor | `Detectable.Vision` | +1 stationary | cells from the probe | USA strength there | detected? |
|---|---|---|---|---|---|
| `Bait` (bmp2) | 2 (`^Vehicle` default) | 3 | 12 | 7 | **yes** — 7 > 3 |
| `Ally1/2/3` (t90) | **7** (set in `rules.yaml`) | 8 | 14–15 | 6 | **no** — 6 > 8 is false |

(`^StandardVision` bands: 7 @ 10–13c, 6 @ 13–16c. Detection needs the viewer's strength to *strictly
exceed* `Detectable.Vision`, `MapLayers.cs:576`.) Detection of an ally would need the probe inside
**7** cells — half the distance — so this is a margin, not a boundary. Firing grants
`firinganyweapon` (`VisionModifier: -1`), leaving the effective value at 7, i.e. still undetected at
14 cells: an ally that opens fire cannot retroactively justify itself.

So in the twin (`ModeDefault: Skirmish`, `Level = NoLevel`), the chain runs end to end — bait
damaged → `Damaged` handler proceeds → `ambushTriggered` + `TriggerNearbyAmbushAllies` → each ally's
latch is set → on its next idle tick it scans, finds the probe (Russia sees it: strength 6–7 against
the abrams' effective `Detectable` 3), and fires with `isSpotted` **false**. **An ally firing in the
twin can only have been sprung by read site 6.**

In the treatment the same click lands the same damage and the allies stay silent.

## What the scenario refuses to assume

* **The trigger must actually be delivered.** The bait's HP loss is asserted, not assumed. "A bug
  that cannot fire is indistinguishable from a bug that does not exist" — if the ordered shot never
  connects, the run reports a *scenario fault* rather than a green.
* **Attribution is re-checked every tick**, not once at setup. If any ally becomes visible to USA
  mid-run, the run fails with `fail: ATTRIBUTION` rather than producing a verdict it cannot support.
* **The allies must be able to shoot.** Range to the probe (`TankRound.T90`, 24c0/1c512) and distance
  to the bait (`AmbushCoordinationRadius`, 10 cells) are both asserted at setup. An ally out of
  either band would be silent for reasons that have nothing to do with the rule.
* **The bait's own return fire is checked ONCE, at the end of the window, and never earlier.** Return
  fire from the bait is read site **2**, not 6; letting it end the run early would let the twin trip
  a few ticks before the allies act and never demonstrate the proxy at all. This ordering is
  deliberate — do not "tidy" it into the per-tick block.

## An honest limit on what a treatment green proves

At DEFCON 2 the allies are held by **more than one** guard. Read site 6 stops the latch being
written; read site 1 (`AutoTarget.ChooseTarget`) independently returns `Target.Invalid`, so even a
latched ally would find no target — and on the stock branch an ally with no target then *clears*
`ambushTriggered` again. So this scenario proves **the lane does not light off**, which is the
player-visible claim and the one worth a regression test; it does not isolate site 6 as the sole
load-bearing guard. Where site 6 does look uniquely load-bearing is the **Stage-3 gated** path, whose
`SPRUNG` latch is terminal until a stance reset and therefore *persists* — a proxy write absorbed at
DEFCON 2 would spring on the transition to 1 with no fresh trigger. That path is not exercised here
(`enable-ambush-tactics` is not granted), and it is written up in `WORKSPACE/DISCOVERIES.md`.

## What counts as the answer

* **Treatment PASS** — `[defcon2-ambush] PASS.` in `lua.log` with `defcon=2`, every ally's trace
  reading `N->N seen=false`, and the bait's ammunition unmoved.
* **Twin `OK(fail)`** — the reason names an ally by index with its ammunition falling, `defcon=0`,
  and `seen=false` on that ally in the same census line. The `seen=false` is the part that makes it
  evidence about the proxy; read it, do not skip it.
* **Twin PASS** — red, and it falsifies the treatment. Check `seen=` on the allies first: if any
  reads `true` the geometry drifted and the scenario is measuring detection, not the proxy.
* **Either arm reporting `fail: ATTRIBUTION` or `fail: SETUP`** — a staging fault in both
  directories, not a finding.

`diff -r` the two directories before believing any result that rests on the comparison.
