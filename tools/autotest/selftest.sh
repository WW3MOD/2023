#!/bin/sh
# WW3MOD autotest harness — self-test for run-test.sh's RESULT REPORTING.
#
# This does NOT start a game and does NOT test any gameplay. It drives
# run-test.sh with a stub launcher (AUTOTEST_LAUNCHER) and a sandboxed HOME, and
# asserts that each way a run can end is reported as a distinct named outcome
# with the right exit code.
#
# Why it exists: between 2026-08-10 and 2026-08-12 the harness destroyed or
# misreported a verdict four times — a shared result path overwritten by a
# concurrent run, an exit code swallowed by `| tail`, and a crash that was
# indistinguishable from a broken harness. Every one was caught by a human
# noticing, never by tooling. These are the cases that must stay caught.
#
# Usage:  ./tools/autotest/selftest.sh
# Takes about a minute on macOS — almost all of it is run-test.sh's osascript
# screen-size and focus queries, which run once per case and are not what is
# being tested. No game is launched.
# Exit code: 0 if every case reports correctly, else the number of failures.

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

RUNNER="./tools/autotest/run-test.sh"
SCENARIO="test-paladin-fires"     # only needs to EXIST; the stub never plays it
# --hidden skips the macOS background-focus watchdog, which otherwise polls a
# full 5s per case waiting for a game window that a stub never opens. Nothing in
# what is under test here depends on window behaviour.
RUNNER_FLAGS="--hidden"
FAILURES=0

if [ ! -d "tools/autotest/scenarios/${SCENARIO}" ]; then
	echo "selftest: scenario ${SCENARIO} is missing; pick another existing one"
	exit 1
fi

SANDBOX="$(mktemp -d)"
trap 'rm -rf "${SANDBOX}"' EXIT

# Each case gets a fresh sandbox HOME, so it cannot touch the real
# ~/.ww3mod-tests — in particular it can never take or break the real run lock
# while another worker is running actual tests.
new_home() {
	_h="${SANDBOX}/home_$1"
	# find_debug_log looks here on macOS; Linux uses .config/openra. Create both so
	# the crash-detection case works on either.
	mkdir -p "${_h}/Library/Application Support/OpenRA/Logs" \
	         "${_h}/.config/openra/Logs"
	printf '%s' "${_h}"
}

log_dir_for() {
	if [ "$(uname -s)" = "Darwin" ]; then
		printf '%s' "$1/Library/Application Support/OpenRA/Logs"
	else
		printf '%s' "$1/.config/openra/Logs"
	fi
}

# Stub launchers. Each mimics one way a real run can end. They receive the same
# argv the game would, so they can find Test.ResultPath the same way it does.
make_stub() {
	_path="${SANDBOX}/stub_$1.sh"
	cat > "${_path}"
	chmod +x "${_path}"
	printf '%s' "${_path}"
}

result_path_from_argv='
for a in "$@"; do
	case "$a" in Test.ResultPath=*) RP="${a#Test.ResultPath=}" ;; esac
done
'

check() {
	_name="$1"; _want_outcome="$2"; _want_exit="$3"; _got_exit="$4"; _output="$5"
	_got_outcome=$(printf '%s' "${_output}" | grep '^AUTOTEST_VERDICT' | tail -1 \
		| sed 's/.*outcome=\([^ ]*\).*/\1/')
	if [ "${_got_outcome}" = "${_want_outcome}" ] && [ "${_got_exit}" = "${_want_exit}" ]; then
		printf '  ok    %-34s outcome=%-13s exit=%s\n' "${_name}" "${_got_outcome}" "${_got_exit}"
	else
		printf '  FAIL  %-34s wanted outcome=%s exit=%s, got outcome=%s exit=%s\n' \
			"${_name}" "${_want_outcome}" "${_want_exit}" "${_got_outcome:-<none>}" "${_got_exit}"
		FAILURES=$((FAILURES + 1))
	fi
}

echo "==> run-test.sh outcome reporting"

# ── A pass, and a fail, written by the "game" to the path it was handed ──────
# Also proves the engine is told a PER-RUN path: the stub writes only to the
# Test.ResultPath it received, and the runner must read the verdict back.
H=$(new_home pass)
STUB=$(make_stub pass <<EOF
#!/bin/sh
${result_path_from_argv}
printf '{"name":"x","status":"pass","notes":"stub"}' > "\${RP}"
EOF
)
OUT=$(HOME="${H}" AUTOTEST_LAUNCHER="${STUB}" "${RUNNER}" ${RUNNER_FLAGS} "${SCENARIO}" 2>&1); RC=$?
check "verdict pass" PASS 0 "${RC}" "${OUT}"

