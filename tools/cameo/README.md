# tools/cameo — source images → drop-in cameos

Turns a folder of arbitrary source images (photos, art, anything Pillow reads)
into **64×48 RGBA PNG** cameos that match the mod's sidebar house style, and
optionally installs them straight into `mods/ww3mod/bits/misc/icons/`. No SHP
encoding, no palette indexing and — the point of the whole exercise — **no YAML
edits**, because the 15 Russian infantry cameos are already wired in
`sequences-infantry.yaml` and are currently byte-identical copies of the US art.
Replacing one is a file-content swap. (`dr` is the single exception; see below.)

```
build.sh <SOURCEDIR>  ->  work/staging/*.png  ->  --install  ->  --check
```

## Quick start

```bash
# 1. Stage. Source images may be any size, any aspect, any common format.
./tools/cameo/build.sh ~/art/russian-infantry
# -> tools/cameo/work/staging/e1russiaicon.png ... (64x48 RGBA)

# 2. Eyeball the staged PNGs, then install + verify in one go.
./tools/cameo/build.sh ~/art/russian-infantry --install --check
```

`--check` runs `./utility.cmd ww3mod --check-missing-sprites`, which *decodes*
every referenced sprite — so a bad drop is caught without launching the game.
Run it standalone any time:

```bash
./utility.cmd ww3mod --check-missing-sprites
```

Note the pre-existing baseline noise: `b2bomb.shp`, `pip-cloak.shp` and
`pip-cover.shp` are reported missing on a clean tree. Three lines is a pass;
anything more is yours.

## Naming your source files

The **file stem picks the target**. Two accepted forms:

| Source filename | Becomes |
|---|---|
| `e1.png`, `medi.jpg`, `dr.webp` | `<key>russiaicon.shp` — bare unit key + `--faction` (default `russia`) |
| `e1russiaicon.png` | `e1russiaicon.shp` — full icon name, passed through |

Anything else is skipped with a message; nothing is silently dropped.

Captions come from an optional **`captions.txt`** in the source folder (or
`--captions FILE`), one `key<TAB>CAPTION` per line, `#` for comments:

```
e1	CONSCRIPT
medi	MEDIC
dr	DRONE OP
```

## The 16 target filenames

15 already exist and are md5-identical to their US twins — overwrite in place.
The 16th (`drrussiaicon.shp`) does not exist yet and is the only one that also
needs a YAML edit.

| # | File | Status | US twin's caption (for reference) |
|---|---|---|---|
| 1 | `e1russiaicon.shp` | exists, = US | CONSCRIPT |
| 2 | `e2russiaicon.shp` | exists, = US | GRENADIER |
| 3 | `e3russiaicon.shp` | exists, = US | RIFLEMAN |
| 4 | `e4russiaicon.shp` | exists, = US | FLAMETHROWER |
| 5 | `e6russiaicon.shp` | exists, = US | ENGINEER |
| 6 | `medirussiaicon.shp` | exists, = US | MEDIC |
| 7 | `snrussiaicon.shp` | exists, = US | SNIPER |
| 8 | `arrussiaicon.shp` | exists, = US | LMG |
| 9 | `mtrussiaicon.shp` | exists, = US | MORTAR |
| 10 | `atrussiaicon.shp` | exists, = US | JAVELIN AT |
| 11 | `aarussiaicon.shp` | exists, = US | STINGER AA |
| 12 | `sfrussiaicon.shp` | exists, = US | SPEC FORCES |
| 13 | `tlrussiaicon.shp` | exists, = US | TEAM LEADER |
| 14 | `spyrussiaicon.shp` | exists, = US | SPY |
| 15 | `tecnrussiaicon.shp` | exists, = US | TECHNICIAN |
| 16 | `drrussiaicon.shp` | **new file + YAML edit** | DRONE OP |

## The `dr` special case

`dr.russia` currently shares the American icon. After dropping
`drrussiaicon.shp` in, point the sequence at it —
`mods/ww3mod/sequences/sequences-infantry.yaml:1511-1513`:

```yaml
dr.russia:
	Inherits: ^dr
	icon: dricon            # <-- change this line
```

becomes

```yaml
dr.russia:
	Inherits: ^dr
	icon: drrussiaicon
```

**Indentation is a literal TAB**, matching the rest of the file. And per
`DOCS/reference/conventions.md`, the **blank line** separating `dr.russia:`
from the next top-level entry (`dog:`) is significant — do not remove it or the
two entries silently merge.

## House style — what the existing cameos actually do

Extracted from the shipped art, not invented. All 15 US infantry cameos were
decoded with `--png` and their pixels compared.

