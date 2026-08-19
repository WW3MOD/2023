#!/bin/sh
# WW3MOD developer test harness — single-test runner
#
# Usage:  ./tools/autotest/run-test.sh [position] [flags] <test-folder-name>
#
# Position shorthand (positional, before the test name; case-insensitive):
#   L | -L | --left      Left half of the screen
#   R | -R | --right     Right half of the screen
#   F | -F | --full      PseudoFullscreen
#                        (no shorthand → centered, ~90% × ~85%, default)
#
# Window-behavior flags (windowed mode only):
#   --background           (default) Visible, but pushed behind your other
#                          windows immediately after launch via osascript so
#                          your terminal keeps focus. Cmd+Tab to OpenRA brings
#                          it forward.
#   --hidden               Never map the window (SDL_WINDOW_HIDDEN): no desktop
#                          window, no focus grab. Rendering suspends engine-side
#                          exactly like --minimized, so the run is invisible with
#                          no GPU cost. This is the unattended/tournament profile;
#                          prefer it over --minimized on Windows, where a
#                          minimized window can surface as a black frame.
#   --minimized            Old behavior: SDL_MinimizeWindow into the dock.
#                          Restore by clicking the small icon next to Trash.
#   --visible              Stay foreground. (Alias: --no-minimize.)
#   --size WxH             Pin the windowed size exactly (e.g. --size 1024x768),
#                          overriding the screen-derived size the position
#                          shorthand computes. Chrome in this mod is laid out in
#                          absolute pixels against WINDOW_WIDTH / WINDOW_HEIGHT,
#                          so whether two panels overlap is a property of the
#                          window, not of the build — and the default size is
#                          derived from whatever monitor the runner happens to
#                          have. Any capture making a claim about layout must
#                          pin the size or it is a statement about one desktop.
#                          Ignored under --fullscreen / F.
#
# Audio flags:
#   --audio                Keep sound on. (run-demo.sh injects this.)
#   --mute                 Force mute. (Default for tests.)
#
# Speed:
#   --speed N              Run the sim at N× wall-clock (1-16). Divides
#                          world.Timestep via Test.SpeedMultiplier — pure
#                          pacing, simulation stays byte-identical. Default:
#                          unset (1×, current behavior).
#
# Determinism:
#   --seed N               Fix the match RNG seed (Test.RandomSeed=N). Same
#                          seed + same code + same map = byte-identical replay
#                          (seeds SharedRandom AND the decorrelated LocalRandom
#                          that bot decisions draw from). Integer; may be
#                          negative. Default: unset — the engine picks a
#                          DateTime.Now-derived seed, which is now RECORDED in
#                          result.json ("seed" field), so any run is reproducible
#                          by rerunning with --seed <that recorded value>.
#
# Timeout:
#   --timeout N            Hard wall-clock watchdog (seconds). If the game is
#                          still alive and no verdict has been written after N
#                          seconds, kill it and synthesize a FAIL result. Guards
#                          against maps whose rules fail to load and idle on the
#                          main menu forever (no Test.Pass/Fail ever runs).
#                          Default: 300. Pure wall-clock — NOT scaled by --speed.
#
# Behavior lint:
#   --lifecycle            Enable the off-by-default UnitLifecycleLogger, which
#                          writes a per-unit JSONL event stream, then run the
#                          tools/behavior-lint analyzer after the match and echo
#                          its WARN report. Advisory ONLY — never changes the
#                          pass/fail verdict. The .lifecycle.jsonl is archived
#                          alongside result.json in the per-run screenshot dir.
#
# Missile audit:
#   --missile-trace        Enable the off-by-default MissileTrace, which writes a
#                          JSONL stream to <result>.missiles.jsonl: one line per
#                          missile per tick, plus one summary line per missile
#                          naming the exact code path that ended it. Observation
#                          only — changes neither the verdict nor the simulation.
#   --missile-trace-summary
#                          Same, but suppress the per-tick lines and keep only the
#                          one summary line per missile. Use for range sweeps.
#
# Saved-game diagnostics:
#   --sync-reports         Arm sync reporting even with a single human client, and
#                          dump the RECORDING side of the sync state when a game save
#                          is acknowledged. Only meaningful for saved-game restore
#                          desyncs, which are single-client by construction and so
#                          produce "No sync report available!" without this. Writes
#                          syncdiag-recorded-frame*.log next to the desync report;
#                          diff the two to name the diverging trait/field. Expensive
#                          per net frame — off by default.
#
# Misc:
#   --position=<centered|left|right|full>  Long form of L/R/F.
#   --fullscreen           Same as F + Mode=PseudoFullscreen.
#   --windowed             Force windowed (default; only useful when overriding
#                          a user settings.yaml that forces fullscreen).
#   --help                 Show this message.
#
# Defaults: windowed, centered (large but not full), background, muted, edge-pan
# disabled (engine-side, gated on Test.Mode + Mode=Windowed).
#
# macOS focus handling: PREV_APP is captured before launch and re-activated
# after the game exits, so the close-time focus shuffle doesn't yank you out
# of the terminal/editor you were typing in.
#
# Examples:
#   ./tools/autotest/run-test.sh test-paladin-fires           # background, muted
#   ./tools/autotest/run-test.sh L test-paladin-fires         # left half, background
#   ./tools/autotest/run-test.sh --visible --audio test-foo   # foreground, sound on
#   ./tools/autotest/run-test.sh --minimized test-foo         # old miniaturize behavior
#   ./tools/autotest/run-test.sh F test-foo                   # fullscreen
#   ./tools/autotest/run-test.sh --speed 8 test-foo           # run 8× wall-clock
#   ./tools/autotest/run-test.sh --seed 1017 test-foo         # fixed seed (reproducible)
#   ./tools/autotest/run-test.sh --lifecycle test-foo         # + behavior-lint report
#
# Exit code: 0=pass, 1=fail, 2=skip, 3=error (crash, hang, or harness error).
#
# Reading the verdict — READ THIS BEFORE SCRIPTING AGAINST THIS RUNNER.
#
#   Every run ends with a banner whose LAST line is machine-readable:
#
#       AUTOTEST_VERDICT outcome=<OUTCOME> exit=<n> test=<name> run=<run-id>
#
#   OUTCOME is one of: PASS, FAIL, SKIP, TIMEOUT-FAIL, CRASH, NO-RESULT,
#   BAD-VERDICT, INTERRUPTED, HARNESS-ERROR. It is strictly more informative
#   than the exit code, which collapses the last five onto 3.
#
#   The banner is emitted from an EXIT trap, so there is NO exit path that
#   prints nothing — not a crash, not Ctrl-C, not an internal `set -e` abort.
#   Because it is the LAST thing written, a truncating filter that keeps the
#   END of the stream (`| tail`) cannot hide it. When the outcome is not PASS
#   the same line is ALSO written to stderr, which a stdout-only pipe does not
#   capture at all.
#
#   THE EXIT CODE IS STILL LOSABLE BY THE CALLER and this runner cannot stop
#   that: `run-test.sh foo | tail` reports tail's status, so a FAIL reads as
#   exit 0. If you pipe, you MUST do one of:
#       run-test.sh foo; rc=$?            # capture first, filter after
#       set -o pipefail; run-test.sh foo | tail
#       run-test.sh foo | tail; rc=${PIPESTATUS[0]}   # bash/zsh only
#   or read the AUTOTEST_VERDICT line instead of the exit code.
#
# Result files are PER-RUN, never shared. Each invocation gets its own
# directory (timestamp + pid + test name) under ~/.ww3mod-tests/screenshots/,
# holding that run's result.json, screenshots and lifecycle log. Concurrent
# runners therefore cannot overwrite or misread each other's verdict. The
# legacy shared ~/.ww3mod-tests/result.json is no longer a verdict: it is
# overwritten with a "moved" stub pointing at the per-run path, so anything
# still reading it fails loudly instead of silently reporting a stranger's run.

