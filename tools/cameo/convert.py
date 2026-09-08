#!/usr/bin/env python3
"""Turn arbitrary source images into drop-in WW3MOD cameos (64x48 RGBA PNG).

Called by build.sh; runnable directly. See tools/cameo/README.md.

House style (measured from the shipped US cameos, not invented):
  - full-bleed art, no inset and no shared background plate
  - 1px bevel: white top+left, black bottom+right, grey corners at TR and BL
  - an optional all-caps caption baked into the bottom of the art
"""

import argparse
import os
import shutil
import sys

try:
	from PIL import Image
except ImportError:
	sys.exit(
		"[cameo] ERROR: Pillow is not installed.\n"
		"        Install it with:  python -m pip install --user Pillow")

# Bevel colours, read off e1/e2/e4/e6/medi/sn/ar/mt/at/aa/sf/tl/spy/tecn
# americaicon.shp -- 14 of the 15 US infantry cameos agree on every border
# pixel. (e3americaicon is the lone outlier: no bevel, rounded transparent
# corners. Do not copy e3.)
BEVEL_LIGHT = (255, 255, 255, 255)
BEVEL_DARK = (0, 0, 0, 255)
BEVEL_CORNER = (170, 170, 170, 255)

CAPTION_FG = (255, 255, 255, 255)
CAPTION_SHADOW = (0, 0, 0, 255)

DEFAULT_SIZE = (64, 48)

# Unit key -> icon stem. The 15 existing Russian infantry cameos are already
# wired in sequences-infantry.yaml and are byte-identical copies of the US
# art, so overwriting the file is the whole change. 'dr' is the exception:
# it has no Russian file and needs a one-line sequence edit as well.
UNIT_KEYS = [
	"e1", "e2", "e3", "e4", "e6", "medi", "sn", "ar",
	"mt", "at", "aa", "sf", "tl", "spy", "tecn", "dr",
]
NEEDS_YAML_EDIT = {"dr"}

# 4x5 uppercase bitmap font, 1px advance gap (5px per character). At 64px wide
# this fits 12 characters, which is exactly the longest shipped caption
# ("FLAMETHROWER"). This APPROXIMATES the hand-authored caption font; it does
# not reproduce it. See README.
FONT = {
	"A": ".##.|#..#|####|#..#|#..#",
	"B": "###.|#..#|###.|#..#|###.",
	"C": ".###|#...|#...|#...|.###",
	"D": "###.|#..#|#..#|#..#|###.",
	"E": "####|#...|###.|#...|####",
	"F": "####|#...|###.|#...|#...",
	"G": ".###|#...|#.##|#..#|.###",
	"H": "#..#|#..#|####|#..#|#..#",
	"I": "###.|.#..|.#..|.#..|###.",
	"J": "..##|...#|...#|#..#|.##.",
	"K": "#..#|#.#.|##..|#.#.|#..#",
	"L": "#...|#...|#...|#...|####",
	"M": "#..#|####|####|#..#|#..#",
	"N": "#..#|##.#|#.##|#..#|#..#",
	"O": ".##.|#..#|#..#|#..#|.##.",
	"P": "###.|#..#|###.|#...|#...",
	"Q": ".##.|#..#|#..#|#.#.|.#.#",
	"R": "###.|#..#|###.|#.#.|#..#",
	"S": ".###|#...|.##.|...#|###.",
	"T": "####|.#..|.#..|.#..|.#..",
	"U": "#..#|#..#|#..#|#..#|.##.",
	"V": "#..#|#..#|#..#|.##.|.##.",
	"W": "#..#|#..#|####|####|#..#",
	"X": "#..#|#..#|.##.|#..#|#..#",
	"Y": "#..#|#..#|.##.|.#..|.#..",
	"Z": "####|...#|.##.|#...|####",
	"0": ".##.|#.##|##.#|#..#|.##.",
	"1": ".#..|##..|.#..|.#..|###.",
	"2": "###.|...#|.##.|#...|####",
	"3": "###.|...#|.##.|...#|###.",
	"4": "#..#|#..#|####|...#|...#",
	"5": "####|#...|###.|...#|###.",
	"6": ".##.|#...|###.|#..#|.##.",
	"7": "####|...#|..#.|.#..|.#..",
	"8": ".##.|#..#|.##.|#..#|.##.",
	"9": ".##.|#..#|.###|...#|.##.",
	"-": "....|....|###.|....|....",
	".": "....|....|....|....|.#..",
	"/": "...#|..#.|.#..|#...|....",
	" ": "....|....|....|....|....",
}
GLYPH_W, GLYPH_H, ADVANCE = 4, 5, 5

IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"}


def fit(img, size, mode):
	"""Scale uniformly to `size`. 'fill' centre-crops the overflow; 'contain'
	letterboxes onto transparent. Never stretches non-uniformly."""
	tw, th = size
	sw, sh = img.size
	if sw == 0 or sh == 0:
		raise ValueError("source image has a zero dimension")

	pick = max if mode == "fill" else min
	scale = pick(tw / sw, th / sh)
	nw, nh = max(1, round(sw * scale)), max(1, round(sh * scale))
	resized = img.resize((nw, nh), Image.LANCZOS)

	out = Image.new("RGBA", size, (0, 0, 0, 0))
	out.paste(resized, ((tw - nw) // 2, (th - nh) // 2))
	return out


def draw_bevel(img):
	w, h = img.size
	px = img.load()
	for x in range(w):
		px[x, 0] = BEVEL_LIGHT
		px[x, h - 1] = BEVEL_DARK
	for y in range(h):
		px[0, y] = BEVEL_LIGHT
		px[w - 1, y] = BEVEL_DARK
	px[w - 1, 0] = BEVEL_CORNER
	px[0, h - 1] = BEVEL_CORNER


def text_width(text):
	return max(0, len(text) * ADVANCE - 1)


def draw_caption(img, text):
	"""Centred all-caps caption whose last glyph row sits at h-3, matching the
	shipped cameos (glyph body around y=41..45 on a 48px canvas)."""
	text = text.upper()
	unknown = sorted({c for c in text if c not in FONT})
	if unknown:
		raise ValueError(f"caption has no glyph for {unknown!r}; font covers A-Z 0-9 space - . /")

	w, h = img.size
	tw = text_width(text)
	if tw > w - 2:
		return f"caption {text!r} is {tw}px wide, canvas allows {w - 2}px"

	x0 = (w - tw) // 2
	y0 = h - 2 - GLYPH_H
	px = img.load()

	on = []
	for i, ch in enumerate(text):
		rows = FONT[ch].split("|")
		for gy, row in enumerate(rows):
			for gx, cell in enumerate(row):
				if cell == "#":
					on.append((x0 + i * ADVANCE + gx, y0 + gy))

	lit = set(on)
	# 1px drop shadow first so glyphs stay legible over bright photo areas.
	for (x, y) in on:
		sx, sy = x + 1, y + 1
		if (sx, sy) not in lit and 0 <= sx < w and 0 <= sy < h:
			px[sx, sy] = CAPTION_SHADOW
	for (x, y) in on:
		if 0 <= x < w and 0 <= y < h:
			px[x, y] = CAPTION_FG
	return None


# The runtime caption font, as wired in mods/ww3mod/mod.yaml -> Fonts: Caption.
# Kept here only to warn about a caption that will not fit; nothing is drawn with it.
RUNTIME_FONT = os.path.join("engine", "mods", "common", "FreeSansBold.ttf")
RUNTIME_FONT_SIZE = 7

# IconSize: 62, 46 in chrome/ingame-player.yaml, less CaptionSideMargin: 1 a side.
RUNTIME_MAX_WIDTH = 60


def runtime_caption_width(text):
	"""Width in pixels of `text` in the runtime caption font, or None if the font
	cannot be found. The widget shortens anything wider than RUNTIME_MAX_WIDTH from
	the right rather than letting it bleed into the neighbouring cameo, so a caption
	that measures over is a caption that will be silently truncated on screen."""
	for root in (".", "..", os.path.join(os.path.dirname(__file__), "..", "..")):
		candidate = os.path.join(root, RUNTIME_FONT)
		if os.path.isfile(candidate):
			try:
				from PIL import ImageFont
				font = ImageFont.truetype(candidate, RUNTIME_FONT_SIZE)
				box = font.getbbox(text)
				return box[2] - box[0]
			except Exception:
				return None
	return None


def emit_runtime_captions(deferred):
	"""Print the YAML for --no-baked-captions. The caption belongs on the ACTOR, not on
	the icon file, so this cannot be pasted blind -- the target names below are icon
	names and the reader has to find the actor whose Buildable: Icon: names each one."""
	print("")
	print("[cameo] --no-baked-captions: nothing was drawn into the pixels. Add the caption")
	print("[cameo] to the ACTOR that uses each icon instead -- BuildableInfo.CameoCaption for")
	print("[cameo] a production cameo, SupportPowerInfo.CameoCaption for a support power one:")
	print("")
	for target, caption in deferred:
		width = runtime_caption_width(caption)
		if width is not None and width > RUNTIME_MAX_WIDTH:
			print(f"		# WARNING: {width}px wide, slot allows {RUNTIME_MAX_WIDTH}px -- will be shortened on screen")
		print(f"		# icon: {target}")
		print(f"		CameoCaption: {caption}")
	print("")


def load_captions(path):
	captions = {}
	if not path or not os.path.isfile(path):
		return captions
	with open(path, encoding="utf-8") as fh:
		for lineno, raw in enumerate(fh, 1):
			line = raw.strip()
			if not line or line.startswith("#"):
				continue
			parts = line.split("\t") if "\t" in line else line.split(None, 1)
			if len(parts) != 2:
				sys.exit(f"[cameo] ERROR: {path}:{lineno}: expected '<key><TAB><CAPTION>'")
			captions[parts[0].strip()] = parts[1].strip()
	return captions


def resolve_target(stem, faction):
	"""'e1' -> ('e1russiaicon', 'e1'); a complete 'e1russiaicon' passes through."""
	if stem in UNIT_KEYS:
		return f"{stem}{faction}icon", stem
	if stem.endswith("icon"):
		for key in UNIT_KEYS:
			if stem == f"{key}{faction}icon":
				return stem, key
		return stem, None
	return None, None


def main():
	ap = argparse.ArgumentParser(
		prog="convert.py",
		description="Convert a folder of source images into WW3MOD cameo PNGs.")
	ap.add_argument("source", help="folder of source images (any size/format Pillow reads)")
	ap.add_argument("--out", default=None, help="staging dir (default tools/cameo/work/staging)")
	ap.add_argument("--faction", default="russia", help="faction infix for bare unit keys (default russia)")
	ap.add_argument("--size", default="64x48", help="canvas WxH (default 64x48)")
	ap.add_argument("--fit", choices=["fill", "contain"], default="fill",
					help="fill = centre crop-to-fill (default); contain = letterbox onto transparent")
	ap.add_argument("--no-bevel", action="store_true", help="skip the house 1px bevel")
	ap.add_argument("--no-baked-captions", action="store_true",
					help="do NOT bake captions into the pixels; emit the YAML to paste instead. "
						 "Use with BuildableInfo/SupportPowerInfo CameoCaption, which draws the "
						 "caption at runtime so the wording stays editable. Baking is still the "
						 "default because it is what every shipped cameo does.")
	ap.add_argument("--captions", default=None,
					help="TSV of '<key><TAB>CAPTION' (default <source>/captions.txt if present)")
	ap.add_argument("--install", action="store_true",
					help="copy staged PNGs into the mod as .shp-named PNGs (see README)")
	ap.add_argument("--icons-dir", default=None, help="override the install destination")
	args = ap.parse_args()

	here = os.path.dirname(os.path.abspath(__file__))
	repo = os.path.abspath(os.path.join(here, "..", ".."))
	out_dir = args.out or os.path.join(here, "work", "staging")
	icons_dir = args.icons_dir or os.path.join(repo, "mods", "ww3mod", "bits", "misc", "icons")

	if not os.path.isdir(args.source):
		sys.exit(f"[cameo] ERROR: source folder not found: {args.source}")

	try:
		w, h = (int(v) for v in args.size.lower().split("x"))
	except ValueError:
		sys.exit(f"[cameo] ERROR: --size must look like 64x48, got {args.size!r}")
	if w < 8 or h < 8:
		sys.exit("[cameo] ERROR: --size too small to carry a bevel")

	captions = load_captions(args.captions or os.path.join(args.source, "captions.txt"))
	deferred = []

	sources = sorted(
		f for f in os.listdir(args.source)
		if os.path.splitext(f)[1].lower() in IMAGE_EXTS)
	if not sources:
		sys.exit(f"[cameo] ERROR: no images in {args.source} "
				 f"(looked for {', '.join(sorted(IMAGE_EXTS))})")

	os.makedirs(out_dir, exist_ok=True)
	written, skipped, warnings = [], [], []

	for name in sources:
		stem = os.path.splitext(name)[0]
		target, key = resolve_target(stem, args.faction)
		if target is None:
			skipped.append(f"{name}: stem {stem!r} is neither a known unit key nor a *icon name")
			continue

		src_path = os.path.join(args.source, name)
		try:
			with Image.open(src_path) as im:
				img = fit(im.convert("RGBA"), (w, h), args.fit)
		except Exception as exc:  # noqa: BLE001 - report and keep going
			skipped.append(f"{name}: could not read ({exc})")
			continue

		caption = captions.get(stem) or captions.get(target)
		if caption and args.no_baked_captions:
			# Requested but deliberately not drawn: the caption is handed back as YAML for the
			# runtime path instead. Keep it in `caption` so the per-file summary still reports it.
			deferred.append((target, caption))
		elif caption:
			problem = draw_caption(img, caption)
			if problem:
				warnings.append(f"{target}: {problem} -- caption omitted")
				caption = None
		if not args.no_bevel:
			draw_bevel(img)

		dest = os.path.join(out_dir, f"{target}.png")
		img.save(dest, "PNG")
		written.append((target, key, dest, caption))

	for t, key, _, cap in written:
		if cap and args.no_baked_captions:
			note = f'  caption "{cap}" -> YAML (not baked)'
		elif cap:
			note = f'  caption "{cap}"'
		else:
			note = "  (no caption)"
		flag = "  [NEEDS YAML EDIT - see README]" if key in NEEDS_YAML_EDIT else ""
		print(f"[cameo] wrote {t}.png  {w}x{h} RGBA{note}{flag}")
	for s in skipped:
		print(f"[cameo] SKIP  {s}")
	for s in warnings:
		print(f"[cameo] WARN  {s}")

	if not written:
		sys.exit("[cameo] ERROR: nothing was written.")

	print(f"[cameo] {len(written)} cameo(s) staged in {out_dir}")

	if deferred:
		emit_runtime_captions(deferred)

	if args.install:
		if not os.path.isdir(icons_dir):
			sys.exit(f"[cameo] ERROR: icons dir not found: {icons_dir}")
		print("[cameo] ----------------------------------------------------------")
		print("[cameo] Installing PNGs under .shp filenames. This is DELIBERATE:")
		print("[cameo] PngSheetLoader sniffs magic bytes, not the extension, so a")
		print("[cameo] PNG named *.shp loads fine and needs no YAML change.")
		print("[cameo] Do not 'fix' these back to real SHP. See tools/cameo/README.md.")
		print("[cameo] ----------------------------------------------------------")
		for t, _, path, _ in written:
			dest = os.path.join(icons_dir, f"{t}.shp")
			shutil.copyfile(path, dest)
			print(f"[cameo] installed {dest}")
		print("[cameo] now verify:  ./utility.sh --check-missing-sprites   (.\\utility.cmd on Windows)")

	return 0


if __name__ == "__main__":
	sys.exit(main())
