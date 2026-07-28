#!/usr/bin/env bash
# import.sh <PREFIX>
#
# Reassemble the edited PNG frames in tools/sprite/work/<PREFIX>/ back into a
# single SHP, after validating them against the constraints the engine enforces
# plus a palette-drift guard.
#
#   ./tools/sprite/import.sh t01
#   ./tools/sprite/import.sh e1
#
# Validation (all HARD failures — nothing is written unless every frame passes):
#   1. every frame is Indexed8  (PNG colour-type 3, bit-depth 8)
#   2. every frame is the same W x H
#   3. every frame's palette matches temperat.pal (index 0 = transparent is
#      exempt).  temperat.pal is 6-bit VGA (0-63); the exported PLTE is 8-bit
#      (x255/63), so the check scales before comparing.  A mismatch means an
#      editor re-quantised / re-ordered the palette, which would render as
#      wrong colours in-game (SHP stores indices, coloured by temperat.pal at
#      draw time).  Re-export or fix the palette rather than importing.
#
# Output:  tools/sprite/work/<PREFIX>/<PREFIX>.shp
# Drop that .shp as a loose file (see README) to override the .mix copy.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ENGINE="$REPO_ROOT/engine"
UTIL="$REPO_ROOT/utility.sh"

if [ $# -lt 1 ]; then
	echo "usage: $0 <PREFIX>   e.g. $0 t01" >&2
	exit 2
fi

PREFIX="$1"
WORKDIR="$SCRIPT_DIR/work/$PREFIX"
PALETTE="$WORKDIR/temperat.pal"

if [ ! -d "$WORKDIR" ]; then
	echo "[import] ERROR: $WORKDIR does not exist. Run export.sh first." >&2
	exit 1
fi

shopt -s nullglob
FRAMES=("$WORKDIR/$PREFIX"-[0-9][0-9][0-9][0-9].png)
shopt -u nullglob
if [ ${#FRAMES[@]} -eq 0 ]; then
	echo "[import] ERROR: no ${PREFIX}-NNNN.png frames in $WORKDIR" >&2
	exit 1
fi

if [ ! -f "$PALETTE" ]; then
	echo "[import] palette not in work dir; extracting temperat.pal ..."
	"$UTIL" --extract temperat.pal >/dev/null
	mv -f "$ENGINE/temperat.pal" "$PALETTE"
fi

echo "[import] validating ${#FRAMES[@]} frame(s) ..."
python3 "$SCRIPT_DIR/validate.py" "$PALETTE" "${FRAMES[@]}"

# Validation passed. Reassemble via the shipped --shp command. It writes the
# SHP to cwd (engine/) named after the first input's pre-'-' token, so stage the
# frames in engine/ under bare names, build, then relocate.
echo "[import] building $PREFIX.shp ..."
STAGE=()
for f in "${FRAMES[@]}"; do
	base="$(basename "$f")"
	cp -f "$f" "$ENGINE/$base"
	STAGE+=("$base")
done
"$UTIL" --shp "${STAGE[@]}" >/dev/null
for base in "${STAGE[@]}"; do rm -f "$ENGINE/$base"; done

mv -f "$ENGINE/$PREFIX.shp" "$WORKDIR/$PREFIX.shp"
echo "[import] done: $WORKDIR/$PREFIX.shp"
echo "         to see it in-game, copy it to a loose bits dir, e.g.:"
echo "           cp $WORKDIR/$PREFIX.shp mods/ww3mod/bits/$PREFIX.shp"