set -e

GRAPHICS_MODE="Windowed"
POSITION="centered"
WINDOW_BEHAVIOR="background"
AUDIO_MUTE=1
SPEED_MULT=""
SEED=""
TIMEOUT_SECS=300
LIFECYCLE=0
MISSILE_TRACE=0
MISSILE_TRACE_MODE=full
SYNC_REPORTS=0
WINDOW_SIZE=""

while [ $# -gt 0 ]; do
	case "$1" in
		L|l|-L|-l|--left)       POSITION="left"; shift ;;
		R|r|-R|-r|--right)      POSITION="right"; shift ;;
		F|f|-F|-f|--full)       POSITION="full"; shift ;;
		C|c|-C|-c|--centered)   POSITION="centered"; shift ;;
		--fullscreen)
			GRAPHICS_MODE="PseudoFullscreen"
			POSITION="full"
			shift ;;
		--windowed)             GRAPHICS_MODE="Windowed"; shift ;;
		--position=*)           POSITION="${1#*=}"; shift ;;
		--background)           WINDOW_BEHAVIOR="background"; shift ;;
		--hidden)               WINDOW_BEHAVIOR="hidden"; shift ;;
		--minimized)            WINDOW_BEHAVIOR="minimized"; shift ;;
		--visible|--no-minimize|--foreground)
		                        WINDOW_BEHAVIOR="visible"; shift ;;
		--audio)                AUDIO_MUTE=0; shift ;;
		--mute)                 AUDIO_MUTE=1; shift ;;
		--size=*)               WINDOW_SIZE="${1#*=}"; shift ;;
		--size)                 WINDOW_SIZE="$2"; shift 2 ;;
		--speed=*)              SPEED_MULT="${1#*=}"; shift ;;
		--speed)                SPEED_MULT="$2"; shift 2 ;;
		--seed=*)               SEED="${1#*=}"; shift ;;
		--seed)                 SEED="$2"; shift 2 ;;
		--timeout=*)            TIMEOUT_SECS="${1#*=}"; shift ;;
		--timeout)              TIMEOUT_SECS="$2"; shift 2 ;;
		--lifecycle)            LIFECYCLE=1; shift ;;
		--missile-trace)        MISSILE_TRACE=1; shift ;;
		--missile-trace-summary) MISSILE_TRACE=1; MISSILE_TRACE_MODE=summary; shift ;;
		--sync-reports)         SYNC_REPORTS=1; shift ;;
		--help|-h)
			sed -n '2,130p' "$0" | sed 's/^# \?//'
			exit 0 ;;
		--*)
			echo "Unknown flag: $1"
			exit 3 ;;
		*)
			break ;;
	esac
done

TEST_NAME="$1"
if [ -z "${TEST_NAME}" ]; then
	echo "Usage: $0 [L|R|F] [--background|--hidden|--minimized|--visible] [--audio] [--size WxH] [--speed N] [--seed N] [--timeout N] [--lifecycle] [--missile-trace] <test-folder-name>"
	echo "  e.g.  $0 test-artillery-turret"
	exit 3
fi

# ── Outcome reporting ───────────────────────────────────────────────────────
# THE PROBLEM THIS SOLVES: exit 3 used to mean six different things (crash,
# hang, bad flag, missing map, lock contention, unparseable verdict) and two of
# them printed nothing distinguishing, so "exit 3" left the reader to GUESS
# whether the build was broken or the harness was. Worse, a caller piping into
# `tail` sees tail's exit status, so a FAIL arrives as exit 0.
#
# Every exit from here on runs emit_verdict via the EXIT trap — including
# `set -e` aborts, Ctrl-C and crashes — so no path is silent. OUTCOME defaults
# to HARNESS-ERROR and is narrowed only at points that actually determined
# something, which means an unforeseen abort reports as a harness error rather
# than inheriting a stale PASS.
#
# The last line is deliberately the machine-readable one: `| tail` keeps the
# END of a stream, so a truncating filter cannot make a failure look clean.
# Non-PASS outcomes also go to stderr, which a stdout-only pipe never sees.
# What this CANNOT fix is the caller's own `$?` — see the header.
OUTCOME="HARNESS-ERROR"
RUN_ID="(not started)"
RESULT_FILE=""
LOCK_DIR=""
LOCK_HELD=0

