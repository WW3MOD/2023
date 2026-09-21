# DEFCON 3 (Positioning) borders for the nine derived-bisector maps

Eight maps authored, one skipped with cause. Base `main @ 442859aa`, branch `wt/map-borders`;
every figure below was measured statically at that ref, with no game launch. River Zeta
(`184680d4`) is the ninth authored map and is included in the table as the reference case.

The check that sees this work is new and is in the tree:

```
python tools/nav-guard/defcon_wall_audit.py --region-from-map --quiet --show-sealed
```

It reads the region each map declares in its own `rules.yaml`, runs the engine's own load
gate against it, then every locomotor. It exits 0 today on all nine. `make nav-guard` cannot
see any of this — it decodes `map.bin` and the `map.yaml` `Actors:` block and has no
`CustomTerrain` handling at all, so it is byte-identically green whether a map authors a
region or not.

## What was authored, and what it follows

| map | the line follows | band | route on real terrain | open comps | spawn→band | sealed ground | capturables (side A / side B) |
|---|---|---|---|---|---|---|---|
| **polar-disorder-ww3** | **the water** — in at the eastern feeder river, down the great northern lake, over the central spur, along the x64-70 cliffs, out through the southern lake | 444 | **123 of 148** (102 Water/River, 20 Cliffs) | 2 — 5133 / 3639 | 35 W, 28 E | 0 ground | W 10 (7 oilb, 2 logisticscenter, 1 gun) / E 6 (5 oilb, 1 gun) |
| **x-lake-ww3** | **the lake, one lobe west of the central causeway** — in at the northern inlet, through the twin cliff ridges, down the western lobe, out at the southern inlet | 484 | **107 of 164** (67 Cliffs, 40 Water/River) | 2 — 8304 / 7596 | 57/55 W, 55/63 E | **0 on every locomotor** | W 8 oilb / E 8 oilb + the central `bio` |
| **siberian-pass-ww3** | **the cliff shelves that wall the pass**, crossing the valley road at its narrowest | 301 | **69 of 101** (60 Cliffs, 9 Road) | 2 — 3048 / 2826 | 34 SW, 34 NE | 1 cell (48,21) | SW 10 (2 oilb, 4 brl3, 3 barl, 1 miss) / NE 10 (2 oilb, 5 brl3, 3 barl) |
| **woodland-warfare-ww3** | **the cliff line across the middle of the woods**, linked by the forest road net | 363 | **77 of 121** (65 Cliffs, 12 Road) | 2 — 4430 / 4423 | 41 N, 41 S | 0 | N 6 (4 oilb, 1 logisticscenter, `bio`) / S 5 (4 oilb, 1 logisticscenter) |
| **nuclear-winter-ww3** | **the two escarpments that terrace the valley** (x40-61 at y25-31 and y40-46), joined across open snowfield | 299 | **44 of 100** (40 Cliffs, 3 Road) | 2 — 3124 / 3577 | 35 SW, 35 NE | 0 | SW 5 (3 oilb, fcom, miss) / NE 6 (3 oilb, fcom, miss, mslo) |
| **seventh-woods-ww3** | **the north-east/south-west diagonal** the map's 180° spawn symmetry implies, picking up cliff shelves and rock belts | 606 | 67 of 202 | 2 — 6569 / 6377 | 35+48 NW, 42+41 SE | 1–2 cells (97,23) | NW 6 (3 oilb, 2 brl3, hosp) / SE 5 (3 oilb, 2 brl3) |
| **twin-rivers-ww3** | **the northern river mouth, then straight south at x60** — both rivers sit far off-centre and neither can carry a fair border | 399 | 24 of 134 | 2 — 8137 / 7340 | 58+57 W, 48+51 E | 2 cells, amphibious only | W 4 oilb / E 6 oilb |
| **arena-tank-duel** | **nothing — 2244 cells of Clear.** The vertical midline at x31-33, which is where the bisector already falls | 96 | 0 of 32 | 2 — 960 / 992 | 25, 25 | 0 | none on the map |
| _river-zeta-ww3 (ref)_ | _the river itself: Water + River + Bridge + 210 hand cells_ | _844_ | _634 by terrain type_ | _2 — 3525 / 3325_ | _11–28_ | _17–52_ | _7 / 7_ |

**skipped: `shellmap-open-field`** — see below.

"route on real terrain" counts cells of the routed chain that sit on Water, River, Cliffs or
Rock, i.e. the border the map already had rather than the border this work invented. "band"
is the dilated cell count actually written to `RegionCells`. "spawn→band" is Chebyshev
distance per spawn — the number that says whether both sides have the same distance to
march, which is the fairness property the derived bisector has by construction and an
authored border has to earn.

