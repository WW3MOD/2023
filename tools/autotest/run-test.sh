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
# Exit code: 0=pass, 1=fail, 2=skip, 3=error.

set -e

GRAPHICS_MODE="Windowed"
POSITION="centered"
WINDOW_BEHAVIOR="background"
AUDIO_MUTE=1
SPEED_MULT=""
SEED=""
TIMEOUT_SECS=300
LIFECYCLE=0
SYNC_REPORTS=0

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
		--speed=*)              SPEED_MULT="${1#*=}"; shift ;;
		--speed)                SPEED_MULT="$2"; shift 2 ;;
		--seed=*)               SEED="${1#*=}"; shift ;;
		--seed)                 SEED="$2"; shift 2 ;;
		--timeout=*)            TIMEOUT_SECS="${1#*=}"; shift ;;
		--timeout)              TIMEOUT_SECS="$2"; shift 2 ;;
		--lifecycle)            LIFECYCLE=1; shift ;;
		--sync-reports)         SYNC_REPORTS=1; shift ;;
		--help|-h)
			sed -n '2,97p' "$0" | sed 's/^# \?//'
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
	echo "Usage: $0 [L|R|F] [--background|--hidden|--minimized|--visible] [--audio] [--speed N] [--seed N] [--timeout N] [--lifecycle] <test-folder-name>"
	echo "  e.g.  $0 test-artillery-turret"
	exit 3
fi

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

# Pick a result path under the user's HOME so the engine can write to it
# regardless of where Platform.SupportDir lands.
RESULT_DIR="${HOME}/.ww3mod-tests"
mkdir -p "${RESULT_DIR}"
RESULT_FILE="${RESULT_DIR}/result.json"

# ── Single-instance lock ────────────────────────────────────────────────────
# RESULT_FILE is a SINGLE shared path, so two concurrent runs silently corrupt
# each other: run B's verdict satisfies run A's "has a verdict been written?"
# watchdog poll, A stops watching, and A's game is left running forever while A
# reports B's result. Observed 2026-08-10 — two overlapping `run-batch.sh --all`
# invocations left orphaned dotnet.exe games stacking up on screen, one of them
# outliving its own 300s watchdog by minutes.
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
		echo "       The harness is single-instance: results go to one shared ${RESULT_FILE}."
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
trap 'rm -rf "${LOCK_DIR}"' EXIT

rm -f "${RESULT_FILE}"

# Per-run screenshot output dir. Tests can capture via Test.Screenshot(label)
# in Lua; the PNGs land here with predictable filenames (NNN_<label>.png) and
# paths are echoed into the verdict JSON's screenshots[] array. Each run gets
# its own folder so successive runs of the same test don't clobber each other.
RUN_ID="$(date +%y%m%d_%H%M%S)_${TEST_NAME}"
SCREENSHOT_DIR="${RESULT_DIR}/screenshots/${RUN_ID}"
mkdir -p "${SCREENSHOT_DIR}"

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
echo "==> Result file: ${RESULT_FILE}"
echo "==> Screenshots: ${SCREENSHOT_DIR}"
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

./launch-game.sh \
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
	${SYNC_REPORT_ARGS} \
	${SUSPEND_ARGS} \
	&
LAUNCH_PID=$!

# If this script is interrupted (Ctrl-C, terminal close, killed by a parent)
# the backgrounded launch-game.sh + its dotnet.exe game child would otherwise be
# orphaned and survive — they accumulate across sessions as stray dotnet.exe.
# Reap the whole tree on INT/TERM (kill_game uses taskkill //T on Windows). The
# normal-completion path below is untouched (it waits for a clean self-exit).
trap 'echo; echo "==> interrupted — killing the game."; kill_game "${LAUNCH_PID}"; exit 130' INT TERM

# Marker for crash detection below: any exception log the engine writes AFTER this
# point belongs to this run. A file mtime comparison is used rather than a clock
# reading so it works the same wherever the support directory lives.
RUN_MARKER="$(mktemp 2>/dev/null || echo "${RESULT_DIR}/.run-marker")"
: > "${RUN_MARKER}"

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
	# A CRASH AND A HANG BOTH PRODUCE NO RESULT FILE, and until now both printed the same
	# line — which is a genuinely expensive ambiguity: a hang means "wait or look at the
	# window", a crash means "the build is broken, stop". The engine writes
	# exception-<timestamp>.log next to debug.log when it dies, so a log newer than this
	# run's marker is proof of a crash, and the first lines carry the exception type and
	# the throwing frame.
	CRASH_LOG=""
	_log_dir="$(dirname "$(find_debug_log)")"
	if [ -d "${_log_dir}" ]; then
		CRASH_LOG="$(find "${_log_dir}" -maxdepth 1 -name 'exception-*.log' -newer "${RUN_MARKER}" 2>/dev/null | sort | tail -1)"
	fi
	rm -f "${RUN_MARKER}" 2>/dev/null || true

	if [ -n "${CRASH_LOG}" ]; then
		echo "==> CRASHED — the game threw and died, so no verdict could be written."
		echo "==> ${CRASH_LOG}"
		sed -n '1,12p' "${CRASH_LOG}" | sed 's/^/    /'
		exit 3
	fi

	echo "==> No result file written, and no crash log — the test hung or was closed by hand."
	exit 3
fi
rm -f "${RUN_MARKER}" 2>/dev/null || true

echo "==> Result:"
cat "${RESULT_FILE}"
echo

# Archive the verdict into the per-run dir so batch runs don't lose it. run-batch.sh
# calls this script once per seed and the single ${RESULT_FILE} (result.json) is rm -f'd
# at the top of every run, so only the LAST seed's verdict survives there. RUN_ID is
# unique per invocation (timestamp + scenario), so this copy keeps every seed's verdict,
# alongside that run's screenshots. The 7-day screenshot-dir prune (find -mtime +7 above)
# also reaps these, so no unbounded growth. cp is POSIX-portable (macOS bash).
if [ -d "${SCREENSHOT_DIR}" ]; then
	cp "${RESULT_FILE}" "${SCREENSHOT_DIR}/result.json" 2>/dev/null || true
	echo "==> Verdict archived: ${SCREENSHOT_DIR}/result.json"
	echo
fi

# Behavior lint (advisory). If --lifecycle produced a log, archive it beside the
# verdict and run the analyzer. This is purely informational: its output is
# echoed for the operator but the pass/fail exit below is untouched by it.
if [ "${LIFECYCLE}" = "1" ] && [ -f "${LIFECYCLE_FILE}" ]; then
	if [ -d "${SCREENSHOT_DIR}" ]; then
		cp "${LIFECYCLE_FILE}" "${SCREENSHOT_DIR}/result.lifecycle.jsonl" 2>/dev/null || true
	fi
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
	else
		# Empty per-run dir is just clutter; drop it so the screenshots/ folder
		# only carries dirs that actually contain captures.
		rmdir "${SCREENSHOT_DIR}" 2>/dev/null || true
	fi
fi

STATUS=$(grep -o '"status":"[^"]*"' "${RESULT_FILE}" | head -1 | sed 's/"status":"\(.*\)"/\1/')

case "${STATUS}" in
	pass) exit 0 ;;
	fail) exit 1 ;;
	skip) exit 2 ;;
	*)    exit 3 ;;
esac