emit_verdict() {
	_code=$?
	if [ "${LOCK_HELD}" = "1" ] && [ -n "${LOCK_DIR}" ]; then
		rm -rf "${LOCK_DIR}" 2>/dev/null || true
	fi
	printf '\n============================================================\n'
	printf '==> VERDICT: %s   (exit %s)\n' "${OUTCOME}" "${_code}"
	printf '==>   test:   %s\n' "${TEST_NAME}"
	printf '==>   run:    %s\n' "${RUN_ID}"
	if [ -n "${RESULT_FILE}" ]; then
		printf '==>   result: %s\n' "${RESULT_FILE}"
	fi
	printf '============================================================\n'
	printf 'AUTOTEST_VERDICT outcome=%s exit=%s test=%s run=%s\n' \
		"${OUTCOME}" "${_code}" "${TEST_NAME}" "${RUN_ID}"
	# Non-PASS also goes to stderr, but ONLY when stdout is not a terminal —
	# i.e. exactly when stdout might be piped into a filter or redirected to a
	# log and the verdict could be lost. On a tty the human already sees it, and
	# a duplicated line there just reads like a bug.
	if [ "${_code}" != "0" ] && [ ! -t 1 ]; then
		printf 'AUTOTEST_VERDICT outcome=%s exit=%s test=%s run=%s\n' \
			"${OUTCOME}" "${_code}" "${TEST_NAME}" "${RUN_ID}" >&2
	fi
	exit "${_code}"
}
trap emit_verdict EXIT

# Validate --speed if supplied: integer 1-16 (matches TestMode arg clamp).
if [ -n "${SPEED_MULT}" ]; then
	case "${SPEED_MULT}" in
		''|*[!0-9]*)
			echo "Error: --speed must be an integer 1-16 (got '${SPEED_MULT}')"
			exit 3 ;;
	esac
	if [ "${SPEED_MULT}" -lt 1 ] || [ "${SPEED_MULT}" -gt 16 ]; then
		echo "Error: --speed must be 1-16 (got '${SPEED_MULT}')"
		exit 3
	fi
fi

# Validate --seed if supplied: a (possibly negative) integer. Matches the engine's
# int Test.RandomSeed; the DateTime.Now fallback can be negative, so allow a sign.
if [ -n "${SEED}" ]; then
	_seed_digits="${SEED#-}"
	case "${_seed_digits}" in
		''|*[!0-9]*)
			echo "Error: --seed must be an integer, e.g. 1017 or -42 (got '${SEED}')"
			exit 3 ;;
	esac
	# Reject 0 (incl. -0/00): the engine treats RandomSeed==0 as the *unset*
	# sentinel (World.cs LocalRandom guard) and falls back to a wall-clock seed,
	# so --seed 0 would NOT reproduce despite the harness reporting a fixed seed
	# and the verdict stamping "seed":0. _seed_digits is all-digits here, so a
	# value with no non-zero digit is exactly zero.
	case "${_seed_digits}" in
		*[!0]*) : ;;
		*)
			echo "Error: --seed 0 is reserved as the unset sentinel; pick any non-zero int"
			exit 3 ;;
	esac
fi

# Validate --timeout: positive integer (seconds). Pure wall-clock; deliberately
# NOT scaled by --speed (a hung game never advances the sim, so speed is moot).
case "${TIMEOUT_SECS}" in
	''|*[!0-9]*)
		echo "Error: --timeout must be a positive integer (seconds), got '${TIMEOUT_SECS}'"
		exit 3 ;;
esac
if [ "${TIMEOUT_SECS}" -lt 1 ]; then
	echo "Error: --timeout must be >= 1 (got '${TIMEOUT_SECS}')"
	exit 3
fi

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

# Detect Git-Bash / MSYS / Cygwin. On those, paths handed to the .NET game
# process must be Windows-form (C:\...). Identity passthrough elsewhere so
# macOS/Linux behavior is byte-for-byte unchanged.
IS_WINDOWS=0
case "$(uname -s)" in
	MINGW*|MSYS*|CYGWIN*|Windows_NT) IS_WINDOWS=1 ;;
esac
to_game_path() {
	if [ "${IS_WINDOWS}" = "1" ] && command -v cygpath >/dev/null 2>&1; then
		cygpath -w "$1"
	else
		printf '%s' "$1"
	fi
}

# Kill the backgrounded launcher and the game it spawned. The PID we hold is the
# launch-game.sh shell; on Git-Bash the actual game is a dotnet.exe child, so a
# plain `kill` of the shell can orphan it. Translate to the Windows PID and
# taskkill the whole tree (//T //F); fall back to POSIX kill everywhere.
kill_game() {
	_pid="$1"
	if [ "${IS_WINDOWS}" = "1" ]; then
		_winpid=""
		[ -r "/proc/${_pid}/winpid" ] && _winpid=$(cat "/proc/${_pid}/winpid" 2>/dev/null || true)
		if [ -n "${_winpid}" ] && command -v taskkill >/dev/null 2>&1; then
			taskkill //PID "${_winpid}" //T //F >/dev/null 2>&1 || true
		fi
	else
		# macOS/Linux: kill the game child first (pkill if present), then the shell.
		command -v pkill >/dev/null 2>&1 && pkill -P "${_pid}" 2>/dev/null || true
	fi
	kill "${_pid}" 2>/dev/null || true
}

# Best-effort locate of the engine's debug.log, mirroring the settings.yaml
# candidate search below. Echoes a path (may not exist) or nothing.
find_debug_log() {
	case "$(uname -s)" in
		Darwin) echo "${HOME}/Library/Application Support/OpenRA/Logs/debug.log" ;;
		Linux)  echo "${HOME}/.config/openra/Logs/debug.log" ;;
		MINGW*|MSYS*|CYGWIN*|Windows_NT)
			for _c in \
				"${REPO_ROOT}/engine/Support/Logs/debug.log" \
				"$(cygpath -u "${APPDATA:-}" 2>/dev/null)/OpenRA/Logs/debug.log" \
				"$(cygpath -u "${USERPROFILE:-}" 2>/dev/null)/Documents/OpenRA/Logs/debug.log"; do
				if [ -f "${_c}" ]; then echo "${_c}"; return; fi
			done
			;;
	esac
}

MAP_DIR="tools/autotest/scenarios/${TEST_NAME}"
if [ ! -d "${MAP_DIR}" ]; then
	echo "Error: test map not found at ${MAP_DIR}"
	exit 3
fi

