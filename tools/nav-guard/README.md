# nav-guard

Catches the class of change whose blast radius nobody can eyeball: **a movement or
blocking rule change that silently seals part of a map off.**

For every map and every locomotor it decodes terrain from `map.bin`, places the
statically-authored blocking actors from `map.yaml`, builds the 8-connected movement graph
the pathfinder would see, and measures the largest connected component. A committed
baseline turns "the biggest drivable region got smaller" into a build failure instead of
something a reviewer has to happen to think about.

The motivating case: the first version of the tank-trap diagonal-squeeze rule
(`b164a312`, reverted) closed a **335-cell hedged field on river-zeta** to every vehicle.
Nothing in the build would have caught it. nav-guard reproduces that number — see
[Acceptance test](#acceptance-test).

## Run

```bash
make nav-guard                              # selftest + baseline check; no build needed
./tools/nav-guard/nav_guard.py check        # the gate on its own
./tools/nav-guard/nav_guard.py bless        # re-record the baseline after a reviewed change
./tools/nav-guard/nav_guard.py validate     # decoder self-check against the map.png previews
./tools/nav-guard/nav_guard.py report       # per-map/per-locomotor table
./tools/nav-guard/nav_guard.py pockets --map river-zeta --locomotor wheeled
./tools/nav-guard/nav_guard.py compare --before none --after generic
```

`check` is standard-library-only so it can gate anywhere. `validate` needs Pillow.

Filters: `--map <substring>` and `--locomotor <name>` are repeatable. `--state dead`
replaces every destructible map actor with its death husk. `--squeeze` selects the
diagonal-squeeze rule variant: `none`, `generic` (the reverted `b164a312`), `tagged`
(shipped, `be036370`).

## Design decisions

### Baseline: a committed file, not a git two-checkout diff

`baseline.json` records `largest`, `passable`, `components` and `pocketed` per map and
locomotor, for two world states. Re-recorded deliberately with `bless`.

Computing old-vs-new from git is self-maintaining, and it was tempting. It is rejected
because **it only ever compares against the parent commit**: a drift of five cells per
commit never trips it, and after twenty commits the map has quietly lost a hundred cells
with every individual check green. A committed baseline is an absolute reference — the
number only moves when a human moves it, and the movement shows up as a reviewable diff
in the same PR as the change that caused it. It also keeps the tool runnable in a
worktree with no second checkout and no network.

The cost is real: the baseline goes stale, and a legitimate map edit means a second
command. That is mitigated by `bless` being one line and by the failure message printing
it verbatim.

### Fail vs report: tiered, with the hard fail kept narrow

- **exit 2 (fail)** — the largest connected component shrank in the *authored* world
  state. That is the specific regression this tool exists for, and nothing else earns a
  hard failure.
- **exit 1 (warn)** — anything else changed: component count, total passable cells, the
  largest component *grew*, a new map or locomotor appeared, the all-husks state shrank,
  or a map's Lua names a static blocker (see blind spots).
- **exit 0** — byte-identical to baseline.

A hard fail on *any* connectivity change would fire on every routine map edit and be
disabled within a month. A pure report gets ignored. Keeping the hard fail to
largest-component shrink in the authored state means it fires rarely and, when it fires,
it is nearly always either a real bug or a change worth a sentence in the commit message.

Reporting `passable` alongside `largest` is what makes a failure actionable: if both drop
by the same amount, terrain simply became impassable; if `largest` drops while `pocketed`
rises, a region split off — which is the bug shape.

### Where it runs: `make nav-guard`, and a prerequisite of `make test`

`tools/nav-guard/` mirrors `tools/behavior-lint/` — a standalone Python entry point with a
README and a self-test. No new runner, no new config.

`nav-guard` is its own phony Makefile target rather than lines appended to `test`, because
`test` depends on `all` (a full engine build, and per CLAUDE.md a .NET 6 runtime
specifically) while nav-guard needs neither. `make nav-guard` is the fast inner-loop form;
`make test` picks it up as a prerequisite so the existing gate covers it.

## What is modelled

| | |
|---|---|
| Terrain | `map.bin` tile plane → tileset template/index → terrain type → per-locomotor `TerrainSpeeds`. A terrain type absent from `TerrainSpeeds` is impassable (`LocomotorInfo`). |
| Playable area | `Bounds`, not `MapSize` — `Map.Contains` tests `Bounds` for flat rectangular grids (`Map.cs:1378`). |
| Blocking actors | Every actor in the map's `Actors:` block, footprint from `Building.Footprint`/`Dimensions` (`x`/`X` block, `+` is transit-only, `_` empty). |
| Crushability | `Passable.PassClasses` ∩ locomotor `Passes`. Note this is `Passes`, **not** `Crushes`: `Locomotor.IsBlockedBy` consults only `PassableClasses`. |
| Mobile actors | Never walls — an ordinary move order uses `BlockedByActor.Immovable`, under which a movable actor does not block. |
| Diagonal squeeze | All three rule variants, edge-level. This is the part a cell-only model would miss entirely: both endpoint cells stay passable and only the *step between them* is denied. |
| Husk state | `--state dead` substitutes every `SpawnActorOnDeath` target. |
| Rule inheritance | A faithful subset of `MiniYaml.Merge`/`ResolveInherits`, including `-Trait:` removal and map-level `Rules:` overrides. All 126 placed actor types across the 10 maps resolve. |

Deliberately **not** modelled, because ww3mod does not use them: terrain height
(`MapGrid.MaximumTerrainHeight` is 0, so the height-discontinuity rule at
`Locomotor.cs:198` can never fire) and custom movement layers (no tunnels; `cell.Layer` is
always 0).

## Decoder self-check

The connectivity numbers are worth exactly what the terrain decode is worth, and a wrong
decode does not look wrong — a transposed tile plane still yields plausible component
counts. Each `map.png` was produced by the engine itself (`Map.SavePreview`,
`Map.cs:1222`) from the same bytes, so it is an independent rendering to check against.

```
map                        preview  terrain  overall  stale  ore  ??  align
arena-tank-duel              66x34  100.00%  100.00%      0    0   0  bounds
nuclear-winter-ww3          100x70  100.00%   94.30%    399    0   0  offset 1,1
polar-disorder-ww3           96x96  100.00%   97.32%    247    0   0  offset 1,1
river-zeta-ww3               98x82  100.00%   99.93%      6    0   0  bounds
seventh-woods-ww3          121x112  100.00%   97.47%    335    8   0  offset 1,1
shellmap-open-field          92x62  100.00%   99.26%     42    0   0  offset 0,0
siberian-pass-ww3            95x65  100.00%   93.91%    375    1   0  offset 1,1
twin-rivers-ww3            112x112  100.00%   94.97%    593   38   0  offset 1,1
woodland-warfare-ww3         98x98  100.00%  100.00%      0    0   0  bounds
x-lake-ww3                 128x128  100.00%   98.07%    293   24   0  offset 1,1
```

**`terrain` is 100.00% on all ten maps and `??` (unexplained) is zero.** Every pixel that
differs is accounted for:

- **`stale`** — one side has an actor the other does not. Seven of the ten previews are
  smaller than the current `Bounds` and sit at offset `1,1`: their `Bounds:` was
  hand-edited in `map.yaml` without re-saving through the editor, so the image predates
  later actor edits too. On river-zeta the six stale pixels are exactly the seven
  `t14`/`t15` trees deleted in `0fa152f1`, whose footprint cell is offset `(1,1)`.
- **`ore`** — RA-era resource cells still sitting in the `map.bin` resource plane, painted
  when the preview was saved. ww3mod has no resource layer today. The counts match the
  non-zero resource cells exactly (twin-rivers 38/38, x-lake 24/24, seventh-woods 8/8).

`terrain` is measured only on cells where neither side involves an actor colour, so it is
the decoder in isolation. Two bugs were found this way and neither would have shown up in
a component count: the preview takes the **first** actor on a stacked cell
(`Map.cs:1286-1288`) and this took the last; and `BlocksDiagonalSqueeze` tagging was
skipped on terrain the locomotor could not cross, which silently dropped one of
river-zeta's 25 traps as a shoulder.

## Acceptance test

```
$ ./nav_guard.py pockets --squeeze generic --map river-zeta --locomotor wheeled
river-zeta-ww3 / wheeled: 164 components, largest 4798
    size    335  bbox x 75..97 y 52..68  e.g. (76, 52)
```

335 cells, all `Clear` terrain, ringed by tree actors (`t11`×79, `t13`×53, `t10`×44,
`t17`×30, `t15`×23, `t03`×17, `t12`×4) — a hedged field, matching the description in
`dd3430a8` exactly. The component does not exist under `--squeeze none`.

Two further results from the same run, both worth knowing:

- The reverted generic rule cost river-zeta vehicles **513–619 cells** from the largest
  component in total, not 335. The hedged field is the single biggest pocket; the rest is
  ~180 cells spread over roughly a hundred small ones (components 51 → 164). The original
  finding named the part a human would notice.
- **The shipped `tagged` rule changes no connectivity metric on any map.** It is not a
  no-op — it denies exactly the 13 diagonal steps between river-zeta's 13 corner-to-corner
  trap pairs (25 traps, matching `dd3430a8`) — but every one of those 13 gaps is already
  filled with a `barb` actor, and no locomotor lists `barbedwire` under `Passes`. The rule
  closes gaps that were already shut. That is a statement about the current map set, not
  about the rule: place traps diagonally on open ground and it will bite.

## What this tool would still miss

The honest list. A narrow tool with a known edge beats a broad one that gets trusted too
far.

1. **Anything dynamic.** This is a static analysis of the authored map. Buildings placed
   during a match, mines laid, bridges destroyed, husks from destroyed *units* (as opposed
   to map actors) — none of it is here. The `--state dead` pass covers exactly one dynamic
   case, and only as an all-or-nothing worst case.
2. **Scripted actors.** Only the `Actors:` block is read. Today the only spawn site in any
   map's Lua is `Reinforcements.Reinforce` in `river-zeta-frontline.lua`, which produces
   mobile units — never walls. `check` scans map Lua for quoted strings naming an immobile
   non-passable actor and warns if one appears, so a future scenario that scripts a
   barrier into place trips a warning rather than passing silently. That tripwire is a
   string match: an actor type built from concatenation, or read from a table, slips past.
3. **`CustomTerrain`.** Bridges rewrite terrain type at runtime. No bridge actor is placed
   on any of the ten maps, and `^Bridge`'s footprint is all-`_` (it blocks nothing, it only
   *adds* passability), so the error direction is conservative: a bridge appearing would
   make cells reachable that nav-guard calls unreachable, never the reverse. If a bridge is
   ever placed, this section is wrong and the tool needs the `CustomTerrain` layer.
