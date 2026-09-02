#!/bin/sh
# WW3MOD autotest — declared expected status for a scenario.
#
# THE PROBLEM. run-batch.sh --all globs every test-* folder and includes any scenario
# that contains an assertion; its only exclusion catches scenarios with NO verdict call
# at all. So a scenario that produces a verdict and *legitimately* fails — a by-merit
# negative, a "we measured it and it is within tolerance" result, a layer of a bug that
# is knowingly unfixed — turns the batch permanently red. run-batch.sh's own comment
# says what that costs: it "is how a red batch stops meaning anything."
#
# THE SHAPE OF THE FIX, and why it is an expected status rather than an opt-out marker.
# An opt-out ("skip this one in --all") hides the scenario, and a hidden scenario stops
# reporting: if it later starts CRASHING nobody hears. A declared status keeps the run
# and grades it, which buys the asymmetry that `mods/ww3mod/lint-baseline.txt` already
# implements deliberately and for the same reason — read that file's header, it is the
# best statement of the principle in this repo:
#
#   * The declared outcome occurring is GREEN. It is the recorded floor.
#   * The declared outcome NO LONGER occurring is RED, loudly. A scenario that was
#     declared `fail` and now passes has had its premise change under it; the
#     declaration is stale and must be removed in the same commit as whatever fixed it.
#     Lowering the floor is a deliberate, reviewable act — exactly as with the lint
#     baseline, where the prune can only ever REMOVE lines.
#   * Any OTHER outcome is RED. Declaring `fail` does not buy silence for a crash.
#
# So this can never be used to make a red run green by fiat: the only thing it silences
# is the one specific outcome the author wrote down and justified, and it fails the
# moment reality stops matching the note.
#
# THE FOURTH BULLET, which is the one that makes the other three safe — A DECLARATION IS
# ONLY EVER SATISFIED BY A SCENARIO THAT REACHED A VERDICT UNDER ITS OWN POWER. A watchdog
# timeout, a crash, a Ctrl-C, a run that produced no verdict at all: none of these can be
# green, whatever is declared. This is `NOTRUN` below, and it is an ALLOWLIST — only PASS,
# FAIL and SKIP count as the scenario answering. Anything else is the HARNESS answering,
# and "the run did not happen" is the one outcome a by-merit declaration must never absorb.
#
# It used to. Until 2026-09-02 a hang was graded GREEN by a `fail` declaration, and the
# reason is worth keeping because it is a plumbing shape that recurs: run-test.sh forks the
# outcome NAME for a timeout (`TIMEOUT-FAIL` vs `FAIL`) but deliberately gives both `exit 1`,
# and its synthesized timeout record is `"status":"fail"` — schema-identical to a real
# assertion failure. run-batch.sh derived its outcome from the EXIT CODE alone, so the fork
# was erased before the grader ever saw it and plain `FAIL` arrived either way. The bug was
# never in this decision table; it was in what reached it. A crash was red only by luck of
# collapsing onto a different exit code (3 -> ERR), not because anything checked.
#
# THE FIX IS A SIDE CHANNEL, not a richer exit code. run-test.sh's EXIT trap writes
# `outcome=<NAME> exit=<n> test=<t> run=<id>` to the path in AUTOTEST_OUTCOME_FILE, and
# run-batch.sh names one file per test and reads the precise OUTCOME back from it. A file
# rather than the stdout banner because both stream reads are traps for a caller: leaving
# the run to print to the terminal means the line was never captured, and capturing it
# through a pipe is exactly how an exit code gets lost -- twice, in this harness, on record.
# Exit codes were left alone deliberately: run-batch, CI and every existing caller depend on
# 0/1/2/3, and widening them would have paid for this fix with a different silent break.
#
# Two things follow for anyone editing this file. (1) The allowlist is here and ONLY here --
# run-batch does not re-derive it, so a harness outcome added to run-test.sh later is RED by
# default rather than quietly satisfying somebody's declaration. (2) If the outcome file is
# missing or disagrees with the exit code, run-batch reports `NO-OUTCOME` / `OUTCOME-MISMATCH`
# rather than falling back to the exit code, because a silent fallback is this same bug again.
# One level down, inside a scenario, the marker scheme (`00-script-loaded` /
# `99-verdict-reached`) answers the same question about the Lua; see CONTROL-ARM.md and
# WORKSPACE/DISCOVERIES.md 2026-09-01.
#
# DECLARING IT. Put a file named `expected-status` in the scenario folder:
#
#   tools/autotest/scenarios/test-<name>/expected-status
#   ------------------------------------------------------
#   fail
#   The negative arm is by merit: the drone genuinely does not prefer the lost-track
#   contact yet. Remove this file when the preference lands and the run goes green.
#
# First non-comment line is the status (`fail` or `skip`). Everything after it is the
# reason and is REQUIRED — an entry with no reason is a configuration error and fails
# the batch, on the same argument as lint-baseline's "[accepted] needs a comment above
# it saying why". `pass` is rejected: that is the default and declaring it says nothing.
# Lines starting with `#` are comments.
#
# USAGE
#   expected_status_read  <scenario-dir>          -> echoes "" | "fail" | "skip" | "ERROR:msg"
#   expected_status_grade <declared> <outcome>    -> GREEN|RED|STOPPED|NOTRUN|CONFIG
#   ./expected-status.sh --selftest               -> proves the decision table, no launch
#
# <outcome> is run-test.sh's OUTCOME NAME -- PASS, FAIL, SKIP, TIMEOUT-FAIL, CRASH,
# NO-RESULT, BAD-VERDICT, INTERRUPTED, HARNESS-ERROR -- NOT an exit-code bucket.
# Passing a bucket is the bug described above: it erases TIMEOUT-FAIL into FAIL.