# Detect screen size on macOS for window positioning. Falls back to 1920x1080.
SCREEN_W=1920
SCREEN_H=1080
if command -v osascript >/dev/null 2>&1; then
	BOUNDS=$(osascript -e 'tell application "Finder" to get bounds of window of desktop' 2>/dev/null || true)
	if [ -n "${BOUNDS}" ]; then
		DETECTED_W=$(echo "${BOUNDS}" | awk -F', *' '{print $3}')
		DETECTED_H=$(echo "${BOUNDS}" | awk -F', *' '{print $4}')
		[ -n "${DETECTED_W}" ] && SCREEN_W=${DETECTED_W}
		[ -n "${DETECTED_H}" ] && SCREEN_H=${DETECTED_H}
	fi
fi

# Build size + position based on POSITION choice.
WINDOW_ARGS=""
WINDOW_POS_ENV=""
case "${POSITION}" in
	centered)
		# Wide but with a visible margin all around — leaves the menu bar/dock free.
		W=$((SCREEN_W * 90 / 100))
		H=$((SCREEN_H * 85 / 100))
		X=$(((SCREEN_W - W) / 2))
		Y=$(((SCREEN_H - H) / 2))
		# Bias slightly downward so the macOS menu bar stays clear.
		[ ${Y} -lt 32 ] && Y=32
		WINDOW_ARGS="Graphics.WindowedSize=${W},${H}"
		WINDOW_POS_ENV="${X},${Y}"
		;;
	right)
		HALF_W=$((SCREEN_W / 2))
		USABLE_H=$((SCREEN_H - 40))
		WINDOW_ARGS="Graphics.WindowedSize=${HALF_W},${USABLE_H}"
		WINDOW_POS_ENV="${HALF_W},32"
		;;
	left)
		HALF_W=$((SCREEN_W / 2))
		USABLE_H=$((SCREEN_H - 40))
		WINDOW_ARGS="Graphics.WindowedSize=${HALF_W},${USABLE_H}"
		WINDOW_POS_ENV="0,32"
		;;
	full)
		# Don't set size or position; let the user's settings.yaml decide.
		;;
	*)
		echo "Unknown position: ${POSITION} (expected: centered, left, right, full)"
		exit 3 ;;
esac

# --size overrides whatever the position shorthand derived from this machine's screen.
# Position (WINDOW_POS_ENV) is deliberately left alone: only the SIZE decides whether
# absolutely-positioned chrome overlaps, and a pinned size with a centered origin still
# fits on any monitor at least that large.
if [ -n "${WINDOW_SIZE}" ]; then
	SIZE_W=$(echo "${WINDOW_SIZE}" | sed -n 's/^\([0-9][0-9]*\)[xX,]\([0-9][0-9]*\)$/\1/p')
	SIZE_H=$(echo "${WINDOW_SIZE}" | sed -n 's/^\([0-9][0-9]*\)[xX,]\([0-9][0-9]*\)$/\2/p')
	if [ -z "${SIZE_W}" ] || [ -z "${SIZE_H}" ]; then
		echo "Bad --size '${WINDOW_SIZE}' (expected WxH, e.g. 1024x768)"
		exit 3
	fi

	if [ "${POSITION}" = "full" ]; then
		echo "Note: --size is ignored with F/--full/--fullscreen."
	else
		WINDOW_ARGS="Graphics.WindowedSize=${SIZE_W},${SIZE_H}"
		echo "Window size pinned to ${SIZE_W}x${SIZE_H}."
	fi
fi

# Pick a result path under the user's HOME so the engine can write to it
# regardless of where Platform.SupportDir lands.
RESULT_DIR="${HOME}/.ww3mod-tests"
mkdir -p "${RESULT_DIR}"

# The pre-2026-08-12 shared verdict path. Nothing writes a VERDICT here any
# more (see the per-run directory below); it is only stubbed out, so a reader
# that still points at it gets a loud redirect instead of a stranger's result.
LEGACY_RESULT_FILE="${RESULT_DIR}/result.json"

# ── Single-instance lock ────────────────────────────────────────────────────
# Verdicts are per-run now, so this lock is no longer what stops two runs
# corrupting each other's RESULT — but it is still load-bearing and must not be
# weakened. It serialises the things that are STILL shared: the settings.yaml
# backup/restore around the launch, the engine's single support directory
# (debug.log, exception-*.log, syncreport-*.log — all of which the crash
# detection below attributes by mtime), and the machine's one screen and focus.
# Two games at once also cost more than the machine has. Original defect it was
# written for, still worth reading: run B's verdict satisfied run A's "has a
# verdict been written?" watchdog poll, so A stopped watching and left its game
# running forever while reporting B's result. Observed 2026-08-10 — two
# overlapping `run-batch.sh --all` invocations left orphaned dotnet.exe games
# stacking up on screen, one outliving its own 300s watchdog by minutes.
#
# `mkdir` is atomic on every platform this runs on, so it is the lock primitive.
# A lock whose recorded PID is gone is stale (a previous run was killed) and is
# reclaimed rather than blocking forever.
LOCK_DIR="${RESULT_DIR}/run.lock"
if ! mkdir "${LOCK_DIR}" 2>/dev/null; then
	_holder=$(cat "${LOCK_DIR}/pid" 2>/dev/null || true)
	_holder_test=$(cat "${LOCK_DIR}/test" 2>/dev/null || echo "unknown test")
	# An EMPTY pid means the holder is mid-acquisition (mkdir at :317 is atomic, but
	# the pid is only written at :333) — that is the opposite of stale. Treating it as
	# dead reclaims a LIVE run's lock, and the rm -f below then destroys that run's
	# verdict, which is unrecoverable. Observed 2026-08-12: a concurrent worker took
	# this path and wiped a completed saved-game-restore verdict between the game
	# writing it and the runner archiving it. Bail when we cannot prove the holder dead.
	if [ -z "${_holder}" ]; then
		echo "Error: another autotest run is acquiring the lock (${_holder_test}). Retry in a moment."
		exit 3
	fi
	if kill -0 "${_holder}" 2>/dev/null; then
		echo "Error: another autotest run is already in flight (pid ${_holder}, ${_holder_test})."
		echo "       The harness is single-instance: one game, one screen, one engine log dir."
		echo "       Wait for it, or kill it, then retry."
		exit 3
	fi
	echo "==> Reclaiming stale lock from dead pid ${_holder:-?} (${_holder_test})."
	rm -rf "${LOCK_DIR}"
	if ! mkdir "${LOCK_DIR}" 2>/dev/null; then
		echo "Error: could not acquire ${LOCK_DIR}"
		exit 3
	fi