4. **Reachability is not usability.** A cell in the largest component may be reachable only
   through a one-cell-wide gap that no group of units can actually negotiate, or only by a
   route so long the AI will never take it. nav-guard would score a map with a single
   1-cell isthmus as perfectly connected.
5. **Subcell occupancy.** `SharesCell` locomotors are modelled at full-cell granularity.
   Infantry crowding is not represented, and `ActorMap.HasFreeSubCell`'s early-out in
   `UpdateCellBlocking` is not reproduced. Since every full-cell blocker also blocks every
   subcell, this is only wrong in the permissive direction for infantry.
6. **Only the largest component is gated.** A change that shrinks the *second* largest
   region, or shuffles cells between two mid-size pockets, produces a warning rather than a
   failure. If a map is ever designed around two separate landmasses of comparable size,
   the gate protects only one of them.
7. **The hierarchical pathfinder is not modelled.** nav-guard answers "is there a path",
   which is the local A* question. HPF's abstract graph is deliberately permissive
   (`BlockedByActor.None`, `Locomotor.cs:236-239`) and a bug in *its* invalidation could
   make a unit fail to find a path that geometrically exists. That is a pathfinder bug, not
   a connectivity regression, and this tool is blind to it by construction.
8. **The baseline can be blessed away.** `bless` is one command and nothing forces a human
   to look at the diff. The gate is only as strong as the review of `baseline.json`.