Previews with the band painted in the trait's own amber, spawns in white, Supply Routes in
cyan and capturables ringed yellow: **`WORKSPACE/mockups/borders/*-defcon3-border.png`**,
regenerate with `python tools/nav-guard/defcon_border_preview.py`.

## Why `shellmap-open-field` is skipped

**It can never run Escalation, so a border on it would be drawn for nobody.**

- `DefconWall.Apply` raises the wall only when `escalation.Level != NoLevel` and the level is
  in `ActiveLevels` (`DefconWall.cs:500-503`).
- `DefconEscalationState` pins `Level = NoLevel` for the whole match when the mode is
  `Skirmish` (`DefconEscalationState.cs:75-76`).
- The mode is a LOBBY OPTION, read in the `DefconEscalation` constructor from
  `LobbyInfo.GlobalSettings.OptionOrDefault(ModeOptionId, info.ModeDefault)`, and
  `ModeDefault` is `Skirmish` (`DefconEscalation.cs:63`, `:424`).
- The map is `Visibility: Shellmap` and NOT `Lobby` (`map.yaml:15`), and every lobby map
  chooser filters on `MapVisibility.Lobby` (`LobbyLogic.cs:1105`, `ServerCreationLogic.cs:105`,
  `MapCache.cs:416`). Nobody can select it and set the mode. Its only role is the menu
  background, and `Game.LoadShellMapInner` resets the session through `Disconnect()` →
  `JoinLocal()` and injects only the `scenario` option (`Game.cs:549-654`).

It is also 5704 cells of `Clear` with nothing to follow, and strips `-SpawnStartingUnits` and
`-ConquestVictoryConditions`, so it has no Supply Route at all.

**`arena-tank-duel` is in the same position and was authored anyway.** It is `Visibility:
Shellmap` only too — `6b162ca2` took both maps out of the lobby list together, for the same
reason — so the argument above applies verbatim and its border is very likely inert. It was
authored because it costs three lines, because it reproduces rather than replaces the derived
line (the bisector of `(6,16)` and `(58,16)` is x=32), and because it is then correct the day
the map goes back in the lobby. Skipping it instead is a reasonable call and is the user's.

## The two things that decided every line

**1. A region that divides nothing raises no wall at all.** `DefconWallRegion.IsDegenerate` is
`ComponentCount < 2` and `DefconWall.BuildRegion` *discards* a degenerate region — it does not
fall back to the derived line, and the only trace is one `Log.Write`. That gate floods the
Bounds rectangle with `Map.Contains` passability and nothing else, so it is strictly stronger
than any per-locomotor test: a region can separate all fifteen locomotors and still never
raise a wall. Every line above is checked against the gate first. All eight report exactly 2
components.

Method: a cell set separates the map in that 8-connected flood **iff** it contains a
4-CONNECTED chain of blocked cells from one Bounds edge to another. So the border is a
shortest path, not a min-cut — `tools/nav-guard/defcon_border_designer.py` runs Dijkstra over
Bounds pricing already-barrier terrain near zero and open ground ten times higher, then
dilates the chain by one cell. The dilation is why the band is three cells thick everywhere
except where Bounds clips it: that is the thickness the shipped `HalfWidth: 1024` produces and
it clears the √2⁄2 floor below which a diagonal band leaks through its own corners.

**2. `RegionTerrainTypes` is empty on all eight, unlike River Zeta, and that is a judgement
per map rather than a default.** On River Zeta the `Water` type *is* the divide: 588 cells,
one channel. On these maps it is not — polar-disorder's 2023 water cells are a system with two
north inlets and two south outlets, twin-rivers' 368 are 16 scattered components, x-lake's
1941 are one square lake whose lobes sit thirty cells either side of the crossing. Naming a
type would pull blobs nowhere near the border into the band and wash a fifth of the map in the
trait's amber fill, which is drawn over every border cell. Same measurement River Zeta made
when it rejected `Rock`.

## Sealed cells, and the one large figure

Rule (b) was "sealed ground ≈ 0". Five of the eight are exactly zero on every locomotor.
The residues:

- **siberian-pass 1 cell** at (48,21), **seventh-woods 1–2 cells** at (97,23): single nooks
  pinched against the band. Both maps already carry more pocketed cells than that in the
  nav-guard baseline (36 and 3).
- **twin-rivers 2 cells** at (62,21)/(63,21), and only for `lighttracked-amphibious` and
  `tracked-amphibious` — two water cells cut off from the rest of the channel.
- **polar-disorder 350 cells** for `immobilepara` **and for no other locomotor**. This
  describes nothing that happens in play: `immobilepara` belongs to `^SummonBase`
  (`defaults.yaml:1227`), whose `Mobile` has `Speed: 0`, `TurnSpeed: 0` and
  `PauseOnCondition: !parachute`. It is the descending-summon placeholder and never walks
  anywhere, so a connectivity number for it is not a fact about the ground. Its `TerrainSpeeds`
  are `Clear`, `Road`, `Beach` only, which is why it fragments where nothing else does.

