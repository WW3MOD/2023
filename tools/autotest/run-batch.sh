#!/bin/sh
# WW3MOD developer test harness — multi-test runner
#
# Usage:  ./tools/autotest/run-batch.sh [--timeout N] <test1> <test2> ...
#         ./tools/autotest/run-batch.sh [--timeout N] --all
#
# Runs each named test sequentially via run-test.sh, prints a per-test
# verdict line and a final summary. Exit code: 0 if all pass; otherwise
# the count of non-pass tests (capped at 99 so the shell doesn't truncate).
#
# --timeout N (optional, leading): forwarded to every run-test.sh as its
# per-test wall-clock kill-timeout. Omit to use run-test.sh's own default
# (300s), which already prevents a rules-broken map from hanging the batch.
#
# Per-test exit codes from run-test.sh: 0=pass, 1=fail, 2=skip, 3=error.
# Pass-through unchanged so a future CI step can read each verdict from logs.

set -u

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

# Optional leading --timeout N / --timeout=N, forwarded to each run-test.sh.
TIMEOUT_ARGS=""
case "${1:-}" in
	--timeout=*) TIMEOUT_ARGS="--timeout ${1#*=}"; shift ;;
	--timeout)   shift; TIMEOUT_ARGS="--timeout ${1:-}"; shift ;;
esac

if [ $# -eq 0 ]; then
	echo "Usage: $0 [--timeout N] <test-folder> [<test-folder> ...]"
	echo "       $0 [--timeout N] --all"
	exit 3
fi

if [ "$1" = "--all" ]; then
	TESTS=$(ls -d tools/autotest/scenarios/test-*/ 2>/dev/null | xargs -n1 basename)
	if [ -z "${TESTS}" ]; then
		echo "No test-* folders found under tools/autotest/scenarios/"
		exit 3
	fi
else
	TESTS="$*"
fi

PASS=0; FAIL=0; SKIP=0; ERR=0
LINES=""

for t in ${TESTS}; do
	echo
	echo "============================================================"
	echo "  Running: ${t}"
	echo "============================================================"

	./tools/autotest/run-test.sh ${TIMEOUT_ARGS} "${t}"
	rc=$?

	case ${rc} in
		0) verdict="PASS"; PASS=$((PASS + 1)) ;;
		1) verdict="FAIL"; FAIL=$((FAIL + 1)) ;;
		2) verdict="SKIP"; SKIP=$((SKIP + 1)) ;;
		*) verdict="ERR ($rc)"; ERR=$((ERR + 1)) ;;
	esac

	LINES="${LINES}${verdict}|${t}
"
done

TOTAL=$((PASS + FAIL + SKIP + ERR))
NON_PASS=$((FAIL + SKIP + ERR))

echo
echo "============================================================"
echo "  Summary (${TOTAL} tests)"
echo "============================================================"
printf '%s' "${LINES}" | awk -F'|' '{ printf "  %-8s %s\n", $1, $2 }'
echo "  ────────────────────────────────────────────"
printf "  Pass: %d  Fail: %d  Skip: %d  Error: %d\n" "${PASS}" "${FAIL}" "${SKIP}" "${ERR}"

if [ ${NON_PASS} -gt 99 ]; then
	exit 99
fi
exit ${NON_PASS}
