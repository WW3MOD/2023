# `test-field-swallows-nuke` — the visual criteria

```bash
./tools/autotest/run-test.sh --hidden test-field-swallows-nuke      # expect PASS + 3 screenshots
```

No `--speed` and no `--timeout`. ~300 ticks, i.e. ~18 s of wall clock at `Timestep: 60`, far inside
the watchdog's 300 s (wall clock, **not** scaled by `--speed`). There is no discriminating twin: this
is not a predicate test, it is a rendering question, and the answer is three PNGs.

**The scenario's own verdict is only that it ran to the captures** — the field bed was under the
burst, the payload actually detonated, three shots were taken, and both witness vehicles are still
alive. A `PASS` here means "the frames are worth looking at". It does **not** mean the feature works.
That judgement is below, and it is the manager's.

## The three captures

| label | camera | zoom | what is in it |
|---|---|---|---|
| `disc-wide` | ground zero `33,16` | `Camera.MinZoom` | the whole ~25-cell scar disc with the 11×11 crop patch centred in it |
| `seam-closeup` | `38,16` | 2 | the field-to-bare boundary on row `y=16` |
| `witness-vehicles` | `39,15` | 2 | a humvee standing **on a field cell** inside the disc (`36,14`) and one on **bare** scarred ground (`42,16`) |

## PASS — all three must hold

1. **The blast disc is continuous across the crop patch.** No interruption at the patch edges, no
   square boundary where farmland begins.
2. **The same darkness over fields as over the bare scarred ground beside it.** The `seam-closeup`
   is the shot for this: `x38` is the last field cell and `x39`/`x40` are bare, and all three sit in
   the **same** `ScarChar` band (distance buckets 5, 6, 7), so they must read identically.
3. **No rectangular grid of unscarred green inside the disc.**

## FAIL — three ways, and they mean different things

1. **A bright green block inside the dark disc.** The feature is inert: the scar was placed on the
   terrain pass and the field sprite is drawn over it, exactly as
   `SmudgeLayerInfo.GroundCoverOverlay`'s own `[Desc]` describes the pre-feature state. Check that
   the five `SmudgeLayer@SCAR*` blocks in `world.yaml:505-540` still carry `GroundCoverOverlay: true`
   and that `^CivField` still carries `Passable.GroundCover: true`.
2. **Field cells visibly DARKER than adjacent scarred bare ground.** Double composite at the
   fringes: `^CivField` carries `RenderSprites.Scale: 1.15` (`civilian.yaml:270-272`), so a field
   sprite is larger than its own cell and overlaps its neighbours — a second draw of the same smudge
   at the same alpha can stack where the sprites overlap.
3. **Any scar pixel on top of a vehicle or infantry sprite.** The overlay's restriction is that a
   cell qualifies only when it holds at least one actor and *every* actor in it is ground cover, so
   a cell holding a humvee must be excluded. `witness-vehicles` is the shot for this, and
   `36,14` is the cell that matters: it is a field cell, inside the `ScarCrater` band, and would
   have taken the overlay if the vehicle were not there.

## Two things that are NOT failures

* **The outermost ring missing.** `Warhead@Scar5Rim` carries `Chance: 60`, so it is absent on ~40% of
  runs. It lands at distance buckets 11–12, **entirely off the crop patch** (the patch's farthest
  cell is a corner at √50 ≈ 7.07, bucket 8), so it plays no part in any criterion above.
* **Fires and smoke on the witnesses.** `Atomic` grants `onfire` out to 12 cells
  (`Warhead@Fire1..Fire10`). Cosmetic here.

## Why the geometry is not what the brief specified

The brief said to graft the patch at `x28–38 / y21–31` with ground zero at `33,26`, on the grounds
that `Atomic`'s outermost band (`ScarRim`, `Size: 12, 11`) "covers the whole patch from that cell and
fits the bounds". Two corrections, both verified by reading the weapon and the map:

* **It does not fit the bounds.** Radius 12 from `y=26` runs to `y=38` against `Bounds: 1,1,64,32`.
  The disc would be clipped along the south edge. The patch and the burst are therefore lifted ten
  cells north — `x28–38 / y11–21`, ground zero `33,16` — which puts the whole disc (`x21–45`,
  `y4–28`) inside the bounds with margin. Nothing else about the arrangement changed.
* **`ScarRim` is not the band that covers the patch, and it is the one band that might not be
  there.** `Map.FindTilesInAnnulus` buckets an offset by `ceil(√(dx²+dy²))`
  (`MapGrid.CreateTilesByDistance`), so Atomic's five `LeaveSmudge` warheads tile the disc as
  0–2 / 3–4 / 5–7 / 8–10 / 11–12 with **no gaps**. The patch's farthest cell is bucket 8, so the
  whole patch is covered by bands that all carry `Chance: 100`. That is a stronger guarantee than
  the brief assumed, and it is why criterion 1 is safe to state as an absolute.

## Why the captures are at detonation +140/+180/+220, not +10

The brief asked for "~10 ticks after detonation". At +10 the only scar on the ground is
`Warhead@Scar1Core` — a two-cell disc at `Delay: 0`. `ScarCrater` is `Delay: 13`, `ScarChar` 33,
`ScarBurn` 54, `ScarRim` 68, so the disc is not finished until **+68**. And `Warhead@Flash` is a
`FlashPaletteEffect` with `Duration: 30`, so a frame at +10 is a white screen with a dot in it.
Smudges are permanent, so there is no upper bound to trade against; +140 is past the last band, past
the flash and past the screen shake.

## Why it is `test-` and not `demo-`

`DEMO.md` is explicit that a demo stages and stops with no verdict. This rig has setup controls that
can genuinely fail and that nobody would otherwise notice until a human opened a blank screenshot
days later: the field bed under the burst (`Map.ActorsInCircle` from inside the polling predicate —
it returns **nothing** when called from `WorldLoaded`), the witnesses standing on the cells they are
supposed to, the payload actually having detonated (`Test.GetImpactEffectCount` must move), and both
witnesses still alive at the end. Those belong in a batch, red.

## A note on the sibling

`test-field-swallows-shell` already exists and is **not** this: it fires one `m109` artillery round
into an 11×11 `v14` patch and asserts the impact effect is not swallowed — the `CreateEffectWarhead`
half of the same family of bugs. It asserts nothing visual and detonates no nuke. The two are
complementary; do not fold one into the other.