H=$(new_home fail)
STUB=$(make_stub fail <<EOF
#!/bin/sh
${result_path_from_argv}
printf '{"name":"x","status":"fail","notes":"stub"}' > "\${RP}"
EOF
)
OUT=$(HOME="${H}" AUTOTEST_LAUNCHER="${STUB}" "${RUNNER}" ${RUNNER_FLAGS} "${SCENARIO}" 2>&1); RC=$?
check "verdict fail" FAIL 1 "${RC}" "${OUT}"

# ── Crash: died writing nothing, but left a fresh exception log ──────────────
# This is the shape that was read as "broken harness" — and in the sync-guard
# case the crash WAS the finding.
H=$(new_home crash)
LOGS=$(log_dir_for "${H}")
STUB=$(make_stub crash <<EOF
#!/bin/sh
sleep 1
printf 'Exception of type SyncGuardException\n  at Fake.Frame()\n' > "${LOGS}/exception-selftest.log"
exit 1
EOF
)
OUT=$(HOME="${H}" AUTOTEST_LAUNCHER="${STUB}" "${RUNNER}" ${RUNNER_FLAGS} "${SCENARIO}" 2>&1); RC=$?
check "crash (fresh exception log)" CRASH 3 "${RC}" "${OUT}"
if ! printf '%s' "${OUT}" | grep -q 'exception-selftest.log'; then
	echo "  FAIL  crash report does not name the exception log"
	FAILURES=$((FAILURES + 1))
fi

# ── A crash log that PREDATES the run must not be attributed to it ───────────
H=$(new_home stalecrash)
LOGS=$(log_dir_for "${H}")
printf 'old crash from a previous run\n' > "${LOGS}/exception-ancient.log"
# Backdate it well before the run's marker.
touch -t 202001010000 "${LOGS}/exception-ancient.log"
STUB=$(make_stub stalecrash <<'EOF'
#!/bin/sh
exit 1
EOF
)
OUT=$(HOME="${H}" AUTOTEST_LAUNCHER="${STUB}" "${RUNNER}" ${RUNNER_FLAGS} "${SCENARIO}" 2>&1); RC=$?
check "stale crash log not attributed" NO-RESULT 3 "${RC}" "${OUT}"

# ── No result at all: exited quietly, no crash log ───────────────────────────
H=$(new_home noresult)
STUB=$(make_stub noresult <<'EOF'
#!/bin/sh
exit 0
EOF
)
OUT=$(HOME="${H}" AUTOTEST_LAUNCHER="${STUB}" "${RUNNER}" ${RUNNER_FLAGS} "${SCENARIO}" 2>&1); RC=$?
check "no result, no crash log" NO-RESULT 3 "${RC}" "${OUT}"

# ── Hung: never writes, never exits. Watchdog kills it. ──────────────────────
H=$(new_home timeout)
STUB=$(make_stub timeout <<'EOF'
#!/bin/sh
sleep 120
EOF
)
OUT=$(HOME="${H}" AUTOTEST_LAUNCHER="${STUB}" "${RUNNER}" ${RUNNER_FLAGS} --timeout 2 "${SCENARIO}" 2>&1); RC=$?
check "hang -> watchdog" TIMEOUT-FAIL 1 "${RC}" "${OUT}"

# ── A result file with a status the runner does not understand is NOT a pass ─
H=$(new_home garbage)
STUB=$(make_stub garbage <<EOF
#!/bin/sh
${result_path_from_argv}
printf '{"name":"x","status":"wat"' > "\${RP}"
EOF
)
OUT=$(HOME="${H}" AUTOTEST_LAUNCHER="${STUB}" "${RUNNER}" ${RUNNER_FLAGS} "${SCENARIO}" 2>&1); RC=$?
check "unparseable verdict" BAD-VERDICT 3 "${RC}" "${OUT}"

# ── Harness errors are named too ─────────────────────────────────────────────
H=$(new_home missing)
OUT=$(HOME="${H}" "${RUNNER}" no-such-scenario-here 2>&1); RC=$?
check "missing scenario" HARNESS-ERROR 3 "${RC}" "${OUT}"

H=$(new_home locked)
mkdir -p "${H}/.ww3mod-tests/run.lock"
echo $$ > "${H}/.ww3mod-tests/run.lock/pid"          # this shell: provably alive
echo "selftest-holder" > "${H}/.ww3mod-tests/run.lock/test"
OUT=$(HOME="${H}" "${RUNNER}" ${RUNNER_FLAGS} "${SCENARIO}" 2>&1); RC=$?
check "lock held by a live pid" HARNESS-ERROR 3 "${RC}" "${OUT}"

echo
echo "==> result paths do not collide"

