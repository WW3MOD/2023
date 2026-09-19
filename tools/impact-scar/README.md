# tools/impact-scar — blast-scar band art

Generates the five graded smudge bands that nuclear weapons leave, and renders
the contact sheet used to review them without launching the game.

Output art lives at `mods/ww3mod/bits/misc/scars/` (180 SHPs: 5 bands × 3
densities × 4 variants × 3 tilesets). Contact sheet at
`WORKSPACE/mockups/impact-scarring.html`.

The three densities are the band itself (`bz*`) and two sparser **edge cuts**
(`by*` at 50% coverage, `bx*` at 25%), which `SmudgeLayer` selects for cells the
shore fade has marked as close to terrain that refuses a scar. See
`gen_scars.py`'s `EDGE_TIERS` for why the boundary needs fewer pixels rather
than lower alpha.

## Why this exists

Every nuclear weapon used to fire three `LeaveSmudge` warheads with a
single-value `Size`, and a single value is a **filled disc**, not a ring
(`LeaveSmudgeWarhead.cs:53`). The three discs were nested, both scorch warheads
wrote into the same layer, and stock scorch art has one frame so the second one
did nothing at all. The scar was one flat tone with a hard rim. These five bands
are true annuli with a monotone density ramp, so a blast reads as a circular
impact point that fades out at the edge.

## Running it

```bash
./make.ps1 all                                  # the Utility must exist
./tools/impact-scar/extract-palettes.sh         # writes pal/, gitignored
./tools/impact-scar/extract-terrain.sh          # writes terrain/, gitignored
python tools/impact-scar/gen_scars.py           # writes out/ and verifies
cp tools/impact-scar/out/* mods/ww3mod/bits/misc/scars/
python tools/impact-scar/contact_sheet.py       # writes the HTML sheet
```

**Run `extract-terrain.sh` before any preview renderer.** Without it they die on
`random.choice` of an empty list, which says nothing about missing art.

`rewire_bands.py` rewrites the warhead blocks on all 14 nuclear weapons from
flat discs into annuli. It has already been run; `--check` shows what it would
do without writing. It is idempotent only in the sense that re-running it on
already-banded YAML is a no-op (it looks for `Warhead@Crater`/`@Scorch1`/
`@Scorch2` triples, which no longer exist on those weapons).

## Files

| | |
|---|---|
| `racontent.py` | Pure-Python RA content reader/writer: MIX index (classic hash), LCW decode **and** encode, XOR-delta decode, ShpTD read/write, 6-bit VGA palettes. Ports of the engine's own implementations, cited line by line. No engine build needed. |
| `gen_scars.py` | The art. Deterministic fbm noise, quantile-thresholded coverage, per-tileset palette ramps harvested from stock smudge art. |
| `rewire_bands.py` | Turns each weapon's three nested discs into five annuli, interpolating the blast-wave delays. |
| `contact_sheet.py` | Simulates `SmudgeLayer` + `LeaveSmudgeWarhead` faithfully and renders before/after over real terrain. |
| `scar_edge_preview.py` | The scar's outer boundary, with and without the sparser edge cuts, over `demo-nuke-river-zeta`'s real terrain at the 21-cell shoreline its own `05-shore-outer` frame is aimed at. Also prints the gradient as numbers: mean cell luminance by distance to unscarrable ground. |
| `scar_density.py` | Measures an autotest SCREENSHOT rather than rendering one: per named cell, the fraction of its ground pixels that are bright unburnt wheat, compared only against neighbours in the same annulus bucket. Measures the display scale off the frame rather than assuming it, because the same scenario at the same window size has captured at both 48 and 60 px per cell depending on the session's Windows display scaling. |
| `extract-terrain.sh` | Fills `terrain/` with real clear-ground and water tiles. |
| `shore_fade_preview.py` | The shoreline case. Reads a scenario's REAL per-cell terrain out of `map.bin` and runs `ShoreAlphaAt` over it, rendering the fade *field* as its own panel. Exists because `contact_sheet.py` models water only as `water_from_x`, a half-plane — a straight vertical line, so it draws a straight edge whatever the code does and cannot be evidence about edges. |

## Things that will bite you

- **`pal/` is not committed.** Those are verbatim Westwood palettes; see
  `WORKSPACE/ASSET-LICENSING.md`. The generated SHPs are fine — a SHP stores
  palette *indices*, and every pixel value here comes from the noise generator.
- **`bits/misc/tiles/{tem,sno,desert,int}` are not mounted.** `mod.yaml` mounts
  `bits/misc/tiles` and folder packages do not recurse
  (`Folder.cs:35`, `SearchOption.TopDirectoryOnly`). Art dropped in those
  subdirectories silently never loads — three stock overrides have been sitting
  there inert. That is why this art went in a new `bits/misc/scars` that *is*
  mounted, rather than mounting those four and switching the strays on.
- **A typo in a `SmudgeLayer`'s `Sequence` is not a lint error.**
  `SmudgeLayerInfo.Sequence` has no `[SequenceReference]`
  (`SmudgeLayer.cs:35-36`); the failure is an `IndexOutOfRangeException` at map
  load. `--check-missing-sprites` does catch a bad *sprite* name inside the
  sequence, including the `INTERIOR: TEMPERAT` fallback.
- **The terrain tile cache is not committed and is not automatic.** `terrain/` holds
  verbatim Westwood tiles, so it is gitignored beside `pal/`. It used to live at a
  hardcoded path under the Windows TEMP tree with nothing in-tree able to refill it;
  Windows emptied it and every renderer here died on an empty `random.choice`. Set
  `WW3MOD_TERRAIN_CACHE` to point elsewhere if you need to.
- **`--png` writes to the PROCESS's cwd, not next to its input.**
  `ConvertSpriteToPngCommand` passes a bare `prefix + "-" + n + ".png"` to Save. Run
  it from `engine/` -- which you must, for the relative `MOD_SEARCH_PATHS` to resolve
  -- and sixteen `clear1-*.png` land in the engine tree.
- **Regeneration is byte-stable.** Seeds are derived from `(band, variant,
  depth)`, never from RNG state, so re-running produces identical files and a
  clean diff. If a regen shows changes, something in the ramp harvest moved.

## Verification that was actually run

- The 60 base SHPs regenerate **byte-identical** after the edge-tier change was added
  to the generator (2026-09-19), so the cuts are purely additive.
- All 300 base frames (60 SHPs × 5 depths) decoded by the **engine's own** SHP loader
  (`--png`) and compared pixel-for-pixel against the source: identical.
- `--check-missing-sprites` clean for every new sequence across all four
  tilesets, proven non-vacuous by injecting a bad name and watching it fail.
- `NuclearYieldTest.NoSmudgeRadiusCrossesTheTileSearchCeiling` now scans all 14
  nuclear weapons and asserts the bands tile exactly, proven non-vacuous by
  injecting a gap.