# Echo the declared status for a scenario dir, or nothing if undeclared.
# An unreadable/ill-formed declaration echoes "ERROR:<msg>" so the caller can fail loudly
# rather than silently treating it as undeclared -- a typo'd status must never read as
# "no declaration" and quietly restore the permanent red this exists to remove.
expected_status_read() {
	_esr_file="$1/expected-status"
	[ -f "${_esr_file}" ] || return 0

	_esr_status=""
	_esr_reason=""
	while IFS= read -r _esr_line || [ -n "${_esr_line}" ]; do
		case "${_esr_line}" in
			'#'*) continue ;;
		esac
		# Trim leading/trailing whitespace without invoking sed per line.
		_esr_line="${_esr_line#"${_esr_line%%[![:space:]]*}"}"
		_esr_line="${_esr_line%"${_esr_line##*[![:space:]]}"}"
		[ -n "${_esr_line}" ] || continue
		if [ -z "${_esr_status}" ]; then
			_esr_status="${_esr_line}"
		else
			_esr_reason="${_esr_line}"
			break
		fi
	done < "${_esr_file}"

	case "${_esr_status}" in
		fail|skip) : ;;
		pass) echo "ERROR:'pass' is the default and declares nothing; delete the file"; return 0 ;;
		"")   echo "ERROR:expected-status file is empty"; return 0 ;;
		*)    echo "ERROR:unknown status '${_esr_status}' (expected 'fail' or 'skip')"; return 0 ;;
	esac

	if [ -z "${_esr_reason}" ]; then
		echo "ERROR:'${_esr_status}' declared with no reason; add one below the status line"
		return 0
	fi

	echo "${_esr_status}"
}

# Grade a run's OUTCOME NAME against a declaration.
#   GREEN   counts as a pass for the batch's exit code
#   STOPPED the declared outcome no longer occurs -- the declaration is stale (RED)
#   NOTRUN  the scenario never reached a verdict under its own power (RED)
#   RED     an outcome nobody declared
#   CONFIG  the declaration itself is malformed (RED)
expected_status_grade() {
	_esg_declared="$1"
	_esg_actual="$2"

	case "${_esg_declared}" in
		ERROR:*) echo "CONFIG"; return 0 ;;
	esac

	# THE ALLOWLIST, and the only copy of it. These three outcomes -- and no others --
	# mean the scenario ran and answered. Every other name run-test.sh can report is
	# the harness giving up, and is graded NOTRUN whatever is declared: an empty value
	# (the outcome never reached us) lands here too, so a broken plumb is loud rather
	# than green. Adding a case here is how a future outcome becomes declarable; there
	# is nowhere else to change, and the default for anything new is RED.
	case "${_esg_actual}" in
		PASS|FAIL|SKIP) : ;;
		*) echo "NOTRUN"; return 0 ;;
	esac

	if [ -z "${_esg_declared}" ]; then
		[ "${_esg_actual}" = "PASS" ] && echo "GREEN" || echo "RED"
		return 0
	fi

	# Uppercase the declaration for comparison without relying on `tr` locale behaviour.
	case "${_esg_declared}" in
		fail) _esg_want="FAIL" ;;
		skip) _esg_want="SKIP" ;;
		*)    echo "CONFIG"; return 0 ;;
	esac

	if [ "${_esg_actual}" = "${_esg_want}" ]; then
		echo "GREEN"
	elif [ "${_esg_actual}" = "PASS" ]; then
		echo "STOPPED"
	else
		echo "RED"
	fi
}

