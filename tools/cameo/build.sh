#!/usr/bin/env bash
# build.sh <SOURCEDIR> [options]
#
# Convert a folder of arbitrary source images into drop-in WW3MOD cameos.
#
#   ./tools/cameo/build.sh ~/art/russian-infantry
#   ./tools/cameo/build.sh ~/art/russian-infantry --install --check
#
# Staged output lands in tools/cameo/work/staging/ as 64x48 RGBA PNGs.
# --install copies them into mods/ww3mod/bits/misc/icons/ under .shp
# filenames (deliberate -- see README.md), and --check then runs the Utility's
# --check-missing-sprites so a broken drop is caught without launching the game.
#
# All other flags are passed straight through to convert.py (--fit, --size,
# --faction, --captions, --no-bevel, --out). Run with --help for the list.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

if [ $# -lt 1 ]; then
	echo "usage: $0 <SOURCEDIR> [--install] [--check] [convert.py options]" >&2
	echo "       $0 --help" >&2
	exit 2
fi

# --- dependency check: Python + Pillow. ImageMagick is NOT used; `convert` on
# --- Windows is the NTFS filesystem tool, not ImageMagick. See README.
PYTHON=""
for candidate in python3 python; do
	if command -v "$candidate" >/dev/null 2>&1; then PYTHON="$candidate"; break; fi
done
if [ -z "$PYTHON" ]; then
	echo "[cameo] ERROR: no python3/python on PATH." >&2
	exit 1
fi
if ! "$PYTHON" -c "import PIL" >/dev/null 2>&1; then
	echo "[cameo] ERROR: Pillow is not installed for $PYTHON." >&2
	echo "        Install it with:  $PYTHON -m pip install --user Pillow" >&2
	exit 1
fi

# Pull --check out; everything else belongs to convert.py.
RUN_CHECK=0
ARGS=()
for a in "$@"; do
	if [ "$a" = "--check" ]; then RUN_CHECK=1; else ARGS+=("$a"); fi
done

"$PYTHON" "$SCRIPT_DIR/convert.py" "${ARGS[@]}"

if [ "$RUN_CHECK" -eq 1 ]; then
	echo "[cameo] running --check-missing-sprites ..."
	if [ -f "$REPO_ROOT/engine/bin/OpenRA.Utility.dll" ]; then
		UTIL="$REPO_ROOT/utility.sh"
		[ -x "$UTIL" ] || UTIL="$REPO_ROOT/utility.cmd"
		# No mod id here: BOTH launchers inject it now (utility.sh:62, utility.cmd:53).
		# Passing `ww3mod` sent it twice, and the utility reads argv[1] as the command
		# name -- so this line raised NoSuchCommandException("ww3mod") on macOS rather
		# than checking any sprite. It only ever worked through utility.cmd, which used
		# to forward %* verbatim.
		"$UTIL" --check-missing-sprites
	else
		echo "[cameo] SKIP: engine/bin not built. Run ./make.ps1 all, then:" >&2
		echo "        ./utility.cmd --check-missing-sprites" >&2
		exit 1
	fi
fi
