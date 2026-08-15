#!/bin/sh
# Poll-copy the engine logs to a private directory while a run is in flight.
#
# The engine writes debug.log / lua.log to ONE fixed path with no run identity, so under concurrent
# workers the file you read after your run may be a stranger's, or gone entirely (AUTOTEST.md, "Clear
# debug.log before the run"). Copying WHILE the run is live makes the evidence independent of anyone
# else's cleanup.
#
# PITFALL, measured 2026-08-15 and it cost a capture: the engine TRUNCATES debug.log at startup rather
# than appending, so the file is SMALLER at the beginning of a run than the previous run left it. A
# "only copy if the source grew" guard — the obvious defence against a competing `rm -f` — therefore
# refuses to copy for the entire run and preserves the STALE file, which is the exact failure it was
# written to prevent. Copy unconditionally and stop the poller the moment the run returns.
#
# Usage: poll-copy-logs.sh <dest-dir> [interval-seconds]
set -eu

DEST="$1"
INTERVAL="${2:-2}"
SRC="${HOME}/Library/Application Support/OpenRA/Logs"

mkdir -p "$DEST"

while :; do
	for name in debug.log lua.log; do
		src="${SRC}/${name}"
		[ -f "$src" ] || continue
		cp "$src" "${DEST}/${name}" 2>/dev/null || true
	done
	sleep "$INTERVAL"
done
