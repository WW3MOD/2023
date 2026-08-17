#!/bin/sh
# AUTOTEST_LAUNCHER shim for the cross-runtime determinism probe.
#
# Drop-in replacement for ./launch-game.sh that (a) pins the .NET runtime to
# WW3_FX_VERSION and (b) appends Test.SyncHashLog=WW3_SYNCHASH_OUT. Everything
# else — window profile, mute, timeout, verdict handling — stays run-test.sh's,
# so the probe inherits the launch profile the harness is known to run at speed
# rather than a hand-rolled one.
#
# The runtime is pinned with `dotnet exec --fx-version`, NOT with a roll-forward
# env var: this machine has 6.0.36 installed, so the runtimeconfig's
# `rollForward: Major` finds an exact 6.0 match and never rolls anywhere.

set -e

[ -n "${WW3_FX_VERSION}" ] || { echo >&2 "WW3_FX_VERSION unset"; exit 2; }
[ -n "${WW3_SYNCHASH_OUT}" ] || { echo >&2 "WW3_SYNCHASH_OUT unset"; exit 2; }

REPO_ROOT=$(cd "$(dirname "$0")/../.." && pwd)
REAL_DOTNET=$(command -v dotnet)

SHIM_DIR=$(mktemp -d)
trap 'rm -rf "${SHIM_DIR}"' EXIT
cat > "${SHIM_DIR}/dotnet" <<SHIM
#!/bin/sh
exec "${REAL_DOTNET}" exec --fx-version "${WW3_FX_VERSION}" "\$@"
SHIM
chmod +x "${SHIM_DIR}/dotnet"

PATH="${SHIM_DIR}:${PATH}" exec "${REPO_ROOT}/launch-game.sh" \
	"$@" "Test.SyncHashLog=${WW3_SYNCHASH_OUT}"
