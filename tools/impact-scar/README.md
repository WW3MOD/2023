# tools/impact-scar — blast-scar band art

Generates the five graded smudge bands that nuclear weapons leave, and renders
the contact sheet used to review them without launching the game.

Output art lives at `mods/ww3mod/bits/misc/scars/` (60 SHPs: 5 bands × 4
variants × 3 tilesets). Contact sheet at
`WORKSPACE/mockups/impact-scarring.html`.

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
python tools/impact-scar/gen_scars.py           # writes out/ and verifies
cp tools/impact-scar/out/* mods/ww3mod/bits/misc/scars/
python tools/impact-scar/contact_sheet.py       # writes the HTML sheet
```

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
- **Regeneration is byte-stable.** Seeds are derived from `(band, variant,
  depth)`, never from RNG state, so re-running produces identical files and a
  clean diff. If a regen shows changes, something in the ramp harvest moved.

## Verification that was actually run

- All 300 generated frames (60 SHPs × 5 depths) decoded by the **engine's own** SHP loader
  (`--png`) and compared pixel-for-pixel against the source: identical.
- `--check-missing-sprites` clean for every new sequence across all four
  tilesets, proven non-vacuous by injecting a bad name and watching it fail.
- `NuclearYieldTest.NoSmudgeRadiusCrossesTheTileSearchCeiling` now scans all 14
  nuclear weapons and asserts the bands tile exactly, proven non-vacuous by
  injecting a gap.