For comparison, the **derived line** that these replace seals **256 cells** on twin-rivers
(x1-20, y104-126) on every ground locomotor, and 8–57 cells on x-lake. Those were re-measured
for this pass with `python tools/nav-guard/defcon_wall_audit.py --quiet --show-sealed`.

## Supply Routes on the Bounds edge

On six maps a Supply Route lands **outside Bounds** and therefore reads as `Unlabelled`:
`SpawnStartingUnits` places the base actor at `HomeLocation + CVec(-1,-1)`
(`MapStartingUnits.cs:37`), and after the 1-cell cordon (`097738f4`) a spawn at `x=1` puts its
SR at `x=0` while `Bounds` start at `1,1`. `DefconWallRegion.IndexOf` returns -1 there.

Affected: nuclear-winter `(0,63)`, polar-disorder `(0,80)`, seventh-woods `(0,32)` and
`(29,0)`, siberian-pass `(0,50)`, twin-rivers `(0,21)` and `(0,91)`, woodland-warfare `(0,3)`,
x-lake `(0,20)` and `(0,107)`. **This is pre-existing and not caused by this work** — it is
equally true of the derived line — and it is harmless for the border, because every one of
those SRs sits directly against its own spawn, and every spawn is correctly labelled on its
own side. But any future check that asserts "each SR is on its own side" must read the SPAWN
on these maps, or it will report ten false failures.

## Manager verification recipe

**There is no scenario that exercises any of these.** `test-defcon-wall` and
`demo-defcon-wall` are their own map packages under `tools/autotest/scenarios/` with their own
`rules.yaml`; they test the mechanism, never a shipped map's border. So this is a lobby check,
and it is the same four steps on every map:

1. Skirmish → pick the map → set the **DEFCON game mode** dropdown to **Escalation**. It
   defaults to Skirmish, which is a strict no-op: leave it and nothing will ever appear.
2. Leave "no-rush period" at its default and start. The match opens at DEFCON 3.
3. **What correct looks like:** a translucent amber band, three cells thick, appears over the
   feature named in the table — and nothing else is drawn. A region has no centre line and no
   hatch strokes (`DefconWall.RenderAnnotations` returns early for the region path); the band
   fill is the whole picture, unlike the derived line.
4. Order a tank across it: the cursor should refuse, and an order placed beyond it should be
   rejected rather than silently accepted. Then wait out the clock — at **DEFCON 2 the band
   must vanish completely** and the ground under it be passable again.

Per map, the thing to look at first:

| map | look here |
|---|---|
| polar-disorder-ww3 | the band should sit *in* the water for most of its length; the only long dry stretch is the central spur at x65, y28-37 |
| x-lake-ww3 | the band crosses the lake's waist just **west** of the central island; the island and its `bio` stay on the eastern side |
| siberian-pass-ww3 | the band should read as the cliff wall of the pass, cutting the valley road once |
| woodland-warfare-ww3 | a clean east–west line across the middle; check the derricks are reachable — the derived line buried two of them |
| nuclear-winter-ww3 | two horizontal shelves at y25-31 and y40-46 joined by vertical runs; the horizontal parts should sit on cliff |
| seventh-woods-ww3 | the weakest one — mostly open woodland floor; judge whether the diagonal reads at all |
| twin-rivers-ww3 | a straight vertical line at x60 after the northern river bends away; judge whether "honest midline" is acceptable here |
| arena-tank-duel | not reachable from the lobby; nothing to see unless the map is given `Lobby` visibility |

## Judgement calls the user may overrule

- **x-lake's central island.** The map is mirror-symmetric and the `bio` at (64,64) is dead
  centre, so *no* dividing border can be symmetric about it. The band runs one lobe west, which
  puts the island on the eastern side. The alternative — running the band down the mirror axis
  and through the island — was measured: it swallows the `bio` **into** the band and seals 34
  cells of ground from both sides on every vehicle locomotor. The stated acceptance criterion
  was "no capturable inside the band", so the western route shipped. If the symmetry matters
  more than the criterion, that is a one-line change.
- **twin-rivers is a straight line and says so.** Following the eastern river would put 53 of
  148 route cells on open ground instead of 121 of 134 — far prettier — and it is rejected
  because that river is 40 cells from the eastern spawns and 60 from the western ones.
- **seventh-woods is two-thirds invented.** 135 of 202 route cells are open woodland floor.
  The diagonal is chosen from the spawn layout (the four spawns are 180° rotations of each
  other), not from terrain, because this map has no terrain to choose from.
