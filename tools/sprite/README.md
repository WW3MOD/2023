# tools/sprite — SHP export / edit / import workbench

Thin wrappers around the shipped OpenRA Utility (`./utility.sh`) that give a
one-command round-trip for WW3MOD sprites:

```
export.sh <SPRITEFILE>  ->  edit PNGs  ->  import.sh <PREFIX>  ->  drop-in
```

No engine changes. Everything runs on the Utility commands already in
`engine/bin/OpenRA.Utility.dll`.

## Quick start

```bash
# 1. Pull a sprite out of the mounted RA .mix archives as indexed PNGs
./tools/sprite/export.sh t01.tem          # temperate tree
# -> tools/sprite/work/t01/t01-0000.png ... t01-0009.png (+ source + palette)

# 2. Edit the PNG frames in an INDEXED-mode editor (see constraints below).
#    Keep them 8-bit palette-indexed to temperat.pal; don't reorder the palette.

# 3. Reassemble into a SHP (validates before writing)
./tools/sprite/import.sh t01
# -> tools/sprite/work/t01/t01.shp

# 4. Drop it in as a loose file to override the .mix copy (see "Drop-in")
cp tools/sprite/work/t01/t01.shp mods/ww3mod/bits/t01.shp
```

## SPRITEFILE names

`export.sh` takes the **in-mix filename including its extension**. Stock trees
and terrain use **tileset extensions**, not `.shp`:

| Kind | Example filename | Note |
|---|---|---|
| Unit / infantry / building | `e1.shp`, `3tnk.shp`, `proc.shp` | real `.shp` inside a mix |
| Tree / terrain object | `t01.tem` (temperate), `t01.sno`, `t01.int`, `t01.des` | tileset-specific |

A bare `find t01*` in the repo returns nothing — stock sprites live **inside**
the RA `.mix` archives, so `export.sh` `--extract`s them first. `import.sh`
always emits a plain `.shp` (SHP is tileset-independent index data).

## Palette constraints (temperat.pal)

Sprites store **palette indices**, not colours; the colour is applied at draw
time from `temperat.pal` (`mods/ww3mod/rules/palettes.yaml:50`, the base actor
palette). So edited art must stay indexed to that exact palette:

- **Index 0 = transparent.** Leave it transparent; the exporter marks it via
  `tRNS`. Anything you paint on index 0 is invisible.
- **Index 4 = shadow.** Reserved for the drop-shadow colour.
- **Indices 80–95 = player-colour remap** (16 entries,
  `palettes.yaml:132`). A pixel on any of these takes the owning player's team
  colour at render. Use them **deliberately** for team-coloured panels; avoid
  them otherwise or the pixel will shift per player.
- All other indices are fixed colours — safe to use.

**Palette is 6-bit VGA.** `temperat.pal` stores channels 0–63. The exported PNG
PLTE is scaled to 8-bit (`value × 255 / 63`). `import.sh`'s validator scales
before comparing, so you don't have to think about it — but if your editor
**re-quantises or reorders** the palette on save, the indices drift and the
sprite renders with wrong colours. The validator catches that and refuses to
build. Fix: edit in indexed mode with the palette **locked**, or re-export a
clean frame and copy your edit onto it.

There is **no Utility command that quantises an arbitrary RGBA image to
`temperat.pal`.** New-from-scratch art must be indexed externally first
(GIMP/Aseprite indexed mode, or `magick in.png -remap temperat-8bit.png out.png`).
`--remap` only conforms an existing *SHP* between two palettes.

## import.sh validation (all hard failures)

Nothing is written unless every frame passes:

1. **Indexed8** — PNG colour-type 3, bit-depth 8. (`--shp` itself also enforces
   this, but the wrapper fails earlier with a clearer message.)
2. **Equal frame size** — every frame identical W×H.
3. **Palette match** — every frame's PLTE equals `temperat.pal` (index 0
   exempt), scaled for 6-bit→8-bit. Guards against the index-drift trap above.

## Drop-in (loose file over .mix)

Mount order in `mod.yaml` puts the loose `ww3mod|bits*` dirs **after** the RA
mixes, so a loose `mods/ww3mod/bits/<name>.shp` overrides the mix copy of the
same name. See `WORKSPACE/DISCOVERIES.md` (2026-07-28) for the verified
precedence result and the PngSheet / RGBA findings.

## Files

| File | Role |
|---|---|
| `export.sh` | `--extract` + `--png` a sprite into `work/<prefix>/` |
| `import.sh` | validate `work/<prefix>/` frames, then `--shp` back to a SHP |
| `validate.py` | the Indexed8 / equal-size / palette-drift checker |
| `work/` | per-sprite scratch dirs (git-ignored; safe to delete) |

## Implementation note

`./utility.sh` `cd`s into `engine/` and writes all output to that cwd. The
wrappers run it there, then relocate artifacts into `work/<prefix>/` and leave
`engine/` clean, so they don't collide with a game/test run in the same
checkout.
