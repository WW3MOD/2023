#!/bin/sh
# WW3MOD external screenshot trigger.
#
# Usage:  ./tools/autotest/screenshot.sh <label> [--wait] [--cmd-file=<path>]
#
# Writes a "screenshot <label>" command to the file watched by an already-running
# WW3MOD game launched with Test.Mode=true Test.ScreenshotCmdFile=<path>. The
# engine polls the file each logic tick (~40 ms at the menu, the world timestep
# -- 60 ms at default speed -- in a match), executes the command, deletes the
# file, and appends an entry to manifest.json in the screenshot output dir. The
# manifest entry is written when the capture is ARMED; the PNG lands a frame
# later, and never under a hidden/minimized window.
#
# Use this for menu/lobby captures where there's no Lua running. To launch the
# game in screenshot mode, see ./tools/autotest/start-screenshot-mode.sh.
#
# --wait: poll manifest.json until its entry count grows, then print the path
#         of the NEWEST entry on stdout (whatever its label). Fixed 10 s
#         deadline. The file may not exist yet when the path prints -- ls it
#         before reading. Default: return immediately after writing the command.
#
# Exit: 0 sent (or path printed), 2 --wait timed out, 1 usage/setup error.
#
# --cmd-file=<path>: explicit override. Default matches start-screenshot-mode.sh.

set -e

LABEL=""
WAIT_FOR_RESULT=0
CMD_FILE_OVERRIDE=""

while [ $# -gt 0 ]; do
	case "$1" in
		--wait)            WAIT_FOR_RESULT=1; shift ;;
		--cmd-file=*)      CMD_FILE_OVERRIDE="${1#*=}"; shift ;;
		--help|-h)
			sed -n '2,22p' "$0" | sed 's/^# \?//'
			exit 0 ;;
		--*)
			echo "Unknown flag: $1" >&2
			exit 1 ;;
		*)
			if [ -z "${LABEL}" ]; then
				LABEL="$1"
				shift
			else
				echo "Extra positional arg: $1" >&2
				exit 1
			fi ;;
	esac
done

if [ -z "${LABEL}" ]; then
	echo "Usage: $0 <label> [--wait]" >&2
	exit 1
fi

# Defaults match start-screenshot-mode.sh: manual screenshots live in
# ~/.ww3mod-tests/screenshots/manual_<run-id>/ with cmd.txt at the root.
SCREENSHOT_BASE="${HOME}/.ww3mod-tests/screenshots"
if [ -n "${CMD_FILE_OVERRIDE}" ]; then
	CMD_FILE="${CMD_FILE_OVERRIDE}"
else
	# Find the most recent manual_* directory. If none, the user hasn't started
	# screenshot mode — bail.
	#
	# PITFALL: this used to pick the directory by lexicographic `sort | tail -1`,
	# which is NOT the newest one. Run-ids are only sometimes timestamps —
	# start-screenshot-mode.sh accepts a name, producing dirs like
	# `manual_smallfixes_210545` alongside `manual_260816_223042`. Any letter
	# sorts after any digit, so a single named run permanently shadows every
	# later timestamped run, and this script then writes its command into a dead
	# directory whose engine exited long ago. The failure surfaces as `--wait`
	# timing out, which reads as "the game ignored me" rather than "you wrote to
	# the wrong file" — it cost a full launch to diagnose. Sort by mtime.
	MANUAL_DIR=$(find "${SCREENSHOT_BASE}" -maxdepth 1 -name 'manual_*' -type d \
		-exec ls -dt {} + 2>/dev/null | head -1)
	if [ -z "${MANUAL_DIR}" ]; then
		echo "Error: no manual_* screenshot dir under ${SCREENSHOT_BASE}." >&2
		echo "Run ./tools/autotest/start-screenshot-mode.sh first to launch the game in screenshot mode." >&2
		exit 1
	fi
	CMD_FILE="${MANUAL_DIR}/cmd.txt"
fi

MANIFEST_FILE="$(dirname "${CMD_FILE}")/manifest.json"

# Record the manifest's pre-existing entries so --wait can detect the new one.
PRE_COUNT=0
if [ -f "${MANIFEST_FILE}" ]; then
	PRE_COUNT=$(grep -o '"path":"' "${MANIFEST_FILE}" 2>/dev/null | wc -l | tr -d ' ')
fi

# Write the command. Overwrite any existing file — the engine reads then deletes,
# but a stale file from a crashed engine shouldn't shadow our new request.
printf "screenshot %s\n" "${LABEL}" > "${CMD_FILE}"

if [ "${WAIT_FOR_RESULT}" = "0" ]; then
	echo "==> Sent: screenshot ${LABEL}"
	echo "    Watching: ${CMD_FILE}"
	exit 0
fi

# Wait for the manifest to grow by one entry — that's our shot.
DEADLINE=$(( $(date +%s) + 10 ))
while [ "$(date +%s)" -lt ${DEADLINE} ]; do
	if [ -f "${MANIFEST_FILE}" ]; then
		CUR_COUNT=$(grep -o '"path":"' "${MANIFEST_FILE}" 2>/dev/null | wc -l | tr -d ' ')
		if [ "${CUR_COUNT}" -gt "${PRE_COUNT}" ]; then
			# Extract the path of the LAST entry (just-added).
			NEW_PATH=$(grep -o '"path":"[^"]*"' "${MANIFEST_FILE}" | tail -1 | sed 's/"path":"\(.*\)"/\1/')
			echo "${NEW_PATH}"
			exit 0
		fi
	fi
	sleep 0.2
done

echo "Error: timed out waiting for ${LABEL} to appear in ${MANIFEST_FILE}" >&2
exit 2