fi
echo $$ > "${LOCK_DIR}/pid"
echo "${TEST_NAME}" > "${LOCK_DIR}/test"
# Hand lock ownership to the EXIT trap installed above (which also prints the
# verdict banner). Set only AFTER the pid file exists, so the trap never removes
# a lock this run does not yet own.
LOCK_HELD=1

# ── Per-run output directory ────────────────────────────────────────────────
# EVERY artifact of this run lives in here: result.json, screenshots, lifecycle
# log. The directory name carries the pid as well as the timestamp, so it cannot
# collide with a concurrent runner even at the same second — and the leaf is
# created with a bare `mkdir` (not `mkdir -p`) so a collision would FAIL rather
# than silently share a destination.
#
# This replaces a single shared ${RESULT_DIR}/result.json, which destroyed or
# misreported a verdict three times in two days: run B's result read as run A's,
# and A's `rm -f` at start-of-run deleted a verdict B had already produced. A
# shared destination fails silently — the file is there, it is just not yours —
# which is the worst possible shape for a result you are about to act on.
RUN_ID="$(date +%y%m%d_%H%M%S)_p$$_${TEST_NAME}"
RUN_DIR="${RESULT_DIR}/screenshots/${RUN_ID}"
mkdir -p "${RESULT_DIR}/screenshots"
if ! mkdir "${RUN_DIR}" 2>/dev/null; then
	echo "Error: per-run directory already exists, refusing to share it: ${RUN_DIR}"
	exit 3
fi
SCREENSHOT_DIR="${RUN_DIR}"
RESULT_FILE="${RUN_DIR}/result.json"

# Neutralise the legacy shared path. Anything still reading it (an old script, a
# stale note, a habit) would otherwise pick up whichever run last wrote there and
# report it as its own. A stub is not a verdict: it has no "status":"pass" for a
# grep to match, and it names the per-run path to look in instead. Written under
# the lock, so it cannot race a concurrent run-test.sh.
printf '{"note":"MOVED - this shared path is no longer a verdict. Per-run result: %s","status":"moved","run":"%s"}\n' \
	"${RESULT_FILE}" "${RUN_ID}" > "${LEGACY_RESULT_FILE}" 2>/dev/null || true

# Cleanup: drop screenshot runs older than 7 days so /.ww3mod-tests/screenshots
# doesn't grow unboundedly. Best-effort — failures (e.g. permissions) ignored.
find "${RESULT_DIR}/screenshots" -mindepth 1 -maxdepth 1 -type d -mtime +7 \
	-exec rm -rf {} \; 2>/dev/null || true

# Optional one-line description shown in the TEST MODE panel.
# Read from <map-folder>/description.txt; first non-empty line wins.
TEST_DESCRIPTION=""
if [ -f "${MAP_DIR}/description.txt" ]; then
	TEST_DESCRIPTION=$(awk 'NF { print; exit }' "${MAP_DIR}/description.txt" | tr -d '\r')
fi

AUDIO_LABEL="muted"
[ "${AUDIO_MUTE}" = "0" ] && AUDIO_LABEL="audio"

SPEED_LABEL="1x"
[ -n "${SPEED_MULT}" ] && SPEED_LABEL="${SPEED_MULT}x"

echo "==> Test: ${TEST_NAME}"
echo "==> Mode: ${GRAPHICS_MODE} (${POSITION}, ${WINDOW_BEHAVIOR}, ${AUDIO_LABEL}, ${SPEED_LABEL})"
if [ -n "${SEED}" ]; then
	echo "==> Seed: ${SEED} (fixed — reproducible run)"
else
	echo "==> Seed: wall-clock (recorded in result.json; rerun with --seed <that> to reproduce)"
fi
[ -n "${WINDOW_POS_ENV}" ] && echo "==> Position: ${WINDOW_POS_ENV} on ${SCREEN_W}x${SCREEN_H}"
[ -n "${TEST_DESCRIPTION}" ] && echo "==> Description: ${TEST_DESCRIPTION}"
echo "==> Run id:      ${RUN_ID}"
echo "==> Run dir:     ${RUN_DIR}   (result.json + screenshots + lifecycle log)"
echo "==> Result file: ${RESULT_FILE}"
echo

# OpenRA's SDL platform reads OPENRA_WINDOW_X/Y at window creation (engine
# patch). Falls back to SDL_WINDOWPOS_CENTERED_DISPLAY when unset.
if [ -n "${WINDOW_POS_ENV}" ]; then
	export OPENRA_WINDOW_X="${WINDOW_POS_ENV%,*}"
	export OPENRA_WINDOW_Y="${WINDOW_POS_ENV#*,}"
fi

# Both hidden and minimized launches suspend engine-side rendering, and a low
# framerate cap would then throttle the *suspended* sim to a few ticks/s (the
# logic gate only clears at the render cadence). Force the cap off in either case
# so the run free-runs. See WORKSPACE/plans/260721_sim_throughput.md, Option C.
SUSPEND_ARGS=""
if [ "${WINDOW_BEHAVIOR}" = "hidden" ] && [ "${POSITION}" != "full" ] && [ "${GRAPHICS_MODE}" = "Windowed" ]; then
	# Engine reads OPENRA_WINDOW_HIDDEN=1 and creates the window with
	# SDL_WINDOW_HIDDEN (never mapped, never focus-steals). The robust unattended
	# profile — no visible black window on Windows (bugs/discovered.md 2026-07-22).
	export OPENRA_WINDOW_HIDDEN=1
	SUSPEND_ARGS="Graphics.CapFramerate=false"
elif [ "${WINDOW_BEHAVIOR}" = "minimized" ] && [ "${POSITION}" != "full" ] && [ "${GRAPHICS_MODE}" = "Windowed" ]; then
	# Engine reads OPENRA_WINDOW_MINIMIZED=1 and calls SDL_MinimizeWindow after
	# window creation. Legacy opt-in — miniaturized SDL windows are awkward to
	# restore on macOS (Cmd+Tab can't unminiaturize — only the small dock icon
	# next to Trash does).
	export OPENRA_WINDOW_MINIMIZED=1
	SUSPEND_ARGS="Graphics.CapFramerate=false"
