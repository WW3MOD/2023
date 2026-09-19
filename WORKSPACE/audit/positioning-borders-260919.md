# DEFCON 3 (Positioning) borders for the nine derived-bisector maps

**STATUS 2026-09-19: WORK IN PROGRESS, PAUSED BY THE USER. No map has been authored yet.**
Base `main @ 442859aa`, branch `wt/map-borders`. Nothing under `mods/ww3mod/maps/` has been
touched. What exists is the authoring method, the measurements below, and one finished
candidate route (polar-disorder). Resume instructions are at the bottom.

## The one map already done, for reference

`river-zeta-ww3` (184680d4): `RegionTerrainTypes: Water, River, Bridge` + 210 hand cells
closing four dry crossings. Its `map.yaml` had to gain a `Rules: rules.yaml` line, because a
rules file the map never declares is silently ignored (`Map.cs:102-107`).

## A map that should NOT be authored: `shellmap-open-field` (settled)

**It can never run Escalation, so a border on it would be drawn for nobody.** The chain:

- `DefconWall.Apply` raises the wall only when `escalation.Level != NoLevel` and the level is
  in `ActiveLevels` (`DefconWall.cs:500-503`).
- `DefconEscalationState` pins `Level = NoLevel` for the whole match when the mode is
  `Skirmish` (`DefconEscalationState.cs:75-76`).
- The mode is a LOBBY OPTION read in the `DefconEscalation` constructor via
  `LobbyInfo.GlobalSettings.OptionOrDefault(ModeOptionId, info.ModeDefault)`, and
  `ModeDefault` is `Skirmish` (`DefconEscalation.cs:63`, `:424`).
- `shellmap-open-field` is `Visibility: Shellmap` and NOT `Lobby` (`map.yaml:15`), and every
  lobby map chooser filters on `MapVisibility.Lobby` (`LobbyLogic.cs:1105`,
  `ServerCreationLogic.cs:105`, `MapCache.cs:416`). So no player can ever select it and set
  the mode. Its only role is the menu background, where `Game.LoadShellMapInner` resets the
  session through `Disconnect()` then `JoinLocal()` and injects only the `scenario` option
  (`Game.cs:549-654`) -- the DEFCON mode stays at its `Skirmish` default.

Its terrain is 5704 cells of `Clear` and nothing else, so there is no feature to follow
either. It is also stripped of `-SpawnStartingUnits` and `-ConquestVictoryConditions`
(`shellmap-open-field/rules.yaml`), i.e. it has no Supply Route at all.

## A map the same argument covers, and the brief did not anticipate: `arena-tank-duel`

**`arena-tank-duel` is in exactly the same position and this needs a ruling before it is
authored.** It is `Visibility: Shellmap` only as well -- both were taken out of the lobby
list together in 6b162ca2 ("Take the two developer maps out of the lobby map list"), for the
same reason: both strip `-ConquestVictoryConditions` and `-SpawnStartingUnits`, so a player
who picked either got a map with no Supply Route and no way to win. It is reachable by name
through `Game.LoadMap` (that is how the combat-sim fixture loads it), but that path supplies
a default session too, so the mode is `Skirmish` there as well.

Its terrain is 2244 cells of `Clear` and nothing else -- no water, no cliffs, no roads, no
rough. Spawns `(6,16)` and `(58,16)`; the only sensible border is the vertical line `x=32`,
which is *precisely* what the derived bisector already draws. So authoring it would replace
the derived line with an identical hand-drawn one, on a map that never raises a wall.

**Recommendation: skip both, author the seven that remain** (nuclear-winter, polar-disorder,
seventh-woods, siberian-pass, twin-rivers, woodland-warfare, x-lake). Not acted on -- the
brief said nine and named only the shellmap as a candidate skip, so this is the user's call.

## Method (implemented in `tools/nav-guard/defcon_border_designer.py`)

A set of cells separates the map in the 8-connected graph `DefconWallRegion.Label` floods
iff it contains a 4-CONNECTED chain of blocked cells from one Bounds edge to another. So the
border is a shortest-path problem: Dijkstra on a 4-connected grid over Bounds, with per-cell
cost making terrain a player already reads as a barrier nearly free (`Water`, `River`,
`Cliffs` = 1) and open ground expensive (`Clear`, `Road` = 10). The route therefore *follows*
the map's real features and crosses open ground only where the map forces it. The chain is
then dilated by one cell, giving a three-cell band -- the same thickness the shipped
`HalfWidth: 1024` produces, so an authored band is never thinner than the line it replaces.

**`RegionTerrainTypes` is deliberately NOT used on these maps, unlike River Zeta.** On River
Zeta the `Water` type IS the divide (588 cells, one linear channel). On these maps it is not:
polar-disorder's 2023 water cells are a sprawling lake system with two north inlets and two
south outlets, twin-rivers' 368 are in 16 scattered components, x-lake's 1941 are one central
square lake whose two lobes sit far to either side of the natural crossing. Naming the type
would drag in blobs nowhere near the border and paint a fifth of the map amber. Hand cells
only. (This is the same measurement River Zeta made when it rejected `Rock`.)

