#!/bin/sh
# Regenerate tools/combat-sim/data/stats.json from the live mod YAML.
#
# Calls OpenRA.Utility's --dump-balance-json command, which iterates the
# fully-resolved Ruleset (post-inheritance) and emits every combat-relevant
# actor + every weapon as JSON. The combat-sim loads this JSON at startup
# instead of carrying its own hardcoded copies, so stat drift is impossible.
#
# Re-run after any change to mods/ww3mod/rules/*.yaml. A pre-commit hook
# can wire this in (see tools/git-hooks/pre-commit).

set -e

REPO_ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
ENGINE_DIR="${REPO_ROOT}/engine"
OUT_FILE="${REPO_ROOT}/tools/combat-sim/data/stats.json"

mkdir -p "$(dirname "${OUT_FILE}")"

if [ ! -f "${ENGINE_DIR}/bin/OpenRA.Utility.dll" ]; then
	echo "OpenRA.Utility.dll not found — run 'make' first." >&2
	exit 1
fi

# A python interpreter is used only for the post-dump sanity check. Probe for it BEFORE spending a
# minute on the dump, and so that a machine without one reports that fact rather than reporting a
# bad dump. Some Linux distributions ship only 'python'.
PYTHON=""
for candidate in python3 python; do
	if command -v "${candidate}" >/dev/null 2>&1; then
		PYTHON="${candidate}"
		break
	fi
done

if [ -z "${PYTHON}" ]; then
	echo "No python3/python on PATH — the dump cannot be sanity-checked, so it was not run." >&2
	echo "${OUT_FILE} is unchanged." >&2
	exit 1
fi

cd "${ENGINE_DIR}"

# Strip the "Loading mod: ww3mod" line that the engine writes to stdout
# before the JSON. Find the first '{' line and emit from there.
#
# This is /bin/sh, so `set -o pipefail` is not available and a dotnet failure here is masked by
# awk's success. That is fine: a failed dump leaves the tmp file empty or truncated, and the JSON
# check below is what catches it.
#
# The two paths handed to dotnet are RELATIVE, resolved against the engine/ directory this script
# has just cd'd into, and match the form every sibling script already uses (utility.sh:54,
# tools/impact-scar/extract-palettes.sh:18, engine/utility.sh:5). They used to be absolute, built from
# REPO_ROOT — which under MSYS makes them '/c/Users/...', a form the native Windows dotnet resolves
# against the current drive instead. The mod search then came up short, the dump wrote nothing, and
# the sanity check below took the blame for it.
ENGINE_DIR=".." \
MOD_SEARCH_PATHS="../mods,./mods" \
	dotnet bin/OpenRA.Utility.dll ww3mod --dump-balance-json 2>/dev/null \
	| awk '/^\{/{seen=1} seen' \
	> "${OUT_FILE}.tmp"

# Sanity check: must be valid JSON with actors+weapons keys.
#
# The JSON is piped in on STDIN rather than handed over as a path, and the python source is single-
# quoted so no shell value is interpolated into it. On Windows the interpreter found on PATH is a
# native Windows python, which cannot open the MSYS-style '/c/Users/...' paths this script works in
# — so this check used to fail on every Windows run. Letting the SHELL open the file keeps path
# translation where it already works, identically on Windows, Linux and macOS.
if ! COUNTS=$("${PYTHON}" -c '
import json, sys
d = json.load(sys.stdin)
assert "actors" in d and "weapons" in d, "missing top-level keys"
assert len(d["actors"]) > 50, "too few actors: %d" % len(d["actors"])
assert len(d["weapons"]) > 20, "too few weapons: %d" % len(d["weapons"])
print(len(d["actors"]), len(d["weapons"]))
' < "${OUT_FILE}.tmp" 2>&1); then
	echo "${COUNTS}" >&2
	echo "Sanity check failed — either the dump is bad, or the check itself is. Aborting." >&2
	# The tmp file is deliberately NOT deleted. A failing check must never destroy the artifact it
	# was checking: this branch also fires when the CHECK itself is broken (the Windows path bug
	# above did exactly that), and deleting here turned a cosmetic portability wart into a
	# data-destroying one — it threw away a perfectly good dump and read like the dump had failed.
	echo "The dump is preserved at ${OUT_FILE}.tmp for inspection; ${OUT_FILE} is unchanged." >&2
	exit 1
fi

mv "${OUT_FILE}.tmp" "${OUT_FILE}"
SIZE=$(wc -c < "${OUT_FILE}" | tr -d ' ')
ACTORS=$(printf '%s\n' "${COUNTS}" | tail -1 | cut -d' ' -f1)
WEAPONS=$(printf '%s\n' "${COUNTS}" | tail -1 | cut -d' ' -f2)

echo "Wrote ${OUT_FILE}"
echo "  ${ACTORS} actors, ${WEAPONS} weapons, ${SIZE} bytes"