fi

# Audio mute via the Sound.Mute toggle (not by zeroing volumes — that would
# risk polluting the saved volume levels if the engine auto-saves settings).
# Sound.Mute is the same flag the in-game mute hotkey toggles.
AUDIO_ARGS=""
if [ "${AUDIO_MUTE}" = "1" ]; then
	AUDIO_ARGS="Sound.Mute=true"
fi

# Speed multiplier (Test.SpeedMultiplier) — applied universally at world load by
# the TestModeSpeedMultiplier trait. Unset → 1× (byte-identical to old behavior).
SPEED_ARGS=""
if [ -n "${SPEED_MULT}" ]; then
	SPEED_ARGS="Test.SpeedMultiplier=${SPEED_MULT}"
fi

# Fixed RNG seed (Test.RandomSeed) — reproducible match. Unset → engine falls back
# to a DateTime.Now-derived seed, which World stamps into the verdict regardless.
SEED_ARGS=""
if [ -n "${SEED}" ]; then
	SEED_ARGS="Test.RandomSeed=${SEED}"
fi

# macOS focus handling. Capture the currently-frontmost app so we can:
#   1. Bounce focus back to it after the game window appears (background mode).
#   2. Restore focus after the game exits (defends against the close-time
#      focus shuffle that picks a random next-frontmost app).
PREV_APP=""
RESTORE_PID=""
if [ "$(uname)" = "Darwin" ] && command -v osascript >/dev/null 2>&1; then
	PREV_APP=$(osascript -e 'tell application "System Events" to name of first application process whose frontmost is true' 2>/dev/null || true)
fi

# Background-mode watchdog: poll for ~5s; once frontmost flips away from
# PREV_APP (i.e. OpenRA grabbed focus), bounce back to PREV_APP.
if [ "${WINDOW_BEHAVIOR}" = "background" ] \
	&& [ "${GRAPHICS_MODE}" = "Windowed" ] \
	&& [ -n "${PREV_APP}" ]; then
	(
		i=0
		while [ ${i} -lt 20 ]; do
			CURRENT=$(osascript -e 'tell application "System Events" to name of first application process whose frontmost is true' 2>/dev/null || echo "")
			if [ -n "${CURRENT}" ] && [ "${CURRENT}" != "${PREV_APP}" ]; then
				# Give the game a brief moment to settle, then defocus it.
				sleep 0.4
				osascript -e "tell application \"${PREV_APP}\" to activate" 2>/dev/null || true
				exit 0
			fi
			sleep 0.25
			i=$((i + 1))
		done
	) &
	RESTORE_PID=$!
fi

# Back up settings.yaml around the launch. The engine sometimes auto-saves
# settings during normal flow (the launch-game.sh comment about Graphics.Mode
# pollution alludes to this), and a saved Sound.Mute=true would carry over to
# normal launches. Restoring the file post-run sidesteps the risk entirely.
SETTINGS_FILE=""
SETTINGS_BACKUP=""
case "$(uname -s)" in
	Darwin) SETTINGS_FILE="${HOME}/Library/Application Support/OpenRA/settings.yaml" ;;
	Linux)  SETTINGS_FILE="${HOME}/.config/openra/settings.yaml" ;;
	MINGW*|MSYS*|CYGWIN*|Windows_NT)
		# engine/Support override, then %APPDATA%\OpenRA (modern), then
		# Documents\OpenRA (legacy). First existing wins; empty → skip backup.
		for _cand in \
			"${REPO_ROOT}/engine/Support/settings.yaml" \
			"$(cygpath -u "${APPDATA:-}" 2>/dev/null)/OpenRA/settings.yaml" \
			"$(cygpath -u "${USERPROFILE:-}" 2>/dev/null)/Documents/OpenRA/settings.yaml"; do
			if [ -f "${_cand}" ]; then SETTINGS_FILE="${_cand}"; break; fi
		done
		;;
esac
if [ -n "${SETTINGS_FILE}" ] && [ -f "${SETTINGS_FILE}" ]; then
	SETTINGS_BACKUP="${RESULT_DIR}/settings.yaml.bak"
	cp "${SETTINGS_FILE}" "${SETTINGS_BACKUP}"
fi

# Marker for crash detection after the run: any engine log written AFTER this
# point belongs to this run. A file mtime comparison is used rather than a clock
# reading so it works the same wherever the support directory lives. It lives in
# the per-run dir (self-cleaning, no shared name) and is created BEFORE the
# launch — a game that dies in the first second would otherwise write its
# exception log ahead of the marker and read as a hang rather than a crash.
RUN_MARKER="${RUN_DIR}/.run-marker"
: > "${RUN_MARKER}"

# Game-side args need Windows-form paths under Git-Bash; identity elsewhere.
RESULT_FILE_GAME=$(to_game_path "${RESULT_FILE}")
SCREENSHOT_DIR_GAME=$(to_game_path "${SCREENSHOT_DIR}")

# Behavior-lint (opt-in --lifecycle): the UnitLifecycleLogger writes a per-unit
# JSONL event stream to this sibling of the verdict file. Advisory only — the
# analyzer runs after the match and never changes the pass/fail verdict.
# Diagnostic (opt-in --sync-reports): arm sync reporting even with one human client,
# and dump the recording side on GameSaved. Only useful for saved-game restore
# desyncs; expensive per net frame, so it is off by default.
SYNC_REPORT_ARGS=""
if [ "${SYNC_REPORTS}" = "1" ]; then
	SYNC_REPORT_ARGS="Test.ForceSyncReports=true"
fi

LIFECYCLE_ARGS=""
LIFECYCLE_FILE="${RESULT_FILE%.json}.lifecycle.jsonl"
if [ "${LIFECYCLE}" = "1" ]; then
	rm -f "${LIFECYCLE_FILE}"
	LIFECYCLE_ARGS="Test.UnitLifecycleLog=$(to_game_path "${LIFECYCLE_FILE}")"
fi