## Measurements taken (all static, no launch, at 442859aa)

| map | size | spawns | terrain the border can use |
|---|---|---|---|
| arena-tank-duel | 66x34 | 2 | none (100% Clear) |
| nuclear-winter-ww3 | 102x72 | 2 (1,64)/(100,7) | Cliffs 276 in 6 clusters, Road 376 |
| polar-disorder-ww3 | 98x98 | 2 (1,81)/(96,16) | Water 2023, Cliffs 562 |
| seventh-woods-ww3 | 123x114 | 4 | Cliffs 344 in 17, Rock 323, Water 109 in 8 |
| shellmap-open-field | 92x62 | 2 | none (100% Clear) |
| siberian-pass-ww3 | 97x67 | 2 (1,51)/(95,15) | Cliffs 1086 in 30, Road 835 |
| twin-rivers-ww3 | 128x128 | 4 W/E | Water 368 in 16, Cliffs 1258, Rock 1159 |
| woodland-warfare-ww3 | 98x98 | 2 (1,4)/(96,93) | Cliffs 908 in 34, Road 1412 |
| x-lake-ww3 | 130x130 | 4 corners | Water 1941 (two lobes x32-63 / x66-97, y32-97) |

### polar-disorder-ww3 -- candidate CHOSEN but not yet written

Anchors `(61,1)` to `(37,96)`. The route follows the eastern feeder river in at the north
edge, runs down the middle of the great northern lake, crosses the central highland spur,
picks up the cliff line at x64-70, drops into the southern river system and leaves through
the southern lake at x37-40.

- 148 path cells, 444 band cells, **25 of 148 on open ground** -- the rest is water or cliff.
- Engine load gate: **2 components**, 5133 / 3639. Spawn `(1,81)` to comp 0, `(96,16)` to comp 1.
- Spawn-to-band distance 35 (west) vs 28 (east); area split 59/41.
- **No capturable inside the band.** 10 on the west side (7 oilb, 2 logisticscenter, 1 gun),
  6 on the east (5 oilb, 1 gun).
- All 15 locomotors separate. `immobilepara` reports 350 sealed cells (bbox x30-65, y1-96) --
  **NOT yet explained, and it is the one open question on this map.**

### Known, NOT yet re-measured

The prior findings the brief lists are still un-re-measured: woodland-warfare's two `oilb`
derricks inside the derived band, twin-rivers' 256-279 cell loss at x1-20 y104-126, and the
polar-disorder 55-95 / x-lake 8-9 cell residues.

### An oddity worth recording

On the maps whose spawns sit on the Bounds edge, the Supply Route lands OUTSIDE Bounds:
`SpawnStartingUnits` places it at `HomeLocation + CVec(-1,-1)`, so polar-disorder's spawn
`(1,81)` gives an SR at `(0,80)` while Bounds start at `1,1`. `DefconWallRegion.IndexOf`
returns -1 there, so that SR's cell is `Unlabelled`. Its spawn is correctly labelled, so the
border still works, but any check asserting "each SR is on its own side" must read the SPAWN
on these maps. Same shape on nuclear-winter, siberian-pass, twin-rivers, x-lake,
woodland-warfare.

## Where to resume

1. Decide the arena-tank-duel question above (skip, vs author an x=32 line nobody sees).
2. Run `sweep(name, refA, refB)` from `tools/nav-guard/defcon_border_designer.py` for each of
   the six remaining maps, eyeball the top candidates with `show(...)`, pick one, and confirm
   with `finalize(...)`. polar-disorder is already picked.
3. Explain or dismiss the `immobilepara` sealed-cell figure before writing any map.
4. Write `rules.yaml` per map with `DefconWall: RegionCells:` and add `Rules: rules.yaml` to
   the six maps that lack one (polar-disorder, seventh-woods, siberian-pass, twin-rivers,
   woodland-warfare, x-lake; arena, nuclear-winter, river-zeta and shellmap already have it).
5. Extend `defcon_wall_audit.py` to READ an authored region back out of a map's rules.yaml
   (`--region-from-map`), which is the only check that can see this work. Note that
   `modload.load_map` does NOT resolve the inline `Rules: rules.yaml` form -- it only walks
   the child nodes of `Rules:` -- so that reader has to handle the file-list form itself.
6. Render a band-overlay PNG per map under `WORKSPACE/mockups/borders/` (Pillow 12.3.0 is
   available; `tools/nav-guard/validate.py` already renders decoded terrain to compare
   against `map.png`).
7. Manager verification recipe per map: lobby, pick the map, set the DEFCON game mode
   dropdown to **Escalation** (it defaults to Skirmish, which is a strict no-op). The wall
   stands at DEFCON 3 only and drops at DEFCON 2. `test-defcon-wall` and `demo-defcon-wall`
   are their own scenario map packages and do NOT exercise any shipped map's border.