_expected_status_selftest() {
	_t_fail=0
	_check() {
		_got=$(expected_status_grade "$2" "$3")
		if [ "${_got}" = "$4" ]; then
			printf '  ok    %-34s declared=%-16s actual=%-5s -> %s\n' "$1" "'$2'" "$3" "${_got}"
		else
			printf '  FAIL  %-34s declared=%-16s actual=%-5s -> %s (want %s)\n' \
				"$1" "'$2'" "$3" "${_got}" "$4"
			_t_fail=$((_t_fail + 1))
		fi
	}

	echo "expected-status selftest — decision table"
	# Undeclared: unchanged behaviour, only PASS is green.
	_check "undeclared, passes"            ""      PASS GREEN
	_check "undeclared, fails"             ""      FAIL RED
	_check "undeclared, skips"             ""      SKIP RED
	_check "undeclared, errors"            ""      ERR  NOTRUN
	# Declared fail: the by-merit negative.
	_check "declared fail, fails"          "fail"  FAIL GREEN
	_check "declared fail, STOPS failing"  "fail"  PASS STOPPED
	_check "declared fail, skips instead"  "fail"  SKIP RED
	# Declared skip.
	_check "declared skip, skips"          "skip"  SKIP GREEN
	_check "declared skip, STOPS skipping" "skip"  PASS STOPPED
	_check "declared skip, fails instead"  "skip"  FAIL RED
	# THE HOLE THIS TABLE WAS WRITTEN AROUND. Every one of these is the harness giving
	# up, and none of them may be absorbed by a declaration. The first row is the bug:
	# a hang reached the grader as plain FAIL and read as the declared outcome.
	_check "declared fail, HANGS"          "fail"  TIMEOUT-FAIL NOTRUN
	_check "declared fail, crashes"        "fail"  CRASH        NOTRUN
	_check "declared fail, no verdict"     "fail"  NO-RESULT    NOTRUN
	_check "declared fail, bad verdict"    "fail"  BAD-VERDICT  NOTRUN
	_check "declared fail, interrupted"    "fail"  INTERRUPTED  NOTRUN
	_check "declared fail, harness broke"  "fail"  HARNESS-ERROR NOTRUN
	_check "declared skip, HANGS"          "skip"  TIMEOUT-FAIL NOTRUN
	_check "undeclared, HANGS"             ""      TIMEOUT-FAIL NOTRUN
	# The outcome never reached the grader at all: loud, never green.
	_check "declared fail, outcome lost"   "fail"  NO-OUTCOME       NOTRUN
	_check "declared fail, plumb mismatch" "fail"  OUTCOME-MISMATCH NOTRUN
	_check "declared fail, empty outcome"  "fail"  ""               NOTRUN
	_check "undeclared, empty outcome"     ""      ""               NOTRUN
	# Malformed declarations are never silently ignored -- and outrank NOTRUN, because
	# the file has to be fixed either way.
	_check "malformed declaration"         "ERROR:bad" FAIL CONFIG
	_check "malformed, would-be green"     "ERROR:bad" PASS CONFIG
	_check "malformed, and it hung"        "ERROR:bad" TIMEOUT-FAIL CONFIG

	echo
	echo "file parsing"
	_tmp="${TMPDIR:-/tmp}/expstatus-selftest.$$"
	mkdir -p "${_tmp}/none" "${_tmp}/good" "${_tmp}/noreason" "${_tmp}/bogus" "${_tmp}/empty" "${_tmp}/pass"
	printf 'fail\nby-merit negative, see CONTROL-ARM.md\n'   > "${_tmp}/good/expected-status"
	printf '# a comment\n\nfail\n'                            > "${_tmp}/noreason/expected-status"
	printf 'flaky\nbecause reasons\n'                         > "${_tmp}/bogus/expected-status"
	printf '\n'                                               > "${_tmp}/empty/expected-status"
	printf 'pass\nwhy not\n'                                  > "${_tmp}/pass/expected-status"

	_checkfile() {
		_got=$(expected_status_read "$2")
		case "${_got}" in
			$3) printf '  ok    %-34s -> %s\n' "$1" "${_got:-<undeclared>}" ;;
			*)  printf '  FAIL  %-34s -> %s (want %s)\n' "$1" "${_got:-<undeclared>}" "$3"
			    _t_fail=$((_t_fail + 1)) ;;
		esac
	}
	_checkfile "no file"                  "${_tmp}/none"     ""
	_checkfile "status + reason"          "${_tmp}/good"     "fail"
	_checkfile "status, no reason"        "${_tmp}/noreason" "ERROR:*"
	_checkfile "unknown status"           "${_tmp}/bogus"    "ERROR:*"
	_checkfile "empty file"               "${_tmp}/empty"    "ERROR:*"
	_checkfile "declares pass"            "${_tmp}/pass"     "ERROR:*"
	rm -rf "${_tmp}"

	echo
	if [ ${_t_fail} -eq 0 ]; then
		echo "expected-status selftest: ok"
		return 0
	fi
	echo "expected-status selftest: ${_t_fail} FAILURE(S)"
	return 1
}

case "$1" in
	--selftest) _expected_status_selftest ;;
esac