# Missile audit (opt-in --missile-trace): MissileTrace writes a per-missile JSONL
# stream to this sibling of the verdict file. Pure observation — the flag changes
# neither the simulation nor the verdict, so a traced run and an untraced run of
# the same seed play out identically.
MISSILE_TRACE_ARGS=""
MISSILE_TRACE_FILE="${RESULT_FILE%.json}.missiles.jsonl"
if [ "${MISSILE_TRACE}" = "1" ]; then
	rm -f "${MISSILE_TRACE_FILE}"
	MISSILE_TRACE_ARGS="Test.MissileTraceLog=$(to_game_path "${MISSILE_TRACE_FILE}") Test.MissileTraceMode=${MISSILE_TRACE_MODE}"
fi

# Launcher indirection. Defaults to the real launcher, byte-for-byte the previous
# behaviour. It exists so tools/autotest/selftest.sh can drive the crash /
# no-result / timeout branches with a stub launcher — those branches are exactly
# the ones that have misreported verdicts, and they are unreachable in a test if
# proving them requires crashing a real game.
LAUNCHER="${AUTOTEST_LAUNCHER:-./launch-game.sh}"

"${LAUNCHER}" \
	"Launch.Map=${TEST_NAME}" \
	"Test.Mode=true" \
	"Test.Name=${TEST_NAME}" \
	"Test.Description=${TEST_DESCRIPTION}" \
	"Test.ResultPath=${RESULT_FILE_GAME}" \
	"Test.ScreenshotDir=${SCREENSHOT_DIR_GAME}" \
	"Graphics.Mode=${GRAPHICS_MODE}" \
	${WINDOW_ARGS} \
	${AUDIO_ARGS} \
	${SPEED_ARGS} \
	${SEED_ARGS} \
	${LIFECYCLE_ARGS} \
	${MISSILE_TRACE_ARGS} \
	${SYNC_REPORT_ARGS} \
	${SUSPEND_ARGS} \
	&
LAUNCH_PID=$!

# If this script is interrupted (Ctrl-C, terminal close, killed by a parent)
# the backgrounded launch-game.sh + its dotnet.exe game child would otherwise be
# orphaned and survive — they accumulate across sessions as stray dotnet.exe.
# Reap the whole tree on INT/TERM (kill_game uses taskkill //T on Windows). The
# normal-completion path below is untouched (it waits for a clean self-exit).
trap 'echo; echo "==> interrupted — killing the game."; OUTCOME="INTERRUPTED"; kill_game "${LAUNCH_PID}"; exit 130' INT TERM

# ── Hard wall-clock watchdog ────────────────────────────────────────────────
# The engine writes result.json only when Test.Pass/Fail/Skip runs. If a map's
# rules fail to load (e.g. a duplicate MiniYaml key), the game logs "Failed to
# load rules" to debug.log, falls back to the main menu, and idles FOREVER — no
# verdict is written and the window sits on screen until a human kills it. Poll
# once a second for either a written verdict or the game exiting on its own; if
# neither happens within TIMEOUT_SECS, kill the game and synthesize a FAIL so
# the runner (and run-batch) always get a definite result.
TIMED_OUT=0
_elapsed=0
while :; do
	if ! kill -0 "${LAUNCH_PID}" 2>/dev/null; then
		break            # launcher/game exited on its own
	fi
	if [ -f "${RESULT_FILE}" ]; then
		break            # verdict written; let the game exit itself
	fi
	if [ "${_elapsed}" -ge "${TIMEOUT_SECS}" ]; then
		TIMED_OUT=1
		break
	fi
	sleep 1
	_elapsed=$((_elapsed + 1))
done

if [ "${TIMED_OUT}" = "1" ]; then
	echo
	echo "==> TIMEOUT: no verdict after ${TIMEOUT_SECS}s — killing the game."
	kill_game "${LAUNCH_PID}"

	# Surface the most likely cause: rules that failed to load.
	DEBUG_LOG=$(find_debug_log)
	if [ -n "${DEBUG_LOG}" ] && [ -f "${DEBUG_LOG}" ]; then
		MATCHES=$(tail -n 200 "${DEBUG_LOG}" 2>/dev/null | grep -i "Failed to load rules" || true)
		if [ -n "${MATCHES}" ]; then
			echo "==> debug.log reports a rules-load failure:"
			printf '%s\n' "${MATCHES}" | sed 's/^/    /'
		else
			echo "==> No 'Failed to load rules' in ${DEBUG_LOG} tail — hang is elsewhere."
		fi
	else
		echo "==> Could not locate debug.log to diagnose the hang."
	fi

	# Synthetic verdict in the engine's schema (name/status/notes/timestamp) so
	# the STATUS grep below and run-batch's exit-code read both see a FAIL.
	# The OUTCOME name stays distinct from a real assertion FAIL: same exit code,
	# but "the game never answered" and "the game answered no" are different
	# findings and the banner must not conflate them (see the TIMED_OUT branch in
	# the outcome mapping at the end of this script).
	if [ ! -f "${RESULT_FILE}" ]; then
		NOW_ISO=$(date -u +%Y-%m-%dT%H:%M:%SZ)
		NOTES="timeout: no verdict after ${TIMEOUT_SECS}s - game hung or rules failed to load; check "'%APPDATA%\\OpenRA\\Logs\\debug.log'
		printf '{"name":"%s","status":"fail","notes":"%s","timestamp":"%s"}\n' \
			"${TEST_NAME}" "${NOTES}" "${NOW_ISO}" > "${RESULT_FILE}"
	fi
fi

# Reap the launcher (returns immediately if already gone or just killed).
wait "${LAUNCH_PID}" 2>/dev/null || true

if [ -n "${SETTINGS_BACKUP}" ] && [ -f "${SETTINGS_BACKUP}" ]; then
	mv "${SETTINGS_BACKUP}" "${SETTINGS_FILE}"
fi

# Reap the watchdog if it's still alive (game exited before window appeared).
if [ -n "${RESTORE_PID}" ]; then
	kill "${RESTORE_PID}" 2>/dev/null || true
	wait "${RESTORE_PID}" 2>/dev/null || true
fi

# Restore focus after the game exits — this is the fix for the close-time
# focus theft. macOS otherwise picks an arbitrary next-frontmost app.
if [ -n "${PREV_APP}" ]; then
	osascript -e "tell application \"${PREV_APP}\" to activate" 2>/dev/null || true