# Two sequential runs of the same scenario. The stub records the Test.ResultPath
# it was handed — i.e. the destination the ENGINE is told to write to, which is
# the thing that used to be shared — and writes a verdict carrying its own pid.
#
# Asserting on "two archive directories exist" would NOT discriminate: the old
# runner also archived a per-run copy after the fact, and passed that check while
# still being the broken thing. The invariant that actually matters is the live
# path, and that a completed verdict is never at the mercy of a later run.
H=$(new_home paths)
RPLOG="${SANDBOX}/rp.log"
: > "${RPLOG}"
STUB=$(make_stub paths <<EOF
#!/bin/sh
${result_path_from_argv}
printf '%s\n' "\${RP}" >> "${RPLOG}"
# The pid must be a printf ARGUMENT: inside the single-quoted format it would
# stay a literal \$\$ and both runs would write identical bytes, which is a
# green that proves nothing.
printf '{"name":"x","status":"pass","notes":"written-by-%s"}' "\$\$" > "\${RP}"
EOF
)

HOME="${H}" AUTOTEST_LAUNCHER="${STUB}" "${RUNNER}" ${RUNNER_FLAGS} "${SCENARIO}" >/dev/null 2>&1
RP1=$(sed -n 1p "${RPLOG}")
V1=$(cat "${RP1}" 2>/dev/null)

# Plant a stale lock before the second run, so it takes the reclaim path — the
# exact sequence of the 2026-08-12 incident, where the reclaiming run's
# `rm -f result.json` deleted a verdict another run had already earned.
mkdir -p "${H}/.ww3mod-tests/run.lock"
echo 99999 > "${H}/.ww3mod-tests/run.lock/pid"       # not a live pid: stale
echo "dead-runner" > "${H}/.ww3mod-tests/run.lock/test"
OUT=$(HOME="${H}" AUTOTEST_LAUNCHER="${STUB}" "${RUNNER}" ${RUNNER_FLAGS} "${SCENARIO}" 2>&1)
RP2=$(sed -n 2p "${RPLOG}")

if [ -n "${RP1}" ] && [ -n "${RP2}" ] && [ "${RP1}" != "${RP2}" ]; then
	echo "  ok    engine is handed a per-run path  two runs, two destinations"
else
	echo "  FAIL  engine is handed a per-run path  both runs got '${RP1}'"
	FAILURES=$((FAILURES + 1))
fi

if ! printf '%s' "${OUT}" | grep -q "Reclaiming stale lock"; then
	echo "  FAIL  stale-lock reclaim             run 2 did not take the reclaim path"
	FAILURES=$((FAILURES + 1))
elif [ -n "${V1}" ] && [ "$(cat "${RP1}" 2>/dev/null)" = "${V1}" ]; then
	echo "  ok    stale-lock reclaim             run 1's verdict survived run 2"
else
	echo "  FAIL  stale-lock reclaim             run 2 destroyed run 1's verdict"
	FAILURES=$((FAILURES + 1))
fi

# ── The legacy shared path must not read as a verdict ────────────────────────
LEGACY="${H}/.ww3mod-tests/result.json"
if [ -f "${LEGACY}" ] && grep -q '"status":"moved"' "${LEGACY}" \
	&& ! grep -q '"status":"pass"' "${LEGACY}"; then
	echo "  ok    legacy shared path           stubbed, cannot be read as a pass"
else
	echo "  FAIL  legacy shared path           still looks like a verdict"
	FAILURES=$((FAILURES + 1))
fi

echo
echo "==> the verdict survives a truncating filter"

# ── `| tail` is the caller mistake that has hit twice. The exit code is lost
# and this runner cannot prevent that — but the VERDICT TEXT must survive.
H=$(new_home pipe)
STUB=$(make_stub pipe <<EOF
#!/bin/sh
${result_path_from_argv}
printf '{"name":"x","status":"fail","notes":"stub"}' > "\${RP}"
EOF
)
TAILED=$(HOME="${H}" AUTOTEST_LAUNCHER="${STUB}" "${RUNNER}" ${RUNNER_FLAGS} "${SCENARIO}" 2>/dev/null | tail -1)
if printf '%s' "${TAILED}" | grep -q 'AUTOTEST_VERDICT outcome=FAIL'; then
	echo "  ok    tail -1 of a failing run     still says outcome=FAIL"
else
	echo "  FAIL  tail -1 of a failing run     lost the verdict: '${TAILED}'"
	FAILURES=$((FAILURES + 1))
fi

# stderr copy: a caller who filters stdout still gets told.
ERRONLY=$(HOME="${H}" AUTOTEST_LAUNCHER="${STUB}" "${RUNNER}" ${RUNNER_FLAGS} "${SCENARIO}" 2>&1 >/dev/null)
if printf '%s' "${ERRONLY}" | grep -q 'AUTOTEST_VERDICT outcome=FAIL'; then
	echo "  ok    stderr of a failing run      carries the verdict independently"
else
	echo "  FAIL  stderr of a failing run      silent when stdout is redirected"
	FAILURES=$((FAILURES + 1))
fi

echo
if [ "${FAILURES}" = "0" ]; then
	echo "==> selftest: all cases report correctly."
	exit 0
fi
echo "==> selftest: ${FAILURES} case(s) misreport. Do not trust harness verdicts until fixed."
exit "${FAILURES}"
