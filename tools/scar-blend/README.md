# tools/scar-blend — offline blast-scar comparison renders

Composites the shipped smudge look beside candidate treatments, over real decoded
terrain, without launching the game. Written for
[`WORKSPACE/reports/scar-blending-260908.md`](../../WORKSPACE/reports/scar-blending-260908.md);
output is `WORKSPACE/mockups/scar-blending-options.png`.

Same idea as `tools/cameo/binmock.py` and `tools/impact-scar/contact_sheet.py`, and it
reuses the latter's `racontent.py` (pure-Python MIX/SHP/palette reader) and
`gen_scars.py` (the fbm noise and the harvested colour ramps) rather than
re-implementing either.

## Running it

```bash
./make.ps1 all                        # the Utility must exist
./tools/scar-blend/prepare-art.sh     # writes work/, gitignored
python tools/scar-blend/blendmock.py  # writes the PNG
```

`prepare-art.sh` calls `tools/impact-scar/extract-palettes.sh` for you if `pal/` is
missing.

## Files

| | |
|---|---|
| `prepare-art.sh` | Extracts the terrain templates, trees and the civilian building from the installed RA content and converts them to PNG. |
| `blendmock.py` | The port of the engine's smudge arithmetic, the treatments, and the measurement. |
| `sheet.py` | Layout and captions only. |

## This is a mockup, and here is exactly how far to trust it

Every **sprite** is real: terrain templates, trees, the building, the five scar bands
and the stock `cr*`/`sc*` art all come out of files this branch ships. The
**arrangement** is a hand port of the engine, cited line by line in `blendmock.py`'s
docstring — annulus membership, the filled-disc-vs-annulus reading of `Size`, the
`AcceptsSmudgeType` gate, the `BlockingActorsAt` + target-type gate, variant choice and
layer Z order.

Nothing checks that the port is still in step with the engine. If `LeaveSmudgeWarhead`
or `SmudgeLayer` changes, this drifts and keeps producing a confident wrong picture.

Deliberately **approximate**: tree and building sprite anchoring is taken as
"sprite box == footprint box". That is right for these actors (`T##` is
`Footprint: __ x_` with a 48×48 sprite; `V01` is `xx xx` with the same) but it is not a
port of `RenderSprites`. The smudge itself is exact.

## Things that will bite you

- **`work/` is not committed.** Extracted Westwood art; see
  `WORKSPACE/ASSET-LICENSING.md`. Same reason `tools/impact-scar/pal/` is not committed.
- **`--png` reads the sprite from DISK, not from the mod's packages.** You must
  `--extract` first. Pointing `--png` at a name that only exists inside a mix fails with
  `FileNotFoundException` on that name *in the current directory*, which reads like a
  path bug rather than a missing extract step.
- **`MOD_SEARCH_PATHS` and `ENGINE_DIR` must be Windows paths.** Git Bash's `pwd`
  returns `/c/Users/...`, which the .NET process cannot resolve; it then silently
  searches only the first entry and dies with *"Could not load mod 'ra'. Available mods:
  ww3mod"*. That message names the wrong problem entirely. `prepare-art.sh` runs the
  paths through `cygpath -m` for this reason.
- **Palette indices 3 and 4 are the SHADOW, and `--png` writes them out as their literal
  colours — bright green on temperate.** The `terrain` palette declares
  `ShadowIndex: 3, 4` (`rules/palettes.yaml:40-44`, and the same for the other three
  tilesets), and `ImmutablePalette` maps every listed index to `140u << 24` — black at
  alpha 140 (`Palette.cs:99`). `blendmock.tiles()` undoes it. Skip that and every tree
  wears a green skirt, which is easy to mistake for the art being wrong. In practice only
  index 4 occurs (3824 pixels across the 126 extracted frames; index 3, zero).
- **Forest density is load-bearing, not decoration.** Every tree cell is a cell the
  warhead skips, so the scar's appearance depends strongly on how many trees are in it.
  At a couple of dozen trees the effect looks like a curiosity; at forest density it is
  the dominant artefact. `forest()` exists to make that honest.

## Measurement

`blendmock` also supports the radial-profile measurement quoted in section 1 of the
report (mean luminance in half-cell bins over accepting terrain only). It is what
established that the current art's radial gradient is already smooth — 32.2 → 47.0 with
no step — and therefore that per-cell alpha, jitter and a scaled decal all buy under one
luminance point. That measurement is of *this render*, not of the game, and is only as
good as the port above.