**There is no shared background plate or frame image to composite onto.** A
pixel-wise diff across five cameos found **2 identical pixels out of 2880** in
the interior — every cameo is its own full-bleed cropped photograph running
edge to edge, with no inset, no vignette and no common backdrop. So the script
does not fabricate one.

**There is, however, an exact 1px bevel**, and the script reproduces it:

| Edge | Colour |
|---|---|
| top row + left column | white `#FFFFFF` |
| bottom row + right column | black `#000000` |
| top-right + bottom-left corner | grey `#AAAAAA` |

All 216 border pixels are identical across the conforming cameos, and 14 of the
15 US infantry icons conform. **`e3americaicon.shp` is the lone outlier** — no
bevel, rounded fully-transparent corners. Do not use e3 as your reference.

**Cameos also carry a baked-in all-caps caption**, white with a 1px dark drop
shadow, centred, ~5px cap height, sitting just above the bottom bevel. Every
one of the 15 has one. The script reproduces the *placement and treatment*
faithfully but renders with a small built-in 4×5 bitmap font — it
**approximates** the hand-authored lettering rather than reproducing it. If you
want an exact match, bake the caption into the source image yourself and just
don't supply a `captions.txt` entry. Max caption length is 12 characters at
64px wide, which is exactly what the longest shipped caption
("FLAMETHROWER") needs.

## Size

**64×48 RGBA.** Existing cameos are a mix of 60×48 and 64×48; 64×48 is the
larger and the target here. The size is a consistency convention, *not* an
engine constraint: `IconSize: 62, 46` in `chrome/ingame-player.yaml` sizes the
slot, but the sprite is drawn by `WidgetUtils.DrawSpriteCentered` and is never
scaled and never clipped — the shipped cameos already overhang the slot.

## ⚠️ Why the installed files are PNGs named `.shp`

**This is deliberate. Do not "fix" it.**

`--install` writes a **32-bit PNG** to a filename ending in **`.shp`**. That
looks wrong on disk and the temptation to convert it to a real SHP will be
strong. Resist it:

- `SpriteFormats:` in `mods/ww3mod/mod.yaml:327` includes `PngSheet`.
- `PngSheetLoader` dispatches on the file's **magic bytes, not its extension**
  (`PngSheetLoader.cs:49`). A PNG called `e1russiaicon.shp` loads correctly.
- Keeping the `.shp` name means **the existing sequence definitions still
  resolve**, so a new cameo needs zero YAML changes.
- Cameos never pass through a player palette: `IconPalette` defaults to
  `chrome` with `IconPaletteIsPlayerPalette = false`
  (`Buildable.cs:42-47`), and ww3mod's `chrome` is a plain `PaletteFromFile`
  with `AllowModifiers: false` (`rules/palettes.yaml:58-62`).
- RGBA sprites then skip the palette entirely — `ResolveTextureIndex` returns
  0 for an RGBA channel *provided the palette has no colour shift*
  (`SpriteRenderer.cs:126-127`), which `chrome` does not. So full truecolour
  art is correct here even though it would be wrong for a team-coloured unit
  sprite, which does need the remap indices.

Renaming these to `.png` would break every sequence that references them.
Converting them to real indexed SHP would throw away the truecolour for no
benefit. Background: `WORKSPACE/DISCOVERIES.md`, 2026-08-11.

## Dependencies

**Python 3 + Pillow.** `build.sh` checks for both and fails with an install
hint if either is missing.

**ImageMagick is deliberately not used** — it is not installed on this machine.
Beware: `which convert` on Windows resolves to `C:\WINDOWS\system32\convert`,
which is the **NTFS filesystem converter**, not ImageMagick. A naïve
`command -v convert` check passes and then the tool does something entirely
unrelated. Pillow is already a dependency of nothing else here, but it ships
with most Python installs and is present on this machine (12.3.0).

## Options

| Flag | Effect |
|---|---|
| `--install` | copy staged PNGs into `mods/ww3mod/bits/misc/icons/` as `.shp` |
| `--check` | run `--check-missing-sprites` afterwards |
| `--fit fill` | *(default)* scale uniformly and centre-crop the overflow |
| `--fit contain` | scale uniformly and letterbox onto transparent |
| `--size WxH` | canvas size, default `64x48` |
| `--faction NAME` | infix for bare unit keys, default `russia` |
| `--captions FILE` | caption table, default `<SOURCEDIR>/captions.txt` |
| `--no-bevel` | skip the house bevel (source art already has its own border) |
| `--out DIR` | staging dir, default `tools/cameo/work/staging` |

Neither fit mode ever stretches non-uniformly.

## Files

| File | Role |
|---|---|
| `build.sh` | dependency check, then `convert.py`, then optional `--check` |
| `convert.py` | fit → caption → bevel → write; also does `--install` |
| `work/` | staging scratch (git-ignored; safe to delete) |
