#!/usr/bin/env bash
# Pull the four tileset palettes out of the installed RA content into pal/.
#
# They are NOT committed: they are verbatim Westwood palette files, and
# WORKSPACE/ASSET-LICENSING.md is explicit about not carrying that kind of
# thing in-tree. The SHPs the generator writes contain no Westwood pixel data
# -- a SHP stores palette INDICES, and the noise fields are generated here.
#
# Needs a built engine (./make.ps1 all) because it goes through the shipped
# Utility, which is the only thing in the tree that can read the encrypted
# local.mix these live in.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
mkdir -p "$HERE/pal"
cd "$ROOT/engine"
for p in temperat snow desert interior; do
	MOD_SEARCH_PATHS="../mods,./mods" ENGINE_DIR=".." \
		dotnet bin/OpenRA.Utility.dll ww3mod --extract "$p.pal" >/dev/null
	mv -f "$p.pal" "$HERE/pal/"
	echo "  pal/$p.pal"
done
