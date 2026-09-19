#!/usr/bin/env bash
# Fill terrain/ with the real clear-ground and water tiles the preview renderers draw on.
#
# WHY THIS SCRIPT EXISTS. It did not, and that cost a worker a rebuild on 2026-09-19.
# `contact_sheet.py` read these out of a hardcoded path under the Windows TEMP
# directory, with nothing in-tree that could put them there. Windows cleans TEMP: the
# four directories were still present and all four were EMPTY, so every preview tool
# in this folder died on `random.choice` of an empty list -- an error that says nothing
# whatever about missing art. The cache now lives beside pal/ and is regenerated here.
#
# NOT COMMITTED, for the same reason pal/ is not: these are verbatim Westwood tiles.
# See WORKSPACE/ASSET-LICENSING.md. The generated SHPs in bits/misc/scars are fine --
# a SHP stores palette indices and every pixel value there is generated.
#
# Needs a built engine (./make.ps1 all) and pal/ (./extract-palettes.sh): it goes
# through the shipped Utility, which is the only thing in the tree that can read the
# mix files.
#
# WATCH THE WORKING DIRECTORY. `--png` derives its output name from the source file
# but writes it to the PROCESS's cwd, not next to the source (ConvertSpriteToPngCommand
# passes a bare `prefix + "-" + n + ".png"` to Save). Run it from engine/ and sixteen
# clear1-*.png land in the engine tree. That is why everything below is absolute and
# the command runs from a scratch directory.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
OUT="$HERE/terrain"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

if [ ! -f "$HERE/pal/temperat.pal" ]; then
	echo "pal/ is empty -- run ./tools/impact-scar/extract-palettes.sh first" >&2
	exit 1
fi

# (subdirectory, template file, palette). `water` is its own bucket rather than a
# tileset: shore_fade_preview.py draws water cells from it whatever the tileset.
SETS="tem:clear1.tem:temperat sno:clear1.sno:snow des:clear1.des:desert water:w1.tem:temperat"

for spec in $SETS; do
	sub="${spec%%:*}"; rest="${spec#*:}"
	file="${rest%%:*}"; pal="${rest#*:}"

	rm -rf "${OUT:?}/$sub"
	mkdir -p "$OUT/$sub"
	rm -f "$WORK/$file"

	if ! (cd "$ROOT/engine" && MOD_SEARCH_PATHS="../mods,./mods" ENGINE_DIR=".." 		dotnet bin/OpenRA.Utility.dll ww3mod --extract "$file" >/dev/null 2>&1); then
		echo "  SKIP $sub: $file is not in any package this mod mounts" >&2
		continue
	fi

	mv -f "$ROOT/engine/$file" "$WORK/"

	# --nopadding: a template tile is exactly one 24x24 cell and the preview tools
	# paste it at a cell origin, so padding out to a frame box would shift every tile.
	# The command must run from engine/ for the relative MOD_SEARCH_PATHS to resolve,
	# which is also where it drops its PNGs -- hence the sweep below.
	prefix="${file%.*}"
	rm -f "$ROOT/engine/$prefix"-*.png
	(cd "$ROOT/engine" && MOD_SEARCH_PATHS="../mods,./mods" ENGINE_DIR=".." 		dotnet bin/OpenRA.Utility.dll ww3mod --png "$WORK/$file" 		"$HERE/pal/$pal.pal" --nopadding >/dev/null)

	# A template carries fully transparent tiles for the cells it does not cover, and
	# those would paste as holes in the background.
	moved=0
	for png in "$ROOT/engine/$prefix"-*.png; do
		[ -e "$png" ] || continue
		if python -c "
import sys
from PIL import Image
im = Image.open(sys.argv[1]).convert('RGBA')
sys.exit(0 if im.size == (24, 24) and im.getchannel('A').getextrema()[1] > 0 else 1)
" "$png"; then
			mv -f "$png" "$OUT/$sub/"
			moved=$((moved + 1))
		else
			rm -f "$png"
		fi
	done

	echo "  terrain/$sub: $moved tiles from $file"
	[ "$moved" -gt 0 ] || { echo "  ERROR: $file yielded no usable 24x24 tiles" >&2; exit 1; }
done
