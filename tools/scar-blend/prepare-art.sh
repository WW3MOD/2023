#!/usr/bin/env bash
# Extract the terrain, tree and building art blendmock.py composites over.
#
# Nothing this writes is committed. tools/scar-blend/work/ is gitignored, for the
# same reason tools/impact-scar/pal/ is: these are verbatim Westwood assets and
# WORKSPACE/ASSET-LICENSING.md is explicit about not carrying that kind of thing
# in-tree. The five scar bands are NOT extracted here -- those are generated art
# and blendmock reads them from mods/ww3mod/bits/misc/scars directly.
#
# Needs a built engine (./make.ps1 all): --extract and --png both go through the
# shipped Utility, which is the only thing in the tree that can read the mixes.
#
# Note the two-step. `--png` reads the sprite from DISK, not from the mod's
# packages, so extracting first is not optional -- pointing --png straight at a
# name inside a mix fails with FileNotFoundException on that name in the cwd.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
ART="$HERE/work/art"

# MOD_SEARCH_PATHS and ENGINE_DIR are read by a .NET process, which cannot resolve
# the /c/Users/... form Git Bash's `pwd` hands back on Windows. Without this the
# Utility silently searches only the first path and dies with "Could not load mod
# 'ra'. Available mods: ww3mod", which reads like a missing mod rather than a
# mangled path. cygpath is absent on Linux/macOS, where ROOT is already usable.
if command -v cygpath >/dev/null 2>&1; then
	ROOT="$(cygpath -m "$ROOT")"
fi

if [ ! -f "$ROOT/tools/impact-scar/pal/temperat.pal" ]; then
	"$ROOT/tools/impact-scar/extract-palettes.sh"
fi

mkdir -p "$ART"
cd "$ART"
cp -f "$ROOT/tools/impact-scar/pal/temperat.pal" .

FILES="clear1.tem w1.tem \
	sh04.tem sh06.tem sh07.tem \
	t01.tem t02.tem t03.tem t05.tem t06.tem t08.tem tc01.tem tc02.tem \
	v01.tem"

MOD_SEARCH_PATHS="$ROOT/mods,$ROOT/engine/mods" ENGINE_DIR="$ROOT/engine" \
	dotnet "$ROOT/engine/bin/OpenRA.Utility.dll" ww3mod --extract $FILES >/dev/null

for f in $FILES; do
	MOD_SEARCH_PATHS="$ROOT/mods,$ROOT/engine/mods" ENGINE_DIR="$ROOT/engine" \
		dotnet "$ROOT/engine/bin/OpenRA.Utility.dll" ww3mod --png "$f" temperat.pal >/dev/null
done

echo "  $ART: $(ls -1 ./*.png | wc -l) frames"
