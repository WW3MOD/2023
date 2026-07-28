#!/usr/bin/env bash
# export.sh <SPRITEFILE> [PALETTE]
#
# Pull a sprite out of the mounted RA .mix archives and convert it to one
# indexed PNG per frame, ready for external editing.
#
#   ./tools/sprite/export.sh t01.tem      # a temperate tree (tileset ext)
#   ./tools/sprite/export.sh e1.shp       # rifle infantry (real .shp)
#
# SPRITEFILE is the in-mix filename INCLUDING extension (.shp, .tem, .sno, ...).
# PALETTE defaults to temperat.pal (the base actor palette, palettes.yaml:50).
#
# Output lands in  tools/sprite/work/<prefix>/  where <prefix> is the filename
# without extension:  <prefix>-0000.png ... plus the source sprite and palette.
# Edit those PNGs in an indexed-mode editor, then feed the prefix to import.sh.
#
# NOTE: the shipped ./utility.sh cd's into engine/ and writes all output there;
# this wrapper runs it, then relocates the artifacts into the work dir and
# leaves engine/ clean.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ENGINE="$REPO_ROOT/engine"
UTIL="$REPO_ROOT/utility.sh"

if [ $# -lt 1 ]; then
	echo "usage: $0 <SPRITEFILE> [PALETTE]   e.g. $0 t01.tem" >&2
	exit 2
fi

SPRITEFILE="$1"
PALETTE="${2:-temperat.pal}"
PREFIX="${SPRITEFILE%.*}"
WORKDIR="$SCRIPT_DIR/work/$PREFIX"

mkdir -p "$WORKDIR"

echo "[export] extracting $SPRITEFILE + $PALETTE from mounted mixes ..."
"$UTIL" --extract "$SPRITEFILE" "$PALETTE" >/dev/null

if [ ! -f "$ENGINE/$SPRITEFILE" ]; then
	echo "[export] ERROR: '$SPRITEFILE' not found in the mod filesystem." >&2
	echo "         Tree/terrain sprites use tileset extensions (.tem/.sno/.int/.des), not .shp." >&2
	exit 1
fi

echo "[export] converting $SPRITEFILE -> indexed PNGs ..."
"$UTIL" --png "$SPRITEFILE" "$PALETTE" >/dev/null

# Relocate artifacts out of engine/ into the work dir, keeping engine/ clean.
mv -f "$ENGINE/$SPRITEFILE" "$WORKDIR/"
mv -f "$ENGINE/$PALETTE" "$WORKDIR/"
mv -f "$ENGINE/$PREFIX"-[0-9][0-9][0-9][0-9].png "$WORKDIR/"

FRAMES="$(ls "$WORKDIR/$PREFIX"-[0-9][0-9][0-9][0-9].png | wc -l | tr -d ' ')"
echo "[export] done: $FRAMES frame(s) in $WORKDIR"
echo "         edit the PNGs (indexed mode, keep index 0 transparent), then:"
echo "           ./tools/sprite/import.sh $PREFIX"