fi

echo

if [ ! -f "${RESULT_FILE}" ]; then
	# A MISSING RESULT IS A NAMED OUTCOME, NEVER SILENCE AND NEVER A PASS. Both a
	# crash and a hang produce no result file, and treating them as one thing is
	# expensive in both directions: a hang means "wait or look at the window", a
	# crash means "stop, the build is broken" — and a crash is sometimes the
	# POSITIVE finding, as when a bot-module sync guard throws from a finally and
	# firing IS the result you were looking for. So: name it, name the log, and
	# never swallow it.
	#
	# The engine writes exception-<timestamp>.log next to debug.log when it dies
	# (engine/OpenRA.Game/Support/ExceptionHandler.cs), so a log newer than this
	# run's marker is proof of a crash, and its first lines carry the exception
	# type and the throwing frame.
	CRASH_LOG=""
	_dbg="$(find_debug_log)"
	# Guard the empty case: dirname "" is ".", which would hunt the repo root for
	# exception logs and could attribute an unrelated file to this run.
	if [ -n "${_dbg}" ]; then
		_log_dir="$(dirname "${_dbg}")"
		if [ -d "${_log_dir}" ]; then
			CRASH_LOG="$(find "${_log_dir}" -maxdepth 1 -name 'exception-*.log' -newer "${RUN_MARKER}" 2>/dev/null | sort | tail -1)"
			# Sync-guard crashes also drop a syncreport-*.log, and that artifact is
			# ONLY written on the failure path — it is the thing that has twice told a
			# reader what actually happened. Name it too.
			SYNC_LOGS="$(find "${_log_dir}" -maxdepth 1 -name 'syncreport-*.log' -newer "${RUN_MARKER}" 2>/dev/null | sort | tail -3)"
		fi
	fi
	rm -f "${RUN_MARKER}" 2>/dev/null || true

	if [ -n "${CRASH_LOG}" ]; then
		OUTCOME="CRASH"
		echo "==> CRASHED — the game threw and died, so no verdict could be written."
		echo "==> A crash is a real finding, not a broken harness. Read the log before rerunning:"
		echo "==> ${CRASH_LOG}"
		sed -n '1,12p' "${CRASH_LOG}" | sed 's/^/    /'
		if [ -n "${SYNC_LOGS:-}" ]; then
			echo "==> Sync report(s) written by this run (failure path only):"
			printf '%s\n' "${SYNC_LOGS}" | sed 's/^/    /'
		fi
		exit 3
	fi

	OUTCOME="NO-RESULT"
	echo "==> NO RESULT FILE, and no crash log newer than this run's marker."
	echo "==> The game hung, was closed by hand, or never reached an assertion."
	echo "==> Expected verdict at: ${RESULT_FILE}"
	if [ -n "${SYNC_LOGS:-}" ]; then
		echo "==> Sync report(s) written by this run (failure path only):"
		printf '%s\n' "${SYNC_LOGS}" | sed 's/^/    /'
	fi
	exit 3
fi
rm -f "${RUN_MARKER}" 2>/dev/null || true

echo "==> Result:"
cat "${RESULT_FILE}"
echo

# The verdict is written directly into the per-run dir by the engine now, so the
# old "archive a copy out of the shared result.json" step is gone — there is
# nothing left to rescue it from.

# Behavior lint (advisory). The lifecycle log is already a sibling of the verdict
# inside the per-run dir, so there is nothing to archive either. This is purely
# informational: its output is echoed for the operator but the pass/fail exit
# below is untouched by it.
if [ "${LIFECYCLE}" = "1" ] && [ -f "${LIFECYCLE_FILE}" ]; then
	LINT_PY="$(dirname "$0")/../behavior-lint/behavior_lint.py"
	if [ -f "${LINT_PY}" ]; then
		echo "==> Behavior lint:"
		python3 "${LINT_PY}" "${LIFECYCLE_FILE}" || true
		echo
	fi
elif [ "${LIFECYCLE}" = "1" ]; then
	echo "==> Behavior lint: no lifecycle log written (${LIFECYCLE_FILE})."
	echo
fi

# PITFALL: Game.TakeScreenshot is async (ThreadPool via Renderer.SaveScreenshot).
# When the verdict is written from Test.Pass/Fail, the PNG files referenced in
# the JSON may still be flushing. A brief settle wait keeps the post-run
# listing accurate. 250ms is empirically enough for one or two captures.
sleep 0.25

# Surface any captured screenshots. The verdict JSON paths are authoritative,
# but a directory listing is the simplest "what's there" view for the runner.
if [ -d "${SCREENSHOT_DIR}" ]; then
	SHOT_COUNT=$(find "${SCREENSHOT_DIR}" -maxdepth 1 -name "*.png" -type f 2>/dev/null | wc -l | tr -d ' ')
	if [ "${SHOT_COUNT}" -gt 0 ]; then
		echo "==> Screenshots (${SHOT_COUNT}):"
		find "${SCREENSHOT_DIR}" -maxdepth 1 -name "*.png" -type f 2>/dev/null \
			| sort | sed 's|^|    |'
		echo
	fi
	# NOTE: the old "rmdir the dir if it holds no PNGs" cleanup is deliberately
	# gone. This directory now holds the authoritative result.json, so deleting it
	# for being screenshot-less would destroy the verdict. The 7-day prune above
	# is what bounds growth.
fi

STATUS=$(grep -o '"status":"[^"]*"' "${RESULT_FILE}" | head -1 | sed 's/"status":"\(.*\)"/\1/')

case "${STATUS}" in
	pass) OUTCOME="PASS"; exit 0 ;;
	fail)
		# A watchdog kill and a real assertion failure both write status=fail. Same
		# exit code (run-batch and CI depend on that), different name.
		if [ "${TIMED_OUT}" = "1" ]; then OUTCOME="TIMEOUT-FAIL"; else OUTCOME="FAIL"; fi
		exit 1 ;;
	skip) OUTCOME="SKIP"; exit 2 ;;
	*)
		# The file exists but carries no status this runner understands: truncated
		# write, schema drift, or something else wrote there. Not a pass.
		OUTCOME="BAD-VERDICT"
		echo "==> Unrecognised status '${STATUS}' in ${RESULT_FILE}"
		exit 3 ;;
esac
